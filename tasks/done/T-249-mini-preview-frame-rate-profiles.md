---
id: T-249
title: Malý náhled kamery – profily snímkové frekvence Úsporný 10 fps a Vyvážený 15 fps (dosud 5 / 10)
status: done
owner: core-dev
priority: P2
platform: all
depends_on: [T-185]
---

## Cíl
Poznatek z používání (uživatel 2026-11-22): profily snímkové frekvence malého náhledu v Nastavení > Kamera (T-185)
jsou zbytečně nízké. Nové hodnoty: **Úsporný 10 fps**, **Vyvážený 15 fps**. Plynulý (bez omezení) a Vlastní (posuvník
1–30 fps) se nemění.

## Rozsah
- `MiniPreviewFrameRate.Economy` = 10, `MiniPreviewFrameRate.Balanced` = 15 (`src/LayerPeek.Core/Cameras/MiniPreviewFrameRate.cs`).
- Výchozí profil zůstává Vyvážený, tedy nově 15 fps (rozhodnutí uživatele 2026-11-22). Výchozí hodnota se do souboru
  nastavení nezapisuje, takže uživatelé s výchozí hodnotou dostanou 15 fps bez migrace.
- **Bez migrace** (rozhodnutí uživatele 2026-11-22): uložená 5 fps (dřívější Úsporný) zůstane 5 fps a zobrazí se jako
  Vlastní; výslovně uložených 10 fps se nově zobrazí jako Úsporný.
- Nápovědy profilů `SettingsMiniFrameRateEconomyHint` / `SettingsMiniFrameRateBalancedHint` ve všech `Strings.*.resx`
  (cs, de, en, es, fr, it, pl, sk) – nová čísla, text přizpůsobit (10 fps už není „trhaný“ pohyb jako 5 fps).
- Komentáře a dokumentace s čísly 5 / 10 fps: `SettingsViewModel.MiniFrameRate.cs`, `AppSettings.cs`, `docs/**`,
  `manuals/<lang>/manual.md` (všechny jazyky), `README.md` + `README.cz.md`, pokud čísla uvádějí, `CHANGELOG.md`
  (Unreleased).
- Testy: `MiniPreviewFrameRateSettingsTests`, `MiniFrameRateSettingsTests`, `CameraViewModelFrameRateTests`,
  `SettingsWindowCameraTabUiTests` a další testy s pevnými čísly 5 / 10 upravit; přidat test, že uložená 5 fps se načte
  jako Vlastní 5 fps.
- Snímky obrazovky v `docs/images/<lang>/`, pokud ukazují nápovědu profilu s čísly – přegenerovat.

## Akceptační kritéria
- [x] Úsporný = 10 fps, Vyvážený = 15 fps (výchozí), Plynulý a Vlastní beze změny
- [x] Soubor nastavení bez hodnoty načte 15 fps; uložená 5 fps se načte jako Vlastní 5 fps; uložených 10 fps jako Úsporný
- [x] Nápovědy profilů ve všech jazycích odpovídají novým hodnotám
- [x] Manuály všech jazyků, dokumentace a CHANGELOG aktualizované
- [x] `dotnet build` a `dotnet test` bez chyb a varování (Windows; sada snímků macOS → přesunuto do T-127, oddíl 1x)

## Otevřené otázky
- (žádné – výchozí profil a migrace rozhodnuty uživatelem 2026-11-22)

## Poznámky k implementaci
- `MiniPreviewFrameRate.Economy` = 10, `Balanced` = 15 (= `Default`); bez migrace – `SettingsMapper` výchozí hodnotu
  nezapisuje, takže soubor bez `miniPreviewFrameRate` načte 15 fps, uložená 5 fps zůstane (profil Vlastní) a uložených
  10 fps je nově Úsporný (test `ALimitSavedBeforeT249IsLoadedUnchanged`).
- Nápovědy `SettingsMiniFrameRateEconomyHint` / `SettingsMiniFrameRateBalancedHint` v 8 jazycích: Úsporný už není
  „trhaný“, ale „pro malý obraz stále dost plynulé“; Vyvážený „plynulý pohyb“. Zachované NBSP (číslo–jednotka, fr před
  dvojtečkou a středníkem).
- Manuály (8 jazyků, oddíl o malém náhledu a 11.6), `README.md` / `README.cz.md`, `docs/architecture.md`, CHANGELOG
  (Unreleased › Changed). Měření v `docs/hw-decoding.md` (výchozí 10 fps v době měření) jsou historický záznam, beze změny.
- Testy: Core `MiniPreviewFrameRateSettingsTests`, `CameraFrameRateLimitTests` (profil 15 fps), App
  `MiniFrameRateSettingsTests`, `CameraViewModelFrameRateTests`, UI `SettingsWindowCameraTabUiTests`.
- Snímky: `docs/images/<lang>/` záložku Kamera s nápovědou profilu neukazují (jen podzáložku tiskárny), přegenerování
  není potřeba. Referenční snímek `tests/LayerPeek.App.UiTests/Snapshots/settings-camera-tab.png` (Windows) přegenerován;
  sada macOS → T-127 (oddíl 1x).
