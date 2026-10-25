---
id: T-092
title: Podzáložka Překryv v nastavení – tabulka viditelnosti widgetů, globální průhlednost, opětovné načtení rozložení
status: todo
owner: ui-dev
priority: P2
platform: all
depends_on: [T-077, T-081]
---

## Cíl
Podzáložka **Překryv** v nastavení tiskárny (T-036) přejde z tabulky 9 položek a volby rohu na nastavení widgetů:
- tabulka **jen s viditelností** každého widgetu,
- akce „Použít toto rozložení u všech tiskáren“,
- „Obnovit výchozí“,
- globální průhlednost pozadí karet.

Okno nastavení navíc převezme rozložení uložené z režimu rozložení v okně kamery, aniž by ztratilo jiné neuložené změny.

## Rozhodnutí (uživatel, 2026-10-24)
- **Tabulka (Q1):** nastavuje **jen viditelnost**. Kotva, pořadí, velikost a zvýraznění se mění jen v režimu rozložení v okně kamery (T-091).
- **Viditelnost** platí pro normální okno i celou obrazovku (sdílené rozložení). Sloupec pro malý náhled **není**, protože malý náhled nemá karty (pás pod obrazem, T-089 / T-090).
- **„Použít toto rozložení u všech tiskáren“:** zkopíruje celé rozložení tiskárny (viditelnost, kotvy, pořadí, velikosti, zvýraznění) do ostatních tiskáren v rozpracovaném nastavení. Uloží se s ostatními změnami.
- **Průhlednost pozadí:** jedno **globální** nastavení, 30–95 %, výchozí 72 %. Umístění podle návrhu T-076 (bod 8), včetně naznačení, že platí pro všechny tiskárny.
- **Vstup do režimu rozložení** je jen v okně kamery (tlačítko a kontextová nabídka, T-091). Tlačítko v nastavení **není**.
- **Souběh (T-077 Q3):** když se rozložení uloží z okna kamery a okno nastavení je otevřené, okno nastavení **znovu načte jen část s rozložením** dané tiskárny a ostatní neuložené změny si ponechá.
- Práh vychladnutí (u tiskárny): logiku pohledu přidal T-080, pole v okně přidává tento úkol (rozhodnutí 2026-10-25).

## Rozhodnutí (uživatel, 2026-10-25) – přepracování úkolu
- Podzáložka Překryv se nahrazuje novou záložkou nastavení **„Widgety“** v uspořádání master-detail:
  - vlevo seznam widgetů s viditelností,
  - vpravo vlastní nastavení vybraného widgetu; u Vychladnutí práh, „Použít práh u všech tiskáren“ a přepínač notifikace „Tiskárna vychladla“,
  - dole globální průhlednost, „Obnovit výchozí“, „Použít rozložení u všech tiskáren“ a otevření režimu rozložení.
- Okno nastavení se smí zvětšit, okna Nastavení a Diagnostika ale musí mít **stejnou velikost**.
- Pole prahu vychladnutí se z T-080 přesouvá sem (T-080 je hotový bez pole v UI). Viz „Převzato z T-080“.
- Toto rozhodnutí mění část rozsahu níže (podzáložka Překryv, vstup do režimu rozložení jen z okna kamery). Úplné přepsání cíle, rozsahu a kritérií připraví architekt.

## Kontext
- Dnes: `SettingsViewModel.OverlayPrinterTab`, `PrinterEditorViewModel.OverlayRows` (`CameraOverlayItemRow`), `SelectedOverlayCorner`, `SettingsWindow.axaml`.
- Podzáložka musí být **bez posuvníku** ve výchozí velikosti 780 ve všech 8 jazycích (testy `SettingsWindowLayoutTests`).
- `AutomationId` `PrinterOverlay*` z T-036.
- Rozložení podzáložky navrhne T-076 (bod 8).

## Rozsah
- **Tabulka widgetů:** jeden řádek na registrovaný widget, s ikonou a názvem, a zaškrtávací pole „Zobrazit“. Hodnoty se mapují na `CameraOverlayLayout` přes čisté operace z T-077.
- **Tlačítka:**
  - „Obnovit výchozí“ (rozložení tiskárny),
  - „Použít u všech tiskáren“ (jen při 2 a více tiskárnách, jinak zakázané nebo skryté podle návrhu).
- **Globální průhlednost:** posuvník nebo číselné pole podle návrhu, 30–95 %, uložení do `AppSettings` (T-077). Změna se po uložení projeví ve všech otevřených oknech kamery.
- **Opětovné načtení rozložení:**
  - Při oznámení zapisovače z T-077 (rozložení uložené v okně kamery) nahradí `PrinterEditorViewModel` dané tiskárny jen své rozložení. Ostatní pole včetně neuložených změn zůstanou a stav „neuloženo“ se kvůli tomu nezmění.
  - Oznámení může přijít z jiného vlákna, proto se zpracuje na UI vlákně.
  - Odebraná nebo nová (neuložená) tiskárna oznámení ignoruje.
- Při vypnuté kameře je tabulka zakázaná a hodnoty zůstanou (jako T-036). Globální průhlednost zůstává dostupná.
- Odstranit staré `OverlayRows`, `SelectedOverlayCorner`, staré texty a `AutomationId`. Testy T-036 převést.
- Nové texty ve všech 8 jazycích, přepnutí jazyka za běhu.

## Akceptační kritéria
- [ ] App.Tests:
  - načtení rozložení do tabulky, změna viditelnosti, uložení, přenos do `PrinterDefinition`,
  - „Obnovit výchozí“ a „Použít u všech tiskáren“ (kopíruje celé rozložení, jiné hodnoty tiskáren nemění),
  - globální průhlednost: hranice 30 a 95, výchozí 72, uložení,
  - oznámení z okna kamery nahradí jen rozložení a neuložené změny jiných polí zůstanou; následné uložení zapíše nové rozložení.
- [ ] Headless UI testy:
  - výchozí hodnoty, uložení kliknutím, vypnutá kamera,
  - „Použít u všech tiskáren“ s 1 a 3 tiskárnami,
  - pořadí Tab, `AutomationId`,
  - **bez posuvníku ve všech 8 jazycích** × stavy z `SettingsWindowLayoutTests`,
  - snímky reference.
- [ ] **Převzato z T-080** (pole prahu vychladnutí, rozhodnutí 2026-10-25):
  - App.Tests i headless UI: pole prahu – výchozí hodnota 50 °C, uložení, hodnoty mimo 30–100 a necelá čísla (chyba „Zadejte celé číslo od 30 do 100.“ místo nápovědy, blokuje uložení, okno přejde na místo s polem a označí ho „!“),
  - „Použít práh u všech tiskáren“ v okně: s 1 tiskárnou zakázané s důvodem v tooltipu, s 3 tiskárnami zkopíruje platný práh a ukáže stavový text,
  - nová záložka s polem prahu **bez posuvníku ve všech 8 jazycích** × stavy z `SettingsWindowLayoutTests` (i s chybou prahu v de / fr).
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- **Q1 (z review T-080):** rozložení widgetů i práh vychladnutí jsou u každé tiskárny zvlášť. Je nová záložka „Widgety“ hlavní záložkou okna s výběrem tiskárny, nebo podzáložkou tiskárny? Na tom závisí, kam patří „Použít … u všech tiskáren“ a kde se ukáže „!“ při chybě prahu.
- **Q2 (z review T-080):** přepínač notifikace „Tiskárna vychladla“ je dnes v záložce Obecné u ostatních upozornění (T-076 bod 9). Má se do detailu widgetu Vychladnutí přesunout, nebo tam být navíc (oba svázané se stejnou hodnotou)?
- **Q3 (z review T-080):** rozhodnutí 2026-10-25 dává do záložky „Widgety“ otevření režimu rozložení, rozhodnutí 2026-10-24 říká, že tlačítko v nastavení není. Platí tedy nové rozhodnutí?

## Poznámky k implementaci
(vyplní agent po dokončení)

## Převzato z T-080
- **Hotové API pohledu (T-080, otestované v `PrinterCooldownThresholdSettingsTests`):**
  - `PrinterEditorViewModel.CooldownThresholdCelsius`, `MinCooldownThresholdCelsius`, `MaxCooldownThresholdCelsius`,
  - `CooldownThresholdError`, `ShowsCooldownThresholdHint`,
  - `ApplyCooldownThresholdToAllCommand`, `ApplyToAllUnavailableReason` (`SettingsViewModel.ApplyCooldownThresholdToAll`),
  - `HasOverlayErrors`, `ShowsOverlayErrorMarker`, `OverlayTabName` a přechod `SettingsViewModel` na `OverlayPrinterTab` při chybě prahu. Při přesunu pole na novou záložku je potřeba je přejmenovat nebo přesměrovat na nové místo.
- **Texty už existují ve všech 8 jazycích:** `SettingsCooldownSection`, `SettingsCooldownThreshold`, `SettingsCooldownThresholdHint`, `SettingsCooldownThresholdError`, `SettingsCooldownApplyAll`, `SettingsCooldownAppliedAll`, `SettingsOverlayApplyAllSingle` (použít, nepřidávat znovu).
- **`SettingsNotifyCooledDownHelp`** (`HelpText` přepínače „Tiskárna vychladla“ v záložce Obecné) dnes jen popisuje, kdy notifikace přijde. Až bude pole prahu na nové záložce, doplnit do textu, kde se práh nastavuje (ve všech 8 jazycích), případně přesunout přepínač podle rozhodnutí do detailu widgetu Vychladnutí.
- **Prototyp XAML z T-080** (pravý sloupec dnešní podzáložky Překryv; přetekl o 9–251 DIP, proto se nepoužil). AutomationId podle T-076: `PrinterCooldownSection`, `PrinterCooldownThreshold`, `PrinterCooldownThresholdHint`, `PrinterCooldownThresholdError`, `PrinterCooldownApplyAll`.

```xml
                          <!--
                            T-080 (design T-076, section 8): the cool-down threshold of the printer at the top of the right
                            column, outside the table, so that it stays editable while the camera is off (the notification
                            needs it too). The error replaces the hint (T-033 pattern).
                          -->
                          <StackPanel AutomationProperties.AutomationId="PrinterCooldownSection" Margin="0,0,0,16">
                            <TextBlock Classes="section"
                                       Margin="0,0,0,4"
                                       TextWrapping="Wrap"
                                       Text="{Binding [SettingsCooldownSection], Source={x:Static loc:LocalizedStrings.Instance}}" />
                            <TextBlock Classes="label"
                                       TextWrapping="Wrap"
                                       Text="{Binding [SettingsCooldownThreshold], Source={x:Static loc:LocalizedStrings.Instance}}" />
                            <NumericUpDown AutomationProperties.AutomationId="PrinterCooldownThreshold"
                                           AutomationProperties.Name="{Binding [SettingsCooldownThreshold], Source={x:Static loc:LocalizedStrings.Instance}}"
                                           AutomationProperties.HelpText="{Binding [SettingsCooldownThresholdHint], Source={x:Static loc:LocalizedStrings.Instance}}"
                                           Value="{Binding CooldownThresholdCelsius}"
                                           Minimum="{x:Static vm:PrinterEditorViewModel.MinCooldownThresholdCelsius}"
                                           Maximum="{x:Static vm:PrinterEditorViewModel.MaxCooldownThresholdCelsius}"
                                           Increment="1"
                                           FormatString="0"
                                           Width="120"
                                           HorizontalAlignment="Left" />
                            <TextBlock AutomationProperties.AutomationId="PrinterCooldownThresholdHint"
                                       Classes="hint"
                                       Text="{Binding [SettingsCooldownThresholdHint], Source={x:Static loc:LocalizedStrings.Instance}}"
                                       IsVisible="{Binding ShowsCooldownThresholdHint}" />
                            <TextBlock AutomationProperties.AutomationId="PrinterCooldownThresholdError"
                                       Classes="error"
                                       Text="{Binding CooldownThresholdError}"
                                       IsVisible="{Binding CooldownThresholdError, Converter={x:Static ObjectConverters.IsNotNull}}" />
                            <Button AutomationProperties.AutomationId="PrinterCooldownApplyAll"
                                    AutomationProperties.Name="{Binding [SettingsCooldownApplyAll], Source={x:Static loc:LocalizedStrings.Instance}}"
                                    Margin="0,8,0,0"
                                    Command="{Binding ApplyCooldownThresholdToAllCommand}"
                                    ToolTip.Tip="{Binding ApplyToAllUnavailableReason}"
                                    ToolTip.ShowOnDisabled="True">
                              <TextBlock TextWrapping="Wrap" Text="{Binding [SettingsCooldownApplyAll], Source={x:Static loc:LocalizedStrings.Instance}}" />
                            </Button>
                          </StackPanel>
```
