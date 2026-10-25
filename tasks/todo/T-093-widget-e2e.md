---
id: T-093
title: E2E – widgety překryvu, malý náhled a režim rozložení na skutečné aplikaci + AOT
status: todo
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
- [ ] Všechny body rozsahu mají E2E test, nebo jsou v přehledu pokrytí zdůvodněně označené jako ruční
- [ ] Testy po sobě uklidí vše i po pádu (data, okna, zásahy do systému). Názvy testů v PascalCase bez podtržítek
- [ ] AOT publish bez varování, E2E zelené proti Debug i AOT
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
