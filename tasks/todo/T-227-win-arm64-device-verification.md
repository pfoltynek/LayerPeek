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
>
> **Rozhodnutí uživatele 2026-11-16 (mění rozhodnutí z 2026-11-12, T-226 otázka 1):** `win-arm64` se vydává
> **naplno** (ne experimentálně), i když ještě neběželo na zařízení s ARM64 – **od prvního vydání po 1.0.0**: 1.0.0
> vychází z existujícího tagu `v1.0.0` jen s macOS a `win-x64`, tyto změny jdou do develop (upřesnění 2026-11-16).
> ARM64 vyžaduje Windows 11 (README a příručky: „Windows 10 nebo 11 (x64), Windows 11 (ARM64)“). `win-arm64` má v tabulce RID
> `Uploadable = $true` (bez `BlockedReason`), skripty vydání sestavují a nahrávají oba RID Windows a README a příručky
> soubory ARM64 nabízejí (provedeno 2026-11-16 na větvi `feature/release-1.0.0-arm64-readme`). **T-227 už není
> podmínkou nahrání**, zůstává otevřený jako dodatečné ověření na zařízení; chyba nalezená na zařízení = nový úkol
> (případně `hotfix/X.Y.Z`).

## Cíl
**Sběrný ověřovací úkol pro Windows ARM64** (rozhodnutí uživatele 2026-11-12, jako T-125): kritéria jiných úkolů, která
potřebují běh na ARM64, se sem přesouvají („→ přesunuto do T-227“) a každý takový bod tu má zaškrtávací pole, původní
úkol, kritérium a krátký postup. Ověřit na vlastním nebo zapůjčeném zařízení s Windows na ARM64 (otázka 1), že
vydaný build `win-arm64` (nahrávaný od prvního vydání po 1.0.0, rozhodnutí uživatele 2026-11-16) funguje stejně jako `win-x64`. Nahrání
`win-arm64` a soubory ARM64 v README a příručkách na výsledku už nezávisí (jsou hotové); výsledek se zapíše do poznámek
a každá chyba je nový úkol.

## Rozsah
Na ARM64 zařízení, se soubory `win-arm64` zveřejněného vydání (nebo s balíčkem z `release-build.ps1`, T-226) a se
simulátorem (`tools/PrusaSimulator`, běží na x64 stroji v síti nebo na zařízení):
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
5. **winget** (uzel `arm64` od prvního vydání se soubory `win-arm64`, T-059 / T-226): `winget install --id
   3DPrint.LayerPeek` (nebo `winget install --manifest` z `release-winget.ps1 -OutputDirectory`) na ARM64 vybere uzel
   `arm64` a nainstaluje `…-win-arm64-Setup.exe`.
6. **E2E na zařízení (volitelné):** `tools/run-e2e.ps1` a `tools/windows-verify.ps1` s RID hostitele (T-226 bod 7);
   výsledek zapsat.
7. Výsledek zapsat do poznámek; nalezené chyby = nové úkoly (případně `hotfix/X.Y.Z`).
   **Předávka z T-226 – provedeno 2026-11-16** (rozhodnutí uživatele, větev `feature/release-1.0.0-arm64-readme`, ne až
   po ověření): `win-arm64` `Uploadable = $true` bez `BlockedReason`; testy Pester, které ověřovaly „nenahrává se“,
   přepsané na nové chování (`ReleaseTools.Tests.ps1`: tabulka RID, `Get-ReleaseBuildRuntimeArguments` na obou OS
   prázdné, build s `win-arm64` přijatý, vydání Windows bez feedu `win-arm64` odmítnuté, obecné odmítnutí RID
   s `Uploadable = $false` dál testované nad umělou tabulkou; `ReleaseOrchestrator.Tests.ps1`: `release.ps1`, plán a
   další kroky bez `-Runtime win-x64`, výchozí build Windows s oběma RID projde `release-upload.ps1 -DryRun`);
   `docs/release.md`, `docs/prerequisites.md`, `docs/velopack.md`, `docs/architecture.md`, README EN + CZ, všech 8
   příruček a `CHANGELOG.md` (Unreleased → Added) nabízejí soubory `…-win-arm64-…`. winget: první odeslání (T-059,
   1.0.0) je jen `x64`, takže první vydání se soubory `win-arm64` jde do winget ručně (`release-winget.ps1
   -OutputDirectory`, `winget validate`, `wingetcreate submit`, `docs/release.md` „Windows ARM64 ve winget“); pak
   `winget install` na ARM64 (bod 5).
   **Kontrola aktualizací ZIP (review T-226):** `src/LayerPeek.App/Updates/GitHubReleaseChecker.cs` počítá vydání jako
   dostupné, jakmile má `SHA256SUMS-windows.txt`, bez ohledu na architekturu. Od prvního vydání po 1.0.0 nese každé vydání oba
   RID Windows (`release-publish.ps1` bez `-AllowIncomplete` vydání bez `releases.win-arm64.json` nezveřejní), takže to
   stačí; zbývá jen okrajový případ vydání zveřejněného `-AllowIncomplete` s buildem `-Runtime win-x64` (otázka 3).
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
- [x] `win-arm64` povolený k nahrání (`Uploadable`) – provedeno 2026-11-16 rozhodnutím uživatele ještě před ověřením
      na zařízení (`win-arm64` naplno od prvního vydání po 1.0.0); dokumentace podle výsledku ověření (případné známé problémy)
      zbývá
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. ~~**Na čem ověřovat?** (a) vlastní / zapůjčené zařízení, (b) Azure ARM64 VM, (c) běžec GitHub Actions
   `windows-11-arm`, (d) QEMU.~~ **Rozhodnuto uživatelem 2026-11-12:** **(a)** vlastní nebo zapůjčené zařízení
   s Windows na ARM64. Žádný běžec CI ani Azure. Do té doby úkol čeká.
2. ~~**Do té doby: zůstává úkol v `todo/`, nebo sběrný úkol „Windows ARM64“?**~~ **Rozhodnuto uživatelem 2026-11-12:**
   T-227 je **sběrný ověřovací úkol pro Windows ARM64** (jako T-125); ostatní úkoly sem přesouvají kritéria, která
   potřebují běh na ARM64 („→ přesunuto do T-227“). Pravidlo je v `tasks/README.md`.
3. **Kontrola aktualizací verze ZIP podle architektury (`GitHubReleaseChecker`)?** Rozhodnutí uživatele 2026-11-16: díky
   tomu, že se od prvního vydání po 1.0.0 vydávají oba RID Windows v každém vydání, je dnešní chování (vydání se počítá podle
   `SHA256SUMS-windows.txt`) v pořádku. Otevřené: má se přesto doplnit levná přesná kontrola (vydání se počítá, jen když
   má i feed `releases.<RID>.json` RID běžící aplikace, který existuje pro každý vydaný RID obou OS) pro případ vydání
   zveřejněného `release-publish.ps1 -AllowIncomplete` s buildem jen `win-x64`? (a) ano, nový malý úkol s testy,
   (b) ne, stačí dnešní stav.
4. ~~**Tag `v1.0.0` už existuje**~~ (zjištěno 2026-11-16 při přípravě): `release/1.0.0` je sloučená do `main`, tag `v1.0.0`
   (b5a560eb) je v `origin/main` a obsahuje ještě `Uploadable = $false` pro `win-arm64` a v `CHANGELOG.md` sekci
   `[1.0.0]` s položkou „Windows on ARM64 (in preparation) … not offered for download yet“. `release-build.ps1` /
   `release-upload.ps1` běží nad čistým checkoutem tagu, takže změny z develop se do buildu 1.0.0 nedostanou a tagy `v*`
   jsou na GitHubu neměnné. **Rozhodnuto uživatelem 2026-11-16:** 1.0.0 vychází
   z existujícího tagu jen s macOS a `win-x64`; změny ARM64 a README jdou do develop a vyjdou v dalším vydání.

## Poznámky k implementaci
(vyplní agent po dokončení)
