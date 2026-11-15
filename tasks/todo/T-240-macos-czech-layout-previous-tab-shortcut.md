---
id: T-240
title: macOS – ⌘⇧[ s českým rozložením klávesnice nepřepne na předchozí záložku Nastavení (T-100 otázka 1)
status: todo
owner: ui-dev
priority: P2
platform: macos
depends_on: [T-100, T-216]
---

## Cíl
S českým softwarovým rozložením (`com.apple.keylayout.Czech`) přepne ⌘⇧] (fyzická klávesa vpravo od „P“ o jednu dál,
na US pozici `]`) hlavní záložku Nastavení dopředu, ale ⌘⇧[ (fyzická klávesa vpravo od „P“, na US pozici `[`, česky
„ú“ / s ⇧ „/“) **nedělá nic** – ani s fokusem v textovém poli, ani s fokusem na záložce. S rozložením US obě zkratky
fungují. T-100 (poznámky třídy `MacShortcuts`) počítá s tím, že zkratky odpovídají i fyzické klávese
(`PhysicalKey.BracketLeft` / `BracketRight`), u `[` to v běžící aplikaci nefunguje.

## Zjištění (2026-11-15, Mac mini Mac14,12, macOS 27.2.1, Avalonia 12.1.6)
- Běh `-Preset release` `artifacts/verify/20261115-155430/` (commit `24d73aa3`): `mac.keys-czech-layout` = `FAIL`
  „Cmd-Shift-[: went from SettingsTabWidgets to SettingsTabWidgets, expected SettingsTabPrinters“. Druhý problém té
  zprávy („the shortcuts changed the printer name … to ''“) byla chyba kontroly (pole se četlo na skryté záložce),
  jméno tiskárny se **nemění** – opraveno na větvi `feature/macos-verify-first-real-run`.
- Opakování na té větvi (kontrola teď zkouší české rozložení a US jako kontrolu, ] pak [ a [ pak ] z textového pole):
  `artifacts/verify/<čas>/evidence/mac.keys-czech-layout/steps.json` – česky ⌘⇧[ ze záložky Widgety i z pole Název
  záložku nezmění, ⌘⇧] ano; US obě zkratky správně. Přední aplikace LayerPeek, klíčové okno Nastavení (sloupce
  `Front`, `KeyWindow`), okna se nemění. Výstup kontroly v poznámkách T-127 (oddíl 1q).
- Helper posílá skutečné události klávesnice s kódem klávesy 33 (`kVK_ANSI_LeftBracket`) a ⌘⇧ (`helper key cmd+shift+[`).
- Hypotézy k ověření: událost nedorazí jako `KeyDown` (AppKit ji zpracuje jako ekvivalent položky menu, např. ⌘? /
  ⌘/ na českém rozložení), nebo Avalonia pro „ú“ hlásí jiný `PhysicalKey`/`Key`. Podrobné logování kláves v
  `SettingsWindow.OnPreviewKeyDown` (jen v testovacím režimu) a `MacShortcutsTests` s událostí, jakou Avalonia skutečně
  pošle.

## Rozsah
- Zjistit, co Avalonia na macOS s českým rozložením pro ⌘⇧ + kód 33 doručí (nebo proč nic), a opravit tak, aby ⌘⇧[
  přepnulo na předchozí záložku (fyzická klávesa jako u ⌘⇧]).
- Jednotkový test `MacShortcuts.MainTabStep` pro zjištěnou podobu události.

## Akceptační kritéria
- [ ] `mac.keys-czech-layout` = `PASS` (české rozložení i US).
- [ ] Žádná změna chování na Windows (Ctrl+Tab, Ctrl+PageUp/PageDown).
- [ ] `dotnet build` a `dotnet test` bez chyb a varování.

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
