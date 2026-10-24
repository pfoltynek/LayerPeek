---
id: T-079
title: Vychladnutí po tisku – detekce v Core (práh trysky, průběh, odhad času)
status: done
owner: core-dev
priority: P2
platform: all
depends_on: []
---

## Cíl
Core logika, která po dokončení nebo zastavení tisku sleduje chladnutí trysky. Pro každou tiskárnu poskytuje stav **Chladne…** (teploty, průběh k prahu, odhad času) a **Vychladlo**. Je podkladem pro widget Vychladnutí (T-088) a volitelnou notifikaci (T-080). Je bez UI a testovatelná s falešnými hodinami.

## Rozhodnutí (uživatel, 2026-10-24)
- Rozhoduje **jen tryska**. Podložka se jen zobrazuje. Notifikace „vychladlo, můžeš vypnout“ je volitelná (T-080).
- **Kdy začne (Q1):** po **dokončeném** (`Finished`) **i zastaveném** (`Stopped`) tisku. Po chybě (`Error`) ne.
- **Kdy skončí (Q2):** stav (i „Vychladlo“) skončí, když:
  - **začne další tisk**,
  - tiskárna přejde do **offline**,
  - se **tryska znovu ohřívá**: cílová teplota trysky > 0, tedy topení zapnuté, např. předehřev.

  Pak stav zmizí bez události. Bez těchto podmínek „Vychladlo“ trvá.
- **Práh (Q3, Q4):** **u každé tiskárny**, rozsah **30–100 °C** po 1 °C, výchozí **50 °C**. K tomu akce „Použít práh u všech tiskáren“ (čistá operace v Core, UI v T-080).
- **Tisk skončil už chladný (Q5):** tryska je už na prahu nebo pod ním. Stav jde rovnou do **„Vychladlo“**, ale **bez události**, takže bez notifikace.

## Rozhodnutí (uživatel, 2026-10-24, po dokončení)
Schválená doporučení po review hotové implementace:
- **Rozhodnutí 7 – offline:** tolerance výpadku **1 minuta** (`CooldownTrackerOptions.OfflineGrace`) zůstává.
- **Rozhodnutí 8 – potvrzení:** „Vychladlo“ dál potvrzují **dvě po sobě jdoucí platná měření** na prahu nebo pod ním.
- **Rozhodnutí 9 – změna prahu:** chování zůstává: zvýšený práh, který poslední dvě měření splňují, vyvolá „Vychladlo“ s událostí; snížený práh „Vychladlo“ nezruší.
- **Rozhodnutí 11 – tiskárna vychladlá už při startu:** když konec tisku nebyl vidět proto, že **první snapshot** tiskárny (start aplikace, přidání tiskárny, nebo jen nečitelné snapshoty předtím) už ukazuje skončený tisk, chladnutí se **nesleduje vůbec** (fáze zůstane `None`, widget se neukáže, žádná událost).
  - Naopak tisk, který byl vidět tisknout a skončil **během výpadku**, se sleduje dál s `PrintEndedAt = null` (UI vynechá řádek časů, rozhodnutí 10 v T-088).
- **Rozhodnutí 12 – výsledek tisku v události:** `CooldownEvent` nese výsledek tisku (`CooldownPrintOutcome` `Finished` / `Stopped`), aby notifikace T-080 mohla napsat „tisk dokončen“ / „tisk zastaven“.
- Rozhodnutí 11 a 12 jsou implementovaná ve větvi `feature/widget-core-followups` (viz Poznámky k implementaci, oddíl „Doplněk po dokončení“).

## Rozsah
- `Core/Cooldown/`:
  - `CooldownTracker` – čistý stavový automat po vzoru `Core/Events/PrintEventDetector` (bez I/O, pod zámkem volajícího).
    - `Process(PrinterSnapshot)` vrátí stav tiskárny `None` / `Cooling` / `CooledDown` a případně událost přechodu `Cooling` → `CooledDown` (nejvýš jednou na úlohu).
    - Začátek a konec podle Rozhodnutí.
  - `CooldownState` (record struct):
    - fáze,
    - teplota trysky a podložky,
    - práh,
    - průběh 0–1 (od teploty trysky na začátku chladnutí k prahu),
    - odhad zbývajícího času (`TimeSpan?`),
    - čas začátku.
  - Odhad času se počítá z posledních vzorků teploty trysky (časy ze snapshotů).
    - Doporučený je model ochlazování 1. řádu (Newton) s odhadem okolní teploty.
    - Nesmyslný odhad se nevrací: když teplota neklesá nebo je méně než N vzorků, odhad je `null`.
    - Přesný algoritmus volí core-dev a popíše ho v poznámkách.
    - Testy s křivkou ze simulátoru (`tools/PrusaSimulator`, `PrinterSimulation`: chladnutí trysky τ = 60 s, podložky τ = 300 s) i se zašuměnými daty.
  - `CooldownMonitor` (služba, po vzoru `PrintEventNotifier`):
    - odebírá `IPrinterMonitor.SnapshotUpdated` a drží tracker pro každou tiskárnu,
    - `Configure(printers, settings)` při každém běhu; stav tiskáren se stejným strojem zachová (jako `PrintEventNotifier.IsSameMachine`),
    - práh se bere z každé tiskárny; změna prahu za běhu se projeví hned,
    - `GetState(printerId)` a událost `Changed(printerId)`,
    - čas „teď“ z `TimeProvider` (falešné hodiny v testech).
- Nastavení prahu u tiskárny (`PrinterDefinition`, např. `CooldownThresholdCelsius`, výchozí 50, 30–100):
  - uloží se do `printers[*]` v `settings.json` (`version` 2 z T-077; pole je nepovinné, výchozí hodnota se nezapisuje),
  - neplatná hodnota → výchozí s varováním,
  - čistá operace „použít práh u všech tiskáren“.
  - UI prahu je v T-080.
- Registrace v composition rootu App (explicitní) a předání nastavení přes `IMonitoringObserver`. Jen napojení, bez UI.
- Testy v `LayerPeek.Core.Tests` s `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`, pokud už je v testech; jinak vlastní falešné hodiny).

## Akceptační kritéria
- [x] Tracker:
  - dokončení i zastavení tisku spustí chladnutí, chyba ne;
  - průběh a odhad se mění s teplotou;
  - pokles trysky na práh nebo pod něj → `CooledDown` a právě jedna událost;
  - nový tisk, offline a znovu zapnuté topení trysky stav ukončí bez události;
  - tisk skončený pod prahem → rovnou `CooledDown` bez události;
  - chybějící teplota nevyvolá výjimku ani falešný přechod.
- [x] Odhad času:
  - konverguje na simulované křivce (chyba v testu < 20 % v druhé polovině chladnutí);
  - při nedostatku dat, rostoucí teplotě a šumu vrací `null`;
  - nikdy není záporný ani nekonečný.
- [x] `CooldownMonitor`:
  - konfigurace běhu, zachování stavu při přejmenování tiskárny, zapomenutí při změně adresy,
  - práh u každé tiskárny zvlášť a jeho změna za běhu,
  - thread-safety (souběžné snapshoty různých tiskáren),
  - uvolnění.
- [x] Nastavení prahu: výchozí 50, hranice 30 a 100, round-trip, neplatné hodnoty, starý soubor, „použít u všech tiskáren“
- [x] Core bez Avalonie, AOT kompatibilní
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné – Q1–Q5 rozhodnuté 2026-10-24, viz Rozhodnutí)

## Poznámky k implementaci
- **Core (`Core/Cooldown/`):**
  - `CooldownTracker` – automat jedné tiskárny (monitor drží jeden na tiskárnu). `Process(snapshot)` a `SetThreshold(°C)` vrací `CooldownUpdate(State, Event?)`. Po vytvoření nealokuje (ověřeno testem alokací).
  - `CooldownState` (record struct): `Phase`, `NozzleCelsius`, `BedCelsius`, `ThresholdCelsius`, `Progress`, `EstimatedRemaining`, `StartedAt`, `UpdatedAt`, `Outcome`, `PrintEndedAt`, `CooledDownAt`. Ve fázi `None` jsou všechny hodnoty výchozí (`CooldownState.None`).
    - Doplněno při review pro text návrhu T-076 „tisk dokončen v 19:52 · vychladlo za 14 min“:
      - `Outcome` (`CooldownPrintOutcome`: `Finished` / `Stopped`) říká, jak tisk skončil.
      - `PrintEndedAt` je čas prvního snapshotu, který hlásí konec tisku (nejvýš jeden interval pollingu po skutečném konci). Je `null`, když tisk skončil, zatímco tiskárna nebyla čitelná (offline, neautorizovaná), nebo v mezeře bez snapshotů delší než `CooldownTracker.GapLimit` (spánek počítače). Hodiny tak nikdy neukážou čas startu aplikace jako konec tisku. (Původně `null` i pro tisk skončený už při prvním snapshotu; ten se po rozhodnutí 11 nesleduje vůbec.)
      - `StartedAt` je začátek chladnutí: první snapshot po konci tisku s vypnutým topením trysky. Obvykle se rovná `PrintEndedAt`, později jen když firmware vypne topení o poll později.
      - `CooledDownAt` je čas přechodu do „Vychladlo“, tedy snapshotu, který ho potvrdil (u události stejný jako `CooldownEvent.Timestamp`). Při chladnutí je `null`.
      - „Vychladlo za“ = `CooledDownAt − PrintEndedAt`. Co widget ukáže, když je `PrintEndedAt` `null`, rozhodl uživatel 2026-10-24 (rozhodnutí 10 v T-088: jen „Vychladlo – můžete vypnout“ s teplotami, bez řádku časů).
  - `CooldownEvent(PrinterId, Timestamp, FileName)` – podklad pro T-080.
  - `CooldownMonitor` (`Configure(printers, pollingInterval)`, `GetState(id)`, `Changed(id)`, `CooledDown`, `Dispose`). Jeden zámek jako `PrintEventNotifier`, události mimo zámek. Odběratelé se volají po jednom bez alokace (`Delegate.EnumerateInvocationList`) a jejich výjimky se jen zalogují. `Configure` nepotřebuje `NotificationSettings`: práh je v `PrinterDefinition`.
  - `GetState` odpočítává odhad podle `TimeProvider` od času snapshotu (nejvýš na nulu, hodiny pozadu odhad neprodlouží).
- **Pravidla nad rámec Rozhodnutí (implementační detaily, ne produktová volba):**
  - Chladnutí se sleduje od prvního snapshotu s **vypnutým topením** trysky (cíl 0 nebo neznámý). Když firmware vypne topení o poll později, nebere se to jako „znovu ohřívá“. „Znovu ohřívá“ = cíl > 0 během chladnutí nebo po vychladnutí. Tisk ukončený opětovným ohřevem nebo offline se už znovu nesleduje (každý tisk nejvýš jedno chladnutí).
  - **Offline** ukončí stav, až tiskárna zůstane nedostupná 1 minutu (`CooldownTrackerOptions.OfflineGrace`, stejně jako zpoždění notifikace „offline“ T-009). Jeden vynechaný poll (slabá Wi-Fi) tak nezahodí chladnutí ani notifikaci. Během té minuty se stav nemění.
  - „Vychladlo“ potvrdí **dvě po sobě jdoucí platná měření** na prahu nebo pod ním (filtr jednorázového výkyvu čidla). U tisku skončeného pod prahem se tedy první poll nic neukáže a druhý rovnou ukáže „Vychladlo“ bez události (o jeden poll později, nikdy „Chladne…“).
  - Neplatná měření (chybějící, NaN, ±∞, < 0 °C, > 500 °C) se přeskočí. Nevyvolají výjimku ani přechod. Teploty ve stavu zůstávají na posledních platných hodnotách.
  - Chyba (`Error`) během tisku nebo před `Stopped` tisk vyřadí. ~~Tiskárna, která už při prvním snapshotu ukazuje `Finished`/`Stopped` (start aplikace), začne chladnutí sledovat.~~ Změněno rozhodnutím 11: takovou tiskárnu tracker nesleduje (viz Doplněk po dokončení). `Idle` ani `Busy` chladnutí neukončí (potvrzení tisku na displeji).
  - Změna prahu se projeví hned. Zvýšený práh, který poslední dvě měření splňují, vyvolá „Vychladlo“ s událostí (tryska byla nad prahem). Snížený práh „Vychladlo“ nezruší (Rozhodnutí: „Vychladlo trvá“). Průběh se počítá od nejvyšší teploty trysky od začátku chladnutí.
- **Odhad času (`CooldownEstimator`, internal):**
  - Model `T(t) = A + C·e^(−t/τ)`. Pro pevné `k = 1/τ` je model lineární v `A` a `C`, ty se spočítají uzavřeně (nejmenší čtverce se slabým apriorním odhadem okolí 25 °C, váha 0,01 měření). Přes `ln k` (τ 5 s – 4 h) se minimalizuje součet čtverců hrubou mřížkou a zlatým řezem (variable projection). Nerovnoměrné rozestupy a mezery v datech zvládá přesně.
  - Bere posledních až 48 měření s rozestupem aspoň 2 s v pevných polích (omezená paměť, bez alokací). Když jdou hodiny zpět, začne znovu.
  - `null`, když:
    - je méně než 6 měření nebo méně než 20 s dat,
    - teplota neklesla aspoň o 2 °C (průměr první a poslední čtvrtiny),
    - je τ na kraji rozsahu,
    - je RMS reziduí větší než 20 % proloženého poklesu (šum),
    - je proložená okolní teplota nad prahem (nikdy prahu nedosáhne),
    - nebo je odhad delší než 12 h.

    Jinak je odhad ≥ 0.
  - Testy:
    - na křivce simulátoru (τ = 60 s, 23 °C) pro prahy 30–100, s jeho výkyvem ±0,2 °C, se šumem ±0,5 °C a s pollingem 1/5/10 s je chyba v druhé polovině chladnutí < 20 % (u posledního pollu do poloviny intervalu pollingu),
    - pomalejší tryska v teplé místnosti (τ = 240 s, 35 °C),
    - mezery v datech,
    - stabilita mezi polly,
    - náhodná data (nikdy záporný ani nad 12 h).
- **Nastavení:** `PrinterDefinition.CooldownThresholdCelsius` (výchozí 50, 30–100, jinak výjimka), konstanty a čistá operace `CooldownThreshold.ApplyToAll` a `AppSettings.WithCooldownThresholdForAllPrinters`.
  - V souboru je to `printers[*].cooldownThresholdCelsius`, celé číslo. Výchozí hodnota se nezapisuje, chybějící hodnota (starý soubor) dává 50.
  - Zlomek, hodnota mimo rozsah nebo obří číslo dají 50 s varováním. Varování hodnotu neopisuje, uvede jen rozsah.
  - **T-077 ještě není hotový**, proto zůstává `version` 1. Pole je nepovinné, takže s verzí 2 z T-077 nekoliduje a T-077 ho jen převezme.
- **App:**
  - `App/Cooldown/CooldownObserver` předává tiskárny při `ApplySettings` i `UpdateSettings`. `CooldownMonitor` a observer jsou explicitně registrované v `AppServices`.
  - `MonitoringSession.KeepsMonitoring` bere práh jako údaj, který se nepolluje: změna jen prahu nerestartuje monitoring a monitor ji dostane přes `UpdateSettings`.
  - `PrinterEditorViewModel` uložený práh při uložení z okna nastavení zachová (pojistka proti ztrátě dat, dokud pole nepřidá T-080).
- **Testy:**
  - Core: `Cooldown/CooldownTrackerTests`, `CooldownEstimatorTests`, `CooldownMonitorTests`, `CooldownThresholdTests`, `CooldownAllocationTests` a `Settings/SettingsMapperCooldownThresholdTests`, s `FakeTimeProvider` a bez souborů mimo `TestDirectories`.
  - App: `Cooldown/CooldownObserverTests`, `Settings/PrinterCooldownThresholdSettingsTests`, `AppServicesTests` a `MonitoringSessionModelTests.CooldownThresholdChangeDoesNotRestart`.
- **Závislosti:** žádné nové balíčky. Vše je kompatibilní s AOT (bez reflexe, JSON přes stávající `SettingsJsonContext`).
- **Doplněk po dokončení (rozhodnutí 11 a 12, větev `feature/widget-core-followups`):**
  - **Kdy tisk zahájí chladnutí:** snapshot `Finished` / `Stopped` s tiskem, který ještě neskončil, zahájí chladnutí jen když
    - tracker viděl **tento tisk** (stejnou úlohu) ve stavu `Printing` / `Paused` – i když mezitím byl výpadek (pak `PrintEndedAt = null`), nebo
    - **předchozí snapshot byl čitelný**, takže konec byl vidět nejvýš o jeden poll později (celý tisk proběhl mezi dvěma polly, `PrintEndedAt` = čas snapshotu).
  - Jinak se tisk jen zapamatuje jako skončený a chladnutí nezačne: první snapshot tiskárny (start aplikace, přidání tiskárny, nová adresa) nebo snapshoty jen nečitelné předtím, a také **jiná úloha**, která celá proběhla i skončila během výpadku. Ta zároveň ukončí případné chladnutí předchozího tisku (tiskárna se mezitím používala).
  - **Mezera mezi snapshoty = nepozorováno (robustnost k rozhodnutí 11, druhé kolo review):** když mezi dvěma po sobě jdoucími snapshoty uplyne víc než `CooldownTracker.GapLimit` a monitor mezitím nedodal ani snapshot `Offline` (počítač spal, monitoring stál), počítá se mezera stejně jako výpadek delší než tolerance offline:
    - tisk stejné úlohy, který v mezeře skončil, se sleduje s `PrintEndedAt = null` (UI vynechá řádek časů, rozhodnutí 10),
    - jiná úloha, která v mezeře proběhla i skončila, se nesleduje,
    - probíhající chladnutí (i čekání na vypnutí topení a stav „vychladlo“) skončí bez události a znovu nezačne – stejně jako po offline delším než `OfflineGrace`, kdy se týž skončený tisk po návratu už nesleduje.
    - `GapLimit` = větší z `OfflineGrace` (1 min) a hranice mezery historie teplot `TelemetryHistory.GapThreshold(polling)` (3 intervaly pollingu, aspoň 30 s). Samotná `OfflineGrace` by nestačila: při pollingu 1–5 min (povolený rozsah nastavení) by každý běžný poll vypadal jako mezera. Při výchozím pollingu 5 s je limit 1 min, při 5 min pollingu 15 min.
    - Interval pollingu předává `CooldownMonitor.Configure(printers, pollingInterval)` (stejně jako `TelemetryHistory.Configure`), z App `CooldownObserver` ze `settings.PollingInterval`; změna intervalu platí i pro ponechané tiskárny. Hodiny posunuté zpět mezeru nedělají.
    - Testy: `CooldownTrackerTests` – `PrintEndIsUnknownWhenItEndedDuringAGapWithoutSnapshots`, `GapUpToTheLimitIsObserved`, `OtherJobThatRanAndEndedDuringAGapStartsNoCoolDown` (předtím tisk i nečinnost), `GapLongerThanTheLimitEndsCoolingWithoutEventAndItDoesNotResume`, `GapUpToTheLimitKeepsCooling`, `GapEndsACoolDownWaitingForTheHeaterToTurnOff`, `GapEndsCooledDown`, `GapBeforeAnOfflineSnapshotEndsCooling`, `ClockGoingBackIsNoGap`, `GapLimitFollowsThePollingInterval`, `GapLimitIsAtLeastTheTelemetryGapWithoutOfflineGrace`, `PollingIntervalMustBePositive`; `CooldownMonitorTests.PollingIntervalSetsHowLongAGapWithoutSnapshotsMayBe`, `ChangedPollingIntervalAppliesToKeptPrinters`; `CooldownObserverTests.AppliedSettingsPassThePollingInterval`. Testy `UninformativeSnapshotsChangeNothing` a `CooledDownAtIsTheTimeOfTheCooledDownTransition` posílají snapshoty po 5 s místo jednoho skoku o 5 min (smysl testu beze změny, skok by teď byl mezera).
  - `CooldownEvent(PrinterId, Timestamp, FileName, Outcome)`: nový poslední parametr `Outcome` (`CooldownPrintOutcome`), stejná hodnota jako `CooldownState.Outcome`. Událost je dál `readonly record struct`, takže bez alokace (test `CoolingDownWithTheEventAllocatesNothing` beze změny prošel).
  - Testy: `CooldownTrackerTests` – upravené `FirstSnapshotOfAFinishedPrintStartsCooling` → `PrintAlreadyEndedAtTheFirstSnapshotIsNotTracked` (Finished i Stopped, až do vychladnutí), odstraněný `PrintEndIsUnknownWhenTheEndWasNotSeen` (start aplikace), nové `PrintEndedBeforeTheFirstReadableSnapshotIsNotTracked` (Offline, Unauthorized, Unknown), `PrintAlreadyEndedAtStartupBelowTheThresholdIsNotTracked`, `NextPrintAfterAnUntrackedEndedPrintIsTracked`, `ReheatingAfterAnUntrackedEndedPrintDoesNotTrackIt`, `JobThatRanBetweenTwoPollsAfterAnUntrackedEndedPrintIsTracked`, `AnotherJobThatEndedDuringAnOutageIsNotTracked`, `AnotherJobThatEndedDuringAShortOutageEndsTheCoolDownWithoutANewOne`, `PausedPrintThatEndedDuringAnOutageIsTracked`, `EventCarriesHowThePrintEnded`, `EventFromARaisedThresholdCarriesHowThePrintEnded`; `CooldownMonitorTests.PrinterAlreadyShowingAnEndedPrintAtStartIsNotTracked` a výsledek tisku v události v `GetStateCarriesTheEndOfThePrintAndTheCooledDownTime`.
