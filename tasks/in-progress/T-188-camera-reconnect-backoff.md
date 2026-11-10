---
id: T-188
title: Kamera – exponenciální backoff při opětovném připojení
status: in-progress
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
- [x] Prodlevy rostou exponenciálně až po strop a po úspěšném připojení se vrátí na výchozí hodnotu (testy).
- [ ] Okno kamery ukazuje skutečnou prodlevu do dalšího pokusu.
- [x] Ukončení streamu během čekání proběhne hned (test).
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
Rozhodnuto uživatelem 2026-11-10:
1. Rozsah prodlevy: **5 s → 1 min** (jako `PrinterMonitorOptions.MaxBackoff`).

Nové (core-dev, 2026-11-10), čekají na rozhodnutí uživatele:
2. **Odpočet v okně kamery neexistuje.** Zadání předpokládá, že okno ukazuje odpočet podle `CameraViewModel._retryDelay`.
   Okno ale ukazuje jen stav („Připojuji se ke kameře…“ / „Bez signálu“ / „Chyba kamery“) a technický důvod; žádný čas
   do dalšího pokusu. `_retryDelay` je jen vlastní opakování okna po vnitřní chybě lease (spadlé zpětné volání snímku),
   ne znovupřipojení kamery. Ruční „Zkusit znovu“ okno také nenabízí. Core už čas dalšího pokusu dodává
   (`CameraStatus.NextAttemptAt`). Má okno ukázat čas do dalšího pokusu, a pokud ano, jak?
   - Návrh A: třetí řádek v překryvu stavu „Další pokus za 35 s“, odpočet po sekundách (časovač 1 s běží jen během čekání
     u aktivního okna), v malém náhledu skrytý (nevejde se do 240 × 135). Nový text do všech `Strings.*.resx`.
   - Návrh B: stejný řádek bez odpočtu, jen zaokrouhlený čas při změně stavu („Další pokus přibližně za 1 min“), žádný časovač.
   - Návrh C: nic neukazovat (kritérium „Okno kamery ukazuje skutečnou prodlevu“ se zruší).
   - Případně i tlačítko „Zkusit znovu“ (okamžitý pokus + vynulování backoffu)?
3. **Otevření okna během dlouhého čekání.** Když spojení drží jiný odběratel (detekce selhání, nahrávání) a kamera je
   v backoffu, nově otevřené nebo obnovené okno se k běžícímu spojení připojí a čeká až 1 min (dřív nejvýš 5 s). Bez
   jiného odběratele se spojení po 10 s zavře a okno začne hned. Má nový odběratel snímků okna čekání zkrátit (hub
   „probudí“ spojení a backoff vynuluje), nebo to nechat tak?


## Poznámky k implementaci
- **Backoff:** `ReconnectingCameraFeed` počítá pokusy za sebou, které skončily dřív, než začaly streamovat. Prodleva =
  `ReconnectDelay` zdvojnásobená za každý další takový pokus, nejvýš `CameraFeedOptions.MaxReconnectDelay` (nová volba,
  výchozí 1 min, `AppServices` ji nastavuje výslovně). Delší `ReconnectDelay` než strop = bez backoffu. Vzorec sdílí s
  `PrinterMonitor` nový interní `Core/Monitoring/ExponentialBackoff` (chování `PrinterMonitor` beze změny).
- **„Úspěšné streamování“** = první paket nebo snímek pokusu (stejný okamžik, kdy se hlásí `Streaming`). Pokus po něm
  čeká zase 5 s. Nové spojení (nový běh hubu, jiná adresa) začíná bez čekání a s backoffem od začátku.
- **Stav nese čas dalšího pokusu:** `CameraStatus.NextAttemptAt` (okamžik podle `TimeProvider.GetUtcNow`, ne délka,
  protože hub pozdějšímu odběrateli předává aktuální stav). Nastaví se u každého `NoSignal` / `Error` z feedu; ostatní
  odběratelé (okno, detekce, nahrávání, test kamery) čtou jen `State`, takže se jich změna nedotkne. Okno ho zatím
  nezobrazuje (otázka 2).
- **Zdroje:** čekání je dál jediné `Task.Delay(…, TimeProvider, stopToken)`, žádné dotazování; stream, enumerátor a
  spojení každého pokusu se zavřou před čekáním jako dřív. Zastavení během čekání skončí hned (test bez posunu času).
- **Testy:** `ReconnectingCameraFeedTests` (řada 5/10/20/40/60/60 s, všechny druhy selhání, reset po streamování,
  `ReconnectDelay` nad stropem, zrušení během minutového čekání, `NextAttemptAt`), `ExponentialBackoffTests`,
  `AppServicesTests` (strop 1 min).
- **Dokumentace:** `docs/architecture.md` (Camera.FFmpeg / znovupřipojení), `manuals/*/manual.md` oddíl 5.4 ve všech
  jazycích. Žádný nový text UI, žádný nový balíček.
- **Nesplněno:** kritérium „Okno kamery ukazuje skutečnou prodlevu“ čeká na otázku 2, proto úkol zůstává v `in-progress`.
