---
id: T-050
title: Bezpečnost – FFmpeg přijímá jen RTSP/TCP a H.264 (M1)
status: in-progress
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
- [x] `avformat_open_input` v síťovém režimu dostává `protocol_whitelist`, `format_whitelist` a `codec_whitelist`; dekodér je vždy H.264 a jiný kodek se odmítne před `avcodec_open2`
- [x] Test s falešnou RTSP kamerou s jiným kodekem končí stavem „nepodporovaný formát“; test H.264 streamu prochází beze změny
- [ ] Ověřeno se skutečnou kamerou Buddy3D (`rtsp://192.168.0.227/live`), pokud je dostupná; E2E testy kamery z T-044 procházejí
- [x] Testy po sobě nic nenechají
- [x] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish win-x64 bez varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- **Síťový režim** (`FFmpegDecoder.Open`): `avformat_open_input` dostává `protocol_whitelist=rtsp,tcp`, `format_whitelist=rtsp` a `codec_whitelist=h264`. Ověřeno testy se simulovanou kamerou:
  - `rtp` ani `udp` nejsou potřeba (RTP jde prokládaně přes RTSP spojení, `rtsp_transport=tcp`).
  - `rtsp` v seznamu protokolů nutné je: bez něj selže přesměrování kamery na jinou `rtsp://` adresu (`Invalid argument`); běžné spojení funguje i bez něj. Důvod je zapsaný v kódu.
  - RTSP demuxer nepotřebuje vnořený formát (SDP parsuje sám), `format_whitelist=rtsp` stačí.
  - Přesměrování na `rtsps://` (protokol `tls`) skončí chybou připojení „Connecting to the camera failed: Invalid argument.“ a cíl přesměrování se vůbec nekontaktuje (test `RedirectToAnotherProtocolIsAConnectionError`). Přesměrování na `http://` FFmpeg nebere jako jiný protokol (dál mluví RTSP přes `tcp`), whitelist ho tedy neblokuje a není to potřeba.
- **Volba streamu a dekodéru:** `av_find_best_stream` se volá bez požadavku na dekodér (`decoder_ret = null`), takže vybere video stream podle typu a nesahá po dekodéru jiného kodeku. Pak se kontroluje `codecpar->codec_id == AV_CODEC_ID_H264` a dekodér se bere explicitně přes `avcodec_find_decoder(AV_CODEC_ID_H264)`. Jiný kodek (i neznámý, `AV_CODEC_ID_NONE`) i build bez H.264 dekodéru → `CameraFeedException` „The video format of the stream is not supported.“ ještě před `avcodec_alloc_context3` / `avcodec_open2`. `avcodec_open2` dostává také `codec_whitelist=h264`.
- **Fallback `avformat_find_stream_info`** se teď v síťovém režimu volá jen tehdy, když popis relace neobsahuje žádný video stream (dřív i při nenalezeném dekodéru, což u jiného kodeku vedlo k pomalému probírání streamu). Při probírání smí FFmpeg otevřít jen dekodér `h264` (`codec_whitelist` na kontextu formátu) a platí stejná kontrola kodeku.
- **Souborový režim** (jen testy): `protocol_whitelist=file`, `format_whitelist=h264`, `codec_whitelist=h264`. Soubor v jiném kontejneru skončí při otevření chybou „Opening the video failed: Invalid argument.“ (FFmpeg vrací pro formát mimo whitelist obecné `EINVAL`, které nejde spolehlivě odlišit od jiných chyb), ne hláškou „nepodporovaný formát“ – pro testovací režim to stačí. `file:` není dosažitelný z nastavení: `CameraSettings` (konstruktor i `TryParseStreamUri`, které používá okno nastavení a test kamery) přijímá jen schéma `rtsp`.
- **Simulátor (T-045):** `RtspCameraOptions.RtpMap` (výchozí `H264/90000`) mění kódování ohlášené v SDP (`a=rtpmap:96 …`); u jiného kódování se vynechá H.264 `fmtp`. Obsah streamu zůstává H.264 – klient ho má odmítnout dřív, než ho dekóduje.
- **Testy:**
  - `FFmpegCameraFeedRtspTests.StreamWithAnotherCodecIsRejectedAsUnsupported` (`H265/90000`, `JPEG/90000`, `X-UNKNOWN/90000`) – živá kamera, chyba „nepodporovaný formát“ do 5 s (před změnou kód buď dekódoval H.264 data dekodérem HEVC/MJPEG, nebo probíral stream; všechny tři případy skončily až po 20 s zrušením).
  - `RedirectToAnotherProtocolIsAConnectionError`, `RedirectToAnotherRtspAddressIsFollowed` – whitelist protokolů (`LoopbackServer` umí nově odpovídat `302` s `Location`).
  - `LiveStreamStartsWithoutABacklog` – první snímek z živé kamery přijde po nejvýše 10 odeslaných snímcích (žádné `avformat_find_stream_info` navíc); stávající `StreamsTheRecordingFromAFakeRtspCamera` prochází beze změny.
  - `RtspCameraTests.DescribeAnnouncesH264ByDefault`, `DescribeAnnouncesTheConfiguredEncoding`, `InvalidRtpMapIsRejected` – nová volba simulátoru.
  - Testy používají jen loopback a efemérní porty, vše se uvolní přes `await using`; žádné soubory nevytvářejí.
- **Neověřeno:** skutečná kamera Buddy3D (`192.168.0.227:554`) nebyla při implementaci dostupná. E2E testy kamery z T-044 v této větvi (z `develop` před sloučením T-044) nejsou, proto je zbývající kritérium nezaškrtnuté – po sloučení T-044 je potřeba je spustit a ověřit se skutečnou kamerou.
- AOT publish `win-x64` proběhl bez varování; žádný nový NuGet balíček.
