---
id: T-207
title: Kamera – dekodér si pamatuje poslední SPS/PPS pro nově otevřený kontext kodeku
status: done
owner: core-dev
priority: P3
platform: all
depends_on: [T-205]
---

## Cíl
Navazuje na T-205 (otázka 1, rozhodnutí uživatele 2026-11-13). Dekodér od T-203 otevírá nový kontext kodeku na klíčovém
snímku při přepnutí volby „Hardwarové dekódování“, při návratu k softwaru a po skončení hardwarové relace. Nový kontext
dostane SPS a PPS jen z `sprop-parameter-sets` v SDP (extradata) nebo z proudu před IDR snímkem. Kamera, která je pošle
jen jednou na začátku spojení a nemá je v SDP, by po přepnutí ukazovala stojící obraz až do nového spojení. Buddy3D
posílá obojí, takže se jí to netýká – jde o pojistku pro jiné kamery.

## Rozsah
- `FFmpegDecoder` si pamatuje poslední SPS a PPS viděné v paketech (Annex B, NAL typ 7 a 8) a předá je novému kontextu
  kodeku jako extradata, pokud je SDP neobsahuje.
- Výkon: kontrola NAL typu jen u klíčových snímků / paketů s parametry, bez alokací na každý snímek (uložené pole se
  přepíše jen při změně obsahu); žádná změna u Buddy3D.
- Bezpečnost: limit velikosti uložených parametrů (data z nedůvěryhodné sítě, T-050).

## Akceptační kritéria
- [x] Proud s SPS/PPS jen na začátku spojení a bez `sprop-parameter-sets`: po přepnutí volby i po návratu k softwaru
      obraz pokračuje od dalšího klíčového snímku (test se simulátorem nebo syntetickým proudem; ověřit, že bez opravy
      selže).
- [x] Buddy3D / simulátor s SPS/PPS před každým IDR beze změny (stávající testy); test alokací ve snímkové cestě 0 B.
- [x] Limit velikosti uložených parametrů s testem.
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
(zatím žádné)

## Poznámky k implementaci
Hotovo 2026-11-13 na Macu (core-dev, Apple Silicon).

**Co dekodér dostává:** z RTSP jsou pakety H.264 ve formátu Annex B (depaketizér RTP + parser H.264 je skládá do
přístupových jednotek, SPS / PPS jsou v paketu IDR snímku). AVCC (s délkami) přichází jen z MP4 (`mov`), a ten má
extradata `avcC` vždy, takže se ho oprava netýká.

**Implementace**
- Nová třída `src/LayerPeek.Camera.FFmpeg/H264ParameterSetMemory.cs` (název kvůli záměně s `Core/Cameras/Timelapse/H264ParameterSets`):
  poslední SPS (NAL 7) a PPS (NAL 8) z paketů. Paket, který začíná řezem (NAL 1–5, běžný P-snímek), se neprohlíží;
  jiný jen do prvního řezu (parametry jsou před ním), takže práce na paket je O(1) / pár bajtů. Každá sada má vlastní
  pole `MaxSetSize` = 1024 B alokované při první sadě a přepsané jen při změně obsahu (`SequenceEqual`); větší sada se
  ignoruje a zůstává předchozí (T-050). Zápis jako Annex B (`00 00 00 01` SPS `00 00 00 01` PPS).
- `FFmpegDecoder`: paměť vzniká jen tehdy, když popis streamu nemá extradata (RTSP bez `sprop-parameter-sets`, surový
  `.h264` bez probe extradata); u Buddy3D (`sprop-parameter-sets`) a MP4 je `null` a nic se neprohlíží. Každý video paket
  se po `av_read_frame` předá paměti (i bez dekódování, aby nový dekodér měl parametry, i když se okno otevře později).
  `OpenDecoder` po `avcodec_parameters_to_context` dá kontextu bez extradata zapamatované sady (`av_mallocz` s
  `AV_INPUT_BUFFER_PADDING_SIZE`, uvolní je `avcodec_free_context`); dekodér H.264 extradata v Annex B přijímá. Jedna malá
  nativní alokace na otevření kontextu, nic na snímek. Platí pro všechny nové kontexty: přepnutí volby, návrat
  k softwaru po chybě hardwaru, softwarový kontext klíčových snímků detekce po skončení hardwarové relace.
- Simulátor: `H264Clip.Parse(annexB, repeatParameterSets: false)` jen pro testy (bez volby příkazové řádky), snímky
  zůstanou jak jsou nahrané. Výchozí chování (SPS / PPS před každým IDR, T-205) beze změny.

**Testy**
- `ParameterSetsOnceTests` (5): simulátor s fixturou, kde SPS / PPS má jen první snímek, bez `sprop`:
  na všech OS přepnutí volby s nedostupným zařízením (VA-API, nový softwarový kontext na 2. klíčovém snímku);
  s VideoToolboxem hardware → software, software → hardware, chyba hardwaru → software od dalšího klíčového snímku,
  klíčový snímek detekce po zavření okna. **Bez opravy selžou všech 5** (ověřeno vypnutím volání: 50 ze 71 snímků,
  3 místo 24, 0 místo 1 klíčového snímku), s opravou projdou.
- `H264ParameterSetMemoryTests` (7): AUD / SEI / tříbajtové start kódy / koncové nuly, PPS v samostatném paketu,
  prohlížení jen do prvního řezu, nepřepisování stejných sad (`Changes`), limit 1024 B (přesně na limitu se vezme,
  o bajt víc ne), poškozená a AVCC data, 0 B alokací při opakovaných sadách.
- `RtspCameraTests.WithoutRepeatingTheParameterSetsTheFramesStayAsRecorded` (simulátor).
- Stávající testy beze změny zelené (včetně `HardwareDecodingVideoToolboxTests`, simulátor se SPS / PPS před každým IDR).
  Alokační testy `FFmpegCameraFeedAllocationTests` čtou `buddy3d.h264`, který na macOS nemá extradata (build bez BSF
  `extract_extradata`), takže měří i cestu s pamětí parametrů včetně klíčového snímku 50: 0 B.
- `dotnet build` 0 varování; `dotnet test` 10 529 testů, 0 selhání (267 přeskočených jako dřív); kamerové testy
  i s `LAYERPEEK_TEST_CAMERA_HARDWARE_DECODING=off` 208/208.

**Dokumentace:** `docs/architecture.md` (Hardwarové dekódování kamery), `docs/hw-decoding.md` (T-205, přepnutí za běhu),
`docs/simulator.md` (klip MP4).

## Poznámky k AOT a licencím
- Bez nového balíčku; jen spany a `av_mallocz` z FFmpeg.AutoGen, žádná reflexe.
