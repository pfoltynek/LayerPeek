---
id: T-164
title: Timelapse z hotové nahrávky (varianta F)
status: todo
owner: core-dev + ui-dev
priority: P3
platform: all
depends_on: [T-158, T-161]
---

> **Ze spiku T-149, rozhodnutí uživatele 2026-11-04 (otázka 8):** dodatečné vytvoření timelapse z hotové nahrávky
> je samostatný úkol s nízkou prioritou. Podrobnosti: `docs/timelapse-spike.md`, varianta F.

## Cíl
Uživatel může z hotové nahrávky tisku (T-144 / T-145) dodatečně vytvořit timelapse, i když během tisku timelapse
nepořizoval. Stejný princip jako varianta D (T-158): z MP4 nahrávky se vyberou klíčové snímky a přebalí se s novými
razítky (30 fps) bez dekódování a bez enkodéru. Funkce patří k timelapse, a je proto také označená jako experimentální.

## Rozsah
- **Camera.FFmpeg:** čtení paketů z MP4 nahrávky (demuxer `mov` už je v buildu pro macOS, BtbN ho má) za abstrakcí
  v Core (Core nezná FFmpeg). Pakety jdou do výběru klíčových snímků a zapisovače z T-158 (sdílená logika, žádná kopie).
- **Core:** výběr snímků po čase podle intervalu timelapse (T-160, výchozí 30 s) v čase nahrávky. Nahrávka rozdělená
  do souborů `-2`, `-3`… (výpadek spojení, T-144) se zpracuje jako jeden celek, pokud to jde (stejné SPS/PPS).
  Zpracování na pozadí s průběhem a zrušením (`CancellationToken`), nízká priorita, neblokuje sledování tiskáren.
  Prototyp T-149: 5 min nahrávky → 0,10 s CPU, 10 h ≈ 12 s CPU + čtení ~4,9 GB.
- **Výstup:** do stejné podsložky tiskárny jako nahrávky, název podle vzoru T-160 (`…_timelapse.mp4`), počítá se do
  společného limitu a rotace. Rotace nesmí smazat nahrávku, ze které se právě timelapse vytváří.
- **UI:** akce „Vytvořit timelapse“ u nahrávky (umístění – otázka 1), průběh, zrušení, notifikace dokončení / chyby,
  štítek „Experimentální“ (T-161), texty ve všech `Strings.*.resx`.

## Akceptační kritéria
- [ ] Z nahrávky (fixture `buddy3d.h264` přebalená do MP4 a nahrávka ze simulátoru) vznikne timelapse jen z klíčových
      snímků; `Mp4Probe`: jedna stopa H.264, všechny snímky klíčové, DTS přísně roste, 30 fps (test).
- [ ] Snímky jsou bitově shodné s klíčovými snímky nahrávky (test jako v T-158).
- [ ] Zrušení uprostřed zpracování nezanechá poškozený soubor (smaže nebo dokončí podle návrhu), rotace nesmaže
      zpracovávanou nahrávku (testy).
- [ ] Akce v UI, průběh, notifikace a štítek „Experimentální“ (headless UI test), texty ve všech jazycích.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish bez varování trim / AOT.

## Otevřené otázky
1. **Kde je akce „Vytvořit timelapse“:** v tray menu u posledních nahrávek, v okně kamery, nebo v jiném seznamu
   nahrávek (aplikace dnes seznam nahrávek nemá, otevírá jen složku)?
2. **Interval:** stejný jako u živého timelapse (nastavení T-160), nebo volba při vytvoření?

## Poznámky k implementaci
(vyplní agent po dokončení)
