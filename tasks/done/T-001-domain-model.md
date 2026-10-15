---
id: T-001
title: Doménový model a abstrakce
status: done
owner: core-dev
priority: P1
platform: all
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
- [x] Typy v `PrusaTrayDiagnostic.Core`, bez závislosti na HTTP/Avalonia
- [x] Unit testy na netriviální logiku (např. výpočet zbývajícího času, mapování stavů)
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- Typy jsou v `src/PrusaTrayDiagnostic.Core/Printers/` (namespace `PrusaTrayDiagnostic.Core.Printers`), všechny jsou immutable `sealed record`.
- Invarianty se hlídají v `init` accessorech (C# `field`), platí tedy i pro výrazy `with`:
  - `PrinterDefinition` – texty se ořežou a nesmí být prázdné, `ConnectionType` musí být definovaná hodnota.
  - `PrintJob` – progress se omezí na 0–100 (NaN → 0), záporné časy → `null` (neznámé), prázdný název souboru → `null`.
- `PrintJob.EstimatedRemaining` vrací čas hlášený tiskárnou, jinak lineární odhad z `Elapsed` a progressu. Při přetečení vrací `null`.
- `PrinterSnapshot.EstimatedCompletion` je chráněný proti přetečení `DateTimeOffset` i pro lokální časové posuny (offsety).
- Rovnost snapshotů zahrnuje `Timestamp` a telemetrii, proto se nehodí k detekci změn – v T-006/T-009 porovnávat `State`/`Job`.
- Klasifikace stavů: `PrinterStateExtensions` (`IsReachable`, `HasActiveJob`, `RequiresAttention`). Nedefinované hodnoty se chovají jako `Unknown`.
- Kontrakt `IPrinterClient`: očekávané chyby (offline, 401, …) se vrací jako stav ve snapshotu, ne jako výjimka. Výjimkou je jen zrušení (`OperationCanceledException`).
- Mapování stavů z PrusaLink/Connect DTO je součástí T-003/T-005.
- Žádné nové NuGet balíčky.
