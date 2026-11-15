---
id: T-221
title: Windows – vzorky z roviny Y se liší od BGRA víc, než dovolují meze T-189 (1,72 / +1,44)
status: done
owner: core-dev
priority: P2
platform: windows
depends_on: []
---

> **Založeno podle rozhodnutí uživatele 2026-11-14** (T-218, otázka 13). Do vyřešení se test
> `GrayscaleSampleEquivalenceTests.TheLumaPlaneSamplesMatchTheBgraSamplesOfTheFixture` **jen v CI na Windows** přeskočí
> s důvodem odkazujícím na tento úkol (`LAYERPEEK_CI=1`, `tests/Shared/ContinuousIntegration.cs`). Lokálně běží dál.
> Souvisí s **T-202** (vlastní build FFmpeg pro Windows místo BtbN).

> **Rozhodnutí uživatele 2026-11-15:** řeší se v rámci **T-202** (vlastní build FFmpeg pro Windows). Meze podle
> architektury (arm64 1,5 / 1, x64 2,0 / 1,5) a měření na Windows vývojáře (1,72 / +1,44; s `SWS_ACCURATE_RND |
> SWS_BITEXACT` 1,54 / +1,20; detekce změny podložky na `buddy3d.h264` beze změny, max. rozdíl skóre 0,0006) už
> zavedl **T-228**. Zbytek (měření s vlastním buildem, dopad na detektory, schválení meze x64, odstranění přeskočení
> v CI) dodělá T-202.

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
- [x] Hodnoty z Windows vývojáře a z runneru zapsané, příčina popsaná
- [x] Dopad na skóre detektorů na Windows změřený
- [x] Test v CI na Windows běží bez přeskočení a projde (s opravou nebo s mezemi schválenými uživatelem) –
      přeskočení odstraněno v T-202 (2026-11-15); zelený job „Build and test (Windows)“ v CI 43745897232 (589fd360)
      a 43748044752 (22f0000e, ruční běh), test nepřeskočen
- [x] `dotnet build` a `dotnet test` bez chyb a varování (v rámci T-202, viz tam)

## Otevřené otázky
- ~~Jsou meze pro Windows přijatelné, pokud se rozdíl ukáže jako vlastnost buildu FFmpeg (rozhodne uživatel po měření)?~~
  **Rozhodnuto uživatelem 2026-11-15:** ano, mez x64 2,0 / 1,7 (příčina je SIMD swscale na x64, ne build).

## Poznámky k implementaci
- 2026-11-15 (hlavní agent): CI na Windows (`windows-2025`) s vlastním buildem FFmpeg (T-202) a mezemi x64
  2,0 / 1,7: `GrayscaleSampleEquivalenceTests` prošel bez přeskočení v bězích 43745897232 (589fd360) a
  43748044752 (22f0000e). Úkol uzavřen spolu s T-202.
Řešeno v rámci T-202 (rozhodnutí uživatele 2026-11-15), měřeno 2026-11-15 na Windows 11 x64 vývojáře.

- **Hodnoty (fixture `buddy3d.h264`, 71 snímků, 320 × 180):**
  - Windows vývojáře, build BtbN (T-228): průměrný |rozdíl| **1,72**, průměrný posun **+1,44**, max 24 úrovní.
  - Runner CI `windows-2025`, build BtbN (T-218): **1,72 / +1,44**, max 24.
  - Windows vývojáře, **vlastní build T-202** (`build/windows/build-ffmpeg.sh`, MinGW-w64 gcc 16.2.0): **1,72 / +1,44**,
    max 24 – bit po bitu stejný výsledek jako BtbN.
  - macOS arm64 (T-189): 1,31 / +0,87.
- **Příčina:** vlastnost swscale na x64, ne buildu BtbN. Převod YUV → BGRA starého vzorku (a plošné zmenšení) počítá
  swscale kódem SIMD podle CPU (NEON na arm64, SSE/AVX na x64), jiné zaokrouhlení dá jiný rozdíl proti rovině Y.
  Vlastní build se stejnými zdroji FFmpeg 9.0.2 a stejnými cestami x86 (nasm) dává přesně stejná čísla jako BtbN.
  S `SWS_ACCURATE_RND | SWS_BITEXACT` by to bylo 1,54 / +1,20 (T-228). Na rovinu Y detektorů (to, co aplikace od
  T-189 používá) to vliv nemá, mění se jen srovnávací vzorek z BGRA.
- **Dopad na detektory na Windows (vlastní build, dočasný pokus jako v T-228, vrácený):** bed-change na fixture
  smyčkou 30 min po 2 s (900 vzorků), práh 0,0030 (výchozí citlivost), tolerance testu klipu 0,0008:
  - výřez Core One, bez poruchy: skóre 0 → 0, žádný poplach v obou cestách;
  - výřez Core One + tmavá hrudka (r 6 px, jas 30): max. |rozdíl skóre| **0,0006**, 100 → 100 vzorků nad prahem, první
    poplach 15:18 → 15:18; s tmavým filamentem 0,0001, 15:08 → 15:08;
  - výřez Core One + světlá hrudka (r 5 px, jas 200): 0,0004, 48 → 45 vzorků nad prahem (u prahu), poplach 15:46 → 15:46;
  - celý snímek (fixture není pohled Core One, scéna se mění): max. |rozdíl| 0,0010, poplachy 02:50 → 02:50 ve všech
    variantách.
  Poplachy jsou ve všech variantách stejné a ve stejný čas. Lokální klip Core One (`~/LayerPeek-samples`) na stroji
  není, test klipu se přeskočil.
- **Meze:** test s mezemi x64 2,0 / 1,5 (T-228) prošel. **Nová mez x64 s rezervou ~15 %, rozhodnuto uživatelem
  2026-11-15** (T-202, otázka 4): průměrný |rozdíl| **2,0** (1,72 × 1,15 = 1,98, beze změny), průměrný posun **1,7**
  (1,44 × 1,15 = 1,66; dřív 1,5 = rezerva jen 4 %); arm64 beze změny 1,5 / 1. Zapsáno v
  `GrayscaleSampleEquivalenceTests.Limits` i s komentářem naměřených hodnot a příčiny (SIMD swscale na x64).
- **CI:** `Assert.SkipWhen(ContinuousIntegration.IsWindowsRunner, …)` v
  `GrayscaleSampleEquivalenceTests.TheLumaPlaneSamplesMatchTheBgraSamplesOfTheFixture` odstraněno (T-202); pomocník
  `ContinuousIntegration` zůstává pro T-220. Komentář `LAYERPEEK_CI` v `.github/workflows/ci.yml` a tabulka
  v `docs/prerequisites.md` už T-221 neuvádí. Zelený běh v CI na Windows zbývá ověřit po pushi (hlavní agent); runner
  může mít jiné CPU, ale s BtbN dal stejná čísla jako stroj vývojáře.
