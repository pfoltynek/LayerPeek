---
id: T-005
title: Prusa Connect klient
status: todo
owner: core-dev
priority: P2
platform: all
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
- Windows Credential Manager má limit 2560 B na jeden tajný údaj (UTF-8) – pokud Connect používá OAuth/JWT tokeny, počítat s tím (např. ukládat jen refresh token nebo tokeny pod samostatnými klíči). Implementace pro macOS Keychain je T-021.

## Poznámky k implementaci
