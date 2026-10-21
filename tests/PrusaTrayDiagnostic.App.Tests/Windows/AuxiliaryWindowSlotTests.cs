using PrusaTrayDiagnostic.App.Windows;

namespace PrusaTrayDiagnostic.App.Tests.Windows;

public sealed class AuxiliaryWindowSlotTests
{
    private readonly AuxiliaryWindowSlot _slot = new();
    private readonly List<FakeWindow> _windows = [];
    private readonly List<FakeOwned> _owned = [];

    [Fact]
    public void FirstRequestCreatesShowsAndActivatesTheWindow()
    {
        _slot.ShowOrActivate(Create);

        FakeWindow window = Assert.Single(_windows);
        Assert.Equal(["show", "activate"], window.Calls);
        Assert.True(_slot.IsOpen);
    }

    [Fact]
    public void WithoutActivationTheWindowIsOnlyShown()
    {
        // T-040: Apply in the settings opens the camera window, the settings window keeps the focus.
        _slot.ShowOrActivate(Create, activate: false);
        _slot.ShowOrActivate(Create, activate: false);

        FakeWindow window = Assert.Single(_windows);
        Assert.Equal(["show"], window.Calls);
        Assert.True(_slot.IsOpen);
    }

    [Fact]
    public void FurtherRequestsActivateTheOpenWindow()
    {
        _slot.ShowOrActivate(Create);
        _slot.ShowOrActivate(Create);

        FakeWindow window = Assert.Single(_windows);
        Assert.Equal(["show", "activate", "activate"], window.Calls);
    }

    [Fact]
    public void ActivatingTheOpenWindowLetsTheCallerUpdateWhatItOwnsFirst()
    {
        List<string> calls = [];
        _slot.ShowOrActivate(Create, _ => calls.Add("first"));

        _slot.ShowOrActivate(Create, owned =>
        {
            Assert.Same(_owned[0], owned);
            calls.Add("reuse activate=" + _windows[0].Calls.Count(call => call == "activate"));
        });

        Assert.Equal(["reuse activate=1"], calls);
        Assert.Single(_windows);
        Assert.Equal(["show", "activate", "activate"], _windows[0].Calls);
    }

    [Fact]
    public void ClosingTheWindowReleasesItsViewModelAndAllowsANewWindow()
    {
        _slot.ShowOrActivate(Create);

        _windows[0].Close();

        Assert.False(_slot.IsOpen);
        Assert.Equal(1, _owned[0].DisposeCount);

        _slot.ShowOrActivate(Create);
        Assert.Equal(2, _windows.Count);
        Assert.True(_slot.IsOpen);
    }

    [Fact]
    public void ShutdownClosesTheWindowAndRefusesNewOnes()
    {
        _slot.ShowOrActivate(Create);

        _slot.Shutdown();
        _slot.ShowOrActivate(Create);

        Assert.Equal("close", _windows[0].Calls[^1]);
        Assert.Equal(1, _owned[0].DisposeCount);
        Assert.Single(_windows);
        Assert.False(_slot.IsOpen);
    }

    [Fact]
    public void ShutdownReleasesTheViewModelEvenIfTheWindowDoesNotReportClosing()
    {
        _slot.ShowOrActivate(() => (new FakeWindow { RaiseClosed = false }, Owned()));

        _slot.Shutdown();

        Assert.Equal(1, _owned[0].DisposeCount);
    }

    [Fact]
    public void ShutdownWithoutWindowIsHarmlessAndRepeatable()
    {
        _slot.Shutdown();
        _slot.Shutdown();

        Assert.False(_slot.IsOpen);
    }

    [Fact]
    public void ViewModelIsReleasedOnlyOnce()
    {
        _slot.ShowOrActivate(Create);
        FakeWindow window = _windows[0];

        window.Close();
        window.Close();
        _slot.Shutdown();

        Assert.Equal(1, _owned[0].DisposeCount);
    }

    [Fact]
    public void FailingShowReleasesTheViewModelAndRethrows()
    {
        InvalidOperationException failure = new("no screen");

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => _slot.ShowOrActivate(() => (new FakeWindow { ThrowOnShow = failure }, Owned()))));

        Assert.Equal(1, _owned[0].DisposeCount);
        Assert.False(_slot.IsOpen);
    }

    [Fact]
    public void RefreshTextsReachesOnlyTheOpenWindow()
    {
        _slot.RefreshTexts();
        _slot.ShowOrActivate(Create);

        _slot.RefreshTexts();
        Assert.Equal(["show", "activate", "refresh"], _windows[0].Calls);

        _windows[0].Close();
        _slot.RefreshTexts();
        Assert.Equal(["show", "activate", "refresh", "close"], _windows[0].Calls);
    }

    private (IAuxiliaryWindow Window, IDisposable? Owned) Create()
    {
        FakeWindow window = new();
        _windows.Add(window);
        return (window, Owned());
    }

    private FakeOwned Owned()
    {
        FakeOwned owned = new();
        _owned.Add(owned);
        return owned;
    }

    private sealed class FakeOwned : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }

    private sealed class FakeWindow : IAuxiliaryWindow
    {
        public event EventHandler? Closed;

        public List<string> Calls { get; } = [];

        public bool RaiseClosed { get; init; } = true;

        public Exception? ThrowOnShow { get; init; }

        public void Show()
        {
            Calls.Add("show");
            if (ThrowOnShow is not null)
            {
                throw ThrowOnShow;
            }
        }

        public void Activate() => Calls.Add("activate");

        public void RefreshTexts() => Calls.Add("refresh");

        public void Close()
        {
            Calls.Add("close");
            if (RaiseClosed)
            {
                Closed?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
