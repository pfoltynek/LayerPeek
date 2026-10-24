using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using LayerPeek.App.Platform.Windows;

namespace LayerPeek.App.Tests.Platform.Windows;

/// <summary>
/// The COM toast activator through real COM (T-068): the class object registered with <c>CoRegisterClassObject</c> under
/// a random CLSID of this test (nothing is written to the registry), created like Windows does for a click
/// (<c>CoCreateInstance</c>, <c>CLSCTX_LOCAL_SERVER</c>) and called through <c>INotificationActivationCallback</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ToastActivatorServerTests
{
    private const int ClassENoAggregation = unchecked((int)0x80040110);
    private const int ENoInterface = unchecked((int)0x80004002);

    private readonly ConcurrentQueue<(string? AppId, string? Arguments)> _received = new();

    public ToastActivatorServerTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "COM exists only on Windows.");
    }

    [Fact]
    public async Task AClickReachesTheAppThroughCom()
    {
        Guid clsid = Guid.NewGuid();
        using ToastActivatorServer server = ToastActivatorServer.Start(clsid, (appId, arguments) => _received.Enqueue((appId, arguments)));

        int result = await OnMtaThreadAsync(() => Activate(clsid, "LayerPeek", "printer=xl"));

        Assert.Equal(0, result);
        Assert.Equal([("LayerPeek", "printer=xl")], _received);
    }

    [Fact]
    public async Task EveryClickCreatesAnActivatorFromTheSameRegistration()
    {
        Guid clsid = Guid.NewGuid();
        using ToastActivatorServer server = ToastActivatorServer.Start(clsid, (appId, arguments) => _received.Enqueue((appId, arguments)));

        await OnMtaThreadAsync(() => Activate(clsid, "A", "printer=a"));
        await OnMtaThreadAsync(() => Activate(clsid, "A", null));

        Assert.Equal([("A", "printer=a"), ("A", null)], _received);
    }

    [Fact]
    public async Task AFailingAppStillAnswersWindows()
    {
        Guid clsid = Guid.NewGuid();
        using ToastActivatorServer server = ToastActivatorServer.Start(clsid, static (_, _) => throw new InvalidOperationException("boom"));

        Assert.Equal(0, await OnMtaThreadAsync(() => Activate(clsid, "A", "printer=a")));
    }

    [Fact]
    public async Task AfterDisposingWindowsCannotCreateTheActivator()
    {
        Guid clsid = Guid.NewGuid();
        ToastActivatorServer server = ToastActivatorServer.Start(clsid, (appId, arguments) => _received.Enqueue((appId, arguments)));
        server.Dispose();
        server.Dispose();

        int hr = await OnMtaThreadAsync(() => ComNative.CoCreateInstance(clsid, 0, ComNative.ClsCtxLocalServer, ToastActivatorIds.NotificationActivationCallback, out nint _));

        Assert.True(hr < 0, $"0x{hr:X8}");
        Assert.Empty(_received);
    }

    [Fact]
    public async Task TheClassFactoryRefusesAggregationAndForeignInterfaces()
    {
        Guid clsid = Guid.NewGuid();
        using ToastActivatorServer server = ToastActivatorServer.Start(clsid, static (_, _) => { });

        (int aggregated, int foreign, int created) = await OnMtaThreadAsync(() =>
        {
            Marshal.ThrowExceptionForHR(ComNative.CoGetClassObject(clsid, ComNative.ClsCtxLocalServer, 0, ToastActivatorIds.ClassFactory, out nint pointer));
            IClassFactory factory = ComObjects.Wrap<IClassFactory>(pointer);
            try
            {
                int aggregated = factory.CreateInstance(1, ToastActivatorIds.NotificationActivationCallback, out nint none);
                Assert.Equal(0, none);
                int foreign = factory.CreateInstance(0, new Guid("00000000-1111-2222-3333-444444444444"), out nint stillNone);
                Assert.Equal(0, stillNone);
                int created = factory.CreateInstance(0, ToastActivatorIds.NotificationActivationCallback, out nint activator);
                Marshal.Release(activator);
                return (aggregated, foreign, created);
            }
            finally
            {
                ComObjects.Release(factory);
            }
        });

        Assert.Equal(ClassENoAggregation, aggregated);
        Assert.Equal(ENoInterface, foreign);
        Assert.Equal(0, created);
    }

    [Fact]
    public void AnEmptyClsidIsRefused()
    {
        Assert.Throws<ArgumentException>(() => ToastActivatorServer.Start(Guid.Empty, static (_, _) => { }));
        Assert.Throws<ArgumentNullException>(() => ToastActivatorServer.Start(Guid.NewGuid(), null!));
    }

    private static int Activate(Guid clsid, string? appId, string? arguments)
    {
        Marshal.ThrowExceptionForHR(ComNative.CoCreateInstance(clsid, 0, ComNative.ClsCtxLocalServer, ToastActivatorIds.NotificationActivationCallback, out nint pointer));
        INotificationActivationCallback callback = ComObjects.Wrap<INotificationActivationCallback>(pointer);
        try
        {
            return callback.Activate(appId, arguments, 0, 0);
        }
        finally
        {
            ComObjects.Release(callback);
        }
    }

    /// <summary>On a thread of its own in the multithreaded apartment, not on the test's thread.</summary>
    private static Task<T> OnMtaThreadAsync<T>(Func<T> call)
    {
        TaskCompletionSource<T> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            try
            {
                using ComApartment apartment = ComApartment.EnterMultithreaded();
                result.SetResult(call());
            }
            catch (Exception ex)
            {
                result.SetException(ex);
            }
        })
        {
            IsBackground = true,
        };
        thread.Start();
        return result.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
