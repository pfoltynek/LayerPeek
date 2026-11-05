---
id: T-167
title: Detekce chyb tisku – detektor „Tisk do vzduchu“ (Z roste, díl neroste)
status: in-progress
owner: core-dev
priority: P3
platform: all
depends_on: [T-155, T-166, T-154]
---

> **Z rozpadu T-156 (rozhodnutí uživatele 2026-11-04):** jako první další detektor si uživatel vybral „tisk do vzduchu“.
> Ostatní kandidáti z T-156 zůstávají zatím nevybraní.

## Cíl
Poznat, že tiskárna dál tiskne (stav `Printing`, osa Z a průběh rostou), ale na podložce nic nového nepřibývá. Příčinou
může být došlý filament bez zásahu senzoru, prokluzující podavač, ucpaná tryska nebo filament z MMU, který se nezavedl.
Detektor `bed-change` (T-155) takovou chybu nepozná: díl zůstane na místě a nic nového nepřibude. Pozná jen díl, který
se odlomil a zmizel, nebo hmotu, která se objevila jinde.

**Priorita (trvalé pravidlo uživatele):** minimum prostředků a vysoký výkon, čitelný, robustní a odolný kód. Detektor
nesmí přidávat složitost bez změřeného přínosu.

## Výchozí zjištění architekta (ověřit na záznamu)
Prohlédl jsem snímky skutečné série `~/LayerPeek-samples/coreone-frames` (19:03, 21:33, Core One, kamera vpředu nahoře).
Jde o pozorování okem, ne o měření:
- **Podložka Core One jezdí v Z dolů, tryska zůstává v obraze na stejném místě.** Horní okraj dílu proto stojí v obraze
  u trysky (řádek ~0,33–0,35 výšky snímku na obou snímcích). Spodek dílu i podložka klesají (spodní hrana dílu
  ~0,75 → ~0,86). V obraze tedy díl roste **dolů**. Podložka se navíc perspektivně zmenšuje, protože se vzdaluje.
- **Při tisku do vzduchu** klesá s podložkou celý díl i s horním okrajem a mezi tryskou a dílem vzniká mezera. Jediný
  rozdíl proti dobrému tisku je tedy u **horních okrajů dílu**. V dobrém tisku stojí, při chybě jedou s podložkou.
  Boční stěna dílu je stejnorodá, takže ve výřezu bez horního okraje vypadá posun celého dílu stejně jako jeho růst.
- **Výchozí výřez Core One (`DetectionRegion.CoreOneBed`, řádky 0,55–1,0) horní okraj vyššího dílu neobsahuje.** Uvnitř
  výřezu proto tisk do vzduchu u vyššího dílu prakticky vidět nejde, dokud horní okraj nesjede pod 0,55. To může trvat
  desítky mm v Z. Detektor proto potřebuje vidět pásmo u trysky (otázka 1, blokuje).
- **Hlava stojí vždy nad horním okrajem dílu** a v obraze je velká (~85 px šířky při 320×180). U dílu užšího než hlava je
  horní okraj většinou zakrytý a běžící medián v tom místě obsahuje hlavu. Malé díly proto budou známé omezení.
- Záznam Z úlohy 354 (`~/LayerPeek-samples/z-recordings/coreone-job354-0.2mm.csv`, ještě se nahrává, **necommitovat**,
  ořezanou fixture přidá T-159): stav `PRINTING` je hlášený už ~4 min před první vrstvou. Z v té době stojí na 40,0 mm,
  pak 15,0 mm a mezi 0–0,7 mm (ohřev, kalibrace, čištění trysky). Teprve potom začne 0,2 mm. Rychlost růstu je
  ~0,40 mm/min (0,2 → 46,2 mm za ~6 980 s). Ve vzorkování po 1 s je po startu 52 skoků > 0,25 mm za ~6 900 s (z-hop,
  zdvih při přejezdu).

## Navržený signál (core-dev ho smí upravit podle měření)
Hypotéza „stojí, nebo jede s podložkou“ po dlaždicích, řízená osou Z:
1. **Pozadí bez hlavy:** stejný přístup jako v `bed-change`, tedy přibližný běžící medián a jen „klidné“ pixely. Kvůli
   paměti a CPU stačí nejspíš poloviční rozlišení (160×90). Pozadí se **nesdílí** s `bed-change`: rozhraní dává každému
   detektoru vlastní stav a duplicita stojí ~0,1 ms na vzorek. Sdílení by byla změna hranice Core, jen při změřené potřebě.
2. **Úroveň Z:** minimum `PrintFailureContext.AxisZ` za posledních ~15 s, tedy aspoň 3 dotazy pollingu po 5 s, aby
   z-hop nepřidal výšku. Když Z pod výškou prvních vrstev (1 mm jako `BedChangeOptions.FirstLayersHeight`) klesne o víc
   než ~1 mm, je to nový základ a reference se smažou (startovní sekvence 40 → 15 → 0,2 mm, sekvenční tisk). Bez
   `AxisZ` (`null`) se detektor nikdy nerozjede (`IsWarmedUp = false`), protože z obrazu sám Z neodhadne.
   *Vlastní malá logika:* úkol nezávisí na T-159. Pokud T-159 skončí dřív, core-dev zváží společný pomocník.
3. **Reference podle Z:** kopie pozadí po každých ~0,5 mm Z v kruhu, který pokryje okno `ΔZ` (např. 2–6 mm, změřit).
   Přidává se podle Z, ne podle času. Když Z stojí (dlouhé vrstvy, výměny nástroje MMU, zdvih bez tisku), nic se
   neposuzuje. MMU fáze tedy poplach jen zdrží, nevyvolají ho.
4. **Posun podložky:** pro každou dlaždici s dostatkem textury (např. 8×8 nebo 16×16 px) se mezi referencí a teď hledá
   nejlepší svislý posun `dy` v malém rozsahu. Dlaždice podložky dají `dy ≈ k·ΔZ`, kde `k` je px/mm podle místa
   v obraze a perspektivy. Dlaždice stěn, rámu, hlavy a horních okrajů dílu v dobrém tisku dají `dy ≈ 0`. Posuzuje se
   jen tehdy, když je posun podložky jasně odlišitelný od 0, tedy aspoň ~2 px. Pokud se podložka v obraze nehýbe
   (tiskárna s pevnou podložkou v Z, jiná poloha kamery), detektor zůstane nerozjetý (otázka 4).
5. **Dlaždice růstu:** dlaždice, která v tomto tisku dřív jela s podložkou (byla tam podložka) a teď stojí (dorostl tam
   horní okraj dílu). Stěny a rám stojí vždy, proto mezi dlaždice růstu nepatří.
6. **Skóre:** propad dlaždic růstu proti jejich nedávnému počtu. Dlaždice, které stály, teď jedou s podložkou.
   **Poplach jen tehdy, když přestane růst všechno** (nebo téměř všechno), ne když skončí jen část. Díl se schodem
   (spodní část skončí, užší sloupek pokračuje) a víc dílů různé výšky na podložce jsou hlavní zdroj falešných poplachů:
   při každém konci horní plochy se její okraj začne posouvat s podložkou (otázka 8). Pod minimálním počtem dlaždic
   růstu se neposuzuje (malý díl schovaný pod hlavou). Mapování citlivosti na práh (`DetectionThresholds`) navrhne
   core-dev podle měření.
7. **Potvrzení K z N a opakování** dělá orchestrace beze změny. Skóre se drží, dokud trvá nesoulad, takže okno 16 s
   stačí.

Co detektor **nemá** řešit: zhoršenou extruzi (díl roste, jen tenčí), kvalitu povrchu, první vrstvu (pod 1 mm se jen
učí) ani odlomený a zmizelý díl (to umí `bed-change`, i když zastavený růst může nahlásit taky).

## Rozsah

### Fáze 1 – měření a brána proveditelnosti (před implementací detektoru)
- Na skutečné sérii změřit, jestli horní okraj dílu v obraze opravdu stojí (řádky okrajů v čase) a kolik je `k`
  (px/mm při 320×180) na podložce u dílu. Bez záznamu Z u série použít lineární průběh Z mezi první vrstvou a koncovou
  výškou dílu (výšku dílu dodá uživatel, nebo se odhadne z obrazu) a uvést, že jde o odhad.
- Změřit, kolik dlaždic růstu dává díl ze série (šířka ~95 px) a jak často je horní okraj zakrytý hlavou.
- Z naměřeného `k` a rychlosti Z odhadnout nejmenší `ΔZ` a dobu do poplachu: úloha 354 (~0,40 mm/min), pomalý tisk
  (0,1 mm/min, MMU) a rychlý malý díl.
- **Brána:** pokud měření ukáže, že signál při 320×180 nejde spolehlivě oddělit (např. `k` < ~0,5 px/mm, okraj skoro
  pořád zakrytý), core-dev skončí zprávou s čísly a možnostmi (vyšší rozlišení vzorku jen pro tento detektor, jiný
  signál, ukončení úkolu). Úkol pak zůstane v `in-progress/` s otázkou. Nic se nedomýšlí.

### Fáze 2 – detektor (po rozhodnutí otázek 1 a 2)
- `src/LayerPeek.Detection/AirPrintingDetector.cs` a `AirPrintingOptions` (název a id volí core-dev, navrženo
  `air-printing`), registrace v `PrintFailureDetectors.All` za `bed-change`. Parametry jsou v čase a v Z, nikdy
  v počtu vzorků, a fungují při vzorkování 2–5 s i se zahozenými vzorky. Stejný `DetectionFrameSpec` (320×180), protože
  odběr vzorků je jeden na tiskárnu.
- Výřez podle rozhodnutí otázky 1. Při variantě a) nebo c) se upraví smlouva `IPrintFailureDetector` (dokumentace
  „skóre jen ve `Region`“) a `docs/architecture.md`. Při variantě b) vznikne samostatný úkol pro UI (ui-designer +
  ui-dev) a tento úkol bude záviset na něm.
- Výchozí zapnutí podle otázky 2. Pokud má být nový detektor po aktualizaci vypnutý, přibude do
  `PrintFailureDetectorRegistration` výchozí hodnota (např. `DefaultEnabled`), kterou použije `DetectorSettings` bez
  uloženého záznamu. Jde o malou změnu Core s testy mapperu nastavení.
- UI (T-154): seznam detektorů je rozšiřitelný, takže se mění jen `App/Detection/DetectorTexts` (název, popis, omezení)
  a nové texty ve **všech 8** `Strings.*.resx` (cs, de, en, es, fr, it, pl, sk). Omezení musí zmínit malé díly pod hlavou,
  díly se schodem nebo víc dílů různé výšky (pokud zůstanou) a zpoždění poplachu podle rychlosti růstu Z. Pokud
  rozhodnutí otázky 2 přinese štítek „Experimentální“, převezme se vzhled z timelapse (T-161).
- `docs/architecture.md`: nový oddíl „Detektor Tisk do vzduchu“ (signál, parametry, paměť, CPU, omezení).

### Data
- **Syntetické řady (commitnuté testy)** z fixture `tests/Shared/Detection` (nic nového v repozitáři kromě kódu): nový
  generátor (např. `SyntheticGrowingPrint`) se stálou vrstvou (stěny, rám), vrstvou podložky posunutou o `k·Z`, dílem
  s horním okrajem u trysky, protaženým dolů, a pohyblivou hlavou z klíčových snímků klipu. Geometrie (`k`, řádek trysky,
  velikost hlavy) se vezme z fáze 1. Průběh Z se modeluje podle úlohy 354: startovní sekvence, z-hop 0,2 i 1,5 mm,
  vrstvy 0,1–0,3 mm a rychlost 0,1–1 mm/min.
  - **Selhání** (rozdělení se zvolí předem, ne podle detektoru, jako v T-166): od náhodného Z díl přestane růst a jede
    s podložkou. Varianty: šířka dílu (širší i užší než hlava), poloha, jas, šum, rychlost Z, odlomený díl, který zmizí.
  - **Úspěšné tisky (záporné vzorky):** stálý růst při různých rychlostech, fáze MMU (Z stojí minuty, hlava jezdí
    k čisticí věži, věž roste), **schody** (širší spodek skončí, sloupek pokračuje; sloupek širší i užší než hlava),
    **víc dílů různé výšky**, startovní sekvence, sekvenční tisk (Z dolů), změny jasu.
  - Volitelné vyhodnocení na ≥ 10 semínkách (jako `BedChangeSeedSweepTests`), v běžném `dotnet test` rychlá regrese.
- **Skutečná data (lokální testy, přeskočí se bez dat):** série `~/LayerPeek-samples/coreone-frames` jen v době tisku
  (série obsahuje i snímky po skončení tisku) musí dát **0 poplachů** při výchozí citlivosti, s uvedenou rezervou.
  Snímek je po 30 s, déle než okno potvrzení, takže jedno skóre nad prahem se počítá jako poplach (jako u `bed-change`).
  Záznam Z úlohy 354 se čte jen lokálně (necommitovat). Commitnutá fixture Z je věc T-159. Další skutečná data
  v otázce 6.

### Cíle (shodně s volbou uživatele u T-155)
- ≥ 90 % odhalených syntetických selhání při výchozí citlivosti na každém semínku, ve výřezu z rozhodnutí otázky 1,
  při vzorkování 2 s i 5 s.
- ≤ 1 falešný poplach za 10 h syntetických úspěšných tisků (včetně schodů, víc dílů a MMU fází) a 0 na skutečné sérii.
- Doba do poplachu uvedená v mm Z i v minutách (medián, 90 %, maximum) pro rychlosti 0,1 / 0,4 / 1 mm/min.
- CPU: oba detektory dohromady < 1 % jednoho jádra při vzorku po 2 s (změřit v Release a uvést ms na vzorek,
  `bed-change` má ~0,22–0,28 ms). Paměť omezená velikostí výřezu, s uvedenými bajty na tiskárnu. **0 B na vzorek**
  v ustáleném stavu (test jako `BedChangeDetectorCostTests`). Žádný časovač ani úloha.
- Žádná nová závislost. Pokud by se ukázala nutná, licence (GPL-3.0 kompatibilní) a AOT se ověří předem a rozhodne
  uživatel.

## Akceptační kritéria
- [x] Fáze 1: v poznámkách jsou změřené `k` (px/mm), chování horního okraje na skutečné sérii, počet dlaždic růstu,
      podíl zakrytí hlavou, nejmenší `ΔZ` a odhad doby do poplachu. Brána je vyhodnocená (pokračovat / zastavit
      s otázkou).
- [x] Otázky 1 a 2 jsou rozhodnuté uživatelem a zapsané. Případná změna smlouvy, registrace nebo nastavení má testy.
- [x] Detektor je registrovaný v `PrintFailureDetectors.All` a ovládá se nastavením detektoru (zapnuto, citlivost) bez
      dalších změn UI kromě textů. Texty (název, popis, omezení) jsou ve všech 8 `Strings.*.resx`.
- [x] Testy z-hopu (0,2 / 1,5 mm), startovní sekvence, sekvenčního tisku, `AxisZ = null`, stojícího Z (MMU), nové
      úlohy, `Reset` po pauze, tmy a nového připojení kamery, zahozených vzorků a mezery mezi vzorky.
- [x] Syntetická sada: ≥ 90 % odhalených selhání na každém semínku při 2 s i 5 s a ≤ 1 falešný poplach za 10 h, nebo
      nejlepší kompromis s možnostmi v otevřených otázkách. Tabulka po semínkách a doba do poplachu jsou v poznámkách. *Splněno variantou kompromisu: 94 % celkově, nejhorší semínko 86–89 %; 0,05 falešného poplachu na 10 h bez
      částečných zastavení, 1,9 s nimi – otázky 9 a 10.*
- [x] 0 poplachů na skutečné sérii Core One (lokální test) s uvedenou rezervou. Na záznamu Z úlohy 354 se úroveň Z
      chová podle očekávání: žádný nový základ uprostřed tisku, z-hop nepřidá výšku.
- [x] 0 B na vzorek (test). CPU obou detektorů na vzorek je změřené a pod rozpočtem 1 % jádra. Paměť je uvedená.
- [x] `docs/architecture.md` je aktuální (oddíl detektoru a případná změna hranice výřezu).
- [x] `dotnet build` a `dotnet test` bez chyb a varování.

## Otevřené otázky
Otázky 1–8 rozhodl uživatel 2026-11-04 (předáno hlavním agentem):
1. **Výřez – rozhodnuto (c):** detektor si odvodí oblast sám, výřez tiskárny rozšíří nahoru po řádek trysky (bez nového
   nastavení). Změna smlouvy rozhraní je schválená.
2. **Výchozí stav – rozhodnuto:** po aktualizaci vypnutý, se štítkem „Experimentální“. V Core přibyla výchozí hodnota
   v registraci.
3. **Zpoždění – rozhodnuto:** 2–4 mm Z je přijatelné (5–10 min, u pomalých tisků a MMU 30 min a víc).
4. **Tiskárny – rozhodnuto:** nabízet jen u Core One / XL (podložka jezdí v Z), u ostatních modelů detektor skrýt
   (model, který aplikace zná).
5. **MMU3 – rozhodnuto:** čisticí věž, která selhání zakryje, je přijaté a zdokumentované omezení.
6. **Částečné selhání – rozhodnuto:** poplach jen tehdy, když přestane růst (téměř) všechno. Selhání jednoho z několika
   dílů se nehlásí.
7. **Název – rozhodnuto:** „Tisk do vzduchu“, id `air-printing`.
8. **Brána – rozhodnuto:** když fáze 1 ukáže, že signál nejde spolehlivě oddělit, zastavit s čísly a možnostmi. Fáze 1
   vyšla kladně (viz poznámky), proto vznikla fáze 2.

Nové otázky (core-dev 2026-11-05, úkol zůstává v `in-progress/`, dokud je uživatel nerozhodne):
9. **Falešné poplachy při „částečném zastavení“ (blokuje uzavření):** díl se širším spodkem, který skončí (sloupek tiskne
   dál), a tisk víc dílů, z nichž většina skončí dřív, vyvolají v syntetice skoro vždy falešný poplach. Na deseti
   semínkách to dělá 2–3 z 3 takových tisků na semínko, tedy 1,9 poplachu na 10 h všech syntetických úspěšných tisků.
   Bez nich je to 0,05 na 10 h. Příčina je principiální. Odkryté okraje skončeného spodku jedou s podložkou stejně jako
   při selhání. Horní okraj sloupku nad nimi se v obraze několik mm Z překrývá s klesajícím spodkem, takže ho detektor
   nemůže dřív potvrdit jako rostoucí. Delší výdrž skóre (3 mm, paměť 6 mm) poplach nezmenšila (77–93 %) a zpoždění
   by narostlo na ~5 mm. Stejně nepomohlo přísnější rozhodování o dlaždicích, které zároveň ztratilo polovinu selhání.
   Možnosti:
   a) přijmout jako zdokumentované omezení (detektor je experimentální a po aktualizaci vypnutý; text omezení v UI už
      díly se schodem a víc dílů zmiňuje) a úkol uzavřít,
   b) detektor do 0.5.0 nezařadit (odebrat z `PrintFailureDetectors.All`, kód a testy zůstanou) a vrátit se k němu se
      skutečnými záznamy,
   c) pořídit skutečný záznam dílu se schodem nebo víc dílů různé výšky (kamera po 5 s a záznam Z jako u úlohy 354)
      a podle něj zkusit jiný signál pro „zbytek roste dál“.
10. **Cíl ≥ 90 % na každém semínku (neblokuje, kompromis podle kritéria):** celkově se najde 94 % selhání při 2 s i 5 s,
    nejhorší semínko má 89 % (2 s) a 86 % (5 s). Chybějí hlavně díly užší než ~35 px (pod hlavou) a selhání v prvních
    6–8 mm (detektor ještě nehodnotí). Vyhovuje to?
11. **Modely (neblokuje):** kromě Core One a XL se detektor nabízí i u Core One L a XL+ (stejná kinematika, podložka jede
    v Z). Potvrďte, nebo omezit jen na Core One a XL.
12. **Skutečný tisk do vzduchu (neblokuje):** skutečný záznam selhání stále chybí. Inscenovaný tisk do vzduchu je
    volitelný bod T-127. Selhání v testech jsou jen syntetická.

## Poznámky k implementaci
### Fáze 1 – měření na skutečných datech (2026-11-05, brána: pokračovat)
- **Data:** `coreone-frames-job354` (513 snímků 1280×720 po 5 s, úloha 354 MMU3, 0,2 mm) + `z-recordings/coreone-job354-0.2mm.csv`.
  Tisk běží v datech 19:28:28–19:58:20 UTC, Z 49,6 → 55,4 mm (5,8 mm za 30 min, ~0,19 mm/min). Od 19:58:22 už jede podložka
  dolů (Z 105) a tiskárna hlásí `FINISHED`. Snímky jsem zmenšil na 320×180 a 640×360 (`flags=area` jako fixture), mediánová
  pozadí jsem počítal z 12 snímků (1 min) a posun dlaždic 8 × 8 px hledal součtem absolutních rozdílů se subpixelem
  (skripty mimo repozitář).
- **k na 320×180:** dlaždice podložky u dílu 0,48 px/mm (0,4–0,6, 125 dlaždic). Na 640×360 je to 0,96 px/mm. Při 160×90
  (~0,24 px/mm) signál zaniká, proto zůstává `DetectionFrameSpec` 320×180 a pozadí se nezmenšuje. Čelo dílu měří 35 px
  při 50 mm, průměrné k od 0 do 50 mm je tedy 0,7. Model k(Z) ≈ 0,9 / (1 + Z / 62,5) px/mm sedí na obojí. Okraj selhaného
  dílu u trysky se proto pohybuje ~0,9 px/mm, rychleji než podložka ve výšce 50 mm.
- **Horní okraj stojí:** 24 dlaždic na horním okraji dílu (díl ~75 px široký, okraj šikmo 0,31–0,53 výšky snímku) má
  posun 0,02–0,09 px, sd 0,10–0,24 px a p95 0,20–0,45 px pro ΔZ 0,5–4 mm. Podložka má při ΔZ ≥ 2 mm medián 0,97 px
  (jednotlivé dlaždice čárkované mřížky mají kvůli periodě sd ~1,5 px). Série 4,4 h (`coreone-frames`, bez Z) to
  potvrzuje: okraj stojí, podložka jede ~10 px/h.
- **Zakrytí hlavou:** dlaždice okraje jsou zakryté ve 40 % vzorků (podle dlaždice 0–100 %, 5 z 24 nad 50 %). Rám vedle
  podložky hlava nezakryje nikdy (0 %). Rozdíl se stal podmínkou dlaždice růstu („hlava nad ní byla“).
- **Nejmenší ΔZ a doba do poplachu:** šum stojícího okraje (p95 ≤ 0,45 px) proti ~0,9 px/mm selhaného okraje znamená
  ΔZ 2–3 mm. Zvolil jsem porovnání o 3 mm. Odhad: úloha 354 (0,19 mm/min) ~16 min, 0,4 mm/min ~7,5 min, 0,1 mm/min
  ~30 min, malý rychlý díl (1 mm/min) ~3 min.
- **Brána:** k ≈ 0,5 px/mm je na hranici z úkolu, ale okraj je ostrý a jeho šum je 5–10× menší než posun při selhání.
  Signál jde oddělit, proto pokračuji.
- **Úroveň Z na záznamu úlohy 354** (`TheHeightOfJob354HasNoNewBaseMidPrintAndZHopsAddNothing`, vzorek po 2 s): 3 nové
  základy, všechny před první vrstvou (40 → 15 → 2 / 0 / 5 → 0,2 mm). Uprostřed tisku žádný. 30 z-hopů > 0,25 mm se
  do úrovně nepropsalo.

### Fáze 2 – návrh (oproti návrhu architekta podle měření)
- `src/LayerPeek.Detection/AirPrintingDetector.cs` + `AirPrintingOptions.cs`, registrace v `PrintFailureDetectors.All`
  za `bed-change`. Algoritmus a čísla jsou v `docs/architecture.md`, oddíl „Detektor Tisk do vzduchu“. Hlavní odchylky:
  - Pozadí v plném 320×180 (ne 160×90, viz k).
  - **Dlaždice růstu bez historie:** architekt navrhoval „dřív tam jela podložka“. To by po resetu uprostřed tisku (a na
    obou skutečných sériích, které začínají uprostřed tisku) nikdy nefungovalo a odkrytá zadní stěna by se počítala
    jako růst. Podmínky teď jsou: stojí, nad ní i pod ní ve stejném sloupci jede podložka (rám vedle podložky má pod
    sebou rám), hlava nad ní byla během posledních 4 mm Z a stojí aspoň 1 mm Z. Smíšené dlaždice (okraj + podložka
    za ním) jinak přeskakují mezi stojí a jede.
  - **Skóre přes výšku:** minimum podílu za 1,5 mm Z. Bez toho by krátký přechod u schodu nebo u vrstvy vyvolal poplach.
    Paměť dlaždic růstu je 4 mm Z.
  - Úroveň Z je minimum za 15 s a aspoň ze 4 hlášení (při vzorku po 5 s jsou 3 hlášení málo, 1,5mm hop pak mohl projít).
    Nový základ je pokles o 2 mm, víc než hop.
  - Bez SIMD a bez sdílení pozadí s `bed-change`, protože to podle měření není potřeba.
- **Core:** `PrintFailureDetectorRegistration.EnabledByDefault` / `IsExperimental` / `Models`, `DefaultSettings`,
  `IsOfferedFor(...)`; `DetectorSettings.Disabled`; `PrinterDetectionSettings.For(registrace)`; `DetectionRegion.ModelOf`;
  orchestrace spouští jen zapnuté a nabízené detektory. Mapper zapisuje každou uloženou položku (výchozí hodnoty
  s `"enabled": true`) a čte ji zpět. Prázdná položka nebo položka jen s neplatnými hodnotami se zahodí. Smlouva
  `IPrintFailureDetector.Analyze` a `DetectionSample.Region` připouští oblast odvozenou z výřezu.
- **App:** `DetectorSettingsViewModel` (registrace, `IsExperimental`, `IsOffered` podle modelu, přepočet při změně modelu),
  `PrinterEditorViewModel.Detection` (výchozí hodnoty podle registrace), `DetectorTexts` + 3 texty ve všech 8
  `Strings.*.resx`, XAML podzáložky Detekce: položka se skryje, když `!IsOffered`, a štítek `Border.badge` z T-161 je vedle
  zaškrtávacího pole (AutomationId `PrinterDetectorExperimental-<id>`). Rozvržení podzáložky se jinak nemění.

### Výsledky (Release, macOS arm64)
- **Syntetická sada** (`AirPrintingSets`, rozdělení zvolené předem: 24 selhání na semínko, díl 25–110 px kdekoli na
  podložce, luma 115–225, vrstvy 0,1–0,3 mm, hop 0,2 / 1,5 mm, 0,1 / 0,4 / 1 mm/min, k × 0,8–1,2, selhání v 6–20 mm, čtvrtina
  MMU, desetina „díl zmizel“. Úspěšné tisky: ~10 h na semínko + ~3 h částečných zastavení). Deset semínek,
  `LAYERPEEK_DETECTION_SWEEP=1`, 4,5 min. Výchozí citlivost (práh 50 %):

  | semínko | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | celkem |
  |---|---|---|---|---|---|---|---|---|---|---|---|
  | nalezeno 2 s | 21/22 | 20/21 | 21/23 | 22/22 | 23/24 | 19/21 | 21/22 | 19/20 | 20/21 | 16/18 | 94 % |
  | nalezeno 5 s | 22/22 | 18/21 | 21/23 | 22/22 | 22/24 | 19/21 | 21/22 | 19/20 | 21/21 | 16/18 | 94 % |
  | fal. poplach 2 s / 5 s (bez částečných zastavení) | 0/0 | 1/0 | 0/0 | 0/0 | 0/0 | 0/0 | 0/0 | 0/0 | 0/0 | 0/0 | 0,05 na 10 h (216 h) |
  | fal. poplach u částečných zastavení 2 s / 5 s (3 tisky) | 2/2 | 2/2 | 2/2 | 3/3 | 3/3 | 2/2 | 3/3 | 3/2 | 3/3 | 3/3 | 1,9 na 10 h vč. nich (273 h) |

  Poplach před selháním: 0. Podle prahu (20 / 30 / 40 / 50 / 60 / 70 / 80 %) se najde při 2 s 93 / 94 / 94 / 94 / 93 / 91 /
  88 % a při 5 s 96 / 96 / 95 / 94 / 90 / 84 / 73 %. Díl zmizel: 13/26 (2 s) a 16/26 (5 s), to je doména `bed-change`.
  Jediný falešný poplach bez částečného zastavení (semínko 2, 2 s) mělo skóre přesně 50 %.
- **Doba do poplachu** (medián / 90 % / maximum): 0,1 mm/min 2,8 / 3,1 / 4,5 mm = 38 / 45 / 55 min; 0,4 mm/min
  2,8 / 3,1 / 4,6 mm = 9,4 / 14 / 19 min; 1 mm/min 3,2 / 4,0 / 7,5 mm = 3,7 / 6,2 / 11,5 min.
- **Skutečné série (0 poplachů):** série 4,4 h (`coreone-frames`, 526 snímků po 30 s, Z **odhadnuté** lineárně
  15 → 94 mm, protože podložka jede ~10 px/h a k ≈ 0,5–0,7) hodnotí 483 snímků, medián 18 dlaždic růstu, posun podložky
  1,6 px. Nejvyšší skóre je 7 %, tedy 7× pod výchozím prahem a 3× pod nejcitlivějším. Úloha 354 se skutečným Z dává při
  5 s 0 % (80 z 358 vzorků hodnoceno, rozjezd ~5 mm z 5,8 mm záznamu) a bez výdrže 10 %. Při 10 s je to 0 %.
- **CPU** (Release, M-series, skutečné snímky úlohy 354 dokola, 3 570 vzorků): `air-printing` 0,58 ms na vzorek ve výřezu
  Core One (max 0,78 ms) a 1,27 ms v celém snímku. `bed-change` má 0,24 a 0,80 ms. Oba dohromady při vzorku po 2 s spotřebují
  0,04 % (Core One) až 0,1 % (celý snímek) jednoho jádra. Žádný časovač ani úloha.
- **Paměť na tiskárnu:** ~0,29 MB ve výřezu Core One (oblast 224 × 130 px: pozadí + 8 referencí po 29 KB, pole 448
  dlaždic, kruh 512 skóre) a ~0,56 MB v celém snímku. V ustáleném stavu 0 B na vzorek (`AirPrintingDetectorCostTests`,
  i v měření výše 0 B za 3 570 vzorků).
- **Upozornění k datům:** všechna selhání jsou syntetická (`SyntheticGrowingPrint` z fixture: podložka posunutá podle
  modelu k(Z) z fáze 1, hlava z klíčových snímků klipu). Skutečný tisk do vzduchu ani díl se schodem zatím nikdo
  nenahrál.

### Testy
- Detection: `AirPrintingDetectorTests` (registrace; z-hop 0,2 / 1,5 mm; startovní sekvence a sekvenční tisk;
  `AxisZ = null`; podložka, která se v obraze nehýbe; dobrý tisk a selhání při 2 s i 5 s; stojící Z / MMU; `Reset`,
  tedy nová úloha, obnovení, tma a nové připojení kamery, které orchestrace převádí na `Reset`; zahozené vzorky
  a 3 min výpadek; oblast po řádek trysky; validace voleb), `AirPrintingDetectorCostTests` (0 B na vzorek),
  `AirPrintingSeedSweepTests` (regrese rychlejších tisků semínka 1 při 2 s a 5 s vždy, ~1 min v Debug; celá sada deseti
  semínek volitelně), `AirPrintingLocalSamplesTests` (úloha 354 se Z, série 4,4 h, úroveň Z; bez dat se přeskočí;
  `LAYERPEEK_DETECTION_REPORT=<soubor>` zapíše čísla). Společné pomocníky lokálních dat přesunuty do `LocalSamples`,
  testy `bed-change` se nezměnily (jen `Assert.Single` → první položka seznamu).
- Core: `DetectionModelTests` (registrace, `ModelOf`, `For(registrace)`), `SettingsMapperDetectionTests` (zápis
  a čtení výchozí položky, prázdná položka), `PrintFailureDetectionServiceTests` (vypnutý ve výchozím stavu, modely).
- App: `SettingsDetectionTests` (texty ve všech 8 jazycích, uložení zapnutí, skrytí podle modelu). UiTests: štítek
  „Experimentální“ u Core One, skrytí u MK4.
- Žádná nová závislost.

### Build a ověření (2026-11-05, macOS arm64)
- `dotnet build`: 0 varování, 0 chyb. `dotnet test`: 9 747 testů, 9 545 úspěšných, 0 neúspěšných, 202 přeskočených
  (podmínky platformy a dat). Celá sada deseti semínek (`LAYERPEEK_DETECTION_SWEEP=1`, Release) prošla za 4,5 min.
- `dotnet publish src/LayerPeek.App -c Release -r osx-arm64`: bez varování trim / AOT (IL2xxx / IL3xxx). Varování
  `dsymutil` k ladicí mapě runtime se objevují stejně jako dřív.
- Úkol zůstává v `in-progress/`. Detektor, texty, CHANGELOG a body T-125 / T-127 jsou hotové, ale uživatel musí
  rozhodnout otázku 9 (falešné poplachy u dílů se schodem a u víc dílů), než se úkol uzavře nebo detektor vyřadí z 0.5.0.
