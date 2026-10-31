---
id: T-118
title: Malý náhled kamery – tenký rámeček okna
status: todo
owner: ui-dev
priority: P3
platform: all
depends_on: [T-089, T-090, T-117]
---

## Cíl
Malý náhled kamery je okno bez rámečku (jen obraz a pás pod ním, neviditelné úchopy pro změnu velikosti po okrajích,
T-089 / T-090). Tmavý obraz na tmavé ploše nebo nad tmavým oknem splyne, není vidět, kde náhled končí a kde ho chytit.
Uživatel si přeje rámeček (2026-10-31). Implementace na Windows i macOS.

## Rozsah (rozhodnuto 2026-10-31)
1. **Tenký neutrální obrys** kolem celého malého náhledu (obraz i pás), poloprůhledný světle šedý ve stylu obrysu oken
   Windows 11 / macOS, čitelný na světlém i tmavém pozadí. Přesnou barvu, tloušťku a vykreslení (uvnitř okna,
   nezmenšit obraz ani úchopy) navrhne `ui-designer`.
2. **Při najetí myší výraznější**, aby bylo jasné, že okraj jde táhnout (s přechodem jako u tlačítek okna, 0,15 s).
3. **Bez barvy podle stavu tiskárny** (rozhodnutí uživatele: jen neutrální obrys; stav dál v pásu).
4. **Bez stínu a zaoblených rohů** (průhlednost bezrámečkového okna v Avalonii na obou OS, výkon u videa).
5. Normální okno a celá obrazovka beze změny.

## Akceptační kritéria
- [ ] Obrys kolem malého náhledu v obou OS, jen v režimu malého náhledu (headless UI testy: viditelnost podle režimu)
- [ ] Při najetí výraznější, po odchodu zpět; úchopy pro změnu velikosti fungují jako dřív (UI testy)
- [ ] Obraz ani pás se nezmenší víc, než návrh určí; rozměry a umístění náhledu (`CameraMiniGeometry`) beze změny
- [ ] Vysoký kontrast Windows: obrys čitelný (systémová barva nebo zdůvodněná výjimka v návrhu)
- [ ] Snímky UI: malý náhled na světlém a tmavém pozadí (Windows → T-114, macOS → T-115)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Návrh vzhledu
(ui-designer 2026-10-31; podklad: `CameraWindow.axaml` (Root, Surface, pás v řádku 1 se `ZIndex 1`, úchopy
`Border.grip` 6 DIP), `CameraWindow.Mini.cs`, `CameraMiniGeometry.cs`, `CameraMiniStrip.axaml` (pás `#141416`,
nahoře 3 DIP čára stavu), návrh tlačítek okna v T-117. Obsah okna je ve všech režimech a tématech černý.)

### 1. Princip
Obrys je **překryvný `Border` uvnitř okna** přes celý `Root` (obraz i pás, `Grid.RowSpan="2"`), jen
`BorderThickness`, bez pozadí, `IsHitTestVisible="False"`. Nemění rozložení: obraz, pás, úchopy ani
`CameraMiniGeometry` (velikost a umístění okna) se nemění, obrys jen překreslí krajní pixel obsahu
(1 px při 100/125 %, 2 px při 150–200 %). Viditelný jen v malém náhledu (`IsVisible="{Binding IsMini}"`),
normální okno a celá obrazovka beze změny.

```
Malý náhled bez myši                    Malý náhled, myš nad oknem (i nad úchopem)
┌──────────────────────────┐ ← 1 DIP     ┏━━━━━━━━━━━━━━━━━━━━━━━━━━┓ ← 1 DIP, světlejší
│                          │   #73FFFFFF ┃        [⛶][▭][📌] ←6→  ┃   #BFFFFFFF
│        obraz kamery      │             ┃        obraz kamery      ┃
│                          │             ┃                          ┃
├──────────────────────────┤             ┣━━ čára stavu 3 DIP ━━━━━━┫
│ pás (název, průběh …)    │             ┃ pás (název, průběh …)    ┃
└──────────────────────────┘             ┗━━━━━━━━━━━━━━━━━━━━━━━━━━┛
 úchopy 6 DIP po obvodu (neviditelné, beze změny) leží pod obrysem i přes něj – obrys nepřijímá myš
```

### 2. Barva a tloušťka
| Stav | `BorderBrush` | Výsledná barva nad černým obsahem | Kontrast vůči černé / #202020 / bílé za oknem |
|---|---|---|---|
| normální | `#73FFFFFF` (bílá 45 %) | `#737373` | 4,4 : 1 / 3,4 : 1 / 4,8 : 1 |
| najetí myší | `#BFFFFFFF` (bílá 75 %) | `#BFBFBF` | 11,4 : 1 / 8,9 : 1 / 1,8 : 1 |

- **Proč poloprůhledná bílá:** okno není průhledné, obrys se míchá jen s vlastním (černým) obsahem, takže výsledek
  je pevná šedá; jen kde obraz kamery sahá až k okraji, prosvítá jeho krajní pixel (obrys tam lehce zesvětlí, okraj
  zůstane čitelný). Splňuje rozsah „poloprůhledný světle šedý“.
- **Kontrast (WCAG 1.4.11, grafické prvky ≥ 3 : 1):** normální šedá je zvolená tak, aby splnila 3 : 1 zároveň vůči
  černé, tmavému oknu Windows 11 (`#202020`) i bílé ploše. Při najetí je zesvětlená pro tmavé okolí (to je případ,
  kvůli kterému úkol vznikl); vůči bílé ploše vyjde jen 1,8 : 1, ale tam okraj okna jednoznačně vymezuje sám černý
  obsah (21 : 1), takže informace „kde okno končí“ se neztrácí. Stav nenese barva, jen jas (bez barvy stavu, bod 3).
- **Tloušťka 1 DIP při normálu i najetí** – mění se jen barva, nic neposkakuje.
- **Ostrost při 125/150/200 %:** `Border` zaokrouhluje tloušťku na celé pixely (`UseLayoutRounding`, výchozí zapnuto):
  100 % → 1 px, 125 % → 1 px (0,8 DIP), 150 % → 2 px, 175 % → 2 px, 200 % → 2 px. Okno má celopixelovou velikost
  a obrys sedí na jeho okraji, takže čára je ostrá, bez rozmazaných půlpixelů. `UseLayoutRounding` nevypínat.
  Ověřit na skutečném Windows při 125 % a 150 % (→ T-114).

### 3. Pořadí vrstev a myš
- V XAML **za mřížku úchopů**, `ZIndex="2"` (nad pásem, který má `ZIndex 1`, a nad obrazem). Protože
  `IsHitTestVisible="False"`, nic nezakrývá pro myš – úchopy, tažení obrazu i pásu a tlačítka pásu fungují jako dřív.
  Tlačítka okna (okraj 6 DIP) se s obrysem nepřekrývají.
- **Najetí:** `Grid#Root:pointerover` – stejný spouštěč jako tlačítka okna (T-117), platí nad obrazem, pásem
  i úchopy (všechno jsou potomci `Root`). Obrys a tlačítka se tedy rozsvěcují současně.
- **Přechod:** `BrushTransition` na `BorderBrush`, 0,15 s (stejná délka jako `DoubleTransition` tlačítek).
- Během systémové smyčky změny velikosti na Windows (`BeginResizeDrag`) může Avalonia hlásit odchod myši; obrys pak
  ztmavne až do puštění – přijatelné, netestovat jako chybu.
- Obrys je dekorace: bez `AutomationId`, testy ho najdou podle `x:Name` (`MiniOutline`).

### 4. Téma, vysoký kontrast, macOS
- **Světlé / tmavé téma OS:** barvy jsou pevné (ne `DynamicResource` tématu), protože obsah okna je vždy černý
  (stejné rozhodnutí jako tlačítka okna, T-117 Q-D2).
- **Vysoký kontrast Windows – zdůvodněná výjimka:** aplikace nikde nesleduje systémové barvy vysokého kontrastu
  a obsah okna zůstává černý i v nich; systémová barva rámečku (např. bílá v motivu „Poušť“) by vedle černého obsahu
  nemusela být čitelná. Pevná šedá má vůči černému okolí (motivy „Akvatický“, „Soumrak“, „Noční obloha“) ≥ 4,4 : 1
  a vůči světlému okolí okraj vymezuje černý obsah. Případné zesílení ve vysokém kontrastu je otevřená otázka Q1.
- **macOS:** nativní okna mají tenký tmavý vnější obrys a v tmavém režimu jemnou světlou vnitřní linku (vlasová
  čára, 1 px na Retině) a zaoblené rohy. Náš obrys je 1 pt = 2 px na Retině, ostré rohy (rozsah bod 4) – o něco
  výraznější než nativní, záměrně kvůli čitelnosti malého náhledu nad tmavými okny. Ověřit vzhled na Macu (→ T-115).

### 5. Zadání pro ui-dev (jen rozložení a styly v `CameraWindow.axaml`)
Do `<Window.Styles>` (za styl `Border.grip`):
```xml
<!-- Thin neutral outline of the borderless mini preview (T-118): fixed colours, the content is always black. -->
<Style Selector="Border#MiniOutline">
  <Setter Property="BorderBrush" Value="#73FFFFFF" />
  <Setter Property="Transitions">
    <Transitions>
      <BrushTransition Property="BorderBrush" Duration="0:0:0.15" />
    </Transitions>
  </Setter>
</Style>
<Style Selector="Grid#Root:pointerover Border#MiniOutline">
  <Setter Property="BorderBrush" Value="#BFFFFFFF" />
</Style>
```
Do `Root` hned za mřížku úchopů (před `lay:CameraLayoutOverlay`):
```xml
<!-- Outline of the mini preview (T-118): over the image and the strip, not a pointer target, no layout change. -->
<Border x:Name="MiniOutline"
        Grid.RowSpan="2"
        ZIndex="2"
        BorderThickness="1"
        IsHitTestVisible="False"
        IsVisible="{Binding IsMini}" />
```
Testy: viditelnost podle režimu (normální / malý náhled / celá obrazovka), `BorderBrush` normál vs. po pohybu myši
nad obraz a nad úchop (po doběhnutí přechodu), úchopy dál přijímají stisk (hit test na okraji vrací `Border.grip`),
`ClientSize` a geometrie malého náhledu beze změny; snímek `camera-mini-outline.png` (+ varianta s najetím)
zkontrolovat. Snímek pozadí za oknem headless nevznikne – světlá a tmavá plocha → T-114 / T-115.

## Otevřené otázky
- Rozsah rozhodnut 2026-10-31: neutrální obrys, výraznější při najetí, bez barvy podle stavu, bez stínu.
- Q1 → **rozhodnuto 2026-10-31: (a)** bez zvláštního chování ve vysokém kontrastu, pevné barvy podle návrhu.
- **Q1 – vysoký kontrast Windows:** (a) bez zvláštního chování, pevné barvy podle návrhu (zdůvodněná výjimka);
  (b) při `PlatformSettings.GetColorValues().ContrastPreference == High` přepnout obrys na plnou bílou `#FFFFFF`
  (malá třída na `Root`, sledovat změnu nastavení); (c) systémová barva rámečku okna. **Doporučení: (a)** – kontrast
  už splňuje WCAG vůči tmavému okolí, (b) přidává kód a test jen pro jeden prvek, zatímco zbytek okna vysoký kontrast
  také nesleduje; (b) zvážit až v samostatném úkolu pro celé okno kamery. (c) nedoporučuji (viz 4.).

## Poznámky k implementaci
(vyplní agent po dokončení)
