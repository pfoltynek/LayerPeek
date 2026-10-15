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

## Poznámky k implementaci
