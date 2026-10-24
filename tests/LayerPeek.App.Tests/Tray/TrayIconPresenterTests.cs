using System.ComponentModel;

using Microsoft.Extensions.Logging;

using LayerPeek.App.Tray;

namespace LayerPeek.App.Tests.Tray;

public sealed class TrayIconPresenterTests
{
    private static readonly TrayIconKey Idle = TrayIconKey.For(TrayStatus.Idle, null);
    private static readonly TrayIconKey Printing = TrayIconKey.For(TrayStatus.Printing, 40);
    private static readonly TrayIconKey Error = TrayIconKey.For(TrayStatus.Error, null);

    private readonly FakeStatusSource _source = new() { IconKey = Idle, ToolTipText = "first" };
    private readonly FakeTarget _target = new();
    private readonly ListLogger<TrayIconPresenterTests> _logger = new();
    private readonly HashSet<TrayIconKey> _failingKeys = [];
    private readonly List<TrayIconKey> _requested = [];

    [Fact]
    public void AppliesCurrentStateOnCreation()
    {
        using TrayIconPresenter<string> presenter = Create();

        Assert.Equal(["icon:" + Idle], _target.Icons);
        Assert.Equal(["first"], _target.ToolTips);
    }

    [Fact]
    public void IconKeyChangeUpdatesOnlyIcon()
    {
        using TrayIconPresenter<string> presenter = Create();

        _source.IconKey = Printing;
        _source.Raise(nameof(ITrayStatusSource.IconKey));

        Assert.Equal(["icon:" + Idle, "icon:" + Printing], _target.Icons);
        Assert.Equal(["first"], _target.ToolTips);
    }

    [Fact]
    public void ToolTipChangeUpdatesOnlyToolTip()
    {
        using TrayIconPresenter<string> presenter = Create();

        _source.ToolTipText = "second";
        _source.Raise(nameof(ITrayStatusSource.ToolTipText));

        Assert.Single(_target.Icons);
        Assert.Equal(["first", "second"], _target.ToolTips);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AllPropertiesChangedUpdatesBoth(string? propertyName)
    {
        using TrayIconPresenter<string> presenter = Create();
        _source.IconKey = Error;
        _source.ToolTipText = "second";

        _source.Raise(propertyName);

        Assert.Equal("icon:" + Error, _target.Icons[^1]);
        Assert.Equal("second", _target.ToolTips[^1]);
    }

    [Fact]
    public void UnrelatedPropertyIsIgnored()
    {
        using TrayIconPresenter<string> presenter = Create();

        _source.Raise("Something");

        Assert.Single(_target.Icons);
        Assert.Single(_target.ToolTips);
    }

    [Fact]
    public void FailedRenderKeepsPreviousIconAndLogs()
    {
        using TrayIconPresenter<string> presenter = Create();
        _failingKeys.Add(Error);

        _source.IconKey = Error;
        _source.Raise(nameof(ITrayStatusSource.IconKey));

        Assert.Equal(["icon:" + Idle], _target.Icons);
        (LogLevel level, _, Exception? exception) = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, level);
        Assert.IsType<InvalidOperationException>(exception);

        // The next state renders again.
        _source.IconKey = Printing;
        _source.Raise(nameof(ITrayStatusSource.IconKey));
        Assert.Equal("icon:" + Printing, _target.Icons[^1]);
    }

    [Fact]
    public void FailedInitialRenderDoesNotThrow()
    {
        _failingKeys.Add(Idle);

        using TrayIconPresenter<string> presenter = Create();

        Assert.Empty(_target.Icons);
        Assert.Equal(["first"], _target.ToolTips);
    }

    [Fact]
    public void DisposeUnsubscribes()
    {
        TrayIconPresenter<string> presenter = Create();
        Assert.Equal(1, _source.SubscriberCount);

        presenter.Dispose();
        _source.Raise(null);

        Assert.Equal(0, _source.SubscriberCount);
        Assert.Single(_target.Icons);
    }

    private TrayIconPresenter<string> Create() => new(_target, _source, Render, _logger);

    private string Render(TrayIconKey key)
    {
        _requested.Add(key);
        return _failingKeys.Contains(key) ? throw new InvalidOperationException("render failed") : "icon:" + key;
    }

    private sealed class FakeStatusSource : ITrayStatusSource
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public TrayIconKey IconKey { get; set; }

        public string ToolTipText { get; set; } = "";

        public int SubscriberCount => PropertyChanged?.GetInvocationList().Length ?? 0;

        public void Raise(string? propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class FakeTarget : ITrayIconTarget<string>
    {
        public List<string> Icons { get; } = [];

        public List<string> ToolTips { get; } = [];

        public void SetIcon(string icon) => Icons.Add(icon);

        public void SetToolTipText(string text) => ToolTips.Add(text);
    }
}
