---
id: T-243
title: macOS – panel „Exportovat timelapse do JPEG…“ dovolí vybrat i jiný soubor než .mp4 (⌘⇧G) (T-165 / T-213)
status: done
owner: ui-dev
priority: P3
platform: macos
depends_on: [T-165, T-213]
---

## Cíl
Panel výběru souboru pro „Exportovat timelapse do JPEG… (experimentální)“ má filtr jen na `.mp4`
(`RecordingFilePicker`: `Patterns`, `MimeTypes = video/mp4`, `AppleUniformTypeIdentifiers = public.mpeg-4`). Kontrola
`mac.file-dialogs` (b) přesto vybrala `notes.txt`: zadala jeho celou cestu do pole „Přejít na složku“ (⌘⇧G), výchozí
tlačítko „Otevřít“ bylo povolené a aplikace soubor dostala. Aplikace to zvládne bez pádu (export skončí
`NotATimelapse`), ale filtr panelu se zřejmě neuplatní – buď ho Avalonia na macOS 27 do `NSOpenPanel` nepředá, nebo
macOS cestu z ⌘⇧G filtrem nekontroluje.

## Zjištění (2026-11-15, Mac mini Mac14,12, macOS 27.2.1, Avalonia 12.1.6)
- V běhu `-Preset release` `artifacts/verify/20261115-155430/` se k bodu (b) kontrola nedostala (chyba se záložkami,
  opraveno na větvi `feature/macos-verify-first-real-run`). Opakování na té větvi `20261115-172032` a `-173957`:
  „(b) notes.txt could be chosen in the panel“; `evidence/mac.file-dialogs/panelgo-b-other.txt` (exit 0, tlačítko
  „Otevřít“) a log aplikace „The JPEG export of 'notes.txt' ended without a folder (NotATimelapse).“
- Panel je samostatné okno v procesu aplikace (`AXWindow`, ne list), tlačítka v jazyce systému.
- Seznam souborů panelu helper z AX stromu zatím nečte spolehlivě (sloupcové zobrazení), takže se nedá říct, jestli je
  `notes.txt` v prohlížeči zašedlý.

## Rozsah
- Ručně ověřit: v panelu je `notes.txt` zašedlý? Projde výběr přes ⌘⇧G? Porovnat s malou AppKit aplikací
  (`allowedContentTypes = [.mpeg4Movie]`).
- Je-li filtr neúčinný kvůli Avalonii: obejít (např. kontrola přípony / typu po výběru s hláškou) nebo nahlásit.
  Je-li to chování macOS u ⌘⇧G: upravit kontrolu `mac.file-dialogs` (b) tak, aby filtr posuzovala podle stavu položky
  v seznamu, a ne podle výběru přes ⌘⇧G.

## Akceptační kritéria
- [x] Zjištěná příčina zapsaná v poznámkách; oprava aplikace, nebo úprava kontroly s testem.
- [x] `mac.file-dialogs` (b) = `PASS`. → přesunuto do T-127 (oddíl 1w)
- [x] `dotnet build` a `dotnet test` bez chyb a varování.

## Otevřené otázky
1. ~~Stačí odmítnutí, nebo musí filtr výběr znemožnit?~~ **Rozhodnutí uživatele 2026-11-15: odmítnutí stačí** (`NotATimelapse`, bez pádu). Zjistit příčinu (macOS u ⌘⇧G, nebo Avalonia) a podle ní upravit kontrolu `mac.file-dialogs` (b) – filtr posuzovat podle stavu položky v seznamu. Aplikace se mění, jen pokud filtr nefunguje ani v seznamu souborů.

## Poznámky k implementaci
**Příčina: chování macOS, ne Avalonie.** Panel na Macu vůbec nejde přes Avalonii: `MacRecordingFilePicker` (T-165)
otevírá vlastní `NSOpenPanel` se `setAllowedContentTypes: [UTType typeWithFilenameExtension:@"mp4"]` (= `public.mpeg-4`).
Ověřeno 2026-11-15 na Mac mini (macOS 27.2, Avalonia 12.1.6) malou AppKit aplikací (`NSOpenPanel`,
`allowedContentTypes = [.mpeg4Movie]`, složka s `clip.mp4` a `notes.txt`), řízenou helperem z Terminálu:
- V seznamu panelu (sloupcové zobrazení) je `notes.txt` zašedlý (snímek obrazovky), skutečné kliknutí myší ho nevybere,
  „Open“ zůstane vypnuté. Filtr v seznamu tedy funguje – v čisté AppKit aplikaci i v LayerPeeku.
- ⌘⇧G s celou cestou k `notes.txt` + Return + „Open“ soubor vybere i v čisté AppKit aplikaci (výsledek `OK
  …/notes.txt`): „Přejít na složku“ filtr typu nekontroluje. Totéž platí pro výběr přes AX (`AXSelectedChildren`
  seznamu). Aplikace takový soubor odmítne jako `NotATimelapse` (rozhodnutí uživatele: stačí), aplikace se nemění.
- AX stav položky: `AXEnabled` je `true` i u zašedlé položky (proto ho kontrola dřív nemohla použít). Liší se jen barva
  názvu (`AXAttributedStringForRange` → `AXForegroundColor`): alfa 0,85 u `.mp4`, 0,25 u `notes.txt`. Helper navíc
  u sloupcového zobrazení četl jen první sloupec (`Applications`), ne složku.

**Úprava kontroly (bez změny aplikace):**
- `tools/macos-verify-helper.swift` `panel`: položky složky, kterou panel ukazuje (u sloupcového zobrazení nejpravější
  `AXList`), název z `AXFilename`, nově `alpha` = průhlednost barvy názvu.
- `tools/VerifyTools.psm1` `Get-VerifyPanelItemProblems`: položka je vypnutá při `enabled = false` nebo `alpha < 0,5`,
  zapnutá při `alpha ≥ 0,5`; samotné `enabled = true` už nestačí (neznámý stav).
- `tools/macos-verify.ps1` `mac.file-dialogs` (b): filtr `.mp4` posuzuje podle seznamu (čeká až 5 s, než prohlížeč
  složku načte; nečitelný stav = `SKIP` té části, nikdy `PASS`). `notes.txt` přes ⌘⇧G se dál zkouší, ale očekává se,
  že ho aplikace odmítne jako `NotATimelapse` s notifikací a běží dál (exit 4 helperu = panel ho nevzal, taky v pořádku);
  pak se notifikace smažou, aby odmítnutí nahrávky nenašlo tutéž.
- Pester (`tools/VerifyTools.Tests.ps1`): přípravek s `alpha`, test „AXEnabled true samo nestačí“; 110 prošlo,
  1 přeskočen.
- `docs/macos-manual-testing.md`: popis `panel` (`alpha`) a bodu (b).

**Živá kontrola** z Terminálu (`-Unattended -Only mac.file-dialogs -Allow mac.file-dialogs`,
`artifacts/verify/20261115-204424/`): `SKIP` jen kvůli zakázaným oznámením „LayerPeek E2E“ (Denied); jinak vše
v pořádku: „(b) the list dims notes.txt, not the .mp4 files“ (`panel-b-open.json`: alfa 0,847 u tří `.mp4`, 0,247 u
`notes.txt`), „(b) notes.txt through Go to refused as NotATimelapse“, (a) a (c) beze změny v pořádku. `PASS` (b)
s povolenými oznámeními → T-127 oddíl 1w.
