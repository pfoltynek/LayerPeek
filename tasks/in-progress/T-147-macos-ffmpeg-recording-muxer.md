---
id: T-147
title: macOS – FFmpeg s muxerem pro nahrávání kamery
status: in-progress
owner: ui-dev
priority: P2
platform: macos
depends_on: [T-144, T-026]
---

## Cíl
Na macOS se FFmpeg staví ze zdrojů minimálně (`build/macos/build-ffmpeg.sh`, T-026: `--disable-everything`, jen `protocol=tcp,file`, `demuxer=rtsp,h264`, `parser=h264`, `decoder=h264`). Pro nahrávání z T-144 chybí muxer výstupního kontejneru. Úkol doplní do buildu přesně to, co nahrávání potřebuje, a licence zůstane **LGPL 3+** (žádné `--enable-gpl`, žádný enkodér).

## Rozsah
- Doplnit do `CONFIGURE_FLAGS` v `build/macos/build-ffmpeg.sh` muxer kontejneru zvoleného v T-144 (např. `--enable-muxer=mp4` a/nebo `--enable-muxer=matroska`) a co muxer případně dál potřebuje (bitstream filtry jako `extract_extradata` / `h264_mp4toannexb`, jen pokud jsou nutné – ověřit). Žádný enkodér.
- Ověřit, že `configure.log` dál hlásí „License: LGPL version 3 or later“ (kontrola ve skriptu zůstává).
- Aktualizovat dokumentaci konfiguračního řádku: komentář ve skriptu, `docs/prerequisites.md`, `tasks/done/T-026` se nemění (historie), poznámka do `docs/architecture.md` (oddíl Camera.FFmpeg).
- Cache v `build/FFmpeg.targets` je klíčovaná i obsahem skriptu, takže změna se přeloží do nové složky sama – ověřit.
- Spustit testy nahrávání z T-144 na Macu (přehrání výsledného souboru v QuickTime u MP4 / VLC u MKV).

## Akceptační kritéria
- [x] První `dotnet build` na Macu postaví FFmpeg s muxerem, licence LGPL 3+, závislosti knihoven jen `@loader_path` a systém (kontroly skriptu prochází).
- [ ] Testy nahrávání z T-144 prochází na `osx-arm64`; factory hlásí „k dispozici“. (Čeká na kód T-144; zatím prochází `FFmpegRuntimeMuxerTests`: muxer `mp4` je v načtených knihovnách.)
- [ ] Nahrávka z kamery simulátoru i ze skutečné kamery jde na Macu přehrát. (Čeká na kód T-144.)
- [x] Velikost knihoven se zvětšila jen o muxer (zapsat do poznámek před / po).
- [ ] `dotnet build` a `dotnet test` na Macu bez chyb a varování; Native AOT publish `osx-arm64` bez nových varování. (Pro změny T-147 splněno 2026-11-03, viz poznámky; zopakovat s kódem T-144.)

## Otevřené otázky
- (žádné vlastní; kontejner rozhoduje otázka 1 v T-144)

## Poznámky k implementaci
Stav 2026-11-03 (core-dev): build pro macOS je hotový, úkol zůstává v `in-progress`. Kritéria 2, 3 a 5 potřebují kód nahrávání z T-144, uzavře se spolu s T-144.

- **Příznak:** do `CONFIGURE_FLAGS` v `build/macos/build-ffmpeg.sh` přibyl jen `--enable-muxer=mp4` (kontejner podle rozhodnutí T-144: fragmentovaný MP4). Matroska ani jiný muxer, žádný enkodér, žádný bitstream filtr navíc.
- **Proč bez bitstream filtrů (ověřeno ve zdrojích FFmpeg 9.0.2, `libavformat/movenc.c`):** u H.264 s extradata ve formátu Annex B (začíná start kódem, ne `1`) převede muxer každý paket na délkově prefixované NAL jednotky sám (`ff_nal_parse_units`) a extradata zapíše jako `avcC` (`ff_isom_write_avcc` v `avc.c` převede Annex B se SPS/PPS). `mov_check_bitstream` přidává filtr jen pro AAC s ADTS (`aac_adtstoasc`) a VP9 (`vp9_superframe`). `h264_mp4toannexb` slouží opačnému směru (MP4 → Annex B) a `extract_extradata` muxer nepoužívá.
- **Co `configure` přidal sám** (`mp4_muxer_select="mov_muxer"`, `mov_muxer_select` = `iso_media iso_writer riffenc rtpenc_chain vp9_superframe_bsf aac_adtstoasc_bsf ac3_parser cbs_apv_lavf cbs_av1_lavf`): muxer `mov`, parser `ac3`, BSF `aac_adtstoasc` a `vp9_superframe`. Výpis `configure`: Enabled muxers `mov mp4`, Enabled encoders prázdné, Enabled bsfs `aac_adtstoasc vp9_superframe`. Odebrat je bez úpravy zdrojů FFmpeg nejde (pevné závislosti muxeru). Muxer `rtp` se nezapnul (`rtpenc_chain` je jen pomocná část pro hint stopy, které nahrávání nepoužívá).
- **Licence a závislosti:** `configure` hlásí „License: LGPL version 3 or later“ (kontrola ve skriptu prošla, `BUILD.txt` v nové cache má nový řádek `configure`). `otool -L`: jen `@loader_path/lib{avformat,avcodec,avutil}…` a systém (`/usr/lib/libSystem.B.dylib`, `CoreFoundation`, `CoreVideo`, `CoreMedia`), architektura `arm64` (kontroly skriptu prošly). Test `ThirdPartyLicensesTests.FFmpegMacOSBuildIsLgplOnlyWithoutHardwareDecoding` nově hlídá i to, že řádek `configure` nemá `--enable-encoder`.
- **Cache:** klíč v `build/FFmpeg.targets` obsahuje obsah skriptu, změna se přeložila sama do nové složky `osx-arm64-9c5e6ca4…` (stará `osx-arm64-a564cf37…` zůstala). První `dotnet build` po změně trval 40 s.
- **Velikosti knihoven (bajty), před → po:** `libavformat.63.dylib` 741 192 → 992 312 (+251 120, muxer `mov`/`mp4`); `libavcodec.63.dylib` 1 604 856 → 1 621 608 (+16 752, parser `ac3`, BSF `aac_adtstoasc`/`vp9_superframe`, CBS AV1/APV); `libavutil.61.dylib` 719 352 beze změny; `libswscale.10.dylib` 1 217 592 beze změny. Celkem +267 872 B (asi 262 KiB). Stejné velikosti má i publish `osx-arm64`.
- **Kód:** `FFmpegRuntime.HasMuxer(string shortName)` (internal, `src/LayerPeek.Camera.FFmpeg/FFmpegRuntime.cs`) vrací, zda načtené knihovny mají muxer s daným krátkým názvem (`av_guess_format(shortName, null, null)`; hledá se jen podle názvu, takže chybějící muxer dá `false`, nikdy jiný kontejner). Je to šev pro `IsAvailable` / důvod nedostupnosti v T-144. Testy: `tests/LayerPeek.Camera.FFmpeg.Tests/FFmpegRuntimeMuxerTests.cs` (muxer `mp4` je k dispozici, neznámý muxer chybí, prázdný název se odmítne). Na Macu běžely, nebyly přeskočené.
- **Windows:** build BtbN (`ffmpeg-n9.0.2-28-g669f5ab083-win64-lgpl-shared-9.0`) je plný LGPL build bez `--disable-everything`. Muxery MP4 i Matroska v `avformat-63.dll` ověřil architekt na Windows 2026-11-03 (viz „Výchozí stav“ v T-144). Na Macu se DLL neověřovaly. Test `FFmpegRuntimeMuxerTests.TheMp4MuxerIsAvailable` to na Windows ověří automaticky při dalším `dotnet test`.
- **Poznámka pro T-144:** s `movflags=frag_keyframe+empty_moov` zapisuje muxer `moov` už v `avformat_write_header`, takže `avcC` potřebuje extradata (SPS/PPS) hned na začátku. Když je SDP kamery nemá (`sprop-parameter-sets`), vezme je `movenc.c` z prvního paketu jen při `delay_moov`. Ověřit na Buddy3D a simulátoru v T-144.
- **Build a testy 2026-11-03 (macOS arm64):** `dotnet build` 0 upozornění, 0 chyb; `dotnet test` celkem 8693, neúspěšné 0, úspěšné 8493, přeskočeno 200 (z toho `LayerPeek.Camera.FFmpeg.Tests` +83/x0/?0). `dotnet publish src/LayerPeek.App -c Release -r osx-arm64` prošel bez varování trim/AOT. Vypsal jen varování nativního linkeru a `dsymutil` (duplicitní debug map v `libRuntime.WorkstationGC.a` runtime NativeAOT, chybějící `.pcm` v module cache clangu, `xcodebuild requires Xcode`). Ta nesouvisí s FFmpeg ani s touto změnou.
