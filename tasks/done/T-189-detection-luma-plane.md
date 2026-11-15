---
id: T-189
title: Detekce chyb tisku – snímek z jasové složky (Y) bez převodu do BGRA
status: done
owner: core-dev
priority: P3
platform: all
depends_on: []
---

## Cíl
Výkonnostní analýza (2026-11-06): detekce chyb tisku (T-153) dostává klíčový snímek převedený do plného BGRA a teprve
potom ho zmenšuje na šedotón. Jasová složka Y ze zdrojového YUV už šedotónový obraz je, takže převod barev a velká
alokace jsou zbytečné. Dopad je malý (jeden snímek za několik sekund).

## Rozsah
- Rozšířit poptávku po klíčovém snímku (`CameraStreamDemand.RequestKeyFrame`) o variantu „šedotón v dané velikosti“.
  `FFmpegDecoder` pak přes swscale (`AV_PIX_FMT_GRAY8`) zmenší rovinu Y rovnou na velikost, kterou detektor potřebuje.
- Detektory dostanou šedotónový snímek. Detektor, který potřebuje barvu, dostane dál BGRA.
- Ověřit, že výsledky detektorů na testovacích snímcích jsou stejné nebo v toleranci (rozdíl BT.601 / BT.709 a rozsahu).

## Akceptační kritéria
- [x] Detekce nepotřebuje BGRA snímek v plném rozlišení (test dekodéru nebo hubu).
- [x] Testy detektorů procházejí beze změny prahů, případná změna je zdůvodněná v poznámkách.
- [x] Timelapse a okno kamery dostávají dál BGRA.
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci

**Návrh (core-dev, 2026-11-11):**
- Poptávka: `CameraStreamDemand.RequestGrayscale(size, timestamp)` / `CancelGrayscale()`, `GrayscaleSize`, `GrayscaleDue`
  (výchozí nikdy; stejný princip jako `FullResolutionDue`, čtení hodin jen během požadavku). Velikost se zapisuje před
  časem, takže čtenář, který vidí nový čas, vidí i velikost.
- Hub (`ScheduleFrames`): odběratel s `Grayscale` (a bez `DisplaySize`) už není odběratel plného rozlišení, ale
  šedotónu; poptávka nese jednu velikost (první odběratel), odběratel jiné velikosti (zatím žádný) se počítá jako
  plné rozlišení a zmenší si snímek při kopii jako dřív. Poptávka převodů se zapisuje **před** požadavkem klíčového
  snímku (každý zápis je `Interlocked`, plná bariéra), aby dekodér, který vidí nový požadavek, věděl, do čeho snímek
  převést. `Dispatch` dá odběrateli šedotónu `CameraStreamItem.GrayscaleFrame` jeho velikosti, jinak plný snímek.
- Core: `CameraGrayscaleFrame` (vypůjčený šedotón s krokem řádku a velikostí zdroje), `CameraStreamItem.GrayscaleFrame`
  + `FromFrames(frame, display, grayscale)`, `CameraFrameExchange.PublishGrayscale` a `CameraFrameBuffer` kopie řádků
  (jméno `PublishGrayscale`, protože přetížení `Publish` by udělalo `Publish(default)` nejednoznačným).
  `GrayscaleScaler` zůstává jako záloha pro feed, který poptávku převodů nezná (výchozí `ICameraFeed.ReadAsync`, fakes
  v testech App), a pro druhou velikost. Feed, který omezuje plné rozlišení na `FullResolutionDue`, musí dodat i šedotón
  (zdokumentováno u `CameraStreamDemand`); v produkci je to jen `FFmpegCameraFeed`.
- Camera.FFmpeg: nový `GrayscaleConverter` (vlastní `SwsContext` přes `sws_getCachedContext`, vlastní připnutý buffer,
  řádky zarovnané na 64 B). Pro všechny 8bitové planární a semiplanární YUV (podle `av_pix_fmt_desc_get`: rovina 0 =
  samotné luma po 1 B) předá swscale **jen rovinu Y jako samostatný obraz `GRAY8`**, takže chroma ani RGB se vůbec
  nepočítají; jiný formát (např. 10bitový) převede celý. Filtr `SWS_AREA` = průměr ploch jako dřívější `GrayscaleScaler`
  i fixture detektorů (`scale=320:180:flags=area`). `AlignedBuffer` (zarovnaný připnutý buffer) je vytažený z
  `BgraConverter`, sdílí ho oba převodníky. `FFmpegDecoder.Convert` převádí jen formy, které jsou splatné, **i u
  samostatně dekódovaného klíčového snímku** (dřív ten vždy v plném BGRA, T-153/T-184). To je jediná změna chování:
  pro detekci samotnou se plné BGRA už nepřevádí vůbec a buffer 3,7 MB se nealokuje; snímek poplachu (lease bez
  `Grayscale`) dostane plné BGRA dál.
- **Rozsah barev (rozhodnutí v rámci úkolu):** výstup je vždy plný rozsah 0–255. Omezený rozsah (Y 16–235) se roztáhne,
  protože dřívější cesta dělala luma z BGRA v plném rozsahu (RGB z swscale je vždy plný rozsah), takže prahy jasu
  (`DarkBrightness`, tmavý filament) znamenají totéž. Buddy3D posílá plný rozsah (`yuvj420p`, `color_range=pc`, BT.709;
  ověřeno `ffprobe` na fixture i na klipu z `~/LayerPeek-samples`), tam se Y jen zmenší. swscale roztahuje rozsah při
  zmenšování (`sws_setColorspaceDetails` s cílovým rozsahem 1; ověřeno testem), ale obraz už ve velikosti vzorku jen
  zkopíruje a rozsah ignoruje (také ověřeno testem: 16 zůstalo 16), proto ho v tom vzácném případě roztáhne tabulka
  256 hodnot.
- BT.601 / BT.709: nová cesta dává Y podle barevného prostoru streamu (u Buddy3D BT.709), stará luma BT.601 z RGB. V šedé
  stejné, v sytých barvách se liší o jednotky úrovní. Podstatné: **fixture i lokální testy detektorů byly vždy z roviny Y**
  (FFmpeg CLI `-pix_fmt gray` s `flags=area`, viz `Fixtures/coreone/README.md` a `LocalSamples.Decode`), takže nová cesta
  odpovídá datům, na kterých se prahy ladily, lépe než ta stará.

**Ověření na snímcích (prahy beze změny, testy detektorů beze změny):**
- Pixely, nový proti starému vzorku (`GrayscaleSampleEquivalenceTests`): fixture `buddy3d.h264` (71 snímků) průměrný
  |rozdíl| 1,31 úrovně, průměrný posun +0,87, maximum 24 úrovní (sytě barevné detaily); lokální klip
  `coreone-20261103-1905-60s.mp4` (26 klíčových snímků) 1,05 / +0,91 / 29.
- Bed-change na klipu (30 min ve smyčce po 2 s, výřez Core One, celý snímek, režim tmavého filamentu): bez selhání skóre
  0 → 0, žádný poplach v obou cestách; s tmavou hroudou (r 6 px, luma 30) max. skóre 0,0042 → 0,0046 (tmavý filament
  0,0048 → 0,0052), max. rozdíl skóre 0,0006 (práh 0,0030), první poplach 15:36 → 15:32 (tmavý filament 15:22 → 15:14);
  se světlou hroudou (r 5 px, luma 200) max. rozdíl 0,0001, první poplach 15:34 → 15:34 ve všech třech variantách.
  Test hlídá: rozdíl skóre ≤ ¼ prahu, jinak posouzené vzorky jen u prahu, stejné poplachy, posun prvního poplachu ≤ 20 s.
- Air-printing potřebuje podložku, která se hýbe se Z; klip ve smyčce to neumí, proto jednorázově (necommitnutá úprava
  `LocalSamples.Decode`: FFmpeg CLI → plné BGRA → stará luma BT.601 a průměr ploch, vrácena): lokální série úlohy 354 a
  4,4 h série se starou i novou cestou dávají stejný výsledek – max. skóre 0 % (5 i 10 s), surové skóre 12 % → 10 %,
  4,4 h série 7 % → 7 % (posun podložky 1,04/1,61/2,87 px v obou), úloha 354 posun 1,30/1,71/1,90 → 1,30/1,71/1,91 px, žádný poplach. JPEG série jsou BT.601, takže rozdíl je
  tam jen zaokrouhlení. Všech 7 lokálních testů detektorů prošlo v obou cestách.

**Měření klíčového snímku pro detekci** (Apple Silicon, Release, 1280×720 → 320×180, průměr 300 opakování, dočasný test):
před: převod BGRA 1280×720 0,15–0,20 ms + zmenšení v C# (`GrayscaleScaler`) 0,84 ms = **~1,0 ms**; po: rovina Y → `GRAY8`
0,077 ms (omezený rozsah 0,090 ms) + kopie řádků 0,001–0,004 ms = **~0,08 ms** (~12× méně). Paměť: s detekcí samotnou
dekodér nealokuje buffer plného BGRA (3,7 MB), šedotón má ~57 KB.

**Testy (nové / upravené):**
- Camera.FFmpeg: `GrayscaleConverterTests` (roztažení omezeného rozsahu se zmenšením i bez něj, plný rozsah beze změny,
  střední šedá, průměr ploch, znovupoužití bufferu a zarovnání), `FFmpegCameraFeedGrayscaleTests` (klíčové snímky jen
  v šedotónu bez BGRA; vedle okna jen splatné snímky; hub se simulovanou kamerou: detekce `Gray8` 320×180 se zdrojem
  1280×720, snímek poplachu plné BGRA), `GrayscaleSampleEquivalenceTests` (viz výše; klip se bez lokálních dat přeskočí),
  alokační `SteadyStateGrayscaleFramesDoNotAllocate` a `SteadyStateGrayscaleFramesThroughTheHubDoNotAllocate`.
- Core: `CameraGrayscaleFrameTests` (poptávka, kopie bez zarovnání, znovupoužití, odmítnutí cizí velikosti / BGRA,
  validace), `CameraStreamHubKeyFrameTests` (+3: šedotón z feedu bez plného převodu; feed bez převodů dál obslouží vzorky
  z plných snímků; druhá velikost šedotónu z plného snímku jen když je splatná), alokační
  `GrayscaleKeyFrameSamplesOfTheFeedDoNotAllocate` (ověří, že jde cestou šedotónu feedu).
- Upravené kvůli záměrné změně chování: `FFmpegCameraFeedDisplayTests.AKeyFrameDecodedAloneIsAlwaysFullResolution` →
  `…IsConvertedOnlyInTheSizesThatAreDue` (s plným rozlišením i bez něj) a `CameraStreamHubDisplayTests.DetectionBesideThe
  WindowGetsFullResolutionFrames…` → `…GetsGrayscaleFramesAndNothingIsConvertedInFull` (0 plných převodů). Testovací
  `FakeItemFeed` převádí jako dekodér (šedotón = skutečné luma jeho BGRA snímku, `IgnoresConversions` pro feed bez
  převodů), `LockstepFeed` umí šedotón. Testy detektorů (`LayerPeek.Detection.Tests`) se nezměnily.
- Timelapse a nahrávky berou pakety (beze změny), okno kamery BGRA pro zobrazení (testy T-184 beze změny), snímek
  poplachu plné BGRA (`TheAlarmSnapshotBesideTheWindowIsFullResolution`, `ANewSubscriberGetsTheNextKeyFrame…`, nový test
  se simulátorem).

**Bez nových balíčků**, Native AOT beze změny (jen další volání FFmpeg.AutoGen a `[LibraryImport]` `sws_scale`, které už
existovalo). Detection na FFmpeg nezávisí, Core nezná typy FFmpeg. Dokumentace: `docs/architecture.md` (převod ve velikosti
zobrazení, vzorky z hubu), CHANGELOG (Changed).

Build: 0 varování, 0 chyb. Testy: celkem 10119, úspěšných 9852, přeskočených 267, selhalo 0 (před úkolem 10095 / 9828 / 267; +24 nových testů).
