---
id: T-076
title: Widgety překryvu okna kamery – návrh UI (design spec)
status: in-progress
owner: ui-designer
priority: P2
platform: all
depends_on: []
---

## Cíl
Podrobný návrh nového překryvu okna kamery složeného z **widgetů**. Nahradí jeden `Border` s řádky z T-036. Návrh je podklad pro úkoly T-077 až T-093. Uživatel ho **musí schválit před začátkem implementace UI** (stejně jako u T-033).

## Kontext
- Uživatel schválil směr podle makety: https://claude.ai/artifact/CJpK1mM9XsWXWT9DpCzrF1. Je to soukromé plátno uživatele, agenti ho nemusí umět otevřít. Schválené jsou varianty **„A“ + „C“ + malý náhled s grafem + další widgety**. Varianta **„B“ (panel / šuplík) je zamítnutá** – neplánovat ani nenavrhovat.
- Dnešní stav:
  - `src/LayerPeek.App/Windows/CameraWindow.axaml` (`PrintOverlay`, ~ř. 121), `App/Cameras/CameraViewModel.cs`, `App/Cameras/CameraPrintContent.cs`.
  - Nastavení `Core/Cameras/CameraOverlayOptions.cs`: položky `CameraOverlayItems`, roh `CameraOverlayCorner`, zvlášť normální okno + celá obrazovka a malý náhled. V nastavení je podzáložka Překryv (T-036).
  - Tlačítka okna jsou vpravo nahoře (`CameraWindowButtons`, 6 + 30 + 4 = 40 DIP).
- Graf teplot z diagnostiky: `App/Diagnostics/Chart/TemperatureChart`. Cíle kreslí čárkovaně, čáry přerušuje při výpadku dat.

### Popis schváleného návrhu slovy (pro agenty bez přístupu k maketě)
- **Varianta A – karty v kotvách:**
  - Každá skupina údajů je samostatná tmavá poloprůhledná karta: pozadí ≈ `rgba(14,14,16,0.72)`, poloměr rohů 10, světlý text.
  - Čísla jsou tabulková (`tabular-nums`). Místo textových popisků jsou ikony. Akcentová barva je oranžová `#FA6831`.
  - Karty se umisťují do jedné ze **6 kotev**: vlevo nahoře, nahoře uprostřed, vpravo nahoře, vlevo dole, dole uprostřed, vpravo dole. Volné umístění na pixely není.
  - Víc karet v jedné kotvě se skládá pod sebe (v dolních kotvách nad sebe).
- **Varianta C – režim rozložení:**
  - V okně kamery se ztlumí obraz a 6 kotev se ukáže jako čárkované zóny.
  - Widget se přetáhne do kotvy. Cílová zóna se zvýrazní a widget se do ní přichytí.
  - Klik na widget otevře popover s volbou „zvýraznit“ a velikostí S/M/L.
  - Nástrojová lišta nabízí „Obnovit výchozí“ a „Hotovo“.
- **Widgety v1:**
  - **Stav:** název tiskárny, „pilulka“ stavu s barevnou tečkou, velké procento průběhu, pruh průběhu.
  - **Časy:** „Zbývá“ jako velké číslo, čas dokončení a doba tisku s ikonami.
  - **Teploty + sparkline:** tryska a podložka (aktuální / cílová) a kompaktní graf posledních 60 minut. Graf nemá osy, cíle jsou čárkovaně, tryska oranžová, podložka modrá.
  - **Název tisku („hezčí název souboru“):** např. `foldable_book_holder_v1_0.4n_0.2mm_PLA_COREONE_2h51m.bgcode` → „Foldable book holder v1“ + čipy (materiál, výška vrstvy, tryska). Velikosti:
    - S: čip,
    - M: náhled 40 px + název + čipy,
    - L: náhled 72 px + název + čipy + odhad sliceru.
  - **Vychladnutí:** objeví se po dokončení nebo zastavení tisku a má dva stavy:
    - „Chladne…“: teploty, průběh k prahu, odhad času,
    - „Vychladlo – můžeš vypnout“.

    Rozhoduje jen tryska (výchozí práh 50 °C), podložka se jen zobrazuje.
- **Malý náhled (platí podle doplnění uživatele 2026-10-24):**
  - Přes obraz malého náhledu **nejsou žádné karty**. Pod obrazem je **informační pás**.
  - Sbalený pás je obraz + jeden řádek ≈ 30 px: pruh průběhu na horní hraně pásu, text „19 % · 2 h 35 min“ a dvě přepínací tlačítka.
  - Rozbalit jde navíc řádek s názvem tisku a/nebo řádek ≈ 60 px s grafem teplot. Každý zvlášť, přímo v malém okně, malými přepínacími tlačítky. Tlačítka jdou ovládat klávesnicí a mají jména pro čtečku obrazovky i `AutomationId`.
  - Stav sbalení se pamatuje. Okno podle něj roste a zmenšuje se na výšku a obraz si drží poměr stran streamu.

## Rozhodnutí (uživatel, 2026-10-24)
- **Ikony (Q1):** **Fluent UI System Icons** (https://github.com/microsoft/fluentui-system-icons, licence MIT, © 2020 Microsoft Corporation, kompatibilní s GPL-3.0).
  - Ikony se do projektu vloží jako vektorové geometrie (`StreamGeometry` / `Path.Data` v `ResourceDictionary`), **bez NuGet balíčku a bez fontu**. Za běhu tak nevzniká žádná závislost a nic nebrání AOT.
  - V návrhu uvést přesné názvy použitých ikon a jejich variantu (Regular / Filled, velikost 16 / 20 / 24).
  - Licenční text MIT doplní ui-dev do přehledu licencí třetích stran (`App/Licenses/ThirdPartyLicenses.cs`, `Licenses/Texts`) v prvním úkolu, který ikony použije (T-081).
- **Víc instancí (Q2):** v1 = **každý typ widgetu nejvýš jednou**.
- **Karty v malém náhledu (Q3):** **žádné karty přes obraz malého náhledu**, jen pás pod obrazem. Volba „viditelný v malém náhledu“ u widgetů **zaniká**.
- **Pořadí v kotvě (Q5):** pořadí karet v jedné kotvě jde v režimu rozložení **měnit tažením**. Klávesová obdoba patří k požadavku na ovládání klávesnicí, navrhne ji designer.
- **Vstup do režimu rozložení (Q6):**
  - Vstupuje se **tlačítkem v řadě tlačítek okna kamery** a **položkou kontextové nabídky** (pravé tlačítko na obraze).
  - Režim je dostupný jen v **normálním okně**. V malém náhledu ani na celé obrazovce není. Celá obrazovka sdílí rozložení normálního okna (Q4), takže úprava v normálním okně platí i pro ni.
- **Celá obrazovka (Q4):** celá obrazovka používá **stejné rozložení jako normální okno** (jako dosud u T-036).
- **Uložení (Q7):** rozložení se uloží až po **„Hotovo“**, **Esc** změny zahodí.
- **Zvýraznění (Q8):** klíčový údaj widgetu se vykreslí **akcentovou barvou** (`#FA6831`):
  - Stav → procento průběhu,
  - Časy → „Zbývá“,
  - Teploty → teplota trysky,
  - Název tisku → název (návrh architekta, designer potvrdí v návrhu),
  - Vychladnutí → aktuální teplota trysky, ve stavu „Vychladlo“ text stavu (návrh architekta, designer potvrdí v návrhu).
- **Průhlednost pozadí (Q9):** **jedno globální nastavení** pro všechny karty i tiskárny. Rozsah **30–95 %**, výchozí **72 %**.
- **Barvy (Q10):** tryska **oranžová**, podložka **modrá**. **Graf v okně Diagnostika používá stejné barvy.** Přesné odstíny (kontrast na tmavé kartě i v diagnostice) navrhne designer v návrhu.

## Rozsah
Do sekce `## Návrh UI` tohoto souboru patří ASCII drátěné modely a text, jako u T-033:
1. **Karty a styl:**
   - rozměry, odsazení, mezery mezi kartami v kotvě, poloměr,
   - písmo a velikosti (S/M/L pro každý widget),
   - barvy: pozadí, text, akcent, stavové barvy tečky (navázat na barvy tray ikony), tryska oranžová a podložka modrá,
   - průhlednost pozadí 30–95 %, výchozí 72 %, globální,
   - tabulková čísla, čitelnost na světlém i tmavém obraze,
   - chování v malém okně (co se zkrátí / skryje), dlouhé texty a nejdelší jazyk (de, it).
2. **Každý widget ve velikostech S/M/L:**
   - obsah,
   - zvýraznění klíčového údaje akcentovou barvou (viz Rozhodnutí),
   - co se ukazuje bez dat (tiskárna nečinná, offline, neznámá teplota, bez náhledu G-code),
   - kdy se widget sám skryje.
3. **Kotvy:**
   - odsazení od okrajů,
   - vztah k tlačítkům okna vpravo nahoře (kotva vpravo nahoře a nahoře uprostřed),
   - směr skládání, zarovnání textu v pravých kotvách, středové kotvy,
   - chování při překryvu kotev v úzkém okně.
4. **Malý náhled (pás pod obrazem):**
   - výšky řádků pásu (≈ 30 px základní řádek, řádek názvu, ≈ 60 px řádek grafu),
   - pruh průběhu na horní hraně pásu (výška, barva),
   - text „19 % · 2 h 35 min“ a jeho stavy bez úlohy, offline a během vychladnutí,
   - dvě přepínací tlačítka (ikony, stav sbaleno / rozbaleno, fokus, tooltip),
   - kompaktní podoba názvu tisku, zástupný text grafu „Žádná data“, pořadí řádků,
   - minimální šířka 240,
   - vztah k úchytům pro změnu velikosti (okraje bez rámu) a k tlačítkům okna.
5. **Režim rozložení (C), jen v normálním okně:**
   - tlačítko v řadě tlačítek okna a položka kontextové nabídky,
   - ztlumení obrazu, zóny kotev, zvýraznění cíle, tažení a přichycení,
   - **pořadí v kotvě tažením**,
   - popover (zvýraznit, S/M/L),
   - lišta „Obnovit výchozí“ / „Hotovo“ (Esc = zrušit),
   - **ovládání klávesnicí**: výběr widgetu, přesun mezi kotvami, pořadí v kotvě, otevření popoveru, Enter / Esc,
   - fokus a jeho vizuál, čtečka obrazovky (`AutomationProperties.Name`), návrh všech `AutomationId`.
6. **Ikony:** seznam potřebných ikon z Fluent UI System Icons s přesnými názvy a variantou. Potřeba jsou například: zbývá, dokončení, doba tisku, tryska, podložka, materiál, vrstva, průměr trysky, vychladnutí, rozložení, přepínače malého náhledu.
7. **Výchozí rozložení:** kotva, pořadí, velikost, zvýraznění a viditelnost pro každý widget.
   - Určit „hlavní kotvu“ výchozího rozložení, tj. kotvu widgetů Stav a Časy.
   - Starý roh T-036 přesune výchozí rozložení zrcadlením tak, aby hlavní kotva skončila ve starém rohu (pravidlo v T-077). Výchozí rozložení musí po zrcadlení dávat smysl ve všech 4 rozích, i s ohledem na tlačítka okna vpravo nahoře.
8. **Podzáložka Překryv v nastavení:**
   - tabulka widgetů **jen s viditelností**, bez posuvníku ve výchozí velikosti 780 i ve všech 8 jazycích,
   - akce „Použít toto rozložení u všech tiskáren“,
   - globální průhlednost pozadí (kde v nastavení a jak naznačit, že platí pro všechny tiskárny),
   - umístění prahu vychladnutí (u tiskárny) a akce „Použít práh u všech tiskáren“.
9. **Notifikace „vychladlo“:** text titulku a těla toastu (cs + en, ostatní jazyky navrhne ui-dev), umístění přepínače v záložce upozornění.
10. Seznam všech nových textů (klíče `Strings.*`) s českou a anglickou podobou.

## Akceptační kritéria
- [x] Sekce `## Návrh UI` pokrývá body 1–10. Obsahuje drátěné modely pro:
  - normální okno (výchozí rozložení),
  - plné rozložení všech widgetů ve všech velikostech,
  - malý náhled (sbalený, s názvem, s grafem, s obojím, bez dat),
  - režim rozložení (tažení, změna pořadí, popover, fokus z klávesnice),
  - stav vychladnutí (oba stavy),
  - podzáložku Překryv v nastavení (výchozí a nejdelší jazyk).
- [x] Varianta B (panel / šuplík) se v návrhu nevyskytuje
- [x] Návrh je v souladu se sekcí Rozhodnutí
- [x] Ikony: názvy z Fluent UI System Icons, licence MIT ověřená (odkaz na `LICENSE`), žádná závislost za běhu
- [ ] Návrh schválený uživatelem (datum schválení v `## Rozhodnutí`)

## Návrh UI

Návrh ui-designer, 2026-10-24. Vychází ze schválené makety (A + C + malý náhled s grafem) a ze sekcí Rozhodnutí T-076 až T-092. Varianta B se nenavrhuje. Všechny rozměry jsou v DIP (při 100 % = px). „Teď“ je ilustrační čas 17:15.

### 0. Výchozí stav (změřeno 2026-10-24)
- Okno kamery: normální okno 960 × 540, `MinWidth` 160, `MinHeight` 90. Tlačítka okna vpravo nahoře: okraj 6, tlačítka 30 × 30, mezera 4, viditelná jen při najetí myší nebo fokusu (`CameraWindowButtons`). Dnes 2 tlačítka (ve všech režimech tlačítka zbývajících dvou režimů).
- Dnešní překryv T-036 (snímek `camera-overlay-default.png`): jeden `Border` 6 od okraje, `#A6000000`, poloměr 4, text 12.
- Okno nastavení: klient 780 × 508. Obsah podzáložky tiskárny je **548 × ≈ 358** (sloupec seznamu 184 + mezera 16; mezi záložkami a řádkem Uložit / Zrušit). Záložka Obecné je zaplněná: ve stavu s chybami končí obsah ≈ 6 DIP nad řádkem Uložit (snímek `settings-general-errors.png`).
- Graf Diagnostiky (`DiagnosticsWindow.axaml`): tryska světlý motiv `#D9480F` / tmavý `#FF8A4C`, podložka `#1864AB` / `#4DABF7`.
- Barvy tray ikony (`ColorTrayIconPalette`): tiskne modrá `#1E88E5`, pozastaveno žlutá `#FDD835`, dokončeno zelená `#43A047`, chyba červená `#E53935`, zaneprázdněna modrá `#1E88E5`, zásah oranžovožlutá `#FB8C00`, nečinná / offline šedá `#8A8D96` / `#9E9E9E`.
- Česká UI používá **vykání** („Zadejte…“, „Zkuste…“). Texty makety tykají („můžeš vypnout“, „přetáhni“). Návrh píše texty vykáním, viz otevřená otázka Q3.

### 1. Karty a styl

#### 1.1 Karta (sdílený styl `Border.overlayCard`)
| Vlastnost | S | M | L |
|---|---|---|---|
| Odsazení (svisle / vodorovně) | 8 / 10 | 12 / 14 | 14 / 16 |
| Poloměr rohů | 8 | 10 | 12 |
| Ikony v řádcích | 16 | 16 | 20 |
| Základní text řádku | 13 SemiBold | 13 Regular | 15 Regular |

- Pozadí: `#0E0E10` s alfou podle **globálního** nastavení (30–95 %, výchozí 72 % = maketa `rgba(14,14,16,0.72)`). Alfa platí jen pro pozadí karty, ne pro text, ikony a pruhy.
- Bez rámečku. Výjimka: „Vychladlo“ má rámeček 1,5 `OverlayCooledBrush`.
- Karty nejsou cílem myši (`IsHitTestVisible=False`) mimo režim rozložení, takže dvojklik a tažení obrazu fungují i přes ně (jako T-036).
- **Mezera mezi kartami v jedné kotvě: 8.** Odsazení kotev od okrajů: **12** (dnes 6; karty s poloměrem 10 potřebují víc vzduchu).
- Velikost karty určuje obsah (`Auto`) v mezích min / max šířky widgetu (oddíl 2). Velká čísla se nikdy nezalamují. Názvy se zkracují (`TextTrimming`), vedlejší řádky se smějí zalomit nejvýš na 2 řádky.

#### 1.2 Písmo
- Rodina: systémové písmo Fluent tématu (`ContentControlThemeFontFamily`). Windows 11: Segoe UI Variable, Windows 10: Segoe UI, macOS: SF Pro. Žádný vložený font.
- **Tabulková čísla** všude, kde se mění číslice (procenta, časy, teploty): `FontFeatures="tnum"` ve sdíleném stylu `TextBlock.numeric`. Segoe UI má tabulkové číslice už ve výchozím stavu, SF Pro ne, proto vlastnost nastavit vždy. Že Avalonia 12 `FontFeatures` na `TextBlock` podporuje, ověří ui-dev v T-081 (test: šířka „11 %“ = „88 %“).
- Typografická škála (velikost / řez / výška řádku):

| Role | S | M | L |
|---|---|---|---|
| Název tiskárny | – | 13 SemiBold / 18 | 16 SemiBold / 22 |
| Velké číslo Stav (%) | 15 Bold / 20 | 30 Bold / 36 | 40 Bold / 48 |
| Velké číslo Časy (zbývá) | 15 Bold / 20 | 26 Bold / 32 | 34 Bold / 42 |
| Popisek („Zbývá“) | – | 12 Regular / 16 | 14 Regular / 18 |
| Řádek s ikonou | 13 SemiBold / 18 | 13 Regular / 18 | 15 Regular / 20 |
| Pilulka stavu | – | 12 SemiBold, výška 22 | 13 SemiBold, výška 26 |
| Čip | – | 11 SemiBold, výška 20 | 12 SemiBold, výška 22 |
| Popisek osy grafu | – | 11 Regular / 14 | 12 Regular / 16 |

- Nejmenší písmo je 11 (jen doplňkové popisky grafu, informace mají i v `AutomationProperties.Name`).

#### 1.3 Barvy (zdroje, nezávislé na motivu)
Překryv leží vždy nad videem, proto má **jednu sadu barev** bez ohledu na světlý / tmavý motiv aplikace. Zdroje v jednom `ResourceDictionary` (návrh: `App/Cameras/Overlay/OverlayResources.axaml`), sdílené všemi widgety, režimem rozložení a pásem malého náhledu.

| Klíč | Hodnota | Použití |
|---|---|---|
| `OverlayCardColor` | `#0E0E10` + alfa z nastavení | pozadí karet |
| `OverlayTextPrimaryBrush` | `#F4F4F5` | hlavní text, velká čísla |
| `OverlayTextSecondaryBrush` | `#C9C9CE` | řádky s ikonou, popisky, ikony |
| `OverlayTextTertiaryBrush` | `#A1A1AA` | jen popisky osy grafu („−60 min“, „teď“) |
| `OverlayAccentBrush` | `#FA6831` | zvýraznění, pruh průběhu, režim rozložení |
| `OverlayTrackBrush` | `#2EFFFFFF` (bílá 18 %) | podklad pruhů průběhu |
| `OverlayPillBrush` | `#1AFFFFFF` (bílá 10 %) | pozadí pilulky stavu |
| `OverlayChipBrush` / `OverlayChipTextBrush` | `#1AFFFFFF` / `#E4E4E7` | čipy názvu tisku |
| `OverlayNozzleBrush` | `#FF8A3D` | tryska (hodnota, ikona, čára grafu) |
| `OverlayBedBrush` | `#6CB4FF` | podložka |
| `OverlayCoolingBrush` | `#6CB4FF` | „Chladne…“ (ikona, pruh) |
| `OverlayCooledBrush` | `#6CCB5F` | „Vychladlo“ (ikona, rámeček) |
| `OverlayStripBrush` | `#141416` (plná) | pozadí pásu malého náhledu |
| `OverlayShadow` | `0 8 24 0 #80000000` | zvednutá karta v režimu rozložení |

- **Tečka stavu** (8 × 8, kruh) má barvy tray ikony. Zdrojem jsou konstanty `ColorTrayIconPalette` vystavené jako zdroje, ne kopie hex hodnot:
  - Tiskne, Zaneprázdněna: modrá `#1E88E5`
  - Pozastaveno: žlutá `#FDD835`
  - Dokončeno: zelená `#43A047`
  - Chyba: červená `#E53935`
  - Vyžaduje zásah, Neplatné přihlašovací údaje: `#FB8C00`
  - Připravena, Zastaveno: šedá `#8A8D96`
  - Nedostupná, Neznámý stav, Připojování: šedá `#9E9E9E`

  Tečka je jen doplněk. Stav vždy nese i text pilulky, informace tedy není jen barvou.
- **Tryska oranžová, podložka modrá a Diagnostika (Q10):** sdílené zdroje `ChartNozzleBrush` a `ChartBedBrush` přejdou z `DiagnosticsWindow.axaml` do sdíleného slovníku (návrh `App/Resources/ChartColors.axaml`) s `ThemeDictionaries`:

  | | Tmavý motiv | Světlý motiv |
  |---|---|---|
  | Tryska | `#FF8A3D` (dnes `#FF8A4C`) | `#D9480F` (beze změny) |
  | Podložka | `#6CB4FF` (dnes `#4DABF7`) | `#1864AB` (beze změny) |

  Tmavý motiv Diagnostiky má tedy **přesně** barvy překryvu. Světlý motiv má tmavší odstín stejného tónu, protože `#FF8A3D` / `#6CB4FF` mají na bílé jen 2,4 : 1 / 2,2 : 1, tedy pod hranicí 3 : 1 pro grafické prvky. `#D9480F` má 4,3 : 1 a `#1864AB` 6,1 : 1. Překryv používá `OverlayNozzleBrush` / `OverlayBedBrush` (= tmavé hodnoty) bez ohledu na motiv.
- **Kontrast (WCAG 2.x, spočítáno 2026-10-24):** pozadí karty smíchané s videem.

  | Text / prvek | alfa 72 %, šedé video `#808080` | alfa 72 %, bílé video | alfa 30 %, šedé video | alfa 95 %, bílé video |
  |---|---|---|---|---|
  | `#F4F4F5` | 12,4 | 7,2 | 5,9 | 15,8 |
  | `#C9C9CE` | 8,2 | 4,8 | 3,9 | 10,5 |
  | `#A1A1AA` | 5,3 | 3,1 | 2,5 | 6,8 |
  | `#FA6831` akcent | 4,6 | **2,7** | 2,2 | 5,9 |
  | `#FF8A3D` tryska | 5,8 | 3,4 | 2,8 | 7,4 |
  | `#6CB4FF` podložka | 6,2 | 3,6 | 3,0 | 8,0 |
  | `#6CCB5F` vychladlo | 6,7 | 3,9 | 3,2 | 10,0 |

  - Při výchozích 72 % splní veškerý text AA (4,5 : 1) na běžném obraze tiskárny (tmavý až středně šedý).
  - Na zcela bílém obraze splní AA hlavní a vedlejší text. Akcent `#FA6831` má tam 2,7 : 1, tedy pod 3 : 1 pro velký text. Zvýrazněné údaje jsou proto vždy velké a tučné (≥ 15 Bold) a nesou i tvar (číslo), ne jen barvu. Barva `#FA6831` je rozhodnutí Q8, návrh ji nemění, jen riziko zapisuje.
  - Pod 50 % na světlém obraze AA nevychází. Nápověda u nastavení to říká („Vyšší hodnota = čitelnější text na světlém obraze“). Spodní mez 30 % je rozhodnutí Q9.
  - `#A1A1AA` jen pro doplňkové popisky osy, nikdy pro jedinou nositelku informace.
- **Prusa oranžová:** `#FA6831` je rozhodnutí uživatele. Oficiální hex Prusa nezveřejňuje (ověřeno 2026-10-24, žádný primární zdroj). Tray ikona používá pro hotend `#FD5000`. Logo ani ochranné známky Prusa se nepoužívají.

#### 1.4 Malé okno, vysoké DPI, dlouhé texty
- **Stupně podle velikosti klientské plochy** normálního okna i celé obrazovky (W × H v DIP). Velikost a uložené rozložení se nemění, mění se jen vykreslení:

  | Stupeň | Podmínka | Vykreslení |
  |---|---|---|
  | 0 | W ≥ 720 a H ≥ 405 | velikosti podle rozložení |
  | 1 | jinak, když W ≥ 480 a H ≥ 270 | každá karta o stupeň menší (L→M, M→S, S zůstane) |
  | 2 | jinak, když W ≥ 320 a H ≥ 180 | jen rohové kotvy, karty ve velikosti S; středové kotvy se nevykreslí |
  | 3 | W < 320 nebo H < 180 | žádné karty (jen obraz) |

- **Ochrana proti kolizi** (po změření, v panelu kotev):
  1. Středová karta nahoře / dole smí být nejvýš `W − 2 × (max(šířka levé kotvy, šířka pravé kotvy) + 8 + 12)` široká. Nahoře se za šířku pravé kotvy bere aspoň rezerva tlačítek okna (6 + 98 + 8 = 112, viz 3.2). Když se nevejde ani v minimální šířce, zmenší se o stupeň. Když se nevejde ani v S, nevykreslí se.
  2. Když se karty v jednom sloupci (horní a dolní kotva stejné strany) překrývají svisle (mezera < 8), skryje se celá karta s nižší prioritou.
  3. Priorita (od nejvyšší): Stav > Časy > Vychladnutí > Teploty > Název tisku.
  4. Pravidla jsou deterministická a čistá (testovatelná funkce bez Avalonie: vstup velikost okna + změřené velikosti, výstup co se vykreslí a v jaké velikosti).
- **Vysoké DPI:** vše ve DIP, ikony jsou vektory (geometrie), pruhy mají celočíselnou výšku ve DIP a `UseLayoutRounding`. Náhled G-code se dekóduje v cílové velikosti × škálování monitoru (40 → 80 px při 200 %). Ověřit při 100 / 125 / 150 / 200 % (snímky v headless testech při 100 %, škálování ručně / E2E T-093).
- **Dlouhé texty (nejdelší de, fr, es, it, pl):**
  - Velká čísla jsou v každém jazyce krátká.
  - Pilulka stavu: nejdelší „Nieprawidłowe dane logowania“ / „Neplatné přihlašovací údaje“ (≈ 180 při 12 SemiBold) + tečka a odsazení ≈ 210 < 252 (vnitřní šířka M). V S se text stavu zkracuje se třemi tečkami, celé znění je ve jménu pro čtečku.
  - Čas dokončení jiný den: „Termina el 17 de octubre a las 8:00“ (es) ≈ 230 při 13: řádek se smí zalomit na 2 řádky, ikona zůstane u prvního řádku.
  - Název tisku a název tiskárny: jedna řádka, zkrácení **uprostřed** (pravidlo T-036: začátek + „…“ + konec), u L název na 2 řádky a pak zkrácení.
  - Popisek vychladnutí: až 2 řádky (de „sicheres Ausschalten in ca. 6 min (Düse unter 50 °C)“).
  - Před „°C“ a „%“ je vždy nezlomitelná mezera (`StringsTests`).

### 2. Widgety

Společné:
- **Zvýraznění** (třída `emphasized`, `IsEmphasized`) obarví klíčový údaj `OverlayAccentBrush` a u velkých čísel ponechá Bold. Klíčové údaje podle Q8 **potvrzuji** včetně návrhů architekta:
  - Stav → procento průběhu,
  - Časy → hodnota „Zbývá“,
  - Teploty → aktuální teplota trysky (jen číslo aktuální hodnoty, ne „/ cíl“),
  - Název tisku → název (zdůvodnění: je to jediný hlavní údaj karty),
  - Vychladnutí → ve stavu „Chladne…“ aktuální teplota trysky, ve stavu „Vychladlo“ text stavu. Ikona a rámeček zůstávají zelené, takže stav nese tvar, text i barva.
- **Velikosti:** všechny widgety podporují S, M i L. S = jeden řádek, M = maketa, L = větší písmo pro čtení z dálky (celá obrazovka na televizi) a u Teplot a Názvu tisku i obsah navíc.
- **Ikony** (přesné názvy v oddílu 6) mají barvu `OverlayTextSecondaryBrush`, pokud není uvedeno jinak.
- **Bez dat** platí pravidla T-036: údaje úlohy jen během úlohy (`HasActiveJob` nebo `Attention`), ostatní, když jsou známé. Neznámá hodnota = řádek se nevykreslí (nikdy „0“ ani „—“). Karta, které nezbude žádný řádek, se skryje celá.
- **Jméno pro čtečku:** každá karta má `AutomationProperties.Name` s celým textem (ikony nahrazují slova, čtečka dostane slova) a `AutomationId` podle 5.9.

#### 2.1 Stav (`status`)
Šířka: S max 240; M min 180, max 280; L min 220, max 340.

```
S                              M                               L
╭────────────────────────╮     ╭──────────────────────────╮    ╭──────────────────────────────╮
│ ● Tiskne     19 %      │     │ MK4 dílna                │    │ MK4 dílna                    │  16 SemiBold
│ ▓▓▓▓░░░░░░░░░░░░░░░░░░ │     │ (● Tiskne)               │    │ (● Tiskne)                   │  pilulka 26
╰────────────────────────╯     │ 19 %                     │    │ 19 %                         │  40 Bold
 výška 46, pruh 4              │ ▓▓▓▓▓░░░░░░░░░░░░░░░░░░░ │    │ ▓▓▓▓▓▓░░░░░░░░░░░░░░░░░░░░░░ │  pruh 8
                               ╰──────────────────────────╯    ╰──────────────────────────────╯
                                výška 126, pruh 6               výška 158
```
- M (shora): název tiskárny 13 SemiBold → 6 → pilulka 22 (odsazení 8 / 0, poloměr 11, tečka 8, mezera 6, text 12 SemiBold) → 8 → „19 %“ 30 Bold → 6 → pruh 6 (poloměr 3, akcent na `OverlayTrackBrush`).
- S: tečka + text stavu (13 SemiBold, zkracuje se) + 8 + „19 %“ 15 Bold vpravo, pod tím pruh 4. Název tiskárny v S není (je v titulku okna).
- Pruh průběhu má akcentovou barvu vždy, nezávisle na zvýraznění. Zvýraznění barví jen číslo.
- **Bez úlohy** (Připravena, Dokončeno, Zastaveno, Zaneprázdněna bez úlohy): jen název + pilulka (M výška 70), procento a pruh se nevykreslí. S = jen „● Připravena“ (výška 36).
- **Pozastaveno / Vyžaduje zásah s úlohou:** procento i pruh zůstanou, pilulka má žlutou / oranžovou tečku.
- **Offline / Neplatné údaje / Chyba:** název + pilulka „● Nedostupná“ / „● Neplatné přihlašovací údaje“ / „● Chyba“. Karta se **nikdy neskryje** (je to jediný ukazatel, že tiskárna nekomunikuje).
- **Před prvním dotazem:** název + pilulka „● Připojování…“ (`StateConnecting`).
- Název tiskárny se zkracuje na 40 znaků (T-036) a pak podle šířky uprostřed.
- Jméno pro čtečku: „MK4 dílna: Tiskne, 19 %“ (bez úlohy „MK4 dílna: Připravena“).

#### 2.2 Časy (`times`)
Šířka: S max 200; M min 180, max 260; L min 220, max 320.

```
S                          M                              L
╭──────────────────╮       ╭────────────────────────╮     ╭─────────────────────────────╮
│ ⧗ 2 h 35 min     │       │ Zbývá                  │     │ Zbývá                       │ 14
╰──────────────────╯       │ 2 h 35 min             │     │ 2 h 35 min                  │ 34 Bold
 výška 36                  │ ⚑ Hotovo v 19:50       │     │ ⚑ Hotovo v 19:50            │ 15, ikona 20
                           │ ◷ 47 min               │     │ ◷ 47 min                    │
                           ╰────────────────────────╯     ╰─────────────────────────────╯
                            výška 122                       výška 146
```
- M (shora): „Zbývá“ 12 `Secondary` → 2 → hodnota 26 Bold → 8 → řádek ⚑ (`Flag`) + čas dokončení 13 → 4 → řádek ◷ (`Clock`) + doba tisku 13. Ikona 16, mezera ikona–text 6.
- S: ⧗ (`Hourglass Half`) + zbývající čas 15 Bold. Čas dokončení a doba tisku v S nejsou.
- Formáty beze změny z T-036: zbývá zaokrouhleno nahoru na minutu (`2 h 35 min`), čas dokončení `CameraOverlayFinishTime` („Hotovo v 19:50“) / jiný den `CameraOverlayFinishDateTime`, absurdní odhad nepřeteče.
- Jen během úlohy. Neznámá hodnota = řádek chybí. Neznámé „Zbývá“: M/L začne řádkem dokončení (popisek „Zbývá“ zmizí s hodnotou). Bez všech tří hodnot se karta skryje.
- Jméno pro čtečku: „Zbývá 2 h 35 min, Hotovo v 19:50, Doba tisku: 47 min“ (`ToolTipRemaining`, `CameraOverlayFinishTime`, `CameraOverlayPrintTime`).

#### 2.3 Teploty (`temperatures`)
Šířka pevná: S podle obsahu (≈ 230); M 284 (graf 256 + 2 × 14); L 392 (graf 360 + 2 × 16).

```
S
╭───────────────────────────────╮
│ ▽ 215 / 215 °C   ━ 60 / 60 °C │   výška 36
╰───────────────────────────────╯

M                                          L
╭────────────────────────────────╮         ╭──────────────────────────────────────────╮
│ ▽ 215 / 215 °C    ━ 60 / 60 °C │ 14 SB   │ ▽ Tryska     215 / 215 °C                │ 16 SB
│ ┌────────────────────────────┐ │         │ ━ Podložka    60 / 60 °C                 │
│ │╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌│ │         │ ┌──────────────────────────────────────┐ │
│ │       ╱‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾│ │ 256×64  │ │╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌│ │
│ │╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌│ │         │ │        ╱‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾│ │ 360×110
│ │____╱‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾│ │         │ │╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌│ │
│ └────────────────────────────┘ │         │ │_____╱‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾│ │
│ −60 min                   teď  │ 11      │ └──────────────────────────────────────┘ │
╰────────────────────────────────╯         │ −60 min                             teď  │ 12
 výška 134                                 ╰──────────────────────────────────────────╯
                                            výška 216
```
- Ikona trysky ▽ (`Cone` otočená o 180°, `OverlayNozzleBrush`), ikona podložky ━ (`Line Horizontal 1`, `OverlayBedBrush`). Ikona má barvu čáry v grafu, takže zastupuje legendu a tryska s podložkou se liší i tvarem, ne jen barvou. Ověřit čitelnost na snímku v T-084. Záložní varianta: barevná čárka 18 × 3 jako legenda Diagnostiky.
- M (shora): řádek hodnot (dvě dvojice ikona + text 14 SemiBold, mezera 16) → 8 → graf 256 × 64 → 4 → popisky osy 11 `Tertiary` („−60 min“ vlevo, „teď“ vpravo).
- L: hodnoty pod sebou se slovem („Tryska“ / „Podložka“, `DiagnosticsNozzle` / `DiagnosticsBed`, 16 Regular `Secondary`, sloupec slov `Auto`). Slova jsou jen v L, kde je místo (es „Base calefactable“ ≈ 120 při 16).
- **Graf** = kompaktní režim `TemperatureChart` (T-084):
  - bez os, mřížky a popisků, průhledné pozadí,
  - okno 60 min, čáry 1,5 DIP (L 2),
  - cíle čárkovaně 1 DIP (4 / 3) v barvě čáry s alfou 70 %, jen při ohřevu,
  - přerušení při výpadku dat beze změny.
- Hodnoty: „215 / 215 °C“ (`CameraOverlayTemperatureWithTarget`) jen při zapnutém topení, jinak „28 °C“. Zvýraznění barví jen „215“ trysky.
- **Bez dat:**
  - chybí podložka → její dvojice zmizí;
  - chybí obě teploty (offline, před prvním dotazem) → karta se skryje;
  - bez historie → místo grafu text „Žádná data“ (`CameraOverlayChartNoData`, 12 `Tertiary`, na střed plochy grafu), popisky osy zůstanou.
- Jméno pro čtečku: „Tryska 215 / 215 °C, Podložka 60 / 60 °C, graf posledních 60 minut“.

#### 2.4 Název tisku (`print-name`)
Šířka: S max 280; M min 240, max 420; L min 300, max 480 (nahoře uprostřed navíc pravidlo kolize 1.4).

```
S                                     M (s náhledem)
╭───────────────────────────────╮     ╭──────────────────────────────────────────────╮
│ ⬡ Foldable book holder v1     │     │ ┌──────┐ Foldable book holder v1             │ 14 SB
╰───────────────────────────────╯     │ │ 40px │ (PLA) (≋ 0,2 mm) (tryska 0,4)        │ čipy 20
 výška 36                             │ └──────┘                                     │
                                      ╰──────────────────────────────────────────────╯
                                       výška 68 (náhled 40, poloměr 6, mezera 10)

M (bez náhledu, T-085 / bez PNG)      L (s náhledem)
╭──────────────────────────────╮      ╭────────────────────────────────────────────────────╮
│ Foldable book holder v1      │      │ ┌──────────┐ Foldable book holder v1               │ 16 SB, 2 ř.
│ (PLA) (≋ 0,2 mm) (tryska 0,4)│      │ │          │ (PLA) (≋ 0,2 mm) (tryska 0,4)         │ čipy 22
╰──────────────────────────────╯      │ │   72px   │ odhad sliceru 2 h 51 min              │ 13 Secondary
 výška 68                             │ └──────────┘                                       │
                                      ╰────────────────────────────────────────────────────╯
                                       výška 102 (náhled 72, poloměr 8, mezera 12)
```
- Čipy: výška 20 (L 22), poloměr 10 (L 11), odsazení 0 / 7, mezera 4, text 11 SemiBold `OverlayChipTextBrush` na `OverlayChipBrush`. Pořadí podle makety:
  1. materiál „PLA“ (víc materiálů „PLA, PETG“ v jednom čipu, max 16 znaků + „…“),
  2. výška vrstvy „≋ 0,2 mm“ s ikonou `Layer` 12,
  3. tryska „tryska 0,4“.
  Desetinný oddělovač podle kultury aplikace. Čipy se nezalamují: co se nevejde do šířky, zmizí zprava (tryska → vrstva → materiál), celé znění je ve jménu pro čtečku.
- S: ikona ⬡ (`Cube`) + název 13 SemiBold. Bez čipů a bez náhledu.
- **Bez náhledu** (T-085, soubor bez PNG, poškozený PNG, náhled se ještě stahuje): sloupec náhledu se nevykreslí vůbec (žádný prázdný rámeček, žádné poskočení výšky, protože M má výšku 68 v obou případech). Náhled se objeví, až je dekódovaný.
- Náhled: `Image` `Stretch=Uniform` na pozadí `#14FFFFFF`, poloměr 6 / 8 (ořez `ClipToBounds` přes `Border`).
- Neznámý tvar názvu souboru → jen název bez čipů. Odhad sliceru (L) jen když ho T-078 rozpozná.
- **Viditelnost:** během úlohy a po dokončení nebo zastavení tisku, dokud nezačne další tisk (rozhodnutí T-085). Offline → skrytý. Bez známého názvu souboru → skrytý.
- Jméno pro čtečku: „Foldable book holder v1, PLA, výška vrstvy 0,2 mm, tryska 0,4 mm, odhad sliceru 2 h 51 min“. Náhled: „Náhled tisku“ (`CameraOverlayThumbnailName`).

#### 2.5 Vychladnutí (`cooldown`)
Šířka: S max 260; M min 240, max 300; L min 280, max 360. Viditelné jen ve stavu chladnutí z `CooldownMonitor` (T-079). Zmizí při dalším tisku, offline a znovu zapnutém topení trysky.

```
„Chladne…“ M                                   „Vychladlo“ M (rámeček 1,5 zelený)
╭──────────────────────────────────────╮       ┏━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┓
│ ❄ Chladne…                           │ 14 SB ┃ ⏻ Vychladlo – můžete vypnout         ┃ 14 SB
│ ▽ 87 °C        ━ 45 °C               │ 22 B  ┃ ▽ 48 °C        ━ 39 °C               ┃ 14 SB
│ ▓▓▓▓▓▓▓▓▓▓▓▓▓▓░░░░░░░░░░░░░░░░░░░░  │ 6 px  ┃ tisk dokončen v 19:52 · vychladlo    ┃ 12
│ bezpečné vypnutí za cca 6 min        │ 12    ┃ za 14 min                            ┃
│ (tryska pod 50 °C)                   │       ┗━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┛
╰──────────────────────────────────────╯        výška 96–112
 výška 134

S „Chladne…“                      S „Vychladlo“
╭──────────────────────────╮      ┏━━━━━━━━━━━━━━━━━━━━━━┓
│ ❄ 87 °C · cca 6 min      │      ┃ ⏻ Vychladlo · 48 °C  ┃   výška 36
│ ▓▓▓▓▓▓▓▓▓▓░░░░░░░░░░░░░  │      ┗━━━━━━━━━━━━━━━━━━━━━━┛
╰──────────────────────────╯
 výška 46, pruh 4
```
- Ikony: ❄ `Weather Snowflake` (20; v S 16, zmenšená varianta 20) `OverlayCoolingBrush`; ⏻ `Power` (20; v S 16) `OverlayCooledBrush`.
- **„Chladne…“ M** (shora): ikona + „Chladne…“ 14 SemiBold → 8 → tryska 22 Bold (`Primary`, se zvýrazněním akcent) a podložka 14 SemiBold `Secondary` → 8 → pruh 6 `OverlayCoolingBrush` (průběh od teploty na začátku chladnutí k prahu) → 8 → popisek 12 `Secondary`, nejvýš 2 řádky:
  - s odhadem: „bezpečné vypnutí za cca 6 min (tryska pod 50 °C)“ (`CameraOverlayCooldownEstimate`),
  - bez odhadu (Core vrátí `null`): „bezpečné vypnutí, až tryska klesne pod 50 °C“ (`CameraOverlayCooldownNoEstimate`), nikdy „0 min“.
- **„Vychladlo“ M:** rámeček 1,5 `OverlayCooledBrush`, ikona ⏻ + text stavu 14 SemiBold (se zvýrazněním akcent), teploty 14 SemiBold, popisek 12 `Secondary`:
  - „tisk dokončen v 19:52 · vychladlo za 14 min“ / „tisk zastaven v 19:52 · vychladlo za 14 min“,
  - tisk skončil už pod prahem (Q5 T-079): jen „tisk dokončen v 19:52“.
  - **Požadavek na T-079 / T-088:** `CooldownState` musí nést čas přechodu do „Vychladlo“ (nebo dobu chladnutí) a důvod začátku (dokončen / zastaven). Dnes T-079 uvádí jen čas začátku.
- L: stejný obsah, titulek 16, tryska 28 Bold, pruh 8, popisek 13, ikony 24 (`_24_` varianty).
- Podložka chybí → jen tryska. Teplota trysky chybí → karta se skryje (stav bez dat nemá smysl).
- Jméno pro čtečku: „Chladne…, tryska 87 °C, podložka 45 °C, bezpečné vypnutí za cca 6 min (tryska pod 50 °C)“ / „Vychladlo – můžete vypnout, tryska 48 °C, podložka 39 °C, tisk dokončen v 19:52, vychladlo za 14 min“.

#### 2.6 Plné rozložení všech widgetů (galerie pro snímky reference)
Snímek `camera-widgets-gallery-{S|M|L}.png` v T-081 až T-088: okno 960 × 540, každý widget v jedné kotvě, všechny v téže velikosti:

```
M ─ 960 × 540 ───────────────────────────────────────────────────────────────────────────────
│ ╭ Stav ─────────╮          ╭ Název tisku M ──────────────────────╮     ╭ Teploty M ─────────╮ │ ← 44 pod
│ │ MK4 dílna     │          │ ┌──┐ Foldable book holder v1        │     │ ▽ 215/215  ━ 60/60 │ │   tlačítky
│ │ (● Tiskne)    │          │ └──┘ (PLA)(≋ 0,2 mm)(tryska 0,4)    │     │ [~~~~~~~~~~~~~~~~] │ │
│ │ 19 %          │          ╰─────────────────────────────────────╯     │ −60 min        teď │ │
│ │ ▓▓▓░░░░░░░░░  │                                                      ╰────────────────────╯ │
│ ╰───────────────╯                                                                             │
│                                                                                               │
│ ╭ Časy ─────────╮          ╭ Vychladnutí M ──────────────────╮                                │
│ │ Zbývá         │          │ ❄ Chladne…                      │                                │
│ │ 2 h 35 min    │          │ ▽ 87 °C   ━ 45 °C               │                                │
│ │ ⚑ Hotovo v 19:50         │ ▓▓▓▓▓▓▓░░░░░░░░░                │                                │
│ │ ◷ 47 min      │          │ bezpečné vypnutí za cca 6 min…  │                                │
│ ╰───────────────╯          ╰─────────────────────────────────╯                                │
──────────────────────────────────────────────────────────────────────────────────────────────────
```
(Galerie jen pro testy. Časy a Vychladnutí spolu v reálu nebývají, protože Časy jsou jen během úlohy.) U galerie L se uplatní stupeň 0 a ochrana proti kolizi: Název tisku L nahoře uprostřed se vedle Stavu L nevejde (960 − 2 × (352 + 8) = 240 < 300), proto se zmenší na M. Je to zároveň test pravidla.

### 3. Kotvy

#### 3.1 Geometrie
- 6 kotev: `TopLeft`, `TopCenter`, `TopRight`, `BottomLeft`, `BottomCenter`, `BottomRight`. Odsazení 12 od okrajů klientské plochy (normální okno i celá obrazovka).
- **Skládání:** pořadí v kotvě = pořadí v seznamu rozložení a vždy **od okraje okna ke středu**. V horních kotvách je první karta nahoře, v dolních kotvách dole (další nad ní). Při zrcadlení (T-077) tak karty zůstanou ve stejném vztahu k okraji.
- **Zarovnání:**
  - levé kotvy: karty vlevo, text vlevo;
  - pravé kotvy: karty vpravo, **text uvnitř karet vlevo**. Karta je samostatný blok, zarovnání vpravo by u víceřádkových karet (Časy, Vychladnutí) zhoršilo čtení. Dnešní zarovnání textu vpravo z T-036 zaniká;
  - středové kotvy: karty na střed vodorovně, text vlevo.

  Karty v jedné kotvě mají každá svou šířku a zarovnávají se k okraji kotvy (vlevo / vpravo / na střed).
- Karty se do kotvy jen přidávají pod sebe, nikdy vedle sebe.

#### 3.2 Tlačítka okna a horní kotvy
- Normální okno dostane třetí tlačítko **Upravit rozložení** (Q6): pořadí `[rozložení][celá obrazovka][malý náhled]`, šířka 3 × 30 + 2 × 4 = 98. Celá obrazovka a malý náhled mají tlačítka beze změny (2 tlačítka, rozložení tam není).
- **Kotva vpravo nahoře** začíná 6 + 30 + 8 = **44** od horního okraje, vždy (i když tlačítka nejsou vidět), aby karty neposkakovaly při najetí myší. Vodorovně 12 od pravého okraje.
- **Kotva nahoře uprostřed** má horní okraj 12. Šířku omezuje pravidlo kolize 1.4 s rezervou tlačítek 112 vpravo (a symetricky vlevo, aby karta zůstala na středu). V 960: max 960 − 2 × 112 − 2 × 20 = 696 > max šířka M 420.
- Tlačítka okna mají vyšší z-pořadí než karty.

#### 3.3 Výchozí rozložení a hlavní kotva (bod 7)
| Widget | Kotva | Pořadí v kotvě | Velikost | Zvýraznění | Viditelný |
|---|---|---|---|---|---|
| Stav | `TopLeft` | 1 | M | ano | ano |
| Název tisku | `TopCenter` | 1 | M | ne | ano |
| Časy | `BottomLeft` | 1 | M | ne | ano |
| Vychladnutí | `BottomLeft` | 2 | M | ne | ano |
| Teploty | `BottomRight` | 1 | M | ne | ano |

- Pořadí v seznamu `CameraOverlayLayout.Default` (T-077): status, print-name, times, cooldown, temperatures.
- Zvýrazněný je výchozí jen Stav: jeden akcentový údaj na obrazovce (jedna hlavní informace na pohled).
- Časy a Vychladnutí sdílejí kotvu a v praxi se střídají (Časy během úlohy, Vychladnutí po ní).
- **Hlavní kotva = `BottomLeft`**, tj. kotva widgetu Časy (a Vychladnutí). Stav je podle makety ve stejném sloupci u opačného okraje (`TopLeft`), takže zrcadlení přesune celý levý sloupec na stranu starého rohu. T-077 předpokládal „Stav + Časy v jedné kotvě“. Návrh drží schválenou maketu, viz otevřená otázka Q1.
- **Zrcadlení do 4 starých rohů T-036** (kontrola s tlačítky okna):

  | Starý roh | Stav | Název tisku | Časy + Vychladnutí | Teploty | Poznámka |
  |---|---|---|---|---|---|
  | `BottomLeft` (výchozí) | TL | TC | BL | BR | přesně výchozí rozložení, nic se nezapíše |
  | `BottomRight` | TR (od 44) | TC | BR | BL | Stav pod tlačítky |
  | `TopLeft` | BL | BC | TL | TR (od 44) | Teploty pod tlačítky |
  | `TopRight` | BR | BC | TR (od 44) | TL | Časy pod tlačítky |

  Ve všech čtyřech rozích se při 960 × 540 nic nepřekrývá:
  - nejvyšší sloupec: Stav M 126 + Časy M 122 + 2 × 12 + 44 ≪ 540;
  - pravá horní kotva začíná pod tlačítky;
  - středová karta je jen jedna.

### 4. Malý náhled – pás pod obrazem

```
Sbalený 320 × 213                       S názvem 320 × 247
┌────────────────────────────────┐       ┌────────────────────────────────┐
│                       [□][▣]  │ ←tl.  │                       [□][▣]  │
│            obraz               │ okna  │            obraz               │
│           320 × 180            │       │           320 × 180            │
│                                │       │                                │
├▓▓▓▓▓▓▓░░░░░░░░░░░░░░░░░░░░░░░░┤ 3     ├▓▓▓▓▓▓▓░░░░░░░░░░░░░░░░░░░░░░░░┤ 3
│ ● 19 % · 2 h 35 min    [⬡][📈] │ 30    │ ● 19 % · 2 h 35 min    [⬡̲][📈] │ 30
└────────────────────────────────┘       │ ⬡ Foldable book holder v1 (PLA)│ 34
                                         └────────────────────────────────┘

S grafem 320 × 277                       S obojím 320 × 311
├▓▓▓▓▓▓▓░░░░░░░░░░░░░░░░░░░░░░░░┤ 3     ├▓▓▓▓▓▓▓░░░░░░░░░░░░░░░░░░░░░░░░┤ 3
│ ● 19 % · 2 h 35 min    [⬡][📈̲] │ 30    │ ● 19 % · 2 h 35 min    [⬡̲][📈̲] │ 30
│ 60 min  ┌────────────────────┐ │       │ ⬡ Foldable book holder v1 (PLA)│ 34
│ ▽ 215°  │  ╱‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾ │ │ 64    │ 60 min  ┌────────────────────┐ │
│ ━ 60°   │_╱‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾ │ │       │ ▽ 215°  │  ╱‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾ │ │ 64
│         └────────────────────┘ │       │ ━ 60°   │_╱‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾ │ │
└────────────────────────────────┘       └────────────────────────────────┘

Bez dat (nečinná, sbalený, šířka 240 → 240 × 168)
┌────────────────────────┐
│        obraz           │ 135
├░░░░░░░░░░░░░░░░░░░░░░░░┤ 3 (jen podklad)
│ ● Připravena   [⬡][📈] │ 30
└────────────────────────┘
```
- **Pás** (`CameraMiniInfoStrip`): plné pozadí `OverlayStripBrush` (není poloprůhledné, globální průhlednost se ho netýká). Výška: 3 + 30 (+ 34) (+ 64). Pořadí řádků shora: pruh průběhu → základní řádek → řádek názvu → řádek grafu. Rozbalení přidá řádek pod předchozí, okno roste podle T-089 (od nejbližšího okraje obrazovky).
- **Pruh průběhu** (`CameraMiniProgress`): 3 na celou šířku na horní hraně pásu, výplň `OverlayAccentBrush`, podklad `OverlayTrackBrush`. Bez úlohy jen podklad s alfou 6 % (výška zůstává, nic neposkočí).
- **Základní řádek** (`CameraMiniSummary`, 30): odsazení vlevo 10, tečka stavu 8 + 6, text 13 SemiBold `Primary`, tabulková čísla, zkracování na konci. Vpravo dvě přepínací tlačítka. Text podle stavu:

  | Stav | Text | Tečka / ikona |
  |---|---|---|
  | Tiskne | „19 % · 2 h 35 min“ (bez známého času „19 %“) | modrá tečka |
  | Úloha a jiný stav (Pozastaveno, Vyžaduje zásah…) | „19 % · Pozastaveno“ (číslo první, slovo se zkracuje) | barva stavu |
  | Chladne | „Chladne… 87 °C · cca 6 min“ | ❄ 16 `OverlayCoolingBrush` |
  | Vychladlo | „Vychladlo · 48 °C“ (krátký text, `CameraMiniCooled`) | ⏻ 16 `OverlayCooledBrush` |
  | Bez úlohy | text stavu („Připravena“, „Dokončeno“, „Zastaveno“) | barva stavu |
  | Offline / chyba / neplatné údaje | „Nedostupná“ / „Chyba“ / „Neplatné přihlašovací údaje“ | barva stavu |
  | Před prvním dotazem | „Připojování…“ | šedá |

  Celé znění je v tooltipu řádku i ve jménu pro čtečku. Při šířce 240 zbývá na text 240 − 10 − 14 − 8 − 2 × 30 − 2 − 6 = **140**. „19 % · 2 h 35 min“ (≈ 120) se vejde ve všech jazycích, delší slova stavů se zkrátí.
- **Přepínací tlačítka** (`ToggleButton`, `CameraMiniToggleName`, `CameraMiniToggleChart`):
  - 30 × 30 (celá výška řádku), mezera 2, vpravo 6 od okraje (úchyt rohu zůstane volný),
  - ikony 16: ⬡ `Cube` (název) a 📈 `Data Trending` (graf),
  - vypnuto: ikona `Secondary`, bez pozadí; najetí: pozadí bílá 8 %; zapnuto: ikona `Primary` + pozadí bílá 14 % + akcentová linka 2 pod ikonou (stav nese tvar, ne jen barva),
  - fokus: systémový dvojitý rámeček Fluent (bílý / černý), vnitřní okraj 1, aby se na tmavém pásu neořízl,
  - tooltip podle stavu: „Zobrazit název tisku“ / „Skrýt název tisku“, „Zobrazit graf teplot“ / „Skrýt graf teplot“,
  - `AutomationProperties.Name` stálé („Název tisku v pásu“, „Graf teplot v pásu“), stav přečte čtečka ze vzoru Toggle,
  - klávesnice: Tab mezi tlačítky, mezerník / Enter přepne,
  - stisk tlačítka nezačne tažení okna,
  - tlačítka leží **nad** úchyty pro změnu velikosti (z-pořadí). Spodní okraj pod nimi tedy nemění velikost, zbytek okraje ano.
  - Odchylka: 30 < 32 DIP (doporučený cíl) kvůli schválené výšce řádku 30, viz otázka Q5.
- **Řádek názvu** (`CameraMiniNameRow`, 34): odsazení 10, ikona ⬡ 16 `Secondary`, 6, název 13 SemiBold (zkrácení uprostřed), za ním čip materiálu (11, výška 18) jen při šířce okna ≥ 320, pak čip výšky vrstvy při ≥ 400. **Bez úlohy řádek zmizí** a okno se zmenší. Tlačítko zůstane zapnuté a jeho tooltip řekne „Název tisku se zobrazí během tisku“.
- **Řádek grafu** (`CameraMiniChartRow`, 64): odsazení 10 / 6, vlevo sloupec 64:
  - „60 min“ 11 `Tertiary`,
  - „▽ 215°“ 12 SemiBold `OverlayNozzleBrush`,
  - „━ 60°“ 12 SemiBold `OverlayBedBrush`.

  Vpravo kompaktní `TemperatureChart` (`CameraMiniChart`) přes zbytek šířky × 52. Bez historie text „Žádná data“ na střed plochy grafu. Bez teplot jen „Žádná data“ a sloupec bez hodnot.
- **Minimální šířka 240** (T-089 Q4): pás se vejde (viz výpočet výše), graf má 240 − 20 − 64 − 8 = 148 × 52.
- **Úchyty a tlačítka okna:** úchyty 6 po obvodu beze změny (i přes pás). Tlačítka okna zůstávají nad obrazem vpravo nahoře. Pás tak tlačítka okna nikdy nepřekryjí. Tažení za pás (mimo přepínače) přesouvá okno.
- **Karty widgetů se v malém náhledu nekreslí** a zvýraznění se tu neuplatní.

### 5. Režim rozložení (C) – jen normální okno

#### 5.1 Vstup a výstup
- **Tlačítko** v řadě tlačítek okna (`CameraLayoutButton`, ikona `Layout Cell Four` 16 Regular, tooltip a jméno „Upravit rozložení“). Je jen v normálním okně.
- **Kontextová nabídka** obrazu (pravé tlačítko, klávesa Menu / Shift+F10; macOS Ctrl+klik): `CameraContextMenu` s položkou „Upravit rozložení“ (`CameraLayoutMenuItem`). Bez trojtečky: režim se otevře hned, bez dialogu. V malém náhledu a na celé obrazovce se nabídka neotevře (v1 nemá jiné položky).
- Režim skončí:
  - **Hotovo** (uloží),
  - **Zrušit** nebo **Esc** (zahodí),
  - zavření okna, přepnutí do malého náhledu nebo na celou obrazovku (F11, dvojklik je v režimu vypnutý) = zahodí (jako Esc, rozhodnutí T-091).
- V režimu jsou skryté tlačítka okna a kontextová nabídka, dvojklik nepřepíná režim okna. Myš ovládá jen karty a lištu.
- Malé okno: viz otázka Q4. Návrh: pod 480 × 300 je tlačítko zakázané s tooltipem (`ToolTip.ShowOnDisabled`) a položka nabídky také.

#### 5.2 Vzhled
```
960 × 540 ─ obraz ztlumený na 50 % (černá vrstva #80000000) ────────────────────────────────
│ ┌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┐   ┌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┐   ┌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┐          │
│ ╎╭ Stav ─────────╮     ╎   ╎ ╭ Název tisku ──────╮╎   ╎                      ╎          │
│ ╎│ MK4 dílna     │     ╎   ╎ │ ┌─┐ Foldable…     │╎   ╎    Vpravo nahoře     ╎ 150      │
│ ╎│ (● Tiskne)    │     ╎   ╎ ╰───────────────────╯╎   ╎                      ╎          │
│ ╎│ 19 %          │     ╎   ╎                      ╎   ╎                      ╎          │
│ ╎╰───────────────╯     ╎   ╎                      ╎   ╎                      ╎          │
│ └╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┘   └╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┘   └╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┘          │
│                 ╭────────────────────────────────────────────────────────────────╮        │
│                 │ ⊞ Režim rozložení · přetáhněte widget do kotvy                 │        │
│                 │                       [Obnovit výchozí]   [ Hotovo ] [ Zrušit ] │ 48–96  │
│                 ╰────────────────────────────────────────────────────────────────╯        │
│ ┌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┐   ┌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┐   ┌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┐          │
│ ╎╭ Časy ─────────╮     ╎   ╎                      ╎   ╎   ╭ Teploty ─────────╮╎          │
│ ╎│ Zbývá 2 h 35  │     ╎   ╎   Dole uprostřed     ╎   ╎   │ ▽ 215  ━ 60      │╎          │
│ ╎╰───────────────╯     ╎   ╎                      ╎   ╎   │ [~~~~~~~~~~~~~~] │╎          │
│ ╎╭ Vychladnutí ──╮     ╎   ╎                      ╎   ╎   ╰──────────────────╯╎          │
│ ╎│ teď bez dat   │     ╎   ╎                      ╎   ╎                      ╎          │
│ └╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┘   └╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┘   └╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┘          │
─────────────────────────────────────────────────────────────────────────────────────────────
```
- **Ztlumení:** černá vrstva 50 % přes obraz (`CameraLayoutDim`), náběh 150 ms. Stream běží dál.
- **Zóny kotev** (`CameraLayoutAnchor{Kotva}`):
  - v 960 × 540 velké 260 × 150, obecně šířka `clamp((W − 4 × 12) / 3, 160, 260)`, výška `clamp((H − 3 × 12 − 56) / 2, 90, 150)`; zóna se zvětší, když je stávající karta nebo sloupec karet větší (+ 8 na každé straně),
  - umístěné v kotvách s odsazením 12; zóna vpravo nahoře také od 12, protože tlačítka okna jsou v režimu skrytá. Rezerva 44 pro karty platí dál a zóna ji zahrnuje,
  - čára 1,5 čárkovaně 6 / 4 bílá 55 %, výplň bílá 6 %, poloměr 12,
  - prázdná zóna ukazuje název kotvy 12 `Secondary` na střed („Vpravo nahoře“).
- **Karty v režimu** se vykreslí ve skutečné velikosti z pracovní kopie rozložení:
  - Widget bez dat (dnes skrytý, např. Vychladnutí nebo Název tisku bez úlohy) se ukáže jako **zástupná karta**: ikona widgetu + název widgetu 13 SemiBold + „teď bez dat“ 12 `Tertiary`, v minimální šířce své velikosti. Jinak by nešel umístit.
  - Neviditelné widgety (vypnuté v nastavení) se v režimu nezobrazí.
  - Kurzor nad kartou `Hand`, při tažení `SizeAll`.
- **Lišta** (`CameraLayoutToolbar`):
  - styl karty s alfou 88 %, poloměr 10, odsazení 12 / 16, vodorovně i svisle na střed okna, šířka nejvýš `min(720, W − 24)`,
  - obsah: ikona `Layout Cell Four` 16 + „Režim rozložení“ 14 SemiBold + „ · “ + nápověda 13 `Secondary` (`CameraLayoutHint`),
  - tlačítka standardní Fluent výšky 32: „Obnovit výchozí“ (`CameraLayoutReset`) vlevo a vpravo „Hotovo“ (`CameraLayoutDone`, třída `accent`) a „Zrušit“ (`CameraLayoutCancel`),
  - když se text s tlačítky nevejde do jednoho řádku (de / fr / es), tlačítka přejdou na druhý řádek zarovnaný vpravo (výška 96).
- **Pořadí tlačítek:** Windows `[Obnovit výchozí] … [Hotovo] [Zrušit]` (jako Uložit / Zrušit v nastavení), macOS `[Obnovit výchozí] … [Zrušit] [Hotovo]` (HIG: potvrzující akce vpravo).
- „Zrušit“ přidávám (T-091 ho nechal na návrhu). Bez něj by uživatel myši neměl jak změny zahodit, protože Esc je jen klávesnice.
- **Chyba uložení:** pod nápovědou v liště řádek 12 `#FF99A4` „Rozložení se nepodařilo uložit. Zkuste to znovu.“ (`CameraLayoutSaveError`). Režim zůstane otevřený a změny se neztratí.
- „Obnovit výchozí“ nastaví v pracovní kopii výchozí rozložení (oddíl 3.3). Bez potvrzovacího dialogu, protože Zrušit / Esc pořád všechno vrátí.

#### 5.3 Tažení a změna pořadí
```
Tažení Teplot z BR do BL nad Časy:
│ ┏━━━━━━━━━━━━━━━━━━━━━━┓                                 ┌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┐
│ ┃     Pustit sem       ┃  ← cílová zóna: čára 2 plná      ╎   ┌╌ ╌ ╌ ╌ ╌ ╌ ╌ ╌┐  ╎ ← místo po
│ ┃  ━━━━━━━━━━━━━━━━━━  ┃    #FA6831, výplň #24FA6831      ╎   ╎  (prázdné místo) ╎  ╎   kartě
│ ┃ ╭ Časy ─────────╮    ┃  ← ukazatel pozice: linka 3      ╎   └╌ ╌ ╌ ╌ ╌ ╌ ╌ ╌┘  ╎   čárkovaně
│ ┃ │ Zbývá 2 h 35  │    ┃    akcent mezi kartami          └╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┘
│ ┃ ╰───────────────╯    ┃
│ ┗━━━━━━━━━━━━━━━━━━━━━━┛          ╭ Teploty ───────────╮  ← zvednutá karta: měřítko 1,03,
│                                    │ ▽ 215  ━ 60        │    stín OverlayShadow, krytí 95 %,
│                                    ╰────────────────────╯    sleduje ukazatel myši
```
- Tažení začne stiskem levého tlačítka na kartě a posunem > 4 DIP. Stisk bez posunu je klik (výběr + popover).
- Zvednutá karta sleduje ukazatel se zachovaným posunem úchopu a leží nad vším. Na původním místě zůstane čárkovaný obrys stejné velikosti, takže ostatní karty neposkočí.
- **Cíl** = zóna pod ukazatelem, jinak zóna s nejbližším středem:
  - zvýraznění čárou 2 plně `OverlayAccentBrush` a výplní akcent 14 %,
  - popisek „Pustit sem“ (`CameraLayoutDropHere`) 13 SemiBold akcent nahoře v zóně (v prázdné zóně na střed).
- **Pozice v kotvě:** podle svislé polohy ukazatele vůči středům karet v cílové kotvě. Ukazatel pozice (`CameraLayoutInsertIndicator`) je linka 3 × (šířka zóny − 16), poloměr 1,5, akcent, v mezeře mezi kartami nebo na začátku / konci. Funguje i v původní kotvě, takže změna pořadí = tažení v rámci kotvy.
- Puštění: karta se přichytí animací 120 ms (pozice a měřítko). Přesun jde přes čistou operaci T-077 na pracovní kopii. Puštění mimo zóny (nad lištou) vrátí kartu.
- Esc během tažení zruší jen tažení, režim zůstane.

#### 5.4 Výběr a popover
```
 ╭ Stav ─────────╮┊       ╭─────────────────────────────╮
 │ MK4 dílna     │┊ ──▶   │ Stav                        │ 14 SemiBold
 │ (● Tiskne)    │┊       │ Zvýraznit           [ ●══ ] │ ToggleSwitch
 │ 19 %          │┊       │ Velikost                    │
 ╰───────────────╯┊       │ ( S )( M̲ )( L )             │ 3 × 40 × 32
  ▲ výběr: obrys 2 akcent │                             │
                          ╰─────────────────────────────╯
                           Flyout 232 široký, odsazení 12, motiv aplikace
```
- Klik na kartu (nebo Enter / F2) ji vybere (obrys 2 `OverlayAccentBrush`, odstup 2 od karty) a otevře **popover** (`CameraLayoutPopover`, Avalonia `Flyout`). Vzhled podle **motivu aplikace** (Fluent flyout), ne podle překryvu.
- Umístění popoveru směrem do středu okna: levé kotvy `RightEdgeAlignedTop`, pravé `LeftEdgeAlignedTop`, horní střed `Bottom`, dolní střed `Top`. Když se nevejde, Avalonia ho převrátí.
- Obsah:
  - název widgetu (`CameraLayoutPopoverTitle`),
  - **Zvýraznit** (`CameraLayoutEmphasize`, `ToggleSwitch` bez textů Zap / Vyp, `HelpText` „Klíčový údaj se zobrazí oranžově“),
  - **Velikost** jako segmentový přepínač: tři `RadioButton` se vzhledem přepínacího tlačítka v jedné skupině, 40 × 32, viditelné „S“, „M“, „L“, jména pro čtečku „Malá“ / „Střední“ / „Velká“ (`CameraLayoutSizeS|M|L`). Nepodporovaná velikost je zakázaná s tooltipem „Tuto velikost widget nemá“ (v1 podporují všechny všechny, příprava pro další widgety).
- Změny se hned projeví na kartě (pracovní kopie). Klik mimo popover nebo Esc ho zavře, výběr zůstane.

#### 5.5 Ovládání klávesnicí
| Klávesa | Kde | Akce |
|---|---|---|
| Tab / Shift+Tab | kdekoli | karty v pořadí čtení (TL, TC, TR, BL, BC, BR; v kotvě shora dolů podle obrazovky), pak tlačítka lišty zleva doprava |
| šipky | karta (ne zvednutá) | fokus na nejbližší kartu daným směrem |
| **Mezerník** | karta | **zvednout** kartu (režim přesunu) |
| ← / → | zvednutá karta | do sousední kotvy ve stejném řádku (TL ↔ TC ↔ TR, BL ↔ BC ↔ BR), na stejnou pozici (nebo na konec) |
| ↑ / ↓ | zvednutá karta | o jednu pozici v kotvě nahoru / dolů podle obrazovky. Za koncem horní kotvy pokračuje ↓ do dolní kotvy stejného sloupce na nejvyšší pozici, ↑ opačně |
| Home / End | zvednutá karta | první / poslední pozice v kotvě (podle obrazovky) |
| Mezerník / Enter | zvednutá karta | **pustit** (přesun do pracovní kopie) |
| Esc | zvednutá karta | zrušit přesun, karta zpět |
| Enter / F2 | karta (ne zvednutá) | otevřít popover, fokus na „Zvýraznit“ |
| Tab, mezerník, šipky | popover | Zvýraznit ↔ S/M/L (šipky mezi S/M/L) |
| Esc | otevřený popover | zavřít popover, fokus zpět na kartu |
| Esc | jinak | zrušit režim (zahodit změny) |
| Enter / mezerník | Hotovo / Zrušit / Obnovit | aktivace tlačítka |

- „Hotovo“ **není** `IsDefault` (Enter na kartě otevírá popover). Uložení klávesnicí = Tab na Hotovo + Enter.
- Zvednutí klávesnicí vypadá jako tažení myší: stín, cílová zóna, ukazatel pozice. Karta se posouvá skokem, bez sledování ukazatele.
- **Fokus:**
  - po vstupu z klávesnice (tlačítko nebo nabídka) je fokus na první kartě, po vstupu myší bez viditelného rámečku (`:focus-visible`),
  - rámeček fokusu karty: dvojitý Fluent (vnější 2 bílý, vnitřní 1 černý, odstup 3, poloměr karty + 3),
  - výběr (akcent, odstup 2) a fokus se nevylučují.
- **Čtečka obrazovky:**
  - karta v režimu má roli tlačítka se jménem „Stav, Vlevo nahoře, pozice 1 z 1“ (`CameraLayoutAnnouncePosition`) a `HelpText` s klávesami (`CameraLayoutKeyboardHint`). Popover je vzor Expand/Collapse,
  - oznámení přes skrytý `TextBlock` `CameraLayoutAnnouncement` s `AutomationProperties.LiveSetting="Polite"` (podporu v Avalonii 12 ověří ui-dev v T-091, záloha `AutomationPeer.RaiseAutomationEvent`):
    - vstup: „Režim rozložení. Mezerník zvedne widget, šipky ho přesunou, Enter otevře volby, Esc zahodí změny.“
    - zvednutí: „Stav zvednut. Šipkami vyberte místo, mezerníkem pusťte, Esc zruší.“
    - každý krok: „Stav: Vpravo nahoře, pozice 2 z 2“
    - puštění: „Stav přesunut: Vpravo nahoře, pozice 2“
    - zrušení: „Přesun zrušen“
    - Obnovit výchozí: „Obnoveno výchozí rozložení“
    - uloženo / zahozeno: „Rozložení uloženo“ / „Změny rozložení zahozeny“

#### 5.6 Platformy
- Windows: Fluent flyout, tlačítka lišty v pořadí Windows, Shift+F10 / klávesa Menu pro kontextovou nabídku.
- macOS (implementace a ověření později na Macu): stejné rozvržení, pořadí `[Zrušit][Hotovo]`, kontextová nabídka Ctrl+klikem, F2 = Fn+F2, Enter funguje stejně.

#### 5.7 `AutomationId` – přehled
- **Normální okno a celá obrazovka:**
  - kotvy `CameraOverlayAnchor{TopLeft|TopCenter|TopRight|BottomLeft|BottomCenter|BottomRight}`,
  - karty `CameraOverlayWidget-{id}` (id = `status`, `times`, `temperatures`, `print-name`, `cooldown`),
  - části karet:
    - Stav: `-Name`, `-State`, `-Percent`, `-Progress`,
    - Časy: `-Remaining`, `-Finish`, `-Elapsed`,
    - Teploty: `-Nozzle`, `-Bed`, `-Chart`,
    - Název tisku: `-Title`, `-Thumbnail`, `-Material`, `-LayerHeight`, `-NozzleDiameter`, `-Estimate`,
    - Vychladnutí: `-Title`, `-Nozzle`, `-Bed`, `-Progress`, `-Detail`,

    např. `CameraOverlayWidget-status-Percent`.
- **Tlačítka a nabídka:** `CameraLayoutButton`, `CameraContextMenu`, `CameraLayoutMenuItem`.
- **Režim rozložení:**
  - `CameraLayoutOverlay` (kořen), `CameraLayoutDim`, `CameraLayoutAnchor{Kotva}`, `CameraLayoutWidget-{id}`, `CameraLayoutInsertIndicator`,
  - lišta: `CameraLayoutToolbar`, `CameraLayoutHint`, `CameraLayoutReset`, `CameraLayoutDone`, `CameraLayoutCancel`, `CameraLayoutSaveError`,
  - popover: `CameraLayoutPopover`, `CameraLayoutPopoverTitle`, `CameraLayoutEmphasize`, `CameraLayoutSizeS`, `CameraLayoutSizeM`, `CameraLayoutSizeL`,
  - `CameraLayoutAnnouncement`.
- **Malý náhled:** `CameraMiniInfoStrip`, `CameraMiniProgress`, `CameraMiniSummary`, `CameraMiniToggleName`, `CameraMiniToggleChart`, `CameraMiniNameRow`, `CameraMiniChartRow`, `CameraMiniChart`.
- **Nastavení:**
  - Překryv: `PrinterOverlayFields`, `PrinterOverlayWidget-{id}`, `PrinterOverlayReset`, `PrinterOverlayApplyAll`, `PrinterOverlayLayoutHint`, `PrinterOverlayCameraOff`,
  - práh: `PrinterCooldownThreshold`, `PrinterCooldownThresholdHint`, `PrinterCooldownThresholdError`, `PrinterCooldownApplyAll`,
  - průhlednost: `SettingsOverlayOpacity`, `SettingsOverlayOpacityValue`,
  - upozornění: `SettingsNotifyCooledDown`.
- Staré `PrinterOverlay*Normal|Mini`, `PrinterOverlayCorner`, `PrinterOverlayNormalHeader`, `PrinterOverlayMiniHeader`, `PrinterOverlayHint` a `CameraPrintOverlay` zaniknou (T-081, T-092).

### 6. Ikony – Fluent UI System Icons
- Zdroj: https://github.com/microsoft/fluentui-system-icons, **licence MIT**, „Copyright (c) 2020 Microsoft Corporation“. Ověřeno 2026-10-24 v souboru https://github.com/microsoft/fluentui-system-icons/blob/main/LICENSE. MIT je kompatibilní s GPL-3.0.
- Vložení podle Q1: data `path` z SVG (`assets/<Název>/SVG/ic_fluent_<název>_<velikost>_<styl>.svg`) jako `StreamGeometry` v `ResourceDictionary` (klíč `Icon<Název><Velikost><Styl>`, např. `IconFlag16Regular`). Bez NuGet balíčku a bez fontu, za běhu žádná závislost. Ikony mají jednu barvu `Fill` (SVG `#212121` se nahradí štětcem). Licence MIT do přehledu licencí třetích stran v T-081.
- Existence každého souboru ověřena 2026-10-24 (HTTP 200 na `raw.githubusercontent.com/.../main/assets/...`).

| Účel | Ikona (složka `assets`) | Soubor | Kde |
|---|---|---|---|
| Zbývá (S) | Hourglass Half | `ic_fluent_hourglass_half_16_regular` (L: `_20_regular`) | Časy S |
| Čas dokončení | Flag | `ic_fluent_flag_16_regular` (L: `_20_regular`) | Časy M/L |
| Doba tisku | Clock | `ic_fluent_clock_16_regular` (L: `_20_regular`) | Časy M/L |
| Tryska | Cone (otočit o 180°) | `ic_fluent_cone_16_filled` (jen 16, v L zvětšit) | Teploty, Vychladnutí, malý náhled |
| Podložka | Line Horizontal 1 | `ic_fluent_line_horizontal_1_16_filled` (L: `_20_filled`) | Teploty, Vychladnutí, malý náhled |
| Teploty (widget) | Temperature | `ic_fluent_temperature_16_regular` | tabulka nastavení, zástupná karta |
| Název tisku | Cube | `ic_fluent_cube_16_regular` | Název tisku S, řádek názvu, přepínač, tabulka |
| Výška vrstvy | Layer | `ic_fluent_layer_20_regular` (16 neexistuje, vykreslit 12) | čip vrstvy |
| Materiál, průměr trysky | (bez ikony, text „PLA“, „tryska 0,4“ podle makety) | – | čipy |
| Chladne | Weather Snowflake | `ic_fluent_weather_snowflake_20_regular` (16 neexistuje; S zmenšit na 16, L `_24_regular`) | Vychladnutí, malý náhled |
| Vychladlo | Power | `ic_fluent_power_20_regular` (16 neexistuje; S zmenšit, L `_24_regular`) | Vychladnutí, malý náhled |
| Stav (widget) | Status | `ic_fluent_status_16_regular` | tabulka nastavení, zástupná karta |
| Časy (widget) | Timer | `ic_fluent_timer_16_regular` | tabulka nastavení, zástupná karta |
| Vychladnutí (widget) | Weather Snowflake | jako výše | tabulka nastavení |
| Rozložení | Layout Cell Four | `ic_fluent_layout_cell_four_16_regular` | tlačítko okna, lišta, položka nabídky |
| Graf v pásu | Data Trending | `ic_fluent_data_trending_16_regular` | přepínač malého náhledu |
| Zvýraznit | Highlight | `ic_fluent_highlight_16_regular` | (volitelně před textem v popoveru) |
| Tečka stavu | Circle | `ic_fluent_circle_16_filled` (nebo `Ellipse` 8 × 8) | pilulka, pás |

- Čitelnost 16 px: ikony `Regular` mají tah 1 px, na tmavé kartě v `#C9C9CE` čitelné. Zkontrolovat na snímcích T-081 a T-084, hlavně Cone a Line Horizontal 1.
- Volitelně, mimo rozsah: dnešní vlastní ikony tlačítek okna (celá obrazovka, malý náhled, tah 1,5) jdou nahradit `Full Screen Maximize` 16 a `Picture In Picture` 16, aby měla řada jednotnou tloušťku tahu. Nový úkol jen na přání uživatele.

### 7. Výchozí rozložení
Viz 3.3 (tabulka, hlavní kotva `BottomLeft`, zrcadlení do 4 rohů).

```
Výchozí rozložení, normální okno 960 × 540, během tisku
┌───────────────────────────────────────────────────────────────────────── [⊞][⤢][▣] ┐
│ ╭─────────────────╮      ╭──────────────────────────────────────────╮               │
│ │ MK4 dílna       │      │ ┌──┐ Foldable book holder v1             │               │
│ │ (● Tiskne)      │      │ └──┘ (PLA) (≋ 0,2 mm) (tryska 0,4)        │               │
│ │ 19 %            │ ←    ╰──────────────────────────────────────────╯               │
│ │ ▓▓▓▓░░░░░░░░░░░ │ akcent                                                          │
│ ╰─────────────────╯                                                                 │
│                                       (obraz)                                        │
│                                                                                      │
│ ╭─────────────────╮                                         ╭──────────────────────╮ │
│ │ Zbývá           │                                         │ ▽ 215/215 ━ 60/60 °C │ │
│ │ 2 h 35 min      │                                         │ [~~~~~~~~~~~~~~~~~~] │ │
│ │ ⚑ Hotovo v 19:50│                                         │ −60 min          teď │ │
│ │ ◷ 47 min        │                                         ╰──────────────────────╯ │
│ ╰─────────────────╯                                                                 │
└──────────────────────────────────────────────────────────────────────────────────────┘

Po dokončení (Časy zmizí, objeví se Vychladnutí, Název tisku zůstane)
│ ╭ Stav: (● Dokončeno) ╮        ╭ Název tisku ╮                                       │
│ ...                                                                                   │
│ ╭ ❄ Chladne…                      ╮                             ╭ Teploty ╮           │
│ │ ▽ 87 °C   ━ 45 °C               │                                                   │
│ │ ▓▓▓▓▓▓▓░░░░░░░░                 │                                                   │
│ │ bezpečné vypnutí za cca 6 min…  │                                                   │
```

### 8. Podzáložka Překryv v nastavení
Obsah 548 × ≈ 358. Dva sloupce: vlevo tiskárna (widgety), vpravo vychladnutí a globální průhlednost.

```
Výchozí (cs), kamera zapnutá
│ Tiskárna  Kamera  Okno kamery  [Překryv]                                                 │
│                                                                                          │
│ Widgety v okně kamery                │  Vychladnutí po tisku                              │ 14 SemiBold
│ [x] ◉ Stav                           │  Práh teploty trysky (°C)                          │
│ [x] ⏲ Časy                           │  [ 50      ][^][v]                                 │ NumericUpDown 120
│ [x] 🌡 Teploty                        │  Pod touto teplotou je tiskárnu bezpečné vypnout.  │ hint 12
│ [x] ⬡ Název tisku                    │  Používá ho widget Vychladnutí a upozornění.       │
│ [x] ❄ Vychladnutí                    │  [Použít práh u všech tiskáren]                    │
│                                      │                                                    │
│ [Obnovit výchozí]                    │  Všechny tiskárny                                  │ 14 SemiBold
│ [Použít rozložení u všech tiskáren]  │  Neprůhlednost pozadí karet                        │
│                                      │  [━━━━━━━━━━━━━●━━━━━━]  72 %                      │ Slider + hodnota
│ Polohu, pořadí, velikost a zvýraznění│  Platí pro okna kamer všech tiskáren. Vyšší        │
│ upravíte v okně kamery tlačítkem ⊞   │  hodnota = čitelnější text na světlém obraze.      │
│ Upravit rozložení.                   │                                                    │
   ← 232 →                         24     ← 292 →

Nejdelší jazyk (de), kamera vypnutá
│ Widgets im Kamerafenster             │  Abkühlen nach dem Druck                           │
│ [ ] ◉ Status            (zakázané)   │  Temperaturschwelle der Düse (°C)                  │
│ [ ] ⏲ Zeiten                         │  [ 50      ][^][v]                                 │
│ [ ] 🌡 Temperaturen                   │  Unter dieser Temperatur kann der Drucker sicher   │
│ [ ] ⬡ Druckname                      │  ausgeschaltet werden. Gilt für das Widget         │
│ [ ] ❄ Abkühlung                      │  „Abkühlung“ und die Benachrichtigung.             │
│ [Standard wiederherstellen]          │  [Schwelle für alle Drucker übernehmen]            │
│ [Für alle Drucker übernehmen]        │                                                    │
│ Die Kamera ist ausgeschaltet; die    │  Alle Drucker                                      │
│ Einstellungen gelten nach dem        │  Deckkraft des Kartenhintergrunds                  │
│ Einschalten.                         │  [━━━━━━━━━━━━━●━━━━━━]  72 %                      │
│                                      │  Gilt für die Kamerafenster aller Drucker. …       │
```
- **Levý sloupec (232):**
  - nadpis sekce „Widgety v okně kamery“ (`TextBlock.section` bez horního okraje),
  - pak 5 řádků `CheckBox` (výška 32). Obsah = ikona 16 + 8 + název widgetu. Pořadí: Stav, Časy, Teploty, Název tisku, Vychladnutí (stálé, ne podle kotev),
  - `AutomationProperties.Name` = název widgetu, `AutomationId` `PrinterOverlayWidget-{id}`,
  - pod tabulkou 12 a dvě tlačítka pod sebou (mezera 8, šířka podle textu, nejvýš sloupec, text se smí zalomit): „Obnovit výchozí“ (`PrinterOverlayReset`) a „Použít rozložení u všech tiskáren“ (`PrinterOverlayApplyAll`),
  - pod tlačítky 8 a nápověda 12 (`PrinterOverlayLayoutHint`). Při vypnuté kameře ji nahradí `SettingsCameraWindowCameraOff` (`PrinterOverlayCameraOff`).
  - Výška: 20 + 4 + 5 × 32 + 12 + 32 + 8 + 32 + 8 + 3 × 16 = **324** ≤ 358.
- **Pravý sloupec (292):**
  - sekce „Vychladnutí po tisku“: popisek nad polem, `NumericUpDown` 120 (30–100, krok 1, `FormatString` „0“), nápověda, při chybě místo nápovědy `TextBlock.error` „Zadejte celé číslo od 30 do 100.“ (vzor T-033: chyba nahrazuje nápovědu), pak 8 a tlačítko „Použít práh u všech tiskáren“,
  - sekce „Všechny tiskárny“ (nadpis s horním okrajem 16; nadpis sám naznačuje, že nastavení není u tiskárny): popisek, řádek `Slider` (30–95, `SmallChange` 1, `LargeChange` 5, `TickFrequency` 5, `IsSnapToTickEnabled=False`) + hodnota 48 vpravo (procenta podle kultury: „72 %“ cs / „72%“ en, tabulková čísla), nápověda.
  - Výška: 20 + 4 + 20 + 4 + 32 + 4 + 48 + 8 + 32 + 16 + 20 + 4 + 20 + 4 + 32 + 4 + 32 = **304** (de s 3řádkovou nápovědou ≈ 336) ≤ 358.
- **Stavy:**
  - kamera vypnutá: zakázaná jen tabulka widgetů a tlačítko Obnovit výchozí. Hodnoty zůstanou (jako T-036). „Použít rozložení u všech tiskáren“ zůstane povolené. Práh a průhlednost zůstanou povolené (práh potřebuje i upozornění bez kamery, průhlednost je globální, rozhodnutí T-092);
  - jediná tiskárna: obě tlačítka „Použít … u všech tiskáren“ jsou **zakázaná** (ne skrytá, rozvržení neposkočí) s tooltipem „Je nastavená jen jedna tiskárna.“ (`ToolTip.ShowOnDisabled`);
  - po „Použít … u všech tiskáren“ ukáže stavový řádek okna „Rozložení se zkopírovalo do ostatních tiskáren. Projeví se po uložení.“ / „Práh se zkopíroval do ostatních tiskáren. Projeví se po uložení.“ (bez počtu, kvůli tvarům množného čísla).
- **Pořadí Tab:** 5 zaškrtávacích polí → Obnovit výchozí → Použít rozložení u všech → práh → Použít práh u všech → posuvník.
- **Proč práh na podzáložce Překryv:** podzáložka Tiskárna je plná (T-033) a Okno kamery se týká okna. Práh patří k tiskárně, k widgetu i k upozornění. Je mimo zakázanou tabulku, takže ho jde nastavit i bez kamery. Upozornění na něj odkazuje přes `HelpText`.
- Riziko na ověření v T-080 a T-092 (headless test „bez posuvníku × 8 jazyků × stavy“): pravý sloupec v de / fr s chybou prahu.

### 9. Notifikace „vychladlo“
- **Toast** (`ToastContentBuilder`, stávající pravidla):
  - titulek = název tiskárny,
  - 2. řádek `NotificationCooledDown`: cs „Vychladlo – můžete tiskárnu vypnout“, en „Cooled down – safe to turn off“,
  - 3. řádek `NotificationCooledDownDetail`: cs „Tryska {0} °C, práh {1} °C“, en „Nozzle {0} °C, threshold {1} °C“ (místo názvu souboru; nezlomitelná mezera před °C),
  - kliknutí jako u ostatních toastů (web tiskárny),
  - informační závažnost (T-080).

  Ostatní jazyky navrhne ui-dev, max. 60 znaků na řádek.
- **Přepínač** v záložce Obecné, levý sloupec upozornění, **za „Tisk zastaven (zrušen)“** (navazuje na průběh tisku: dokončen → zastaven → vychladlo):
  - „Tiskárna vychladla“ (`SettingsNotifyCooledDown`), výchozí vypnuto,
  - `AutomationProperties.HelpText` „Práh teploty trysky se nastavuje u tiskárny na podzáložce Překryv.“
  - Levý sloupec tím má 6 řádků (192), pravý ≈ 175–195 (s chybou zbývajícího času). Výška řádku mřížky se tak zvýší nejvýš o ≈ 16 a jen bez chyby, kdy je dnes rezerva ≈ 23 (snímek `settings-general.png`). Ověřit testem `SettingsWindowLayoutTests` ve všech 8 jazycích v T-080. Záloha: přepínač do pravého sloupce za „Tiskárna je offline…“.

### 10. Nové texty (klíče `Strings.*`)
cs = vykání (otázka Q3). Ostatní jazyky doplní ui-dev v příslušném úkolu. Sloupec „Max.“ = doporučená max. délka v nejdelším jazyce (znaky).

| Klíč | cs | en | Max. | Úkol |
|---|---|---|---|---|
| `CameraOverlayWidgetStatus` | Stav | Status | 16 | T-081 |
| `CameraOverlayWidgetTimes` | Časy | Times | 16 | T-081 |
| `CameraOverlayWidgetTemperatures` | Teploty | Temperatures | 16 | T-081 |
| `CameraOverlayWidgetPrintName` | Název tisku | Print name | 18 | T-081 |
| `CameraOverlayWidgetCooldown` | Vychladnutí | Cool-down | 18 | T-081 |
| `CameraOverlayRemainingLabel` | Zbývá | Remaining | 14 | T-083 |
| `CameraOverlayChartStart` | −60 min | −60 min | 8 | T-084 |
| `CameraOverlayChartNow` | teď | now | 8 | T-084 |
| `CameraOverlayChartNoData` | Žádná data | No data | 14 | T-084 |
| `CameraOverlayChartName` | graf posledních 60 minut | chart of the last 60 minutes | – | T-084 |
| `CameraOverlayNozzleChip` | tryska {0} | nozzle {0} | 14 | T-085 |
| `CameraOverlayLayerHeightName` | výška vrstvy {0} mm | layer height {0} mm | – | T-085 |
| `CameraOverlayNozzleDiameterName` | tryska {0} mm | nozzle {0} mm | – | T-085 |
| `CameraOverlaySlicerEstimate` | odhad sliceru {0} | slicer estimate {0} | 32 | T-085 |
| `CameraOverlayThumbnailName` | Náhled tisku | Print preview | – | T-087 |
| `CameraOverlayCooling` | Chladne… | Cooling down… | 18 | T-088 |
| `CameraOverlayCooled` | Vychladlo – můžete vypnout | Cooled down – safe to turn off | 32 | T-088 |
| `CameraOverlayCooldownEstimate` | bezpečné vypnutí za cca {0} (tryska pod {1} °C) | safe to turn off in about {0} (nozzle below {1} °C) | 64 | T-088 |
| `CameraOverlayCooldownNoEstimate` | bezpečné vypnutí, až tryska klesne pod {0} °C | safe to turn off once the nozzle is below {0} °C | 64 | T-088 |
| `CameraOverlayCooldownFinishedAt` | tisk dokončen v {0} | print finished at {0} | 30 | T-088 |
| `CameraOverlayCooldownStoppedAt` | tisk zastaven v {0} | print stopped at {0} | 30 | T-088 |
| `CameraOverlayCooldownCooledIn` | vychladlo za {0} | cooled down in {0} | 26 | T-088 |
| `CameraMiniCooled` | Vychladlo · {0} °C | Cooled down · {0} °C | 22 | T-089 |
| `CameraMiniToggleName` | Název tisku v pásu | Print name in the strip | – | T-090 |
| `CameraMiniToggleChart` | Graf teplot v pásu | Temperature chart in the strip | – | T-090 |
| `CameraMiniShowName` / `CameraMiniHideName` | Zobrazit název tisku / Skrýt název tisku | Show print name / Hide print name | 40 | T-090 |
| `CameraMiniShowChart` / `CameraMiniHideChart` | Zobrazit graf teplot / Skrýt graf teplot | Show temperature chart / Hide temperature chart | 40 | T-090 |
| `CameraMiniNameNoJob` | Název tisku se zobrazí během tisku | The print name appears during a print | 50 | T-090 |
| `CameraLayoutEdit` | Upravit rozložení | Edit layout | 28 | T-091 |
| `CameraLayoutTitle` | Režim rozložení | Layout mode | 24 | T-091 |
| `CameraLayoutHint` | přetáhněte widget do kotvy | drag a widget to an anchor | 48 | T-091 |
| `CameraLayoutKeyboardHint` | Mezerník zvedne widget, šipky ho přesunou, Enter otevře volby, Esc zahodí změny. | Space picks a widget up, arrow keys move it, Enter opens its options, Esc discards the changes. | – | T-091 |
| `CameraLayoutReset` | Obnovit výchozí | Restore defaults | 26 | T-091, T-092 |
| `CameraLayoutDone` | Hotovo | Done | 12 | T-091 |
| (Zrušit) | znovu použít `SettingsCancel` | | | T-091 |
| `CameraLayoutDropHere` | Pustit sem | Drop here | 20 | T-091 |
| `CameraLayoutNoData` | teď bez dat | no data right now | 24 | T-091 |
| `CameraLayoutEmphasize` | Zvýraznit | Highlight | 20 | T-091 |
| `CameraLayoutEmphasizeHelp` | Klíčový údaj se zobrazí oranžově. | Shows the key value in orange. | – | T-091 |
| `CameraLayoutSize` | Velikost | Size | 14 | T-091 |
| `CameraLayoutSizeSmall` / `Medium` / `Large` | Malá / Střední / Velká | Small / Medium / Large | – | T-091 |
| `CameraLayoutSizeUnsupported` | Tuto velikost widget nemá. | This widget has no such size. | 40 | T-091 |
| `CameraLayoutAnchorTopCenter` | Nahoře uprostřed | Top center | 22 | T-091 |
| `CameraLayoutAnchorBottomCenter` | Dole uprostřed | Bottom center | 22 | T-091 |
| (rohy) | znovu použít `SettingsCameraWindowCorner*` | | | T-091 |
| `CameraLayoutAnnounceEnter` | Režim rozložení. {0} | Layout mode. {0} | – | T-091 |
| `CameraLayoutAnnounceLifted` | {0} zvednut. Šipkami vyberte místo, mezerníkem pusťte, Esc zruší. | {0} picked up. Use the arrow keys to choose a place, Space to drop, Esc to cancel. | – | T-091 |
| `CameraLayoutAnnouncePosition` | {0}: {1}, pozice {2} z {3} | {0}: {1}, position {2} of {3} | – | T-091 |
| `CameraLayoutAnnounceDropped` | {0} přesunut: {1}, pozice {2} | {0} moved: {1}, position {2} | – | T-091 |
| `CameraLayoutAnnounceCancelled` | Přesun zrušen | Move cancelled | – | T-091 |
| `CameraLayoutAnnounceReset` | Obnoveno výchozí rozložení | Default layout restored | – | T-091 |
| `CameraLayoutAnnounceSaved` / `Discarded` | Rozložení uloženo / Změny rozložení zahozeny | Layout saved / Layout changes discarded | – | T-091 |
| `CameraLayoutSaveError` | Rozložení se nepodařilo uložit. Zkuste to znovu. | The layout could not be saved. Try again. | 70 | T-091 |
| `CameraLayoutTooSmall` | Pro úpravu rozložení zvětšete okno. | Enlarge the window to edit the layout. | 60 | T-091 (podle Q4) |
| `SettingsOverlayWidgets` | Widgety v okně kamery | Camera window widgets | 30 | T-092 |
| `SettingsOverlayApplyAll` | Použít rozložení u všech tiskáren | Use layout for all printers | 34 | T-092 |
| `SettingsOverlayApplyAllSingle` | Je nastavená jen jedna tiskárna. | Only one printer is set up. | – | T-092 |
| `SettingsOverlayAppliedAll` | Rozložení se zkopírovalo do ostatních tiskáren. Projeví se po uložení. | The layout was copied to the other printers. It applies after saving. | 90 | T-092 |
| `SettingsOverlayLayoutHint` | Polohu, pořadí, velikost a zvýraznění upravíte v okně kamery tlačítkem Upravit rozložení. | Change position, order, size and highlighting in the camera window with Edit layout. | 120 | T-092 |
| `SettingsOverlayAllPrinters` | Všechny tiskárny | All printers | 24 | T-092 |
| `SettingsOverlayOpacity` | Neprůhlednost pozadí karet | Card background opacity | 34 | T-092 (Q2) |
| `SettingsOverlayOpacityHint` | Platí pro okna kamer všech tiskáren. Vyšší hodnota = čitelnější text na světlém obraze. | Applies to the camera windows of all printers. Higher values keep text readable on bright video. | 130 | T-092 |
| `SettingsCooldownSection` | Vychladnutí po tisku | Cool-down after printing | 30 | T-080 |
| `SettingsCooldownThreshold` | Práh teploty trysky (°C) | Nozzle temperature threshold (°C) | 36 | T-080 |
| `SettingsCooldownThresholdHint` | Pod touto teplotou je tiskárnu bezpečné vypnout. Používá ho widget Vychladnutí a upozornění. | Below this temperature the printer is safe to turn off. Used by the Cool-down widget and notification. | 130 | T-080 |
| `SettingsCooldownThresholdError` | Zadejte celé číslo od {0} do {1}. | Enter a whole number from {0} to {1}. | 60 | T-080 |
| `SettingsCooldownApplyAll` | Použít práh u všech tiskáren | Use threshold for all printers | 34 | T-080 |
| `SettingsCooldownAppliedAll` | Práh se zkopíroval do ostatních tiskáren. Projeví se po uložení. | The threshold was copied to the other printers. It applies after saving. | 90 | T-080 |
| `SettingsNotifyCooledDown` | Tiskárna vychladla | Printer cooled down | 34 | T-080 |
| `SettingsNotifyCooledDownHelp` | Práh teploty trysky se nastavuje u tiskárny na podzáložce Překryv. | The nozzle temperature threshold is set per printer on the Overlay sub-tab. | – | T-080 |
| `NotificationCooledDown` | Vychladlo – můžete tiskárnu vypnout | Cooled down – safe to turn off | 60 | T-080 |
| `NotificationCooledDownDetail` | Tryska {0} °C, práh {1} °C | Nozzle {0} °C, threshold {1} °C | 60 | T-080 |

- Znovu použité: `State*`, `StateConnecting`, `ToolTipRemaining`, `CameraOverlayFinishTime`, `CameraOverlayFinishDateTime`, `CameraOverlayPrintTime`, `CameraOverlayTemperatureWithTarget`, `DiagnosticsNozzle`, `DiagnosticsBed`, `SettingsCancel`, `SettingsCameraWindowCorner*`, `SettingsCameraWindowCameraOff`, `SettingsPrinterTabOverlay`.
- Zaniknou (T-081, T-092): `SettingsOverlayCorner`, `SettingsOverlayNormalColumn`, `SettingsOverlayItem*`, `SettingsOverlayHint` a `CameraOverlayLabeledValue`, pokud ho už nic nepoužívá.
- Před „°C“ a „%“ vždy nezlomitelná mezera U+00A0. „…“ je jeden znak U+2026, pomlčka „–“ U+2013, mínus „−“ U+2212.

### 11. Odkazy na zásady platforem
- Windows:
  - Fluent 2 / Windows app design: mřížka 4 / 8, ovládací prvky výšky 32, výchozí fokus (dvojitý rámeček), pořadí tlačítek potvrdit / zrušit vpravo,
  - kontextové nabídky (Shift+F10),
  - typografie Segoe UI Variable (Caption 12, Body 14, Subtitle 20, Title 28),
  - kontrast WCAG 2.1 AA.
- macOS HIG: popovery, kontextové nabídky (Ctrl+klik), pořadí tlačítek Zrušit / OK, SF Pro s tabulkovými číslicemi.
- Klávesnicové tažení podle zavedeného vzoru „zvednout mezerníkem – přesunout šipkami – pustit mezerníkem – Esc zruší“ (ARIA Authoring Practices, přístupné drag & drop).
- **Neověřeno:** skutečné Windows DPI 125 / 150 / 200 % a monitory s různým DPI (T-093), macOS (později na Macu), skutečné video kamery (kontrast na světlém obraze), Avalonia 12 `FontFeatures` a `AutomationProperties.LiveSetting` (ověří ui-dev v T-081 / T-091).

## Otevřené otázky
Q1–Q10 z Rozhodnutí jsou vyřešené. Hotový návrh čeká na schválení. Návrh otevřel tyto otázky:

- **Q1 – Výchozí rozložení a hlavní kotva:** maketa má Stav vlevo nahoře a Časy vlevo dole. T-077 počítal se „Stav + Časy v jedné kotvě“.
  - (a) **Doporučuji:** držet maketu (oddíl 3.3). Hlavní kotva = `BottomLeft` = kotva Časů, Stav je ve stejném sloupci nahoře. Zrcadlení přesune celý levý sloupec na stranu starého rohu. T-077 upraví jen definici „hlavní kotvy“ (kotva widgetu Časy).
  - (b) Stav i Časy pod sebou v `BottomLeft` (jako dnešní překryv T-036). Odchylka od schválené makety, levý horní roh zůstane prázdný.
- **Q2 – Název nastavení průhlednosti:** hodnota 72 % je krytí (alfa 0,72), ne průhlednost. „Průhlednost 72 %“ by znamenala opak.
  - (a) **Doporučuji:** popisek „Neprůhlednost pozadí karet“ / „Card background opacity“, hodnoty 30–95 %, výchozí 72 % beze změny.
  - (b) Popisek „Průhlednost pozadí karet“ s obráceným rozsahem 5–70 %, výchozí 28 % (uložená hodnota by byla 100 − krytí). Zbytečně mate.
  - (c) Posuvník bez čísla s krajními popisky „průhlednější“ / „sytější“.
- **Q3 – Tón českých textů:** aplikace vyká („Zadejte…“), maketa tyká („můžeš vypnout“, „přetáhni“).
  - (a) **Doporučuji:** vykat jako zbytek aplikace („Vychladlo – můžete vypnout“, „přetáhněte widget do kotvy“). Návrh je takto napsaný.
  - (b) Tykat jen v nových textech překryvu. Nekonzistentní.
  - (c) Přejít na tykání v celé aplikaci (samostatný úkol pro všech 8 jazyků).
- **Q4 – Režim rozložení v malém okně** (normální okno jde zmenšit až na 160 × 90):
  - (a) **Doporučuji:** pod 480 × 300 je tlačítko i položka nabídky zakázané s tooltipem „Pro úpravu rozložení zvětšete okno.“
  - (b) Vstup okno dočasně zvětší na 640 × 360 a po skončení vrátí. Okno se mění bez přímého pokynu uživatele.
  - (c) Režim povolit vždy, zóny se zmenší až na 120 × 70. Hůř se trefuje.
- **Q5 – Velikost přepínačů v pásu malého náhledu:** schválený základní řádek je 30 vysoký, doporučený cíl je ≥ 32 DIP.
  - (a) **Doporučuji:** přepínače 30 × 30 (celá výška řádku) a přijmout odchylku 2 DIP. Rozměry okna 320 × 213 / 311 podle makety zůstanou.
  - (b) Řádek 32 → okno 320 × 215 / 313.

## Poznámky k implementaci
- 2026-10-24, ui-designer: návrh (oddíly 0–11) napsaný do sekce Návrh UI. Úkol zůstává v `in-progress`, dokud uživatel návrh neschválí a nerozhodne otázky Q1–Q5.
- Měřeno v kódu a na snímcích reference (`tests/LayerPeek.App.UiTests/Snapshots`: `camera-overlay-*.png`, `settings-overlay*.png`, `settings-general*.png`):
  - rozměry okna kamery, nastavení a podzáložky,
  - barvy tray ikony a grafu Diagnostiky,
  - texty všech 8 jazyků.
- Kontrast spočítaný podle WCAG 2.x (relativní jas, alfa karty smíchaná s černým, šedým a bílým videem).
- Fluent UI System Icons: licence MIT ověřená v `LICENSE` repozitáře (Copyright (c) 2020 Microsoft Corporation). Existence každého jmenovaného souboru SVG ověřena (HTTP 200). Žádný balíček ani font, žádná závislost za běhu, takže otázka AOT nevzniká.
- Pro jiné úkoly:
  - T-077: hlavní kotva = kotva widgetu Časy (`BottomLeft`, Q1). Pořadí v kotvě = od okraje okna ke středu. Výchozí rozložení podle 3.3.
  - T-079 / T-088: `CooldownState` potřebuje čas přechodu do „Vychladlo“ a důvod začátku (dokončen / zastaven) pro popisek „tisk dokončen v 19:52 · vychladlo za 14 min“.
  - T-084: změna tmavých barev grafu Diagnostiky (`#FF8A4C` → `#FF8A3D`, `#4DABF7` → `#6CB4FF`). Snímky reference Diagnostiky přegenerovat.
- Kód ani XAML se v tomto úkolu nemění (návrh, implementace v T-077 až T-093). Build a testy se proto nespouštěly.
