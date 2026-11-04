---
id: T-167
title: Detekce chyb tisku – detektor „Tisk do vzduchu“ (Z roste, díl neroste)
status: todo
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
- [ ] Fáze 1: v poznámkách jsou změřené `k` (px/mm), chování horního okraje na skutečné sérii, počet dlaždic růstu,
      podíl zakrytí hlavou, nejmenší `ΔZ` a odhad doby do poplachu. Brána je vyhodnocená (pokračovat / zastavit
      s otázkou).
- [ ] Otázky 1 a 2 jsou rozhodnuté uživatelem a zapsané. Případná změna smlouvy, registrace nebo nastavení má testy.
- [ ] Detektor je registrovaný v `PrintFailureDetectors.All` a ovládá se nastavením detektoru (zapnuto, citlivost) bez
      dalších změn UI kromě textů. Texty (název, popis, omezení) jsou ve všech 8 `Strings.*.resx`.
- [ ] Testy z-hopu (0,2 / 1,5 mm), startovní sekvence, sekvenčního tisku, `AxisZ = null`, stojícího Z (MMU), nové
      úlohy, `Reset` po pauze, tmy a nového připojení kamery, zahozených vzorků a mezery mezi vzorky.
- [ ] Syntetická sada: ≥ 90 % odhalených selhání na každém semínku při 2 s i 5 s a ≤ 1 falešný poplach za 10 h, nebo
      nejlepší kompromis s možnostmi v otevřených otázkách. Tabulka po semínkách a doba do poplachu jsou v poznámkách.
- [ ] 0 poplachů na skutečné sérii Core One (lokální test) s uvedenou rezervou. Na záznamu Z úlohy 354 se úroveň Z
      chová podle očekávání: žádný nový základ uprostřed tisku, z-hop nepřidá výšku.
- [ ] 0 B na vzorek (test). CPU obou detektorů na vzorek je změřené a pod rozpočtem 1 % jádra. Paměť je uvedená.
- [ ] `docs/architecture.md` je aktuální (oddíl detektoru a případná změna hranice výřezu).
- [ ] `dotnet build` a `dotnet test` bez chyb a varování.

## Otevřené otázky
1. **Výřez (blokuje fázi 2):** detektor potřebuje vidět pásmo u trysky, kde stojí horní okraje dílu. Výchozí výřez Core One
   (spodních 45 % snímku) ho u vyšších dílů neobsahuje. Možnosti:
   a) tento detektor výřez tiskárny ignoruje a pracuje v celém snímku (změna smlouvy rozhraní; stěny a rám stojí,
      takže falešný poplach nedělají, ale ředí signál),
   b) vlastní výřez pro každý detektor (změna nastavení a editoru výřezu z T-154, samostatný úkol pro UI),
   c) výřez odvozený automaticky z výřezu tiskárny, např. rozšířený nahoru po řádek trysky (jen Core One, změna smlouvy).
   Rozhodne uživatel, případně až po číslech z fáze 1.
2. **Výchozí stav nového detektoru (blokuje registraci):** dnes je každý detektor bez uloženého nastavení zapnutý
   (`DetectorSettings.Enabled = true`). Po aktualizaci by se tedy nový detektor sám zapnul u každé tiskárny se zapnutou
   detekcí. Má být po aktualizaci zapnutý, nebo vypnutý (malá změna Core: výchozí hodnota v registraci)? Má mít štítek
   „Experimentální“ jako timelapse?
3. **Přijatelné zpoždění poplachu (neblokuje):** signál potřebuje, aby Z narostlo o několik mm (odhad 2–4 mm, změří
   fáze 1). Při 0,4 mm/min (úloha 354) to dělá ~5–10 min od selhání, u pomalých tisků a MMU i 30 min a víc. Je to
   přijatelné? Kratší okno `ΔZ` přinese víc falešných poplachů. Fáze 1 nabídne možnosti s čísly.
4. **Podporované tiskárny (blokuje jen zpřístupnění v UI):** signál předpokládá podložku, která jezdí v Z (Core One,
   podle všeho i XL; ověřit). U MK4 / MINI se v Z zvedá tryska a díl roste v obraze nahoru, takže by byla potřeba jiná
   hypotéza. Nabízet detektor jen u Core One (model je známý, `DetectionRegion.IsCoreOne`), nebo u všech s tím, že bez
   zjištěného posunu podložky zůstane nerozjetý (nikdy nevyhlásí poplach)?
5. **MMU3 na Core One (neblokuje syntetiku):** potvrďte prosím, že tisk s MMU3 používá čisticí věž (ne čištění do výplně
   nebo do objektu). Věž roste i tehdy, když díl neroste, a selhání dílu může zakrýt. Přijmout to jako omezení, nebo
   věž vyloučit (souvisí s otázkou 1)? Pokud to jde, pořiďte záznam tisku s MMU (kamera + Z), aby šlo ověřit, že
   výměny nástroje nedělají falešný poplach.
6. **Skutečná data (neblokuje fázi 1):** máme jen jednu úspěšnou sérii jednoduchého dílu bez schodů. Bylo by možné:
   a) ke záznamu Z úlohy 354 (nebo k dalšímu tisku) pořídit i snímky kamery ve stejném čase (např. po 5–30 s), aby
      geometrie šla měřit se skutečným Z,
   b) inscenovaný tisk do vzduchu: vyšší krabička, v polovině filament vytáhnout (při vypnutém senzoru filamentu, nebo
      ručně ve chvíli, kdy senzor nezasáhne),
   c) úspěšný tisk dílu se schodem nebo víc dílů různé výšky (hlavní riziko falešných poplachů)?
7. **Název v UI (neblokuje fázi 1):** pracovní název je „Tisk do vzduchu“ (`air-printing`). Vyhovuje, nebo jiný?
8. **Částečné selhání (neblokuje fázi 1):** kvůli schodům a dílům různé výšky navrhuji poplach jen tehdy, když přestane
   růst (téměř) všechno. Selhání jednoho z několika dílů na podložce, nebo jen části dílu, tak detektor nenahlásí.
   Je to přijatelné? Pokud fáze 1 ukáže, že schody falešné poplachy nedělají, nabídne core-dev citlivější variantu.

## Poznámky k implementaci
(vyplní agent po dokončení)
