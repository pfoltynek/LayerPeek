namespace PrusaTrayDiagnostic.Core.Cameras;

/// <summary>Why a camera stream address was rejected (see <see cref="CameraSettings.ValidateStreamUri"/>).</summary>
public enum CameraUrlProblem
{
    /// <summary>The address is valid.</summary>
    None,

    /// <summary>No address was given.</summary>
    Missing,

    /// <summary>The text is not an absolute URL.</summary>
    NotAbsolute,

    /// <summary>The scheme is not <c>rtsp</c>.</summary>
    UnsupportedScheme,

    /// <summary>The URL has no host (e.g. <c>rtsp:live</c>).</summary>
    MissingHost,

    /// <summary>
    /// The URL contains user info (user name and/or password). Credentials must never be stored in the plain-text
    /// settings file; cameras that need them will get them from <see cref="Secrets.ISecretStore"/>.
    /// </summary>
    ContainsCredentials,
}
