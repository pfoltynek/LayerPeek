---
id: T-160
title: Timelapse – služba v Core (spouštěč po čase, automaticky při tisku, složka, rotace)
status: todo
owner: core-dev
priority: P3
platform: all
depends_on: [T-158, T-145]
---

> **Návrh ze spiku T-149 (2026-11-03).** Rozsah upřesní odpovědi na otázky 3–7 v T-149. Podrobnosti: `docs/timelapse-spike.md`.

## Cíl
Služba po vzoru `CameraRecordingService` (T-145) rozhoduje, kdy se timelapse pořizuje, jak často se bere snímek
a kam se soubor uloží. Používá rekordér timelapse z T-158.

## Rozsah
- **Spouštěč po čase:** jeden časovač (`TimeProvider`) jen během pořizování timelapse; při každém tiku požadavek
  na snímek rekordéru (T-158). Interval podle rozhodnutí (otázka 3 v T-149, návrh 30 s).
- **Kdy:** podle otázky 5 v T-149 (návrh: volba u tiskárny po vzoru `camera.recordPrints`, výchozí vypnuto; začátek
  s `Printing`, konec stejnými pravidly jako automatické nahrávání T-145, během `Paused` / `Attention` se nesnímá).
  Ruční start / stop jen pokud ho uživatel chce.
- **Kam:** podle otázky 6 v T-149 (návrh: kořen a podsložka tiskárny z `CameraRecordingFolders`, přípona `_timelapse`
  v názvu, rozpoznání vzoru názvu a rotace rozšířené tak, aby se timelapse počítal do limitu a smazal se jako nahrávka).
- **Souběh s nahráváním:** nahrávání i timelapse mají každý vlastní odběr paketů téhož spojení (T-143). Zastavení
  jednoho neovlivní druhé.
- **Nastavení:** nové hodnoty v `AppSettings` / `CameraSettings` (mapování v `SettingsMapper`, výchozí hodnoty
  se nezapisují, chybné hodnoty = výchozí s varováním).
- Stav pro UI (`GetStatus`, událost `Changed`, chyby přes událost jako `CameraRecordingService.Failed`), texty
  lokalizuje App (T-161).
- Nečinnost: bez tisku s timelapse žádné časovače ani úlohy.

## Akceptační kritéria
- [ ] Testy služby se všemi přechody stavu tiskárny (jako `CameraRecordingServiceTests`), spouštěč po čase přes
      `FakeTimeProvider`, souběh s nahráváním, vypnutá kamera, odebraná tiskárna, chyba zápisu, ukončení aplikace
      (soubor se dokončí).
- [ ] Rotace a limit započítávají timelapse podle rozhodnutí; cizí soubory zůstanou.
- [ ] Žádné časovače v nečinnosti (test jako `IdleCostsNoTimers`).
- [ ] Měření na skutečné kameře (relay): CPU a velikost souboru za ≥ 10 min, s nahráváním i bez něj, zapsané
      v poznámkách.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish bez varování trim / AOT.

## Otevřené otázky
- Závisí na otázkách 3, 5, 6 a 7 v T-149.

## Poznámky k implementaci
(vyplní agent po dokončení)
