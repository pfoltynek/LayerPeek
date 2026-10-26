using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

using LayerPeek.App.Cameras.Overlay;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Tests.Cameras.Overlay;

/// <summary>What the test widget shows: the printer name, its state and the job progress, rounded like the display.</summary>
public readonly record struct TestOverlayContent(string? Name, PrinterState? State, int? Percent);

/// <summary>
/// A widget for the framework tests (T-081; the real widgets come in T-082 and later): shows the state and the progress of
/// the snapshot, counts its work and can be told to fail. Linked into the UI tests, which draw it with <see cref="TestOverlayWidgets.Template"/>.
/// </summary>
public sealed class TestOverlayWidget : OverlayWidgetViewModel<TestOverlayContent>
{
    private readonly Func<OverlayWidgetInput, TestOverlayContent> _compute;
    private string _title = string.Empty;
    private string _value = string.Empty;
    private double _progress;

    /// <param name="compute">The content for an input; <see langword="null"/> = <see cref="Default"/>.</param>
    public TestOverlayWidget(Func<OverlayWidgetInput, TestOverlayContent>? compute = null)
    {
        _compute = compute ?? Default;
    }

    /// <summary>State name ("Printing"), formatted in the current language.</summary>
    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    /// <summary>The key value: "42%", or the printer name without a job.</summary>
    public string Value
    {
        get => _value;
        private set => SetProperty(ref _value, value);
    }

    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public int ComputeCount { get; private set; }

    public int FormatCount { get; private set; }

    public bool IsDisposed { get; private set; }

    /// <summary>Throws while computing (from now on).</summary>
    public bool FailCompute { get; set; }

    /// <summary>Throws while formatting (from now on).</summary>
    public bool FailFormat { get; set; }

    public TestOverlayContent Shown => ShownContent;

    /// <summary>The content of the default widget: name, and with a snapshot its state and the progress of an active job.</summary>
    public static TestOverlayContent Default(OverlayWidgetInput input) => new(
        input.Printer.Name,
        input.Snapshot?.State,
        OverlayValues.ActiveJob(input.Snapshot) is { } job ? OverlayValues.ProgressPercent(job) : null);

    /// <summary>Asks the overlay to compute this widget again (a source besides the snapshot changed).</summary>
    public void Invalidate() => RequestUpdate();

    protected override TestOverlayContent ComputeContent(in OverlayWidgetInput input)
    {
        ComputeCount++;
        if (FailCompute)
        {
            throw new InvalidOperationException("Test widget failed computing.");
        }

        return _compute(input);
    }

    protected override void Format(in TestOverlayContent content)
    {
        FormatCount++;
        if (FailFormat)
        {
            throw new InvalidOperationException("Test widget failed formatting.");
        }

        Title = content.State is { } state ? OverlayFormat.StateName(state) : string.Empty;
        Value = content.Percent is { } percent ? OverlayFormat.Percent(percent) : content.Name ?? string.Empty;
        Progress = content.Percent ?? 0;
        AccessibleName = string.IsNullOrEmpty(Title) ? Value : string.Concat(Title, ", ", Value);
    }

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }
}

/// <summary>Definitions and the card content of <see cref="TestOverlayWidget"/>.</summary>
public static class TestOverlayWidgets
{
    /// <summary>Ids of six test widgets, one per anchor in the UI tests.</summary>
    public static IReadOnlyList<string> Ids { get; } = ["test-a", "test-b", "test-c", "test-d", "test-e", "test-f"];

    /// <summary>
    /// The card content, built in code without reflection bindings: the state (secondary text), the key value (large, tabular,
    /// accent when emphasized), a progress bar and the shared parts (pill, icon row, chip) of the T-076 design.
    /// </summary>
    public static IDataTemplate Template { get; } = new FuncDataTemplate<TestOverlayWidget>(static (widget, _) => widget is null ? null : Build(widget));

    /// <param name="create">The widget for a window; <see langword="null"/> = a new <see cref="TestOverlayWidget"/>.</param>
    internal static OverlayWidgetDefinition Definition(
        string id,
        int priority = 0,
        Func<OverlayWidgetContext, OverlayWidgetViewModel>? create = null,
        OverlayWidgetMinWidths minWidths = default,
        IReadOnlyList<Core.Cameras.Overlay.OverlayWidgetSize>? sizes = null) =>
        new(id, "CameraOverlayWidgetStatus", priority, Template, create ?? (static _ => new TestOverlayWidget()), minWidths, sizes);

    internal static OverlayWidgetRegistry Registry(params string[] ids) => new([.. ids.Select(id => Definition(id))]);

    private static StackPanel Build(TestOverlayWidget widget)
    {
        TextBlock title = new() { Name = "Title" };
        title.Classes.Add("secondary");
        TextBlock value = new() { Name = "Value" };
        value.Classes.AddRange(["big", "numeric"]);
        OverlayProgressBar progress = new() { Name = "Progress", Margin = new(0, 6, 0, 0) };
        OverlayStatusPill pill = new() { Name = "Pill", DotBrush = new SolidColorBrush(Color.Parse("#1E88E5")), Margin = new(0, 0, 0, 8) };
        OverlayIconText row = new() { Name = "Row", Margin = new(0, 8, 0, 0) };
        OverlayChip chip = new() { Name = "Chip", Text = "PLA", Margin = new(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        row.Bind(OverlayIconText.IconProperty, row.GetResourceObservable("IconFlag16Regular"));
        StackPanel panel = new() { Children = { pill, title, value, progress, row, chip } };

        void Update()
        {
            title.Text = widget.Title;
            value.Text = widget.Value;
            value.Classes.Set("emphasized", widget.IsEmphasized);
            progress.Value = widget.Progress;
            pill.Text = widget.Title;
            row.Text = widget.Title;
            title.IsVisible = pill.IsVisible = row.IsVisible = progress.IsVisible = widget.Title.Length > 0;
            chip.IsVisible = !widget.IsSmall;
        }

        void OnChanged(object? sender, PropertyChangedEventArgs e) => Update();
        widget.PropertyChanged += OnChanged;
        panel.DetachedFromLogicalTree += (_, _) => widget.PropertyChanged -= OnChanged;
        Update();
        return panel;
    }
}
