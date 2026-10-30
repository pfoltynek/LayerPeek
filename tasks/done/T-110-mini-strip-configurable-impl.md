---
id: T-110
title: Malý náhled – nastavitelný obsah pásu (implementace návrhu T-104)
status: done
owner: ui-dev
priority: P3
platform: all
depends_on: [T-104, T-103, T-109]
---

## Cíl
Uživatel si u každé tiskárny vybere, co ukazuje informační pás malého náhledu kamery: 7 nezávislých položek (pruh
průběhu, procento, zbývající čas, čas dokončení, vychladnutí po tisku, řádek názvu tisku, řádek grafu teplot).
Nastavuje se na záložce Widgety v nové položce „Malý náhled“. Výchozí stav (vše zapnuto) = dnešní chování po T-103.

Závazná specifikace: `tasks/done/T-104-mini-strip-configurable.md`, oddíl `## Návrh UI` (oddíly 0–9) a `## Rozhodnutí`
(uživatel 2026-10-30: Q1–Q10 vše varianta A). Kde se tento úkol a návrh liší, platí návrh; rozpor zapsat do Otevřených
otázek.

**Proč jeden úkol (ne T-104a/b/c z odhadu návrhu):** Core část je malá (jeden `sealed record` + mapper, ≈ 0,5 dne)
a bez UI nemá samostatnou hodnotu ani testovatelné chování pro uživatele; nastavení a pás sdílejí model, texty
a snímky a mění tytéž soubory (`Strings.*.resx`, `WidgetSettingsTemplates.axaml`, `CameraMiniStripViewModel`).
Rozdělení by přidalo jen mezikrok s neúplnou funkcí. Pokud se úkol v jedné relaci nestihne, smí ui-dev skončit po
Core + nastavení s nezaškrtnutými kritérii pásu a nahlásit to (pravidlo 6 v `CLAUDE.md`).

## Rozsah
- **Core (`LayerPeek.Core.Cameras`):** `CameraMiniStripSettings` (`sealed record`: `ProgressBar`, `Percent`,
  `Remaining`, `Finish`, `Cooldown`, `NameRow`, `ChartRow`, všechny `bool`, výchozí `true`;
  `CameraMiniStripSettings.Default`) v `CameraSettings.MiniStrip`. Mapper `settings.json` (čtení, zápis, varování).
  Core zůstává bez Avalonie a HTTP.
- **App – nastavení:** položka „Malý náhled“ pod oddělovačem v seznamu záložky Widgety, panel se 7 zaškrtávacími poli
  a poznámkou (návrh oddíl 2), rozpracovaná kopie `PrinterEditorViewModel.MiniStrip`, zahrnutí do „Obnovit výchozí“
  a „Použít rozložení u všech tiskáren“ (Q10 A).
- **App – pás:** `CameraMiniStripViewModel` / `MiniStripContent` / `Texts` / `CameraMiniSummaryPanel` / přepínače
  vynechají vypnuté položky (návrh oddíly 1, 3–5), aplikace nastavení po Uložit do otevřeného malého okna.
- Texty v 8 jazycích, testy (Core, App, headless UI), snímky, `docs/architecture.md`, CHANGELOG.
- **Mimo rozsah:** nastavitelné pořadí (Q5), volba priority (Q6), volba formátu času (Q7), živý náhled v nastavení
  (Q8), nastavení v malém okně / kontextové nabídce (Q1 C), změna `camera-windows.json`.

## Akceptační kritéria

### Core – model a uložení (`settings.json`)
- [x] `CameraMiniStripSettings` se 7 vlastnostmi `bool` (výchozí `true`) a `Default`; `CameraSettings.MiniStrip`
      výchozí `CameraMiniStripSettings.Default`.
- [x] Uložení u tiskárny jako `printers[*].camera.miniStrip` s klíči `progressBar`, `percent`, `remaining`, `finish`,
      `cooldown`, `nameRow`, `chartRow` (camelCase podle zbytku souboru). **Zapisují se jen hodnoty `false`**; když jsou
      všechny `true`, objekt `miniStrip` se nezapíše vůbec.
- [x] Chybějící objekt nebo klíč = `true`. Neplatná hodnota klíče (řetězec, číslo, `null`, objekt) = `true` + varování
      mapperu (bez hodnoty v textu, jen klíč a tiskárna podle vzoru `CameraOverlayDocument`); zbytek souboru i ostatní
      klíče `miniStrip` platí. `miniStrip` jiného typu než objekt = vše `true` + jedno varování.
- [x] Starý klíč `camera.overlay.mini` (formát T-036) se pro pás **nepoužívá** a jeho čtení / převod se nemění;
      test: soubor s `overlay.mini` i `miniStrip` → `miniStrip` se čte nezávisle, převod rozložení se chová jako dřív.
- [x] Verze souboru zůstává (`CurrentVersion = 2`), žádná migrace; soubory 0.1.0 / 0.3.0 (stávající fixture) se načtou
      bez nových varování a s `MiniStrip == Default`.
- [x] Round-trip test (každá položka zvlášť vypnutá, všechny vypnuté, vše zapnuté), JSON přes `SettingsJsonContext`
      (source generator, bez reflexe).
- [x] Nastavení pásu se **nemaže** se zapomenutím pamatovaného stavu okna (`CameraWindowStateStore.Remove` nebo
      ekvivalent; test). `camera-windows.json` (`miniNameRow` / `miniChartRow`) se nemění a vypnutí řádku v nastavení
      jeho pamatovaný stav nesmaže (po opětovném zapnutí se řádek vrátí, jak byl; test).

### App – záložka Widgety, položka „Malý náhled“
- [x] Seznam widgetů: pod posledním widgetem `Separator` (9 DIP, není položkou výběru, klávesnicí se přeskočí)
      a položka „Malý náhled“ (`SettingsCameraWindowMini`, ikona Fluent PictureInPicture 16) **bez** zaškrtávacího
      pole viditelnosti (prázdný prostor 20, ikony a názvy lícují s widgety). Nový druh položky vedle
      `WidgetItemViewModel`, ne `IOverlayWidgetDefinition`.
- [x] Mezerník na položce nic nepřepíná; `AutomationProperties.Name` = „Malý náhled“ (bez „zobrazený / skrytý“);
      `AutomationId` `WidgetsItem-miniStrip`.
- [x] Panel (`WidgetsDetail`, `DataTemplate` v `WidgetSettingsTemplates.axaml`, kompilované bindingy): nadpis, popis
      `SettingsMiniStripDescription` (≤ 2 řádky), mřížka `*,24,*` – vlevo Pruh průběhu nahoře, Procento, Zbývající
      čas, Čas dokončení; vpravo Vychladnutí po tisku, Řádek názvu tisku, Řádek grafu teplot; pod tím poznámka
      `SettingsMiniStripHint` (1 řádek). `CheckBox` `MinHeight` 32, text se smí zalomit na 2 řádky.
      `AutomationId` `MiniStripProgressBar`, `MiniStripPercent`, `MiniStripRemaining`, `MiniStripFinish`,
      `MiniStripCooldown`, `MiniStripNameRow`, `MiniStripChartRow`, `MiniStripHint`.
- [x] Panel platí pro tiskárnu vybranou nahoře záložky (přepnutí tiskárny ukáže její hodnoty, rozpracované hodnoty
      jiné tiskárny se neztratí). Vypnutá kamera tiskárny = pole zakázaná, hodnoty zůstanou. Žádná tiskárna = položka
      ani panel nejsou vidět (jako T-092 3.6).
- [x] Pořadí Tab: seznam → 7 polí po sloupcích (levý shora dolů, pak pravý) → oddíl Všechny widgety.
- [x] Změny se projeví až po **Uložit**; Zrušit je zahodí.
- [x] „Obnovit výchozí“ vrátí i pás vybrané tiskárny na `Default`; „Použít rozložení u všech tiskáren“ zkopíruje i pás
      vybrané tiskárny do ostatních. Texty tlačítek beze změny; tooltip `SettingsWidgetsResetHelp` zmiňuje i pás
      malého náhledu (8 jazyků).

### App – pás malého náhledu
- [x] Minimum: tečka / ikona stavu + název stavu jsou vždy vidět. Když zapnuté položky nemají co ukázat, souhrn je
      název stavu („● Tiskne“); „Pozastaveno“ s vypnutým procentem bez procenta. Název stavu se nikdy neschová.
- [x] Vypnuté procento / zbývající čas / čas dokončení se neobjeví v žádné variantě souhrnu (`MiniStripVariant`);
      výběr varianty zůstává podle pevných pravidel T-103 3.1 / 3.2 (první, která se celá vejde; procento > zbývající
      čas > čas dokončení > slovo „Hotovo v“) nad zapnutými položkami. Skupina času dokončení je dál celá, nebo žádná.
- [x] Vypnuté vychladnutí: po tisku „● Dokončeno“ / „● Připravena“ bez ❄ / ⏻ a bez textů C1–C3 / K1–K2, horní pruh
      jako bez úlohy. Notifikace „Vychladlo“ a widget Vychladnutí se nemění (týká se jen pásu).
- [x] Vypnutý pruh průběhu: horní 3 DIP jsou vždy slabý podklad (jako bez úlohy) ve všech stavech, výška pásu
      se nemění (Q3 dílčí a).
- [x] Vypnutý řádek názvu / grafu: přepínač ⬡ / 📈 má `IsVisible=False` (není ve stromu automatizace ani v pořadí Tab),
      řádek se nikdy neukáže, `StripHeight` počítá jen dostupné a rozbalené řádky. Šířka souhrnu
      `W = šířka − 100` (oba řádky), `− 68` (jeden), `− 34` (žádný) – test pro 240 / 320 / 400.
- [x] Vypnutý graf: historie teplot se pro pás neodebírá (jako dnes při sbaleném grafu).
- [x] Tooltip a `AutomationProperties.Name` souhrnu: celé znění **jen ze zapnutých položek** (vypnutá položka není
      ani v tooltipu, Q3 dílčí c); název stavu vždy.
- [x] Po Uložit se otevřené malé okno překreslí: varianty se přepočítají, přepínače zmizí / objeví se, okno dopočítá
      výšku a zůstane na pracovní ploše (pravidla T-089), bez restartu monitoringu.
- [x] Žádná alokace při pollu beze změny (`CameraMiniStripAllocationTests` i s nevýchozím nastavením).

### Texty
- [x] Nové klíče `SettingsMiniStripDescription`, `SettingsMiniStripProgressBar`, `SettingsMiniStripPercent`,
      `SettingsMiniStripRemaining`, `SettingsMiniStripFinish`, `SettingsMiniStripCooldown`, `SettingsMiniStripNameRow`,
      `SettingsMiniStripChartRow`, `SettingsMiniStripHint` (+ případné tooltipy polí 6 a 7, viz Otevřené otázky)
      ve **všech 8** `Strings.*.resx` (cs, en, de, es, fr, it, pl, sk); výchozí znění = návrh T-104 oddíl 7, ui-dev
      zkontroluje terminologii a vykání / tykání podle zbytku daného souboru. Upravený `SettingsWidgetsResetHelp`.
      `StringsTests` projde. `SettingsMiniStripHint` ≤ 60 znaků ve všech jazycích.

### Testy
- [x] Core.Tests: model, mapper (chybějící, jen `false` se zapisuje, neplatné hodnoty s varováním, starý
      `overlay.mini`, starší fixture bez nových varování, round-trip).
- [x] App.Tests: rozpracovaná kopie a Uložit / Zrušit, Obnovit výchozí a Použít u všech (vč. pásu), přepnutí tiskárny,
      kamera vypnutá; `MiniStripContent` / `Texts` / varianty pro kombinace vypnutých položek (aspoň: každá položka
      zvlášť, 2–5 vypnuté = minimum, vychladnutí vypnuté po tisku, zbývající čas vypnutý → čas dokončení při 240),
      tooltip bez vypnutých položek, `StripHeight` a šířka souhrnu bez přepínačů.
- [x] Headless UI (`LayerPeek.App.UiTests`): záložka Widgety v 8 jazycích při 780 × 532 – položka a panel bez oříznutí,
      `ScrollViewer` extent ≤ viewport (`SettingsWindowLayoutTests`), pořadí Tab, mezerník na položce, kamera vypnutá,
      žádná tiskárna; pás 240 / 320 / 400 × 8 jazyků pro nevýchozí kombinace (bez přepínačů, bez zbývajícího času,
      minimum) – souhrn se nepřekrývá s přepínači (mezera ≥ 8), skryté přepínače nejsou v Tab ani ve stromu
      automatizace, výška pásu podle `StripHeight`; po Uložit se otevřené malé okno změní.
- [x] Snímky: nové `settings-widgets-mini-strip`, `settings-widgets-mini-strip-de`, `settings-widgets-mini-strip-fr`,
      `camera-mini-strip-custom-240`, `camera-mini-strip-no-rows-320`; stávající `settings-widgets*` aktualizované
      (oddělovač a nová položka v seznamu). **macOS sadu (`Snapshots/macOS/`) přegenerovat a prohlédnout zde**
      (`LAYERPEEK_UPDATE_SNAPSHOTS=1 dotnet test --project tests/LayerPeek.App.UiTests`). **Windows sadu
      (`Snapshots/`) přegenerovat ručně na Windows** – seznam změněných a nových souborů zapsat do Poznámek
      k implementaci; do té doby tyto testy na Windows selžou (stejně jako u T-103 / T-109). *(macOS sada přegenerovaná a prohlédnutá, viz Poznámky k implementaci; zbývá Windows.)* → přesunuto do T-114
- [x] **Bezpečné testy:** žádný test nesahá na skutečný profil uživatele (`settings.json`, `camera-windows.json`,
      logy v `%APPDATA%` / `~/Library/Application Support/LayerPeek`). Testy mapperu a store pracují v paměti nebo ve
      vyhrazených testovacích složkách (`TestDirectories` / `ReservedTempDirectory`), headless testy ve svém harnessu
      (`SettingsWindowHarness`, `CameraHarness`); případné spuštění aplikace jen s `LAYERPEEK_TEST_DATA_DIR`
      a `LAYERPEEK_INSTANCE_SUFFIX` (Debug). Žádná síť mimo loopback.

### Dokumentace a build
- [x] `docs/architecture.md` (oddíl Malý náhled T-089 / T-090 / T-103: `CameraMiniStripSettings`,
      `printers[*].camera.miniStrip`, vztah k `miniNameRow` / `miniChartRow`) a CHANGELOG `[0.3.1]` (nebo aktuální
      nevydaná verze) Added; `README.md` / `README.cz.md`, pokud popisují obsah pásu.
- [x] Žádný nový balíček; vše AOT kompatibilní (bez reflexe, JSON přes source generator, kompilované bindingy).
- [x] `dotnet build` (Debug **i** Release) a `dotnet test` bez chyb a varování.

## Otevřené otázky
**Rozhodnuto uživatelem 2026-10-30** (zapsáno při implementaci):
- **Závislosti:** začít hned, i když T-103 a T-109 jsou ještě v `tasks/in-progress` (jejich kód je v `develop`, čekají jen
  na Windows snímky / revizi návrhu).
- **Q1 – tooltipy polí 6 a 7:** ano, krátký tooltip (cs) „Řádek rozbalíte tlačítkem ⬡ v malém náhledu.“ / „Řádek rozbalíte
  tlačítkem 📈 v malém náhledu.“ (symbol podle řádku), přeloženo do všech 8 jazyků (`SettingsMiniStripNameRowHelp`,
  `SettingsMiniStripChartRowHelp`; i jako `AutomationProperties.HelpText`).
- **Q2 – `SettingsWidgetsResetHelp`:** (cs) „Vrátí viditelnost, polohu, pořadí, velikost a zvýraznění widgetů a obsah
  malého náhledu této tiskárny na výchozí.“, přeloženo do všech 8 jazyků.

**Rozhodnuto uživatelem 2026-10-30 (po revizi ui-designer, provedeno ve větvi `feature/design-review-fixes`):**
- **Ikona položky „Malý náhled“ – varianta A:** ruční náhrada nahrazena oficiální cestou Fluent
  `ic_fluent_picture_in_picture_16_regular` (stejný klíč i plátno). ✔ vyřešeno.
- **Q-D1 – varianta A:** tooltipy / `HelpText` polí 6 a 7 jmenují tlačítko pásu jeho názvem (`CameraMiniToggleName` /
  `CameraMiniToggleChart`) místo symbolů ⬡ / 📈, ve všech 8 jazycích. ✔ vyřešeno.
- **Nález 1 (řádky sloupců):** jedna `Grid` se sdílenými řádky `Auto`. ✔ vyřešeno.
- **Nález 4 (snímky):** doplnit `settings-widgets-mini-strip-camera-off-it` a `settings-widgets-mini-strip-dark`. ✔ vyřešeno.

Původní znění otázek (pro historii):
- **Ikona položky „Malý náhled“:** oficiální cesta Fluent `ic_fluent_picture_in_picture_16_regular` nebyla v offline
  relaci k dispozici (bez sítě, žádný lokální balíček ikon). `IconPictureInPicture16Regular` v `OverlayIcons.axaml` je
  zatím ručně nakreslená náhrada stejného tvaru (obrys 1, roh 2,5, malý obraz vpravo dole), v XAML označená komentářem.
  Při příští aktualizaci sady ikon ji nahradit cestou z `@fluentui/svg-icons` (stejný klíč i plátno, nic jiného se
  nemění). Potvrdit, že je to tak v pořádku.

Revize ui-designer 2026-10-30 (rozhodnuto výše):
- **Ikona položky „Malý náhled“ (odpověď na otázku výše):** náhrada je jako dočasná přijatelná (čitelná při 16 px).
  (A, doporučeno) nahradit ji oficiální cestou Fluent hned v tomto úkolu – cesta je teď dostupná a zapsaná v revizi
  níže, licence MIT / (B) nechat náhradu do příští aktualizace sady ikon.
- **Q-D1 – symboly ⬡ / 📈 v tooltipech polí 6 a 7:** text ukazuje jiné symboly, než jaké mají přepínače v pásu (Fluent
  krychle a graf), 📈 je barevné emoji a čtečka čte názvy znaků. (A, doporučeno) jmenovat tlačítko jeho názvem bez
  symbolu, např. cs „Řádek rozbalíte v malém náhledu tlačítkem Název tisku v pásu.“ / „… tlačítkem Graf teplot
  v pásu.“ (názvy = `CameraMiniToggleName` / `CameraMiniToggleChart`) / (B) vlastní obsah tooltipu se skutečnou ikonou
  (`OverlayIcon` + text, kompilované bindingy), `HelpText` bez symbolu / (C) ponechat, jak uživatel schválil.

## Poznámky k implementaci
- Uzavřeno 2026-10-30: zbývající ruční ověření přesunuto do T-114 (rozhodnutí uživatele)

(ui-dev, 2026-10-30, macOS; větev `feature/T-110-mini-strip-configurable-impl`)

- **Core:** `CameraMiniStripSettings` (`Core/Cameras`), `CameraSettings.MiniStrip` (i v `Equals` / `GetHashCode` /
  `ToString`). `settings.json`: `CameraDocument.MiniStrip` jako `JsonElement?` (čte se volně jako `overlay`), zápis přes
  `CameraMiniStripDocument` (jen `false`, `WhenWritingNull`) a `SettingsJsonContext`. Neplatná hodnota klíče = `true` +
  varování „Mini preview strip of printer 'x': 'key' is not true or false; the item is shown.“ (jedno na klíč, poslední
  z duplicit vyhrává), sekce jiného typu než objekt = jedno varování; JSON `null` u celé sekce = chybějící (bez
  varování), `null` u klíče = varování. Verze 2 beze změny, `overlay.mini` (T-036) se čte a převádí jako dřív.
- **Nastavení:** seznam záložky Widgety má `WidgetsListEntries` (widgety, `WidgetsSeparatorEntry`, `MiniStripListEntry`)
  a `SelectedWidgetsListEntry`; `Widgets` / `SelectedWidget` zůstávají (při vybrané položce „Malý náhled“ je
  `SelectedWidget` `null`). Oddělovač je `ListBoxItem` s `IsEnabled=False`, `Focusable=False`, `AccessibilityView=Raw`
  (šipky ho přeskočí – ověřeno testem). Panel `MiniStripSettingsViewModel` (`Printer`, `CanEdit` = kamera zapnutá
  a okno editovatelné) + `DataTemplate` `MiniStripSettings`. Hodnoty v `PrinterEditorViewModel.MiniStrip` (7 vlastností
  `MiniStrip*`), do `BuildCamera` / dirty stavu. „Obnovit výchozí“ / „Použít rozložení u všech tiskáren“ volají i
  `SetMiniStrip`. „Tovární nastavení“ okna kamery (Remove stavu okna) pás nemění.
- **Pás:** `MiniStripContent.Of(…, items)` + `HidesPercent`; `Texts` skládá souhrn jen ze zapnutých položek (bez procenta
  i zbývajícího času = název stavu, čas dokončení pak za názvem stavu); `IsNameRowAvailable` / `IsChartRowAvailable` /
  `ShowsToggles` / `ShowsChartRow`; `ApplySettings` z `CameraViewModel.ApplyPrinters`. Základní řádek v XAML
  `*,Auto,Auto` (přepínače s okraji 8 / 6, bez přepínačů 10). Řádek názvu se při vypnutí nepočítá vůbec, graf nepočítá
  teploty a neodebírá historii.
- **Texty:** 11 nových klíčů + upravený `SettingsWidgetsResetHelp` v 8 jazycích. Terminologie podle souborů (de
  „Mini-Vorschau“ / „Leiste“, es „vista mini“ / „franja“ a vykání „Elija“, fr „mini-aperçu“ / „bandeau“, it „anteprima
  mini“ a tykání „Scegli“, pl „mały podgląd“ / „pasek“); de poznámka „Der Druckerstatus ist in der Leiste immer
  sichtbar.“ (51 znaků). `SettingsMiniStripHint` ≤ 60 znaků ve všech jazycích (test). Tooltip „Obnovit výchozí“ se
  zalamuje standardně (Fluent `ToolTip`), nic se neořízne.
- **Testy:** Core `SettingsMapperMiniStripTests`; App `MiniStripSettingsTests`, `CameraMiniStripItemsTests`, nový případ
  v `CameraMiniStripAllocationTests`; UI `SettingsWindowMiniStripUiTests` (položka, oddělovač, zarovnání, Space, Tab,
  kamera vypnutá, žádná tiskárna, uložení, 8 jazyků bez oříznutí), dva nové stavy v `SettingsWindowLayoutTests`
  (extent ≤ viewport v 8 jazycích), `CameraWindowMiniStripItemsUiTests` (240 / 320 / 400 × 8 jazyků × bez přepínačů /
  bez zbývajícího času / bez grafu / minimum; Tab a strom automatizace; po Uložit se otevřené okno změní a spodní hrana
  zůstane). Upravené `SettingsWindowWidgetsUiTests` (7 / 12 položek, End končí na „Malý náhled“).
- **Ověřeno na macOS:** `dotnet build` Debug i Release 0 varování, `dotnet test` 8014 testů, 0 selhání (182 přeskočeno
  jako dřív). Stav profilu (Application Support, Logs, LaunchAgents, Preferences / Saved State / Caches / HTTPStorages
  bundlu, Keychain, LaunchServices) před a po testech beze změny. `dotnet run` (Debug, `LAYERPEEK_TEST_DATA_DIR`,
  `LAYERPEEK_INSTANCE_SUFFIX`) se spustil a běžel; v neinteraktivní relaci bez aktivního displeje čekal na displej
  (menu bar se nezobrazil), UI tedy ručně ověřené není. Spuštění zaregistrovalo složku `src/LayerPeek.App` v
  LaunchServices (název končí na `.App`); registrace byla hned odebrána `lsregister -u`.
- **Snímky macOS** (přegenerované, prohlédnuté): nové `settings-widgets-mini-strip`, `-de`, `-fr`,
  `camera-mini-strip-custom-240`, `camera-mini-strip-no-rows-320`; změněné `settings-widgets`, `settings-widgets-dark`,
  `settings-widgets-camera-off-it`, `settings-widgets-cooldown-de`, `settings-widgets-cooldown-error-fr` (oddělovač
  a položka v seznamu). Ostatní snímky se po přegenerování nezměnily.
- **Zbývá (ručně na Windows):** přegenerovat a prohlédnout Windows sadu `tests/LayerPeek.App.UiTests/Snapshots/`
  (`LAYERPEEK_UPDATE_SNAPSHOTS=1 dotnet test --project tests/LayerPeek.App.UiTests`): nové `settings-widgets-mini-strip.png`,
  `settings-widgets-mini-strip-de.png`, `settings-widgets-mini-strip-fr.png`, `camera-mini-strip-custom-240.png`,
  `camera-mini-strip-no-rows-320.png`; změněné `settings-widgets.png`, `settings-widgets-dark.png`,
  `settings-widgets-camera-off-it.png`, `settings-widgets-cooldown-de.png`, `settings-widgets-cooldown-error-fr.png`.
  Do té doby tyto testy na Windows selžou. Proto úkol zůstává v `in-progress`.

### Revize ui-designer (2026-10-30)
Podklady: snímky macOS `settings-widgets-mini-strip(-de, -fr)`, `settings-widgets(-dark)`, `settings-widgets-camera-off-it`,
`settings-widgets-cooldown-de`, `settings-widgets-cooldown-error-fr`, `camera-mini-strip-custom-240`,
`camera-mini-strip-no-rows-320`, `camera-mini-strip-printing` (zvětšené výřezy měřené v px = DIP při 100 %), XAML
`Settings/Widgets/WidgetSettingsTemplates.axaml` (`MiniStripSettings`), `Cameras/Overlay/OverlayIcons.axaml`,
`Cameras/Mini/CameraMiniStrip.axaml`, `Strings.*.resx`. Aplikace se nespouštěla.

**Verdikt: OK s drobnými nedostatky** (nic neblokuje uzavření po Windows snímcích).

V pořádku (ověřeno na snímcích):
- Položka „Malý náhled“: ikona i název lícují s widgety (ikona x = 52, text x = 76 ve všech třech jazycích), oddělovač
  9 DIP přes šířku seznamu bez okrajů položky, výběr stejnou barvou jako widgety; ve tmavém motivu čitelné.
- Panel: nadpis s ikonou + popis na 1 řádek (en / de / fr), dva sloupce `*,24,*`, poznámka pod levým sloupcem na 1 řádek,
  nic se neořízne ani v de („Zeile mit Temperaturdiagramm“ končí 28 DIP před okrajem okna). Celá záložka se vejde
  do 780 × 532 bez posuvníku.
- Pás: `custom-240` (bez zbývajícího času → „● 19 % ⚑ 2:35 PM“) a `no-rows-320` (bez přepínačů, souhrn využije šířku
  „Done at 2:35 PM“) odpovídají návrhu T-104 oddíly 3–5; výška pásu se nemění.

Nálezy:
1. **Nízká – řádky sloupců nelícují při zalomení** (`WidgetSettingsTemplates.axaml`, `MiniStripSettings`, dva nezávislé
   `StackPanel`). Ve fr se „Refroidissement après l'impression“ a „Ligne du graphique des températures“ zalomí na 2 řádky,
   takže pravý sloupec (170 / 204 / 236) se rozjede proti levému (170 / 202 / 234 / 266). Návrh: jedna `Grid`
   s `RowDefinitions="Auto,Auto,Auto,Auto"` sdílenými oběma sloupci (potomci deklarovaní po sloupcích, aby pořadí Tab
   zůstalo levý → pravý), nebo stav přijmout – čitelnost netrpí.
2. **Nízká – symboly ⬡ / 📈 v tooltipech polí 6 a 7** (`SettingsMiniStripNameRowHelp`, `SettingsMiniStripChartRowHelp`, všech
   8 `Strings.*.resx`, i jako `HelpText`). Skutečné přepínače v pásu kreslí Fluent ikony `IconCube16Regular`
   a `IconDataTrending16Regular` (`CameraMiniStrip.axaml`), ne šestiúhelník a barevné emoji; 📈 se v tooltipu vykreslí
   barevným emoji fontem a čtečka přečte „white hexagon“ / „chart increasing“. Znění schválil uživatel (Q1), proto viz
   Otevřené otázky (Q-D1).
3. **Nízká – náhradní ikona `IconPictureInPicture16Regular`** (`Cameras/Overlay/OverlayIcons.axaml`). Při 16 px čitelná
   ve světlém i tmavém motivu (zvětšený výřez snímku), tloušťka obrysu 1 odpovídá ostatním ikonám Fluent Regular –
   **jako dočasná náhrada přijatelná**. Tvar se ale od oficiální ikony liší: náhrada je rámeček 14 × 10 s malým obrazem
   uvnitř, oficiální je rámeček 13 × 10 (1..14 × 2..12) s vyříznutým pravým dolním rohem a malým obrazem 7 × 5
   (8..15 × 9..14), který rámeček přesahuje – opticky výraznější a typický „obraz v obraze“. Oficiální cesta je teď
   dostupná (`@fluentui/svg-icons` `icons/picture_in_picture_16_regular.svg`, ověřeno 2026-10-30 i v repozitáři
   microsoft/fluentui-system-icons, licence MIT – kompatibilní s GPL-3.0):
   `F1 M1 4.5C1 3.11929 2.11929 2 3.5 2H11.5C12.8807 2 14 3.11929 14 4.5V8H13V4.5C13 3.67157 12.3284 3 11.5 3H3.5C2.67157 3 2 3.67157 2 4.5V9.5C2 10.3284 2.67157 11 3.5 11H7V12H3.5C2.11929 12 1 10.8807 1 9.5V4.5ZM9 9C8.44772 9 8 9.44772 8 10V13C8 13.5523 8.44772 14 9 14H14C14.5523 14 15 13.5523 15 13V10C15 9.44772 14.5523 9 14 9H9Z`.
   Návrh: ui-dev nahradí cestu (stejný klíč, komentář `<!-- ic_fluent_picture_in_picture_16_regular.svg -->` jako
   u ostatních) ještě v tomto úkolu, protože Windows snímky se stejně přegenerují; macOS snímky se seznamem widgetů
   (`settings-widgets*`) pak přegenerovat a prohlédnout znovu.
4. **Nízká – chybí snímek zakázaného panelu a tmavý snímek panelu.** Stav „kamera vypnutá“ s vybranou položkou „Malý
   náhled“ a tmavý motiv panelu nejsou ve snímcích (jen v testech stromu). Návrh: doplnit
   `settings-widgets-mini-strip-camera-off-it` a `settings-widgets-mini-strip-dark` (kontrast zakázaných polí v tmavém
   motivu).
5. **Informace – neověřeno:** zda čtečky (Narrator / VoiceOver) nezapočítají oddělovač do „položka x z 8“
   (`AccessibilityView=Raw` je správně, ale `PositionInSet` headless test neukáže); šířky textů na Windows (Segoe UI) –
   poznámka `MiniStripHint` má `NoWrap` + výpustku, při ≤ 60 znacích riziko malé, potvrdí Windows snímky.

### Úpravy po revizi ui-designer (ui-dev, 2026-10-30, macOS; větev `feature/design-review-fixes`)
- **Ikona:** `IconPictureInPicture16Regular` v `Cameras/Overlay/OverlayIcons.axaml` má oficiální cestu
  `@fluentui/svg-icons` `icons/picture_in_picture_16_regular.svg` (z revize výše, „F1“ = nenulové pravidlo výplně jako
  ostatní ikony), komentář `<!-- ic_fluent_picture_in_picture_16_regular.svg -->`. Licence MIT (Fluent UI System Icons,
  © Microsoft), kompatibilní s GPL-3.0, text licence už je v `Licenses/Texts/mit-fluentui-system-icons.txt`; žádný balíček,
  jen geometrie (AOT beze změny). Pozn.: hlavička souboru uvádí verzi sady 1.1.344, cesta této ikony je převzatá z revize
  (ověřeno ui-designerem 2026-10-30), přesná verze balíčku není zapsaná.
- **Tooltipy polí 6 a 7** (`SettingsMiniStripNameRowHelp`, `SettingsMiniStripChartRowHelp`, 8 jazyků): cs „Řádek
  rozbalíte v malém náhledu tlačítkem „Název tisku v pásu“.“ / „… tlačítkem „Graf teplot v pásu“.“; ostatní jazyky stejně
  s přesným názvem přepínače z `CameraMiniToggleName` / `CameraMiniToggleChart` v uvozovkách daného jazyka (fr « … »
  s U+00A0). Test `ResetHelpMentionsTheMiniPreviewInEveryLanguage` ověřuje, že text obsahuje název přepínače a žádný
  symbol.
- **Panel:** `MiniStripSettings` je jedna `Grid` `*,24,*` × `Auto,Auto,Auto,Auto`, pole deklarovaná po sloupcích (pořadí
  Tab beze změny – test `SpaceOnTheItemChangesNothingAndTabGoesThroughTheColumns` prochází), `VerticalAlignment="Top"`.
  Nový test v `PanelFitsWithoutClippingInEveryLanguage`: řádky 1–3 obou sloupců začínají ve všech 8 jazycích na stejném
  y (ve fr dřív 170 / 204 / 236 proti 170 / 202 / 234). Záložka se dál vejde (`SettingsWindowLayoutTests`).
- **Nové snímky:** `settings-widgets-mini-strip-dark` (tmavý motiv, Zbývající čas vypnutý) a
  `settings-widgets-mini-strip-camera-off-it` (kamera vypnutá, zakázaná pole, Vychladnutí vypnuté) v
  `SettingsWindowSnapshotTests`.
- **Snímky macOS přegenerované a prohlédnuté:** nové `settings-widgets-mini-strip-dark`,
  `settings-widgets-mini-strip-camera-off-it`; změněné (nová ikona v seznamu, ve fr zarovnané řádky)
  `settings-widgets`, `settings-widgets-dark`, `settings-widgets-camera-off-it`, `settings-widgets-cooldown-de`,
  `settings-widgets-cooldown-error-fr`, `settings-widgets-mini-strip`, `settings-widgets-mini-strip-de`,
  `settings-widgets-mini-strip-fr`.
- **Windows (ručně, nahrazuje seznam výše):** přegenerovat a prohlédnout v `tests/LayerPeek.App.UiTests/Snapshots/`
  nové `settings-widgets-mini-strip.png`, `settings-widgets-mini-strip-de.png`, `settings-widgets-mini-strip-fr.png`,
  `settings-widgets-mini-strip-dark.png`, `settings-widgets-mini-strip-camera-off-it.png`,
  `camera-mini-strip-custom-240.png`, `camera-mini-strip-no-rows-320.png`; změněné `settings-widgets.png`,
  `settings-widgets-dark.png`, `settings-widgets-camera-off-it.png`, `settings-widgets-cooldown-de.png`,
  `settings-widgets-cooldown-error-fr.png`. Úkol proto zůstává v `in-progress`.
