---
id: T-160
title: Timelapse – služba v Core (spouštěč po čase, automaticky i ručně, složka, rotace)
status: todo
owner: core-dev
priority: P3
platform: all
depends_on: [T-158, T-145, T-148]
---

> **Návrh ze spiku T-149 (2026-11-03), rozsah upřesněný rozhodnutím uživatele 2026-11-04** (otázky 3–7 a 10 v T-149).
> Implementuje se až po dashcamu T-148. Podrobnosti: `docs/timelapse-spike.md`.

## Cíl
Služba po vzoru `CameraRecordingService` (T-145) rozhoduje, kdy se timelapse pořizuje, jak často se bere snímek
a kam se soubor uloží. Používá rekordér timelapse z T-158.

## Rozsah
- **Spouštěč po čase:** jeden časovač (`TimeProvider`) jen během pořizování timelapse; při každém tiku požadavek
  na snímek rekordéru (T-158). Interval nastavuje uživatel, **výchozí 30 s**, platný rozsah 5–600 s (otázka 3 v T-149;
  ovládací prvek – pevné volby nebo volné číslo – vybere T-161, služba přijme libovolnou hodnotu z rozsahu).
- **Kdy (rozhodnutí 2026-11-04, otázka 5 v T-149) – obojí:**
  - **automaticky:** volba u tiskárny po vzoru `camera.recordPrints`, výchozí vypnuto; začátek s `Printing`, konec
    stejnými pravidly jako automatické nahrávání T-145,
  - **ručně:** API pro ruční start / stop u tiskárny (volá tray menu i okno kamery, T-161), po vzoru ručního nahrávání
    T-145 (ruční timelapse skončí ručním stopem nebo koncem tisku – pravidla převzít z ručního nahrávání a zapsat),
  - během `Paused` / `Attention` se nesnímá (časovač stojí, soubor zůstává otevřený), po obnovení se pokračuje.
  - Stav „pořizuje se timelapse“ u tiskárny jde z `GetStatus` / `Changed` (indikátor v okně kamery a tray menu, T-161).
- **Kam (schváleno 2026-11-04, otázka 6 v T-149):** kořen a podsložka tiskárny z `CameraRecordingFolders`, název
  `yyyy-MM-dd_HH-mm-ss[_<tisk>]_timelapse.mp4`, rozpoznání vzoru názvu a rotace rozšířené tak, aby se timelapse počítal
  do společného limitu a smazal se jako nahrávka.
- **Spojení s kamerou (schváleno 2026-11-04, otázka 7 v T-149):** RTSP zůstává otevřené po celý tisk (odběr paketů
  hubu), žádné připojování jen na snímek.
- **Souběh s nahráváním:** nahrávání i timelapse mají každý vlastní odběr paketů téhož spojení (T-143). Zastavení
  jednoho neovlivní druhé.
- **Nastavení:** nové hodnoty v `AppSettings` / `CameraSettings` (mapování v `SettingsMapper`, výchozí hodnoty
  se nezapisují, chybné hodnoty = výchozí s varováním).
- Stav pro UI (`GetStatus`, událost `Changed`, chyby přes událost jako `CameraRecordingService.Failed`), texty
  lokalizuje App (T-161).
- Nečinnost: bez tisku s timelapse žádné časovače ani úlohy.

## Akceptační kritéria
- [ ] Automatický i ruční timelapse: start, stop, konec tisku, pauza (bez snímků během `Paused` / `Attention`) – testy.
- [ ] Interval: výchozí 30 s, hodnoty mimo 5–600 s se při načtení nastavení nahradí výchozí hodnotou s varováním (test).
- [ ] Testy služby se všemi přechody stavu tiskárny (jako `CameraRecordingServiceTests`), spouštěč po čase přes
      `FakeTimeProvider`, souběh s nahráváním, vypnutá kamera, odebraná tiskárna, chyba zápisu, ukončení aplikace
      (soubor se dokončí).
- [ ] Rotace a limit započítávají timelapse podle rozhodnutí; cizí soubory zůstanou.
- [ ] Žádné časovače v nečinnosti (test jako `IdleCostsNoTimers`).
- [ ] Měření na skutečné kameře (relay): CPU a velikost souboru za ≥ 10 min, s nahráváním i bez něj, zapsané
      v poznámkách.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish bez varování trim / AOT.

## Otevřené otázky
- (žádné – otázky 3, 5, 6 a 7 v T-149 rozhodnuté 2026-11-04)

## Poznámky k implementaci
(vyplní agent po dokončení)
