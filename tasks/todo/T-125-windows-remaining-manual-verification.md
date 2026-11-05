---
id: T-125
title: Windows – zbývající ruční ověření (interaktivní windows-verify, tiskárna, vydání)
status: todo
owner: qa-reviewer
priority: P1
platform: windows
depends_on: [T-017, T-055, T-058, T-101, T-105, T-107, T-114, T-117, T-118]
---

## Před vydáním 0.5.0 – doporučené pořadí
Doplněno 2026-11-04 (architekt). Všechny funkce od 0.4.0 (T-143 až T-166) vznikly na Macu a na Windows se zatím
nepřeložily ani neotestovaly. `scripts/release-build.ps1` spouští celý `dotnet test`, takže soubory Windows pro 0.5.0
jdou sestavit až po krocích 1–2. Odkazy jsou oddíly níže, body se odškrtávají tam. Co blokuje vydání, rozhodne uživatel.

1. **Build a testy (blokuje build vydání):** 2d · **T-162** (prázdná cache FFmpeg, nový build BtbN) → `dotnet test` na
   Windows (testovací body 2c–2p: T-143, T-144, T-145, T-146, T-148, T-154, T-155, T-158, T-160, T-161, T-164, T-165,
   T-159, T-163).
2. **Snímky UI sady Windows (blokuje build vydání; bez nich selžou snímkové testy nastavení):** jeden běh
   `LAYERPEEK_UPDATE_SNAPSHOTS=1` pro 2b · T-151, 2e · T-146, 2f · T-148, 2g · T-154, 2k · T-161 a 2n · detekce experimentální, 2o · T-159, 2p · T-163 a kontrola souborů
   vyjmenovaných u těchto bodů.
3. **Native AOT `win-x64`:** jeden `dotnet publish src/LayerPeek.App -c Release -r win-x64` uzavře body AOT v 2b–2p.
4. **E2E:** 2b · **T-151** (`CameraWindowAutoCloseTests`, `*Camera*`), 2e · **T-146** (`*CameraRecording*`,
   `*TrayMenu*`, `*CameraTaskbarAndModeSwitch*`), 2g · **T-154** (`*PrintFailureAlarm*`), 2k · **T-161**
   (`CameraTimelapseTests`).
5. **Ručně se simulátorem (nejvyšší priorita: dialogy souborů a toasty dosud nikdo neviděl):** 2l · **T-165** a 2m ·
   **T-164** (dialog Otevřít bez okna aplikace, v popředí) → 2e · **T-146** („Změnit…“, toast chyby nahrávání) → 2g ·
   **T-154** (obrázek toastu a editor výřezu) → 2f · **T-148** → 2k · **T-161** a 2o · **T-159** → 2d · **T-144** a 2h · **T-158**
   (přehrání ve Filmech a TV).
6. **Když zbývá čas:** 2b · T-151 (probuzení, celá obrazovka), 2c · T-143 (ruční), 2i · T-155 (měření CPU), 2d · T-145
   (OneDrive), 2p · T-163 (ruční), 3 · T-107, potom zbytek oddílů 1–2.

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

### 2g. Detekce chyb tisku – UI (z T-154, implementováno na Macu 2026-11-04)
- [ ] **T-154** (krit. „Poplach zobrazí notifikaci podle návrhu (E2E na Windows)“ a „Falešný poplach funguje z místa podle
      návrhu“, E2E): `pwsh tools/run-e2e.ps1 -Class '*PrintFailureAlarm*'` = PASS – detektor testovacího režimu
      (`LAYERPEEK_TEST_FAKE_DETECTOR=1`, Debug) během tisku simulátoru s kamerou vyvolá toast „Possible print failure –
      Detector e2e-alarm“ s názvem tiskárny, snímek `…_alarm-e2e-alarm.jpg` je v `Recordings` testovací datové složky,
      tlačítko „False alarm“ v banneru (UIA Invoke) uloží `detection/false-alarms/*.json` a nic neotevře, klik na toast
      v centru oznámení otevře okno kamery. Test je na Macu jen přeložený: kdyby UIA tlačítko akce v banneru nenašlo
      (jiný název / typ prvku), upravit hledání v testu a zapsat, co UIA hlásí.
- [ ] **T-154** (ruční, obrázek toastu): v banneru i v centru oznámení je pod textem snímek kamery v poměru stran kamery
      (vložený obrázek, ne oříznutý hero); jednou s „Ukládat snímek každého poplachu“ zapnutým (obrázek = uložený soubor ve
      složce nahrávek) a jednou vypnutým (obrázek z `%LOCALAPPDATA%\LayerPeek\cache\notification-images`, ve složce nejvýš
      5 souborů). Zvuk jako u chyby tisku. Postup: simulátor s kamerou, v testovacím režimu `LAYERPEEK_TEST_FAKE_DETECTOR=1`
      (Debug) nebo s detektorem „Změna na podložce“ po T-155 a zakrytím podložky.
- [ ] **T-154** (krit. „`dotnet build` a `dotnet test` bez chyb a varování; Native AOT publish bez nových varování“, část
      Windows): `dotnet test` na Windows bez chyb (nové `SettingsDetectionTests`, `DetectionRegionGeometryTests`,
      `PrintFailureNotificationTests`, `PrintFailureActivationTests`, `SettingsWindowDetectionUiTests`,
      `SettingsWindowLayoutTests.DetectionSubTabFitsInEveryLanguage`); `dotnet publish src/LayerPeek.App -c Release -r win-x64`
      bez nových varování AOT / trim.
- [ ] **T-154** (snímky UI, sada Windows): `LAYERPEEK_UPDATE_SNAPSHOTS=1 dotnet test --project tests/LayerPeek.App.UiTests`
      a zkontrolovat nové `settings-detection.png`, `settings-detection-camera-off-fr.png` a změněné `settings-recording.png`
      (sekce „Detekce chyb tisku“, nápověda dashcamu v levém sloupci) a všechny `settings-*.png` podzáložek tiskárny (čtvrtá
      podzáložka „Detekce“ v záhlaví) v `tests/LayerPeek.App.UiTests/Snapshots/` (macOS sada je hotová).
- [ ] **T-154** (ruční, editor výřezu se systémovým písmem): podzáložka Detekce u Core One – „Načíst snímek“ ukáže snímek
      kamery simulátoru, tažení myší kreslí / přesouvá / mění velikost obdélníku (kurzor podle místa), šipky a Shift+šipky ho
      mění, Ctrl+šipky o 10 %, Esc během tažení vrátí původní; Předčítání (Narrator) přečte „Výřez podložky: … % zleva …“.
      Po zavření a novém otevření okna bez kamery editor ukáže poslední uložený snímek s popiskem. Záhlaví čtyř podzáložek
      je na jednom řádku i ve francouzštině se Segoe UI.

### 2h. Timelapse – zápis z klíčových snímků (z T-158, implementováno na Macu 2026-11-04)
- [ ] **T-158** (krit. „`dotnet build` a `dotnet test` … AOT publish“, část Windows): `dotnet test` na Windows bez chyb
      (nové `CameraTimelapse*` v Core, `FFmpegTimelapseTests` s BtbN – fixture, simulátor, „pád“, výpadek, dvakrát týž IDR);
      `dotnet publish src/LayerPeek.App -c Release -r win-x64` bez nových varování AOT / trim.
- [ ] **T-158** (krit. „dva po sobě jdoucí vybrané IDR se stejným `idr_pic_id` … přehrání ve Filmech a TV ručně“): dekodér
      Media Foundation zatím neověřený (spike T-149). Na Windows zkopírovat z Macu `~/LayerPeek-samples/timelapse/`
      (`t158-same-idr-pic-id.mp4`: 5 snímků, `idr_pic_id` 7, 7, 8, 8, 7; `t158-coreone-clip-25-keyframes.mp4`: 25 klíčových
      snímků skutečné Buddy3D, 0,83 s) nebo po T-161 pořídit timelapse z aplikace; oba soubory se ve Filmech a TV i ve VLC
      přehrají bez zelených / rozpadlých snímků a posuvník projde všechny snímky. Kdyby Media Foundation stejné `idr_pic_id`
      odmítal, založit úkol na přepis `idr_pic_id` v hlavičce řezu (spike T-149, bez dekódování).

### 2i. Detekce chyb tisku – detektor „Změna na podložce“ (z T-155, implementováno na Macu 2026-11-04)
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

### 2j. Timelapse – služba (z T-160, implementováno na Macu 2026-11-04)
- [ ] **T-160** (krit. „`dotnet build` a `dotnet test` … AOT publish“, část Windows): `dotnet test` na Windows bez chyb
      (nové `CameraTimelapseServiceTests`, `CameraTimelapseTimeTriggerTests`, `TimelapseFileNameTests`,
      `SettingsMapperTimelapseTests`, `PreparingANewFolderFromSeveralWritersAtOnce…` v Core, `SettingsTimelapseTests` v App);
      `dotnet publish src/LayerPeek.App -c Release -r win-x64` bez nových varování AOT / trim.

### 2k. Timelapse – UI (z T-161, implementováno na Macu 2026-11-04)
- [ ] **T-161** (krit. „Headless UI testy a E2E prochází; snímky UI přegenerované“ a „`dotnet build` a `dotnet test`“, část
      Windows): `dotnet test` na Windows bez chyb (nové `TrayMenuRecordingTests` s timelapse, `SettingsTimelapseTests`,
      `RecordingNotificationTests.AFailedTimelapse…`, `CameraRecordingsTests.PlayLastTimelapse…`, UiTests
      `CameraWindowTimelapseUiTests`, `SettingsWindowTimelapseUiTests`, `TrayMenuTests.TheTrayMenuAndTheCameraWindowShowTheSameTimelapse`,
      rozšířené `SettingsWindowLayoutTests`); E2E `CameraTimelapseTests` (ruční timelapse z tray menu → soubor
      `…_timelapse.mp4`, „TL“ v okně kamery i v malém náhledu, stop z okna kamery; automatický timelapse tisku simulátoru bez
      okna kamery) a upravené `TrayMenuTests`, `TrayMenuDetailTests` (klávesy O, C, R, T, P, L, D),
      `CameraTaskbarAndModeSwitchTests` prochází; `dotnet publish src/LayerPeek.App -c Release -r win-x64` bez nových varování
      AOT / trim.
- [ ] **T-161** (snímky UI, sada Windows): `LAYERPEEK_UPDATE_SNAPSHOTS=1 dotnet test --project tests/LayerPeek.App.UiTests`
      a zkontrolovat nové `camera-timelapse.png`, `camera-mini-timelapse.png`, `camera-mini-timelapse-buttons.png` a změněné
      `camera-mini-recording-buttons.png` (tlačítko timelapse před REC), `settings-recording.png` (dashcam zhuštěný, sekce
      „Timelapse“ se štítkem), `settings-camera.png`, `settings-camera-preview.png`, `settings-camera-preview-4x3-de.png`,
      `settings-camera-notice.png` (řádek „Pořizovat timelapse tisku“ + štítek, popisek adresy vlevo od pole) v
      `tests/LayerPeek.App.UiTests/Snapshots/` (macOS sada je hotová).
- [ ] **T-161** (ruční část Windows): simulátor s kamerou (`--speed 20`); v podmenu tiskárny „Spustit timelapse
      (experimentální)“ → během pár sekund `%USERPROFILE%\Videos\LayerPeek\<tiskárna>\…_timelapse.mp4`, položka se změní na
      „Zastavit timelapse (experimentální)“, okno kamery i malý náhled ukazují jantarové stopky s „TL“ (bez ukazatele nad
      oknem), `1 pause` v konzoli simulátoru je ztlumí; stop z okna kamery → „Přehrát poslední timelapse“ otevře soubor ve
      Filmech a TV. Štítek „Experimentální“ v nastavení (záložka Nahrávání, podzáložka Kamera) se systémovým písmem Segoe UI
      nic neuřízne a nic se neposouvá. Chyba timelapse (např. složka na odpojeném USB disku) ukáže toast „Timelapse selhal“.

### 2l. Timelapse – export do JPEG (z T-165, implementováno na Macu 2026-11-04)
- [ ] **T-165** (krit. „`dotnet build` a `dotnet test` … AOT publish“, část Windows): `dotnet test` na Windows bez chyb,
      hlavně `FFmpegTimelapseJpegExportTests` (demuxer `mov` buildu BtbN čte timelapse, oříznutý soubor dá 4 z 8 snímků),
      `CameraTimelapseJpegExporterTests.FileNamesWithSpecialCharactersReachTheDecoderUnchanged` (cesta s `#`, `%` a
      diakritikou přes `Uri` na Windows), `JpegExportFolderTests`, `CameraRecordingsExportTests`,
      `TimelapseExportNotificationTests` (toast bez `launch`), UiTests `TimelapseJpegExportUiTests` a
      `TrayMenuTests.TheJpegExportItemStartsTheExportAndShowsItsProgressInPlace`; E2E upravené `TrayMenuDetailTests`
      (klávesy O, C, R, T, P, L, E, D), `TrayMenuTests` a `CameraTaskbarAndModeSwitchTests` (nová položka „Export timelapse to
      JPEG… (experimental)“ za „Play last timelapse“); `dotnet publish src/LayerPeek.App -c Release -r win-x64` bez nových
      varování AOT / trim.
- [ ] **T-165** (ruční část Windows): simulátor s kamerou, timelapse z T-161 hotový; v podmenu tiskárny „Exportovat timelapse
      do JPEG… (experimentální)“ → dialog Otevřít se ukáže v popředí (vlastník je skryté okno, ověřit, že nezůstane za jiným
      oknem ani v hlavním panelu nevisí prázdné okno), otevře se v `%USERPROFILE%\Videos\LayerPeek\<tiskárna>` a filtr
      „Timelapse (MP4)“ ukazuje jen `*_timelapse*.mp4`. Výběr → položka u všech tiskáren „Zrušit export do JPEG (x %)“ s
      rostoucím procentem, potom složka `…_timelapse_jpg` vedle souboru s `0001.jpg`… (otevřít ve Fotkách), toast
      „Timelapse exportován do JPEG“; zrušení uprostřed nezanechá žádnou složku (ani skrytou `.….partial`); zavřený dialog
      bez výběru nic nespustí; klik na toast nic neotevře a nespadne.

### 2m. Timelapse z hotové nahrávky (z T-164, implementováno na Macu 2026-11-04)
- [ ] **T-164** (krit. „`dotnet build` a `dotnet test` … AOT publish“, část Windows): `dotnet test` na Windows bez chyb,
      hlavně `FFmpegTimelapseFromRecordingTests` (demuxer `mov` buildu BtbN čte nahrávku jen jako pakety; nahrávka useknutá
      uprostřed dat fragmentu dá 22 z 39 snímků, useknutá uvnitř hlavičky `moof` skončí `Unreadable` – zapsat, jestli BtbN
      takový soubor otevře), `CameraTimelapseFromRecordingMakerTests` (i `AFileWithSpecialCharactersInItsPathIsRead`: `#`, `%`
      a diakritika přes `Uri` na Windows), `TimelapseFromRecordingFolderTests`, `CameraRecordingsTimelapseFromRecordingTests`,
      `TimelapseExportNotificationTests` (toast nového druhu bez `launch`), UiTests
      `TrayMenuTests.TheTimelapseFromRecordingItemStartsItAndShowsItsProgressInPlace`; E2E upravené `TrayMenuDetailTests`
      (klávesy O, C, R, T, P, L, E, M, D), `TrayMenuTests` a `CameraTaskbarAndModeSwitchTests` (nová položka „Create timelapse
      from recording… (experimental)“ za „Export timelapse to JPEG…“); `dotnet publish src/LayerPeek.App -c Release -r win-x64`
      bez nových varování AOT / trim.
- [ ] **T-164** (ruční část Windows): simulátor s kamerou, hotová nahrávka tisku (T-145, ideálně s výpadkem kamery, tedy
      i `…-2.mp4`); v podmenu tiskárny „Vytvořit timelapse z nahrávky… (experimentální)“ → dialog Otevřít v popředí, otevřený
      v `%USERPROFILE%\Videos\LayerPeek\<tiskárna>`, filtr „Nahrávka (MP4)“. Výběr nahrávky → položka u všech tiskáren
      „Zrušit timelapse z nahrávky (x %)“, potom vedle nahrávky `…_recording_timelapse.mp4` (přehraje se ve Filmech a TV
      při 30 fps, obsahuje i část po výpadku), toast „Timelapse z nahrávky vytvořen“; „Přehrát poslední timelapse“ ho otevře,
      když je nejnovější. Výběr timelapse → toast „… selhalo / Soubor není nahrávka z LayerPeeku.“ Zrušení uprostřed (velká
      nahrávka, nebo nahrávka na síťovém disku) nezanechá žádný soubor (ani skrytý `.….partial`, `dir /a`). Během vytváření
      jde spustit i export do JPEG jiného timelapse.

### 2n. Detekce chyb tisku označená jako experimentální (vydání 0.5.0, implementováno na Macu 2026-11-05)
- [ ] **Štítek „Experimentální“ u detekce** (rozhodnutí uživatele 2026-11-04, stejný štítek jako timelapse z T-161):
      `dotnet test` na Windows bez chyb (nový `SettingsWindowDetectionUiTests.TheDetectionHasTheExperimentalBadge` ve všech
      jazycích). Snímky UI sady Windows (`LAYERPEEK_UPDATE_SNAPSHOTS=1 dotnet test --project tests/LayerPeek.App.UiTests`,
      dohromady s body T-154 a T-161 výše): `settings-detection.png`, `settings-detection-camera-off-fr.png` (štítek vpravo
      od „Hlídat tisk této tiskárny kamerou“) a `settings-recording.png` (štítek vedle nadpisu „Detekce chyb tisku“);
      macOS sada je hotová. Ručně: štítky se systémovým písmem Segoe UI nic neuříznou, tip ukazuje text o vyladění na
      simulovaných selháních.

### 2o. Timelapse – spouštěč po vrstvě (z T-159, implementováno na Macu 2026-11-05)
- [ ] **T-159** (krit. „`dotnet build` a `dotnet test` bez chyb a varování“, část Windows): `dotnet test` na Windows bez
      chyb, hlavně Core `LayerChangeDetectorTests` (záznam `Fixtures/z-recordings/coreone-job354-0.2mm.csv` se kopíruje do
      výstupu, 0 B na vzorek), `CameraTimelapseLayerTriggerTests`, `CameraTimelapseServiceLayerTests` (i se skutečným
      rekordérem nad falešným proudem), `SettingsMapperTimelapseLayerTests`; App `SettingsTimelapseTriggerTests`; UiTests
      `SettingsWindowTimelapseUiTests` (výběr spouštěče ve všech jazycích, texty se vejdou do pole se Segoe UI) a rozšířené
      `SettingsWindowLayoutTests.RecordingTabFitsInEveryLanguage`; `dotnet publish src/LayerPeek.App -c Release -r win-x64`
      bez nových varování AOT / trim.
- [ ] **T-159** (snímky UI, sada Windows): spolu s 2k přegenerovat a zkontrolovat `settings-recording.png` (v řádku nadpisu
      „Timelapse“ pole „Každých N sekund“ místo popisku intervalu).
- [ ] **T-159** (ruční část Windows): na záložce Nahrávání přepnout „Každých N sekund“ → „Každou N. vrstvu (odhad)“, místo
      intervalu se ukáže počet vrstev (1), tipy vysvětlují odhad z osy Z; uložit, znovu otevřít – volba zůstane. Simulátor
      (`--speed 20`) s automatickým timelapse: na Windows zkontrolovat jen, že timelapse vznikne a má snímek začátku a konce
      (simulátor osu Z nemění po vrstvách, počet snímků po vrstvách ověří T-127 se skutečnou tiskárnou).

### 2p. Detekce – režim „Tmavý filament“ (z T-163, implementováno na Macu 2026-11-05)
- [ ] **T-163** (Windows): `dotnet test` na Windows bez chyb, hlavně `SettingsMapperDetectionTests`,
      `PrintFailureDetectionServiceTests.TheDarkFilamentModeIsInTheContext`, `BedChangeDetectorTests` /
      `BedChangeDetectorCostTests` (0 B na vzorek i v režimu), `BedChangeDarkSweepTests` a UiTests
      `SettingsWindowDetectionUiTests.TheDarkFilamentModeIsOffByDefaultAndSaved`; snímky UI pro Windows přegenerovat
      (`LAYERPEEK_UPDATE_SNAPSHOTS=1`, hlavně `settings-detection*`: nové zaškrtávátko „Tmavý filament“ s odznakem
      „Experimentální“, vysvětlení v tooltipu). Ručně: podzáložka Detekce, zaškrtnout, uložit, znovu otevřít – volba zůstala, text se
      nepřekrývá ani v němčině a francouzštině (125 % a 150 % DPI).

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
