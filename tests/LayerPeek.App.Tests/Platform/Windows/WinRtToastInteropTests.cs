using System.Globalization;
using System.Runtime.Versioning;

using LayerPeek.App.Notifications;
using LayerPeek.App.Platform.Windows;
using LayerPeek.App.Tests.Notifications;
using LayerPeek.Core.Events;

namespace LayerPeek.App.Tests.Platform.Windows;

/// <summary>
/// Real WinRT interop up to (not including) showing a toast: no notifier is created, so nothing appears on screen,
/// nothing is registered and nothing lands in the notification center.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WinRtToastInteropTests
{
    public WinRtToastInteropTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "WinRT toasts exist only on Windows.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuiltToastXmlIsAcceptedByWindows(bool playSound)
    {
        string xml = ToastContentBuilder.Build(ToastContentBuilderTests.Notification(PrintEventKind.PrintFinished) with
        {
            FileName = "a&b <c> \"d\".bgcode",
            PlaySound = playSound,
        }, CultureInfo.GetCultureInfo("cs-CZ"));

        RunOnNewThread(() =>
        {
            using WinRtToastFactory factory = WinRtToastFactory.Create();
            using ToastActivationCallback callback = new(static _ => { });
            using ToastFailedCallback failed = new(static _ => { });
            using WinRtToast toast = factory.CreateToast(xml);
            toast.SubscribeActivated(callback);
            toast.SubscribeFailed(failed);
        });
    }

    [Fact]
    public void MalformedXmlIsRejectedWithAnException()
    {
        Exception? failure = null;
        RunOnNewThread(() =>
        {
            using WinRtToastFactory factory = WinRtToastFactory.Create();
            failure = Record.Exception(() => factory.CreateToast("<toast><visual>"));
        });

        Assert.NotNull(failure);
    }

    [Fact]
    public void FactoryAndCallbackCanBeDisposedTwice()
    {
        RunOnNewThread(() =>
        {
            WinRtToastFactory factory = WinRtToastFactory.Create();
            ToastActivationCallback callback = new(static _ => { });
            factory.Dispose();
            factory.Dispose();
            callback.Dispose();
            callback.Dispose();
            Assert.Throws<ObjectDisposedException>(() => factory.CreateToast("<toast/>"));
        });
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("argument")]
    [InlineData("other")]
    public void ToastDisposeSurvivesAFailingUnsubscribe(string failure)
    {
        // How WinRT failures surface: RO_E_CLOSED as ObjectDisposedException, E_INVALIDARG as ArgumentException, anything else.
        Exception exception = failure switch
        {
            "closed" => new ObjectDisposedException("toast"),
            "argument" => new ArgumentException("token"),
            _ => new InvalidOperationException("gone"),
        };
        FakeToastNotification notification = new(exception);
        using ToastActivationCallback callback = new(static _ => { });
        using ToastFailedCallback failed = new(static _ => { });
        WinRtToast toast = new(notification);
        toast.SubscribeActivated(callback);
        toast.SubscribeFailed(failed);

        toast.Dispose();
        toast.Dispose();

        // A failing first unsubscribe does not skip the second one.
        Assert.Equal(1, notification.RemoveCount);
        Assert.Equal(1, notification.RemoveFailedCount);
    }

    [Fact]
    public void ToastCanBeSubscribedToFailuresOnlyOnce()
    {
        using ToastFailedCallback failed = new(static _ => { });
        using WinRtToast toast = new(new FakeToastNotification(new InvalidOperationException("gone")));
        toast.SubscribeFailed(failed);

        Assert.Throws<InvalidOperationException>(() => toast.SubscribeFailed(failed));
    }

    [Fact]
    public void FailureHandlerReportsUnreadableArgumentsAndSwallowsCallbackFailures()
    {
        List<int> codes = [];
        ToastFailedHandler handler = new(codes.Add);
        handler.Invoke(0, 0);
        Assert.Equal([ToastFailedHandler.UnknownError], codes);

        ToastFailedHandler throwing = new(static _ => throw new InvalidOperationException("logger gone"));
        throwing.Invoke(0, 0);
    }

    [Fact]
    public void FailedCallbackCanBeDisposedTwice()
    {
        ToastFailedCallback callback = new(static _ => { });
        callback.Dispose();
        callback.Dispose();
        Assert.Throws<ObjectDisposedException>(() => callback.Pointer);
    }

    [Fact]
    public void WrappingNoObjectFailsClearly()
    {
        Assert.Throws<InvalidOperationException>(() => ComObjects.Wrap<IToastNotification>(0));
    }

    [Fact]
    public void InterfaceIdValuesMatchTheInterfaceDeclarations()
    {
        // The values avoid reflection in the AOT app; here (JIT) reflection checks they did not drift apart.
        Assert.Equal(typeof(IActivationFactory).GUID, WinRtInterfaceIds.ActivationFactory);
        Assert.Equal(typeof(IToastNotificationManagerStatics).GUID, WinRtInterfaceIds.ToastNotificationManagerStatics);
        Assert.Equal(typeof(IToastNotificationManagerStatics2).GUID, WinRtInterfaceIds.ToastNotificationManagerStatics2);
        Assert.Equal(typeof(IToastNotificationFactory).GUID, WinRtInterfaceIds.ToastNotificationFactory);
        Assert.Equal(typeof(IToastActivatedHandler).GUID, WinRtInterfaceIds.ToastActivatedHandler);
        Assert.Equal(typeof(IToastFailedHandler).GUID, WinRtInterfaceIds.ToastFailedHandler);
        Assert.Equal(typeof(IShellLinkW).GUID, ShellLinkIds.ShellLinkW);
    }

    /// <summary>A thread that never ran COM code, like the app's notification thread.</summary>
    private static void RunOnNewThread(Action action)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new AggregateException(failure);
        }
    }
}
