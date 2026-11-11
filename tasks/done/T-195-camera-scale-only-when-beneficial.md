---
id: T-195
title: Kamera – zmenšovat dekódovaný obraz jen tehdy, když to ušetří
status: done
owner: core-dev + ui-dev
priority: P3
platform: all
depends_on: [T-184]
---

## Cíl
Navazuje na T-184 (otázka 3, rozhodnutí uživatele 2026-11-11: **„škálovat jen když to dává smysl“**). Okno jen o málo menší
než obraz kamery (měřeno 960 × 540 ze 1280 × 720) stojí po T-184 o ~1 procentní bod CPU víc než bez zmenšení, protože
dvouprůchodové zmenšení swscale je dražší, než kolik ušetří menší kopie.

## Rozsah
- Zavést práh: při malém zmenšení zůstat u plného rozlišení zdroje (zmenší renderer), při velkém zmenšení (malý náhled,
  malé okno) zmenšovat v dekodéru jako dnes. Hodnotu prahu **určit měřením** (CPU i paměť, 720p a 1080p, simulátor;
  několik velikostí okna mezi malým náhledem a plnou velikostí), ne odhadem; tabulku zapsat do poznámek.
- Práh s hysterezí, aby plynulá změna velikosti okna kolem prahu nepřepínala dekodér sem a tam (navazuje na zaokrouhlení
  na 32 px a debounce 250 ms z T-184).
- Export snímku dál v plném rozlišení (beze změny).

## Akceptační kritéria
- [x] Naměřená tabulka CPU/paměti pro několik poměrů zmenšení (720p, 1080p) v `## Poznámky k implementaci` a z ní
      odvozený práh.
- [x] Nad prahem dekodér nezmenšuje, pod ním ano; hystereze proti přepínání (unit testy výpočtu cílové velikosti).
- [x] CPU okna jen o málo menšího než zdroj není vyšší než bez zmenšení; malý náhled si ponechá úsporu z T-184.
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
(zatím žádné)

## Poznámky k implementaci

### Řešení (core-dev, 2026-11-11)
- `Core/Cameras/CameraDisplayScaler` (jedna instance na stream, jen vlákno dekodéru, bez alokací): z velikosti pro
  zobrazení a velikosti zdroje vrátí cílovou velikost (`CameraFrameSize.Fit`), nebo `null` = plné rozlišení.
  Rozhoduje větší z poměrů stran výsledku ke zdroji: **zmenšovat od ≤ 70 % strany** (`ScaleFromPercent`), **plné
  rozlišení od ≥ 75 %** (`FullFromPercent`), mezi tím zůstává předchozí rozhodnutí (hystereze), první rozhodnutí vychází
  z plného rozlišení. Zdroj, který se vejde, je jako dřív plné rozlišení.
- `FFmpegDecoder.Convert` volá scaler místo dosavadního „zmenšit, kdykoli je cíl menší“. Bez zmenšení se snímek převede
  jednou v plném rozlišení a hub ho dá i oknu (stávající cesta „zdroj se vejde“, `CameraStreamItem.DisplayFrame`
  prázdný). Hub, okno, zaokrouhlení na 32 px a debounce 250 ms (T-184) i tempo malého náhledu (T-185) beze změny;
  export, detekce, nahrávky a timelapse mají dál plné rozlišení (netýká se jich to).
- Proč přímo v dekodéru: jen on zná rozlišení zdroje u každého snímku a rozhodnutí potřebuje stav (hystereze) pro
  daný stream; Core zůstává bez UI a FFmpeg (scaler je čistý C# v Core, dekodér ho jen používá).
- Bez nových balíčků; AOT: žádná reflexe, jen celočíselné porovnání.

### Měření (macOS 27.2 / Apple M2 Pro, 10 jader, oba displeje Retina 2×, Debug JIT, 2026-11-11)
Postup jako v T-184 / T-185: simulátor (`tools/PrusaSimulator`, scénář `idle`, `--fps 25`, porty 18490 / 18564) s klipem
z `~/LayerPeek-samples/clips/coreone-20261103-1905-60s.mp4` přepočteným přes `ffmpeg` (libx264 baseline, GOP 50,
30 s) na **1280 × 720** (1,1 Mb/s) a **1920 × 1080** (2,4 Mb/s); aplikace v izolovaném testovacím režimu
(`LAYERPEEK_TEST_DATA_DIR` v dočasné složce + přípona instance, skutečný profil netknutý), okno kamery otevřené příkazem
instance, normální okno s plochou obrazu dané velikosti (`startMode: Normal`, `normal.width/height`), resp. malý náhled
(320 × 180 DIP, výchozí 10 fps z T-185). Varianty ze stejného sestavení přes dočasnou sondu (necommitnutou): **dek.** =
dekodér vždy zmenšuje (chování T-184), **rend.** = dekodér nikdy nezmenšuje, okno dostane plné rozlišení a zmenší
renderer, **auto** = práh této větve. Po 15 s zahřátí 30 s měření procesu aplikace (dekódování + převod + kopie +
vykreslení): **instrukce/s** (`proc_pid_rusage`, `ri_instructions`), CPU (% jednoho jádra, `ps -o time`), medián RSS;
sonda kontrolovala skutečnou velikost bitmapy okna a fps (vždy 25,0, malý náhled 10,0).

Podmínky: displej probuzený (`caffeinate -d -u`), obrazovka zamčená (animace tapety zamčené obrazovky, ve všech bězích
stejně; vykreslovací smyčka okna běží, ověřeno sondou `RequestAnimationFrame`). Zátěž před každým během kontrolovaná
(`sysctl vm.loadavg`, čekání na load ≤ 6, sledování během měření): v první půlhodině souběžní agenti stavěli (load až 25),
ty běhy jsem zahodil a zopakoval; všechny započtené běhy měly load 1,2–4,9 na 10 jader. **CPU v % je na Apple Silicon
zavádějící:** stejná práce stojí na úsporných jádrech ~3× víc času než na výkonných a podíl jader se mezi běhy měnil
(týž běh 6 % nebo 19 % CPU podle podílu P-jader 100 % / 3 %), proto rozhoduji podle instrukcí, které se mezi opakováními
lišily o < 1 %. CPU v tabulce je z běhů s podobným podílem jader (převážně E-jádra). 2 kola, pořadí variant střídané;
u 1080p 33 / 25 / 60–68 % jen 1 kolo (opakování ostatních bodů se lišilo o < 0,5 %).

**720p (1280 × 720), plocha obrazu v px zařízení (okno v DIP = polovina):**

| Plocha (poměr strany) | dek.: instr. G/s / CPU / RSS | rend.: instr. G/s / CPU / RSS | auto |
|---|---|---|---|
| 1280 (100 %, bez zmenšení) | – | 0,391 / 20,5 % / 267 MB | plné |
| 1152 (90 %) | 0,657 / 23,5 % / 263 MB | **0,391** / 20,4 % / 267 MB | plné (0,392) |
| 960 (75 %) | 0,542 / 22,3 % / 262 MB | **0,392** / 20,6 % / 267 MB | plné (0,392) |
| 928 (72,5 %) | 0,532 / 22,6 % / 262 MB | **0,392** / 20,8 % / 267 MB | plné (0,391) |
| 896 (70 %) | **0,521** / 22,4 % / 261 MB | 0,634 / 29,8 % / 266 MB | zmenšit (0,521) |
| 864 (67,5 %) | **0,511** / 21,4 % / 261 MB | 0,635 / 29,9 % / 266 MB | zmenšit |
| 832 (65 %) | **0,527** / 23,3 % / 259 MB | 0,633 / 30,2 % / 266 MB | zmenšit |
| 768 (60 %) | **0,506** / 22,1 % / 258 MB | 0,631 / 29,6 % / 266 MB | zmenšit |
| 640 (50 %) | **0,447** / 19,4 % / 258 MB | 0,631 / 29,9 % / 266 MB | zmenšit (0,446) |
| 448 (35 %) | **0,495** / 19,3 % / 256 MB | 0,593 / 27,5 % / 267 MB | zmenšit |
| 320 (25 %) | **0,433** / 18,0 % / 255 MB | 0,594 / 28,0 % / 266 MB | zmenšit |
| malý náhled 640 (50 %, 10 fps) | **0,344** / 14,1 % / 256 MB | 0,403 / 17,6 % / 264 MB | zmenšit (0,346) |

**1080p (1920 × 1080):**

| Plocha (poměr strany) | dek.: instr. G/s / CPU / RSS | rend.: instr. G/s / CPU / RSS | auto |
|---|---|---|---|
| 1920 (100 %, bez zmenšení) | – | 1,011 / 40,1 % / 296 MB | plné |
| 1728 (90 %) | 1,468 / 39,9 % / 293 MB | **1,010** / 39,5 % / 296 MB | plné (1,008) |
| 1440 (75 %) | 1,269 / 40,1 % / 280 MB | **1,009** / 39,1 % / 296 MB | plné (1,008) |
| 1376 (71,7 %) | 1,224 / 39,8 % / 278 MB | **0,993** / 40,1 % / 295 MB | plné (0,994) |
| 1344 (70 %) | **1,093** / 36,1 % / 280 MB | 1,331 / 42,2 % / 291 MB | zmenšit (1,093) |
| 1312 (68,3 %) | **1,078** / 36,3 % / 280 MB | 1,329 / 43,3 % / 290 MB | zmenšit |
| 1280 (66,7 %) | **1,063** / 35,4 % / 279 MB | 1,328 / 42,7 % / 290 MB | zmenšit |
| 1248 (65 %) | **1,049** / 35,9 % / 278 MB | 1,330 / 42,1 % / 290 MB | zmenšit |
| 1152 (60 %) | **1,054** / 36,7 % / 273 MB | 1,335 / 42,4 % / 291 MB | zmenšit |
| 960 (50 %) | **0,927** / 32,7 % / 272 MB | 1,329 / 42,9 % / 290 MB | zmenšit (0,927) |
| 640 (33 %) | **1,013** / 31,8 % / 267 MB | 1,339 / 42,6 % / 290 MB | zmenšit |
| 480 (25 %) | **0,897** / 29,7 % / 266 MB | 1,331 / 42,7 % / 291 MB | zmenšit |
| malý náhled 640 (33 %, 10 fps) | **0,764** / 24,6 % / 265 MB | 0,890 / 32,0 % / 286 MB | zmenšit (0,764) |

**Závěr a práh:** zmenšení v rendereru (Avalonia / Skia) stojí v obou rozlišeních do poměru ~71 % strany prakticky nic
(stejně jako bez zmenšení) a pod ním skokově o ~60 % (720p) / ~33 % (1080p) instrukcí – mezi 72,5 % a 70 % u 720p a mezi
71,7 % a 70 % u 1080p, tedy na 1/√2 ≈ 70,7 %, kde Skia nejspíš přechází na dražší filtr (mipmapy; příčina z měření,
neověřená ve zdrojích Skia). Zmenšení v dekodéru stojí nad tou hranicí víc (+23 až +68 % instrukcí, CPU stejné nebo o 1–3
body vyšší), pod ní méně. Práh proto leží na 70,7 %: **zmenšovat od ≤ 70 %, plné rozlišení od ≥ 75 %**. Horní mez je
o 2,5 % strany 720p (32 px) výš, protože okno žádá plochu zaokrouhlenou nahoru na 32 px (T-184) a skutečně vykreslená
plocha tak může být o tolik menší než požadavek; od 75 % požadavku je vykreslená plocha vždy nad 71 %. Pásmo 70–75 %
(dva kroky po 32 px u 720p) je hystereze proti přepínání při plynulé změně velikosti. Ověřeno během „auto“: ve všech
bodech vybral levnější variantu (sloupec auto). Okno jen o málo menší než zdroj (75–90 %) teď stojí stejně jako bez
zmenšení (0,39 / 1,01 G instr./s místo 0,54–0,66 / 1,27–1,47), malý náhled si úsporu z T-184 ponechal (−15 % / −14 %
instrukcí, −8 / −21 MB proti plnému rozlišení).

**Paměť:** nad prahem stojí plné rozlišení o 4–5 MB (720p) / 3–16 MB (1080p) RSS víc než zmenšený snímek (větší buffer
převodu a bitmapa okna). Podle rozhodnutí „škálovat jen když to dává smysl“ (otázka 3 T-184 byla volbou mezi CPU
a pamětí) má přednost CPU. Pásmo hystereze: když okno zůstane v pásmu 70–75 % v plném rozlišení a jeho skutečná plocha
je pod 71 %, platí dražší varianta rendereru (+0,11 / +0,24 G instr./s), dokud se okno nezmenší pod 70 % – cena za to,
že dekodér při tažení nepřepíná.

Na Windows (x64 SIMD swscale, Skia přes Direct3D/ANGLE) neměřeno → T-125 (oddíl 2x).

**Testy:** `CameraDisplayScalerTests` (zřetelně menší snímek se zmenšuje včetně malého náhledu a okna širšího než obraz,
přesně na prahu se zmenšuje, o málo menší a zdroj, který se vejde, zůstává v plném rozlišení, hystereze při růstu přes
pásmo, na horním prahu a zpět, chybný zdroj nemění rozhodnutí, prahy nechávají pásmo), `FFmpegCameraFeedDisplayTests.
ADisplaySizeOnlyALittleSmallerThanTheCameraIsConvertedInFullResolution` (skutečný dekodér: 1216 × 720 z 1280 × 720 =
jen plný převod, bez zmenšeného snímku). Stávající testy T-184 (zmenšení na ≤ 50 %) beze změny.

`dotnet build` 0 varování, `dotnet test` 10 347 testů: 10 080 prošlo, 267 přeskočeno, 0 chyb (macOS 27.2 / Apple M2 Pro,
SDK 11.0.100, po sloučení `develop` `e79898d4`).
