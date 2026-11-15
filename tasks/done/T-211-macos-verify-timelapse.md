---
id: T-211
title: macOS ověření – timelapse na testovacím bundlu (ruční start / stop, po vrstvách, indikátor, dekódování)
status: done
owner: qa-reviewer
priority: P2
platform: macos
depends_on: [T-210]
---

> **Rozhodnuto uživatelem 2026-11-13:** přehrání v QuickTime Playeru nahrazuje úplné dekódování přes AVFoundation
> (`helper decode` z T-210). Kontrola přes Accessibility nahrazuje poslech VoiceOveru.

## Cíl
Automatizovat simulátorové části bodů T-161 (timelapse z menu a z okna kamery) a T-159 (volba „Každou N. vrstvu“)
v T-127 a přehrání timelapse v QuickTime (T-160).

## Rozsah
- **Kontrola `mac.timelapse`** (Permission: Zpřístupnění, preset `release`):
  - testovací bundle a simulátor s kamerou, `--speed 20`, složka nahrávek v dočasné složce (jako T-210);
  - `menupress …>Spustit timelapse (experimentální)` → položka se změní na „Zastavit timelapse (experimentální)“;
  - v okně kamery i v malém náhledu je indikátor „TL“ (AX identifikátor a popisek);
  - během scénáře `paused` je indikátor ztlumený. Ztlumení se pozná podle stavu v AX, pokud ho aplikace vystavuje.
    Jinak porovnáním dvou snímků oblasti indikátoru (`regiondiff`, jen se Záznamem obrazovky, bez něj `SKIP` této
    části);
  - zastavení tlačítkem v okně kamery: AXPress, AX popisek „Zastavit timelapse (experimentální)“;
  - „Přehrát poslední timelapse“ je pak povolené (předání souboru systému jako v T-210, otázka 1 tam);
  - soubor `…_timelapse.mp4` projde `decode` OK a počet snímků odpovídá intervalu a době tisku.
- **Kontrola `mac.timelapse-layers`** (Permission, preset `release`): na záložce Nahrávání přepnout volbu „Pořizovat
  snímek“ na „Každou N. vrstvu (odhad)“. Číslo vedle se vymění (interval ↔ „Vrstev mezi snímky“, AX popisky) a tip
  vysvětlí odhad z osy Z (AXHelp). Uložit, znovu otevřít: volba zůstala, je i v `settings.json`. Tisk simulátoru,
  jehož `axis_z` roste po vrstvách: `decode` a počet snímků ≈ počet vrstev + 2 (tolerance podle T-159). Pauza snímek
  nepřidá.
- Selhání timelapse s notifikací („Timelapse selhal“) doplní T-212, uříznutí textu se systémovým písmem T-214.
- Skutečná tiskárna (T-159 vrstvy 0,1 mm, záznam `record-z.sh`) a měření CPU / velikosti s Buddy3D (T-160) zůstávají
  ruční. Přehrání vzniklého `…_timelapse.mp4` z T-160 nahrazuje `helper decode`.

## Akceptační kritéria
- [x] `mac.timelapse` = `PASS` (uzavře v T-127 bod **T-161** „Ruční start / stop …, indikátor …, tray menu …“ kromě
      notifikace (T-212) a uříznutí (T-214); zjištění AXHelp zakázané položky zapsané)
      → přesunuto do T-127
- [x] `mac.timelapse-layers` = `PASS` (uzavře v T-127 bod **T-159** „volba je v UI a uloží se“; VoiceOver nahrazený
      AX popisky) → přesunuto do T-127
- [x] U bodu **T-160** v T-127 je přehrání v QuickTime nahrazené `helper decode` (část měření se skutečnou kamerou
      zůstává)
- [x] Pester / `-SelfTest` a `dotnet build` / `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
Implementováno 2026-11-15 na Macu (qa-reviewer, worktree `LayerPeek-t211`), postupem T-210. Terminál bez Zpřístupnění,
obrazovka během běhu zamčená.

- **Simulátor `--layers <n>`** (1–1000, `SimulationTiming.Layers`): osa Z tisku stoupá po `n` vrstvách 0,2 mm (vrstva k
  průběhu = k × 0,2 mm), i pauza drží vrstvu; bez volby plynule jako dosud. Stav tiskárny pro skript čte
  `GET /api/v1/status` s `X-Api-Key` (bez změny simulátoru). Testy: `PrinterSimulationTests` (plynulá Z beze změny,
  schody vrstev, pauza drží vrstvu, **odhad vrstev aplikace `LayerChangeDetector` s pollingem 1 s při `--speed 20`
  najde přesně 11 změn u 12 vrstev i s pauzou**), `SimulatorHostTests` (parsování, neplatné hodnoty). `docs/simulator.md`.
- **Kontroly** (`tools/macos-verify.ps1`, všechny v presetu `release`, teď 29 kontrol):
  - `mac.timelapse-files` (Auto, bez Zpřístupnění, i se zamčenou obrazovkou): simulátor `--speed 20 --layers 12` se
    skutečným klipem Buddy3D, polling 1 s, `timelapsePrints`, složka nahrávek v testovacích datech. (1) Po čase,
    interval 5 s: 30 s tisku, 30 s `paused`, 20 s tisku, ukončení aplikací (dokončí soubor). Počet snímků musí ležet
    v rozsahu `Get-VerifyTimelapseFrameRange` (snímek na začátku úseku + 1 za interval, tolerance 7 s na úsek: dotaz,
    start, klíčový snímek). Snímky pauzy by daly +6 a rozsah překročily. (2) Po vrstvě: celý tisk s pauzou 15 s
    → **přesně** 13 snímků (`Get-VerifyLayerTimelapseFrames`: začátek + 11 změn + hotový díl). Tolerance T-159 platí pro
    skutečné tisky (krátké vrstvy, z-hop), schody simulátoru je nemají, proto přesně. Oba soubory projdou `decode`.
  - `mac.timelapse` (Permission, `Session` + `Accessibility`): podle rozsahu přes `menu` / `menupress`. Před prvním
    timelapse je „Přehrát poslední timelapse“ zakázané (AXHelp se zapíše, jiný tip než `MenuNoTimelapseYet` = chyba).
    Běh v normálním okně: start z menu → „Zastavit timelapse…“, „TL“ (`CameraTimelapseIndicator`), tlačítko
    `CameraTimelapseButton` s názvem „Stop timelapse (experimental)“. Pauza: **ztlumení aplikace vystavuje v AX** (název
    tlačítka má druhý řádek `TimelapsePaused`, položka menu tip). Proto se `regiondiff` ani Záznam obrazovky
    nepoužívají. Zastavení AXPress, pak „Přehrát poslední timelapse“ → `opened-files.txt` (stejný `RecordingFileLauncher`
    jako T-210). Běh v malém náhledu: „TL“ a zastavení z menu. `decode` a počet snímků obou souborů podle intervalu.
  - `mac.timelapse-layers` (Permission): Nastavení › Nahrávání, `SettingsTimelapseTrigger` (název, zobrazená volba, AXHelp
    = `SettingsTimelapseTriggerHint`), viditelné číslo a jeho název a tip, druhé skryté. Přepnutí: klik otevře seznam,
    pak klik na položku „Every N-th layer (estimate)“, nebo záloha ↓ a Return. Uložit (`SettingsSave`) →
    `recording.timelapseTrigger` = `layer` v `settings.json`. Znovu otevřené Nastavení volbu ukáže. Počet snímků tisku
    po vrstvách pokrývá `mac.timelapse-files`.
  - `New-TestData` má `-RecordingOptions` a `-PollingSeconds`, `Get-Simulator` má `-Layers`. Nové funkce skriptu
    `Get-SimulatorPrinterState`, `Get-TimelapseFiles`, `Test-Timelapse`.
  - čisté funkce ve `VerifyTools.psm1`: `Get-VerifyRecordingEvents` zná `TimelapseStarted` (soubor, režim) a
    `TimelapseFailed` (nejsou to `Failed` nahrávání), nové `Get-VerifyTimelapseFrameRange` a
    `Get-VerifyLayerTimelapseFrames`. Pester testy jsou v bloku „Recordings“.
  - Dokumentace: tabulka kontrol, Zpřístupnění a vzorky v `docs/macos-manual-testing.md`.
- **Neověřeno proti skutečnému oknu** (nešlo bez Zpřístupnění): výběr položky seznamu Avalonia přes klik / ↓ Return
  a to, zda skrytý `NumericUpDown` ve stromu AX chybí (kontrola bere chybějící prvek nebo nulovou velikost jako skrytý).
  Menu z T-209 zatím také nikdo proti skutečnému menu neověřil (T-127 1j). Poznámka je v T-127 1m.
- **Ověřeno 2026-11-15** na commitu `69dc7d3b` s neuloženými změnami tohoto úkolu:
  `caffeinate -d -u -i pwsh tools/macos-verify.ps1 -Unattended -Only mac.timelapse-files,mac.timelapse,mac.timelapse-layers,mac.profile-unchanged -Allow mac.timelapse,mac.timelapse-layers`,
  běh `artifacts/verify/20261115-104923/` (4 min 1 s):
  - `PASS mac.timelapse-files`: po čase `…_Calibration cube_timelapse.mp4` 11 snímků (rozsah 8–14, snímky pauzy by
    daly ~18), 0,37 s; po vrstvě `…_Phone stand_timelapse.mp4` 13 snímků (očekáváno 13), 0,43 s. Vše `decode` bez chyby,
    `~/Movies/LayerPeek` beze změny;
  - `PASS mac.profile-unchanged`;
  - `SKIP mac.timelapse`, `SKIP mac.timelapse-layers`: předběžné podmínky Session (zamčená obrazovka) a Accessibility.
    Skutečný běh: T-127 oddíl 1m.
- **Build a testy 2026-11-15:** `dotnet build` 0 varování, 0 chyb. Plný `dotnet test`: 10 838 testů, 0 selhalo,
  10 567 prošlo, 271 přeskočeno. Pester `tools/VerifyTools.Tests.ps1` 74 prošlo / 1 přeskočen (Windows), `-SelfTest`
  PASSED (39 kontrol).
- Bez nových balíčků. Nic na GitHub.
- Uzavřeno 2026-11-15: běhy přes menu a Nastavení přesunuty do T-127 (oddíl 1m) podle `tasks/README.md`.
