using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using LayerPeek.App.Cameras.Recording;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// The open dialog of the menu bar app (T-165): a free-standing <c>NSOpenPanel</c> (<c>beginWithCompletionHandler:</c>,
/// modeless), as menu bar apps show it. Avalonia's picker would need a window and attach the panel to it as a sheet, and
/// the app has no window when the tray menu asks. The app is activated first (an agent app's panel would open behind the
/// active app). Filters by the extension only (macOS filters by type); the caller checks the name. UI (main) thread only,
/// like every AppKit call; one panel at a time; never throws.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe partial class MacRecordingFilePicker(ILogger<MacRecordingFilePicker> logger) : IRecordingFilePicker
{
    /// <summary><c>NSModalResponseOK</c>.</summary>
    private const nint ModalResponseOk = 1;

    // The block is global (no captured state): it finds the open panel and its result here. Main thread only.
    private static nint s_panel;
    private static TaskCompletionSource<string?>? s_pending;
    private static readonly Lazy<nint> CompletionBlock = new(static () => ObjCBlocks.Global((nint)(delegate* unmanaged<nint, nint, void>)&OnCompleted));

    public Task<string?> PickAsync(RecordingFilePickRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (s_pending is not null)
        {
            return Task.FromResult<string?>(null);
        }

        try
        {
            nint panelClass = ObjCRuntime.Class("NSOpenPanel");
            if (panelClass == 0)
            {
                // No AppKit in this process.
                return Task.FromResult<string?>(null);
            }

            TaskCompletionSource<string?> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            MacAppActivation.Activate();
            nint panel = ObjCRuntime.InAutoreleasePool(() => Configure(panelClass, request));
            if (panel == 0)
            {
                return Task.FromResult<string?>(null);
            }

            s_panel = panel;
            s_pending = pending;
            ObjCRuntime.Send(panel, ObjCRuntime.Selector("beginWithCompletionHandler:"), CompletionBlock.Value);
            return pending.Task;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            LogPickFailed(ex);
            return Task.FromResult<string?>(null);
        }
    }

    /// <summary>A new panel (retained until it completed) for one existing file of the request's type.</summary>
    private static nint Configure(nint panelClass, RecordingFilePickRequest request)
    {
        nint panel = ObjCRuntime.Send(panelClass, ObjCRuntime.Selector("openPanel"));
        if (panel == 0)
        {
            return 0;
        }

        panel = ObjCRuntime.Send(panel, ObjCRuntime.Selector("retain"));
        ObjCRuntime.SendVoid(panel, ObjCRuntime.Selector("setCanChooseFiles:"), (byte)1);
        ObjCRuntime.SendVoid(panel, ObjCRuntime.Selector("setCanChooseDirectories:"), (byte)0);
        ObjCRuntime.SendVoid(panel, ObjCRuntime.Selector("setAllowsMultipleSelection:"), (byte)0);
        ObjCRuntime.SendVoid(panel, ObjCRuntime.Selector("setResolvesAliases:"), (byte)1);
        nint title = ObjCRuntime.NSString(request.Title);
        ObjCRuntime.Send(panel, ObjCRuntime.Selector("setTitle:"), title);
        ObjCRuntime.Send(panel, ObjCRuntime.Selector("setMessage:"), title);

        // macOS 11+: a content type; older systems the extension (deprecated since macOS 12, still honoured).
        nint extension = ObjCRuntime.NSString(request.Extension);
        nint utType = ObjCRuntime.Class("UTType");
        nint type = utType != 0 && ObjCRuntime.RespondsTo(panel, "setAllowedContentTypes:")
            ? ObjCRuntime.Send(utType, ObjCRuntime.Selector("typeWithFilenameExtension:"), extension)
            : 0;
        if (type != 0)
        {
            ObjCRuntime.Send(panel, ObjCRuntime.Selector("setAllowedContentTypes:"), ObjCRuntime.Send(ObjCRuntime.Class("NSArray"), ObjCRuntime.Selector("arrayWithObject:"), type));
        }
        else
        {
            ObjCRuntime.Send(panel, ObjCRuntime.Selector("setAllowedFileTypes:"), ObjCRuntime.Send(ObjCRuntime.Class("NSArray"), ObjCRuntime.Selector("arrayWithObject:"), extension));
        }

        if (request.StartFolder is { } start)
        {
            nint url = ObjCRuntime.Send(ObjCRuntime.Class("NSURL"), ObjCRuntime.Selector("fileURLWithPath:"), ObjCRuntime.NSString(start));
            if (url != 0)
            {
                ObjCRuntime.Send(panel, ObjCRuntime.Selector("setDirectoryURL:"), url);
            }
        }

        return panel;
    }

    /// <summary>The panel closed (main thread): the chosen file, or none. Must never throw into AppKit.</summary>
    [UnmanagedCallersOnly]
    private static void OnCompleted(nint block, nint response)
    {
        nint panel = s_panel;
        TaskCompletionSource<string?>? pending = s_pending;
        s_panel = 0;
        s_pending = null;
        string? path = null;
        try
        {
            if (response == ModalResponseOk && panel != 0)
            {
                path = ObjCRuntime.InAutoreleasePool(() =>
                {
                    nint url = ObjCRuntime.Send(panel, ObjCRuntime.Selector("URL"));
                    return url == 0 ? null : ObjCRuntime.ToText(ObjCRuntime.Send(url, ObjCRuntime.Selector("path")));
                });
            }

            ObjCRuntime.Release(panel);
        }
        catch (Exception)
        {
            // Nothing may escape into native code; no file then.
            path = null;
        }

        pending?.TrySetResult(path);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The open dialog for a recording file failed.")]
    private partial void LogPickFailed(Exception exception);
}
