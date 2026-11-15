---
id: T-226
title: Windows ARM64 – vydání `win-arm64` (skripty, Velopack, aktualizace, winget, README a příručky)
status: todo
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
- [ ] `pwsh scripts/release-build.ps1` (bez nahrání, `-SkipFeedSignature` nebo zkušebním klíčem) na Windows x64 sestaví
      `win-x64` i `win-arm64` (podle `Default`): zip, `Setup.exe`, `.nupkg`, feed a podpis feedu `win-arm64`, řádky
      v `SHA256SUMS-windows.txt` (odpovídají `Get-FileHash`); exe a DLL `win-arm64` jsou ARM64 PE; smoke test
      `win-arm64` zapsaný jako neověřený spuštěním
- [ ] `release-upload.ps1 -DryRun` výstup se soubory `win-arm64` odmítne (`Uploadable = $false`, T-227), výstup jen
      `win-x64` projde; nic se nenahrálo
- [ ] Šablona a skript winget vykreslí manifest jen `x64` i `x64` + `arm64` podle souborů ve vydání; oba projdou
      schématem 1.28.0 (stejně jako v T-059) a Pester testy
- [ ] Pester testy skriptů (bez sítě) a C# testy aktualizací pokrývají `win-arm64`; odmítnutí z T-130 odstraněna
- [ ] README EN + CZ, všech 8 příruček, `docs/release.md`, `docs/velopack.md`, `docs/prerequisites.md`,
      `docs/architecture.md`, `docs/windows-manual-testing.md`, `CHANGELOG.md` aktuální; grep `(?i)win-arm64|arm64|T-130`
      mimo `tasks/` nenajde zastaralé „nepodporováno“
- [ ] Pokusné instalace a dočasné složky uklizené
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. ~~**Vydávat `win-arm64` před ověřením na zařízení?**~~ **Rozhodnuto uživatelem 2026-11-12:** **(a)** `Default =
   $true`, `Uploadable = $false` s `BlockedReason` „not verified on an ARM64 device (T-227)“; nahrávat až po úspěšném
   T-227 (přepnutí `Uploadable` je součást T-227). README a příručky do té doby soubory ARM64 ke stažení nenabízejí
   (podpora „připravuje se“), soubory ke stažení doplní T-227.
2. ~~**Uživatelé x64 buildu na ARM64 zařízení?**~~ **Rozhodnuto uživatelem 2026-11-12:** jen zdokumentovat ruční přechod
   (odinstalovat x64, nainstalovat ARM64); žádný automatický přechod.
3. ~~**winget a 1.0.0?**~~ **Rozhodnuto uživatelem 2026-11-12:** uzel `arm64` se do manifestu přidá až od prvního
   vydání, které `win-arm64` skutečně nahraje; šablona a skript ať umí obě varianty (podle souborů ve vydání).
4. ~~**Patří `win-arm64` do vydání 1.0.0 (T-198)?**~~ **Rozhodnuto uživatelem 2026-11-12:** ne, není to bloker 1.0.0;
   přijde v 1.0.x / 1.1.0. T-198 se nemění.

## Poznámky k implementaci
(vyplní agent po dokončení)
