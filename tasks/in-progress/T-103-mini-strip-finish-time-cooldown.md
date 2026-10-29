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

## Rozhodnutí k návrhu (uživatel, 2026-10-29)
- **Q1 → A:** ikona ❄ / ⏻ zůstává místo tečky stavu jako hlavní obsah řádku po tisku (oddíl 3.2).
- **Q2 → A:** stav „Vychladlo“ = text K1 „Vychladlo – můžete vypnout“ (když se nevejde, K2 „Vychladlo · 48 °C“),
  zelená ikona ⏻ a plný zelený horní pruh. Bez podbarvení, bez obruby, bez nového kratšího textu.
- **Q3 → A:** horní pruh při chladnutí ukazuje průběh k prahu v `OverlayCoolingBrush`, po vychladnutí je plný
  v `OverlayCooledBrush`.
- **Q4 → A:** při nedostatku místa (240) zůstává zbývající čas, čas dokončení je jen v tooltipu a pro čtečku.
- **Q5 → A:** čas dokončení jen při stavu „Tiskne“ (pozastaveno, vyžaduje zásah, čeká: beze změny).
- **Q6 → A:** vlajka 14 DIP v `OverlayTextSecondaryBrush`.
- **Q7 → A:** dokončení v jiný den = zkratka dne v týdnu + čas („pá 19:50“, `CameraMiniFinishDayTime`).

## Rozsah
- Návrh UI (oddíl Návrh UI níže): rozložení základního řádku při 240 / 320 / 400 DIP, pravidla priority a zkracování,
  ikony, barvy, tooltip a texty pro čtečku, chování podle stavu, souhra s přepínači řádků, nové texty v 8 jazycích.
- Implementace (ui-dev, po odpovědích na Otevřené otázky a po sloučení T-101):
  - `MiniStripContent` doplnit o čas dokončení (`OverlayFinishTime?` z `OverlayValues.Finish`, časové pásmo jako
    `OverlayWidgetInput.TimeZone`) a o průběh chladnutí k prahu (pro horní pruh, Q3 → A). Obsah se dál porovnává levně; čas dokončení se mění
    nejvýš jednou za minutu.
  - `CameraMiniStrip.axaml`: skupina „⚑ čas“ za textem souhrnu, výběr nejdelší varianty, která se vejde (bez zkrácení),
    zvýraznění stavu „Vychladlo“.
  - Nové texty v `Strings.*.resx` (všech 8 jazyků, tabulka v Návrhu UI, oddíl 8).
  - Kompilované bindingy, žádná reflexe (AOT), žádný nový balíček.
- Mimo rozsah: výběr položek pásu (T-104), widgety překryvu, notifikace (T-101), tray menu (T-102).

## Akceptační kritéria
- [x] Při tisku se známým časem dokončení řádek ukáže čas dokončení podle pravidel oddílu 3 (varianta L1 / L2 / L3 podle
      šířky). Formát času je stejný jako ve widgetu Časy (`OverlayFormat.Finish`, region `t`, datum podle jazyka).
- [x] Skupina času dokončení se nikdy nezkracuje: buď je vidět celá, nebo vůbec. Text „19 % · 2 h 35 min“ se dál nikdy
      nezkracuje při šířce 240 (stávající test).
- [x] Chladnutí a vychladnutí: ikona ❄ / ⏻ místo tečky (oddíl 4), texty podle variant C1–C3 / K1–K2, horní pruh při
      chladnutí = průběh k prahu modře, po vychladnutí plný zeleně (Q2, Q3 → A).
- [x] Tooltip a `AutomationProperties.Name` souhrnu obsahují celé znění včetně času dokončení a „můžete vypnout“
      (oddíl 6).
- [x] Nové texty ve všech 8 jazycích (oddíl 8).
- [x] App.Tests: `MiniStripContent.Of` s časem dokončení (stejný den, jiný den, neznámý, pozastaveno), `Texts` pro všechny
      varianty, žádná alokace při pollu beze změny (stávající `CameraMiniStripAllocationTests`).
- [x] Headless UI testy: všech 8 jazyků × šířky 240, 320, 400 × stavy tisk (stejný den, jiný den, en-US AM/PM),
      chladne, vychladlo: žádný text souhrnu ani času dokončení není zkrácený mimo pravidla, skupina času se nepřekrývá
      s přepínači (mezera ≥ 8), výška pásu se nemění (30).
- [ ] Snímky: aktualizované `camera-mini-strip-printing`, `camera-mini-strip-cooled-de`, `camera-mini-strip-240-pl`
      a nové `camera-mini-strip-finish-320`, `camera-mini-strip-finish-400-es`, `camera-mini-strip-cooling-240-it`
      (Windows i `Snapshots/macOS/`), vizuálně zkontrolované.
      *(macOS sada hotová a prohlédnutá ui-dev; zbývá: vygenerovat sadu pro Windows na Windows a vizuální kontrola
      uživatelem, viz Poznámky k implementaci.)*
- [x] `dotnet build` a `dotnet test` bez chyb a varování

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
  „Vychladlo · 48 °C“. T-103 tedy u vychladnutí **mění zvýraznění, texty a horní pruh**, ikona zůstává místo tečky
  (Q1 → A).
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
│ ● 19 % · 2 h 35 min [⬡][📈] │ 30   (L3: čas dokončení jen v tooltipu, Q4 → A)
└────────────────────────┘
```
Pozastaveno / vyžaduje zásah (beze změny, Q5 → A):
```
│ ● 19 % · Pozastaveno               [⬡][📈] │
```
Chladne (bez úlohy), 320 / 240:
```
├▒▒▒▒▒▒▒▒▒▒▒▒░░░░░░░░░░░░░░░░░░░┤ 3  (Q3 → A: průběh k prahu v OverlayCoolingBrush)
│ ❄ Chladne… 87 °C · cca 6 min       [⬡][📈] │   C1, cs 173 ≤ 212
│ ❄ 87 °C · cca 6 min [⬡][📈] │                 240: C2 (slovo nese ikona ❄), cs 107 ≤ 132
```
Vychladlo, 320 / 240 / de 320:
```
├████████████████████████████████┤ 3  (Q3 → A: plný pruh v OverlayCooledBrush)
│ ⏻ Vychladlo – můžete vypnout       [⬡][📈] │   K1, cs 178 ≤ 212; zvýraznění Q2 → A
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
  celý doplněk (L2 → L3). Zbývající čas se kvůli času dokončení nikdy neschová (Q4 → A).
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
  Žádné nové artwork ani SVG (Q6 → A). Čitelnost 14 px ověřit na snímku (Flag má tah 1 px, při 14 se zmenší na
  0,875 px); kdyby byl na snímku nečitelný, ui-dev to nahlásí uživateli – na 16 / `Tertiary` se sám nepřepíná.
- Chladne: beze změny ❄ 16 `OverlayCoolingBrush` (kontrast `#6CB4FF` na `#141416` ≈ 8,4 : 1, WCAG AA splněno).
- Vychladlo: ⏻ 16 `OverlayCooledBrush` (kontrast `#6CCB5F` na `#141416` ≈ 9,0 : 1). Zvýraznění (Q2 → A):
  - text K1 „Vychladlo – můžete vypnout“ (stejné znění jako widget), 13 SemiBold `Primary`,
  - **plný pruh 3 na horní hraně pásu** v `OverlayCooledBrush` (Q3 → A), obdoba zelené obruby karty widgetu (T-076 1.1):
    stav má tvar (plný pruh + jiný text + jiná ikona), nejen barvu.
  - Bez podbarvení řádku a bez obruby (zamítnuté alternativy Q2 B / C).
- Chladne – horní pruh (Q3 → A): průběh k prahu (`CooldownState.Progress`, 0–100 %, stejně jako pruh widgetu)
  ve výplni `OverlayCoolingBrush` na podkladu `OverlayTrackBrush`; po vychladnutí 100 % v `OverlayCooledBrush`.
- Světlý / tmavý motiv: pás je vždy tmavý (`#141416`), barvy jsou společné pro oba motivy, nic dalšího netřeba.
- Vysoký kontrast (Windows): pás a widgety používají vlastní pevné barvy už dnes (T-076); beze změny.

### 5. Chování podle stavu
| Stav | Tečka / ikona | Souhrn | Doplněk | Horní pruh |
|---|---|---|---|---|
| Tiskne, čas dokončení známý | ● modrá | „19 % · 2 h 35 min“ | ⚑ čas (L1 / L2 / skrytý) | průběh tisku (akcent) |
| Tiskne, čas neznámý | ● modrá | „19 %“ | skrytý | průběh tisku |
| Pozastaveno, vyžaduje zásah, čeká | ● barva stavu | „19 % · Pozastaveno“ | skrytý (Q5 → A) | průběh tisku |
| Dokončeno / zastaveno + chladne | ❄ | C1 / C2 / C3 | – | průběh k prahu v `OverlayCoolingBrush` |
| Vychladlo | ⏻ | K1 / K2 | – | plný v `OverlayCooledBrush` |
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
- Informace nikdy jen barvou: čas dokončení má vlajku, vychladnutí jinou ikonu, jiný text a plný pruh (Q3 → A).
- Žádné nové interaktivní prvky → cílové plochy beze změny (odchylka 30 < 32 u přepínačů trvá, T-076 Q5).

### 7. Rizika podle jazyků
- es má nejdelší „Hotovo v“ („Termina a las“, 125) a „aprox.“; při 400 se L1 ještě vejde (262 ≤ 300).
- de: K1 „Abgekühlt – Sie können ausschalten“ (227) se při výchozí šířce 320 nevejde → K2 „Abgekühlt · 48 °C“.
  Uživatel v de tedy uvidí „můžete vypnout“ až od ≈ 335 DIP nebo v tooltipu (kratší text pro pás uživatel nezvolil,
  Q2 → A); stav nese i zelená ikona a plný zelený pruh.
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
| `CameraMiniFinishDayTime` | `{0}&#160;{1}` | `{0}&#160;{1}` | `{0}&#160;{1}` | `{0}&#160;{1}` |

| Klíč | es | fr | it | pl |
|---|---|---|---|---|
| `CameraMiniCoolingShort` | `{0}&#160;°C · aprox. {1}` | `{0}&#160;°C · env. {1}` | `{0}&#160;°C · circa {1}` | `{0}&#160;°C · ok. {1}` |
| `CameraMiniCooledAccessible` | `Enfriada – ya puede apagarla, boquilla {0}&#160;°C` | `Refroidie – vous pouvez l'éteindre, buse {0}&#160;°C` | `Raffreddata – puoi spegnerla, ugello {0}&#160;°C` | `Ostygła – można wyłączyć, dysza {0}&#160;°C` |
| `CameraMiniFinishDayTime` | `{0}&#160;{1}` | `{0}&#160;{1}` | `{0}&#160;{1}` | `{0}&#160;{1}` |

- `CameraMiniFinishDayTime`: `{0}` = zkratka dne v týdnu v jazyce aplikace, `{1}` = čas podle oblasti (`t`). Komentář
  v resx: pořadí smí překladatel změnit. Mezi nimi nezalomitelná mezera (text se nezalamuje, jen kvůli konzistenci).
- `CameraMiniCoolingShort`: stejná zkratka „cca“ jako v `CameraMiniCooling` daného jazyka.
- `CameraMiniCooledShort` není potřeba (Q2 → A, bez kratšího textu pro pás).

### 9. Odkazy na zásady platforem
- Windows: Fluent typografie (Body 13 / SemiBold pro hlavní hodnotu), ikony Fluent UI System Icons 16 / 20,
  „Don't rely on color alone“ (Windows accessibility – color & contrast), WCAG 2.2 AA 1.4.3 a 1.4.11.
- macOS HIG: „Menu bar extras“ / „Panels“ – stručný stavový text, plné znění v nápovědě (tooltip), „Color“ – barva
  nesmí být jediný nositel stavu; SF Symbols se nepoužívají (aplikace má jednotnou sadu Fluent ikon na obou OS).
- Platformní rozdíly: žádné v rozložení (pás je vlastní kreslený prvek). Rozdíl jen v písmu (oddíl 0) a v datech
  kultury (zkratky dnů, oddíl 7).

## Otevřené otázky
Všechny otázky rozhodl uživatel 2026-10-29 (ve všech případech doporučená varianta A), viz Rozhodnutí k návrhu.
- ~~**Q1 – Kde má být ikona stavu vychladnutí?** (A) místo tečky jako hlavní obsah řádku po tisku / (B) „● Dokončeno“ +
  vychladnutí jako doplněk za textem.~~ (rozhodnuto 2026-10-29: A)
- ~~**Q2 – Jak zvýraznit stav „Vychladlo“?** (A) text K1 + zelená ikona ⏻ + plný zelený horní pruh / (B) A + zelené
  podbarvení řádku / (C) A + zelená obruba řádku / (D) kratší text jen pro pás.~~ (rozhodnuto 2026-10-29: A)
- ~~**Q3 – Horní pruh (3 DIP) po tisku?** (A) chladne = průběh k prahu modře, vychladlo = plný zelený / (B) jen
  vychladlo zeleně / (C) beze změny.~~ (rozhodnuto 2026-10-29: A)
- ~~**Q4 – Co při 240 dřív: zbývající čas, nebo čas dokončení?** (A) zbývající čas, čas dokončení jen v tooltipu /
  (B) „19 % · ⚑ 19:50“ / (C) volba uživatele (T-104).~~ (rozhodnuto 2026-10-29: A)
- ~~**Q5 – Čas dokončení i při pozastavení / vyžaduje zásah?** (A) ne, jen při tisku / (B) ano, jako widget Časy.~~
  (rozhodnuto 2026-10-29: A)
- ~~**Q6 – Velikost a barva vlajky:** (A) 14 DIP `Secondary` / (B) 16 DIP `Tertiary`.~~ (rozhodnuto 2026-10-29: A)
- ~~**Q7 – Jiný den:** (A) zkratka dne v týdnu „pá 19:50“ / (B) „zítra 19:50“ / (C) krátké datum „30. 10. 19:50“.~~
  (rozhodnuto 2026-10-29: A)

## Poznámky k implementaci
Návrh ui-designer 2026-10-29 (šířky změřené dočasným headless testem s písmem Inter, test není v repozitáři);
implementace ui-dev 2026-10-29 na macOS (větev `feature/T-103-mini-strip-finish-time-cooldown-impl`).

- **Obsah:** `MiniStripContent` má navíc `Finish` (`OverlayFinishTime?` z `OverlayValues.Finish`, jen při stavu Tiskne,
  Q5), `FinishDays` (místní dny od snapshotu) a `CoolingPercent` (celé procento k prahu zaokrouhlené dolů jako widget,
  po vychladnutí 100). `Of` dostává časové pásmo hodin okna (`TimeProvider.LocalTimeZone`, jako
  `OverlayWidgetInput.TimeZone`). Porovnání zůstává levné, poll beze změny nic nealokuje ani neposílá (stávající
  `CameraMiniStripAllocationTests` beze změny prochází i s výpočtem času dokončení).
- **Varianty:** `CameraMiniStripViewModel.Texts` vrací `MiniStripTexts` = až tři `MiniStripVariant` (souhrn + čas za
  vlajkou), nejdelší první, a celé znění pro tooltip / čtečku. Tisk: L1 (`OverlayFormat.Finish`), L2 (stejný den jen
  `OverlayFormat.FinishClock`, jiný den do 6 dnů `OverlayFormat.FinishDayTime` = `CameraMiniFinishDayTime`), L3 bez
  doplňku; ≥ 7 dnů jen L1 a L3. Chladne: C1 / C2 (`CameraMiniCoolingShort`) / C3 (`DiagnosticsTemperatureValue`),
  bez odhadu C1 / C3. Vychladlo: K1 (`CameraOverlayCooled`) / K2 (`CameraMiniCooled`), tooltip
  `CameraMiniCooledAccessible`. Texty se formátují jen při změně obsahu nebo jazyka.
- **Výběr:** nový `CameraMiniSummaryPanel` (obdoba `OverlayChipsPanel`): děti jsou tři sloty (souhrn `*` + skupina
  `⚑14 · 4 · čas` s odsazením 10) navázané kompilovanými bindingy na `FirstVariant` / `SecondVariant` / `ThirdVariant`.
  Panel měří sloty bez omezení a vykreslí první, který se celý vejde; když žádný, poslední (vždy bez času dokončení)
  dostane dostupnou šířku a souhrn se zkrátí „…“ (L4). Ostatní sloty leží za oříznutým pravým okrajem a nejsou ve
  stromu automatizace. Volné místo je za doplňkem; mezi panelem a přepínači zůstává sloupec 8.
- **Horní pruh:** třídy `cooling` / `cooled` na `CameraMiniProgress` (výplň `OverlayCoolingBrush` / `OverlayCooledBrush`
  na podkladu `OverlayTrackBrush`), `HasProgress` platí i pro chladnutí a vychladnutí.
- **Výsledek měření (headless, Inter) odpovídá návrhu:** 240 → L3 / C2 nebo C3 / K2 ve všech jazycích; 320 → cs L2,
  fr L1, cs jiný den L2, cs K1, de K2; 400 → cs, es, en-US L1. Test tyto výsledky drží, pravidlo samo se ověřuje měřením.
- **Vlajka 14 DIP** je na snímcích při 100 % čitelná (tenký tah, ale tvar vlajky je zřetelný); 16 / `Tertiary` jsem
  nepoužil (Q6 → A).
- **Kultura – Windows vs. macOS:**
  - Zkratky dnů v týdnu bere `OverlayFormat.FinishDayTime` z `DateTimeFormat.GetAbbreviatedDayName` kultury jazyka
    aplikace (specifické, jako `MonthDay`). Obě platformy používají ICU (CLDR), Windows ale systémovou ICU starší
    verze. Na macOS: cs „pá“, sk „pi“, en „Fri“, de „Fr“, es „vie“, fr „ven.“, it „ven“, pl „pt.“. Test
    `DayAbbreviationsOfTheLanguagesWithStableCalendarData` drží jen en / cs / de / it (stabilní v CLDR), ostatní test
    jen kontroluje, že zkratka je v jazyce aplikace. **Na Windows neověřeno** (zejména es / pl).
  - en-US odděluje AM/PM na macOS U+202F, na Windows mezerou (`CultureTime.EnUs` v testech); šířka se tím nemění
    podstatně, výběr varianty je stejně měřený.
  - Písmo: skutečná aplikace kreslí systémovým písmem (Segoe UI Variable / SF Pro), hranice variant se proto mohou
    posunout o pár DIP; chování zůstane „celá varianta, nebo o stupeň kratší“.
- **Testy:** App.Tests `CameraMiniStripFinishTimeTests` (obsah a varianty pro tisk stejný den / jiný den / ≥ 7 dnů /
  neznámý / pozastaveno / vyžaduje zásah, chladne s odhadem i bez, vychladlo, bez úlohy, offline, před prvním dotazem,
  časové pásmo, cs s 24 h, zkratky dnů ve všech 8 jazycích, nové klíče ve všech jazycích), upravené
  `CameraMiniStripViewModelTests` (pruh chladnutí, K1, změna jazyka). UiTests `CameraWindowMiniStripFinishUiTests`
  (8 jazyků × vlastní region i en-US × 240 / 320 / 400 × tisk stejný den, jiný den, chladne, vychladlo: první
  varianta, která se vejde, nic nezkráceno mimo L4, čas dokončení nikdy, ≥ 8 od přepínačů, výška 33; tooltip a jméno,
  jen vykreslená varianta ve stromu automatizace; barvy a hodnoty horního pruhu).
- **Snímky (macOS, `Snapshots/macOS/`):** aktualizované `camera-mini-strip-printing` (L2 „⚑ 2:35 PM“),
  `camera-mini-strip-cooled-de` (plný zelený pruh), `camera-mini-strip-rows` (tisk při 320 má nově čas dokončení);
  nové `camera-mini-strip-finish-320` (cs, L2), `camera-mini-strip-finish-400-es` (L1), `camera-mini-strip-cooling-240-it`
  (C2). `camera-mini-strip-240-pl` (stav Neautorizováno) se pixelově nezměnil, reference zůstává.
  **Windows sada zatím chybí / je stará:** na Windows spustit
  `LAYERPEEK_UPDATE_SNAPSHOTS=1 dotnet test --project tests/LayerPeek.App.UiTests` a zkontrolovat
  `camera-mini-strip-printing`, `camera-mini-strip-cooled-de`, `camera-mini-strip-rows`, `camera-mini-strip-finish-320`,
  `camera-mini-strip-finish-400-es`, `camera-mini-strip-cooling-240-it`. Do té doby tyto testy na Windows selžou.
- Žádný nový balíček, žádná reflexe (kompilované bindingy, `ObjectConverters.IsNotNull`), AOT beze změny.
- Úkol zůstává `in-progress`: chybí Windows snímky a vizuální kontrola uživatelem (kritérium Snímky).
