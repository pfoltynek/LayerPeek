using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LayerPeek.App.Platform.Windows;

/// <summary>
/// The resume after sleep from the power manager (T-151): <c>PowerRegisterSuspendResumeNotification</c> with a callback
/// (no window needed), <c>PBT_APMRESUMEAUTOMATIC</c> / <c>PBT_APMRESUMESUSPEND</c>. The callback comes on a thread of the
/// system. Never throws.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe partial class WindowsSystemResumeEvents : ISystemResumeEvents, IDisposable
{
    private const uint DeviceNotifyCallback = 2;
    private const uint PbtApmResumeSuspend = 0x7;
    private const uint PbtApmResumeAutomatic = 0x12;

    // The callback is static: it finds the instance here. One instance per process.
    private static WindowsSystemResumeEvents? s_current;

    private nint _registration;
    private DeviceNotifySubscribeParameters* _parameters;
    private bool _started;

    public event EventHandler? Resumed;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        try
        {
            s_current = this;
            // Must stay valid while registered.
            _parameters = (DeviceNotifySubscribeParameters*)NativeMemory.AllocZeroed((nuint)sizeof(DeviceNotifySubscribeParameters));
            _parameters->Callback = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, uint>)&OnPowerEvent;
            if (PowerRegisterSuspendResumeNotification(DeviceNotifyCallback, _parameters, out nint registration) == 0)
            {
                _registration = registration;
            }
            else
            {
                Free();
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Before Windows 8: no resume events; the gap between snapshots still tells.
            Free();
        }
    }

    public void Dispose()
    {
        if (_registration != 0)
        {
            try
            {
                _ = PowerUnregisterSuspendResumeNotification(_registration);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
            }

            _registration = 0;
        }

        Free();
    }

    private void Free()
    {
        if (_parameters is not null)
        {
            NativeMemory.Free(_parameters);
            _parameters = null;
        }

        if (ReferenceEquals(s_current, this))
        {
            s_current = null;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint OnPowerEvent(nint context, uint type, nint setting)
    {
        try
        {
            if (type is PbtApmResumeAutomatic or PbtApmResumeSuspend && s_current is { } current)
            {
                current.Resumed?.Invoke(current, EventArgs.Empty);
            }
        }
        catch (Exception)
        {
            // Never into the power manager: the handlers post to the UI thread and do not throw.
        }

        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceNotifySubscribeParameters
    {
        public nint Callback;
        public nint Context;
    }

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerRegisterSuspendResumeNotification(uint flags, DeviceNotifySubscribeParameters* recipient, out nint registrationHandle);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerUnregisterSuspendResumeNotification(nint registrationHandle);
}
