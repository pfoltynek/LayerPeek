---
id: T-172
title: Kamera – omezení snímkové frekvence v malém náhledu
status: todo
owner: core-dev + ui-dev
priority: P2
platform: all
depends_on: []
---

## Cíl
Výkonnostní analýza (2026-11-06): malý náhled kamery (T-089) zpracovává plnou snímkovou frekvenci zdroje (např. 22 fps),
přestože drobnému obrázku v rohu obrazovky stačí 5–10 fps. Omezení ušetří u většiny snímků převod do BGRA, kopie
a vykreslení na UI vlákně.

## Rozsah
- V režimu malého náhledu dostane okno nejvýš N snímků za sekundu. H.264 se dál dekóduje celé (P-snímky závisí
  na předchozích). Vynechává se až převod, kopie a předání na UI vlákno.
- Omezení platí jen pro proud snímků pro okno. Detekce, nahrávání a timelapse se nemění.
- Po přepnutí zpět do normálního okna nebo na celou obrazovku se hned vrátí plná frekvence.
- Změřit CPU v malém náhledu před a po a zapsat do poznámek.

## Akceptační kritéria
- [ ] V malém náhledu dostává okno nejvýš N snímků za sekundu (test s falešným `TimeProvider`).
- [ ] Normální okno a celá obrazovka mají plnou frekvenci a přepnutí režimu se projeví okamžitě.
- [ ] Nahrávání, detekce a timelapse nejsou ovlivněné (testy hubu).
- [ ] Naměřené hodnoty jsou v `## Poznámky k implementaci`.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. Kolik snímků za sekundu v malém náhledu: 5, 10, nebo nastavitelná hodnota v nastavení kamery?
2. Omezit frekvenci i u normálního okna bez fokusu (např. na 10–15 fps), nebo jen v malém náhledu?

## Poznámky k implementaci
(vyplní agent po dokončení)
