---
id: T-153
title: Detekce chyb tisku – infrastruktura (projekt LayerPeek.Detection, vzorkování snímků, kontext tisku, události)
status: todo
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
- **Vzorkování:** odběr dekódovaných snímků přes hub (T-143) jen během tisku tiskárny, která má zapnutou kameru a aspoň jeden zapnutý detektor. Frekvence ~1 snímek za 1–2 s (podle spiku), zmenšení (např. na šířku 320–640 px, šedotón / BGRA podle spiku) do znovupoužívaného bufferu bez alokací na vzorek. Okno kamery přitom nemusí být otevřené.
- **Kontext tisku** ze snapshotů (`PrinterState`, `Telemetry.AxisZ`, `PrintJob.ProgressPercent`, id úlohy), aby detektor věděl např., že se tiskne první vrstva. Detekce se spouští jen ve stavu `Printing`; při `Paused`, `Attention`, konci tisku a offline se zastaví a vyčistí stav detektorů.
- **Vyhodnocení:** detektor vrací skóre; poplach až po potvrzení (např. K z N po sobě jdoucích vzorků nad prahem podle citlivosti), nejvýš jeden poplach na typ a úlohu (nebo s odstupem – otázka 3). Analýza mimo vlákno dekodéru i mimo UI vlákno; pomalý detektor zahazuje vzorky, nebrzdí stream.
- **Událost poplachu** (např. nový `PrintEventKind` nebo vlastní událost) s tiskárnou, typem, skóre, časem a cestou k uloženému snímku; doručení přes `INotificationService` (texty v T-154).
- **Snímek při poplachu:** uložení snímku (formát a složka – otázka 4) a jeho rotace (limit počtu / velikosti).
- **Zpětná vazba „falešný poplach“:** API pro označení poplachu jako falešného; uloží se lokálně metadata (typ, skóre, čas, snímek) jako podklad pro ladění detektorů; nic se neodesílá ze stroje.
- **Testovací detektor** (jen v testech): falešný detektor řízený skriptem, aby šla infrastruktura otestovat bez skutečného detektoru.
- Dokumentace: `docs/architecture.md` (Pravidla vrstev, nový projekt, tok dat). Úpravu oddílu „Repository layout“ v `CLAUDE.md` předložit uživateli ke schválení (agent ji sám neprovádí).

## Vstupy ze spiku T-152 (2026-11-03)
Podrobně v `docs/detection-spike.md` (oddíly 4–6). Platí, pokud uživatel přijme doporučení T-152 (otázky 1, 5, 9, 10).
- **Formát vzorku:** šedotón 8 bit, 320×180 (detektor ho deklaruje přes `FrameSpec`, orchestrace zmenšuje). Snímek poplachu se ukládá z plného rozlišení posledního snímku.
- **Frekvence:** ~1 vzorek za 2 s. Pokud uživatel schválí dekódování jen klíčových snímků (otázka 9 v T-152), vzorek přichází s každým klíčovým snímkem (Buddy3D ~2,3 s).
- **Navržené rozhraní:** `IPrintFailureDetector { string Id; DetectionFrameSpec FrameSpec; void Reset(in PrintFailureContext); DetectionResult Analyze(in DetectionSample); }`. `DetectionSample` je `readonly ref struct` (šedotónové pixely vypůjčené do návratu z `Analyze`, rozměry, `Timestamp`, `Context`), `DetectionResult(bool IsWarmedUp, float Score)`. `PrintFailureContext` obsahuje `JobId`, `AxisZ`, `ProgressPercent` a `PrintElapsed` (od začátku tisku nebo od obnovení). Detektor je synchronní, jen CPU a v ustáleném stavu bez alokací. Vzorkování, převod citlivosti na práh, potvrzení K z N a snímek poplachu dělá orchestrace.
- **Rozjezd:** po startu, obnovení tisku a novém připojení kamery `Reset` a poplachy potlačené, dokud detektor nevrátí `IsWarmedUp` (u prvního detektoru ~1–2 min).
- **Tma:** orchestrace měří průměrný jas vzorku a pod prahem (vypnuté LED komory) detekci pozastaví. Práh doplní T-155.
- **Paměť a CPU:** dnešní `SubscribeFrames` kopíruje celé snímky BGRA 1280×720 do tří buferů (~11 MB na tiskárnu). Pokud uživatel schválí otázku 10 v T-152, přibude volba odběru se zmenšením / šedotónem při kopii (~0,2 MB). Detektor sám stojí ~1,4 ms na vzorek (M2 Pro), hlavní náklad je dekódování streamu (~1,9 % jádra při plném dekódování, ~0,13 % jen klíčové snímky).
- **Fixture:** `tests/LayerPeek.Detection.Tests/Fixtures/coreone/` (10 snímků 640×360 z T-152). Testy si je načtou jako soubory. K dekódování JPEG v testech použijte závislost jen pro testy (s ověřenou licencí), nebo snímky převeďte na surová šedotónová data, která se commitnou vedle fixture. Rozhodne core-dev, nová závislost jen pro testy → otázka 7.

## Akceptační kritéria
- [ ] Nový projekt `LayerPeek.Detection` a testovací projekt jsou v `LayerPeek.slnx`, build bez varování (včetně AOT / trim analyzátorů).
- [ ] Vzorkování běží jen během tisku a jen se zapnutým detektorem, bez otevřeného okna kamery (test se simulátorem: scénář tisku → vzorky; nečinná tiskárna → žádné spojení kvůli detekci).
- [ ] Frekvence vzorkování a zmenšení odpovídají nastavení; ustálený stav bez alokací na vzorek (test).
- [ ] Potvrzení poplachu (K z N), jeden poplach na typ a úlohu a vyčištění stavu na konci tisku – testy s falešným detektorem a falešnými hodinami.
- [ ] Poplach vyvolá notifikaci přes `INotificationService` a uloží snímek; rotace snímků funguje (test v dočasné složce, úklid i po pádu).
- [ ] „Falešný poplach“ uloží lokální záznam (test).
- [ ] Core nereferencuje Detection ani žádný runtime ML / CV; Detection nereferencuje Avalonii ani FFmpeg (test architektury nebo revize referencí).
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. **Vrstvy:** souhlasí uživatel s novým projektem `src/LayerPeek.Detection` (App → Detection → Core) a s úpravou oddílu repozitáře v `CLAUDE.md`? Název projektu?
2. **Frekvence vzorkování a rozlišení:** potvrdit podle spiku T-152 (návrh ~1 snímek za 1–2 s, zmenšený). *Doporučení T-152: ~1 vzorek za 2 s (nebo každý klíčový snímek), šedotón 320×180; souvisí s otázkami 9 a 10 v T-152.*
3. **Opakování poplachu:** jen jeden poplach na typ a tisk, nebo znovu po N minutách, pokud problém trvá?
4. **Snímky poplachů:** ukládat (formát JPEG / PNG – kodér musí být AOT a licenčně v pořádku), do jaké složky (vedle nahrávek T-145?), jaký limit?
5. **Zpětná vazba „falešný poplach“:** jen lokální záznam pro ruční ladění, nebo i ovlivnění citlivosti automaticky (např. zvýšit práh u tiskárny)?
6. **Výkon:** jaký nejvyšší přípustný průměrný odběr CPU detekce na pozadí (např. < 2 % jednoho jádra)? *Měření T-152 (M2 Pro): detektor ~0,07 % jádra, dekódování streamu ~1,9 % (plné) nebo ~0,13 % (jen klíčové snímky) na tiskárnu.*
7. **Dekódování JPEG fixture v testech:** smí testovací projekt dostat závislost jen pro testy (např. dekodér JPEG, licence a AOT se ověří), nebo se fixture převedou jednou na surová šedotónová data a commitnou se (320×180 = 57,6 KB na snímek, 10 snímků ~0,6 MB navíc)?

## Poznámky k implementaci
(vyplní agent po dokončení)
