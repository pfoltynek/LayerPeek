using Avalonia.Controls;
using Avalonia.Layout;

namespace PrusaTrayDiagnostic.App.UiTests.Infrastructure;

/// <summary>The snapshot comparison itself: its tolerance must not let small but real changes of the UI pass.</summary>
public sealed class SnapshotAssertTests(HeadlessSession session)
{
    [Fact]
    public Task IdenticalWindowsRenderIdentically() =>
        session.RunAsync(static scope =>
        {
            Window expected = Show(scope, placeholder: "192.168.1.20", margin: 16);
            Window actual = Show(scope, placeholder: "192.168.1.20", margin: 16);

            int different = SnapshotAssert.CountDifferentPixels(expected, actual);
            Assert.Equal(0, different);
            Assert.True(SnapshotAssert.IsWithinTolerance(different));
            return Task.CompletedTask;
        });

    [Fact]
    public Task OneChangedDigitOfALightPlaceholderExceedsTheTolerance() =>
        session.RunAsync(static scope =>
        {
            // The faintest text of the settings window: the grey placeholder of the address box.
            Window expected = Show(scope, placeholder: "192.168.1.20", margin: 16);
            Window actual = Show(scope, placeholder: "192.168.1.29", margin: 16);

            int different = SnapshotAssert.CountDifferentPixels(expected, actual);
            Assert.False(SnapshotAssert.IsWithinTolerance(different), $"A changed character must fail the snapshot comparison ({different} pixels differ).");
            return Task.CompletedTask;
        });

    [Fact]
    public Task OnePixelShiftExceedsTheTolerance() =>
        session.RunAsync(static scope =>
        {
            Window expected = Show(scope, placeholder: "192.168.1.20", margin: 16);
            Window actual = Show(scope, placeholder: "192.168.1.20", margin: 17);

            int different = SnapshotAssert.CountDifferentPixels(expected, actual);
            Assert.False(SnapshotAssert.IsWithinTolerance(different), $"A 1 px shift must fail the snapshot comparison ({different} pixels differ).");
            return Task.CompletedTask;
        });

    private static Window Show(UiTestScope scope, string placeholder, double margin)
    {
        Window window = scope.Track(new Window
        {
            Width = 320,
            Height = 120,
            Content = new TextBox
            {
                PlaceholderText = placeholder,
                Margin = new Avalonia.Thickness(16, margin, 16, 0),
                VerticalAlignment = VerticalAlignment.Top,
            },
        });
        window.Show();
        return window;
    }
}
