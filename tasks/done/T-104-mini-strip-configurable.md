---
id: T-104
title: Malý náhled – nastavitelný obsah pásu (výběr položek jako u widgetů) – návrh
status: done
owner: ui-designer
priority: P3
platform: all
depends_on: [T-092, T-103]
---

## Cíl
Uživatel si sám vybere, co ukazuje informační pás malého náhledu, podobně jako u widgetů překryvu na záložce Widgety
(T-092). Kandidátní položky: průběh (horní pruh a procento), zbývající čas, čas dokončení, stav vychladnutí, řádek
názvu tisku, řádek grafu teplot.

Zatím **jen zástupný úkol pro návrh** (rozhodnutí uživatele 2026-10-29). Začne se až po T-103, který do základního řádku
přidá čas dokončení a zvýrazněné vychladnutí; návrh T-104 na jeho pravidla priority (T-103, oddíl 3) naváže.

## Rozsah
- Návrh UI (ui-designer, Česky, oddíl `## Návrh UI`): kde se pás nastavuje, jak se volby promítnou do základního řádku
  a řádků pod ním, výchozí stav (= dnešní chování po T-103), chování při úzkém okně (240), texty v 8 jazycích.
- Rozdělení na implementační úkoly (Core: model a uložení nastavení; App: UI nastavení, pás) – po odpovědích na otázky.
- Mimo rozsah: implementace v tomto úkolu.

## Rozhodnutí (uživatel, 2026-10-30)
Všechny otázky podle doporučení (varianta A):
- **Q1 A:** pás se nastavuje na záložce Widgety, položka „Malý náhled“ pod oddělovačem na konci seznamu, vlastní panel
  (oddíl 2), bez zaškrtávacího pole viditelnosti.
- **Q2 A:** u každé tiskárny (`printers[*].camera.miniStrip`), pod výběrem tiskárny jako rozložení widgetů.
- **Q3 A:** 7 nezávislých položek (oddíl 1); minimum je vždy tečka / ikona + název stavu. Dílčí **(a)**: vypnutý pruh
  průběhu = slabý podklad 3 DIP, výška pásu beze změny. Dílčí **(c)**: vypnutá položka není ani v tooltipu
  (a `AutomationProperties.Name`).
- **Q4 A:** nastavení určuje dostupnost řádků: vypnutý řádek = přepínač ⬡ / 📈 zmizí a řádek se nikdy neukáže; zapnutý
  = přepínač zůstává a rozbalení se pamatuje jako dnes. Skryté přepínače uvolní místo pro souhrn.
- **Q5 A:** pevné pořadí podle T-103.
- **Q6 A:** pevná pravidla priority T-103 nad zapnutými položkami, bez volby.
- **Q7 A:** formát času dokončení jen automatikou T-103, bez volby.
- **Q8 A:** bez živého náhledu v nastavení.
- **Q9 A:** `settings.json` u tiskárny, zapisují se jen vypnuté položky, chybějící = zapnuto, bez změny verze; nezapomíná
  se se stavem okna (`Remove`), `camera-windows.json` se nemění.
- **Q10 A:** „Obnovit výchozí“ i „Použít rozložení u všech tiskáren“ zahrnou i pás vybrané tiskárny; texty tlačítek
  beze změny, tooltip `SettingsWidgetsResetHelp` doplněný.

## Akceptační kritéria
- [x] Návrh UI s drátovými modely (nastavení i pás při 240 / 320 / 400) a pravidly priority.
- [x] Odpovědi uživatele na Otevřené otázky zapsané v `## Rozhodnutí`.
- [x] Navazující implementační úkoly v `tasks/todo/` a v `tasks/README.md` (T-110).

## Návrh UI

Návrh ui-designer, 2026-10-29 (macOS, headless snímky `Snapshots/macOS/`). Navazuje na schválený návrh T-103 (oddíly
1–6, rozhodnutí Q1–Q7) a na záložku Widgety z T-092 (Návrh UI 3–5). Rozměry v DIP, čeština vyká.
**Stav: rozhodnuto 2026-10-30, vše podle doporučení (oddíl Rozhodnutí)** (oddíl Otevřené otázky; u každé otázky je doporučená varianta označená
„(doporučeno)“). Drátové modely níže ukazují doporučené varianty. Předpokládá se T-109 (prahy trysky a podložky
v panelu Vychladnutí), který se tohoto návrhu netýká: panel Vychladnutí zůstává panelem widgetu, malý náhled má vlastní
položku.

### 0. Výchozí stav (změřeno 2026-10-29)
- **Pás po T-103** (`CameraMiniStrip.axaml`): horní pruh 3, základní řádek 30 = `10, tečka/ikona, 6, souhrn (*), 8,
  přepínače 62, 6`; šířka souhrnu `W = šířka okna − 100` (240 → 140, 320 → 220, 400 → 300). Řádek názvu 34 a řádek
  grafu 64 se rozbalují přepínači ⬡ / 📈 (T-090), stav rozbalení je u tiskárny v `camera-windows.json`
  (`miniNameRow` / `miniChartRow`) a zapomíná se s pamatovaným stavem okna (`Remove`).
- **Záložka Widgety** (`settings-widgets.png`, `settings-widgets-cooldown-de.png`): výběr tiskárny nahoře, seznam
  216 × ≈ 228 (řádky 32: zaškrtávací pole viditelnosti, ikona 16, název), panel vybraného widgetu 508 × ≈ 228 (nadpis
  20 + 4, popis ≤ 2 řádky, 12, obsah), dole oddíl „Všechny widgety“ (Upravit rozložení…, Obnovit výchozí, Použít
  rozložení u všech tiskáren; neprůhlednost karet).
- **Oprava předpokladu v zadání:** na záložce Widgety je globální jen neprůhlednost karet. **Viditelnost a rozložení
  widgetů jsou u tiskárny** (`printers[*].camera.overlay.widgets`, výběr tiskárny nahoře záložky; T-092 Rozhodnutí
  2026-10-24). Otázka Q2 je proto formulovaná znovu.
- Pozor na úložiště: klíč `printers[*].camera.overlay.mini` už existuje jako **starý formát T-036**, který
  `SettingsMapper` jen čte kvůli převodu (jeho přítomnost spouští převod rozložení). Nové nastavení ho nesmí použít
  (oddíl 6).
- Název „Malý náhled“ už v aplikaci je (`SettingsCameraWindowMini`: cs „Malý náhled“, en „Mini preview“, de „Kleine
  Vorschau“, fr „Petit aperçu“ …) – znovu se použije jako název položky seznamu.

### 1. Položky pásu (doporučený rozsah, Q3)
| # | Položka | Kde v pásu | Vypnutá = | Výchozí |
|---|---|---|---|---|
| 1 | Pruh průběhu | horní hrana 3 DIP (tisk, chladnutí, vychladnutí) | slabý podklad jako bez úlohy (výška 3 zůstane) | zapnuto |
| 2 | Procento | souhrn „19 %“ | souhrn bez procenta | zapnuto |
| 3 | Zbývající čas | souhrn „2 h 35 min“ | souhrn bez času | zapnuto |
| 4 | Čas dokončení | doplněk „⚑ 19:50“ (T-103 3.1) | doplněk se nikdy neukáže; tooltip podle Q3 (c), oddíl 4 | zapnuto |
| 5 | Vychladnutí po tisku | ❄ / ⏻ + texty C1–C3 / K1–K2 (T-103 3.2) | „● Dokončeno“ / „● Připravena“ (název stavu), pruh podle položky 1 jako bez úlohy | zapnuto |
| 6 | Řádek názvu tisku | přepínač ⬡ + řádek 34 | přepínač zmizí, řádek nikdy (Q4) | zapnuto |
| 7 | Řádek grafu teplot | přepínač 📈 + řádek 64 | přepínač zmizí, řádek nikdy (Q4) | zapnuto |

- **Minimum (vždy):** tečka stavu + název stavu. Když zapnuté položky nemají co ukázat (např. tisk s vypnutými 2–4),
  souhrn je název stavu: „● Tiskne“. Pás tedy nikdy není prázdný a zůstává místem pro tažení okna a dvojklik.
- Stavy bez úlohy, offline, chyba, pozastaveno („19 % · Pozastaveno“ – s vypnutým procentem jen „Pozastaveno“) se
  řídí stejnými položkami; název stavu se nikdy neschová.
- Výchozí stav všech položek „zapnuto“ = **dnešní chování po T-103** (řádky jsou dostupné a sbalené jako dnes).

### 2. Nastavení: položka „Malý náhled“ na záložce Widgety (doporučeno, Q1)
Seznam widgetů dostane pod oddělovačem jednu položku navíc. Není to widget překryvu: **nemá zaškrtávací pole
viditelnosti** (pás se nevypíná, na jeho místě je prázdný prostor 20, aby ikony a názvy lícovaly).
```
┌──────────────────────────┐
│[x] ◉ Stav                │  32
│[x] ⏲ Časy                │
│[x] 🌡 Teploty             │
│[x] ⬡ Název tisku         │
│[x] ❄ Vychladnutí         │
│──────────────────────────│  9  (čára 1, okraje 4, `Separator`, není položkou výběru)
│    ▭ Malý náhled        ▐│  32 (ikona Fluent PictureInPicture 16, MIT)
└──────────────────────────┘     5 × 32 + 9 + 32 = 201 ≤ 228 (rezerva 27, další widget ještě vejde)
```
Panel vybrané položky „Malý náhled“ (cs, 508 × 228, výchozí stav):
```
▭ Malý náhled                                                                   20 SemiBold (WidgetsDetailTitle)
Informační pás pod obrazem malého náhledu kamery. Vyberte, co v něm bude.        hint 12, ≤ 2 řádky
                                                                                 12
[x] Pruh průběhu nahoře            [x] Vychladnutí po tisku                      32
[x] Procento                       [x] Řádek názvu tisku                         32
[x] Zbývající čas                  [x] Řádek grafu teplot                        32
[x] Čas dokončení                                                                32
                                                                                 4
Stav tiskárny je v pásu vidět vždy.                                              hint 12, 1 řádek
←──────── 242 ────────→ 24 ←──────── 242 ────────→
```
- Rozvržení: `Grid ColumnDefinitions="*,24,*"`, levý sloupec = základní řádek při tisku (1–4), pravý = po tisku a řádky
  pod ním (5–7). Bez nadpisů sloupců (nevešly by se do výšky, viz rozpočet); pořadí v seznamu = pořadí v pásu.
- `CheckBox` Fluent, `MinHeight` 32, text 14, smí se zalomit na 2 řádky (pak 40). Nejdelší odhad (Inter 14,
  ≈ 7 DIP/znak): fr „Ligne du graphique des températures“ ≈ 240 > 214 → 2 řádky, pravý sloupec 32 + 40 + 40 = 112 ≤ 128.
  Přesné šířky změří ui-dev testem (8 jazyků, bez oříznutí, extent ≤ viewport).
- **Výškový rozpočet:** 24 (nadpis) + 32 (popis 2 ř.) + 12 + 128 (4 × 32) + 4 + 16 (poznámka 1 ř.) = **216 ≤ 228**.
  Poznámka na 2 řádky by dala 232 > 228, proto je to jedna krátká věta (≤ 60 znaků ve všech jazycích, oddíl 7) –
  delší vysvětlení („řádky rozbalíte tlačítky v malém náhledu“) patří do tooltipů polí 6 a 7. UI test hlídá
  `ScrollViewer` extent ≤ viewport v 8 jazycích při 780 × 532.
- Vypnutá kamera tiskárny: zaškrtávací pole zakázaná, hodnoty zůstanou (stejně jako viditelnost widgetů, T-092 3.5).
  Žádná tiskárna: položka se nezobrazí (seznam a panel jsou skryté, T-092 3.6).
- Ovládání: Tab ze seznamu → 7 zaškrtávacích polí po sloupcích (levý shora dolů, pak pravý) → oddíl Všechny widgety.
  Mezerník na položce „Malý náhled“ v seznamu nic nedělá (nemá viditelnost). `AutomationProperties.Name` položky =
  „Malý náhled“ (bez „zobrazený / skrytý“).
- Změny se projeví po **Uložit** (jako všechna nastavení), otevřené malé okno se překreslí a dopočítá výšku (T-089).
- `AutomationId`: položka `WidgetsItem-miniStrip`, panel `WidgetsDetail` (stávající), pole `MiniStripProgressBar`,
  `MiniStripPercent`, `MiniStripRemaining`, `MiniStripFinish`, `MiniStripCooldown`, `MiniStripNameRow`,
  `MiniStripChartRow`, poznámka `MiniStripHint`.
- **Platformy:** Windows – vzor list/details, zaškrtávací pole Fluent 32, mřížka 4 / 8, text zaškrtávacích polí ve
  větném tvaru (Windows i macOS používají u voleb větný tvar). macOS – stejné vykreslení (Fluent téma, záložky jako zbytek
  nastavení, T-092 5.6); HIG „Toggles“: checkbox pro nezávislé volby v seznamu nastavení je správný prvek (ne přepínač).

### 3. Pás podle voleb (drátové modely, doporučené varianty)
Pravidla T-103 3.1 / 3.2 zůstávají (první varianta, která se vejde celá, pořadí důležitosti procento > zbývající čas >
čas dokončení > slovo „Hotovo v“), jen **vypnutá položka v žádné variantě není** (Q6 → pevná pravidla). Šířka souhrnu
roste o skryté přepínače (Q4): oba řádky dostupné `W = šířka − 100` (dnes), jeden `W = šířka − 68` (přepínač 30),
žádný `W = šířka − 34` (sloupce 8 + přepínače + 6 nahradí okraj 10).

| Okno | W, oba řádky | W, jeden řádek | W, žádný řádek |
|---|---|---|---|
| 240 | 140 | 172 | 206 |
| 320 | 220 | 252 | 286 |
| 400 | 300 | 332 | 366 |

Výchozí (vše zapnuto) – beze změny proti T-103:
```
240 │ ● 19 % · 2 h 35 min [⬡][📈] │                          L3
320 │ ● 19 % · 2 h 35 min  ⚑ 19:50       [⬡][📈] │           L2
400 │ ● 19 % · 2 h 35 min  ⚑ Hotovo v 19:50          [⬡][📈] │ L1
```
Zbývající čas vypnutý (uživatel chce čas dokončení i při 240):
```
240 │ ● 19 %  ⚑ 19:50     [⬡][📈] │        30 + 10 + 18 + 39 = 97 ≤ 140 → L2
320 │ ● 19 %  ⚑ Hotovo v 19:50           [⬡][📈] │   156 ≤ 220 → L1
```
Procento vypnuté, pruh zapnutý (průběh jen graficky):
```
├▓▓▓▓▓▓░░░░░░░░░░░░░░░░░┤
240 │ ● 2 h 35 min  ⚑ 19:50 [⬡][📈] │       ≈ 70 + 10 + 18 + 39 = 137 ≤ 140 → L2
```
Řádky vypnuté (bez přepínačů), 240 (W = 206):
```
┌────────────────────────┐
│         obraz          │
├▓▓▓▓▓░░░░░░░░░░░░░░░░░░┤ 3
│ ● 19 % · 2 h 35 min  ⚑ 19:50 │ 30   109 + 28 + 39 = 176 ≤ 206 → L2
└────────────────────────┘
```
Jen pruh průběhu (2–5 vypnuté) – minimum:
```
├▓▓▓▓▓▓░░░░░░░░░░░░░░░░░┤
│ ● Tiskne                      [⬡][📈] │   název stavu (tooltip: celé znění)
```
Vychladnutí vypnuté, po tisku:
```
├░░░░░░░░░░░░░░░░░░░░░░░┤ 3  slabý podklad (žádný průběh chladnutí)
│ ● Dokončeno                   [⬡][📈] │   jako dnes bez sledování chladnutí
```
Pruh průběhu vypnutý: výška pásu zůstává 33, horní 3 DIP jsou vždy slabý podklad (oddělí obraz od pásu, okno
neposkočí; alternativa v Q3).

### 4. Tooltip a přístupnost
- Tooltip a `AutomationProperties.Name` souhrnu: **celé znění podle zapnutých položek** (vypnutá položka není ani
  v tooltipu – uživatel ji nechtěl vidět; doporučení, viz Q3 bod c). Název stavu je v něm vždy („Tiskne, 19 %“).
- Skrytý přepínač není ve stromu automatizace ani v pořadí Tab (`IsVisible=False`). S oběma skrytými nemá pás žádnou
  zastávku Tab (okno je ovladatelné dál zkratkami a kontextovou nabídkou kamery).
- Informace nikdy jen barvou: beze změny proti T-103.

### 5. Chování a souvislosti
- **Vypnutý řádek a pamatované rozbalení:** pamatovaný stav (`miniNameRow` / `miniChartRow` v `camera-windows.json`)
  se při vypnutí v nastavení **nemaže**; po opětovném zapnutí se řádek vrátí, jak byl. Výška okna se počítá jen
  z dostupných a rozbalených řádků (`StripHeight`).
- Vypnutý graf: historie teplot se neodebírá (jako dnes při sbaleném grafu).
- **Obnovit výchozí / Použít rozložení u všech tiskáren** (oddíl Všechny widgety, Q10): doporučeno zahrnout i položky
  pásu (jsou pod výběrem tiskárny jako rozložení widgetů); texty tlačítek se nemění, tooltip `SettingsWidgetsResetHelp`
  dostane zmínku o pásu.
- Malé okno při změně: po Uložit se varianty přepočítají, přepínače zmizí / objeví se, okno dopočítá výšku a zůstane
  na pracovní ploše (pravidla T-089).

### 6. Datový model (návrh pro core-dev, doporučené varianty Q2 a Q9)
- **Core:** `CameraMiniStripSettings` (`sealed record`, `LayerPeek.Core.Cameras`) v `CameraSettings.MiniStrip`:
  `ProgressBar`, `Percent`, `Remaining`, `Finish`, `Cooldown`, `NameRow`, `ChartRow` – všechny `bool`, výchozí `true`;
  `CameraMiniStripSettings.Default` = dnešní chování.
- **JSON (`settings.json`):** u tiskárny `printers[*].camera.miniStrip`, **ne** `camera.overlay.mini` (starý klíč
  T-036, oddíl 0). Zapisují se jen hodnoty `false`; chybějící objekt nebo klíč = `true`. Neplatná hodnota (řetězec,
  číslo, `null`) = `true` + varování mapperu, zbytek souboru platí (vzor `CameraOverlayDocument`). Verze souboru
  (`CurrentVersion = 2`) se nemění, migrace není potřeba.
  ```json
  "camera": { "enabled": true, "overlay": { "widgets": [ … ] },
              "miniStrip": { "remaining": false, "chartRow": false } }
  ```
- **App:** `PrinterEditorViewModel.MiniStrip` (rozpracovaná kopie), `SettingsViewModel` položka seznamu (nový druh
  položky vedle `WidgetItemViewModel`, ne `IOverlayWidgetDefinition` – není to widget), panel jako `DataTemplate`
  v `WidgetSettingsTemplates.axaml` (kompilované bindingy). `CameraMiniStripViewModel` dostane nastavení při vytvoření
  a při uložení nastavení; `MiniStripContent` / `Texts` vynechají vypnuté položky (porovnání a nulová alokace při pollu
  zůstávají).
- `camera-windows.json` se nemění; `Remove` nastavení pásu nezapomíná (je to nastavení, ne pamatovaný stav, Q9).

### 7. Nové texty (`Strings.*`, návrh klíčů; překlady dodá ui-dev ve všech 8 jazycích)
| Klíč | cs | en |
|---|---|---|
| `SettingsMiniStripDescription` | Informační pás pod obrazem malého náhledu kamery. Vyberte, co v něm bude. | The info strip under the mini preview image. Choose what it shows. |
| `SettingsMiniStripProgressBar` | Pruh průběhu nahoře | Progress bar at the top |
| `SettingsMiniStripPercent` | Procento | Percentage |
| `SettingsMiniStripRemaining` | Zbývající čas | Time remaining |
| `SettingsMiniStripFinish` | Čas dokončení | Finish time |
| `SettingsMiniStripCooldown` | Vychladnutí po tisku | Cool-down after printing |
| `SettingsMiniStripNameRow` | Řádek názvu tisku | Print name row |
| `SettingsMiniStripChartRow` | Řádek grafu teplot | Temperature chart row |
| `SettingsMiniStripHint` | Stav tiskárny je v pásu vidět vždy. | The printer state is always shown in the strip. |

Návrh překladů (ui-dev je zkontroluje a změří):

| Klíč | sk | de | es |
|---|---|---|---|
| `…Description` | Informačný pás pod obrazom malého náhľadu kamery. Vyberte, čo v ňom bude. | Die Infoleiste unter dem Bild der kleinen Vorschau. Wählen Sie, was sie zeigt. | La banda de información bajo la imagen de la vista reducida. Elija qué muestra. |
| `…ProgressBar` | Pruh priebehu hore | Fortschrittsbalken oben | Barra de progreso arriba |
| `…Percent` | Percento | Prozent | Porcentaje |
| `…Remaining` | Zostávajúci čas | Restzeit | Tiempo restante |
| `…Finish` | Čas dokončenia | Fertigstellungszeit | Hora de finalización |
| `…Cooldown` | Vychladnutie po tlači | Abkühlung nach dem Druck | Enfriamiento tras imprimir |
| `…NameRow` | Riadok názvu tlače | Zeile mit Druckname | Fila del nombre de impresión |
| `…ChartRow` | Riadok grafu teplôt | Zeile mit Temperaturdiagramm | Fila del gráfico de temperaturas |
| `…Hint` | Stav tlačiarne je v páse viditeľný vždy. | Der Druckerstatus wird immer angezeigt. | El estado de la impresora se muestra siempre. |

| Klíč | fr | it | pl |
|---|---|---|---|
| `…Description` | Le bandeau d'informations sous l'image du petit aperçu. Choisissez ce qu'il affiche. | La barra informativa sotto l'immagine dell'anteprima ridotta. Scegli cosa mostra. | Pasek informacji pod obrazem małego podglądu. Wybierz, co ma pokazywać. |
| `…ProgressBar` | Barre de progression en haut | Barra di avanzamento in alto | Pasek postępu u góry |
| `…Percent` | Pourcentage | Percentuale | Procent |
| `…Remaining` | Temps restant | Tempo rimanente | Pozostały czas |
| `…Finish` | Heure de fin | Ora di fine | Godzina zakończenia |
| `…Cooldown` | Refroidissement après l'impression | Raffreddamento dopo la stampa | Stygnięcie po wydruku |
| `…NameRow` | Ligne du nom de l'impression | Riga del nome di stampa | Wiersz nazwy wydruku |
| `…ChartRow` | Ligne du graphique des températures | Riga del grafico delle temperature | Wiersz wykresu temperatur |
| `…Hint` | L'état de l'imprimante est toujours affiché. | Lo stato della stampante è sempre visibile. | Stan drukarki jest zawsze widoczny. |

- Terminologie podle existujících textů daného jazyka (např. de „Leiste“, fr „bandeau“ z `CameraMiniToggleName`,
  „Abkühlung“ / „Refroidissement“ z názvu widgetu); vykání / formální tvar jako ve zbytku aplikace (it „Scegli“
  odpovídá stávajícímu tykání v it textech – ui-dev ověří podle zbytku `Strings.it.resx`).
- Název položky seznamu: stávající `SettingsCameraWindowMini`. Poznámka 7 je zkrácená na 1 větu kvůli výšce (oddíl 2).
- Rizika: fr / es / it jsou u „Ligne du graphique des températures“, „Fila del gráfico de temperaturas“ nejdelší
  (2 řádky v pravém sloupci, vejde se); de „Temperaturdiagramm-Zeile“ je 1 řádek. Popis v de / fr se zalomí na 2 řádky
  (rozpočet s tím počítá).

### 8. Odhad implementace (po odpovědích)
| Úkol | Obsah | Odhad |
|---|---|---|
| T-104a Core (core-dev) | `CameraMiniStripSettings`, mapper čtení / zápis, varování, testy (starý soubor, neplatné hodnoty, jen `false` se zapisuje) | S (≈ 0,5 dne) |
| T-104b Nastavení (ui-dev) | položka „Malý náhled“ + oddělovač v seznamu, panel 7 polí, rozpracovaná kopie, Obnovit / Použít u všech (Q10), 9 textů × 8 jazyků, headless testy (8 jazyků, extent ≤ viewport, klávesnice, kamera vypnutá), snímky `settings-widgets-mini-strip`, `-de`, `-fr` | M (≈ 1–1,5 dne) |
| T-104c Pás (ui-dev) | varianty bez vypnutých položek, skryté přepínače a šířka souhrnu, horní pruh, tooltip, `StripHeight`, aplikace po uložení, testy (kombinace × 240 / 320 / 400 × 8 jazyků, alokace 0 B), snímky `camera-mini-strip-custom-240`, `-no-rows-320` | M (≈ 1 den) |

Celkem ≈ 3 dny agentní práce, bez nového balíčku, bez reflexe. T-104b a T-104c lze spojit do jednoho úkolu (M–L).
Varianty „nastavitelné pořadí“ (Q5 B), „volba priority“ (Q6 B), „formát času“ (Q7 B) nebo „živý náhled“ (Q8 B) by každá
přidala S–M a u Q8 B i zvětšení okna nastavení.

### 9. Odkazy na zásady platforem
- Windows: „List/details pattern“, Fluent `CheckBox` (nezávislé volby, větný tvar), mřížka 4 / 8, cíle ≥ 32;
  Windows accessibility – keyboard navigation, „Don't rely on color alone“; WCAG 2.2 AA 1.4.3.
- macOS HIG: „Settings“ (nastavení v okně nastavení, ne v pomocném okně), „Toggles“ (checkbox pro volby v seznamu),
  „Panels“ (malé plovoucí okno s minimem ovládacích prvků – důvod nedávat nastavení do kontextové nabídky pásu).
- Platformní rozdíly: žádné v rozvržení; jen písmo (Segoe UI Variable / SF Pro proti Inter v testech) posune hranice
  variant o pár DIP (T-103 oddíl 0).

## Otevřené otázky
**Rozhodnuto 2026-10-30 (uživatel): Q1 A, Q2 A, Q3 A (včetně dílčích (a) slabý podklad a (c) ne v tooltipu), Q4 A, Q5 A, Q6 A, Q7 A, Q8 A, Q9 A, Q10 A** – vše podle doporučení.

Návrh připraven 2026-10-29 (ui-designer). U každé otázky je doporučená varianta označená „(doporučeno)“; rozhodnutí
viz výše a oddíl `## Rozhodnutí`. Odpověď stačí jako „Q1 A, Q2 A, …“.
- **Q1 – Kde se pás nastavuje?**
  - **A (doporučeno):** záložka Widgety, položka „Malý náhled“ pod oddělovačem na konci seznamu, vlastní panel
    (oddíl 2). Důvod: uživatel hledá „co se ukazuje v okně kamery“ na jednom místě, panel 508 × 228 se vejde bez
    zvětšení okna, stejný vzor list/details a výběr tiskárny.
  - B: Nastavení → Tiskárny → podzáložka Kamera u velikosti malého náhledu. Proti: podzáložka má jen 548 × ≈ 358
    a je už plná, druhé místo pro „co ukazuje okno kamery“.
  - C: jen v malém okně (kontextová nabídka pásu / tlačítko ⋯). Proti: třetí tlačítko v pásu ubere 32 DIP při 240,
    kontextová nabídka není objevitelná; lze přidat později jako zkratku k variantě A.
- **Q2 – Globálně, nebo u každé tiskárny?** (Oprava: viditelnost widgetů na záložce Widgety je u tiskárny, globální je
  jen neprůhlednost karet.)
  - **A (doporučeno):** u tiskárny (`printers[*].camera.miniStrip`), stejně jako rozložení widgetů pod výběrem
    tiskárny; pro sjednocení slouží „Použít rozložení u všech tiskáren“ (Q10).
  - B: globálně (jedno nastavení pro všechny malé náhledy). Jednodušší pro více tiskáren, ale panel pod výběrem
    tiskárny by musel hlásit „platí pro všechny tiskárny“, nebo by patřil do oddílu Všechny widgety (tam se nevejde).
- **Q3 – Které položky jdou vypnout?**
  - **A (doporučeno):** 7 nezávislých položek (oddíl 1): pruh průběhu, procento, zbývající čas, čas dokončení,
    vychladnutí po tisku, řádek názvu, řádek grafu. Minimum je vždy tečka + název stavu.
  - B: 4 hrubší skupiny (průběh = pruh + procento, časy = zbývající + dokončení, vychladnutí, řádky). Méně voleb,
    ale nejde „čas dokončení místo zbývajícího“ při 240.
  - C: smí se vypnout úplně vše včetně stavu (pás jen s přepínači). Nedoporučeno: prázdný pás bez informace.
  - Dílčí: (a) vypnutý pruh = slabý podklad 3 DIP, výška beze změny **(doporučeno)** / pás o 3 nižší;
    (c) vypnutá položka **není ani v tooltipu (doporučeno)** / tooltip má vždy celé znění jako dnes.
- **Q4 – Vztah k přepínačům ⬡ / 📈:**
  - **A (doporučeno):** nastavení určuje dostupnost: vypnutý řádek = přepínač zmizí a řádek se nikdy neukáže; zapnutý
    = přepínač zůstává a rozbalení se pamatuje u tiskárny jako dnes. Skryté přepínače uvolní 32 / 62 DIP pro souhrn.
  - B: nastavení určuje jen výchozí rozbalení, přepínače zůstávají vždy.
  - C: zapnutý řádek je trvale rozbalený, přepínače zaniknou.
- **Q5 – Pořadí položek:**
  - **A (doporučeno):** pevné podle T-103 (souhrn, čas dokončení, řádek názvu, řádek grafu).
  - B: nastavitelné (šipky nahoru / dolů v panelu; přetahování by potřebovalo klávesnicovou alternativu). Komplikuje
    pravidla variant a testy, malý užitek u jednoho řádku.
- **Q6 – Priorita při úzkém okně:**
  - **A (doporučeno):** pevná pravidla T-103 (procento > zbývající čas > čas dokončení > slovo); kdo chce čas
    dokončení při 240, vypne zbývající čas (oddíl 3).
  - B: volba „Při nedostatku místa ukázat: zbývající čas / čas dokončení“ (T-103 Q4 C). Další pole v panelu, výška
    se nevejde bez zmenšení popisu.
- **Q7 – Formát času dokončení:**
  - **A (doporučeno):** jen automatika T-103 (nejdelší varianta, která se vejde: „Hotovo v 19:50“ → „19:50“), bez volby.
  - B: volba „Automaticky / Jen čas“ (bez slova „Hotovo v“ ani při široké šířce).
- **Q8 – Náhled v nastavení:**
  - **A (doporučeno):** bez náhledu, jen seznam voleb a jednovětá poznámka; výsledek je vidět po Uložit v otevřeném
    malém okně.
  - B: živý náhled základního řádku (320 × 33 s ukázkovými daty) v panelu. Nevejde se do 228 (rozpočet 216 + 41),
    vyžadoval by zvětšit okno nastavení (Nastavení a Diagnostika musí mít stejnou velikost, T-092).
- **Q9 – Uložení a migrace:**
  - **A (doporučeno):** `settings.json` u tiskárny, zapisují se jen vypnuté položky, chybějící = zapnuto (= dnešní
    chování), bez změny verze; **nezapomíná se** se stavem okna (`Remove`), mění se jen v nastavení.
  - B: `camera-windows.json` vedle stavu rozbalení, zapomíná se s ním (T-090 Q3). Proti: změna výchozího okna
    v nastavení by tiše smazala volby, které uživatel nastavil v témže okně.
- **Q10 (nová) – Obnovit výchozí a Použít rozložení u všech tiskáren:**
  - **A (doporučeno):** obě tlačítka oddílu Všechny widgety zahrnou i položky pásu vybrané tiskárny (texty tlačítek
    beze změny, tooltip doplněný).
  - B: tlačítka pás nemění; panel „Malý náhled“ dostane vlastní „Použít u všech tiskáren“ (jako práh vychladnutí) –
    jeden řádek navíc, do výšky 228 se nevejde bez zkrácení popisu.

## Poznámky k implementaci
(architect, 2026-10-30)
- Návrhový úkol uzavřen: návrh UI hotový, rozhodnutí uživatele zapsaná v `## Rozhodnutí`.
- Implementace: **T-110** `tasks/todo/T-110-mini-strip-configurable-impl.md` (ui-dev, `all`, závisí na T-104, T-103,
  T-109). Odhad T-104a/b/c z oddílu 8 je sloučený do jednoho úkolu (Core část je malá a bez UI nemá samostatnou
  hodnotu; nastavení i pás sdílejí model, texty a snímky).
- Nevyřešené drobnosti znění (tooltipy polí 6 a 7, nové znění `SettingsWidgetsResetHelp`) jsou v Otevřených otázkách
  T-110; implementaci neblokují.
