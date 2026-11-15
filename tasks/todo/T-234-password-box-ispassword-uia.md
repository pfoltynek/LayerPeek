---
id: T-234
title: Pole hesla – UI Automation hlásí IsPassword = False a délku hesla (Avalonia 12.1.6)
status: todo
owner: ui-dev
priority: P3
platform: windows
depends_on: [T-054]
---

## Cíl
Měření v T-054 (2026-11-15, Avalonia 12.1.6, Windows 11, Debug i Native AOT) ukázalo, že pole hesla PrusaLink
v okně nastavení (`TextBox` s `PasswordChar="●"`, `PrinterPassword`) **heslo samotné přes UI Automation nevydá**
(`ValuePattern.Value` i `LegacyIAccessible.Value` = maska `●`, `Name` / `HelpText` = popisek a nápověda, `TextPattern`
prvek nenabízí, v surovém stromu okna heslo nemá žádný prvek). Zbyly ale dva nálezy:

1. **`IsPassword` = `False`.** Win32 vrstva automatizace Avalonie (`Avalonia.Win32.Automation.AutomationNode.GetPropertyValue`)
   vlastnost `UIA_IsPasswordPropertyId` vůbec neobsluhuje a `AutomationPeer` pro ni nemá API, takže ji nejde nastavit ani
   vlastním peerem. Klienti UIA (Předčítání, NVDA, JAWS) se podle `IsPassword` rozhodují, zda při psaní předčítají znaky;
   pole se jim jeví jako běžné textové pole. Možný důsledek: Předčítání nahlas přečte psané znaky hesla. **Neověřeno**
   (vyžaduje zapnuté Předčítání).
2. **Délka hesla.** `ValuePattern.Value` vrací masku o délce hesla (např. 23 × `●`), takže každý proces stejného
   uživatele zjistí délku hesla. Na obrazovce je stejná informace vidět; nativní pole hesla Windows (`ES_PASSWORD`)
   a WPF `PasswordBox` hodnotu přes `ValuePattern` nevydávají vůbec.

Kopírování a vyjmutí z pole hesla Avalonia zakazuje (`TextBox.IsPasswordBox` → `CanCopy`/`CanCut` = false), schránkou
heslo neunikne.

## Rozsah (návrh opravy – rozhodne uživatel)
- `IsPassword`: oprava jen v Avalonii (vlastnost v `AutomationPeer`, např. `IsPasswordCore`, a její mapování
  v `AutomationNode` Windows a v macOS vrstvě). Návrh: nahlásit upstream (issue / PR v `AvaloniaUI/Avalonia`)
  a po vydání opravy aktualizovat Avalonii; v aplikaci se jinak nic měnit nedá.
- Délka hesla (volitelně): vlastní `TextBox` pro hesla s `OnCreateAutomationPeer`, jehož peer znovu implementuje
  `IValueProvider` a vrací prázdnou hodnotu (případně pevnou masku) místo masky o délce hesla; `SetValue` zůstane
  (testy T-073 a T-054 heslo zadávají přes `ValuePattern.SetValue`). Ověřit dopad na čtečky (prázdné pole vs. „vyplněno“).
- Po opravě `IsPassword` zpřísnit E2E test `PasswordAutomationTests` (T-054): dnes `IsPassword` jen zapisuje do
  zprávy, po opravě má vyžadovat `True`.
- macOS: ověřit, zda Avalonia.Native vydává pole jako `AXSecureTextField` a nevydává heslo přes `AXValue`
  (přes `tools/macos-verify-helper.swift` / AX) – samostatný bod do T-127, nebo vlastní úkol.

## Akceptační kritéria
- [ ] Rozhodnutí uživatele k otevřeným otázkám zapsané níže
- [ ] `IsPassword` = `True` u `PrinterPassword` (po opravě v Avalonii) a test `PasswordAutomationTests` to vyžaduje
- [ ] (pokud se schválí) `ValuePattern.Value` pole hesla nevydává délku hesla; test to ověřuje
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; E2E běh na Windows úspěšný

## Otevřené otázky
- Nahlásit chybějící `IsPassword` upstream do Avalonie (issue, případně PR)? Kdo a kdy?
- Skrývat délku hesla vlastním peerem (prázdná / pevná hodnota `ValuePattern.Value`), nebo ji přijmout jako
  rovnocennou tomu, co je vidět na obrazovce?
- Ověřit ručně s Předčítáním, zda při psaní do pole hesla předčítá znaky (zapnutí Předčítání je zásah do systému)?
- macOS: ověřit stejné vlastnosti přes AX v T-127, nebo samostatným úkolem?

## Poznámky k implementaci
(vyplní agent po dokončení)
