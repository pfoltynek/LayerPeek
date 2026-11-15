---
id: T-210
title: macOS ověření – nahrávání a dashcam na testovacím bundlu a dekódování souborů přes AVFoundation (`decode`)
status: done
owner: qa-reviewer
priority: P2
platform: macos
depends_on: [T-208, T-209]
---

> **Rozhodnuto uživatelem 2026-11-13:** přehrání v QuickTime Playeru nahrazuje úplné dekódování přes AVFoundation:
> každý snímek, délka, přeskakování (seek). Kontrola stavu přes Accessibility (popisek / stav prvku) nahrazuje
> poslech VoiceOveru.

## Cíl
Automatizovat simulátorové části bodů T-146 (ruční nahrávání) a T-148 (dashcam) v T-127 a přehrání nahrávek v QuickTime
(T-147, T-145) na testovacím bundlu `LayerPeek E2E.app` se simulátorem, bez skutečné tiskárny.

## Rozsah
- **Helper `decode <soubor.mp4>`** (AVFoundation, bez nových závislostí) vypíše JSON:
  - `isPlayable`, `isReadable`, kodek stopy (`avc1`), rozměry, `duration` a počet snímků;
  - průchod všemi vzorky přes `AVAssetReader` (dekodér QuickTime): počet dekódovaných snímků, chyby, monotónní PTS;
  - `seek`: `AVAssetImageGenerator` s nulovou tolerancí na 0 %, 25 %, 50 %, 75 % a konci délky;
  - průměrný jas a rozptyl každého vygenerovaného snímku (černý nebo jednobarevný snímek = chyba).
  Exit 1, když se některý snímek nedekóduje nebo seek selže. Stejný příkaz použijí T-211 a T-213.
- **Simulátor:** ovládací endpointy `POST /_simulator/camera/offline` a `POST /_simulator/camera/online` (dnes jen
  příkaz konzole `camera offline` / `camera online`). Odpojí klienty a nepřijímá nová spojení stejně jako příkaz
  konzole, platí stejná pravidla přihlášení jako u ostatních ovládacích endpointů. Testy v `tests/PrusaSimulator.Tests`,
  doplnit `docs/simulator.md`. Použijí je i T-213 (nahrávka s výpadkem, `…-2.mp4`) a T-215 (T-188).
- Nahrávka s výpadkem kamery: `camera/offline` na ~10 s simulovaného času během nahrávání → `…mp4` a `…-2.mp4`, oba
  `decode` OK.
- **Izolace:** složka nahrávek testovacího bundlu je v dočasné složce běhu (nastavení testovacích dat). Nikdy
  `~/Movies/LayerPeek`. Simulátor je jen na 127.0.0.1. Na konci se vše smaže.
- **Kontrola `mac.recording`** (Permission: Zpřístupnění, preset `release`):
  - simulátor s kamerou a `--speed 20`;
  - v podmenu tiskárny (`helper menu`) je za oddělovačem „Nahrávat“, „Přehrát poslední nahrávku“ (zakázané, dokud
    nahrávka neexistuje) a „Otevřít složku s nahrávkami“;
  - `menupress …>Nahrávat`: tooltip ikony je „Nahrává se“ (`statusitems`), v okně kamery je prvek „● REC“ (AX
    identifikátor) ve všech třech režimech okna a pod ukazatelem tlačítko Stop;
  - zastavení jednou z menu („Zastavit nahrávání“), podruhé tlačítkem v okně (AXPress), oba soubory `decode` = OK;
  - „Přehrát poslední nahrávku“: v testovacím režimu se ověří jen předání souboru systému. QuickTime se nespouští,
    viz otázka 1;
  - „Otevřít složku s nahrávkami“: Finder ukáže podsložku tiskárny. Finder AX okno, AXDocument = složka. Okno se potom
    zavře;
  - nezapisovatelná složka nahrávek (`chmod 0500` v dočasné složce, nastavená v `settings.json` testovacích dat) ukáže
    v Nastavení › Nahrávání hlášku (AX text), aplikace nespadne.
- **Kontrola `mac.dashcam`** (Permission, preset `release`):
  - tiskárna se zapnutým „Uchovávat poslední minuty (dashcam)“, bez automatického nahrávání;
  - mimo tisk je „Uložit posledních 2 min“ v podmenu zakázané (AXHelp zapsat), během tisku povolené;
  - totéž v místní nabídce okna kamery (`ctrlclick` + AX menu);
  - klik → za ~30 s simulovaného času soubor `…_dashcam-manual.mp4`, `decode` OK, délka v očekávaném rozsahu;
  - scénáře simulátoru `error` / `attention` / `stopped` přes HTTP během tisku → `…_dashcam-error.mp4` /
    `…-attention.mp4` / `…-stopped.mp4`, každý `decode` OK;
  - automatické nahrávání tisku (T-145, simulátorová část): scénář `printing` → `finished`, soubor
    `<datum>_<čas>_<název tisku>.mp4` vznikne a uzavře se asi minutu po konci, během nahrávání je „Uložit posledních
    2 min“ zakázané s tipem „Nahrává se celý tisk.“, další tisk = nový soubor, `decode` OK.
- Notifikace („Nahrávání kamery selhalo“ a klik na ni) sem nepatří: doplní je T-212 do těchto kontrol. Systémový dialog
  složky („Změnit…“) doplní T-213. Uříznutí textu se systémovým písmem pokryje T-214.
- Paměť dashcamu se skutečnou Buddy3D (+~17 MB) a obsah „~2 min před / ~30 s po“ se skutečnou kamerou zůstávají ruční
  (T-127).

## Akceptační kritéria
- [x] `helper decode` na `~/LayerPeek-samples/timelapse/t158-same-idr-pic-id.mp4` vrátí 5 dekódovaných snímků bez
      chyby a seek OK; na uměle poškozeném souboru (oříznutý `mdat`, vytvořený v dočasné složce) exit 1
- [x] Simulátor: `POST /_simulator/camera/offline|online` s testy v `tests/PrusaSimulator.Tests` a v `docs/simulator.md`
- [x] `mac.recording` = `PASS` (uzavře v T-127 bod **T-146** kromě částí přesunutých do T-212 / T-213 / T-214)
      → přesunuto do T-127
- [x] `mac.dashcam` = `PASS` (uzavře v T-127 simulátorovou část bodu **T-148** a simulátorovou část bodu **T-145**)
      → přesunuto do T-127
- [x] Nahrávka simulátoru přes `decode` OK (uzavře v T-127 bod **T-147**, část „kamera simulátoru“; skutečná Buddy3D
      viz otázka 2)
- [x] Po běhu žádný soubor mimo dočasnou složku (`mac.profile-unchanged` = `PASS`, `~/Movies/LayerPeek` beze změny)
- [x] Pester / `-SelfTest` a `dotnet build` / `dotnet test` bez chyb a varování

## Otevřené otázky
1. „Přehrát poslední nahrávku“ / „Otevřít složku s nahrávkami“: stačí ověřit, že aplikace předala systému správnou
   cestu? Šlo by to přes záznam v logu testovacího režimu nebo okno Finderu. Nebo má kontrola skutečně spustit
   QuickTime Player a ověřit jeho okno s názvem souboru (pomalejší, mění stav QuickTime, např. nedávné soubory)?
2. Stačí jako náhrada „nahrávky ze skutečné Buddy3D“ (T-147) nahrávka simulátoru, který přehrává skutečný klip
   Buddy3D (`--camera-clip ~/LayerPeek-samples/clips/coreone-20261103-1905-60s.mp4`), dekódovaná přes `decode`?
   Bitstream je skutečné kamery, síť a kamera ne.

Rozhodnuto uživatelem 2026-11-13: 1 **stačí ověřit předání cesty systému** (bez spouštění QuickTime); 2 **ano**, nahrávka simulátoru se
skutečným klipem Buddy3D nahrazuje skutečnou kameru.

3. (otevřená, zjištěno při implementaci 2026-11-15, implementaci neblokuje) **Nezapisovatelná složka nahrávek
   nastavená v `settings.json`:** rozsah předpokládá hlášku v Nastavení › Nahrávání. Aplikace ale hlášku
   `SettingsRecordingFolderNotWritable` („The chosen folder cannot be written to.“) ukáže jen po výběru složky přes
   „Změnit…“ (`SettingsViewModel.Recording.cs`, to ověří T-213). U složky už nastavené v souboru Nastavení nic neukáže.
   Start nahrávání pak selže s `FolderUnavailable` (log) a ohlásí se notifikací „Nahrávání kamery selhalo“ (T-212).
   Možnosti:
   a) stačí, že aplikace nespadne, nahrávání nezačne, nevznikne soubor a selhání se zaloguje. Kontrola `mac.recording`
      to dnes ověřuje a text v Nastavení jen zapíše;
   b) Nastavení má při otevření záložky Nahrávání zkontrolovat i uloženou složku a hlášku ukázat. To je nová funkce
      (nový úkol pro ui-dev), kontrola by pak hlášku vyžadovala.
   Do rozhodnutí zůstává v T-127 (oddíl 1l) samostatný neodškrtnutý podbod.

## Poznámky k implementaci
Implementováno 2026-11-15 na Macu (qa-reviewer, worktree `LayerPeek-t210`). Terminál bez Zpřístupnění, obrazovka během
posledního běhu zamčená.

- **Helper `decode <soubor.mp4>`** (`tools/macos-verify-helper.swift`, jen AVFoundation, bez nové závislosti):
  - JSON: `isPlayable`, `isReadable`, `duration`, kodek stopy (`avc1`), `width` / `height`, `nominalFrameRate`, `samples`
    (uložené vzorky), `decodedFrames`, `monotonicPts`, `seeks` (0, 25, 50, 75 % a poslední snímek, nulová tolerance,
    `actualTime`, `mean` a `stdDev` jasu ze zmenšeného šedého obrazu) a `problems`;
  - exit 1, když se některý vzorek nedekóduje, počet dekódovaných ≠ uložených, časy nerostou, seek selže nebo je
    obraz jednobarevný (směrodatná odchylka < 2);
  - čtení vzorků jde přes `AVAssetReader.outputProvider` (macOS 26+). macOS 27 synchronní `copyNextSampleBuffer`
    označil za zastaralé, proto zůstává jen jako záloha pro starší systémy v samostatné funkci;
  - helper se překládá bez varování; 60s klip Buddy3D (1284 snímků) dekóduje za ~1,7 s.
- **Další příkazy helperu:** `axwindows <pid>` (okna procesu s AXDocument, bez obsahu: složka okna Finderu) a
  `axclose <pid> <cesta>` (zavře okno s tím dokumentem tlačítkem zavření). Uzly `ax` mají navíc `enabled` a `help`
  (AXHelp), pokud je prvek má.
- **Simulátor:** `POST /_simulator/camera/offline` a `/online` (`SimulatedPrinterOptions.Camera`, sdílená kamera hostitele,
  stejné `RtspCamera.SetOffline` jako příkaz konzole, stejné přihlášení jako ostatní ovládací endpointy). Bez kamery
  vrací `404`. Stav s kamerou má navíc `camera_offline`, stav bez kamery se nemění (stávající test beze změny). Testy jsou
  v `tests/PrusaSimulator.Tests/SimulatorCameraControlTests.cs` (odpojení a odmítnutí klienta, znovu online, 404 bez
  kamery a pro GET, přihlášení). Popis je v `docs/simulator.md`.
- **Aplikace (jen Debug, testovací režim):** `RecordingFileLauncher` zapíše místo spuštění QuickTime Playeru plnou cestu
  souboru „Přehrát poslední nahrávku“ do `opened-files.txt` v testovacích datech (rozhodnutí 2026-11-13, otázka 1;
  stejný vzor jako `opened-urls.txt` z T-067). Odmítá totéž co `ProcessFileLauncher`, cestu do logu nepíše. Release
  a produkční režim se nemění. Testy: `RecordingFileLauncherTests`, `TestModeSwitchesTests`. Otevření složky zůstává
  skutečné (Finder), složka je v dočasné složce.
- **Kontroly** (`tools/macos-verify.ps1`, všechny v presetu `release`, `-ListChecks -Preset release` = 26 kontrol):
  - `mac.decode` (Auto): vzorek T-158 = 5 snímků a 5 seeků. Kopie oříznutá uprostřed prostředního `mdat`
    (`Get-VerifyMdatCut`) v dočasné složce běhu skončí exit 1. Bez vzorku `SKIP`.
  - `mac.recording-files` (Auto, bez Zpřístupnění, jde i se zamčenou obrazovkou): simulátor `--speed 20` se skutečným
    60s klipem Buddy3D (`--camera-clip`, otázka 2). Nahrávky jsou ve složce testovacích dat (`recording.folder`
    v `settings.json`) a `~/Movies/LayerPeek` se porovná před a po.
    - automatické nahrávání: výpadek kamery 10 s dá `…-2.mp4`, další tisk do minuty po konci dá nový soubor, konec
      nahrávání 50–90 s po konci tisku;
    - dashcam: `error`, `attention`, `stopped` → `…_dashcam-<spouštěč>.mp4` (25–160 s);
    - každý soubor projde `decode` bez chyby.
    Toto je simulátorová část spouštěčů T-148 a souborů T-145 z rozsahu `mac.dashcam`. Nepotřebuje menu, a proto ji šlo
    ověřit hned.
  - `mac.recording` (Permission, `Session` + `Accessibility`): podle rozsahu přes `menu` / `menupress`. Jeden běh pro
    každý režim okna (startMode), zastavení z menu (normální, celá obrazovka) a tlačítkem v okně (AXPress, malý náhled).
    „Přehrát poslední nahrávku“ → `opened-files.txt`, „Otevřít složku s nahrávkami“ → okno Finderu s podsložkou
    tiskárny (`axwindows`) a jeho zavření (`axclose`). Nakonec složka `chmod 0500`. Ověří se: start selže
    s `FolderUnavailable`, aplikace běží, žádný soubor; text v Nastavení › Nahrávání se zapíše (otázka 3). Nakonec
    `decode` všech tří nahrávek.
  - `mac.dashcam` (Permission, `Session` + `Accessibility`): „Uložit posledních 2 min“ v podmenu a v místní nabídce okna
    kamery (⌃-klik, `CameraSaveDashcamMenuItem`). Mimo tisk je zakázané a tip se zapíše; jiný tip než
    `DashcamNotPrinting` = chyba. Během tisku je povolené. Uložení z menu dá `…_dashcam-manual.mp4` (`decode` 25–160 s).
    S automatickým nahráváním je položka během tisku zakázaná s tipem `DashcamWholePrintRecorded` a v podmenu je
    „Zastavit nahrávání“.
  - čisté funkce ve `VerifyTools.psm1` (`ConvertFrom-VerifyRecordingFileName`, `Get-VerifyDecodeProblems`,
    `Get-VerifyMdatCut`, `Get-VerifyRecordingEvents`) mají Pester testy v bloku „Recordings“ v `tools/VerifyTools.Tests.ps1`.
    `New-TestData` má navíc `-CameraOptions` a `-RecordingFolder`, `Get-Simulator` má `-Clip`.
  - Dokumentace: tabulka kontrol a příprava (Zpřístupnění, vzorky) v `docs/macos-manual-testing.md`.
- **Ověřeno 2026-11-15** na commitu `ccef79f5` (čistý strom):
  `caffeinate -d -u -i pwsh tools/macos-verify.ps1 -Unattended -Only mac.decode,mac.recording-files,mac.recording,mac.dashcam,mac.profile-unchanged -Allow mac.recording,mac.dashcam`,
  běh `artifacts/verify/20261115-102144/` (5 min 19 s):
  - `PASS mac.decode`: 5 z 5 snímků, seeky se střední hodnotou jasu 100,9–105,7 a odchylkou ~67; kopie oříznutá na bajtu
    201374 z 391464 → exit 1 („Invalid sample cursor“);
  - `PASS mac.recording-files`: `…_Calibration cube.mp4` (8,8 s), `…_Calibration cube-2.mp4` po výpadku (16,1 s),
    `…_Phone stand.mp4` (další tisk, 73,1 s, konec 66 s po konci tisku), `…_dashcam-error.mp4` a `…-attention.mp4`
    (45 s), `…-stopped.mp4` (95 s). Vše `decode` bez chyby, `~/Movies/LayerPeek` beze změny (neexistuje);
  - `PASS mac.profile-unchanged` („no change“, Login Items se nekontrolují);
  - `SKIP mac.recording`, `SKIP mac.dashcam`: předběžné podmínky Session (zamčená obrazovka) a Accessibility (Terminál
    bez Zpřístupnění). Skutečný běh přes menu: T-127 oddíl 1l.
  Předchozí běh `20261115-100859` (odemčená obrazovka, bez Zpřístupnění) měl `mac.decode` i `mac.recording-files`
  `PASS` také.
- **Build a testy 2026-11-15:** `dotnet build` 0 varování, 0 chyb. Plný `dotnet test`: 10 736 testů, 10 465 prošlo,
  270 přeskočeno, 1 selhal: `SettingsWindowLayoutTests.CameraLocalNetworkHintWithItsButtonFitsInEveryLanguage(de)`
  v `LayerPeek.App.UiTests.SystemFont` (počet kliknutí na tlačítko 0 místo 1, test UI, tento úkol ho nemění).
  Samostatně prošel 8 z 8. Pester `tools/VerifyTools.Tests.ps1` 71 prošlo / 1 přeskočen (Windows), `-SelfTest` PASSED
  (36 kontrol), `tests/PrusaSimulator.Tests` 157 prošlo.
- Bez nových balíčků (AVFoundation je součást systému). Nic na GitHub.
- Uzavřeno 2026-11-15: zbývající ruční ověření přesunuto do T-127 (rozhodnutí uživatele)
