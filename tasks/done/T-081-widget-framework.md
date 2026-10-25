---
id: T-081
title: Widgety překryvu – framework v okně kamery (registr, 6 kotev, karty)
status: done
owner: ui-dev
priority: P2
platform: all
depends_on: [T-076, T-077]
---

## Cíl
Nahradit jediný `Border` s řádky (T-036) rozšiřitelným systémem widgetů: karty v 6 kotvách podle `CameraOverlayLayout` (T-077), styl podle schváleného návrhu T-076. Přidání widgetu = jedna třída (+ šablona) a jedna explicitní registrace. Konkrétní widgety jsou samostatné úkoly `T-0NN-widget-<název>` (Stav T-082, Časy T-083, Teploty T-084, Název tisku T-085, Vychladnutí T-088); tento úkol dodá jen framework a testovací widget v testech.

## Kontext
- Dnes: `App/Windows/CameraWindow.axaml` (`PrintOverlay`, `AutomationId` `CameraPrintOverlay`), `App/Cameras/CameraViewModel.cs` (`OverlayLines`, `OverlayCorner`…), `App/Cameras/CameraPrintContent.cs` (levné porovnání – formátuje se jen při změně zobrazeného obsahu, `CameraOverlayTextCache`). Pravidla „kdy se co ukazuje“ z T-036 (údaje úlohy jen během úlohy, ostatní když jsou známé) platí dál.
- Tlačítka okna vpravo nahoře – kotvy nahoře nesmí kolidovat (dnes 40 DIP odsazení vpravo nahoře, 74 DIP volné šířky vlevo nahoře).

## Rozsah
- **Registr widgetů (Native AOT, bez reflexe):** `App/Cameras/Overlay/IOverlayWidgetDefinition` (id z `OverlayWidgetIds`, klíč názvu v `Strings`, podporované velikosti, `Create(OverlayWidgetContext)` → view model), `OverlayWidgetRegistry` naplněný **explicitním seznamem** v composition rootu (`Composition/…`), žádné skenování sestavení ani `Activator`. Šablony karet jako `DataTemplate` s `x:DataType` (kompilované bindingy) podle typu view modelu.
- **Vstup widgetu:** `OverlayWidgetInput` (snapshot tiskárny, `PrinterDefinition`, režim okna, čas z `TimeProvider`, kultura) – widget přepočítá jen svůj levně porovnatelný obsah a formátuje texty jen při jeho změně (zachovat přístup `CameraPrintContent`); přepnutí jazyka přes `ILanguageAware`.
- **Hostitel:** `CameraOverlayViewModel`.
  - Má 6 slotů kotev s viditelnými widgety v pořadí rozložení.
  - Normální okno a celá obrazovka sdílejí rozložení. **V malém náhledu se karty nekreslí** (rozhodnutí 2026-10-24, malý náhled má pás pod obrazem, T-089).
  - Průhlednost pozadí je **globální** nastavení z `AppSettings` (30–95 %, výchozí 72 %, T-077).
  - Widget bez dat se skryje podle návrhu. Prázdná kotva nic nevykreslí.
  - Uložení nastavení se projeví v otevřeném okně hned (jako T-036): okno se nehýbe a stream běží dál.
- **XAML:**
  - `Grid` se 6 kotvami nad obrazem.
  - Karty: `Border` s pozadím ≈ `rgba(14,14,16,α)` podle globální průhlednosti, poloměr 10, tabulková čísla přes `FontFeatures` `tnum`.
  - Styly v jednom `ResourceDictionary` sdíleném s dalšími widgety.
  - `AutomationId` podle návrhu: kotvy `CameraOverlayAnchor{Kotva}`, karty `CameraOverlayWidget-{id}`.
- **Ikony:** Fluent UI System Icons (https://github.com/microsoft/fluentui-system-icons, MIT, rozhodnutí 2026-10-24) vložené jako geometrie (`StreamGeometry` v `ResourceDictionary`, jen ty, které návrh T-076 použije).
  - Bez NuGet balíčku a bez fontu, takže za běhu nevzniká žádná závislost.
  - Licenční text MIT patří do přehledu licencí třetích stran (`App/Licenses/ThirdPartyLicenses.cs` + `Licenses/Texts`).
  - Do poznámek k implementaci zapsat ověření licence a seznam ikon.
- **Zvýraznění** (rozhodnutí 2026-10-24): klíčový údaj widgetu se vykreslí **akcentovou barvou** `#FA6831`. Sdílený styl (třída `emphasized`) a vlastnost view modelu `IsEmphasized`.
- **Sdílené prvky karet** pro všechny widgety: pruh průběhu, pilulka stavu s tečkou, řádek „ikona + hodnota“, čip, velké číslo, styl zvýraznění; sdílené formátovače z `CameraPrintContent` (čas dokončení, doby, teploty, zkracování textů) přesunout tak, aby je widgety jen volaly (pravidla T-036 – jiný den, časové pásmo, zaokrouhlení, přetečení – převzít, ne přepsat).
- **Odstranění starého překryvu:** `OverlayLines`, `CameraOverlayLine`, staré typy `CameraOverlayOptions` / `CameraOverlayItems` / `CameraOverlayCorner`, pokud už je nepotřebuje převod v T-077; nepoužívané texty T-036 odstranit ze všech `Strings.*.resx`. Testy pravidel zobrazení z T-036 se přesunou k widgetům (T-082 až T-085) – nic se nesmaže bez náhrady.
- **Pozor – mezistav:** po sloučení tohoto úkolu překryv nic nezobrazí, dokud nejsou hotové widgety T-082 až T-085 (umístění z převodu se do té doby jen nevykreslí). Mezi T-081 a T-085 **nevydávat verzi** (zapsat do poznámek; vydání hlídá T-055). Doporučené pořadí: T-081 → T-082 → T-083 hned po sobě.
- Malý náhled: po tomto úkolu je bez překryvu (žádné karty), dokud T-089 nepřidá pás pod obrazem. Totéž platí pro zákaz vydání.

## Akceptační kritéria
- [x] Registr: nový widget lze přidat jednou třídou + šablonou + jedním řádkem registrace; test, že všechny id z `OverlayWidgetIds` s implementací jsou registrované právě jednou a neznámé id se přeskočí bez výjimky
- [x] App.Tests: základní třída widgetu – formátování jen při změně obsahu (žádný post na UI vlákno bez viditelné změny), přepnutí jazyka, uvolnění; sdílené formátovače (testy T-036 přenesené)
- [x] Hostitel: rozdělení do kotev a pořadí, víc widgetů v jedné kotvě, změna nastavení za běhu (okno se nehýbe, stream běží), prázdné rozložení = žádná karta
- [x] Headless UI testy (`LayerPeek.App.UiTests`) s testovacím widgetem:
  - karty ve všech 6 kotvách v normálním okně a na celé obrazovce (stejné rozložení),
  - v malém náhledu žádná karta,
  - kotvy nahoře nepřekrývají tlačítka okna,
  - skládání v kotvě,
  - globální průhlednost 30 / 72 / 95 % a její změna za běhu,
  - zvýraznění akcentovou barvou,
  - snímky reference (nové, vizuálně zkontrolované),
  - `CameraWindowOverlayUiTests` převedené.
- [x] Licence Fluent UI System Icons (MIT) je v přehledu licencí třetích stran. Žádný nový NuGet balíček ani font
- [x] Žádné reflexní bindingy, žádné trim/AOT varování; `dotnet publish -c Release -r win-x64` bez varování
- [x] Nové texty ve všech 8 jazycích, nepoužívané odstraněné
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné – T-076 a T-077 jsou rozhodnuté 2026-10-24; zbývá jen schválení návrhu T-076)

## Poznámky k implementaci
- 2026-10-25, ui-dev. Kód v `App/Cameras/Overlay/`, registrace v `App/Composition/OverlayWidgets.cs`, popis v `docs/architecture.md` (oddíl Překryv okna kamery – widgety).
- **Smlouva widgetu:**
  - `IOverlayWidgetDefinition` (id, klíč názvu, podporované velikosti, priorita kolize, šablona obsahu karty, min. šířka podle velikosti, `Create(OverlayWidgetContext)`); běžná implementace `OverlayWidgetDefinition`.
  - View model dědí z `OverlayWidgetViewModel<TContent>`: `ComputeContent` (vlákno monitoru, levně porovnatelná hodnota bez formátování), `Format` (UI vlákno, jen při změně a po přepnutí jazyka), volitelně `HasData`, `RequestUpdate`, `Dispose`.
  - Vstup `OverlayWidgetInput` (snapshot, `PrinterDefinition`, režim okna, čas a pásmo z `TimeProvider`, kultura) je hodnota bez alokace.
  - **Přidání widgetu (T-082 a dál):** view model + šablona obsahu (`DataTemplate` s `x:DataType`) + jeden řádek `new OverlayWidgetDefinition(...)` v `OverlayWidgets.CreateRegistry`. Klíče názvů jsou připravené v `OverlayWidgetNames`, priority (Stav > Časy > Vychladnutí > Teploty > Název tisku) v `OverlayWidgets`.
  - **Rozšíření smlouvy bez rozbití** (pokyn vedoucího 2026-10-25, budoucí záložka „Widgety“ s vlastním panelem nastavení widgetu): nový volitelný údaj, např. popis panelu nastavení, přibude jako `init` vlastnost `OverlayWidgetDefinition` s výchozí hodnotou `null` a v `IOverlayWidgetDefinition` jako člen s výchozí implementací (default interface member, bez reflexe, AOT v pořádku). Stávající registrace se nemění. V T-081 se nic z toho neimplementuje.
- **Hostitel `CameraOverlayViewModel`** (`CameraViewModel.Overlay`): 6 seznamů kotev v pořadí od okraje okna, `AreCardsVisible` (ne v malém náhledu), globální průhlednost z `AppSettings` přes `MonitoredPrintersState.CameraOverlayOpacityPercent` (neplatná hodnota = 72 %). Widget, který v novém rozložení zůstane, si nechá instanci; změna velikosti a zvýraznění mění kartu na místě. Neznámé i zatím neregistrované id se přeskočí bez výjimky a bez logu. Výjimka widgetu (vytvoření, výpočet, formátování) se zaloguje jednou a widget zůstane skrytý (`IsFaulted`) do zavření okna.
- **Výkon:** na snapshot se pod zámkem spočítají jen hodnoty; post na UI vlákno jen při změně obsahu (víc snapshotů během zaneprázdněného UI = jeden post), formátuje jen změněný widget. Test `SnapshotThatChangesNothingVisibleAllocatesNothing` ověřuje 0 B alokací na 1000 snapshotů bez viditelné změny. Rozmístění se přepočítá jen při změně velikosti okna nebo obsahu karet (layout Avalonie).
- **Rozmístění a malé okno:** `OverlayAnchorsPanel` + `OverlayAnchorItems` + `OverlayStackPanel`; čistá pravidla v `OverlayArrangement` (stupně 0–3, pravidlo 1 šířky středové karty s rezervou tlačítek 112, pravidlo 2 skrytí celé karty s nižší prioritou). Vlastní doplnění: pravidlo 2 platí i pro středový sloupec a i pro jediný zásobník vyšší než sloupec; při shodné prioritě jde pryč karta dál od okraje (pak spodní). Karta skrytá pravidly malého okna se změří, ale umístí mimo okno (jako recyklované prvky virtualizujícího panelu), aby rozhodnutí nekmitalo.
- **Vzhled:** `OverlayStyles.axaml` (jeden sdílený soubor: barvy T-076 1.3, karta `Border.overlayCard` S/M/L, `TextBlock.numeric`/`.big`/`.secondary`/`.emphasized`, části `OverlayStatusPill`, `OverlayIconText`, `OverlayChip`, `OverlayProgressBar`, `OverlayIcon`). Barvy tečky stavu z `ColorTrayIconPalette` doplní T-082.
- **Ověření Avalonie 12.1.4:** `TextBlock.FontFeatures` existuje a funguje (`FontFeatures="tnum"`, test `NumbersUseTabularFigures`: s `tnum` mají „11%“ a „88%“ stejnou šířku, bez něj v Inter ne). `AutomationProperties.LiveSetting` v Avalonii 12 existuje (`AutomationProperties.LiveSettingProperty`); funkčně ho ověří T-091, který ho použije.
- **Ikony – Fluent UI System Icons:** licence MIT ověřená 2026-10-25 v https://github.com/microsoft/fluentui-system-icons/blob/main/LICENSE („Copyright (c) 2020 Microsoft Corporation“), kompatibilní s GPL-3.0. Data cest SVG z commitu 82ecf7c (balíček `@fluentui/svg-icons` 1.1.344) jako `StreamGeometry` v `OverlayIcons.axaml` (pravidlo výplně nonzero `F1`), bez NuGet balíčku a fontu, za běhu žádná závislost. Licence v `Licenses/Texts/mit-fluentui-system-icons.txt` a v `ThirdPartyLicenses.All` (verze 1.1.344, bez balíčku). Ikony (všechny z návrhu T-076 oddíl 6): Hourglass Half 16/20 Regular, Flag 16/20 Regular, Clock 16/20 Regular, Cone 16 Filled, Line Horizontal 1 16/20 Filled, Temperature 16 Regular, Cube 16 Regular, Layer 20 Regular, Weather Snowflake 20/24 Regular, Power 20/24 Regular, Status 16 Regular, Timer 16 Regular, Layout Cell Four 16 Regular, Data Trending 16 Regular, Highlight 16 Regular, Circle 16 Filled.
- **Odstraněno:** `CameraPrintContent` (`CameraOverlayLine`, `CameraOverlayTextCache`, `OverlayTemperature` přešly do `OverlayValues`/`OverlayFormat`/`OverlayTextCache`), `OverlayLines`, `IsPrintOverlayVisible`, `OverlayCorner`, `IsOverlayTop/Right`, `OverlayItems` z `CameraViewModel`, `Border` `CameraPrintOverlay` a jeho styly v `CameraWindow.axaml`, text `CameraOverlayLabeledValue` ve všech 8 jazycích, snímky `camera-overlay-*.png`. `CameraTarget` nese `PrinterDefinition` a rozložení (`Overlay` = `CameraOverlayLayout`).
- **Ponecháno do T-092 (výklad zadání):** `CameraSettings.LegacyOverlay` a typy `CameraOverlayOptions` / `CameraOverlayItems` / `CameraOverlayCorner` zůstávají, protože je stále používá stará podzáložka Překryv v nastavení. Její odstranění (`OverlayRows`, `SelectedOverlayCorner`, texty `SettingsOverlay*`, `AutomationId`) je výslovně v rozsahu T-092 a podzáložku současně upravuje T-080; převod T-077 je nepotřebuje. Okno kamery `LegacyOverlay` už nečte, takže tabulka položek a roh na podzáložce do T-092 nic nemění (test `SavedOldCornerDoesNotChangeTheWidgetLayout`).
- **Nové texty (8 jazyků):** `CameraOverlayWidgetStatus/Times/Temperatures/PrintName/Cooldown` (názvy widgetů pro tabulku a režim rozložení, klíče v `OverlayWidgetNames`).
- **Testy:** App.Tests `Cameras/Overlay/*` (registr, základní třída, hostitel, pravidla rozmístění, sdílené hodnoty a texty = převedené testy T-036 z `CameraPrintContentTests`), `CameraViewModelOverlayTests` a úpravy `CameraViewModelTests`. Testovací widget `TestOverlayWidget` (sdílený s UI testy). Headless `CameraWindowOverlayUiTests` převedené: 6 kotev v normálním okně i na celé obrazovce, malý náhled bez karet, tlačítka okna, skládání, průhlednost 30/72/95 % a její změna, zvýraznění, tabulková čísla, malé okno, kolize, uložení za běhu, jazyk; nové snímky `camera-widgets-anchors.png` a `camera-widgets-sizes-opacity-30.png` (vizuálně zkontrolované).
- **Mezistav – nevydávat:** po tomto úkolu okno kamery nekreslí žádné karty (produkční registr je prázdný), dokud nejsou hotové T-082 až T-085; malý náhled je bez překryvu do T-089. Mezi T-081 a T-085 **nevydávat verzi** (hlídá T-055). Doporučení: T-082 a T-083 hned po sobě; zvážit, zda nepřidat do zákazu vydání i T-092 (stará podzáložka Překryv do té doby nic nedělá).
- E2E testy pro tento úkol žádné (celý průchod je T-093). Ručně neověřeno: skutečné DPI 125–200 %, macOS (SF Pro s `tnum`), čitelnost na skutečném videu.
