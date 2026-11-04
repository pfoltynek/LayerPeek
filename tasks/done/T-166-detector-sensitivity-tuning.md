---
id: T-166
title: Detekce chyb tisku – ladění citlivosti detektoru „Změna na podložce“ na více semínkách
status: done
owner: core-dev
priority: P2
platform: all
depends_on: [T-155, T-154]
---

## Cíl
Cíl přesnosti z T-155 (rozhodnutí uživatele 2026-11-04: **≥ 90 % odhalených syntetických selhání a ≤ 1 falešný poplach
za 10 h** úspěšného tisku při výchozí citlivosti 50) platí jen na commitnuté sadě (`SyntheticSets.Failures(seed: 7)`,
95 %). Stejně vygenerované sady s jinými semínky dávají 86–91 % (T-155, otázka 7). Uživatel 2026-11-04 schválil ladění
detektoru `bed-change`, aby cíl platil spolehlivě na mnoha semínkách ve výřezu Core One i v celém snímku při vzorkování
2 s i 5 s.

## Rozsah
- Vyhodnocení na mnoha nezávislých semínkách **stejného** rozdělení syntetických selhání (rozdělení se nemění podle
  detektoru, bylo zvolené předem jako věrohodná selhání): podíl odhalených selhání po semínkách, falešné poplachy, doba do
  poplachu. Pomalé vyhodnocení mimo běžný `dotnet test` (volitelné), rychlá regrese v běžném běhu.
- Rozbor, proč se hroudy nenajdou (skóre vs. práh, pravidlo „klidných“ pixelů 45 s, přepočet jasu, tolerance posunu,
  pevná plocha 0,315).
- Ladění mapování citlivosti → práh a / nebo parametrů detektoru (`BedChangeOptions`, `DetectionThresholds`). Nejjednodušší
  změna, která funguje; žádná složitost bez změřeného přínosu.
- Falešné poplachy zůstanou 0 na skutečné sérii (`~/LayerPeek-samples/coreone-frames`, výřez Core One), na skutečném klipu
  s pohyblivou hlavou a na syntetických úspěšných tiscích, s rozumnou rezervou (uvést násobek).
- Celý snímek u Core One (most X) je známé omezení (T-155, otázka 4), tady se neřeší.
- Výkon: 0 B na vzorek (`BedChangeDetectorCostTests`), CPU na vzorek se znatelně nezvýší (změřit před / po).
- `docs/architecture.md` a texty T-154 (`Strings.*.resx`) jen při změně chování nebo omezení.

## Akceptační kritéria
- [x] Vyhodnocení na ≥ 10 nezávislých semínkách (volitelný test) a rychlá regrese v běžném `dotnet test`
- [x] Rozbor neodhalených selhání a tabulka po semínkách před / po (min / medián, falešné poplachy, doba do poplachu)
- [x] Při výchozí citlivosti ≥ 90 % na každém semínku ve výřezu Core One i v celém snímku při 2 s i 5 s, ≤ 1 falešný
      poplach za 10 h, nebo nejlepší kompromis a možnosti v otevřených otázkách (splněno na všech 30 semínkách kromě
      jednoho v celém snímku při 5 s, 89,3 % kvůli falešnému poplachu od hlavy; možnost v otázce 1)
- [x] 0 poplachů na skutečné sérii (výřez Core One), na skutečném klipu a na syntetických úspěšných tiscích; rezerva uvedená
- [x] 0 B na vzorek, CPU na vzorek změřené před / po
- [x] `docs/architecture.md` (a texty T-154, pokud se mění omezení) aktuální
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. **Celý snímek při 5 s, semínko 16 (neblokuje):** 50/56 = 89,3 %, protože jeden běh skončil falešným poplachem dřív, než
   selhání začalo (bez něj 50/55). Příčina je syntetická pohyblivá hlava v celém snímku: šest klíčových snímků dokola,
   při 5 s může místo, kam hlava jezdí jen někdy, náhodou vypadat klidné po celou dobu `Quiet`. Celkem 5 falešných poplachů
   za 112 h (0,45 / 10 h, cíl ≤ 1 / 10 h splněný). Možnosti: a) nechat `Quiet` 60 s (zvoleno); b) `Quiet` 90 s: celý snímek
   při 5 s 0 falešných poplachů a ≥ 51/56 na všech 30 semínkách, ale poplach později (výřez Core One 2 s medián 71 → 77 s,
   90 % do 130 → 153 s) a hmota pod hlavou se počítá až po 90 s klidu (`Quiet` 75 s: 1 falešný poplach, minimum stále 50/56).
   Rozhodne uživatel. **Rozhodnuto (uživatel 2026-11-04): a) `Quiet` zůstává 60 s.**
2. **Celý snímek na skutečné sérii Core One (neblokuje, souvisí s otázkou 4 v T-155):** vyšší citlivost zhoršila poplachy od
   mostu X v celém snímku: 42 z 526 snímků nad výchozím prahem (dřív 13), nejvyšší skóre 4,03 % (dřív 2,11 %). Výchozí
   výřez Core One je dál bez poplachu. Výchozí výřez ostatních tiskáren je celý snímek; záběry z nich nemáme. Řešení
   otázky 4 v T-155 (výřez jen na podložku, maska pohyblivých konstrukcí) je teď o to důležitější.
3. **Rezerva na skutečných datech (neblokuje):** nejvyšší skóre skutečné série ve výřezu Core One vzrostlo z 0,011 % na
   0,033 % (rezerva 27× → 9× pod výchozím prahem, 9× → 3× pod nejcitlivějším 0,1 %). Ověřeno jen na jedné sérii 4,4 h
   (snímek po 30 s) a jednom klipu 60 s. Doporučuji před vydáním nasbírat delší záznam s vzorkováním 2–5 s (ruční ověření
   detekce se skutečnou tiskárnou je v T-127).
4. **Zbývající neodhalená selhání (neblokuje):** po ladění zbývá na 30 sadách 94 neodhalených ze 1 680 (výřez Core One
   2 s), z toho 92 hrud a 2 špagety. Jde hlavně o hroudy s jasem blízkým plátu, hroudy na logu a nápisu vpravo vpředu,
   kde tolerance posunu pokryje celou škálu jasu, a hroudy r 5 px, které jsou menší než výchozí práh. Další zlepšení by
   chtělo jiné pravidlo změny, které rozliší posunutou texturu od nové hmoty. Obě zkoušené varianty (posun celé záplaty,
   obousměrný rozsah) ale zvedly skóre skutečné série. Založit na to samostatný úkol (spolu s T-163), nebo nechat?

## Poznámky k implementaci
- **Změna (jen parametry, kód detektoru beze změny):** `BedChangeOptions.ChangeLevel` 25 → **14** úrovní a
  `BedChangeOptions.Quiet` 45 s → **60 s**. Mapování citlivosti na práh (`DetectionThresholds` 0,5 % / 0,3 % / 0,1 %)
  zůstává, výchozí práh 0,3 %. Texty T-154 (`Strings.*.resx`) se neměnily: neuvádějí čísla a omezení zůstávají stejná.
  `docs/architecture.md` (oddíl „Detektor Změna na podložce“) má nové hodnoty a odkaz na vyhodnocení.
- **Vyhodnocení na více semínkách:** `tests/LayerPeek.Detection.Tests/BedChangeSeedSweep.cs`.
  - `BedChangeSeedSweepTests.EverySeedOfTheSyntheticFailures` je volitelný (`LAYERPEEK_DETECTION_SWEEP=1`, ~2,5 min
    v Release, výstup do testu a s `LAYERPEEK_DETECTION_SWEEP_FILE=<cesta>` do souboru). Zahrnuje 30 sad
    `SyntheticSets.Failures(seed)` se **stejným rozdělením** selhání (beze změny), 4 konfigurace (výřez Core One / celý
    snímek × 2 s / 5 s) a 5 prahů najednou (0,1–0,3 %). Úspěšné tisky jsou 6 průběhů × 10 semínek × 4 konfigurace
    (40 h). Test hlídá ≤ 1 falešný poplach / 10 h v každé konfiguraci, 0 poplachů na úspěšných tiscích a ≥ 90 % na každém
    semínku. Běh, který skončil falešným poplachem, se tu za neodhalený nepočítá, protože ho hlídá limit falešných poplachů.
  - Šum a časování každé sady mají vlastní semínko (`seed × 1000 + index`), proto řádek semínka 7 není totožný
    s commitnutým testem (`100 + index`).
  - Ladilo se na 12 sadách (7, 11, 23, 31, 47, 101, 202, 303, 404, 505, 606, 707). Dalších 18 sad (1–20 bez 7 a 11) se
    při ladění nepoužilo a slouží jako kontrola (holdout).
  - Rychlá regrese v běžném `dotnet test`: `OtherSeedsAlsoFindNineInTen` (sada 11 při 2 s, sada 23 při 5 s, výřez Core
    One, dřív obě 48/56 = 86 %, teď 53/56 a 51/56), samotné ~5 s v Debug. Commitnuté `BedChangeAccuracy*Tests` (sada 7) zůstávají.
- **Rozbor neodhalených selhání** (před laděním, výřez Core One 2 s, 12 sad): neodhalené byly téměř jen hroudy
  (58 ze 180) a špagety s jasem 105–130 (20 z 360), posunuté / zmizelé díly všechny nalezené. Pro každé selhání jsem spočítal
  „ideální“ skóre na čistém mediánu scény: bez tolerance posunu, s tolerancí rozsahu ±1 / ±2 px (jako detektor) a
  s posunem celé záplaty 3 × 3. Nejvyšší skóre detektoru téměř přesně odpovídalo ideálu s tolerancí rozsahu, takže
  ztráty nevznikají učením ani časováním, ale samotným pravidlem změny:
  - **Hmota s jasem blízkým plátu** (jas 105–130; plát vpředu je v obraze ~90–120, ne černý) se od pozadí liší o méně
    než 25 úrovní. Je to hlavní příčina a řeší ji nižší `ChangeLevel`.
  - **Tolerance posunu na texturovaném místě:** logo a nápis vpravo vpředu (x > ~225) a mřížka plátu. Rozsah min / max
    v okolí ±1 / ±2 px tam pokrývá skoro celou škálu, takže hrouda libovolného jasu „leží v rozsahu“ (např. hrouda r 8 px s jasem 160 na
    logu: ideál bez tolerance 0,5 %, s tolerancí 0,0 %). Zůstává i po ladění (otázka 4).
  - **Malé hroudy:** hrouda r 5 px má plochu ~47 px, méně než výchozí práh 0,3 % = 54 px, takže ji výchozí citlivost
    nenajde z principu.
  - **„Klidné“ pixely, přepočet jasu, pevná plocha 0,315:** ve výřezu Core One nehrají roli. `Quiet` 30 s dal stejný
    výsledek, úspěšné tisky se změnami světla zůstaly na 0,00 % a skóre v obou výřezech je stejné. V celém snímku při 5 s
    ale `Quiet` 45 s způsoboval falešné poplachy od syntetické hlavy (viz otázka 1).
- **Vyzkoušené a zamítnuté varianty** (12 sad, výřez Core One 2 s, minimum sady při 0,3 % / nejvyšší skóre skutečné série):
  - jen nižší práh při původních parametrech: při 0,1 % minimum 87,5 %, nestačí,
  - posun celé záplaty 3 × 3 místo rozsahu: syntetika 91,1 % (0,3 %), ale skutečná série medián 0,49 %, 411 snímků nad
    prahem (podložka se v Z nedeformuje jen posunem). Zamítnuto,
  - obousměrný rozsah (navíc reference proti rozsahu pozadí): 83,9 % (0,3 %) / 91,1 % (0,15 %), skutečná série 0,09 %.
    Horší rezerva než nižší `ChangeLevel` a víc kódu, zamítnuto,
  - `ChangeLevel` (bez změny kódu): 25 → 82,1 % / 0,011 %; 20 → 85,7 % / 0,017 %; 17 → 87,5 % / 0,017 %; 15 → 89,3 % /
    0,028 %; **14 → 91,1 % / 0,033 %**; 13 → 91,1 % / 0,033 %; 12 → 92,9 % / 0,182 %; 10 → 92,9 % / 0,270 %. Pod 13 roste
    skóre skutečné série skokem (změny samotné podložky), 14 je nejvyšší hodnota, která cíl splní s rezervou,
  - obousměrný rozsah + `ShiftY` 1: 89,3 % a skutečná série 0,41 %, zamítnuto,
  - `Quiet` 60 s (s `ChangeLevel` 14): celý snímek při 5 s 11 → 1 falešný poplach za 44,8 h (12 sad), výřez Core One beze
    změny, medián doby do poplachu zpět na ~71 s (s 45 s byl ~59 s). `Quiet` 75 / 90 s viz otázka 1.
- **Výsledky při výchozí citlivosti (práh 0,3 %), 30 sad × 56 selhání = 1 680 na konfiguraci:**

  | Konfigurace | Před: min / medián sady | Před: celkem | Před: falešné poplachy | Po: min / medián sady | Po: celkem | Po: falešné poplachy |
  |---|---|---|---|---|---|---|
  | výřez Core One, 2 s | 82,1 % / 91,1 % | 1 508 (89,8 %) | 0 za 112 h | **91,1 % / 94,6 %** | 1 586 (94,4 %) | 0 za 112 h |
  | výřez Core One, 5 s | 82,1 % / 91,1 % | 1 504 (89,5 %) | 0 za 112 h | **91,1 % / 94,6 %** | 1 587 (94,5 %) | 0 za 112 h |
  | celý snímek, 2 s | 82,1 % / 91,1 % | 1 512 (90,0 %) | 0 za 112 h | **91,1 % / 94,6 %** | 1 586 (94,4 %) | 0 za 112 h |
  | celý snímek, 5 s | 82,1 % / 89,3 % | 1 496 (89,0 %) | 19 za 112 h (1,7 / 10 h) | **89,3 % / 94,6 %** | 1 583 (94,2 %) | 5 za 112 h (0,45 / 10 h) |

  Citlivější prahy (po): při 0,2 % minimum 91,1–92,9 % (celý snímek při 5 s 91,1 %, 6 falešných poplachů / 112 h), při
  0,1 % 91,1–92,9 % (celý snímek při 5 s 12 / 112 h). Hold-out sady (18, při ladění nepoužité) samy: výřez Core One 2 s
  minimum 91,1 %, medián 94,6 %; celý snímek při 5 s minimum 89,3 % (semínko 16, otázka 1), 4 falešné poplachy za 67 h.
  Commitnutá sada 7 (`BedChangeAccuracy*Tests`): 53/56 → **54/56 = 96 %** ve všech čtyřech konfiguracích (neodhalené
  hrouda r 10 px s jasem 105 a r 5 px s jasem 190, skóre 0,23–0,29 %).
- **Po semínkách** (nalezeno z 56 při 0,3 %, „FP“ = falešný poplach před začátkem selhání; první 4 sloupce před, další 4 po):

  | Semínko | Před: Core One 2 s | Core One 5 s | celý 2 s | celý 5 s | Po: Core One 2 s | Core One 5 s | celý 2 s | celý 5 s |
  |---|---|---|---|---|---|---|---|---|
  | 1 | 51 | 51 | 52 | 51 (1 FP) | 54 | 54 | 54 | 53 (1 FP) |
  | 2 | 50 | 50 | 50 | 50 (1 FP) | 51 | 51 | 51 | 51 |
  | 3 | 52 | 52 | 52 | 52 | 54 | 54 | 54 | 54 |
  | 4 | 52 | 52 | 53 | 52 (1 FP) | 54 | 54 | 54 | 54 |
  | 5 | 51 | 51 | 51 | 50 (1 FP) | 53 | 53 | 53 | 53 |
  | 6 | 51 | 50 | 51 | 50 | 52 | 52 | 52 | 52 |
  | 7 | 53 | 53 | 53 | 53 | 54 | 54 | 54 | 54 |
  | 8 | 53 | 53 | 53 | 53 | 56 | 56 | 56 | 56 |
  | 9 | 50 | 49 | 50 | 48 (2 FP) | 53 | 53 | 53 | 52 (1 FP) |
  | 10 | 53 | 53 | 53 | 53 | 53 | 54 | 53 | 54 |
  | 11 | 48 | 48 | 48 | 49 (1 FP) | 53 | 53 | 53 | 53 (1 FP) |
  | 12 | 51 | 52 | 51 | 52 | 53 | 53 | 53 | 53 |
  | 13 | 50 | 49 | 50 | 47 (2 FP) | 51 | 51 | 51 | 51 |
  | 14 | 49 | 48 | 49 | 46 (2 FP) | 52 | 52 | 52 | 52 |
  | 15 | 48 | 48 | 48 | 48 | 51 | 51 | 51 | 51 |
  | 16 | 49 | 49 | 49 | 49 | 51 | 51 | 51 | 50 (1 FP) |
  | 17 | 49 | 49 | 49 | 50 | 54 | 54 | 54 | 54 |
  | 18 | 52 | 51 | 52 | 51 | 54 | 54 | 54 | 54 |
  | 19 | 51 | 50 | 51 | 51 (1 FP) | 53 | 53 | 53 | 53 |
  | 20 | 52 | 52 | 52 | 52 | 54 | 54 | 54 | 53 (1 FP) |
  | 23 | 48 | 48 | 48 | 47 (1 FP) | 51 | 51 | 51 | 51 |
  | 31 | 51 | 52 | 51 | 51 (1 FP) | 52 | 52 | 52 | 52 |
  | 47 | 48 | 48 | 48 | 48 | 53 | 53 | 53 | 53 |
  | 101 | 48 | 47 | 49 | 48 | 53 | 53 | 53 | 53 |
  | 202 | 51 | 51 | 51 | 48 (3 FP) | 54 | 54 | 54 | 54 |
  | 303 | 51 | 52 | 51 | 52 | 54 | 54 | 54 | 54 |
  | 404 | 46 | 46 | 47 | 46 | 51 | 51 | 51 | 51 |
  | 505 | 53 | 53 | 53 | 53 | 53 | 53 | 53 | 53 |
  | 606 | 51 | 51 | 51 | 49 (2 FP) | 52 | 52 | 52 | 52 |
  | 707 | 46 | 46 | 46 | 47 | 53 | 53 | 53 | 53 |

- **Doba do poplachu** (od začátku selhání, výchozí práh, 30 sad): před medián 71 / 72 s (2 / 5 s), 90 % do 130 / 133 s,
  nejvýš 254 / 350 s; po medián 71 / 73 s, 90 % do 130 / 133 s, nejvýš 273 / 303 s (celý snímek 69 / 70 s). Nejdelší
  případy jsou selhání, která rostou až 4 min.
- **Falešné poplachy na skutečných datech** (`BedChangeLocalSamplesTests` s `~/LayerPeek-samples` prošel):
  - skutečná série 4,4 h ve výřezu Core One: 0 snímků nad prahem, nejvyšší skóre 0,011 % → **0,033 %**, rezerva
    **9×** pod výchozím prahem (dřív 27×), 3× pod nejcitlivějším,
  - klip s pohyblivou hlavou ve smyčce 30 min: výřez Core One 0,00 % při 2 s i 5 s, celý snímek 0,00 % / 0,02 % (2 / 5 s),
    bez poplachu,
  - celý snímek na sérii: 42 snímků nad prahem (dřív 13), most X, otázka 2,
  - syntetické úspěšné tisky (40 h na konfiguraci): 0 poplachů, nejvyšší skóre 0,00 % (Core One) / 0,05 % (celý snímek
    při 5 s, pomalý posun jasu).
- **Výkon:** kód detektoru se nezměnil, mění se jen dvě konstanty. Paměť je stejná (12 B na pixel výřezu) a
  `BedChangeDetectorCostTests` dál hlídá 0 B na vzorek v obou výřezech. Změřeno v Release (JIT, Apple M2 Pro, tentýž běh,
  20 000 vzorků po zahřátí, 3 opakování), čas před / po: výřez Core One 0,274–0,279 / 0,273–0,278 ms na vzorek, celý snímek
  0,850–0,883 / 0,842–0,867 ms. Rozdíl je v šumu měření. Absolutní čísla jsou vyšší než v T-155 (0,22 / 0,70 ms, Native
  AOT), protože jde o jiný režim i zátěž stroje.
- **Licence a AOT:** žádná nová závislost, žádná změna kódu v `src/` kromě dvou výchozích hodnot a komentářů.
