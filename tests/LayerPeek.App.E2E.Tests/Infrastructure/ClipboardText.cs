using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>
/// The text of the user's clipboard (T-064): a test that copies through the app saves it first and puts it back
/// afterwards (decided 2026-10-22: only text survives; other content is lost).
/// </summary>
internal static partial class ClipboardText
{
    private const uint UnicodeText = 13;
    private const uint GmemMoveable = 0x0002;

    /// <summary>The text in the clipboard, or <see langword="null"/> when it holds none.</summary>
    public static string? Get() => WithClipboard(static () =>
    {
        nint data = GetClipboardData(UnicodeText);
        if (data == 0)
        {
            return null;
        }

        nint text = GlobalLock(data);
        if (text == 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(text);
        }
        finally
        {
            GlobalUnlock(data);
        }
    });

    /// <summary>Whether the clipboard holds the registered format <paramref name="name"/> (e.g. the "exclude from history" marker).</summary>
    public static bool HasFormat(string name)
    {
        uint format = RegisterClipboardFormat(name);
        return format != 0 && IsClipboardFormatAvailable(format);
    }

    /// <summary>
    /// Puts <paramref name="text"/> into the clipboard, kept out of the clipboard history (Win+V) and the cloud clipboard,
    /// so that neither the test's probe nor the restored text of the user adds an entry there; <see langword="null"/>
    /// empties it.
    /// </summary>
    public static void Set(string? text) => WithClipboard(() =>
    {
        if (!EmptyClipboard())
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        if (text is null)
        {
            return 0;
        }

        byte[] content = new byte[(text.Length + 1) * sizeof(char)];
        System.Text.Encoding.Unicode.GetBytes(text, content);
        SetData(UnicodeText, content);
        SetData(RegisterClipboardFormat(ExcludeFromHistoryFormat), [0]);
        return 0;
    });

    /// <summary>The Windows format that keeps clipboard content out of the history and cloud sync.</summary>
    public const string ExcludeFromHistoryFormat = "ExcludeClipboardContentFromMonitorProcessing";

    /// <summary>Hands a copy of <paramref name="content"/> to the open clipboard, which owns it from then on.</summary>
    private static void SetData(uint format, byte[] content)
    {
        nint memory = GlobalAlloc(GmemMoveable, content.Length);
        if (memory == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        try
        {
            nint target = GlobalLock(memory);
            if (target == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            Marshal.Copy(content, 0, target, content.Length);
            GlobalUnlock(memory);
            if (format == 0 || SetClipboardData(format, memory) == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
        catch
        {
            GlobalFree(memory);
            throw;
        }
    }

    /// <summary>Another program (or the app under test) may hold the clipboard for a moment: retried.</summary>
    private static T WithClipboard<T>(Func<T> action)
    {
        for (int attempt = 0; !OpenClipboard(0); attempt++)
        {
            if (attempt == 50)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "The clipboard stayed open in another program.");
            }

            Thread.Sleep(20);
        }

        try
        {
            return action();
        }
        finally
        {
            CloseClipboard();
        }
    }

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16, EntryPoint = "RegisterClipboardFormatW")]
    private static partial uint RegisterClipboardFormat(string name);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint owner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetClipboardData(uint format);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetClipboardData(uint format, nint memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalAlloc(uint flags, nint bytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalFree(nint memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalLock(nint memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint memory);
}
