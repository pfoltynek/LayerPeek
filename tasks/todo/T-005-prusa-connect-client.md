---
id: T-005
title: Prusa Connect klient
status: todo
owner: core-dev
priority: P2
depends_on: [T-001, T-004]
---

## Cíl
Implementace `IPrinterClient` nad cloudovým Prusa Connect podle výsledku T-004.

## Rozsah
- Přihlášení dle T-004, obnova tokenu
- Načtení seznamu tiskáren účtu a jejich stavu
- Mapování na doménový model

## Akceptační kritéria
- [ ] Kritéria doplní T-004
- [ ] Testy s JSON fixtures, bez reálné sítě
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Závisí na výsledku T-004

## Poznámky k implementaci
