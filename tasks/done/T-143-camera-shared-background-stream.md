---
id: T-143
title: Kamera – sdílené spojení se streamem na pozadí (okno, nahrávání, detekce)
status: done
owner: core-dev + ui-dev
priority: P2
platform: all
depends_on: [T-014, T-034]
---

## Cíl
Dnes patří spojení s kamerou oknu kamery: `CameraViewModel` si přes `ICameraFeedFactory` a `ReconnectingCameraFeed` otevře vlastní RTSP spojení a po zavření (i minimalizaci) okna ho zavře (T-014, „Životní cyklus“). Nahrávání (T-144 až T-148), detekce chyb tisku (T-153) i automatické zavírání okna (T-151) ale potřebují stream **i bez otevřeného okna** a nemají otevírat druhé, třetí spojení na tutéž kameru.

Úkol zavede jedno sdílené spojení na tiskárnu s více odběrateli. Je to společný základ pro nápady 1 (nahrávání) a 3 (detekce chyb), pro nápad 2 (automatické okno) je volitelný.

## Rozsah
- **Core (`Core/Cameras`), core-dev:**
  - Nová služba, pracovně `CameraStreamHub` (název volí core-dev): pro každou tiskárnu se zapnutou kamerou nejvýš jedno spojení (`ReconnectingCameraFeed`), sdílené odběrateli.
  - Odběr přes „lease“ (`IAsyncDisposable`): spojení se otevře s prvním odběratelem a zavře po posledním (případně po krátké prodlevě, viz otázky). Každý odběratel hlásí, co potřebuje:
    - **dekódované snímky** (okno kamery, malý náhled, detekce),
    - **zakódované pakety H.264** bez dekódování (nahrávání, T-144) – dekódování se pak spouští jen tehdy, když ho někdo potřebuje (nižší CPU při nahrávání bez okna).
  - Pomalý odběratel nesmí brzdit ostatní ani dekodér: každý odběratel dostane vlastní omezenou frontu / „poslední snímek vyhrává“ (detekci stačí ~1 snímek za 1–2 s, oknu každý snímek). Půjčený buffer `CameraFrame` se dál nesmí držet přes posun enumerátoru – sdílení řeší kopie nebo výměna bufferů (po vzoru `App/Cameras/CameraFrameExchange`), ne držení cizího bufferu.
  - Rozšíření `ICameraFeed` / `ICameraFeedFactory` o přístup k paketům (rozhraní pro pakety navrhne core-dev; Core nesmí znát typy FFmpeg – paket = bajty + pts/dts + příznak klíčového snímku + časová základna, parametry kodeku = extradata SPS/PPS).
  - Stav spojení (`CameraStatus`) dostanou všichni odběratelé.
  - Změna adresy kamery, vypnutí kamery nebo odebrání tiskárny v nastavení spojení restartuje / ukončí pro všechny odběratele.
- **Camera.FFmpeg, core-dev:** `FFmpegDecoder` umí předat přečtené pakety (`av_read_frame`) odběrateli paketů ještě před dekódováním; dekóduje jen tehdy, když existuje odběratel snímků. Ustálený stav dál bez alokací na snímek (test z T-014).
- **App, ui-dev:** okno kamery a malý náhled odebírají stream přes hub místo vlastního spojení. Chování okna se nemění: minimalizované okno odběr snímků uvolní, zavření okna uvolní lease. Náhled na záložce Kamera v nastavení (T-034) a „Otestovat kameru“ zůstávají podle otázky 3.
- Registrace v DI (explicitní), uvolnění hubu při ukončení aplikace (`TrayHost`) s časovým limitem jako u ostatních služeb.

## Akceptační kritéria
- [x] Dva a více odběratelů téže tiskárny sdílí jedno RTSP spojení (test proti kameře simulátoru: simulátor vidí jednoho klienta).
- [x] Spojení se otevře s prvním odběratelem a zavře po posledním; po zavření nezůstane žádné visící spojení ani vlákno dekodéru (test).
- [x] Odběratel jen paketů nespustí dekódování (test: dekodér se neotevře / počítadlo dekódovaných snímků = 0).
- [x] Pomalý odběratel nezpomalí ostatní (test s falešným odběratelem, který blokuje).
- [x] Změna adresy, vypnutí kamery a odebrání tiskárny restartují / ukončí spojení pro všechny odběratele (test).
- [x] Okno kamery, malý náhled a překryv fungují jako dřív; stávající testy (unit, headless UI, E2E okna kamery) prochází beze změny chování. → přesunuto do T-125 a T-127
- [x] Ustálený stav streamu bez alokací na snímek (stávající test z T-014 prochází). → přesunuto do T-125
- [x] Core dál nereferencuje FFmpeg ani Avalonii; `docs/architecture.md` (Pravidla vrstev, Camera.FFmpeg) popisuje hub.
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. **Kolik souběžných RTSP spojení kamera Buddy3D snese?** Pokud jen jedno, je hub nutný; pokud víc, jde o optimalizaci. Ověřit proti skutečné kameře uživatele (simulátor dává každému klientovi vlastní relaci, to nic nedokazuje). Neblokuje implementaci, ovlivní prioritu.
2. **Prodleva zavření spojení po posledním odběrateli:** hned (jako dnes po zavření okna), nebo např. 5–10 s, aby rychlé zavření a otevření okna nečekalo znovu ~2 s na klíčový snímek?
3. **Náhled v nastavení (T-034) a „Otestovat kameru“:** mají jít přes hub (sdílet spojení s otevřeným oknem), nebo zůstat samostatné (testují neuloženou adresu z formuláře, takže samostatné spojení dává smysl)? Doporučení architekta: test neuložené adresy samostatně, náhled uložené kamery přes hub – rozhodne uživatel.
4. **Minimalizované okno + běžící nahrávání:** dnes minimalizace stream zastaví. S hubem minimalizované okno jen přestane odebírat snímky; spojení drží jiný odběratel. Souhlasí uživatel, že se tím chování okna samotného nemění?

### Rozhodnutí uživatele 2026-11-03
1. Měření 2026-11-03 na kameře uživatele (Buddy3D na Core One, 1280×720): **tři souběžná RTSP spojení** fungují, každé ~20 fps, první snímek za 0,5–2 s. Hub je tedy optimalizace, ne nutnost (priorita zůstává, protože ho potřebuje nahrávání a detekce).
2. Prodleva zavření spojení po posledním odběrateli: **10 s**.
3. Test neuložené adresy („Otestovat kameru“) **samostatným spojením**, náhled uložené kamery v nastavení **přes hub** (doporučení architekta).
4. Minimalizované okno jen přestane odebírat snímky; spojení drží ostatní odběratelé – **souhlas**, chování okna samotného se nemění.

## Poznámky k implementaci
Implementováno 2026-11-03 na macOS (core-dev + ui-dev).

**Veřejné API (Core, `LayerPeek.Core.Cameras`)**
- `ICameraFeed.ReadAsync(CameraStreamDemand, CancellationToken)` → `IAsyncEnumerable<CameraStreamItem>`: po připojení
  jeden `StreamStarted` (`CameraStreamInfo`: kodek H.264, šířka/výška, je-li známá, `CameraTimeBase`, extradata SPS/PPS
  jako vlastní kopie), pak pakety (`CameraPacket`: bajty, pts/dts, `long?`, délka, klíčový snímek, časová základna) a
  snímky (`CameraFrame`). `CameraStreamDemand` (`Frames`, `Packets`) se smí měnit za běhu. Výchozí implementace rozhraní
  (pro falešné feedy bez paketů) vrací jen snímky z `ReadFramesAsync`. Core nezná žádný typ FFmpeg.
- `ReconnectingCameraFeed.ReadAsync(uri, demand, status, ct)`: totéž se znovupřipojením; „Streaming“ až první paket nebo
  snímek (popis streamu se nepočítá). `ReadFramesAsync` je beze změny chování (tenká vrstva nad ním).
- `CameraStreamHub` (singleton, `IAsyncDisposable` + `IDisposable`): `Configure(printers)`, `IsConfigured(id, uri)`,
  `SubscribeFrames(id, uri?, CameraFrameSubscriptionOptions?)`, `SubscribePackets(id, uri?, CameraPacketSubscriptionOptions?)`,
  `Linger` (výchozí 10 s, rozhodnutí 2), `OpenConnections`.
- Lease `CameraStreamSubscription` (`Dispose`/`DisposeAsync`, `Status`, `Ended` (Task s `CameraStreamEndReason`),
  `EndReason`, `Failure`): `CameraFrameSubscription.TakeLatest()` (vlastní buffer `CameraFrameBuffer`, platí do dalšího
  volání, `ToImage()`), `DroppedFrames`; volby `FrameAvailable`, `Status`, `Ended`, `MinFrameInterval`.
  `CameraPacketSubscription.ReadAsync(ct)` (popisy a pakety v pořadí, data platí do dalšího kroku); volby `QueueCapacity`
  (výchozí 512 ≈ 25 s), `Status`, `Ended`. Důvody konce: `Disposed`, `CameraUnavailable`, `HubStopped`, `Failed`, `Overflow`.
- `CameraFrameExchange` / `CameraFrameBuffer` se přesunuly z App do Core (veřejné), testy do Core.Tests.

**Chování hubu**
- Jedno spojení na tiskárnu; otevře se s prvním odběrem, zavře `Linger` po posledním. Stejně dlouho po odchodu
  posledního odběratele snímků se ještě dekóduje (rychlé obnovení minimalizovaného okna nečeká na klíčový snímek);
  pakety se vypnou hned. Bez odběratele hub nedrží žádné vlákno ani časovač (časovač prodlevy se po zavření uvolní).
- Dekóduje se jen se zájmem o snímky: `FFmpegDecoder` otevře dekodér H.264 až při prvním zájmu, po vypnutí ho vyprázdní
  (`avcodec_flush_buffers`) a po novém zapnutí začne od dalšího klíčového snímku (`AV_PKT_FLAG_KEY`). Paket jde odběratelům
  ještě před dekódováním (kopie do znovupoužívaného bufferu dekodéru).
- Rozvod snímků: každý odběratel snímků má vlastní trojitý buffer (jedna kopie snímku na odběratele, nutná, protože buffer
  dekodéru je jen zapůjčený; `MinFrameInterval` kopii u řídkých odběratelů, např. detekce, přeskočí). Odběratel paketů má
  vlastní omezenou frontu (kruh) a malý vlastní pool bufferů (16), čtenáře probouzí přes thread pool, nikdy ne na vlákně
  streamu. Pomalý odběratel snímků jen zahazuje snímky, pomalý čtenář paketů po přetečení skončí s `Overflow`, ostatní
  běží dál. Výjimka z `FrameAvailable` ukončí jen ten odběr (`Failed`); výjimky stavových pozorovatelů se zalogují.
- Ustálený stav bez alokací: ověřeno testy `CameraStreamHubAllocationTests` (snímky dvěma odběratelům, pakety čtenáři)
  a `FFmpegCameraFeedAllocationTests` (pakety + snímky z FFmpeg, snímky přes hub, stávající testy T-014).
- Stav: všichni odběratelé dostanou stav spojení; odběratel snímků vidí „Připojování“ až do svého prvního snímku (okno
  se tak chová jako dřív i při připojení k běžícímu spojení).
- `Configure` reaguje jen na změny proti minulému volání: nová adresa restartuje spojení pro všechny, vypnutá kamera /
  odebraná tiskárna ukončí všechny odběry (`CameraUnavailable`) a spojení hned (bez prodlevy). Odběr může uvést adresu,
  kterou čeká; když se liší, spojení se pro všechny přepne na ni (okno otevřené přes „Použít“ dřív, než nový běh
  monitorování doručí uložené nastavení, T-040). Snímky a stavy starého spojení se po restartu nedoručí (generace).

**App**
- `CameraViewModel` místo vlastního spojení drží lease (`SubscribeFrames(printerId, adresa okna)`); minimalizace, zavření
  a změna adresy lease uvolní. Lease, který hub ukončí chybou (např. vyhozené `Post` při ukončování UI), se zaloguje
  stejně jako dřív a po prodlevě znovu vezme; konec kvůli vypnuté kameře nebo zastavenému hubu se neopakuje.
  `StreamCompletion` = konec aktuálního lease. Malý náhled je režim téhož okna, takže jde také přes hub.
- `CameraStreamHubObserver` (`IMonitoringObserver`, v seznamu před `MonitoredPrinters`) volá `Configure`.
- „Otestovat kameru“ (rozhodnutí 3): `ICameraTester.TestAsync(uri, printerId, ct)`; když je adresa uloženou zapnutou
  kamerou tiskárny (`hub.IsConfigured`), vezme se první snímek po přihlášení z hubu (sdílí spojení otevřeného okna) a první
  chybový stav je selhání; jinak (neuložená adresa) vlastní spojení jako dřív. Timeout 15 s a hláška o místní síti beze změny.
- Registrace v DI explicitní (`AppServices.AddCameras`); `ServiceHost.StopAsync` hub uvolní s časovým limitem souběžně se
  zastavením monitoringu a proxy, kontejner ho pak už jen dovře.
- Testy okna používají hub bez prodlevy (`CameraHubs.Immediate`), takže stávající testy životního cyklu (minimalizace
  zavře spojení…) platí beze změny; prodleva 10 s je otestovaná v Core.

**Jak se napojí T-144 / T-145 / T-153**
- T-144: `hub.SubscribePackets(printerId)` + `ReadAsync`. `StreamStarted` = začátek spojení (po výpadku přijde nový →
  podle rozhodnutí T-144 nový soubor `-2`, `-3`…), `CameraStreamInfo.Extradata` = SPS/PPS z SDP pro `codecpar`,
  `TimeBase` pro přepočet razítek; zahodit pakety do prvního `IsKeyFrame`. Zápis běží na vlákně čtenáře, ne dekodéru;
  přetečení fronty = `EndReason == Overflow` → nahrávka skončí s chybou, živý obraz běží dál. Nahrávání bez okna nic
  nedekóduje. Druhé spojení není potřeba. Pozor: u RTSP bez `avformat_find_stream_info` může být `Width`/`Height` 0
  (rozměr je pak v SPS).
- T-153: `hub.SubscribeFrames(printerId, options: new() { MinFrameInterval = 1–2 s, FrameAvailable = … })` a `TakeLatest`
  na vlastním vlákně; zmenšení si dělá detekce sama.

**Testy**: `CameraStreamHubTests` (26), `CameraStreamHubAllocationTests` (2), `ReconnectingCameraFeedItemsTests` (4),
`CameraFrameExchangeTests` (přesunuté), `FFmpegCameraFeedPacketTests` (6, soubor fixture a simulátor: jedno RTSP spojení
pro okno + detekci + nahrávání, po uvolnění žádná relace; samotné pakety → dekodér neotevřen), 2 nové alokační testy FFmpeg,
`CameraSnapshotTesterTests` (+6, cesta přes hub), `CameraViewModelTests` (+3: minimalizace při držení spojení jiným
odběratelem, dvě okna na jedno spojení, vypnutá kamera bez opakování), `AppServicesTests` (+1).

**Ověřeno (macOS arm64, 2026-11-03)**: `dotnet build` 0 varování, `dotnet test` 8687 testů, 0 selhání, 200 přeskočených
(Windows-only); `dotnet publish src/LayerPeek.App -c Release -r osx-arm64` bez varování IL (AOT / trim). Žádný nový balíček. E2E okna kamery (Windows) a ruční
kontrola se skutečnou kamerou přesunuty: Windows → T-125 (oddíl 2c), Mac → T-127 (oddíl 2).

Uzavřeno 2026-11-03: zbývající ruční ověření přesunuto do T-125 a T-127 (rozhodnutí uživatele).

## Review
Review 2026-11-03 (qa-reviewer, macOS), opraveno přímo:
- Spojení, které po odchodu posledního odběratele paketů čekalo `Linger`, nemělo žádnou poptávku (ani snímky, ani pakety),
  feed tedy nic nevracel a hlídač `ReconnectingCameraFeed` (`FrameTimeout` 10 s ≤ prodleva 10 s) ho vyhodnotil jako
  zaseknuté a připojoval znovu. Běžící spojení teď vždy čte aspoň pakety (`CameraStreamHub.KeepReading`), test
  `LingeringPacketOnlyConnectionKeepsReadingAndIsNotTakenForStalled`.
- `Pending.Run` mohl volat `Cancel` na token source, který smyčka mezitím uvolnila (smyčka skončí sama při změně generace) →
  `ObjectDisposedException` z `Configure` / `Dispose` lease; zachyceno.
- `Configure` u vypnuté kamery už nemaže záznam tiskárny: nové spojení po opětovném zapnutí počká na zavření starého a
  `DisposeAsync` hubu na něj také čeká.
- `CameraPacketSubscription.ReadAsync` kontroluje zrušení i při plné frontě; test `CancelledPacketReaderStopsWhileItemsAreStillQueued`.
