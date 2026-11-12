---
id: T-209
title: Windows ARM64 – vydání `win-arm64` (skripty, Velopack, aktualizace, winget, README a příručky)
status: todo
owner: qa-reviewer
priority: P2
platform: windows
depends_on: [T-207, T-208]
---

> **Rozhodnutí uživatele 2026-11-12:** LayerPeek se vydává i pro `win-arm64` (série T-207 až T-210, ruší T-130).
> Tento úkol vrací `win-arm64` do skriptů vydání, které ho od T-130 odmítají, a přidává ho do instalátoru, aktualizací,
> manifestu winget a dokumentace pro uživatele.

## Cíl
Jedno spuštění `pwsh scripts/release-build.ps1` na Windows x64 sestaví a zabalí `win-x64` i `win-arm64`; vydání na
GitHubu nese soubory obou architektur Windows (podle otázky 1 i jejich nahrání), aplikace nainstalovaná z ARM64
instalátoru se aktualizuje z kanálu `win-arm64` a uživatel v README a příručce najde, který soubor stáhnout.

## Rozsah
1. **Konfigurace RID** (`$script:ReleaseRuntimes` ve `scripts/ReleaseTools/ReleaseTools.psm1`): přidat
   `'win-arm64' = @{ Os = 'windows'; Default = …; Uploadable = …; BlockedReason = … }` podle otázky 1. Obecný mechanismus
   `Uploadable = $false` / `BlockedReason` z T-055 / T-130 existuje a je testovaný nad umělou tabulkou. Odstranit
   odmítnutí a texty „not supported (T-130)“ v `scripts/aot-smoke.ps1`, `scripts/release-build.ps1` (nápověda
   `-Runtime`), `release-upload.ps1`, `release-publish.ps1`, komentář nad tabulkou. Pester testy, které dnes ověřují
   odmítnutí `win-arm64` (`ReleaseTools.Tests.ps1`, `aot-smoke`), přepsat na nové chování.
2. **Sestavení** (`release-build.ps1`): AOT publish `win-arm64` (toolchain z T-207, FFmpeg z T-208); kontrola ARM64 PE
   (funkce z T-207); smoke test na x64 hostiteli „not verified by running (this machine is windows-x64)“ (existující
   větev `Test-RuntimeRunnable`); `aot-smoke.ps1 -Runtime win-arm64` na x64 skončí jasnou hláškou „nelze spustit na
   tomto stroji“ (ne chybou buildu – podle otázky 1). Na ARM64 hostiteli (T-210) se smoke test spustí normálně.
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
   Uninstall a `ProductCode` ve winget) – chování při instalaci ARM64 přes existující x64 instalaci zjistit a
   zdokumentovat podle otázky 2.
6. **winget** (`build/winget/3DPrint.LayerPeek.installer.yaml`, `scripts/release-winget.ps1`, funkce `*-Winget*`):
   druhý uzel `Installers` s `Architecture: arm64` a URL `…-win-arm64-Setup.exe`; `wingetcreate update` dostane dvě URL
   (architekturu bere z URL); kontrola, že vydání obsahuje oba `Setup.exe`; `ReleaseWinget.Tests.ps1`. Koordinace
   s T-059 (rozpracovaný, první odeslání po 1.0.0) podle otázky 3.
7. **Testy a nástroje s pevným `win-x64`:** `VelopackInstallerTests` (`Runtime = "win-x64"`), `IconAndBuildTests`
   (cesta `win-x64/publish`), `tools/run-e2e.ps1`, `tools/windows-verify.ps1` (`$script:Runtime`): brát RID hostitele
   (`win-x64` / `win-arm64` podle architektury OS), aby šly spustit v T-210 na ARM64 zařízení; na x64 beze změny.
8. **Dokumentace a texty pro uživatele:**
   - `README.md` + `README.cz.md`: tabulka platforem („Windows on ARM64 ❌ not supported“ → podporováno podle otázky 1),
     sekce stažení (který soubor pro x64 a který pro ARM64, jak zjistit architekturu: Nastavení → Systém → O systému →
     Typ systému), příkazy publish;
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
9. Licence třetích stran pro `win-arm64` řeší T-207 (runtime balíčky) a T-208 (FFmpeg); zde jen kontrola, že okno
   licencí v ARM64 buildu nic nepostrádá.

## Akceptační kritéria
- [ ] `pwsh scripts/release-build.ps1` (bez nahrání, `-SkipFeedSignature` nebo zkušebním klíčem) na Windows x64 sestaví
      `win-x64` i `win-arm64` (podle `Default`): zip, `Setup.exe`, `.nupkg`, feed a podpis feedu `win-arm64`, řádky
      v `SHA256SUMS-windows.txt` (odpovídají `Get-FileHash`); exe a DLL `win-arm64` jsou ARM64 PE; smoke test
      `win-arm64` zapsaný jako neověřený spuštěním
- [ ] `release-upload.ps1 -DryRun` se chová podle `Uploadable` (otázka 1); nic se nenahrálo
- [ ] Šablona winget má uzly `x64` a `arm64`, vykreslený manifest projde schématem 1.28.0 (stejně jako v T-059) a
      Pester testy
- [ ] Pester testy skriptů (bez sítě) a C# testy aktualizací pokrývají `win-arm64`; odmítnutí z T-130 odstraněna
- [ ] README EN + CZ, všech 8 příruček, `docs/release.md`, `docs/velopack.md`, `docs/prerequisites.md`,
      `docs/architecture.md`, `docs/windows-manual-testing.md`, `CHANGELOG.md` aktuální; grep `(?i)win-arm64|arm64|T-130`
      mimo `tasks/` nenajde zastaralé „nepodporováno“
- [ ] Pokusné instalace a dočasné složky uklizené
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. **Vydávat `win-arm64` před ověřením na zařízení?** Uživatel ARM64 zařízení nemá a Windows x64 ARM64 binárky spustit
   neumí (žádná emulace ARM64 na x64). Varianty: (a) `Default = $true`, `Uploadable = $false` s `BlockedReason`
   „not verified on an ARM64 device (T-210)“, dokud T-210 neproběhne; (b) nahrávat hned a v README / příručce /
   textu vydání označit jako „experimentální, netestováno na zařízení“; (c) nahrávat bez označení. **Doporučení: (a)**
   – stejný postup jako dřív u T-057 / T-063; přepnutí na `Uploadable = $true` je jednořádková změna po T-210.
2. **Uživatelé x64 buildu na ARM64 zařízení** (x64 build tam běží v emulaci Prism a aktualizuje se z kanálu
   `win-x64`): nechat je na x64 (Velopack kanál sám nemění), jen v příručce popsat ruční přechod (odinstalovat x64,
   nainstalovat ARM64), nebo aplikace má sama nabídnout přechod na ARM64? **Doporučení:** jen dokumentovat; automatický
   přechod by byl nový UX úkol.
3. **winget a 1.0.0:** má první odeslání do winget (T-059, hned po 1.0.0) obsahovat i `arm64`, nebo až po T-210?
   **Doporučení:** `arm64` do manifestu až s prvním vydáním, které `win-arm64` skutečně nahraje (podle otázky 1);
   šablona a skript ať umí obě varianty.
4. **Patří `win-arm64` do vydání 1.0.0 (T-198)?** Pokud ano, T-202 → T-208 → T-209 se stávají cestou k 1.0.0 a T-198 je
   potřeba doplnit. **Doporučení:** ne jako bloker 1.0.0; přidat v 1.0.x / 1.1.0 podle otázky 1.

## Poznámky k implementaci
(vyplní agent po dokončení)
