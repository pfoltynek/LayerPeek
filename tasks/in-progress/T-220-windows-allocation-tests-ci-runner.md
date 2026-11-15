---
id: T-220
title: Alokační testy ASampleDoesNotAllocate a SteadyStateTimelapseDoesNotAllocate nestabilní na 4jádrovém runneru Windows
status: in-progress
owner: qa-reviewer
priority: P2
platform: windows
depends_on: []
---

> **Založeno podle rozhodnutí uživatele 2026-11-14** (T-218, otázka 12). Do opravy se oba testy **jen v CI na Windows**
> přeskočí s důvodem odkazujícím na tento úkol (`LAYERPEEK_CI=1` z `.github/workflows/ci.yml`,
> `tests/Shared/ContinuousIntegration.cs`). Lokálně běží dál.

## Cíl
Oba testy na runneru `windows-2025` (4 vCPU, ostatní testovací sestavy běží současně) selhávají, lokálně na Windows
vývojáře téměř vždy projdou (známá citlivost na zátěž: T-165, T-168). Zjistit příčinu a změřit tak, aby test byl
spolehlivý i na pomalém stroji a pořád zachytil skutečnou alokaci na vzorek / paket.

## Zjištěno v CI (T-218)
- `LayerPeek.Core.Tests.Detection.PrintFailureDetectionAllocationTests.ASampleDoesNotAllocate`: běh 43501907104 –
  minimum ze všech platných průchodů **173 840 B** (test trval 41 s); běh 43513903970 znovu selhal. Velikost thread poolu
  se během platných průchodů neměnila, takže jde o jiný zdroj (časovače na systémových hodinách? tiered JIT? jiné
  vlákno procesu?).
- `LayerPeek.Core.Tests.Cameras.Timelapse.CameraTimelapseAllocationTests.SteadyStateTimelapseDoesNotAllocate`:
  „Packets not read.“ – čtení paketů se za 10 s (`TestTimeouts.Wait`) nedokončilo (vlákno čtení hladovělo vedle
  aktivního čekání testu).
- Pod extrémní zátěží na Macu (load ~120, 2026-11-13) selhal stejně i
  `CameraStreamHubAllocationTests.GrayscaleKeyFrameSamplesDoNotAllocate` (120 B); bez zátěže prošel.

## Rozsah
- Na Windows (ideálně s omezeným CPU, např. `start /affinity 3` nebo souběh s ostatními sestavami jako v CI) reprodukovat
  a určit původce alokace (`dotnet-trace` / `GC.GetAllocatedBytesForCurrentThread` po vláknech, události
  `GCAllocationTick`).
- Opravit kód, nebo měření (např. alokace jen vláken, která se vzorku účastní; čekání bez aktivního točení), ne mez.
- Pak odstranit přeskočení v CI (`ContinuousIntegration.IsWindowsRunner` u obou testů) a ověřit zelený běh CI.

## Akceptační kritéria
- [x] Příčina obou selhání popsaná v poznámkách
- [ ] Oba testy spolehlivé v CI na Windows (aspoň 3 zelené běhy po sobě) bez přeskočení; záruka 0 B na vzorek / paket
      zůstává
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- ~~**Q1 (2026-11-15, qa-reviewer): jak oddělit alokační testy od provozu výsledků platformy?**~~ **Rozhodnuto
  uživatelem 2026-11-15: varianta A – samostatný krok CI.** (Zvažované varianty: A samostatný krok CI, B samostatné
  testovací projekty, C ponechat čekání a opakovat job; zdůvodnění v „Selhání v CI 2026-11-15“.) Implementace viz
  „Samostatný krok CI (rozhodnutí Q1)“.

## Poznámky k implementaci
Stav 2026-11-15 (qa-reviewer, worktree `LP-T-220`): příčiny nalezené a opravené v měření (ne v mezi, ne v kódu aplikace),
přeskočení v CI odstraněné. **Zbývá kritérium „3 zelené běhy CI po sobě“** – ověří se až po pushi (hlavní agent), proto
úkol zůstává v `in-progress`.

### Reprodukce
- Počítač vývojáře (16 CPU, Windows 11), testovaný proces omezený afinitou na 4 jádra (maska `0xF`) nebo 2 jádra (`0x3`)
  a vedle něj 4–6 procesů `pwsh` s nekonečnou smyčkou na stejných jádrech (zátěž jako ostatní sestavy v CI). Počítač byl
  navíc zatížený buildy a testy jiných agentů (T-202, T-186, T-224) – zátěž reprodukci pomohla, čísla níže jsou z ní.
- Přímý běh `LayerPeek.Core.Tests.exe` (nativní runner xUnit) chybu `ASampleDoesNotAllocate` **nereprodukoval**;
  `dotnet test --project tests/LayerPeek.Core.Tests` (Microsoft.Testing.Platform v režimu `--server dotnettestcli`, jako
  v CI) na 4 jádrech se zátěží ano: 2 ze 2 běhů selhaly (260 880 B a 191 200 B; CI: 173 840 B).
- Původce alokací určen v procesu dočasným `EventListener` na událostech `GCAllocationTick` (typ a OS vlákno; po
  diagnostice odstraněn, `dotnet-trace` ani jiný globální nástroj se neinstaloval).

### Příčina 1 – `ASampleDoesNotAllocate` (~200 KB v každém průchodu)
- Alokuje **testovací platforma**, ne detekce: 31 z 32 vzorků `GCAllocationTick` během měřených průchodů byly typy
  `Microsoft.Testing.Platform.IPC.*` – `SuccessfulTestResultMessage(s)`, `TestNodeDetails`, `TestInProgressMessage(s)`,
  stavové automaty `NamedPipeConnectionBase.WriteMessageAsync`, `NamedPipeClient.RequestReplyAsync`,
  `DotnetTestDataConsumer.ConsumeAsync` (zbylý jeden byl `EventWrittenEventArgs` samotného posluchače). Vlákno testu
  samo alokovalo 0 B.
- Mechanismus: kolekce `AllocationSensitive` běží až po paralelní fázi (~3 400 testů). Na zatíženém stroji testovací
  hostitel ještě mnoho sekund posílá výsledky předchozích testů přes pojmenovanou rouru do `dotnet test` (na vláknech
  thread poolu, často i na tom, které právě analyzuje vzorek). Test počítá alokace všech vláken
  (`GC.GetTotalAllocatedBytes`), takže každý průchod v té době naměří ~200–390 KB. Doběh fronty trval 13,7–17 s na
  4 jádrech se zátěží (v CI déle – test trval 41 s a zasažené byly všechny platné průchody). Test detekce běžel ve všech mých
  bězích jako první test kolekce, který měří všechna vlákna – proto nejspíš v CI padal hlavně on; pozdější testy už
  frontu nezastihly.
- Počítat jen vlákna, která se vzorku účastní, nejde spolehlivě: pracovní položka detekce běží na vláknech thread poolu,
  na kterých běží i odesílání platformy (stejné OS vlákno v záznamu). Proto oprava měření:
  `AllocationProbe.WaitForQuietProcess(timeout)` (`tests/Shared/AllocationProbe.cs`) čeká (spánkem, bez alokace), až
  žádné vlákno procesu 250 ms nealokuje; po limitu se měří i tak. Volá se **na začátku testu**, než test cokoli vytvoří
  (`QuietBeforeTest` = 60 s – tady doběhne fronta platformy), a **před každým průchodem** (`QuietBeforePass` = 5 s, kratší
  než `FrameTimeout` 10 s kamerového feedu na skutečných hodinách v testech hubu, aby čekání nevyvolalo reconnect).
  Průchod dál počítá **všechna** vlákna a mez zůstává 0 B, takže alokace na vzorek se pořád projeví v každém průchodu.
- Ověření (mezistav, čekání před každým průchodem): 3 ze 3 běhů `dotnet test` celé sestavy Core.Tests na 4 jádrech se
  zátěží zelené; čekání na klid trvalo 13,7 s / 17,0 s / 14,1 s, před dalšími průchody 0,26 s; zbylé ojedinělé drobnosti
  (648–1 816 B v jednom průchodu) zachytí opakování průchodů jako dřív.
- Ověření konečné verze: `dotnet test --project tests/LayerPeek.Core.Tests` 3 ze 3 zelené na 4 jádrech + 4 zátěžové
  procesy (52–55 s na běh, před opravou 56–57 s a 2 ze 2 červené), všechny alokační testy Core.Tests (nativní runner)
  3 ze 3 zelené na 2 jádrech + 6 zátěžových procesů (158–338 s), bez zátěže Core.Tests i Camera.FFmpeg.Tests zelené.
- Krok testu navíc čeká přes `SpinWait.SpinOnce` (uvolní CPU vláknům hubu a poolu) s časovým limitem na vzorek místo
  nekonečného `Thread.Yield()`.

### Příčina 2 – `SteadyStateTimelapseDoesNotAllocate` („Packets not read.“)
- Žádné zablokování ani ztracené probuzení: na 2 jádrech se zátěží průchod (200 dvojic paketů) trval 8,5–10+ s, jedna
  dvojice čekala na čtenáře průměrně ~50 ms, nejdéle 74–138 ms (každé probuzení čtenáře na thread poolu čeká na volné
  CPU; při překročení byl čtenář o 2 položky pozadu s 1 čekající pracovní položkou v poolu a dočetl za 66 ms). Limit
  `TestTimeouts.Wait` (10 s) platil pro **celý průchod**, takže pomalý stroj ho překročil.
- Oprava: limit platí pro každou dvojici (čtenář, který přestal číst, test pořád shodí), čekání dál přes
  `SpinWait.SpinOnce`; na začátku testu a před průchodem `WaitForQuietProcess` jako u detekce.
- Stejná chyba se reprodukovala u `CameraRecordingAllocationTests` („Packets not written.“, 2 jádra) – opraveno stejně
  (limit na paket; `CameraDashcamAllocationTests` ho už na paket měl).

### Další nalezená chyba – zablokování testů hubu (testovací fake)
- Na 2 jádrech se zátěží `CameraStreamHubAllocationTests.PacketsToAReaderDoNotAllocate` dvakrát visel bez využití CPU
  (> 2,5 min, i přes `CancelAfter(60 s)`). Příčina v `LockstepFeed`: hub po posledním přečteném paketu požádá o další
  (`MoveNextAsync` → `_requests.Release()`); když test mezitím fake zastaví a vlákno producenta se k CPU dostane až potom,
  producent po `_requests.Wait()` uvidí `_stopped` a skončí **bez dokončení** čekajícího čtení – hub pak při zavírání
  spojení čeká navždy (fake ignoruje zrušení). Oprava: producent při zastavení dokončí čekající čtení `false`,
  `MoveNextAsync` po zastavení vrátí `false` hned (pod zámkem, bez alokace). Týká se všech testů s `LockstepFeed` (hub,
  detekce). Po opravě test už nevisel, ale jednou selhal na pojistce `CancelAfter(60 s)` nastavené jednou pro celý test
  (zahřátí + 4 × 256 paketů na 2 jádrech se zátěží trvá přes minutu); pojistka se teď nastaví znovu před každým
  průchodem (mimo měřenou smyčku), stejně v `ReconnectingCameraFeedAllocationTests`.

### Ostatní změny
- `WaitForQuietProcess` i u ostatních testů Core.Tests, které počítají všechna vlákna (hub – 4 testy,
  `ReconnectingCameraFeed` asynchronní, nahrávání, dashcam) – stejný zdroj šumu; nejspíš i příčina 120 B u
  `GrayscaleKeyFrameSamplesDoNotAllocate` pod zátěží na Macu (neověřeno na Macu).
- Odstraněno `Assert.SkipWhen(ContinuousIntegration.IsWindowsRunner, …)` u obou testů; `ContinuousIntegration` zůstává
  pro T-221. Aktualizován komentář v `.github/workflows/ci.yml`, řádek `LAYERPEEK_CI` v `docs/prerequisites.md`
  a řádek v `tasks/README.md`.
- Revize (qa-reviewer): kroky testů snímků a vzorků hubu (`MeasureFramesAsync`, `MeasureSamplesAsync`) čekaly
  nekonečným `Thread.Yield()` bez limitu – teď stejně jako detekce s limitem `TestTimeouts.Wait` na krok, přes
  `SpinWait.SpinOnce(sleep1Threshold: -1)` (výchozí `SpinOnce` se `Sleep(1)` prodloužil 4 testy hubu z ~3,5 s na 10–12 s).
- Po review (2026-11-15): `WaitForQuietBeforeTest()` (60 s, na začátku testu) a `WaitForQuietProcess(QuietBeforePass)`
  (5 s, před průchodem) počítají vypršená čekání; `AllocationProbe.AssertNoneAllocated` / zpráva testu timelapse pak
  uvedou, že proces před N průchody neutichl (mimo měřenou část, bez alokace v ní). Čekání respektuje
  `TestContext.Current.CancellationToken` (zrušený běh nečeká až 60 s). Stejný vzor i v
  `FFmpegCameraFeedAllocationTests` (čekání před každým průchodem, než průchod vytvoří feed – žádný časovač na
  skutečných hodinách mezitím neběží; po zahřátí ne, přehrávání souboru za hubem nikdy nestojí). Ověřeno: build řešení
  0 varování, Core.Tests 2× a Camera.FFmpeg.Tests 1× zelené (bez zátěže).
- Žádný nový balíček NuGet.

### Selhání v CI po T-202 (běh 43712544910, 2026-11-15, větev `feature/ci-windows-ffmpeg-allocation`)
- Selhaly tři testy hubu v `FFmpegCameraFeedAllocationTests`: `SteadyStateFramesThroughTheHubDoNotAllocate`
  („Allocated 240 B“), `SteadyStateDisplayFramesThroughTheHubDoNotAllocate` a
  `SteadyStateGrayscaleFramesThroughTheHubDoNotAllocate` („No pass measured a steady state.“). Ostatní alokační testy
  FFmpeg (bez hubu) prošly.
- **Vlastní build FFmpeg (T-202) příčinou není.** Porovnání A/B se stejnými testy a dočasnou diagnostikou (kopie
  výstupu testů s DLL z mezipaměti BtbN `e907b01f…` proti vlastnímu buildu `win-x64-6363cabd…`): bez zátěže na 4 jádrech
  obě sady ~2 ms na snímek, žádné selhání; na 2 jádrech s 8–10 zátěžovými procesy obě sady stejně – 240 B a průchody
  „pozdě“. Dekodér běží v obou s `thread_count = 1` (`FFmpegDecoder`), takže w32threads/pthreads nehraje roli. V CI šlo
  o zátěž souběžných sestav (Core.Tests, Detection.Tests běžely současně) a náhodu, že předchozí běh prošel.
- Reprodukce: nativní runner `LayerPeek.Camera.FFmpeg.Tests.exe -method "*ThroughTheHub*"`, afinita `0x3` a 8–10
  procesů `pwsh` s nekonečnou smyčkou na stejných jádrech: 5 z 5 běhů s původním testem červených (240 B v 4 bězích,
  „No pass measured a steady state.“ ve 2), přesně jako v CI.
- **Příčina A – hladovějící spotřebitel:** spotřebitel testu se točil přes `Thread.Yield()`; vedle vlákna dekodéru a
  zátěže dostal tak málo CPU, že než vzal 3 snímky (podmínka zahřátí), soubor (71 snímků, přehrává se tak rychle, jak se
  dekóduje) odehrál 40–71 snímků. Průchod pak neměl 30 snímků rezervy (`start + 30 > 71`) a nic neměřil; v CI všech
  10 průchodů → „No pass measured a steady state.“ Diagnostika: `taken` 3–7 proti `dropped` 45–66.
- **Příčina B – konec souboru uvnitř měření (240 B):** průchod, kterému rezerva zbyla, mohl přesto dojet na konec
  souboru, protože spotřebitel byl mimo CPU, zatímco dekodér dohrál zbytek. Po posledním snímku `ReconnectingCameraFeed`
  zavírá dekodér (čeká na jeho vlákno) a plánuje reconnect (propojený `CancellationTokenSource`, `Task.Delay`…) –
  ~240 B – a stav „konec streamu“ hlásí až potom, takže kontrola `lease.Status.State == Streaming` po měření ještě
  prošla. Důkaz: všech 11 průchodů s 240 B mělo po měření `taken + dropped` = 71 (celý soubor), žádný průchod s menším
  počtem 240 B nenaměřil; vlákno testu i vlákno dekodéru (dočasný čítač `GetAllocatedBytesForCurrentThread` po každém
  snímku) alokovaly 0 B. Nejde o alokaci na snímek ani o soupeření o `Lock` (`Monitor.LockContentionCount` = 0 během
  průchodů; první soupeření o nový `Lock` by stálo 56 B, ověřeno zvlášť).
- **Oprava měření** (`MeasureHubPassAsync`, mez 0 B beze změny, všechna vlákna se dál počítají): spotřebitel čeká na
  `FrameAvailable` hubu přes `AutoResetEvent` (`WaitOne(10)`, jako okno kamery, které si nechá poslat oznámení) místo
  aktivního točení – probuzené vlákno dostane CPU hned, takže zahřátí končí na 10 snímcích a spotřebitel vezme většinu
  snímků; průchod je platný jen tehdy, když po měření (po převzetí posledního snímku) `taken + dropped` < 71, tedy konec
  souboru v měření nenastal. Měřená smyčka má navíc časový limit („The frames stopped.“).
- Ověření: 2 jádra + 10 zátěžových procesů, testy hubu 6 z 6 zelené (diagnostika: `start` většinou 10, `taken` 21–40
  z 30–40, žádný průchod „pozdě“, ojedinělý šum 24 B / 98 KB v jednom průchodu pokryjí opakované průchody);
  4 jádra + 8 procesů 4 z 4 zelené; `dotnet test --project tests/LayerPeek.Camera.FFmpeg.Tests` (jako v CI) bez omezení
  1×, na 4 jádrech + 6 procesech 3×, na 2 jádrech + 8 procesech 2× zelené.
- Souběžně opraven nestabilní `FileLogProviderTests.TwoIndependentWritersLoseAndSplitNoRecords` („The primary writer
  rotated.“, v CI 14 ms): zapisovač zapíše celou frontu jedním `Append` a rotuje jen soubor, který už záznamy má
  (`LogFile.Append`: `end > 0`). Když vlákno zapisovače primární instance dostalo CPU až po všem logování, zapsalo vše
  jedním zápisem do ještě prázdného souboru a nerotovalo nikdy (případně jediný pokus o rotaci selhal a další zápis už
  nepřišel). Chování aplikace je správné; test teď u primárních vláken po každých 100 záznamech volá `Flush`, takže
  primární zapisovač zapíše aspoň desetkrát během zápisů sekundárního a rotace nastane vždy. Lokálně se selhání
  nereprodukovalo (1 jádro + 6 procesů, 10 běhů původního testu zelených), příčina je odvozená z kódu a z doby běhu v CI.
- Revize (qa-reviewer): `AutoResetEvent` v `MeasureHubPassAsync` se deklaruje před hubem, takže se uvolní až po
  něm (snímek publikovaný během konce odběru ho ještě může signalizovat).

### Zbývá (hlavní agent)
- Kritérium 2: po pushi aspoň 3 zelené běhy CI po sobě na `windows-2025` (bez přeskočení); teprve pak úkol do `done/`.
- Kritérium 3: `dotnet build LayerPeek.slnx` je bez varování (ověřeno ve worktree); úplný `dotnet test` celého řešení
  agent nespouštěl (zadání) – spustí hlavní agent. Spuštěno: Core.Tests, Camera.FFmpeg.Tests, `CiWorkflowTests`.
- Při slučování pozor: `.github/workflows/ci.yml` a `docs/prerequisites.md` mění souběžně i větev T-202.

### Selhání v CI 2026-11-15 (běh 43742173997, commit c23597c8) – qa-reviewer
- `PrintFailureDetectionAllocationTests.ASampleDoesNotAllocate` selhal po 1 min 48 s: „Allocated 227288 B (expected 0).
  5 wait(s) for a quiet process timed out“ – čekání na začátku testu (60 s) i před všemi 4 platnými průchody (5 s)
  vypršela. Další dva běhy (43745897232, 43748044752) na Windows prošly. Podle TRX (artefakt `test-results-win-x64`)
  běžel test jako první měřící test kolekce `AllocationSensitive` po paralelní fázi (~3 400 výsledků); hned po něm
  doběhly další alokační testy rychle, tj. šum trval ~108 s od konce paralelní fáze. V CI současně běží 4 sestavy
  na 4 vCPU (Core, Detection, Camera.FFmpeg, UiTests), hostitel `dotnet test` odpovídá na zprávy výsledků pomalu.
- **Reprodukce lokálně:** `dotnet test --solution` s výběrem Core, Detection, Camera.FFmpeg, PrusaLink, App,
  PrusaSimulator (bez E2E/UI), proces omezený afinitou na 4 jádra (`0xF`, děti dědí), `LAYERPEEK_CI=1`, dočasná
  diagnostika čekání a průchodů (odstraněna). Běh 1: čekání na začátku testu skončilo „klidem“ po 9,0 s, ale hned
  další čekání (5 s) vypršelo a **průchod 0 naměřil 1 774 272 B**; průchody 1–2 pak 648 B a 0 B (test prošel díky
  opakování). Běh 2: klid po 0,4 s, první průchod 0 B.
- **Závěr:** fronta výsledků platformy (IPC do `dotnet test`, `RequestReplyAsync` čeká na odpověď hostitele) se
  vyprazdňuje po dávkách – mezi dávkami je proces i stovky ms bez alokace, takže „250 ms bez alokace“ neznamená
  „fronta je prázdná“. Délka fronty závisí na rychlosti hostitele (v CI desítky až stovky sekund). Čekání je proto jen
  heuristika; delší limity by problém jen posunuly.
- **Zvážené a zamítnuté varianty:**
  - Počítat jen vlákna testovaného kódu: vzorek prochází vláknem testu, vláknem streamu hubu
    (`CameraStreamHub.RunAsync`, `Task.Run`) a pracovní položkou `PrintFailureDetectionService.Run`
    (`ThreadPool.UnsafeQueueUserWorkItem`, globální fronta poolu) – na stejných vláknech poolu běží i odesílání
    platformy. `GC.GetAllocatedBytesForCurrentThread` nejde ohraničit kolem cizí pracovní položky bez háčku
    v produkčním kódu; fake detektor pokrývá jen část `Execute` (ne `TakeLatest`, kontext, jas, potvrzovací okno).
  - Vyřazení vláken platformy: vlákna poolu nejsou vyhrazená, nelze je odlišit.
  - Spustit kolekci `AllocationSensitive` jako první (vlastní `ITestCollectionOrderer` na úrovni sestavy):
    vyzkoušeno – xUnit 4.0.3 pouští kolekce s `DisableParallelization` vždy až po paralelní fázi bez ohledu na pořadí
    (TRX: alokační testy na pozicích 3390–3409 z 3411). Prototyp odstraněn. Vlastní testovací framework
    (`[assembly: TestFramework]`, přepsání `RunTestCollections`) by šel, ale závisí na vnitřnostech xUnit – nedoporučuji.
  - Vzorkování `GCAllocationTick` (~100 KB) k vyřazení průchodů s cizí alokací: nepřesné, posluchač sám alokuje.
- Zbývá izolace procesu (otázka Q1, rozhodnuto: samostatný krok CI).
- Vedlejší nález (jiný úkol): při běhu na 4 jádrech jednou selhal
  `UnixSocketInstanceTransportTests.ConnectionOfAnotherUserIsRefusedUnread(userKnown: False)`
  (`tests/LayerPeek.App.Tests/SingleInstance/InstanceTransportTests.cs:528`, `Assert.True(refusing.PeerChecks > 0)`):
  klient vzdá přeposlání po 1 s dřív, než primární instance pod zátěží spojení přijme a zkontroluje – závod v testu.

### Samostatný krok CI (rozhodnutí Q1, 2026-11-15) – qa-reviewer
- `tests/Shared/AllocationProbe.cs`: definice kolekce `AllocationSensitive` nese `[Trait("Category", "AllocationSensitive")]`
  (konstanty `TraitName` / `TraitValue`). Trait definice kolekce xUnit přenáší na všechny její testy, takže stačí jedno
  místo pro všech 5 projektů (Core, Camera.FFmpeg, App, PrusaLink, Detection) a nový test v kolekci ho dostane sám.
- Hlídací testy `AllocationSensitiveTraitTests` (ve stejném sdíleném souboru, běží v každém projektu, který ho linkuje):
  kolekce nese trait, podle kterého CI filtruje; každá třída `*AllocationTests` je v kolekci `AllocationSensitive`
  (jinak by v CI běžela ve společném kroku). Opačný směr se nehlídá: v kolekci jsou i třídy s jiným názvem
  (`*CostTests` v Detection.Tests, `WebProxyStreamingTests`), a to je v pořádku.
- `.github/workflows/ci.yml`: hlavní krok `Test (Debug, …)` má `--filter-not-trait "Category=AllocationSensitive"`,
  nový krok `Test the allocation-sensitive tests (Debug, in their own processes)` hned po něm spustí nad stejným
  `ci-tests.slnf` jen `--filter-trait "Category=AllocationSensitive"`. TRX do stejné složky `TEST_RESULTS` (názvy souborů s časem se nepřekrývají), artefakt beze změny. Platí pro
  job Windows i macOS (jeden job s maticí). Hlídá `CiWorkflowTests.TheAllocationTestsRunInTheirOwnStepAfterTheOtherTests`
  (napsán nejdřív, před změnou workflow selhal). `docs/prerequisites.md`, oddíl „CI (GitHub Actions)“, bod 2 doplněn.
- Lokální `dotnet test` beze změny (alokační testy běží se všemi ostatními).
- Ověřeno lokálně (`dotnet test … --list-tests`): Core.Tests 3 371 testů = 3 351 s `--filter-not-trait` + 20
  s `--filter-trait`; Camera.FFmpeg.Tests 224 = 213 + 11; `--filter-trait` vybere v App.Tests, PrusaLink.Tests
  a Detection.Tests právě třídy kolekce.
- Běhy (bez zátěže): Core.Tests s `--filter-not-trait` 3 393 zelených, s `--filter-trait` 20 zelených;
  Camera.FFmpeg.Tests 211 + 2 přeskočené / 11 zelených; `AllocationSensitiveTraitTests` v App, PrusaLink a Detection
  zelené; Scripts.Tests 25 zelených + 1 přeskočený. Krok alokačních testů (Core + Camera.FFmpeg) na 4 jádrech se
  4 zátěžovými procesy zelený (31 testů, 48 s). `dotnet build LayerPeek.slnx` 0 varování.
- Kritérium „3 zelené běhy CI po sobě“ zůstává otevřené – ověří hlavní agent po sloučení.
- **Po review (2026-11-15):** (1) krok alokačních testů má `if: ${{ !cancelled() && steps.build-debug.outcome == 'success' }}`
  (krok `Build (Debug)` dostal `id: build-debug`), takže běží i po selhání hlavního kroku testů; job selže, když selže
  kterýkoli z nich. (2) `--ignore-exit-code 8` odstraněn – předchozí poznámka byla chybná: kódem 8 skončí jen samostatný
  běh projektu bez alokačních testů, `dotnet test --solution` nad více projekty, z nichž jeden žádný nemá, vrátí 0
  (projekt jen hlásí „Zero tests ran“; ověřeno: Core + Camera.FFmpeg + PrusaSimulator s filtrem → exit 0, 31 testů).
  Přepínač by tedy skryl jen případ, kdy neproběhl **žádný** alokační test (filtr bez shody → exit 8, ověřeno
  s `Category=NoSuch`). `--minimum-expected-tests` záměrně nepoužit: pevné číslo by se muselo měnit s každým přidaným
  či odebraným alokačním testem a liší se podle OS (přeskočené testy na macOS); úplnost výběru hlídá jediný trait
  na definici kolekce a `AllocationSensitiveTraitTests`, prázdný výběr shodí exit 8 (ověřeno, že
  `--minimum-expected-tests` na úrovni řešení funguje: 32 při 31 testech → exit 9).
