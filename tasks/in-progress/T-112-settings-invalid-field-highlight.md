---
id: T-112
title: Okno Nastavení – zvýraznění chybného pole a přístupný stav chyby
status: in-progress
owner: ui-designer + ui-dev
priority: P3
platform: all
depends_on: [T-101, T-109, T-110, T-111]
---

## Cíl
Při chybné hodnotě patička okna píše „Některé hodnoty nejsou platné. Opravte zvýrazněná pole.“ (`SettingsFixErrors`).
Samotná pole se ale nijak nezvýrazní, jsou vizuálně stejná jako platná. Nejvíc to vadí tam, kde jedna chybová zpráva
patří ke dvěma polím (prahy trysky a podložky u widgetu Vychladnutí). Uživatel pak nepozná, které pole je špatně.
Zjistil to ui-designer při revizi T-109 (2026-10-30, nález 1 v
`tasks/in-progress/T-109-cooldown-nozzle-bed-thresholds.md`). Uživatel 2026-10-30 rozhodl řešit to samostatným úkolem
s jednotným vzorem pro všechny záložky okna.

Postup: nejdřív ui-designer napíše krátký design spec (sekce `## Návrh` v tomto úkolu), pak ho implementuje ui-dev.

## Současný stav (k 2026-10-30, `develop` 291d949)
- Vzor T-033: chyba (`TextBlock.error`, barva `SettingsErrorBrush`, `FontSize` 12) **nahradí nápovědu** svého pole na
  stejném místě, takže se výška nemění. Zobrazí se až po úpravě nebo pokusu o uložení (`RevealErrors()` /
  `_showErrors` v `PrinterEditorViewModel`). Na záložkách a v seznamu tiskáren / widgetů se objeví značka „!“
  (`TextBlock.marker`, `ShowsErrorMarker`, `ShowsConnectionErrorMarker`, `ShowsCameraErrorMarker`,
  `ShowsCameraWindowErrorMarker`, `ShowsWidgetsErrorMarker`).
- Pole s validací a jejich chybové texty:

  | Záložka | Pole (`AutomationId`) | Ovládací prvek | Chyba (`AutomationId`, vlastnost VM) |
  |---|---|---|---|
  | Tiskárny → Tiskárna | název | `TextBox` | `PrinterNameError`, `NameError` (přes sloupce názvu i obrázku) |
  | Tiskárny → Tiskárna | obrázek tiskárny | `ComboBox` + import | `ImageError` (chyba importu, ne validace hodnoty) |
  | Tiskárny → Tiskárna | `PrinterAddress` | `TextBox` | `PrinterAddressError`, `AddressError` |
  | Tiskárny → Tiskárna | `PrinterUserName` | `TextBox` | `PrinterUserNameError`, `UserNameError` |
  | Tiskárny → Tiskárna | `PrinterPassword` | `TextBox` (heslo) | `PrinterPasswordError`, `PasswordError` |
  | Tiskárny → Kamera | `PrinterCameraUrl` | `TextBox` | `PrinterCameraUrlError`, `CameraUrlError` |
  | Tiskárny → Okno kamery | normální velikost | editovatelný `ComboBox` | `PrinterCameraWindowNormalSizeError`, `NormalSizeError` |
  | Tiskárny → Okno kamery | velikost malého náhledu | editovatelný `ComboBox` | `PrinterCameraWindowMiniSizeError`, `MiniSizeError` |
  | Widgety → Vychladnutí | práh trysky a práh podložky | 2× `NumericUpDown` | **jedna** `WidgetCooldownThresholdError`, `CooldownThresholdError` |
  | Obecné | `SettingsPollingInterval` | `NumericUpDown` | `SettingsPollingIntervalError`, `PollingIntervalError` |
  | Obecné | `SettingsRemainingTime` | `NumericUpDown` | `SettingsRemainingTimeError`, `RemainingTimeError` |

- Chyba není svázaná s polem pro čtečku obrazovky. Žádné pole nemá chybu v `AutomationProperties.HelpText` ani jiný
  stav „neplatné“, takže čtečka při fokusu na pole chybu nepřečte. Aplikace nepoužívá `INotifyDataErrorInfo` ani
  `DataValidationErrors` z Avalonie. Validace je vlastní ve view modelech.

## Rozsah
1. **Návrh (ui-designer):** jednotný vzor chybného pole pro celé okno: okraj (barva `SettingsErrorBrush`, tloušťka,
   chování při fokusu a najetí myší, světlý / tmavý motiv, vysoký kontrast Windows), umístění a znění chyby pro
   skupinu polí s jednou zprávou (Vychladnutí) a přístupnost (co přečte VoiceOver / Narrator). Návrh nesmí měnit výšku
   řádků (záložky bez posuvníku, T-033, výchozí velikost 780 × 532, T-094). Podle odpovědí na Otevřené otázky.
2. **Implementace (ui-dev):**
   - View modely: vlastnosti `Has…Error` pro každé pole z tabulky, které platí jen při `_showErrors` (stejně jako
     texty chyb). U Vychladnutí oddělené `HasNozzleThresholdError` / `HasBedThresholdError` (návrh z revize T-109).
   - XAML: chybné pole dostane třídu (např. `Classes.invalid="{Binding Has…Error}"`) se stylem okraje z návrhu,
     společně definovaným v `SettingsWindow.axaml` pro `TextBox`, `NumericUpDown` i editovatelný `ComboBox`. Platí
     i pro `WidgetSettingsTemplates.axaml`.
   - Přístupnost: chybné pole předá text chyby čtečce podle návrhu (např. `AutomationProperties.HelpText` = chyba
     místo nápovědy, nebo jiná vlastnost automatizace, kterou Avalonia 12 podporuje – ověřit z dokumentace / zdrojů
     Avalonie a uvést zdroj).
   - Pokud se rozhodne pro chybu se jménem pole (Otázka 1, varianta B/C), přidat nové řetězce do **všech**
     `Strings.*.resx` (en, cs, de, es, fr, it, pl, sk).
   - Jen kompilované bindingy, bez reflexe (Native AOT). Žádná nová závislost.

## Akceptační kritéria
- [x] Sekce `## Návrh` od ui-designera je zapsaná v tomto úkolu (vzor okraje, stavy, motivy, přístupnost, Vychladnutí)
      dřív, než začne implementace.
- [x] Každé pole z tabulky výše se při zobrazené chybě odliší podle návrhu (headless test ověří třídu nebo okraj
      v barvě `SettingsErrorBrush`). Po opravě hodnoty se zvýraznění odstraní. Platné pole zvýrazněné není, stejně
      jako chybné pole před `RevealErrors()`.
- [x] Vychladnutí: při chybě jen v prahu podložky je zvýrazněné jen pole podložky, při chybě jen v prahu trysky jen
      pole trysky, při obou chybách obě pole. Text chyby odpovídá rozhodnutí v Otázce 1.
- [x] Při fokusu na chybné pole čtečka obrazovky dostane text chyby (headless test čte vlastnost automatizace
      zvolenou v návrhu). U platného pole dostane nápovědu jako dnes.
- [x] Zvýraznění nemění výšku ani šířku pole. `EverythingFitsWithoutScrollingInEveryState`,
      `ErrorReplacesTheHintOfItsFieldOnly`, `PollingIntervalErrorReplacesItsHint`,
      `SaveWithACameraErrorOpensTheCameraSubTabWithItsMarker`, `MarkersOnAllSubTabHeadersStillFitOnOneRow` a testy
      záložky Widgety projdou ve všech jazycích. Okno zůstává 780 × 532 bez posuvníku.
- [x] Nový headless test, který pro každé pole z tabulky vyvolá chybu a ověří zvýraznění i stav pro čtečku. Při
      přidání nového validovaného pole bez zvýraznění test selže (např. kontrolou, že každý viditelný `TextBlock.error`
      má zvýrazněné pole).
- [x] Barva okraje má kontrast aspoň 3 : 1 k pozadí ve světlém i tmavém motivu (WCAG 2.2, 1.4.11). Výpočet uvede
      ui-designer v návrhu.
- [x] Nové nebo změněné řetězce jsou ve všech `Strings.*.resx` (en, cs, de, es, fr, it, pl, sk), žádný nechybí.
- [ ] Snímky: macOS sada (`tests/LayerPeek.App.UiTests/Snapshots/macOS/`) přegenerována a prohlédnuta zde
      (`LAYERPEEK_UPDATE_SNAPSHOTS=1 dotnet test --project tests/LayerPeek.App.UiTests`). Nové snímky chybových stavů
      aspoň: Tiskárna (adresa), Okno kamery (velikost), Vychladnutí (jen podložka, fr), Obecné, tmavý motiv. Windows
      sadu (`Snapshots/`) přegeneruje uživatel ručně na Windows. Seznam změněných a nových souborů se zapíše do
      Poznámek k implementaci. *(macOS hotovo 2026-10-30, Windows čeká na uživatele.)*
- [x] Revize ui-designer po implementaci zapsaná v úkolu. *(2026-10-30, viz `### Revize ui-designer (po implementaci)`.)*
- [x] **Bezpečné testy:** žádný test nesahá na skutečný profil uživatele (`settings.json`, `camera-windows.json`,
      `windows.json`, logy v `%APPDATA%` / `~/Library/Application Support/LayerPeek`). Headless testy běží
      v `SettingsWindowHarness` s úložištěm v paměti nebo ve vyhrazených testovacích složkách (`TestDirectories` /
      `ReservedTempDirectory`). Případné spuštění aplikace jen s `LAYERPEEK_TEST_DATA_DIR`
      a `LAYERPEEK_INSTANCE_SUFFIX` (Debug). Žádná síť mimo loopback.
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Návrh
Autor: ui-designer, 2026-10-30. Vychází z rozhodnutí Q1 A, Q2 A, Q3 A a Q4 A. Podklady: XAML `SettingsWindow.axaml`
a `WidgetSettingsTemplates.axaml` na `develop` 34d44d4, šablony Fluent motivu Avalonia 12.1.4 (zdroje níže) a macOS snímky
`settings-validation`, `settings-errors-fr`, `settings-camera-window-errors-fr`, `settings-widgets-cooldown-error-fr`,
`settings-general-errors` a `settings-widgets-dark`. Pixely jsem změřil přímo v PNG: běžný okraj pole má 1 px, ve světlém
motivu `#666666`, v tmavém `#999999`, a pozadí okna je `#FFFFFF` / `#000000`.

### 1. Vzor chybného pole (všechny záložky stejně)
- **Okraj:** 2 px v `SettingsErrorBrush` (světlý `#C42B1C`, tmavý `#FF99A4`, beze změny) kolem **celého** ovládacího
  prvku. U `NumericUpDown` to zahrnuje i tlačítka ▲▼, u editovatelného `ComboBox` i šipku. Poloměr rohů zůstává
  `ControlCornerRadius` (3). Vnitřní dělicí čáry `NumericUpDown` (text | ▲ | ▼) zůstanou v barvě motivu 1 px, červený
  je jen vnější obrys.
- **Proč 2 px a ne 1 px:** červená `#C42B1C` má k šedé `#666666` běžného okraje jasový kontrast jen 1,01 : 1, v tmavém
  motivu `#FF99A4` k `#999999` 1,40 : 1. Chybné a platné pole by se tak lišila skoro jen odstínem. To je problém
  hlavně u Vychladnutí, kde jedna zpráva patří ke dvěma polím (WCAG 1.4.1 Use of Color). Tloušťka 2 px proti 1 px je
  druhá, nebarevná odlišnost.
- **Stavy:** chybné pole vypadá stejně v klidu, při najetí myší, při stisku a při otevřeném seznamu: okraj 2 px
  červený, pozadí a text beze změny proti Fluent motivu. Červená má přednost před barvou najetí
  (`TextControlBorderBrushPointerOver`) i před akcentem fokusu (`TextControlBorderBrushFocused`).
- **Fokus + chyba:** okraj zůstává červený 2 px (akcentová modrá se nepoužije). Na fokus upozorní **kurzor pro psaní**
  (všechna validovaná pole jsou textová, i `NumericUpDown` a editovatelný `ComboBox` mají vnitřní `TextBox`)
  a u příchodu klávesou Tab i výběr textu. WCAG 2.4.7 (Understanding) uvádí kurzor v textovém poli jako platný indikátor
  fokusu. Stejně se dnes chová editovatelný `ComboBox` i bez chyby: jeho vnitřní `TextBox` má
  `TextControlBorderThemeThicknessFocused` = 0, takže fokus ukazuje jen kurzor. Totéž dělá vestavěný stav `:error`
  Fluent motivu Avalonie: jeho styl stojí za stylem `:focus`, takže chybová barva přebije akcent. Zvažoval jsem modrou při
  fokusu a červenou mimo fokus. Zamítl jsem to, protože u Vychladnutí by právě upravované chybné pole přestalo být
  odlišené.
- **Zakázané pole:** podle VM nemůže mít zobrazenou chybu. Jméno uživatele je zakázané jen u Prusa Connect a jeho chyba
  platí jen pro PrusaLink. Pole okna kamery jsou zakázaná jen při vypnuté kameře, kdy chyby velikostí nejsou. Pro
  `:disabled` proto žádné zvláštní pravidlo není. Test ověří, že zakázané pole třídu `invalid` nemá.
- **Bez posunu rozložení:** okraj se kreslí **dovnitř** stávajícího okraje šablony, výška ani šířka pole se nemění
  a text v poli se neposune. Chyba dál nahrazuje nápovědu na stejném místě (T-033, Q2 A). Okno zůstává 780 × 532 bez
  posuvníku ve všech 8 jazycích.
- **Heslo:** `PrinterPassword` je `TextBox` s `PasswordChar` a bez tlačítka pro zobrazení (`revealPasswordButton` se
  nepoužívá), takže platí totéž co pro `TextBox`. Kdyby se tlačítko přidalo, leží uvnitř okraje (`InnerRightContent`)
  a vzor se nemění.
- **Obrázek tiskárny** (`ImageError`): nejde o validaci hodnoty, ale o výsledek importu ve stavovém řádku dole. Neblokuje
  uložení, takže se nezvýrazňuje. Tlačítko obrázku zůstává beze změny.

### 2. Kontrast okraje (WCAG 2.2, 1.4.11, požadavek ≥ 3 : 1)
Vzorec relativního jasu podle WCAG. Pozadí pole je stejné jako pozadí okna (Fluent `TextControlBackground` je
`AltMediumLow`, tedy 40 % bílé na bílé nebo 40 % černé na černé), takže okraj sousedí z obou stran se stejnou barvou.

| Motiv | Okraj | Pozadí | Kontrast |
|---|---|---|---|
| světlý | `#C42B1C` | `#FFFFFF` (okno / pole, Avalonia Fluent) | **5,66 : 1** |
| světlý | `#C42B1C` | `#F3F3F3` (rezerva pro světlejší šedé plochy) | 5,10 : 1 |
| tmavý | `#FF99A4` | `#000000` (okno / pole, Avalonia Fluent) | **10,34 : 1** |
| tmavý | `#FF99A4` | `#202020` (rezerva, tmavé plochy Windows 11) | 8,03 : 1 |
| tmavý | `#FF99A4` | `#2C2C2C` | 6,88 : 1 |

Splněno s rezervou. Text chyby (`TextBlock.error`, 12 px, potřebuje 4,5 : 1) má stejné hodnoty, takže splňuje i 1.4.3.

**Vysoký kontrast Windows:** aplikace ani Fluent motiv Avalonie 12 nemají slovník pro vysoký kontrast (v `src/` není
`HighContrast` ani `ContrastPreference`). V režimu vysokého kontrastu se proto použijí barvy světlého nebo tmavého motivu.
Odlišení chybného pole tloušťkou 2 px přitom zůstane. Plná podpora systémových barev je mimo rozsah úkolu (Otázka 7).
Neověřeno na skutečném Windows.

### 3. Drátěné modely
Legenda: `┌─┐` běžný okraj 1 px, `╔═╗` chybný okraj 2 px `SettingsErrorBrush`, `|` kurzor, `!` značka chyby. Rozměry
jsou beze změny proti dnešku.

Tiskárny → Tiskárna, po Uložit s chybou v adrese a jménu uživatele (fokus na Adresa):
```
 Tiskárny  Widgety  Obecné  Spuštění a aktualizace
┌──────────────┐  ! Tiskárna   Kamera   Okno kamery
│▣ ! MK4       │  ┌────┐ Název                  Připojení
│              │  │ img│ ┌──────────────────┐   ┌───────────────────────┐
│              │  └────┘ │MK4               │   │PrusaLink (místní síť) v│
│              │         └──────────────────┘   └───────────────────────┘
│              │  Adresa
│              │  ╔═══════════════════════════════════════════════════════╗
│              │  ║|192.168.1.20 (zástupný text)                          ║  <- fokus
│              │  ╚═══════════════════════════════════════════════════════╝
│              │  Zadejte adresu tiskárny.                (červeně, místo nápovědy)
│              │  Jméno uživatele              Heslo
│              │  ╔════════════════════════╗   ┌────────────────────────┐
│              │  ║ma:ker                  ║   │                        │
│              │  ╚════════════════════════╝   └────────────────────────┘
│              │  Jméno nesmí obsahovat …      Heslo PrusaLink z nastavení tiskárny.
└──────────────┘  [Otestovat připojení]
 Některé hodnoty nejsou platné. Opravte zvýrazněná pole.        [Uložit] [Zrušit]
```

Okno kamery (editovatelné `ComboBox`), chyba jen v normální velikosti:
```
                   Velikost              Poloha             Vždy navrchu
 Normální okno     ╔═══════════════╗     ┌───────────────┐      ☐
                   ║100 × 50     v ║     │Uprostřed    v │
                   ╚═══════════════╝     └───────────────┘
                   Zadejte šířku od 160 do 16384 a výšku od 90 do 16384.
 Malý náhled       ┌───────────────┐     ┌───────────────┐      ☑
                   │320 × 180    v │     │Vpravo dole  v │
                   └───────────────┘     └───────────────┘
```

Widgety → Vychladnutí, chyba jen v prahu podložky (text chyby zůstává společný, Q1 A):
```
 Tryska (°C)            Podložka (°C)
 ┌──────┬───┬───┐       ╔══════╤═══╤═══╗     [Použít prahy pro všechny tiskárny]
 │40    │ ^ │ v │       ║120   │ ^ │ v ║
 └──────┴───┴───┘       ╚══════╧═══╧═══╝
 Zadejte celé číslo od 30 do 100.          (červeně, místo nápovědy)
```
Při chybě jen v trysce je červené jen pole trysky, při obou chybách obě pole. Zpráva je jedna a stejná. Pole se odliší
okrajem a čtečka dostane text chyby u každého chybného pole zvlášť (oddíl 5).

Obecné (`NumericUpDown`), chyba v intervalu dotazování:
```
 Interval dotazování (sekundy)
 ╔══════════╤═══╤═══╗
 ║|0        │ ^ │ v ║   <- fokus po Uložit
 ╚══════════╧═══╧═══╝
 Zadejte číslo od 1 do 300.
```

### 4. Fokus při Uložit s chybou (Q3 A)
Pořadí přesně kopíruje dnešní výběr záložky v `SettingsViewModel.SaveAsync`. Fokus dostane **první chybné pole
v pořadí čtení (Tab) v pohledu, na který se okno přepne**:
1. první tiskárna se zobrazenou chybou v pořadí seznamu, v ní první podzáložka s chybou:
   - **Tiskárna:** Název → Adresa → Jméno uživatele → Heslo,
   - **Kamera:** Adresa kamery,
   - **Okno kamery:** Normální velikost → Velikost malého náhledu,
   - jinak **Widgety** → vybraný widget Vychladnutí (`SelectFirstWidgetWithError`): Tryska → Podložka;
2. jinak **Obecné:** Interval dotazování → Zbývající čas.

Pravidla:
- Fokus jde do textové části pole: u `NumericUpDown` do `PART_TextBox`, u editovatelného `ComboBox` do
  `PART_EditableTextBox` (ten má `IsTabStop=False` na sobě a fokus drží vnitřní `TextBox`). Fokus se nastaví
  s `NavigationMethod.Tab`, aby se pole chovalo jako při příchodu klávesou Tab (`:focus-visible`, výběr textu podle
  motivu).
- Fokus se nastaví až po přepnutí záložky a vykreslení obsahu (např. `Dispatcher.UIThread.Post(…,
  DispatcherPriority.Loaded)`). Pokud pole v té chvíli není viditelné nebo zapnuté, fokus se nemění a zůstane jen
  stav v patičce jako dnes.
- Stejně se chová **Použít** v okně kamery (`SaveAsync(applying)`), protože jde o tutéž cestu. Pro Otestovat
  připojení / Otestovat kameru viz Otázka 5.
- Jak má VM o fokus požádat, rozhodne ui-dev. Návrh: událost nebo příkaz s `AutomationId` pole, okno najde prvek
  průchodem vizuálního stromu. Bez reflexe, bez nové závislosti.

### 5. Přístupnost: co dostane čtečka
**Mechanismus: `AutomationProperties.HelpText` na poli = text chyby, když je pole chybné, jinak viditelná nápověda
pole (nebo nic).** Text chyby se tak přečte při fokusu spolu s názvem a hodnotou. Ověřeno ve zdrojích Avalonie 12.1.4:
- `ControlAutomationPeer.GetHelpTextCore()` vrátí `AutomationProperties.HelpText`. Když je prázdné, zkusí
  `DataValidationErrors`, pak `ToolTip.Tip` a nakonec zástupný text
  (`src/Avalonia.Controls/Automation/Peers/ControlAutomationPeer.cs`).
- Windows: UIA `HelpText` (`UiaPropertyId.HelpText` v `src/Windows/Avalonia.Win32.Automation/AutomationNode.cs`).
  Narrator i NVDA ho čtou při fokusu jako popis.
- macOS: `accessibilityHelp` (`native/Avalonia.Native/src/OSX/automation.mm`). VoiceOver ho čte jako nápovědu po
  krátké prodlevě, pokud uživatel nemá nápovědy vypnuté.
- Zamítnuto: `AutomationProperties.ItemStatus`. Dokumentace v `AutomationProperties.cs` uvádí „This property currently
  has no effect“. Na Windows se sice mapuje na UIA ItemStatus, na macOS se ale nemapuje vůbec. UIA `IsDataValidForForm`
  ani ARIA `aria-invalid` Avalonia 12 nemá. Vestavěné `DataValidationErrors` vylučuje Q4 A.
- Zdroje (tag `release/12.1.4`):
  https://github.com/AvaloniaUI/Avalonia/blob/release/12.1.4/src/Avalonia.Controls/Automation/AutomationProperties.cs,
  …/src/Avalonia.Controls/Automation/Peers/ControlAutomationPeer.cs,
  …/src/Windows/Avalonia.Win32.Automation/AutomationNode.cs,
  …/native/Avalonia.Native/src/OSX/automation.mm.

**Důležité, nález z šablon:** u `NumericUpDown` a editovatelného `ComboBox` drží klávesový fokus **vnitřní** `TextBox`
šablony (`PART_TextBox` / `PART_EditableTextBox`). Jeho peer nemá `Name` ani `HelpText`, protože
`TextBoxAutomationPeer` je nedědí od rodiče. Čtečka proto při fokusu pravděpodobně nepřečte název ani chybu (týká se to
i dnešního `HelpText` u prahů Vychladnutí). Návrh: jeden společný styl v `SettingsWindow.axaml` předá vnitřnímu
`TextBox` `AutomationProperties.Name` i `AutomationProperties.HelpText` z rodičovského prvku. Například
`NumericUpDown /template/ TextBox#PART_TextBox` a `ComboBox /template/ TextBox#PART_EditableTextBox` se setterem
`{TemplateBinding AutomationProperties.HelpText}`, nebo kompilovaným `$parent[…]` bindingem. Pak se přečte totéž, ať
čtečka hlásí vnější prvek nebo vnitřní `TextBox`. Headless test proto čte peer **skutečně fokusovaného prvku**
(`FocusManager.GetFocusedElement()`), ne vnějšího ovládacího prvku.

Obsah `HelpText` pro jednotlivá pole (vlastnost VM počítá ui-dev, např. `AddressHelpText => AddressError ??
(ShowsAddressHint ? hint : null)`):

| Pole | Chybné | Platné |
|---|---|---|
| `PrinterName` | `NameError` | – (nemá nápovědu) |
| `PrinterAddress` | `AddressError` | `SettingsAddressHint`, pokud je vidět |
| `PrinterUserName` | `UserNameError` | `UserNameHint`, pokud je vidět |
| `PrinterPassword` | `PasswordError` | `PasswordHint`, pokud je vidět |
| `PrinterCameraUrl` | `CameraUrlError` | `SettingsCameraUrlHint`, pokud je vidět |
| `PrinterCameraWindowNormalSize` / `MiniSize` | `NormalSizeError` / `MiniSizeError` | – |
| `WidgetCooldownNozzleThreshold` / `BedThreshold` | `CooldownThresholdError`, jen u chybného z nich | `SettingsCooldownThresholdHint` (jako dnes) |
| `SettingsPollingInterval` | `PollingIntervalError` | `SettingsPollingIntervalHint`, pokud je vidět |
| `SettingsRemainingTime` | `RemainingTimeError` | – |

Kde je v tabulce „–“, `HelpText` je `null` a Avalonia vrátí zástupný text pole (`PlaceholderText`), pokud nějaký má.
Platné pole tak dostane nápovědu jako dnes nebo lépe, protože viditelné nápovědy jsou dnes čtečce skryté.
Když se chyba objeví při psaní, změnu `HelpText` peer neoznámí (`OwnerPropertyChanged` hlásí jen `IsVisible`, `Bounds`,
`ItemStatus` a `AutomationId`). Čtečka ji přečte při dalším fokusu, nebo hned po Uložit díky přesunu fokusu (oddíl 4).
Živé ohlášení je Otázka 6.

### 6. Patička a řetězce
- `SettingsFixErrors` („Některé hodnoty nejsou platné. Opravte zvýrazněná pole.“) **zůstává beze změny**. S tímto
  návrhem je konečně pravdivá. Totéž platí pro stejný text ve výsledku Otestovat připojení.
- **Žádné nové ani změněné řetězce** (Q1 A). `.resx` se nemění.

### 7. Implementační vodítka (jen styly a bindingy, AOT bezpečné, bez nových balíčků)
Styly v `Window.Styles` okna `SettingsWindow.axaml` platí i pro obsah šablon z `WidgetSettingsTemplates.axaml`, protože
se vykreslují uvnitř okna. Okraj se nastaví na **prvek šablony**, ne na `BorderThickness` ovládacího prvku. Ten je
přes `TemplateBinding` i ve vnitřních okrajích a ve výpočtu velikosti:
- `TextBox.invalid /template/ Border#PART_BorderElement`: `BorderBrush` = `SettingsErrorBrush`, `BorderThickness` = 2.
  Vnitřní obsah má `Margin="{TemplateBinding BorderThickness}"` (1), takže text se neposune.
- `ComboBox.invalid /template/ Border#Background` a `ComboBox.invalid /template/ Border#HighlightBackground` (překryv při
  `:focus-visible`): `BorderBrush` = `SettingsErrorBrush`, `BorderThickness` = 2. Oba prvky nemají potomka, takže se
  rozměr nezmění.
- `NumericUpDown.invalid`: `BorderThickness` vnitřního `ButtonSpinner#PART_Spinner` vstupuje do měření okraje.
  Při 2 px by pole narostlo o 2 px: vnitřní `TextBox` má `MinHeight` 32 a `Margin -1`. Tloušťku je proto nutné
  vyrovnat, například `PART_TextBox` `Margin -2`, a dělicí čáry tlačítek `/template/ RepeatButton` vrátit na
  `TextControlBorderBrush` 1 px. Druhá možnost je jiný způsob vykreslení bez změny měření, volbu nechávám na ui-dev.
  Vnitřní `PART_TextBox` při fokusu kreslí vlastní akcentový rámeček 2 px přes levou, horní a dolní hranu obrysu. Ve
  stavu `.invalid` musí být červený nebo skrytý. Ověřit snímkem `settings-general-errors-focused`.
- Pořadí stylů: vlastní styly okna mají v Avalonii přednost před `ControlTheme`, takže `.invalid` přebije `:pointerover`
  i `:focus` motivu. ui-dev to ověří testem štětce okraje ve stavu fokusu a najetí myší (headless vstup myši).
- `Classes.invalid="{Binding Has…Error}"`, kde `Has…Error` platí přesně tehdy, když je vidět text chyby (stejná
  podmínka `_showErrors`).
- Jen kompilované bindingy, žádná reflexe, žádné `UnconditionalSuppressMessage`.

### 8. Testy a snímky
Headless testy (ui-dev):
- teorie přes 11 polí z tabulky (bez `ImageError`): před `RevealErrors()` třída chybí; po Uložit má pole třídu
  `invalid` a okraj šablony v barvě `SettingsErrorBrush` o tloušťce 2; po opravě hodnoty třída zmizí;
- **ochrana proti zapomenutému poli:** pro každý viditelný `TextBlock.error` existuje pole s třídou `invalid`. Párování
  podle `AutomationId`: chyba = pole + `Error` (`PrinterAddressError` ↔ `PrinterAddress` atd.), jediná výjimka je
  `WidgetCooldownThresholdError` ↔ aspoň jedno z `WidgetCooldownNozzleThreshold` / `WidgetCooldownBedThreshold`;
- Vychladnutí: jen podložka / jen tryska / obě;
- `Bounds` každého pole (a pozice jeho textové části) jsou stejné v platném i chybném stavu, a to i při fokusu.
  Stávající testy rozložení (`EverythingFitsWithoutScrollingInEveryState` atd.) dál projdou v 8 jazycích;
- fokus po Uložit podle pořadí z oddílu 4 (adresa + heslo → Adresa; jen Okno kamery → vnitřní `TextBox` normální
  velikosti; jen Obecné → Interval dotazování). `HelpText` peeru **fokusovaného** prvku = text chyby. Platné pole má
  nápovědu podle tabulky z oddílu 5;
- determinismus snímků: po Uložit bude ve snímcích fokus a kurzor. ui-dev zajistí, aby blikání kurzoru nedělalo snímky
  nestabilními (headless bez běžícího času, nebo pevně daná fáze kurzoru).

Snímky macOS (`Snapshots/macOS/`), přegenerovat a prohlédnout:
- **změněné:** `settings-validation`, `settings-validation-cs`, `settings-errors-fr`, `settings-camera-window-errors-fr`,
  `settings-widgets-cooldown-error-fr` (jen podložka, fr), `settings-general-errors`;
- **nové:**
  - `settings-invalid-address-focused`: en, světlý, jen chybná adresa, fokus na ní (fokus + chyba, `TextBox`);
  - `settings-camera-window-error-normal-size`: en, jen normální velikost, editovatelný `ComboBox` vedle platného;
  - `settings-widgets-cooldown-error-nozzle`: en, jen tryska;
  - `settings-general-errors-focused`: fokus v chybném `NumericUpDown`, kontrola akcentového rámečku vnitřního
    `TextBox`;
  - `settings-errors-dark`: Tiskárna v tmavém motivu (adresa, jméno, heslo);
  - `settings-general-errors-dark`: `NumericUpDown` v tmavém motivu.
- Windows sadu (`Snapshots/`) přegeneruje uživatel na Windows.

### 9. Platformy a co není ověřené
- Windows (Fluent): červený okraj a text chyby pod polem odpovídají chybovému stavu vstupních polí Fluent i vestavěnému
  `:error` v Avalonii. Pořadí Uložit / Zrušit se nemění.
- macOS (HIG): chyba přímo u pole a fokus na první chybné pole odpovídají doporučení řešit chybu tam, kde vznikla. Vzhled
  je Fluent z Avalonie na obou systémech (stejně jako zbytek okna).
- Neověřeno: skutečný Narrator / NVDA / VoiceOver (jen headless peer), skutečné DPI na Windows (125 / 150 / 175 %,
  zaokrouhlení 2 px okraje) a vysoký kontrast Windows.

## Otevřené otázky
**Rozhodnuto 2026-10-30 (uživatel): Q1 A (červený rámeček, text chyby beze změny), Q2 A (chyba místo nápovědy, T-033), Q3 A (fokus na první neplatné pole), Q4 A (vlastní třída `invalid` řízená vlastnostmi `Has…Error`)** – vše podle doporučení.
**Rozhodnuto 2026-10-30 (uživatel): Q5 A (fokus i po Otestovat připojení / kameru v rámci aktuální podzáložky), Q6 A (bez živého ohlášení), Q7 A (vysoký kontrast mimo rozsah).**

1. **Jak označit chybné pole?** (blokuje návrh)
   - (A, doporučeno) **Okraj pole** v `SettingsErrorBrush`, text chyby zůstává na místě nápovědy jako dnes (vzor T-033).
     U Vychladnutí se zvýrazní jen chybné pole, společná zpráva zůstane beze změny. Nejmenší zásah, žádné nové
     řetězce, výšky se nemění. Doporučil to ui-designer v revizi T-109.
   - (B) Okraj **a** text chyby se jménem pole jen tam, kde jedna zpráva patří k více polím (Vychladnutí: „Podložka:
     zadejte celé číslo od 30 do 100.“; při obou chybách stávající text). Nové řetězce v 8 jazycích.
   - (C) Jen text chyby se jménem pole, bez okraje (varianta (b) z revize T-109). Nevyřeší, že patička mluví
     o „zvýrazněných polích“.
2. **Umístění zprávy:** (A, doporučeno) zachovat vzor T-033 – chyba nahradí nápovědu pod polem, výška se nemění /
   (B) chyba jako samostatný řádek pod polem vedle nápovědy (změní výšky, hrozí posuvník, vyžádá si přepracování
   záložek).
3. **Fokus při uložení s chybou:** (A, doporučeno) jen přepnout záložku / podzáložku jako dnes
   (`SaveWithACameraErrorOpensTheCameraSubTabWithItsMarker`) a navíc dát fokus prvnímu chybnému poli, aby čtečka
   chybu hned přečetla / (B) fokus neměnit (dnešní stav).
4. **Technika (rozhodne uživatel na návrh ui-dev):** (A, doporučeno) vlastní třída `invalid` řízená vlastnostmi
   `Has…Error` ve view modelech – navazuje na stávající vlastní validaci, žádná reflexe, AOT bez rizika / (B) přejít
   na `INotifyDataErrorInfo` a vestavěné `DataValidationErrors` Avalonie (Fluent okraj a text pod polem). Větší
   přestavba, mění vzor T-033 (text pod polem navíc), kompatibilitu s AOT a kompilovanými bindingy je nutné ověřit.

**Nové otázky z návrhu (ui-designer, 2026-10-30), čekají na rozhodnutí uživatele:**

5. **Fokus i u Otestovat připojení / Otestovat kameru?** Obě tlačítka při chybě zobrazí `SettingsFixErrors` ve výsledku
   testu a nic neotestují.
   - (A, doporučeno) Ano, stejné pravidlo jako u Uložit, jen v rámci aktuální podzáložky (Tiskárna: Název → Adresa →
     Jméno → Heslo, Kamera: Adresa kamery). Uživatel čtečky jinak po stisku neslyší, co je špatně.
   - (B) Ne, fokus zůstane na tlačítku testu (dnešní stav). Chybu čtečka přečte až při ručním přechodu na pole.
6. **Živé ohlášení chyby čtečkou** (`AutomationProperties.LiveSetting`)?
   - (A, doporučeno) Ne. Chyba se přečte při fokusu z `HelpText` a po Uložit se fokus přesune na pole (Q3). Nic
     nehlásí při každém úhozu.
   - (B) `LiveSetting="Polite"` na patičce `SettingsStatus`: po Uložit ohlásí „Opravte zvýrazněná pole“. Hrozí, že se
     ohlášení kryje se čtením pole s fokusem.
   - (C) `LiveSetting="Polite"` i na každém `TextBlock.error`: hlásí změnu chyby během psaní (podle Avalonie se ohlašuje
     změna `Name`, u `TextBlock` tedy textu). Upovídané, nedoporučuji.
7. **Vysoký kontrast Windows:** aplikace dnes nemá slovník pro vysoký kontrast.
   - (A, doporučeno) Mimo T-112: okraj 2 px zůstane i v režimu vysokého kontrastu. Systémové barvy (`SystemColors`)
     pro celé okno řešit případně samostatným úkolem.
   - (B) V T-112 přidat pro `SettingsErrorBrush` variantu pro vysoký kontrast. Avalonia 12 na to nemá hotový slovník
     motivu, takže by bylo nutné nejdřív ověřit `PlatformSettings.GetColorValues().ContrastPreference` a postup.

## Poznámky k implementaci
ui-dev, 2026-10-30, macOS (branch `feature/T-112-settings-invalid-field-highlight`).

**Start před dokončením závislostí:** uživatel 2026-10-30 rozhodl začít, i když T-101, T-109, T-110 a T-111 jsou ještě
v `tasks/in-progress/`. Jejich kód je v `develop` (e1183ed), čekají jen na snímky Windows / revizi ui-designer.

**Stav:** úkol zůstává v `in-progress`. Chybí jen Windows snímky (přegeneruje uživatel na Windows) a revize ui-designer.

Co je hotové (podle `## Návrh`, rozhodnutí Q1–Q7 vše A):
- **View modely:** `PrinterEditorViewModel.FieldStates.cs` – `HasNameError`, `HasAddressError`, `HasUserNameError`,
  `HasPasswordError`, `HasCameraUrlError`, `HasNormalSizeError`, `HasMiniSizeError`, `HasNozzleThresholdError`,
  `HasBedThresholdError` (platí přesně, když je vidět text chyby) a `…HelpText` podle tabulky z oddílu 5.
  `SettingsViewModel.FieldStates.cs` – `HasPollingIntervalError`, `HasRemainingTimeError`, `PollingIntervalHelpText`,
  `RemainingTimeHelpText`, událost `FieldFocusRequested` (automation id pole, konstanty v `SettingsFieldIds`). Stávající
  `HasCooldownThresholdError` (blokuje uložení, zobrazená i nezobrazená) zůstal beze změny.
- **Fokus (Q3, Q5):** `SaveAsync` (Uložit i Použít) po výběru záložky / podzáložky požádá o první chybné pole v pořadí
  z oddílu 4. Widgety jen tehdy, když je vybraný panel Vychladnutí. Odmítnuté Otestovat připojení / Otestovat kameru
  požádá o první chybné pole své podzáložky, jen když je ta podzáložka té tiskárny právě vidět. `SettingsWindow` pole
  najde průchodem vizuálního stromu podle `AutomationId` (bez reflexe) s `DispatcherPriority.Loaded`. Zaostří
  `PART_TextBox` / `PART_EditableTextBox` s `NavigationMethod.Tab`, jen když je pole viditelné a povolené.
- **Styly (`SettingsWindow.axaml`)**, části šablon ověřené výpisem vizuálního stromu v headless testu a jmény v
  `Avalonia.Themes.Fluent.dll` 12.1.4:
  - `TextBox.invalid /template/ Border#PART_BorderElement`: 2, `SettingsErrorBrush`.
  - `ComboBox.invalid /template/ Border#Background` a `Border#HighlightBackground`: 2, `SettingsErrorBrush`.
  - `NumericUpDown.invalid`: `ButtonSpinner#PART_Spinner` 2, `SettingsErrorBrush`. Vnitřní `TextBox#PART_TextBox`
    `Margin="-2,-2,0,-2"` (místo −1) a jeho `Border#PART_BorderElement` `2,2,0,2` v červené barvě, takže akcent fokusu
    nepřekreslí obrys a vpravo nekreslí čáru. Šablona odvozuje levý okraj tlačítek ▲ ▼ z `BorderThickness` spinneru,
    proto mají `RepeatButton` ve stavu `invalid` znovu `1,0,0,0` a `TextControlBorderBrush`.
  - Vnitřní `TextBox` v `NumericUpDown` a `ComboBox` dostane `AutomationProperties.Name` a `HelpText` rodiče
    kompilovaným bindingem `$parent[NumericUpDown]` / `$parent[ComboBox]`. `TemplateBinding` v obyčejném `Style`
    kompilátor XAML odmítne (AVLN3000).
- **Odchylka od drátěného modelu (pro revizi):** u chybného `NumericUpDown` jsou tlačítka ▲ ▼ uvnitř silnějšího obrysu
  o 2 DIP nižší (28 místo 30). Dělicí čára a šipky jsou o 1 DIP vlevo. Rozměr pole a poloha textu se nemění, ověřuje to test.
- **Řetězce:** žádné nové ani změněné (Q1 A), `.resx` beze změny. Bez `LiveSetting` (Q6 A), bez vysokého kontrastu (Q7 A).
- **Testy:** nový `tests/LayerPeek.App.UiTests/Settings/SettingsWindowInvalidFieldUiTests.cs` (43 testů):
  - teorie přes 11 polí: třída a obrys 2 DIP v `#C42B1C` po Uložit, při fokusu, při najetí myší a v klidu, po opravě
    zmizí; ochrana proti zapomenutému poli pro každou zobrazenou chybu;
  - neodhalená chyba nové tiskárny se nezvýrazní;
  - Vychladnutí: jen podložka, jen tryska, obě;
  - tmavý motiv (`#FF99A4`);
  - zakázané pole bez třídy (kamera vypnutá, Prusa Connect);
  - teorie přes 11 polí: `Bounds.Size` a poloha `PART_TextPresenter` stejné v platném i chybném stavu, s fokusem
    i bez něj;
  - fokus a `HelpText` peeru **fokusovaného** prvku po Uložit (adresa + heslo → Adresa, jen Okno kamery →
    `PART_EditableTextBox`, Obecné → `PART_TextBox` intervalu, Vychladnutí → podložka);
  - fokus po odmítnutém Otestovat připojení a Otestovat kameru;
  - nápovědy platných polí a změna jazyka;
  - ochrana ve všech 8 jazycích.

  Mutační kontrola: bez vyrovnání okraje `NumericUpDown` nebo bez `Classes.invalid` u hesla testy selžou.
- **Snímky a kurzor:** `SnapshotAssert.Matches` nejdřív vyřídí čekající práci (fokus po Uložit), pak fokus zruší.
  Nový `Matches(..., keepFocus: true)` fokus ponechá a `Ui.FreezeCaret` (interval blikání jeden den) zajistí stabilní
  kurzor.
- **Snímky macOS** (`Snapshots/macOS/`), přegenerované a prohlédnuté. Obrys jsem změřil v pixelech: 2 px `#C42B1C`,
  resp. `#FF99A4`; dělicí čáry `NumericUpDown` 1 px `#666666`, resp. `#999999`.
  - změněné: `settings-validation`, `settings-validation-cs`, `settings-errors-fr`, `settings-camera-window-errors-fr`,
    `settings-widgets-cooldown-error-fr`, `settings-general-errors`;
  - nové: `settings-invalid-address-focused`, `settings-camera-window-error-normal-size`,
    `settings-widgets-cooldown-error-nozzle`, `settings-general-errors-focused`, `settings-errors-dark`,
    `settings-general-errors-dark`.
- **Snímky Windows** (`Snapshots/`), přegeneruje uživatel na Windows (`LAYERPEEK_UPDATE_SNAPSHOTS=1 dotnet test
  --project tests/LayerPeek.App.UiTests`) a prohlédne:
  - změněné: `settings-validation.png`, `settings-validation-cs.png`, `settings-errors-fr.png`,
    `settings-camera-window-errors-fr.png`, `settings-widgets-cooldown-error-fr.png`, `settings-general-errors.png`;
  - nové (do té doby na Windows selžou jako chybějící reference): `settings-invalid-address-focused.png`,
    `settings-camera-window-error-normal-size.png`, `settings-widgets-cooldown-error-nozzle.png`,
    `settings-general-errors-focused.png`, `settings-errors-dark.png`, `settings-general-errors-dark.png`.
- **Ověřeno na macOS:** `dotnet build` i `dotnet build -c Release` bez varování. Celý `dotnet test`: 8070 testů,
  0 selhání, 182 přeskočených. Aplikace se nespouštěla (pravidlo bezpečného sandboxu), vše ověřují headless testy.
- **Neověřeno:** skutečný Narrator / NVDA / VoiceOver (jen headless peer), Windows, DPI 125–175 % a vysoký kontrast.

Podklady: revize ui-designer v `tasks/in-progress/T-109-cooldown-nozzle-bed-thresholds.md` (sekce `### Revize
ui-designer (2026-10-30)`, nálezy 1 a 2 – tlačítko „Použít prahy…“ zůstává při chybě povolené, beze změny).
Závislost na T-111 je kvůli pořadí: oba úkoly mění styly `SettingsWindow.axaml` a snímky `settings-*`.

### Revize ui-designer (po implementaci)
ui-designer, 2026-10-30, `develop` 3536a00 (commit f9705f6), macOS. Kód ani XAML jsem neměnil.

**Verdikt: schváleno.** Implementace odpovídá `## Návrh` (oddíly 1–8). Žádný nález neblokuje dokončení úkolu. Zbývá jen
Windows sada snímků (kritérium Snímky), kterou přegeneruje uživatel.

Co jsem prověřil:
- **Snímky macOS**, všech 12 (6 změněných, 6 nových), prohlédnuté celé. U změněných jsem je porovnal po pixelech s verzí
  před f9705f6. Zvětšené výřezy (6×–12×) jsem udělal u adresy, názvu (fr), `NumericUpDown` intervalu dotazování (v klidu,
  s fokusem, tmavý motiv) a u spojů obrysu s dělicí čarou.
- **Obrys 2 px dovnitř:** `TextBox` adresy má ve `settings-validation` obrys na y 181–182 a 211–212 a na x 216–217
  v `#C42B1C`. Předtím měl 1 px `#666666` na y 181 / 212. Vnější rozměr je tedy stejný (32 px) a glyfy placeholderu
  i textu jsou na stejných pixelech. Tmavý motiv: `#FF99A4` 2 px (`settings-errors-dark`,
  `settings-general-errors-dark`). Dlouhý text názvu (fr) se ořízne před obrysem, obrys nepřekreslí.
- **`NumericUpDown`:** pole má dál výšku 32 px (y 75–106) a šířku x 16–175. Číslice „0“ zůstává na x 27–34. Dělicí
  čára ▲ je 1 px `#666666` / `#999999` na x 106 (předtím 107), šipky jsou o 1 px vlevo, spoj s červeným obrysem
  nahoře i dole je čistý (T-spoj bez mezery). **Odchylku přijímám** (▲▼ uvnitř obrysu 28 místo 30 DIP, čára o 1 DIP
  vlevo): v zoomu není rozpoznatelná, ovládací prvek se nezvětšil a tlačítka byla pod 32 DIP už předtím (30, Fluent
  výchozí). Hlavní cesta je psaní a šipky klávesnice.
- **Fokus + chyba** (`settings-invalid-address-focused`, `settings-general-errors-focused`): obrys zůstává červený
  2 px, akcent fokusu se nikde neprokreslí (ani akcentový rámeček vnitřního `PART_TextBox`). Fokus ukazuje kurzor
  a u `NumericUpDown` výběr textu (`NavigationMethod.Tab`), jak návrh počítal.
- **Vychladnutí:** `settings-widgets-cooldown-error-fr` (jen podložka) a `…-nozzle` (jen tryska) zvýrazní jen chybné
  pole, společná zpráva je beze změny pod oběma poli. Chybné pole se tedy pozná i bez barvy (tloušťka).
- **Okno kamery:** `settings-camera-window-error-normal-size` a `…-errors-fr`: obrys celého editovatelného `ComboBox`
  včetně šipky, sousední platné pole zůstává 1 px šedé. Bez posuvníku i se dvěma dvouřádkovými chybami (fr).
- **Styly v `SettingsWindow.axaml`:** selektory míří na neměřené části šablon (`PART_BorderElement`, `Background`,
  `HighlightBackground`), u `ButtonSpinner` je vyrovnání okraje okomentované. Pořadí stylů okna proti
  `ControlTheme` drží červenou při najetí myší i fokusu, což kryjí testy. Přenos `Name` / `HelpText` na vnitřní
  `TextBox` přes kompilovaný `$parent[NumericUpDown]` / `$parent[ComboBox]` je AOT bezpečný. Důvod, proč ne
  `TemplateBinding` (AVLN3000), je zapsaný.
- **`dotnet test --project tests/LayerPeek.App.UiTests`:** 1172 / 1172 úspěšných (macOS, 2026-10-30).

Nálezy:

| # | Soubor / prvek | Závažnost | Popis | Oprava |
|---|---|---|---|---|
| 1 | `SettingsWindow.axaml`, `NumericUpDown.invalid /template/ TextBox#PART_TextBox /template/ Border#PART_BorderElement` | kosmetická, nízká | Na pravém konci vnitřního `TextBox` vzniká u horní a dolní hrany (x 103–105, y 77 a 104 ve `settings-general-errors*`) 2–3 px růžový antialiasing. Jde o vnitřní oblouk zaobleného rohu okraje `2,2,0,2`. Při 100 % není pouhým okem vidět, při 150–200 % na Windows může být patrný jako drobná skvrna u dělicí čáry. | Volitelně ve stejném stylu `CornerRadius="3,0,0,3"` (vpravo hranatý roh u dělicí čáry). Pak ověřit zoomem `settings-general-errors-focused`. Neblokuje, klidně později. |
| 2 | `SettingsWindow.axaml`, styly `NumericUpDown /template/ TextBox#PART_TextBox` a `ComboBox /template/ TextBox#PART_EditableTextBox` | informativní | Vnější prvek i vnitřní `TextBox` mají teď stejný `Name` / `HelpText`. Narrator v režimu procházení (scan mode) může název přečíst dvakrát (skupina + editační pole). Při fokusu klávesou se čte jen fokusovaný vnitřní prvek, tedy správně. | Nic teď. Ověřit při ruční zkoušce s Narratorem / VoiceOverem (stejně jako zbytek přístupnosti). |
| 3 | Windows sada `Snapshots/` | proces | Nové snímky na Windows zatím chybí (selžou jako chybějící reference). | Uživatel přegeneruje na Windows a prohlédne hlavně `settings-general-errors-focused` a `settings-camera-window-error-normal-size` (jiné vyhlazování písma i hran). |

Neověřeno (beze změny proti oddílu 9 návrhu): skutečný Narrator / NVDA / VoiceOver, Windows a DPI 125 / 150 / 175 %
(zaokrouhlení 2 px obrysu a spoje s 1 px dělicí čarou), vysoký kontrast Windows.

Nové produktové otázky: žádné.
