---
id: T-041
title: Okno Diagnostika ve velikosti nastavení bez scrollbaru
status: in-progress
owner: ui-designer + ui-dev
priority: P2
platform: all
depends_on: [T-011, T-033]
---

## Cíl
Okno Diagnostika má stejnou velikost jako okno nastavení (T-033: klientská oblast 780×508, vejde se na obrazovku 800×600 při 100 %) a ve výchozím stavu nezobrazuje scrollbar. Požadavek z ručního testu uživatele (2026-10-20).

## Současný stav (2026-10-20)
Okno je úzké a vysoké (cca 560×790), obsah (stav, teploty, ventilátory a pohyb, graf historie teplot, informace o tiskárně) se nevejde a zobrazuje se svislý scrollbar.

## Rozsah
- Nové rozložení pro 780×508 (např. teploty a ventilátory vedle sebe, graf přes celou šířku), navržené agentem ui-designer a schválené uživatelem před implementací (sekce `## Návrh UI`).
- Počítat s obrázkem tiskárny 48 DIP v záhlaví (T-039).
- Stejné umístění a zmenšení na pracovní plochu jako u nastavení (`SettingsWindowPlacement`), záložní ScrollViewer.
- V titulku jen tlačítko zavřít (bez minimalizace a maximalizace), jako u nastavení (T-040).
- Všech 8 jazyků bez scrollbaru (headless layout test jako u T-033), snapshoty.

## Rozhodnuto
- **Velikost (2026-10-20):** stejná jako okno nastavení (780×508 klient).
- **Titulek (2026-10-20):** jen křížek.
- **Postup (2026-10-20):** návrh ui-designera ke schválení uživatelem, pak implementace.
- **Návrh schválen (2026-10-20):** varianta A – dva sloupce hodnot, graf přes celou šířku s pružnou výškou (min. 120 DIP), informace o tiskárně v patičce bez nadpisu, během načítání jen text stavu; výběr tiskárny jako rozbalovací seznam místo názvu; tlačítko Zavřít zůstává; prázdný stav jen hláška. Implementovat až po sloučení T-039 (obě úlohy mění záhlaví Diagnostiky).

## Akceptační kritéria
- [x] Návrh UI schválený uživatelem
- [x] Okno 780×508 (klient), zmenšení na pracovní plochu, bez minimalizace a maximalizace
- [x] Bez scrollbaru ve všech 8 jazycích ve všech stavech (offline, tisk, bez teplot, dlouhé hodnoty) – headless test
- [x] Snapshoty aktualizované a zkontrolované
- [ ] Ruční ověření na Windows (uživatel)
- [x] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish win-x64 bez varování

## Návrh UI
Stav: **návrh 2026-10-20 (ui-designer), schválen uživatelem 2026-10-20 (všechny otázky varianta A).**

### Postup a ověření
- Současný stav: okno 560×760 (`Width`/`Height` v `DiagnosticsWindow.axaml`), jeden sloupec pod sebou, graf pevně 240 DIP, svislý posuvník. Snímková reference okna Diagnostika zatím neexistuje.
- Návrh je ověřený **prototypem** (vyhozený kód mimo repozitář, headless Avalonia 12.1.4 + Fluent + Skia, písmo Inter jako v UI testech, **skutečný ovládací prvek `TemperatureChart`** s 2,5 h historie, skutečné texty z resx ve všech 8 jazycích, obrázek tiskárny 48 DIP z T-039). Klientská oblast **780×508 DIP**, 8 jazyků × 8 stavů. Měřeno: `DesiredSize` obsahu vs. `Viewport` záchranného `ScrollViewer`, výška grafu, oříznutí jednořádkových textů.
- Výsledek: **bez posuvníku ve všech 64 kombinacích**; graf 748 × 214–218 DIP v běžném stavu, 208 offline s detailem, **157 v nejhorším stavu** (výběr ze 3 tiskáren + hláška „Sledování se zastavilo kvůli chybě“ + nejdelší stav + detail + patička na 2 řádky).
- Inter je o něco širší než Segoe UI, měření je pro Windows spíš konzervativní. Skutečné Windows DPI (125/150 %), Segoe UI a macOS ověřené nejsou.

### Velikost okna a rám (stejně jako nastavení, T-033 / T-040)
| Prvek | Hodnota |
|---|---|
| Okno | `Width=780`, `Height=508` (klient), `MinWidth=640`, `MinHeight=400`; umístění a zmenšení na pracovní plochu stejnou logikou jako `SettingsWindowPlacement` (implementace: znovupoužít / zobecnit, rozhodne ui-dev). Titulek: jen zavřít (bez minimalizace a maximalizace, jako T-040). |
| Okraje obsahu | `DockPanel Margin="16,12,16,12"` → obsah 748 DIP široký |
| Záchranný `ScrollViewer` | zůstává kolem hlavní části (vše kromě patičky); posouvá se jen pod minimální výškou grafu (okno menší než výchozí). |
| Mřížka | rozestupy 4/8/12/16, řádek hodnot 19 DIP (14 px + 1/1), ovládací prvky výška 32 |

Rozpočet výšky (508): 12 + **záhlaví 48** (58 s detailem, 68 s výběrem tiskárny a detailem) + 12 + [hláška 29 + 8] + **hodnoty 116** + 12 + **záhlaví grafu 32** + 4 + **graf (zbytek, min. 120)** + 12 + **patička 32–36** + 12.

### Drátěný model – tiskne, jedna tiskárna (cs)
```
┌─ Diagnostika ─────────────────────────────────────────────────────────── ✕ ┐
│ ┌48┐ MK4S workshop                                    Poslední aktualizace │  záhlaví
│ └──┘ Stav  Tiskne                                                 14:32:05 │
│                                                                            │
│ Teploty                                Ventilátory a pohyb                 │  hodnoty: 2 sloupce *,24,*
│            Aktuální   Cílová           Ventilátor hotendu        7 200 ot./min
│ Tryska     215,3 °C   215 °C           Ventilátor chlazení tisku 5 400 ot./min
│ Podložka    60,0 °C    60 °C           Rychlost tisku            100 %     │
│                                        Průtok                    95 %      │
│                                        Osa Z                     123,45 mm │
│                                                                            │
│ Historie teplot  ━ Tryska ━ Podložka  čárkovaně: cílová          [ 2 h  ▾] │  záhlaví grafu 32
│ 250 °C ┤‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾  │
│        │   graf přes celou šířku 748 × 214 (vyplní zbytek výšky)          │
│   0 °C ┼──────┬──────┬──────┬──────┬──────┬──────┬───────                 │
│          12:40  13:00  13:20  13:40  14:00  14:20                          │
│                                                                            │
│ Firmware 6.2.1+8896  Kód modelu 1.4.0  Název hostitele prusa-mk4s  [Zavřít]│  patička: informace + Zavřít
│ Průměr trysky 0,40 mm  MMU Ne                                              │
└────────────────────────────────────────────────────────────────────────────┘
```

### Drátěný model – nejhorší stav (více tiskáren, chyba sledování, offline, fr)
```
│ ┌48┐ [Original Prusa Core One L – dílna, 2. pat… ▾]     Dernière mise à jour│  výběr tiskárny místo názvu (Q2)
│ └──┘ État  Identifiants non valides                  19/10/2026 14:32:05 │
│      The secure (https) connection to the printer failed.                  │  detail 12 px, jen když je
│ ┌────────────────────────────────────────────────────────────────────────┐ │
│ │ Surveillance arrêtée suite à une erreur                                │ │  hláška (NoticeBrush)
│ └────────────────────────────────────────────────────────────────────────┘ │
│ Températures …                         Ventilateurs et mouvement …         │
│ Historique des températures ━ Buse ━ Plateau chauffant  pointillés : cible [2 h ▾]
│ graf 748 × 157                                                             │
│ Lecture impossible : Identifiants non valides                     [Fermer] │  stav informací místo hodnot (Q4)
```

### Drátěný model – bez tiskárny / spouštění
```
│ ┌────────────────────────────────────────────────────────────────────────┐ │
│ │ Není nastavena žádná tiskárna                                          │ │  jen hláška nahoře (záhlaví, hodnoty,
│ └────────────────────────────────────────────────────────────────────────┘ │  graf i informace skryté)
│                                                                   [Zavřít] │
```

### Prvky a rozměry
| Oblast | Návrh |
|---|---|
| Záhlaví | `Grid 48,12,*,16,Auto`, `MinHeight=48`, skryté bez tiskárny. Obrázek tiskárny 48×48 (T-039, nahoře). Název 18 px SemiBold, jeden řádek, zkrácení „…“ (+ `ToolTip` s celým názvem). Pod ním „Stav **Tiskne**“ (14 px, stav SemiBold, zalamuje se) a případný detail (12 px, `Opacity=0.75`, zalamuje se). Vpravo nahoře „Poslední aktualizace“ (12 px, 0.75) a čas (14 px), zarovnané vpravo. |
| Výběr tiskárny | při více tiskárnách `ComboBox` **místo názvu** (16 px SemiBold, `MinWidth=240`, `MaxWidth=440`, položky se zkracují „…“), `AutomationProperties.Name` = `DiagnosticsPrinter` („Tiskárna“) – Q2. |
| Hláška | beze změny (`NoticeBrush`, `Padding=10,6`), pod záhlavím, odstup 8. |
| Hodnoty | `Grid *,24,*` (2 × 362 DIP). Vlevo „Teploty“: tabulka `Auto | Auto (MinWidth 96) | Auto (MinWidth 80)` – záhlaví sloupců Aktuální/Cílová 12 px 0.75, aktuální hodnota **16 px SemiBold**, cílová 14 px; min. šířky brání poskakování, když místo „–“ přijdou hodnoty. Vpravo „Ventilátory a pohyb“: 5 řádků popisek | hodnota (popisky 0.75). Nejdelší popisek it „Ventola di raffreddamento stampa“ 226 DIP + hodnota ≈ 330 ≤ 362. |
| Záhlaví grafu | jeden řádek `Auto,16,*,12,Auto`: název SemiBold, legenda 12 px (`WrapPanel`), výběr rozsahu `ComboBox MinWidth=96` vpravo (`AutomationProperties.Name` = `DiagnosticsChart`). Nejdelší fr: 189 + 16 + ≈ 330 + 12 + 96 ≈ 643 ≤ 748. |
| Graf | `TemperatureChart` v řádku `*` přes celou šířku, `MinHeight=120`: **vyplní zbývající výšku** (Q5). Záchranný `ScrollViewer` se tak ve výchozí velikosti nikdy neposouvá. |
| Patička | `Grid *,16,Auto`, odstup 12. Vlevo informace o tiskárně jako dvojice „popisek hodnota“ (12 px, popisek 0.75) ve `WrapPanel` (1–2 řádky, každá dvojice celá; název hostitele `MaxWidth=200` se zkrácením „…“ a `ToolTip`). Dokud informace nejsou načtené, místo dvojic jen stav („Načítání…“, „Nelze načíst: …“, „Pro Prusa Connect není k dispozici“) – Q4. Vpravo **Zavřít** (`MinWidth=96`, výška 32, `IsCancel`, zarovnané dolů) – Q6. Stejné uspořádání jako stav uložení + tlačítka v nastavení. Skupina dostane `AutomationProperties.Name` = `DiagnosticsPrinterInfo`. |

### Naměřené výšky (prototyp, DIP)
| Stav | Jazyky | Záhlaví | Hláška | Hodnoty | Patička | Graf |
|---|---|---|---|---|---|---|
| Tiskne (1 tiskárna) | en | 48 | – | 116 | 32 | 748 × 218 |
| Tiskne | cs, sk, de, es, fr, it, pl | 48 | – | 116 | 36 (2 řádky) | 748 × 214 |
| Nečinná, tmavý motiv | všechny | 48 | – | 116 | 32–36 | 214–218 |
| Připojování, informace se načítají | všechny | 48 | – | 116 | 32 | 218 |
| Nedosažitelná + nejdelší detail + datum | všechny | 58 | – | 116 | 32 | 208 |
| Dlouhý název (60 znaků) + dlouhý hostname | všechny | 48 | – | 116 | 36 | 214 |
| Nejhorší: výběr + hláška + nejdelší stav + detail | všechny | 68 | 29+8 | 116 | 32 | 161 |
| Nejhorší + načtené informace na 2 řádky | všechny | 68 | 29+8 | 116 | 36 | **157** |
| Bez tiskárny | všechny | – | 29 | – | 32 | – |

Výška hodnot (116) nezávisí na jazyce (žádný popisek se nezalamuje); jazyk mění jen počet řádků patičky (fr, es, it, cs… 2 řádky, en 1).

### Zamítnuté varianty (změřené)
- **Tři sloupce (Teploty | Ventilátory a pohyb | Informace)** – nevejde se: v it/es/pl potřebuje sloupec ventilátorů ≈ 340 DIP, teploty ≈ 275, informace ≈ 280 (součet ≈ 950 > 748); v prototypu italské hodnoty ventilátorů dostaly šířku 0. Šlo by jen se zalamováním popisků na 2 řádky (a „Druckgeschwindigkeit“ se zalomit nedá).
- **Hodnoty vlevo pod sebou, graf vpravo** – levý sloupec má 13 řádků + 3 nadpisy ≈ 360 DIP a ≥ 340 DIP šířky (it), graf by měl jen ≈ 380 DIP šířky; s hláškou nebo detailem by se nevešel bez posuvníku.

### Chování
- Přepnutí tiskárny: obsah se vymění na místě, rozložení se nemění (minimální šířky sloupců teplot), graf zůstává stejně vysoký.
- Chybějící hodnoty: „–“ (beze změny). Chybějící data grafu: text `DiagnosticsChartNoData` uprostřed grafu (beze změny).
- Klávesnice: Tab = výběr tiskárny (je-li) → rozsah grafu → Zavřít; Esc zavře (`IsCancel`). Graf ani texty nejsou zastávky Tab. Fokus podle Fluent (viditelný).
- Přístupnost: obrázek tiskárny `AutomationProperties.Name` (T-039 `DiagnosticsPrinterImage`), výběry pojmenované (viz tabulka), stav je textem (ne jen barvou), kontrast popisků s `Opacity=0.75` ≈ 10:1 (světlý) – WCAG AA splněno; legenda grafu má text u každé barvy a cílovou čáru popisuje text „čárkovaně: cílová“.
- Rizika jazyků: popisek „Ventola di raffreddamento stampa“ (it 226) a „Wentylator chłodzenia wydruku“ (pl 208) jsou nejširší; nový text v pravém sloupci nesmí přesáhnout ≈ 240 DIP. Stav „Verbindung wird hergestellt…“ (de 194) a „Nieprawidłowe dane logowania“ (pl 206) se v záhlaví vejdou na 1 řádek. Detail stavu je technická angličtina z klienta (beze změny).

### Platformy
- **Windows (Fluent):** mřížka 4/8, ovládací prvky 32 DIP, tlačítko Zavřít vpravo dole, titulek jen se zavřením (T-040). Okno bez posuvníku na 800×600 při 100 %.
- **macOS (HIG, ověří T-018):** pomocné okno se zavírá semaforem / Cmd+W / Esc; tlačítko Zavřít v patičce je na macOS netypické – lze ho tam skrýt bez vlivu na rozložení (patička zůstává kvůli informacím). Neověřeno.

### Testy pro implementaci
- Headless test jako T-033: okno 780×508, v 8 jazycích × stavy výše (tiskne, offline s detailem, bez teplot, načítání, nejhorší, bez tiskárny) `Extent.Height <= Viewport.Height` a graf ≥ 120 DIP; jednořádkové texty bez oříznutí (kromě záměrně zkracovaného názvu a hostname).
- Snímky: `diagnostics-printing.png`, `diagnostics-offline-fr.png`, `diagnostics-worst-it.png`, `diagnostics-no-printer.png`, tmavý motiv `diagnostics-idle-dark.png`.

### Poznámka ke slučování
T-039 (větev `feature/T-039-printer-image`) už mění záhlaví v `DiagnosticsWindow.axaml` (obrázek 48 + výběr místo názvu). T-041 přepisuje stejný soubor – doporučuji implementovat T-041 až po sloučení T-039 do `develop` (nebo na něm založit), jinak bude konflikt.

## Otevřené otázky
- (žádné – Q1–Q7 rozhodnuty 2026-10-20, viz Rozhodnuto)

## Poznámky k implementaci
Stav 2026-10-20 (ui-dev): implementováno podle schváleného návrhu (varianta A), zbývá jen ruční ověření uživatelem na Windows – úloha proto zůstává v `in-progress`.

**Okno (`DiagnosticsWindow.axaml`, `.axaml.cs`)**
- `Width=780`, `Height=508`, `MinWidth=640`, `MinHeight=400`, `CanMinimize="False"`, `CanMaximize="False"` (velikost jde dál měnit), `CenterScreen`.
- Rozložení přesně podle návrhu: `DockPanel 16,12,16,12`; patička (informace o tiskárně ve `WrapPanel` jako dvojice „popisek hodnota“ 12 px, nebo jen stavový text, vpravo **Zavřít** `MinWidth=96`, `IsCancel`) mimo záchranný `ScrollViewer`; v něm `Grid Auto,Auto,Auto,Auto,*`: záhlaví (obrázek 48, název 18 px se zkracováním „…“ a `ToolTip`, nebo `ComboBox` 16 px `MinWidth=240`/`MaxWidth=440` s položkami zkracovanými „…“; „Stav **…**“ jako `Run`; detail 12 px 0.75; vpravo „Poslední aktualizace“ + čas), hláška, dva sloupce hodnot `*,24,*` (teploty `Auto | Auto ≥96 | Auto ≥80`, aktuální 16 px SemiBold), záhlaví grafu na jednom řádku (název, legenda 12 px, rozsah `MinWidth=96`) a `TemperatureChart` v řádku `*` s `MinHeight=120`.
- Nadpis „Informace o tiskárně“ ani popisek „Detail“ se už nezobrazují. Čtečky obrazovky je přesto dostanou: `TextBlock` hlásí jen svůj text a panel nemá vlastní automation peer, proto je detail obalený `Border` a patička `StackPanel` s `AutomationProperties.AccessibilityView="Control"`, `ControlTypeOverride="Group"` a jménem „Detail“ / „Informace o tiskárně“ (UI Automation = skupina s tímto jménem, uvnitř texty). Výběr tiskárny má jméno „Tiskárna“, výběr rozsahu „Historie teplot“. Ověřeno přes automation peery (`GetName`, `IsControlElement`, typ Group).
- Hodnoty nemají zkracování (headless test tak hlídá, že se nic neořízne); zkracuje se jen název tiskárny, položky výběru a název hostitele (`MaxWidth=200`, `ToolTip`).
- Pro testy mají prvky `AutomationProperties.AutomationId` (`DiagnosticsHeader`, `DiagnosticsPrinterName`, `DiagnosticsPrinterSelector`, `DiagnosticsState`, `DiagnosticsDetail`, `DiagnosticsLastUpdate`, `DiagnosticsNotice`, `DiagnosticsValues`, `DiagnosticsChartHeader`, `DiagnosticsChartRange`, `DiagnosticsChart`, `DiagnosticsScroll`, `DiagnosticsFooter`, `DiagnosticsPrinterInfo`, `DiagnosticsInfoStatus`, `DiagnosticsInfoValues`, `DiagnosticsHostname`, `DiagnosticsClose`).
- Kompilované bindingy beze změny, živé přepnutí jazyka funguje (test).

**Umístění (zobecněno)**
- `SettingsWindowPlacement` → `AuxiliaryWindowPlacement` (+ `AuxiliaryWindowBounds`), čisté funkce beze změny chování; výchozí 780×508 a minimum 640×400 jsou společné pro nastavení i Diagnostiku.
- Nový `WorkAreaFitter`: kód okna z `SettingsWindow` (přizpůsobení před zobrazením + oprava po otevření podle skutečného rámečku) přesunutý do sdílené třídy; používají ho `SettingsWindow` i `DiagnosticsWindow`.

**View model**
- `DiagnosticsViewModel.HasInfoValues` = je vybraná tiskárna a informace jsou načtené (bez stavového textu); patička zobrazuje buď dvojice, nebo stav. Změna se hlásí při změně `HasPrinter` i `InfoStatusText`.

**Testy**
- App.Tests: `DiagnosticsViewModelTests` (+3: dvojice až po načtení a znovu „Načítání…“ po přepnutí tiskárny, chyba / Prusa Connect jen stav, bez tiskárny nic), `Windows/AuxiliaryWindowPlacementTests` (přejmenované `SettingsWindowPlacementTests`).
- UiTests (`Diagnostics/`): `DiagnosticsWindowHarness` (okno jako v aplikaci přes `AuxiliaryWindowSlot`, falešný monitor, informace a 2,5 h historie grafu, pevný čas), `DiagnosticsWindowLayoutTests`:
  - velikost 780×508 = `AuxiliaryWindowPlacement`, minimum, jen křížek (`CanMinimize`/`CanMaximize` = false, `CanResize` = true);
  - **8 jazyků × 10 stavů** (tiskne, nečinná v tmavém motivu, offline s nejdelším detailem a datem, připojování s načítáním, Prusa Connect, dlouhý název + hostname, nejhorší stav s neúspěšnými i s načtenými informacemi, bez tiskárny, spouštění): žádný `ScrollViewer` se neposouvá, žádný jednořádkový text se neořízne, graf ≥ 120 DIP a sahá až 12 DIP nad patičku;
  - samokontrola: při výšce 400 se posouvá, bez `MinHeight` je graf menší než 120, krátký graf nesahá k patičce, zúžený popisek se hlásí jako oříznutý;
  - graf vyplní zbytek (o 100 DIP vyšší okno = o 100 DIP vyšší graf), přepnutí tiskárny nemění rozložení, název vs. výběr, jména pro čtečky přes automation peery (výběry, skupina detailu a informací), zkrácení dlouhého názvu a hostname s `ToolTip`, detail jen s důvodem, patička stav → dvojice, Zavřít vpravo, prázdný stav jen hláška + Zavřít, Tab: výběr tiskárny → rozsah → Zavřít, Esc zavře, přepnutí jazyka za běhu (it) bez problémů rozložení.
- Snímky (zkontrolované): `diagnostics-printing.png`, `diagnostics-idle-dark.png`, `diagnostics-offline-fr.png`, `diagnostics-worst-it.png`, `diagnostics-no-printer.png`; přegenerovaný `diagnostics-printer-image.png` (T-039, nové záhlaví).
- Poznámka k testům: výsledek dotazu na informace se v headless UI vlákně vrací přes thread pool (UI vlákno neběží na výchozím `TaskScheduler`), harness proto na výsledek čeká (s časovým limitem).

**Ověření (Windows 11, 2026-10-20)**
- `dotnet build` celé řešení 0 varování, `dotnet test` 2× zelené (3980 testů, 1 přeskočený – podmíněný prostředím, mimo T-041), AOT publish win-x64 0 varování.
- Aplikace se ručně nespouštěla: nejde ji izolovat od skutečného `%APPDATA%` (`Environment.GetFolderPath` proměnnou prostředí ignoruje) a uživateli běží jiná instance. Vzhled ověřují headless testy a snímky (písmo Inter); Segoe UI, skutečné DPI 125/150 % a macOS neověřené – ruční ověření na Windows zůstává na uživateli.
- macOS: beze změny oproti návrhu (tlačítko Zavřít je tam netypické, lze ho později skrýt bez vlivu na rozložení) – neověřeno (T-018).
