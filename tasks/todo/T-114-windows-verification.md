---
id: T-114
title: Ověření na Windows
status: todo
owner: qa-reviewer
priority: P1
platform: windows
depends_on: [T-017, T-055, T-058, T-101, T-103, T-105, T-106, T-107, T-109, T-110, T-111, T-112]
---

## Cíl
Sběrný úkol se vším, co zbývá ověřit na Windows: build a testy po úkolech vyvíjených na Macu, přegenerování windowsové
sady snímků UI, instalátor, autostart, chování specifické pro Windows a ruční kontroly. Vznikl rozhodnutím uživatele
2026-10-30: rozpracované úkoly, kterým chybělo jen ověření na druhém OS nebo ruční ověření, se uzavřely a zbývající body
se přesunuly sem (v původním úkolu odškrtnuté s „→ přesunuto do T-114“). Protějšek pro Mac je T-115.

Body pocházejí i z úkolů `macos` (T-021, T-026, T-053, T-095, T-097, T-102, uzavřené 2026-10-30) a z úkolů T-018 a T-100
(uzavřené 2026-10-30 po rozhodnutí otevřených otázek). Podle `tasks/README.md` úkol `windows` na úkolech `macos`
nezávisí, proto nejsou v `depends_on`.

Postup: [docs/windows-manual-testing.md](../../docs/windows-manual-testing.md) a skript `tools/windows-verify.ps1`.

## Rozsah
- Pořadí: nejdřív snímky (oddíl 1), protože bez nich selžou snímkové testy v `dotnet test` i v `release-build.ps1`;
  pak sestavení vydání a jeden běh `tools/windows-verify.ps1` (oddíl 2 a 3), ruční kroky, tiskárna a vydání.
- Výsledky (verze Windows, datum, výňatek z `artifacts/verify/<čas>/report.md`) zapsat do `## Poznámky k implementaci`.
  Každý `FAIL` nebo nalezená chyba = nový úkol. Bod se odškrtne až po úspěšném ověření.
- Přegenerované snímky commitnout na feature větvi tohoto úkolu (jen soubory ze seznamů níže, nesouvisející změny
  vrátit).
- Hesla ani API klíče nikdy nevkládat do chatu ani do úkolů.

## Akceptační kritéria

### 1. Snímky UI – windowsová sada (`tests/LayerPeek.App.UiTests/Snapshots/`, bez `macOS/`)
Postup: `$env:LAYERPEEK_UPDATE_SNAPSHOTS = '1'; dotnet test --project tests/LayerPeek.App.UiTests;
Remove-Item Env:LAYERPEEK_UPDATE_SNAPSHOTS`, potom `git status tests/LayerPeek.App.UiTests/Snapshots`, každý soubor
prohlédnout (porovnat s protějškem v `Snapshots/macOS/`), nesouvisející změny vrátit (`git restore`) a spustit testy znovu
bez proměnné – musí projít. Soubory se v seznamech opakují (několik úkolů měnilo tytéž); stačí jedno přegenerování.
- [ ] **T-101** (krit. „Texty v 8 jazycích, UI testy bez ořezání na obou platformách …“): změněné `settings-general.png`,
      `settings-general-errors.png`; nové `settings-general-test-sent-fr.png`, `settings-general-turned-off-de.png`,
      `settings-general-test-sent-dark.png`. UI testy `GeneralTabWithTheTestNotificationFitsInEveryLanguage`
      a `GeneralTabWithNotificationsTurnedOffFitsInEveryLanguage` na Windows projdou (Segoe UI, termín „Nerušit“).
- [ ] **T-058** (poznámka u krit. „`dotnet build` a `dotnet test` …“): změněné `settings-startup-updates.png`,
      `settings-startup-updates-not-installed.png` (volby kontroly aktualizací a „Zkontrolovat teď“).
- [ ] **T-103** (krit. „Snímky …“): změněné `camera-mini-strip-printing.png`, `camera-mini-strip-cooled-de.png`,
      `camera-mini-strip-rows.png`; nové `camera-mini-strip-finish-320.png`, `camera-mini-strip-finish-400-es.png`,
      `camera-mini-strip-cooling-240-it.png`; `camera-mini-strip-240-pl.png` se nemá změnit. Vizuální kontrola
      uživatelem (čas dokončení, ❄ / ⏻, horní pruh modře / zeleně).
- [ ] **T-106** (krit. „Snímky Windows přegenerované na Windows“): změněné `camera-widget-temperatures-m.png`,
      `camera-widget-temperatures-l.png`, `camera-mini-strip-rows.png`; nové `camera-widget-temperatures-steady-m.png`,
      `camera-widget-temperatures-steady-l.png`, `camera-mini-strip-chart-steady.png`.
- [ ] **T-109** (krit. „Snímky … Windows sadu přegenerovat na Windows“): změněné `camera-widget-cooldown-cooling-l.png`,
      `camera-widget-cooldown-cooling-m.png`, `camera-widget-cooldown-cooling-m-de.png`,
      `camera-widget-cooldown-cooling-s.png`, `settings-widgets-cooldown-de.png`, `settings-widgets-cooldown-error-fr.png`,
      `settings-widgets-dark.png`; nový `camera-widget-cooldown-cooling-s-bed.png` (vyhřívaná podložka s vlnkami).
      Zkontrolovat, zda se tlačítko „Použít prahy u všech tiskáren“ v Segoe UI nezalomí hůř než na macOS.
- [ ] **T-110** (krit. „Snímky …“): nové `settings-widgets-mini-strip.png`, `settings-widgets-mini-strip-de.png`,
      `settings-widgets-mini-strip-fr.png`, `settings-widgets-mini-strip-dark.png`,
      `settings-widgets-mini-strip-camera-off-it.png`, `camera-mini-strip-custom-240.png`,
      `camera-mini-strip-no-rows-320.png`; změněné `settings-widgets.png`, `settings-widgets-dark.png`,
      `settings-widgets-camera-off-it.png`, `settings-widgets-cooldown-de.png`, `settings-widgets-cooldown-error-fr.png`.
      Poznámka `MiniStripHint` bez výpustky.
- [ ] **T-111** (krit. „Snímky …“): všechny `settings-*.png` – `settings-camera-notice`, `settings-camera-preview-4x3-de`,
      `settings-camera-preview`, `settings-camera-window-errors-fr`, `settings-camera-window-off-cs`,
      `settings-camera-window`, `settings-camera`, `settings-errors-fr`, `settings-general-errors`,
      `settings-general-test-sent-dark`, `settings-general-test-sent-fr`, `settings-general-turned-off-de`,
      `settings-general`, `settings-maximized`, `settings-printer-images`, `settings-prusaconnect`, `settings-prusalink`,
      `settings-startup-updates-not-installed`, `settings-startup-updates`, `settings-validation-cs`,
      `settings-validation`, `settings-widgets-camera-off-it`, `settings-widgets-cooldown-de`,
      `settings-widgets-cooldown-error-fr`, `settings-widgets-dark`, `settings-widgets-mini-strip-camera-off-it`,
      `settings-widgets-mini-strip-dark`, `settings-widgets-mini-strip-de`, `settings-widgets-mini-strip-fr`,
      `settings-widgets-mini-strip`, `settings-widgets-no-printers`, `settings-widgets` (a ostatní `settings-*` změněné
      svislým vystředěním textu tlačítek, otázka Q2). Tlačítka vysoká aspoň 32 DIP, text svisle uprostřed.
- [ ] **T-112** (krit. „Snímky …“): změněné `settings-validation.png`, `settings-validation-cs.png`,
      `settings-errors-fr.png`, `settings-camera-window-errors-fr.png`, `settings-widgets-cooldown-error-fr.png`,
      `settings-general-errors.png`; nové `settings-invalid-address-focused.png`,
      `settings-camera-window-error-normal-size.png`, `settings-widgets-cooldown-error-nozzle.png`,
      `settings-general-errors-focused.png`, `settings-errors-dark.png`, `settings-general-errors-dark.png`. Hlavně
      `settings-general-errors-focused` a `settings-camera-window-error-normal-size` (jiné vyhlazení písma a hran).

### 2. Build, testy, AOT a E2E (`tools/windows-verify.ps1`)
Postup: `pwsh scripts/release-build.ps1` (instalátor pro oddíl 3, AOT publish s `-warnaserror`), pak
`pwsh tools/windows-verify.ps1 -ListChecks` a `pwsh tools/windows-verify.ps1` (interaktivně).
- [ ] **T-095** (krit. „Na Windows `dotnet build` a `dotnet test` beze změny výsledku“): `win.build` a `win.test` =
      `PASS` (mimo jiné `SnapshotAssert` s windowsovou sadou, `CultureTime`, `aot-smoke.ps1` se zálohou,
      `profile-snapshot.ps1` s `cmdkey`; viz otázka „Ověření na Windows“ v T-095).
- [ ] **T-018** (krit. „Windows chování z T-007 zůstává beze změny“ a „`dotnet build` a `dotnet test` … na macOS
      i Windows“, část Windows): `win.build`, `win.test` a `win.e2e` = `PASS`; `Snapshots/TrayIcons/` beze změny.
- [ ] **T-021** (krit. „Windows build a testy beze změny chování“): `win.build` a `win.test` = `PASS`
      (`WindowsCredentialSecretStore` a jeho testy beze změny).
- [ ] **T-026** (krit. „Windows build a testy beze změny chování“): `win.build`, `win.test` a `win.e2e` = `PASS`;
      DLL FFmpeg z `build/FFmpeg.targets` jsou ve výstupu jako dřív a kamera simulátoru v E2E běží.
- [ ] **T-053** (krit. „… Windows build a testy beze změny chování“): `win.build`, `win.test` a `win.aot-smoke` = `PASS`.
- [ ] **T-097** (krit. „Windows beze změny“): `win.build`, `win.test`, `win.aot-smoke` a `win.e2e` = `PASS`.
- [ ] **T-100** (krit. „Windows beze změny“): `win.test` a `win.e2e` = `PASS` (F11, Esc a zkratky Nastavení na Windows
      jako dřív, tooltipy ukazují zkratky Windows).
- [ ] **T-101** (krit. „`dotnet build` a `dotnet test` bez chyb a varování“, část Windows): `win.build` a `win.test` =
      `PASS`.
- [ ] **T-058** (krit. „… AOT publish `win-x64` bez varování“): `pwsh scripts/release-build.ps1` (AOT s `-warnaserror`)
      i `win.aot-smoke` bez varování trimmingu / AOT.
- [ ] **T-105** (krit. „Skripty spuštěné uživatelem“, část Windows): `tools/windows-verify.ps1` proběhl celý
      interaktivně (i `-RestoreOnly` po případném přerušení); nalezené chyby skriptu zapsané a opravené (nový úkol).

### 3. Instalátor, autostart a chování Windows
- [ ] **T-017** (krit. „Odinstalace smaže registraci COM aktivátoru toastů … ; po instalaci kliknutí na toast …
      otevře web tiskárny, i když aplikace neběží“): `win.e2e-installer`, `win.install`, `win.toast-click`
      a `win.uninstall` = `PASS` / potvrzeno (`docs/windows-manual-testing.md`, „Skutečná instalace“).
- [ ] **T-017** (krit. „Autostart po přihlášení funguje na Windows“): `win.autostart` (zapnout v nainstalované aplikaci,
      odhlásit, přihlásit, `pwsh tools/windows-verify.ps1 -Only win.autostart`), potom `win.uninstall`.
- [ ] **T-101** (krit. „Ověřeno uživatelem na Macu i Windows“, část Windows): tlačítko „Poslat zkušební upozornění“
      pošle toast (zvuk podle hodnoty vybrané v okně, i neuložené), druhý klik předchozí toast nahradí; po vypnutí
      v Nastavení → Systém → Oznámení varování „Upozornění jsou ve Windows vypnutá.“ s tlačítkem, které otevře
      `ms-settings:notifications`; klik na zkušební toast přenese okno Nastavení na záložku Obecné.
- [ ] **T-102** (krit. „Ověřeno na Windows: tlačítka na hlavním panelu podle pravidla, ‚Zobrazit kameru‘, blikání“):
      kroky W1–W3 v T-102 („Ruční ověření na Windows“) a `pwsh tools/windows-verify.ps1 -Only win.taskbar`.
- [ ] **T-017** (krit. „Instalátor `win-arm64` vznikne stejným skriptem, pokud je `win-arm64` zapnutý“): po doinstalaci
      komponenty MSVC ARM64 jako správce (`docs/prerequisites.md`, T-063 bod 1) `pwsh scripts/release-build.ps1 -Runtime
      win-x64,win-arm64` vytvoří i `LayerPeek-<verze>-win-arm64-Setup.exe` a soubory kanálu `win-arm64` bez kolize
      s `win-x64`; nic se nenahrává. Spuštění na zařízení ARM64 se tu neověřuje (T-057). Lze spojit s T-063.

### 4. Se skutečnou tiskárnou
- [ ] **T-107** (krit. „Ruční ověření uživatelem se skutečnou tiskárnou“, část Windows): Edge i Chrome, menu tiskárny →
      Otevřít web – web PrusaLink přes proxy se načte, zobrazí stav, nahraje a stáhne soubor; v konzoli prohlížeče žádná
      chyba kvůli Service Workeru nebo hlavičkám proxy.

### 5. Vydání (zapisuje na GitHub – jen s výslovným souhlasem uživatele, postup `docs/release.md`)
- [ ] **T-055** (krit. „Zkušební vydání … následné přidání souborů druhým během“, část Windows): ve zkušebním vydání
      z T-115 (běh na Macu první) doplní Windows své soubory (`release-upload.ps1 -AddToPublished`, `docs/release.md`,
      „Zkouška doplnění druhého OS (T-113)“); `release-publish.ps1 -DryRun` bez `-AllowIncomplete` pak projde.
- [ ] **T-058** (krit. „Klik na položku / notifikaci otevře stránku vydání, případně spustí aktualizaci přes Velopack“,
      část Windows): skutečný klik na položku „Je dostupná nová verze X.Y.Z“ v menu a na banner toastu; nainstalovaná
      starší verze se přes Velopack aktualizuje z GitHub Releases (bez tokenu) a znovu spustí; `.zip` verze otevře
      stránku vydání.
- [ ] **T-017** (otevřená otázka SmartScreen): stažení `Setup.exe` z GitHub vydání v Edge – zapsat varování SmartScreen
      při stahování a doplnit ho do `docs/release.md` (spuštění se značkou Mark-of-the-Web ověřil T-056).

### 6. Závěr
- [ ] Výsledky zapsané v poznámkách (verze Windows, datum, zpráva skriptu); každá chyba má nový úkol; profil vrácený
      (`win.uninstall`, případně `pwsh tools/windows-verify.ps1 -RestoreOnly`).

## Otevřené otázky
- (žádné vlastní; T-113 Q1 rozhodnuto 2026-10-30: doplnění druhého OS se poprvé ověří ostře u 0.3.1)

## Poznámky k implementaci
(vyplní agent / uživatel po ověření)
