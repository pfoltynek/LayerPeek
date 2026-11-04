---
id: T-158
title: Timelapse – zápis z klíčových snímků bez překódování (Core + Camera.FFmpeg)
status: todo
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
- [ ] Z fixture `buddy3d.h264` a ze simulátoru vznikne MP4 jen z klíčových snímků; `Mp4Probe` (T-144): jedna stopa
      H.264, všechny snímky klíčové a dekódovatelné, DTS přísně roste, délka = počet snímků / fps.
- [ ] Test, že dekódovaný snímek timelapse je bitově shodný s týmž klíčovým snímkem dekódovaným z celého streamu
      (spike: shoda `framemd5` u všech 26 klíčových snímků skutečné Buddy3D).
- [ ] Test: dva po sobě jdoucí vybrané IDR se stejným `idr_pic_id` (Buddy3D cyklí 0–15) se přehrají bez chyby
      (FFmpeg; přehrání v QuickTime / Filmech a TV ručně → T-127 / T-125).
- [ ] Odběr bez dekódování (počítadlo dekódovaných snímků = 0), ustálený stav bez alokací na paket.
- [ ] Výpadek spojení: stejné SPS/PPS = jeden soubor, jiné = nový soubor s příponou.
- [ ] Pád procesu: soubor čitelný až po poslední snímek před pádem (test kopie nedokončeného souboru jako v T-144).
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; `dotnet publish src/LayerPeek.App -c Release -r osx-arm64`
      (nebo `win-x64`) bez varování trim / AOT.

## Otevřené otázky
- (žádné – otázky 1 a 4 v T-149 rozhodnuté 2026-11-04)

## Poznámky k implementaci
(vyplní agent po dokončení)
