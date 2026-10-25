---
id: T-093
title: E2E – widgety překryvu, malý náhled a režim rozložení na skutečné aplikaci + AOT
status: done
owner: qa-reviewer
priority: P2
platform: windows
depends_on: [T-080, T-087, T-088, T-090, T-091, T-092]
---

## Cíl
Ruční kontroly widgetového překryvu převést na automatické E2E testy skutečné aplikace na Windows (UI Automation, `tests/LayerPeek.App.E2E.Tests`, infrastruktura T-044 / T-065). Proti simulátoru (`tools/PrusaSimulator`, loopback). Ověřit Native AOT publish.

## Rozsah
- Simulátor: tisk se známým názvem souboru PrusaSliceru, náhled G-code (T-086) a režim bez náhledu, dokončení a chladnutí (vychladnutí trysky pod práh).
- Náhled G-code ve widgetu Název tisku (T-087) se simulátorem: M 40 / L 72 DIP vlevo od názvu (`CameraOverlayWidget-print-name-Thumbnail`, jméno „Náhled tisku“), ostrý při 100 / 125 / 150 / 200 % (dekóduje se na velikost × škálování monitoru), v S žádný; zůstane po dokončení i zastavení tisku a během chladnutí, začátek dalšího tisku ho vymění; se simulátorem `--no-previews` (nebo `no-previews` za běhu) karta bez náhledu a bez prázdného rámečku; zavření a znovuotevření okna kamery náhled ukáže znovu (z paměti, bez nového stažení); přesun okna na monitor s jiným DPI náhled znovu dekóduje. Ručně: vzhled skutečného náhledu z PrusaSliceru a skutečná tiskárna (`/thumb/l`).
- Okno kamery v normálním režimu a na celé obrazovce: karty ve správných kotvách (podle `AutomationId` a polohy), čitelnost při 100 / 125 / 150 % (zásahy do systému přes T-066, s návratem), kotvy nahoře mimo tlačítka okna.
- Režim rozložení (jen normální okno – v malém náhledu a na celé obrazovce není): vstup tlačítkem i kontextovou nabídkou, přesun widgetu myší (skutečné tažení přes UIA nebo `SendInput`) do kotvy i změna pořadí v kotvě, ovládání klávesnicí, popover, „Obnovit výchozí“, „Hotovo“, Esc zahodí změny. Po restartu aplikace je rozložení stejné a platí i na celé obrazovce. Uložení z okna kamery při otevřeném okně nastavení s neuloženou změnou jiného pole: okno nastavení převezme rozložení a změnu zachová.
- Malý náhled: žádné karty přes obraz, pás pod obrazem, obraz drží poměr streamu (simulátor 16:9), výchozí oba řádky sbalené, oba přepínače myší i klávesnicí, okno roste od nejbližšího okraje a zůstane na pracovní ploše (u spodního i horního okraje, na druhém monitoru s jiným DPI, pokud je k dispozici), po změně velikosti úchytem se výška dopočítá, minimální šířka 240, stav sbalení přežije restart (i při vypnutém „pamatovat poslední umístění“).
- Malý náhled – kontroly doplněné z T-089 (headless testy je ověřit nemohou):
  - skutečné tažení úchytu (`SendInput`, Windows modální smyčka `BeginResizeDrag`): během tažení se výška nedopočítává (žádné blikání), po puštění se dopočítá jednou; tažení za pás (mimo tlačítka) okno přesouvá,
  - start se starým `camera-windows.json` z verze 0.1.0 (malý náhled 320 × 180 v rohu vpravo dole a 160 × 90 u spodního okraje): okno vyroste o pás nahoru a zůstane na pracovní ploše, užší než 240 se rozšíří,
  - `settings.json` z verze 0.1.0 s malým náhledem užším než 240: načte se jako 240 s jedním varováním v logu,
  - text pásu ve všech stavech tiskárny ze simulátoru (tiskne „19 % · 2 h 35 min“, pozastaveno, nečinná, offline, chladne, vychladlo) a jméno pro čtečku obrazovky přes UIA (`CameraMiniSummary`),
  - snímek prvního obrazu se známým poměrem: simulátor má jen klip 16:9, stream 4:3 pokrývají headless testy.
- Vychladnutí: widget prošel stavy „Chladne…“ → „Vychladlo“ po dokončeném i zastaveném tisku; zmizí při dalším tisku. Toast „vychladlo“ jen se zapnutým přepínačem (výchozí vypnuto), informační, tj. bez zvuku při „jen důležité“ (infrastruktura toastů T-068); práh u tiskárny.
- Převod nastavení: spuštění s `settings.json` z verze 0.1.0 (overlay T-036, roh např. vpravo nahoře) zobrazí výchozí rozložení zrcadlené do starého rohu; po uložení má soubor `version` 2 a žádné staré klíče.
- `dotnet publish src/LayerPeek.App -c Release -r win-x64` bez varování. Sada testů proti Debug i AOT buildu (po vzoru T-073).
- Přehled pokrytí ručních kontrol (T-065) doplnit o nové kontroly a označit ty, které zůstávají ruční (například vizuální dojem na skutečném obraze kamery).

## Akceptační kritéria
- [x] Všechny body rozsahu mají E2E test, nebo jsou v přehledu pokrytí zdůvodněně označené jako ruční
- [x] Testy po sobě uklidí vše i po pádu (data, okna, zásahy do systému). Názvy testů v PascalCase bez podtržítek
- [x] AOT publish bez varování, E2E zelené proti Debug i AOT
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- 2026-10-25, qa-reviewer. Větev `feature/T-093-widget-e2e` z `develop` (987ba69). Přehled pokrytí: `tasks/done/T-065-e2e-system-changes-infrastructure.md`, oddíl „Doplněno v T-093“ (co pokrývá který test a co zůstává ručně).
- **Nové E2E testy** (`tests/LayerPeek.App.E2E.Tests`, jen v izolovaném testovacím režimu, data ve vyhrazené dočasné složce, úklid i po pádu přes stávající `E2EFixture` / `AppInstance`):
  - `CameraWidgetTests` (3): karty ve výchozích kotvách v normálním okně i na celé obrazovce, náhled G-code (40 DIP, jméno „Print preview“, vlevo od názvu), znovuotevřené okno bez nového stažení náhledu, „Chladne…“ → „Vychladlo“ po dokončeném i zastaveném tisku (práh 100 °C), další tisk bez karty vychladnutí a s novým náhledem; simulátor bez náhledů = karta bez obrázku a bez prázdného rámečku.
  - `CameraMiniPreviewTests` (6): žádné karty přes obraz, pás pod obrazem 16:9, oba řádky výchozí sbalené, přepínače myší (klik) i klávesnicí (Tab, mezerník, Enter), růst od spodního i horního okraje uvnitř pracovní plochy, stav řádků po restartu i s vypnutým „pamatovat“, skutečné tažení úchytu (během tažení výška beze změny, po puštění jednou dopočtená, minimum 240), tažení za pás přesouvá okno, `camera-windows.json` z 0.1.0 (320 × 180 v rohu, 160 × 90 u okraje), `settings.json` z 0.1.0 s užším malým náhledem (240 a jedno varování v logu), text pásu a jméno pro čtečku ve stavech tiskne / chladne / vychladlo / pozastaveno / nečinná / offline.
  - `CameraLayoutModeTests` (+3 k původnímu): kontextová nabídka skutečným pravým klikem, klávesnice (zvednutí, přesun do sousední kotvy, puštění, oznámení), popover (F2, zvýraznění, velikost S), Esc zavře popover a druhé Esc zahodí změny (nic se nezapíše), „Obnovit výchozí“ + „Hotovo“, změna pořadí v kotvě tažením, rozložení po restartu a na celé obrazovce, žádný režim rozložení v malém náhledu ani na celé obrazovce, „Hotovo“ při otevřeném nastavení s neuloženým novým názvem tiskárny (nastavení si změnu ponechá a uloží obojí).
  - `WidgetSettingsTests` (2): převod `settings.json` 0.1.0 s rohem vpravo nahoře (zrcadlené výchozí rozložení, jedna zpráva v logu, po uložení `version` 2 bez `corner` / `normal` / `mini`), záložka Widgety (skrytí názvu tisku, „Upravit rozložení…“ otevře režim s rozpracovaným rozložením, Esc nic nezapíše, Uložit skryje kartu).
  - `CameraWidgetScaleTests` (2, zásah `DisplayScale` přes deník T-066): 100 / 125 / 150 / 200 % hlavního monitoru (karty v kotvách v normálním okně i na celé obrazovce, náhled 40 DIP v pixelech monitoru a ostrý, malý náhled 320 × 213 DIP v pracovní ploše); druhý monitor na 150 % – okno přetažené titulkem skutečnou myší náhled znovu dekóduje (60 px), malý náhled tam roste uvnitř pracovní plochy.
  - `ToastNotificationTests.CooledDownToastComesOnlyWithItsSwitchOn` (zásahy `Toasts`, `DoNotDisturb` přes T-068): první dokončený tisk vychladne bez toastu (výchozí vypnuto), přepínač zapnutý v okně nastavení, další tisk přinese toast „Print finished, printer cooled down“ s názvem tiskárny.
  - Ostrost náhledu: hrany nakresleného náhledu se porovnají s náhledem simulátoru zmenšeným přímo na nakreslené pixely a s rozmazaným (dekódovaným na polovinu a zvětšeným); naměřeno 6–8 vs. 16–17,5 (0,35–0,5 rozmazaného), test chce méně než 0,75.
- **Infrastruktura:** `Infrastructure/WidgetRun.cs` (simulátor tiskárny s krátkým cyklem + kamera + aplikace, `CameraWidgets` pro kotvy, karty a pás přes UIA, `FailureScreenshots` = při selhání snímek a strom UIA každého okna aplikace do výsledků), `AppInstance.StartAsync` umí předem zapsat další soubory datové složky (`camera-windows.json` 0.1.0). Simulátor: nový scénář `stopped` (`STOPPED`, bez úlohy, chladne od aktuálních teplot; `docs/simulator.md`, testy `PrusaSimulator.Tests`).
- **Chyby produktu nalezené E2E a opravené (test napřed):**
  1. Karty widgetů a pojmenované části (náhled tisku, pilulka stavu, průběh, `CameraMiniSummary`, řádky a graf pásu, průběh pásu) nebyly ve stromu UI Automation: obyčejný `Border` / `Grid` nemá prvek, takže čtečka obrazovky jméno karty (T-076 3.1) nikdy nedostala a E2E je nenašly. Oprava: `AutomationProperties.AccessibilityView="Content"` (`OverlayStyles.axaml`, `PrintNameWidgetView.axaml`, `StatusWidgetTemplate.axaml`, `CameraMiniStrip.axaml`). Testy: `CameraWindowOverlayUiTests.CardsAreUiAutomationElementsWithTheirFullName`, rozšířené `CameraWindowPrintThumbnailUiTests` a `CameraWindowMiniStripUiTests`.
  2. „Hotovo“ v režimu rozložení bez změny (nebo po „Obnovit výchozí“ u tiskárny s výchozím rozložením) ukázalo „The layout could not be saved“ a režim neskončil: úložiště hlásí stejnou sekci jako „nezapsáno“. Oprava: `CameraLayoutModeViewModel.SaveAsync` při pracovní kopii rovné uloženému rozložení nic nezapisuje a režim ukončí jako uložený. Test `DoneWithTheSavedLayoutEndsTheModeWithoutWriting`; `ClosingTheWindowWhileSavingKeepsTheWrite` teď před uložením něco změní (jinak by nebylo co zapisovat).
  3. Přepnutí normálního okna na malý náhled v pravém dolním rohu monitoru 125 % vedle monitoru 100 % nechalo okno velké jako normální okno a přes oba monitory: velikost se projeví až v dalším průchodu rozvržení, poloha hned, takže staré velké okno na chvíli zasáhlo převážně na sousední monitor a Windows ho přeškáloval (WM_DPICHANGED). Oprava: `CameraWindow.ApplySizeBeforeMoving` (před nastavením polohy průchod rozvržení, jen u okna Win32, kde se velikost změní hned). Ověřeno E2E (bez opravy deterministicky selže, s ní projde); headless testy to napodobit neumí.
- **Výsledky (Windows 11 26300, 2 × DELL U2719D, 2026-10-25):**
  - Celá E2E sada proti Debug exe se všemi zásahy (varování + 5 s, deník): 454 testů, 446 prošlo, 7 přeskočeno, 1 selhal jen proto, že chyběl Release build (`ReleaseBuildsContainNoTestSwitches`, `tools/run-e2e.ps1` ho staví sám); po `dotnet build -c Release` prošel. Konec běhu „Differences from the start: none“.
  - Celá E2E sada proti Native AOT exe konfigurace Debug: 454 testů, 446 prošlo, 0 selhalo, 8 přeskočeno, „Differences from the start: none“.
  - Přeskočené: skutečná kamera nedostupná, instalátor Velopack nesestavený, vstupy jen pro podřízené / restore procesy, kanáry pojistky, v AOT kontrola Release buildu.
  - `dotnet publish src/LayerPeek.App -c Release -r win-x64` bez varování (ILC vypisuje jen informační hlášky o `UnmanagedCallersOnly` v `Avalonia.Win32`, nejsou to varování). `dotnet build` 0 varování, `dotnet test` 7432 testů, 0 selhalo.
  - Skripty `tools/run-e2e.ps1` nešlo v tomto prostředí spustit (`pwsh` blokovaný izolací worktree); běh proto přímo testovacím exe se stejnými proměnnými, jaké nastaví skript (varování a odpočet 5 s v konzoli, `LAYERPEEK_E2E_SYSTEM_CHANGE_KINDS` = všechny druhy).
  - Skutečný profil uživatele `%APPDATA%\LayerPeek\` beze změny (SHA-256 `settings.json` i `camera-windows.json` stejné před i po, žádný `windows.json`), žádný proces aplikace nezůstal, deník zásahů prázdný.
- **Zůstává ručně / mimo E2E** (zdůvodnění v přehledu T-065): vzhled skutečného náhledu z PrusaSliceru a skutečná tiskárna, čtení skutečnou čtečkou (Předčítání), slyšitelnost / zvuk toastu „vychladlo“ (unit testy XML), stream 4:3 (headless testy, simulátor má jen 16:9), celkový dojem na skutečném videu, macOS.
- **Poznámka (bez opravy):** náhled 380 × 285 se v čtverci M kreslí s poměrem 4 : 3, takže při 125 % má 37,5 px výšky a při 150 % začíná o půl pixelu níž; okraje jsou o kousek měkčí, test ostrosti to toleruje. Případná změna zarovnání na celé pixely je rozhodnutí designu.
- `develop` mezitím dostal změnu nezlomitelných mezer (`feature/nbsp-units`); větev na ni není rebázovaná. Testy porovnávají texty z `Strings*.resx` a regulární výrazy počítají s libovolnou mezerou, po sloučení je vhodné E2E spustit znovu.
