---
id: T-191
title: A/B měření nastavení GC a Native AOT (paměť a CPU)
status: done
owner: qa-reviewer
priority: P3
platform: all
depends_on: []
---

## Cíl
Výkonnostní analýza (2026-11-06): výchozí nastavení GC a AOT se nezkoušelo. Tray aplikaci, která většinu času jen
čeká, může pomoct vypnutý souběžný GC (`ConcurrentGarbageCollection=false`, ušetří vlákno na pozadí) nebo
`OptimizationPreference=Size`. Úkol je **jen měření**. Změna buildu potřebuje souhlas uživatele.

## Rozsah
- Publikovat Native AOT ve variantách: výchozí, `ConcurrentGarbageCollection=false`, `OptimizationPreference=Size`
  a obojí najednou.
- Scénáře na simulátoru: jen sledování 3 tiskáren (30 min), otevřené okno kamery 720p (10 min), malý náhled (10 min).
- Měřit pracovní sadu a privátní paměť, CPU, počet vláken, velikost binárky a čas startu.
- Výsledky zapsat do `docs/` (tabulka) s doporučením. V buildu nic neměnit bez souhlasu.

## Akceptační kritéria
- [x] Tabulka všech variant a scénářů je v `docs/` (česky).
- [x] Doporučení je předložené uživateli. Případná změna buildu vznikne jako samostatný úkol po jeho souhlasu.
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné pro samotné měření; o změně buildu rozhodne uživatel podle výsledků)
- Pro uživatele: ponechat výchozí build (doporučeno), nebo zapnout `OptimizationPreference=Size` (−1,9 % exe, bez měřitelné ztráty) jako samostatný úkol?
- Pro uživatele: doplnit `~/Library/Caches/LayerPeek` do smoke profilu (`Get-SmokeProfile`) jako nový úkol?

Rozhodnuto uživatelem 2026-11-11: **build ponechat výchozí** (žádná změna, žádný navazující úkol); mezipaměť do smoke
profilu **ano, samostatný úkol T-196**.

## Poznámky k implementaci
- Měřeno 2026-11-11 na Macu mini M2 Pro (osx-arm64, SDK 11.0.100): 4 varianty, 3 scénáře, 2 opakování se střídaným
  pořadím, celkem 24 platných běhů a asi 7 h. Tabulky, postup a omezení jsou v [docs/aot-gc-settings.md](../../docs/aot-gc-settings.md),
  odkaz je v `docs/architecture.md`.
- Výsledek: žádná varianta se měřitelně neliší. Sledování 0,08 % jádra a ~50 MB footprint, okno kamery ~20,6 % a ~140 MB,
  malý náhled ~18,2 % a ~90 MB. Rozdíly mezi variantami jsou menší než rozptyl opakování. Vypnutý souběžný GC neušetřil
  vlákno. `OptimizationPreference=Size` zmenší exe o 648 KiB (1,9 %). Pauzy GC změřit nejdou (AOT build má vypnutý `EventSource`).
- **Doporučení: build neměnit.** Případnou změnu (`OptimizationPreference=Size`) by po souhlasu uživatele udělal
  samostatný úkol. V `src/**` se nic neměnilo.
- Release exe nemá izolovaný režim. Profil uživatele se proto před každým během odsunul a po něm obnovil funkcemi
  `Backup-SmokeProfile` / `Restore-SmokeProfile` (jako `aot-smoke.ps1`). Po každém běhu se ověřil otisk (soubory,
  domény `defaults`, Klíčenka): shodný ve všech bězích i na konci.
- Heslo simulátoru nešlo dát do Klíčenky: položku vytvořenou nástrojem `security` macOS aplikaci nevydá bez dialogu,
  a ten při zamčené obrazovce blokuje. Aplikace proto šla přes loopback proxy s `X-Api-Key`. Okno kamery a malý náhled
  otevřela automatika T-151 (`autoClosed` v `camera-windows.json` + `startMode`), bez testovacích háčků.
- Zjištění mimo rozsah (nice-to-have): smoke profil (`Get-SmokeProfile`) nepokrývá mezipaměť `~/Library/Caches/LayerPeek`
  (T-154, `AppDataPaths.GetDefaultCacheDirectory`), jen `Caches/<bundle id>`. Smoke s prázdným profilem ji nevytvoří,
  ale měřicí nebo ruční běh Release exe ano. Tady se hlídala zvlášť.
- `dotnet build`: 0 varování, 0 chyb. `dotnet test`: 10 125 testů, 0 selhalo (267 přeskočeno, na vyžádání / jiný OS).
