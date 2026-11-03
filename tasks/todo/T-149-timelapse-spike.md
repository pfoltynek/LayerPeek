---
id: T-149
title: Timelapse tisku – průzkum (spike): způsob, licence, AOT
status: todo
owner: architect
priority: P3
platform: all
depends_on: [T-144]
---

## Cíl
Timelapse (zrychlené video celého tisku) na rozdíl od nahrávání z T-144 **nejde udělat bez překódování**: snímky se vybírají (např. jeden za vrstvu nebo každých N sekund) a musí se z nich složit nové video nebo sekvence obrázků. Spike zjistí, jak to udělat v souladu s GPL-3.0-only, LGPL FFmpeg a Native AOT, a výsledek předloží uživateli k rozhodnutí. Neimplementuje se produkční kód (nápad 1, fáze 2).

## Rozsah
- **Zdroj snímků:** dekódované snímky ze sdíleného spojení (T-143); spouštěč po čase, nebo po vrstvě (změna `Telemetry.AxisZ` z PrusaLink; ověřit, jak spolehlivě se Z mění při Z-hopu). PrusaLink endpoint `PUT /api/v1/job/{id}/continue` („Continues the job after a timelapse capture“) ve specifikaci PrusaLink naznačuje podporu timelapse ve firmwaru – ověřit, co znamená a zda se dá použít (zdroj: https://github.com/prusa3d/Prusa-Link-Web/blob/master/spec/openapi.yaml).
- **Varianty výstupu – porovnat (licence, AOT, velikost, kvalita, přehratelnost na Windows i macOS):**
  1. sekvence obrázků (JPEG / PNG) – kodér obrázků v .NET bez reflexe, nebo přes FFmpeg (`mjpeg` / `png` enkodér, LGPL),
  2. MJPEG v kontejneru (AVI / MKV / MOV) přes vestavěný enkodér FFmpeg `mjpeg` (LGPL) – na Windows je v buildu BtbN (ověřeno 2026-11-03: „MJPEG (Motion JPEG)“ v `avcodec-63.dll`), na macOS by se musel doplnit do `build-ffmpeg.sh`,
  3. H.264 přes OpenH264 – build BtbN obsahuje `--enable-libopenh264` (ověřeno 2026-11-03). Ověřit licenci zdrojového kódu (BSD-2) **a** patentovou licenci Cisco, která podle dostupných informací platí jen pro binárky distribuované Ciscem, ne pro staticky vestavěný build – rozhodnutí patří uživateli,
  4. platformní enkodéry OS (Media Foundation na Windows, VideoToolbox na macOS) přes vlastní interop – AOT, náročnost, dvě implementace,
  5. vestavěný enkodér FFmpeg `mpeg4` (LGPL) – kvalita / přehratelnost.
- **Prusa Connect:** poskytuje jen snímky, ne stream; klient Prusa Connect ale v projektu není (T-005 vyřazen 2026-10-29, bez veřejného API). Timelapse ze snímků Prusa Connect proto do průzkumu jen jako poznámka.
- Výstup: tabulka variant s citovanými zdroji (licence, dokumentace FFmpeg), doporučení k rozhodnutí a návrh rozpadu na implementační úkoly. Zapsat do tohoto úkolu (česky).

## Akceptační kritéria
- [ ] Porovnání variant 1–5 s ověřenými licencemi (odkazy na primární zdroje) a dopadem na AOT a na build FFmpeg na macOS.
- [ ] Ověřené chování spouštěče „po vrstvě“ na datech ze simulátoru / skutečné tiskárny (nebo zdůvodnění, proč to ověřit nejde).
- [ ] Otázky pro uživatele zapsané níže, navržené implementační úkoly (ID, owner, závislosti) zapsané v poznámkách.

## Otevřené otázky
1. **Chce uživatel timelapse vůbec**, a pokud ano, ve fázi 2 hned po dashcamu (T-148), nebo později?
2. **Výstupní formát** (sekvence obrázků / MJPEG / H.264 / platformní enkodér) – rozhodne uživatel po spiku.
3. **Spouštěč snímku:** po čase, po vrstvě, obojí jako volba?

## Poznámky k implementaci
(vyplní agent po dokončení)
