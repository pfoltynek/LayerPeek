---
id: T-160
title: Timelapse – služba v Core (spouštěč po čase, automaticky i ručně, složka, rotace)
status: done
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
- [x] Automatický i ruční timelapse: start, stop, konec tisku, pauza (bez snímků během `Paused` / `Attention`) – testy.
- [x] Interval: výchozí 30 s, hodnoty mimo 5–600 s se při načtení nastavení nahradí výchozí hodnotou s varováním (test).
- [x] Testy služby se všemi přechody stavu tiskárny (jako `CameraRecordingServiceTests`), spouštěč po čase přes
      `FakeTimeProvider`, souběh s nahráváním, vypnutá kamera, odebraná tiskárna, chyba zápisu, ukončení aplikace
      (soubor se dokončí).
- [x] Rotace a limit započítávají timelapse podle rozhodnutí; cizí soubory zůstanou.
- [x] Žádné časovače v nečinnosti (test jako `IdleCostsNoTimers`).
- [x] Měření na skutečné kameře (relay): CPU a velikost souboru za ≥ 10 min, s nahráváním i bez něj, zapsané
      v poznámkách. → přesunuto do T-127 (kamera Buddy3D byla při implementaci vypnutá; změřeno na simulátoru se
      skutečným klipem Buddy3D, viz poznámky)
- [x] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish bez varování trim / AOT. (macOS ověřeno; část
      Windows → přesunuto do T-125)

## Otevřené otázky
- (žádné – otázky 3, 5, 6 a 7 v T-149 rozhodnuté 2026-11-04)
- Nebrání uzavření, předáno do T-161: interval je globální (`recording.timelapseIntervalSeconds`, jako délka dashcamu);
  má být místo toho u každé tiskárny? Výchozí bez rozhodnutí: globální.

## Poznámky k implementaci
Implementováno 2026-11-04 (core-dev, macOS arm64).

**Návrh:** timelapse je třetí „pracovník“ `CameraRecordingService` vedle nahrávky a dashcamu (nový soubor
`CameraRecordingService.Timelapse.cs`), ne samostatná služba: sdílí sledování tisku (`Printing`, minutový a offline
časovač, `JobIdentity` – žádná kopie pravidel), složky, přípravu složky a kontrolu místa (vytaženo do `PrepareToWrite`,
používá ho i nahrávka a dashcam), kontrolu disku, limit, rotaci, `StopAllAsync` a události `Changed` / `Failed`. Stav
timelapse jedné tiskárny je ve vnořené třídě `TimelapseSlot` (vlastní pracovník, `Suppressed`, `NextFile`, chyba). Rekordér
(`ICameraTimelapseRecorder`, T-158) dostane služba volitelným parametrem konstruktoru; bez něj timelapse nejsou
(`TimelapseAvailability`, `StartTimelapse` vrací `false`).

**Pravidla (zapsaná i v dokumentaci třídy):**
- Automaticky (`camera.timelapsePrints`, výchozí vypnuto): jako automatické nahrávání – začne s `Printing` / `Paused`,
  skončí 1 min po konci tisku nebo po 1 min nedostupnosti, jiná úloha (stejná identita úlohy jako nahrávka) = nový soubor.
  Stop ukončí i automatický a ten do konce tisku znovu nezačne; timelapse s chybou se v témže tisku znovu nespustí.
- Ručně `StartTimelapse` / `StopTimelapse`: jeden timelapse na tiskárnu, Start během automatického nic nepřidá. Ruční
  timelapse skončí ručním stopem, chybou, ukončením aplikace nebo **koncem tisku, který viděl** (spuštěný během tisku,
  nebo tisk začal až potom) podle stejných pravidel konce (minuta / nedostupnost / jiná úloha). Spuštěný bez tisku běží
  do stopu (jako ruční nahrávka). Spuštěný v minutě po skončeném tisku tím tiskem neskončí. Aby ruční timelapse poznal konec
  tisku, sleduje se tisk i u tiskárny bez automatických voleb, ale jen během ručního timelapse (`FollowsPrints`).
- Snímky: strategie `ICameraTimelapseTrigger` (`SetCapturing`, `OnSnapshot`, `Dispose`; volá se pod zámkem služby).
  T-160 má `CameraTimelapseTimeTrigger`: snímek hned při začátku a po obnovení, pak každý interval; jeden `ITimer`
  na běžící timelapse, při `Paused` / `Attention` jen zastavený (`Change(Infinite)`), tik jen nastaví příznak
  `RequestFrame` (test: 0 B na tik). Spouštěč vybírá jediné místo `StartTrigger` – tam se přidá spouštěč po vrstvě T-159,
  který dostane snímky tiskárny přes `OnSnapshot`. Během nedostupnosti (Offline v toleranci) a minuty po tisku se snímá dál
  (kamera na API tiskárny nezávisí; konec stejně jako nahrávka). Změna intervalu v nastavení vymění spouštěč běžícího
  timelapse (jeden snímek hned, pak nový interval).
- Soubory: `CameraRecordingFolders.TimelapseFileName` → `yyyy-MM-dd_HH-mm-ss[_<tisk>]_timelapse.mp4` (s `-2` … od
  rekordéru), `IsTimelapseFileName`. Vzor nahrávky splňuje, takže rotace a limit ho počítají a mažou jako nahrávku (cizí
  soubory dál nikdy); běžící timelapse je chráněný (plánovaný soubor i seznam souborů), jeho bajty jdou do limitu
  a kontroly disku. Rotace nově vrací zvlášť `NewestTimelapseByFolder`; `CameraRecordingStatus.LastFile` („Přehrát
  poslední nahrávku“) timelapse nevybere.
- Stav: `CameraRecordingStatus.Timelapse` (`CameraTimelapseStatus`: `Activity`, `Mode`, `IsCapturing`, `StartedAt`,
  `CurrentFile`, `FrameCount`, `BytesWritten`, `Duration`, `LastFile`, `LastError`, `TimelapsePrints`, `Interval`).
  `Changed` i při přechodu pauza ↔ snímání. Chyby přes `Failed` s `CameraRecordingError.IsTimelapse = true`
  (`LastError` nahrávky se nemění). Notifikace s vlastními texty dodá T-161 (dnešní pozorovatel je ukáže jako chybu nahrávání).
- Nastavení: `CameraSettings.TimelapsePrints` (`printers[*].camera.timelapsePrints`, jen když zapnuto; součást vlastní
  rovnosti / hash / výpisu `CameraSettings`) a `CameraRecordingSettings.TimelapseIntervalSeconds`
  (`recording.timelapseIntervalSeconds`, jen když ≠ 30; chybná hodnota = 30 s s varováním). Editor nastavení v App obě
  hodnoty zatím jen přenáší (aby je uložení okna nesmazalo), ovládání dodá T-161.
- App: jen kompozice (`CameraTimelapseRecorder` nad hubem s odběrem jen klíčových snímků, předaný `CameraRecordingService`)
  a přenos hodnot v editoru nastavení.

**Nalezená a opravená chyba (souběh):** `CameraRecordingStorage.PrepareFolder` – když nahrávka a timelapse začínají se
stejným tiskem do nové složky tiskárny, druhý zápis mohl vidět značku `.layerpeek-recordings` (nebo soubor) prvního
a složku odmítl jako cizí (`FolderUnavailable`). Značka se už nepočítá jako soubor uživatele a značka nalezená po kontrole
složku přijme. Test `PreparingANewFolderFromSeveralWritersAtOnceNeverTakesItForAForeignOne` bez opravy selhává 3/3.

**Měření (macOS arm64, M2 Pro, 2026-11-04, Release, 600 s ustáleného stavu, interval 30 s):** skutečná kamera
(192.168.0.227) nebyla dostupná (tiskárna vypnutá), proto simulátor (`tools/PrusaSimulator`, klip skutečné Buddy3D,
25 fps, ~1 Mbit/s) v samostatném procesu a měřicí program ve scratchpadu (hub + FFmpeg + služba, jako aplikace):
- jen timelapse: CPU procesu 1 675 ms = **0,28 % jednoho jádra**, spravované alokace 30,6 kB za 10 min, RSS 42 MB;
  20 snímků za 600 s (á 30 s), soubor 1,58 MB za 10,3 min (≈ 75 kB / snímek, ≈ 92 MB za 10 h),
- timelapse + nahrávání na stejném spojení: CPU 3 630 ms = **0,60 %**, alokace 91 kB, RSS 43 MB; timelapse 1,57 MB,
  nahrávka 77,2 MB (≈ 1,03 Mbit/s).
- Měření se skutečnou Buddy3D přes relay → T-127.

Měřicí program (jen scratchpad, ne v repozitáři): konzolová aplikace s odkazem na `LayerPeek.Camera.FFmpeg`, sestaví
`ReconnectingCameraFeed` → `CameraStreamHub` → `CameraRecorder` + `CameraTimelapseRecorder` → `CameraRecordingService`
(falešný monitor pošle jeden snímek `Printing`), po 15 s zahřátí měří `Process.TotalProcessorTime`
a `GC.GetTotalAllocatedBytes` za 10 min.

**Testy:** Core `Cameras/Timelapse/CameraTimelapseServiceTests` (32 testů, 38 případů: automatický – snímek hned a každých 30 s, konec po
minutě, pauza / čekání bez snímků a pokračování ve stejném souboru, tisk začínající pauzou, nedostupnost; ruční – bez
tisku do stopu, konec s tiskem, spuštěný před tiskem, po skončeném tisku, jiná úloha; jiná úloha / dotisk = nový soubor,
Stop do konce tisku, souběh s nahráváním oběma směry, interval 5 s a změna za běhu, předání snímků spouštěči a jeho
uvolnění, bez rekordéru / muxeru, složka, málo místa, chyby zápisu bez opakování, limit, poslední timelapse z dřívějška,
rotace starého timelapse, vypnutá kamera, vypnutá volba, odebraná tiskárna, ukončení aplikace i během startu, žádné
časovače v nečinnosti, logy bez cesty), `CameraTimelapseTimeTriggerTests` (6, vč. 0 B na tik), `TimelapseFileNameTests`,
`SettingsMapperTimelapseTests` (+ fixture `settings-v2-timelapse.json`), v `CameraRecordingRotationTests` timelapse
v rotaci a souběžná příprava složky; App `SettingsTimelapseTests` (uložení okna nastavení hodnoty zachová). Všechny
dosavadní testy nahrávání a dashcamu beze změny prochází; služby nahrávání / dashcamu / timelapse 6× po sobě bez selhání.

**Výsledek:** `dotnet build` 0 upozornění, 0 chyb; `dotnet test` celkem 9333, neúspěšné 0, úspěšné 9133, přeskočeno 200
(macOS arm64). `dotnet publish src/LayerPeek.App -c Release -r osx-arm64` bez varování trim / AOT (jen dosavadní hlášky
nástrojů Xcode: dsymutil, `ModuleCache`, `xcodebuild`).

Uzavřeno 2026-11-04: měření se skutečnou kamerou přesunuto do T-127, část Windows (testy, AOT `win-x64`) do T-125.
