---
id: T-125
title: Windows – zbývající ruční ověření (interaktivní windows-verify, tiskárna, vydání)
status: todo
owner: qa-reviewer
priority: P1
platform: windows
depends_on: [T-017, T-055, T-058, T-101, T-105, T-107, T-114, T-117, T-118]
---

## Cíl
Sběrný úkol se zbytkem ověření na Windows mimo instalátor: celý interaktivní běh `tools/windows-verify.ps1` (T-105),
zkušební upozornění (T-101), nepovinné subjektivní kontroly okna kamery (T-117, T-118), instalátor `win-arm64` (T-017;
zrušeno T-130),
ověření se skutečnou tiskárnou (T-107) a body vydání (T-055, T-058, SmartScreen z T-017). Vznikl rozdělením T-114
(rozhodnutí uživatele 2026-11-01): body se přesunuly sem (v T-114 odškrtnuté s „→ přesunuto do T-125“); skutečná
instalace, autostart a odinstalace jsou v T-124. Protějšek pro Mac je T-127.

Postup: [docs/windows-manual-testing.md](../../docs/windows-manual-testing.md) a skript `tools/windows-verify.ps1`;
vydání podle [docs/release.md](../../docs/release.md).

## Rozsah
- Kontroly se skutečnou instalací (`win.e2e-installer`, `win.install`, `win.toast-click`, `win.autostart`,
  `win.uninstall`) patří do T-124. Zda je má „celý interaktivní běh“ u T-105 obsahovat znovu, nebo stačí běh
  s `-Skip` těchto kontrol a jejich výsledek z T-124, rozhodne uživatel (otázka níže).
- Aktualizace přes Velopack (bod T-058) potřebuje nainstalovanou starší verzi – stejný předpoklad jako T-124
  (odložená instalace uživatele).
- Oddíl „Vydání“ zapisuje na GitHub – jen s výslovným souhlasem uživatele.
- Výsledky (verze Windows, datum, výňatek z `artifacts/verify/<čas>/report.md`) zapsat do `## Poznámky k implementaci`.
  Každý `FAIL` nebo nalezená chyba = nový úkol. Bod se odškrtne až po úspěšném ověření.
- Hesla ani API klíče nikdy nevkládat do chatu ani do úkolů.

## Akceptační kritéria

### 1. Skript `tools/windows-verify.ps1` (z T-114, oddíl 2)
Postup: `pwsh scripts/release-build.ps1`, pak `pwsh tools/windows-verify.ps1 -ListChecks` a
`pwsh tools/windows-verify.ps1` (interaktivně).
- [ ] **T-105** (krit. „Skripty spuštěné uživatelem“, část Windows): `tools/windows-verify.ps1` proběhl celý
      interaktivně (i `-RestoreOnly` po případném přerušení); nalezené chyby skriptu zapsané a opravené (nový úkol).
      *Automatizováno v T-129:* průchod hlavní části skriptu s podvrženými kontrolami (odpovědi `y` / `n` na
      standardním vstupu, souhrn, `report.md` / `report.json`, přerušení uprostřed a `-RestoreOnly`) testují Pester testy
      `tools/windows-verify.Tests.ps1` v `dotnet test`. Ručně zbývá skutečný interaktivní běh se skutečnými kontrolami.

### 2. Ruční kontroly bez tiskárny (z T-114, oddíl 3)
- [x] **T-101** (krit. „Ověřeno uživatelem na Macu i Windows“, část Windows): tlačítko „Poslat zkušební upozornění“
      pošle toast (zvuk podle hodnoty vybrané v okně, i neuložené), druhý klik předchozí toast nahradí; po vypnutí
      v Nastavení → Systém → Oznámení varování „Upozornění jsou ve Windows vypnutá.“ s tlačítkem, které otevře
      `ms-settings:notifications`; klik na zkušební toast přenese okno Nastavení na záložku Obecné.
      → Nahrazuje E2E test `TestNotificationTests.TestNotificationFollowsTheWindowReplacesItselfOpensGeneralAndWarnsWhenWindowsTurnedItOff`
      (T-128): `pwsh tools/run-e2e.ps1 -Class '*TestNotificationTests'` (systémové změny `Toasts`, `DoNotDisturb`;
      s `-NoSystemChanges` se přeskočí). Zvuk se ověřuje v XML toastu z historie oznámení (uložené „Všechna“, v okně
      neuložené „Vypnuto“ → toast je tichý). Odškrtnout po zeleném běhu.
      → E2E zelený 2026-11-03 (T-133).
- [ ] **T-101** (ruční zbytek z T-133): E2E test vypnutí ve Windows jen předstírá (Debug přepínač), proto skutečné vypnutí
      upozornění LayerPeeku v Nastavení → Systém → Oznámení → varování v okně Nastavení ověřte ručně (a pak upozornění zase
      zapněte).
- [ ] *Nepovinné, doporučené (zbytek T-117 po T-120):* čitelnost ztlumeného špendlíku na světlém obraze kamery a při
      200 % (subjektivní; E2E jen měří jas a ukládá výřezy) a skutečný hlas Předčítání v češtině („Vždy navrchu: zapnuto /
      vypnuto“; E2E ověřuje jen UIA název a stav, které Předčítání čte).
- [ ] *Nepovinné, doporučené (zbytek T-118 po T-120):* vzhled obrysu nad skutečnou tapetou a Průzkumníkem (E2E používá
      vlastní bílé a tmavé okno za náhledem); ztmavnutí obrysu během tažení úchopem je přijaté a netestuje se.
- [x] **T-017** (krit. „Instalátor `win-arm64` vznikne stejným skriptem, pokud je `win-arm64` zapnutý“): po doinstalaci
      komponenty MSVC ARM64 jako správce (`docs/prerequisites.md`, T-063 bod 1) `pwsh scripts/release-build.ps1 -Runtime
      win-x64,win-arm64` vytvoří i `LayerPeek-<verze>-win-arm64-Setup.exe` a soubory kanálu `win-arm64` bez kolize
      s `win-x64`; nic se nenahrává. Spuštění na zařízení ARM64 se tu neověřuje (T-057). Lze spojit s T-063.
      → zrušeno (T-130): podporu `win-arm64` uživatel 2026-11-01 zrušil, skripty `win-arm64` odmítají, T-057 a T-063
      jsou zamítnuté.

- [ ] *Nepovinné (z T-124, rozhodnutí uživatele 2026-11-01):* na nainstalované verzi (po vydání 0.4.0) skutečný klik na
      upozornění v centru oznámení, když aplikace neběží (`pwsh tools/windows-verify.ps1 -Only win.toast-click`), a autostart
      po skutečném odhlášení a přihlášení (`-Only win.autostart`).
- [x] **T-128** (z T-124): bez nainstalované LayerPeek `pwsh tools/run-e2e.ps1 -Class '*UpdateTests'` – projdou i oba
      instalační případy. Předpoklad: T-133 (nové testovací AUMID `LayerPeek.E2E.2`, jehož upozornění jsou zapnutá).
      → nahrazeno ostrou aktualizací 0.3.1 → 0.4.0 (rozhodnutí uživatele 2026-11-03: 0.4.0 neodinstalovat); instalační
      případy se při instalaci přeskočí, viz poznámky.

### 2b. Automatické okno kamery (z T-151, implementováno na Macu 2026-11-03)
- [ ] **T-151** (krit. „Vypnutí tiskárny (simulátor offline) okno zavře po debounce, zapnutí ho otevře na posledním místě“,
      „Ručně zavřené okno se znovu neotevře“ a „Automaticky otevřené okno nebere fokus …“, část Windows):
      `pwsh tools/run-e2e.ps1 -Class '*CameraWindowAutoCloseTests'` = PASS. Test byl napsaný a přeložený na Macu a na Windows
      ještě neběžel. Když selže kvůli testu, opravit test, když kvůli aplikaci, založit úkol. Prohlédnout snímky
      `camera-auto-close-1-open` a `camera-auto-close-2-reopened`.
- [ ] **T-151** (krit. „Stávající E2E okna kamery (T-071, T-120) prochází“): `pwsh tools/run-e2e.ps1 -Class '*Camera*'` = PASS.
- [ ] **T-151** (snímky UI a `dotnet test` na Windows): přegenerovat a zkontrolovat referenční snímky Windows
      `tests/LayerPeek.App.UiTests/Snapshots/settings-camera-window.png`, `settings-camera-window-error-normal-size.png`,
      `settings-camera-window-errors-fr.png` a `settings-camera-window-off-cs.png`
      (`$env:LAYERPEEK_UPDATE_SNAPSHOTS=1; dotnet test --project tests/LayerPeek.App.UiTests -- --filter-class "*SettingsWindowSnapshotTests"`).
      V podzáložce „Okno kamery“ přibylo zaškrtávátko „Close when the printer turns off…“ pod tlačítky. Pak celý
      `dotnet test` bez chyb, včetně `SettingsWindowLayoutTests` (bez posuvníku ve všech jazycích).
- [ ] **T-151** (krit. Native AOT, část Windows): `dotnet publish src/LayerPeek.App -c Release -r win-x64` bez nových
      varování (nový `[LibraryImport("powrprof.dll")]` + `UnmanagedCallersOnly` v `WindowsSystemResumeEvents`).
- [ ] **T-151** (ruční, Windows, probuzení): se zapnutým podrobným logováním otevřít okno kamery, uspat počítač
      a probudit ho. V logu je „The computer resumed from sleep: the camera window delays start again.“ a okno se po
      probuzení nezavře podle stavu z doby před uspáním (T-150 rozhodnutí 9).
- [ ] **T-151** (ruční, Windows, okrajový případ): u tiskárny s „Otevřít jako: Celá obrazovka“ okno po automatickém
      otevření (simulátor `offline` → `online`, nebo E2E přepínače) nesmí vzít popředí oknu jiné aplikace. Avalonia na Windows
      aktivuje zobrazené okno, jehož stav se změní (T-040). Pokud ho vezme, zapsat a založit úkol.

### 2c. Sdílené spojení s kamerou (z T-143, implementováno na Macu 2026-11-03)
- [ ] **T-143** (krit. „Okno kamery, malý náhled a překryv fungují jako dřív; stávající … E2E okna kamery prochází“, část
      Windows): `pwsh tools/run-e2e.ps1 -Class '*Camera*'` = PASS a `dotnet test` na Windows bez chyb (unit a headless UI
      testy prošly na Macu).
- [ ] **T-143** (krit. „Ustálený stav streamu bez alokací“, část Windows): `FFmpegCameraFeedAllocationTests` (včetně
      `SteadyStatePacketsAndFramesDoNotAllocate` a `SteadyStateFramesThroughTheHubDoNotAllocate`) a
      `CameraStreamHubAllocationTests` prochází na Windows; `dotnet publish src/LayerPeek.App -c Release -r win-x64` bez
      nových varování AOT / trim.
- [ ] **T-143** (ruční, simulátor): okno kamery minimalizovat a do 10 s obnovit – obraz je tam hned (bez čekání na klíčový
      snímek); zavřít okno a po 10 s simulátor nevidí žádnou relaci (`GET /_simulator/state`, nebo log s podrobným
      logováním: „The shared camera stream of printer '…' closed after its last subscriber left.“).

### 2d. Nahrávání kamery (z T-144, implementováno na Macu 2026-11-03)
- [ ] **T-144** (krit. „Nahrávka z fixture i z RTSP kamery simulátoru … platný soubor“ a „Ustálený stav … bez alokací“, část
      Windows): `FFmpegRecordingTests`, `FFmpegRecordingAllocationTests` a `FFmpegRuntimeMuxerTests` prochází na Windows
      (build BtbN má muxer MP4); `dotnet publish src/LayerPeek.App -c Release -r win-x64` bez nových varování AOT / trim.
- [ ] **T-144** (ruční, přehrání): nahrávka z kamery simulátoru (po T-146 z menu nebo okna kamery) jde přehrát ve
      Filmech a TV i ve VLC od začátku do konce, posuvník funguje.
- [ ] **T-145** (služba nahrávání, implementováno na Macu 2026-11-03): testy `CameraRecording*` v `LayerPeek.Core.Tests`
      prochází na Windows (výchozí složka `%USERPROFILE%\Videos\LayerPeek` podle známé složky Videa, volné místo přes
      `DriveInfo` kořene disku, skrytá značka `.layerpeek-recordings`); složka se značkou je v Průzkumníku skrytá; s Videi na OneDrive (Files On-Demand) rotace staré nahrávky skutečně maže (soubory s reparse pointem rotace přeskakuje, revize T-145).
- [ ] **T-162** (krit. „Build na Windows s prázdnou cache stáhne a ověří FFmpeg a projde“, připnuto na Macu 2026-11-04):
      `dotnet build` s `LayerPeekFFmpegCacheRoot` na prázdnou složku stáhne `autobuild-2026-10-31-12-59`
      (`ffmpeg-n9.0.2-39-g7adfbe40dc-win64-lgpl-shared-9.0.zip`), ověří SHA-256 a projde; pak `dotnet test` (FFmpeg testy
      včetně MP4 muxeru T-144) a `dotnet publish src/LayerPeek.App -c Release -r win-x64` bez chyb; `pwsh -File
      build/update-ffmpeg.Tests.ps1` (Pester) prochází i na Windows.

### 2e. Nahrávání kamery – UI (z T-146, implementováno na Macu 2026-11-03)
- [ ] **T-146** (krit. „Automatické nahrávání během tisku běží bez otevřeného okna kamery (E2E se simulátorem)“ a „E2E testy
      po sobě smažou nahrávky i po pádu“): `pwsh tools/run-e2e.ps1 -Class '*CameraRecording*'` = PASS (obě metody; druhá
      trvá kvůli minutě po konci tisku ~2 min); po běhu ve vyhrazeném kořeni E2E nezůstala žádná složka `Recordings`.
- [ ] **T-146** (změněné podmenu tiskárny): `pwsh tools/run-e2e.ps1 -Class '*TrayMenu*'` a `-Class '*CameraTaskbarAndModeSwitch*'`
      = PASS (očekávané položky doplněné o „Start recording“, „Play last recording“, „Open recordings folder“; oddělovač
      není položka menu – kdyby UI Automation hlásilo oddělovač jako `MenuItem`, opravit `TrayMenu.ItemsOf`).
- [ ] **T-146** (krit. „`dotnet build` a `dotnet test` bez chyb …; Native AOT publish bez nových varování“, část Windows):
      `dotnet test` na Windows bez chyb; `dotnet publish src/LayerPeek.App -c Release -r win-x64` bez nových varování AOT / trim.
- [ ] **T-146** (snímky UI, sada Windows): `LAYERPEEK_UPDATE_SNAPSHOTS=1 dotnet test --project tests/LayerPeek.App.UiTests`
      a zkontrolovat nové `camera-recording.png`, `camera-mini-recording.png`, `camera-mini-recording-buttons.png`,
      `settings-recording.png` (vlastní záložka „Nahrávání“, rozhodnutí 2026-11-04) a změněné **všechny** `settings-*.png`
      (pátá hlavní záložka v záhlaví; navíc obsah `settings-camera*.png` s volbou „Automaticky nahrávat tisk“)
      v `tests/LayerPeek.App.UiTests/Snapshots/` (macOS sada je hotová). Bez přegenerování na Windows selžou snímkové testy
      nastavení.
- [ ] **T-146** (krit. „Ruční nahrávání jde spustit a zastavit … stav je vidět“, ruční část Windows): simulátor s kamerou;
      v podmenu tiskárny „Nahrávat“ → za pár sekund tooltip ikony „Nahrává se“ (ikona beze změny), v okně kamery „● REC“
      i bez ukazatele (normální okno, malý náhled, celá obrazovka), „Zastavit nahrávání“ v menu i tlačítkem v okně;
      „Přehrát poslední nahrávku“ otevře soubor ve Filmech a TV (přehraje se celý, posuvník funguje), i když název tisku
      i cesta obsahují čárku a žádnou mezeru (např. tiskárna `MK4` a tisk `a,b.bgcode`; `explorer.exe` dělí argument
      na čárkách – revize T-146, při chybě nový úkol); „Otevřít složku
      s nahrávkami“ v podmenu tiskárny otevře Průzkumníka v její podsložce `%USERPROFILE%\Videos\LayerPeek\<tiskárna>`, u tiskárny
      bez nahrávek (podsložka ještě není) ve `%USERPROFILE%\Videos\LayerPeek` (rozhodnutí 2026-11-04). Nastavení má pátou
      hlavní záložku „Nahrávání“ a všech pět záhlaví je na jednom řádku i ve francouzštině a polštině se systémovým písmem
      (Segoe UI; headless testy měří s písmem Inter). „Změnit…“ otevře systémový dialog
      složky; složka na odpojeném USB disku nebo bez práva zápisu ukáže hlášku místo nápovědy; při nahrávání do složky,
      jejíž disk se odpojí, přijde toast „Nahrávání kamery selhalo“ s důvodem a klik na něj otevře nastavení rovnou na
      záložce „Nahrávání“ (i když je okno nastavení už otevřené na jiné záložce; rozhodnutí 2026-11-04).

### 2f. Dashcam – posledních N minut (z T-148, implementováno na Macu 2026-11-04)
- [ ] **T-148** (krit. „`dotnet build` a `dotnet test` bez chyb a varování“, část Windows): `dotnet test` na Windows bez chyb
      (nové `CameraDashcam*`, `DashcamBufferTests`, `SettingsMapperDashcamTests` v Core, `FFmpegRecordingTests
      .TheDashcamSavesAPlayableFileWithTheTimeBeforeAndAfterTheEvent` s BtbN, `SettingsDashcamTests`, `TrayMenuRecordingTests`,
      `CameraWindowDashcamUiTests`); `dotnet publish src/LayerPeek.App -c Release -r win-x64` bez nových varování AOT / trim.
- [ ] **T-148** (snímky UI, sada Windows): `LAYERPEEK_UPDATE_SNAPSHOTS=1 dotnet test --project tests/LayerPeek.App.UiTests`
      a zkontrolovat změněné `settings-recording.png` (sekce „Poslední minuty tisku (dashcam)“) a `settings-camera.png`,
      `settings-camera-preview.png`, `settings-camera-preview-4x3-de.png`, `settings-camera-notice.png` (vedle
      „Automaticky nahrávat tisk“ nově „Uchovávat poslední minuty (dashcam)“ v tomtéž řádku) v
      `tests/LayerPeek.App.UiTests/Snapshots/` (macOS sada je hotová).
- [ ] **T-148** (krit. „Uložení po spouštěči vytvoří přehratelný soubor …“, ruční část Windows): simulátor s kamerou
      (`--speed 20`), tiskárna se zapnutým „Uchovávat poslední minuty (dashcam)“ a bez „Automaticky nahrávat tisk“; během tisku
      je v podmenu tiskárny „Uložit posledních 2 min“ (mimo tisk zakázané s tipem „Poslední minuty se uchovávají jen během
      tisku.“) a v místní nabídce okna kamery totéž; klik → za ~30 s soubor `…_dashcam-manual.mp4` v
      `%USERPROFILE%\Videos\LayerPeek\<tiskárna>`, přehraje se ve Filmech a TV celý (začíná obrazem, ne šedou); v konzoli simulátoru
      `1 error` / `1 attention` / `1 stopped` během tisku → soubory `…_dashcam-error.mp4` / `…-attention` / `…-stopped`; „Přehrát poslední
      nahrávku“ otevře nejnovější z nich. Ve Správci úloh roste paměť procesu během tisku jen o ~16 MB na tiskárnu (2 min)
      a po konci tisku se vrátí.

### 2g. Detekce chyb tisku – detektor „Změna na podložce“ (z T-155, implementováno na Macu 2026-11-04)
- [ ] **T-155** (krit. „`dotnet build` a `dotnet test` bez chyb a varování“, část Windows): `dotnet test` na Windows bez
      chyb (nové `BedChangeDetectorTests`, `BedChangeDetectorCostTests`, `BedChangeAccuracyCoreOneTests`,
      `BedChangeAccuracyFullFrameTests` v Detection; `BedChangeLocalSamplesTests` se bez `~/LayerPeek-samples` přeskočí;
      `BedChangeAlarmSimulatorTests` a `FFmpegRecordingTests.ARecordingPlaysAsTheSimulatorsCameraClip` s BtbN; `Mp4ClipTests`
      simulátoru).
- [ ] **T-155** (krit. „Licence … Native AOT publish bez nových varování“, část Windows): `dotnet publish src/LayerPeek.App
      -c Release -r win-x64` bez nových varování AOT / trim (žádná nová závislost).
- [ ] **T-155** (krit. „Spotřeba CPU a paměti změřená“, část win-x64): změřit čas detektoru na vzorek jako v poznámkách T-155
      (testovací aplikace mimo repozitář: `PrintFailureDetectors.All[0].Create()`, 6 klíčových snímků fixture dokola,
      20 000 vzorků po 300 zahřívacích, `Stopwatch` a `GC.GetAllocatedBytesForCurrentThread`, Native AOT `-r win-x64`) pro
      výřez Core One i celý snímek; zapsat ms na vzorek a podíl jádra při 2 s a 5 s. Mez T-153: detekce < 1 % jednoho jádra
      na tiskárnu (macOS: 0,22 / 0,71 ms na vzorek). Volitelně celou cestu jako v T-153 (simulátor `--camera-clip`,
      `Process.TotalProcessorTime` za 60 s).

### 3. Se skutečnou tiskárnou (z T-114, oddíl 4)
- [ ] **T-107** (krit. „Ruční ověření uživatelem se skutečnou tiskárnou“, část Windows): Edge i Chrome, menu tiskárny →
      Otevřít web – web PrusaLink přes proxy se načte, zobrazí stav, nahraje a stáhne soubor; v konzoli prohlížeče žádná
      chyba kvůli Service Workeru nebo hlavičkám proxy.

### 4. Vydání (z T-114, oddíl 5; zapisuje na GitHub – jen s výslovným souhlasem uživatele, postup `docs/release.md`)
- [ ] **T-055** (krit. „Zkušební vydání … následné přidání souborů druhým během“, část Windows): ve zkušebním vydání
      z T-115 (běh na Macu první; od 2026-11-01 bod v T-127) doplní Windows své soubory (`release-upload.ps1
      -AddToPublished`, `docs/release.md`, „Zkouška doplnění druhého OS (T-113)“); `release-publish.ps1 -DryRun` bez
      `-AllowIncomplete` pak projde.
- [x] **T-058** (krit. „Klik na položku / notifikaci otevře stránku vydání, případně spustí aktualizaci přes Velopack“,
      část Windows): skutečný klik na položku „Je dostupná nová verze X.Y.Z“ v menu a na banner toastu; nainstalovaná
      starší verze se přes Velopack aktualizuje z GitHub Releases (bez tokenu) a znovu spustí; `.zip` verze otevře
      stránku vydání.
      → Nahrazují E2E testy `UpdateTests` (T-128): `InstalledOlderVersionUpdatesFromALocalFeedAfterAClick` (klik na položku
      menu i na banner; přenosná instalace Velopack v `%TEMP%` z místního zdroje, ne z GitHubu; aktualizovanou kopii spustí
      test, protože testovací režim aplikaci po aktualizaci znovu nespouští) a
      `ZipVersionOpensTheReleasePageFromTheBannerAndTheMenuItem`. Běh: `pwsh tools/run-e2e.ps1 -Class '*UpdateTests'`, test
      instalace jen bez nainstalované LayerPeek (poprvé s T-124). Znovuspuštění samotným `Update.exe` se neověřuje, stačí
      spuštění aktualizované kopie testem (rozhodnutí uživatele 2026-11-01). Odškrtnout po zeleném běhu.
      → 2026-11-03: případ `.zip` (banner i položka menu) PASS; instalační případy přeskočené (nainstalovaná 0.4.0)
      a nahrazené ostrou aktualizací uživatelem (klik na položku menu, viz poznámky).
- [x] **T-058** (ruční zbytek, rozhodnutí uživatele 2026-11-01): jednou po skutečném vydání aktualizace nainstalované
      starší verze z GitHub Releases (bez tokenu) – E2E testy používají jen místní zdroj.
      → ověřeno uživatelem 2026-11-03 (0.3.1 → 0.4.0, viz poznámky).
- [ ] **T-017** (otevřená otázka SmartScreen): stažení `Setup.exe` z GitHub vydání v Edge – zapsat varování SmartScreen
      při stahování a doplnit ho do `docs/release.md` (spuštění se značkou Mark-of-the-Web ověřil T-056).

### 5. Závěr (z T-114, oddíl 6)
- [ ] Výsledky zapsané v poznámkách (verze Windows, datum, zpráva skriptu); každá chyba má nový úkol; profil vrácený
      (`win.uninstall`, případně `pwsh tools/windows-verify.ps1 -RestoreOnly`).

## Otevřené otázky
- Má „celý interaktivní běh“ `tools/windows-verify.ps1` (T-105) obsahovat i kontroly se skutečnou instalací, které
  ověřuje T-124 (pak i tady musí být instalace uživatele odložená), nebo stačí běh s
  `-Skip win.e2e-installer,win.install,win.toast-click,win.autostart,win.uninstall` a výsledek těchto kontrol z T-124?
- ~~Aktualizace přes Velopack (T-058) potřebuje nainstalovanou starší verzi: dělat ji ve stejném sezení jako T-124,
  nebo samostatně?~~ Vyřešeno: ověřeno samostatně na ostré instalaci (2026-11-03).

## Poznámky k implementaci
### T-058 – ostrá aktualizace (uživatel, 2026-11-03, Windows 11 Pro 10.0.26300)
- Nainstalovaná 0.3.1 (instalátor Velopack) se po vydání 0.4.0 aktualizovala z GitHub Releases bez tokenu.
- Spuštěno klikem na položku „Je dostupná nová verze 0.4.0“ v menu v tray; klik na banner toastu se ručně nezkoušel
  (pokrývá E2E `UpdateTests`, bod výše).
- Po aktualizaci `Update.exe` aplikaci sám znovu spustil a běží 0.4.0 – tím je ověřené i znovuspuštění, které E2E testy
  záměrně nepokrývají.

### E2E běhy 2026-11-03 (hlavní agent, po T-133)
- `TestNotificationTests` 1/1 PASS (nové AUMID `LayerPeek.E2E.2`, vypnutí ve Windows jen předstírané) → T-101 (E2E část).
- `UpdateTests`: `ZipVersionOpensTheReleasePageFromTheBannerAndTheMenuItem` PASS; oba případy
  `InstalledOlderVersionUpdatesFromALocalFeedAfterAClick` SKIP kvůli nainstalované 0.4.0. Uživatel rozhodl 0.4.0
  neodinstalovat a body T-058 (E2E) a T-128 uzavřít na základě ostré aktualizace výše. Klik na banner toastu
  u instalované verze tak ověřuje jen případ `.zip` (stejná cesta banneru, jiná akce).
