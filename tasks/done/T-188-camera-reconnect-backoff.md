---
id: T-188
title: Kamera – exponenciální backoff při opětovném připojení
status: done
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
- [x] Okno kamery ukazuje skutečnou prodlevu do dalšího pokusu (přibližně, bez časovače; testy, ruční kontrola → přesunuto
  do T-125 a T-127).
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



Rozhodnuto uživatelem 2026-11-11 (otázky 2 a 3):
- 2: okno kamery ukáže **přibližný čas do dalšího pokusu bez časovače** („Další pokus asi za 1 min“), nastavený jen při
  změně stavu z `CameraStatus.NextAttemptAt`; v malém náhledu skrytý. **Bez tlačítka** „Zkusit znovu“.
- 3: **otevření nebo obnovení okna kamery během pauzy zkrátí čekání**: pokus o připojení hned a vynulovaná pauza.
  Detekce ani nahrávání pauzu samy nezkracují.

Navazující (ui-dev, 2026-11-11, neblokuje tento úkol), čeká na rozhodnutí uživatele:
4. **„Otestovat kameru“ během pauzy.** Test uložené kamery jde přes hub a čeká na snímek nejvýš 15 s; při „Bez signálu“
   během minutové pauzy proto skončí „Žádný obraz“, i když by nový pokus mohl uspět (při „Chybě kamery“ skončí hned
   s jejím důvodem). Rozhodnutí 3 jmenuje jen okno kamery, takže test pauzu nezkracuje. Má ji zkracovat i test?
## Poznámky k implementaci
- **Backoff:** `ReconnectingCameraFeed` počítá pokusy za sebou, které skončily dřív, než začaly streamovat. Prodleva =
  `ReconnectDelay` zdvojnásobená za každý další takový pokus, nejvýš `CameraFeedOptions.MaxReconnectDelay` (nová volba,
  výchozí 1 min, `AppServices` ji nastavuje výslovně). Delší `ReconnectDelay` než strop = bez backoffu. Vzorec sdílí s
  `PrinterMonitor` nový interní `Core/Monitoring/ExponentialBackoff` (chování `PrinterMonitor` beze změny).
- **„Úspěšné streamování“** = první paket nebo snímek pokusu (stejný okamžik, kdy se hlásí `Streaming`). Pokus po něm
  čeká zase 5 s. Nové spojení (nový běh hubu, jiná adresa) začíná bez čekání a s backoffem od začátku.
- **Stav nese čas dalšího pokusu:** `CameraStatus.NextAttemptAt` (okamžik podle `TimeProvider.GetUtcNow`, ne délka,
  protože hub pozdějšímu odběrateli předává aktuální stav). Nastaví se u každého `NoSignal` / `Error` z feedu; ostatní
  odběratelé (detekce, nahrávání, test kamery) čtou jen `State`, takže se jich změna nedotkne.
- **Zdroje:** čekání je dál jediné `Task.Delay(…, TimeProvider, stopToken)`, žádné dotazování; stream, enumerátor a
  spojení každého pokusu se zavřou před čekáním jako dřív. Zastavení během čekání skončí hned (test bez posunu času).
- **Testy:** `ReconnectingCameraFeedTests` (řada 5/10/20/40/60/60 s, všechny druhy selhání, reset po streamování,
  `ReconnectDelay` nad stropem, zrušení během minutového čekání, `NextAttemptAt`), `ExponentialBackoffTests`,
  `AppServicesTests` (strop 1 min).
- **Čas do dalšího pokusu v okně (otázka 2):** třetí řádek v překryvu stavu „Další pokus asi za 20 s“ (pod 1 min
  sekundy zaokrouhlené nahoru, od 1 min celé minuty, u stropu „asi za 1 min“; mezera před jednotkou nezlomitelná).
  `CameraViewModel` ho spočítá jednou při změně stavu z `NextAttemptAt` a `TimeProvider.GetUtcNow` a dál jen přeformátuje
  (jazyk, režim): žádný časovač, v klidu žádná práce. Čas, který už uplynul (stav předaný pozdě), řádek neukáže.
  Skrytý v malém náhledu, prázdný při `Connecting` / `Streaming`. Vlastní opakování okna po vnitřní chybě lease
  (`_retryDelay`, T-014) ukazuje svou prodlevu stejně. Nové texty `CameraNextAttemptSeconds` / `CameraNextAttemptMinutes`
  ve všech osmi `Strings.*.resx`. Bez tlačítka „Zkusit znovu“.
- **Otevření nebo obnovení okna zkrátí čekání (otázka 3):** nový `Core/Cameras/CameraReconnectSignal`. Feed před
  čekáním vytvoří `CancellationTokenSource` propojený se stop tokenem, zaregistruje ho v signálu a čeká v tom jediném
  `Task.Delay` s jeho tokenem; `Retry()` ho zruší přes `CancelAsync` (pod zámkem signálu, takže ho feed mezitím
  nezlikviduje; další pokus běží na thread poolu, nikdy na vlákně volajícího, který může držet zámek hubu). Zrušené
  čekání = pokus hned a `failedAttempts = 0`. Mimo čekání (připojování, streamování, nic nečte, zastavený feed)
  `Retry()` vrátí `false` a nic si nepamatuje, takže pozdější selhání čeká normálně. Zastavení během probuzení: filtr
  stop tokenu vyhrává, smyčka skončí. Hub drží jeden signál na tiskárnu (`PrinterStream.Reconnect`, spojení běží vždy
  jen jedno) a spustí ho v `Add` jen pro lease s `CameraFrameSubscriptionOptions.ShortensReconnectWait`, a to ještě před
  předáním stavu: novému oknu pak rovnou pošle `Connecting` místo staré chyby, takže nebliká. Příznak místo
  `DisplaySize` z T-184, protože okno se přihlásí i před rozvržením (bez velikosti) a velikost se mění i při přepnutí
  režimu. Okno ho nastaví jen ve `SetActive(true)` (otevření, obnovení z minimalizace, i „Použít“ u minimalizovaného
  okna); přepnutí režimu viditelného okna lease nemění, vlastní opakování po chybě lease ani změna adresy příznak
  nemají. Detekce, nahrávání, timelapse a test kamery čekání nezkracují.
- **Testy (otázky 2 a 3):** `ReconnectingCameraFeedRetryTests` (probuzení během čekání → pokus hned a backoff od 5 s,
  během připojování / streamování nic a nezapamatuje se, zastavení během probuzení, probuzení po zastavení),
  `CameraStreamHubReconnectTests` (okno ukončí čekání detekce a ukáže hned `Connecting`; snímky, detekce ani pakety
  čekání nezkrátí; okno u streamujícího spojení nic nezmění; nové spojení bez čekání; zastavení hubu po probuzení),
  `CameraViewModelNextAttemptTests` (zaokrouhlení, text z `NextAttemptAt` s falešným `TimeProvider`, neodpočítává se,
  `Connecting` / `Streaming` ho smaže, malý náhled ho skryje, otevření a obnovení okna při čekání nahrávání zkrátí,
  přepnutí režimu ne), `CameraViewModelTests` (prodleva vlastního opakování), UiTests `CameraWindowNextAttemptUiTests`
  (ve všech 8 jazycích se „40 s“ i „1 min“ vejde do okna 960 × 540 až 320 × 180, v malém náhledu chybí; kontrola
  překryvu stavu sdílená s T-097 v `CameraHarness.StatusProblems`).
- **Dokumentace:** `docs/architecture.md` (Camera.FFmpeg / znovupřipojení), `manuals/*/manual.md` oddíl 5.4 ve všech
  jazycích (backoff; okno ukazuje přibližný čas dalšího pokusu, otevření nebo obnovení okna zkusí připojení hned).
  Žádný nový balíček.
- **Ověřeno na macOS (Apple Silicon):** `dotnet build` bez varování, `dotnet test` zelený, aplikace se spustí a běží
  (`dotnet run`). Okno se skutečně nedostupnou kamerou vizuálně neověřeno (bez obsluhy) → ruční kontrola přesunuta do
  T-125 (Windows) a T-127 (Mac).

## Review
- qa-reviewer 2026-11-11 (commit f279449c): bez blokujících nálezů. Opraveno: `BeginWait` signálu je ve feedu uvnitř
  `try`, takže `EndWait` proběhne vždy a signál si nikdy neponechá už zlikvidovaný `CancellationTokenSource`. Doplněn
  test přeformátování času dalšího pokusu po změně jazyka (`CameraViewModelNextAttemptTests`).
