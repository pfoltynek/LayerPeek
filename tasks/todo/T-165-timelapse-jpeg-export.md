---
id: T-165
title: Timelapse – export sekvence snímků JPEG
status: todo
owner: core-dev + ui-dev
priority: P3
platform: all
depends_on: [T-160, T-161]
---

> **Ze spiku T-149, rozhodnutí uživatele 2026-11-04 (otázka 9):** export jednotlivých snímků timelapse jako JPEG
> (např. pro vlastní střih) je samostatný úkol s nízkou prioritou. Podrobnosti: `docs/timelapse-spike.md`, varianty A1 / A2.

## Cíl
Kromě MP4 (T-158, bez dekódování) může uživatel získat snímky timelapse jako sekvenci souborů JPEG. Na rozdíl
od MP4 to potřebuje dekódování a kódování JPEG. Funkce patří k timelapse, a je proto označená jako experimentální.

## Rozsah
- **Kodér JPEG:** podle spiku bez nové závislosti – Avalonia (`Bitmap.Save` + `JpegBitmapEncoderOptions`, Skia +
  libjpeg-turbo, už v AOT publish; varianta A1) v App za rozhraním v Core, nebo FFmpeg `mjpeg` (varianta A2, na macOS
  +232 kB v `build-ffmpeg.sh`). Výběr core-dev zdůvodní v poznámkách (licence a AOT jsou u obou ověřené ve spiku).
- **Zdroj snímků** podle otázky 1: dekódování klíčových snímků z hotového timelapse MP4 (jen při exportu, nic za tisku
  navíc), nebo ukládání JPEG během tisku (dekódování po celý tisk, dnes ~0,15 % jádra jen klíčové snímky + kopie BGRA).
- **Výstup:** podsložka vedle timelapse (např. `…_timelapse_jpg/0001.jpg`), počítá se do limitu a rotace nahrávek
  (T-160). Kvalita JPEG (návrh ~85).
- **UI:** spuštění exportu (otázka 1), průběh / zrušení, notifikace, štítek „Experimentální“ (T-161), texty ve všech
  `Strings.*.resx`.

## Akceptační kritéria
- [ ] Export z timelapse (fixture / simulátor) vytvoří tolik JPEG, kolik má timelapse snímků, ve správném pořadí
      a rozměru (test).
- [ ] Ustálený stav bez alokací na snímek tam, kde to kodér dovolí; zrušení nezanechá napůl zapsaný soubor (test).
- [ ] Složka exportu se počítá do limitu a rotace (test).
- [ ] UI akce, štítek „Experimentální“, texty ve všech jazycích (headless UI test).
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish bez varování trim / AOT.

## Otevřené otázky
1. **Kdy se snímky JPEG vytváří:** dodatečně exportem z hotového timelapse MP4 (návrh architekta: nic navíc za tisku),
   nebo průběžně během tisku jako volba vedle MP4?
2. **Kde se export spouští** (tray menu, okno kamery, nastavení)?

## Poznámky k implementaci
(vyplní agent po dokončení)
