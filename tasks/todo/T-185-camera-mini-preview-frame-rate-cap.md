---
id: T-185
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
Rozhodnuto uživatelem 2026-11-10:
1. Malý náhled: **10 fps**. Uživatel zvažuje, že by hodnota mohla být nastavitelná v aplikaci; to chce probrat
   před implementací (zatím pevná hodnota, nastavení případně samostatně).
2. Normální okno bez fokusu: **beze změny** (plná frekvence).

Zbývá: před implementací s uživatelem probrat, jestli má být frekvence malého náhledu nastavitelná (blokuje).


## Poznámky k implementaci
(vyplní agent po dokončení)
