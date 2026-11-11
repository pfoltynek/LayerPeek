---
id: T-184
title: Kamera – převod obrazu rovnou ve velikosti, ve které se zobrazuje
status: done
owner: core-dev + ui-dev
priority: P2
platform: all
depends_on: []
---

## Cíl
Výkonnostní analýza (2026-11-06): převod YUV → BGRA běží vždy v plném rozlišení zdroje
(`FFmpegDecoder.Configure`, `sws_getCachedContext` má stejnou cílovou velikost jako zdroj,
[FFmpegDecoder.cs](../../src/LayerPeek.Camera.FFmpeg/FFmpegDecoder.cs)). Každý snímek se pak ještě dvakrát
kopíruje, z toho jednou na UI vlákně (`BgraPixels.CopyTo` do `WriteableBitmap`). U 720p a 22 fps to dělá zhruba
250 MB/s paměťového provozu na jedno okno a malý náhled stojí stejně jako celá obrazovka. Cílem je nechat swscale
zmenšit obraz na velikost, ve které se opravdu vykresluje, a odstranit zbytečnou kopii.

## Rozsah
- Čtenář streamu (okno kamery) sdělí přes `CameraStreamDemand` (nebo podobnou cestou v `CameraStreamHub`) požadovanou
  maximální velikost snímku v pixelech zařízení (velikost obrazu × škálování monitoru).
- `FFmpegDecoder` předá cílovou velikost do `sws_getCachedContext` (zachová poměr stran, nikdy nezvětšuje)
  a při změně velikosti okna nebo režimu (malý náhled ↔ normální okno ↔ celá obrazovka) kontext přenastaví.
- Hub může mít víc čtenářů (okno, detekce T-153, timelapse T-158 / T-165). Snímky pro detekci a timelapse musí zůstat
  v rozlišení, které potřebují. Zmenšuje se jen proud snímků pro okno.
- Odstranit jednu ze dvou kopií snímku (ideálně zapisovat převod přímo do bufferu, který jde do bitmapy, bez kopie
  na UI vlákně), pokud to jde bez porušení pravidel vláken v Avalonii.
- Změřit stav před a po (CPU, alokace a paměťový provoz, fps) na simulátoru v 720p a zapsat do poznámek.

## Akceptační kritéria
- [x] V malém náhledu a v malém okně se převádí jen na zobrazenou velikost (test dekodéru: výstupní `CameraFrame` má
      požadovanou velikost, poměr stran sedí, menší zdroj se nezvětšuje).
- [x] Změna velikosti okna nebo režimu přenastaví převod bez přerušení streamu.
- [x] Detekce chyb tisku a timelapse dostávají snímky ve stejném rozlišení jako dosud (testy hubu).
- [x] Kvalita obrazu na celé obrazovce se viditelně nezhorší (ruční kontrola) → přesunuto do T-125 a T-127
- [x] Naměřené hodnoty před a po jsou v `## Poznámky k implementaci`.
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
Rozhodnuto uživatelem 2026-11-10:
1. Filtr: **ponechat `SWS_BILINEAR`**.
2. Zmenšení **jen pro zobrazení**; snímky, export a nahrávky mají vždy plné rozlišení.

Otázka pro uživatele (navazující, T-184 neblokuje; ui-dev 2026-11-11):
3. Okno jen o málo menší než obraz kamery (měřeno 960 × 540 px ze 1280 × 720) stojí po T-184 o ~1 procentní bod CPU
   víc než dřív (dvouprůchodové zmenšení swscale je dražší, než kolik ušetří menší kopie), paměť a paměťový provoz jsou
   i tak nižší. Má se zavést práh (např. při zmenšení na víc než ~75 % strany zůstat u plného rozlišení a nechat zmenšit
   renderer), nebo ponechat zmenšení vždy? Je to volba mezi CPU a pamětí; bez rozhodnutí zůstává zmenšení vždy.


## Poznámky k implementaci

### Část Core + Camera.FFmpeg (core-dev, 2026-11-10)

Nezaškrtnuté zůstávají kritéria, která potřebují okno: dekodér a hub to umí a mají testy, ale okno zatím velikost
nehlásí (1, 2), kvalita na celé obrazovce (4, ruční kontrola), měření celé cesty včetně UI (5) a build/test po části UI (6).

**API (Core):**
- `CameraFrameSize` (šířka × výška v pixelech zařízení, 1–16384) s `Fit(sourceWidth, sourceHeight)`: omezující strana
  dostane přesně požadovanou hodnotu, druhá zachová poměr stran (zaokrouhleně, aspoň 1), zdroj, který se vejde, se
  nezvětšuje.
- `CameraFrameSubscriptionOptions.DisplaySize` (počáteční velikost) a `CameraFrameSubscription.SetDisplaySize(size?)`
  (z libovolného vlákna; `null` = plné rozlišení; stejná velikost nic nedělá). Platí od dalšího snímku, spojení se
  nepřeruší.
- `CameraFrame.SourceWidth/SourceHeight` a `CameraFrameBuffer.SourceWidth/SourceHeight`: velikost kamery, ze které se
  zmenšovalo (přesný poměr stran).
- `CameraStreamDemand.DisplaySize`, `FullResolutionDue`, `RequestFullResolution(timestamp)`, `CancelFullResolution()`;
  výchozí stav = každý snímek v plném rozlišení, takže dosavadní čtenáři (`ReadFramesAsync`, `CameraSnapshot`) se nemění.
- `CameraStreamItem.DisplayFrame` vedle `Frame` (`FromFrames`); prázdný = nepožadován, nebo se zdroj do velikosti vejde.
- `CameraFrameSubscriptionOptions.FrameReceived` (`CameraFrameHandler(in CameraFrame)`): vypůjčený snímek přímo na
  vlákně streamu místo kopie do trojitého bufferu odběru (`TakeLatest` pak vrací `null`, `FrameAvailable` se nevolá;
  nejde kombinovat s `Grayscale`; výjimka ukončí jen tento odběr s `Failed`). Připraveno pro odstranění kopie na UI vlákně.

**Hub:** `ScheduleFrames` (místo `ScheduleKeyFrame`) spočítá z odběratelů: klíčový snímek pro odběratele jen klíčových
snímků (beze změny), `FullResolutionDue` = nejbližší čas, kdy si snímek v plném rozlišení vezme odběratel bez
`DisplaySize` (bez intervalu vždy, detekce s `MinFrameInterval` jen jednou za interval; po převzetí se přeplánuje, jako
dosud klíčový snímek) a `DisplaySize` = nejmenší obdélník, do kterého se vejdou požadavky všech odběratelů pro zobrazení.
`Dispatch` dá odběratelům pro zobrazení `DisplayFrame` (bez něj `Frame`), ostatním jen `Frame` – plné rozlišení nikdy
nedostane zmenšený snímek. Během doběhu spojení bez odběratelů (linger) se dekóduje bez převodu.

**Dekodér:** `FFmpegDecoder.Convert` převádí každý snímek jen v potřebných velikostech, každou vlastním `BgraConverter`
(vlastní `SwsContext` přes `sws_getCachedContext`, `SWS_BILINEAR`, vlastní připnutý zarovnaný buffer). Zmenšený snímek
jde ve **dvou průchodech**: zmenšení v YUV 4:2:0, pak stejný nezmenšující převod YUV → BGRA jako u plného rozlišení.
Druhý průchod převádí šířku zaokrouhlenou na 16 px a sudou výšku (jen tak swscale použije NEON; přebytečné sloupce padnou
do zarovnání řádku, řádek navíc do rezervy bufferu, do snímku nepatří – test kontroluje krajní sloupec a řádek). Buffer
při růstu dostane rezervu ×1,5 (postupné zvětšování okna = pár alokací), přepínání mezi už použitými velikostmi je bez
alokací (test). Samostatně dekódovaný klíčový snímek (detekce) je vždy v plném rozlišení.

**Proč takhle (měřeno, viz níže):** jeden průchod swscale se zmenšením rovnou do BGRA byl na Apple Silicon mnohem
dražší než celý plný převod (pro výstup do packed RGB ve škálující cestě nemá swscale SIMD kód). Zmenšení z hotového
plného BGRA by čtlo 4 B/px místo 1,5 B/px YUV a potřebovalo plný převod vždy; varianta z YUV plný převod úplně vynechá,
když ho nikdo nechce, a barvy počítá stejně. `SWS_FAST_BILINEAR` (jen pro srovnání, nepoužito) nebyl rychlejší.

**Snímek z okna v plném rozlišení (rozhodnutí 2):** krátký vlastní lease bez `DisplaySize` vedle lease okna (jako
`CameraSnapshotTester.CaptureSharedAsync` nebo snímek poplachu detekce): hub po dobu jeho trvání navíc převádí plné
rozlišení, okno dál dostává malé snímky, spojení se nepřeruší (test `AShortFullResolutionLeaseCostsFullConversionsOnlyWhileItLasts`).

**Pro ui-dev (druhá část úkolu):**
1. `CameraViewModel.StartStream`: `SubscribeFrames(..., new() { DisplaySize = …, … })` s velikostí prvku `Video`
   (Image, `Stretch="Uniform"`) × `TopLevel.RenderScaling`, zaokrouhleno nahoru, omezeno na 1–16384; před prvním layoutem
   klidně `null` (plné rozlišení) a velikost hned po něm.
2. Při změně `Video.Bounds` (změna velikosti okna, režim malý náhled ↔ okno ↔ celá obrazovka) a `ScalingChanged`
   (přesun na monitor s jiným škálováním) volat `lease.SetDisplaySize(...)`. Core přenastaví převod jen při změně
   výsledné velikosti; každá nová velikost ale znamená novou `WriteableBitmap` v `CameraWindow.Present`, takže během tažení
   okna doporučuji požadavek zaokrouhlit nahoru na krok (např. 32 px) nebo poslat až po uklidnění.
3. `OnFrameShown` / `VideoAspectRatio` brát z `frame.SourceWidth/SourceHeight`, ne z `Width/Height`: zaokrouhlení
   zmenšeného snímku by jinak měnilo poměr stran a malý náhled, jehož výška poměr sleduje (T-089), by si mohl sám
   měnit velikost (zpětná vazba okno → velikost → poměr → okno).
4. Odstranění kopie: buď (a) bez změny Core – kopie z `TakeLatest` zůstane na UI vlákně, ale je 4–16× menší, nebo
   (b) `FrameReceived`: na vlákně streamu zamknout zadní `WriteableBitmap` (trojitý buffer bitmap v okně), zkopírovat
   řádky s ohledem na `frame.Stride` (zarovnaný na 64 B, `BgraPixels` dnes počítá se zhuštěnými řádky), odemknout,
   prohodit pod zámkem a na UI vlákno poslat jen `Video.Source = …` + `InvalidateVisual` (nejvýš jeden čekající post).
   Varianta (b) potřebuje ověřit, že Avalonia 12 dovolí `WriteableBitmap.Lock()` mimo UI vlákno a že renderer nečte
   bitmapu, do které se právě píše; jinak zůstat u (a).
5. Změřit celou cestu s oknem (CPU procesu včetně UI a renderu, fps) na simulátoru v 720p a doplnit sem; zkontrolovat
   kvalitu na celé obrazovce (→ T-125 / T-127). Na Windows (x64, jiné SIMD cesty swscale) změřit převod znovu.

**Měření (Apple Silicon, macOS, .NET 11, simulátor kamery Buddy3D 1280×720; zatížený stroj – souběžně běžely jiné
agenty, proto best-of a jen konzistentní kola):**

Dekodér samotný (soubor 720p, co nejrychleji, CPU na snímek, nejlepší ze 7 průchodů):

| Varianta | CPU ms/snímek | z toho převod |
|---|---|---|
| jen dekódování, bez převodu (linger) | 0,72 | 0 |
| před: plný převod 1280×720 | 0,84–0,90 | ~0,18 |
| po: plný převod 1280×720 | 0,89–0,91 | ~0,18 |
| po: 960×540, jeden průchod (zamítnuto) | 2,73 | ~2,0 |
| po: 960×540, dva průchody | 1,25 | ~0,53 |
| po: 640×360, jeden průchod (zamítnuto) | 1,56 | ~0,84 |
| po: 640×360, dva průchody | 1,04 | ~0,32 |
| po: 533×300, dva průchody bez / se zarovnáním na 16 px | 1,24 / 1,12 | ~0,52 / ~0,40 |
| po: 320×180, dva průchody | 0,93 | ~0,21 |

Hub + odběratel, který každý snímek zkopíruje do „bitmapy“ jako `CameraWindow.Present` (RTSP simulátor v reálném
čase 22 fps, 15 s; CPU procesu včetně simulátoru, samotný příjem paketů = 1,2 %):

| Varianta | fps | CPU (% jednoho jádra) | Working set | Alokace snímků |
|---|---|---|---|---|
| před: plné rozlišení | 22,0 | 3,3 % | 95 MB | 0 B |
| po: okno 640×360 | 22,0 | 3,2 % | 82–83 MB | 0 B |
| po: malý náhled 320×180 | 22,0 | 2,9 % | 79 MB | 0 B |
| po: 640×360 + `FrameReceived` (jedna kopie) | 22,0 | 3,0 % | 79 MB | 0 B |

(Alokace ~5,8 MB za 15 s jsou simulátor a měřicí program, stejné jako u samotných paketů; cesta snímků je bez alokací,
viz testy `FFmpegCameraFeedAllocationTests`.) Bez tempa (simulátor co nejrychleji, CPU na dekódovaný snímek): před 0,99 ms,
po 0,99 (plné) / 1,01 (640×360) / 0,88 (320×180) ms – v rámci šumu.

Paměťový provoz za převodem (zápis + čtení kopií, 22 fps): před 4 × 3,69 MB = 14,7 MB/snímek ≈ **325 MB/s** (+ zápis
převodu 81 MB/s); po 640×360 4 × 0,92 MB ≈ **81 MB/s** (s `FrameReceived` ≈ 40 MB/s), po 320×180 ≈ **20 MB/s**; mezisnímek
YUV 640×360 je 0,35 MB. Do toho nepočítám nahrání textury a zmenšování ve rendereru Avalonie, které také klesnou s velikostí
bitmapy (měří ui-dev).

Závěr: na straně dekodéru a hubu je CPU na Apple Silicon v 720p prakticky stejné (dražší zmenšení vyrovná levnější
kopie), přínos je v paměťovém provozu (−75 % u 640×360, −94 % u 320×180), paměti (−12 až −16 MB) a hlavně v práci na
UI vlákně a v rendereru, kterou změří až část UI. Detekce vedle otevřeného okna stojí jeden plný převod za vzorek místo
každého snímku. Na Retina displeji je okno 640×360 DIP = 1280×720 px zařízení, tam se nezmenšuje (malý náhled ano).

**Testy:** `CameraFrameSizeTests` (Fit, poměr stran, nezvětšování, validace, poptávka, velikost zdroje, růst bufferu),
`CameraStreamHubDisplayTests` (okno jen zmenšené bez plného převodu; plné rozlišení vedle okna; krátký lease plného
rozlišení; detekce a snímek poplachu v plném rozlišení a plný převod jen pro vzorek; nahrávka a timelapse dostanou
pakety beze změny; změna velikosti bez nového spojení včetně celé obrazovky a `null`; více oken; linger bez převodu;
`FrameReceived`), `FFmpegCameraFeedDisplayTests` (velikost a poměr stran, stejné barvy v obou velikostech i pro liché
rozměry, nezvětšování, změna velikosti během streamu bez restartu – všech 71 snímků v jednom spojení, dekódování bez
převodu, klíčový snímek vždy v plném rozlišení, celá cesta přes hub), `FFmpegCameraFeedAllocationTests` (zmenšené snímky,
přepínání velikostí a hub se zmenšením bez alokací). Falešný feed v testech Core zmenšuje stejně jako dekodér.

Bez nových balíčků; AOT: žádná reflexe, jen existující `NativeMethods.Scale` (`LibraryImport`) a `sws_*` z FFmpeg.AutoGen.

### Část UI (ui-dev, 2026-11-11)

**Velikost pro okno:** `CameraWindow` hlásí plochu obrazu (`Surface.Bounds`, obraz ji vyplní se zachovaným poměrem stran)
× `RenderScaling` při změně `Bounds` (změna velikosti, malý náhled ↔ okno ↔ celá obrazovka, režim rozložení), při
`ScalingChanged` (jiný monitor) a před startem streamu v `OnOpened`. `CameraViewModel.SetVideoArea` z ní udělá
`CameraFrameSize` v pixelech zařízení zaokrouhlený nahoru na **32 px** (`VideoSizeOf`, 32–16384, prázdná nebo neplatná
plocha = beze změny). První velikost běhu jde hned (`DisplaySize` v `StartStream`, nebo `SetDisplaySize`, když běh začal
před prvním layoutem v plném rozlišení), každá další až po **250 ms** bez změny (`VideoSizeSettleTime`). Proč obojí:
každá nová velikost znamená nový `SwsContext`, nový buffer dekodéru a novou bitmapu okna; čekání na uklidnění udělá
z tažení okraje nebo animace celé obrazovky jednu změnu, krok 32 px pohltí drobné změny (malý náhled si dorovnává výšku
o pixel). Časovač je jeden `ITimer` z `TimeProvider`, vznikne až při první změně a běží jen do vypršení – nečinné okno
nemá žádný běžící časovač. Mezitím renderer škáluje předchozí snímky, stream se nepřeruší (test: jedno spojení přes
všechny režimy). Velikost změněná při minimalizovaném okně použije další běh.

**Poměr stran:** `OnFrameShown` dostává `SourceWidth/SourceHeight` posledního zapsaného snímku, ne jeho rozměry, takže
zaokrouhlení zmenšeného snímku (např. 341 × 192) nemění `VideoAspectRatio` a malý náhled si sám nemění výšku.

**Odstranění kopie – varianta (b):** odběr okna používá `FrameReceived`; `CameraWindow.FrameSink` kopíruje vypůjčený
snímek na vlákně streamu přímo do zobrazené `WriteableBitmap` (řádky dekodéru zarovnané na 64 B, nový
`BgraPixels.CopyRows/CopyTo` se zdrojovým krokem; blokově, když kroky sedí). UI vlákno jen při nové velikosti nasadí
novou bitmapu (vytvořenou na vlákně streamu, starou zlikviduje až po výměně) a zavolá `InvalidateVisual`; nejvýš jedno
čekající volání (`_presentQueued`). Na snímek tedy jedna kopie místo dvou a žádná na UI vlákně, bez alokací.
Ověřeno ve zdrojích Avalonie 12.1.4: `WriteableBitmap.Lock()` pro `Bgra8888` vrací přímo `Lock()` Skia
`WriteableBitmapImpl`, který bere stejný monitor jako `Draw` (vlákno rendereru) i `Dispose`; konstruktor bitmapy nemá
vazbu na vlákno (`AvaloniaLocator` + Skia). Renderer kompozitoru běží na vlastním vlákně, takže dřívější zápis z UI vlákna
měl s rendererem přesně stejný souběh – zápis z vlákna streamu není méně bezpečný. Bitmapu, kterou renderer ještě kreslí,
drží vlastní referencí (`IRef`), proto `Dispose` po výměně `Source` stačí (stejně jako dřív).
**Vlákna a životnost:** zámek snímků ve view modelu (`_frameGate`): snímek se zapíše jen pro aktuální běh a připojený
sink; zastavení běhu (`NextRun`) i `AttachFrameSink(null)` počkají na rozepsaný snímek, potom už žádný snímek starého běhu
nepřijde (test s blokovaným zápisem). Zámek bitmap v okně chrání výměnu a `Clear` (zavření okna, jiná adresa) proti zápisu.
Při změně adresy se běh zastaví před vymazáním obrazu (dřív naopak), aby se pozdní snímek staré kamery nezapsal po vymazání.
Selhání zápisu nebo `Post` (UI končí) ukončí lease s `Failed` → stávající obnova po prodlevě.
Pořadí volání UI se změnilo: snímek se teď posílá na UI před stavem „streaming“ (callback běží uvnitř `Publish`); dva
testy `FaultyDispatcher` proto selhávají na 2. místo 3. volání.

**Snímky v plném rozlišení (rozhodnutí 2):** okno kamery ani malý náhled nemají žádnou akci, která by ukládala nebo
kopírovala snímek (kontextové menu: rozložení, dashcam, navrchu). Dashcam, nahrávky a timelapse berou pakety vlastními
odběry, detekce a její snímek poplachu vlastní odběry bez `DisplaySize`, test kamery v Nastavení a poslední snímek pro
editor oblasti `CameraSnapshotTester.CaptureSharedAsync` (vlastní lease bez `DisplaySize`). Test
`AFullResolutionCaptureNextToTheWindowGetsTheCamerasResolution`: zachycení vedle okna 320 × 180 má 1280 × 720, okno
dál dostává 320 × 180, jedno spojení.

**Testy:** `CameraViewModelDisplaySizeTests` (zaokrouhlení a škálování, neplatná plocha, velikost před startem,
první layout hned bez nového spojení, odložení a restart čekání při tažení, změna v kroku nic nepošle, jiné škálování,
celá obrazovka větší než kamera = plné rozlišení, prázdná plocha, velikost z doby minimalizace, poměr stran ze zdroje,
každý snímek zapsán na vlákně streamu a zaneprázdněné UI ukáže nejnovější, zastavení počká na rozepsaný snímek, odpojený
sink, zachycení v plném rozlišení), `BgraPixelsTests` (zarovnaný zdroj, zarovnaný zdroj do zarovnaného cíle, neplatné
zdroje), headless `CameraWindowDisplaySizeUiTests` (normální okno → malý náhled → celá obrazovka: požadovaná velikost
a velikost bitmapy odpovídá ploše, poměr stran kamery, jedno spojení; zavření okna při přicházejících snímcích uvolní
bitmapu). `FakeCameraFeed` v testech App zmenšuje podle poptávky jako dekodér (řádky zarovnané na 64 B).
`dotnet build` 0 varování, `dotnet test` 10 046 testů: 9 779 prošlo, 267 přeskočeno, 0 chyb (macOS 26 / Apple M2 Pro,
SDK 11.0.100). Bez nových balíčků; AOT: žádná reflexe, kompilované bindingy beze změny.

**Měření celé cesty s oknem (macOS, Apple M2 Pro, oba displeje Retina 2×, Debug JIT – testovací režim existuje jen
v Debug; simulátor kamery 1280 × 720 při 25 fps, tiskárna `idle`; izolovaný testovací režim `LAYERPEEK_TEST_DATA_DIR` +
přípona instance, okno otevřené příkazem instance; 15 s zahřátí, pak 30 s: CPU procesu aplikace (% jednoho jádra, `ps`),
medián RSS, fps = zobrazené snímky na UI vlákně (dočasná sonda, necommitnutá). „Před“ = `origin/develop` `bd22010c`,
„po“ = tato větev; střídavě, 2 kola, průměr; stroj byl zatížený souběžnými agenty):**

| Okno (DIP → px zařízení) | Snímek po | CPU před → po | RSS před → po | fps před / po |
|---|---|---|---|---|
| malý náhled 320 × 180 → 640 × 360 | 640 × 360 | 28,5 % → 18,6 % (−35 %) | 271 → 253 MB | 25,0 / 25,0 |
| okno 480 × 270 → 960 × 540 | 960 × 540 | 21,0 % → 21,9 % (+0,9 b.) | 274 → 259 MB | 25,0 / 25,0 |
| okno 960 × 540 (výchozí) → 1920 × 1080 | plné 1280 × 720 | 20,8 % → 20,1 % | 275 → 264 MB | 25,0 / 25,0 |
| celá obrazovka 1287 × 900 → 2574 × 1800 | plné 1280 × 720 | 26,0 % → 24,6 % | 276 → 265 MB | 25,0 / 25,0 |

Předchozí přerušené kolo (víc zatížený stroj) dalo stejný obraz: malý náhled 31,6 → 23,4 %, okno 960 × 540 px 25,5 → 27,4 %.
Paměťový provoz kopií za převodem při 25 fps: před 4 × 3,69 MB/snímek ≈ 369 MB/s (kopie do bufferu odběru + kopie na UI
vlákně, každá čtení i zápis), po v plném rozlišení 2 × 3,69 ≈ 184 MB/s, při 960 × 540 ≈ 104 MB/s, v malém náhledu
≈ 46 MB/s. Na Retina je výchozí okno i celá obrazovka větší než obraz kamery, takže tam se nezmenšuje; úspora je jedna
kopie méně (bez práce na UI vlákně) a −11 MB. Malý náhled ušetří třetinu CPU celé aplikace (méně převodu, kopií,
nahrávání textury i zmenšování v rendereru). Okno jen o málo menší než kamera stojí o ~1 bod CPU víc (dvouprůchodové
zmenšení, viz měření dekodéru výše), paměti méně – viz otázka 3. Na Windows (x64) a v Release AOT neměřeno → T-125 (2s).

