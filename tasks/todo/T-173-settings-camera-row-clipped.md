---
id: T-173
title: Oříznutý text „dashcam“ na podzáložce Kamera v delších jazycích
status: todo
owner: ui-dev
priority: P2
platform: all
depends_on: []
---

## Cíl
Na podzáložce **Kamera** tiskárny jsou „Record prints automatically“ a „Keep the last minutes (dashcam)“ vedle sebe
v jednom vodorovném `StackPanel` (`SettingsWindow.axaml`, T-146/T-148). V polštině, francouzštině a španělštině se druhé
zaškrtávací pole nevejde a text se ořízne (vidět na `docs/images/{pl,fr,es}/settings-camera.png`, nalezeno při T-171).

## Rozsah
- Rozložení řádku tak, aby se text nikdy neořízl (např. `WrapPanel` nebo dva řádky), při zachování fit záložky (T-033).
- Ověřit ve všech 8 jazycích (snímky UI testů, `docs/images/*/settings-camera.png` přegenerovat).

## Akceptační kritéria
- [ ] V žádném jazyce se text na podzáložce Kamera neořízne
- [ ] Okno nastavení se vejde jako dosud (T-033)
- [ ] Snímky přegenerované a zkontrolované
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Dva řádky, nebo zalamování jen když se nevejdou? (rozhodne uživatel / ui-designer)

## Poznámky k implementaci
(vyplní agent po dokončení)
