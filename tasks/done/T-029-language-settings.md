---
id: T-029
title: Jazyk aplikace a další překlady
status: done
owner: ui-dev
priority: P2
platform: all
depends_on: [T-015]
---

## Cíl
Uživatel si v nastavení zvolí jazyk aplikace nezávisle na jazyku systému (např. systém anglicky, aplikace česky). Aplikace dostane kromě angličtiny a češtiny i další běžné jazyky.

## Rozsah
- Nové nastavení „Jazyk“ v záložce **Obecné** okna nastavení (T-015):
  - volba **Podle systému** (výchozí) + konkrétní jazyky,
  - jazyky: angličtina (`en`, neutrální `Strings.resx`), čeština (`cs`), slovenština (`sk`), němčina (`de`), španělština (`es`), francouzština (`fr`), italština (`it`), polština (`pl`) – stejná sada jako firmware/PrusaSlicer od Prusy + slovenština,
  - názvy jazyků ve výběru vždy ve vlastním jazyce (Čeština, Deutsch, Español…), aby je uživatel našel i v neznámém jazyce.
- Uložení do `settings.json` (Core: `AppSettings`, `SettingsDocument`, `SettingsMapper`) – kód jazyka (např. `"language": "de"`), chybějící nebo neznámá hodnota = Podle systému. Starší soubory bez hodnoty fungují beze změny.
- **Přepnutí za běhu, bez restartu:** po uložení nastavení se okamžitě přepne tray menu, tooltip ikony, otevřená pomocná okna (nastavení, diagnostika…) a nově vytvářené texty (notifikace).
- Mění se jen **jazyk textů** (`CurrentUICulture` / `Strings.Culture`). Formát čísel, dat a času (`CurrentCulture`) zůstává podle regionálního nastavení systému.
- „Podle systému“: použije se jazyk systému, pokud je podporovaný (včetně regionálních variant, např. `de-AT` → `de`), jinak angličtina – stejně jako dnes.
- Nové soubory `Strings.{sk,de,es,fr,it,pl}.resx` se všemi texty z `Strings.resx`; satelitní sestavení musí fungovat i v Native AOT publish.
- Test, který hlídá, že každý jazykový `.resx` obsahuje přesně stejné klíče jako `Strings.resx` a stejné zástupné symboly (`{0}`, `{1}`…) ve formátovacích řetězcích.

## Akceptační kritéria
- [x] V záložce Obecné je výběr jazyka (Podle systému + 8 jazyků), hodnota se ukládá a po restartu načte (testy view modelu, mapperu a úložiště; při startu se jazyk čte z uloženého nastavení)
- [x] Neznámá/poškozená hodnota v `settings.json` = Podle systému (s varováním při načtení jako u ostatních opravených hodnot)
- [x] Po uložení se jazyk přepne bez restartu: tray menu, tooltip, otevřená okna; formáty čísel a dat se nemění (testy view modelů a presenteru; otevřená okna ověřena dočasným harnessem mimo repozitář – skutečné tray menu ručně ověřil uživatel 2026-10-18)
- [x] Všech 6 nových jazyků má kompletní překlad; test kontroluje shodu klíčů a zástupných symbolů se `Strings.resx`
- [x] Jazyk se při startu aplikuje dřív, než se vytvoří první text (tray menu, tooltip)
- [x] Native AOT publish (win-x64) obsahuje všechny jazyky – publish prošel, všech 7 překladů je zabudovaných v exe a publikovaná aplikace běží; zobrazení jiného jazyka v AOT buildu ručně neověřeno, uživatel kritérium uznal (2026-10-18)
- [x] Ověřeno na Windows – **ručně ověřeno uživatelem (2026-10-18)** (přepnutí jazyka za běhu, Debug build)
- [x] `dotnet build` a `dotnet test` bez chyb a varování (0 varování, 1635 testů: 1634 prošlo, 1 přeskočený existující)

## Otevřené otázky
- (žádné)

**Rozhodnutí uživatele k otázkám z implementace a review (2026-10-18)** – vše ponecháno tak, jak je implementováno:
1. ~~**Umístění výběru jazyka**~~ (záložka Obecné, hned pod intervalem dotazování, před upozorněními.)
2. ~~**Pořadí jazyků**~~ („Podle systému“ první, pak abecedně podle vlastního názvu.)
3. ~~**Anglický text volby „Podle systému“**~~ („System default“, de „Systemsprache“, fr „Langue du système“…)
4. ~~**Nápověda pod výběrem**~~ (ponechat.)
5. ~~**Kontrola překladů**~~ (přijmout tak, jak jsou; revize rodilým mluvčím případně později.)
6. ~~**Hodnota jiného typu** (např. `"language": 5`)~~ (ponechat konzistentně s ostatními poli: chyba JSON = poškozený soubor.)

**Rozhodnutí uživatele (2026-10-18):**
- Jazyky: sada jako Prusa – en, cs, de, es, fr, it, pl + sk.
- Změna jazyka se projeví hned, bez restartu.
- Formáty čísel/dat zůstávají podle systému, mění se jen texty.
- Implementace ve vlastním worktree z `develop`; texty kamery z T-014 (rozpracovaný v jiné větvi) se do nových jazyků přeloží při jeho merge, resp. v T-014.

## Poznámky k implementaci
- **Core:** `AppSettings.Language` (`string?`, `null` = podle systému; jiná hodnota než kód z `Core/Settings/AppLanguages.Supported` vyhodí `ArgumentException`, porovnává se v `Equals`). `AppLanguages.TryNormalize` přijme kód bez ohledu na velikost písmen a mezery na okrajích, regionální variantu (`de-AT`) ne. `SettingsDocument.Language` → `"language": "de"`; při „Podle systému“ se hodnota nezapisuje (`JsonIgnore WhenWritingNull`), chybějící i `null` = podle systému bez varování. Neznámá hodnota (např. `"ja"`, `"de-AT"`, `""`) = podle systému s varováním „Unknown language '…'“ (zkrácené jako ostatní). Core zůstává bez UI, JSON jen přes source-gen kontext.
- **Přepnutí za běhu – návrh:**
  - Mění se jen `Strings.Culture` (statická vlastnost generované třídy, kterou čte každý text na libovolném vlákně). `CurrentCulture`/`CurrentUICulture` se nemění, takže čísla, data a časy zůstávají podle systému. „Podle systému“ = `Strings.Culture = null`, tedy jako dřív UI jazyk systému včetně regionálních variant a angličtiny pro nepřeložené jazyky. `CurrentUICulture` jsem záměrně nenastavoval: v .NET se šíří přes `ExecutionContext` (AsyncLocal), nastavení v async kódu by se neprojevilo všude.
  - `App/Localization/UiLanguage` (singleton, UI vlákno) přepne `Strings.Culture`, obnoví XAML texty a vyvolá `Changed`. Za běhu ho volá `UiLanguageObserver` – první `IMonitoringObserver` v `MonitoringSession`, takže dostane uložené nastavení při restartu monitoringu (`ApplySettings`) i při změně jen jazyka/upozornění (`UpdateSettings`; monitoring se nerestartuje, ikona neukáže „Připojování…“). Přepnutí se pošle na UI vlákno, rychle po sobě jdoucí nastavení se sloučí.
  - **XAML:** `{x:Static res:Strings.X}` se vyhodnotí jen jednou při vytvoření okna, proto jsou všechny texty v XAML vázané jako `{Binding [X], Source={x:Static loc:LocalizedStrings.Instance}}`. `LocalizedStrings` má indexer nad `Strings.ResourceManager` a při přepnutí vyvolá změnu indexeru („Item“ i „Item[]“). Je to kompilovaný binding (bez reflexe, Native AOT); Avalonia odebírá změny slabě – ověřeno, že i po vytvoření 12 oken má `LocalizedStrings` jediného přímého odběratele, zavřená okna tedy nedrží. Neznámý klíč vrací sám sebe (viditelný překlep); `XamlLocalizationTests` hlídá, že každý klíč v `.axaml` existuje a že v XAML nezůstal žádný `x:Static …Strings.`.
  - **Texty z view modelů:** rozhraní `ILanguageAware.RefreshTexts()` (UI vlákno) – view model si pamatuje, z čeho text vznikl (typ upozornění, stav, výsledek testu, načtené informace…), a zformátuje ho znovu; vstupy, výběr ani běžící test se nemění, nic se znovu nenačítá. Implementují ho `TrayStatusViewModel` (tooltip aktuálního stavu; souběžně formátovaný starý text zahodí kontrola verze), `TrayMenuViewModel` (pevné položky, položky tiskáren i uložená podmenu; nová událost `ITrayMenuSource.FixedItemsChanged` → `TrayMenuPresenter` postaví menu jednou celé znovu), `DiagnosticsViewModel`, `SettingsViewModel` (+ `PrinterEditorViewModel`: chyby validace, výsledek testu, nápovědy hesla, stav uložení, upozornění), `AboutViewModel`. Položky výběrů (`SoundOption`, `ConnectionTypeOption`, `ChartWindowOption`, nový `LanguageOption`) jsou místo recordů třídy odvozené z `LocalizedOption` – instance zůstává (výběr se zachová), mění se jen `Text`. `ChartWindowOption.Label` se přejmenovalo na `Text`.
  - **Rozvod:** `TrayHost` odebírá `UiLanguage.Changed`, obnoví tray view modely a vyvolá `TrayHost.LanguageChanged`; `App` pak zavolá `AuxiliaryWindowSlot.RefreshTexts()` u oken nastavení, diagnostiky a O aplikaci (`IAuxiliaryWindow.RefreshTexts` → `DataContext as ILanguageAware`). Po `StopAsync` se nic neobnovuje.
- **Start:** `TrayHost.Start` → `TrayHost.StartAsync`: nejdřív načte nastavení mimo UI vlákno (`MonitoringSession.LoadSettingsAsync`, stejné logování a výchozí hodnoty jako dřív), aplikuje jazyk a teprve pak vytvoří tray view modely, ikonu a menu; `MonitoringSession.Start(settings)` už nastavení podruhé nenačítá. Během načítání (milisekundy) je v trayi statická ikona z `App.axaml` s prázdným menu. `App.StartHost` je `async void`, aby chyba startu shodila aplikaci jako dřív; když systém ukončí relaci během načítání, host se hned zastaví. Pokračování po `await` běží na UI vlákně (Avalonia instaluje `AvaloniaSynchronizationContext` v `Application.RegisterServices`).
- **Okno nastavení:** záložka Obecné má pod intervalem dotazování výběr „Jazyk“ (Podle systému + 8 jazyků vlastním názvem) s nápovědou. Změna jazyka je změna nastavení (dirty tracking), po uložení se přepne hned. Nové texty `SettingsLanguage`, `SettingsLanguageSystem`, `SettingsLanguageHint` (en + cs).
- **Překlady:** nové `Strings.{sk,de,es,fr,it,pl}.resx` se všemi 157 klíči. Zachované zástupné symboly, výpustka „…“ u položek otevírajících okno, typografické uvozovky podle jazyka (sk/de „…“, pl „…”, fr/es « … », it “…”), ve francouzštině nezlomitelná mezera před dvojtečkou a středníkem, nezlomitelná mezera před % tam, kde to jazyk vyžaduje (sk, de, fr, es; it/pl bez mezery). Terminologie podle Prusa (Heizbett, Düse, Bauteillüfter; plateau chauffant, buse; base calefactable, boquilla, ventilador de capa; piano riscaldato, ugello; stół grzewczy, dysza; podložka, tryska). Texty kamery z T-014 v těchto souborech nejsou – doplní se při merge T-014 (test shody klíčů to pohlídá).
- **Satelitní sestavení / Native AOT:** v csproj se nic měnit nemuselo (`SatelliteResourceLanguages` není nastavené, sestaví se všechny jazyky; `NeutralLanguage` = en). Debug build má složky `cs, de, es, fr, it, pl, sk`. `dotnet publish -c Release -r win-x64` prošel (MSVC je k dispozici; hlášky ILC o Avalonia DComposition jsou starší a nesouvisí), satelity jsou zabudované přímo v `PrusaTrayDiagnostic.exe` (binárka obsahuje `Strings.{cs,sk,de,es,fr,it,pl}.resources` i přeložené texty), publikovaná aplikace běžela 20 s bez pádu. Že AOT build skutečně zobrazí jiný jazyk, jsem neověřil (nechtěl jsem měnit uživatelův `settings.json`).
- **Testy (nové/změněné):**
  - Core: `AppSettingsTests` (výchozí = systém, platné/neplatné kódy, rovnost, normalizace), `SettingsMapperTests` (round-trip všech 8 kódů jako `"language"`, „Podle systému“ se nezapisuje, chybějící/`null`, velikost písmen, neznámá hodnota s varováním, zkrácení dlouhé hodnoty), `JsonSettingsStoreTests` (round-trip s jazykem, neznámý jazyk s varováním, zápis kódu).
  - App: `Localization/StringsTests` (každý překlad má přesně klíče a zástupné symboly `Strings.resx` – i čeština, formátovací řetězce jsou platné, každý podporovaný jazyk kromě en má překlad, regionální varianty → jazyk, indexer `LocalizedStrings`), `UiLanguageTests` (přepnutí, idempotence, návrat na systém, neznámý kód, pozorovatel na UI vlákně a slučování), `GlobalLanguageSwitchTests` (skutečné `Strings.Culture` pro všech 8 jazyků i na jiném vlákně, `CurrentCulture` se nemění, „Podle systému“ sleduje UI kulturu, změna indexeru pro XAML; běží samostatně bez paralelizace a vždy vrátí jazyk na systém), `XamlLocalizationTests`, `Settings/SettingsLanguageTests` (výběr, načtení, uložení, dirty, `RefreshTexts` všech textů se zachováním úprav), testy `RefreshTexts` v `TrayStatusViewModelTests`, `TrayMenuViewModelTests`, `TrayMenuPresenterTests`, `DiagnosticsViewModelTests` (jazyk se mění, formát čísel ne, žádný nový dotaz), `AboutViewModelTests`, `AuxiliaryWindowSlotTests`, `MonitoringSessionTests` (start s načteným nastavením bez druhého načtení, `LoadSettingsAsync`, změna jen jazyka nerestartuje monitoring a přepne jazyk), `AppServicesTests`. `TrayToolTipFormatterTests.UnsupportedCultureFallsBackToEnglish` používá místo `de-DE` (nově podporované) `ja-JP`; `FakeMenuTarget.SetFixedItems` maže položky tiskáren jako skutečný target.
  - Testy nesahají na disk (kromě čtení `.axaml` z repozitáře), síť ani uživatelská data; globální jazyk vrací v `Dispose`.
- **Ověření na Windows 11:** `dotnet build` 0 varování, `dotnet test` 1634 testů (1633 prošlo, 1 přeskočený existující), opakováno 3× bez chyby. Dočasný harness mimo repozitář (scratchpad, Avalonia bez zobrazení oken) vytvořil skutečné `SettingsWindow` a `DiagnosticsWindow` a postupně přepnul de, fr, pl, cs, sk, es, it, en: titulek i texty už vytvořených oken se pokaždé přepnuly. Debug build aplikace spuštěný 25 s běžel a nespadl (asynchronní start v pořádku).
- **Co ověřit ručně (uživatel):** Nastavení → Obecné → Jazyk: přepnout např. na Deutsch a Uložit → tray menu (i podmenu tiskáren), tooltip ikony a otevřené okno Diagnostika / O aplikaci jsou hned německy, čísla a časy zůstávají v českém formátu; restart aplikace → menu je německy hned od startu (bez probliknutí češtiny); „Podle systému“ vrátí jazyk Windows; ručně do `settings.json` napsat `"language": "xx"` → aplikace běží v jazyce systému a okno nastavení ukáže upozornění na opravené hodnoty; AOT build (`dotnet publish -c Release -r win-x64`) spustit a přepnout jazyk; projít texty všech jazyků v okně nastavení (délka popisků, zalamování).
- **Platformy:** vše společné (Avalonia + .NET resources), žádný platformní kód. **Na macOS nic neověřeno** (jazyk systému tam určuje `CurrentUICulture` stejně jako na Windows; AOT pro macOS se kompiluje jen na macOS).

## Review
**qa-reviewer (2026-10-18): bez blokujících nálezů.** `dotnet build` 0 varování, `dotnet test` 1635 testů (1634 prošlo, 1 přeskočený existující), `dotnet publish -c Release -r win-x64` bez varování, všech 7 překladů (`Strings.{cs,sk,de,es,fr,it,pl}.resources`) je v `PrusaTrayDiagnostic.exe`.
- Doplněn test `XamlLocalizationTests.EveryLocalizedStringsUseHasTheCheckedForm`: každé použití `LocalizedStrings.Instance` v `.axaml` musí mít tvar, který kontroluje test existence klíčů (jinak by překlep v klíči prošel).
- Drobnosti (neblokující, **opraveno**): test `LanguageOnlyChangeKeepsTheMonitorRunningAndReachesTheLanguage` místo pevných 50 ms čeká přímo na dokončení `ApplyAsync`; sk „toľkoto minút“ → „toľko minút“; fr stav „Impression“ → „Impression en cours“. Ponecháno: čeština má před `%` obyčejnou mezeru (stávající stav); `TrayMenuViewModel.RefreshTexts` aktualizuje položky tiskáren a presenter pak menu stejně přestaví (jednorázová práce navíc při přepnutí, výsledek správný).
- Otázka k hodnotě jiného typu (např. `"language": 5`) rozhodnuta: ponechat konzistentně s ostatními poli (viz Otevřené otázky).
- Po opravách: `dotnet build` 0 varování, `dotnet test` 1635 testů (1634 prošlo, 1 přeskočený existující).
- Při merge T-010 (toasty) a T-014 (kamera): texty notifikací se musí formátovat až při zobrazení (čtou `Strings.Culture`), texty kamery chybí v 6 nových jazycích – test shody klíčů merge zastaví, dokud se nepřeloží.
