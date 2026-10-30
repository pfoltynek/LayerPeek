---
id: T-111
title: Okno Nastavení – minimální výška tlačítek 32 DIP
status: in-progress
owner: ui-dev
priority: P3
platform: all
depends_on: [T-101, T-109, T-110]
---

## Cíl
Tlačítka v okně Nastavení mají dnes výchozí výšku Fluent (headless macOS asi 28 DIP, Windows se Segoe UI asi 30–32 DIP),
tedy méně než cílových 32 DIP pro cíl klepnutí. Zjistil to ui-designer při revizi T-101 (2026-10-30, nález 2 a otevřená
otázka Q6 v `tasks/in-progress/T-101-test-notification.md`). Uživatel 2026-10-30 rozhodl vyřešit to samostatným úkolem
pro celé okno (varianta A z Q6).

## Rozsah
- Styl `Button` s `MinHeight="32"` v `Window.Styles` okna `src/LayerPeek.App/Windows/SettingsWindow.axaml`. Platí pro
  všechna tlačítka okna včetně šablon ze `Settings/Widgets/WidgetSettingsTemplates.axaml` (Uložit, Zavřít, Obnovit
  výchozí, Testovat připojení, Testovat kameru, Přidat / Odebrat tiskárnu, Odeslat zkušební upozornění, Otevřít
  nastavení oznámení, Použít prahy u všech tiskáren, Upravit rozložení, …).
- Odstranit lokální `MinHeight="32"` u tlačítek, kde je nově zbytečné (dnes několik míst ve
  `WidgetSettingsTemplates.axaml`). Tlačítka s vlastní větší výškou nechat beze změny. Odkazová tlačítka (vzhled
  hypertextového odkazu, např. „Změnit na záložce Obecné“) mají cíl klepnutí také 32 DIP. Pokud by styl měnil jejich
  vzhled, zapsat to do poznámek a nechat rozhodnout ui-designera.
- Selektor stylu nesmí zasáhnout ovládací prvky, které uvnitř šablony používají `Button` (např. šipky `NumericUpDown`
  (`ButtonSpinner`), rozbalovací tlačítko `ComboBox`, záhlaví `TabItem`, tlačítko „zobrazit heslo“ v `TextBox`).
  Použít třídu nebo selektor, který cílí jen na tlačítka okna. Konkrétní selektor zvolí ui-dev a zdůvodní ho
  v poznámkách.
- Znovu zkontrolovat výšky všech záložek a podzáložek (Tiskárny → Tiskárna / Kamera / Okno kamery, Widgety a nastavení
  všech widgetů včetně Vychladnutí a Malého náhledu, Obecné včetně stavů T-101 / T-020, Spouštění a aktualizace,
  O aplikaci, pokud je součástí okna). Nikde se nesmí objevit posuvník (T-033). Okno musí zůstat ve výchozí velikosti
  780 × 532 (T-094), stejné jako okno Diagnostika (T-041).
- Kontrola návrhu: po implementaci ui-designer projde snímky (výšky, mezery na mřížce 4/8 DIP, žádná tlačítka „lepící“
  na sousední prvky, viz nález 1 T-101) a verdikt zapíše do sekce `### Revize ui-designer`.

## Akceptační kritéria
- [x] Každé viditelné tlačítko okna Nastavení (všechny záložky a podzáložky ve výchozím stavu i ve stavech s chybou,
      výsledkem testu a varováním T-020 / T-097) má v headless testu `Bounds.Height >= 32`. Nový test ve
      `tests/LayerPeek.App.UiTests/Settings/SettingsWindowLayoutTests.cs` projde stromem a vypíše tlačítka, která
      podmínku nesplňují.
- [x] Vnitřní tlačítka šablon (`NumericUpDown`, `ComboBox`, `TabItem`, `TextBox`) mají stejnou velikost jako před
      úpravou. Test nebo porovnání snímků to potvrdí.
- [x] Stávající testy rozložení (`EverythingFitsWithoutScrollingInEveryState`,
      `GeneralTabWithTheTestNotificationFitsInEveryLanguage`, `GeneralTabWithNotificationsTurnedOffFitsInEveryLanguage`,
      `StartupAndUpdatesTabFitsInEveryLanguage`, `LocalNetworkHintWithItsButtonFitsInEveryLanguage`,
      `CameraLocalNetworkHintWithItsButtonFitsInEveryLanguage`, `MaximizedWindowStretchesWithoutScrollingOrCutTexts`
      a testy záložky Widgety) projdou ve všech jazycích beze změny výchozí velikosti okna 780 × 532.
- [x] `WindowOpensAtTheDesignedSizeThatFitsTheSmallestScreen` a testy velikosti okna Diagnostika
      (`tests/LayerPeek.App.UiTests/Diagnostics/DiagnosticsWindowLayoutTests.cs`) projdou: obě okna mají dál stejnou
      výchozí velikost (T-041, T-094).
- [x] Pokud se záložka do výchozí velikosti nevejde, úkol se **nezavírá**. Problém se zapíše do Otevřených otázek
      (zvětšit okno, nebo zhustit záložku – rozhodne uživatel).
- [ ] Snímky: macOS sada (`tests/LayerPeek.App.UiTests/Snapshots/macOS/`, `settings-*`) přegenerována a prohlédnuta zde
      (`LAYERPEEK_UPDATE_SNAPSHOTS=1 dotnet test --project tests/LayerPeek.App.UiTests`). Windows sadu
      (`Snapshots/`) přegeneruje uživatel ručně na Windows. Seznam změněných souborů se zapíše do Poznámek
      k implementaci. Do té doby snímkové testy na Windows selžou (stejně jako u T-103 / T-109 / T-110).
- [x] Revize ui-designer zapsaná v úkolu, verdikt OK, nebo nálezy vyřešené či přesunuté do Otevřených otázek.
- [x] **Bezpečné testy:** žádný test nesahá na skutečný profil uživatele (`settings.json`, `camera-windows.json`,
      `windows.json`, logy v `%APPDATA%` / `~/Library/Application Support/LayerPeek`). Headless testy běží ve svém
      harnessu (`SettingsWindowHarness`, `DiagnosticsWindowHarness`) s úložištěm v paměti nebo ve vyhrazených
      testovacích složkách (`TestDirectories` / `ReservedTempDirectory`). Případné spuštění aplikace jen
      s `LAYERPEEK_TEST_DATA_DIR` a `LAYERPEEK_INSTANCE_SUFFIX` (Debug). Žádná síť mimo loopback.
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (rozhodnuto 2026-10-30: styl `MinHeight` 32 pro celé okno, varianta A z T-101 Q6)
- **Q1 (2026-10-30, ui-dev) – VYŘEŠENO: uživatel 2026-10-30 zvolil variantu A.** Záložka Widgety s 10 widgety se po
  zvýšení tlačítek sekce „Všechny widgety“ (sekce 149 místo 143 DIP) nevešla o 1 DIP (obsah 423 DIP ve viewportu
  422 DIP, `ManyWidgetsScrollInsideTheListWhileTheTabFits` selhal ve všech 8 jazycích). Zvažované možnosti: A) `CompactHeight`
  seznamu widgetů 226 → 224, B) mezera mezi tlačítky sekce 8 → 4, C) horní okraj nadpisu sekce 16 → 12, D) mezera nad
  seznamem 12 → 8, E) vyjmout tlačítka sekce ze stylu, F) zvětšit okno. Provedeno A: `CompactHeight="224"`
  (7 × 32, rámeček seznamu 2 DIP sedmý řádek při rolování ořízne), test hlídá spodní mez 224, `docs/architecture.md`
  upraveno.
- **Q2 (2026-10-30, ui-designer) – svislé vystředění textu tlačítek (nález 1 revize).** Text tlačítek je po zvýšení na
  32 DIP o 1,5 DIP nad středem (dřív 0,5), protože Fluent `Button` nestřeďuje obsah svisle.
  - **A) (doporučeno)** V rámci T-111 přidat do stylu `Button, HyperlinkButton` setter
    `VerticalContentAlignment="Center"` a přegenerovat macOS snímky `settings-*`. Jeden řádek XAML, rozložení ani výšky
    se nemění, Windows snímky se přegenerovávají tak jako tak.
  - B) Nechat beze změny (rozdíl 1 DIP je okem téměř neviditelný, na Windows se nejspíš neprojeví).
  - C) Řešit později samostatným úkolem pro všechna okna (Diagnostika, okno kamery) najednou.
  Pozn.: T-112 mění totéž okno v jiné větvi; varianta A sahá jen na styl, konflikt by byl nanejvýš ve snímcích.
  **Rozhodnuto 2026-10-30 (uživatel): A.** Provedeno: setter `VerticalContentAlignment="Center"` ve stylu
  `Button, HyperlinkButton` v `SettingsWindow.axaml`, macOS snímky `settings-*` přegenerovány (38 souborů, text
  tlačítek ověřen na `settings-general.png`). Windows snímky přegeneruje uživatel spolu s ostatními.

## Poznámky k implementaci
- **Zahájení:** uživatel 2026-10-30 rozhodl začít, i když závislosti T-101, T-109 a T-110 jsou ještě
  v `tasks/in-progress/` (jejich kód je sloučený v `develop`, čekají jen na snímky z Windows).
- **Styl:** v `Window.Styles` okna `SettingsWindow.axaml` styl `Button, HyperlinkButton` s `MinHeight` 32. Platí i pro
  šablony panelů widgetů (`WidgetSettingsTemplates.axaml`), protože se zobrazují uvnitř okna. Lokální `MinHeight="32"`
  odkazového tlačítka „Změnit na záložce Obecné“ (Vychladnutí) je teď zbytečné a je odstraněné; jeho vzhled se nemění
  (měl totéž lokálně). `MinHeight="32"` u zaškrtávacích políček panelu Malý náhled zůstává – nejsou to tlačítka.
  Tlačítko obrázku tiskárny (56 × 56) a tlačítka s víc řádky textu jsou vyšší, beze změny.
- **Proč tento selektor:** typový selektor v Avalonii odpovídá přesnému typu (`StyleKey`), ne odvozeným typům. Části
  šablon jiných ovládacích prvků jsou jiné typy: `RepeatButton` (šipky `NumericUpDown` / `ButtonSpinner`, `Slider`,
  `ScrollBar`), `ToggleButton` (zobrazení hesla v `TextBox`); `ComboBox` a `TabItem` tlačítko v šabloně nemají. Žádná
  šablona použitá v okně neobsahuje prostý `Button` (test to hlídá). Třída na každém tlačítku by se u nového tlačítka
  dala snadno zapomenout; `:is(Button)` by zasáhl i `RepeatButton` / `ToggleButton`.
- **Testy** (`SettingsWindowLayoutTests`):
  - `EveryButtonIsAtLeast32DipHighInEveryState` – všechny stavy layout testu × všechny pohledy (záložky a podzáložky)
    plus stavy s vlastním tlačítkem (macOS Login Items vyžadují schválení T-019, oznámení vypnutá T-020, odeslané
    zkušební oznámení T-101, Local Network u testu spojení i kamery T-097, běžící test spojení se Zrušit). Vypíše každé
    viditelné vlastní tlačítko (bez `TemplatedParent`) nižší než 32 DIP a ověří, že kontrola dosáhla všech 21
    očekávaných tlačítek. Před úpravou hlásil 30 DIP u všech běžných tlačítek (headless macOS).
  - `ButtonsInsideTheTemplatesOfOtherControlsKeepTheirSize` – části šablon typu `Button` / odvozené (v okně jen
    `RepeatButton` u `ButtonSpinner` a `Slider`) mají stejné `MinHeight` a `Height` jako v prostém okně jen s motivem
    aplikace. Snímky to potvrzují také: u `settings-widgets-cooldown-*` se pixely mění až od řádku 327 (pod
    `NumericUpDown`), u `settings-general*` pole a pole se šipkami jen posunuté o 2 DIP.
- **Výsledky:** `dotnet build` (Debug i Release) 0 varování, 0 chyb. `dotnet test` (macOS): 8021 celkem, 7831 úspěšných,
  182 přeskočených, 8 selhávajících (`ManyWidgetsScrollInsideTheListWhileTheTabFits`, Q1). Po variantě A z Q1:
  8021 celkem, 7839 úspěšných, 182 přeskočených, **0 selhávajících**; všechny testy rozložení (všechny jazyky)
  a velikosti oken Nastavení i Diagnostika prochází.
- **Q1 varianta A:** `CompactHeight` seznamu widgetů 226 → 224 nemění žádný snímek (macOS ani Windows): se skutečnými
  widgety je seznam nižší než 224, mez se uplatní jen při víc widgetech (test s 10 widgety).
- **Snímky macOS** (přegenerované, každý prohlédnutý vedle původního): `settings-camera-notice`,
  `settings-camera-preview-4x3-de`, `settings-camera-preview`, `settings-camera-window-errors-fr`,
  `settings-camera-window-off-cs`, `settings-camera-window`, `settings-camera`, `settings-errors-fr`,
  `settings-general-errors`, `settings-general-test-sent-dark`, `settings-general-test-sent-fr`,
  `settings-general-turned-off-de`, `settings-general`, `settings-maximized`, `settings-printer-images`,
  `settings-prusaconnect`, `settings-prusalink`, `settings-startup-updates-not-installed`, `settings-startup-updates`,
  `settings-validation-cs`, `settings-validation`, `settings-widgets-camera-off-it`, `settings-widgets-cooldown-de`,
  `settings-widgets-cooldown-error-fr`, `settings-widgets-dark`, `settings-widgets-mini-strip-camera-off-it`,
  `settings-widgets-mini-strip-dark`, `settings-widgets-mini-strip-de`, `settings-widgets-mini-strip-fr`,
  `settings-widgets-mini-strip`, `settings-widgets-no-printers`, `settings-widgets` (32 souborů). Pozorování: tlačítka
  o 2 DIP vyšší, obsah pod nimi posunutý; seznam tiskáren a seznam widgetů o několik DIP nižší (vyplňují zbytek); nic
  oříznutého, žádný posuvník, mezery mezi tlačítky a sousedy zůstaly (nejmenší 4 DIP pod tlačítky v záhlaví oznámení).
- **Snímky Windows k přegenerování na Windows** (`tests/LayerPeek.App.UiTests/Snapshots/`): všechny výše uvedené
  `settings-*.png` se stejnými názvy. Ve Windows sadě dnes existuje 24 z nich; zbylých 8
  (`settings-general-test-sent-dark`, `settings-general-test-sent-fr`, `settings-general-turned-off-de`,
  `settings-widgets-mini-strip*` ×5) tam chybí už kvůli T-101 / T-110 a vzniknou při stejném přegenerování.
- **Platformy:** změna je čistě v XAML a platí stejně na Windows i macOS. Ověřeno jen headless na macOS; na Windows
  (Segoe UI, výchozí výška asi 30–32 DIP) neověřeno – ověří snímky z Windows.
- **Bezpečnost:** aplikace se nespouštěla; testy běží jen v headless harnessu s úložištěm v paměti, bez sítě.

### Revize ui-designer
**Verdikt (ui-designer, 2026-10-30): OK s drobnou výhradou.** Prohlédnuty macOS snímky `settings-*` (develop e1183ed)
a porovnány s verzí před T-111 (`8948638^`), u vybraných (`settings-general`, `settings-prusalink`,
`settings-widgets-cooldown-error-fr`) změřeny pixely (1 px = 1 DIP při 100 %).

Co je v pořádku:
- Všechna běžná tlačítka mají přesně 32 DIP (Uložit/Zrušit 488–519, Odeslat zkušební upozornění 150–181, Testovat
  připojení 339–370, Přidat/Odebrat tiskárnu 404–435 / 444–475, tři tlačítka „Všechny widgety“ 364–395 / 404–435 /
  444–475 s mezerou 8). Spodní okraje tlačítek zápatí zůstaly, tlačítka rostou nahoru.
- Tlačítka vedle 32 DIP polí lícují horní i dolní hranou: „Testovat připojení“ pod poli, „Použít prahy u všech
  tiskáren“ vedle `NumericUpDown` (190–221 obojí, `settings-widgets-dark`). Dvouřádková varianta ve francouzštině
  (`settings-widgets-cooldown-error-fr`) je vyšší, horní hrana lícuje s poli, beze změny proti dřívějšku.
- Tlačítko zkušebního oznámení vs. řádek Zvuk (`settings-general*`): mezera 5 DIP (řádky 182–186) je stejná jako před
  T-111, tlačítko se nedotýká rozbalovacího seznamu; mimo mřížku o 1 DIP (zaoblení / okraj `ComboBox`), nález 1 T-101
  platí dál, není to regrese.
- Nic oříznutého, žádný posuvník ve všech prohlédnutých stavech (de, fr, it, cs, chyby, oznámení vypnutá, odeslané
  zkušební oznámení, Login Items, kamera s upozorněním, okno kamery s chybami). Seznam tiskáren a seznam widgetů jsou
  o 8 DIP nižší (vyplňují zbytek), všechny položky dál viditelné.
- Tmavý režim (`settings-general-test-sent-dark`, `settings-widgets-dark`, `settings-widgets-mini-strip-dark`):
  tlačítka čitelná, kontrast beze změny.
- Odkazové tlačítko „Změnit na záložce Obecné“ vypadá stejně (dřív lokální `MinHeight`, má `VerticalContentAlignment="Center"`).
- Šipky `NumericUpDown` a posuvník mají stejnou velikost jako dřív (jen posun).

Nález:
1. **Drobný – text tlačítek o 1 DIP nad středem.** `SettingsWindow.axaml`, styl `Button, HyperlinkButton`. Šablona
   Fluent `Button` nenastavuje `VerticalContentAlignment` (výchozí `Stretch`, text u horního okraje + `ButtonPadding`),
   takže 2 DIP navíc z `MinHeight` jdou celé pod text. Naměřeno: Uložit/Zrušit text 498–507 (dřív 500–509) při tlačítku
   488–519 – text se posunul s horní hranou; Testovat připojení text 348–358 v tlačítku 339–370. Dřív byl text
   0,5 DIP nad středem, teď 1,5 DIP. Okem téměř nepostřehnutelné; na Windows se Segoe UI (přirozená výška asi 32 DIP)
   se pravděpodobně neprojeví vůbec. Návrh opravy: do stejného stylu přidat
   `<Setter Property="VerticalContentAlignment" Value="Center" />` (lokální hodnoty mají přednost: obrázek tiskárny
   má `Stretch`, odkaz Vychladnutí `Center`), znovu přegenerovat macOS snímky. Rozhodnutí viz Q2.

Nebylo ověřeno: skutečné Windows (Segoe UI, 125/150 % DPI) a skutečný macOS mimo headless – čeká na snímky z Windows.

Předchozí vlastní kontrola ui-dev (2026-10-30) podle kritérií úkolu:
výšky tlačítek ≥ 32 DIP všude (test), mezery na mřížce 4/8 se nezměnily (mění se jen výška tlačítek), žádné tlačítko se
nedotýká sousedního prvku (nejmenší mezera 4 DIP u tlačítek v záhlaví oznámení, nález 1 T-101 platí dál), vnitřní
tlačítka šablon beze změny. Případ 10 widgetů vyřešen variantou A z Q1.

Podklady: revize ui-designer v `tasks/in-progress/T-101-test-notification.md` (sekce `### Revize ui-designer
(2026-10-30)`, nálezy 1 a 2, otázka Q6). Závislost na T-101, T-109 a T-110 je jen kvůli pořadí: všechny tři mění
`SettingsWindow.axaml` a snímky `settings-*`, takže by se jinak snímky přegenerovávaly dvakrát a vznikaly by
konflikty při slučování.
