---
id: T-011
title: Teploty a diagnostika
status: todo
owner: ui-dev
priority: P2
depends_on: [T-006]
---

## Cíl
Malé okno / popup s diagnostikou otevírané z tray menu.

## Rozsah
- Teplota trysky a podložky (aktuální/cílová), ventilátory, rychlost, osa Z
- Chybové stavy a poslední zprávy tiskárny
- Informace o tiskárně a firmwaru (`/api/v1/info`, `/api/version`)

## Akceptační kritéria
- [ ] Okno se otevře z menu, zavření neukončí aplikaci
- [ ] Hodnoty se živě aktualizují
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Má okno obsahovat graf teplot (navazuje na T-012) a jakou knihovnu grafů použít?

## Poznámky k implementaci
