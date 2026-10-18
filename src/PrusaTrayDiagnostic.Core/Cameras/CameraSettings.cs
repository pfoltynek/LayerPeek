using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace PrusaTrayDiagnostic.Core.Cameras;

/// <summary>
/// Camera of a printer: an RTSP stream entered by the user. A disabled camera may keep its address, so that it can be
/// switched on again without retyping it. <see cref="StreamUri"/> is always valid (see <see cref="ValidateStreamUri"/>);
/// an enabled camera always has one. An invalid address read from the settings file is kept as text in
/// <see cref="InvalidStreamUrl"/> (camera disabled) so that the user can correct it.
/// </summary>
public sealed record CameraSettings
{
    /// <summary>The only supported stream scheme (Prusa Buddy3D camera, see T-014).</summary>
    public const string RtspScheme = "rtsp";

    /// <summary>No camera configured (also used for settings files written before cameras existed).</summary>
    public static CameraSettings Disabled { get; } = new(enabled: false, streamUri: null);

    /// <exception cref="ArgumentException">
    /// <paramref name="streamUri"/> is not a valid stream address, or <paramref name="enabled"/> is set without an address.
    /// The message never contains the address (it could contain credentials).
    /// </exception>
    public CameraSettings(bool enabled, Uri? streamUri)
    {
        if (streamUri is not null && ValidateStreamUri(streamUri) is var problem and not CameraUrlProblem.None)
        {
            throw new ArgumentException($"Invalid camera stream address: {Describe(problem)}.", nameof(streamUri));
        }

        if (enabled && streamUri is null)
        {
            throw new ArgumentException("An enabled camera needs a stream address.", nameof(streamUri));
        }

        Enabled = enabled;
        StreamUri = streamUri;
    }

    private CameraSettings(string invalidStreamUrl, CameraUrlProblem problem)
    {
        InvalidStreamUrl = invalidStreamUrl;
        StreamUrlProblem = problem;
    }

    public bool Enabled { get; }

    /// <summary>Absolute <c>rtsp://</c> URL without user info; <see langword="null"/> when none was entered or it is invalid.</summary>
    public Uri? StreamUri { get; }

    /// <summary>
    /// An invalid address kept exactly as stored (the camera is then disabled and <see cref="StreamUri"/> is
    /// <see langword="null"/>); <see langword="null"/> otherwise. It may contain credentials: never log it.
    /// </summary>
    public string? InvalidStreamUrl { get; }

    /// <summary>Why <see cref="InvalidStreamUrl"/> is invalid; <see cref="CameraUrlProblem.None"/> when there is no invalid address.</summary>
    public CameraUrlProblem StreamUrlProblem { get; }

    /// <summary>
    /// Credentials were removed from the stored address while loading (see <see cref="FromStoredStreamUrl"/>); the camera
    /// is disabled until the user reviews the address. Informational only: not persisted and not part of equality.
    /// </summary>
    public bool CredentialsRemoved { get; private init; }

    /// <summary>A disabled camera that keeps an invalid address for correction.</summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="streamUrl"/> is empty (no address), valid (use the constructor) or contains credentials
    /// (they are never kept). The message never contains the text.
    /// </exception>
    public static CameraSettings FromInvalidStreamUrl(string streamUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamUrl);
        if (TryParseStreamUri(streamUrl, out _, out CameraUrlProblem problem))
        {
            throw new ArgumentException("The camera stream address is valid.", nameof(streamUrl));
        }

        if (problem == CameraUrlProblem.ContainsCredentials || RemoveUserInfo(streamUrl, out _))
        {
            throw new ArgumentException("The camera stream address contains credentials, which are never kept.", nameof(streamUrl));
        }

        return new CameraSettings(streamUrl, problem);
    }

    /// <summary>
    /// Camera for an address read from the settings file. A valid address is used as is; an invalid one is kept as
    /// <see cref="InvalidStreamUrl"/> (camera disabled). Credentials are removed first: the camera is then disabled and
    /// <see cref="CredentialsRemoved"/> is set, and what is left is used as a valid (but disabled) address, kept as
    /// invalid text, or dropped when nothing is left.
    /// </summary>
    /// <remarks>
    /// Credentials are detected in the text, so that also unparseable addresses lose them: everything up to the last
    /// <c>@</c> of the authority (after <c>scheme://</c>, or from the start when there is no <c>://</c>, up to the first
    /// <c>/</c>, <c>?</c> or <c>#</c>) is removed. Limit: a password containing an unescaped <c>/</c>, <c>?</c> or
    /// <c>#</c> ends the authority early and is not recognised.
    /// </remarks>
    internal static CameraSettings FromStoredStreamUrl(bool enabled, string streamUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamUrl);
        if (TryParseStreamUri(streamUrl, out Uri? uri, out _))
        {
            return new CameraSettings(enabled, uri);
        }

        if (!RemoveUserInfo(streamUrl, out string withoutCredentials))
        {
            return FromInvalidStreamUrl(streamUrl);
        }

        CameraSettings camera;
        if (TryParseStreamUri(withoutCredentials, out uri, out CameraUrlProblem problem))
        {
            camera = new CameraSettings(enabled: false, uri);
        }
        else if (string.IsNullOrWhiteSpace(withoutCredentials)
            || problem == CameraUrlProblem.ContainsCredentials
            || RemoveUserInfo(withoutCredentials, out _))
        {
            // Nothing left, or still something credential-like: drop it rather than risk keeping a password.
            camera = Disabled;
        }
        else
        {
            camera = new CameraSettings(withoutCredentials, problem);
        }

        return camera with { CredentialsRemoved = true };
    }

    /// <returns>Whether the text had user info; <paramref name="result"/> is the text without it.</returns>
    private static bool RemoveUserInfo(string text, out string result)
    {
        int schemeEnd = text.IndexOf(Uri.SchemeDelimiter, StringComparison.Ordinal);
        int start = schemeEnd < 0 ? 0 : schemeEnd + Uri.SchemeDelimiter.Length;
        int end = text.AsSpan(start).IndexOfAny('/', '?', '#');
        ReadOnlySpan<char> authority = end < 0 ? text.AsSpan(start) : text.AsSpan(start, end);
        int at = authority.LastIndexOf('@');
        if (at < 0)
        {
            result = text;
            return false;
        }

        result = string.Concat(text.AsSpan(0, start), text.AsSpan(start + at + 1));
        return true;
    }

    /// <summary>Checks whether <paramref name="uri"/> can be used as a camera stream address.</summary>
    public static CameraUrlProblem ValidateStreamUri(Uri? uri)
    {
        if (uri is null)
        {
            return CameraUrlProblem.Missing;
        }

        if (!uri.IsAbsoluteUri)
        {
            return CameraUrlProblem.NotAbsolute;
        }

        if (!string.Equals(uri.Scheme, RtspScheme, StringComparison.OrdinalIgnoreCase))
        {
            return CameraUrlProblem.UnsupportedScheme;
        }

        if (string.IsNullOrEmpty(uri.Host))
        {
            return CameraUrlProblem.MissingHost;
        }

        return string.IsNullOrEmpty(uri.UserInfo) ? CameraUrlProblem.None : CameraUrlProblem.ContainsCredentials;
    }

    /// <summary>Parses user input (surrounding white space is ignored) into a valid stream address.</summary>
    public static bool TryParseStreamUri(string? value, [NotNullWhen(true)] out Uri? uri, out CameraUrlProblem problem)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            problem = CameraUrlProblem.Missing;
            return false;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? parsed))
        {
            problem = CameraUrlProblem.NotAbsolute;
            return false;
        }

        problem = ValidateStreamUri(parsed);
        if (problem != CameraUrlProblem.None)
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    /// <summary>Technical English description of a problem, for logs and load warnings; never contains the address.</summary>
    internal static string Describe(CameraUrlProblem problem) => problem switch
    {
        CameraUrlProblem.None => "valid",
        CameraUrlProblem.Missing => "the address is missing",
        CameraUrlProblem.NotAbsolute => "the address is not an absolute URL",
        CameraUrlProblem.UnsupportedScheme => "only rtsp:// addresses are supported",
        CameraUrlProblem.MissingHost => "the address has no host",
        CameraUrlProblem.ContainsCredentials => "the address contains credentials, which must not be stored in the settings file",
        _ => throw new ArgumentOutOfRangeException(nameof(problem), problem, "Unknown camera address problem."),
    };

    // Compares the address text: Uri.Equals ignores user info and fragments and normalizes the address.
    public bool Equals(CameraSettings? other) =>
        other is not null
        && Enabled == other.Enabled
        && string.Equals(StreamUri?.OriginalString, other.StreamUri?.OriginalString, StringComparison.Ordinal)
        && string.Equals(InvalidStreamUrl, other.InvalidStreamUrl, StringComparison.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Enabled, StreamUri?.OriginalString, InvalidStreamUrl);

    // Replaces the synthesized member list used by ToString: the invalid text may contain credentials.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Enabled = ").Append(Enabled)
            .Append(", StreamUri = ").Append(StreamUri is null ? "null" : UriRedaction.Redact(StreamUri))
            .Append(", InvalidStreamUrl = ").Append(InvalidStreamUrl is null ? "null" : "(hidden)")
            .Append(", StreamUrlProblem = ").Append(StreamUrlProblem)
            .Append(", CredentialsRemoved = ").Append(CredentialsRemoved);
        return true;
    }
}
