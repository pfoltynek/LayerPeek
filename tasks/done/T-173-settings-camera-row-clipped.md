---
id: T-173
title: Oříznutý text „dashcam“ na podzáložce Kamera v delších jazycích
status: done
owner: ui-dev
priority: P2
platform: all
depends_on: []
---

> Rozhodnutí uživatele 2026-11-05: rozložení zůstane jednořádkové, v dotčených jazycích se zkrátí překlady.

## Cíl
Na podzáložce **Kamera** tiskárny jsou „Record prints automatically“ a „Keep the last minutes (dashcam)“ vedle sebe
v jednom vodorovném `StackPanel` (`SettingsWindow.axaml`, T-146/T-148). V polštině, francouzštině a španělštině se druhé
zaškrtávací pole nevejde a text se ořízne (vidět na `docs/images/{pl,fr,es}/settings-camera.png`, nalezeno při T-171).

## Rozsah
- Kratší překlady obou popisků v it, es, fr, pl tak, aby se řádek vešel; příručky těchto jazyků upravit.
- Test ořezu (`CutTexts`) rozšířit, aby takový ořez zachytil i do budoucna.
- `docs/images/*/settings-camera.png` přegenerovat.

## Akceptační kritéria
- [x] V žádném jazyce se text na podzáložce Kamera neořízne
- [x] Okno nastavení se vejde jako dosud (T-033), rozložení beze změny
- [x] Snímky přegenerované a zkontrolované
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné) – rozhodnuto: kratší překlady

## Poznámky k implementaci
- Zalomení (`WrapPanel`) bylo vyzkoušené a zamítnuté: přidaný řádek přetekl v nejplnějším stavu podzáložky o 8–10 DIP (T-033).
- Nové popisky: it „Registra ogni stampa“; es „Grabar cada impresión“ a „Conservar últimos minutos (dashcam)“;
  fr „Enregistrer chaque impression“ a „Dernières minutes (dashcam)“; pl „Nagrywaj każdy wydruk“.
- `CutTexts` (`SettingsWindowLayoutTests`) dosud kontroloval jen text proti jeho vlastní šířce a vynechával zalamované
  texty (obsah zaškrtávacích polí). Nově hlásí každý text, který končí za ořezávajícím předkem. Před opravou hlásil právě
  tyto čtyři popisky, nic jiného.
