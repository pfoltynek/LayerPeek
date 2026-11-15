---
id: T-227
title: Windows ARM64 – ověření na zařízení ARM64 (aplikace, kamera, instalátor, aktualizace)
status: todo
owner: qa-reviewer
priority: P2
platform: windows
depends_on: [T-226]
---

> **Rozhodnutí uživatele 2026-11-12:** LayerPeek se vydává i pro `win-arm64` (série T-224 až T-227, ruší T-130).
> Na Windows x64 nejde ARM64 binárky spustit (Windows na x64 emulaci ARM64 nemá), proto vše, co vyžaduje běh, je
> soustředěné sem. Uživatel zatím zařízení s Windows na ARM64 nemá (stav 2026-11-12, stejně jako 2026-10-21 v T-057).

## Cíl
**Sběrný ověřovací úkol pro Windows ARM64** (rozhodnutí uživatele 2026-11-12, jako T-125): kritéria jiných úkolů, která
potřebují běh na ARM64, se sem přesouvají („→ přesunuto do T-227“) a každý takový bod tu má zaškrtávací pole, původní
úkol, kritérium a krátký postup. Ověřit na vlastním nebo zapůjčeném zařízení s Windows na ARM64 (otázka 1), že AOT
build `win-arm64` z T-226 funguje stejně jako `win-x64`, a podle výsledku povolit nahrávání `win-arm64` (`Uploadable = $true`, viz T-226 otázka 1) a doplnit README
a příručky o soubory ARM64 ke stažení.

## Rozsah
Na ARM64 zařízení, s balíčkem z `release-build.ps1` (T-226) a se simulátorem (`tools/PrusaSimulator`, běží na x64
stroji v síti nebo na zařízení):
1. **AOT smoke:** `pwsh scripts/aot-smoke.ps1 -PublishDir <publish win-arm64>` projde (na ARM64 hostiteli se spustí).
2. **Aplikace:** tray ikona, menu, okno nastavení, diagnostika, okno licencí (FFmpeg ARM64, T-225), jazyk, jump list,
   jediná instance, upozornění (toast, AUMID `LayerPeek`), Správce pověření, proxy webu PrusaLink.
3. **Kamera (T-225):** testy `[FFmpegFact]` projdou nativně na ARM64; živý stream ze simulátoru (případně Buddy3D),
   nahrávání MP4, timelapse a export JPEG; hardwarové dekódování D3D11VA (T-206) – funguje, nebo spadne zpět na
   software (zapsat GPU a ovladač).
4. **Instalátor a aktualizace (Velopack):** `LayerPeek-X.Y.Z-win-arm64-Setup.exe` nainstaluje bez práv správce,
   zástupce ve Start menu s AUMID, autostart, odinstalace beze zbytku; aktualizace N → N+1 z kanálu `win-arm64`
   (lokální feed nebo zkušební vydání podle `docs/release.md`); instalace ARM64 přes existující x64 instalaci podle
   T-226 otázky 2.
5. **winget** (až bude `arm64` v manifestu, T-059 / T-226): `winget install --manifest` na ARM64 vybere uzel `arm64`.
6. **E2E na zařízení (volitelné):** `tools/run-e2e.ps1` a `tools/windows-verify.ps1` s RID hostitele (T-226 bod 7);
   výsledek zapsat.
7. Výsledek zapsat do poznámek; nalezené chyby = nové úkoly. Po úspěchu přepnout `win-arm64` na `Uploadable = $true`
   (malá změna v `ReleaseTools.psm1` + test), v README a příručkách nabídnout soubory ARM64 ke stažení; od prvního
   vydání s `win-arm64` přidat uzel `arm64` do winget (T-226 otázka 3).
   **Předávka z T-226:** po přepnutí `Uploadable` upravit testy, které dnes stav „nenahrává se“ ověřují:
   `ReleaseTools.Tests.ps1` (`releases win-x64, win-arm64 and osx-arm64; win-arm64 is built but not uploadable until
   T-227`, `lists the RIDs a release build of an OS may upload …` – `Get-ReleaseBuildRuntimeArguments -Os windows` pak
   vrátí prázdno, `refuses a build containing win-arm64 …`, `needs no win-arm64 feed and refuses every win-arm64 file …`)
   a `ReleaseOrchestrator.Tests.ps1` (`-Runtime win-x64` v dalších krocích, v plánu a v `release.ps1`, `refuses the
   default Windows build with win-arm64 …`). V `docs/release.md` (oddíl „Konfigurace RID“, „3. Běh na Windows“ bez
   `-Runtime win-x64`), README EN + CZ a všech 8 příručkách (odstavec pod tabulkou balíčků Windows, řádek „Windows on
   ARM64 🚧“) doplnit soubory `…-win-arm64-…`. winget: první vydání s `win-arm64` nejde přes `wingetcreate update`
   (předchozí manifest má jeden uzel), jen ručně `release-winget.ps1 -OutputDirectory` + `winget validate` +
   `wingetcreate submit` (postup v `docs/release.md`, „Windows ARM64 ve winget“); pak `winget install --manifest` na
   ARM64 (bod 5).
   **K řešení (review T-226, zatím beze změny kódu):** `src/LayerPeek.App/Updates/GitHubReleaseChecker.cs:17` počítá
   vydání jako dostupné, jakmile má `SHA256SUMS-windows.txt`, bez ohledu na architekturu. Po povolení `win-arm64` by tak
   uživatel ARM64 verze ZIP (bez Velopacku, kontrola přes GitHub Releases) mohl dostat upozornění na vydání, které má jen
   soubory `win-x64`. Před přepnutím `Uploadable` rozhodnout a upravit (např. vyžadovat i `LayerPeek-X.Y.Z-<RID>.zip`
   RID běžící aplikace) + test.
8. **Přesunuté body z jiných úkolů** (doplňují se sem podle pravidla v `tasks/README.md`):
   - [ ] **T-225** – „Běh kamery na ARM64 (RTSP, nahrávání, timelapse, D3D11VA)“. Postup: na zařízení
     `dotnet test tests/LayerPeek.Camera.FFmpeg.Tests -r win-arm64` (FFmpeg ARM64 se přeloží křížově přes emulaci x64,
     nebo zkopírovat cache `win-arm64-…` z x64 stroje a nastavit `LayerPeekFFmpegCacheRoot`); `[FFmpegFact]` se na ARM64
     spouští, když jsou DLL vedle testů (`FFmpegTestEnvironment.IsProvidedOnWindows`), včetně
     `FFmpegWindowsBuildTests` (architektura `Arm64`, importy, D3D11VA). Pak v aplikaci z publish `win-arm64` živý stream
     ze simulátoru, nahrávání MP4, timelapse a export JPEG, okno licencí (MinGW-w64 runtime 15.0.0) a hardwarové
     dekódování D3D11VA – funguje, nebo spadne zpět na software (zapsat GPU a ovladač).

## Akceptační kritéria
- [ ] AOT smoke test `win-arm64` prošel na ARM64 (zařízení, Windows verze a CPU v poznámkách)
- [ ] Aplikace a upozornění fungují (body 2)
- [ ] Testy FFmpeg prošly nativně na ARM64 a živý stream, nahrávání a timelapse fungují; stav D3D11VA zapsaný
- [ ] `Setup.exe` `win-arm64` nainstaluje, aktualizuje a odinstaluje aplikaci (bod 4)
- [ ] `win-arm64` povolený k nahrání (`Uploadable`), dokumentace podle výsledku
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. ~~**Na čem ověřovat?** (a) vlastní / zapůjčené zařízení, (b) Azure ARM64 VM, (c) běžec GitHub Actions
   `windows-11-arm`, (d) QEMU.~~ **Rozhodnuto uživatelem 2026-11-12:** **(a)** vlastní nebo zapůjčené zařízení
   s Windows na ARM64. Žádný běžec CI ani Azure. Do té doby úkol čeká.
2. ~~**Do té doby: zůstává úkol v `todo/`, nebo sběrný úkol „Windows ARM64“?**~~ **Rozhodnuto uživatelem 2026-11-12:**
   T-227 je **sběrný ověřovací úkol pro Windows ARM64** (jako T-125); ostatní úkoly sem přesouvají kritéria, která
   potřebují běh na ARM64 („→ přesunuto do T-227“). Pravidlo je v `tasks/README.md`.

## Poznámky k implementaci
(vyplní agent po dokončení)
