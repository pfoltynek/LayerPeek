---
id: T-144
title: Nahrávání kamery – zápis streamu do souboru bez překódování (Camera.FFmpeg + abstrakce v Core)
status: done
owner: core-dev
priority: P2
platform: all
depends_on: [T-143]
---

## Cíl
Technický základ nahrávání videa z kamery (nápad 1, fáze 1). Pakety H.264 z RTSP se jen **přebalí (remux)** do souborového kontejneru, **bez překódování**: žádný enkodér, nízké CPU a žádná otázka licence GPL / x264. Licence FFmpeg zůstává LGPL.

Tento úkol řeší jen „zapiš pakety ze sdíleného spojení (T-143) do souboru a správně ho ukonči“. Kdy se nahrává, kam a jak dlouho se nahrávky drží, řeší T-145. UI řeší T-146.

## Výchozí stav (ověřeno 2026-11-03)
- **Windows (`win-x64`):** build BtbN `ffmpeg-n9.0.2-…-win64-lgpl-shared-9.0` (`build/FFmpeg.targets`) je plný LGPL build bez `--disable-everything`. V `avformat-63.dll` jsou muxery „MP4 (MPEG-4 Part 14)“ a „Matroska / WebM“ (ověřeno na stažených DLL v cache `%LOCALAPPDATA%\LayerPeek.Build\ffmpeg\00ab3bc8…\bin`). Pro tento úkol se na Windows nic nemění.
- **macOS (`osx-arm64`):** `build/macos/build-ffmpeg.sh` staví minimální FFmpeg (`--disable-everything`, jen `protocol=tcp,file`, `demuxer=rtsp,h264`, `parser=h264`, `decoder=h264`). **Muxer chybí**, nahrávání na Macu proto bude fungovat až po T-147 (úkol `macos`). Tento úkol musí za běhu poznat, že muxer není k dispozici (např. `av_guess_format` / `avformat_alloc_output_context2` vrátí chybu), a vrátit srozumitelný stav „nahrávání není k dispozici“ – ne výjimku při startu.

## Rozsah
- **Core (`Core/Cameras/Recording` nebo podobně):** abstrakce, pracovně `ICameraRecorderFactory` / `ICameraRecording` (návrh rozhraní je na core-dev):
  - vytvoření nahrávky do zadané cesty z odběru paketů hubu (T-143),
  - `IsAvailable` / důvod nedostupnosti (chybějící muxer, kamera vypnutá),
  - průběžná velikost souboru a délka nahrávky (pro limit v T-145),
  - korektní ukončení (`StopAsync` zapíše konec kontejneru) a chování při chybě (výpadek streamu, plný disk, odebraný disk).
- **Camera.FFmpeg:** implementace přes `avformat_alloc_output_context2` + `avio_open` + `avformat_write_header` + `av_interleaved_write_frame` + `av_write_trailer` (FFmpeg.AutoGen, jako dosavadní dekodér; žádný nový balíček).
  - Nahrávka začíná **na klíčovém snímku** (IDR); pakety před prvním klíčovým snímkem se zahodí.
  - Časová razítka z RTSP se přepočítají na časovou základnu výstupu a začínají od nuly; po výpadku streamu a novém připojení (T-014: `ReconnectingCameraFeed`) se buď pokračuje se spojitými razítky, nebo začne nový soubor – podle otázky 3.
  - Parametry kodeku (SPS/PPS, rozměr) z popisu relace (SDP) / ze streamu; ověřit, že výsledný soubor přehraje běžný přehrávač (Windows: Filmy a TV / VLC, macOS: QuickTime u MP4).
  - Zápis souboru mimo vlákno dekodéru, aby pomalý disk nezdržel živý obraz (fronta s limitem; při přetečení se nahrávka ukončí s chybou, živý obraz běží dál).
  - Adresa kamery jde do logu jen přes `UriRedaction`. Co se smí zapsat o cestě nahrávky, určí otázka 5.
- **Měření:** změřit datový tok kamery Buddy3D (MB za hodinu) na skutečné kameře uživatele – podklad pro výchozí limit velikosti v T-145. Výsledek zapsat do poznámek.

## Akceptační kritéria
- [x] Nahrávka z fixture `buddy3d.h264` i z RTSP kamery simulátoru (loopback) vytvoří platný soubor zvoleného kontejneru; test ho otevře zpět přes FFmpeg demuxer a ověří počet snímků, rozměr 1280×720, kodek H.264 a monotónní časová razítka. (část Windows → přesunuto do T-125)
- [x] Nahrávka začíná klíčovým snímkem (test).
- [x] Žádné překódování: enkodér se neotevírá (test / revize kódu) a CPU při nahrávání bez okna je zanedbatelné (změřit, zapsat do poznámek).
- [x] Výpadek kamery během nahrávání (simulátor `camera offline` / `online`) neshodí aplikaci; chování podle otázky 3 je otestované.
- [x] Ukončení aplikace / zrušení tokenu nahrávku korektně uzavře (soubor jde přehrát). Chování po tvrdém pádu procesu odpovídá zvolenému kontejneru (otázka 1) a je popsané v poznámkách.
- [x] Bez muxeru (simulovaná nedostupnost) vrátí factory stav „není k dispozici“ s důvodem, bez výjimky.
- [x] Testy po sobě smažou všechny vytvořené soubory i po pádu testu (dočasná složka testu, úklid na začátku i na konci).
- [x] Ustálený stav živého obrazu se zapnutým nahráváním bez alokací na snímek v dekodéru (test z T-014 dál prochází). (část Windows → přesunuto do T-125)
- [x] `dotnet build` a `dotnet test` bez chyb a varování; žádná nová varování AOT / trim.

## Otevřené otázky
1. **Kontejner:** MP4, MKV, nebo volba v nastavení?
   - MP4 přehraje vše (i QuickTime), ale klasický MP4 je po pádu aplikace bez `moov` nečitelný. Řešením je fragmentovaný MP4 (`movflags=frag_keyframe+empty_moov`); některé starší přehrávače / editory s ním mají potíže.
   - MKV je po pádu čitelný až do posledního zapsaného bloku, QuickTime na macOS ho ale neotevře.
2. **Rozdělování dlouhých nahrávek:** jeden soubor na celý tisk (i 20 h), nebo segmenty po N minutách (snazší rotace v T-145, menší ztráta při chybě)? Pokud segmenty, jaká délka?
3. **Výpadek streamu během nahrávání:** pokračovat ve stejném souboru (mezera v čase / skok) nebo začít nový soubor?
4. **Zvuk:** Buddy3D zvuk nemá (předpoklad z T-014 – jedna stopa H.264); nahrává se jen video. Potvrdit.
5. **Co se smí objevit v logu:** stačí název souboru nahrávky bez složky (složka může obsahovat uživatelské jméno), nebo celá cesta?

### Rozhodnutí uživatele 2026-11-03
1. Kontejner: **fragmentovaný MP4** (`movflags=frag_keyframe+empty_moov`), bez volby v nastavení.
2. **Jeden soubor na tisk**, bez segmentů.
3. Výpadek streamu během nahrávání: **nový soubor** (přípona `-2`, `-3`…).
4. **Jen video, bez zvuku** – potvrzeno.
5. V logu **jen název souboru**, bez složky.

## Poznámky k implementaci
Implementováno 2026-11-03 na macOS arm64 (core-dev), větev nad T-147 (muxer `mp4` v buildu pro macOS).

**Rozhraní (Core, `src/LayerPeek.Core/Cameras/Recording/`):**
- `ICameraRecorder` (`CameraRecorder` nad `CameraStreamHub`): `Availability` (`CameraRecorderAvailability`: `IsAvailable`, `Reason`) a `Start(printerId, filePath, changed)` → `ICameraRecording`. `ArgumentException` jen pro chybné argumenty (relativní cesta, bez názvu souboru), jinak nic nevyhazuje: bez muxeru vrátí nahrávku hned ve stavu `Failed` / `Unavailable`, bez kamery skončí `Failed` / `CameraUnavailable`.
- `ICameraRecording`: `PrinterId`, `State` (`Starting`, `Recording`, `Completed`, `Failed`), `Failure` (`Unavailable`, `CameraUnavailable`, `WriteFailed`, `DiskFull`, `TooSlow`, `StreamFailed`), `FailureMessage` (anglicky, bez cesty), `Files`, `BytesWritten`, `Duration` (podle časových razítek), `Completion` (nikdy nespadne), `StopAsync()` / `DisposeAsync()` (idempotentní, dokončí soubor). Zpětné volání `changed` při změně stavu nebo nového souboru (na vlákně nahrávky, nesmí blokovat). Pro T-145: start / stop za tiskárnu, chyby přes `Completion` + `Failure`, velikost a délka pro limit.
- `ICameraRecordingWriterFactory` / `ICameraRecordingWriter`: šev ke Camera.FFmpeg (a falešný zapisovač v testech Core).
- Registrace v `AppServices.AddCameras` (`ICameraRecordingWriterFactory` = `FFmpegCameraRecordingWriterFactory`, `ICameraRecorder` = `CameraRecorder`); zatím ji nikdo nepoužívá (T-145).

**Chování:**
- Každá nahrávka má vlastní odběr paketů hubu a čte ho na thread poolu. Hub tak nikdy nečeká na disk. Fronta odběru je omezená (512 položek, ~23 s), při přetečení skončí nahrávka `TooSlow` a živý obraz běží dál.
- Pakety před prvním `StreamStarted` (paket starého spojení po změně adresy, poznámka revize T-143) a před prvním klíčovým snímkem se zahodí. Soubor vzniká až s prvním klíčovým snímkem, nahrávka bez obrazu nenechá prázdný soubor.
- Nový `StreamStarted` (výpadek, nové připojení) dokončí soubor a další začne s příponou `-2`, `-3`… (rozhodnutí 3). Název se rezervuje přes `FileMode.CreateNew`, existující soubor se nikdy nepřepíše (obsazená přípona se přeskočí). Když zapisovač soubor neotevře, prázdný soubor se smaže.
- Časová razítka (`RecordingClock`): v časové základně streamu od nuly, DTS vždy roste, PTS nikdy pod DTS. Chybějící, zpětné nebo o víc než 10 s skočené razítko = o jeden snímek dál (podle posledního rozestupu nebo délky paketu) a hodiny se tam znovu ukotví. FFmpeg zapisovač přepočítá na časovou základnu muxeru (`av_rescale_q_rnd`) a po zaokrouhlení hlídá rostoucí DTS.
- Konec odběru hubem: `HubStopped` (ukončení aplikace) = `Completed`; `CameraUnavailable`, `Overflow`, `Failed` = `Failed`. Soubor se dokončí vždy, i po chybě zápisu (co bylo zapsáno, zůstane čitelné).
- Log: jen ID tiskárny a název souboru bez složky (rozhodnutí 5), adresa kamery nikdy.
- **FFmpeg** (`src/LayerPeek.Camera.FFmpeg/FFmpegRecordingWriter.cs`): `avformat_alloc_output_context2("mp4")`, `avio_open("file:" + cesta)`, `avformat_write_header` s `movflags=frag_keyframe+empty_moov` (rozhodnutí 1) a `flush_packets=1`, `av_write_frame` (paket bez počítání referencí, data jen připíchnutá přes `fixed`, bez kopie), `av_write_trailer` + `avio_closep`. **Enkodér se nikde neotevírá** (revize kódu; build pro macOS žádný enkodér nemá – `configure`: „Enabled encoders“ prázdné – a nahrávání funguje). Jen video (rozhodnutí 4).
- SPS/PPS: z `CameraStreamInfo.Extradata` (skutečná Buddy3D: `sprop-parameter-sets`, 43 B), jinak vyjmuté z prvního klíčového snímku (simulátor je posílá jen v proudu). Bez nich `StreamFailed`. Rozměr: RTSP ho nehlásí (`CameraStreamInfo` 0×0), proto ho zapisovač jednou na soubor zjistí parserem H.264 (`av_parser_parse2`, žádné dekódování). Muxer sám převede Annex B na `avcC` a délkové prefixy (ověřeno v T-147).
- **Pád procesu:** fragmentovaný MP4 má hlavičku na začátku a fragment končí každým klíčovým snímkem; `flush_packets` ho hned předá systému. Po pádu je soubor čitelný až po poslední dokončený fragment, ztratí se nejvýš jeden interval klíčových snímků (~2,3 s u Buddy3D). Ověřeno: test `AfterACrashTheFileIsReadableUpToTheLastFragment` (kopie souboru bez dokončení) a ručně `kill -9` během nahrávání ze skutečné kamery: ~20 s nahrávání, čitelných 17,0 s / 400 snímků, plné dekódování bez chyby.

**Měření (macOS arm64, 2026-11-03):**
- Skutečná Buddy3D (Core One, přes relay `rtsp://127.0.0.1:8556/live`), 30 s ustáleného stavu bez okna (jen nahrávání, Release): **1,09 Mbit/s = ~490 MB/h** (podklad pro limit v T-145); CPU celého procesu 512 ms za 30 s = **1,7 % jednoho jádra** (příjem RTSP + zápis, bez dekódování); spravované alokace celého procesu 832 B za 30 s (tj. žádné na paket).
- Simulátor (25 fps, v témže procesu i se simulátorem): 1,00 Mbit/s, 2,7 % jednoho jádra.
- Výsledné soubory: `ffprobe` = `mov,mp4,…`, H.264 Constrained Baseline 1280×720, jedna stopa; `ffprobe -count_frames` = všech 762 paketů dekódovaných. `ffmpeg -v error -i f.mp4 -f null -` hlásí u skutečné kamery „non monotonically increasing dts to muxer“ výstupu `null`: ffmpeg CLI tam přepočítá na časovou základnu podle `r_frame_rate` (139/6) a kolísající rozestupy kamery (41–76 ms) se při zaokrouhlení potkají. Soubor sám má DTS přísně rostoucí (ověřeno `ffprobe -show_packets`), s `-enc_time_base:v demux` nebo `-c copy` žádné hlášení.

**Testy:**
- Core (`tests/LayerPeek.Core.Tests/Cameras/Recording/`): `CameraRecordingTests` (zahazování před popisem a klíčovým snímkem, nový soubor s příponou po restartu, nepřepsání existujícího souboru, žádný soubor bez klíčového snímku, konce odběru, chyba zápisu / dokončení / otevření, chybějící složka, log bez složky, velikost a délka), `RecordingClockTests`, `CameraRecorderTests` (přes hub s falešným streamem: bez dekódování, ukončení aplikace, tiskárna bez kamery, nedostupný muxer, neplatné cesty), `CameraRecordingAllocationTests` (ustálený stav bez alokací).
- Camera.FFmpeg (`tests/LayerPeek.Camera.FFmpeg.Tests/`): `FFmpegRecordingTests` – fixture `buddy3d.h264` (71 snímků), soubor po „pádu“, SPS/PPS z klíčového snímku, chybějící složka, simulovaně chybějící muxer, simulátor od klíčového snímku uprostřed GOP bez dekodéru, výpadek simulátoru (`SetOffline`) → `outage.mp4` + `outage-2.mp4`, ukončení aplikace (hub). Každý soubor čte zpět `Mp4Probe` (demuxer + dekodér FFmpeg: MP4, jedna stopa H.264 1280×720, první klíčový snímek, rostoucí DTS, všechny snímky se dekódují). `FFmpegRecordingAllocationTests` (zápis bez alokací). Soubory testů jsou v rezervované dočasné složce podle worktree (`TestDirectories`, T-032: úklid na začátku i na konci, i po pádu).
- Testy dekodéru z T-014 / T-143 (bez alokací) dál prochází.
- `dotnet build` 0 upozornění, `dotnet test` celkem 8744, neúspěšné 0, úspěšné 8544, přeskočeno 200. `dotnet publish src/LayerPeek.App -c Release -r osx-arm64` bez varování trim / AOT.
- Na Windows (BtbN, muxer MP4 je v něm) se testy spustí v T-125 (bod 2d) spolu s přehráním nahrávky ve Filmech a TV / VLC; přehrání v QuickTime na Macu v T-127 (z T-147).
- Uzavřeno 2026-11-03: zbývající ověření na Windows (testy, AOT publish `win-x64`, přehrání ve Filmech a TV / VLC) přesunuto do T-125.

## Review
Revize 2026-11-03 (qa-reviewer), bez blokujících nálezů. Opraveno při revizi:
- `CameraRecording.Start`: `Completion` se nastaví dřív, než úloha nahrávky poběží (dřív `Task.Run` a přiřazení až potom – zpětné volání `changed` z prvních kroků, např. u hned ukončeného odběru, mohlo vidět `Task.CompletedTask`). Test `AnObserverOfAnAtOnceEndedRecordingSeesItsRealCompletion`.
- `CameraRecording.IsDiskFull`: kódy podle OS (Windows `ERROR_DISK_FULL` / `ERROR_HANDLE_DISK_FULL`, Unix ENOSPC = 28), dřív se kódy Windows porovnávaly i s errno na Unixu. Test `DiskFullIsRecognizedByThePlatformsErrorCode`. Ověřeno na plném RAM disku (macOS): .NET `IOException.HResult` = 28, FFmpeg zapisovač `DiskFull` při zápisu i dokončení, nativní objekty uvolněné, soubor čitelný až po přerušený fragment.
- Nový test `EveryNativeObjectIsFreedWithOrWithoutCompleting` (`HoldsNativeResources` po `Complete` + `Dispose` i po `Dispose` bez `Complete`, opakované volání, zápis po uzavření).
- Hlášení „non monotonically increasing dts“ u `ffmpeg -f null`: potvrzeno, že jde o časovou základnu ffmpeg CLI (1/`r_frame_rate`), ne o soubor – stejné hlášení dává i referenční `-c copy` záznam téže kamery (`coreone-20261103-1905-60s.mp4`, 21×), s `-enc_time_base:v demux` nic; náš soubor má stejná metadata (`time_base` 1/90000, žádné vlastní `avg_frame_rate` / `r_frame_rate`, ty počítá ffprobe z paketů) a přísně rostoucí DTS.

