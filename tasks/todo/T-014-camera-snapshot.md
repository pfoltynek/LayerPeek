---
id: T-014
title: Snímek z kamery
status: todo
owner: core-dev + ui-dev
priority: P3
platform: all
depends_on: [T-003, T-005]
---

## Cíl
Zobrazit aktuální snímek z kamery tiskárny.

## Rozsah
- PrusaLink: kamery a snímek (ověřit endpointy v OpenAPI)
- Prusa Connect: kamera dle T-004
- UI: okno se snímkem a obnovením (část pro ui-dev)

## Akceptační kritéria
- [ ] Klient vrací obrázek jako stream/bytes, testy s fixture
- [ ] Okno se snímkem
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Přikládat snímek k notifikaci o dokončení/chybě?
- Interval automatického obnovení?

## Poznámky k implementaci
