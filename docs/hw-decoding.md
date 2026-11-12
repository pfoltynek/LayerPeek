# Hardwarové dekódování H.264 kamery – průzkum (spike T-192)

> **Rozhodnutí uživatele 2026-11-12 (T-192, otázky 2–5):** implementovat, nejdřív macOS; volba v Nastavení > Kamera
> „Hardwarové dekódování“ Automaticky / Vypnuto (výchozí Automaticky); Windows (D3D11VA) souběžně, ale výchozí
> software do měření v T-125; rozšířená útočná plocha a paměť navíc přijaty. Podrobnosti v oddílu „Rozhodnutí
> a implementační úkoly“. Text níže je průzkum z 2026-11-11.

Stav 2026-11-11 (architekt, Mac mini M2 Pro, 10 jader, macOS 27.2, .NET SDK 11.0.100, Debug JIT). Rozhodnutí uživatele
2026-11-11: **„ano, zkoumat – nejdřív měřit“**. Prototypy běžely jen mimo repozitář: benchmark v C a dočasná sonda
v `FFmpegDecoder` v odděleném worktree, který byl po měření smazán. **Produktový kód ani build se nezměnily.** Windows
je jen rozbor, měření tam patří do T-125.

## Shrnutí

- **Dekódování H.264 je po T-184, T-185 a T-195 pořád největší část zátěže kamery:** 53–58 % instrukcí procesu
  u normálního okna 720p, 50–52 % u 1080p a 67–69 % u malého náhledu. Malý náhled dekóduje všech 25 snímků/s, i když
  zobrazí jen 10.
- **VideoToolbox (macOS) ušetří 45–63 % instrukcí celé aplikace.** Počítá se i pomocný proces `VTDecoderXPCService`,
  ve kterém macOS dekódování řídí. Normální okno 720p: 0,41–0,45 → 0,21–0,22 G instr./s, 1080p: 1,04–1,08 → 0,55–0,56,
  malý náhled 720p: 0,35 → 0,18, 1080p: 0,78 → 0,29.
- **Doba CPU klesne méně, než by odpovídalo instrukcím:** u normálního okna 720p skoro vůbec (21,7 % → 16,5 % aplikace
  + 4,5 % pomocného procesu), u 1080p o ~15 %, u malého náhledu o 5 % (720p) a 35 % (1080p). Pomocný proces stojí
  ~4,5 % jádra při velmi malém počtu instrukcí, protože čeká na hardware. **Odhad energie CPU** (`ri_energy_nj`) klesne
  o 20–40 % (720p) a 40–50 % (1080p). Energii samotného hardwarového dekodéru (blok AVD) bez `sudo` změřit nejde.
- **Kopie snímku z paměti GPU úsporu nesní:** na Apple Silicon je paměť sdílená. `av_hwframe_transfer_data` (NV12) stojí
  0,7 M instrukcí na snímek 720p (2,2 M u 1080p) a `av_hwframe_map` bez kopie skoro nic. Převod NV12 → BGRA stojí stejně
  jako převod YUV 4:2:0 → BGRA dnes.
- **Build:** macOS potřebuje dva přepínače configure (`--enable-videotoolbox --enable-hwaccel=h264_videotoolbox`).
  Licence zůstává LGPL 3, knihovny jsou větší o 27 KB a build trvá o ~4 s déle. **Windows build BtbN už D3D11VA, DXVA2,
  D3D12VA, NVDEC, QSV i Vulkan obsahuje**, takže tam se build nemění. AOT: jen funkce FFmpeg, které vazby už mají, a jeden
  callback `[UnmanagedCallersOnly]`. Žádný nový balíček.
- **Doporučení: vyplatí se, nejdřív na macOS** (VideoToolbox s `av_hwframe_map`), s automatickým návratem
  k softwarovému dekódování. Windows (D3D11VA) až po měření na Windows. Uživatel 2026-11-12 implementaci schválil,
  úkoly T-203 až T-206 (oddíl „Rozhodnutí a implementační úkoly“).

## Postup měření

Metrika jsou **instrukce procesu za sekundu** (`proc_pid_rusage`, `RUSAGE_INFO_V6`, `ri_instructions`) jako v T-195.
CPU v % je na Apple Silicon zavádějící: podle podílu výkonných (P) a úsporných (E) jader se stejná práce liší až 3×.
V měření níže měl týž benchmark 2–13 % CPU podle podílu P-jader, instrukce se mezi opakováními lišily o < 1,5 %.
CPU % a odhad energie (`ri_energy_nj`, energie CPU přiřazená procesu) uvádím jen doplňkově. `powermetrics` potřebuje
`sudo` (nepoužito). IOReport bez práv roota vrací v této verzi macOS u skupiny „Energy Model“ nuly a kanál dekodéru
(AVD) v ní stejně není, jen enkodér (AVE).

**Pomocný proces:** VideoToolbox dekóduje H.264 v procesu `VTDecoderXPCService`, který macOS spustí pro každého klienta.
Jeho instrukce a CPU se **počítají do výsledku**, jinak by úspora vypadala větší, než je. Proces se přiřazuje přes
`responsibility_get_pid_responsible_for_pid` a jen pokud vznikl během běhu. Stejný proces má i animovaná tapeta zamčené
obrazovky (`WallpaperAerialsExtension`) a bez tohoto filtru by se do měření přimíchal.

**Knihovny:** dva buildy FFmpeg 9.0.2 z oficiálního tarballu (SHA256 ověřený, stejný jako v `build/FFmpeg.targets`):
`build/macos/build-ffmpeg.sh` beze změny (SW) a jeho kopie s `--enable-videotoolbox --enable-hwaccel=h264_videotoolbox`
(VT). Softwarové dekódování je s oběma buildy stejné (9,406 / 9,401 M instr./snímek).

**Klipy:** stejné jako v T-195. Úsek z `~/LayerPeek-samples/clips/coreone-20261103-1905-60s.mp4` přepočtený na
1280 × 720 (1,1 Mb/s) a 1920 × 1080 (2,4 Mb/s), libx264 Constrained Baseline, GOP 50, 25 fps, `yuvj420p` (plný rozsah,
BT.709 jako Buddy3D).

**A. Benchmark dekodéru (C, odkaz na knihovny výše):** čte surový H.264 ze souboru, dekóduje stejně jako `FFmpegDecoder`
(`thread_count = 1`, `AV_CODEC_FLAG_LOW_DELAY`) v tempu 25 snímků/s (751 snímků, 30 s) a převádí jako `BgraConverter` /
`GrayscaleConverter` (`SWS_BILINEAR`, dvouprůchodové zmenšení, barevný prostor a rozsah streamu). Varianty dekódování:
`sw` (software), `vtraw` (VideoToolbox, snímek zůstane v paměti GPU – spodní mez), `vt` (+ `av_hwframe_transfer_data`
do NV12), `vtmap` (+ `av_hwframe_map` bez kopie), `vtbgra` (VideoToolbox rovnou vydává BGRA, pak jedna kopie).
2 kola, zátěž stroje před během ≤ 6 (load average na 10 jader). Opakování se lišila o < 1,5 %.

**B. Aplikace (sonda, necommitnutá):** `FFmpegDecoder` podle proměnné prostředí otevře dekodér s
`AV_HWDEVICE_TYPE_VIDEOTOOLBOX` (`get_format` vybere `AV_PIX_FMT_VIDEOTOOLBOX`). Snímek se stáhne (`vt`) nebo namapuje
(`vtmap`) **jen tehdy, když se převádí**, a dál jde beze změny přes `BgraConverter` / `GrayscaleConverter` (NV12 obojí
umí). Do výstupu aplikace se nakopírovaly knihovny VT. Simulátor (`tools/PrusaSimulator`, scénář `idle`, `--fps 25`,
klipy výše, porty 18492 / 18566) a aplikace v izolovaném testovacím režimu jako v T-195 (`LAYERPEEK_TEST_DATA_DIR`
v dočasné složce, přípona instance, `test-secrets.txt`). Skutečný profil uživatele zůstal netknutý. Okno se otevíralo
příkazem instance `camera:sim`. Normální okno s obrazem v plném rozlišení (720p: 640 × 360 DIP = 1280 px, 1080p:
960 × 540 DIP = 1920 px, Retina 2×) a malý náhled (výchozí 10 fps). Po 15 s zahřátí 30 s měření, 2 kola, pořadí
střídané. Sonda hlásila 25,0 dekódovaných snímků/s (malý náhled 10 převedených/s) a `nv12` u VT. Displej probuzený
(`caffeinate -d -u`), obrazovka zamčená jako v T-195. Rozptyl normálního okna mezi koly byl tentokrát ~10 % (720p SW
0,446 / 0,405), proto uvádím obě kola.

## Výsledky A – benchmark (M instrukcí na snímek, aplikace + pomocný proces)

| Krok | 720p SW | 720p VT | 1080p SW | 1080p VT |
|---|---|---|---|---|
| dekódování (VT: snímek zůstane v GPU, `vtraw`) | **9,44** | **2,96** (0,89 + 2,07 pomocný) | **21,56** | **3,20** (0,96 + 2,24) |
| + stažení do NV12 (`av_hwframe_transfer_data`) | – | +0,69 | – | +2,20 |
| + namapování bez kopie (`av_hwframe_map`) | – | ~0 | – | ~0 |
| převod plného rozlišení do BGRA | +1,89 (YUV 4:2:0) | +1,87 (NV12) | +4,22 | +4,13 |
| převod do 640 × 360 (dva průchody, malý náhled) | +4,94 | +5,06 | +14,72 | +14,88 |
| šedotón 320 × 180 z jasové složky (detekce) | +3,00 | +3,06 | +6,45 | +6,51 |
| **celá cesta plné rozlišení: SW / `vt` / `vtmap` / `vtbgra`** | **11,33** | 5,52 / **4,83** / 6,22 | **25,78** | 9,50 / **7,37** / 10,39 |
| totéž v G instr./s při 25 fps | 0,283 | 0,138 / **0,121** / 0,155 | 0,645 | 0,237 / **0,184** / 0,260 |

- VideoToolbox sníží instrukce samotného dekódování o 69 % (720p) a 85 % (1080p). Celá cesta s převodem pro okno
  v plném rozlišení klesne o 57 % / 71 % (`vtmap`).
- `vtbgra` (barevný převod ve VideoToolboxu) vychází **hůř** než NV12 + swscale: kopie 4 B/px místo 1,5 B/px a pomocný
  proces má víc práce. Nedoporučuji.
- Zmenšení pro malý náhled a šedotón stojí z NV12 stejně jako z YUV 4:2:0. T-184 / T-195 (práh zmenšení) a T-189
  (jasová rovina) tedy platí beze změny.
- Doplňkově, 1. kolo: CPU 720p SW 3,8 % / `vtmap` 8,5 % jádra (P-jádra 89 % / 35 %), odhad energie 104 / 26 mW. Že má
  méně instrukcí vyšší CPU %, je vliv P/E jader, ne práce. Proto rozhodují instrukce.

## Výsledky B – aplikace (G instr./s, kolo 1 / kolo 2)

| Scénář | SW | VT (`transfer`) | VT (`map`) | úspora `map` |
|---|---|---|---|---|
| bez okna kamery (jen sledování, 720p simulátor) | 0,031 / 0,032 | – | – | – |
| normální okno 720p | 0,446 / 0,405 | 0,258 / 0,221 | **0,223 / 0,214** | **−49 %** |
| malý náhled 720p (10 fps) | 0,350 / 0,355 | 0,183 / 0,182 | **0,176 / 0,176** | **−50 %** |
| normální okno 1080p | 1,083 / 1,043 | 0,582 / 0,558 | **0,558 / 0,549** | **−48 %** |
| malý náhled 1080p (10 fps) | 0,782 / – | 0,296 / 0,292 | **0,286 / 0,286** | **−63 %** |

Z toho pomocný proces VT: 0,038 G/s (720p) a 0,042 G/s (1080p), tj. ~1,5–1,7 M instrukcí na snímek. Podíl dekódování
na zátěži (benchmark SW 0,236 / 0,539 G/s ku aplikaci): normální okno 53–58 % (720p) a 50–52 % (1080p), malý náhled
67 % / 69 %.

**Doplňkové veličiny (kolo 1 / kolo 2; podíl P-jader 4–20 %, převážně E-jádra):**

| Scénář | CPU SW | CPU VT `map` (aplikace + pomocný) | energie CPU SW | energie CPU VT `map` | paměť VT proti SW |
|---|---|---|---|---|---|
| normální okno 720p | 21,6 / 21,8 % | 16,4 + 4,5 / 16,5 + 4,4 % | 64 / 48 mW | 40 / 36 mW | footprint aplikace −3 až −9 MB, pomocný proces +17,5 MB |
| malý náhled 720p | 14,7 / 15,2 % | 9,7 + 4,5 / 10,0 + 4,5 % | 36 / 36 mW | 29 / 28 mW | −5 MB, +18,8 MB |
| normální okno 1080p | 39,8 / 42,8 % | 30,0 + 4,7 / 31,2 + 4,9 % | 158 / 131 mW | 93 / 83 mW | −11 až +4 MB, +32,9 MB |
| malý náhled 1080p | 25,6 % | 11,7 + 4,9 / 11,4 + 4,8 % | 76 mW | 38 / 41 mW | −12 MB, +35,8 MB |

RSS aplikace se mezi SW a VT liší o 1–3 MB. Celková paměť (aplikace + pomocný proces) je s VT **o ~10–25 MB vyšší**.

## Rozbor buildu

### macOS (vlastní build ze zdrojů, T-026)

- Do `CONFIGURE_FLAGS` v `build/macos/build-ffmpeg.sh` přibude `--enable-videotoolbox --enable-hwaccel=h264_videotoolbox`.
  Nutné explicitně, protože build má `--disable-autodetect` a `--disable-everything`. Klíč cache v `build/FFmpeg.targets`
  zahrnuje obsah skriptu, takže se po změně sestaví nová cache sama.
- **Licence:** configure dál hlásí „License: LGPL version 3 or later“ (kontrola ve skriptu projde). VideoToolbox není
  v seznamech GPL ani nonfree, je to systémový framework (stejně jako dnes `libSystem`).
- **Závislosti:** všechny čtyři knihovny navíc linkují `/System/Library/Frameworks/{VideoToolbox,CoreMedia,CoreVideo,
  CoreFoundation,CoreServices}`. Kontrola cizích závislostí v `build-ffmpeg.sh` je povoluje (`/System/Library/`).
  Entitlementy ani podpis se nemění, VideoToolbox žádné oprávnění nepotřebuje.
- **Velikost:** `libavcodec` 1 621 608 → 1 624 952 B (+3,3 KB), `libavutil` 719 352 → 742 648 B (+23 KB), ostatní beze
  změny. Celkem +27 KB (+0,6 % knihoven FFmpeg).
- **Doba buildu:** 30,6 s → 34,3 s (jedno měření, souběžně běželi jiní agenti, rozdíl je zhruba v šumu). Jde o jednorázový
  build cache.

### Windows (BtbN LGPL shared, T-014)

Statická kontrola připnutého `ffmpeg-n9.0.2-39-g7adfbe40dc-win64-lgpl-shared-9.0.zip` (SHA256 sedí s
`build/FFmpeg.targets`, nic se nespouštělo): `avcodec-63.dll` obsahuje hwaccel `h264_d3d11va`, `h264_d3d11va2`,
`h264_dxva2`, `h264_d3d12va`, `h264_nvdec`, `h264_vulkan` a dekodéry `h264_qsv`, `h264_cuvid`, `h264_amf`. Konfigurace
BtbN je `--enable-version3` (LGPL 3) bez `--disable-autodetect`, D3D11VA / DXVA2 / D3D12VA se tedy zapnou automaticky
([variants/defaults-lgpl.sh](https://github.com/BtbN/FFmpeg-Builds/blob/master/variants/defaults-lgpl.sh),
[scripts.d](https://github.com/BtbN/FFmpeg-Builds/tree/master/scripts.d)). **Build se na Windows nemění**, chybí jen kód.
Pro LayerPeek dává smysl jen D3D11VA: funguje na každé GPU s ovladačem WDDM. NVDEC, QSV a AMF jsou vázané na výrobce
GPU a `codec_whitelist` je dnes stejně vylučuje.

Doplněno 2026-11-12: build BtbN nahradí T-202 (vlastní minimální LGPL build FFmpeg pro Windows). Ten musí
D3D11VA (`h264_d3d11va`) obsahovat; ověří to T-206.

### AOT a interop

Všechno potřebné je ve stávajících vazbách FFmpeg.AutoGen 9.0.1.1 (`[DllImport]`, už ověřené pro AOT):
`av_hwdevice_ctx_create`, `AVCodecContext.hw_device_ctx`, `av_hwframe_transfer_data`, `av_hwframe_map`, `av_buffer_unref`.
`get_format` je ukazatel na funkci `[UnmanagedCallersOnly]` jako dnešní callback přerušení. Sonda se přeložila s nulou
varování. Žádný nový balíček, žádná reflexe.

## Rizika a vazby

- **Návrat k softwaru (fallback):** `get_format` musí vybrat softwarový formát, když hardwarový chybí nebo selže jeho
  inicializace (FFmpeg pak `get_format` zavolá znovu bez něj). Chyba za běhu (Windows: ztráta zařízení D3D11 po spánku,
  změně GPU nebo aktualizaci ovladače, vzdálená plocha nebo VM bez GPU) se projeví chybou dekódování. Dekodér se pak musí
  znovu otevřít softwarově, nejlépe s pamětí „hardware na tomto streamu selhal“, aby se nepřepínal pořád dokola.
  VideoToolbox umí ztracenou relaci (`kVTInvalidSessionErr`, např. po spánku) obnovit sám
  ([libavcodec/videotoolbox.c](https://github.com/FFmpeg/FFmpeg/blob/release/9.0/libavcodec/videotoolbox.c), `reconfig_needed`).
- **Rozdíly ovladačů a OS:** Windows (iGPU vs. dedikovaná GPU, staging textura přes PCIe, synchronní `Map` v
  [hwcontext_d3d11va.c](https://github.com/FFmpeg/FFmpeg/blob/release/9.0/libavutil/hwcontext_d3d11va.c)) může dopadnout
  jinak než sdílená paměť Apple Silicon. Na Windows `av_hwframe_map` z D3D11 vyžaduje mapovatelné textury. Úspora na
  Windows je proto **neověřená** a měření patří do T-125.
- **Formát snímku NV12 → BGRA:** VideoToolbox vrací NV12 (u Buddy3D plný rozsah, BT.709, sonda ověřila). `BgraConverter`
  (bez zmenšení i dvouprůchodové zmenšení) a `GrayscaleConverter` (rovina Y) ho zpracují beze změny kódu. Ověřit je
  potřeba jen barvy proti SW (stejný převod swscale, jen vstupní formát se liší).
- **T-184 / T-195 (zmenšení pro zobrazení):** beze změny, ceny převodů z NV12 jsou stejné. Práh 70 / 75 % platí dál.
- **T-185 (vynechané snímky malého náhledu):** s VT stojí nepřevedený snímek jen dekódování v hardwaru. Proto má malý
  náhled největší úsporu (−50 / −63 %). Stahovat nebo mapovat se musí **jen splatné snímky**, sonda to tak dělala.
- **T-189 / T-153 (detekce):** šedotón z NV12 funguje stejně (rovina Y). Samostatné dekódování klíčového snímku (flush →
  IDR → drain → flush) se s VT **netestovalo**. U řídkých vzorků (bez okna) hardware nic neušetří a otevření relace VT
  stojí víc, proto doporučuji pro tuto cestu software ponechat.
- **Bezpečnost (T-050; přijato uživatelem 2026-11-12, viz „Bezpečnost a paměť – rozhodnutí“):** s VT parsuje bitstream dál FFmpeg (hlavičky, `codec_whitelist` platí). Data řezů ale navíc
  zpracuje systémový dekodér Apple (pomocný proces v sandboxu a ovladač v jádře). Útočná plocha se tak rozšíří
  o ovladač hardwarového dekodéru, v minulosti s aktivně zneužitými chybami (např. AppleAVD, CVE-2022-22675, opraveno
  v macOS 12.3.1). Proti tomu chrání hlavně aktualizace OS. Na Windows totéž platí pro ovladač GPU (DXVA).
- **Paměť:** +10–25 MB celkem (pomocný proces 17–36 MB). Při několika současných kamerách roste s počtem relací.
- **Zpoždění:** VT dekóduje synchronně (FFmpeg čeká na snímek). Sonda neukázala pokles fps (25,0). Zpoždění se
  neměřilo.
- **Testy:** hardware v CI ani v headless testech nemusí být. Testy dekodéru musí běžet i se softwarem (např. přepínač
  „vynutit software“) a cesta VT potřebuje vlastní test, který se bez hardwaru přeskočí.

## Odhad pracnosti

| Část | Kdo | Odhad |
|---|---|---|
| macOS build (2 přepínače, test příznaků v `build-ffmpeg.sh` / `update-ffmpeg.ps1`, `docs/prerequisites.md`) | core-dev | malé, do 1 h |
| `FFmpegDecoder`: hwaccel VT s `av_hwframe_map`, `get_format` s fallbackem, převod jen splatných snímků, znovuotevření v SW po chybě, klíčové snímky detekce dál v SW, testy (VT i vynucený SW), měření | core-dev | 1 sezení |
| volitelně nastavení „Hardwarové dekódování“ (texty 8 jazyků, manuály) – podle rozhodnutí uživatele | ui-dev | 0,5 sezení |
| Windows D3D11VA (stejný kód, jiný typ zařízení, ztráta zařízení), ověření a měření na Windows | core-dev + T-125 | 0,5 sezení + ověření na Windows |

## Doporučení

**Vyplatí se, na macOS hned, na Windows po změření.** S VideoToolboxem klesnou instrukce aplikace s otevřenou kamerou
na polovinu (malý náhled 1080p na třetinu) a odhad energie CPU o 20–50 %. Build se změní o dva přepínače (+27 KB)
a interop zůstane v mezích AOT. Úspora doby CPU je menší (0–35 %), protože pomocný proces systému čeká na hardware.
Navrhuji:

1. macOS: VideoToolbox s `av_hwframe_map` (bez kopie) pro plné dekódování (okno kamery), software jako fallback
   a pro samostatné klíčové snímky detekce. Výchozí stav (zapnuto / vypnuto / nastavení) rozhodne uživatel.
2. Windows: stejná abstrakce s D3D11VA, ale výchozí stav až podle měření v T-125. Do té doby software.
3. `vtbgra` (převod barev ve VideoToolboxu) nepoužívat, vychází hůř.

Rozhodnutí uživatele a implementační úkoly jsou v následujícím oddílu.

## Rozhodnutí a implementační úkoly

Uživatel 2026-11-12 rozhodl otázky 2–5 z T-192:

| Otázka | Rozhodnutí |
|---|---|
| 2. Implementovat? | Ano, **nejdřív macOS**: VideoToolbox s `av_hwframe_map`, software jako fallback a pro samostatné klíčové snímky detekce. |
| 3. Výchozí stav a volba | Zapnuto s volbou v Nastavení > Kamera: „Hardwarové dekódování“ **Automaticky** (výchozí) / **Vypnuto**. Texty v 8 jazycích a všechny příručky. |
| 4. Windows | Souběžně (D3D11VA), ale na Windows výchozí **software**, dokud ho T-125 nezměří. Závisí na T-202 (vlastní LGPL build FFmpeg pro Windows místo BtbN, s D3D11VA). |
| 5. Bezpečnost a paměť | Přijato: rozšířená útočná plocha (systémový hardwarový dekodér a ovladač GPU) a +10–25 MB na stream. Pojistkou je „Vypnuto“. |

**Výchozí stav podle OS:** uložená hodnota je na obou OS stejná (`Automatic`, výchozí). Co „Automaticky“ znamená,
určuje jediné pravidlo v `Camera.FFmpeg` (T-203): macOS → VideoToolbox, Windows → software. Po měření v T-125
a rozhodnutí uživatele se na Windows změní jen toto pravidlo (D3D11VA), uložené nastavení se nemigruje. „Vypnuto“
znamená vždy software. Pro měření na Windows zapne D3D11VA vývojový přepínač `LAYERPEEK_TEST_HW_DECODER=d3d11va`
(jen Debug, testovací režim). Jestli se volba na Windows do přepnutí zobrazuje, je otázka 1 v T-204.

| Úkol | Obsah | Platforma | Owner |
|---|---|---|---|
| T-203 | sdílený základ: model nastavení v Core, pravidlo podle OS, hwaccel v `FFmpegDecoder` (`get_format`, `av_hwframe_map`, jen splatné snímky, návrat k softwaru, klíčové snímky v SW), macOS build s VideoToolboxem, testy | all (doporučeno na Macu) | core-dev |
| T-204 | volba v Nastavení > Kamera, 8 jazyků, příručky, snímky | all | ui-dev |
| T-205 | macOS – měření a ověření produktového kódu (AOT, kvalita obrazu, zpoždění, spánek, dvě kamery) | macos | core-dev |
| T-206 | Windows – D3D11VA nad T-202 a T-203, ztráta zařízení, příprava měření pro T-125 | windows | core-dev |

Priorita P2, není to bloker vydání 1.0.0 (výchozí software je dnešní stav a funguje).

### Bezpečnost a paměť – rozhodnutí

- **Útočná plocha (doplněk k T-050):** bitstream z kamery je nedůvěryhodný. FFmpeg ho dál parsuje sám (hlavičky,
  `protocol_whitelist` / `format_whitelist` / `codec_whitelist` z T-050 platí beze změny), data řezů ale při
  hardwarovém dekódování zpracuje navíc systémový dekodér: na macOS `VTDecoderXPCService` (sandbox) a ovladač AVD
  v jádře, na Windows uživatelská a jaderná část ovladače GPU (D3D11VA / DXVA). Chyby v nich se v minulosti zneužívaly
  (např. AppleAVD, CVE-2022-22675). Ochranou jsou aktualizace OS a ovladačů; LayerPeek je neopravuje. Uživatel tuto
  plochu 2026-11-12 přijal.
- **Pojistka:** „Vypnuto“ vrátí stav před T-203 (jen software, žádný systémový dekodér). Samostatné klíčové snímky
  detekce, test kamery a jednorázový snímek jdou vždy softwarem, takže bez otevřeného okna kamery se systémový dekodér
  nepoužije vůbec.
- **Paměť:** +10–25 MB na stream (macOS: pomocný proces 17–36 MB, aplikace o 3–12 MB méně); relace existuje jen
  při plném dekódování a uvolní se po zavření okna. Mez pro ověření v T-205: ≤ 40 MB na stream.

### Windows – měření pro přepnutí výchozího stavu

Body přidá do T-125 úkol T-206 po svém dokončení. Měří se D3D11VA (`LAYERPEEK_TEST_HW_DECODER=d3d11va`, Debug) proti
„Vypnuto“:

- **Metriky:** cykly procesu (`QueryProcessCycleTime`; Windows nemá snadno dostupné instrukce procesu, uživatelská část
  ovladače GPU ale běží v procesu aplikace), CPU % procesu, využití enginu GPU „Video Decode“
  (`\GPU Engine(*engtype_VideoDecode)\Utilization Percentage`), paměť (private bytes, working set,
  `\GPU Process Memory(*)\Dedicated Usage` a `Shared Usage`), dekódované a převedené snímky/s.
- **Scénáře:** bez okna kamery, normální okno 720p a 1080p (100 % DPI, plné rozlišení), malý náhled 720p a 1080p
  (10 fps); simulátor s klipy z T-192, 15 s zahřátí, 30 s měření, 2 kola se střídaným pořadím; integrovaná
  i dedikovaná GPU, pokud jsou k dispozici; `av_hwframe_map` proti `av_hwframe_transfer_data`.
- **Robustnost:** spánek / probuzení, `Win+Ctrl+Shift+B` (restart ovladače grafiky), vzdálená plocha, VM bez GPU –
  vždy obraz, případně návrat k softwaru.
- **Návrh meze pro přepnutí (rozhoduje uživatel):** cykly procesu −30 % a víc v normálním okně i malém náhledu na každé
  změřené GPU, doba CPU nevzroste, paměť navíc ≤ 50 MB na stream, v robustních scénářích žádná trvalá chyba. Splněno →
  uživatel rozhodne o přepnutí (nový úkol: pravidlo, příručky, případně zobrazení volby na Windows). Nesplněno →
  na Windows zůstává software.
