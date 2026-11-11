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
- V režimu malého náhledu dostane okno nejvýš N snímků za sekundu (N z nastavení, viz rozhodnutí 3–5). H.264 se dál dekóduje celé (P-snímky závisí
  na předchozích). Vynechává se až převod, kopie a předání na UI vlákno.
- Omezení platí jen pro proud snímků pro okno. Detekce, nahrávání a timelapse se nemění.
- Po přepnutí zpět do normálního okna nebo na celou obrazovku se hned vrátí plná frekvence.
- Změřit CPU v malém náhledu před a po a zapsat do poznámek.

## Akceptační kritéria
- [ ] V malém náhledu dostává okno nejvýš N snímků za sekundu (test s falešným `TimeProvider`).
- [ ] Normální okno a celá obrazovka mají plnou frekvenci a přepnutí režimu se projeví okamžitě.
- [ ] Nahrávání, detekce a timelapse nejsou ovlivněné (testy hubu).
- [ ] Nastavení > Kamera má profily Úsporný / Vyvážený / Plynulý / Vlastní a posuvník 1–30 fps (výchozí Vyvážený,
      10 fps); hodnota se ukládá do nastavení a změna platí hned i pro otevřený malý náhled.
- [ ] Texty ve všech `Strings.*.resx`, popis ve všech `manuals/<lang>/manual.md`, rozvržení s dlouhými překlady.
- [ ] Naměřené hodnoty jsou v `## Poznámky k implementaci`.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
Rozhodnuto uživatelem 2026-11-10:
1. Malý náhled: **10 fps**. Uživatel zvažuje, že by hodnota mohla být nastavitelná v aplikaci; to chce probrat
   před implementací (zatím pevná hodnota, nastavení případně samostatně).
2. Normální okno bez fokusu: **beze změny** (plná frekvence).

Rozhodnuto uživatelem 2026-11-11 (nastavitelnost, odblokováno):
3. Frekvence malého náhledu **je nastavitelná** posuvníkem, jen pro malý náhled (normální okno a celá obrazovka mají
   dál plnou frekvenci zdroje).
4. **Doporučené profily** Úsporný / Vyvážený / Plynulý = 5 / 10 fps / plná frekvence zdroje; výchozí **Vyvážený (10 fps)**.
   Ke každému profilu krátký popis dopadu na CPU a paměť. Posuvník 1–30 fps; ruční posun mimo hodnotu profilu přepne
   výběr na „Vlastní“. „Plynulý“ = bez omezení.
5. Umístění: **Nastavení > Kamera**, jedno globální nastavení pro všechny kamery. Změna se projeví hned (i u otevřeného
   malého náhledu), nové texty do všech `Strings.*.resx` a do všech manuálů.


## Poznámky k implementaci
(vyplní agent po dokončení)
