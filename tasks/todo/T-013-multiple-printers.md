---
id: T-013
title: Více tiskáren
status: todo
owner: ui-dev
priority: P2
platform: all
depends_on: [T-007, T-002]
---

## Cíl
Monitorovat více tiskáren současně.

## Rozsah
- Agregovaný stav ikony (nejzávažnější stav vyhrává)
- Tiskárny v menu jako podmenu
- Notifikace s názvem tiskárny

## Akceptační kritéria
- [ ] Pravidlo agregace pokryté testy
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Jedna ikona pro všechny tiskárny, nebo ikona pro každou tiskárnu zvlášť?
- Pořadí závažnosti stavů?

**Rozhodnutí (2026-10-16):** Jedna agregovaná ikona, tiskárny jako podmenu. Pořadí závažnosti: chyba > vyžaduje pozornost > offline > pozastaveno > tiskne > hotovo > idle (detaily stavů Busy/Stopped/Unknown/Unauthorized viz T-007).

## Poznámky k implementaci
