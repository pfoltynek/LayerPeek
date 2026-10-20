using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using PrusaTrayDiagnostic.App.Platform.Windows;

namespace PrusaTrayDiagnostic.App.Tests.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class TaskbarWindowPropertiesTests
{
    private const string AppId = "PrusaTrayDiagnostic";

    public TaskbarWindowPropertiesTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Window property stores exist only on Windows.");
    }

    [Fact]
    public void PreventPinningIsSetBeforeTheAppId()
    {
        RecordingPropertyStore store = new();

        TaskbarWindowProperties.Apply(store, AppId);

        Assert.Equal(
            [
                (TaskbarWindowProperties.PreventPinning, TaskbarWindowProperties.VtBool, "-1"),
                (ShellLinkIds.AppUserModelId, PropVariant.VtLpwstr, AppId),
            ],
            store.Writes);
    }

    [Fact]
    public void ClearingEmptiesBothProperties()
    {
        RecordingPropertyStore store = new();

        TaskbarWindowProperties.Clear(store);

        Assert.Equal(
            [
                (ShellLinkIds.AppUserModelId, PropVariant.VtEmpty, string.Empty),
                (TaskbarWindowProperties.PreventPinning, PropVariant.VtEmpty, string.Empty),
            ],
            store.Writes);
    }

    [Fact]
    public void PreventPinningKeyIsTheShellOne()
    {
        // propkey.h: PKEY_AppUserModel_PreventPinning = {9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}, 9.
        Assert.Equal(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), TaskbarWindowProperties.PreventPinning.FormatId);
        Assert.Equal(9u, TaskbarWindowProperties.PreventPinning.PropertyId);
    }

    [Fact]
    public void RealWindowGetsAndLosesTheProperties()
    {
        // A top-level window of this test process that is never shown; destroyed at the end.
        nint hwnd = TestWindowNative.CreateHiddenWindow();
        try
        {
            TaskbarWindowProperties.ApplyToWindow(hwnd, AppId);
            (ushort pinningType, short pinning) = ReadBool(hwnd, TaskbarWindowProperties.PreventPinning);
            (ushort idType, string? id) = ReadString(hwnd, ShellLinkIds.AppUserModelId);

            Assert.Equal(TaskbarWindowProperties.VtBool, pinningType);
            Assert.Equal(-1, pinning);
            Assert.Equal(PropVariant.VtLpwstr, idType);
            Assert.Equal(AppId, id);

            TaskbarWindowProperties.ClearFromWindow(hwnd);

            Assert.Equal(PropVariant.VtEmpty, ReadBool(hwnd, TaskbarWindowProperties.PreventPinning).Type);
            Assert.Equal(PropVariant.VtEmpty, ReadString(hwnd, ShellLinkIds.AppUserModelId).Type);
        }
        finally
        {
            TestWindowNative.Destroy(hwnd);
        }
    }

    [Fact]
    public void InvalidWindowFails()
    {
        Assert.ThrowsAny<Exception>(() => TaskbarWindowProperties.ApplyToWindow(unchecked((nint)0x7FFF_FFF0), AppId));
    }

    private static unsafe (ushort Type, short Value) ReadBool(nint hwnd, PropertyKey key)
    {
        (ushort Type, short Value) result = default;
        TaskbarWindowProperties.WithStore(hwnd, store =>
        {
            store.GetValue(in key, out PropVariant value);
            result = (value.VarType, unchecked((short)(value.Value & 0xFFFF)));
            _ = ComNative.PropVariantClear(&value);
        });
        return result;
    }

    private static unsafe (ushort Type, string? Value) ReadString(nint hwnd, PropertyKey key)
    {
        (ushort Type, string? Value) result = default;
        TaskbarWindowProperties.WithStore(hwnd, store =>
        {
            store.GetValue(in key, out PropVariant value);
            result = (value.VarType, value.VarType == PropVariant.VtLpwstr ? Marshal.PtrToStringUni(value.Value) : null);
            _ = ComNative.PropVariantClear(&value);
        });
        return result;
    }

    /// <summary>Records the property writes with a readable value (the bool as its 16-bit number, strings as text).</summary>
    private sealed class RecordingPropertyStore : IPropertyStore
    {
        public List<(PropertyKey Key, ushort Type, string Value)> Writes { get; } = [];

        public void GetCount(out uint count) => throw new NotSupportedException();

        public void GetAt(uint index, out PropertyKey key) => throw new NotSupportedException();

        public void GetValue(in PropertyKey key, out PropVariant value) => throw new NotSupportedException();

        public void SetValue(in PropertyKey key, in PropVariant value)
        {
            string text = value.VarType switch
            {
                TaskbarWindowProperties.VtBool => unchecked((short)(value.Value & 0xFFFF)).ToString(System.Globalization.CultureInfo.InvariantCulture),
                PropVariant.VtLpwstr => Marshal.PtrToStringUni(value.Value) ?? "<null>",
                _ => string.Empty,
            };
            Writes.Add((key, value.VarType, text));
        }

        public void Commit() => throw new NotSupportedException();
    }
}

/// <summary>A plain hidden top-level window for the property store tests (class STATIC, never shown).</summary>
[SupportedOSPlatform("windows")]
internal static partial class TestWindowNative
{
    private const uint WsOverlappedWindow = 0x00CF0000;

    public static nint CreateHiddenWindow()
    {
        nint hwnd = CreateWindowExW(0, "STATIC", "PrusaTrayDiagnostic test window", WsOverlappedWindow, 0, 0, 100, 100, 0, 0, 0, 0);
        return hwnd != 0 ? hwnd : throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}.");
    }

    public static void Destroy(nint hwnd) => _ = DestroyWindow(hwnd);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateWindowExW(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);
}
