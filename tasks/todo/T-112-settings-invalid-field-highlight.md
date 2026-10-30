---
id: T-112
title: Okno Nastavení – zvýraznění chybného pole a přístupný stav chyby
status: todo
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
- [ ] Sekce `## Návrh` od ui-designera je zapsaná v tomto úkolu (vzor okraje, stavy, motivy, přístupnost, Vychladnutí)
      dřív, než začne implementace.
- [ ] Každé pole z tabulky výše se při zobrazené chybě odliší podle návrhu (headless test ověří třídu nebo okraj
      v barvě `SettingsErrorBrush`). Po opravě hodnoty se zvýraznění odstraní. Platné pole zvýrazněné není, stejně
      jako chybné pole před `RevealErrors()`.
- [ ] Vychladnutí: při chybě jen v prahu podložky je zvýrazněné jen pole podložky, při chybě jen v prahu trysky jen
      pole trysky, při obou chybách obě pole. Text chyby odpovídá rozhodnutí v Otázce 1.
- [ ] Při fokusu na chybné pole čtečka obrazovky dostane text chyby (headless test čte vlastnost automatizace
      zvolenou v návrhu). U platného pole dostane nápovědu jako dnes.
- [ ] Zvýraznění nemění výšku ani šířku pole. `EverythingFitsWithoutScrollingInEveryState`,
      `ErrorReplacesTheHintOfItsFieldOnly`, `PollingIntervalErrorReplacesItsHint`,
      `SaveWithACameraErrorOpensTheCameraSubTabWithItsMarker`, `MarkersOnAllSubTabHeadersStillFitOnOneRow` a testy
      záložky Widgety projdou ve všech jazycích. Okno zůstává 780 × 532 bez posuvníku.
- [ ] Nový headless test, který pro každé pole z tabulky vyvolá chybu a ověří zvýraznění i stav pro čtečku. Při
      přidání nového validovaného pole bez zvýraznění test selže (např. kontrolou, že každý viditelný `TextBlock.error`
      má zvýrazněné pole).
- [ ] Barva okraje má kontrast aspoň 3 : 1 k pozadí ve světlém i tmavém motivu (WCAG 2.2, 1.4.11). Výpočet uvede
      ui-designer v návrhu.
- [ ] Nové nebo změněné řetězce jsou ve všech `Strings.*.resx` (en, cs, de, es, fr, it, pl, sk), žádný nechybí.
- [ ] Snímky: macOS sada (`tests/LayerPeek.App.UiTests/Snapshots/macOS/`) přegenerována a prohlédnuta zde
      (`LAYERPEEK_UPDATE_SNAPSHOTS=1 dotnet test --project tests/LayerPeek.App.UiTests`). Nové snímky chybových stavů
      aspoň: Tiskárna (adresa), Okno kamery (velikost), Vychladnutí (jen podložka, fr), Obecné, tmavý motiv. Windows
      sadu (`Snapshots/`) přegeneruje uživatel ručně na Windows. Seznam změněných a nových souborů se zapíše do
      Poznámek k implementaci.
- [ ] Revize ui-designer po implementaci zapsaná v úkolu.
- [ ] **Bezpečné testy:** žádný test nesahá na skutečný profil uživatele (`settings.json`, `camera-windows.json`,
      `windows.json`, logy v `%APPDATA%` / `~/Library/Application Support/LayerPeek`). Headless testy běží
      v `SettingsWindowHarness` s úložištěm v paměti nebo ve vyhrazených testovacích složkách (`TestDirectories` /
      `ReservedTempDirectory`). Případné spuštění aplikace jen s `LAYERPEEK_TEST_DATA_DIR`
      a `LAYERPEEK_INSTANCE_SUFFIX` (Debug). Žádná síť mimo loopback.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
**Rozhodnuto 2026-10-30 (uživatel): Q1 A (červený rámeček, text chyby beze změny), Q2 A (chyba místo nápovědy, T-033), Q3 A (fokus na první neplatné pole), Q4 A (vlastní třída `invalid` řízená vlastnostmi `Has…Error`)** – vše podle doporučení.

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

## Poznámky k implementaci
(vyplní agent po dokončení)

Podklady: revize ui-designer v `tasks/in-progress/T-109-cooldown-nozzle-bed-thresholds.md` (sekce `### Revize
ui-designer (2026-10-30)`, nálezy 1 a 2 – tlačítko „Použít prahy…“ zůstává při chybě povolené, beze změny).
Závislost na T-111 je kvůli pořadí: oba úkoly mění styly `SettingsWindow.axaml` a snímky `settings-*`.
