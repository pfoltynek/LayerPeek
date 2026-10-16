---
id: T-006
title: Monitorovací služba (polling)
status: done
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
- [x] Chyba jedné tiskárny neovlivní ostatní
- [x] Zrušení přes `CancellationToken` ukončí smyčku čistě
- [x] Testy s `FakeTimeProvider`
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- **API:** `IPrinterMonitor` / `PrinterMonitor` v `Core/Monitoring`.
  - `RunAsync(printers, interval, ct)` běží do zrušení tokenu a pak skončí úspěšně. Změna nastavení = zrušit běh, počkat na něj a spustit nový. Souběžný druhý běh se odmítne.
  - Událost `SnapshotUpdated` přichází po každém dotazu z vláken na pozadí.
  - `Snapshots` a `TryGetSnapshot` vrací poslední snapshoty v pořadí konfigurace; po skončení běhu zůstávají snapshoty posledního běhu.
- **Plánování:**
  - Každá tiskárna má vlastní smyčku (`Task.Run`) s `PeriodicTimer` nad `TimeProvider`.
  - Dostupná tiskárna se dotazuje v pevném rytmu. Nedostupná (`!IsReachable`: Offline, Unauthorized, Unknown) s exponenciálním backoffem: interval × 2^(n−1), max. `max(MaxBackoff = 1 min, interval)`, počítáno od konce neúspěšného dotazu.
  - Timeout jednoho dotazu je 10 s; pak se tiskárna hlásí jako Offline.
- **Odolnost:**
  - Výjimka klienta nebo továrny se zaloguje a hlásí jako `Unknown`. Do UI jde jen typ výjimky, ne její text (může obsahovat adresy).
  - Klienta, který ignoruje zrušení, monitor nevolá znovu, dokud nedokončí předchozí požadavek (po 100 ms ochranné lhůtě), a jeho pozdní chybu zaloguje.
  - Výjimka v jednom odběrateli události neovlivní ostatní odběratele.
  - Klienti (`IDisposable` / `IAsyncDisposable`) se po skončení uvolní.
- **Výkon:**
  - V ustáleném stavu monitor sám nealokuje: timer i timeout se znovu používají a publikování jde přes pole handlerů měněné při odběru (copy-on-write).
  - Nové objekty vznikají jen při přechodu mezi chybou a zotavením.
  - Tabulka snapshotů se čte bez zámků.
- **Meze:** interval 1 ms – 1 den, timeout 1 ms – 5 min, `MaxBackoff` ≤ 1 h. Jde o technické meze; uživatelský rozsah 1 s – 5 min hlídá `AppSettings`.
- **Kontrakt `IPrinterClient`** je doplněný: klient musí respektovat zrušení a volání na jedné instanci se nepřekrývají.
- **Balíčky a logování:**
  - Logování přes `Microsoft.Extensions.Logging.Abstractions` (je ve sdíleném frameworku .NET 11, žádný balíček navíc) se source-generated `LoggerMessage`.
  - Nový testovací balíček `Microsoft.Extensions.TimeProvider.Testing` 10.11.0, jen v testech (AOT se testů netýká). Oba schválil uživatel.
- Testy jsou deterministické nad `FakeTimeProvider`; navíc jeden test v reálném čase pro cestu `TryReset` se `TimeProvider.System`. Opakované běhy (i paralelně pod zátěží) prošly bez chyby.
- Code review: 4 kola, poslední bez nálezů.
