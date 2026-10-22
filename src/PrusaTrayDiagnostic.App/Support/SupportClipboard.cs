using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace PrusaTrayDiagnostic.App.Support;

/// <summary>Puts text into the clipboard (T-049). UI thread only.</summary>
internal interface ISupportClipboard
{
    /// <exception cref="InvalidOperationException">The window has no clipboard.</exception>
    Task SetTextAsync(string text);
}

/// <summary>The clipboard of the window's <see cref="TopLevel"/> (Native AOT safe).</summary>
internal sealed class TopLevelSupportClipboard(TopLevel owner) : ISupportClipboard
{
    public Task SetTextAsync(string text) =>
        (owner.Clipboard ?? throw new InvalidOperationException("The window has no clipboard.")).SetTextAsync(text);
}
