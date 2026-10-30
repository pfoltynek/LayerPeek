using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

using LayerPeek.Core.IO;

namespace LayerPeek.Core.Updates;

/// <summary>
/// What the update check remembers between runs (T-058): when it last succeeded (Q1 a: once a day from that time), the
/// newest release it found (so that the tray menu offers it right after a start, before the next check) and the version a
/// notification was shown for (one notification per version). App state, not a setting: it lives in its own file
/// (<see cref="JsonUpdateStateStore"/>), so a save of the settings window never overwrites it and vice versa.
/// </summary>
/// <param name="LastCheck">The last successful check; <see langword="null"/> before the first one.</param>
/// <param name="Latest">The newest release that check found on offer; <see langword="null"/> when there was none.</param>
/// <param name="NotifiedVersion">The newest version a notification was shown for; <see langword="null"/> before the first one.</param>
public sealed record UpdateState(DateTimeOffset? LastCheck, UpdateRelease? Latest, ReleaseVersion? NotifiedVersion)
{
    public static UpdateState Empty { get; } = new(null, null, null);
}

/// <summary>Reads and writes <see cref="UpdateState"/>; never throws for file problems.</summary>
public interface IUpdateStateStore
{
    /// <summary>The saved state, or <see cref="UpdateState.Empty"/> when there is none or it cannot be read.</summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<UpdateState> LoadAsync(CancellationToken cancellationToken);

    /// <summary>Saves <paramref name="state"/>; a failure is only logged.</summary>
    Task SaveAsync(UpdateState state, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IUpdateStateStore"/> in <c>update-state.json</c> next to the settings file, on the principles of
/// <c>windows.json</c> (T-094): written atomically (temporary file, then replace, retried briefly while another process holds
/// the file, <see cref="FileRetry"/>); a damaged, too large or unknown-format file reads as empty and is replaced by the next
/// save. Only https release pages on github.com are read back. Saves run one after another. Creating the store touches no files.
/// </summary>
public sealed partial class JsonUpdateStateStore : IUpdateStateStore
{
    public const string FileName = "update-state.json";

    private const int CurrentVersion = 1;

    /// <summary>A real file has a few hundred bytes.</summary>
    private const long MaxFileSizeBytes = 16 * 1024;

    private readonly string _directory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<JsonUpdateStateStore> _logger;
    private readonly Lock _gate = new();

    // Guarded by _gate: the last save, which the next one waits for.
    private Task _lastSave = Task.CompletedTask;

    public JsonUpdateStateStore(string directory, TimeProvider timeProvider, ILogger<JsonUpdateStateStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _directory = Path.GetFullPath(directory);
        FilePath = Path.Combine(_directory, FileName);
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public string FilePath { get; }

    public async Task<UpdateState> LoadAsync(CancellationToken cancellationToken)
    {
        UpdateStateDocument? document;
        try
        {
            FileInfo file = new(FilePath);
            if (!file.Exists)
            {
                return UpdateState.Empty;
            }

            if (file.Length > MaxFileSizeBytes)
            {
                LogFileIgnored("the file is too large");
                return UpdateState.Empty;
            }

            document = await FileRetry.RunAsync(static (path, token) => ReadAsync(path, token), FilePath, replacing: false, _timeProvider, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (FileNotFoundException)
        {
            return UpdateState.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            LogLoadFailed(ex);
            return UpdateState.Empty;
        }

        if (document is null)
        {
            return UpdateState.Empty;
        }

        if (document.Version != CurrentVersion)
        {
            LogFileIgnored(document.Version > CurrentVersion ? "a newer format" : "an unknown format");
            return UpdateState.Empty;
        }

        return FromDocument(document);
    }

    public Task SaveAsync(UpdateState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        byte[] content = JsonSerializer.SerializeToUtf8Bytes(ToDocument(state), UpdateStateJsonContext.Default.UpdateStateDocument);
        lock (_gate)
        {
            // One after another, in the order of the calls; WriteAfterAsync never faults.
            Task save = WriteAfterAsync(_lastSave, content, cancellationToken);
            _lastSave = save;
            return save;
        }
    }

    private async Task WriteAfterAsync(Task previous, byte[] content, CancellationToken cancellationToken)
    {
        await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ForceYielding);
        string temporary = FilePath + ".tmp";
        try
        {
            Directory.CreateDirectory(_directory);
            await FileRetry.RunAsync(
                static async (args, token) =>
                {
                    await File.WriteAllBytesAsync(args.Path, args.Content, token).ConfigureAwait(false);
                    return true;
                },
                (Path: temporary, Content: content),
                replacing: false,
                _timeProvider,
                cancellationToken).ConfigureAwait(false);
            await FileRetry.RunAsync(
                static paths => File.Move(paths.Source, paths.Target, overwrite: true),
                (Source: temporary, Target: FilePath),
                replacing: true,
                _timeProvider,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The app quits: nothing more to save.
            TryDelete(temporary);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogSaveFailed(ex);
            TryDelete(temporary);
        }
    }

    private static async Task<UpdateStateDocument?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, useAsync: true);
        await using (stream.ConfigureAwait(false))
        {
            return await JsonSerializer.DeserializeAsync(stream, UpdateStateJsonContext.Default.UpdateStateDocument, cancellationToken).ConfigureAwait(false);
        }
    }

    private static UpdateStateDocument ToDocument(UpdateState state) => new()
    {
        Version = CurrentVersion,
        LastCheck = state.LastCheck,
        Latest = state.Latest is { } latest
            ? new UpdateReleaseDocument { Version = latest.Version.ToString(), Page = latest.ReleasePage.AbsoluteUri, Prerelease = latest.IsPrerelease }
            : null,
        NotifiedVersion = state.NotifiedVersion?.ToString(),
    };

    /// <summary>Values that cannot be used (an unknown version text, a page off GitHub) read as missing.</summary>
    private static UpdateState FromDocument(UpdateStateDocument document)
    {
        UpdateRelease? latest = document.Latest is { Version: { } text, Page: { } page }
            && ReleaseVersion.TryParse(text, out ReleaseVersion? version)
            && GitHubReleasePage.TryCreate(page, out Uri? uri)
            ? new UpdateRelease(version, uri, document.Latest.Prerelease ?? version.IsPrerelease)
            : null;
        ReleaseVersion? notified = ReleaseVersion.TryParse(document.NotifiedVersion, out ReleaseVersion? n) ? n : null;
        return new UpdateState(document.LastCheck, latest, notified);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Replaced by the next save.
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The update state was not loaded ({Reason}); the next check starts afresh.")]
    private partial void LogFileIgnored(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The update state could not be loaded; the next check starts afresh.")]
    private partial void LogLoadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The update state could not be saved.")]
    private partial void LogSaveFailed(Exception exception);
}

/// <summary>The release pages the app opens: https on github.com only (a damaged file or feed never opens anything else).</summary>
public static class GitHubReleasePage
{
    public static bool TryCreate(string? text, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Uri? page)
    {
        page = null;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2048 || !Uri.TryCreate(text, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.IdnHost, "github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        page = uri;
        return true;
    }
}

internal sealed class UpdateStateDocument
{
    public int Version { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? LastCheck { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UpdateReleaseDocument? Latest { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NotifiedVersion { get; set; }
}

internal sealed class UpdateReleaseDocument
{
    public string? Version { get; set; }

    public string? Page { get; set; }

    public bool? Prerelease { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(UpdateStateDocument))]
internal sealed partial class UpdateStateJsonContext : JsonSerializerContext;
