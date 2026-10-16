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
- Prusa Connect: nedokumentovaný `GET /app/cameras/{camera_id}/snapshots/last` (viz `docs/api/prusa-connect.md`) – jen pokud uživatel schválí nedokumentované API (T-005)
- UI: okno se snímkem a obnovením (část pro ui-dev)

## Akceptační kritéria
- [ ] Klient vrací obrázek jako stream/bytes, testy s fixture
- [ ] Okno se snímkem
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Přikládat snímek k notifikaci o dokončení/chybě?
- Interval automatického obnovení?

**Stav (2026-10-16):** Blokováno – Buddy firmware v PrusaLink kamery nemá (`/api/v1/cameras` vrací 404) a Prusa Connect klient (T-005) je odložený. Úkol zůstává v todo, dokud nebude zdroj dat.

## Poznámky k implementaci
