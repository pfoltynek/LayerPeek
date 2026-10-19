---
id: T-036
title: Nastavení obsahu překryvu v okně kamery
status: todo
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
- [ ] Core: model, výchozí hodnoty = dnešní obsah, uložení a načtení, starý soubor, neznámé položky; testy
- [ ] `CameraPrintContent` / `CameraViewModel` (App.Tests): každá položka jen když je zapnutá a data jsou k dispozici; údaje úlohy jen během úlohy, ostatní vždy, když jsou známé; jiný výběr v malém náhledu a v normálním okně / na celé obrazovce; žádná položka = překryv skrytý; změna nastavení za běhu; formátování podle kultury; bez zbytečného formátování při nezměněném obsahu
- [ ] View model nastavení: načtení, změna, uložení, přenos do `PrinterDefinition` (App.Tests)
- [ ] Headless UI testy: okno kamery ukazuje přesně vybrané položky v normálním okně, na celé obrazovce a v malém náhledu, ve zvoleném rohu; po uložení nastavení se otevřené okno změní; sekce Překryv v nastavení; snímky reference aktualizované; test „bez posuvníku“ z T-033 projde ve všech 8 jazycích
- [ ] Test přepnutí jazyka pro nové texty
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
