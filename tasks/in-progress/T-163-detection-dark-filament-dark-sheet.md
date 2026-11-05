---
id: T-163
title: Detekce chyb: tmavý filament na tmavém plátu
status: in-progress
owner: core-dev
priority: P3
platform: all
depends_on: [T-155]
---

## Cíl
První detektor „Změna na podložce“ (`bed-change`, T-155) porovnává jas pozadí ve výřezu podložky. U tmavého (černého, tmavě šedého) filamentu na černém tiskovém plátu je kontrast malý a detektor je méně spolehlivý. Spike T-152 to uvedl jako známé omezení (otázka 11). Uživatel 2026-11-04 rozhodl: zatím jen známé omezení v popisu detektoru, ale později se má vyřešit – to je tento úkol.

## Rozsah
- Změřit kontrast tmavého filamentu na černém plátu na záběru z Core One (Buddy3D, standardní držák, LED komory) proti světlému filamentu (prototyp T-152: skóre a podíl změněných pixelů při prahu 25 úrovní).
- Navrhnout a ověřit úpravu bez nové závislosti (čistý C#, Native AOT), např.:
  - lokální normalizace kontrastu ve výřezu nebo adaptivní práh podle šumu pozadí místo pevných 25 úrovní,
  - příznaky z hran / gradientu (textura spleti filamentu, okraje dílu) místo samotného jasu,
  - odraz světla LED na lesklém filamentu (malé jasné body),
  - případně rozšíření `DetectionFrameSpec` (např. větší rozlišení výřezu), pokud to výkon dovolí.
- Úprava buď v detektoru `bed-change` (volba / automatické přepnutí podle kontrastu pozadí), nebo samostatný detektor – vybere core-dev podle měření a zapíše do poznámek.
- Výkon a alokace v mezích T-153 / T-155 (ustálený stav bez alokací, vzorek po 2–5 s).
- ML odložil uživatel 2026-11-04 (otázka 12 v T-152), tento úkol ho nepoužívá.

## Akceptační kritéria
- [x] Změřený kontrast a skóre tmavého filamentu na černém plátu zapsané v poznámkách (data podle otázky 1).
- [ ] Syntetická / inscenovaná selhání s tmavým filamentem na černém plátu se odhalí, úspěšné tisky (světlý i tmavý filament) nehlásí poplach při výchozí citlivosti (testy). – úspěšné tisky splněno (0 poplachů bez režimu i s režimem „Tmavý filament“); odhalení na tmavých plátech 82–84 %, s režimem 85–86 % (sada nejméně 77 %) místo ≥ 90 %, viz poznámky a otázka 6
- [x] Výsledky T-155 pro světlý filament se nezhorší (stejná testovací sada). – s vypnutým režimem (výchozí) se detektor chová stejně, celý běh T-166 vyšel bajt po bajtu stejně; s režimem 1 594 / 1 680 místo 1 586 (Core One 2 s)
- [x] Popis detektoru v UI (T-154) upravený, známé omezení odstraněné nebo zpřesněné, texty ve všech jazycích. – rozhodnutí uživatele 2026-11-05: text omezení ponechat beze změny; nová volba „Tmavý filament“ má texty ve všech 8 jazycích
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
Otázky 1 a 2 rozhodl uživatel 2026-11-04, otázky 3 a 5 uživatel 2026-11-05. Otázka 4 zůstává otevřená (neblokuje),
otázka 6 je nová a blokuje uzavření (kritérium 2).

1. ~~**Data:** odkud záběry tmavého filamentu na černém plátu? Záběry z vlastních tisků uživatel plánovaným způsobem nepořizuje (rozhodnutí 2026-11-04 k T-152). Možnosti: inscenovaná scéna (tmavý díl nebo chomáč tmavého filamentu na plátu v nečinné tiskárně se zapnutým osvětlením a nahráváním T-145), automatická nahrávka běžného tisku tmavým filamentem, syntetické varianty (přebarvení dílu na fixture snímcích – méně věrné kvůli odleskům).~~
   **Rozhodnuto 2026-11-04:** zatím jen syntetická data (přebarvení dílu a filamentu na fixture snímcích); meze věrnosti
   (odlesky a lesk na černém PEI a černém filamentu) uvést v poznámkách.
2. ~~**Které kombinace podporovat:** jen černý filament na černém (texturovaném / hladkém) plátu, nebo i další málo kontrastní kombinace (např. bílý filament na světlém plátu)?~~
   **Rozhodnuto 2026-11-04:** jen černý filament na černém plátu (texturovaném i hladkém, pokud to fixture dovolí).
   Další málo kontrastní kombinace (bílý na světlém) zůstávají na později.
3. ~~**Cíl přesnosti pro černé na černém:** na syntetické sadě detektor najde 82–84 % (nejhorší sada 77 %) a je skoro na
   stropu svého pravidla (dokonalý detektor stejného pravidla 84–85 %). Cíl ≥ 90 % na každé sadě by šel jen se ztrátou
   rezervy na skutečných datech (níž v poznámkách: nejlepší vyzkoušená varianta 2,5× místo 9×). Možnosti:
   a) přijmout současný stav a omezení jen zpřesnit (doporučuji, otázka 5),
   b) volitelný režim „tmavý filament“ u tiskárny (např. `ChangeLevel` 10 jen v tomto režimu: černé na černém medián
      89 %, nejhorší sada 80–84 %, ale skutečná série jen 1,2× pod prahem, tedy riziko falešných poplachů),
   c) počkat na skutečná / inscenovaná data tmavého filamentu (otázka 4) a rozhodnout podle nich.~~
   **Rozhodnuto 2026-11-05:** volitelný režim / profil „Tmavý filament“ u tiskárny, výchozí vypnutý, označený
   „Experimentální“. Uživatel upozornil, že Prusa černý plát nemá a pláty mají různé barvy (oddíl „Pláty Prusa“ níž).
4. **Skutečná data (otevřené, neblokuje):** jas černého filamentu v obraze (zvoleno předem 20–60 při plátu ~50–90) i jeho odlesky jsou
   odhad. Má se později pořídit inscenovaná scéna (chomáč a hrouda černého filamentu na černém plátu, LED zapnuté,
   nahrávka T-145), nebo zůstat u syntetiky? Bez ní nelze věrohodně říct, kolik selhání se ve skutečnosti pozná.
5. ~~**Text omezení v UI:** současný text („méně spolehlivé u tmavého filamentu na černém plátu“) měření potvrzuje.
   Ponechat (navrhuji), nebo zpřesnit, např. „hrouda nebo chomáč tmavého filamentu se pozná jen tam, kde je výrazně
   tmavší než plát“? Změna by šla do všech 8 `Strings.*.resx`.~~
   **Rozhodnuto 2026-11-05:** současný text ponechat.
6. **Přijmout naměřenou přesnost jako splnění kritéria 2?** Černý filament na tmavém plátu: 82–84 % (s režimem
   85–86 %), na světlejších plátech 87–89 % (s režimem 89–91 %); nejhorší sada 77–84 %. Zbytek jsou z velké části
   selhání, která na obraze nejsou vidět ani pro dokonalý detektor stejného pravidla (černé na černém dílu, na tmavé podlaze
   mimo plát, jas filamentu skoro jako plát). Možnosti: a) přijmout a úkol uzavřít (doporučuji, omezení v UI zůstává),
   b) nechat otevřené do skutečných dat (otázka 4). Do rozhodnutí zůstává úkol v `in-progress/`.

## Poznámky k implementaci
Stav: **rozpracováno** jen kvůli otázce 6 (přijetí přesnosti), implementace režimu „Tmavý filament“ je hotová
a otestovaná. Oddíly níž: nejdřív první kolo (2026-11-04, jen měření), pak druhé kolo po zpětné vazbě uživatele
(2026-11-05: barvy plátů, režim).

### Druhé kolo (2026-11-05): pláty Prusa a režim „Tmavý filament“

- **Pláty Prusa (obchod prusa3d.com, 2026-11-05):**
  - Pro CORE One / MK4 se prodávají: Smooth PEI (https://www.prusa3d.com/product/core-one-smooth-pei-print-sheet/,
    https://www.prusa3d.com/product/smooth-pei-print-sheet/ – „The steel sheet color may differ from the product
    picture (black, gold, green, etc)“, hladký povrch), Satin Powder-coated (https://www.prusa3d.com/product/satin-powder-coated-print-sheet/,
    https://www.prusa3d.com/product/core-one-satin-powder-coated-print-sheet/ – „lightly textured matte finish“, mezi
    hladkým a texturovaným), Textured Powder-coated (https://www.prusa3d.com/product/textured-powder-coated-print-sheet/ –
    „double-sided textured PEI powder-coated“, výrazná textura), PA Nylon Powder-coated
    (https://www.prusa3d.com/product/pa-nylon-powder-coated-print-sheet/ – drsnější povrch, časem lesklejší místa)
    a HighTemp (https://www.prusa3d.com/product/hightemp-print-sheet/ – „Rough texture“, pro Core One s HT hotendem).
    Přehled: https://www.prusa3d.com/category/print-sheets/.
  - Texty obchodu **barvu plátů neuvádějí** (kromě hladkého PEI: černá, zlatá, zelená ocel podle šarže). Barvy
    texturovaného (zlatý / bronzový) a ostatních jsou z fotek a obecné znalosti, nejsou ověřené textem.
  - Core One+ (https://www.prusa3d.com/product/prusa-core-one/) má v balení „Smooth PEI Print Sheet“.
  - **Plát uživatele** (fixture, `~/LayerPeek-samples/coreone-frames` i `coreone-frames-job354`): tmavý matný plát se
    světlou čárkovanou mřížkou a pruhem s logem „ORIGINAL PRUSA“ vpravo; scéna mimo díl p10 / p50 / p90 = 15 / 60 / 132
    (vpředu odlesk LED). Nejspíš přibalený hladký PEI na černé oceli, případně satin – z 320 × 180 to nejde poznat,
    v kódu se proto jmenuje „fixture sheet (dark, printed grid)“. Uživatel má pravdu, že čistě černý plát Prusa
    neprodává: i „tmavý“ plát má v obraze jas ~50–90 vpředu.
- **Syntetická pozadí podle plátu** (`SheetLook`, `DarkScene.Sheets`, černý díl na všech): plát fixture s mřížkou;
  tmavý plát bez značek (PA Nylon, hladký PEI na černé oceli); hladký PEI zelený (jas 1,6× plátu fixture, zrno σ 1);
  hladký PEI zlatý (2×, σ 1); texturovaný PEI zlatý (2,3×, zrno σ 6). Přebarví se jen obrys plátu (průběh světla
  zůstává z fixture), světlejší pláty bez mřížky. **Meze věrnosti:** zesílení jasu je odhad (texty obchodu jas neuvádějí),
  lesk hladkého PEI a odlesk LED pásku se nemodelují, zrno se nehýbe s podložkou v Z, odlesk LED vpředu se na zlatých
  plátech saturuje (255). Rozdělení selhání beze změny (stejné druhy, velikosti, počty, jen barvy).
- **Kontrast podle plátu** (výřez Core One, bez šumu, 12 semínek; medián rozdílu jasu špagety / hroudy / díl;
  dokonalý detektor pravidla 14 úrovní):

  | Plát | Scéna p50 | Medián rozdílu | Dokonalý detektor (celkem) |
  |---|---|---|---|
  | fixture, tmavý s mřížkou | 60 | 22–24 / 31 / 21 | 84,7 % |
  | tmavý bez značek | 58 | 20–22 / 28 / 17 | 84,0 % |
  | hladký PEI zelený | 90 | 45–47 / 61 / 36 | 88,4 % |
  | hladký PEI zlatý | 110 | 66–68 / 85 / 46 | 90,5 % |
  | texturovaný PEI zlatý | 125 | 81–83 / 103 / 40 | 89,9 % |

  Na světlejších plátech je černý filament vidět 2–4× lépe. Strop ~90 % dělají hroudy mimo plát (tmavá podlaha vlevo)
  a na černém dílu, kde černý filament není vidět na žádném plátu.
- **Režim „Tmavý filament“ (implementace):**
  - `PrinterDetectionSettings.DarkFilament` (výchozí `false`), v `settings.json` jako `printers[*].detection.darkFilament`,
    zapisuje se jen zapnutý (staré soubory se načtou beze změny, neplatná hodnota = vypnuto s varováním).
  - `PrintFailureContext.DarkFilament` (nový poslední parametr s výchozí hodnotou `false`); služba ho bere z nastavení
    tiskárny a je součástí `RunConfig`, takže změna volby za tisku běh restartuje. Detektor si režim přečte z prvního
    vzorku úseku a při jiném režimu se učí znovu (jako při jiném výřezu).
  - `BedChangeDetector`: v režimu se **ztmavnutí** proti referenci počítá od `BedChangeOptions.DarkFilamentLevel` = 10
    úrovní místo 14 (vzdálenost pod rozsahem reference × 22/16 v jediném kanálu, bez další paměti). Zesvětlení dál
    od 14. S vypnutým režimem je váha 16/16, výpočet je matematicky totožný (běh T-166 bajt po bajtu stejný).
  - Proč jen ztmavnutí a proč 10: černý filament plát jen ztmaví. Vyzkoušeno (výřez Core One 2 s, 12 sad; skutečná
    série = nejvyšší skóre a rezerva pod prahem 0,3 %):

    | Úroveň ztmavnutí | Fixture plát (celkem) | Tmavý bez značek | Zlatý texturovaný | Světlý filament (12 sad) | Skutečná série | job354 Core One |
    |---|---|---|---|---|---|---|
    | 14 (výchozí) | 562 | 553 | 598 | 633 | 0,033 % (9,1×) | 0,000 % |
    | 12 | 569 | 564 | 604 | 634 | 0,033 % (9,1×) | 0,000 % |
    | 11 | 575 | 568 | 605 | 635 | 0,039 % (7,8×) | 0,000 % |
    | **10** | **577** | **575** | **610** | **636** | **0,072 % (4,2×)** | **0,000 %** |
    | 9 | – | – | – | – | 0,276 % (1,1×) | 0,006 % |
    | 8 | – | – | – | – | 0,430 % (5 snímků nad prahem) | 0,022 % |

    10 je nejnižší úroveň, kde skutečná série zůstává pod nejcitlivějším prahem (0,1 %); od 9 se počítají vlastní
    změny plátu.
  - **Plát v nastavení ani automatický odhad plátu nepřidávám:** režim pomáhá na všech plátech podobně (+1,5 až
    +3,3 bodu) a světlejší plát zlepšuje výchozí detekci sám, bez nastavení. Volba plátu by nic nepřepínala.
- **Výsledky** (výchozí citlivost 0,3 %, 12 semínek × 56 = 672 na konfiguraci, celkem nalezeno, bez / s režimem):

  | Plát | Core One 2 s | Core One 5 s | celý snímek 2 s | celý snímek 5 s | min sady (Core One 2 s) | medián sady |
  |---|---|---|---|---|---|---|
  | fixture, tmavý s mřížkou | 562 / 577 | 563 / 577 | 562 / 577 | 560 / 574 | 76,8 / 76,8 % | 83,9 / 87,5 % |
  | tmavý bez značek | 553 / 575 | 554 / 574 | 553 / 575 | 552 / 571 | 76,8 / 76,8 % | 82,1 / 87,5 % |
  | hladký PEI zelený | 589 / 599 | 586 / 600 | 589 / 599 | 584 / 597 | 76,8 / 78,6 % | 89,3 / 91,1 % |
  | hladký PEI zlatý | 600 / 613 | 599 / 612 | 600 / 613 | 597 / 609 | 80,4 / 82,1 % | 91,1 / 92,9 % |
  | texturovaný PEI zlatý | 598 / 610 | 601 / 613 | 598 / 610 | 600 / 611 | 78,6 / 82,1 % | 91,1 / 91,1 % |

  V procentech: tmavé pláty 82–84 % → 85–86 %, světlejší 87–89 % → 89–91 %. Režim přidá hlavně hroudy (fixture 108 →
  116 ze 180) a nejtmavší filament (jas 20: 99 → 104 ze 126). Doba do poplachu beze změny (medián 75–80 s).
- **Falešné poplachy v režimu:**
  - syntetické (selhání před začátkem): Core One 0 ve všech konfiguracích; celý snímek 5 s 3 → 4 za 44,8 h na plát
    (0,67 → 0,89 / 10 h) a u světlého filamentu 5 → 8 za 112 h (0,45 → 0,71 / 10 h), stále pod limitem 1 / 10 h; je to
    syntetická hlava v celém snímku (T-166, otázka 1),
  - úspěšné tisky (6 průběhů × 10 semínek × 4 konfigurace na plát, 40 h): **0 poplachů při výchozí citlivosti** na všech
    plátech i u světlého filamentu. Nejvíc riskantní je náhlé ztmavení světla o 25 %: nejvyšší skóre v režimu 0,06–0,11 %
    (Core One 5 s) a 0,11–0,31 % (celý snímek 5 s, tmavý plát bez značek: 1 poplach při nejcitlivějším prahu 0,1 %),
    bez režimu ≤ 0,01 %,
  - **skutečná data** (`BedChangeLocalSamplesTests`, proběhl): série 4,4 h (světlý stříbrný filament na tmavém plátu
    uživatele) ve výřezu Core One 0,033 % → **0,072 %**, rezerva 9,1× → **4,2×**, 0 snímků nad prahem; celý snímek
    42 → 50 snímků nad prahem (most X, výřez pro Core One se nemá používat celý). Klip ve smyčce: Core One 0,000 %,
    celý snímek 0,017 % (5 s), bez poplachu v obou režimech. Co to dokazuje: vlastní změny tmavého plátu uživatele
    (odlesky LED, pohyb v Z) během 4,4 h tisku dají v režimu skóre nejvýš 0,072 %, tedy 4× pod výchozím prahem a pod
    nejcitlivějším. Nedokazuje to, jak se chová skutečný černý filament (jen odhad, otázka 4).
  - **Riziko:** režim má proti výchozímu stavu zhruba poloviční rezervu proti falešným poplachům a je citlivější na
    náhlé ztmavení komory (vypnutá část LED, stín ruky v komoře). Proto výchozí vypnutý a „Experimentální“.
- **UI:** podzáložka Detekce tiskárny, pod seznamem detektorů zaškrtávátko „Tmavý filament“ s odznakem
  „Experimentální“ (stejný `Border.badge` jako T-161); vysvětlení je v tooltipu a v nápovědě pro čtečku (viditelný text
  pod volbou se do podzáložky bez posouvání nevešel, `SettingsWindowLayoutTests`, v němčině o 46 DIP).
  Nové texty `SettingsDetectionDarkFilament` a `SettingsDetectionDarkFilamentHint` ve všech 8 `Strings.*.resx`.
  Text omezení detektoru beze změny (rozhodnutí 2026-11-05).
- **Výkon:** v režimu i bez něj jedno násobení a posun navíc na pixel (váha ztmavnutí), žádná paměť navíc.
  `BedChangeDetectorCostTests` hlídá 0 B na vzorek i v režimu. Release (JIT, Apple M2 Pro, 20 000 vzorků po zahřátí,
  3 opakování, scéna s černým dílem): výchozí výřez Core One 0,306–0,309 ms na vzorek, celý snímek 0,914–0,925 ms (před
  T-163 0,299–0,306 / 0,916–0,921 ms, rozdíl v šumu měření); v režimu 0,297 ms / 0,94–1,01 ms. Při vzorku po 2 s je to
  ~0,015 % (výřez) / ~0,05 % (celý snímek) jednoho jádra.
- **Testy (běžný běh):** `BedChangeDetectorTests.AnotherDarkFilamentModeStartsOver`,
  `TheDarkFilamentModeCountsASmallerDarkeningOnly`, validace `DarkFilamentLevel`, `BedChangeDetectorCostTests` i v režimu;
  `BedChangeDarkSweepTests` (regrese 2 sad bez režimu 46 / 44 a v režimu 48 / 45 ze 56, úspěšné černé tisky bez režimu
  i s ním); `BedChangeLocalSamplesTests` i v režimu (Core One 0 snímků nad prahem); `SettingsMapperDetectionTests`
  (zápis jen zapnutého, načtení, neplatná hodnota s varováním); `PrintFailureDetectionServiceTests.TheDarkFilamentModeIsInTheContext`;
  UiTests `SettingsWindowDetectionUiTests.TheDarkFilamentModeIsOffByDefaultAndSaved`.
  **Volitelné** (`LAYERPEEK_DETECTION_SWEEP=1`): `BedChangeDarkSweepTests.EverySeedOfTheDarkFailures` (všech 5 plátů ×
  bez / s režimem × 2 výřezy × 2 intervaly, kontrast; ~14 min v Release) a `TheLightFilamentSetsInTheDarkFilamentMode`
  (30 sad T-166 v režimu, ≥ 90 % na sadu, ≤ 1 FP / 10 h; prošel: min 91,1 %, Core One 1 594 / 1 680).
- **Dokumentace:** `docs/architecture.md` (oddíl detektoru), `CHANGELOG.md` (`[Unreleased]`), T-127 (smoke test před
  vydáním 0.5.0), T-125 (Windows: testy, snímky UI, ruční kontrola).

### První kolo (2026-11-04): jen měření

- **Syntetická sada černé na černém** (`tests/Shared/Detection/DarkScene.cs`, `SyntheticSets.DarkFailures`):
  - pozadí (`SyntheticBackdrop`): šest klíčových snímků fixture, ve kterých je stříbrný díl přebarvený na černý filament
    (jas 35 + 0,1 × stínování dílu, i jeho smíchaný okraj 2 px). Přebarvuje se jen tam, kde snímek ukazuje scénu, ne
    hlavu (rozdíl od mediánu ≤ 20), takže hlava se dál pohybuje nad tmavší scénou. Dvě varianty:
    - **„black sheet with grid“**: černý plát fixture se světlou mřížkou a logem,
    - **„plain black sheet“**: plát bez mřížky a loga (v obrysu plátu každý pixel nejvýš dolní kvartil okolí 9 × 9),
      tedy nejmenší kontrast, pod selháním není žádná světlá značka. Skutečný hladký PEI plát s leskem to není, viz meze.
  - selhání: stejné druhy, velikosti, místa a počty jako `Failures` (30 špaget, 15 hrud, 10 posunutých dílů, 1 zmizelý),
    jen jas filamentu **20 / 30 / 45 / 60** (zvoleno předem jako černý filament pod LED komory, plát vpředu ~50–90,
    neladěno na detektor) a u špaget **lesk** 0 nebo 5 % pixelů vlákna s odleskem jasu 170 (`Spaghetti.Glint`,
    pevné místo, prolnuté s alfou vlákna). Posunutý / zmizelý díl je černý (`MovedPart.Backdrop`).
  - `Failures`, `SyntheticPrint` a `MovedPart` pro stávající sady se chovají stejně: nové parametry jsou volitelné
    a posun rámečku dílu o 2 px nic nemění, protože maska fixture leží uvnitř. Celý běh T-166
    (`LAYERPEEK_DETECTION_SWEEP=1`) vyšel **bajt po bajtu stejně** jako v T-166.
  - **Meze věrnosti:** světlo komory, textura plátu a šum jsou z fixture. Jas černého filamentu je odhad. Odlesky lesklého
    filamentu jsou jen pevné body (`Glint`), lesk LED pásku na hladkém PEI se nemodeluje. Stínování dílu je škálované,
    ne znovu nasvícené. Hroudy a špagety leží podle rozdělení T-155 i mimo plát (tmavá podlaha vlevo, jas ~15) a na
    černém dílu (jas ~35), kde černý filament není vidět z principu.
- **Kontrast** (výřez Core One, scéna bez šumu a hlavy, selhání v plné velikosti, 12 semínek; „dokonalý detektor“ =
  pravidlo detektoru bez šumu a časování: průměr 3 × 3 mimo rozsah ±1 / ±2 px o víc než 14 úrovní na ≥ 55 px):

  | Sada | Druh | Medián rozdílu jasu | Změněno > 25 úrovní (T-152) | > 14 úrovní (T-166) | Dokonalý detektor | Bez tolerance posunu |
  |---|---|---|---|---|---|---|
  | světlý filament (fixture) | špagety | 74 | 86 % | 93 % | 100 % | 100 % |
  | | hroudy | 77 | 86 % | 93 % | 83 % | 97 % |
  | | díl | 119 | 89 % | 92 % | 100 % | 100 % |
  | černé, plát s mřížkou | špagety matné / lesklé | 22 / 24 | 44 / 48 % | 66 / 67 % | 90 / 92 % | 95 / 98 % |
  | | hroudy | 31 | 56 % | 70 % | 61 % | 76 % |
  | | díl | 21 | 42 % | 60 % | 100 % | 100 % |
  | černé, plát bez mřížky | špagety matné / lesklé | 20 / 22 | 40 / 45 % | 63 / 65 % | 90 / 91 % | 95 / 98 % |
  | | hroudy | 28 | 53 % | 68 % | 59 % | 74 % |
  | | díl | 17 | 36 % | 55 % | 100 % | 100 % |

  Scéna mimo díl (p10 / p50 / p90): fixture 15 / 62 / 133, s mřížkou 15 / 60 / 132, bez mřížky 15 / 58 / 119.
  Kontrast černého filamentu je proti světlému asi 3× menší a při prahu 25 úrovní (T-152) se změní jen 36–56 %
  pixelů selhání (světlý 86–89 %).
- **Výsledek detektoru** (beze změny, výchozí citlivost 0,3 %, 12 semínek × 56 selhání = 672 na konfiguraci):

  | Pozadí | Konfigurace | Min / medián sady | Celkem | Falešné poplachy | Poplach po začátku (medián / 90 % / max) |
  |---|---|---|---|---|---|
  | plát s mřížkou | výřez Core One, 2 s | 76,8 % / 83,9 % | 562 (83,6 %) | 0 za 44,8 h | 77 / 153 / 254 s |
  | | výřez Core One, 5 s | 76,8 % / 85,7 % | 563 (83,8 %) | 0 za 44,8 h | 80 / 155 / 256 s |
  | | celý snímek, 2 s | 76,8 % / 83,9 % | 562 (83,6 %) | 0 za 44,8 h | 77 / 150 / 254 s |
  | | celý snímek, 5 s | 76,8 % / 85,7 % | 560 (83,3 %) | 3 za 44,8 h (0,67 / 10 h) | 80 / 156 / 256 s |
  | plát bez mřížky | výřez Core One, 2 s | 76,8 % / 82,1 % | 553 (82,3 %) | 0 za 44,8 h | 75 / 150 / 262 s |
  | | výřez Core One, 5 s | 76,8 % / 82,1 % | 554 (82,4 %) | 0 za 44,8 h | 79 / 156 / 288 s |
  | | celý snímek, 2 s | 76,8 % / 82,1 % | 553 (82,3 %) | 0 za 44,8 h | 74 / 150 / 262 s |
  | | celý snímek, 5 s | 76,8 % / 82,1 % | 552 (82,1 %) | 3 za 44,8 h (0,67 / 10 h) | 79 / 158 / 288 s |

  Podle druhu (výřez Core One 2 s, s mřížkou / bez mřížky): špagety matné 160 / 158 ze 182 (88 / 87 %), lesklé 162 /
  161 ze 178 (91 / 90 %), hroudy 108 / 102 ze 180 (60 / 57 %), díl 132 ze 132. Podle jasu (špagety a hroudy, s mřížkou):
  20 → 79 %, 30 → 73 %, 45 → 82 %, 60 → 85 %. Nejtmavší filament se ztrácí na tmavé podlaze a na černém dílu.
  Při nejcitlivějším prahu 0,1 % je to min 78,6–80,4 %, medián 87,5–89,3 %. Celý snímek při 5 s má stejné 3 falešné
  poplachy od syntetické hlavy jako u světlého filamentu (T-166, otázka 1), limit ≤ 1 / 10 h platí.
  **Úspěšné tisky** černého dílu na obou plátech (6 průběhů světla a šumu × 10 semínek × 4 konfigurace = 40 h na plát):
  **0 poplachů**, nejvyšší skóre 0,01 %.
- **Rozbor:** detektor je téměř na stropu svého pravidla (83,6 % proti dokonalému 84,7 % s mřížkou). Ztráty nejsou
  v učení ani v časování, ale v kontrastu: hrouda s jasem blízkým plátu (45–60 na ~50–90) nebo černá hrouda na černém
  dílu / tmavé podlaze se od pozadí liší méně než o 14 úrovní. Druhá ztráta je tolerance posunu ±1 / ±2 px (bez ní
  dokonalý detektor 92 %), kterou ale potřebuje skutečná podložka při pohybu v Z (T-166: bez ní skutečná série 0,49 %).
- **Vyzkoušené varianty** (pokusná kopie detektoru jen v testech, smazaná; výřez Core One 2 s, 12 sad; skutečná
  série 4,4 h, nejvyšší skóre a rezerva pod prahem 0,3 %; „job354“ = další lokální série Core One, 14 min po 5 s):

  | Varianta | Černé s mřížkou (min / medián / celkem) | Černé bez mřížky | Světlé 2 s (min / celkem) | Skutečná série (rezerva) | job354 |
  |---|---|---|---|---|---|
  | současný detektor | 76,8 % / 83,9 % / 562 | 76,8 % / 82,1 % / 553 | 91,1 % / 633 | 0,033 % (**9,1×**) | 0,000 % |
  | `ChangeLevel` 12 | 76,8 % / 87,5 % / 575 | 76,8 % / 85,7 % / 574 | 92,9 % / 639 | 0,160 % (1,9×) | 0,006 % |
  | `ChangeLevel` 10 | 80,4 % / 89,3 % / 590 | 83,9 % / 89,3 % / 598 | 92,9 % / 643 | 0,248 % (1,2×) | 0,017 % |
  | + kanál tmavnutí (jen tmavší než reference) 3 × 3 při 10 | 76,8 % / 87,5 % / 579 | 76,8 % / 87,5 % / 576 | 91,1 % / 636 | 0,121 % (2,5×) | 0,000 % |
  | + kanál tmavnutí 3 × 3 při 8 | 76,8 % / 87,5 % / 585 | 76,8 % / 87,5 % / 585 | 91,1 % / 641 | 0,463 % (0,6×, 5 snímků nad prahem) | 0,022 % |
  | + širší průměr 5 × 5 při 10 | 78,6 % / 87,5 % / 584 | 80,4 % / 87,5 % / 585 | 92,9 % / 642 | 0,198 % (1,5×) | 0,006 % |
  | + kanál tmavnutí 5 × 5 při 8 | 76,8 % / 87,5 % / 586 | 76,8 % / 89,3 % / 585 | 91,1 % / 639 | 0,386 % (0,8×) | 0,011 % |
  | + širší průměr 5 × 5 / 7 × 7 při 8 a níž | – | – | – | 0,49–3,0 % (nad prahem) | – |

  - **Práh podle jasu reference** (nižší úroveň jen na tmavém plátu) nepomůže: změněné pixely skutečné série při úrovni 10
    jsou ve všech pásmech jasu, i v tmavých (16–31 a 80–95), protože syntetický plát *je* skutečný plát fixture.
    Nejvyšší skóre skutečné série při úrovni 10 je souvislá skvrna ~45 px uprostřed plátu v odlesku LED, tedy skutečná
    změna obrazu plátu, ne šum. Proto nepomáhá ani širší průměr (5 × 5, 7 × 7), který šum potlačí, ale skvrnu ne.
  - **Kanál textury / gradientu** jsem nezkoušel v kódu: hroudy (hlavní ztráta) jsou plné plochy bez textury a jejich
    okraj má stejný malý kontrast jako jas. Každá změna textury je i změnou jasu, takže strop „bez tolerance posunu“
    (92 %) platí i pro ni. Špagety, kde by textura pomohla, už detektor najde z 87–91 %.
  - **Odlesky** lesklého filamentu (5 % pixelů) zvednou špagety jen o ~2 body (88 → 91 %), samostatný kanál „malé
    jasné body“ by stál víc, než přinese.
  - Žádná varianta nezvedne nejhorší sadu (semínko 7: 43/56) nad 90 % a všechny, které zvednou medián, srazí rezervu
    skutečné série z 9× na 2,5× a méně. Podle zadání proto detektor zůstává beze změny (otázka 3).
- **Skutečná data** (`BedChangeLocalSamplesTests` s `~/LayerPeek-samples`, proběhl, nepřeskočil se): série 4,4 h ve výřezu
  Core One nejvyšší skóre 0,033 %, 0 snímků nad prahem, rezerva **9,1×** (beze změny proti T-166). Klip ve smyčce 30 min:
  0,00 % (Core One), 0,00 / 0,02 % (celý snímek 2 / 5 s). Celý snímek série 42 snímků nad prahem (most X, beze změny).
- **Výkon:** kód detektoru beze změny, paměť stejná (12 B na pixel výřezu), `BedChangeDetectorCostTests` dál hlídá
  0 B na vzorek. Změřeno v Release (JIT, Apple M2 Pro, 20 000 vzorků po zahřátí, 3 opakování): výřez Core One
  0,299–0,306 ms na vzorek (černá scéna 0,301–0,303 ms), celý snímek 0,916–0,921 ms (černá 0,907–0,919 ms). Při vzorku
  po 2 s je to 0,015 % (výřez) / 0,046 % (celý snímek) jednoho jádra.
- **Testy:**
  - `BedChangeDarkSweepTests.BlackFilamentOnABlackSheetIsFoundAsOften` (běžný běh): sada 1 při 2 s bez mřížky (46/56)
    a sada 3 při 5 s s mřížkou (44/56), výřez Core One. Hlídá, aby se černé na černém nezhoršilo, a 0 falešných poplachů.
  - `BedChangeDarkSweepTests.ASuccessfulBlackPrintRaisesNoAlarm` (běžný běh): černý díl na obou plátech, 6 průběhů
    světla a šumu × 2 semínka, 2 s a 5 s, bez poplachu.
  - `BedChangeDarkSweepTests.EverySeedOfTheDarkFailures` (volitelný, `LAYERPEEK_DETECTION_SWEEP=1`, ~2 min v Release,
    výstup s `LAYERPEEK_DETECTION_DARK_SWEEP_FILE=<cesta>`): kontrast, 12 semínek × 2 pláty × 2 výřezy × 2 intervaly,
    rozpad podle druhu a jasu, úspěšné tisky. Hlídá ≤ 1 falešný poplach / 10 h a 0 poplachů na úspěšných tiscích.
    Přesnost ≥ 90 % nehlídá, protože to je naměřené omezení (otázka 3).
  - `BedChangeSeedSweep.Evaluate` a `SuccessfulPrints` mají volitelné pozadí (a intervaly), `SetResult.Kinds`
    sčítá nalezené podle druhu.
- **Texty v UI** (`Strings.*.resx`): beze změny, omezení trvá a text „méně spolehlivé u tmavého filamentu na černém
  plátu“ odpovídá měření (otázka 5).
- **Licence a AOT:** žádná nová závislost, žádná změna v `src/`.

## Review
qa-reviewer 2026-11-05 (po sloučení `develop` s odznakem detekce, T-159 a dokumentací 0.5.0): bez blokujících nálezů.
Vypnutý režim se chová stejně jako T-166 (běh `BedChangeSeedSweepTests` v Release: 1 586 / 1 587 / 1 586 / 1 583
z 1 680, 5 falešných poplachů jen v celém snímku při 5 s, semínko 16 50/56 – shodné s tabulkou T-166);
`BedChangeLocalSamplesTests` s `~/LayerPeek-samples` proběhl (Core One 0,033 % = 9,1×, s režimem 0,072 % = 4,2×).
Doplněný test `PrintFailureDetectionServiceTests.TogglingTheDarkFilamentModeRestartsTheRun` (přepnutí režimu během
tisku restartuje běh s novými detektory). Úkol zůstává v `in-progress/` jen kvůli otázce 6 (rozhodnutí uživatele).
