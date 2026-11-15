---
id: T-226
title: Windows ARM64 – vydání `win-arm64` (skripty, Velopack, aktualizace, winget, README a příručky)
status: done
owner: qa-reviewer
priority: P2
platform: windows
depends_on: [T-224, T-225]
---

> **Rozhodnutí uživatele 2026-11-12:** LayerPeek se vydává i pro `win-arm64` (série T-224 až T-227, ruší T-130).
> Tento úkol vrací `win-arm64` do skriptů vydání, které ho od T-130 odmítají, a přidává ho do instalátoru, aktualizací,
> manifestu winget a dokumentace pro uživatele.

## Cíl
Jedno spuštění `pwsh scripts/release-build.ps1` na Windows x64 sestaví a zabalí `win-x64` i `win-arm64`; vydání na
GitHubu nese soubory `win-arm64` až po ověření v T-227 (otázka 1), aplikace nainstalovaná z ARM64
instalátoru se aktualizuje z kanálu `win-arm64` a uživatel v README a příručce najde, který soubor stáhnout.

## Rozsah
1. **Konfigurace RID** (`$script:ReleaseRuntimes` ve `scripts/ReleaseTools/ReleaseTools.psm1`): přidat
   `'win-arm64' = @{ Os = 'windows'; Default = $true; Uploadable = $false; BlockedReason = 'not verified on an ARM64
   device (T-227)' }` (otázka 1). Obecný mechanismus
   `Uploadable = $false` / `BlockedReason` z T-055 / T-130 existuje a je testovaný nad umělou tabulkou. Odstranit
   odmítnutí a texty „not supported (T-130)“ v `scripts/aot-smoke.ps1`, `scripts/release-build.ps1` (nápověda
   `-Runtime`), `release-upload.ps1`, `release-publish.ps1`, komentář nad tabulkou. Pester testy, které dnes ověřují
   odmítnutí `win-arm64` (`ReleaseTools.Tests.ps1`, `aot-smoke`), přepsat na nové chování.
2. **Sestavení** (`release-build.ps1`): AOT publish `win-arm64` (toolchain z T-224, FFmpeg z T-225); kontrola ARM64 PE
   (funkce z T-224); smoke test na x64 hostiteli „not verified by running (this machine is windows-x64)“ (existující
   větev `Test-RuntimeRunnable`); `aot-smoke.ps1 -Runtime win-arm64` na x64 skončí jasnou hláškou „nelze spustit na
   tomto stroji“ (ne chybou buildu). Na ARM64 hostiteli (T-227) se smoke test spustí normálně.
3. **Velopack** (`Get-VelopackPackArguments`, `Copy-VelopackOutput`): `--channel win-arm64 -r win-arm64` nad publish
   složkou `win-arm64`; soubory `LayerPeek-X.Y.Z-win-arm64.zip`, `LayerPeek-X.Y.Z-win-arm64-Setup.exe`,
   `LayerPeek-X.Y.Z-win-arm64-full.nupkg`, `releases.win-arm64.json` (+ podpis `releases.win-arm64.json.sig`, T-181),
   `assets.win-arm64.json`, `RELEASES-win-arm64`; vedle souborů `win-x64` bez kolize názvů (T-056 bod 8: názvy ověřené,
   `Setup.exe` / `Update.exe` jsou nativní ARM64). `vpk` architekturu exe nekontroluje (`docs/velopack.md` bod 8) –
   kontrolu PE z bodu 2 volat před `vpk pack`. Jeden `SHA256SUMS-windows.txt` pro obě architektury.
4. **Nahrání a zveřejnění** (`release-upload.ps1`, `release-publish.ps1`, `release.ps1`): očekávaná sada souborů
   Windows (`Get-ReleaseBuildOutput`, kontrola podpisů feedů, text „release on two machines“, kontrola úplnosti vydání
   před zveřejněním) včetně `win-arm64`; RID s `Uploadable = $false` se nenahraje (existující kontrola). Pester testy
   (`ReleaseTools.Tests.ps1`, `ReleaseOrchestrator.Tests.ps1`).
5. **Aplikace a aktualizace:** `VelopackUpdateEngine` bere kanál z instalace (ARM64 instalace → `win-arm64`), změna
   kódu se nečeká; ověřit testy `AppUpdatesTests` / `UpdateFeedVerifier` s kanálem `win-arm64` (doplnit případ, kde
   chybí). Obě architektury mají stejné `packId` `LayerPeek` (stejná složka `%LOCALAPPDATA%\LayerPeek`, stejný klíč
   Uninstall a `ProductCode` ve winget) – zdokumentovat ruční přechod: odinstalovat x64, nainstalovat ARM64 (otázka 2);
   chování instalace ARM64 přes existující x64 instalaci se ověří na zařízení v T-227.
6. **winget** (`build/winget/3DPrint.LayerPeek.installer.yaml`, `scripts/release-winget.ps1`, funkce `*-Winget*`):
   druhý uzel `Installers` s `Architecture: arm64` a URL `…-win-arm64-Setup.exe`; `wingetcreate update` dostane dvě URL
   (architekturu bere z URL); uzel `arm64` jen když vydání obsahuje `…-win-arm64-Setup.exe` (první vydání, které
   `win-arm64` nahraje, otázka 3), jinak manifest jen `x64`; `ReleaseWinget.Tests.ps1` pro obě varianty. T-059
   (první odeslání po 1.0.0) zůstává jen `x64`.
7. **Testy a nástroje s pevným `win-x64`:** `VelopackInstallerTests` (`Runtime = "win-x64"`), `IconAndBuildTests`
   (cesta `win-x64/publish`), `tools/run-e2e.ps1`, `tools/windows-verify.ps1` (`$script:Runtime`): brát RID hostitele
   (`win-x64` / `win-arm64` podle architektury OS), aby šly spustit v T-227 na ARM64 zařízení; na x64 beze změny.
8. **Dokumentace a texty pro uživatele:**
   - `README.md` + `README.cz.md`: tabulka platforem („Windows on ARM64 ❌ not supported“ → „připravuje se“ do T-227,
     otázka 1),
     sekce stažení (který soubor pro x64 a který pro ARM64, jak zjistit architekturu: Nastavení → Systém → O systému →
     Typ systému – text připravit, soubory ARM64 nabídnout až v T-227), ruční přechod z x64 na ARM64 (otázka 2),
     příkazy publish;
   - `manuals/<jazyk>/manual.md` všech 8 jazyků (`en` je zdroj): věta „Windows 10 or 11, x64 (Windows on ARM is not
     supported)“ a tabulka souborů ke stažení;
   - `docs/release.md` (vydávané RID, oddíl „Konfigurace RID“, seznam souborů vydání, chyby `Unknown runtime
     'win-arm64'`, winget), `docs/velopack.md` (bod 8 – už ne „zrušeno“, výsledek skutečného `vpk pack`),
     `docs/prerequisites.md` (věta „Vydávané RID“, řádek častých chyb T-130, `vpk` na Windows balí oba RID),
     `docs/architecture.md` (tabulka „Vydání“), `docs/windows-manual-testing.md`, `docs/aot-gc-settings.md` (měřeno jen
     na `osx-arm64`; Windows ARM64 neměřeno – jen poznámka);
   - `CHANGELOG.md` (Unreleased → Added: Windows ARM64);
   - `CLAUDE.md` (řádek `dotnet publish … -r win-arm64` v Commands, zmínka o komponentě MSVC ARM64) a
     `.claude/agents/qa-reviewer.md` – jen přes hlavního agenta / se souhlasem uživatele.
9. Licence třetích stran pro `win-arm64` řeší T-224 (runtime balíčky) a T-225 (FFmpeg); zde jen kontrola, že okno
   licencí v ARM64 buildu nic nepostrádá.

## Akceptační kritéria
- [x] `pwsh scripts/release-build.ps1` (bez nahrání, `-SkipFeedSignature` nebo zkušebním klíčem) na Windows x64 sestaví
      `win-x64` i `win-arm64` (podle `Default`): zip, `Setup.exe`, `.nupkg`, feed a podpis feedu `win-arm64`, řádky
      v `SHA256SUMS-windows.txt` (odpovídají `Get-FileHash`); exe a DLL `win-arm64` jsou ARM64 PE; smoke test
      `win-arm64` zapsaný jako neověřený spuštěním (ověřeno kopií skriptu bez testů řešení, smoke testu `win-x64` a kroku
      `ffmpeg-source`, viz poznámky; plný běh skriptu → přesunuto do T-125 (2ai))
- [x] `release-upload.ps1 -DryRun` výstup se soubory `win-arm64` odmítne (`Uploadable = $false`, T-227), výstup jen
      `win-x64` projde; nic se nenahrálo
- [x] Šablona a skript winget vykreslí manifest jen `x64` i `x64` + `arm64` podle souborů ve vydání; oba projdou
      schématem 1.28.0 (stejně jako v T-059) a Pester testy
- [x] Pester testy skriptů (bez sítě) a C# testy aktualizací pokrývají `win-arm64`; odmítnutí z T-130 odstraněna
- [x] README EN + CZ, všech 8 příruček, `docs/release.md`, `docs/velopack.md`, `docs/prerequisites.md`,
      `docs/architecture.md`, `docs/windows-manual-testing.md`, `CHANGELOG.md` aktuální; grep `(?i)win-arm64|arm64|T-130`
      mimo `tasks/` nenajde zastaralé „nepodporováno“
- [x] Pokusné instalace a dočasné složky uklizené
- [x] `dotnet build` a `dotnet test` bez chyb a varování (bez E2E; celé `dotnet test` s E2E → přesunuto do T-125 (2ai), E2E s myší nejdou přes RDP)

## Otevřené otázky
1. ~~**Vydávat `win-arm64` před ověřením na zařízení?**~~ **Rozhodnuto uživatelem 2026-11-12:** **(a)** `Default =
   $true`, `Uploadable = $false` s `BlockedReason` „not verified on an ARM64 device (T-227)“; nahrávat až po úspěšném
   T-227 (přepnutí `Uploadable` je součást T-227). README a příručky do té doby soubory ARM64 ke stažení nenabízejí
   (podpora „připravuje se“), soubory ke stažení doplní T-227.
   **Změněno rozhodnutím uživatele 2026-11-16:** `win-arm64` se vydává **naplno** (ne experimentálně), i bez
   běhu na zařízení s ARM64, od prvního vydání po 1.0.0 (1.0.0 vychází z existujícího tagu jen s macOS a `win-x64`): `Uploadable = $true` bez `BlockedReason`, README a příručky soubory ARM64 nabízejí,
   T-227 zůstává jen dodatečné ověření na zařízení (už ne podmínka nahrání). Provedeno 2026-11-16 na větvi
   `feature/release-1.0.0-arm64-readme` (viz poznámky).
2. ~~**Uživatelé x64 buildu na ARM64 zařízení?**~~ **Rozhodnuto uživatelem 2026-11-12:** jen zdokumentovat ruční přechod
   (odinstalovat x64, nainstalovat ARM64); žádný automatický přechod.
3. ~~**winget a 1.0.0?**~~ **Rozhodnuto uživatelem 2026-11-12:** uzel `arm64` se do manifestu přidá až od prvního
   vydání, které `win-arm64` skutečně nahraje; šablona a skript ať umí obě varianty (podle souborů ve vydání).
   Platí dál: první odeslání do winget (T-059, 1.0.0) je jen `x64`, uzel `arm64` přijde s prvním vydáním se soubory
   `win-arm64` (ruční odeslání podle otázky 6).
4. ~~**Patří `win-arm64` do vydání 1.0.0 (T-198)?**~~ **Rozhodnuto uživatelem 2026-11-12:** ne, není to bloker 1.0.0;
   přijde v 1.0.x / 1.1.0. T-198 se nemění. **Upřesněno uživatelem 2026-11-16:** 1.0.0 bez `win-arm64`
   (z existujícího tagu), `win-arm64` naplno v dalším vydání (viz otázka 1).
5. ~~**Build pro nahrání bez `win-arm64`?**~~ **Rozhodnuto uživatelem 2026-11-15:** schváleno, build pro nahrání je
   `release-build.ps1 -Runtime win-x64` (jak je implementováno). **Od 2026-11-16 neplatí:** `win-arm64` je nahratelný,
   `Get-ReleaseBuildRuntimeArguments` vrací na obou OS prázdno, build pro nahrání je výchozí build bez `-Runtime`
   (mechanismus pro nenahratelný RID zůstává a je testovaný nad umělou tabulkou). Původní znění: s `Default = $true` a `Uploadable = $false` by výchozí
   build Windows nešel nahrát, takže by `release.ps1` (a tedy vydání 1.0.0 z Windows) skončil chybou. Implementováno:
   `release.ps1`, plán a vypsané příkazy sestavují pro nahrání jen nahratelné RID (`release-build.ps1 -Runtime win-x64`),
   ruční `release-build.ps1` bez `-Runtime` dál sestaví oba RID (lokální ověření). Po T-227 se `-Runtime` vynechá samo.
   Alternativy: `release-upload.ps1` by soubory nenahratelných RID přeskočil (pak by ale `SHA256SUMS-windows.txt` uváděl
   nenahrané soubory a kritérium 2 by neplatilo), nebo `Default = $false` do T-227.
6. ~~**winget: první vydání s `arm64` automaticky?**~~ **Rozhodnuto uživatelem 2026-11-15:** první odeslání s `arm64`
   zůstává ruční krok v T-227 (jak je implementováno). Platí dál: 1.0.0 jde do winget jen s `x64`, první vydání
   se soubory `win-arm64` se odešle ručně (`-OutputDirectory`, `winget validate`, `wingetcreate submit`), ověření
   `winget install` na ARM64 v T-227. Původní znění: `wingetcreate update` potřebuje stejný počet URL jako uzlů
   předchozího manifestu (T-059), takže první vydání s `win-arm64` jde jen ručně (`-OutputDirectory`, `winget validate`,
   `wingetcreate submit`; skript při selhání `update` postup vypíše). Má `release-winget.ps1` tento přechod rozpoznat sám
   (přečíst architektury předchozí verze z `microsoft/winget-pkgs` přes `gh api` a poslat manifest ze šablon), nebo
   zůstane ruční krok v T-227?
7. **Rozhodnuto uživatelem 2026-11-15:** úpravy `CLAUDE.md` (příkaz `dotnet publish … -r win-arm64`, komponenta MSVC
   ARM64) a `.claude/agents/qa-reviewer.md` schválené, provede je hlavní agent. Úloha CI pro křížový build `win-arm64`
   je samostatný úkol **T-236** (založen 2026-11-15).

## Poznámky k implementaci
Implementováno 2026-11-15 na Windows 11 x64 (worktree `LP-T-226`, větev `feature/T-226-win-arm64-release-packaging`
z develop 94f53485). Testy napsané před implementací (Pester, C#), pak implementace.

**Tabulka RID (bod 1, otázka 1):** `$script:ReleaseRuntimes` má `win-arm64` (`Os = 'windows'; Default = $true;
Uploadable = $false; BlockedReason = 'not verified on an ARM64 device (T-227)'`) mezi `win-x64` a `osx-arm64`; komentář
nad tabulkou přepsaný. Odmítnutí z T-130 odstraněná: `aot-smoke.ps1` (místo „Windows is built as win-x64 only“ odmítá jen
Windows RID mimo tabulku, např. `win-x86`, a `win-arm64` na stroji, který ho nespustí: `AOT smoke check NOT RUN: runtime
'win-arm64' cannot run on this machine (windows-x64): Windows on x64 cannot run ARM64 binaries. Run it on a Windows
ARM64 device (T-227).` – exit 1, nic nepublikuje ani nespustí, také s `-PublishDir`), nápověda `-Runtime`
v `release-build.ps1`, texty `release-upload.ps1` / `release-publish.ps1` (ty parametr RID nemají).

**Důsledek otázky 1 pro tok vydání (implementováno, otázka 5 níže):** výchozí build Windows teď obsahuje `win-arm64` a
`release-upload.ps1` ho (správně) odmítne, takže by `release.ps1` i ruční postup z `docs/release.md` na Windows skončil
chybou. Nové čisté funkce `Get-UploadableReleaseRuntimes` (výchozí a nahratelné RID OS) a
`Get-ReleaseBuildRuntimeArguments` (dnes Windows `-Runtime win-x64`, macOS nic; po T-227 samo prázdné). Používá je
`release.ps1` (volá `release-build.ps1 -Runtime win-x64`), plán kroků (`Get-ReleaseFlowPlan`, `Format-ReleaseFlowPlan`),
další kroky (`Get-ReleaseNextSteps`) a hláška `Get-BuildInfoProblems` („… Rebuild without it: pwsh
scripts/release-build.ps1 -Runtime win-x64“). Kontrola toolchainu v `release.ps1` (`Get-ReleaseFlowFacts`) počítá jen
s nahratelnými RID, takže vydání `win-x64` nepotřebuje komponentu MSVC ARM64. Nová `Get-HostRuntime` (RID stroje).

**Sestavení a Velopack (body 2–3):** `release-build.ps1`, `Get-VelopackPackArguments` a `Copy-VelopackOutput` už byly
obecné (kanál = RID, kontrola PE z T-224 před smoke testem i před balením, smoke test jen pro RID stroje) – beze změny
kódu. Ověřeno zkušebním buildem (níže).

**Nahrání a zveřejnění (bod 4):** mechanismus `Uploadable`/`BlockedReason` z T-055 beze změny: build s `win-arm64`
odmítne `Get-BuildInfoProblems`, `Get-PublishProblems` odmítne každý soubor `win-arm64` ve vydání (i uvedený v součtech) a
feed `releases.win-arm64.json` nečeká. Jeden `SHA256SUMS-windows.txt` pro obě architektury.

**Aplikace a aktualizace (bod 5):** kód beze změny (`VelopackUpdateEngine` bere kanál z instalace). Nové testy:
`UpdateFeedVerifierTests` (feed podepsaný pro `win-x64` / `win-arm64` / `osx-arm64` platí pro svůj kanál; feed jedné
Windows architektury neplatí pro druhou, i `WIN-ARM64`), `SignedUpdateSourceTests` (x64 feed nabídnutý instalaci ARM64
odmítnut; složka: instalace ARM64 čte jen `releases.win-arm64.json`; GitHub: instalace ARM64 přeskočí vydání jen s
`win-x64` a čte `v0.3.2/releases.win-arm64.json` + `.sig`; `VelopackUpdateEngine` s kanálem `win-arm64` z lokátoru
nabídne 0.3.2). Ruční přechod x64 → ARM64 (otázka 2) v README, příručkách a `docs/velopack.md` bod 5; chování instalace
ARM64 přes x64 instalaci ověří T-227 (bod 4).

**winget (bod 6, otázka 3):** šablona `3DPrint.LayerPeek.installer.yaml` má za uzlem `x64` blok mezi řádky
`# @ARM64_BEGIN@` a `# @ARM64_END@` s uzlem `arm64` (`@INSTALLER_SHA256_ARM64@`). `ConvertTo-WingetManifests
-Arm64InstallerSha256` značky odstraní a SHA doplní, bez něj odstraní celý blok (výstup bez slova „arm64“); šablona bez
právě jednoho bloku = chyba. `Test-WingetArm64Installer` (vydání má `…-win-arm64-Setup.exe`), `Get-WingetUrls`
(`InstallerArm64`), `Get-WingetInstallerFileName`/`Get-WingetInstallerSha256 -Runtime`, `Get-WingetReleaseProblems`
(arm64 instalátor, když je ve vydání, musí být nahraný celý se SHA256; x64 dál povinný), `Get-WingetCreateArguments
-Arm64` (`--urls <x64> <arm64>`), `Invoke-WingetRelease` (plán vypíše oba instalátory a oba SHA256). **Zjištění:**
podle T-059 (`docs/release.md`) potřebuje `wingetcreate update` stejný počet URL jako uzlů předchozího manifestu, takže
**první** vydání s `win-arm64` přes `update` neprojde; skript v tom případě při chybě `wingetcreate` vypíše ruční postup
(`-OutputDirectory`, `winget validate`, `wingetcreate submit`), zdokumentováno v `docs/release.md` („Windows ARM64 ve
winget“) a předáno do T-227 (otázka 6). `winget validate` (winget 1.30.300, lokálně, bez sítě) 2026-11-15: manifest
jen `x64` i `x64` + `arm64` „Manifest validation succeeded.“, negativní kontrola (`Architecture: arm65`) selhala se
„Schema Error … Failed to match against any enum values“. T-059 zůstává jen `x64`.

**Nástroje a testy s pevným `win-x64` (bod 7):** `tools/windows-verify.ps1` (`$script:Runtime = Get-HostRuntime`,
název kontroly `win.aot-smoke` podle RID), `tools/run-e2e.ps1` (`-PublishAot` publikuje RID hostitele, importuje
`ReleaseTools.psm1` kvůli `Get-HostRuntime`), E2E testy přes nový `tests/LayerPeek.App.E2E.Tests/Infrastructure/HostRuntime.cs`
(`VelopackInstallerTests`, `IconAndBuildTests` – cesta `<rid>/publish`, a navíc `UpdateTests`, který balil kanál
`win-x64` a na ARM64 by se přeskočil). Na x64 beze změny chování. E2E jsem nespouštěl (plochu používá jiná relace),
jen sestavil.

**Dokumentace (bod 8):** README EN + CZ (tabulka platforem „🚧 in preparation / připravuje se“, odstavec pod soubory ke
stažení: jak zjistit architekturu v Nastavení → Systém → O systému → Typ systému, soubory ARM64 zatím nejsou, ruční
přechod; komponenta MSVC ARM64; příkaz `publish -r win-arm64`), všech 8 příruček (řádek požadavků „verze pro Windows na
ARM se připravuje“ + stejný odstavec pod tabulkou balíčků Windows; názvy nabídek Windows v cs/en podle zadání, v ostatních
jazycích obecné „Typ systému“ s hodnotou *x64* / *ARM*), `docs/release.md` (vydávané RID, krok 3 s `-Runtime win-x64`,
výstupy `win-arm64`, smoke test, tabulka chyb, zdroj FFmpeg, winget, „Konfigurace RID“ s tabulkou, popis testů),
`docs/velopack.md` (bod 5 kanály, bod 8 stav a výsledek skutečného `vpk pack`), `docs/prerequisites.md` (vydávané RID,
spuštění, `vpk` balí oba RID, dva nové řádky častých chyb místo řádku T-130), `docs/architecture.md`,
`docs/windows-manual-testing.md` (odkaz na T-227 místo „zrušeno, T-130“), `docs/aot-gc-settings.md` (ARM64 neměřeno),
`CHANGELOG.md` (Unreleased → Added). Grep `(?i)win-arm64|arm64|T-130` mimo `tasks/`: zastaralé „nepodporováno“ zůstalo
jen v historickém záznamu vydání 0.4.0 v `CHANGELOG.md` (Removed) – vydaná verze, záměrně beze změny; „not supported“
v `build/FFmpeg.targets` / `build/FFmpeg.Tests.ps1` / `build-ffmpeg.sh` je aktuální chování z T-225 (build bez RID na
hostiteli ARM64, `win-x86`). `CLAUDE.md` a `.claude/agents/qa-reviewer.md` neupravené (návrh vět v hlášení hlavnímu
agentovi).

**Licence (bod 9):** `ThirdPartyLicensesTests` + `LicensesViewModelTests` prošly i po publish `win-arm64` (aktuální
`project.assets.json` s balíčky `win-arm64`); nový balíček NuGet ani nástroj žádný.

**Zkušební build (kritérium 1), 2026-11-15:** kopie `release-build.ps1` ve scratchpadu agenta, ve které chyběly jen tři
části: `dotnet build`/`dotnet test` celého řešení (celé `dotnet test` s E2E má spouštět hlavní agent), smoke test
`win-x64` (spustil by aplikaci na ploše, kterou používá jiná relace, a LayerPeek tam běžel) a krok `ffmpeg-source`
(cache `ffmpeg-source` na stroji chybí, stahoval by tarball). Všechno ostatní (publish obou RID `-warnaserror`, verze
exe, kontrola PE, smoke větev, balení zip + `vpk`, součty, `build-info.json`) je beze změny. Spuštěno
`-SkipFeedSignature`, verze 0.5.0, výsledek exit 0: `LayerPeek-0.5.0-win-x64.zip` (25,9 MB), `…-win-x64-full.nupkg`,
`…-win-x64-Setup.exe`, feed `win-x64`, `LayerPeek-0.5.0-win-arm64.zip` (24,3 MB), `…-win-arm64-full.nupkg` (26,5 MB),
`…-win-arm64-Setup.exe` (32,2 MB), `releases.win-arm64.json`, `assets.win-arm64.json`, `RELEASES-win-arm64`,
`SHA256SUMS-windows.txt` (12 řádků, všechny = `Get-FileHash`, žádný soubor navíc). Publish `win-arm64`: 8 PE, vše ARM64
`0xAA64`, 0 problémů (`win-x64` 8 PE x64); po rozbalení zip a `.nupkg` `win-arm64` (včetně `Squirrel.exe` a
`LayerPeek_ExecutionStub.exe`) a `Setup.exe` vše ARM64, `win-x64` vše x64. Smoke: `win-arm64 = not verified by running
(this machine is windows-x64)`. Feed `releases.win-arm64.json` → `LayerPeek-0.5.0-win-arm64-full.nupkg`. Podpis feedu:
kopie `dist/` podepsaná testovacím klíčem z `tests/Shared/Fixtures/UpdateFeed` (`Add-UpdateFeedSignatures`, ověření
proti veřejnému testovacímu klíči): oba `.sig` platné pro svůj RID, pro druhý Windows RID neplatné. Kontrola uploadu
nad skutečným výstupem (`Get-ReleaseBuildOutput`, jako `release-upload.ps1`): špinavý strom, `Build output contains
win-arm64, which must not be uploaded: not verified on an ARM64 device (T-227). Rebuild without it: pwsh
scripts/release-build.ps1 -Runtime win-x64`, `-SkipFeedSignature` a dva chybějící `.sig`. Nic se nenahrálo (žádné
volání `gh`).

**Kritérium 2:** Pester `ReleaseOrchestrator.Tests.ps1` „refuses the default Windows build with win-arm64 until T-227,
and accepts the build of win-x64 only (T-226)“: skutečný `release-upload.ps1` proti falešnému `gh` – výstup `win-x64`
+ `win-arm64` odmítne s `-DryRun` i bez něj, výstup jen `win-x64` s `-DryRun` projde (exit 0, bez `win-arm64`), žádné
`release create/upload/edit`, vydání nevzniklo. Plus kontrola skutečného výstupu výše.

**Spuštěno:** `dotnet build LayerPeek.slnx -warnaserror` 0 varování, 0 chyb; `LayerPeek.Scripts.Tests` (všechny soubory
Pester) 22 testů (21 prošlo, 1 přeskočen na vyžádání, 0 selhání; 5 min 47 s); zvlášť Pester `ReleaseTools.Tests.ps1` 342 prošlo / 18 přeskočeno, `ReleaseOrchestrator.Tests.ps1`
124 / 1 (před posledním testem uploadu; celý soubor pak v `LayerPeek.Scripts.Tests`), `ReleaseWinget.Tests.ps1` 44 / 1 (0 selhání); `LayerPeek.Core.Tests` třída `UpdateFeedVerifierTests` 44/44;
`LayerPeek.App.Tests` třídy `SignedUpdateSourceTests`, `AppUpdatesTests`, `ThirdPartyLicensesTests`,
`LicensesViewModelTests`, `UserManualTests`, `SettingsAddressWarningTests` – vše prošlo. Celé `dotnet test` (s E2E a UI)
a plný běh `pwsh scripts/release-build.ps1 -SkipFeedSignature` (s testy řešení a smoke testem `win-x64`) spouští hlavní
agent.

**Úklid:** zkušební výstup `artifacts/release/0.5.0` smazaný; rozbalené balíčky, kopie `dist/` s podpisy a vykreslené
manifesty winget byly jen ve scratchpadu agenta a skripty je smazaly; dočasné složky Pester (`LAYERPEEK_PESTER_TESTS_TEMP`)
smazané. Nic se neinstalovalo. Mezivýstupy buildu `src/LayerPeek.App/bin|obj/Release/net11.0/win-arm64` zůstávají
(běžný výstup buildu).

**Po review (2026-11-15):** review schválilo (reviewer opravil chybějící zpětný apostrof v `manuals/fr` a komentář
dokumentace u `VelopackUpdateEngine`). Drobnosti: hláška `aot-smoke.ps1` sestaví popis hostitele obecně (OS a
architektura, ne pevné „x64“); nápověda při selhání `wingetcreate update` pokrývá i opačný případ (předchozí manifest má
`arm64`, vydání ne: doplnit soubory `win-arm64` přes `-AddToPublished`, nebo ruční odeslání manifestu jen `x64`) + test
a věta v `docs/release.md`; do T-227 přidán úkol k `GitHubReleaseChecker.cs` (vydání se počítá podle
`SHA256SUMS-windows.txt` bez ohledu na architekturu – uživatel ARM64 verze ZIP by mohl dostat upozornění na vydání jen
`win-x64`), bez změny kódu.

Přesunuto do T-227 (zařízení ARM64, už v jeho rozsahu): smoke test `win-arm64` (bod 1), instalace / aktualizace /
odinstalace `win-arm64` a instalace ARM64 přes x64 instalaci (bod 4), `winget install --manifest` s uzlem `arm64`
(bod 5), `tools/run-e2e.ps1` a `tools/windows-verify.ps1` s RID `win-arm64` (bod 6); do T-227 bodu 7 doplněná předávka
(které testy a texty změnit po přepnutí `Uploadable`, ruční první odeslání winget s `arm64`).

**Změna po rozhodnutí uživatele 2026-11-16 (`win-arm64` naplno od prvního vydání po 1.0.0; 1.0.0 z existujícího tagu
jen s macOS a `win-x64`; ARM64 vyžaduje Windows 11):** na větvi `feature/release-1.0.0-arm64-readme`
(z develop 786f0893) `$script:ReleaseRuntimes['win-arm64']` → `Uploadable = $true`, `BlockedReason = $null`; komentáře a
nápověda skriptů (`ReleaseTools.psm1`, `release.ps1`, `release-build.ps1`, `release-upload.ps1`, `release-publish.ps1`,
`release-winget.ps1`) bez „do T-227“. Kód toku vydání beze změny: `Get-ReleaseBuildRuntimeArguments` teď vrací prázdno,
takže `release.ps1`, plán i další kroky volají `release-build.ps1` bez `-Runtime` samy; `release-publish.ps1` čeká
i `releases.win-arm64.json` (+ `.sig`); kontrola toolchainu `release.ps1` chce i komponentu MSVC ARM64. Testy:
`ReleaseTools.Tests.ps1` (tabulka RID, nahratelné RID, build s `win-arm64` přijatý, vydání bez feedu `win-arm64`
odmítnuté, úplný koncept s `win-arm64`, obecné odmítnutí nenahratelného RID nad umělou tabulkou),
`ReleaseOrchestrator.Tests.ps1` (bez `-Runtime win-x64`, zástupný build Windows s oběma RID, `-AddToPublished` nahraje
i soubory `win-arm64`). Dokumentace: `docs/release.md`, `docs/prerequisites.md`, `docs/velopack.md`,
`docs/architecture.md`, README EN + CZ, 8 příruček, `CHANGELOG.md` (Unreleased → Added). Podrobnosti a stav ověření
na zařízení: T-227.
