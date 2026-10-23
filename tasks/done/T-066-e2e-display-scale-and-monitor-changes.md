---
id: T-066
title: E2E – zásahy do zobrazení: měřítko monitoru a dočasné vypnutí monitoru
status: done
owner: qa-reviewer
priority: P2
platform: windows
depends_on: [T-065]
---

## Cíl
Dva zásahy do systému pro E2E testy (infrastruktura T-065), bez kterých nejde automaticky ověřit chování oken a ikon při 125/150 % (T-025, T-035, T-039, T-041) a při odpojeném monitoru (T-035):
1. **měřítko** (100/125/150 %) vybraného monitoru za běhu, bez odhlášení;
2. **dočasné vypnutí** dalšího (ne primárního) monitoru, jako by se odpojil.

Úkol dodá jen zásahy a jejich samotesty. Testy aplikace jsou v T-070 a T-071.

## Rozhodnuto
- **Okna jiných aplikací při vypnutí monitoru (uživatel 2026-10-22, dřív Q1):** spolehnout se na to, že Windows okna po návratu monitoru vrátí (nastavení „Pamatovat umístění oken podle připojení monitoru“). Test cizí okna sám nepřesouvá. Samotest ale ověří, že se okna vrátila.
- **Nedokumentované API měřítka (uživatel 2026-10-22, dřív Q2):** přijato (`DisplayConfigSetDeviceInfo` s typy −3/−4). Když se změna neprojeví, test se **přeskočí** s důvodem, neselže.
- **Implementační volby (uživatel 2026-10-22, dřív Q3):**
  - (a) přísné potvrzení po druzích: skript předává `PTD_E2E_SYSTEM_CHANGE_KINDS` se seznamem druhů z varování, zásah jiného druhu se odmítne (test bez správného traitu selže) – ano;
  - (b) Nerušit se na začátku a konci běhu čte (Win+N) jen tehdy, když varování uvedlo `DoNotDisturb` nebo `Toasts` – ano;
  - (c) snímek stavu každého E2E běhu (i `-NoSystemChanges`) čte měřítko nedokumentovaným paketem −3 (jen čtení) – ano, ale nikdy nesmí běh shodit ani zdržet, když paket nefunguje: měřítko je pak „neznámé“, jen se zapíše do snímku a neporovnává;
  - (d) tolerance 8 px pro „okno je zpět“ – ano;
  - (e) kanár pojistky má vlastní hodnotu `TestProbe` a ve varování se objeví řádek o testovací hodnotě v `HKCU\Software\PrusaTrayDiagnostic.E2E.Tests` – ano.
- **Okna jiných aplikací ve výsledcích (uživatel 2026-10-22):** žádné snímky celé plochy a žádné titulky oken jiných aplikací; kontrola „okna jsou zpět“ smí zapsat jen název procesu a obdélník.

## Rozsah

### Měřítko (`DisplayScaleChange`)
- Čtení a nastavení přes `DisplayConfigGetDeviceInfo` / `DisplayConfigSetDeviceInfo` s **nedokumentovanými** typy paketů −3 (čtení: aktuální, doporučené, min. a max. krok) a −4 (nastavení relativního kroku k doporučenému měřítku), pro zdroj (`sourceId` + `adapterId`) monitoru z `QueryDisplayConfig`. Vzor: https://github.com/lihas/windows-DPI-scaling-sample (Unlicense; jen jako zdroj informací, kód se nekopíruje, žádný balíček). Interop přes `[LibraryImport]` v testovacím projektu.
- Kroky měřítka: 100, 125, 150, 175, 200 … %. Test požádá o absolutní měřítko, zásah ho převede na krok. Měřítko mimo rozsah monitoru → zásah ohlásí „nedostupné“ a test se **přeskočí** s důvodem (např. malý monitor nedovolí 150 %).
- **Ověření, že změna proběhla:** po nastavení do 10 s `GetDpiForMonitor(MDT_EFFECTIVE_DPI)` daného monitoru vrátí očekávané DPI (např. 144 pro 150 %). Pokud ne (nedokumentované API přestalo fungovat), vrátí se původní hodnota a test se přeskočí s důvodem „změna měřítka za běhu není na této verzi Windows k dispozici“. Číslo sestavení Windows se zapíše do výsledků.
- **Návrat:** původní krok. Windows hodnotu ukládají do `HKCU\Control Panel\Desktop\PerMonitorSettings\<id monitoru>\DpiValue`. Pokud hodnota (nebo klíč) před změnou neexistovala, po návratu se smaže, aby registr zůstal přesně jako před během. Původní stav v záznamu T-065 obsahuje krok i stav registru.
- **Systémové DPI se nemění:** `GetDpiForSystem` zůstává hodnota z přihlášení. Zásah to zapíše do výsledků (`systemDpi`, `monitorDpi`), aby testy T-070 věděly, zda mohou ověřovat věci závislé na systémovém DPI (viz T-065, Zůstává ručně 1).
- Okna uživatele se při změně měřítka přeskládají (jako při ruční změně). Po návratu měřítka se vrátí, protože Windows je přepočítají zpět. Zapsat do varování T-065 (už tam je).

### Vypnutí monitoru (`MonitorDisconnectChange`)
- `QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS)` uloží současné cesty a režimy. Nová konfigurace bez cesty vybraného **neprimárního** monitoru (`SetDisplayConfig` s `SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES`, **bez** `SDC_SAVE_TO_DATABASE`, takže po restartu počítače Windows použijí uloženou konfiguraci s oběma monitory). Primární monitor se nikdy nevypíná.
- Ověření: do 10 s `EnumDisplayMonitors` monitor nevrací.
- **Návrat:** `SetDisplayConfig` s uloženými cestami a režimy. Když selže (např. po restartu se změnilo LUID adaptéru), použije se `SDC_APPLY | SDC_TOPOLOGY_EXTEND`, případně `SDC_USE_DATABASE_CURRENT`. Ověří se, že počet monitorů, jejich rozlišení, poloha a primární monitor odpovídají stavu před změnou.
- Záznam T-065 obsahuje uložené struktury (binárně, base64) a čitelný popis (názvy monitorů, rozlišení, poloha) pro ruční návrat.
- Bez druhého monitoru se testy přeskočí s důvodem.
- **Okna jiných aplikací:** Windows je při vypnutí monitoru přesunou na zbývající monitor. Po návratu je vrátí podle nastavení „Pamatovat umístění oken podle připojení monitoru“ (rozhodnutí 2026-10-22). Zásah si před vypnutím zapíše viditelná okna ostatních procesů na tomto monitoru (HWND, proces, obdélník, stav) jen pro kontrolu, sám je nepřesouvá. Po návratu monitoru ověří, že jsou zpět na původním monitoru. Pokud ne, zapíše do výsledků seznam (název procesu a obdélník, bez titulku okna – rozhodnutí 2026-10-22) a nahlásí to jako chybu běhu.

### Samotesty
- Měřítko: 100 → 125 → 150 → původní na primárním i na druhém monitoru (podle dostupnosti); po každém kroku `GetDpiForMonitor`; po návratu registr `PerMonitorSettings` stejný jako před testem (porovnání exportu klíče).
- Monitor: vypnout → ověřit → vrátit → ověřit stejnou topologii a že viditelná okna ostatních aplikací jsou zpět na původním monitoru (obdélník v toleranci).
- Zabitý podřízený proces uprostřed obou zásahů → návrat úklidem i hlídacím procesem (T-065).
- Oba zásahy mají trait `SystemChanges` a bez potvrzeného varování se přeskočí.

## Akceptační kritéria
- [x] `DisplayScaleChange`: nastavení 100/125/150 % jednoho monitoru za běhu, ověření přes `GetDpiForMonitor`, návrat včetně stavu `PerMonitorSettings`; nedostupná změna → přeskočeno s důvodem (samotest)
- [x] `MonitorDisconnectChange`: vypnutí neprimárního monitoru bez zápisu do databáze konfigurací, návrat a ověření topologie; záloha `SDC_TOPOLOGY_EXTEND`; okna ostatních aplikací jsou po návratu zpět na původním monitoru, test je nepřesouvá (samotest)
- [x] Návrat po zabitém procesu pro oba zásahy (samotest s T-065)
- [x] Výsledky běhu obsahují sestavení Windows, systémové DPI a DPI monitorů před a po (`system-state-start.txt` / `system-state-end.txt` každého běhu, ověřeno 2026-10-22; samotesty navíc zapisují zprávu před a po každém zásahu)
- [x] `dotnet build` a `dotnet test` bez chyb a varování; žádný nový NuGet balíček

## Otevřené otázky
- Q1, Q2 a Q3 rozhodnuty 2026-10-22, viz Rozhodnuto. Žádné otevřené otázky.

## Poznámky k implementaci
- **Kód** (vše v `tests/PrusaTrayDiagnostic.App.E2E.Tests`, žádný nový NuGet balíček, interop přes `[LibraryImport]`; vzor lihas/windows-DPI-scaling-sample jen jako zdroj informací):
  - `Infrastructure/DisplayConfig.cs`: CCD API (`QueryDisplayConfig`, `SetDisplayConfig`, názvy zdroje a cíle přes `DisplayConfigGetDeviceInfo`), nedokumentované pakety −3 (čtení min./aktuálního/max. kroku) a −4 (nastavení kroku), `GetDpiForMonitor(MDT_EFFECTIVE_DPI)`, `GetDpiForSystem`, `EnumDisplayMonitors`, číslo sestavení Windows (`CurrentBuild.UBR` + `DisplayVersion`) a `Report()` (sestavení, `systemDpi`, ke každému monitoru měřítko a `monitorDpi`). Monitor se pozná podle `monitorDevicePath` cíle (stálé, i u dvou stejných DELL U2719D); zdroj (`adapterId` + `sourceId`) se pro každé volání zjišťuje znovu.
  - `Infrastructure/DisplayScaleChange.cs`: `DisplayScaleKind` (druh `DisplayScale`, jeden záznam pro všechny monitory, aby se registr vracel jako celek). Stav = krok každého aktivního monitoru (+ procenta pro čtenáře) a strom `HKCU\Control Panel\Desktop\PerMonitorSettings` (`null` = klíč neexistoval). Shoda = kroky + textový export klíče (seřazený). Návrat: původní kroky → počkat, až je registr stabilní (Windows zapisuje `DpiValue` po změně) → plán návratu registru (přepsat změněné hodnoty, smazat hodnoty a podklíče, které předtím nebyly, chyběl-li klíč, smazat celý); jen uvnitř `PerMonitorSettings`. `DisplayScaleSteps`: kroky 100/125/150/175/200/225/250/300/… %, minimum je vždy 100 %, krok mimo rozsah monitoru → `null` (test se přeskočí); hodnoty, které tabulka neumí vyjádřit, se odmítnou (API se změnilo). `SetPercentAsync`: nedostupné měřítko → `Assert.Skip`; když se krok v API do 10 s neprojeví nebo `GetDpiForMonitor` do 10 s nevrátí očekávané DPI (96 × %/100), vrátí původní stav a test přeskočí s důvodem včetně sestavení Windows (rozhodnutí Q2).
  - `Infrastructure/MonitorDisconnectChange.cs`: `MonitorDisconnectKind` (druh `MonitorDisconnect`). Stav = aktivní cesty a režimy (base64 struktur) + čitelný popis monitorů (název, GDI název, rozlišení, poloha, otočení, primární). `WithoutMonitor` odebere cestu a jen režimy, na které už nic neodkazuje (indexy přečísluje); primární monitor odmítne. Použití: `SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES`, nikdy `SDC_SAVE_TO_DATABASE`; zásah odmítne topologii bez současného primárního monitoru. Návrat: uložené cesty a režimy → `SDC_TOPOLOGY_EXTEND` → `SDC_USE_DATABASE_CURRENT`; po každém pokusu do 10 s ověří počet, rozlišení, polohu, otočení a primární monitor. `RestoreOrder = -10`: úklid vrací vypnutý monitor dřív než měřítko (nové `ISystemChangeKind.RestoreOrder`, journal řadí podle něj a pak podle jména souboru). `SwitchOffAsync` zapíše viditelná, neminimalizovaná, neskrytá (cloaked) okna ostatních procesů se středem na vypínaném monitoru, vypne ho a do 10 s ověří, že ho `EnumDisplayMonitors` nevrací; bez druhého monitoru test přeskočí. `RestoreAndCheckWindowsAsync` vrátí topologii a do 10 s čeká, až jsou zapsaná okna zpět (tolerance 8 px na každé hraně; zavřená, skrytá a minimalizovaná okna se nekontrolují). Okna nikdy nepřesouvá; seznam nevrácených oken (jen název procesu a obdélník, žádné titulky – rozhodnutí 2026-10-22) jde do výsledků a test selže.
  - `SystemChanges` / `SystemChangeTraits`: trait `SystemChanges` má teď hodnotu podle druhu zásahu (`Theme`, `DoNotDisturb`, `TrayPromotion`, `DisplayScale`, `MonitorOff`, `Toasts`, `Credentials`, `TestProbe` pro kanár pojistky); test se dvěma druhy má dva traity. Každý druh zná svou hodnotu (`ISystemChangeKind.Trait`). Skript po odpočtu nastaví kromě `PTD_E2E_SYSTEM_CHANGES=1` i `PTD_E2E_SYSTEM_CHANGE_KINDS` se seznamem druhů z varování; zásah druhu, který varování neuvedlo, se odmítne (konstruktor `SystemChange<T>` i `ApplyStateAsync`) s hláškou, jaký trait test potřebuje. Fixture otevírá běhový zásah tray ikony jen s `TrayPromotion` a Nerušit na začátku a konci běhu čte (Win+N) jen s `DoNotDisturb` nebo `Toasts`.
  - `SystemStateSnapshot`: začátek a konec každého E2E běhu (i `-NoSystemChanges`) navíc obsahuje topologii, měřítka, export `PerMonitorSettings` a `DisplayConfig.Report()` (sestavení Windows, `systemDpi`, DPI monitorů); rozdíl topologie nebo měřítka na konci je chyba běhu. Čtení je jen čtení (CCD + paket −3), nečitelný stav se zapíše a neporovnává.
- **Skript:** výběr a texty varování jsou v novém modulu `tools/RunE2E.SystemChanges.psm1` (`Get-SelectedSystemChanges` z výstupu `-list full/json -trait 'SystemChanges=*'` s filtry `-Class`/`-Method`, `Get-SystemChangesWarningLines`, `Get-SystemChangeKindNames`). Varování vypíše jen druhy vybraných testů a ruční návrat jen k nim; odpočet 5 s zůstal. Neznámá hodnota traitu (překlep, nový druh bez textu) běh zastaví dřív, než se cokoli změní. Proměnná `PTD_E2E_SYSTEM_CHANGE_KINDS` se mezi zděděnými proměnnými odstraňuje stejně jako ostatní.
- **Testy (běží v každém `dotnet test`, nic v systému nemění):** `DisplayChangeKindTests` (kroky a procenta, plán návratu registru včetně „klíč chyběl“, text. podoba hodnot registru, shoda stavů, JSON záznamu, velikosti struktur interopu 72/64/20/84/420/32/24/104 B, topologie bez cesty včetně sdíleného režimu, rozdíly topologie, kontrola oken ostatních aplikací), `SystemChangeJournalTests` (pořadí návratu podle druhu, odmítnutí druhu, který varování neuvedlo, potvrzení druhu, každý trait testu je známý druh), `RunE2EScriptTests` (funkce modulu skriptu přes `pwsh` s vymyšlenými seznamy testů: jen druhy vybraných testů v pořadí varování, žádný test → žádné varování, neznámý druh → chyba, text varování jen pro vybrané druhy, názvy druhů skriptu = `SystemChangeTraits.All`, skript nastaví druhy až po odpočtu). E2E samotesty `DisplayChangeTests` (jen s potvrzeným varováním): měřítko primárního a druhého monitoru 100 → 125 → 150 → původní s `GetDpiForMonitor` a kontrolou, že `GetDpiForSystem` se nezměnilo, registr po návratu stejný jako před testem; vypnutí druhého monitoru, návrat, topologie a okna ostatních aplikací; zabitý podřízený proces s měřítkem i s vypnutým monitorem vrácený úklidem (vrstva 2) i jen hlídacím procesem (vrstva 4). Každý samotest zapíše do výsledků zprávu se sestavením Windows, `systemDpi` a DPI monitorů před a po.

- **Ověřeno 2026-10-22 (bez zásahů do systému):** `dotnet build` 0 varování, 0 chyb; `dotnet test` 4726 testů, 0 selhalo (53 přeskočeno: E2E bez `PTD_E2E` a dříve známé). `tools/run-e2e.ps1 -NoSystemChanges`: 164 testů, 16 přeskočeno (mezi nimi 7 nových `DisplayChangeTests`), 1 selhal: `LogAndSupportTests.SaveDiagnosticBundleThroughTheRealSaveDialog` (`COMException: Operation timed out` v UIA, známý nestabilní test mimo rozsah, viz Review T-065); samostatně prošel. „nothing pending“ před i po běhu, konec běhu „Differences from the start: none“. Čtení stavu obrazovky funguje i na tomto počítači (Windows 10.0.26300.9711, 26H2, `systemDpi=96`): oba DELL U2719D 100 % (doporučeno 100 %, rozsah 100–225 %, tedy 125 i 150 % jsou dostupné), `PerMonitorSettings` neexistuje (po návratu se proto musí celý smazat). Výběr varování na skutečném seznamu testů: bez filtru `Theme, DoNotDisturb, TrayPromotion, DisplayScale, MonitorOff, TestProbe`; `-Class '*DisplayChangeTests'` jen `DisplayScale, MonitorOff`; `-Class '*TrayMenuTests'` žádné varování.
- Postřeh k ověření při řízeném běhu: druhý monitor hlásí otočení 90/270° (`rotated`), ale plochu 2560×1440 na pozici 2560,0. Hodnoty se jen zapisují a porovnávají beze změny, návrat na ně nezávisí; při řízeném běhu zkontrolovat ve zprávě `monitor-off-report`.
- Záznamy T-065 v jeho poznámkách zmiňují trait `SystemChanges=true`; T-066 ho nahradil hodnotami podle druhu (T-068 má použít `Toasts`, T-073 `Credentials`).
- **Neověřeno (čeká na řízený běh se zásahy, spouští uživatel):** samotesty `DisplayChangeTests` (měřítko, vypnutý monitor, zabitý proces). Hlavní rizika: (1) nedokumentované pakety −3/−4 nemusí na sestavení 26300 fungovat → testy měřítka se přeskočí s důvodem (rozhodnutí Q2); (2) vrací Windows okna ostatních aplikací na druhý monitor (nastavení „Pamatovat umístění oken podle připojení monitoru“)? Když ne, samotest selže se seznamem oken; (3) proces hlídacího procesu spuštěný přes WMI musí smět volat `SetDisplayConfig` (stejná interaktivní relace ověřená v T-065); (4) zda Windows zapisuje `PerMonitorSettings` hned, nebo se zpožděním (návrat čeká na ustálení registru a opakuje se až 5×).

## Review
qa-reviewer, 2026-10-22 (kód bez zásahů do systému; vše níže opraveno ve stejné větvi, testy doplněné):
1. **(blokující) Titulky oken jiných aplikací ve výsledcích:** `OtherAppWindow` ukládal titulek okna a `MonitorDisconnectChanges.Describe` / `OtherAppWindows.NotReturned` ho psaly do výsledků (rozhodnutí 2026-10-22: jen název procesu a obdélník). Titulek odstraněn ze záznamu i z textu (test `WindowsOfOtherAppsCountAsBackWithinTheToleranceOrWhenClosed` ověřuje přesný text).
2. **(blokující, Q2 / Q3c) Nefunkční čtecí paket −3 test shodil místo přeskočení:** `DisplayScaleKind.ReadStateAsync` vyhodila obecnou výjimku už v `BeginAsync` (před zásahem). Teď `DisplayScaleUnavailableException` a `SystemChanges.DisplayScaleAsync` i samotest se zabitým procesem test přeskočí s důvodem (sestavení Windows); jiné chyby (např. nevrácený záznam pádu) dál selžou (test `UnreadableScaleSkipsTheTestInsteadOfFailingIt`).
3. **(střední, Q3c) Snímek stavu:** měřítko a topologie se četly v jednom `try`, takže nečitelné měřítko vyřadilo i porovnání topologie; chytala se jen `InvalidOperationException` a hlavička `DisplayConfig.Report()` byla mimo `try`. Teď se čtou zvlášť, chyba se jen zapíše (`SystemStateSnapshot.ReadDisplayAsync`, test `UnreadableDisplayStateIsOnlyNotedInTheSnapshot`) a `Report()` opravdu nikdy nevyhodí.
4. **(střední, bezpečnost) Pojistka primárního monitoru** kontrolovala čitelný seznam `Monitors` ve stavu, ne struktury, které se skutečně posílají do `SetDisplayConfig`. Teď se kontrolují dekódované cesty a režimy.
5. **(střední, bezpečnost) `SDC_SAVE_TO_DATABASE`:** konstanta byla definovaná (nepoužitá) a příznaky nešly otestovat. Konstanta odstraněna; testy `TopologyIsNeverSavedToTheDatabase` (žádný použitý příznak nemá 0x200) a `OnlyTheDisplayKindsCallTheSettersOfTheDisplayApi` (surové `DisplayConfig.Set/SetTopology/SetDpiScale` volají jen oba druhy zásahů, které mají pojistku a záznam).
6. **(střední, robustnost) Ustálení `PerMonitorSettings`:** `Wait.StableAsync` vracela hned při prvním dotazu (porovnává se s právě přečtenou hodnotou), takže „ustáleno“ znamenalo jen 1 s čekání. Teď dvě čtení 1 s od sebe se musí shodovat (nejvýš 10 s), teprve pak se počítá plán návratu.
7. **(nízká, úklid) `RunE2EScriptTests`:** při zrušení běhu zůstal proces `pwsh` běžet; teď se ukončí ve `finally`.

Nezměněno (drobnosti): `EnumDisplayMonitors` přes `[DllImport]` s delegátem (stejně jako dosavadní `Native.cs`); `DisplayScaleSteps.StepFor` alokuje `ToList()`; `Wait.UntilAsync` neruší čekání tokenem.

Ověřeno po opravách 2026-10-22: `dotnet build` 0 varování, 0 chyb; `dotnet test` 0 selhalo; `tools/run-e2e.ps1 -NoSystemChanges` 168 testů, 0 selhalo, 16 přeskočeno (7 `DisplayChangeTests`, ostatní zásahy, kanár, skutečná kamera, vstupy podřízeného procesu), „nothing pending“ před i po běhu, „Differences from the start: none“; snímek stavu obsahuje sestavení Windows, `systemDpi`, měřítko a `monitorDpi` obou monitorů a `PerMonitorSettings: (key missing)`.

## Ověření během se zásahy (Windows 11 26300, 2026-10-22)
- `tools/run-e2e.ps1 -Class '*DisplayChangeTests'` spuštěný ve viditelném okně na ploše uživatele (varování s odpočtem 5 s): 7 testů, 0 selhalo, 0 přeskočeno, „nothing pending“.
- Škálování: oba DELL U2719D 100 → 125 → 150 % (`monitorDpi` 96 → 120 → 144, `systemDpi` stále 96) a zpět na 100 %; `PerMonitorSettings` před i po během „(key missing)“.
- Vypnutí DISPLAY2: okna ostatních aplikací (3, jen proces a obdélník) se vrátila, „Windows of other apps not back on their monitor: none“; topologie obnovena včetně otočení.
- Zabité procesy: oba zásahy obnovil úklid při startu i samotný hlídací proces. Snímek stavu na konci: „Differences from the start: none“.
