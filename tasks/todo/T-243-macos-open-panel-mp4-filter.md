---
id: T-243
title: macOS – panel „Exportovat timelapse do JPEG…“ dovolí vybrat i jiný soubor než .mp4 (⌘⇧G) (T-165 / T-213)
status: todo
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
- [ ] Zjištěná příčina zapsaná v poznámkách; oprava aplikace, nebo úprava kontroly s testem.
- [ ] `mac.file-dialogs` (b) = `PASS`.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování.

## Otevřené otázky
1. ~~Stačí odmítnutí, nebo musí filtr výběr znemožnit?~~ **Rozhodnutí uživatele 2026-11-15: odmítnutí stačí** (`NotATimelapse`, bez pádu). Zjistit příčinu (macOS u ⌘⇧G, nebo Avalonia) a podle ní upravit kontrolu `mac.file-dialogs` (b) – filtr posuzovat podle stavu položky v seznamu. Aplikace se mění, jen pokud filtr nefunguje ani v seznamu souborů.

## Poznámky k implementaci
(vyplní agent po dokončení)
