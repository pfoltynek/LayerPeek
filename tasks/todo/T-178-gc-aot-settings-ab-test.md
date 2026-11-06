---
id: T-178
title: A/B měření nastavení GC a Native AOT (paměť a CPU)
status: todo
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
- [ ] Tabulka všech variant a scénářů je v `docs/` (česky).
- [ ] Doporučení je předložené uživateli. Případná změna buildu vznikne jako samostatný úkol po jeho souhlasu.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné pro samotné měření; o změně buildu rozhodne uživatel podle výsledků)

## Poznámky k implementaci
(vyplní agent po dokončení)
