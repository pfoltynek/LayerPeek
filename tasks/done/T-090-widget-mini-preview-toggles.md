---
id: T-090
title: Malý náhled – sbalitelný název tisku a graf teplot v pásu, zapamatování stavu
status: done
owner: ui-dev
priority: P2
platform: all
depends_on: [T-084, T-085, T-089]
---

## Cíl
Do informačního pásu malého náhledu (T-089) přidat dva řádky, které jde nezávisle sbalit a rozbalit přímo v malém okně:
- **název tisku**: kompaktní podoba widgetu Název tisku z T-085,
- **graf teplot**: řádek se sparkline ≈ 60 px, kompaktní `TemperatureChart` z T-084.

Ovládají je dvě malá přepínací tlačítka v základním řádku pásu. Stav každého řádku se pamatuje i po restartu.

## Rozhodnutí (uživatel, 2026-10-24)
- **Přepínače:** malý náhled ukazuje sparkline teplot **i** název tisku. Každý řádek jde sbalit a rozbalit zvlášť malými přepínacími tlačítky přímo v malém okně. Tlačítka jdou ovládat klávesnicí a mají jména pro čtečku obrazovky i `AutomationId`.
- **Výchozí stav (Q1):** oba řádky jsou **sbalené**.
- **U tiskárny (Q2):** stav sbalení je **u tiskárny** a pamatuje se (přežije restart).
- **Zapomenutí (Q3):**
  - Stav sbalení se zapomíná **spolu s pamatovaným stavem okna**: `CameraWindowStateStore.Remove` po změně výchozího okna v nastavení ho zapomene také.
  - Stav sbalení se ale **ukládá i při vypnutém „pamatovat poslední umístění“** (`RememberLastPlacement = false`). Ta volba se týká jen polohy a velikosti.
- **Bez dat (Q4):** bez úlohy **řádek názvu zmizí** a tlačítko zůstane. Bez historie ukáže řádek grafu **„Žádná data“**.

## Rozsah
- **Přepínací tlačítka** (`ToggleButton`):
  - stav pro čtečku obrazovky (sbaleno / rozbaleno) a tooltip,
  - `AutomationId` podle návrhu, například `CameraMiniToggleName` a `CameraMiniToggleChart`, řádky `CameraMiniNameRow` a `CameraMiniChartRow`,
  - ovládání klávesnicí: Tab mezi tlačítky, mezerník / Enter. Malé okno je bez rámu, fokus musí být vidět,
  - kliknutí na tlačítko nezačne tažení okna.
- Rozbalení nebo sbalení změní výšku pásu a okna podle pravidel T-089: obraz drží poměr streamu, okno roste od nejbližšího okraje a zůstane na obrazovce. Zmizení řádku názvu (konec úlohy) okno také zmenší.
- **Uložení stavu:**
  - v `camera-windows.json` u tiskárny (`CameraWindowState`, `App/Cameras/CameraWindowStateStore`), vedle režimu a umístění,
  - pole jsou nepovinná, chybějící hodnota = sbaleno,
  - ukládá se při přepnutí, po vzoru změny režimu, a to i při `RememberLastPlacement = false`,
  - zapomene se v `Remove`.
- Odběr historie teplot jen při rozbaleném grafu a otevřeném okně. Sbalený graf nic nestojí.
- Nové texty (tooltipy, jména pro čtečku obrazovky, „Žádná data“, pokud ještě neexistuje) ve všech 8 jazycích.

## Akceptační kritéria
- [x] App.Tests:
  - uložení a načtení stavu v `camera-windows.json`: nová pole, starý soubor bez nich (= sbaleno), neplatné hodnoty,
  - uložení i při `RememberLastPlacement = false`,
  - zapomenutí přes `Remove`,
  - změna výšky při přepnutí a při zmizení řádku názvu,
  - odběr historie jen při rozbaleném grafu.
- [x] Headless UI testy:
  - výchozí stav (oba sbalené) a všechny 4 kombinace,
  - přepnutí myší i klávesnicí, stav tlačítek pro čtečku obrazovky,
  - okno roste a zmenšuje se a obraz drží poměr,
  - bez úlohy řádek názvu zmizí, bez historie graf ukáže „Žádná data“,
  - stav přežije zavření a nové otevření okna,
  - nejdelší jazyk při šířce 240, snímky reference.
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné – Q1–Q4 rozhodnuté 2026-10-24, viz Rozhodnutí)

## Poznámky k implementaci
- 2026-10-25, ui-dev. Popis v `docs/architecture.md` (oddíl Malý náhled, Implementace T-090).
- **Přepínače** (`CameraMiniStrip.axaml`, `ControlTheme` `MiniToggleTheme`):
  - `ToggleButton` 30 × 30, mezera 2, 6 od pravého okraje, v `StackPanel` `CameraMiniToggles`; ikony Fluent `Cube` 16 a `Data Trending` 16,
  - vypnuto bez pozadí, najetí bílá 8 %, zapnuto bílá 14 % + akcentová linka 2 pod ikonou; fokus z klávesnice dvojitý rámeček (bílý 2 / černý 1) 1 DIP uvnitř tlačítka, aby se na pásu neořízl,
  - `AutomationProperties.Name` stálé („Název tisku v pásu“, „Graf teplot v pásu“), stav čte čtečka ze vzoru Toggle; tooltip podle stavu („Zobrazit / Skrýt …“, bez úlohy „Název tisku se zobrazí během tisku“),
  - Tab mezi tlačítky, mezerník / Enter přepne; stisk ani dvojklik na tlačítku nezačne tažení okna ani návrat do normálního okna.
- **Úchyty:** pás leží v `CameraWindow.axaml` nad úchyty (`ZIndex="1"`). Vlastní hit test pásu (`CameraMiniStripPanel`) nechává okraje 6 DIP vlevo, vpravo a dole úchytům, takže okraje podél pásu dál mění velikost. Pod přepínači patří spodní okraj tlačítku (podle návrhu T-076 4). Pruh průběhu není cílem myši.
- **Řádky** (`CameraMiniStripViewModel`):
  - řádek názvu (34) = `PrintNameCompactView.RowTemplate` nad vlastním `PrintNameWidgetViewModel` pásu, vstup s režimem Mini, takže se nikdy nežádá náhled G-code; čipy podle šířky okna přes nové `OverlayChipsPanel.MaxCount` (pod 320 žádný, od 320 materiál, od 400 i výška vrstvy); bez úlohy řádek zmizí, tlačítko zůstane zapnuté,
  - řádek grafu (64) = sloupec 64 („60 min“, ▽ tryska, ━ podložka v celých stupních) + kompaktní `TemperatureChart` (`CameraMiniChart`, při šířce 320 má 228 × 52, při 240 148 × 52); bez historie „Žádná data“ (existující `CameraOverlayChartNoData`), bez teplot sloupec bez hodnot,
  - graf odebírá historii jen viditelný (sbalený řádek, normální okno, celá obrazovka, zavřené okno: neodebírá); teploty sloupce se počítají jen při rozbaleném grafu,
  - `StripHeight` = 33 + 34 (zobrazený název) + 64 (rozbalený graf), okno ho sleduje dopočtem z T-089: při šířce 320 a 16:9 výšky 213 / 247 / 277 / 311, okno roste a zmenšuje se od nejbližšího okraje, obraz drží poměr,
  - snapshot beze změny zobrazeného obsahu nealokuje ani s rozbalenými řádky a sbalený graf nealokuje ani při změně teplot (testy 0 B).
- **Uložení stavu** (`CameraWindowState.MiniRows`, `CameraMiniRows`):
  - v `camera-windows.json` u tiskárny nepovinná pole `miniNameRow` / `miniChartRow`, zapisují se jen při `true`; soubor předchozí verze (bez polí) = sbaleno, formát ani verze souboru se nemění,
  - neplatné hodnoty (řetězec, číslo, objekt, pole, `null`) čte `LenientBooleanConverter` jako sbaleno a zbytek souboru zůstane platný (source-generated `JsonSerializerContext`, bez reflexe),
  - ukládá se hned při přepnutí (`RowsChanged`), i při vypnutém „pamatovat poslední umístění“ (ostatní pamatovaný stav zůstane), a při zavření okna,
  - `Remove` (změna výchozího okna v nastavení) ho zapomene se zbytkem stavu; otevřené okno při „Použít“ v nastavení řádky sbalí (`ResetRows`, bez události `RowsChanged`).
- **Nové texty (8 jazyků):** `CameraMiniToggleName`, `CameraMiniToggleChart`, `CameraMiniShowName`, `CameraMiniHideName`, `CameraMiniShowChart`, `CameraMiniHideChart`, `CameraMiniNameNoJob`; test délek podle návrhu ve všech jazycích.
- **Testy:**
  - App.Tests: `Cameras/Mini/CameraMiniStripRowsTests` (výchozí stav, 4 kombinace výšky, otevření se zapamatovanými řádky, hlášení změny, zmizení názvu bez úlohy, teploty grafu, sbalený graf, `ResetRows`, uvolnění, jazyky, texty v 8 jazycích), `CameraMiniStripAllocationTests` (0 B), `CameraViewModelWindowTests` (uložení hned při přepnutí, při vypnutém pamatování, při zavření, zapomenutí, „Použít“ v nastavení), `CameraWindowStateStoreTests` (uložení a načtení, sbalené se nezapisují, starý soubor, neplatné hodnoty, `Remove`),
  - headless `CameraWindowMiniTogglesUiTests`: výchozí stav a 4 kombinace myší včetně velikosti a polohy okna a poměru obrazu, klávesnice (mezerník, Tab, Enter) a stav pro čtečku, přepínače nad úchyty a stisk bez tažení, `AutomationId` / jména / tooltipy, zmizení řádku názvu a zmenšení okna, „Žádná data“ a odběr historie jen při rozbaleném a viditelném grafu, graf bez teplot, zavření a nové otevření, čipy podle šířky 240 / 320 / 400, 8 jazyků při 240 a 320 bez přetečení, viditelný fokus,
  - snímky: nové `camera-mini-strip-rows.png` (oba řádky, 320 × 311) a `camera-mini-strip-rows-240-de.png` (240, němčina, dlouhý název), přegenerované snímky T-089 `camera-mini-strip-printing.png`, `camera-mini-strip-cooled-de.png`, `camera-mini-strip-240-pl.png` a `camera-mini-buttons.png` (přibyly přepínače); všechny vizuálně zkontrolované. Test snímku s grafem nastavuje hodiny před spuštěním streamu, jinak by se nad obrazem objevilo „Bez signálu“ (test to nově hlídá).
- Ověřeno na Windows 11: `dotnet build` bez varování, `dotnet test` celého řešení 2× zelený. Aplikace se v tomto úkolu nespouštěla (vše ověřeno headless testy). Neověřeno: macOS (fokus a hit test v Avalonia.Native), skutečné okno při DPI 125–200 %, čtení stavu přepínačů skutečnou čtečkou obrazovky (Předčítání) a skutečné tažení úchytu vedle přepínačů – patří do E2E T-093; Native AOT publish (nový kód je bez reflexe, kompilované bindingy, JSON přes source generator).
