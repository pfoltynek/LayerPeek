<!--
Koncept issue pro AvaloniaUI/Avalonia (T-234, rozhodnuto uživatelem 2026-11-15). Text issue je anglicky, protože jde
upstream. Odesílá ho uživatel ze svého účtu (https://github.com/AvaloniaUI/Avalonia/issues/new/choose, šablona
"Bug report"); agent nic neodesílá. Před odesláním vyhledat duplicity ("IsPassword", "PasswordChar automation").
Zdroj zjištění: E2E test tests/LayerPeek.App.E2E.Tests/PasswordAutomationTests.cs (T-054) a dekompilace balíčků
Avalonia 12.1.6 (Avalonia.Controls.dll, Avalonia.Win32.Automation.dll) ilspycmd 2026-11-15.
Po odeslání sem připsat odkaz na issue a totéž do poznámek T-234.
-->

# Title

TextBox with PasswordChar reports IsPassword = false to UI Automation (no IsPassword support in AutomationPeer)

# Body

## Describe the bug

A `TextBox` with `PasswordChar` set is exposed to Windows UI Automation as a plain edit control:
`UIA_IsPasswordPropertyId` (30019) is `false`. UIA clients (Narrator, NVDA, JAWS) use `IsPassword` to decide
whether typed characters may be echoed and how the field is announced, so a password box is announced as an ordinary
text field.

The masking itself works correctly: `ValuePattern.Value` and `LegacyIAccessible.Value` return the masked string
(`TextBoxAutomationPeer.Value` uses `PasswordChar`), and copy/cut are disabled. The issue is only the missing
`IsPassword` flag.

## To Reproduce

1. Create a window with:

   ```xml
   <TextBox AutomationProperties.AutomationId="Password"
            AutomationProperties.Name="Password"
            PasswordChar="●" />
   ```

2. Run the app on Windows, type something into the box.
3. Inspect the element with `Inspect.exe` (Windows SDK), Accessibility Insights, or a UIA client, e.g. FlaUI:

   ```csharp
   var box = window.FindFirstDescendant(cf => cf.ByAutomationId("Password"));
   bool isPassword = box.Properties.IsPassword.ValueOrDefault; // false
   ```

## Expected behavior

`IsPassword` (`UIA_IsPasswordPropertyId`) is `true` for a `TextBox` whose `PasswordChar` is set, as for a native
`ES_PASSWORD` edit control or a WPF `PasswordBox`.

## Actual behavior

`IsPassword` is `false` (the provider returns no value for the property, so UIA falls back to the default).

## Analysis

- `Avalonia.Win32.Automation.AutomationNode.GetPropertyValue(int propertyId)` has no case for
  `UiaPropertyId.IsPassword` (the enum value exists in `Avalonia.Win32.Automation.Interop.UiaPropertyId`); the
  `default` branch returns `null`.
- `AutomationPeer` has no API to provide the value (no `IsPassword()` / `IsPasswordCore()`), so an application cannot
  fix it with a custom peer either.
- `TextBoxAutomationPeer` already knows the state: it masks `Value` through `GetDisplayedText(Text, PasswordChar,
  RevealPassword)`.
- macOS: from a look at the 12.1.6 package, `libAvaloniaNative.dylib` does not seem to reference
  `NSAccessibilitySecureTextFieldSubrole`, so the same field is probably not exposed as a secure text field there
  either (not verified at runtime).

## Suggested fix

1. `AutomationPeer`: add `public bool IsPassword()` backed by `protected virtual bool IsPasswordCore() => false`
   (mirrors WPF/UWP `AutomationPeer.IsPassword()` / `IsPasswordCore()`).
2. `TextBoxAutomationPeer`: override `IsPasswordCore()` to return `Owner.PasswordChar != '\0'`, and raise a property
   changed event for it when `PasswordCharProperty` changes (an automation property identifier for it would be
   needed, e.g. `AutomationElementIdentifiers.IsPasswordProperty`). Whether `RevealPassword = true` should report
   `false` is open; WPF keeps `IsPassword = true` for a `PasswordBox` regardless of how it is displayed.
3. `Avalonia.Win32.Automation.AutomationNode.GetPropertyValue`:

   ```csharp
   case UiaPropertyId.IsPassword:
       obj = InvokeSync(() => Peer.IsPassword());
       break;
   ```

4. macOS (`Avalonia.Native`): expose the flag through the native automation peer and report
   `NSAccessibilitySecureTextFieldSubrole` (`AXSecureTextField`) for such an element; optionally AT-SPI
   (`Avalonia.FreeDesktop.AtSpi`) `PasswordText` role.

## Environment

- OS: Windows 11 Pro (10.0.26300), x64
- Avalonia version: 12.1.6; Fluent theme
- .NET 11; same result in a Debug (JIT) build and a Native AOT publish
- UIA client: FlaUI (UIA3), cross-checked with `LegacyIAccessiblePattern`

## Additional context

Found by an automated UI Automation regression test of a password field in an open-source tray application. Happy to
test a nightly build or a PR.
