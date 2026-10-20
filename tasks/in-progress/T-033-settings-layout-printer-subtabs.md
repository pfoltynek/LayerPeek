---
id: T-033
title: Okno nastavení bez posuvníku a podzáložky tiskárny (Tiskárna / Kamera)
status: in-progress
owner: ui-designer + ui-dev
priority: P2
platform: all
depends_on: [T-014]
---

## Cíl
Okno nastavení se při prvním zobrazení vejde celé bez svislého posuvníku, a to i na obrazovce 800×600. Formulář tiskárny se rozdělí na dvě podzáložky: **Tiskárna** (připojení) a **Kamera** (nastavení kamery této tiskárny). Na podzáložce Kamera pak přibudou další volby z navazujících úkolů (T-034 až T-037).

## Současný stav (2026-10-19)
- `SettingsWindow.axaml`: `Width=780`, `Height=680`, `MinWidth=640`, `MinHeight=480`, `WindowStartupLocation=CenterScreen`, okraj 16 px. Dole pruh Uložit/Zrušit + stav (≈ 44 px), nahoře volitelný pruh upozornění (`HasNotice`) a text „Načítám…“.
- `TabControl` se dvěma záložkami (`SelectedTabIndex`: 0 = Tiskárny, 1 = Obecné). `SettingsViewModel.SaveAsync` při chybě přepne na záložku s chybou.
- Záložka **Tiskárny**: vlevo seznam (220 px) s tlačítky Přidat/Odebrat, vpravo `ScrollViewer` s jedním `StackPanel`: Název, Připojení, Adresa (+ nápověda), Uživatelské jméno (+ nápověda), Heslo (+ nápověda), Otestovat spojení + výsledek, sekce **Kamera** (hláška o odstraněných údajích, zapnutí, RTSP adresa + nápověda + chyba, Otestovat + výsledek).
- Proč je potřeba posuvník: obsah pravého panelu je zhruba 650–700 px vysoký (5 polí po ≈ 83 px, 2× tlačítko testu, sekce Kamera ≈ 200 px), viditelná výška je ≈ 550 px (680 − okraje − pruh tlačítek − záhlaví záložek). S chybovými hláškami nebo v češtině (dvouřádkové zaškrtávací pole kamery) je obsah ještě vyšší. Viz referenční snímky `tests/PrusaTrayDiagnostic.App.UiTests/Snapshots/settings-*.png` – posuvník je vidět ve všech.
- Záložka **Obecné** (interval, jazyk, 8 zaškrtávacích polí notifikací, zbývající čas, zvuk) má odhadem ≈ 580 px, takže se pravděpodobně také posouvá (ověřit měřením).
- Testy: `SettingsWindowSnapshotTests` (4 PNG, okno 780×680, škálování 1.0, en-US), `SettingsWindowTests`, `SettingsWindowCameraUiTests`, `SettingsWindowLanguageTests`; view modely v `tests/PrusaTrayDiagnostic.App.Tests/Settings`.

## Rozhodnuto
- **Cílová obrazovka (2026-10-19):** nejmenší podporovaná obrazovka je **800×600 při 100 %**. Pracovní plocha je ≈ 800×560 DIP, takže okno včetně titulku se musí vejít do ≈ **780×540 DIP** (klientská oblast ≈ 780×508 při titulku 32 DIP). Návrh se dělá pro tuto velikost.
- **Forma podzáložek (2026-10-19):** vnořené podzáložky **Tiskárna | Kamera** v pravém panelu záložky Tiskárny. *(Počet podzáložek upřesněn schválením návrhu níže – Q4.)*
- **Co se musí vejít (2026-10-19):** bez posuvníku včetně **všech chybových hlášek a výsledků testů, ve všech 8 jazycích**. Posuvník smí vyvolat jen pruh upozornění nahoře (`HasNotice`).
- **Jak získat místo (2026-10-19):** podzáložky + menší okno, kde je potřeba, dva sloupce. Nápovědy zůstávají viditelné. Podrobné rozložení navrhne agent `ui-designer` (`.claude/agents/ui-designer.md`) do sekce `## Návrh UI` a **před implementací ho schválí uživatel**.
- **Přepnutí tiskárny (2026-10-19):** vybraná podzáložka zůstává stejná (Kamera → Kamera). Záhlaví podzáložky s chybným polem ukazuje značku `!`.
- **Záložka Obecné (2026-10-19):** musí se také vejít bez posuvníku (např. dva sloupce).
- **Velikost okna (2026-10-19):** pevná výchozí velikost, omezená pracovní plochou. `ScrollViewer` zůstává jako záchrana. Uživatel může okno zvětšit.
- **Závislost na T-014 (2026-10-19):** nové úkoly smí stavět na T-014, i když je zatím v `in-progress` (kód je sloučený v `develop`).
- **Schválení návrhu UI (2026-10-19):** uživatel schválil návrh v sekci `## Návrh UI` s těmito odpověďmi:
  - **Q1 – nápověda a chyba:** chyba se zobrazí **místo nápovědy svého pole**, dokud je vidět; po opravě se nápověda vrátí.
  - **Q2 – odmítnutý test:** test odmítnutý kvůli neplatným polím ukáže jen krátký text `SettingsFixErrors` („Některé hodnoty nejsou platné…“), chyba pole se ve výsledku neopakuje.
  - **Q3 – hláška o odstraněných údajích kamery:** zmizí, jakmile uživatel kameru zapne, změní adresu nebo kameru úspěšně otestuje (a jako dosud po uložení).
  - **Q4 – podzáložky:** čtyři podzáložky **Tiskárna | Kamera | Okno kamery | Překryv** (nahrazuje dřívější rozhodnutí o dvou podzáložkách). Okno kamery a Překryv přidají T-035/T-036/T-037.
  - **Q5 – velikost okna kamery:** editovatelný `ComboBox` („960 × 540“, předvolby + vlastní zadání) – T-035.
  - **Q6 – Přidat/Odebrat:** textová tlačítka pod sebou přes celou šířku seznamu.
  - **Q7 – klávesnice:** Ctrl+Tab / Ctrl+Shift+Tab hlavní záložky, Ctrl+PageDown / Ctrl+PageUp podzáložky; opravit pořadí Tab (obsah před Přidat/Odebrat a Uložit/Zrušit).
  - **Q8 – obrázek tiskárny:** seznam 24, podzáložka Tiskárna 56 (tlačítko s nabídkou), Diagnostika 48, tray 16 – T-039.
  - **Q9 – validace okna kamery:** u vypnuté kamery se velikost okna kamery nevaliduje – T-035.
  - Součástí schválení: položky překryvu seskupené nejdřív údaje tiskárny, pak údaje úlohy (T-036); hlavní záložky 18 px / 36 DIP; před náhledem kamery se nerezervuje zástupné místo (T-034).
  - **Q10 – odmítnutý test kamery bez chyby u pole (2026-10-19):** vypnutá kamera s prázdnou adresou (pole chybu neukazuje) – výsledek testu dál říká konkrétně „Zadejte adresu streamu, např. rtsp://192.168.0.227/live.“ (`SettingsCameraErrorMissing`), ne `SettingsFixErrors` (ponechat současné chování).
  - **Oprava při implementaci:** **úspěšný** výsledek testu se nesmí zobrazit červeně (prototyp ukazoval „Die Kamera funktioniert“ červeně) – výsledky testu spojení i kamery: úspěch zeleně (`SettingsSuccessBrush`), chyba červeně, probíhající / zrušený test běžnou barvou textu.

## Rozsah
1. **Návrh (ui-designer):** do sekce `## Návrh UI` tohoto souboru drátěné modely (ASCII) záložky Tiskárny (obě podzáložky) a záložky Obecné ve stavech: výchozí, všechny chyby, výsledky testů, nejdelší jazyk; velikosti a rozestupy; místo pro sekce z T-034 (náhled), T-035 + T-037 (jedna sekce „Okno kamery“) a T-036 (Překryv) – podzáložka Kamera s nimi musí zůstat bez posuvníku, případně návrh, jak je rozvrhnout. Návrh předložit uživateli ke schválení. **Implementace začne až po schválení.**
2. **Implementace (ui-dev, případně ui-designer pro XAML):**
   - Vnořený `TabControl` v pravém panelu (Tiskárna | Kamera); obsah podle schváleného návrhu.
   - Výchozí velikost okna podle návrhu (vejde se do 780×540 DIP včetně titulku), omezená pracovní plochou aktuální obrazovky; `MinWidth`/`MinHeight` podle návrhu; `ScrollViewer` jako záchrana.
   - Výběr podzáložky ve view modelu (např. `SettingsViewModel.SelectedPrinterTabIndex`), zachovaný při přepnutí tiskárny. Uložení s chybou přepne na záložku **i podzáložku** s první chybou (např. chybná adresa kamery → Kamera).
   - Značka `!` na záhlaví podzáložky s chybným polem (např. `PrinterEditorViewModel.HasConnectionErrors` / `HasCameraErrors`).
   - Záložka Obecné bez posuvníku podle návrhu.
   - `AutomationProperties.AutomationId` pro nové prvky, překlady nových textů do všech 8 jazyků (konvence T-029).
   - Bez nového NuGet balíčku.

## Akceptační kritéria
- [x] Návrh UI v sekci `## Návrh UI` schválený uživatelem (datum schválení zapsané v Rozhodnuto)
- [x] Podzáložky Tiskárna / Kamera u vybrané tiskárny; existující pole a testy spojení/kamery fungují beze změny chování (kromě schválených změn Q2 a Q3)
- [x] Uložení s chybou přepne na záložku **a podzáložku** s první chybou; testy view modelu (App.Tests) pro chybu v poli tiskárny, v adrese kamery a na záložce Obecné
- [x] Při přepnutí tiskárny zůstává vybraná podzáložka (test view modelu); značka `!` na záhlaví podzáložky s chybou (test view modelu + UI test)
- [x] Headless UI test „bez posuvníku“: okno ve výchozí velikosti; u každé záložky/podzáložky platí pro každý viditelný `ScrollViewer` `Extent.Height <= Viewport.Height`. Stavy: tiskárna PrusaLink s uloženým heslem, Prusa Connect, všechny chyby validace, výsledek testu spojení (úspěch i dlouhá chyba), výsledek testu kamery, hláška o odstraněných údajích kamery; záložka Obecné včetně chyb. Vše ve všech 8 jazycích
- [x] Test, že výchozí velikost okna + titulek (konstanta 32 DIP v testu) ≤ 780×540 DIP a že se velikost omezí na menší pracovní plochu (logika omezení testovaná bez skutečné obrazovky)
- [x] Snímkové reference aktualizované (`PTD_UPDATE_SNAPSHOTS=1`, vizuálně zkontrolované) a nové snímky: podzáložka Kamera, záložka Obecné
- [x] Testy přepnutí jazyka za běhu pro nové texty (záhlaví podzáložek), `XamlLocalizationTests` projde
- [x] `dotnet build` a `dotnet test` bez chyb a varování; `dotnet publish -c Release -r win-x64` (AOT) bez varování

## Návrh UI
Stav: **návrh 2026-10-19 (ui-designer), schválený uživatelem 2026-10-19** (odpovědi Q1–Q9 v sekci Rozhodnuto). Místa níže označená „viz Q…“ platí ve schválené variantě A.

### Postup a ověření
- Současný stav viz referenční snímky `tests/PrusaTrayDiagnostic.App.UiTests/Snapshots/settings-*.png` (780×680, posuvník všude).
- Návrh je ověřený **prototypem** (vyhozený kód mimo repozitář, headless Avalonia 12.1.4 + Fluent + Skia, písmo Inter jako v UI testech): klientská oblast **780×508 DIP**, 8 jazyků × 26 stavů (nejdelší chybové hlášky a výsledky testů z resx, nové texty jako koncepty ve všech 8 jazycích). Měřeno: `DesiredSize` obsahu každého vlastního `ScrollViewer` vs. `Viewport`, oříznutí jednořádkových textů, počet řádků záhlaví záložek.
- Inter je o něco širší než Segoe UI (Windows), takže měření je pro Windows spíš konzervativní. Skutečné Windows DPI (125/150 %), Segoe UI a macOS ověřené nejsou.

### Velikost okna a rám (Windows Fluent: mřížka 4/8 px, výška ovládacích prvků 32)
| Prvek | Hodnota |
|---|---|
| Okno | `Width=780`, `Height=508` (klientská oblast; s titulkem 32 a rámečkem ≈ 782×541). Při otevření omezit na pracovní plochu obrazovky (DIP, po odečtení titulku 32 + rámečku), pak vycentrovat. `MinWidth=640`, `MinHeight=400`; menší okno = záchranný `ScrollViewer`. |
| Okraje obsahu | `DockPanel Margin="16,8,16,12"` |
| Spodní pruh | odstup 12; Uložit (`accent`, výchozí) + Zrušit, `MinWidth=96`, výška 32, vpravo, mezera 8; vlevo stav uložení (zalamuje se). Pořadí Uložit/Zrušit = Windows; na macOS (T-018) Zrušit vlevo, Uložit vpravo. |
| Pruh upozornění | beze změny nahoře (`HasNotice`), smí jako jediný vyvolat posuvník. |
| Hlavní záložky | Fluent `TabItem` zmenšené: `FontSize=18`, `MinHeight=36`, `Padding=12,0` (výchozí Fluent 24 px / 48 DIP by vzal 12 DIP navíc). Odstup obsahu 8. |
| Záložka Tiskárny | `Grid ColumnDefinitions="184,16,*"` → pravý panel **548** DIP |
| Podzáložky | vnořený `TabControl` (třída `sub`): `FontSize=14`, `MinHeight=32`, `Padding=10,0`, jedna řada (ověřeno i v italštině se značkami `!` na 3 záhlavích: ≈ 520/548). Obsah: `ScrollViewer` (záchrana), odstup 8 → **viditelná plocha 548×358**. |
| Záložka Obecné | `ScrollViewer`, viditelná plocha **748×400**, dva sloupce `*,24,*` (362 + 362). |
| Popisky / nápovědy / chyby | popisek nad polem (14 px, odstup 4); nápověda 12 px, `Opacity=0.7`, odstup 4; chyba 12 px, `SettingsErrorBrush`, odstup 2, hned pod polem. Odstup skupin polí 8, před tlačítkem testu 12. |

Rozpočet výšky (508): 8 + 36 (záložky) + 8 + 34 (podzáložky) + 8 + **358** + 12 + 32 + 12.

### Záložka Tiskárny – seznam (vlevo, 184 DIP)
```
┌──────────────────────┐
│[▣] ! MK4S workshop   │  položka: obrázek 24×24 (T-039) · 8 · „!“ (jen s chybou) · název (zkrácení „…“)
│[▣]   Core One – off… │
│[▣]   XL 5T           │
│                      │
├──────────────────────┤
│[ Přidat tiskárnu    ]│  tlačítka pod sebou přes celou šířku, výška 32, mezera 8
│[ Odebrat            ]│  (nejdelší: fr „Ajouter une imprimante“ 154+22 = 176 ≤ 184)
└──────────────────────┘
```
Prázdný seznam: vpravo jen text `SettingsNoPrinters` (bez podzáložek), Odebrat zakázané.

### Podzáložky (návrh: 4, jedna úroveň) – viz Q4
`Tiskárna | Kamera | Okno kamery | Překryv` – vybraná podzáložka je ve view modelu (`SelectedPrinterTabIndex` 0–3), při přepnutí tiskárny zůstává. Záhlaví s chybným polem: `! Tiskárna` („!“ tučně, `SettingsErrorBrush`, vlevo od textu; význam nenese jen barva). Značka se ukazuje podle stejného pravidla jako `!` v seznamu (až po odhalení chyb). Mapování: Tiskárna = název, adresa, jméno, heslo; Kamera = adresa kamery; Okno kamery = velikost (T-035); Překryv = bez validace. Uložení s chybou přepne na první chybnou tiskárnu, pak podzáložku v pořadí Tiskárna → Kamera → Okno kamery, jinak na Obecné.

#### Tiskárna (548×358)
```
┌56×56─┐ Název                     Připojení
│ [▣]▾ │ [MK4S workshop       ]   [PrusaLink (místní síť)          ▾]   ← 308 DIP
└──────┘ Název může mít nejvýš 200 znaků.  (chyba přes šířku Název + Připojení)
Adresa
[192.168.1.30                                                         ]
IP adresa nebo název hostitele, např. 192.168.1.20 nebo http://prusa-mk4.local   (nápověda / chyba – Q1)
Uživatelské jméno                     Heslo                        ← 2 sloupce 266 | 16 | 266
[maker                       ]        [●●●●●  (Beze změny)        ]
Uživatelské jméno PrusaLink z …       Ponechte prázdné, pokud …
                                    
[Otestovat spojení] [Zrušit test] ▭▭  Tiskárna není dosažitelná. Zkontrolujte adresu
                                      a jestli je tiskárna zapnutá a připojená k síti.
```
- Obrázek tiskárny (T-039): tlačítko 56×56 s rozbalovací nabídkou (Podle modelu (MK4S) / výběr modelu / Vlastní obrázek… / Odebrat vlastní obrázek), `AutomationProperties.Name` = „Obrázek tiskárny: …“. Do T-039 sloupec obrázku chybí (Název je širší).
- Výsledek testu vpravo od tlačítek, zalamuje se, zarovnaný nahoru (+6). Během testu: Zrušit test + neurčitý `ProgressBar` 48 DIP.
- Prusa Connect: jméno/heslo zakázané, nápovědy „nepoužívá“ (beze změny chování).

#### Kamera (548×358)
```
[x] Používat kameru (položka Kamera v nabídce v oznamovací oblasti)      ← 1 řádek ve všech jazycích (fr 508/548)
Adresa RTSP streamu
[rtsp://192.168.0.227/live                                             ]
Například rtsp://192.168.0.227/live (kamera Prusa Buddy3D); výchozí port je 554. Nezadávejte …  (2 řádky)
<chyba adresy>
┌──────── náhled 320×180 (T-034) ┐  [Otestovat kameru]
│  snímek, Stretch=Uniform,      │  [Zrušit test] ▭▭      (jen během testu)
│  poměr stran zachován          │  Kamera funguje (1920 × 1080 px).
└────────────────────────────────┘
```
- Náhled jen po úspěšném testu; **bez zástupného prvku** (místo se nerezervuje – s rezervovanými 180 DIP by se stav „hláška o odstraněných údajích“ nevešel). Bez náhledu jsou tlačítko a výsledek zarovnané vlevo.
- Hláška o odstraněných údajích (`CameraCredentialsRemoved`) zůstává nahoře na podzáložce (12 px, pozadí `NoticeBrush`); se snímkem náhledu se nevejde → Q3.

#### Okno kamery (T-035 + T-037, 548×358)
```
Kamera je vypnutá. Nastavení zůstane uložené a použije se, až kameru zapnete (záložka Kamera).   ← jen u vypnuté kamery
[x] Pamatovat poslední pozici a velikost
    Když je zapnuto, hodnoty níže se použijí, jen dokud okno nemá zapamatovanou pozici. …  (2 řádky)
Otevřít jako  [Jako minule        ▾]                       (combo ≥ 220)
Monitor       [Monitor 2 (2560 × 1440) – nepřipojený                    ▾]
                    Velikost        Umístění              Vždy navrchu   (záhlaví SemiBold, „Vždy navrchu“ max. 2 řádky v 88)
Normální okno     [960 × 540  ▾]   [Uprostřed        ▾]       [ ]
Malý náhled       [320 × 180  ▾]   [Vpravo dole      ▾]       [x]
                  Zadejte šířku 160–16384 a výšku 90–16384.
```
- Sloupce: popisek Auto | 12 | velikost 140 | 12 | umístění * | 12 | 88. Velikost = editovatelný `ComboBox` (předvolby 640×360…1920×1080 + vlastní „Š × V“) → Q5 v sekci otázek.
- U vypnuté kamery je celá sekce zakázaná (hodnoty zůstávají) a nahoře je hint.

#### Překryv (T-036, 548×358)
```
Roh překryvu              Normální okno   Malý náhled   Údaje o úloze (průběh,
[Vlevo dole        ▾]     a celá obrazovka              časy, název souboru) se
Název tiskárny                [ ]             [ ]        zobrazují jen během tisku.
Stav tiskárny                 [x]             [x]        Změny se po uložení projeví
Teplota trysky                [ ]             [ ]        i v otevřených oknech.
Teplota podložky              [ ]             [ ]
Průběh (%)                    [x]             [x]        (u vypnuté kamery navíc hint
Zbývající čas                 [x]             [x]         „Kamera je vypnutá…“)
Čas dokončení (hodiny)        [ ]             [ ]
Doba tisku                    [ ]             [ ]
Název souboru                 [ ]             [ ]
```
- Matice 9 × 2 zaškrtávacích polí, řádek 32 DIP (cíl kliknutí ≥ 32), sloupce Auto | 120 | 120 | 16 | * (nápověda). Výběr rohu v levé horní buňce. Pořadí položek: údaje tiskárny, pak údaje úlohy (sedí k nápovědě; T-036 má jiné pořadí – stačí potvrdit při schválení).
- Každé pole má `AutomationProperties.Name` „{položka} – {sloupec}“ (např. „Stav tiskárny – Malý náhled“).

### Záložka Obecné (748×400, dva sloupce)
```
Interval dotazování (sekundy)                Jazyk
[ 5     ][^][v]                              [Podle systému          ▾]
Jak často se aplikace ptá tiskáren na stav.  Mění jen texty. Čísla, data a časy se vždy
<chyba intervalu>                            řídí regionálním nastavením systému.
Upozornění
[x] Zobrazovat upozornění                    Zvuk  [Jen důležitá upozornění          ▾]
    [x] Tisk zahájen                         [x] Chyba tiskárny
    [x] Tisk dokončen                        [x] Tiskárna vyžaduje zásah (i odmítnuté heslo)
    [x] Tisk pozastaven                      [x] Tiskárna je offline nebo opět online
    [x] Tisk obnoven                         Upozornit, když zbývá tolik minut tisku (0 = vypnuto)
    [x] Tisk zastaven (zrušen)               [ 15    ][^][v]
                                             <chyba zbývajícího času>
```
Zvuk, události a zbývající čas jsou zakázané, když jsou upozornění vypnutá (beze změny chování). Nejvyšší obsah: es 353/400 (s chybami).

### Naměřené výšky obsahu (DIP; podzáložky viditelná plocha 358, Obecné 400)
Doporučená varianta (Q1 = A, Q2 = A, Q3 = A):
| Stav | nejvyšší | jazyk | rezerva |
|---|---|---|---|
| Tiskárna – PrusaLink s uloženým heslem | 287 | es/fr/it | 71 |
| Tiskárna – Prusa Connect | 273 | všechny | 85 |
| Tiskárna – všechny 4 chyby | 328 | fr | 30 |
| Tiskárna – všechny chyby + odmítnutý test (Q2) | 338 | fr | 20 |
| Tiskárna – chyba názvu + nejdelší výsledek testu (de „nicht erreichbar…“, fr „https…“) | 330 | fr/it | 28 |
| Tiskárna – hesla nepodporována + chyby + odmítnutý test | 355 | fr | **3** |
| Kamera – chyba adresy + výsledek | 177–194 | de | ≥ 164 |
| Kamera – dlouhá chyba testu / test běží | 211 / 194 | – | ≥ 147 |
| Kamera – náhled 320×180 + výsledek | 319 | všechny | 39 |
| Kamera – hláška o odstraněných údajích | 233 | de/es/fr/it | 125 |
| Okno kamery – kamera vypnutá + chyba velikosti | 327 | – | 31 |
| Překryv (i vypnutá kamera) | 345 | všechny | 13 |
| Obecné – s chybami | 353 | es | 47 |
| Pruh upozornění + Tiskárna (smí se posouvat) | 287 / 304 | es | 17 |

Bez Q1 (nápověda i chyba zároveň) se Tiskárna se všemi chybami **nevejde**: fr 395 (−37), es/it 381 (−23), cs/de/en/pl/sk 367 (−9). Dnešní chování testu (odmítnutý test opakuje dlouhou chybu adresy) přidá dalších až 44 (fr 439). Hláška o odstraněných údajích + náhled: 369–383 (−11 až −25).

### Klávesnice a přístupnost
- Pořadí Tab: hlavní záložky → seznam tiskáren → Přidat → Odebrat → záhlaví podzáložek → pole podzáložky v pořadí čtení (zleva doprava, shora dolů) → Uložit → Zrušit. Pozor: v XAML jsou spodní pruh a tlačítka pod seznamem deklarované dřív než obsah (`DockPanel.Dock`), takže je potřeba `TabIndex`/pořadí prvků upravit.
- Enter = Uložit (`IsDefault`), Esc = Zrušit (`IsCancel`) – beze změny. Šipky přepínají záložky na fokusovaném záhlaví. Zkratky pro přepínání → Q7.
- Viditelný fokus: výchozí Fluent focus adorner. Cíle kliknutí ≥ 32 DIP (zaškrtávací pole, tlačítka, záhlaví podzáložek 32).
- `AutomationProperties.AutomationId` pro nové prvky (`SettingsPrinterTabs`, `PrinterTabConnection`, `PrinterTabCamera`, `PrinterTabCameraWindow`, `PrinterTabOverlay`, …); `AutomationProperties.Name` záhlaví s chybou = „Tiskárna – obsahuje chyby“ (nový text), u obrázkového tlačítka a polí matice překryvu viz výše.
- Kontrast: chyby `#C42B1C` na bílé 5.9:1, tmavé téma `#FF99A4`; nápovědy `Opacity=0.7` ≈ 8:1 na bílé. Neaktivní záhlaví Fluent ≈ 5.7:1.

### Rizika jazyků (naměřeno, Inter 14 px / 12 px)
| Místo | nejdelší | šířka | limit |
|---|---|---|---|
| Přidat tiskárnu (tlačítko) | fr „Ajouter une imprimante“ | 154 + 22 | 184 |
| Připojení (combo) | sk „Prusa Connect (zatiaľ nie je k dispozícii)“ | 264 + 42 | 308 |
| Zaškrtávací pole kamery | fr | 480 + 28 | 548 |
| Záhlaví podzáložek celkem | it („Finestra della telecamera“ 166) | ≈ 520 | 548 – překladatelé: „Okno kamery“ ≤ 170 |
| Názvy rohů (combo) | es „Abajo a la izquierda“ | 129 + 42 | umístění ≈ 170, překryv 176 – limit textu ≤ 125 |
| Položky překryvu | es „Temperatura de la boquilla“ | 176 | sloupec Auto |
| Záhlaví „Normální okno a celá obrazovka“ | es | 236 | 2 řádky v 120 |
| Monitor (combo) | de „Bildschirm 2 (2560 × 1440) – nicht angeschlossen“ | 330 + 42 | ≈ 450 |
| Chyba adresy tiskárny | fr `SettingsErrorAddressInvalid` | 1140 (12 px) | 3 řádky v 548 |
| Chyba jména | de `SettingsErrorUserNameUnsupported` | 681 (12 px) | 3 řádky v 266 |
| Nápověda hesla (nepodporováno) | fr | 829 (12 px) | 4 řádky v 266 – nejtěsnější stav |
| Výsledek testu | de `SettingsTestNotReachable` | 841 | 3 řádky vedle tlačítka |

### Headless test „bez posuvníku“ (pro ui-dev)
- Okno ve výchozí velikosti (`Width/Height` z XAML, žádné ruční zvětšení), `SetRenderScaling(1.0)`, Inter. Pro každý jazyk × stav × záložka/podzáložka: přepnout `SelectedTabIndex` / `SelectedPrinterTabIndex`, `Settle()`, pak pro každý **viditelný vlastní** `ScrollViewer` (`TemplatedParent is null`, tj. ne vnitřní ScrollViewer `TextBox`/`ListBox`/`ComboBox`): `Extent.Height <= Viewport.Height` (při kratším obsahu se rovnají). Výjimka jen pro stav s pruhem upozornění.
- Doplňkově: všechny `TabItem` podzáložek mají stejné `Bounds.Y` (jedna řada); jednořádkové `TextBlock` nejsou oříznuté (změřená šířka textu ≤ `Bounds.Width`, jako v prototypu) – aspoň pro tlačítka a combo boxy.
- Stavy: PrusaLink s uloženým heslem; Prusa Connect; všechny chyby (nejdelší varianty hlášek); všechny chyby + odmítnutý test; chyba názvu + nejdelší výsledek testu spojení (úspěch i nejdelší chyba); hesla nepodporována (`PasswordPolicy.Unsupported`) + chyby; kamera: chyba adresy, test běží, nejdelší chyba testu, úspěch (+ náhled od T-034), hláška o odstraněných údajích; Okno kamery a Překryv (od T-035–T-037); Obecné s chybami. Velikost okna + titulek (32) ≤ 780×540 a omezení na menší pracovní plochu testovat jako čistou funkci.

### macOS (HIG) – rozdíly, řeší T-018
Pořadí tlačítek Zrušit | Uložit (výchozí vpravo); Nastavení se otevírá i přes ⌘,; HIG doporučuje panel nástrojů s ikonami místo záložek a okamžité ukládání – ponecháváme záložky a Uložit/Zrušit kvůli jednotnému chování. Systémové písmo SF Pro má podobnou šířku jako Inter, titulek ≈ 28 pt.

## Otevřené otázky
(žádné)

## Poznámky k implementaci
Stav 2026-10-19 (ui-dev): implementace hotová, všechna kritéria splněná v headless testech; úkol zůstává `in-progress` kvůli ruční kontrole na skutečném Windows (viz „Zbývá ručně“).

**Okno (`SettingsWindow.axaml`, `.axaml.cs`, nový `SettingsWindowPlacement.cs`)**
- Klientská oblast 780×508, `MinWidth/MinHeight` 640×400, `WindowStartupLocation=CenterScreen`. **Před zobrazením** (v konstruktoru okna) se velikost přizpůsobí pracovní ploše obrazovky, kterou vybere `CenterScreen` pro okno bez vlastníka (`Screens.ScreenFromPoint(Position)`, jinak `Primary`), s předpokládaným titulkem 32 DIP (`SettingsWindowPlacement.SizeBeforeShowing`); `CenterScreen` pak vycentruje už zmenšené okno, takže neposkočí. Po otevření (`OnOpened`) se jen opraví případ, kdy se okno se skutečně změřeným rámečkem (`FrameSize`: boky a spodek stejně, zbytek titulek) nebo na jiné obrazovce nevejde (`CorrectionAfterOpening`: jen zmenšení + nové vycentrování, nikdy zvětšení). Každý rozměr se zmenšuje zvlášť, ne pod minimum. Vše jsou čisté funkce `SettingsWindowPlacement` (testy bez obrazovky).
- Kořen je `Grid` (upozornění, „Načítám…“, záložky, Uložit/Zrušit) místo `DockPanel` – prvky jsou deklarované v pořadí čtení, takže **pořadí Tab** je: záhlaví záložek → seznam tiskáren → Přidat → Odebrat → záhlaví podzáložek → pole podzáložky → Uložit → Zrušit. Totéž pod seznamem (`Grid` místo `DockPanel`).
- Okraje `16,8,16,12`, hlavní záložky 18 px / `MinHeight` 36 / `Padding 12,0` (třída `main`), podzáložky 14 px / 32 / `10,0` (třída `sub`), seznam 184 DIP, Přidat/Odebrat pod sebou přes celou šířku (Q6).
- Podzáložky: vnořený `TabControl` (`SettingsPrinterTabs`) se `SelectedIndex` → `SettingsViewModel.SelectedPrinterTabIndex`. Obsah každé podzáložky je `ScrollViewer` (záchrana) s `ContentControl` nad `SelectedPrinter`, takže výběr podzáložky patří oknu a při přepnutí tiskárny zůstává. Záhlaví: „!“ (tučně, `SettingsErrorBrush`) + text; `AutomationProperties.Name` = „Tiskárna – obsahuje chyby“ (nový text `SettingsTabContainsErrors`).
- **Okno kamery a Překryv se zatím nezobrazují** (záhlaví vynechaná, žádné prázdné podzáložky); šířka záhlaví počítá se čtyřmi (ověřeno prototypem), T-035/T-036/T-037 je přidají a zvýší `SettingsViewModel.PrinterTabCount`.
- **Obrázek tiskárny (T-039) zatím není:** v seznamu se pro něj místo nerezervuje (jen „!“ a název), na podzáložce Tiskárna je Název širší (`*,16,320`). Sloupec Připojení má 320 místo navržených 308 – headless test našel o 1 DIP oříznuté „Prusa Connect (zatiaľ nie je k dispozícii)“ (sk).
- **Doplněno v T-039 (2026-10-20):** obrázek 24 v seznamu a tlačítko 56 × 56 s nabídkou na podzáložce Tiskárna (`56,12,*,16,320`, Připojení zůstává 320). Chyba importu obrázku je ve stavovém řádku, aby se podzáložka vešla bez posuvníku.
- Podzáložka Tiskárna: Název + Připojení v řádku (chyba názvu pod oběma), Adresa, Uživatelské jméno | Heslo ve dvou sloupcích, řádek testu (tlačítko, během testu Zrušit test + `ProgressBar` 48) s výsledkem vpravo (zalamuje se).
- Podzáložka Kamera: hláška o odstraněných údajích (12 px), zaškrtávací pole, adresa + nápověda/chyba, pod tím tlačítko testu a výsledek (bez náhledu – T-034). Dřívější nadpis sekce „Kamera“ odpadl (je v záhlaví podzáložky, které používá stávající text `SettingsCamera`).
- Záložka Obecné ve dvou sloupcích `*,24,*` podle návrhu.
- **Výsledky testů:** třída `result` už sama nebarví; úspěch = `SettingsSuccessBrush` (zelená), chyba = `SettingsErrorBrush`, probíhající / zrušený test běžnou barvou (UI test kontroluje skutečnou barvu `Foreground`).
- **Zkratky (Q7):** tunelovaný `KeyDown` okna: Ctrl+Tab / Ctrl+Shift+Tab → `SelectAdjacentTab`, Ctrl+PageDown / Ctrl+PageUp → `SelectAdjacentPrinterTab` (jen na záložce Tiskárny s vybranou tiskárnou); obojí cyklí dokola. Fokus se přesune na záhlaví vybrané (pod)záložky, aby nezůstal v právě skrytém obsahu.
- Nová `AutomationId`: `SettingsTabPrinters`, `SettingsTabGeneral`, `SettingsPrinterTabs`, `PrinterTabConnection`, `PrinterTabCamera`, `PrinterTabConnectionMarker`, `PrinterTabCameraMarker`, `PrinterAddressHint`, `PrinterCameraUrlHint`, `PrinterCancelTest`, `SettingsNotice`, `SettingsPollingInterval`, `SettingsPollingIntervalHint`, `SettingsPollingIntervalError`, `SettingsNotificationsEnabled`, `SettingsSound`, `SettingsRemainingTime`, `SettingsRemainingTimeError`. Vstupní pole mají `AutomationProperties.Name` podle svého popisku.

**View modely**
- `SettingsViewModel`: konstanty `PrintersTab`/`GeneralTab`, `ConnectionPrinterTab`/`CameraPrinterTab`/`PrinterTabCount`; `SelectedPrinterTabIndex` (hodnoty mimo rozsah, např. −1 z `TabControl`, ignoruje; nově totéž u `SelectedTabIndex`); uložení s chybou vybere první chybnou tiskárnu, záložku Tiskárny a podzáložku v pořadí Tiskárna → Kamera, jinak záložku Obecné (podzáložka zůstává); `ShowsPollingIntervalHint`; `SelectAdjacentTab` / `SelectAdjacentPrinterTab`.
- `PrinterEditorViewModel`: `HasConnectionErrors` / `HasCameraErrors`, `ShowsConnectionErrorMarker` / `ShowsCameraErrorMarker` (stejné pravidlo jako „!“ v seznamu), `ConnectionTabName` / `CameraTabName`, `ShowsAddressHint` / `ShowsUserNameHint` / `ShowsPasswordHint` / `ShowsCameraUrlHint` (Q1). Odmítnutý test spojení i kamery → `SettingsFixErrors` (Q2, výjimka viz Q10). Hláška o odstraněných údajích kamery zmizí po zapnutí kamery, úpravě adresy nebo úspěšném testu (Q3).
- Nové texty ve všech 8 jazycích: `SettingsPrinterTabConnection` (Printer / Tiskárna / Tlačiareň / Drucker / Impresora / Imprimante / Stampante / Drukarka) a `SettingsTabContainsErrors` („{0} – obsahuje chyby“ …). Záhlaví Kamera používá stávající `SettingsCamera`.

**Testy**
- App.Tests: `Settings/SettingsLayoutTests.cs` (podzáložky, značky, navigace po uložení, zkratky, chyba místo nápovědy, texty a styly výsledků testů, hláška kamery), `Windows/SettingsWindowPlacementTests.cs` (velikost + titulek ≤ 780×540, omezení a centrování na menší pracovní plochu, škálování, rámeček). `FakeCameraTester` přesunutý do sdíleného `SettingsFakes.cs`. Kvůli Q2/Q3 upravené stávající testy (`SettingsViewModelTests`, `CameraSettingsTests`, `SettingsLanguageTests`, UI `SettingsWindowTests`).
- UiTests: `Settings/SettingsWindowLayoutTests.cs` – „bez posuvníku“ pro 8 jazyků × 13 stavů × 3 pohledy (Tiskárna, Kamera, Obecné): každý viditelný vlastní `ScrollViewer` (`TemplatedParent == null`) má `Extent ≤ Viewport` (výška i šířka), jednořádkové texty nejsou oříznuté (změřená šířka ≤ `Bounds.Width`), záhlaví (pod)záložek v jedné řadě; kontrolní test, že tyto kontroly na malém okně opravdu selžou; velikost okna; podzáložka zůstává při změně tiskárny i záložky; uložení s chybou kamery otevře Kameru se značkou; chyba místo nápovědy; barvy výsledků; pořadí Tab (Tiskárna, Kamera, Obecné); zkratky; přepnutí jazyka záhlaví. `SettingsWindowCameraUiTests` otevírá podzáložku Kamera kliknutím (bez zvětšeného okna); nový test, že zapnutí kamery skryje hlášku.
- Snímky (vizuálně zkontrolované): přegenerované `settings-prusalink`, `settings-prusaconnect`, `settings-validation`, `settings-validation-cs`; nové `settings-camera`, `settings-camera-notice`, `settings-general`, `settings-general-errors`, `settings-errors-fr`.
- Naměřené rezervy (Inter, headless; plocha podzáložky 358, Obecné 400): nejtěsněji fr „hesla nepodporována + všechny chyby + odmítnutý test“ 5 DIP; fr/es/it „chyba názvu + nejdelší chyba testu spojení“ 13; fr „všechny chyby + odmítnutý test“ 22; Kamera nejvýš 275 (hláška + nejdelší chyba testu, de/es/fr/it); Obecné s chybami 351 (es).
- `dotnet build` 0 varování; `dotnet test` (2580 testů) zeleně. V jednom z plných běhů vypršel nesouvisející `WebProxyLifecycleTests.RequestsPostponeTheIdleStop` (PrusaLink, mimo tento úkol; samostatně 3× a v dalším plném běhu prošel). `dotnet publish -c Release -r win-x64` (AOT) bez varování. Aplikace (`dotnet run`) se spustí a běží; okno Nastavení z nabídky v oznamovací oblasti jsem ručně neotevíral.

**Zbývá ručně (uživatel)**
- Otevřít Nastavení na skutečném Windows při 100 %, 125 % a 150 % (Segoe UI místo Inter z testů, skutečný titulek a rámeček): okno bez posuvníku, na malé pracovní ploše zmenšené a vycentrované, zkratky a pořadí Tab.
- Malá pracovní plocha (např. 800×600 se škálováním 125 % nebo vysoký hlavní panel): okno se při otevření **objeví rovnou zmenšené a vycentrované, bez viditelného poskočení**; u více monitorů ověřit, že se otevře na očekávané obrazovce.
- macOS (T-018) neověřeno: pořadí Zrušit/Uložit, ⌘ místo Ctrl, chování `FrameSize` a pracovní plochy.
