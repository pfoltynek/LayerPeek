---
id: T-184
title: Kamera – převod obrazu rovnou ve velikosti, ve které se zobrazuje
status: todo
owner: core-dev + ui-dev
priority: P2
platform: all
depends_on: []
---

## Cíl
Výkonnostní analýza (2026-11-06): převod YUV → BGRA běží vždy v plném rozlišení zdroje
(`FFmpegDecoder.Configure`, `sws_getCachedContext` má stejnou cílovou velikost jako zdroj,
[FFmpegDecoder.cs](../../src/LayerPeek.Camera.FFmpeg/FFmpegDecoder.cs)). Každý snímek se pak ještě dvakrát
kopíruje, z toho jednou na UI vlákně (`BgraPixels.CopyTo` do `WriteableBitmap`). U 720p a 22 fps to dělá zhruba
250 MB/s paměťového provozu na jedno okno a malý náhled stojí stejně jako celá obrazovka. Cílem je nechat swscale
zmenšit obraz na velikost, ve které se opravdu vykresluje, a odstranit zbytečnou kopii.

## Rozsah
- Čtenář streamu (okno kamery) sdělí přes `CameraStreamDemand` (nebo podobnou cestou v `CameraStreamHub`) požadovanou
  maximální velikost snímku v pixelech zařízení (velikost obrazu × škálování monitoru).
- `FFmpegDecoder` předá cílovou velikost do `sws_getCachedContext` (zachová poměr stran, nikdy nezvětšuje)
  a při změně velikosti okna nebo režimu (malý náhled ↔ normální okno ↔ celá obrazovka) kontext přenastaví.
- Hub může mít víc čtenářů (okno, detekce T-153, timelapse T-158 / T-165). Snímky pro detekci a timelapse musí zůstat
  v rozlišení, které potřebují. Zmenšuje se jen proud snímků pro okno.
- Odstranit jednu ze dvou kopií snímku (ideálně zapisovat převod přímo do bufferu, který jde do bitmapy, bez kopie
  na UI vlákně), pokud to jde bez porušení pravidel vláken v Avalonii.
- Změřit stav před a po (CPU, alokace a paměťový provoz, fps) na simulátoru v 720p a zapsat do poznámek.

## Akceptační kritéria
- [ ] V malém náhledu a v malém okně se převádí jen na zobrazenou velikost (test dekodéru: výstupní `CameraFrame` má
      požadovanou velikost, poměr stran sedí, menší zdroj se nezvětšuje).
- [ ] Změna velikosti okna nebo režimu přenastaví převod bez přerušení streamu.
- [ ] Detekce chyb tisku a timelapse dostávají snímky ve stejném rozlišení jako dosud (testy hubu).
- [ ] Kvalita obrazu na celé obrazovce se viditelně nezhorší (ruční kontrola → T-125 / T-127).
- [ ] Naměřené hodnoty před a po jsou v `## Poznámky k implementaci`.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. Filtr zmenšení: ponechat `SWS_BILINEAR`, nebo pro výrazné zmenšení (malý náhled) použít `SWS_AREA`
   (ostřejší, o něco dražší)?
2. Má se zmenšení týkat i všeho, co se z okna kamery ukládá nebo exportuje, nebo má takový export vždy plné rozlišení?

## Poznámky k implementaci
(vyplní agent po dokončení)
