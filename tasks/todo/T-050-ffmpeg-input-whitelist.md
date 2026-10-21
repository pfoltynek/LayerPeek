---
id: T-050
title: Bezpečnost – FFmpeg přijímá jen RTSP/TCP a H.264 (M1)
status: todo
owner: core-dev
priority: P2
platform: all
depends_on: [T-014]
---

## Cíl
Zmenšit plochu útoku dekodéru kamery. Nález M1 z bezpečnostní revize (2026-10-21, závažnost **střední**): obsah streamu z kamery je nedůvěryhodný, protože kameru, síť nebo RTSP server může ovládat útočník. Dnes ale FFmpeg přijme jakýkoli protokol, demuxer i kodek, který jeho build obsahuje:
- `src/PrusaTrayDiagnostic.Camera.FFmpeg/FFmpegDecoder.cs:100-112`: `avformat_open_input` dostává jen `rtsp_transport`, `timeout` a `fflags`, bez whitelistů;
- `FFmpegDecoder.cs:126-146`: `av_find_best_stream` přijme libovolný video kodek a pro něj se otevře dekodér.

Chyba v kterémkoli méně používaném demuxeru nebo dekodéru LGPL buildu FFmpeg by tak byla dosažitelná ze sítě.

## Rozsah
- **Síťový režim (`rtsp:`):** do slovníku pro `avformat_open_input` přidat:
  - `protocol_whitelist` = `rtsp,tcp`. S `rtsp_transport=tcp` jde RTP prokládaně přes RTSP spojení, takže protokoly `rtp` / `udp` by neměly být potřeba. **Ověřit testem s `FakeRtspCamera`** a se skutečnou kamerou Buddy3D. Rozšířit jen o to, co je prokazatelně nutné, a důvod zapsat do kódu.
  - `format_whitelist` = `rtsp`. Ověřit, že RTSP demuxer nepotřebuje vnořený formát (např. `sdp` / `rtp`); případné rozšíření zdůvodnit.
  - `codec_whitelist` = `h264`.
- **Kontrola kodeku před otevřením dekodéru:** po `av_find_best_stream` odmítnout stream, jehož `codecpar->codec_id != AV_CODEC_ID_H264`, a dekodér brát explicitně přes `avcodec_find_decoder(AV_CODEC_ID_H264)`, ne ten, který vrátí `av_find_best_stream`. `codec_whitelist` nastavit i v options pro `avcodec_open2`. Fallback `avformat_find_stream_info` (`FFmpegDecoder.cs:132-137`) musí platit stejné omezení.
- **Srozumitelný stav:** nepodporovaný kodek nebo formát → `CameraFeedException` se stejnou hláškou jako dnes u `AVERROR_DECODER_NOT_FOUND` („The video format of the stream is not supported.“). Okno kamery i test v nastavení (T-034) ukážou existující stav „nepodporovaný formát“, ne obecnou chybu připojení. Selhání kvůli whitelistu protokolu, např. přesměrování na `http`, se hlásí jako chyba připojení.
- **Souborový režim (`file:`):** používají ho jen testy (`FFmpegCameraFeedFactory`, nastavení přijímá jen `rtsp`). Whitelist `file` + formáty, které potřebují testovací fixtures (raw H.264). Ověřit, že `file:` není dosažitelný z nastavení; pokud ano, zapsat do Poznámek.
- **Testy:** rozšířit `FakeRtspCamera` (`tests/PrusaTrayDiagnostic.Camera.FFmpeg.Tests`) o volitelné SDP s jiným kodekem (např. `H265/90000` nebo MJPEG, payload 26) a ověřit `CameraFeedException` „nepodporovaný formát“ bez dekódování. Dál test, že běžný H.264 stream funguje beze změny a se stejnou latencí (žádné `avformat_find_stream_info` navíc).
- Bez nového NuGet balíčku. Žádná změna buildu FFmpeg; zmenšení buildu (vypnutí nepotřebných demuxerů a dekodérů) je mimo rozsah.

## Akceptační kritéria
- [ ] `avformat_open_input` v síťovém režimu dostává `protocol_whitelist`, `format_whitelist` a `codec_whitelist`; dekodér je vždy H.264 a jiný kodek se odmítne před `avcodec_open2`
- [ ] Test s falešnou RTSP kamerou s jiným kodekem končí stavem „nepodporovaný formát“; test H.264 streamu prochází beze změny
- [ ] Ověřeno se skutečnou kamerou Buddy3D (`rtsp://192.168.0.227/live`), pokud je dostupná; E2E testy kamery z T-044 procházejí
- [ ] Testy po sobě nic nenechají
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish win-x64 bez varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
