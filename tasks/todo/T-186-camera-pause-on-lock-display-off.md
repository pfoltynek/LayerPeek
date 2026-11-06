---
id: T-186
title: Kamera – pozastavení streamu při zamčení počítače a vypnutém displeji (Windows)
status: todo
owner: ui-dev
priority: P2
platform: windows
depends_on: []
---

## Cíl
Výkonnostní analýza (2026-11-06): stream okna kamery se pozastaví jen při minimalizaci
(`viewModel.SetActive(WindowState != WindowState.Minimized)` v
[CameraWindow.axaml.cs](../../src/LayerPeek.App/Windows/CameraWindow.axaml.cs)). Při zamčeném počítači, vypnutém
displeji nebo okně na jiné virtuální ploše se obraz dál dekóduje, převádí a vykresluje, i když ho nikdo nevidí.

## Rozsah
- Abstrakce v Core (např. `IDisplayActivityService`: „obrazovka je vidět“ + událost změny), implementace pro Windows
  v App, pro macOS v T-187. Do té doby má macOS výchozí implementaci „vždy vidět“.
- Windows: zamčení a odemčení relace (`WTSRegisterSessionNotification`, `WM_WTSSESSION_CHANGE`) a stav displeje
  (`RegisterPowerSettingNotification` s `GUID_CONSOLE_DISPLAY_STATE`) přes `[LibraryImport]`, bez reflexe (AOT).
- Když obrazovka není vidět, okno vypne jen svou poptávku po snímcích (`SetActive(false)`). Nahrávání, detekce
  a timelapse běží dál. Po návratu se obraz obnoví od nejbližšího klíčového snímku.
- Testy logiky s falešnou službou. Zamčení ověřit E2E podle pravidel pro systémové změny, jinak ručně (→ T-125).

## Akceptační kritéria
- [ ] Při zamčení počítače a při vypnutí displeje okno kamery přestane přijímat snímky a po odemčení nebo zapnutí
      se obnoví.
- [ ] Nahrávání, detekce a timelapse při zamčení pokračují (test).
- [ ] Žádné varování trim/AOT, interop jen přes `[LibraryImport]`.
- [ ] Ruční ověření na Windows → T-125 (pokud se neautomatizuje).
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. **Mění to chování aplikace. Chcete to?** (Blokuje implementaci.)
2. Pozastavit i okno na jiné virtuální ploše (`DWMWA_CLOAKED`)? Okno zakryté jinými okny Windows spolehlivě nehlásí,
   takže tento případ se řešit nebude.
3. Platí to i pro malý náhled „vždy navrchu“, nebo jen pro běžné okno?

## Poznámky k implementaci
(vyplní agent po dokončení)
