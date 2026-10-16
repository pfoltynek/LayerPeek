using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.App.Tray.Icons;

namespace PrusaTrayDiagnostic.App.Tests.Tray;

public sealed class TrayIconCacheTests
{
    [Fact]
    public void RendersEachKeyOnlyOnce()
    {
        List<TrayIconKey> rendered = [];
        using TrayIconCache<object> cache = new(key =>
        {
            rendered.Add(key);
            return new object();
        });

        object first = cache.Get(TrayIconKey.For(TrayStatus.Printing, 41));
        object second = cache.Get(TrayIconKey.For(TrayStatus.Printing, 44));
        object other = cache.Get(TrayIconKey.For(TrayStatus.Printing, 45));

        Assert.Same(first, second);
        Assert.NotSame(first, other);
        Assert.Equal([TrayIconKey.For(TrayStatus.Printing, 40), TrayIconKey.For(TrayStatus.Printing, 45)], rendered);
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void FailedRenderIsNotCachedAndPropagates()
    {
        int calls = 0;
        using TrayIconCache<object> cache = new(_ => ++calls == 1 ? throw new InvalidOperationException("boom") : new object());
        TrayIconKey key = TrayIconKey.For(TrayStatus.Idle, null);

        Assert.Throws<InvalidOperationException>(() => cache.Get(key));
        Assert.NotNull(cache.Get(key));
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void DisposeDisposesCachedIcons()
    {
        List<DisposableIcon> icons = [];
        TrayIconCache<DisposableIcon> cache = new(_ =>
        {
            DisposableIcon icon = new();
            icons.Add(icon);
            return icon;
        });
        cache.Get(TrayIconKey.For(TrayStatus.Idle, null));
        cache.Get(TrayIconKey.For(TrayStatus.Error, null));

        cache.Dispose();

        Assert.All(icons, icon => Assert.True(icon.IsDisposed));
        Assert.Equal(0, cache.Count);
        Assert.Throws<ObjectDisposedException>(() => cache.Get(TrayIconKey.For(TrayStatus.Idle, null)));
    }

    private sealed class DisposableIcon : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
