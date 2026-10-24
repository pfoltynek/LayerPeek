namespace LayerPeek.Tests.Shared;

/// <summary>
/// Files held open like another process that shares nothing (an antivirus scan, a backup, an editor), for tests of the
/// retries of app file stores. Dispose releases everything; thread-safe, so a scripted delay can release them.
/// </summary>
internal sealed class HeldFiles : IDisposable
{
    private readonly Lock _lock = new();
    private readonly List<FileStream> _streams = [];

    /// <summary>Opens <paramref name="path"/> with <see cref="FileShare.None"/> until released.</summary>
    public void Hold(string path)
    {
        FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.None);
        lock (_lock)
        {
            _streams.Add(stream);
        }
    }

    /// <summary>For <see cref="ScriptedDelayTimeProvider.OnDelay"/>: releases all files when <paramref name="release"/>, then lets the timer fire.</summary>
    public bool ReleaseWhen(bool release)
    {
        if (release)
        {
            ReleaseAll();
        }

        return true;
    }

    public void ReleaseAll()
    {
        lock (_lock)
        {
            foreach (FileStream stream in _streams)
            {
                stream.Dispose();
            }

            _streams.Clear();
        }
    }

    public void Dispose() => ReleaseAll();
}
