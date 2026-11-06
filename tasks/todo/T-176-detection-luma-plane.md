---
id: T-176
title: Detekce chyb tisku – snímek z jasové složky (Y) bez převodu do BGRA
status: todo
owner: core-dev
priority: P3
platform: all
depends_on: []
---

## Cíl
Výkonnostní analýza (2026-11-06): detekce chyb tisku (T-153) dostává klíčový snímek převedený do plného BGRA a teprve
potom ho zmenšuje na šedotón. Jasová složka Y ze zdrojového YUV už šedotónový obraz je, takže převod barev a velká
alokace jsou zbytečné. Dopad je malý (jeden snímek za několik sekund).

## Rozsah
- Rozšířit poptávku po klíčovém snímku (`CameraStreamDemand.RequestKeyFrame`) o variantu „šedotón v dané velikosti“.
  `FFmpegDecoder` pak přes swscale (`AV_PIX_FMT_GRAY8`) zmenší rovinu Y rovnou na velikost, kterou detektor potřebuje.
- Detektory dostanou šedotónový snímek. Detektor, který potřebuje barvu, dostane dál BGRA.
- Ověřit, že výsledky detektorů na testovacích snímcích jsou stejné nebo v toleranci (rozdíl BT.601 / BT.709 a rozsahu).

## Akceptační kritéria
- [ ] Detekce nepotřebuje BGRA snímek v plném rozlišení (test dekodéru nebo hubu).
- [ ] Testy detektorů procházejí beze změny prahů, případná změna je zdůvodněná v poznámkách.
- [ ] Timelapse a okno kamery dostávají dál BGRA.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
