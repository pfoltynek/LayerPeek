---
id: T-103
title: Malý náhled – čas dokončení a stav vychladnutí v základním řádku pásu
status: in-progress
owner: ui-designer + ui-dev
priority: P2
platform: all
depends_on: [T-079, T-083, T-088, T-089, T-090, T-101]
---

## Cíl
Základní řádek informačního pásu malého náhledu (T-089, návrh T-076 4) má ukázat víc z toho, co dnes umí jen widgety
překryvu:
1. **čas dokončení** tisku jako widget Časy (T-083): zbývající čas + „Hotovo v 19:50“, se stejnými pravidly formátu času
   (čas podle oblasti, slova a datum podle jazyka aplikace, T-036 Q3),
2. **ikonu stavu vychladnutí** (chladne / vychladlo) jako widget Vychladnutí (T-088). Stav „Vychladlo“ je vizuálně
   zvýrazněný a říká totéž co widget: „Vychladlo – můžete vypnout“.

Postup: nejdřív návrh (ui-designer, tento soubor), implementace později (ui-dev), **až bude v `develop` sloučený T-101**
(proto je T-101 v `depends_on`: oba úkoly mění `Strings.*.resx` a texty nastavení).

## Rozhodnutí (uživatel, 2026-10-29)
- T-103 (teď): do základního řádku pásu přidat čas dokončení (zbývá + „hotovo v HH:MM“ podle pravidel widgetu) a ikonu
  stavu vychladnutí (chladne / vychladlo, při vychladnutí zvýrazněná, v souladu s widgetem „Vychladlo – můžete vypnout“).
  Nejdřív návrh, implementace později (ui-dev) po sloučení T-101.
- T-104 (později): nastavitelný obsah pásu jako u widgetů (výběr položek). Zatím jen zástupný úkol pro návrh.

## Rozsah
- Návrh UI (oddíl Návrh UI níže): rozložení základního řádku při 240 / 320 / 400 DIP, pravidla priority a zkracování,
  ikony, barvy, tooltip a texty pro čtečku, chování podle stavu, souhra s přepínači řádků, nové texty v 8 jazycích.
- Implementace (ui-dev, po odpovědích na Otevřené otázky a po sloučení T-101):
  - `MiniStripContent` doplnit o čas dokončení (`OverlayFinishTime?` z `OverlayValues.Finish`, časové pásmo jako
    `OverlayWidgetInput.TimeZone`) a o průběh chladnutí (podle Q3). Obsah se dál porovnává levně; čas dokončení se mění
    nejvýš jednou za minutu.
  - `CameraMiniStrip.axaml`: skupina „⚑ čas“ za textem souhrnu, výběr nejdelší varianty, která se vejde (bez zkrácení),
    zvýraznění stavu „Vychladlo“.
  - Nové texty v `Strings.*.resx` (všech 8 jazyků, tabulka v Návrhu UI, oddíl 8).
  - Kompilované bindingy, žádná reflexe (AOT), žádný nový balíček.
- Mimo rozsah: výběr položek pásu (T-104), widgety překryvu, notifikace (T-101), tray menu (T-102).

## Akceptační kritéria
- [ ] Při tisku se známým časem dokončení řádek ukáže čas dokončení podle pravidel oddílu 3 (varianta L1 / L2 / L3 podle
      šířky). Formát času je stejný jako ve widgetu Časy (`OverlayFormat.Finish`, region `t`, datum podle jazyka).
- [ ] Skupina času dokončení se nikdy nezkracuje: buď je vidět celá, nebo vůbec. Text „19 % · 2 h 35 min“ se dál nikdy
      nezkracuje při šířce 240 (stávající test).
- [ ] Chladnutí a vychladnutí: ikona ❄ / ⏻ podle oddílu 4, texty podle variant C1–C3 / K1–K2, zvýraznění stavu
      „Vychladlo“ podle odpovědi na Q2 (a pruh podle Q3).
- [ ] Tooltip a `AutomationProperties.Name` souhrnu obsahují celé znění včetně času dokončení a „můžete vypnout“
      (oddíl 6).
- [ ] Nové texty ve všech 8 jazycích (oddíl 8).
- [ ] App.Tests: `MiniStripContent.Of` s časem dokončení (stejný den, jiný den, neznámý, pozastaveno), `Texts` pro všechny
      varianty, žádná alokace při pollu beze změny (stávající `CameraMiniStripAllocationTests`).
- [ ] Headless UI testy: všech 8 jazyků × šířky 240, 320, 400 × stavy tisk (stejný den, jiný den, en-US AM/PM),
      chladne, vychladlo: žádný text souhrnu ani času dokončení není zkrácený mimo pravidla, skupina času se nepřekrývá
      s přepínači (mezera ≥ 8), výška pásu se nemění (30).
- [ ] Snímky: aktualizované `camera-mini-strip-printing`, `camera-mini-strip-cooled-de`, `camera-mini-strip-240-pl`
      a nové `camera-mini-strip-finish-320`, `camera-mini-strip-finish-400-es`, `camera-mini-strip-cooling-240-it`
      (Windows i `Snapshots/macOS/`), vizuálně zkontrolované.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Návrh UI

### 0. Výchozí stav (změřeno 2026-10-29, headless, písmo Inter)
- Pás: `OverlayStripBrush` `#141416`, **pevná tmavá barva v obou motivech** (nezávisí na světlém / tmavém motivu systému).
  Barvy textu `OverlayTextPrimaryBrush` `#F4F4F5`, `Secondary` `#C9C9CE`, `Tertiary` `#A1A1AA`, chladne `#6CB4FF`,
  vychladlo `#6CCB5F`. Ve světlém motivu se tedy nic nemění, stačí jedna sada barev.
- Základní řádek 30: vlevo 10, tečka 8 (nebo ikona 16 při chladnutí), 6, text souhrnu 13 SemiBold `Primary`
  s tabulkovými čísly (`tnum`), 8, přepínače 2 × 30 + 2 = 62, vpravo 6.
- Šířka pro text souhrnu `W = šířka okna − 100` (s ikonou 16 místo tečky `W − 8`):

  | Šířka okna | W (tečka) | W (ikona ❄ / ⏻) |
  |---|---|---|
  | 240 (minimum) | 140 | 132 |
  | 320 (výchozí) | 220 | 212 |
  | 400 | 300 | 292 |

- **Ikona chladnutí v řádku už existuje** (T-089): ❄ `IconWeatherSnowflake20Regular` 16 v `OverlayCoolingBrush`,
  ⏻ `IconPower20Regular` 16 v `OverlayCooledBrush`, místo tečky stavu, s texty „Chladne… 87 °C · cca 6 min“ a
  „Vychladlo · 48 °C“. T-103 tedy u vychladnutí **mění zvýraznění a texty**, ne samotnou přítomnost ikony
  (viz Q1, kdyby uživatel chtěl ikonu jinde).
- Čas dokončení a chladnutí se v řádku **nikdy nepotkají**: čas dokončení je jen u aktivní úlohy (`OverlayValues.ActiveJob`),
  chladnutí jen bez ní (nový tisk chladnutí ukončí, T-079 Q2). Řádek má tedy jeden „doplňkový“ prvek podle fáze.

Naměřené šířky (Inter 13 SemiBold, `tnum`, headless, zaokrouhleno na DIP):

| Text | DIP | Text | DIP |
|---|---|---|---|
| „19 % · 2 h 35 min“ | 109 | „19 % · 23 h 59 min“ | 118 |
| „19:50“ | 39 | „10:50 PM“ (en-US) | 63 |
| „pá 19:50“ | 58 | „Fri 10:50 PM“ | 82 |
| „ven. 19:50“ (fr) | 69 | „vie 19:50“ (es) | 61 |
| „Hotovo v 19:50“ | 98 | „Done at 10:50 PM“ | 114 |
| „Fertig um 19:50“ | 102 | „Termina a las 19:50“ | 125 |
| „Fin à 19:50“ | 72 | „Fine alle 19:50“ / „Koniec o 19:50“ | 94 / 96 |
| „Hotovo 30. října v 19:50“ | 153 | „Termina el 30 de octubre a las 19:50“ | 231 |
| „Vychladlo – můžete vypnout“ | 178 | „Abgekühlt – Sie können ausschalten“ | 227 |
| „Cooled down – safe to turn off“ | 189 | „Refroidie – vous pouvez l'éteindre“ | 211 |
| „Enfriada – ya puede apagarla“ / „Raffreddata – puoi spegnerla“ | 181 / 181 | „Ostygła – można wyłączyć“ / „Vychladla – môžete vypnúť“ | 166 / 170 |
| „Cooled down · 48 °C“ | 129 | „Abgekühlt · 48 °C“ | 112 |
| „Chladne… 87 °C · cca 6 min“ | 173 | „Raffreddamento… 87 °C · circa 6 min“ | 231 |
| „87 °C · cca 6 min“ | 107 | „87 °C · about 16 min“ | 130 |

**Výhrada písma (T-038 otázka 3):** headless testy kreslí přibaleným Inter, skutečná aplikace systémovým písmem
(Windows Segoe UI Variable, macOS San Francisco). SF Pro bývá o 3–8 % širší než Inter. Proto pravidla níže **nezávisí na
pevných prazích šířky okna**: varianta se vybírá měřením textu v aktuálním písmu (oddíl 3.3). Tabulky „co se kde vejde“
jsou odhad pro Inter; na macOS se hranice mohou posunout o pár DIP, chování zůstane správné (vždy celá varianta, nebo
o stupeň kratší).

### 1. Struktura řádku (nová)
```
│←10→[●]←6→[souhrn: Auto, nezkracuje se, pokud se vejde]←10→[⚑14 ←4→ čas]······←8→[⬡][📈]←6→│
        8         13 SemiBold Primary                          13 SemiBold Secondary      30  30
```
- Sloupce: `10, Auto (tečka / ikona), 6, souhrn, 10, doplněk, *, 8, 62, 6`. Souhrn i doplněk mají šířku podle obsahu;
  volné místo je **za** doplňkem (sloupec `*`), takže doplněk těsně navazuje na text (nesedí u přepínačů, aby
  nevypadal jako třetí tlačítko).
- **Doplněk „čas dokončení“**: ikona Flag `IconFlag16Regular` vykreslená 14 × 14 (`CanvasSize` 16, stejná ikona jako
  řádek dokončení ve widgetu Časy), barva `OverlayTextSecondaryBrush`, mezera 4, text 13 SemiBold `Secondary`, `tnum`,
  `NoWrap`, **bez `TextTrimming`** (buď celý, nebo skrytý). Svisle na střed řádku.
- Proč `Secondary` a menší ikona: hlavní informace řádku zůstává procento a zbývající čas (`Primary`); čas dokončení je
  doplňující. Ikona 14 je o stupeň menší než ikony přepínačů (16), aby se s nimi nepletla.
- Výška řádku 30 se nemění, pás ani okno neposkočí.
- Celá oblast souhrnu včetně doplňku je jedno místo pro tooltip, jméno pro čtečku i tažení okna (jako dnes);
  doplněk není fokusovatelný a není tlačítko.

### 2. Drátové modely

Tisk, stejný den, cs (24 h) – výchozí šířka 320 (W = 220):
```
┌────────────────────────────────┐
│             obraz              │ 180
├▓▓▓▓▓▓░░░░░░░░░░░░░░░░░░░░░░░░░┤ 3
│ ● 19 % · 2 h 35 min  ⚑ 19:50       [⬡][📈] │ 30   (109 + 10 + 18 + 39 = 176 ≤ 220 → L2)
└────────────────────────────────┘
```
Tisk, stejný den, 400 (W = 300):
```
│ ● 19 % · 2 h 35 min  ⚑ Hotovo v 19:50          [⬡][📈] │   (109 + 10 + 18 + 98 = 235 ≤ 300 → L1)
│ ● 19 % · 2 h 35 min  ⚑ Termina a las 19:50     [⬡][📈] │   es: 262 ≤ 300 → L1
```
Tisk, dokončení jiný den, 320, cs / en-US:
```
│ ● 19 % · 11 h 5 min  ⚑ so 7:50     [⬡][📈] │   cs: 109 + 28 + ≈ 50 ≈ 187 → L2 (den v týdnu + čas)
│ ● 19% · 9 h 5 min  ⚑ Sat 7:50 AM   [⬡][📈] │   en-US: ≈ 211 → L2 (těsně, při „23 h 59 min“ už L3)
```
Minimální šířka 240 (W = 140), všechny jazyky:
```
┌────────────────────────┐
│         obraz          │ 135
├▓▓▓▓▓░░░░░░░░░░░░░░░░░░┤ 3
│ ● 19 % · 2 h 35 min [⬡][📈] │ 30   (L3: čas dokončení jen v tooltipu, viz Q4)
└────────────────────────┘
```
Pozastaveno / vyžaduje zásah (beze změny, viz Q5):
```
│ ● 19 % · Pozastaveno               [⬡][📈] │
```
Chladne (bez úlohy), 320 / 240:
```
├▒▒▒▒▒▒▒▒▒▒▒▒░░░░░░░░░░░░░░░░░░░┤ 3  (podle Q3: průběh k prahu v OverlayCoolingBrush)
│ ❄ Chladne… 87 °C · cca 6 min       [⬡][📈] │   C1, cs 173 ≤ 212
│ ❄ 87 °C · cca 6 min [⬡][📈] │                 240: C2 (slovo nese ikona ❄), cs 107 ≤ 132
```
Vychladlo, 320 / 240 / de 320:
```
├████████████████████████████████┤ 3  (podle Q3: plný pruh v OverlayCooledBrush)
│ ⏻ Vychladlo – můžete vypnout       [⬡][📈] │   K1, cs 178 ≤ 212; zvýraznění podle Q2
│ ⏻ Vychladlo · 48 °C [⬡][📈] │                  240: K2 (dnešní text), cs 109 ≤ 132
│ ⏻ Abgekühlt · 48 °C                [⬡][📈] │   de 320: K1 227 > 212 → K2
```
Bez úlohy / offline / chyba / před prvním dotazem: beze změny (tečka + název stavu), bez doplňku.

### 3. Pravidla priority a zkracování

#### 3.1 Tisk se známým časem dokončení (`Job`, `Printing`)
Vybere se **první varianta, která se celá vejde** do W:

| Varianta | Souhrn | Doplněk | Kdy se typicky vejde (Inter) |
|---|---|---|---|
| **L1** | „19 % · 2 h 35 min“ | ⚑ + celé znění widgetu (`OverlayFormat.Finish`): „Hotovo v 19:50“, jiný den „Hotovo 30. října v 19:50“ | stejný den od ≈ 335–362 DIP okna (cs 335, en-US 351, es 362); fr („Fin à 19:50“) už od ≈ 310, tedy i při výchozích 320; jiný den jen u širokých oken |
| **L2** | „19 % · 2 h 35 min“ | ⚑ + krátký tvar: stejný den jen čas „19:50“ / „7:50 PM“; jiný den den v týdnu + čas „pá 19:50“ / „Fri 7:50 PM“ (`CameraMiniFinishDayTime`) | od ≈ 276 DIP (24 h), ≈ 300 (en-US); jiný den ≈ 295–320 |
| **L3** | „19 % · 2 h 35 min“ | skrytý (čas dokončení jen v tooltipu a pro čtečku) | vždy (240: W 140 ≥ 118) |
| L4 | souhrn se zkrátí „…“ (dnešní chování) | skrytý | jen extrémní případy (macOS písmo, nestandardní dny) |

- Pořadí důležitosti: procento > zbývající čas > čas dokončení > slovo „Hotovo v“. Nejdřív zmizí slovo (L1 → L2), pak
  celý doplněk (L2 → L3). Zbývající čas se kvůli času dokončení nikdy neschová (alternativa v Q4).
- Jiný den: den v týdnu jen do 6 dnů dopředu. Dál (≥ 7 dnů, nejednoznačný den) jen L1 (celé datum), jinak L3.
- Neznámý zbývající čas, ale známý čas dokončení (teoreticky): souhrn „19 %“, doplněk podle stejných pravidel.
- Neznámý čas dokončení: L3 (doplněk nikdy neukazuje „–“ ani prázdnou vlajku).
- Na hranici šířky se může varianta změnit, když se změní šířka zbývajícího času („2 h 35 min“ → „2 h 9 min“). Mění se
  nejvýš jednou za minutu s obsahem, `tnum` drží čísla stejně široká. Bez hystereze (jednoduchost, deterministické testy).

#### 3.2 Chladnutí a vychladnutí (bez úlohy)
| Fáze | Varianta | Text (cs) | Poznámka |
|---|---|---|---|
| Chladne | **C1** | „Chladne… 87 °C · cca 6 min“ (`CameraMiniCooling`, beze změny) | bez odhadu „Chladne… 87 °C“ (`CameraMiniCoolingNoEstimate`) |
| Chladne | **C2** | „87 °C · cca 6 min“ (nový `CameraMiniCoolingShort`) | slovo „Chladne…“ zastoupí ikona ❄ a tooltip; bez odhadu rovnou C3 |
| Chladne | **C3** | „87 °C“ | poslední stupeň (es „aprox. 16 min“ při 240) |
| Vychladlo | **K1** | „Vychladlo – můžete vypnout“ (stávající `CameraOverlayCooled`, stejné znění jako widget) | teplota jen v tooltipu |
| Vychladlo | **K2** | „Vychladlo · 48 °C“ (`CameraMiniCooled`, beze změny) | při 240 ve všech jazycích; při nedostatku místa „…“ |

Odhad pro Inter: při 320 se K1 vejde ve všech jazycích kromě de (227 > 212); fr 211 je na hraně 1 DIP (na macOS spíš
K2). C1 se při 320 nevejde v fr a it (228 / 231) → C2. Při 240 vždy C2 (nebo C3) a K2.

#### 3.3 Mechanismus (pro ui-dev)
- View model dává seřazené varianty (texty se formátují jen při změně obsahu nebo jazyka, ne při pollu).
- Pohled vybírá při `Measure` první variantu, jejíž šířka (změřená `TextLayout` v aktuálním písmu) se vejde; doplněk se
  skryje celý. Návrh: malý panel `CameraMiniSummaryPanel` (obdoba `OverlayChipsPanel` s `MaxCount`), bez reflexe, bez
  alokací při layoutu beze změny textu a šířky.
- Testy ověřují výsledek měřením (`DrawnLine` / `IsTrimmed`), ne pevnými prahy šířky.

### 4. Ikony a zvýraznění
- Čas dokončení: `IconFlag16Regular` (Fluent UI System Icons, MIT, už v aplikaci od T-083), 14 DIP, `Secondary`.
  Žádné nové artwork ani SVG; čitelnost 14 px ověřit na snímku (Flag má tah 1 px, při 14 se zmenší na 0,875 px –
  pokud bude na snímku rozmazaný, použít 16 a `Tertiary`, viz Q6).
- Chladne: beze změny ❄ 16 `OverlayCoolingBrush` (kontrast `#6CB4FF` na `#141416` ≈ 8,4 : 1, WCAG AA splněno).
- Vychladlo: ⏻ 16 `OverlayCooledBrush` (kontrast `#6CCB5F` na `#141416` ≈ 9,0 : 1). Zvýraznění podle Q2, doporučeno:
  - text K1 „Vychladlo – můžete vypnout“ (stejné znění jako widget), 13 SemiBold `Primary`,
  - **plný pruh 3 na horní hraně pásu** v `OverlayCooledBrush` (Q3), obdoba zelené obruby karty widgetu (T-076 1.1):
    stav má tvar (plný pruh + jiný text + jiná ikona), nejen barvu.
  - Alternativy v Q2: zelené podbarvení řádku (bílá → `#6CCB5F` 14 % přes `#141416` ≈ `#202E20`, text `Primary`
    kontrast ≈ 14 : 1, zelená ikona ≈ 7 : 1) nebo vnitřní obruba 1,5 `OverlayCooledBrush` kolem základního řádku.
- Světlý / tmavý motiv: pás je vždy tmavý (`#141416`), barvy jsou společné pro oba motivy, nic dalšího netřeba.
- Vysoký kontrast (Windows): pás a widgety používají vlastní pevné barvy už dnes (T-076); beze změny.

### 5. Chování podle stavu
| Stav | Tečka / ikona | Souhrn | Doplněk | Horní pruh |
|---|---|---|---|---|
| Tiskne, čas dokončení známý | ● modrá | „19 % · 2 h 35 min“ | ⚑ čas (L1 / L2 / skrytý) | průběh tisku (akcent) |
| Tiskne, čas neznámý | ● modrá | „19 %“ | skrytý | průběh tisku |
| Pozastaveno, vyžaduje zásah, čeká | ● barva stavu | „19 % · Pozastaveno“ | skrytý (Q5) | průběh tisku |
| Dokončeno / zastaveno + chladne | ❄ | C1 / C2 / C3 | – | podle Q3 (průběh k prahu modře / dnes slabý podklad) |
| Vychladlo | ⏻ | K1 / K2 | – | podle Q3 (plný zelený / dnes slabý podklad) |
| Bez úlohy (připravena, dokončeno bez sledování chladnutí) | ● barva stavu | název stavu | – | slabý podklad |
| Offline / chyba / neplatné údaje / před prvním dotazem | ● barva stavu | název stavu | – | slabý podklad |

- Offline během chladnutí: první minutu (`OfflineGrace`) ukazuje řádek stav „Nedostupná“ (tiskárna není dosažitelná,
  dnešní pravidlo `MiniStripContent.Of`); chladnutí se po návratu ukáže znovu, pokud ho tracker mezitím neukončil.
- Změna jazyka nebo oblasti: texty se přeformátují jako dnes (`FormatRowTexts` / `Format`), varianta se vybere znovu.
- Přepínače řádků (⬡ název, 📈 graf) se nemění: stále vpravo, 2 × 30 + mezera 2, 6 od okraje, fokus, tooltipy a jména
  beze změny. Doplněk je vždy ≥ 8 DIP od nich (sloupec 8). Rozbalené řádky názvu a grafu nemají na základní řádek vliv.
  Pořadí tabulátoru se nemění (doplněk není fokusovatelný).

### 6. Tooltip a přístupnost
- Tooltip a `AutomationProperties.Name` souhrnu (`AccessibleText`) obsahují vždy celé znění bez ohledu na variantu:
  - tisk: „Tiskne, 19 %, zbývá 2 h 35 min, Hotovo v 19:50“ (stávající text + `, ` + `OverlayFormat.Finish`),
  - chladne: beze změny „Chladne… 87 °C · cca 6 min“,
  - vychladlo: „Vychladlo – můžete vypnout, tryska 48 °C“ (nový `CameraMiniCooledAccessible`).
- Ikony nemají vlastní jméno (jsou součástí obsahu souhrnu, `AccessibilityView="Content"` zůstává na souhrnu).
- Informace nikdy jen barvou: čas dokončení má vlajku, vychladnutí jinou ikonu, jiný text a (Q3) jiný tvar pruhu.
- Žádné nové interaktivní prvky → cílové plochy beze změny (odchylka 30 < 32 u přepínačů trvá, T-076 Q5).

### 7. Rizika podle jazyků
- es má nejdelší „Hotovo v“ („Termina a las“, 125) a „aprox.“; při 400 se L1 ještě vejde (262 ≤ 300).
- de: K1 „Abgekühlt – Sie können ausschalten“ (227) se při výchozí šířce 320 nevejde → K2 „Abgekühlt · 48 °C“.
  Uživatel v de tedy uvidí „můžete vypnout“ až od ≈ 335 DIP nebo v tooltipu (Q2 alternativa: kratší text pro pás).
- fr / it: C1 „Refroidissement…“ / „Raffreddamento…“ se při 320 nevejde → C2 „87 °C · env. 6 min“.
- en-US: 12h čas je o 24 DIP širší; jiný den („Fri 10:50 PM“) se při 320 vejde jen s krátkým zbývajícím časem
  (souhrn ≤ 110 DIP, např. „19% · 9 h 59 min“), při dvoumístných hodinách a minutách („23 h 59 min“) L3.
- fr: „Fin à 19:50“ je krátké → při 320 jako jediný jazyk L1 (109 + 28 + 72 = 209 ≤ 220).
- pl: nejdelší zkratka dne „niedz.“ (≈ 74 s časem); stále L2 při 320.
- Data dnů v týdnu: `CultureInfo.DateTimeFormat.AbbreviatedDayNames` jazyka aplikace (jako `MonthDay`). Na macOS
  (ICU) změřeno: cs „pá“, sk „pi“, en „Fri“, de „Fr“, es „vie“, fr „ven.“, it „ven“, pl „pt.“. Na Windows (ICU
  od .NET 5 také) by měly být stejné; ověřit na Windows při implementaci.

### 8. Nové texty (klíče `Strings.*`)
`&#160;` = nezalomitelná mezera (jako u stávajících textů s °C). Stávající klíče se jen používají:
`CameraOverlayFinishTime`, `CameraOverlayFinishDateTime`, `CameraOverlayCooled`, `CameraMiniCooling`,
`CameraMiniCoolingNoEstimate`, `CameraMiniCooled`, `ToolTipRemaining`.

| Klíč | en | cs | sk | de |
|---|---|---|---|---|
| `CameraMiniCoolingShort` | `{0}&#160;°C · about {1}` | `{0}&#160;°C · cca {1}` | `{0}&#160;°C · cca {1}` | `{0}&#160;°C · ca. {1}` |
| `CameraMiniCooledAccessible` | `Cooled down – safe to turn off, nozzle {0}&#160;°C` | `Vychladlo – můžete vypnout, tryska {0}&#160;°C` | `Vychladla – môžete vypnúť, tryska {0}&#160;°C` | `Abgekühlt – Sie können ausschalten, Düse {0}&#160;°C` |
| `CameraMiniFinishDayTime` | `{0} {1}` | `{0} {1}` | `{0} {1}` | `{0} {1}` |

| Klíč | es | fr | it | pl |
|---|---|---|---|---|
| `CameraMiniCoolingShort` | `{0}&#160;°C · aprox. {1}` | `{0}&#160;°C · env. {1}` | `{0}&#160;°C · circa {1}` | `{0}&#160;°C · ok. {1}` |
| `CameraMiniCooledAccessible` | `Enfriada – ya puede apagarla, boquilla {0}&#160;°C` | `Refroidie – vous pouvez l'éteindre, buse {0}&#160;°C` | `Raffreddata – puoi spegnerla, ugello {0}&#160;°C` | `Ostygła – można wyłączyć, dysza {0}&#160;°C` |
| `CameraMiniFinishDayTime` | `{0} {1}` | `{0} {1}` | `{0} {1}` | `{0} {1}` |

- `CameraMiniFinishDayTime`: `{0}` = zkratka dne v týdnu v jazyce aplikace, `{1}` = čas podle oblasti (`t`). Komentář
  v resx: pořadí smí překladatel změnit. Mezi nimi nezalomitelná mezera (text se nezalamuje, jen kvůli konzistenci).
- `CameraMiniCoolingShort`: stejná zkratka „cca“ jako v `CameraMiniCooling` daného jazyka.
- Pokud Q2 dopadne alternativou „kratší text pro pás“, přibude `CameraMiniCooledShort` (návrh cs „Vychladlo – lze
  vypnout“, de „Abgekühlt · ausschaltbar“; doplní se po odpovědi).

### 9. Odkazy na zásady platforem
- Windows: Fluent typografie (Body 13 / SemiBold pro hlavní hodnotu), ikony Fluent UI System Icons 16 / 20,
  „Don't rely on color alone“ (Windows accessibility – color & contrast), WCAG 2.2 AA 1.4.3 a 1.4.11.
- macOS HIG: „Menu bar extras“ / „Panels“ – stručný stavový text, plné znění v nápovědě (tooltip), „Color“ – barva
  nesmí být jediný nositel stavu; SF Symbols se nepoužívají (aplikace má jednotnou sadu Fluent ikon na obou OS).
- Platformní rozdíly: žádné v rozložení (pás je vlastní kreslený prvek). Rozdíl jen v písmu (oddíl 0) a v datech
  kultury (zkratky dnů, oddíl 7).

## Otevřené otázky
- **Q1 – Kde má být ikona stavu vychladnutí?** Ikona ❄ / ⏻ už dnes v řádku je (místo tečky, T-089).
  - (A) **Doporučeno:** nechat ji místo tečky jako hlavní obsah řádku po tisku; T-103 jen zvýrazní „Vychladlo“ a upraví
    texty (oddíl 3.2). Po tisku je vychladnutí nejdůležitější informace, „Dokončeno“ přidává málo.
  - (B) Řádek ukazuje dál stav („● Dokončeno“) a vychladnutí je doplněk vpravo od textu, stejně jako čas dokončení:
    „● Dokončeno  ❄ 87 °C · 6 min“ / „● Dokončeno  ⏻ 48 °C“. Konzistentní „slot doplňku“, ale při 240 se doplněk vejde
    jen zkrácený a hláška „můžete vypnout“ by byla jen v tooltipu.
- **Q2 – Jak zvýraznit stav „Vychladlo“?**
  - (A) **Doporučeno:** text „Vychladlo – můžete vypnout“, když se vejde (jinak „Vychladlo · 48 °C“), zelená ikona ⏻ a
    plný zelený horní pruh (Q3).
  - (B) Jako A + zelené podbarvení základního řádku (≈ `#202E20`).
  - (C) Jako A + vnitřní zelená obruba 1,5 kolem základního řádku (přímá obdoba karty widgetu).
  - (D) Kratší text jen pro pás („Vychladlo – lze vypnout“), aby se při 320 vešel i v de; nový klíč ve všech jazycích.
- **Q3 – Horní pruh (3 DIP) po tisku?** Dnes bez úlohy jen slabý podklad.
  - (A) **Doporučeno:** chladne = průběh k prahu v `OverlayCoolingBrush` (stejný jako pruh widgetu), vychladlo = plný
    pruh v `OverlayCooledBrush`.
  - (B) Jen vychladlo plný zelený, při chladnutí slabý podklad.
  - (C) Beze změny (slabý podklad).
- **Q4 – Co při úzkém okně (240) dřív: zbývající čas, nebo čas dokončení?** Při 240 se oba nevejdou v žádném jazyce.
  - (A) **Doporučeno:** zbývající čas („19 % · 2 h 35 min“), čas dokončení jen v tooltipu (jako widget Časy ve velikosti
    S, který také ukazuje jen zbývající čas).
  - (B) „19 % · ⚑ 19:50“ – čas dokončení místo zbývajícího času.
  - (C) Uživatel si vybere (to je pak součást T-104).
- **Q5 – Čas dokončení i při pozastavení / vyžaduje zásah?** Dnes pás při jiném stavu než „Tiskne“ ukazuje místo
  zbývajícího času slovo stavu a odhad tiskárny se během pauzy posouvá.
  - (A) **Doporučeno:** ne, jen při tisku (stejné pravidlo jako zbývající čas v pásu, T-076 4).
  - (B) Ano, jako widget Časy (ten ukazuje časy u každé aktivní úlohy).
- **Q6 – Velikost a barva vlajky:** (A) **doporučeno** 14 DIP `Secondary` (menší než ikony přepínačů, nepletou se);
  (B) 16 DIP `Tertiary` (ostřejší vykreslení 16px ikony, ale vizuálně blíž přepínačům). Rozhodne se i podle snímku při
  implementaci; pokud je 14 na snímku čitelná, platí A.
- **Q7 – Jiný den:** (A) **doporučeno** zkratka dne v týdnu „pá 19:50“ (bez nových slov, kultura je dodá);
  (B) „zítra 19:50“ a dál zkratka dne (nový text „zítra“ v 8 jazycích); (C) krátké datum „30. 10. 19:50“ (širší,
  ≈ 88 DIP, při 320 by v en-US vypadlo).

## Poznámky k implementaci
(vyplní ui-dev po dokončení; návrh ui-designer 2026-10-29, šířky změřené dočasným headless testem s písmem Inter,
test není v repozitáři)
