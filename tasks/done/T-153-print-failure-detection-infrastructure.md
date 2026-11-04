---
id: T-153
title: Detekce chyb tisku – infrastruktura (projekt LayerPeek.Detection, vzorkování snímků, kontext tisku, události)
status: in-progress
owner: core-dev
priority: P2
platform: all
depends_on: [T-143, T-152]
---

## Cíl
Společný základ pro všechny detektory selhání tisku (nápad 3, fáze 1): vzorkování snímků ze sdíleného spojení kamery na pozadí, detekce jen během tisku s kontextem z PrusaLink, vyhodnocení výsledků detektorů, událost pro notifikaci, uložení snímku při poplachu a zpětná vazba „falešný poplach“. Skutečný detektor přijde v T-155, UI v T-154.

## Navržené vrstvy (návrh architekta, potvrdit v otázce 1)
```
App ──► LayerPeek.Detection ──► Core
 │                               ▲
 └──► Camera.FFmpeg ─────────────┘
```
- **Core (`Core/Detection`)** – jen abstrakce a orchestrace bez závislostí: `IPrintFailureDetector` (typ detektoru, analýza vzorku → skóre / výsledek), `PrintFailureKind` (nebo textové id typu, aby přibývaly detektory bez změny enumu – volí core-dev podle spiku), nastavení detektoru (zapnuto, citlivost), `PrintFailureDetectionService` (orchestrace), `PrintFailureContext` (stav, Z, progress, id úlohy, čas od začátku tisku).
- **`src/LayerPeek.Detection`** (nový projekt, `IsAotCompatible=true` přes `src/Directory.Build.props`) – implementace detektorů, později případně závislost na runtime z T-152. Referencuje jen Core. Testy v `tests/LayerPeek.Detection.Tests` (xUnit v3, MTP).
- **App** – composition root: registruje detektory explicitně (žádné skenování assembly), napojí notifikace a UI (T-154).
- Core nesmí znát Avalonii, FFmpeg ani runtime ML; Detection nesmí znát Avalonii ani FFmpeg (snímky dostává jako `CameraFrame` / vlastní zmenšený buffer z Core).

## Rozsah
- **Vzorkování (rozhodnutí uživatele 2026-11-04):** odběr snímků přes hub (T-143) **jen ve stavu `Printing`** tiskárny, která má zapnutou kameru a aspoň jeden zapnutý detektor. Mimo tisk (nečinná, pozastavená, offline tiskárna) detekce nedrží spojení s kamerou, nic nedekóduje a nic nepočítá. Okno kamery přitom nemusí být otevřené.
  - **Interval vzorkování 2–5 s**, nastavitelný v tomto rozsahu (hodnoty mimo rozsah se při načtení oříznou), **výchozí 2 s** (návrh architekta: odpovídá prototypu T-152 a u Buddy3D vychází na každý klíčový snímek, ~2,3 s). Skutečný interval = první klíčový snímek po uplynutí intervalu.
  - **Nová volba odběru v hubu** (`CameraFrameSubscriptionOptions` nebo podobně, upřesní core-dev): jen klíčové snímky + zmenšení a šedotón při kopii (Gray8 podle `DetectionFrameSpec` detektoru, 320×180). Když detekci nestačí žádný jiný odběratel dekódovaných snímků (okno kamery zavřené, náhled v nastavení zavřený), `Camera.FFmpeg` dekóduje jen klíčové snímky a klíčové snímky, které na vzorek nepřipadnou, vůbec nedekóduje. Nahrávání (T-144 / T-145) odebírá pakety, plné dekódování nevynutí. Když je otevřené okno kamery, dekóduje se plně kvůli němu a detekce si bere jen zmenšenou šedotónovou kopii v intervalu.
  - Zmenšení do znovupoužívaného bufferu bez alokací na vzorek.
- **Výřez podložky (ROI, rozhodnutí uživatele 2026-11-04):** model výřezu u každé tiskárny – obdélník v **normalizovaných souřadnicích** (0–1 vůči šířce a výšce snímku, nezávislé na rozlišení kamery), uložený v konfiguraci tiskárny (T-002) a převáděný při načtení (validace, oříznutí do 0–1, nenulová velikost). Výchozí výřez = **celý snímek**. U tiskárny modelu **Core One** (model z PrusaLink jako u T-039) se při prvním zapnutí detekce předvyplní výřez pro standardní držák kamery z prototypu T-152: dolních 45 % výšky a prostředních 70 % šířky (`X = 0,15; Y = 0,55; Š = 0,70; V = 0,45`). Výřez dostane detektor (např. v `PrintFailureContext` nebo `Reset`, upřesní core-dev) a skóre počítá jen uvnitř něj. Editor výřezu je v T-154. Automatické nalezení podložky v obraze může přijít později (zatím bez úkolu).
- **Kontext tisku** ze snapshotů (`PrinterState`, `Telemetry.AxisZ`, `PrintJob.ProgressPercent`, id úlohy), aby detektor věděl např., že se tiskne první vrstva. Detekce se spouští jen ve stavu `Printing`; při `Paused`, `Attention`, konci tisku a offline se zastaví a vyčistí stav detektorů.
- **Vyhodnocení:** detektor vrací skóre; poplach až po potvrzení (např. K z N po sobě jdoucích vzorků nad prahem podle citlivosti), nejvýš jeden poplach na typ a úlohu (nebo s odstupem – otázka 3). Analýza mimo vlákno dekodéru i mimo UI vlákno; pomalý detektor zahazuje vzorky, nebrzdí stream.
- **Událost poplachu** (např. nový `PrintEventKind` nebo vlastní událost) s tiskárnou, typem, skóre, časem a cestou k uloženému snímku; doručení přes `INotificationService` (texty v T-154).
- **Snímek při poplachu:** uložení snímku (formát a složka – otázka 4) a jeho rotace (limit počtu / velikosti). Odběr detekce má jen zmenšené šedotónové snímky, proto si orchestrace při poplachu vyžádá jeden plný barevný snímek (krátký odběr bez zmenšení na nejbližší klíčový snímek).
- **Zpětná vazba „falešný poplach“:** API pro označení poplachu jako falešného; uloží se lokálně metadata (typ, skóre, čas, snímek) jako podklad pro ladění detektorů; nic se neodesílá ze stroje.
- **Testovací detektor** (jen v testech): falešný detektor řízený skriptem, aby šla infrastruktura otestovat bez skutečného detektoru.
- Dokumentace: `docs/architecture.md` (Pravidla vrstev, nový projekt, tok dat). Úpravu oddílu „Repository layout“ v `CLAUDE.md` předložit uživateli ke schválení (agent ji sám neprovádí).

## Vstupy ze spiku T-152 (2026-11-03)
Podrobně v `docs/detection-spike.md` (oddíly 4–7). Doporučení T-152 uživatel 2026-11-04 schválil (otázky 1, 5, 9, 10 v T-152), navíc chce vzorkování po 2–5 s jen během tisku a editovatelný výřez podložky (viz Rozsah).
- **Formát vzorku:** šedotón 8 bit, 320×180 (detektor ho deklaruje přes `FrameSpec`, orchestrace zmenšuje). Snímek poplachu se ukládá z plného rozlišení posledního snímku.
- **Frekvence:** nastavitelný interval 2–5 s, výchozí 2 s, vzorek z klíčového snímku (Buddy3D ~2,3 s). Detektory i orchestrace musí počítat v čase (`Timestamp`), ne v počtu vzorků (např. „pozadí před ~5 min“, rozjezd 1–2 min), aby fungovaly při každém intervalu z rozsahu.
- **Navržené rozhraní:** `IPrintFailureDetector { string Id; DetectionFrameSpec FrameSpec; void Reset(in PrintFailureContext); DetectionResult Analyze(in DetectionSample); }`. `DetectionSample` je `readonly ref struct` (šedotónové pixely vypůjčené do návratu z `Analyze`, rozměry, `Timestamp`, `Context`), `DetectionResult(bool IsWarmedUp, float Score)`. `PrintFailureContext` obsahuje `JobId`, `AxisZ`, `ProgressPercent` a `PrintElapsed` (od začátku tisku nebo od obnovení). Detektor je synchronní, jen CPU a v ustáleném stavu bez alokací. Vzorkování, převod citlivosti na práh, potvrzení K z N a snímek poplachu dělá orchestrace.
- **Rozjezd:** po startu, obnovení tisku a novém připojení kamery `Reset` a poplachy potlačené, dokud detektor nevrátí `IsWarmedUp` (u prvního detektoru ~1–2 min).
- **Tma:** orchestrace měří průměrný jas vzorku a pod prahem (vypnuté LED komory) detekci pozastaví. Práh doplní T-155.
- **Paměť a CPU:** dnešní `SubscribeFrames` kopíruje celé snímky BGRA 1280×720 do tří buferů (~11 MB na tiskárnu). Nová volba odběru se zmenšením / šedotónem při kopii (schváleno, otázka 10 v T-152) to sníží na ~0,2 MB. Detektor sám stojí ~1,4 ms na vzorek (M2 Pro), hlavní náklad je dekódování streamu (~1,9 % jádra při plném dekódování, ~0,13 % jen klíčové snímky).
- **Fixture:** `tests/LayerPeek.Detection.Tests/Fixtures/coreone/` (10 snímků 640×360 z T-152). Testy si je načtou jako soubory. K dekódování JPEG v testech použijte závislost jen pro testy (s ověřenou licencí), nebo snímky převeďte na surová šedotónová data, která se commitnou vedle fixture. Rozhodne core-dev, nová závislost jen pro testy → otázka 7.

## Akceptační kritéria
- [ ] Nový projekt `LayerPeek.Detection` a testovací projekt jsou v `LayerPeek.slnx`, build bez varování (včetně AOT / trim analyzátorů).
- [ ] Vzorkování běží jen ve stavu `Printing` a jen se zapnutým detektorem, bez otevřeného okna kamery (test se simulátorem: scénář tisku → vzorky; nečinná, pozastavená a offline tiskárna → žádné spojení, žádné dekódování a žádná analýza kvůli detekci).
- [ ] Hub má volbu odběru „jen klíčové snímky, zmenšené, šedotón“. Když je detekce jediným odběratelem dekódovaných snímků, dekódují se jen klíčové snímky potřebné pro vzorek (test s počítadlem dekódovaných snímků, např. přes simulátor nebo falešný dekodér). Otevřené okno kamery dál dostává plné snímky (test).
- [ ] Interval vzorkování je nastavitelný v rozsahu 2–5 s (výchozí 2 s, hodnoty mimo rozsah se oříznou). Skutečná frekvence a zmenšení odpovídají nastavení; ustálený stav bez alokací na vzorek (test s falešnými hodinami).
- [ ] Výřez podložky: model u tiskárny v normalizovaných souřadnicích, uložení a načtení (validace, oříznutí), výchozí celý snímek, předvyplnění pro Core One. Detektor dostane výřez a falešný detektor v testu ověří, že se skóre počítá jen z něj (testy).
- [ ] Potvrzení poplachu (K z N), jeden poplach na typ a úlohu a vyčištění stavu na konci tisku – testy s falešným detektorem a falešnými hodinami.
- [ ] Poplach vyvolá notifikaci přes `INotificationService` a uloží snímek; rotace snímků funguje (test v dočasné složce, úklid i po pádu).
- [ ] „Falešný poplach“ uloží lokální záznam (test).
- [ ] Core nereferencuje Detection ani žádný runtime ML / CV; Detection nereferencuje Avalonii ani FFmpeg (test architektury nebo revize referencí).
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. **Vrstvy:** souhlasí uživatel s novým projektem `src/LayerPeek.Detection` (App → Detection → Core) a s úpravou oddílu repozitáře v `CLAUDE.md`? Název projektu?
2. ~~**Frekvence vzorkování a rozlišení**~~ – *vyřešeno 2026-11-04:* interval 2–5 s (nastavitelný), jen klíčové snímky, šedotón 320×180 zmenšený v hubu, jen během tisku. Výchozí 2 s navrhl architekt (uživatel může změnit, ovládání v T-154).
3. **Opakování poplachu:** jen jeden poplach na typ a tisk, nebo znovu po N minutách, pokud problém trvá?
4. **Snímky poplachů:** ukládat (formát JPEG / PNG – kodér musí být AOT a licenčně v pořádku), do jaké složky (vedle nahrávek T-145?), jaký limit?
5. **Zpětná vazba „falešný poplach“:** jen lokální záznam pro ruční ladění, nebo i ovlivnění citlivosti automaticky (např. zvýšit práh u tiskárny)?
6. **Výkon:** jaký nejvyšší přípustný průměrný odběr CPU detekce na pozadí (např. < 2 % jednoho jádra)? *Měření T-152 (M2 Pro): detektor ~0,07 % jádra, dekódování streamu ~1,9 % (plné) nebo ~0,13 % (jen klíčové snímky) na tiskárnu. Uživatel 2026-11-04 schválil dekódování jen klíčových snímků a vzorkování jen během tisku. Konkrétní horní mez zatím neurčil.*
8. **Předvyplněný výřez Core One:** předvyplnit ho jen podle modelu tiskárny (o držáku kamery aplikace neví), nebo se uživatele při prvním zapnutí detekce zeptat, zda má standardní držák? Návrh architekta: předvyplnit podle modelu, uživatel výřez upraví v editoru (T-154).
7. **Dekódování JPEG fixture v testech:** smí testovací projekt dostat závislost jen pro testy (např. dekodér JPEG, licence a AOT se ověří), nebo se fixture převedou jednou na surová šedotónová data a commitnou se (320×180 = 57,6 KB na snímek, 10 snímků ~0,6 MB navíc)?

## Poznámky k implementaci
(vyplní agent po dokončení)
