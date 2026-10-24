---
id: T-081
title: Widgety překryvu – framework v okně kamery (registr, 6 kotev, karty)
status: todo
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
- [ ] Registr: nový widget lze přidat jednou třídou + šablonou + jedním řádkem registrace; test, že všechny id z `OverlayWidgetIds` s implementací jsou registrované právě jednou a neznámé id se přeskočí bez výjimky
- [ ] App.Tests: základní třída widgetu – formátování jen při změně obsahu (žádný post na UI vlákno bez viditelné změny), přepnutí jazyka, uvolnění; sdílené formátovače (testy T-036 přenesené)
- [ ] Hostitel: rozdělení do kotev a pořadí, víc widgetů v jedné kotvě, změna nastavení za běhu (okno se nehýbe, stream běží), prázdné rozložení = žádná karta
- [ ] Headless UI testy (`LayerPeek.App.UiTests`) s testovacím widgetem:
  - karty ve všech 6 kotvách v normálním okně a na celé obrazovce (stejné rozložení),
  - v malém náhledu žádná karta,
  - kotvy nahoře nepřekrývají tlačítka okna,
  - skládání v kotvě,
  - globální průhlednost 30 / 72 / 95 % a její změna za běhu,
  - zvýraznění akcentovou barvou,
  - snímky reference (nové, vizuálně zkontrolované),
  - `CameraWindowOverlayUiTests` převedené.
- [ ] Licence Fluent UI System Icons (MIT) je v přehledu licencí třetích stran. Žádný nový NuGet balíček ani font
- [ ] Žádné reflexní bindingy, žádné trim/AOT varování; `dotnet publish -c Release -r win-x64` bez varování
- [ ] Nové texty ve všech 8 jazycích, nepoužívané odstraněné
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné – T-076 a T-077 jsou rozhodnuté 2026-10-24; zbývá jen schválení návrhu T-076)

## Poznámky k implementaci
(vyplní agent po dokončení)
