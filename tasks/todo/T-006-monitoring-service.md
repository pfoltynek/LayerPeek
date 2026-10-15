---
id: T-006
title: Monitorovací služba (polling)
status: todo
owner: core-dev
priority: P1
platform: all
depends_on: [T-001]
---

## Cíl
Služba v Core, která periodicky dotazuje všechny nakonfigurované tiskárny a publikuje aktuální snapshoty.

## Rozsah
- `IPrinterMonitor` – start/stop, událost/observable se snapshotem pro každou tiskárnu
- Paralelní polling, backoff u offline tiskárny
- Testovatelné přes `TimeProvider` a fake `IPrinterClient`

## Akceptační kritéria
- [ ] Chyba jedné tiskárny neovlivní ostatní
- [ ] Zrušení přes `CancellationToken` ukončí smyčku čistě
- [ ] Testy s `FakeTimeProvider`
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
