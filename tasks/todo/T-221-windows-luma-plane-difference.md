---
id: T-221
title: Windows – vzorky z roviny Y se liší od BGRA víc, než dovolují meze T-189 (1,72 / +1,44)
status: todo
owner: core-dev
priority: P2
platform: windows
depends_on: []
---

> **Založeno podle rozhodnutí uživatele 2026-11-14** (T-218, otázka 13). Do vyřešení se test
> `GrayscaleSampleEquivalenceTests.TheLumaPlaneSamplesMatchTheBgraSamplesOfTheFixture` **jen v CI na Windows** přeskočí
> s důvodem odkazujícím na tento úkol (`LAYERPEEK_CI=1`, `tests/Shared/ContinuousIntegration.cs`). Lokálně běží dál.
> Souvisí s **T-202** (vlastní build FFmpeg pro Windows místo BtbN).

## Cíl
T-189 bere vzorky detekce z roviny Y dekodéru místo z BGRA snímku. Meze testu ekvivalence (průměrný |rozdíl| ≤ 1,5,
průměrný posun ≤ 1) byly změřené jen na macOS (1,31 / +0,87). Na runneru Windows (`windows-2025`, build FFmpeg BtbN)
vyšlo **1,72 / +1,44** (max 24 úrovní) na fixture `buddy3d.h264`. Posun jasu o ~1,4 úrovně může posunout prahy detekce
(tmavý filament, `DarkBrightness`) na Windows. Zjistit příčinu a rozhodnout, jestli jde o přijatelný rozdíl, nebo o chybu
převodu.

## Rozsah
- Změřit na Windows vývojáře (stejný test, `--show-live-output on`) a porovnat s runnerem (jiné CPU / SIMD cesty swscale?).
- Příčina: build BtbN proti macOS buildu ze zdrojů (verze, příznaky swscale, `color_range` / matice), cesta SIMD.
- Dopad na detektory: skóre bed-change / air-printing na fixture a lokálních vzorcích na Windows proti macOS.
- Podle výsledku oprava převodu, nebo zdůvodněné meze pro Windows (rozhodne uživatel). Navázat na T-202: po přechodu
  na vlastní build FFmpeg změřit znovu.
- Pak odstranit přeskočení v CI a ověřit zelený běh.

## Akceptační kritéria
- [ ] Hodnoty z Windows vývojáře a z runneru zapsané, příčina popsaná
- [ ] Dopad na skóre detektorů na Windows změřený
- [ ] Test v CI na Windows běží bez přeskočení a projde (s opravou nebo s mezemi schválenými uživatelem)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Jsou meze pro Windows přijatelné, pokud se rozdíl ukáže jako vlastnost buildu FFmpeg (rozhodne uživatel po měření)?

## Poznámky k implementaci
(vyplní agent po dokončení)
