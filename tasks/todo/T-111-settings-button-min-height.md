---
id: T-111
title: Okno Nastavení – minimální výška tlačítek 32 DIP
status: todo
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
- [ ] Každé viditelné tlačítko okna Nastavení (všechny záložky a podzáložky ve výchozím stavu i ve stavech s chybou,
      výsledkem testu a varováním T-020 / T-097) má v headless testu `Bounds.Height >= 32`. Nový test ve
      `tests/LayerPeek.App.UiTests/Settings/SettingsWindowLayoutTests.cs` projde stromem a vypíše tlačítka, která
      podmínku nesplňují.
- [ ] Vnitřní tlačítka šablon (`NumericUpDown`, `ComboBox`, `TabItem`, `TextBox`) mají stejnou velikost jako před
      úpravou. Test nebo porovnání snímků to potvrdí.
- [ ] Stávající testy rozložení (`EverythingFitsWithoutScrollingInEveryState`,
      `GeneralTabWithTheTestNotificationFitsInEveryLanguage`, `GeneralTabWithNotificationsTurnedOffFitsInEveryLanguage`,
      `StartupAndUpdatesTabFitsInEveryLanguage`, `LocalNetworkHintWithItsButtonFitsInEveryLanguage`,
      `CameraLocalNetworkHintWithItsButtonFitsInEveryLanguage`, `MaximizedWindowStretchesWithoutScrollingOrCutTexts`
      a testy záložky Widgety) projdou ve všech jazycích beze změny výchozí velikosti okna 780 × 532.
- [ ] `WindowOpensAtTheDesignedSizeThatFitsTheSmallestScreen` a testy velikosti okna Diagnostika
      (`tests/LayerPeek.App.UiTests/Diagnostics/DiagnosticsWindowLayoutTests.cs`) projdou: obě okna mají dál stejnou
      výchozí velikost (T-041, T-094).
- [ ] Pokud se záložka do výchozí velikosti nevejde, úkol se **nezavírá**. Problém se zapíše do Otevřených otázek
      (zvětšit okno, nebo zhustit záložku – rozhodne uživatel).
- [ ] Snímky: macOS sada (`tests/LayerPeek.App.UiTests/Snapshots/macOS/`, `settings-*`) přegenerována a prohlédnuta zde
      (`LAYERPEEK_UPDATE_SNAPSHOTS=1 dotnet test --project tests/LayerPeek.App.UiTests`). Windows sadu
      (`Snapshots/`) přegeneruje uživatel ručně na Windows. Seznam změněných souborů se zapíše do Poznámek
      k implementaci. Do té doby snímkové testy na Windows selžou (stejně jako u T-103 / T-109 / T-110).
- [ ] Revize ui-designer zapsaná v úkolu, verdikt OK, nebo nálezy vyřešené či přesunuté do Otevřených otázek.
- [ ] **Bezpečné testy:** žádný test nesahá na skutečný profil uživatele (`settings.json`, `camera-windows.json`,
      `windows.json`, logy v `%APPDATA%` / `~/Library/Application Support/LayerPeek`). Headless testy běží ve svém
      harnessu (`SettingsWindowHarness`, `DiagnosticsWindowHarness`) s úložištěm v paměti nebo ve vyhrazených
      testovacích složkách (`TestDirectories` / `ReservedTempDirectory`). Případné spuštění aplikace jen
      s `LAYERPEEK_TEST_DATA_DIR` a `LAYERPEEK_INSTANCE_SUFFIX` (Debug). Žádná síť mimo loopback.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné – rozhodnuto 2026-10-30: styl `MinHeight` 32 pro celé okno, varianta A z T-101 Q6)

## Poznámky k implementaci
(vyplní agent po dokončení)

Podklady: revize ui-designer v `tasks/in-progress/T-101-test-notification.md` (sekce `### Revize ui-designer
(2026-10-30)`, nálezy 1 a 2, otázka Q6). Závislost na T-101, T-109 a T-110 je jen kvůli pořadí: všechny tři mění
`SettingsWindow.axaml` a snímky `settings-*`, takže by se jinak snímky přegenerovávaly dvakrát a vznikaly by
konflikty při slučování.
