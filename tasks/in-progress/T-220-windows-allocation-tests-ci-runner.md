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
- (žádné)

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

### Zbývá (hlavní agent)
- Kritérium 2: po pushi aspoň 3 zelené běhy CI po sobě na `windows-2025` (bez přeskočení); teprve pak úkol do `done/`.
- Kritérium 3: `dotnet build LayerPeek.slnx` je bez varování (ověřeno ve worktree); úplný `dotnet test` celého řešení
  agent nespouštěl (zadání) – spustí hlavní agent. Spuštěno: Core.Tests, Camera.FFmpeg.Tests, `CiWorkflowTests`.
- Při slučování pozor: `.github/workflows/ci.yml` a `docs/prerequisites.md` mění souběžně i větev T-202.
