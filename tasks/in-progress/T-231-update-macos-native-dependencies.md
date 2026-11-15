---
id: T-231
title: macOS – aktualizace nativních závislostí a nástrojů (FFmpeg ze zdrojů, Xcode, runner CI)
status: in-progress
owner: qa-reviewer
priority: P2
platform: macos
depends_on: [T-233]
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
- [ ] Testy kamery a `dotnet test` bez chyb; AOT publish osx-arm64 bez varování; `mac.release-bundle` (T-217) PASS
  (testy kamery, AOT publish OK; `dotnet test` a tím i `release-build.ps1` blokuje T-233, viz poznámky)
- [x] Swift nástroje se přeloží bez varování na aktuálním Xcode
- [x] `docs/prerequisites.md` odpovídá

## Otevřené otázky
1. Přechod na novou **hlavní** verzi FFmpeg (pokud vyjde) – jen po rozhodnutí uživatele (dopad na API, Windows build
   T-202 musí jít stejnou verzí).

## Poznámky k implementaci
Stav 2026-11-15 (macOS 27.2.1, Command Line Tools 27.2, Swift 6.4.2): **úkol zůstává v `in-progress`**, protože
`mac.release-bundle` nejde spustit, dokud neprojde T-233 (viz níže). Ostatní je hotové.

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
  (jen známé hlášky `dsymutil` / `xcodebuild` jako v T-229). Po sloučení `develop` (T-211, T-230) padá v
  `release-build.ps1` (krok `dotnet test (Release)`) jediný test `ProxyOriginGuardTests.ClassicFormPostOfTheProxied
  PageReachesThePrinter` – stále i samostatně, v Debug i Release, s Edge 157.0.4341.60 (při prvním běhu prošel, Edge
  se mezitím zřejmě aktualizoval). To je **T-233**; `release-build.ps1` nemá přepínač bez testů a test oslabovat
  nesmím, proto `mac.release-bundle` čeká na T-233. Profil LayerPeek: `tools/profile-snapshot.ps1 -Compare` „No
  change“ (Login Items nečitelné – `sfltool dumpbtm` čekal na heslo správce, proces jsem ukončil).
- Dokončení: po T-233 spustit `caffeinate -d -u -i pwsh scripts/release-build.ps1 -SkipFeedSignature` a
  `caffeinate -d -u -i pwsh tools/macos-verify.ps1 -Unattended -Only mac.release-bundle,mac.profile-unchanged`,
  odškrtnout krit. 2 a přesunout do `done/`.
- Žádná nová závislost.
