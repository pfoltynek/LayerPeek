---
id: T-239
title: macOS – záložky Nastavení nejdou přepnout přes Accessibility (AXPress, VoiceOver) (Avalonia 12.1.6)
status: todo
owner: ui-dev
priority: P3
platform: macos
depends_on: []
---

## Cíl
Záložky okna Nastavení (`TabItem`, v AX `AXRadioButton`: `SettingsTab*` i podzáložky tiskárny `PrinterTab*`) na
AXPress nereagují: akce vrátí úspěch, ale záložka se nevybere. AXPress je to, co dělá VoiceOver (VO-mezerník) a jiné
asistenční nástroje; uživatel VoiceOveru tak záložky přepne jen klávesnicí (⌃Tab, ⌘⇧] / ⌘⇧[, ⌘⌥→ / ⌘⌥←), ne
„aktivací“ záložky. Skutečný klik myší záložku vybere.

## Zjištění (první běh s odemčenou obrazovkou, 2026-11-15)
- Běh `artifacts/verify/20261115-155430/` (commit `24d73aa3`): šest kontrol skončilo `FAIL`, protože po AXPress na
  záložku chyběl prvek z ní (`PrinterCameraWindowApply`, `PrinterCameraTest`, `PrinterCameraWindowNormalTopmost`,
  `PrinterDetectionRegion`, `SettingsTimelapseTrigger`, `SettingsRecordingFolderChange`). Strom AX po
  AXPress na `SettingsTabRecording` (`evidence/mac.timelapse-layers/settings-by-time.json`,
  `evidence/mac.recording/locked-settings-ax.json`) ukazuje dál vybranou záložku Tiskárny (`PART_SelectedPipe` pod
  `SettingsTabPrinters`). Helper `axpress` neohlásil chybu (bez `-AllowFailure` v `mac.file-dialogs` by výjimku vyhodil).
- Kontroly teď záložku vybírají skutečným klikem (`Select-SettingsTab` v `tools/macos-verify.ps1`, AXPress a klávesnice
  jako záloha) – opakované běhy na větvi `feature/macos-verify-first-real-run` záložky přepnuly klikem.
- Pravděpodobná příčina: `TabItemAutomationPeer` Avalonie poskytuje `ISelectionItemProvider`, ne `IInvokeProvider`, a
  nativní `accessibilityPerformPress` macOS v Avalonia 12.1.6 výběr nevolá. Ověřit ve zdrojích Avalonie; jde-li o chybu
  Avalonie, nahlásit ji (bez zápisu na GitHub bez souhlasu uživatele) a případně obejít vlastním automation peerem.

## Rozsah
- Rozbor (Avalonia `AvnAccessibilityElement` / automation peer `TabItem`), rozhodnutí: vlastní peer s `IInvokeProvider`
  (výběr záložky) pro `TabItem` v Nastavení, nebo čekat na Avalonii.
- Po opravě: v `Select-SettingsTab` stačí AXPress (klik zůstane jako záloha) a ruční bod VoiceOveru v T-127 (T-117 / T-122)
  doplnit o přepnutí záložky.

## Akceptační kritéria
- [ ] AXPress na `SettingsTabRecording` (helper `axpress <pid> SettingsTabRecording`) vybere záložku Nahrávání.
- [ ] Totéž pro podzáložku `PrinterTabCamera`.
- [ ] Windows: UI Automation `SelectionItemPattern.Select` záložek funguje dál (beze změny).
- [ ] `dotnet build` a `dotnet test` bez chyb a varování.

## Otevřené otázky
1. Obejít chybu Avalonie vlastním automation peerem, nebo počkat na opravu v Avalonii (a do té doby záložky pro
   VoiceOver jen klávesnicí)?

## Poznámky k implementaci
(vyplní agent po dokončení)
