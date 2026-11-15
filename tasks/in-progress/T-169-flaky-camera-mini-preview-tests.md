---
id: T-169
title: Občasné selhání E2E malého náhledu kamery (CameraMiniPreviewTests)
status: in-progress
owner: qa-reviewer
priority: P3
platform: windows
depends_on: []
---

## Cíl
Dva E2E testy malého náhledu kamery na Windows občas selžou a hned potom samostatně projdou. Zjistit, jestli jde
o nestabilitu testu, nebo o chybu aplikace (souběh při změně velikosti okna), a opravit ji. Podobně jako T-123.

## Rozsah
Pozorováno 2026-11-05 (develop `7047eef5`–`21c66c63`, Windows 11 Pro 10.0.26300):
- `CameraMiniPreviewTests.ResizeGripCorrectsTheHeightOnceAfterTheReleaseAndTheStripMovesTheWindow`: 2× FAIL (jednou při
  samostatném běhu třídy, jednou v `-Class '*Camera*'`). Zpráva: „The window kept changing after the correction:
  (960,590)-(1440,893) 480x303, (960,545)-(1440,848) 480x303, …“. Okno se po opravě výšky jednou posune o 45 px nahoru
  (stejná velikost) a pak už je stabilní. Test asi bere jako změnu i poslední posun, nebo aplikace okno posune
  s opožděním.
- `CameraMiniPreviewTests.TogglesGrowTheWindowFromTheBottomEdgeAndSurviveARestart`: 1× FAIL v `-Class '*Camera*'`:
  „Waited 15 s for the name row expanded: 320 × 247 (now (320, 213))“. Řádek s názvem se po přepnutí nerozbalil.
- Ve všech případech hned následující samostatný běh `-Class '*CameraMiniPreviewTests'` 6/6 PASS. Výsledky selhání
  jsou v `%TEMP%\LayerPeek.App.E2E.Results.<id>-failed-*` (snímky a logy aplikace).
- Postup: z uložených výsledků a logů určit příčinu; opakovat `pwsh tools/run-e2e.ps1 -Class '*CameraMiniPreviewTests'
  -NoSystemChanges` 10× (jako u T-123). Chyba aplikace = oprava v aplikaci, jinak oprava čekání v testu.

## Akceptační kritéria
- [x] Příčina obou selhání zjištěná a zapsaná (test / aplikace)
- [ ] `pwsh tools/run-e2e.ps1 -Class '*CameraMiniPreviewTests' -NoSystemChanges` 10× za sebou PASS na Windows
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- ~~Blokováno prostředím: relace uživatele odpojená (RDP `Disc`).~~ **Rozhodnutí uživatele 2026-11-15:** uživatel relaci
  znovu připojí; před během E2E se ověří `qwinsta` (relace `Active`).
- ~~Opravit dvoukrokovou změnu velikosti a polohy v aplikaci, nebo jen v testu?~~ **Rozhodnutí uživatele 2026-11-15:**
  pokud se dvoukroková změna potvrdí, **opravit aplikaci**: na Windows nastavit velikost i polohu jedním `SetWindowPos`,
  aby nebyl vidět žádný mezistav (mění pořadí zvolené v T-093). Chování na macOS beze změny, pokud tam stejný problém
  není (zapsat). Navíc zpevnit test: stav přepínače kontrolovat před čekáním na velikost, aby šel ztracený klik
  odlišit od chyby aplikace.
- ~~Aplikace Orca si během E2E bere popředí (5 z 10 běhů) – může ji uživatel na dobu běhů zavřít nebo pozastavit?~~
  **Rozhodnutí uživatele 2026-11-15:** uživatel Orca na dobu série **minimalizuje** (nezavírá – Orca se nikdy nezavírá
  ani neukončuje, stejně jako žádná jiná aplikace uživatele) a nechá plochu asi 40 min v klidu. Série se spustí až po
  pokynu; nejdřív jeden běh testu úchytu ověří, že popředí nikdo nebere, jinak se série nespouští a hlásí se.
- ~~E2E s myší přes RDP nefungují, když klient vzdálené plochy vrací ukazatel na místo myši uživatele – kde spustit sérii?~~
  **Rozhodnutí uživatele 2026-11-15:** RDP zůstane otevřené (neminimalizované), myš uživatele bude **mimo okno klienta
  RDP** a asi 1 h se jí nedotkne. Nejdřív se ověří, že ukazatel nad oknem kamery vydrží; pak série 10×.
- Má E2E infrastruktura (`tools/run-e2e.ps1`) na začátku běhu varovat před spuštěnými aplikacemi, které berou popředí
  (např. Orca)? Otevřené na později, rozhodne uživatel.
- ~~Varovat v `tools/run-e2e.ps1` před během ve vzdálené ploše?~~ **Rozhodnutí uživatele 2026-11-15:** ano, jen
  varování na začátku běhu, že E2E testy s myší jsou v relaci RDP nespolehlivé (klient posouvá ukazatel); běh pokračuje.
  Pester test a věta do dokumentace E2E.
- ~~Ověřit na macOS, jestli dopočet výšky malého náhledu (`SetBounds`: velikost až v dalším průchodu rozvržení, poloha
  hned) neukáže stejný mezistav – přidat bod do T-127, nebo nový úkol? Rozhodne uživatel (doplněno při revizi).~~
  **Rozhodnutí uživatele 2026-11-15:** bod do T-127 (přidán jako oddíl 1s).

## Poznámky k implementaci
- **Příčina 1 – `ResizeGripCorrectsTheHeight…` („The window kept changing after the correction“): chyba aplikace, opraveno.**
  Dopočet výšky (`CameraWindow.CorrectMiniSize` → `SetBounds`) nastavoval nejdřív velikost (`Width`/`Height` +
  `UpdateLayout`, první `SetWindowPos`) a pak polohu (`Position`, druhé `SetWindowPos`). Okno uprostřed roste od středu,
  takže chvíli existoval mezistav „nová výška u staré horní hrany“. Změřeno 2026-11-15 rychlým čtením `GetWindowRect`
  po puštění úchytu: `(510,379) 480x213` → 7,5 ms `(510,379) 480x303` (mezistav) → 9,5 ms `(510,334) 480x303`; pod
  zátěží trvá mezistav déle a test ho pak viděl jako další změnu (posun o 45 px = polovina z +90 px).
  - Oprava (rozhodnutí uživatele 2026-11-15): na Windows dopočet nastaví velikost i polohu jedním `SetWindowPos`
    (`CameraWindow.SetBoundsAtOnce`, `Platform/Windows/WindowFrameNative.TryGetClientFrame` + `TrySetBounds`, obdélník
    okna z čisté funkce `CameraMiniGeometry.WindowRect` se stejným zaokrouhlením, jakým dopočet porovnává). Avalonia
    převezme velikost z `WM_SIZE` (`Window.HandleResized` nastaví `Width`/`Height`) a polohu z `WM_MOVE`. Po opravě:
    `(510,379) 480x213` → 5–18 ms `(510,334) 480x303`, žádný mezistav. Týká se i rozbalení řádků pásu (stejný dopočet).
  - Pořadí z T-093 zůstává pro umístění na jiný monitor (`PlaceMini`, nejdřív velikost, pak poloha) – dopočet je vždy
    na monitoru, kde okno je. macOS beze změny (`SetBounds`); na macOS se tam velikost projeví až v dalším průchodu
    rozvržení, takže podobný mezistav je možný – neověřeno, případně do T-127 / nového úkolu.
  - Testy: jednotkové `CameraMiniGeometryTests.WindowRect*` (napsané před implementací); E2E nově hlídá přímo
    mezistav: po puštění úchytu se opravená výška smí objevit jen v jednom obdélníku okna. Proti aplikaci bez opravy
    test selhal („showed 2 window rectangles … 7,5 ms … 9,5 ms“), s opravou prošel.
- **Příčina 2 – `TogglesGrowTheWindowFromTheBottomEdge…` („name row expanded … now 320 × 213“): rušení plochy, test
  zpevněn.** Okno se nezvětšilo, protože klik nedošel na přepínač:
  - jednou zachyceno, že přepínač v pravém dolním rohu (1591, 939) zakrývalo systémové okno
    `Windows.UI.Core.CoreWindow` (oznámení / banner nad hlavním panelem). Test teď před klikem i před stiskem úchytu
    čeká (až 30 s), než bod nic nezakrývá, a hlásí, co ho zakrývá;
  - jindy si popředí vzala jiná aplikace uživatele (viz níže).
  - Test kontroluje stav přepínače hned po kliku (Space / Enter / myš) před čekáním na velikost, takže ztracený vstup
    hlásí jako „the input did not reach the toggle“ (s oknem v popředí, když patří jinému procesu), a ne jako chybu
    velikosti okna.
- **Rušení prostředí (2026-11-15):** aplikace uživatele **Orca** (stablyai, Electron, `%LOCALAPPDATA%\Programs\orca\Orca.exe`,
  okno `Chrome_WidgetWin_1`) si při stisku myši v testu bere popředí. Stisk pak jde do ní, systémová smyčka změny
  velikosti se nespustí (`GetGUIThreadInfo`: bez `GUI_INMOVESIZE`) a okno se nezmění. Test to nově hlásí výslovně:
  „Another app took the foreground during the drag …: … (Orca). The desktop was in use during the run“. Stejné okno
  převzalo popředí už v prvním běhu dne (tehdy chybně považováno za Edge). V odpojené relaci RDP se úchyt nedal použít
  vůbec.
- Běhy `pwsh tools/run-e2e.ps1 -Class '*CameraMiniPreviewTests' -NoSystemChanges` s opravou:
  - první série 10×: 8× PASS, 2× selhání úchytu (tehdy ještě bez rozlišení příčiny, podle pozdější diagnostiky Orca);
  - samotný test úchytu 15× za sebou PASS;
  - poslední série 10× (finální kód): 5× PASS, 5× FAIL, **všech 5 hlášeno jako popředí převzaté aplikací Orca**
    (4× při tažení úchytu, 1× při kliku na přepínač). Kritérium „10× za sebou PASS“ proto zatím **nesplněno** – čeká na
    běh bez Orca (viz Otevřené otázky).
- Dočasné trasování a diagnostika jsou odstraněné (`DIAG` v kódu není).
- `dotnet build`: 0 varování, 0 chyb; `dotnet test`: 11419 celkem, 10828 prošlo, 591 přeskočeno, 0 selhalo.
- **Kontrola po minimalizaci Orca (2026-11-15, plocha v klidu, relace `Active`):** 2 běhy samotného testu úchytu, oba
  FAIL, popředí tentokrát převzala **plocha** (`Progman`), ne Orca – série 10× se podle dohody nespustila.
- **Diagnóza aktivace (2026-11-15, rozhodnutí uživatele: diagnostika teď, plocha v klidu):** relace je **RDP**
  (`qwinsta`: `rdp-tcp#0` Active, konzole je jiná relace 2 `Conn`). Dočasný záznam WinEventů v celém systému
  (`EVENT_SYSTEM_FOREGROUND`, `CAPTURESTART/END`, `MOVESIZESTART/END`) a sondy kurzoru ukázaly:
  - stisk vůbec nedošel do okna kamery: v okamžiku stisku byl kurzor na **(1548, 17)**, ne na úchytu (672, 469);
    stisk dostal seznam ikon plochy (`SysListView32`, explorer) a ten přešel do popředí. Okno kamery na `WM_NCHITTEST`
    odpovídalo normálně, Avalonia ani aplikace kurzor nikdy neposouvá (`SetCursorPos` nikde);
  - `Mouse.Position` kurzor na úchyt dá, ale 27–160 ms poté ho něco vrátí na pevný bod (1548, 17), a to jen nad oknem
    kamery při změně tvaru kurzoru (úchyt, první vjezd na obraz). Mimo okno kurzor zůstal, mimo test také. Výklad:
    klient vzdálené plochy synchronizuje ukazatel na místo, kde leží myš uživatele, když vzdálený kurzor změní tvar.
    Dřívější „krádež popředí aplikací Orca“ byla totéž: stisk padl na okno pod kurzorem v (1548, 17), tehdy Orca.
  - **Příčina: prostředí (ukazatel přes RDP), ne aplikace.** V aktuálním stavu se kurzor nad oknem kamery udržet nedá
    vůbec (3 běhy, pokaždé 10 pokusů, vždy vrácen do 300 ms).
- Úpravy testu (bez dočasné diagnostiky, ta je odstraněná):
  - před stiskem úchytu i před klikem na přepínač test umístí kurzor a čeká, až na místě 300 ms vydrží (při posunu ho
    vrátí, nejvýš 10×); jinak selže zprávou „The pointer did not stay at … (moved to …): something else moves it,
    e.g. a remote desktop client with the user's mouse“;
  - každý krok tažení ověří, že kurzor je opravdu na svém místě a popředí nemá okno jiného procesu; zavádějící zpráva
    „Another app took the foreground“ je nahrazená zprávou „The input did not reach the camera window … the pointer is
    at … (expected …) … not a fault of the app“.
- Kritérium „10× za sebou PASS“ zatím **nesplněno**: potřebuje běh, ve kterém ukazatel nikdo neposouvá (místní konzole,
  nebo RDP bez myši uživatele nad oknem vzdálené plochy / s minimalizovaným oknem klienta). Viz Otevřené otázky.
- **Ověření po rozhodnutí uživatele (myš mimo okno klienta RDP, 2026-11-15):** 3 běhy testu úchytu, ve všech se ukazatel
  nad oknem kamery opět vrátil, tentokrát na **(997, 994)** – zřejmě místo, kde myš uživatele okno klienta opustila
  (dolní okraj). Klient RDP tedy vrací ukazatel na poslední známou polohu místní myši i tehdy, když je myš mimo jeho
  okno. Série 10× se nespustila; kritérium zůstává nesplněné. Zbývá místní konzole (nebo jiný způsob, jak klientovi
  RDP synchronizaci ukazatele zakázat) – rozhodne uživatel.
- Připomínka revize k ověření při běhu na konzoli: zužující tažení jde za minimální šířku 240 a každý krok kontroluje
  `AssertInputReached` (kurzor na cílovém bodě). Kdyby systémová smyčka změny velikosti kurzor u minima omezila
  (`ClipCursor`), krok by selhal zprávou „The input did not reach …“ – pak kontrolu u zužování povolit jen pro popředí.
- Po revizi (2026-11-15, bez běhu E2E):
  - záznam okna po puštění úchytu běží ve vlastním vlákně a začíná těsně **před** puštěním (`StartTrace`); prvních
    300 ms čte co nejčastěji s `Thread.Yield`, pak každých 5 ms (už netočí celé jádro 1,5 s);
  - `WindowFrameNative.TrySetBounds` vrací kód chyby Win32 (`Marshal.GetLastPInvokeError`); okno ho při selhání
    zaloguje na úrovni Debug (`CameraViewModel.LogWindowBoundsFailed`) a nastaví velikost a polohu postupně;
  - `ExpectToggleAsync` dostává okno od volajícího;
  - `tools/run-e2e.ps1` v relaci vzdálené plochy (`SESSIONNAME` `RDP-*` nebo `GetSystemMetrics(SM_REMOTESESSION)`)
    na začátku vypíše varování, že testy s myší jsou tam nespolehlivé, a pokračuje (`Get-RemoteSessionWarning`
    v `tools/RunE2E.Results.psm1`, Pester v `tools/RunE2E.Results.Tests.ps1`); věta v `docs/troubleshooting.md`
    a v nápovědě skriptu;
  - T-127: nový oddíl 1s (macOS – dopočet výšky bez mezistavu).
