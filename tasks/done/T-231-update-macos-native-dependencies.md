---
id: T-231
title: macOS – aktualizace nativních závislostí a nástrojů (FFmpeg ze zdrojů, Xcode, runner CI)
status: done
owner: qa-reviewer
priority: P2
platform: macos
depends_on: []
---

## Cíl
Zadání uživatele 2026-11-15: aktualizovat všechny knihovny na Macu i na Windows. Tento úkol pokrývá nativní závislosti
a nástroje, které používá jen macOS; spravované balíčky řeší T-230, Windows T-232.

## Rozsah
- **FFmpeg** (`build/FFmpeg.targets`, `build/macos/build-ffmpeg.sh`): dnes oficiální zdroj 9.0.2 (2026-11-15
  nejnovější vydání řady 9.0 na https://ffmpeg.org/releases/). Ověřit, jestli nevyšlo novější vydání (oprava řady 9.0,
  nebo nová hlavní verze – ta jen po rozhodnutí uživatele, viz otázky), aktualizovat URL, SHA-256 a složku, nechat
  přestavět cache, ověřit licenci (LGPL v3 or later, `--enable-version3`), věty v okně O aplikaci (T-199) a zdroj
  k vydání. Testy kamery (`LayerPeek.Camera.FFmpeg.Tests` i s hardwarovým dekódováním, T-203) a měření z T-205 namátkou.
- **Xcode / Command Line Tools a Swift** (`tools/macos-verify-helper.swift`, `tools/render-macos-icon.swift`): build
  na aktuální verzi bez varování; `docs/prerequisites.md` aktualizovat, mění-li se minimální verze.
- **Runner CI** (T-218): obraz macOS (`xcode-27`, zatím preview) – přejít na stabilní označení, jakmile ho GitHub nabídne;
  připnuté SHA akcí aktualizuje Dependabot (T-218), zkontrolovat, že jeho PR procházejí.
- Swift helper ani skripty nesmí záviset na Homebrew.

## Akceptační kritéria
- [x] FFmpeg na nejnovějším vydání zvolené řady (nebo zdůvodněno, proč beze změny); cache, licence, zdroj k vydání OK
- [x] Testy kamery a `dotnet test` bez chyb; AOT publish osx-arm64 bez varování; `mac.release-bundle` (T-217) PASS
- [x] Swift nástroje se přeloží bez varování na aktuálním Xcode
- [x] `docs/prerequisites.md` odpovídá

## Otevřené otázky
1. Přechod na novou **hlavní** verzi FFmpeg (pokud vyjde) – jen po rozhodnutí uživatele (dopad na API, Windows build
   T-202 musí jít stejnou verzí). – K 2026-11-15 nová hlavní verze nevyšla, rozhodnutí zatím netřeba.

## Poznámky k implementaci
Ověřeno 2026-11-15 na macOS 27.2.1, Command Line Tools 27.2, Swift 6.4.2.

- **FFmpeg beze změny:** https://ffmpeg.org/releases/ i tagy oficiálního gitu (`git ls-remote --tags
  https://git.ffmpeg.org/ffmpeg.git`) končí na `9.0.2` / `n9.0.2`; novější oprava řady 9.0 ani nová hlavní verze
  (10.x) nevyšla. URL, SHA-256 a složka v `build/FFmpeg.targets` zůstávají, cache se nepřestavuje (klíč beze změny),
  licence (LGPL v3 or later, `--enable-version3`), věty v okně O aplikaci (T-199) a zdroj k vydání platí dál. Windows
  URL (BtbN) jsem neměnil (T-202 / T-232). Měření z T-205 jsem neopakoval: knihovny jsou bitově stejné.
- **Swift nástroje:** `xcrun swiftc -O` přeloží `tools/macos-verify-helper.swift` i `tools/render-macos-icon.swift`
  bez jediného varování; `swift tools/render-macos-icon.swift --out <dočasná složka>` dá PNG bajtově shodné
  s repozitářem. Binárky nelinkují nic z Homebrew (`otool -L`). Minimální verze se nemění, proto
  `docs/prerequisites.md` beze změny (žádnou minimální verzi CLT neuvádí).
- **Runner CI:** podle README `actions/runner-images` (2026-11-15) je macOS 27 stále jen `xcode-27` (preview),
  stabilní `macos-27` neexistuje; `macos-latest` = macOS 26. `.github/workflows/ci.yml`, docs a `CiWorkflowTests`
  beze změny.
- **Dependabot:** `gh pr list --state all` nevrací žádný PR (ani od Dependabotu); nebylo co kontrolovat.
- **Oprava chyby na `develop`:** `dotnet build -c Release` padal na
  `tests/LayerPeek.App.Tests/Installer/AutostartChoiceMigrationTests.cs` (T-201): `AppEnvironment.FromVariables` je
  jen v `#if DEBUG`. Testovací režim jsem obalil `#if DEBUG` (stejně jako `UpdateInstallationTests`), ostatní
  kontroly testu běží i v Release. Release build tím padal i v `scripts/release-build.ps1` a v kroku CI „Build
  (Release)“.
- **Ověření:** `dotnet build` 0 varování; `dotnet test` (Debug, před sloučením `develop`) 0 chyb (testy kamery 208
  OK, i hardwarové dekódování); `dotnet publish src/LayerPeek.App -c Release -r osx-arm64` bez varování IL / trim / AOT
  (jen známé hlášky `dsymutil` / `xcodebuild` jako v T-229).
- **Vydání (po T-233, `develop` 3bdc359a):** `caffeinate -d -u -i pwsh scripts/release-build.ps1 -SkipFeedSignature`
  (0.5.0): `dotnet test (Release)` 10 412 OK / 0 chyb, smoke obou testů PASS. Pak `caffeinate -d -u -i pwsh
  tools/macos-verify.ps1 -Unattended -Only mac.release-bundle,mac.profile-unchanged`: oba `PASS`
  (`artifacts/verify/20261115-113744/`). První pokus selhal jen v
  `FFmpegCameraFeedAllocationTests.SteadyStateFramesThroughTheHubDoNotAllocate` (19 872 B místo 0) při souběžném
  `dotnet test` agenta T-212; samostatně 3× třída a 1× celý projekt v Release OK, opakované vydání po skončení
  souběžného běhu prošlo celé. Dočasně padal i E2E test z T-233 (Edge 157) – opraveno v T-233.
- **Profil LayerPeek:** `tools/profile-snapshot.ps1 -Save` před a `-Compare` po vydání a kontrole: „No change“
  (Login Items nečitelné – `sfltool dumpbtm` čeká na heslo správce, proces jsem po chvíli ukončil).
- Žádná nová závislost.
