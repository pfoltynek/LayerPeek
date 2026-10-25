---
id: T-088
title: Widget Vychladnutí („Chladne…“ / „Vychladlo – můžete vypnout“)
status: done
owner: ui-dev
priority: P2
platform: all
depends_on: [T-079, T-081, T-092]
---

## Cíl
Widget **Vychladnutí**, který se objeví po dokončení nebo zastavení tisku (ne po chybě): stav **„Chladne…“** (teplota trysky a podložky, průběh k prahu, odhad času) a **„Vychladlo – můžete vypnout“**. Data z `CooldownMonitor` (T-079); rozhoduje jen tryska, podložka se jen zobrazuje.

## Rozsah
- View model + šablona, jedna registrace v `OverlayWidgetRegistry`; odběr `CooldownMonitor.Changed` jen pro tiskárnu okna a jen při připojení okna (odhlášení při zavření).
- Velikosti S/M/L a zvýraznění podle T-076; odhad času jen když ho Core vrátí (jinak bez odhadu, ne „0 min“); práh v textu podle nastavení tiskárny (T-079, T-080); zvýraznění = aktuální teplota trysky, ve stavu „Vychladlo“ text stavu (akcentová barva).
- Widget je bez stavu chladnutí skrytý; zmizí při začátku dalšího tisku, při přechodu tiskárny do offline a při znovu zapnutém topení trysky (T-079); tisk skončený pod prahem ukáže rovnou „Vychladlo“.
- Nové texty ve všech 8 jazycích (s nezlomitelnou mezerou před „°C“).
- **Panel nastavení na záložce Widgety** (rozhodnutí 2026-10-25, smlouva T-092, Návrh UI 6 v T-092): registrace widgetu Vychladnutí dostane `DescriptionKey` (`CameraOverlayWidgetCooldownDescription`), `IconKey` (ikona sněhové vločky z T-076 oddílu 6) a `Settings` = továrnu panelu Vychladnutí, kterou připraví T-092 (práh trysky, „Použít práh u všech tiskáren“, stav upozornění „Tiskárna vychladla“ s odkazem na záložku Obecné). Panel, jeho view model, šablonu a texty dodává T-092; tento úkol je jen zapojí do registrace.

## Akceptační kritéria
- [x] App.Tests: oba stavy, zmizení při dalším tisku / offline / ohřevu, tisk skončený pod prahem, chybějící podložka, bez odhadu, neznámý konec tisku (`PrintEndedAt` `null` → bez řádku časů), změna prahu za běhu, levné porovnání, přepnutí jazyka, odhlášení
- [x] Headless UI testy: widget ve všech velikostech v obou stavech, nejdelší jazyk; přechod „Chladne…“ → „Vychladlo“ s falešnými hodinami a snapshoty; snímky reference
- [x] Registrace podle smlouvy T-092: test registru (popis a ikona Vychladnutí, panel nastavení deklarovaný) a headless UI test, že na záložce Widgety se po výběru Vychladnutí ukáže panel s polem prahu (`WidgetCooldownThreshold`) se skutečnou registrací
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Rozhodnutí (uživatel, 2026-10-24)
- Začátek po dokončeném i zastaveném tisku, ne po chybě; konec při dalším tisku, offline nebo znovu zapnutém topení trysky; tisk skončený pod prahem = rovnou „Vychladlo“ (bez notifikace); práh u tiskárny 30–100 °C, výchozí 50 °C (T-079).
- Zvýraznění: aktuální teplota trysky, ve stavu „Vychladlo“ text stavu (návrh architekta, designer potvrdí v T-076).

## Rozhodnutí (uživatel, 2026-10-24, po dokončení T-079)
- **Rozhodnutí 10 – konec tisku nebyl vidět:** když `CooldownState.PrintEndedAt` je `null` (tisk skončil během výpadku spojení), widget ukáže jen **„Vychladlo – můžete vypnout“** s teplotami, **bez** řádku „tisk dokončen v … · vychladlo za …“. Ve stavu „Chladne…“ se řádek s časem konce tisku také vynechá.
- **Rozhodnutí 11 – tiskárna vychladlá už při startu:** tiskárna, která už při startu aplikace (nebo při přidání tiskárny) ukazuje skončený tisk, widget **nemá**. Core (T-079, větev `feature/widget-core-followups`) pro ni chladnutí nesleduje, fáze zůstane `None`; widget nic zvláštního řešit nemusí.
- **Vykání:** texty widgetu vykají („Vychladlo – můžete vypnout“), stejně jako zbytek české UI.

## Rozhodnutí (uživatel, 2026-10-25)
- Nastavení widgetu (práh trysky, „Použít práh u všech tiskáren“, stav upozornění) je v panelu Vychladnutí na nové záložce **Widgety** (T-092, otázka Q6 jeho návrhu). Tento úkol zaregistruje panel Vychladnutí podle smlouvy, kterou zavádí T-092 (`DescriptionKey`, `IconKey`, `IOverlayWidgetSettingsFactory`). Proto nově závisí na T-092 (T-092 naopak na tomto úkolu nezávisí).
- **Délky se nezalamují mezi číslem a jednotkou (po review, nejdřív testy):** mezi číslem a jednotkou je ve všech 8 jazycích nezlomitelná mezera (U+00A0), stejně jako před „°C“: „3 min“, „cca 6 min“, „−60 min“, odhad sliceru. Mezi částmi („2 h“ a „35 min“) zůstává obyčejná mezera, aby se dlouhá délka mohla zalomit mezi nimi.

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- 2026-10-25, ui-dev. Kód v `App/Cameras/Overlay/Widgets/CooldownWidgetViewModel.cs` (view model, obsah `CooldownContent`, texty `CooldownFormat`) a `CooldownWidgetView.axaml`. Registrace je poslední řádek `OverlayWidgets.CreateRegistry` (pořadí seznamu na záložce Widgety: Stav, Časy, Teploty, Název tisku, Vychladnutí) s `DescriptionKey = OverlayWidgetDescriptions.Cooldown`, `IconKey = OverlayWidgetIcons.Cooldown` a `Settings = CooldownWidgetSettings.Instance` (panel z T-092).
- **Napojení:** `OverlayWidgetContext` dostal nepovinný `CooldownMonitor? Cooldown`. `CameraViewModel` do něj předává monitor, který už měl pro malý náhled (T-089). Widget odebírá `CooldownMonitor.Changed` od vytvoření do `Dispose` (zavření okna nebo odebrání z rozložení) a reaguje jen na svou tiskárnu (`RequestUpdate`). Bez monitoru se karta nikdy neukáže.
- **Kdy je karta vidět:** jen ve fázi `Cooling` / `CooledDown` se známou teplotou trysky. Konec stavu (další tisk, offline po 1 min, znovu zapnuté topení) řeší Core. Widget navíc kartu skryje hned u nedostupného snapshotu a při aktivní úloze (stejně jako pás malého náhledu), aby karta se zastaralými teplotami nestála vedle „Nedostupná“ nebo nového tisku. Tiskárna vychladlá už při startu (rozhodnutí 11) má fázi `None`, tedy žádnou kartu.
- **Obsah:** `CooldownContent` je hodnota zaokrouhlená jako na obrazovce (celé stupně, průběh v celých procentech dolů, minuty odhadu nahoru, místní čas konce tisku na minutu, minuty chladnutí nahoru). Snapshot bez viditelné změny nic nealokuje a nic nepošle na UI vlákno (testy v kolekci `AllocationSensitive`). Ve stavu „Vychladlo“ chodí `Changed` při každém pollu (mění se `UpdatedAt`), karta se přesto nepřekresluje.
- **Odhad a zastaralý stav (poznámka z review T-079):**
  - Odhad se ukáže, jen když ho Core vrátí, ještě neuplynul (Core ho odpočítává od času měření; nula = text jen s prahem, nikdy „0 min“) a stav není starší než `CooldownWidgetViewModel.StaleAfter` = 15 min. To je nejdelší mezera, kterou Core ještě bere jako pozorovanou (`CooldownTracker.GapLimit` = 3 × nejdelší interval pollingu 5 min). Starší stav znamená zastavený monitoring a Core chladnutí stejně ukončí při dalším snapshotu.
  - Aby odpočet nezamrzl, když snapshoty nechodí (zastavený monitoring, polling 1–5 min), běží při zobrazeném odhadu hodiny okna (`TimeProvider.CreateTimer`) po 15 s (`TickInterval`) a widget se přepočítá. Když odhad zmizí, hodiny se zastaví. Vytvoří se jednou za život widgetu, potom se jen přepínají.
  - Rozhodnutí do textu: odhad dál odpočítává čas od posledního měření (extrapolace Core). Po jeho uplynutí nebo po 15 min bez snapshotu zůstane text jen s prahem, teploty zůstanou poslední známé (jako u ostatních widgetů).
- **Vychladlo:** řádek „tisk dokončen v 19:52 · vychladlo za 14 min“ / „tisk zastaven v …“. Bez pozorovaného konce tisku (`PrintEndedAt` `null`, rozhodnutí 10) se řádek vynechá. Ve stavu „Chladne…“ řádek časů není nikdy. Tisk skončený už pod prahem (Q5 T-079, návrh T-076 2.5) ukáže jen „tisk dokončen v …“: Core k tomu dostal `CooldownState.EndedBelowThreshold` (nový nepovinný poslední parametr, výchozí `false`; `true` ve stavu „Vychladlo“, když žádné měření od konce tisku nebylo nad prahem; test v `CooldownTrackerTests`). Čas konce je jen hodina a minuta (text návrhu má jeden parametr). U stavu „Vychladlo“ trvajícího přes půlnoc se datum neukazuje.
- **Vzhled (návrh T-076 2.5, 1.3):** jeden strom prvků pro S/M/L, každé `AutomationId` jednou (`CameraOverlayWidget-cooldown-Title/-Nozzle/-Bed/-Progress/-Detail`).
  - M: ikona 20 + titulek 14 SemiBold, tryska 22 Bold / podložka 14 SemiBold `Secondary`, pruh 6 `OverlayCoolingBrush`, popisek 12 nejvýš na 2 řádky.
  - L: 16 / 28 / pruh 8 / 13, ikony `_24_`.
  - S: jeden řádek „❄ 87 °C · cca 6 min“ (tryska 15 Bold) s pruhem 4, nebo „⏻ Vychladlo · 48 °C“ (`CameraMiniCooled`).
  - Ikona trysky a podložky má barvy widgetu Teploty, kužel je pod ikonou stavu.
  - Zvýraznění: při chladnutí číslo trysky, ve stavu „Vychladlo“ text stavu (akcent a aspoň 15 Bold, L 16, ve stejné výšce řádku).
  - Zelený rámeček 1,5 `OverlayCooledBrush` kreslí sdílená karta podle nové vlastnosti `OverlayWidgetViewModel.IsOutlined` (třída `outlined`, v `OverlayStyles.axaml` i v kartě režimu rozložení). Odsazení vrací šířku rámečku, takže obsah při přechodu neposkočí. Zástupná karta režimu rozložení rámeček nemá.
  - Prvky přes víc sloupců mřížky vždy zahrnují i sloupec `*`, aby nerozšiřovaly automatické sloupce (ikona, tryska v S).
- **Texty (8 jazyků, nezlomitelná mezera před „°C“, vykání):** `CameraOverlayCooling`, `CameraOverlayCooled`, `CameraOverlayCooldownEstimate`, `CameraOverlayCooldownNoEstimate`, `CameraOverlayCooldownFinishedAt`, `CameraOverlayCooldownStoppedAt`, `CameraOverlayCooldownCooledIn` (cs a en z návrhu T-076 10) a navíc `CameraOverlayCooldownAbout` („cca {0}“ pro S, návrh má jen celou větu). de, es, fr, it, pl, sk navrhl ui-dev ve stejném tónu jako upozornění T-080. Jméno pro čtečku podle návrhu („Chladne…, Tryska 87 °C, Podložka 45 °C, bezpečné vypnutí za cca 6 min (tryska pod 50 °C)“).
- **Testy:**
  - App.Tests `Cameras/Overlay/Widgets/CooldownWidgetTests` (registrace a smlouva T-092, obě fáze se skutečným `CooldownMonitor` na falešných hodinách, dokončen / zastaven, neznámý konec, tisk pod prahem, další tisk / offline / ohřev, chybějící podložka a tryska, odhad, zaokrouhlení, nikdy „0 min“, zastaralý stav, odpočet hodinami bez snapshotů, tikání jen s odhadem, změna prahu za běhu, S/M/L, zvýraznění, levné porovnání, jiná tiskárna, přepnutí jazyka, odhlášení při zavření a skrytí, texty v 8 jazycích, vykání).
  - `CooldownWidgetAllocationTests` (0 B na 1000 snapshotů ve stavu „Vychladlo“ i „Chladne…“).
  - Headless `CameraWindowCooldownWidgetUiTests` (S/M/L v obou stavech: vykreslené velikosti, řezy a barvy textů, ikony, rámeček, výšky karet, zarovnání, `AutomationId`, jméno pro čtečku, odpočet s falešnými hodinami, dokončen / zastaven / neznámý konec / pod prahem, zmizení, karta v režimu rozložení se zástupnou kartou i s rámečkem, nejdelší texty ve všech 8 jazycích a velikostech, přepnutí jazyka).
  - Snímky reference `camera-widget-cooldown-{cooling|cooled}-{s|m|l}.png` (přechod „Chladne…“ → „Vychladlo“) a `camera-widget-cooldown-cooling-m-de.png` (vizuálně zkontrolované).
  - `SettingsWindowWidgetsUiTests.CooldownPanelComesFromTheAppRegistration`.
  - Testovací registrace `WidgetSettingsTestData.WithCooldown()` je odstraněná. Testy záložky Widgety (App.Tests i UI) používají produkční registr. `CameraHarness` počítá chladnutí na falešných hodinách okna.
- E2E pro tento úkol žádné (celý průchod je T-093). AOT: `dotnet publish -c Release -r win-x64` bez varování.
- Ručně neověřeno: skutečné DPI 125–200 %, macOS (SF Pro), čitelnost na skutečném videu, chování s pollingem 1–5 min v reálné aplikaci.
- **Pás malého náhledu (T-089), sjednoceno při review:** pás dřív bral `EstimatedRemaining` bez kontroly nuly a stáří („cca 0 min“, bez snapshotů zamrzlý text). Teď widget i pás používají sdílené `CooldownEstimate.Minutes` (odhad jen kladný a čerstvý podle `StaleAfter`) a sdílené hodiny `CooldownCountdown` (po 15 s jen při zobrazeném odhadu). Testy `CameraMiniStripCooldownEstimateTests`.

## Review
- 2026-10-25, qa-reviewer. Verdikt: schváleno po opravách níže.
- **Oprava (Core):** `CooldownState.EndedBelowThreshold` se počítal v `BuildState` s aktuálním prahem, takže změna prahu po „Vychladlo“ ho přepnula: zvýšený práh nad nejvyšší teplotu po tisku schoval řádek „vychladlo za …“, snížený práh u tisku skončeného pod prahem ukázal nesmyslné „vychladlo za …“. Teď se rozhodne jednou při potvrzení „Vychladlo“ se stejným prahem jako událost (`CooldownTracker._endedBelowThreshold`). Testy `RaisedThresholdAfterCoolingDownKeepsThatTheNozzleCooledDown`, `LoweredThresholdAfterEndingBelowItKeepsThatNothingCooledDown`.
- **Sjednocení s pásem malého náhledu (T-089):** viz poznámku výše; pravidlo odhadu a hodiny odpočtu jsou ve sdíleném `Cameras/Overlay/Widgets/CooldownEstimate.cs`, widget i pás je jen používají. `MiniStripContent.Of` dostal čas `now` (hodiny okna).
- ~~Drobnost mimo rozsah: `OverlayFormat.Duration` má mezi číslem a „min“ obyčejnou mezeru, takže se v řádku časů karty M může zalomit „3 / min“.~~ Opraveno podle rozhodnutí uživatele 2026-10-25 (viz výše).
- 2026-10-25, qa-reviewer, potvrzující kolo. Bez dalších nálezů (latch `_endedBelowThreshold` na všech cestách k „Vychladlo“, `CooldownEstimate` / `CooldownCountdown`, testy po sloučení s develop).
- **Nezlomitelné mezery v délkách:** klíče `DurationLessThanMinute` („< 1 min“, nezlomitelné obě mezery), `DurationMinutes`, `DurationHoursMinutes`, `DurationDaysHours` a `CameraOverlayChartStart` („−60 min“) ve všech 8 `Strings*.resx`. Délky skládá jen `TrayToolTipFormatter.FormatMinutes` (přes `OverlayFormat.Duration` i ve widgetech Časy, Název tisku – odhad sliceru, Vychladnutí, v pásu malého náhledu a v upozorněních), takže pravidlo platí všude, i v tooltipu ikony. Hlídá ho `StringsTests.DurationsKeepNumberAndUnitTogether`. Očekávané texty testů jsou přepsané na ` `. Změnil se jen snímek `camera-widget-cooldown-cooled-m.png` („3 min“ je teď celé na druhém řádku). Mimo rozsah zůstaly volby okna Diagnostika (`DiagnosticsChartWindow*`: „30 min“, „2 h“, „24 h“) a `SettingsCameraTestTimeout` („{0} s“).
