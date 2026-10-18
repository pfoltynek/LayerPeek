---
id: T-031
title: Headless UI testy Avalonie (okno nastavení, tray menu)
status: done
owner: qa-reviewer
priority: P2
platform: all
depends_on: [T-015, T-028]
---

## Cíl
Automaticky testovat skutečné Avalonia UI (XAML, kompilované bindingy, ovládací prvky) bez zobrazení oken. Dnešní testy v `PrusaTrayDiagnostic.App.Tests` pokrývají view modely a prezentéry přes fakes, ale ne XAML okna nastavení ani napojení `NativeMenu`/`TrayIcon`.

## Rozsah
- Rozhodnuto uživatelem 2026-10-18: headless testy Avalonie (Avalonia.Headless) pro okno nastavení a tray menu.
- Testovací infrastruktura podle rozhodnutí Q1. Headless aplikace s `UseHeadless(...)` a stejným tématem jako aplikace (Fluent). Okna se v testu vytvoří přes stejný kód jako v aplikaci (`SettingsWindow` + `SettingsViewModel` s fake `ISettingsEditorStore`, `ISecretStore`, `ISettingsApplier`).
- **Okno nastavení (T-015, T-027, T-029):**
  - přidání a odebrání tiskárny;
  - vyplnění adresy, uživatelského jména a hesla psaním do `TextBox` (`KeyTextInput`);
  - heslo je maskované;
  - validační chyby se zobrazí u pole;
  - Uložit zavolá store se správnými hodnotami;
  - Zrušit nic neuloží;
  - přepnutí jazyka přepne texty v otevřeném okně;
  - zavření okna uvolní view model.
- **Tray menu (T-008, T-013, T-028):**
  - `AvaloniaNativeTrayMenu` nad skutečným `NativeMenu`: struktura položek pro jednu a pro víc tiskáren (podmenu);
  - texty po změně stavu a jazyka;
  - vyvolání `Command` položky „Otevřít PrusaLink v prohlížeči“ zavolá `PrinterWebOpener` s fake `IPrinterWebGateway`/`IUrlLauncher` (T-028);
  - `TrayIcon` lze v headless režimu vytvořit a přiřadit mu menu (ověřeno pokusem). Nativní vykreslení ikony a menu headless netestuje, to zůstává ručním ověřením.
- Kompilované bindingy zůstávají zapnuté (`AvaloniaUseCompiledBindingsByDefault`), testy nesmí potřebovat `x:CompileBindings="False"`.
- Testy po sobě uklidí okna, view modely a dočasné soubory, i při pádu. Nic se nezapisuje do skutečného úložiště tajných údajů ani do skutečného `settings.json`.

## Akceptační kritéria
- [x] Infrastruktura podle Q1 funguje s `dotnet test` (MTP), bez balíčků VSTest.
- [x] Testy okna nastavení podle Rozsahu (minimálně: přidání tiskárny, vyplnění polí psaním, validace, uložení, zrušení, přepnutí jazyka).
- [x] Testy tray menu podle Rozsahu (struktura pro 1 a N tiskáren, změna textů, příkaz „Otevřít v prohlížeči“).
- [x] Testy jsou deterministické (bez `Task.Delay` na čekání UI, jen `Dispatcher.UIThread.RunJobs()` / fake čas) a dají se spouštět paralelně s ostatními testovacími projekty.
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- **Q1 – Balíček a integrace s xUnit v3.** Ověřeno 2026-10-18 pokusnými projekty ve scratchpadu (Avalonia 12.1.4, SDK `11.0.100-rc.2.26504.105`):
  - `Avalonia.Headless.XUnit` **12.1.4** (nejnovější, `[AvaloniaFact]`, `[AvaloniaTestApplication]`) je sestavený proti `xunit.v3.extensibility.core` **3.2.2**. S `xunit.v3` **4.0.2**, kterou repozitář používá, padá už při objevování testů na `MissingMethodException` (`TestIntrospectionHelper.GetTestCaseDetails`). S `xunit.v3` 3.2.2 oba pokusné testy prošly (psaní do `TextBox`, vytvoření `TrayIcon` + `NativeMenu`). Dokumentace: https://docs.avaloniaui.net/docs/testing/headless-xunit. Avalonia 12 přešla na xUnit v3: https://v12.docs.avaloniaui.net/docs/avalonia12-breaking-changes.
  - `Avalonia.Headless` **12.1.4** bez integrace s xUnit: vlastní `HeadlessUnitTestSession.StartNew(typeof(TestApp))` a `session.Dispatch(...)` z běžného `[Fact]` funguje s `xunit.v3` 4.0.2. V pokusu ale `session.Dispose()` zavolané z testovacího vlákna **visel**. Bez `Dispose` (jedna relace na celé sestavení, ukončí se s procesem) test prošel.
  - Možnosti:
    - (a) `Avalonia.Headless.XUnit` 12.1.4 a pro celé řešení snížit `xunit.v3` na 3.2.2;
    - (b) `Avalonia.Headless.XUnit` v samostatném testovacím projektu s jinou verzí `xunit.v3`. Centrální správa balíčků to umožní jen přes `VersionOverride`, což porušuje pravidlo „verze jen v `Directory.Packages.props`“;
    - (c) `Avalonia.Headless` 12.1.4 s vlastní fixture (jedna relace na sestavení, přes `IAssemblyFixture` xUnit v3), bez snižování xUnit. Visící `Dispose` je nutné vyřešit nebo obejít;
    - (d) počkat na `Avalonia.Headless.XUnit` s podporou xUnit v3 4.x.
  - Oba balíčky jsou jen testovací, takže je požadavek na AOT netýká. Licence MIT.
  - **Doporučení architekta:** (c). Zachová xUnit 4.0.2 a pravidla centrální správy balíčků. Před implementací ověřit příčinu visícího `Dispose` (např. volat ho přes `Dispatch`, nebo nechat relaci žít do konce procesu).
  - **Rozhodnuto 2026-10-18:** (c) samotný **`Avalonia.Headless`** s vlastní `HeadlessUnitTestSession`, **jedna relace na testovací sestavení**, bez snižování `xunit.v3`. Před implementací nejdřív zjistit příčinu visícího `Dispose`.
- **Q2 – Kam testy umístit.** Do stávajícího `PrusaTrayDiagnostic.App.Tests` (sdílí fakes, `MenuFakes.cs`), nebo do nového `PrusaTrayDiagnostic.App.UiTests` (izolace headless platformy od ostatních testů)?
  - **Doporučení architekta:** nový projekt `tests/PrusaTrayDiagnostic.App.UiTests`. Sdílené fakes přesunout do `tests/Shared`.
  - **Rozhodnuto 2026-10-18:** nový projekt **`tests/PrusaTrayDiagnostic.App.UiTests`**.

- **Q3 (neblokující, vzniklo při implementaci 2026-10-18) – Vizuální (snímkové) testy.** Headless kreslení je teď vypnuté (`UseHeadlessDrawing = true`, bez Skia), testuje se strom ovládacích prvků, ne vzhled. Chceme někdy porovnávat vykreslené snímky (Skia + `CaptureRenderedFrame`, referenční PNG v repozitáři)? Je to rozhodnutí o rozsahu a údržbě, zatím nic neimplementováno.
  - **Rozhodnuto 2026-10-18:** ano, jako součást T-031: vykreslení přes Skia + `CaptureRenderedFrame`, referenční PNG v repozitáři, porovnání s tolerancí, deterministicky (pevná velikost okna, pevná kultura, písmo přibalené k testům, DPI 1,0, bez kurzoru, animací a fokusu). Pokrýt okno nastavení (PrusaLink s uživatelským jménem, Prusa Connect se zakázanými poli, viditelné validační chyby) a jednu variantu v češtině. Přegenerování přes `PTD_UPDATE_SNAPSHOTS=1`, při neshodě actual + diff PNG do vyhrazené dočasné složky. Hotovo, viz Poznámky k implementaci.
- **Q4 (neblokující) – Zámky testovacích běhů napříč worktree.** `TestRunLock` používá název podle sestavení v `%TEMP%`, takže souběžné běhy ze dvou worktree (např. dva agenti) na sebe čekají až 5 min a pak selžou (pozorováno u `Core.Tests`). Nechat (bezpečné, jen pomalé), nebo zámek a úklid omezit na worktree?
  - **Rozhodnuto 2026-10-18:** zámek `TestRunLock` a vyhrazené složky omezit na worktree – samostatný úkol **T-032** (založí koordinátor), v T-031 se nemění.
- **Q5 (neblokující, vzniklo při implementaci snímků) – Reference na macOS.** Referenční PNG jsou vygenerované na Windows. Skia na macOS rasterizuje písmo jinak (jiný scaler), takže snímky tam pravděpodobně neprojdou v toleranci. Mít oddělené reference podle OS (např. `Snapshots/osx/`), snímkové testy na macOS přeskočit, nebo toleranci zvýšit? Rozhodne uživatel, až se bude pracovat na Macu.

## Poznámky k implementaci
### Příčina visícího `session.Dispose()` (ověřeno pokusem a výpisem zásobníků přes `dotnet-stack`)
`HeadlessUnitTestSession` (Avalonia.Headless 12.1.4) dokončuje úlohu vrácenou z `Dispatch(...)` přes `TaskCompletionSource` **bez** `RunContinuationsAsynchronously`, a to ze svého vlastního vlákna (smyčka nad `BlockingCollection`). Pokračování testu za `await session.Dispatch(...)` proto běželo **přímo na vlákně relace**, uvnitř její smyčky. Synchronní `Dispose()` pak zruší smyčku a čeká `_dispatchTask.Wait()` na dokončení vlákna, na kterém samo běží – zablokuje samo sebe. Bez okna (bez `await`) se to neprojevilo, proto test bez `Dispose` prošel. `ConfigureAwait(ConfigureAwaitOptions.ForceYielding)` nepomůže (týká se jen už dokončené úlohy), pomůže až vlastní `TaskCompletionSource` s `RunContinuationsAsynchronously`.

### Řešení
- Nový projekt `tests/PrusaTrayDiagnostic.App.UiTests` (ve `PrusaTrayDiagnostic.slnx`), balíček `Avalonia.Headless` 12.1.4 (verze jen v `Directory.Packages.props`, stejná jako Avalonia; MIT, jen testovací, AOT se netýká). `dotnet list package --include-transitive`: žádný VSTest balíček, běží na Microsoft.Testing.Platform 2.5.1 s `xunit.v3` 4.0.2.
- `Infrastructure/HeadlessSession.cs` – assembly fixture xUnit v3 (`[assembly: AssemblyFixture]`), **jedna relace na sestavení**, izolace `PerTest` (každý test dostane novou instanci aplikace). Vstupní bod `HeadlessTestApp` používá **skutečnou třídu `App`** (App.axaml: Fluent téma, `TrayIcon` s prázdným `NativeMenu`) na headless platformě s vykreslováním přes **Skia** (`UseSkia()`, `UseHeadlessDrawing = false`) – kvůli snímkovým testům (Q3); jediné písmo je přibalený Inter, viz níže. Headless nemá desktop lifetime, takže `App` nespustí `TrayHost` – nesáhne na `settings.json`, úložiště tajných údajů, síť ani proxy.
- `RunAsync(body)` spustí tělo testu na UI vlákně relace s tokenem = xUnit token + časový limit 30 s; test pak vždy pokračuje **mimo** vlákno relace (vlastní TCS s `RunContinuationsAsynchronously`). Token se záměrně nepředává do `Dispatch`: při jeho zrušení by Avalonia opustila frame a zablokovala UI vlákno na `GetAwaiter().GetResult()` nedokončeného těla. Ukončení relace: `DisposeAsync` z vlákna xUnit s omezeným čekáním 10 s (když by nějaké tělo po timeoutu dál viselo, ohlásí se chyba úklidu, běh nevisí).
- Každé tělo začíná se systémovým jazykem en-US a `Strings.Culture = null`; po testu se jazyk vrátí. `UiTestScope` zavře zaregistrovaná okna a uvolní objekty i při selhání testu. Testy uvnitř sestavení běží sériově (`[assembly: Parallelization(Mode = ParallelMode.None)]`) – UI vlákno je stejně jedno a jazyk textů je globální; ostatní testovací projekty běží paralelně.
- `Infrastructure/Ui.cs` – hledání ovládacích prvků podle `AutomationProperties.AutomationId`, klik myší na střed prvku (po `BringIntoView`), psaní přes `KeyTextInput`, Ctrl+A / Backspace přes `KeyPressQwerty`, `Settle()` = `Dispatcher.UIThread.RunJobs()` + layout + `ForceRenderTimerTick()`. Žádné `Task.Delay`; na asynchronní práci se čeká přes `ExecutionTask` příkazů nebo dokončení TCS.
- Sdílené fakes se nekopírují: `SettingsFakes.cs`, `TrayFakes.cs`, `MenuFakes.cs`, `FakeWebGateway.cs` z `App.Tests` jsou do UiTests nalinkované (jako T-030 linkuje `FakePrusaLinkServer`). `FakeUrlLauncher.WaitForOpenedAsync` čeká nově na TCS místo dotazování s `Task.Delay(5)` (stejná sémantika, limit 10 s; App.Tests dál procházejí).
- Testy nepoužívají žádné soubory kromě čtení referenčních PNG (jen paměťové fakes); zapisuje se jen do vyhrazené složky snímkových chyb (viz níže), kterou uklízí další běh. Ověřeno, že běh nic nevytvoří v `%TEMP%` ani nezmění skutečný `settings.json`.

### Testy (41)
- Snímky okna nastavení (4): viz „Snímkové testy“ níže.
- Porovnání snímků (3, doplněno při review): stejné okno se vykreslí bit po bitu stejně; změna jedné číslice v šedém zástupném textu i posun o 1 px překročí toleranci.
- Infrastruktura (11): tělo běží na UI vlákně se skutečnou `App` a Fluent tématem, okna jsou headless (implementace z Avalonia.Headless), `TrayIcon` z App.axaml s menu, výchozí jazyk, chyba těla se propíše a relace funguje dál, test pokračuje mimo vlákno relace, okno se zavře i při selhání, selhání úklidu se ohlásí a nepřekryje chybu těla, tělo reagující na token skončí `TimeoutException` a relace jede dál, tělo blokující UI vlákno po timeoutu způsobí rychlé selhání dalších testů s názvem viníka (doplněno při review).
- Okno nastavení (12): přidání tiskárny (prázdný formulář, výchozí uživatel „maker“, nápovědy, chyby až po pokusu o uložení), psaní všech polí a uložení přes skutečný `SettingsEditorStore` nad paměťovým úložištěm nastavení a fake `ISecretStore` (heslo jen v úložišti tajných údajů, okno se zavře, VM uvolněn), nezměněné jméno se uloží jako „maker“, validace u pole (prázdné jméno uživatele, dvojtečka, chybějící adresa, stavový text, nic se neuloží, oprava chybu hned skryje), Prusa Connect zakáže jméno i heslo, heslo maskované (`PasswordChar`, `TextPresenter`) a uložené heslo se do pole nikdy nenačte, Zrušit nic neuloží, zavření okna uvolní VM a zruší běžící test spojení, odebrání tiskárny (klik v seznamu), test spojení (text „Probíhá…“, předané hodnoty, výsledek s třídou `success`), test spojení s neplatným jménem nic neodešle.
- Přepnutí jazyka (3): cs a de přeloží titulek, tlačítka, popisek, nápovědu, inline chybu i stavový text otevřeného okna (stejně jako `App.OnLanguageChanged`: `UiLanguage.Apply` + `AuxiliaryWindowSlot.RefreshTexts`), úpravy zůstanou; návrat na jazyk systému vrátí angličtinu.
- Tray menu (8) nad skutečným `NativeMenu` z `TrayIcon` aplikace, zapojené jako v `TrayHost` (VM → presenter → `TrayMenuTarget` → `AvaloniaNativeTrayMenu`, `AvaloniaUiDispatcher`): úvodní struktura, jedna tiskárna (prostá položka, `_` zdvojené, text podle stavu, aktualizace na místě), víc tiskáren (podmenu, návrat na jednu), klik na tiskárnu → `PrinterWebOpener` s fake gateway pro správnou tiskárnu → proxy adresa jen přes `OpenSecretAsync`, „Otevřít PrusaLink“ v podmenu → správná tiskárna, „Diagnostika…“ v podmenu předvybere tiskárnu, pevné položky volají příkazy aplikace a zakázaná položka nic, přepnutí jazyka přeloží celé menu, po `Dispose` presenteru klik nic nedělá. Klik se vyvolá přes `INativeMenuItemExporterEventsImplBridge.RaiseClicked()` – stejnou cestu jako platformní exportér menu (Avalonia ji značí `[Unstable]`). Nativní vykreslení ikony a menu zůstává ručním ověřením.

### Snímkové testy (Q3)
- **Volba relace:** jedna relace na sestavení zůstává; celá relace se přepnula na Skia místo headless kreslení. Druhý projekt ani druhá relace nejsou potřeba, ostatní testy na Skia procházejí beze změny, cena je asi +3 s na běh. `Avalonia.Skia` 12.1.4 a `Avalonia.Fonts.Inter` 12.1.4 jsou v `Directory.Packages.props` jako testovací balíčky (MIT, písmo Inter pod SIL OFL 1.1). Aplikace samotná je nereferencuje přímo (Skia už má tranzitivně přes `Avalonia.Desktop`, Inter jen testy), AOT se jich netýká. Bez balíčků VSTest (ověřeno `dotnet list package --include-transitive`).
- **Písmo:** Avalonia.Skia v headless režimu bere výchozí písmo ze systému (na Windows Segoe UI), proto testy registrují `WithInterFont()` a `FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter", FontFallbacks = [] }`. Znak, který Inter nemá, by Avalonia stejně vykreslila systémovým písmem – `SnapshotAssert` proto před každým snímkem ověří, že všechny zobrazené znaky (texty, zástupné texty, znak hesla `●`) Inter obsahuje, jinak test selže. Aplikace v produkci dál používá systémové písmo; snímky tedy kontrolují rozvržení, texty a stavy, ne přesné písmo Windows/macOS.
- **Determinismus:** okno má velikost z XAML (780 × 680), render scaling 1,0 (`SetRenderScaling(1.0)`), kultura en-US (případně přepnutá cs), hodnoty se nastavují přes view model (bez psaní a klikání – žádný kurzor v `TextBox`, žádné najetí myší), fokus se před snímkem zruší. Snímek se pořídí dvakrát s 10 tiky vykreslovacího časovače mezi nimi a musí být bit po bitu stejný, jinak test hlásí běžící animaci nebo kurzor.
- **Porovnání:** pixel se liší, když se některý kanál liší o víc než 16; shoda, když se liší nejvýš 10 pixelů (zpřísněno při review z původních 0,1 % = 530 pixelů, viz Review). Při neshodě se zapíše `<název>.actual.png` a `<název>.diff.png` (rozdíly červeně) do `%TEMP%\PrusaTrayDiagnostic.App.UiTests.Snapshots\` a cesta je ve zprávě testu. Složku smaže začátek dalšího běhu (pod zámkem `TestRunLock` tohoto sestavení); po úspěšném běhu nezůstane nic. Ověřeno záměrnou záměnou reference (test selhal se správnou zprávou a obrázky) a pak 5× zelený běh bez pozůstatků.
- **Pokryté snímky** (`tests/PrusaTrayDiagnostic.App.UiTests/Snapshots/`): `settings-prusalink.png` (tiskárna PrusaLink s uživatelem „operator“ a uloženým heslem, v seznamu i tiskárna Prusa Connect), `settings-prusaconnect.png` (Prusa Connect se zakázaným jménem, heslem a testem spojení), `settings-validation.png` (nová tiskárna bez adresy a s dvojtečkou ve jménu po pokusu o uložení, chyby u polí + stavový text), `settings-validation-cs.png` (totéž po živém přepnutí do češtiny).
- **Jak aktualizovat reference** (po záměrné změně vzhledu nebo textů):
  ```powershell
  $env:PTD_UPDATE_SNAPSHOTS = '1'; dotnet test --project tests/PrusaTrayDiagnostic.App.UiTests; Remove-Item Env:PTD_UPDATE_SNAPSHOTS
  ```
  (bash: `PTD_UPDATE_SNAPSHOTS=1 dotnet test --project tests/PrusaTrayDiagnostic.App.UiTests`). V tomto režimu se PNG přepíšou místo porovnání a testy projdou. Nové obrázky pak prohlédnout (`git diff` / náhled) a commitnout spolu se změnou. Chybějící reference v normálním běhu je chyba s návodem ve zprávě.

### Změny produkčního kódu (malé)
- `SettingsWindow.axaml`: 22× `AutomationProperties.AutomationId` (jen atributy, bez změny chování; zároveň usnadní přístupnost / UI Automation).
- `PrusaTrayDiagnostic.App.csproj`: `InternalsVisibleTo` pro `PrusaTrayDiagnostic.App.UiTests`.

### Ověření (Windows, SDK 11.0.100-rc.2.26504.105)
- `dotnet build --no-incremental`: 0 varování, 0 chyb.
- UiTests 5× za sebou (bez Skia, 30 testů): 30/30, ~3,1–3,2 s na běh.
- UiTests 5× za sebou po přidání snímkových testů (Skia, 34 testů): 34/34, ~6,3 s na běh (v rámci celého řešení ~14 s, souběžně s ostatními projekty).
- Celé řešení po přidání snímků: 2081 testů zelených (~16 s), `dotnet build --no-incremental` 0 varování. Dříve (bez snímků): 2077 testů, dva po sobě jdoucí běhy zelené (~8 s). V prvním běhu `Core.Tests` nespustily nic a po ~4,5 min selhaly – zřejmě čekaly na sdílený zámek `%TEMP%\PrusaTrayDiagnostic.Core.Tests.lock`, který drží souběžný běh z jiného worktree; nesouvisí se změnou (Core.Tests beze změny).
- `dotnet publish src/PrusaTrayDiagnostic.App -c Release -r win-x64`: 0 varování trim/AOT; `scripts/aot-smoke.ps1` PASSED (po změně XAML).

## Review
QA review 2026-10-18 (qa-reviewer), worktree `PTD-T030`, necommitováno.

**Ověřeno:** analýza visícího `Dispose` odpovídá dekompilovanému `HeadlessUnitTestSession` 12.1.4 (TCS bez `RunContinuationsAsynchronously`, `Dispose` čeká `_dispatchTask.Wait()`); `DisposeAsync` relace je skutečně asynchronní, takže omezené čekání 10 s funguje. Klik v tray menu přes `RaiseClicked()` vyvolá `NativeMenuItem.Click`, na který je napojený `AvaloniaNativeTrayMenu` – realistické. `[Unstable]` API jen tato jedna (zdůvodněno). Okna jsou headless, `%APPDATA%\PrusaTrayDiagnostic\settings.json` beze změny, přímé spuštění testů nic nenechá v `%TEMP%`. PNG 33–49 kB, `.gitattributes` `*.png binary`. Žádné VSTest balíčky, verze jen v `Directory.Packages.props`, nalinkované fakes se překládají v obou projektech.

**Opraveno při review:**
- (střední) Tolerance snímků byla příliš volná: 0,1 % = 530 pixelů. Záměrná změna `PlaceholderText` z `192.168.1.20` na `192.168.1.29` dala jen 21 rozdílných pixelů a všechny 4 snímky **prošly**; posun okraje o 1 px dal 924–1241 pixelů (jen 2× nad limitem). Na tomto stroji je vykreslení mezi běhy bit po bitu stejné (ověřeno s tolerancí 0). Limit zpřísněn na `MaxDifferentPixels = 10` (kanálová tolerance 16 zůstává) a přidány testy `SnapshotAssertTests` (shoda, změna jedné číslice, posun o 1 px), které s původním limitem selžou. Hodnotu může uživatel upravit, pokud se na jiném stroji ukáže šum.
- (nízká) Výjimka z úklidu (`UiTestScope.Dispose`) ve `finally` překryla původní chybu těla testu. Nově se při selhání těla i úklidu ohlásí obě (`AggregateException`), při selhání jen úklidu selže test s chybou úklidu. Testy `FailingCleanupAfterAFailingBodyReportsBothFailures`, `FailingCleanupAfterAPassingBodyFailsTheTest`.

**Druhé kolo review (2026-10-18), opraveno:**
- Testy tolerance snímků teď používají stejný verdikt jako `Matches` (`SnapshotAssert.IsWithinTolerance`), ne vlastní porovnání s konstantou; srovnávání pixelů je stejné (`CountDifferentPixels` s kanálovou tolerancí). Obě okna jsou ve stejném testu, se stejnou velikostí a bez fokusu – deterministické.
- Tělo, které po vypršení limitu (nebo zrušení) dál blokuje UI vlákno, si relace zapamatuje; další testy po krátkém čekání (250 ms) hned selžou zprávou „UI thread still blocked by <test>“ místo 30 s čekání každý. Hláška při ukončení relace také jmenuje viníka. Test `BodyStillBlockingTheUiThreadAfterItsTimeoutMakesLaterTestsFailFast` (bez opravy selže až po 30 s) a `BodyThatObservesItsTokenTimesOutAndTheSessionKeepsWorking`; limit těla je pro tyto testy krátký (interní přetížení `RunAsync(body, timeout)`), sada se prodloužila o ~1 s.
- Časový limit se nově pozná podle zrušeného tokenu (ne podle stavu úlohy), takže tělo, které na token zareaguje, vždy skončí `TimeoutException`; vlastní `OperationCanceledException` těla bez zrušení tokenu se už nevydává za timeout. Zrušení testovacího běhu (xUnit token) se dál propíše jako zrušení.
- `FakeUrlLauncher.WaitForOpenedAsync`: čekatel se po vypršení limitu nebo zrušení odebere ze seznamu.

**Zůstává otevřené:** Q5 (reference na macOS) rozhodne uživatel při práci na Macu; souvisí s tím i to, že Skia na Windows rasterizuje písmo přes DirectWrite, takže reference nemusí sedět na jiné verzi Windows.

**Výsledky (po druhém kole):** `dotnet build --no-incremental` 0 varování / 0 chyb; UiTests 5× 41/41 (~7,4 s); celé řešení 2088 testů, 0 selhání (1 přeskočený, existující `RecordedFixturesTests`); `dotnet publish src/PrusaTrayDiagnostic.App -c Release -r win-x64` 0 varování trim/AOT (produkční kód se ve druhém kole neměnil). Žádné nálezy nezůstávají.
