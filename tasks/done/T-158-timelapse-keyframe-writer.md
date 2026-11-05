---
id: T-158
title: Timelapse – zápis z klíčových snímků bez překódování (Core + Camera.FFmpeg)
status: done
owner: core-dev
priority: P3
platform: all
depends_on: [T-149, T-144, T-148]
---

> **Návrh ze spiku T-149 (2026-11-03), schválený uživatelem 2026-11-04** (varianta D, výstup pevně 30 fps). Implementuje
> se až po dashcamu T-148 (rozhodnutí uživatele, otázka 10 v T-149). Podrobnosti a měření: `docs/timelapse-spike.md`.

## Cíl
Timelapse jako MP4 s H.264 bez enkodéru: z odběru paketů hubu (T-143) se vybírají jen klíčové snímky (IDR) kamery,
dostanou nová časová razítka (snímek *n* = *n* × délka snímku výstupu) a přebalí se stávajícím zapisovačem z T-144.
Žádné dekódování, žádný enkodér, žádná nová závislost, žádná změna buildu FFmpeg.

## Rozsah
- **Core (`Core/Cameras/Timelapse/`, názvy volí core-dev):**
  - Rekordér timelapse po vzoru `CameraRecorder` / `CameraRecording`: vlastní `SubscribePackets(printerId)`, čtení na
    thread poolu, stav (`Starting`, `Recording`, `Completed`, `Failed`), `Failure`, `Files`, `BytesWritten`,
    `FrameCount`, `Completion`, `StopAsync()`. Nic za běhu nevyhazuje (stejná pravidla jako T-144).
  - Výběr snímku: vstupem je „požadavek na snímek“ (spouštěč volá metodu typu `RequestFrame()`; spouštěč po čase
    a po vrstvě jsou mimo tento úkol – čas zde jen jako jednoduchý interní časovač nebo test). Vezme se **první klíčový
    snímek po požadavku**; víc požadavků před dalším klíčovým snímkem = jeden snímek. Ostatní pakety se zahodí hned
    (žádná kopie navíc).
  - Hodiny timelapse: v časové základně streamu, PTS = DTS = *n* × (časová základna / fps výstupu), délka = jeden
    snímek. Snímková frekvence výstupu je pevně **30 fps** (rozhodnutí 2026-11-04, otázka 4 v T-149).
  - Nové spojení (`StreamStarted`): když jsou SPS/PPS (`Extradata`) a rozměr stejné, pokračuje stejný soubor; jinak
    se soubor dokončí a další začne s příponou `-2`, `-3`… (jako T-144).
  - Volitelně (rozhodne core-dev podle měření): volba odběru paketů „jen klíčové snímky“ v `CameraPacketSubscriptionOptions`,
    aby se do fronty timelapse nekopírovaly P-snímky (~23 za s).
- **Camera.FFmpeg:** `ICameraRecordingWriter` se použije beze změny (bere `pts` / `dts` / `duration` od volajícího).
  Fragmentovaný MP4 (`frag_keyframe`) tu znamená fragment na každý snímek: ověřit režii (prototyp: +135 B na snímek).
- Bez UI, bez služby (kdy, kam, rotace = T-160).

## Akceptační kritéria
- [x] Z fixture `buddy3d.h264` a ze simulátoru vznikne MP4 jen z klíčových snímků; `Mp4Probe` (T-144): jedna stopa
      H.264, všechny snímky klíčové a dekódovatelné, DTS přísně roste, délka = počet snímků / fps.
- [x] Test, že dekódovaný snímek timelapse je bitově shodný s týmž klíčovým snímkem dekódovaným z celého streamu
      (spike: shoda `framemd5` u všech 26 klíčových snímků skutečné Buddy3D).
- [x] Test: dva po sobě jdoucí vybrané IDR se stejným `idr_pic_id` (Buddy3D cyklí 0–15) se přehrají bez chyby
      (FFmpeg; přehrání v QuickTime / Filmech a TV ručně → T-127 / T-125). Ruční přehrání → přesunuto do T-125 a T-127
- [x] Odběr bez dekódování (počítadlo dekódovaných snímků = 0), ustálený stav bez alokací na paket.
- [x] Výpadek spojení: stejné SPS/PPS = jeden soubor, jiné = nový soubor s příponou.
- [x] Pád procesu: soubor čitelný až po předposlední snímek před pádem (test kopie nedokončeného souboru jako v T-144). *Uživatel 2026-11-04 přijal, že poslední snímek (1/30 s videa) se při pádu ztratí; zůstává ve svém fragmentu, dokud nezačne další.*
- [x] `dotnet build` a `dotnet test` bez chyb a varování; `dotnet publish src/LayerPeek.App -c Release -r osx-arm64`
      (nebo `win-x64`) bez varování trim / AOT. Část Windows (testy s BtbN, `win-x64`) → přesunuto do T-125

## Otevřené otázky
- (žádné – otázky 1 a 4 v T-149 rozhodnuté 2026-11-04)

## Poznámky k implementaci
Implementováno 2026-11-04 na macOS arm64 (core-dev), větev `feature/T-158-timelapse-keyframe-writer`. Žádný nový balíček,
žádná změna buildu FFmpeg ani zapisovače `FFmpegRecordingWriter`.

**Rozhraní (Core, `src/LayerPeek.Core/Cameras/Timelapse/`):**
- `ICameraTimelapseRecorder` (`CameraTimelapseRecorder` nad `CameraStreamHub` a `ICameraRecordingWriterFactory`, samostatně
  vedle `ICameraRecorder`, takže falešné rekordéry služby nahrávání se nemění): `Availability`, `Start(printerId, filePath,
  changed)` → `ICameraTimelapse`, konstanta `FramesPerSecond = 30`. Výjimka jen pro chybné argumenty (jako T-144), bez muxeru
  vrátí timelapse hned `Failed` / `Unavailable`, bez kamery skončí `Failed` / `CameraUnavailable`.
- `ICameraTimelapse`: `PrinterId`, `State` a `Failure` (stejné výčty jako nahrávka: `CameraRecordingState`
  `Starting` → `Recording` → `Completed` / `Failed`, `CameraRecordingFailure`), `FailureMessage`, `Files`, `BytesWritten`,
  `FrameCount`, `Duration` (= snímky / 30), `Completion` (nikdy nespadne), `StopAsync()` / `DisposeAsync()` (idempotentní,
  dokončí soubor) a `RequestFrame()` (libovolné vlákno, jen nastaví příznak; `false`, když timelapse skončil). `changed` při
  změně stavu nebo nového souboru, ne při každém snímku. Spouštěč (časovač) není součástí – přidá ho služba T-160.
- Registrace v `AppServices` zatím není (nikdo ho nepoužívá); přidá ji T-160 spolu se službou (App není v rozsahu úkolu).

**Chování:**
- Vlastní odběr paketů sdíleného spojení (`SubscribePackets`), čtení na thread poolu jako u nahrávky; hub nikdy nečeká na disk.
- Výběr snímku: po `RequestFrame()` se zapíše první klíčový snímek, který čtenář vezme; víc požadavků před ním = jeden snímek.
  Klíčové snímky bez požadavku a pakety před prvním `StreamStarted` se zahodí. Požadavek před připojením kamery počká na její
  první klíčový snímek.
- **Volba odběru `CameraPacketSubscriptionOptions.KeyFramesOnly`** (rozhodnutí podle měření): P-snímky hub vůbec nekopíruje
  a čtenáře kvůli nim nebudí (změna jen v `CameraStreamSubscription.cs`, hub beze změny). Fronta timelapse má 16 položek
  (~35 s klíčových snímků Buddy3D); disk, který se zasekne déle, ukončí timelapse `TooSlow` a nedrží víc paměti.
- **Hodiny:** `RecordingFiles` (sdílené s nahrávkou a dashcamem) dostal volitelnou pevnou snímkovou frekvenci a
  `RecordingClock.AtFrameRate(timeBase, fps)`: snímek *n* má PTS = DTS = *n* / 30 s v časové základně streamu (zaokrouhleno na
  nejbližší tik, bez driftu; 90 kHz → přesně 3000 tiků), délka jeden snímek; příliš hrubá časová základna dál dává přísně
  rostoucí razítka. Razítka kamery se ignorují. Bez pevné frekvence se chování nahrávky a dashcamu nemění (všechny testy
  T-144 / T-145 / T-148 beze změny prochází).
- **Nové spojení:** rozhoduje se u prvního zapsaného klíčového snímku nového spojení. Stejný kodek, rozměr, `Extradata`
  a SPS/PPS v proudu (`H264ParameterSets`: NAL 7 a 8 z Annex B, bez dekódování; alokace jen jednou na soubor / spojení)
  = pokračuje stejný soubor a číslování snímků; jinak se soubor dokončí a další začne s příponou `-2`, `-3`… (název
  a rezervace `CreateNew` z `RecordingFiles`). Časová základna nového spojení na razítka souboru nemá vliv (počítají se
  z čísla snímku v základně souboru).
- Konec odběru jako u nahrávky (`HubStopped` = `Completed`; `CameraUnavailable`, `Overflow` → `TooSlow`, `Failed` →
  `StreamFailed`), chyba zápisu = `Failed` s `WriteFailed` / `DiskFull`; soubor se vždy dokončí. Log jen s ID tiskárny
  a názvem souboru.
- **Fragmentovaný MP4:** `frag_keyframe` = fragment na každý snímek; po pádu je soubor čitelný až po předposlední snímek
  (poslední čeká ve fragmentu na další snímek). Režie kontejneru ~170 B na snímek (skutečný klip: 1 714 912 B souboru,
  1 710 622 B H.264 v 25 snímcích), tj. ~0,25 % – za 10 h po 30 s ~0,2 MB navíc k ~78 MB.

**Měření (macOS arm64, 2026-11-04):**
- Skutečná Buddy3D: klip `coreone-20261103-1905-60s.mp4` (Annex B) přehrávaný simulátorem v reálném čase, požadavek každých
  100 ms po 50 s → 25 snímků (klip má 26 klíčových snímků, simulátor hraje 25 fps, za 50 s jich přišlo 25), všech 25
  **bitově shodných** (SHA-256 pixelů Y/U/V) s klíčovými snímky dekódovanými z celého streamu, přísné dekódování FFmpeg
  (`-err_detect explode -xerror`) bez chyby, AVFoundation (`AVAssetReader`, dekodér QuickTime) 25/25, délka 0,833 s, DTS
  0, 3000, 6000… Soubory pro ruční přehrání: `~/LayerPeek-samples/timelapse/` (mimo repozitář).
- Cena volby `KeyFramesOnly` (simulátor v témže procesu, 25 fps, 20 s): 10 položek místo 500 do fronty a o ~480 méně
  pracovních položek thread poolu (1 084 vs. 1 565 za 20 s, tj. ~24 probuzení za sekundu méně); rozdíl CPU procesu je pod
  šumem simulátoru (142–288 ms vs. 166 ms za 20 s). Alokace čtenáře 0 B (Core test), alokuje jen simulátor.
- Paměť: fronta 16 položek, z poolu odběru se v ustáleném stavu používá 1–2 buffery (IDR ~65 kB → buffer 64 / 128 KiB).

**Testy:**
- Core (`tests/LayerPeek.Core.Tests/Cameras/Timelapse/`): `CameraTimelapseTests` (výběr prvního klíčového snímku po
  požadavku, víc požadavků = jeden snímek, razítka *n* × 3000 / délka 3000, požadavek před připojením, staré spojení,
  pokračování po novém spojení se stejnými parametry, rozhodnutí podle SPS/PPS v proudu, jiné `Extradata` / rozměr → `-2`,
  konce odběru, chyba zápisu, stop a odmítnutý požadavek, padající pozorovatel, `H264ParameterSets`),
  `CameraTimelapseRecorderTests` (přes hub: sdílené spojení s oknem kamery, bez dekódování – `DecodedFrames = 0`, odběr
  `KeyFramesOnly` nepřeteče P-snímky, tiskárna bez kamery, bez muxeru, neplatné cesty), `CameraTimelapseAllocationTests`
  (ustálený stav 0 B); `RecordingClockTests` +4 (pevná frekvence, zaokrouhlení, hrubá časová základna, neplatná frekvence).
- Camera.FFmpeg (`FFmpegTimelapseTests`): fixture přes hub (každé spojení přehraje fixture jednou, hub se znovu připojí →
  jeden soubor), `Mp4Probe` v přísném režimu (jedna stopa H.264 1280×720, všechny snímky klíčové a dekódované, DTS roste,
  délka = snímky / 30), snímky = klíčové snímky fixture, režie < 1 kB na snímek; oba klíčové snímky fixture dvakrát po sobě
  (`idr_pic_id` 7, 7, 8, 8, 7 – ověřeno `trace_headers`) bitově shodné s plným dekódováním; simulátor se souběžnou nahrávkou
  na stejném spojení, bez dekodéru, kopie nedokončeného souboru = všechny snímky kromě posledního; výpadek simulátoru se
  stejným streamem = jeden soubor. `Mp4Probe` rozšířený o přísné dekódování, součet bajtů paketů a otisky snímků.
- `dotnet build` 0 upozornění; `dotnet test` celkem 9126, neúspěšné 0, úspěšné 8926, přeskočeno 200 (macOS arm64); Core
  a FFmpeg testy timelapse opakovaně (5× / 3×) bez selhání. `dotnet publish src/LayerPeek.App -c Release -r osx-arm64` bez
  varování trim / AOT (jen dosavadní hlášky linkeru / dsymutil).

Uzavřeno 2026-11-04: zbývající ruční ověření (přehrání v QuickTime / Filmech a TV) a část Windows (testy s BtbN, AOT
`win-x64`) přesunuty do T-125 a T-127 (rozhodnutí uživatele).
