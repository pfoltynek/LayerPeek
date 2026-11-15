---
id: T-238
title: Testovací režim – volitelné skutečné otevírání odkazů přes /usr/bin/open pro `mac.open-fake` (T-053 L4)
status: done
owner: ui-dev
priority: P3
platform: all
depends_on: [T-216]
---

## Cíl
Kontrola `mac.open-fake` (T-216) má ověřit, že aplikace otevírá odkazy přes `/usr/bin/open` (ne přes `open` z `PATH`,
T-053 L4) a že výchozí prohlížeč načte web PrusaLink přes proxy. Testovací bundle je ale Debug v izolovaném testovacím
režimu a ten adresy jen zapisuje do `opened-urls.txt` (`RecordingUrlLauncher`, T-067); `ProcessUrlLauncher` se
v kontrole vůbec nespustí, prohlížeč se neotevře a žádný požadavek na proxy nepřijde. Kontrola proto nikdy nemůže
skončit `PASS`.

## Zjištění (první běh s odemčenou obrazovkou, 2026-11-15)
- Běh `artifacts/verify/20261115-155430/` (commit `24d73aa3`): `FAIL` „the repository link did not bring the default
  browser (com.apple.safari) to the front …; the web proxy answered no request with 200 within 30 s after Open PrusaLink
  (answers: )“. Log aplikace (`evidence/mac.open-fake/app-vb986f349.log`): „RecordingUrlLauncher: Test mode: an address
  on github.com was recorded instead of opening the browser.“ a totéž pro 127.0.0.1.
- Od větve `feature/macos-verify-first-real-run` kontrola v tomto případě končí `SKIP` s důvodem (odkazuje sem),
  zapsanou adresu proxy načte sama (ověří proxy) a dál hlídá, že se falešné `open` nespustilo a licence se otevřou.

## Rozsah
- Debug sestavení: proměnná prostředí testovacího režimu (např. `LAYERPEEK_TEST_REAL_URL_LAUNCHER=1`), se kterou
  `AppServices` i v izolovaném režimu použije `ProcessUrlLauncher`. Release beze změny (testovací režim nemá).
- `mac.open-fake` proměnnou nastaví a `SKIP` větev zůstane jen pro starší sestavení.
- Test registrace (proměnná zapnutá / vypnutá) v `tests/LayerPeek.App.Tests`.

## Akceptační kritéria
- [x] S proměnnou otevře Debug testovací režim odkazy přes `/usr/bin/open`, bez ní je zapisuje jako dosud.
- [x] `mac.open-fake` = `PASS` s odemčenou obrazovkou (uzavře v T-127 bod T-053, oddíl 1q).
- [x] `dotnet build` a `dotnet test` bez chyb a varování.

## Otevřené otázky
1. ~~Smí testovací režim dostat přepínač?~~ **Rozhodnutí uživatele 2026-11-15: ano** – přidat proměnnou testovacího režimu (Debug), se kterou se odkazy otevřou přes `/usr/bin/open` ve skutečném prohlížeči; Release beze změny.

## Poznámky k implementaci
- **Aplikace (jen Debug):** `AppEnvironment.TestRealUrlLauncherVariable` = `LAYERPEEK_TEST_REAL_URL_LAUNCHER`,
  vlastnost `TestRealUrlLauncher` (jen hodnota `1`), parametr `realUrlLauncher` v `ForTests`. Jako ostatní přepínače
  testovacího režimu platí jen s `LAYERPEEK_TEST_DATA_DIR`, jinak se ignoruje a zaloguje (`IgnoredTestSwitches`).
  `AppServices` pak i v izolovaném režimu registruje `ProcessUrlLauncher` (na macOS `/usr/bin/open`, na Windows
  shell, `UseShellExecute`), bez něj dál `RecordingUrlLauncher` (`opened-urls.txt`). Ostatní náhrady testovacího režimu
  (`RecordingFileLauncher` atd.) beze změny. Vše za `#if DEBUG`; Release kód beze změny (vlastnost v Release neexistuje,
  hlídá `AppEnvironmentTests.ReleaseBuildsHaveNoTestMode`).
- **Kontrola `mac.open-fake`:** prostředí testovací aplikace skládá nová čistá funkce `Get-VerifyOpenFakeEnvironment`
  (`tools/VerifyTools.psm1`: `PATH` s falešným `open` + přepínač). Větev `SKIP` (`Test-VerifyUrlsRecorded`) zůstala jen
  pro sestavení starší než T-238; texty důvodu a komentáře upravené.
- **Testy:** `TestModeSwitchesTests.TheRealUrlLauncherIsOnlyForTheTestModeWithItsSwitch` (hodnoty, mimo testovací režim)
  a `TestModeWithTheRealUrlLauncherSwitchStartsTheBrowser` (registrace s / bez přepínače, `opened-urls.txt` nevznikne);
  Pester test `Get-VerifyOpenFakeEnvironment` v `tools/VerifyTools.Tests.ps1`; Release kontrola v `AppEnvironmentTests`.
- **Dokumentace:** `docs/architecture.md` (bod 9, otevření webu PrusaLink) a řádek `mac.open-fake`
  v `docs/macos-manual-testing.md`.
- **Ověřeno na macOS (2026-11-15):** `dotnet build` (Debug, celé řešení) a `dotnet build src/LayerPeek.App -c Release`
  0 varování; `dotnet test` 11 012 testů, 10 725 prošlo, 287 přeskočeno, 0 selhalo; Release build
  `LayerPeek.App.Tests` a test `ReleaseBuildsHaveNoTestMode` prošly. Živá kontrola z Terminálu
  `caffeinate -d -u -i pwsh tools/macos-verify.ps1 -Unattended -Only mac.open-fake` (obrazovka odemčená) = `PASS`
  (`artifacts/verify/20261115-203317/`): falešné `open` se nespustilo, odkaz na GitHub přenesl Safari dopředu, licence se
  otevřely, „Open PrusaLink“ → web simulátoru přes proxy (`GET / 200`, `GET /api/v1/status 200`), prohlížeč spuštěný
  kontrolou ukončen. V T-127 odškrtnut bod T-053 (a poznámka v oddílu 1q). Pozn.: `-Allow mac.open-fake` skript odmítne
  (kontrola je `Auto`, ne `Permission`), spouští se jen s `-Only`.
- Windows: přepínač je společný, ale neověřeno (na Windows ho žádná kontrola nepoužívá).
- Bez nových balíčků.
