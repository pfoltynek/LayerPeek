---
id: T-092
title: Záložka Widgety v nastavení – seznam widgetů s vlastním nastavením, globální neprůhlednost, úprava rozložení
status: todo
owner: ui-dev
priority: P2
platform: all
depends_on: [T-077, T-080, T-081, T-088, T-091]
---

## Cíl
Nastavení widgetů překryvu okna kamery dostane vlastní oblast **Widgety** (master-detail):
- vlevo **seznam widgetů** (Stav, Časy, Teploty, Název tisku, Vychladnutí, později další), u každého zaškrtávací pole viditelnosti,
- vpravo **vlastní nastavení vybraného widgetu** (každý widget ho může, ale nemusí deklarovat; např. Vychladnutí: práh trysky 30–100 °C, „Použít práh u všech tiskáren“, stav upozornění „Tiskárna vychladla“),
- dole oddíl **Všechny widgety**: globální „Neprůhlednost pozadí karet“ (30–95 %), „Upravit rozložení v okně kamery…“ (otevře režim rozložení T-091), „Obnovit výchozí“, „Použít rozložení u všech tiskáren“.

Podzáložka **Překryv** v nastavení tiskárny (T-036) zanikne. Okno nastavení navíc převezme rozložení uložené z režimu rozložení v okně kamery, aniž by ztratilo jiné neuložené změny.

## Rozhodnutí (uživatel, 2026-10-25)
Nahrazují koncept podzáložky Překryv z T-076 bodu 8 (schválený T-076 zůstává beze změny, toto je jeho dodatek).
- Místo podzáložky Překryv vznikne oblast **Widgety** s rozvržením master-detail (seznam widgetů s viditelností vlevo, vlastní nastavení vybraného widgetu vpravo, oddíl „Všechny widgety“ dole).
- Každý widget **může deklarovat vlastní panel nastavení**. Widget bez něj ukáže jen popis.
- Kotva, pořadí, velikost a zvýraznění se v nastavení **neupravují** (jen v režimu rozložení T-091).
- Oddíl „Všechny widgety“ obsahuje tlačítko **„Upravit rozložení v okně kamery…“**, které otevře režim rozložení. Tím se ruší rozhodnutí z 2026-10-24 „Tlačítko v nastavení není“.
- Okno nastavení smí trochu narůst, ale okna **Nastavení a Diagnostika musí mít dál stejnou velikost**.
- Bez posuvníku ve všech 8 jazycích, ovládání klávesnicí, `AutomationId`, vykání.

## Rozhodnutí (uživatel, 2026-10-24), která platí dál
- **Viditelnost** platí pro normální okno i celou obrazovku (sdílené rozložení). Malý náhled karty nemá (T-089 / T-090), sloupec pro něj **není**.
- **„Použít rozložení u všech tiskáren“** zkopíruje celé rozložení tiskárny (viditelnost, kotvy, pořadí, velikosti, zvýraznění) do ostatních tiskáren v rozpracovaném nastavení. Uloží se s ostatními změnami.
- **Neprůhlednost pozadí karet** je jedno **globální** nastavení, 30–95 %, výchozí 72 %. Popisek „Neprůhlednost pozadí karet“ / „Card background opacity“ (T-076 Q2).
- **Souběh (T-077 Q3):** když se rozložení uloží z okna kamery a okno nastavení je otevřené, okno nastavení **znovu načte jen rozložení** dané tiskárny a ostatní neuložené změny si ponechá.
- Práh vychladnutí je **u tiskárny**, 30–100 °C, výchozí 50 °C (T-079), s akcí „Použít práh u všech tiskáren“.

## Kontext
- Dnes: `SettingsViewModel.OverlayPrinterTab`, `PrinterEditorViewModel.OverlayRows` (`CameraOverlayItemRow`), `SelectedOverlayCorner`, podzáložka `PrinterTabOverlay` v `SettingsWindow.axaml`, testy `SettingsWindowOverlayUiTests`.
- Velikost oken: `AuxiliaryWindowPlacement.DefaultClientSize` / `MinClientSize`, `Width` / `Height` v `SettingsWindow.axaml` a `DiagnosticsWindow.axaml`, testy `SettingsWindowLayoutTests`, `DiagnosticsWindowLayoutTests`, `AuxiliaryWindowPlacementTests`.
- Rozložení: `CameraOverlayLayout` a čisté operace (`WithVisible`, `ResetToDefault`, `ApplyToAll`), globální `AppSettings.CameraOverlayOpacityPercent`, zapisovač `ICameraOverlayLayoutWriter` s událostí `LayoutSaved` (T-077, hotovo).
- Práh: `printers[*].cooldownThresholdCelsius`, `CooldownThreshold.ApplyToAll` (T-079, hotovo). Upozornění `NotificationSettings.CooledDown` (T-080).
- Registr widgetů `IOverlayWidgetDefinition` / `OverlayWidgetRegistry` (T-081). Smlouva pro panel nastavení je v oddílu Návrh UI 6.
- Vstup do režimu rozložení z okna kamery (T-091). Tento úkol potřebuje navíc vstup „zvenku“ pro konkrétní tiskárnu (Návrh UI 5.4).
- Vzhled, ikony, texty a vykání: schválený návrh `tasks/done/T-076-widget-design-spec.md` (oddíly 1.3, 6, 8, 10). Tento soubor ho doplňuje, ne mění.

## Rozsah
- **Záložka Widgety** podle Návrhu UI (umístění podle rozhodnutí otázky Q1, návrh počítá s variantou B: hlavní záložka s výběrem tiskárny).
- **Seznam widgetů:** jeden řádek na registrovaný widget v pořadí registrace (Stav, Časy, Teploty, Název tisku, Vychladnutí), ikona, název, zaškrtávací pole viditelnosti, značka „!“ při chybě v panelu widgetu. Hodnoty se mapují na `CameraOverlayLayout` přes `WithVisible`. Neznámé id widgetů z rozložení se v seznamu neukazují a zůstanou zachované.
- **Panel vybraného widgetu:** nadpis s ikonou, popis, vlastní panel nastavení nebo text „nemá další nastavení“ (Návrh UI 4.2, 6).
- **Panel Vychladnutí:** číselné pole prahu (°C, 30–100, validace a chyba jako u ostatních číselných polí), „Použít práh u všech tiskáren“, řádek se stavem upozornění „Tiskárna vychladla“ a odkazem na záložku Obecné (Návrh UI 4.3, podle rozhodnutí Q3 a Q6).
- **Oddíl Všechny widgety:**
  - „Upravit rozložení v okně kamery…“ (Návrh UI 5.4, podle rozhodnutí Q4 a Q5),
  - „Obnovit výchozí“ (rozložení vybrané tiskárny),
  - „Použít rozložení u všech tiskáren“ (při jediné tiskárně zakázané s tooltipem),
  - posuvník neprůhlednosti 30–95 % s hodnotou, uložení do `AppSettings`, po uložení se změna projeví ve všech otevřených oknech kamery.
- **Opětovné načtení rozložení:**
  - Při `LayoutSaved` nahradí `PrinterEditorViewModel` dané tiskárny jen své rozložení. Ostatní pole včetně neuložených změn zůstanou a stav „neuloženo“ se kvůli tomu nezmění.
  - Událost může přijít z jiného vlákna, zpracuje se na UI vlákně.
  - Odebraná nebo nová (neuložená) tiskárna událost ignoruje.
- **Vypnutá kamera:** zaškrtávací pole a „Upravit rozložení…“ zakázané, hodnoty zůstanou. Výběr widgetu, panel Vychladnutí a neprůhlednost zůstanou dostupné.
- **Společná velikost oken** Nastavení a Diagnostika podle rozhodnutí Q2 (návrh 780 × 532): XAML obou oken, `AuxiliaryWindowPlacement`, komentáře a testy velikosti. Snímky reference obou oken přegenerovat a zkontrolovat.
- **Smlouva panelu nastavení** v registru widgetů (Návrh UI 6), bez reflexe (Native AOT).
- Odstranit podzáložku Překryv, `OverlayRows`, `SelectedOverlayCorner`, staré texty a `AutomationId` `PrinterOverlay*`, `PrinterTabOverlay`. Testy T-036 převést.
- Nové texty ve všech 8 jazycích, přepnutí jazyka za běhu.

## Akceptační kritéria
- [ ] App.Tests:
  - načtení rozložení do seznamu, změna viditelnosti, uložení, přenos do `PrinterDefinition`,
  - „Obnovit výchozí“ (jen rozložení vybrané tiskárny, práh se nemění) a „Použít rozložení u všech tiskáren“ (kopíruje celé rozložení, jiné hodnoty tiskáren nemění),
  - práh: hranice 30 a 100, hodnoty mimo rozsah blokují uložení, „Použít práh u všech tiskáren“, značka chyby u widgetu, tiskárny a záložky,
  - neprůhlednost: hranice 30 a 95, výchozí 72, uložení,
  - `LayoutSaved` nahradí jen rozložení a neuložené změny jiných polí zůstanou; následné uložení zapíše nové rozložení; nová i odebraná tiskárna událost ignoruje,
  - „Upravit rozložení v okně kamery…“: dostupnost (kamera vypnutá, neuložená tiskárna, žádná tiskárna) a předání tiskárny (a podle Q4 rozpracovaného rozložení) do vstupu T-091,
  - registr: každý widget má klíč názvu a popisu ve všech 8 `Strings.*.resx`; panel se vytvoří jen pro widget, který ho deklaruje, a při změně výběru se uvolní.
- [ ] Headless UI testy (`LayerPeek.App.UiTests`):
  - výchozí hodnoty, změna viditelnosti myší i klávesnicí, uložení kliknutím, vypnutá kamera, žádná tiskárna,
  - výběr widgetu přepne panel; widget bez vlastního nastavení; Vychladnutí s chybou prahu,
  - „Použít … u všech tiskáren“ s 1 a 3 tiskárnami,
  - odkaz na upozornění přepne na záložku Obecné a dá fokus přepínači „Tiskárna vychladla“,
  - pořadí Tab, `AutomationId` podle Návrhu UI 7,
  - **bez posuvníku ve všech 8 jazycích** × stavy (výchozí, Vychladnutí vybrané, chyba prahu, vypnutá kamera, jediná tiskárna, žádná tiskárna),
  - nová velikost obou oken (výchozí, minimum, přizpůsobení pracovní ploše) a všechny dosavadní testy rozvržení obou oken v nové velikosti,
  - snímky reference (nové i přegenerované, vizuálně zkontrolované).
- [ ] Žádné reflexní bindingy, žádné trim/AOT varování
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Návrh UI

Návrh ui-designer, 2026-10-25. Dodatek ke schválenému návrhu T-076 (oddíl 8 se tímto nahrazuje, ostatní oddíly platí). Rozměry v DIP. Čeština vyká.

### 0. Výchozí stav (změřeno 2026-10-25)
- Okna Nastavení a Diagnostika: klient **780 × 508**, minimum 640 × 400 (`AuxiliaryWindowPlacement.DefaultClientSize` / `MinClientSize`, `Width` / `Height` v obou XAML). Cílem T-033 je obrazovka **800 × 600 při 100 %** (okno s titulkem ≤ 780 × 540). Na menší pracovní ploše se okno zmenší (`WorkAreaFitter`) a obsah pak posouvá.
- Okno nastavení (snímky `settings-overlay.png`, `settings-general-errors.png`): okraje 16 / 8 / 16 / 12, hlavní záložky 36 (písmo 18), řádek Uložit / Zrušit 32 + 12.
  - Obsah **hlavní záložky** (např. Obecné): **748 × 400**.
  - Obsah **podzáložky tiskárny**: **548 × ≈ 358** (seznam tiskáren 184 + 16, podzáložky 32 + 8).
- Diagnostika: graf vyplňuje zbývající výšku (min. 120), takže vyšší okno jen zvětší graf.
- Hlavní záložky dnes: Tiskárny / Obecné / Spuštění a aktualizace. Nejdelší jazyk fr („Imprimantes“, „Général“, „Démarrage et mises à jour“) zabere ≈ 470 z 748. S novou záložkou „Widgets“ ≈ 560 ≤ 748.

### 1. Umístění: hlavní záložka Widgety (doporučeno, otázka Q1)
Porovnané varianty:

| | **B – hlavní záložka „Widgety“ s výběrem tiskárny** (doporučeno) | A – podzáložka „Widgety“ u tiskárny (místo Překryvu) |
|---|---|---|
| Šířka obsahu | 748 (seznam 216 + panel 508) | 548 (seznam ≈ 200 + panel ≈ 324) |
| Výška pro seznam + panel | ≈ 228 při 780 × 532 | ≈ 170 (nevejde se ani v 780 × 532, potřebuje ≈ + 60) |
| Výběr tiskárny | rozbalovací seznam nahoře, sdílený výběr s záložkou Tiskárny | stávající seznam tiskáren |
| Globální neprůhlednost | přirozeně v hlavní záložce | uvnitř nastavení jedné tiskárny (matoucí) |
| Vnoření | záložka → seznam → panel | záložka → seznam tiskáren → podzáložka → seznam → panel (3 úrovně master-detail) |

Doporučení **B**. Důvody:
- Varianta A se do okna nevejde bez výrazného zvětšení (≈ 780 × 570, víc než malé notebooky při 125 %, viz 2) a vnořuje tři úrovně výběru vedle sebe.
- Widgety jsou samostatná oblast (rozložení, globální neprůhlednost, nastavení widgetů), ne vlastnost připojení tiskárny.
- Výběr tiskárny je **konzistentní** se stávajícím UI: rozbalovací seznam používá stejnou šablonu položky jako seznam tiskáren (obrázek 24, značka „!“, název) a **sdílí výběr** (`SettingsViewModel.SelectedPrinter`). Přepnutí mezi záložkami Tiskárny a Widgety tak zachová vybranou tiskárnu.
- Pořadí hlavních záložek: **Tiskárny | Widgety | Obecné | Spuštění a aktualizace** (Widgety hned za Tiskárnami, protože jsou u tiskárny).

### 2. Společná velikost oken (otázka Q2)
Návrh: klient **780 × 532** pro Nastavení i Diagnostiku (o 24 vyšší), minimum beze změny **640 × 400**.
- Okno s titulkem ≈ 780 × 564. Šířka se nemění (na 800 × 600 se vejde dál).
- **Nová cílová obrazovka** místo 800 × 600 při 100 %: **1366 × 768 při 125 %** (pracovní plocha ≈ 1093 × 566 DIP s hlavním panelem 48 → klient ≤ 534). Výchozí velikost se tam vejde celá, bez zmenšení.
- Kontrola dalších obrazovek (hlavní panel Windows 11 48 DIP, titulek 32):

  | Obrazovka | Pracovní plocha (DIP) | Max. klient | 780 × 532 |
  |---|---|---|---|
  | 800 × 600, 100 % | 800 × 552 | 780 × 520 | zmenší se o 12; obsah v1 (5 widgetů) se vejde bez posuvníku (viz 3.3) |
  | 1024 × 768, 100 % | 1024 × 720 | 688 | ✓ |
  | 1280 × 720, 100 % / 2560 × 1440, 200 % | 1280 × 672 | 640 | ✓ |
  | 1366 × 768, 100 % | 1366 × 720 | 688 | ✓ |
  | 1366 × 768, 125 % | 1093 × 566 | 534 | ✓ (rezerva 2) |
  | 1920 × 1080, 150 % | 1280 × 672 | 640 | ✓ |
  | 1920 × 1080, 175 % | 1097 × 569 | 537 | ✓ (rezerva 5) |
  | 2560 × 1600, 200 % / MacBook 1280 × 800 „vypadá jako“ | 1280 × ≈ 750 | ≈ 720 | ✓ |

- Diagnostika: o 24 vyšší graf, nic dalšího se nemění. Snímky reference přegenerovat.
- Ostatní záložky nastavení dostanou 24 DIP rezervy dole (žádná změna rozvržení).
- Alternativy v otázce Q2: ponechat 780 × 508 (těsné, viz 3.3) nebo 840 × 560 (pohodlné, ale nevejde se na 1366 × 768 při 125 % ani 1920 × 1080 při 175 %).

### 3. Záložka Widgety – rozvržení

#### 3.1 Mřížka (obsah 748 × 424 při klientu 780 × 532)
```
Řádek 0  výběr tiskárny ..................................................... 32
         mezera ............................................................. 12
Řádek 1  seznam widgetů 216 │ 24 │ panel vybraného widgetu 508 ............... *  (≈ 228)
Řádek 2  mezera 16, nadpis „Všechny widgety“ 20 + 4, tři tlačítka │ neprůhlednost  152
```
- Řádek 1 má výšku `*`: oddíl Všechny widgety je **vždy dole** a při přepínání widgetů neposkakuje.
- Řádek 0: `Grid ColumnDefinitions="Auto,12,320,24,*"`: popisek „Tiskárna“, rozbalovací seznam 320, vpravo nápověda o vypnuté kameře (12, nejvýš 2 řádky).
- Řádek 2: `Grid ColumnDefinitions="300,32,*"`: vlevo tlačítka pod sebou (mezera 8, šířka podle textu, nejvýš 300, `HorizontalAlignment="Left"`), vpravo neprůhlednost (416).
- Mezi řádky 1 a 2 nadpis `TextBlock.section` (horní okraj 16 ze stylu). Bez čáry, stejně jako ostatní oddíly nastavení.

#### 3.2 Výchozí stav (cs, vybraný Stav, kamera zapnutá, 3 tiskárny)
```
┌ Nastavení ─────────────────────────────────────────────────────────────────────────────────┐
│  Tiskárny   [Widgety]   Obecné   Spuštění a aktualizace                                    │ 36
│                                                                                            │
│  Tiskárna  [▣ MK4 dílna                    ▾]                                              │ 32
│                                                                                            │
│  ┌──────────────────────────┐   ◉ Stav                                                     │ 14 SemiBold
│  │[x] ◉ Stav               ▐│   Název tiskárny, stav a průběh tisku v procentech.          │ 12 hint
│  │[x] ⏲ Časy                │                                                            │
│  │[x] 🌡 Teploty             │   Tento widget nemá další nastavení.                        │ 14
│  │[x] ⬡ Název tisku         │   Polohu, pořadí, velikost a zvýraznění upravíte tlačítkem  │ 12 hint
│  │[x] ❄ Vychladnutí         │   Upravit rozložení v okně kamery.                          │
│  │                          │                                                            │
│  │                          │                                                            │
│  └──────────────────────────┘                                                            │
│                                                                                            │
│  Všechny widgety                                                                           │ 14 SemiBold
│  [Upravit rozložení v okně kamery…]       Neprůhlednost pozadí karet                       │
│  [Obnovit výchozí]                        [━━━━━━━━━━━━━━━━━●━━━━━━━━━━]  72 %             │ Slider + 48
│  [Použít rozložení u všech tiskáren]      Platí pro okna kamer všech tiskáren. Vyšší       │ 12 hint
│                                           hodnota = čitelnější text na světlém obraze.     │
│                                                                                            │
│                                                                  [ Uložit ]  [ Zrušit ]    │
└────────────────────────────────────────────────────────────────────────────────────────────┘
   ← 216 →  24  ←───────────────── 508 ──────────────────→
   ← 300 → 32 ←──────────── 416 ────────────→
```
- **Seznam** (`ListBox`, šířka 216, výška = řádek 1): řádky výšky **32** (`ListBoxItem` `MinHeight` 32, `Padding` 8,0). Obsah řádku: zaškrtávací pole bez textu (20, `MinWidth` 0) + 8 + ikona 16 (`Icon…16Regular` z T-076 oddílu 6, barva textu) + 8 + název (14, `TextTrimming` se třemi tečkami a `ToolTip` s celým názvem) + značka „!“ (`TextBlock.marker`, jen při chybě v panelu widgetu).
  - Vybraný řádek = výběr Fluent (pozadí + svislý akcentový pruh vlevo). Výběr se při přepnutí tiskárny **zachová** (stejný widget u jiné tiskárny).
  - Výchozí výběr při prvním otevření: první widget (Stav).
  - Místo: 7 řádků × 32 + 2 = 226 ≤ 228. Při 8 a více widgetech se seznam posouvá uvnitř (jediná posuvná oblast; stránka sama se neposouvá). Pak je čas rozhodnout o zvětšení nebo seskupení.
- **Panel** (šířka 508): nadpis = ikona 20 + 8 + název widgetu (`TextBlock.section` bez horního okraje), 4, popis widgetu (hint 12, nejvýš 2 řádky), 12, pak obsah panelu nastavení (4.2, 4.3).
- **Neprůhlednost:** popisek nad posuvníkem, řádek `Slider` (30–95, `SmallChange` 1, `LargeChange` 5, `TickFrequency` 5, bez přichycení) + hodnota 48 vpravo (procenta podle kultury: „72 %“ cs / „72%“ en, tabulková čísla), nápověda pod ním (nejvýš 3 řádky). Hodnota se mění plynule; okna kamer ji převezmou **po uložení** (jako ostatní nastavení).
- Tlačítka vlevo dole nejsou akcentová (primární akce okna zůstává Uložit).

#### 3.3 Výškový rozpočet (nejhorší stav ve všech jazycích)
| Část | Výška |
|---|---|
| Řádek 0 + mezera | 32 + 12 = 44 |
| Seznam 7 widgetů / panel Vychladnutí (4.3: popis na 2 řádky = 212; navíc nápověda nebo chyba na 2 řádky = 228) | 226 / 212–228 |
| Všechny widgety: 16 + 20 + 4 + max(3 × 32 + 2 × 8 = 112; 20 + 4 + 32 + 4 + 3 × 16 = 108) | 152 |
| **Celkem** | **≤ 424 z 424** |

- Při 780 × 508 (obsah 400) by na řádek 1 zbylo 204: vešlo by se 6 widgetů a panel Vychladnutí jen s jednořádkovým popisem. Proto návrh 780 × 532 (Q2).
- Na 800 × 600 při 100 % (zmenšení na 780 × 520, obsah 412, řádek 1 = 216): v1 s 5 widgety (162) a panelem Vychladnutí (212) se vejde **bez posuvníku**. Jen když se v některém jazyce zalomí popis i nápověda / chyba prahu na 2 řádky (228), objeví se tam posuvník o 12 DIP. To je přijatelné, protože 800 × 600 už není cílová obrazovka (Q2).

#### 3.4 Nejdelší jazyk (de), vybrané Vychladnutí, jediná tiskárna
```
│  Drucker   [Widgets]   Allgemein   Autostart und Updates                                   │
│                                                                                            │
│  Drucker  [▣ MK4 Werkstatt                 ▾]                                              │
│                                                                                            │
│  ┌──────────────────────────┐   ❄ Abkühlung                                                │
│  │[x] ◉ Status              │   Zeigt nach dem Druckende, wie die Düse abkühlt und wann    │
│  │[x] ⏲ Zeiten              │   der Drucker sicher ausgeschaltet werden kann.              │
│  │[x] 🌡 Temperaturen        │                                                            │
│  │[x] ⬡ Druckname           │   Temperaturschwelle der Düse (°C)                           │
│  │[x] ❄ Abkühlung          ▐│   [ 50     ][^][v]   [Schwelle für alle Drucker übernehmen]  │ (zakázané)
│  │                          │   Unter dieser Temperatur ist Ausschalten sicher.            │
│  │                          │                                                            │
│  │                          │   Benachrichtigung „Drucker abgekühlt“: aus                  │ 12
│  └──────────────────────────┘   In „Allgemein“ ändern                                      │ odkaz
│                                                                                            │
│  Alle Widgets                                                                              │
│  [Layout im Kamerafenster bearbeiten…]    Deckkraft des Kartenhintergrunds                 │
│  [Standard wiederherstellen]              [━━━━━━━━━━━━━━━━━●━━━━━━━━━━]  72 %             │
│  [Layout für alle Drucker übernehmen]     Gilt für die Kamerafenster aller Drucker.        │ (zakázané)
│                                           Höhere Werte halten Text auf hellem Bild lesbar. │
```
- Jediná tiskárna: obě „… u všech tiskáren“ / „für alle Drucker“ jsou **zakázané** (ne skryté, rozvržení neposkočí) s tooltipem `SettingsOverlayApplyAllSingle` („Je nastavená jen jedna tiskárna.“, `ToolTip.ShowOnDisabled`).
- Tlačítka se nezalamují: nejdelší texty ≤ 36 znaků ≈ 280 ≤ 300 (viz délky v oddílu 8). Kdyby překlad přetekl, smí se zalomit na 2 řádky (tlačítko 52): rozpočet má pak rezervu jen při ≤ 6 widgetech, test to odhalí.

#### 3.5 Vypnutá kamera (it), vybraný Stav
```
│  Stampante  [▣ MK4 officina            ▾]   La telecamera è disattivata: le impostazioni   │ 12 hint
│                                              valgono dopo l'attivazione.                   │
│  ┌──────────────────────────┐   ◉ Stato                                                    │
│  │[▫] ◉ Stato   (zakázané)  │   …                                                        │
│  │[▫] ⏲ Tempi               │                                                            │
│  ...                                                                                       │
│  [Modifica layout nella finestra…] (zakázané, tooltip)                                     │
```
- Zakázaná jsou jen zaškrtávací pole (hodnoty zůstanou) a „Upravit rozložení v okně kamery…“. **Výběr řádků zůstane funkční** (seznam sám zakázaný není), takže panel Vychladnutí jde nastavit i bez kamery (práh potřebuje i upozornění).
- Nápověda vpravo od výběru tiskárny znovu použije `SettingsCameraWindowCameraOff` (`AutomationId` `WidgetsCameraOff`).
- „Obnovit výchozí“ a „Použít rozložení u všech tiskáren“ zůstanou povolené (mění rozpracované hodnoty, jako u T-036). Neprůhlednost je globální, zůstane povolená.

#### 3.6 Žádná tiskárna
```
│  Žádná tiskárna není nastavená. Přidejte ji na záložce Tiskárny.                           │ místo řádku 0
│                                                                                            │
│  (seznam a panel skryté)                                                                   │
│                                                                                            │
│  Všechny widgety                                                                           │
│  [Upravit rozložení v okně kamery…]  (zakázané)   Neprůhlednost pozadí karet               │
│  [Obnovit výchozí]                   (zakázané)   [━━━━━━━━━━━━━●━━━━━━]  72 %              │
│  [Použít rozložení u všech tiskáren] (zakázané)   …                                        │
```
- Text `SettingsWidgetsNoPrinters` (`AutomationId` `WidgetsNoPrinters`). Neprůhlednost jde nastavit i bez tiskáren.

#### 3.7 Neuložená (právě přidaná) tiskárna
Vše jako výchozí stav. Jen „Upravit rozložení v okně kamery…“ je zakázané s tooltipem `SettingsWidgetsEditLayoutUnsaved` („Nejdřív nastavení uložte.“): okno kamery pracuje s uloženou tiskárnou.

### 4. Panel vybraného widgetu

#### 4.1 Společná část (vždy)
- Nadpis: ikona 20 (stejná ikona jako v seznamu, varianta 20 nebo zvětšená 16) + 8 + název, `AutomationId` `WidgetsDetailTitle`. Panel má `AutomationProperties.Name` = název widgetu.
- Popis: `TextBlock.hint`, nejvýš 2 řádky (≤ 110 znaků ve všech jazycích), `AutomationId` `WidgetsDetailDescription`.
- Pod popisem 12 a obsah podle 4.2 nebo 4.3.

#### 4.2 Widget bez vlastního nastavení (Stav, Časy, Teploty, Název tisku)
```
◉ Stav
Název tiskárny, stav a průběh tisku v procentech.

Tento widget nemá další nastavení.
Polohu, pořadí, velikost a zvýraznění upravíte tlačítkem Upravit rozložení v okně kamery.
```
- „Tento widget nemá další nastavení.“ 14 (`WidgetsDetailNoSettings`), pod ním 4 a nápověda 12 `SettingsOverlayLayoutHint` (upravené znění, oddíl 8). Nic z toho není fokusovatelné, Tab jde ze seznamu rovnou do oddílu Všechny widgety.

#### 4.3 Vychladnutí (vlastní panel)
```
Výchozí                                                     Chyba prahu (fr)
❄ Vychladnutí                                               ❄ Refroidissement
Po skončení tisku ukáže chladnutí trysky a kdy je           Après la fin d'une impression, montre le refroidissement
tiskárnu bezpečné vypnout.                                  de la buse et quand l'imprimante peut être éteinte.

Práh teploty trysky (°C)                                    Seuil de température de la buse (°C)
[ 50     ][^][v]   [Použít práh u všech tiskáren]           [ 120    ][^][v]   [Appliquer le seuil à toutes…]
Pod touto teplotou je tiskárnu bezpečné vypnout.            Saisissez un nombre entier de 30 à 100.        ← červeně
                                                            
Upozornění „Tiskárna vychladla“: vypnuté                    Notification « Imprimante refroidie » : désactivée
Změnit na záložce Obecné                                    Modifier dans l'onglet Général
```
- Popisek nad polem 20 + 4 (`SettingsCooldownThreshold`).
- Řádek: `NumericUpDown` 120 (30–100, krok 1, `FormatString` „0“, `AutomationId` `WidgetCooldownThreshold`, `AutomationProperties.Name` = popisek) + 12 + tlačítko „Použít práh u všech tiskáren“ (`WidgetCooldownApplyAll`, ≤ 280). 120 + 12 + 280 = 412 ≤ 508.
- Pod řádkem 4 a nápověda `SettingsCooldownThresholdHint` (zkrácená na 1 větu, oddíl 8, `WidgetCooldownThresholdHint`). **Chyba nahrazuje nápovědu** (vzor T-033): `TextBlock.error` `SettingsCooldownThresholdError` „Zadejte celé číslo od 30 do 100.“ (`WidgetCooldownThresholdError`). Chyba se ukáže hned při zadání, ne až při uložení (jako u ostatních číselných polí).
- **Při chybě:** značka „!“ u řádku Vychladnutí v seznamu, u tiskárny ve výběru tiskárny i v seznamu tiskáren a u záložky Widgety (`SettingsTabWidgetsMarker`). Uložení skončí stavovou hláškou jako dnes. Chyba nepřekáží v přepínání widgetů ani tiskáren (hodnota je v rozpracované tiskárně, ne v panelu).
- 16 a **stav upozornění** (otázka Q3, doporučeno: přepínač jen na záložce Obecné, tady jen stav a odkaz):
  - text 12 (`WidgetCooldownNotifyState`), podle stavu na záložce Obecné (rozpracované hodnoty, ne uložené):
    - „Upozornění „Tiskárna vychladla“: zapnuté“ (`SettingsWidgetsCooledDownNotifyOn`),
    - „Upozornění „Tiskárna vychladla“: vypnuté“ (`SettingsWidgetsCooledDownNotifyOff`),
    - hlavní přepínač upozornění vypnutý: „Upozornění jsou vypnutá.“ (`SettingsWidgetsNotificationsOff`),
  - pod ním 4 a odkaz `HyperlinkButton` „Změnit na záložce Obecné“ (`WidgetCooldownNotifyLink`, `MinHeight` 32 kvůli cíli kliknutí, text 12 / 14 podle Fluent). Přepne na záložku Obecné a dá fokus přepínači „Tiskárna vychladla“ (`SettingsNotifyCooledDown`).
- Výška: 20 + 4 + 32 (popis 2 ř.) + 12 + 24 + 32 + 4 + 16 + 16 + 16 + 4 + 32 = **212** (nápověda nebo chyba na 2 řádky: + 16 = 228, pořád ≤ 228 řádku 1).
- Práh platí pro widget i upozornění, a proto zůstane povolený i s vypnutou kamerou.

#### 4.4 Proč přepínač upozornění jen na záložce Obecné (Q3)
- **Jeden zdroj pravdy:** upozornění je **globální**, panel Vychladnutí je **u tiskárny**. Zaškrtávací pole v panelu jedné tiskárny by naznačovalo, že jde zapnout jen pro ni.
- Přepínač závisí na hlavním přepínači upozornění a nastavení zvuku, které jsou na záložce Obecné. Zrcadlo by muselo ukazovat i tuto závislost.
- Zrcadlené pole by bylo druhou cestou ke stejné hodnotě se dvěma `AutomationId` a dvojím testováním. Stav + odkaz dá stejnou informaci a vede na místo, kde jsou všechna upozornění.
- Záložka Obecné na práh odkazuje přes `AutomationProperties.HelpText` přepínače (`SettingsNotifyCooledDownHelp`, upravené znění, oddíl 8).

### 5. Chování

#### 5.1 Výběr tiskárny
- `ComboBox` (`AutomationId` `WidgetsPrinter`, `AutomationProperties.Name` = „Tiskárna“), položky jako seznam tiskáren (obrázek 24, „!“, název se třemi tečkami), `ItemsSource` = `Printers`, `SelectedItem` = `SelectedPrinter` (sdílený se záložkou Tiskárny).
- Přidání nebo odebrání tiskárny na záložce Tiskárny se tu projeví hned.

#### 5.2 Seznam widgetů a klávesnice
- Seznam je **jedna zastávka Tab**. Šipky nahoru / dolů a Home / End mění výběr (panel se přepne hned).
- **Mezerník** na vybraném řádku přepne jeho viditelnost (ui-dev: obsluha `KeyDown` seznamu). Zaškrtávací pole v řádku nejsou zastávkou Tab (`IsTabStop=False`), myší jdou kliknout přímo (klik zaškrtne a zároveň vybere řádek). Klik jinam do řádku jen vybere.
- `AutomationProperties.Name` řádku = „{název}, zobrazený“ / „{název}, skrytý“ (`SettingsWidgetsItemShown` / `Hidden`). Zaškrtávací pole má `AutomationProperties.Name` „Zobrazit widget {0}“ (`SettingsWidgetsShowName`), aby šlo přepnout i přes UI Automation (vzor Toggle).
- Záložní řešení, kdyby obsluha mezerníku v `ListBox` Avalonie 12 nefungovala spolehlivě: zaškrtávací pole jako samostatné zastávky Tab za seznamem (ui-dev ověří a zapíše do poznámek).

#### 5.3 Pořadí Tab
Hlavní záložky → výběr tiskárny → seznam widgetů → prvky panelu (Vychladnutí: práh → Použít práh u všech → odkaz) → Upravit rozložení v okně kamery… → Obnovit výchozí → Použít rozložení u všech tiskáren → posuvník → Uložit → Zrušit. Zakázané prvky se přeskočí.

#### 5.4 „Upravit rozložení v okně kamery…“
- Trojtečka: akce pokračuje v jiném okně (zásada Windows i macOS pro příkazy, které potřebují další vstup).
- `AutomationProperties.HelpText` a `ToolTip` `SettingsWidgetsEditLayoutHelp` („Otevře okno kamery této tiskárny v režimu rozložení. Tam změníte polohu, pořadí, velikost a zvýraznění widgetů.“).
- Povolené, když: je vybraná tiskárna, je uložená (není jen v rozpracovaném nastavení) a má zapnutou kameru v uloženém i rozpracovaném nastavení. Jinak zakázané s tooltipem podle důvodu (`SettingsWidgetsEditLayoutCameraOff`, `SettingsWidgetsEditLayoutUnsaved`; `ToolTip.ShowOnDisabled`).
- Po stisku (požadavek na T-091, vstup zvenku):
  - okno kamery dané tiskárny se otevře, nebo se aktivuje a přepne do normálního okna (z malého náhledu i z celé obrazovky),
  - pokud je normální okno menší než 480 × 300 (pod tím je režim zakázaný, T-076 Q4), podle otázky Q5,
  - spustí se režim rozložení s pracovní kopií podle otázky Q4 (doporučeno: rozpracované rozložení z nastavení),
  - okno nastavení zůstane otevřené. Po „Hotovo“ přijde `LayoutSaved` a nastavení převezme rozložení (Rozsah, Opětovné načtení). Po Esc se v nastavení nic nemění.

#### 5.5 Obnovit výchozí, použít u všech
- „Obnovit výchozí“ (`CameraLayoutReset`) vrátí **celé rozložení vybrané tiskárny** (`ResetToDefault`: viditelnost, kotvy, pořadí, velikosti, zvýraznění; neznámé widgety ponechá). Práh ani neprůhlednost nemění. Bez potvrzení (vrátí ho Zrušit okna). Tooltip `SettingsWidgetsResetHelp`.
- Stavový řádek okna po akci (bez počtu kvůli množnému číslu):
  - „Použít rozložení u všech tiskáren“ → `SettingsOverlayAppliedAll`,
  - „Použít práh u všech tiskáren“ → `SettingsCooldownAppliedAll`,
  - „Obnovit výchozí“ → `SettingsWidgetsResetDone`.

#### 5.6 Platformy
- **Windows (Fluent):** mřížka 4 / 8, ovládací prvky 32, master-detail podle vzoru „list/details“ Windows App SDK, pořadí Uložit / Zrušit vpravo (beze změny).
- **macOS (HIG):** okno nastavení zůstává se záložkami (`TabControl`, jako zbytek aplikace; nástrojová lišta s ikonami podle HIG je mimo rozsah). Master-detail odpovídá vzoru „sidebar + detail“. `ComboBox` se na macOS vykreslí jako tlačítko s nabídkou (Fluent téma, bez nativního vzhledu). Pořadí tlačítek okna řeší T-033 / T-019.

### 6. Smlouva panelu nastavení widgetu (požadavek na T-081, Native AOT)
Registrace widgetu (`IOverlayWidgetDefinition`, explicitní seznam v composition rootu) musí kromě dnešního obsahu poskytnout:

| Člen | Účel |
|---|---|
| `string Id` | id z `OverlayWidgetIds` (už v T-081) |
| `string DisplayNameKey` | klíč `Strings` názvu (už v T-081), např. `CameraOverlayWidgetCooldown` |
| `string DescriptionKey` | **nový:** klíč `Strings` popisu pro panel (4.1) |
| `string IconKey` | **nový:** klíč zdroje `StreamGeometry` ikony 16 pro seznam (např. `IconWeatherSnowflake20Regular` zmenšená, T-076 oddíl 6) |
| `IOverlayWidgetSettingsFactory? Settings` | **nový:** `null` = widget nemá vlastní nastavení (4.2) |

`IOverlayWidgetSettingsFactory` (bez reflexe, instance vytvořená v composition rootu):
- `OverlayWidgetSettingsViewModel Create(IOverlayWidgetSettingsHost host)` – view model panelu pro **vybranou tiskárnu**. Vytvoří se při změně výběru (tiskárna nebo widget), předchozí se uvolní (`IDisposable`, odhlásí odběry).
- `bool HasError(PrinterEditorViewModel printer)` – značka „!“ u řádku seznamu bez vytváření panelu.

`IOverlayWidgetSettingsHost` (úzké rozhraní, implementuje `SettingsViewModel`):
- `PrinterEditorViewModel Printer` – rozpracovaná tiskárna. **Hodnoty a validace patří tiskárně** (např. `CooldownThresholdCelsius`, `CooldownThresholdError`), ne panelu. Uložit / Zrušit, stav „neuloženo“, značky chyb a opětovné načtení tak fungují jako u ostatních polí.
- `int PrinterCount` a operace „použít u ostatních tiskáren“ nad rozpracovanými tiskárnami (předaná typovaná kopírovací funkce, např. `CooldownThreshold.ApplyToAll`).
- `void ShowStatus(string text)` – stavový řádek okna.
- `void ShowNotificationSetting(PrintEventKind kind)` – přepnout na Obecné a dát fokus přepínači.
- čtení rozpracovaného stavu upozornění (hlavní přepínač + `CooledDown`) s oznámením o změně.
- `bool IsCameraEnabled`.

View panelu: `DataTemplate` s `x:DataType` = typ view modelu panelu v jednom sdíleném `ResourceDictionary` (např. `Settings/WidgetSettingsTemplates.axaml`) sloučeném do `SettingsWindow.DataTemplates`. Hostitel je `ContentControl Content="{Binding SelectedWidget.SettingsPanel}"`. Výběr šablony podle typu je bez reflexe a s kompilovanými bindingy. Nový widget s nastavením = view model panelu + šablona + factory v jednom řádku registrace.

Pořadí v seznamu = pořadí registrace (stálé, ne podle kotev, T-076 bod 8).

### 7. `AutomationId`
| Prvek | `AutomationId` |
|---|---|
| Záložka, její značka „!“ | `SettingsTabWidgets`, `SettingsTabWidgetsMarker` |
| Výběr tiskárny, nápověda vypnuté kamery, žádná tiskárna | `WidgetsPrinter`, `WidgetsCameraOff`, `WidgetsNoPrinters` |
| Seznam, řádek, zaškrtávací pole, značka | `WidgetsList`, `WidgetsItem-{id}`, `WidgetsVisible-{id}`, `WidgetsItemMarker-{id}` |
| Panel, nadpis, popis, bez nastavení | `WidgetsDetail`, `WidgetsDetailTitle`, `WidgetsDetailDescription`, `WidgetsDetailNoSettings` |
| Vychladnutí | `WidgetCooldownThreshold`, `WidgetCooldownThresholdHint`, `WidgetCooldownThresholdError`, `WidgetCooldownApplyAll`, `WidgetCooldownNotifyState`, `WidgetCooldownNotifyLink` |
| Všechny widgety | `WidgetsAllSection`, `WidgetsEditLayout`, `WidgetsReset`, `WidgetsApplyAll`, `WidgetsOpacity`, `WidgetsOpacityValue`, `WidgetsOpacityHint` |
| Upozornění na Obecné (T-080) | `SettingsNotifyCooledDown` (cíl odkazu) |

Navržená `PrinterOverlay*` z T-076 bodu 8 se nepoužijí. Zaniknou `PrinterTabOverlay`, `PrinterOverlayFields`, `PrinterOverlayCorner`, `PrinterOverlay*Normal|Mini`, `PrinterOverlayNormalHeader`, `PrinterOverlayMiniHeader`, `PrinterOverlayHint`, `PrinterOverlayCameraOff`.

### 8. Texty (`Strings.*`)
cs vyká. Ostatní jazyky doplní ui-dev. „Max.“ = doporučená max. délka v nejdelším jazyce (znaky).

Znovu použité z T-076 / T-080 beze změny: `CameraOverlayWidget*` (názvy), `CameraLayoutReset` („Obnovit výchozí“), `SettingsOverlayApplyAll`, `SettingsOverlayApplyAllSingle`, `SettingsOverlayAppliedAll`, `SettingsOverlayOpacity`, `SettingsOverlayOpacityHint`, `SettingsCooldownThreshold`, `SettingsCooldownThresholdError`, `SettingsCooldownApplyAll`, `SettingsCooldownAppliedAll`, `SettingsNotifyCooledDown`, `SettingsCameraWindowCameraOff`. Max. délka tlačítek `SettingsOverlayApplyAll` a `SettingsCooldownApplyAll`: **36** (šířka sloupce 300).

Nepoužijí se: `SettingsOverlayWidgets`, `SettingsOverlayAllPrinters`, `SettingsCooldownSection` (nadpisy dvousloupcové podzáložky T-076).

Změněné:
| Klíč | cs | en | Max. |
|---|---|---|---|
| `SettingsOverlayLayoutHint` | Polohu, pořadí, velikost a zvýraznění upravíte tlačítkem Upravit rozložení v okně kamery. | Change position, order, size and highlighting with Edit layout in camera window. | 120 |
| `SettingsCooldownThresholdHint` | Pod touto teplotou je tiskárnu bezpečné vypnout. | Below this temperature the printer is safe to turn off. | 75 |
| `SettingsNotifyCooledDownHelp` | Práh teploty trysky se nastavuje u tiskárny na záložce Widgety, widget Vychladnutí. | The nozzle temperature threshold is set per printer on the Widgets tab, Cool-down widget. | – |

Nové:
| Klíč | cs | en | Max. |
|---|---|---|---|
| `SettingsTabWidgets` | Widgety | Widgets | 12 |
| `SettingsWidgetsPrinter` | Tiskárna | Printer | 14 |
| `SettingsWidgetsNoPrinters` | Žádná tiskárna není nastavená. Přidejte ji na záložce Tiskárny. | No printer is set up. Add one on the Printers tab. | 90 |
| `SettingsWidgetsListName` | Widgety v okně kamery | Camera window widgets | – (jméno seznamu pro čtečku) |
| `SettingsWidgetsShowName` | Zobrazit widget {0} | Show the {0} widget | – |
| `SettingsWidgetsItemShown` / `SettingsWidgetsItemHidden` | {0}, zobrazený / {0}, skrytý | {0}, shown / {0}, hidden | – |
| `SettingsWidgetsNoSettings` | Tento widget nemá další nastavení. | This widget has no other settings. | 50 |
| `SettingsWidgetsAll` | Všechny widgety | All widgets | 24 |
| `SettingsWidgetsEditLayout` | Upravit rozložení v okně kamery… | Edit layout in camera window… | 36 |
| `SettingsWidgetsEditLayoutHelp` | Otevře okno kamery této tiskárny v režimu rozložení. Tam změníte polohu, pořadí, velikost a zvýraznění widgetů. | Opens this printer's camera window in layout mode, where you change the position, order, size and highlighting of the widgets. | – |
| `SettingsWidgetsEditLayoutCameraOff` | Zapněte kameru této tiskárny a uložte nastavení. | Turn on this printer's camera and save the settings. | 70 |
| `SettingsWidgetsEditLayoutUnsaved` | Nejdřív nastavení uložte. | Save the settings first. | 40 |
| `SettingsWidgetsResetHelp` | Vrátí viditelnost, polohu, pořadí, velikost a zvýraznění widgetů této tiskárny na výchozí. | Restores the visibility, position, order, size and highlighting of this printer's widgets. | – |
| `SettingsWidgetsResetDone` | Obnoveno výchozí rozložení. Projeví se po uložení. | The default layout was restored. It applies after saving. | 90 |
| `SettingsWidgetsCooledDownNotifyOn` | Upozornění „Tiskárna vychladla“: zapnuté | “Printer cooled down” notification: on | 60 |
| `SettingsWidgetsCooledDownNotifyOff` | Upozornění „Tiskárna vychladla“: vypnuté | “Printer cooled down” notification: off | 60 |
| `SettingsWidgetsNotificationsOff` | Upozornění jsou vypnutá. | Notifications are turned off. | 60 |
| `SettingsWidgetsNotifyLink` | Změnit na záložce Obecné | Change on the General tab | 36 |
| `CameraOverlayWidgetStatusDescription` | Název tiskárny, stav a průběh tisku v procentech. | Printer name, state and print progress in percent. | 110 |
| `CameraOverlayWidgetTimesDescription` | Zbývající čas, čas dokončení a doba tisku. | Time remaining, finish time and print time. | 110 |
| `CameraOverlayWidgetTemperaturesDescription` | Teplota trysky a podložky s grafem posledních 60 minut. | Nozzle and bed temperature with a chart of the last 60 minutes. | 110 |
| `CameraOverlayWidgetPrintNameDescription` | Čitelný název tisku s materiálem, výškou vrstvy a tryskou, ve větší velikosti s náhledem. | Readable print name with material, layer height and nozzle; larger sizes add a preview. | 110 |
| `CameraOverlayWidgetCooldownDescription` | Po skončení tisku ukáže chladnutí trysky a kdy je tiskárnu bezpečné vypnout. | After a print ends, shows the nozzle cooling down and when the printer is safe to turn off. | 110 |

Zaniknou: `SettingsPrinterTabOverlay`, `SettingsOverlayCorner`, `SettingsOverlayNormalColumn`, `SettingsOverlayItem*`, `SettingsOverlayHint` (pokud je už neodstranil T-081). Před „°C“ a „%“ nezlomitelná mezera, „…“ jeden znak U+2026, uvozovky podle jazyka („…“ cs/sk/de, “…” en, « … » fr, „…” pl, «…» it/es podle zvyklosti stávajících překladů).

### 9. Rizika podle jazyka
- de / fr: tlačítka „Použít … u všech tiskáren“ a „Upravit rozložení…“ – max. 36 znaků, jinak přeteče sloupec 300 (test `TextBlock` měření ze `SettingsWindowLayoutTests`).
- fr / es: popis widgetu na 2 řádky a nápověda / chyba prahu na 2 řádky zároveň = 228 = celá výška řádku 1 (rezerva 0). Proto max. 110 znaků popisu a 75 znaků nápovědy i chyby prahu (1 řádek v 508). Seznam má při 5 widgetech rezervu 66, při 7 jen 2.
- pl / it: stav upozornění s uvozovkami – 1 řádek v 508 při 12 (≈ 60 znaků).
- Hlavní záložky s „Widgety“: fr ≈ 560 / 748.

### 10. Snímky reference
`settings-widgets.png` (en, Stav), `settings-widgets-cooldown-de.png` (jediná tiskárna), `settings-widgets-cooldown-error-fr.png`, `settings-widgets-camera-off-it.png`, `settings-widgets-no-printers.png`, `settings-widgets-dark.png`. Přegenerovat všechny `settings-*.png` a `diagnostics-*.png` kvůli nové výšce oken.

### 11. Neověřeno
Skutečné Windows DPI 125 / 150 / 175 % a hlavní panel jiné výšky (měřeno výpočtem), macOS, chování mezerníku v `ListBox` Avalonie 12 (5.2), vzhled `HyperlinkButton` v obou motivech. Výšky textů jsou odhad (≈ 7,2 DIP na znak při 14, ≈ 6,3 při 12); rozhodne test bez posuvníku ve všech 8 jazycích.

## Otevřené otázky
- **Q1 – Umístění oblasti Widgety:**
  - **B (doporučeno):** hlavní záložka „Widgety“ za záložkou Tiskárny, s výběrem tiskárny sdíleným se záložkou Tiskárny (Návrh UI 1). Vejde se do 780 × 532, globální neprůhlednost je na přirozeném místě.
  - A: podzáložka „Widgety“ u tiskárny místo Překryvu. Konzistentní s ostatními podzáložkami, ale 548 × 358 nestačí: okno by muselo narůst na ≈ 780 × 570 (nevejde se na 1366 × 768 při 125 %) a vnoří tři úrovně výběru.
- **Q2 – Společná velikost oken Nastavení a Diagnostika:**
  - **780 × 532 (doporučeno):** cílová obrazovka 1366 × 768 při 125 % (nahrazuje 800 × 600 při 100 % z T-033). Na 800 × 600 se okno zmenší o 12 a obsah v1 se pořád vejde bez posuvníku.
  - 780 × 508 (beze změny): vejde se 6 widgetů a panel Vychladnutí jen s jednořádkovým popisem v každém jazyce; 7. widget nebo delší překlad = posuvník.
  - 840 × 560: pohodlná rezerva, ale nevejde se na 1366 × 768 při 125 % ani 1920 × 1080 při 175 % (zmenší se a obsah se posouvá).
- **Q3 – Přepínač upozornění „Tiskárna vychladla“:**
  - **Jen na záložce Obecné, v panelu Vychladnutí stav + odkaz (doporučeno):** jeden zdroj pravdy, upozornění je globální a panel je u tiskárny (Návrh UI 4.4).
  - Zrcadlené zaškrtávací pole v panelu (dvě místa pro jednu hodnotu, naznačuje nastavení u tiskárny).
  - Jen na Obecné, v panelu nic (uživatel se nedozví, že práh řídí i upozornění).
- **Q4 – „Upravit rozložení v okně kamery…“ s neuloženými změnami rozložení této tiskárny:**
  - **Režim rozložení začne z rozpracovaného rozložení nastavení (doporučeno):** uživatel uvidí, co právě nastavil. „Hotovo“ ho uloží (tím se uloží i rozpracovaná viditelnost této tiskárny), Esc nic nezmění.
  - Tlačítko zakázané, dokud rozložení není uložené (tooltip „Nejdřív nastavení uložte.“).
  - Dotaz „Uložit změny?“ před otevřením.
- **Q5 – Okno kamery menší než 480 × 300 při spuštění z nastavení:**
  - **Na dobu režimu zvětšit na 640 × 360 na stejném místě a po Hotovo / Esc vrátit původní velikost (doporučeno).**
  - Neměnit velikost: okno se otevře a tlačítko režimu ukáže „Pro úpravu rozložení zvětšete okno.“ (T-076 Q4).
  - Zvětšit natrvalo.
- **Q6 – Přesun rozsahu mezi úkoly (pro architekta po schválení):**
  - **Doporučeno:** pole prahu a „Použít práh u všech tiskáren“ přejdou z T-080 do tohoto úkolu (panel Vychladnutí); T-080 si ponechá Core, toast a přepínač na záložce Obecné (s upraveným `SettingsNotifyCooledDownHelp`). T-091 dostane vstup do režimu rozložení zvenku (tiskárna + volitelná počáteční pracovní kopie, Q4, Q5). T-081 rozšíří `IOverlayWidgetDefinition` podle Návrhu UI 6 a T-088 zaregistruje panel Vychladnutí.
  - Alternativa: T-080 udělá pole prahu dočasně jinde (např. na podzáložce Kamera) a tento úkol ho přesune. Dvojí práce na UI a testech.
- **Q7 – Popis widgetu v panelu:**
  - **Ano, jedna věta pod nadpisem (doporučeno):** widget bez nastavení tak nemá prázdný panel a uživatel ví, co karta ukazuje.
  - Ne: méně textů k překladu (5 klíčů × 8 jazyků), panel widgetu bez nastavení ukáže jen „Tento widget nemá další nastavení.“

## Poznámky k implementaci
- 2026-10-25, ui-designer: rozsah a kritéria přepsané podle rozhodnutí uživatele z 2026-10-25, doplněný Návrh UI (oddíly 0–11) a otázky Q1–Q7. Implementace čeká na rozhodnutí otázek.
