---
id: T-012
title: Historie telemetrie
status: todo
owner: core-dev
priority: P3
platform: all
depends_on: [T-006]
---

## Cíl
Uchovávat historii teplot a stavů pro grafy a diagnostiku.

## Rozsah
- Ring buffer v paměti pro každou tiskárnu
- Volitelně perzistence

## Akceptační kritéria
- [ ] Testy bufferu (kapacita, pořadí, souběžný přístup)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Jak dlouhou historii držet? Ukládat na disk (např. SQLite), nebo jen v paměti?

**Rozhodnutí (2026-10-16):** Jen v paměti, 24 h historie na tiskárnu (ring buffer), bez perzistence na disk.

## Poznámky k implementaci
