using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>UI Automation helpers over the automation ids of the app's views.</summary>
internal static class Ui
{
    private static readonly TimeSpan FindTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Waits for the element with <paramref name="automationId"/> under <paramref name="root"/>.</summary>
    public static async Task<AutomationElement> FindAsync(AutomationElement root, string automationId)
    {
        AutomationElement? found = null;
        await Wait.UntilAsync(() => (found = root.FindFirstDescendant(cf => cf.ByAutomationId(automationId))) is not null, FindTimeout, $"element '{automationId}'");
        return found!;
    }

    public static AutomationElement? TryFind(AutomationElement root, string automationId) =>
        root.FindFirstDescendant(cf => cf.ByAutomationId(automationId));

    /// <summary>Whether the element exists and is on screen (Avalonia removes hidden elements from the tree).</summary>
    public static bool IsShown(AutomationElement root, string automationId) =>
        TryFind(root, automationId) is { } element && !element.Properties.IsOffscreen.ValueOrDefault;

    /// <summary>Selects a tab item or list item.</summary>
    public static async Task SelectAsync(AutomationElement root, string automationId)
    {
        AutomationElement element = await FindAsync(root, automationId);
        element.Patterns.SelectionItem.Pattern.Select();
        await Wait.UntilAsync(() => element.Patterns.SelectionItem.Pattern.IsSelected.ValueOrDefault, FindTimeout, $"'{automationId}' selected");
    }

    /// <summary>The list items of a list box.</summary>
    public static async Task<AutomationElement[]> ItemsAsync(AutomationElement root, string listId, int expected)
    {
        AutomationElement list = await FindAsync(root, listId);
        AutomationElement[] items = [];
        await Wait.UntilAsync(() => (items = list.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem))).Length == expected, FindTimeout, $"{expected} items in '{listId}'");
        return items;
    }

    public static async Task SetTextAsync(AutomationElement root, string automationId, string text)
    {
        AutomationElement element = await FindAsync(root, automationId);
        element.Patterns.Value.Pattern.SetValue(text);
        await Wait.UntilAsync(() => element.Patterns.Value.Pattern.Value.ValueOrDefault == text, FindTimeout, $"'{automationId}' = '{text}'");
    }

    public static async Task InvokeAsync(AutomationElement root, string automationId)
    {
        AutomationElement element = await FindAsync(root, automationId);
        element.Patterns.Invoke.Pattern.Invoke();
    }

    public static async Task SetCheckedAsync(AutomationElement root, string automationId, bool isChecked)
    {
        AutomationElement element = await FindAsync(root, automationId);
        var toggle = element.Patterns.Toggle.Pattern;
        ToggleState wanted = isChecked ? ToggleState.On : ToggleState.Off;
        for (int i = 0; i < 3 && toggle.ToggleState.ValueOrDefault != wanted; i++)
        {
            toggle.Toggle();
        }

        Assert.Equal(wanted, toggle.ToggleState.ValueOrDefault);
    }

    /// <summary>Selects the combo box item at <paramref name="index"/>.</summary>
    public static Task SelectComboItemAsync(AutomationElement root, string automationId, int index) =>
        SelectComboItemAsync(root, automationId, (_, i) => i == index, $"item {index}");

    /// <summary>Selects the first combo box item matching <paramref name="match"/> (item text, index); returns its text.</summary>
    public static async Task<string> SelectComboItemAsync(AutomationElement root, string automationId, Func<string, int, bool> match, string what)
    {
        AutomationElement combo = await FindAsync(root, automationId);
        combo.Patterns.ExpandCollapse.Pattern.Expand();
        try
        {
            AutomationElement? item = null;
            await Wait.UntilAsync(
                () =>
                {
                    AutomationElement[] items = combo.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
                    item = items.Where((candidate, i) => match(ItemText(candidate), i)).FirstOrDefault();
                    return item is not null;
                },
                FindTimeout,
                $"{what} in '{automationId}'");
            string text = ItemText(item!);
            item!.Patterns.SelectionItem.Pattern.Select();
            return text;
        }
        finally
        {
            combo.Patterns.ExpandCollapse.Pattern.Collapse();
        }
    }

    /// <summary>The texts of all items of a combo box (opened for reading, closed again) and the index of the selected one (-1: none).</summary>
    public static async Task<(List<string> Items, int Selected)> ComboItemsAsync(AutomationElement root, string automationId)
    {
        AutomationElement combo = await FindAsync(root, automationId);
        combo.Patterns.ExpandCollapse.Pattern.Expand();
        try
        {
            AutomationElement[] items = [];
            await Wait.UntilAsync(() => (items = combo.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))).Length > 0, FindTimeout, $"the items of '{automationId}'");
            List<string> texts = [.. items.Select(ItemText)];
            int selected = Array.FindIndex(items, static item => item.Patterns.SelectionItem.TryGetPattern(out FlaUI.Core.Patterns.ISelectionItemPattern? pattern) && pattern.IsSelected.ValueOrDefault);
            return (texts, selected);
        }
        finally
        {
            combo.Patterns.ExpandCollapse.Pattern.Collapse();
        }
    }

    /// <summary>The text of a list item: its name, or the text of its first text child (data templates).</summary>
    private static string ItemText(AutomationElement item) =>
        item.Properties.Name.ValueOrDefault is { Length: > 0 } name
            ? name
            : item.FindFirstDescendant(cf => cf.ByControlType(ControlType.Text))?.Properties.Name.ValueOrDefault ?? string.Empty;

    /// <summary>The text of a text block (its UIA name).</summary>
    public static string Text(AutomationElement root, string automationId) =>
        TryFind(root, automationId)?.Properties.Name.ValueOrDefault ?? string.Empty;
}
