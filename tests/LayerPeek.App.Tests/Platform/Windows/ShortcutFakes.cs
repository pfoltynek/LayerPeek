using LayerPeek.App.Platform.Windows;

namespace LayerPeek.App.Tests.Platform.Windows;

/// <summary>Shortcut files in memory: stands in for the Start menu, nothing touches the disk.</summary>
internal sealed class InMemoryShortcutStore : IShortcutStore
{
    public Dictionary<string, AppShortcut> Shortcuts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Exception? ReadFailure { get; set; }

    public Exception? WriteFailure { get; set; }

    public Exception? DeleteFailure { get; set; }

    public int WriteCount { get; private set; }

    public List<string> Deleted { get; } = [];

    public AppShortcut? Read(string path) =>
        ReadFailure is not null ? throw ReadFailure : Shortcuts.GetValueOrDefault(path);

    public void Write(string path, AppShortcut shortcut)
    {
        if (WriteFailure is not null)
        {
            throw WriteFailure;
        }

        WriteCount++;
        Shortcuts[path] = shortcut;
    }

    public bool Delete(string path)
    {
        if (DeleteFailure is not null)
        {
            throw DeleteFailure;
        }

        Deleted.Add(path);
        return Shortcuts.Remove(path);
    }
}
