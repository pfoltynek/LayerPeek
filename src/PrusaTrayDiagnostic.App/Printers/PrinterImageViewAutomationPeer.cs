using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls.Automation.Peers;

namespace PrusaTrayDiagnostic.App.Printers;

/// <summary>
/// What UI Automation (Windows) and the accessibility API of macOS report for a <see cref="PrinterImageView"/>: an image,
/// like Avalonia's <c>Image</c>, but only in the control and content view while it has an
/// <see cref="AutomationProperties.NameProperty"/> (the Diagnostics header). Without a name it stays decorative (the
/// printer list row and the image button name the printer themselves), as the drawing control was before (T-069 Q3).
/// An explicit <see cref="AutomationProperties.AccessibilityViewProperty"/> still wins (Avalonia checks it first).
/// </summary>
internal sealed class PrinterImageViewAutomationPeer(PrinterImageView owner) : ImageAutomationPeer(owner)
{
    protected override bool IsContentElementCore() => IsNamed;

    protected override bool IsControlElementCore() => IsNamed;

    // Read on every query (cheap: one property lookup), so a name that a binding sets or changes later counts at once.
    private bool IsNamed => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(Owner));
}
