---
id: T-161
title: Timelapse – UI (nastavení, tray menu, notifikace) a E2E
status: todo
owner: ui-designer + ui-dev
priority: P3
platform: all
depends_on: [T-160, T-146]
---

> **Návrh ze spiku T-149 (2026-11-03).** Rozsah upřesní odpovědi na otázky 3–6 v T-149.

## Cíl
Ovládání timelapse v aplikaci po vzoru UI nahrávání (T-146): volby v nastavení, stav v tray menu, otevření posledního
timelapse a notifikace chyb.

## Rozsah
- **ui-designer:** umístění voleb (záložka Kamera u tiskárny / globální sekce nahrávání), délka textů v překladech.
- **ui-dev:**
  - nastavení: zapnutí timelapse u tiskárny, interval (a fps výstupu, pokud bude volitelná – otázka 4 v T-149),
  - tray menu: indikace „pořizuje se timelapse“, „Přehrát poslední timelapse“, „Otevřít složku“ (sdílená s nahrávkami),
    ruční start / stop jen podle otázky 5 v T-149,
  - notifikace chyb ze služby (T-160) přes `INotificationService`, texty ve všech `Strings.*.resx`,
  - E2E / headless UI testy po vzoru T-146.
- Volbu spouštěče „po vrstvě“ přidá až T-159.

## Akceptační kritéria
- [ ] Volby se uloží a načtou, chybné hodnoty se odmítnou jako u ostatních polí nastavení (T-112).
- [ ] Tray menu ukazuje stav a otevře poslední timelapse v přehrávači systému.
- [ ] Notifikace chyb lokalizované ve všech jazycích.
- [ ] Headless UI testy a E2E prochází; snímky UI přegenerované, pokud se mění.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování.

## Otevřené otázky
- Závisí na otázkách 3–6 v T-149.

## Poznámky k implementaci
(vyplní agent po dokončení)
