using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace LayerPeek.App.Support;

/// <summary>Puts text into the clipboard (T-049). UI thread only.</summary>
internal interface ISupportClipboard
{
    /// <param name="keepOutOfHistory">
    /// Mark the text so that Windows keeps it out of the clipboard history (Win+V) and the cloud clipboard (the test mode,
    /// T-064: an end-to-end test leaves nothing behind).
    /// </param>
    /// <exception cref="InvalidOperationException">The window has no clipboard.</exception>
    Task SetTextAsync(string text, bool keepOutOfHistory = false);
}

/// <summary>The clipboard of the window's <see cref="TopLevel"/> (Native AOT safe).</summary>
internal sealed class TopLevelSupportClipboard(TopLevel owner) : ISupportClipboard
{
    /// <summary>
    /// The Windows clipboard format that keeps content out of the clipboard history and cloud sync; other systems ignore it.
    /// </summary>
    private static readonly DataFormat<byte[]> ExcludeFromHistory = DataFormat.CreateBytesPlatformFormat("ExcludeClipboardContentFromMonitorProcessing");

    public Task SetTextAsync(string text, bool keepOutOfHistory = false)
    {
        IClipboard clipboard = owner.Clipboard ?? throw new InvalidOperationException("The window has no clipboard.");
        if (!keepOutOfHistory)
        {
            return clipboard.SetTextAsync(text);
        }

        DataTransferItem item = new();
        item.SetText(text);
        item.Set(ExcludeFromHistory, [0]);
        DataTransfer data = new();
        data.Add(item);
        // The clipboard disposes of the data when it is no longer used.
        return clipboard.SetDataAsync(data);
    }
}
