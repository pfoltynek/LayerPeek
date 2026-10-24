using LayerPeek.App.Windows;

namespace LayerPeek.App.Tests.Windows;

public sealed class InstanceActivationTests
{
    private readonly RecentWindows _recent = new();
    private readonly List<string> _calls = [];
    private readonly InstanceActivation _activation;

    public InstanceActivationTests()
    {
        _activation = new InstanceActivation(_recent, () => _calls.Add("settings"));
    }

    [Fact]
    public void WithoutOpenWindowsTheSettingsOpen()
    {
        _activation.Activate();

        Assert.Equal(["settings"], _calls);
    }

    [Fact]
    public void TheOnlyOpenWindowComesToTheFront()
    {
        FakeRecentWindow camera = Open("camera");

        _activation.Activate();

        Assert.Equal(["camera front"], _calls);
        Assert.Equal(1, camera.FrontCount);
    }

    [Fact]
    public void TheLastOpenedWindowIsTheMostRecent()
    {
        Open("settings window");
        Open("diagnostics");

        _activation.Activate();

        Assert.Equal(["diagnostics front"], _calls);
    }

    [Fact]
    public void ActivatingAWindowMakesItTheMostRecent()
    {
        FakeRecentWindow cameraMk4 = Open("camera MK4");
        Open("camera XL");
        Open("about");

        cameraMk4.RaiseActivated();
        _activation.Activate();

        Assert.Equal(["camera MK4 front"], _calls);
    }

    [Fact]
    public void AWindowOpenedWithoutFocusCountsOnlyOnceActivated()
    {
        // T-040: Apply opens the camera window without taking the focus from the settings window.
        FakeRecentWindow settings = Open("settings window");
        FakeRecentWindow camera = new("camera", _calls);
        _recent.Track(camera, used: false);

        _activation.Activate();
        camera.RaiseActivated();
        _activation.Activate();
        settings.RaiseClosed();
        camera.RaiseClosed();
        _activation.Activate();

        Assert.Equal(["settings window front", "camera front", "settings"], _calls);
    }

    [Fact]
    public void AnUnusedWindowIsStillBroughtToTheFrontWhenItIsTheOnlyOne()
    {
        FakeRecentWindow camera = new("camera", _calls);
        _recent.Track(camera, used: false);

        _activation.Activate();

        Assert.Equal(["camera front"], _calls);
    }

    [Fact]
    public void ClosedWindowsDropOut()
    {
        FakeRecentWindow diagnostics = Open("diagnostics");
        FakeRecentWindow licences = Open("licences");

        licences.RaiseClosed();
        _activation.Activate();
        diagnostics.RaiseClosed();
        _activation.Activate();

        Assert.Equal(["diagnostics front", "settings"], _calls);
        Assert.Equal(0, _recent.Count);
    }

    [Fact]
    public void ClosedWindowIsNoLongerFollowed()
    {
        FakeRecentWindow about = Open("about");
        Open("settings window");
        about.RaiseClosed();

        about.RaiseActivated();
        _activation.Activate();

        Assert.Equal(["settings window front"], _calls);
        Assert.False(about.HasSubscribers);
    }

    [Fact]
    public void TrackingTwiceKeepsOneEntry()
    {
        FakeRecentWindow camera = Open("camera");
        Open("about");

        _recent.Track(camera);

        Assert.Equal(2, _recent.Count);
        _activation.Activate();
        Assert.Equal(["about front"], _calls);
    }

    private FakeRecentWindow Open(string name)
    {
        FakeRecentWindow window = new(name, _calls);
        _recent.Track(window);
        return window;
    }

    private sealed class FakeRecentWindow(string name, List<string> calls) : IRecentWindow
    {
        public event EventHandler? Activated;

        public event EventHandler? Closed;

        public int FrontCount { get; private set; }

        public bool HasSubscribers => Activated is not null || Closed is not null;

        public void BringToFront()
        {
            FrontCount++;
            calls.Add(name + " front");
        }

        public void RaiseActivated() => Activated?.Invoke(this, EventArgs.Empty);

        public void RaiseClosed() => Closed?.Invoke(this, EventArgs.Empty);
    }
}
