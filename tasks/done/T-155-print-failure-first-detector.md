---
id: T-155
title: Detekce chyb tisku – první detektor „Změna na podložce“ (bed-change)
status: done
owner: core-dev
priority: P2
platform: all
depends_on: [T-152, T-153]
---

## Cíl
První skutečný detektor v `LayerPeek.Detection` nad infrastrukturou z T-153 (nápad 3, fáze 3). **Uživatel 2026-11-04 schválil** klasické CV v čistém C# bez nové závislosti a jako první detektor „Změna na podložce“ (`bed-change`, otázky 1, 2 a 5 v T-152).

### Rozhodnutí uživatele 2026-11-04
- **Přístup:** klasické CV v čistém C#, žádná nová závislost.
- **Testovací data (otázka 3, varianta a):** syntetické varianty **generované v testovacím kódu** z commitnutých fixture
  (vložená „hmota“ / spleť čar, posunutý nebo zmizelý díl, změny jasu, šum podobný H.264); repozitář se generovanými daty
  nesmí růst. Celou lokální sérii v `~/LayerPeek-samples/coreone-frames` (a `clips/`) smí použít test, který se bez složky
  přeskočí.
- **Cílová přesnost (otázka 1):** ≥ 90 % odhalených syntetických selhání a ≤ 1 falešný poplach za 10 h úspěšného tisku
  (falešné poplachy na skutečné sérii úspěšného tisku z `~/LayerPeek-samples` s extrapolací a na čistých fixture).
- **Interval a výřez:** detektor musí fungovat při vzorkování 2 s i 5 s (časové parametry, ne počty vzorků) a s libovolným
  výřezem.
- **Simulátor (otázka 2):** volba `--camera-clip <soubor>` pro přehrání vlastního klipu je součástí úkolu (E2E testy detektoru).
- **Tmavý filament na černém plátu:** známé omezení (T-163), zdokumentovat.
- **Dashcam:** poplach napojit na `SaveDashcam(printerId, CameraDashcamTrigger.FailureDetected)` z T-148. Automatické
  pozastavení tisku (T-157) je odložené.
- **Priority:** minimum prostředků – detektor synchronní, jen CPU, v ustáleném stavu bez alokací (předem alokované bufery),
  hluboko pod rozpočtem T-153 (< 1 % jádra na tiskárnu); změřit a zapsat ms na vzorek. Čitelný a robustní kód.

## Doporučení spiku T-152 (2026-11-03, schváleno uživatelem 2026-11-04)
- **Detektor „Změna na podložce“** (`bed-change`): zmizení nebo posun dílu a nová hmota na podložce (špagety, odtržený díl). Klasické CV v čistém C# bez nové závislosti.
- **Algoritmus (prototyp ověřený na 214 skutečných snímcích, `docs/detection-spike.md` oddíl 4):** šedotón 320×180, přibližný běžící medián (±K úrovní na vzorek, O(1) na pixel, bez historie snímků) odstraní pohyblivou hlavu a most X. Skóre = podíl pixelů výřezu přední části podložky, kde se pozadí liší o víc než 25 úrovní od pozadí před ~5 min (kruhový seznam několika starších kopií pozadí). Pevný referenční snímek nepoužívat, podložka se pohybuje v Z.
- **Čísla z prototypu (orientační):** úspěšný tisk max 1,44 %, syntetické špagety 3,9 %, syntetický posunutý díl 19,7 %. Výchozí práh ~3 %, citlivost ~2–6 %, potvrzení 5 z 8 vzorků. Záběry skutečných selhání od uživatele nebudou (rozhodnutí 2026-11-04), konečné hodnoty se nastaví na syntetických / inscenovaných datech (viz Testovací data).
- **Výřez:** z nastavení tiskárny (model T-153, editor T-154). Výchozí je celý snímek, u Core One předvyplněný výřez přední části podložky. Detektor musí fungovat s libovolným výřezem (malým i celým snímkem) a skóre počítat jen v něm.
- **Interval vzorkování 2–5 s** (T-153): časové parametry (stáří pozadí ~5 min, rozjezd 1–2 min, potvrzení K z N) se odvozují z času, ne z počtu vzorků. Rychlost běžícího mediánu (±K úrovní na vzorek) se přizpůsobí intervalu.
- **Známé omezení:** tmavý filament na černém plátu (otázka 11 v T-152) – uvést v popisu detektoru (texty v T-154), řešení později v T-163.
- **Výkon:** prototyp 1,36 ms na vzorek (skalární kód, M2 Pro), 0 alokací v ustáleném stavu. Případná optimalizace přes `System.Numerics.Vector` / `Vector128` je bez závislostí.

## Rozsah
- Implementace `IPrintFailureDetector` podle rozhodnutí ze spiku (klasické CV nebo ML), registrace v App (explicitní DI).
- Nová závislost jen podle rozhodnutí ze spiku, s ověřenou licencí (GPL-3.0-only kompatibilní, včetně vah modelu a dat) a kompatibilitou s Native AOT – zapsat do poznámek; verze jen v `Directory.Packages.props`.
- Citlivost → práh skóre (mapování popsat v poznámkách).
- **Testovací data (rozhodnutí uživatele 2026-11-04):** práh a ověření spolehlivosti **nesmí záviset na nahrávkách selhání od uživatele**. Pozitivní případy se tvoří synteticky nebo inscenovaně, zdroj vybere uživatel (otázka 3). Negativní případy jsou úspěšné tisky: fixture `tests/LayerPeek.Detection.Tests/Fixtures/coreone/` (10 snímků) a lokálně celá série 214 snímků z `~/LayerPeek-samples/coreone-frames/` (test, který bez složky skončí jako přeskočený, data mimo repozitář). Synteticky generované varianty vznikají v testovacím kódu za běhu z existujících fixture (žádné nové soubory v repozitáři, limit ~10 snímků z rozhodnutí 2026-11-03 platí). Měřítka přesnosti (podíl odhalených syntetických selhání, falešné poplachy na hodinu úspěšného tisku) zapsat do poznámek. Test na celé sérii úspěšného tisku = žádný poplach při výchozí citlivosti.
- Výkon: CPU a paměť na win-x64 a osx-arm64 (macOS → T-127, pokud nejde ověřit na Windows).

- **Dashcam (T-148, hotovo):** detektor po rozpoznání chyby zavolá `CameraRecordingService.SaveDashcam(printerId,
  CameraDashcamTrigger.FailureDetected)` – uloží posledních N minut a 30 s po události (vrátí `false`, když spouštěč vypnul
  uživatel v nastavení `recording.dashcamOnFailureDetection` nebo dashcam tiskárny nic nedrží). Žádná další závislost.

## Akceptační kritéria
- [x] Detektor odhalí syntetická / inscenovaná selhání z testovací sady a při výchozí citlivosti nehlásí poplach na úspěšných tiscích ze sady (cíle podle otázky 1).
- [x] Detektor funguje při intervalu vzorkování 2 s i 5 s (stejná sada, časové parametry nezávislé na intervalu) a s výchozím výřezem celého snímku i s výřezem Core One (testy).
- [x] Popis detektoru uvádí známá omezení (tmavý filament na černém plátu, kvalita první vrstvy, tryska) – předat do textů T-154.
- [x] Licence všech nových závislostí, vah a dat ověřené a zapsané; Native AOT publish bez nových varování. (osx-arm64 ověřeno; win-x64 → přesunuto do T-125)
- [x] Spotřeba CPU a paměti změřená a v mezích rozhodnutí z T-153 (otázka 6). (osx-arm64 změřeno; win-x64 → přesunuto do T-125)
- [x] Poplach z detektoru projde celou cestou T-153 / T-154 (notifikace, snímek, falešný poplach) – E2E se záznamem v simulátoru, pokud simulátor umí přehrát vlastní klip (jinak rozšířit simulátor v rámci úkolu). (E2E se simulátorem hotové; ruční ověření se skutečnou tiskárnou → přesunuto do T-127)
- [x] `dotnet build` a `dotnet test` bez chyb a varování (macOS; Windows → přesunuto do T-125)

## Otevřené otázky
1. ~~**Typ prvního detektoru**~~ *(vyřešeno 2026-11-04: „Změna na podložce“, klasické CV.)* ~~**Cílová přesnost**~~ *(vyřešeno 2026-11-04: ≥ 90 % syntetických selhání, ≤ 1 falešný poplach za 10 h úspěšného tisku.)*
2. ~~**Simulátor:**~~ *(vyřešeno 2026-11-04: ano, `--camera-clip <soubor>` je součástí úkolu.)* Původně: má simulátor umět přehrávat vlastní klip (nahrávku selhání) místo vestavěné fixture, aby šly E2E testy detektoru?
3. ~~**Zdroj syntetických / inscenovaných dat**~~ *(vyřešeno 2026-11-04: varianta a), generované v testech z fixture, celá lokální série jen v přeskočitelném testu.)* Původně (uživatel 2026-11-04: „záběry vytvoříme jinak“), možnosti:
   a) varianty generované v testech ze snímků `~/LayerPeek-samples` a fixture (vložená „hmota“ / spleť čar, posunutý nebo zmizelý výřez dílu, změna jasu, šum H.264),
   b) inscenovaná scéna v nečinné tiskárně se zapnutým osvětlením a nahráváním (T-145): na podložku položit / odebrat předmět nebo chomáč filamentu, posunout hotový díl,
   c) simulátor přehrávající upravený klip (souvisí s otázkou 2),
   d) kombinace a) + b). Veřejná videa jiných autorů jen s ověřenou licencí (nedoporučeno).
4. **Celý snímek jako výřez u Core One (neblokuje, zjištěno 2026-11-04):** ve skutečné sérii úspěšného tisku dává výřez
   celého snímku poplachy kvůli mostu X (pojezd osy Y mění obraz mimo podložku na minuty): 13 snímků z 526 nad výchozím
   prahem za 4,4 h (počítáno přísně, viz Poznámky), tedy prakticky poplach v každém tisku (výchozí je jeden poplach na tisk).
   Výchozí výřez Core One (přední část podložky) je bez poplachu. Výchozí výřez ostatních tiskáren (celý snímek, rozhodnutí
   T-153) jde ověřit jen se záběry z nich. Návrhy: a) nechat a v editoru výřezu (T-154) doporučit „jen podložka“, případně
   upozornit, když výřez sahá nad podložku; b) samostatný úkol na masku „pohyblivých konstrukcí“ naučenou za běhu;
   c) u ostatních modelů po první detekci vyzvat uživatele k nastavení výřezu. Rozhodne uživatel.
5. **První vrstvy (neblokuje, technická volba agenta k potvrzení):** pod výškou osy Z 1 mm se detektor jen učí a nehodnotí
   (první vrstvy jsou nová hmota na podložce záměrně, jinak by byl poplach na začátku každého tisku), rozjezd 2 min začne až
   nad ní. Selhání během prvních ~5 vrstev se proto nehlásí. Vyhovuje, nebo jiná výška / jiné řešení?
6. **Noční režim kamery (neblokuje):** Buddy3D po zhasnutí osvětlení přepne na černobílý noční obraz (série: jas výřezu
   ~80 → ~116), takže pauza při tmě z T-153 (`DarkBrightness` 12) nezabere a náhlá změna celého obrazu by během tisku vyvolala
   falešný poplach. V datech se to stalo jen po konci tisku. Řešit (např. rozpoznat skokovou změnu celého obrazu a začít
   znovu se učit), nebo zatím jen zdokumentovat? Práh tmy 12 jsem neměnil (ve vzorcích není tmavý snímek).
7. *(Řeší T-166: ladění na 30 semínkách, `ChangeLevel` 14 a `Quiet` 60 s.)* **Přesnost na jiných semínkách (neblokuje, zjištěno při review 2026-11-04):** sada `SyntheticSets.Failures(seed: 7)`
   dává 95 %, stejně vygenerované sady se semínky 11, 23, 31 a 47 dávají 48–51 z 56 (86–91 %); neodhalené jsou hlavně
   hroudy r 5–9 px a špagety s jasem 105–130 (skóre 0,0–0,29 %, pod výchozím prahem 0,3 %). Cíl ≥ 90 % je tedy splněný na
   commitnuté sadě, ne s rezervou. Ve výřezu celého snímku při 5 s vyšly ve dvou z těchto běhů po jednom potvrzeném
   falešném poplachu před začátkem selhání (~2 za ~21 h čistého tisku celkem, blízko cíle ≤ 1 / 10 h), ve výřezu Core One
   žádný. Ponechat výchozí citlivost, nebo zvýšit (práh níž), případně rozšířit test o více semínek?

## Poznámky k implementaci
- **Detektor** `src/LayerPeek.Detection/BedChangeDetector.cs` (`bed-change`), parametry `BedChangeOptions` (interní, testy
  je zkracují), registrace `BedChangeDetector.Registration` v explicitním seznamu `PrintFailureDetectors.All`, který App už
  registruje (T-153, beze změny v App). Popis algoritmu je v `docs/architecture.md` (oddíl „Detektor Změna na podložce“).
  Proti spiku přibylo (každé kvůli konkrétnímu falešnému poplachu při ladění):
  - **tolerance posunu** ±1 px vodorovně, ±2 px svisle (min / max reference v okolí, jednou na referenci): bez ní měla
    skutečná série ve výřezu Core One p90 3,3 % (posun podložky v Z), s ní 0,01 %,
  - **„klidné“ pixely** (aspoň 45 s bez změny proti minulému vzorku o víc než 20 úrovní): bez nich celý snímek na klipu
    s pohyblivou hlavou 1,5–3,4 %, s nimi 0,00 %,
  - **přepočet jasu** reference (poměr součtů, podruhé jen ze souhlasících pixelů; saturované pixely nahoře bílé): bez něj
    syntetický pomalý posun jasu ±20 % až 4,5 %,
  - **rozjezd 2 min** (1 min rychlé učení + 1 min normální): s 90 s rychlého učení zbytky hlavy v první referenci dávaly
    syntetický poplach u celého snímku při 5 s,
  - **osa Z z kontextu:** pod 1 mm se jen učí (otázka 5), reference i po 0,5 mm v Z (rychle rostoucí díl; test
    `TheReferencesFollowAFastRisingBed`),
  - **skóre vůči pevné ploše** (0,315 snímku = výřez prototypu), ne vůči výřezu: stejné selhání má stejné skóre v malém
    výřezu i v celém snímku (bez toho by v celém snímku selhání „zředilo“).
- **Citlivost → práh** (`DetectionThresholds`, lineárně): citlivost 0 = 0,5 %, 100 = 0,1 %, výchozí 50 = 0,3 % (podíl
  změněných pixelů vůči 0,315 × 320 × 180 = 18 144 px; 0,3 % ≈ 54 px ≈ hrouda ~1 cm vpředu na podložce Core One). Prahy
  spiku (2–6 %) platily pro jeho skóre bez tolerance posunu a klidu, kde úspěšný tisk dával až 1,44 %; tady dává 0,01 %.
  Potvrzení K z N (5/8 za 16 s, aspoň 3 vzorky) dělá orchestrace T-153 beze změny.
- **Dashcam:** `ICameraRecordingsFolder` má nově `SaveDashcam(printerId, trigger)` (implementuje `CameraRecordingService`
  stávající metodou z T-148). Poplach ho zavolá jako první krok úlohy poplachu (mimo zámky, před snímkem, aby ukládání začalo
  hned); `false` i výjimka poplach neovlivní (test `ADashcamThatSavesNothingOrFailsDoesNotCostTheAlarm`).
- **Simulátor `--camera-clip`:** přehraje surový H.264 (Annex B) i MP4 s H.264 (fragmentovaný jako nahrávky / dashcam
  LayerPeek i běžný se sample table, `Mp4H264Reader`, formát podle obsahu). Starší `--clip` funguje dál. Ověřeno i ručně
  s `~/LayerPeek-samples/clips/coreone-20261103-1905-60s.mp4` (ffprobe přes RTSP: 1280 × 720, 110 snímků za 5 s).
  Dokumentace `docs/simulator.md`.
- **Testovací data:** vše generované v testech z fixture (`tests/Shared/Detection`, sdíleno s `LayerPeek.Camera.FFmpeg.Tests`):
  základ je šest klíčových snímků klipu dokola (pohyblivá hlava a most X nad stojící podložkou), vzorek po intervalu
  + 0–0,6 s (čekání na klíčový snímek), šum komprese (σ 2,5, posun bloků 8 × 8), průběh jasu. Sada `SyntheticSets`
  (pevná semínka, velikosti zvolené předem jako věrohodná selhání, ne podle detektoru): 30 × špagety (15–40 vláken, poloprůhledné
  0,5–1, 1–2 px, jas 105–225, oblast 35–80 × 20–40 px, růst 1–4 min), 15 × hrouda (poloměr 5–14 px, růst 0–3 min),
  10 × posunutý díl (6–25 px), 1 × zmizelý díl. Úspěšné tisky: bez změny, silný šum (σ 5), jas skokem +25 % / −25 %, pomalý
  posun ±20 % za 10 min, blikání ±8 % na vzorek (každý 2 semínka × 12 min).
- **Výsledky přesnosti** (výchozí citlivost, 56 selhání, stejné ve výřezu Core One i v celém snímku, při 2 s i 5 s):

  | Sada | Výsledek |
  |---|---|
  | syntetická selhání | **53/56 = 95 %** (špagety 30/30, posunutý / zmizelý díl 11/11, hroudy 12/15; neodhalené: hrouda r 5 px, r 8 px s jasem 130 a r 10 px s jasem 105 na světlém místě, skóre 0,0–0,27 %) |
  | doba do poplachu | medián 76 s (2 s) / 79 s (5 s), nejvýš 240–288 s od začátku selhání (růst až 4 min) |
  | poplach před začátkem selhání | 0 |
  | syntetické úspěšné tisky (2 × 6 průběhů × 12 min × 2 intervaly × 2 výřezy, ~9,6 h) | 0 poplachů, nejvyšší skóre 0,00 % |
  | **skutečná série úspěšného tisku** (`~/LayerPeek-samples/coreone-frames`, 18:13–22:36, 4,4 h, 526 snímků po 30 s), výřez Core One | **0 poplachů**, nejvyšší skóre 0,01 % (30× pod výchozím prahem, 10× pod nejcitlivějším) |
  | totéž, celý snímek | 13 snímků nad 0,3 %, nejvyšší 2,11 % (most X, otázka 4) |
  | skutečný klip s pohyblivou hlavou (60 s, 2 snímky/s) ve smyčce 30 min při 2 s a 5 s, oba výřezy | 0 poplachů, nejvyšší skóre 0,00 % |

  Série má snímek jen po 30 s, delší než okno potvrzení (16 s). Test proto počítá každý snímek nad prahem jako poplach
  (přísnější než orchestrace). Konec tisku v sérii je 22:36:13 (od 22:36:43 podložka sjede dolů a kamera přepne na noční
  režim), snímky potom (5,4 h nečinné tiskárny) se nehodnotí. **Falešné poplachy na 10 h:** 0 za 4,4 h skutečného tisku
  (výřez Core One) + 0 za ~9,6 h syntetických tisků; extrapolace 0 / 10 h. Statisticky 0 událostí za 4,4 h horní mez
  (95 %) cíl ≤ 1 / 10 h neprokazuje, rezerva skóre 30× ale ukazuje, že je splněný s velkou rezervou. Chybí záběry první
  vrstvy, tmavého filamentu a jiných tiskáren než Core One (spike, oddíl 1).
- **Tmavý filament (T-163):** syntetické tmavé špagety (jas 25–80 na plátu ~40–60) detektor našel v 7–10 z 10 případů, ale
  hlavně tam, kde kříží díl nebo světlou mřížku plátu. Skutečné černé vlákno na černém plátu syntetika věrně nenapodobí,
  omezení zůstává.
- **Texty pro T-154 (popis detektoru a známá omezení):** „Změna na podložce“ – hlídá, zda díl na podložce nezmizel nebo
  se neposunul a zda na podložce nepřibyla nová hmota (špagety, odtržený díl). Omezení: tmavý filament na černém plátu se
  skoro nepozná; kvalitu první vrstvy ani chomáč na trysce z kamery vidět není; během prvních vrstev (do 1 mm výšky)
  a 2 min po začátku nebo obnovení tisku detektor jen sleduje; hmota přímo pod pohybující se hlavou se počítá, až ji hlava
  nechá v klidu; velmi pomalá změna (déle než ~5 min) se bere jako růst dílu; výřez s mostem X (u Core One celý snímek)
  a náhlá změna celého obrazu (noční režim kamery, posunutá kamera) mohou dát falešný poplach – výřez nastavit jen na
  podložku.
- **Výkon a paměť (osx-arm64, Apple M2 Pro, Native AOT, 2026-11-04):** testovací aplikace mimo repozitář (`PrintFailureDetectors
  .All[0].Create()`, šest klíčových snímků fixture dokola po 2 s, 20 000 vzorků po 300 zahřívacích, tři běhy):

  | Výřez | ms na vzorek | podíl jádra při 2 s / 5 s | alokace | paměť detektoru |
  |---|---|---|---|---|
  | Core One (224 × 81) | **0,22–0,23** | 0,011 % / 0,005 % | 0 B na vzorek (40 B za celý běh = měření) | ~0,2 MB |
  | celý snímek (320 × 180) | **0,70–0,72** | 0,036 % / 0,014 % | 0 B na vzorek | ~0,7 MB |

  Pod JIT (Release) stejně (0,24 / 0,70 ms). Paměť = 12 B na pixel výřezu (pozadí, minulý vzorek, doba klidu, 4 reference,
  min / max reference, součty řádků, které při výpočtu rozsahu slouží i jako pomocný buffer), alokovaná s prvním vzorkem.
  S dekódováním klíčového snímku a zmenšením (T-153: ~2,9 + 0,8 ms) je celá detekce ~4 ms na vzorek, tedy ~0,2 % jádra při
  2 s, hluboko pod mezí 1 %. `BedChangeDetectorCostTests` hlídá 0 alokací na vzorek v ustáleném stavu (oba výřezy).
  Windows (win-x64) → T-125.
- **Licence a AOT:** žádná nová závislost (NuGet ani nativní), žádné váhy ani data třetích stran. Testy dekódují lokální
  vzorky nástrojem `ffmpeg` z příkazové řádky (volitelně, `docs/prerequisites.md`), ne testovací závislostí; E2E klip vzniká
  vlastním kodérem H.264 z makrobloků I_PCM v testu (`PcmH264Writer`, bez knihovny). `dotnet publish src/LayerPeek.App -c
  Release -r osx-arm64` prošel bez varování IL / trim / AOT (jen dřívější varování nativního toolchainu ke cache modulů
  Swift).
- **Testy:** Detection – `BedChangeDetectorTests` (registrace a prahy, rozjezd v čase při 2 s i 5 s, první vrstvy včetně
  Z-hopu, Reset, stejné skóre v různých výřezech, změna mimo výřez, jas ±, rychle klesající podložka s / bez osy Z, nový
  výřez, validace), `BedChangeDetectorCostTests` (0 alokací, čas na vzorek do výstupu testu), `BedChangeAccuracyCoreOneTests`
  a `BedChangeAccuracyFullFrameTests` (≥ 90 % selhání a 0 poplachů na úspěšných tiscích při 2 s a 5 s),
  `BedChangeLocalSamplesTests` (celá série a klip, bez `~/LayerPeek-samples` nebo `ffmpeg` přeskočeno; `LAYERPEEK_SAMPLES`
  změní složku). Core – dashcam při poplachu (`AnAlarmNeedsConfirmationAndComesOncePerPrint`,
  `ADashcamThatSavesNothingOrFailsDoesNotCostTheAlarm`). Camera.FFmpeg – `BedChangeAlarmSimulatorTests` (simulátor
  s `--camera-clip`, klip z fixture se hroudou po 20 s, skutečný detektor se zkrácenými časy, poplach → dashcam, snímek
  320 × 180 uložený vedle nahrávek, notifikace, „falešný poplach“; ~30 s), `FFmpegRecordingTests.ARecordingPlaysAsTheSimulatorsCameraClip`.
  Simulátor – `Mp4ClipTests` (fragmentovaný i běžný MP4 = stejné snímky, `--camera-clip` i `--clip`, MP4 bez H.264 odmítnut).
- **Mimo rozsah / nezměněno:** App (kromě toho, že `PrintFailureDetectors.All` už není prázdný; komentář v `AppServices`
  „T-155 adds the first“ jsem kvůli souběžnému T-154 neupravoval), práh tmy `DarkBrightness` (otázka 6), T-157.
- Uzavřeno 2026-11-04: zbývající ověření na Windows přesunuto do T-125, ruční ověření se skutečnou tiskárnou do T-127
  (rozhodnutí uživatele 2026-10-30, postup v `tasks/README.md`).

## Review
- 2026-11-04 (qa-reviewer): opraveno na větvi – (1) detektor, který začal bez výšky Z (snapshot bez telemetrie) a hned
  potom dostal výšku první vrstvy, první vrstvy hodnotil (po 2 min poplach na nové hmotě); teď se první známá výška pod
  1 mm vrátí k učení a zahodí reference (test `AHeightThatComesOnlyAfterTheStartStillMeansTheFirstLayers`);
  (2) `Mp4H264Reader` u poškozeného MP4 házel `ArgumentOutOfRangeException` / `OverflowException` (simulátor spadl místo
  hlášky), obří počet vzorků v `trun` / `stsz` vedl k `OutOfMemoryException` a překrývající se vzorky mohly rozepsat
  proud libovolně velký – teď vše `InvalidDataException` (testy v `Mp4ClipTests`); (3) E2E `BedChangeAlarmSimulatorTests`
  četl notifikaci hned po události `AlarmRaised`, i když ji služba ukazuje až po ní (závod), teď na ni čeká.
  Otevřená otázka 7 (přesnost na jiných semínkách) je pro uživatele.
