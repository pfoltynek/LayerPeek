# Timelapse tisku – průzkum (spike T-149)

Stav 2026-11-03 (architekt, macOS arm64, Mac mini M2 Pro). Prototypy běžely jen mimo repozitář (scratchpad,
Homebrew FFmpeg 9.0.2 a vlastní buildy knihoven podle `build/macos/build-ffmpeg.sh`). Do repozitáře jde jen tato zpráva
a návrh úkolů. Rozhodnutí uživatele z 2026-11-04 jsou v oddílu „Rozhodnutí uživatele“ na konci a v úkolu T-149.

## Shrnutí

- **Doporučení: timelapse z klíčových snímků kamery bez překódování (varianta D).** Kamera Buddy3D posílá každých
  ~2,2 s snímek IDR (nezávisle dekódovatelný), mezi nimi jen P-snímky. Když se z proudu paketů vezmou jen vybrané IDR
  a dostanou nová časová razítka, vznikne platné H.264 v MP4. Přehraje ho FFmpeg i AVFoundation (QuickTime) a dekódované
  snímky jsou **bitově shodné** s týmiž snímky z celého streamu. Nepotřebuje to enkodér, dekodér, novou závislost ani
  změnu buildu FFmpeg (0 B navíc na obou OS). CPU ani paměť nepřidá nic nad příjem RTSP, který už dělá nahrávání (T-144).
  Stávající `ICameraRecordingWriter` jde použít beze změny.
- Velikost: ~65 kB na snímek, tj. **~78 MB za 10 h při 1 snímku za 30 s** (40 s videa při 30 fps). Kvalita = kvalita
  klíčového snímku kamery, žádná druhá ztrátová komprese.
- Omezení: snímek se dá vzít jen v okamžiku klíčového snímku (zpoždění 0–2,4 s proti požadavku). Rozlišení a kvalitu
  nelze měnit (žádné zmenšení, žádný vodoznak, žádné otočení). Pro tuto kameru to stačí.
- **Spouštěč po čase** je triviální. **Spouštěč po vrstvě** jde jen odhadnout z `axis_z` při pollingu. PrusaLink
  na Buddy číslo vrstvy ani událost změny vrstvy nehlásí a z-hop (Core One až 1,5 mm) je v `axis_z` vidět. Navržený
  odhad ověří až záznam skutečného tisku (T-159).
- Endpoint `PUT /api/v1/job/{id}/continue` („after timelapse capture“) firmware Buddy **neimplementuje** (vrátí 400).
  Specifikace OpenAPI je společná i pro PrusaLink na Raspberry Pi a SL1. Na Buddy to použít nejde.
- Vedlejší nález: **připnutý build FFmpeg pro Windows (BtbN `autobuild-2026-10-17-20-51`) už na GitHubu není (404)**.
  Čistý build na Windows bez cache proto selže. Návrh opravy je v T-162.

## Zdrojová data

| Data | Popis |
|---|---|
| Klip | `~/LayerPeek-samples/clips/coreone-20261103-1905-60s.mp4`: 59 s, H.264 Constrained Baseline 1280×720, level 4.0, 1284 paketů, 26 klíčových snímků, ~1,06 Mbit/s |
| Živý záznam | 5 min z Buddy3D přes relay `rtsp://127.0.0.1:8556/live` (2026-11-03 ~22:05, `-c copy`, jen ve scratchpadu): 6891 paketů (22,97 fps), 1,065 Mbit/s, 138 klíčových snímků |
| Snímky | `~/LayerPeek-samples/coreone-frames/`: 463 JPEG 1280×720 po 30 s ze skutečného tisku (`sips`, kvalita 80, ~220 kB), zdroj pro porovnání enkodérů |

## Struktura streamu Buddy3D (ověřeno `trace_headers`)

- Každý klíčový snímek je **IDR** (NAL typ 5), před každým jsou v proudu znovu SPS a PPS (NAL 7, 8). Ostatní snímky jsou
  P (NAL 1, `slice_type` P), jeden řez na snímek, žádné B-snímky (`has_b_frames=0`), `max_num_ref_frames = 1`,
  `pic_order_cnt_type = 0`, `gaps_in_frame_num_allowed_flag = 1`.
- Interval klíčových snímků (5 min živě): průměr **2,164 s**, min 2,099 s, max 2,380 s. Velikost IDR: průměr 65,0 kB
  (živě), 68,4 kB (klip), rozptyl 61–72 kB. IDR tvoří ~23 % datového toku.
- `idr_pic_id` cyklí 0–15. Při výběru jen některých IDR tak mohou dva sousední mít stejné `idr_pic_id`. Norma H.264
  (7.4.3) žádá, aby se dvě po sobě jdoucí IDR lišila. Ověřeno: FFmpeg (přísně, `-err_detect explode -xerror`)
  i AVFoundation takový soubor dekódují bez chyby (6/6 snímků). Dekodér Media Foundation na Windows zatím neověřen
  (T-158 → ruční ověření T-125). Kdyby vadil, dá se `idr_pic_id` v hlavičce řezu přepsat (exp-Golomb, bez dekódování).

## Varianty

Měření enkodérů: 463 snímků 1280×720, jedno vlákno (`-threads 1`), Homebrew FFmpeg 9.0.2, M2 Pro. CPU enkodéru
= CPU procesu minus samotné dekódování vstupních JPEG (1,42 s). SSIM proti zdrojovým snímkům (zdroj je už sám JPEG,
SSIM tedy měří ztrátu druhou kompresí). „10 h“ = 1200 snímků (1 za 30 s), video 40 s při 30 fps.

| Varianta | Licence (primární zdroj) | Native AOT | Binárka navíc macOS / Windows | CPU navíc (na tiskárnu) | Na snímek | 10 h po 30 s | Přehratelnost |
|---|---|---|---|---|---|---|---|
| **D. Klíčové snímky H.264 bez překódování** (živě z odběru paketů) | žádný nový kód třetí strany; FFmpeg muxer `mp4` LGPL (už v buildu, T-147) | beze změny (stávající P/Invoke FFmpeg) | **0 B / 0 B** | ~0 nad příjem RTSP (výběr paketů, zápis 65 kB / 30 s); bez nahrávání příjem RTSP ~1,7 % jádra (měření T-144) | ~65 kB, kvalita = IDR kamery (bitově shodné) | **~78 MB** | H.264 Constrained Baseline v MP4: všude (FFmpeg + AVFoundation ověřeno; Filmy a TV / VLC na Windows ověřit) |
| A1. Sekvence JPEG přes Avalonia (`Bitmap.Save` + `JpegBitmapEncoderOptions`, Skia + libjpeg-turbo) | Avalonia MIT, SkiaSharp MIT, Skia BSD-3, libjpeg-turbo (IJG/BSD-3/zlib) – vše už je v aplikaci a v `skia-harfbuzz-native-notices.txt` | ano (Avalonia/Skia už v AOT publish) | 0 B / 0 B | dekódování streamu (dnes celé: **2,35 % jádra**, jen klíčové snímky: **0,15 %**) + kódování JPEG (neměřeno, odhad 3–5 ms / snímek podle FFmpeg `mjpeg`) + 3 buffery BGRA (~11 MB, T-143) | ~160 kB (odhad podle FFmpeg `mjpeg` q3, SSIM 0,991) | ~190 MB | obrázky; video si uživatel složí sám |
| A2. Sekvence JPEG / PNG přes FFmpeg `mjpeg` / `png` | LGPL (FFmpeg `libavcodec/mjpegenc.c`, [ffmpeg.org/legal](https://ffmpeg.org/legal.html)); PNG potřebuje zlib (macOS systémová `/usr/lib/libz`, Windows v BtbN) | ano | mjpeg **+232 kB**, png **+17,7 kB** / 0 B (BtbN obsahuje) | jako A1; kódování JPEG 5,1 ms, **PNG 106 ms** na snímek | JPEG ~160 kB, PNG ~930 kB | 192 MB / **1,1 GB** | obrázky |
| B. MJPEG v MOV / AVI přes FFmpeg `mjpeg` | LGPL | ano | +232 kB / 0 B | jako A1; 3,9 ms (q5) / 4,9 ms (q3) na snímek | 110 kB (q5, SSIM 0,981) / 157 kB (q3, 0,991) | 132 / 188 MB | AVFoundation ověřeno (MOV i AVI); Windows neověřeno |
| C1. H.264 VideoToolbox (macOS) přes FFmpeg `h264_videotoolbox` | obal LGPL (`videotoolboxenc.c`); VideoToolbox je součást OS – „System Libraries“ GPLv3 § 1 ([gpl-3.0](https://www.gnu.org/licenses/gpl-3.0.html)) | ano | **+84 kB** (`--enable-videotoolbox --enable-encoder=h264_videotoolbox`; linkuje VideoToolbox a CoreServices) | jako A1; enkodér v hardwaru, v procesu ~0 ms | 37 kB (q55, SSIM 0,974); 17 kB při 4 Mbit/s (0,955) | 44 / 20 MB | všude |
| C2. H.264 Media Foundation (Windows) přes FFmpeg `h264_mf` | obal LGPL (`mfenc.c`); MF je součást OS | ano | 0 B (BtbN: `h264_mf` v `avcodec-63.dll`, ověřeno na buildu `autobuild-2026-10-31-12-59`) | neměřeno (jen Windows); enkodér MS (software) nebo MFT výrobce GPU | neměřeno | neměřeno | všude; edice Windows N bez Media Feature Pack MF nemají |
| C3. Vlastní interop na VideoToolbox / Media Foundation (bez FFmpeg) | OS | ano, ale dvě vlastní implementace (`[LibraryImport]`, `[GeneratedComInterface]`) | 0 B | jako C1 / C2 | jako C1 / C2 | jako C1 / C2 | všude |
| C4. H.264 OpenH264 (`libopenh264`) | kód BSD-2-Clause ([LICENSE](https://github.com/cisco/openh264/blob/master/LICENSE)); **patentová licence Cisco platí jen pro binárku staženou od Cisca** ([BINARY_LICENSE](https://www.openh264.org/BINARY_LICENSE.txt), [FAQ](https://www.openh264.org/faq.html): „must download it at the time the product … is installed“, při vlastním buildu platí poplatky ten, kdo distribuuje) | ano | macOS: nutno přeložit OpenH264 (neměřeno) / Windows 0 B (BtbN ho má staticky v `avcodec-63.dll`) | jako A1 + softwarový enkodér | neměřeno | neměřeno | všude |
| E. `mpeg4` (MPEG-4 Part 2) přes FFmpeg | LGPL | ano | **+431 kB** (s `h263`) / 0 B | jako A1; 3,9–5,1 ms na snímek | 58 kB (q5, SSIM 0,974) / 105 kB (q3, 0,985) | 70 / 126 MB | AVFoundation ověřeno; Windows neověřeno |
| F. Dodatečně z hotové nahrávky (T-145) – klíčové snímky z MP4 | LGPL; demuxer `mov` už v buildu pro macOS je (vybírá ho `rtpdec`, ověřeno: build s `--enable-demuxer=mov` má stejnou velikost) | ano | 0 B / 0 B | 5 min nahrávky (40 MB) → 0,10 s CPU, bez dekódování; 10 h ≈ 12 s CPU + čtení ~4,9 GB | jako D | jako D (ale nahrávka ~4,9 GB za 10 h musí existovat) | jako D |

Poznámky k tabulce:
- Variantu C3 (vlastní interop) nedoporučuji. Obaly FFmpeg dělají totéž a je to stejný kód pro obě platformy.
- Patenty: JPEG a PNG jsou bez patentových otázek. U H.264 se varianta D od dnešního stavu nijak neliší (dnes už se H.264
  dekóduje a přebaluje). Varianty C1 a C2 kódují enkodérem OS. C4 a E kódují vlastním enkodérem distribuovaným s aplikací,
  to je nová patentová otázka a posoudit ji musí uživatel. Netvrdím, že se tam patentová licence vyžaduje nebo nevyžaduje.
- Velikosti knihoven macOS jsou z vlastních buildů skriptu `build-ffmpeg.sh` s přidaným příznakem (FFmpeg 9.0.2,
  stejný tarball a SHA-256 jako `build/FFmpeg.targets`). Základní build dává stejné velikosti jako T-147:
  `libavcodec` 1 621 608 B, `libavformat` 992 312 B. Přidání: mjpeg → `libavcodec` 1 853 864 B; mpeg4 → 2 052 360 B;
  VideoToolbox → `libavcodec` 1 682 632 B + `libavutil` 742 648 B; png → `libavcodec` 1 639 032 B + `libavformat` 992 600 B.
  Všechny prošly kontrolou licence ve skriptu („LGPL version 3 or later“) a závislostí jen na systému. Kdyby se zapnul
  VideoToolbox, musí se upravit test `ThirdPartyLicensesTests.FFmpegMacOSBuildIsLgplOnlyWithoutHardwareDecoding`
  (hlídá řádek `configure`). Hardwarové dekódování se tím nezapne („Enabled hwaccels“ zůstává prázdné).
- Windows BtbN (`ffmpeg-n9.0.2-39-g7adfbe40dc-win64-lgpl-shared-9.0`, ověřeno přes `strings avcodec-63.dll`, protože
  `.dll` na Macu spustit nejde): enkodéry `mjpeg`, `png`, `mpeg4`, `h264_mf`, `hevc_mf`, `av1_mf`, `libopenh264`,
  `h264_amf`, `h264_nvenc`, `h264_qsv`, `h264_vulkan` a další. `libx264` / `libx265` jsou vypnuté (`--disable-libx264`).
  `avcodec-63.dll` má 91,8 MB, enkodéry tedy už dnes s aplikací na Windows chodí.

## Zdroj snímků a náklady za běhu

- **Varianta D** potřebuje jen odběr paketů hubu (`CameraStreamHub.SubscribePackets`, T-143). Ten nespouští dekodér.
  Když současně běží nahrávání, sdílí se jedno spojení, timelapse pak stojí prakticky nic. Bez nahrávání drží timelapse
  spojení RTSP otevřené celý tisk: ~1,7 % jednoho jádra (příjem + zápis, měření T-144) a ~1,07 Mbit/s v místní síti.
  Paměť: fronta odběru (pool 16 bufferů paketů, T-143), < 1 MB.
- **Varianty A–C, E** potřebují dekódované snímky. Odběr snímků dnes zapne dekódování **všech** snímků: 7,06 s CPU
  za 5 min živého záznamu = **2,35 % jádra** (Homebrew FFmpeg, jedno vlákno; T-152 naměřil 1,9 % na klipu). Jen klíčové
  snímky (`skip_frame = AVDISCARD_NONKEY`): 0,45 s = **0,15 %**. Režim „jen klíčové snímky“ hub zatím nemá, navrhuje ho
  i spike T-152 (otázka 9 tam). K tomu kopie BGRA 1280×720 do tří bufferů na odběratele (~11 MB) a kódování snímku.
  Při 1 snímku za 30 s je samo kódování zanedbatelné (MJPEG 4 ms = 0,013 % jádra).
- **Připojení jen na snímek** (bez trvalého spojení): od připojení RTSP po první IDR 1,74–3,92 s (5 pokusů přes relay).
  Při intervalu 30 s by spojení běželo ~6–13 % času. Ušetří to síť a CPU příjmu, ale spojení Buddy3D se pak každých
  30 s otevírá a zavírá. Robustnost kamery při tom neověřena. U spouštěče po vrstvě přidá zpoždění 2–4 s.
  Doporučuji trvalé spojení po dobu tisku (jednodušší, stejné jako nahrávání), rozhodne uživatel (otázka 7).
- **Varianta F (dodatečně)** nepotřebuje za tisku nic navíc, jen hotovou nahrávku (~490 MB/h, T-144) a krátké zpracování
  po tisku. Spouštěč po vrstvě by pak potřeboval uložený průběh Z s časem tisku.

## Spouštěč po vrstvě (ověření)

**Co hlásí PrusaLink na Buddy** (firmware `master` `baac5e67`, 2026-11-03):
- `GET /api/v1/status` (`lib/WUI/nhttp/status_renderer.cpp`): `printer.axis_z` = `marlin_vars().logical_curr_pos[2]`,
  1 desetinné místo (`JSON_FIELD_FFIXED(..., 1)`). `job` má jen `id`, `progress`, `time_remaining`,
  `filament_change_in`, `time_printing`. Číslo vrstvy, výška vrstvy ani počet vrstev tam nejsou.
- `logical_curr_pos` se plní z Marlin `current_position` (`src/common/marlin_server.cpp`, `_server_update_vars`). To je
  cíl posledního zařazeného pohybu (planner), ne skutečná poloha. Je bez korekce mesh levelingu, Z-hop v něm ale je.
- Metadata souboru (`layer_height` …) Buddy v `/api/v1/job` neposílá (viz `docs/api/prusalink.md`).
- Specifikace OpenAPI zná `CameraConfig.trigger_scheme` (`TEN_SEC`, `THIRTY_SEC`, `SIXTY_SEC`, `EACH_LAYER`,
  `FIFTH_LAYER`, `MANUAL`) a `PUT /api/v1/job/{id}/continue` („Continue in job with given id after timelapse capture“).
  Na Buddy je ale `/api/v1/cameras` 404 a příkazy úlohy (`lib/WUI/link_content/prusa_link_api_v1.cpp`, `handle_command`)
  znají jen `pause` a `resume`, cokoli jiného vrátí **400 Bad Request**. Použít to nejde.
  Zdroje: https://github.com/prusa3d/Prusa-Link-Web/blob/master/spec/openapi.yaml,
  https://github.com/prusa3d/Prusa-Firmware-Buddy/blob/baac5e6710204cc726b298be557c376f2abbee33/lib/WUI/link_content/prusa_link_api_v1.cpp,
  https://github.com/prusa3d/Prusa-Firmware-Buddy/blob/baac5e6710204cc726b298be557c376f2abbee33/src/common/marlin_server.cpp.

**Z-hop na Core One** (profil `Prusa CORE One HF0.4 nozzle`, PrusaSlicer-settings 2.5.14,
https://github.com/prusa3d/PrusaSlicer-settings-prusa-fff/blob/main/PrusaResearch/2.5.14.ini): `retract_lift = 0.2`,
`retract_lift_below = 269`, `travel_ramping_lift = 1`, `travel_max_lift = 1.5`, `travel_slope = 1`. Při přejezdu
se tedy Z zvedne o 0,2 až 1,5 mm. To je víc než jedna vrstva (0,05–0,3 mm). Vzorek pollingu, který padne do přejezdu,
ukáže vyšší Z. Prosté „Z se změnilo“ by tak dávalo falešné vrstvy.

**Polling:** výchozí 5 s (`AppSettings.DefaultPollingInterval`). Vrstvy kratší než interval se sloučí. Zaokrouhlení
na 0,1 mm: vrstva 0,05 mm se projeví jen každou druhou vrstvou, 0,07 mm nepravidelně. Pro timelapse to nevadí, snímků
je pak jen méně.

**Proč to teď nejde ověřit na datech:**
- Simulátor (`tools/PrusaSimulator`, `PrinterSimulation`) počítá Z lineárně z průběhu (`0.2 + (MaxZ − 0.2) × progress / 100`)
  bez vrstev a bez z-hopu. Na detekci vrstvy se proto nehodí.
- Skutečná tiskárna: agent má přístup jen ke kameře (relay RTSP). `GET /api/v1/status` vyžaduje heslo PrusaLink
  (Digest). Hesla uživatele agent nepoužívá, záznam `axis_z` během tisku proto pořídit nemohl.
- Návrh: v T-159 připravit skript, který během tisku uloží jen `čas, state, axis_z, progress` každou 1 s. Uživatel ho
  spustí na jednom až dvou tiscích. Detektor (minimum *k* posledních vzorků, práh 0,05 mm, reset u nové úlohy) se ověří
  na tomto záznamu. Do té doby je spouštěč po vrstvě **neověřený odhad**. Spouštěč po čase žádné ověření nepotřebuje.

**Ověření na záznamu (T-159, 2026-11-05):** skutečný tisk Core One (0,2 mm, 277 vrstev, ~2,8 h, vzorek ~1 s,
`tests/LayerPeek.Core.Tests/Fixtures/z-recordings/`). Z-hop je v 1 % vzorků (+0,1 až +0,6 mm, 1–3 s), nejkratší vrstva
27 s. Navržené minimum *k* vzorků dávalo falešné vrstvy (dva z-hopy za sebou a přechodová hodnota 11,9 mezi 11,8
a 12,0), proto má detektor `LayerChangeDetector` plató 3 stejných vzorků, záložní „rovnoměrné stoupání“ pro krátké
vrstvy a strop 1 mm na krok. Při pollingu 2, 3 i 5 s (všechny fáze) 276/276 změn bez falešných, polling se zkracovat
nemusí. Podrobně T-159.

**Hlava v záběru:** bez parkování hlavy při změně vrstvy (to dělá G-code timelapse v PrusaSliceru / OctoPrintu) bude
hlava na snímcích na náhodném místě. Parkování by znamenalo úpravu G-code uživatelem a jeho rozpoznání z Z / času,
mimo rozsah.

## Prusa Connect

Prusa Connect poskytuje jen snímky kamery (ne stream) a klient Prusa Connect v projektu není (T-005 vyřazen). Timelapse
ze snímků Prusa Connect se proto neřeší.

## Návrh architektury (pro variantu D)

```
Core/Cameras/Timelapse            (T-158, T-160)
  rekordér timelapse ── SubscribePackets ──► CameraStreamHub (T-143)
        │  vybere první IDR po požadavku, razítka n × délka snímku
        └──► ICameraRecordingWriter (T-144, beze změny) ──► Camera.FFmpeg (muxer mp4)
  spouštěč po čase (TimeProvider)  /  spouštěč po vrstvě ◄── detektor vrstvy z Telemetry.AxisZ (T-159)
  služba timelapse (kdy, kam, rotace) – po vzoru CameraRecordingService (T-145)
App                                (T-161) nastavení, tray menu, okno kamery, notifikace
```

Core nezná FFmpeg ani Avalonii. Varianty A–C by místo odběru paketů potřebovaly odběr snímků a nové rozhraní kodéru
snímků v Core s implementací v App (Avalonia JPEG) nebo v Camera.FFmpeg (enkodér).

## Prototypy (jen scratchpad, nic v repozitáři)

- Výběr klíčových snímků: `ffmpeg -c copy -bsf:v "noise=drop=not(key),setts=ts=N*3000:duration=3000"` (časová základna
  1/90000, 30 fps). Výsledek 26 snímků, 1 778 637 B (režie kontejneru ~1 kB); fragmentovaný MP4 jako T-144
  (`frag_keyframe+empty_moov`) 1 782 147 B (+135 B na snímek).
- Kontrola: `ffmpeg -err_detect explode -xerror … -f null -` bez chyby; `ffprobe -count_frames` 26/26; vlastní program
  AVFoundation (`AVAssetReader`, systémový dekodér jako QuickTime) 26/26; `framemd5` snímků timelapse = `framemd5`
  klíčových snímků z celého streamu (`-skip_frame nokey`), 26/26 shodných.
- Totéž z 5min živého záznamu: 138 snímků, 8 977 143 B, FFmpeg i AVFoundation bez chyby.
- V C# by výběr byl jednoduchý: `CameraPacket.IsKeyFrame` + vlastní hodiny místo `RecordingClock`. Bitstream filtry
  FFmpeg (`noise`, `setts`) potřeba nejsou.

## Rozhodnutí uživatele (2026-11-04)

- **Varianta D** (MP4 z klíčových snímků bez překódování) schválena, výstup pevně 30 fps → T-158.
- **Spouštěč:** nejdřív po čase (T-158, T-160), výchozí interval 30 s, nastavitelný (návrh 10 / 30 / 60 s nebo
  5–600 s, ovládání vybere T-161). Po vrstvě až po ověření na skutečném tisku (T-159).
- **Kdy:** automaticky při tisku (volba u tiskárny, výchozí vypnuto) i ručně z tray menu a z okna kamery. Během
  `Paused` / `Attention` se nesnímá. Okno kamery ukazuje indikátor pořizování timelapse (jako REC).
- **Experimentální funkce:** celý timelapse má v UI štítek „Experimentální“ → T-161.
- **Ukládání** do kořene a podsložky tiskárny jako nahrávky, společný limit a rotace. RTSP zůstává otevřené po celý tisk.
- **Priorita:** až po dashcamu T-148 (T-158 až T-161 na něm závisí).
- **Samostatné úkoly P3:** timelapse z hotové nahrávky (varianta F) → T-164, export sekvence snímků JPEG → T-165.
- **FFmpeg pro Windows (T-162):** připnout trvalý měsíční build `autobuild-2026-10-31-12-59`.
