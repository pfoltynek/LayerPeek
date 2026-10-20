---
id: T-036
title: Nastavení obsahu překryvu v okně kamery
status: in-progress
owner: core-dev + ui-dev
priority: P2
platform: all
depends_on: [T-033]
---

## Cíl
Uživatel si na podzáložce Kamera vybere, které informace se zobrazují přes obraz kamery a ve kterém rohu.

## Současný stav (2026-10-19, T-014)
- `CameraWindow.axaml`: překryv stavu tisku vlevo dole (`IsPrintOverlayVisible`), ve všech režimech (normální, celá obrazovka, malý náhled), písmo 12, poloprůhledné černé pozadí. Obsah: řádek „Stav – 42 %“ (`PrintStateText`) a „zbývá 1 h 5 min“ (`RemainingText`).
- Obsah počítá `CameraPrintContent.From(PrinterSnapshot)` (stejná pravidla jako tooltip tray ikony: progress a zbývající čas jen u běžící / pozastavené / čekající úlohy dostupné tiskárny). Texty se formátují jen při změně zobrazeného obsahu.
- Uprostřed obrazu je zvlášť překryv stavu připojení kamery (připojuji / bez signálu / chyba).
- Dostupná data v `PrinterSnapshot`: `State`, `Job` (`FileName`, `ProgressPercent`, `Elapsed`, `Remaining`/`EstimatedRemaining`), `EstimatedCompletion`, `Telemetry` (`Nozzle`/`Bed` aktuální + cílová, …). Název tiskárny je v `PrinterDefinition`.

## Rozhodnuto
- **Položky (2026-10-19):** nastavitelné jsou: název tiskárny, stav tiskárny, progress %, zbývající čas, čas dokončení (hodiny, např. „hotovo v 14:35“), doba tisku, název souboru, teplota trysky (aktuální / cílová), teplota podložky (aktuální / cílová). Výchozí zapnuté zůstávají stav + progress + zbývající čas (dnešní stav).
- **Režimy (2026-10-19):** malý náhled má vlastní výběr položek, oddělený od normálního okna. Celá obrazovka používá výběr normálního okna.
- **Kdy se zobrazují (2026-10-19):** údaje úlohy (progress, zbývající čas, čas dokončení, doba tisku, název souboru) jen během úlohy (stejná pravidla jako dnes); ostatní (název tiskárny, stav, teploty), kdykoli jsou známé.
- **Uložení (2026-10-19):** u tiskárny v `settings.json`, `printers[*].camera.overlay`, nepovinná pole, `version: 1`.
- **Vzhled (2026-10-19):** nastavuje se jen roh překryvu (4 rohy, výchozí vlevo dole). Překryv stavu připojení kamery zůstává vždy zapnutý a nenastavuje se.
- **Kdy se projeví (2026-10-19):** v otevřených oknech hned po uložení nastavení.

## Rozsah

### Core (core-dev)
- Model `CameraOverlayOptions` jako součást `CameraSettings`: sada položek pro normální okno (+ celá obrazovka), sada položek pro malý náhled, roh (`TopLeft`, `TopRight`, `BottomLeft`, `BottomRight`). Výchozí hodnoty = dnešní obsah a vlevo dole (pro oba výběry).
- Uložení jako nepovinná sekce `printers[*].camera.overlay`; neznámé položky v souboru se ignorují s varováním; neplatný roh → výchozí. Scrubber sekci nemění.
- Testy v Core.Tests: mapování, výchozí hodnoty, neznámé hodnoty, starý soubor, scrubber.

### App (ui-dev)
- `CameraPrintContent` / `CameraViewModel` rozšířit o všechny položky; výběr podle režimu (malý náhled vs. ostatní); zachovat levné porovnání (formátování jen při změně zobrazeného obsahu). Čas dokončení a doba tisku formátované podle kultury; teploty „215 / 215 °C“ (formát podle návrhu UI).
- `CameraWindow.axaml`: překryv zobrazí jen vybrané položky ve zvoleném rohu; nesmí kolidovat s tlačítky okna vpravo nahoře (řešení podle návrhu UI, např. posun pod tlačítka).
- Sekce **Překryv** na podzáložce Kamera: zaškrtávací pole položek zvlášť pro normální okno a malý náhled, volba rohu. Rozložení podle schváleného návrhu UI z T-033, bez posuvníku.
- Změna nastavení se po uložení projeví v otevřených oknech kamery.
- Texty ve všech 8 jazycích, přepnutí jazyka za běhu (`ILanguageAware`).

## Akceptační kritéria
- [x] Core: model, výchozí hodnoty = dnešní obsah, uložení a načtení, starý soubor, neznámé položky; testy
- [x] `CameraPrintContent` / `CameraViewModel` (App.Tests): každá položka jen když je zapnutá a data jsou k dispozici; údaje úlohy jen během úlohy, ostatní vždy, když jsou známé; jiný výběr v malém náhledu a v normálním okně / na celé obrazovce; žádná položka = překryv skrytý; změna nastavení za běhu; formátování podle kultury; bez zbytečného formátování při nezměněném obsahu
- [x] View model nastavení: načtení, změna, uložení, přenos do `PrinterDefinition` (App.Tests)
- [x] Headless UI testy: okno kamery ukazuje přesně vybrané položky v normálním okně, na celé obrazovce a v malém náhledu, ve zvoleném rohu; po uložení nastavení se otevřené okno změní; sekce Překryv v nastavení; snímky reference aktualizované; test „bez posuvníku“ z T-033 projde ve všech 8 jazycích
- [x] Test přepnutí jazyka pro nové texty
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- **Q1 – nové překlady (2026-10-20):** schválit texty ve všech 8 jazycích (seznam v Poznámkách, oddíl „Texty“). Italský „Roh překryvu“ je zkrácený na „Angolo sovrimpressione“ (s „Angolo della sovrimpressione“ by sloupec nápovědy zúžil pod 100 DIP).
- **Q2 – formát teploty (2026-10-20):** navrženo „Tryska: 215 / 215 °C“ (celé stupně, popisek z okna Diagnostika); při vypnutém topení (cíl 0 nebo neznámý) jen „Tryska: 25 °C“. Ponechat, nebo jinak (např. „25 / vyp.“)?
- **Q3 – čas dokončení jiný den (2026-10-20):** když tisk skončí jiný den, ukazuje se „Hotovo 17. října v 8:00“ (měsíc a den podle regionálního nastavení). Ponechat?
- **Q4 – samotný průběh (2026-10-20):** když je zapnutý průběh bez stavu tiskárny, ukazuje se samostatný řádek „42 %“ (tučně, místo řádku stavu). Ponechat, nebo s popiskem („Průběh: 42 %“)?
- **Q5 – záhlaví sloupců (2026-10-20):** záhlaví „Normální okno a celá obrazovka“ / „Malý náhled“ mají písmo 13 px (návrh: 14 px), protože es „pantalla completa“ se při 14 px tučně do 120 DIP nevejde a záhlaví by mělo 3 řádky. Alternativa: 14 px a 3 řádky v es (obsah se pořád vejde bez posuvníku).

## Poznámky k implementaci
Stav 2026-10-20 (ui-dev): implementace hotová, kritéria splněná v headless testech; úkol zůstává `in-progress` kvůli otázkám Q1–Q5 a ruční kontrole (viz „Zbývá ručně“).

**Core**
- `Core/Cameras/CameraOverlayItems` (`[Flags]`: `PrinterName`, `State`, `NozzleTemperature`, `BedTemperature`, `Progress`, `RemainingTime`, `FinishTime`, `PrintTime`, `FileName`), `CameraOverlayCorner` (4 rohy), `CameraOverlayOptions` (`Normal`, `Mini`, `Corner`; konstanty `AllItems`, `JobItems`, `DefaultItems` = stav + průběh + zbývající čas, `DefaultCorner` = vlevo dole; `Items` = pořadí zobrazení, nejdřív údaje tiskárny; `ItemsFor(mode)` – celá obrazovka používá `Normal`). Neznámé příznaky / roh → `ArgumentOutOfRangeException`. `CameraSettings.Overlay` (výchozí sdílená instance, součást rovnosti, `ToString`).
- `settings.json`: nepovinná sekce `printers[*].camera.overlay` = `{ "corner": "TopRight", "normal": ["PrinterName", …], "mini": [ … ] }` (názvy položek v pořadí zobrazení, prázdný seznam = nic). Zapisuje se jen, když se liší od výchozích hodnot (starý soubor se uložením nezmění), `version` zůstává 1. Načítání: chybějící hodnota = výchozí; neznámý roh → výchozí roh s varováním; neznámé položky (i `null`, čísla jako text, „State, Progress“) se vynechají s jedním varováním na seznam („1 unknown item is ignored“), hodnoty se do varování neopisují; názvy bez ohledu na velikost písmen a mezery. Špatný typ JSON (např. `"normal": "State"`) poškodí celý soubor jako u ostatních hodnot. Scrubber sekci nemění (test bajt po bajtu).

**Obsah překryvu (`App/Cameras/CameraPrintContent`)**
- Levně porovnatelná struktura jen s **vybranými** položkami, zaokrouhlenými jako na displeji (nezobrazená teplota ani změna pod přesností displeje nevyvolá formátování ani post na UI vlákno). Řádky (`CameraOverlayLine`: položka, text, tučně) se formátují až při změně obsahu a při přepnutí jazyka.
- Kdy se co ukazuje: název tiskárny vždy (i před prvním dotazem; zkrácený na 40 znaků „…“); stav po prvním dotazu; teploty, kdykoli jsou ve snapshotu (konečné hodnoty; offline tiskárna je nemá); údaje úlohy jen u dostupné tiskárny s běžící / pozastavenou / čekající úlohou (`HasActiveJob` nebo `Attention`) – stejné pravidlo jako dosud a jako tooltip – a každý jen se známou hodnotou.
- Formáty (en / cs): stav s průběhem „Printing – 42%“ / „Tiskne – 42 %“ (beze stavu samostatně „42%“, tučně); „Nozzle: 215 / 215 °C“ / „Tryska: 215 / 215 °C“ (celé stupně, cíl jen při zapnutém topení, jinak „Tryska: 25 °C“); „1 h 5 min remaining“ / „Zbývá 1 h 5 min“; „Done at 1:05 PM“ / „Hotovo v 13:05“ (místní čas z `TimeProvider.LocalTimeZone`, zaokrouhlený nahoru na minutu jako zbývající čas; jiný den „Hotovo 17. října v 8:00“); „Print time: 2 h 10 min“ / „Doba tisku: 2 h 10 min“; název souboru do 40 znaků, delší si nechá začátek a posledních 16 znaků (materiál, čas, přípona) kolem „…“ (nerozdělí náhradní páry). Čísla a časy podle regionálního nastavení, slova podle jazyka aplikace.
- Absurdní odhad (`TimeSpan.MaxValue`) nepřeteče: čas dokončení se vynechá, doba se omezí.

**Okno kamery**
- `CameraViewModel`: `OverlayLines`, `IsPrintOverlayVisible` (aspoň jeden řádek; žádná položka = překryv skrytý), `OverlayCorner`, `IsOverlayTop`, `IsOverlayRight`. Poslední snapshot tiskárny si drží, takže přepnutí režimu (malý náhled má vlastní výběr) i uložené nastavení se projeví hned, bez čekání na další dotaz. `ApplyPrinters` po uložení převezme nový překryv (položky, roh) i nový název – okno se nehýbe, nemění velikost ani režim a stream běží dál. Odstraněné vlastnosti `PrintStateText`, `RemainingText`, `HasRemaining`.
- `CameraWindow.axaml`: `ItemsControl` s řádky v `Border` (`AutomationId` `CameraPrintOverlay`), roh přes třídy `top` / `right`, odsazení 6 DIP. Vpravo nahoře začíná překryv pod tlačítky okna (6 + 30 + 4 = 40 DIP); vlevo nahoře nechává vpravo volnou šířku tlačítek (74 DIP) a zalamuje se – jinak by v malém náhledu dlouhý název souboru zajel pod tlačítko. V pravých rozích je text zarovnaný doprava. Překryv stavu připojení beze změny.

**Nastavení – podzáložka Překryv**
- `SettingsViewModel.OverlayPrinterTab = 3`, `PrinterTabCount = 4` (Ctrl+PageDown/PageUp cyklí přes 4 podzáložky; Překryv nemá validaci ani značku `!`, uložení s chybou ho nikdy nevybere). `OverlayCornerOptions` (4 rohy, texty z T-035 „Vlevo nahoře“…).
- `PrinterEditorViewModel`: `SelectedOverlayCorner`, `OverlayRows` (9 × `CameraOverlayItemRow` s `IsNormal`, `IsMini`, `NormalName`, `MiniName` = „{položka} – {sloupec}“), `CameraOverlay`; překryv se ukládá u každé varianty kamery (zapnutá, vypnutá s adresou, bez adresy, s neplatnou adresou). Změna jen překryvu nezapomíná zapamatovanou pozici okna kamery.
- `SettingsWindow.axaml`: podle návrhu – vlevo `Grid` Auto | 120 | 120 (první řádek: „Roh překryvu“ + combo, záhlaví sloupců 13 px tučně, max. 2 řádky; pak 9 řádků po 32 DIP), mezera 16, vpravo nápovědy 12 px (u vypnuté kamery nahoře `SettingsCameraWindowCameraOff`). U vypnuté kamery je tabulka zakázaná (hodnoty zůstávají), nápovědy čitelné. Pořadí Tab: roh → po řádcích normální okno, malý náhled → Uložit → Zrušit. Nová `AutomationId`: `PrinterTabOverlay`, `PrinterOverlayFields`, `PrinterOverlayCorner`, `PrinterOverlayNormalHeader`, `PrinterOverlayMiniHeader`, `PrinterOverlay{Položka}Normal` / `…Mini`, `PrinterOverlayCameraOff`, `PrinterOverlayHint`.
- Při minimální šířce okna 640 (záchranný stav) se 4 záhlaví podzáložek zalomí do 2 řad; ve výchozí velikosti 780 jsou v jedné řadě ve všech jazycích včetně italštiny se značkami.

**Texty (nové klíče, všech 8 jazyků; en / cs / sk / de / es / fr / it / pl)**
- `CameraOverlayLabeledValue`: „{0}: {1}“ (fr „{0} : {1}“ s nezlomitelnou mezerou); `CameraOverlayTemperatureWithTarget`: „{0} / {1} °C“.
- `CameraOverlayFinishTime`: Done at {0} / Hotovo v {0} / Hotovo o {0} / Fertig um {0} / Termina a las {0} / Fin à {0} / Fine alle {0} / Koniec o {0}.
- `CameraOverlayFinishDateTime`: Done {0} at {1} / Hotovo {0} v {1} / Hotovo {0} o {1} / Fertig am {0} um {1} / Termina el {0} a las {1} / Fin le {0} à {1} / Fine il {0} alle {1} / Koniec {0} o {1}.
- `CameraOverlayPrintTime`: Print time: {0} / Doba tisku: {0} / Čas tlače: {0} / Druckzeit: {0} / Tiempo de impresión: {0} / Durée d'impression : {0} / Tempo di stampa: {0} / Czas druku: {0}.
- `SettingsPrinterTabOverlay`: Overlay / Překryv / Prekrytie / Einblendung / Superposición / Superposition / Sovrimpressione / Nakładka.
- `SettingsOverlayCorner`: Overlay corner / Roh překryvu / Roh prekrytia / Ecke der Einblendung / Esquina de la superposición / Coin de la superposition / Angolo sovrimpressione / Róg nakładki.
- `SettingsOverlayNormalColumn`: Normal window and full screen / Normální okno a celá obrazovka / Normálne okno a celá obrazovka / Normales Fenster und Vollbild / Ventana normal y pantalla completa / Fenêtre normale et plein écran / Finestra normale e schermo intero / Zwykłe okno i pełny ekran.
- Položky `SettingsOverlayItem*`: Printer name, Printer state, Nozzle temperature, Bed temperature, Progress (%), Remaining time, Finish time (clock), Print time, File name / Název tiskárny, Stav tiskárny, Teplota trysky, Teplota podložky, Průběh (%), Zbývající čas, Čas dokončení (hodiny), Doba tisku, Název souboru / Názov tlačiarne, Stav tlačiarne, Teplota trysky, Teplota podložky, Priebeh (%), Zostávajúci čas, Čas dokončenia (hodiny), Čas tlače, Názov súboru / Druckername, Druckerstatus, Düsentemperatur, Heizbetttemperatur, Fortschritt (%), Restzeit, Fertigstellung (Uhrzeit), Druckzeit, Dateiname / Nombre de la impresora, Estado de la impresora, Temperatura de la boquilla, Temperatura de la base, Progreso (%), Tiempo restante, Hora de finalización, Tiempo de impresión, Nombre del archivo / Nom de l'imprimante, État de l'imprimante, Température de la buse, Température du plateau, Progression (%), Temps restant, Heure de fin, Durée d'impression, Nom du fichier / Nome della stampante, Stato della stampante, Temperatura dell'ugello, Temperatura del piano, Avanzamento (%), Tempo rimanente, Ora di fine, Tempo di stampa, Nome del file / Nazwa drukarki, Stan drukarki, Temperatura dyszy, Temperatura stołu, Postęp (%), Pozostały czas, Godzina zakończenia, Czas druku, Nazwa pliku.
- `SettingsOverlayHint` (cs): „Údaje o úloze (průběh, časy, název souboru) se zobrazují jen během tisku. Změny se po uložení projeví i v otevřených oknech kamery.“ (ostatní jazyky obdobně v resx).
- Znovu použité: `DiagnosticsNozzle` / `DiagnosticsBed` (popisky teplot), `DiagnosticsTemperatureValue`, `ToolTipStateWithProgress`, `ToolTipProgress`, `ToolTipRemaining`, `SettingsCameraWindowCorner*`, `SettingsCameraWindowMini`, `SettingsCameraWindowCameraOff`.

**Testy**
- Core.Tests: `Cameras/CameraOverlayOptionsTests` (výchozí hodnoty, pořadí, validace, rovnost, `CameraSettings`), `Settings/SettingsMapperCameraOverlayTests` (výchozí hodnoty, round-trip všech rohů a položek, nezapisování výchozích, neznámé hodnoty s varováním bez opisu, špatné typy JSON, vypnutá / neplatná kamera), `JsonSettingsStoreTests` (starý soubor + uložení bez sekce, round-trip), `JsonSettingsStoreScrubTests.CameraOverlaySectionIsLeftByteForByte`.
- App.Tests: `Cameras/CameraPrintContentTests` (každá položka, viditelnost podle stavu, chybějící data, teploty, čas dokončení – zaokrouhlení, časové pásmo, jiný den, přetečení –, kultury cs/de/fr, dlouhé názvy, levné porovnání), `Cameras/CameraViewModelOverlayTests` (výběr podle režimu, přepnutí režimu bez dotazu, žádná položka, uložení za běhu – položky, roh, název –, jazyk, dispose), `Settings/CameraOverlaySettingsTests`; upravené `CameraViewModelTests`, `SettingsLayoutTests`.
- UiTests: `Cameras/CameraWindowOverlayUiTests` (přesné řádky v normálním okně, na celé obrazovce a v malém náhledu; poloha ve všech 4 rozích ve všech režimech, nepřekrývá tlačítka; uložení změní otevřené okno bez posunu – normální okno i malý náhled; jiná tiskárna; jazyk; snímky), `Settings/SettingsWindowOverlayUiTests` (výchozí hodnoty, pořadí řádků, uložení kliknutím, vypnutá kamera, pořadí Tab, tabulka ve všech 8 jazycích – záhlaví max. 2 řádky, pole ve sloupcích, nápověda ≥ 100 DIP –, přepnutí jazyka); `SettingsWindowLayoutTests` – podzáložka Překryv ve všech 8 jazycích × všech stavech + nový stav „vypnutá kamera, všechny položky“, 4 záhlaví v jedné řadě se značkami; Ctrl+PageDown přes 4 podzáložky.
- Snímky (vizuálně zkontrolované): nové `settings-overlay`, `settings-overlay-off-it`, `camera-overlay-default`, `camera-overlay-all-top-right`, `camera-overlay-mini`; přegenerované všechny `settings-*` (čtvrté záhlaví podzáložek).
- `dotnet build` 0 varování; `dotnet test` 2× zeleně (3363 testů, 1 přeskočený E2E bez Edge); `dotnet publish -c Release -r win-x64` (AOT) bez varování; publikovaná aplikace se spustí a běží (okna jsem ručně neotevíral).

**Zbývá ručně (uživatel)**
- Windows (Segoe UI, 100/125/150 %): podzáložka Překryv bez posuvníku, záhlaví podzáložek v jedné řadě (hlavně italština), čitelnost nápovědy.
- Okno kamery se skutečným obrazem: čitelnost překryvu ve všech rozích, vpravo nahoře pod tlačítky, malý náhled s dlouhým názvem souboru; po uložení nastavení se otevřené okno změní a nepohne se.
- macOS (T-018) neověřeno.
