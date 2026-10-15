---
id: T-001
title: Doménový model a abstrakce
status: todo
owner: core-dev
priority: P1
depends_on: []
---

## Cíl
Společný model stavu tiskárny nezávislý na zdroji dat (PrusaLink / Prusa Connect).

## Rozsah
- `PrinterState` (Idle, Busy, Printing, Paused, Finished, Stopped, Error, Attention, Offline, Unauthorized, Unknown)
- `PrinterSnapshot` – stav, čas pořízení, aktuální úloha, telemetrie
- `PrintJob` – název souboru, progress (%), uplynulý a zbývající čas
- `Telemetry` – teplota trysky/podložky (aktuální + cílová), ventilátory, osa Z, rychlost/průtok
- `PrinterDefinition` – id, název, typ připojení, adresa/identifikátor
- `IPrinterClient` – `Task<PrinterSnapshot> GetSnapshotAsync(CancellationToken)`; `IPrinterClientFactory`

## Akceptační kritéria
- [ ] Typy v `PrusaTrayDiagnostic.Core`, bez závislosti na HTTP/Avalonia
- [ ] Unit testy na netriviální logiku (např. výpočet zbývajícího času, mapování stavů)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
