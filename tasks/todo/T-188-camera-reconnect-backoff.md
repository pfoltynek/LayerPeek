---
id: T-188
title: Kamera – exponenciální backoff při opětovném připojení
status: todo
owner: core-dev
priority: P2
platform: all
depends_on: []
---

## Cíl
Výkonnostní analýza (2026-11-06): když je kamera offline, `ReconnectingCameraFeed` se pokouší připojit každých
`CameraFeedOptions.ReconnectDelay` (5 s, nastaveno v `AppServices`) donekonečna. Pokaždé vytvoří nový stream,
vlákno pro čtení a TCP spojení ([ReconnectingCameraFeed.cs](../../src/LayerPeek.Core/Cameras/ReconnectingCameraFeed.cs),
`Task.Delay` po neúspěchu). U tiskáren to řeší exponenciální backoff v `PrinterMonitor`, kamera má dostat stejný.

## Rozsah
- Po každém neúspěšném pokusu se prodleva zdvojnásobí od `ReconnectDelay` až po strop (nová volba
  v `CameraFeedOptions`). Po úspěšném streamování se vrátí na začátek.
- Stav a odpočet v okně kamery (`CameraViewModel._retryDelay`) musí odpovídat skutečné prodlevě.
- Ruční opakování pokusu v okně (pokud ho okno nabízí) se provede hned a backoff vynuluje.
- Testy s falešným `TimeProvider`: prodlevy, strop, reset po úspěchu, zrušení během čekání.

## Akceptační kritéria
- [ ] Prodlevy rostou exponenciálně až po strop a po úspěšném připojení se vrátí na výchozí hodnotu (testy).
- [ ] Okno kamery ukazuje skutečnou prodlevu do dalšího pokusu.
- [ ] Ukončení streamu během čekání proběhne hned (test).
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
Rozhodnuto uživatelem 2026-11-10:
1. Rozsah prodlevy: **5 s → 1 min** (jako `PrinterMonitorOptions.MaxBackoff`).


## Poznámky k implementaci
(vyplní agent po dokončení)
