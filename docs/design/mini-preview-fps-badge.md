# Štítek fps v malém náhledu – návrh UI (T-251)

Malý štítek se **skutečnou a nastavenou snímkovou frekvencí** v levém horním rohu obrazu malého náhledu (rozhodnutí
uživatele 2026-11-22: obsah, umístění, volitelnost, výchozí zapnuto). Navazuje na omezení fps malého náhledu (T-185, T-249).
Platí jen pro malý náhled; normální okno a celá obrazovka se nemění.

## 1. Rozměry, ze kterých návrh vychází (změřeno na snímcích `camera-mini-*-buttons.png`)

| Prvek | Hodnota (DIP) |
|---|---|
| Nejužší malý náhled | 240 × 135 obraz + informační pruh (`CameraWindowSize.MinMiniWidth`) |
| Úchyty pro změnu velikosti | 6 od každého okraje (průhledné `Border.grip`, nad obrazem) |
| Tlačítka okna | `Margin="6"`, `Spacing="4"`, tlačítko 30 × 30 (ikona 16 + `Padding` 6 + rámeček 1), TL aktivní ≈ 49–51, REC aktivní ≈ 59 |
| Levý okraj panelu tlačítek při 240 DIP | jen celá obrazovka, normální okno, pin: x = 136; + REC (nečinné): x = 102; + TL + REC (nečinné): x = 68; TL + REC aktivní: x ≈ 17 |
| Levý okraj tlačítek viditelných **bez najetí** (připínáček zapnutý, REC, TL aktivní) | jen připínáček: 204; REC + připínáček: ≈ 141; TL + REC + připínáček: ≈ 85 |

Skrytá tlačítka mají jen `Opacity=0`, místo v rozvržení zabírají pořád – proto je při najetí myší vidět celý panel.

## 2. Wireframe (240 DIP, poměr 1:1 nepřesný)

Klid (myš mimo okno), připínáček zapnutý ([FS] = celá obrazovka, [Win] = normální okno):
```
┌──────────────────────────────────────────┐
│ ┌────────────┐                    [Pin] │  ← štítek: x 8, svisle na středu řady tlačítek (y 6–36)
│ │14 / 15 fps │                          │
│ └────────────┘                          │
│                                          │
│              (obraz kamery)              │
│                                          │
├──────────────────────────────────────────┤
│ ● Tiskne… 42 %                 [3D] [Graf] │  ← informační pruh (beze změny)
└──────────────────────────────────────────┘
```

Najetí myší, tlačítka se vejdou (bez TL/REC):
```
┌──────────────────────────────────────────┐
│ ┌────────────┐          [FS] [Win] [Pin]   │
│ │14 / 15 fps │                          │
│ └────────────┘                          │
```

Najetí myší, tlačítka by štítek překryla (TL + REC při 240 DIP):
```
┌──────────────────────────────────────────┐
│  [FS] [Win] [(o) TL] [● REC] [Pin]          │  ← štítek plynule zmizí (0,15 s), vrátí se po odjetí myši
│                                          │
```

Plynulý profil (bez omezení): `24 fps / bez limitu` – jinak stejné.

## 3. Vzhled

| Vlastnost | Hodnota |
|---|---|
| Prvek | `Border Classes="overlay fpsBadge"` (dědí `Background #A6000000`, `CornerRadius 4`, `IsHitTestVisible=False`) s jedním `TextBlock` |
| Umístění | v řádku 0 (obraz) `HorizontalAlignment="Left"`, `VerticalAlignment="Top"`; obal `Panel Margin="8,6,0,0" Height="30"` (výška řady tlačítek) a štítek v něm `VerticalAlignment="Center"` → střed štítku na středu tlačítek, horní okraj ≈ 12 DIP, levý 8 DIP (2 DIP mimo úchyty) |
| `Padding` | `5,2` (implementace T-251: s `6,2` chyběl při 240 DIP s TL + REC + připínáčkem v klidu 1 DIP do mezery 8 DIP) |
| Text | `FontSize="11"` (jako „REC“/„TL“), `FontWeight="SemiBold"`, `Foreground="White"`, `Opacity` 1, `FontFeatures="tnum"` (jako karty překryvu), **`TextWrapping="NoWrap"`** – přepisuje `Wrap` ze stylu `Border.overlay TextBlock` |
| Výška | ≈ 18 DIP (řádek 11 px + 2 × 2) |
| Šířka | podle textu, ukotveno vlevo (roste doprava); žádné `MinWidth` ani zkracování (`TextTrimming` zakázané) |
| Stabilní šířka | skutečná hodnota se formátuje na **nejméně 2 číslice**, chybějící zleva doplní `U+2007` (figure space, šířka tabulkové číslice): „ 9 / 10 fps“ i „10 / 10 fps“ mají stejnou šířku. Limit (1–30) se mění jen v nastavení, nepadduje se. `tnum` + `U+2007` platí i pro Segoe UI / Inter (číslice tam jsou tabulkové už ve výchozím stavu). |
| Kontrast | bílá na `#A6000000` přes nejsvětlejší (bílý) obraz = bílá na RGB ≈ 89 → ≈ 7 : 1 (WCAG AA i pro malý text) |
| ZIndex / pořadí | v XAML hned **za** `OverlayCards` a **před** stavem připojení a `WindowButtons` (ZIndex 0): pod tlačítky, pod úchyty (které stejně nejsou zasaženy – štítek nebere ukazatel), pod `MiniOutline` (ZIndex 2) a pod režimem rozvržení |
| Animace | `DoubleTransition Opacity 0:0:0.15` (stejně jako tlačítka); skrytí kvůli místu = `Opacity 0`, skrytí kvůli stavu (vypnuto, bez obrazu, jiný režim) = `IsVisible False` |
| High-DPI | 11 DIP = 16,5 px při 150 %, 22 px při 200 %; zaokrouhlování rozvržení zapnuté (výchozí) |

### Kdy je štítek vidět

`IsVisible` = malý náhled **a** nastavení zapnuto **a** běží obraz (`!IsStatusVisible` a už přišel snímek) **a** není zapnutý
režim rozvržení. Při připojování, chybě, „bez signálu“ zmizí okamžitě (bez animace), aby neukazoval zastaralé číslo.

### Kolize s tlačítky (rozhoduje rozvržení, ne pevná šířka okna)

Mezera mezi štítkem a tlačítky je nejméně **8 DIP**. Pravá hrana štítku = 8 + jeho šířka.

1. **Klid** (myš mimo okno, žádné tlačítko nemá fokus): porovnává se s levou hranou **nejlevějšího tlačítka viditelného
   v klidu** (zapnutý připínáček, REC při nahrávání, TL při timelapse); není-li žádné, s pravým okrajem − 6.
2. **Najetí myší / fokus v tlačítkách** (`Grid#Root:pointerover`, `StackPanel.windowButtons:focus-within`): porovnává se
   s levou hranou **celého panelu tlačítek**.
3. Nevejde-li se štítek celý, dostane `Opacity 0` (viz otázka 1). Text se nikdy nezkracuje ani nezalamuje.

Odhad při 240 DIP (Inter 11 SemiBold; přesné hodnoty změří headless test): „14 / 15 fps“ ≈ 68 DIP (pravá hrana 76),
„24 fps / senza limite“ (nejdelší) ≈ 120 DIP (pravá hrana 128).

| Stav při 240 DIP | Klid | Najetí myší |
|---|---|---|
| bez TL/REC tlačítek | vejde se vše | vejde se „x / y fps“ i nejdelší „bez limitu“ jen těsně (hranice 128 DIP) |
| REC tlačítko nečinné | vejde se vše | „x / y fps“ ano (do 94), „bez limitu“ ne → zmizí |
| TL + REC nečinné | vejde se vše | nic → zmizí |
| TL + REC aktivní | „x / y fps“ těsně (do 77), pl „kl./s“ a „bez limitu“ ne → zmizí | nic → zmizí |

Implementace: pravidlo spočítá code-behind okna po rozvržení (`LayoutUpdated` / změna `Bounds` panelu tlačítek, stav
najetí a fokusu) a nastaví třídu `Classes.cramped` na štítku; styl `Border.fpsBadge.cramped` → `Opacity 0`. Žádný výpočet
na snímek.

### Přístupnost

- `AutomationProperties.Name` = celá věta (bez `U+2007`, čísla bez doplnění), viz texty níže; **žádný**
  `LiveSetting` – čtečka ho přečte jen při procházení.
- `AutomationProperties.AutomationId="CameraMiniFrameRateBadge"`.
- Informace není jen barvou; štítek není ovládací prvek (žádná tab zastávka).
- Obnovení textu nejvýš 1× za sekundu (rozsah úkolu); změna jen při jiné zaokrouhlené hodnotě.

## 4. Nastavení > Kamera

Hned pod popisem profilu (`MiniFrameRateDescription`), před sekcí „Hardwarové dekódování“; bez vlastního nadpisu, patří
k sekci „Snímková frekvence malého náhledu“:

```
Snímková frekvence malého náhledu
[Vyvážený ▾]  ───────●──────────────            15 fps
15 fps (doporučeno): plynulý pohyb v malém obrazu …
[✓] Zobrazovat snímkovou frekvenci v malém náhledu          ← CheckBox, Margin 0,8,0,0
    Skutečná a nastavená hodnota v levém horním rohu …      ← TextBlock.hint, Margin 28,0,0,0
Hardwarové dekódování
```

- `CheckBox AutomationProperties.AutomationId="MiniFrameRateBadge"`, `AutomationProperties.HelpText` = text nápovědy.
- Nápověda: `TextBlock Classes="hint" Margin="28,0,0,0"` (stejný vzor jako `PrinterCameraWindowRememberHint`).
- Místo: záložka má volných ≈ 200 DIP (snímek `settings-camera-tab.png`), přibude ≈ 50 DIP – posouvat se nic nebude ve
  všech 8 jazycích (ověří test rozsahu `ScrollViewer`).

## 5. Texty (všechny jazyky)

`{0}` = skutečné fps (ve štítku řetězec s doplněním `U+2007`, ve jméně pro čtečky celé číslo), `{1}` = limit.
**`⍽` v tabulce = U+00A0 (NBSP)**; ostatní mezery normální. Jednotky jako v `SettingsMiniFrameRateValue`
(fr „i/s“, pl „kl./s“, jinak „fps“). Ve fr a pl obsahuje jednotka lomítko, proto má varianta bez limitu místo
„ / “ čárku (jinak by vzniklo „24 i/s / sans limite“).

Pro „bez limitu“ je zvolen krátký tvar (štítek má málo místa); v nastavení dál zůstává „Bez omezení“ / „Keine Begrenzung“
apod. – jde o stejný význam, jen kratší.

| Klíč | en | cs | sk | de |
|---|---|---|---|---|
| `CameraMiniFrameRateBadge` | `{0} / {1}⍽fps` | `{0} / {1}⍽fps` | `{0} / {1}⍽fps` | `{0} / {1}⍽fps` |
| `CameraMiniFrameRateBadgeNoLimit` | `{0}⍽fps / no limit` | `{0}⍽fps / bez limitu` | `{0}⍽fps / bez limitu` | `{0}⍽fps / ohne Limit` |
| `CameraMiniFrameRateBadgeName` | `Frame rate {0} of {1} fps` | `Snímková frekvence {0} z {1} fps` | `Snímková frekvencia {0} z {1} fps` | `Bildrate {0} von {1} fps` |
| `CameraMiniFrameRateBadgeNameNoLimit` | `Frame rate {0} fps, no limit` | `Snímková frekvence {0} fps, bez limitu` | `Snímková frekvencia {0} fps, bez limitu` | `Bildrate {0} fps, ohne Limit` |
| `SettingsMiniFrameRateBadge` | `Show the frame rate in the mini preview` | `Zobrazovat snímkovou frekvenci v malém náhledu` | `Zobrazovať snímkovú frekvenciu v malom náhľade` | `Bildrate in der Mini-Vorschau anzeigen` |
| `SettingsMiniFrameRateBadgeHint` | `Actual and set value in the top left corner of the picture while it streams.` | `Skutečná a nastavená hodnota v levém horním rohu obrazu, když běží obraz.` | `Skutočná a nastavená hodnota v ľavom hornom rohu obrazu, keď beží obraz.` | `Tatsächlicher und eingestellter Wert oben links im Bild, solange es läuft.` |

| Klíč | es | fr | it | pl |
|---|---|---|---|---|
| `CameraMiniFrameRateBadge` | `{0} / {1}⍽fps` | `{0} / {1}⍽i/s` | `{0} / {1}⍽fps` | `{0} / {1}⍽kl./s` |
| `CameraMiniFrameRateBadgeNoLimit` | `{0}⍽fps / sin límite` | `{0}⍽i/s, sans limite` | `{0}⍽fps / senza limite` | `{0}⍽kl./s, bez limitu` |
| `CameraMiniFrameRateBadgeName` | `Frecuencia de fotogramas {0} de {1} fps` | `Fréquence d'images {0} sur {1} i/s` | `Frequenza fotogrammi {0} su {1} fps` | `Liczba klatek {0} z {1} kl./s` |
| `CameraMiniFrameRateBadgeNameNoLimit` | `Frecuencia de fotogramas {0} fps, sin límite` | `Fréquence d'images {0} i/s, sans limite` | `Frequenza fotogrammi {0} fps, senza limite` | `Liczba klatek {0} kl./s, bez limitu` |
| `SettingsMiniFrameRateBadge` | `Mostrar los fotogramas por segundo en la vista mini` | `Afficher la fréquence d'images dans le mini-aperçu` | `Mostra la frequenza fotogrammi nell'anteprima mini` | `Pokazuj liczbę klatek w małym podglądzie` |
| `SettingsMiniFrameRateBadgeHint` | `Valor real y configurado en la esquina superior izquierda de la imagen mientras se transmite.` | `Valeur réelle et réglée dans le coin supérieur gauche de l'image pendant la diffusion.` | `Valore reale e impostato nell'angolo in alto a sinistra dell'immagine durante lo streaming.` | `Rzeczywista i ustawiona wartość w lewym górnym rogu obrazu, gdy obraz jest przesyłany.` |

Jména pro čtečky mají mezi číslem a jednotkou také NBSP (stejně jako `SettingsMiniFrameRateValue`). Ve francouzštině
nejsou v textech dvojtečky ani středníky, NBSP před nimi tedy není potřeba.

## 6. Rizika podle jazyka

- Nejdelší štítek: it „24 fps / senza limite“, fr „24 i/s, sans limite“, es „24 fps / sin límite“ (≈ 110–120 DIP).
  Nejdelší s limitem: pl „14 / 15 kl./s“ (≈ 76 DIP).
- Popisek zaškrtávacího pole: es (≈ 50 znaků) se vejde na jeden řádek i v okně 780 DIP.
- Fonty: Windows Segoe UI, macOS přibalený Inter (T-223) – stejné písmo jako headless snímky; obě mají tabulkové
  číslice a `U+2007`. Segoe UI je užší (text „‒ / 15 fps“ ≈ 51 DIP proti ≈ 58 DIP s Inter).

## 7. Testy (pro implementaci)

- Headless: malý náhled 240 DIP ve všech 8 jazycích, s limitem i bez – štítek je celý v obrazu, ≥ 8 DIP od úchytů
  (levá hrana ≥ 8, horní ≥ 6), nepřekrývá žádné tlačítko viditelné v daném stavu (klid / najetí; TL + REC aktivní).
- Šířka štítku se nezmění mezi „9“ a „10“ skutečných fps.
- Klik a tažení na štítku pohne oknem (`IsHitTestVisible=False`).
- Snímky: `camera-mini-fps-badge` (klid, 240 DIP); stav při najetí s TL + REC pokrývají stávající
  `camera-mini-recording-buttons` / `camera-mini-timelapse-buttons` (štítek skrytý, beze změny), záložku Kamera
  `settings-camera-tab`.

## Odkazy na zásady

- Windows: typografie Fluent (Caption 12 px je nejmenší doporučený text; 11 DIP je kompromis shodný s „REC“/„TL“),
  mřížka 4/8 DIP, popisky zaškrtávacích polí větou bez tečky.
- macOS HIG: překryvy nad médii s průsvitným tmavým pozadím, nezasahovat do oblasti pro změnu velikosti okna.
- WCAG 2.2 AA 1.4.3 (kontrast), 4.1.3 (stavová hlášení – záměrně **ne** živá oblast, viz zadání).

## Rozhodnutí uživatele (2026-11-22)

1. **Štítek se nevejde vedle tlačítek** (hlavně najetí myší při 240 DIP, nebo TL + REC aktivní): varianta **A** – štítek
   plynule zmizí (`Opacity 0`, 0,15 s) a po odjetí myši / uvolnění místa se vrátí. Text se nezkracuje.
2. **Začátek obrazu:** štítek se ukáže hned s prvním snímkem jako „‒ / 15 fps“ (místo skutečné hodnoty pomlčka – v
   implementaci „figure dash“ U+2012, široká jako číslice, doplněná `U+2007` na šířku dvou číslic) a skutečnou hodnotu
   doplní první obnovení po 1 s. Čtečka obrazovky do té doby dostane „Frame rate: measuring, limit 15 fps“ / „…,
   no limit“ (klíče `CameraMiniFrameRateBadgeNameMeasuring` a `…MeasuringNoLimit`, doplněno po code review).
