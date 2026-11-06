---
id: T-187
title: macOS – pozastavení streamu kamery při zamčení, spánku displeje a zakrytí okna
status: todo
owner: ui-dev
priority: P3
platform: macos
depends_on: [T-186]
---

## Cíl
Protějšek T-186 pro macOS. Na macOS jde navíc spolehlivě zjistit, že je okno úplně zakryté nebo na jiné ploše
(`NSWindow.occlusionState`), takže se stream může pozastavit i v tomto případě.

## Rozsah
- Implementace abstrakce z T-186 pro macOS: zamčení obrazovky (`com.apple.screenIsLocked` /
  `com.apple.screenIsUnlocked` přes `NSDistributedNotificationCenter`) a spánek displeje
  (`NSWorkspaceScreensDidSleepNotification` / `NSWorkspaceScreensDidWakeNotification`).
- Zakrytí okna: `NSWindowDidChangeOcclusionStateNotification` a `occlusionState` okna kamery → okno vypne poptávku
  po snímcích.
- Interop bez reflexe (AOT), stejně jako ostatní macOS kód v App.

## Akceptační kritéria
- [ ] Zamčení, spánek displeje a úplné zakrytí okna pozastaví příjem snímků a po návratu se obnoví.
- [ ] Nahrávání, detekce a timelapse pokračují.
- [ ] Ruční ověření na Macu → T-127.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Stejné jako v T-186 (odpovědi platí pro obě platformy).

## Poznámky k implementaci
(vyplní agent po dokončení)
