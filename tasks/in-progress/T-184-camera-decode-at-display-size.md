---
id: T-184
title: Kamera – převod obrazu rovnou ve velikosti, ve které se zobrazuje
status: in-progress
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
- [ ] V malém náhledu a v malém okně se převádí jen na zobrazenou velikost (test dekodéru: výstupní `CameraFrame` má
      požadovanou velikost, poměr stran sedí, menší zdroj se nezvětšuje).
- [ ] Změna velikosti okna nebo režimu přenastaví převod bez přerušení streamu.
- [x] Detekce chyb tisku a timelapse dostávají snímky ve stejném rozlišení jako dosud (testy hubu).
- [ ] Kvalita obrazu na celé obrazovce se viditelně nezhorší (ruční kontrola → T-125 / T-127).
- [ ] Naměřené hodnoty před a po jsou v `## Poznámky k implementaci`.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
Rozhodnuto uživatelem 2026-11-10:
1. Filtr: **ponechat `SWS_BILINEAR`**.
2. Zmenšení **jen pro zobrazení**; snímky, export a nahrávky mají vždy plné rozlišení.


## Poznámky k implementaci

### Část Core + Camera.FFmpeg (core-dev, 2026-11-10) – hotovo, část UI (ui-dev) zbývá

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
