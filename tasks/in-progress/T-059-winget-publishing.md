---
id: T-059
title: Publikace do winget
status: in-progress
owner: qa-reviewer
priority: P3
platform: windows
depends_on: [T-017, T-055]
---

## Cíl
Aplikaci jde nainstalovat a aktualizovat příkazem `winget install` z oficiálního repozitáře manifestů `microsoft/winget-pkgs` (https://github.com/microsoft/winget-pkgs). Fáze 2 distribuce (veřejné vydání pro Windows).

> **Předpoklad:** veřejný repozitář a veřejně stažitelné soubory vydání. Validace winget-pkgs stahuje instalátor z URL v manifestu. Rozhodnutí uživatele 2026-11-11: repozitář je veřejný s vydáním 1.0.0 a první odeslání proběhne hned po něm.

## Rozsah
- Identifikátor balíčku (rozhodnuto: `3DPrint.LayerPeek`) a první manifest: revidované šablony v `build/winget` (vstup místo interaktivního `wingetcreate new`), z nichž `scripts/release-winget.ps1` vytvoří manifest verze s SHA256 instalátoru z GitHubu a odešle ho `wingetcreate submit`.
- Typ instalátoru a přepínač tiché instalace podle instalátoru z T-017 / doporučení T-056 (Velopack `Setup.exe` → typ `exe`, `--silent`); `Scope: user`; architektura jen `x64` (Windows ARM64 se nepodporuje, rozhodnutí uživatele 2026-11-01, T-130).
- Licence v manifestu: `GPL-3.0-only`, odkaz na `LICENSE`.
- **Bez CI** (T-016 odložený): aktualizace manifestu z lokálního postupu vydání – po zveřejnění vydání skriptem T-055 volitelný krok `wingetcreate update <id> --version X.Y.Z --urls <url> … --submit`. Postup v `docs/release.md`. Token pro `wingetcreate` jen lokálně (`gh auth token` za běhu, z dočasného `gh auth login`, rozhodnutí 2026-11-11), nikdy v repozitáři ani v logu; telemetrie `wingetcreate` vypnutá.
- Ověření manifestu `winget validate` a lokální instalace `winget install --manifest <složka>` před odesláním.
- **Nepodepsaný instalátor:** podle rozhodnutí uživatele (žádný placený podpis) se odesílá nepodepsaný. Riziko: automatická validace winget-pkgs kontroluje instalátor Microsoft Defenderem / SmartScreen a nepodepsaný, málo stahovaný instalátor může být označen nebo PR zdržen ruční kontrolou. Výsledek první validace zapsat do poznámek.
- Souhra s aktualizacemi Velopacku (aplikace aktualizovaná Velopackem vs. `winget upgrade`) – zdokumentovat (`docs/release.md`, příručky); oprava autostartu při přeinstalaci v T-201.
- Předběžná vydání `0.x` (T-055): do winget se neposílají (rozhodnutí 2026-11-11, začíná se 1.0.0).

## Akceptační kritéria
- [ ] První manifest přijat do `microsoft/winget-pkgs` (Windows, po vydání 1.0.0)
- [ ] `winget install <id>` nainstaluje aplikaci bez dotazů, `winget uninstall <id>` ji beze zbytku odinstaluje (ověřeno; před odesláním `winget validate` a `winget install --manifest`, Windows)
- [ ] Postup aktualizace manifestu při dalším vydání zdokumentovaný v `docs/release.md` a jednou vyzkoušený (zdokumentováno; vyzkoušení `wingetcreate update` při prvním vydání po 1.0.0 na Windows)
- [ ] Žádný token v repozitáři ani ve výstupu skriptů (Pester testy s falešnými nástroji to ověřují; ověřit ještě se skutečným `wingetcreate` při prvním odeslání)
- [x] Volitelný krok skriptu vydání: `release-publish.ps1` a `release-upload.ps1 -AddToPublished` po zveřejnění nabídnou odeslání, `scripts/release-winget.ps1` samostatně; mimo Windows jasná hláška
- [x] Pester testy kroku (falešné `gh`, `wingetcreate`, `winget`; token není ve výstupu, v argumentech ani v prostředí po běhu)
- [x] `dotnet build` a `dotnet test` bez chyb a varování (macOS)

## Otevřené otázky
- ~~Kdy začít: až po zveřejnění repozitáře a první stabilní verzi `1.0.0`, nebo už s předběžnými `0.x`?~~ **Rozhodnuto uživatelem 2026-11-11:** začít hned; další vydání bude **1.0.0** (.NET 11 je GA), repozitář je veřejný s 1.0.0 a první odeslání do winget proběhne hned po vydání 1.0.0. Verze `0.x` se do winget neposílají.
- ~~Identifikátor balíčku a jméno vydavatele (`Publisher`) v manifestu?~~ **Rozhodnuto uživatelem 2026-11-11:** `PackageIdentifier` **`3DPrint.LayerPeek`**, `Publisher` **`Petr Foltýnek`**.
- ~~Má být odeslání do winget volitelným krokem skriptu vydání (T-055), nebo samostatným ručním příkazem?~~ **Rozhodnuto uživatelem 2026-11-11:** volitelný krok skriptu vydání – po zveřejnění se skript zeptá, zda spustit `wingetcreate update … --submit`; token z `gh auth token` za běhu, nikdy uložený, vypsaný ani viditelně předaný na příkazové řádce, která by skončila v historii nebo logu.
- **Otázky 1–6 z 2026-11-11 – rozhodnuto uživatelem 2026-11-11:**
  1. ~~Token pro `wingetcreate` vs. T-180 (fine-grained `GH_TOKEN` `wingetcreate` nepodporuje): dočasné `gh auth login`, klasický token v terminálu, nebo jinak?~~ **Rozhodnuto:** **dočasné `gh auth login` (OAuth v prohlížeči) jen pro odeslání**, pak `gh auth logout` a zpět obvyklé přihlášení (T-180: omezený `GH_TOKEN`). Token se nikdy neukládá ani nevypisuje. Skript ověří, zda token `gh` může `wingetcreate` použít (scope `public_repo`), a když ne, vypíše přesný postup.
  2. ~~`winget upgrade` přes běžící instalaci (ukončí aplikaci, smaže logy, nespustí ji, hook znovu zapne autostart): jen zdokumentovat, upravit hook, nebo `RequireExplicitUpgrade`?~~ **Rozhodnuto:** opravu hooku (nezapínat autostart, když ho uživatel vypnul) řeší **nový úkol T-201** (ui-dev, P1, windows). Zbytek chování (aplikace ukončena, nespuštěna, logy smazané) zdokumentovat v `docs/release.md` a v kapitole 12 všech 8 příruček a doporučit aktualizaci v aplikaci.
  3. ~~Viditelnost repozitáře~~ **Rozhodnuto:** repozitář je **už veřejný** (ověřeno), nic dalšího se nečeká.
  4. ~~Microsoft CLA~~ **Rozhodnuto:** uživatel CLA **podepíše u prvního PR** svým účtem; zapsáno do postupu.
  5. ~~Telemetrie `wingetcreate`~~ **Rozhodnuto:** **vypnout** přes jeho `settings.json`; skript ji nastaví před spuštěním `wingetcreate`, opakovaně bezpečně a bez přepsání jiných nastavení.
  6. ~~Volitelná pole manifestu (`ElevationRequirement`, `InstallLocation`, `PrivacyUrl`)~~ **Rozhodnuto:** **žádná další volitelná pole** (ani `RequireExplicitUpgrade`).
- **Nové (2026-11-11, po doplnění), k rozhodnutí uživatele:**
  1. T-201 má podle zadání `depends_on: [T-059]`. T-059 bude hotový až po prvním odeslání po vydání 1.0.0, takže by oprava autostartu do 1.0.0 podle pravidel nestihla, i když na T-059 technicky nezávisí (týká se i ruční přeinstalace `Setup.exe`). Zrušit závislost? (stejná otázka je v T-201)
  2. Pozdější verze: `wingetcreate` před vytvořením větve synchronizuje fork (`merge-upstream`). Když se ve `microsoft/winget-pkgs` mezitím změní soubory `.github/workflows`, GitHub může synchronizaci tokenu bez scope `workflow` odmítnout (neověřeno; v issues winget-create jsem hlášení nenašel). Výchozí `gh auth login` scope `workflow` nemá. Řešit až při výskytu (`gh auth refresh --scopes workflow` jen pro odeslání, nebo smazat fork), nebo přihlašovat rovnou se `workflow`?

## Poznámky k implementaci
Rozpracováno 2026-11-11 na macOS (agent qa-reviewer), větev `feature/T-059-winget-publishing`. Nic se neodeslalo, nic se nepushnulo.

**Hotovo (macOS):**
- `build/winget/` – revidované šablony manifestu 1.28.0 (`3DPrint.LayerPeek.yaml`, `.installer.yaml`, `.locale.en-US.yaml`): `exe`, `Scope: user`, jen `x64`, `--silent` pro `Silent` i `SilentWithProgress`, `Log: --log "<LOGPATH>"`, `ProductCode: LayerPeek`, `UpgradeBehavior: install`, `License: GPL-3.0-only`, `LicenseUrl` a `ReleaseNotesUrl` dané verze, `Moniker: layerpeek`, 8 tagů, popis s větou o nezávislosti na Prusa Research. Vykreslená verze 1.0.0 prošla JSON schématy 1.28.0 z `microsoft/winget-cli` (Python `jsonschema` v dočasném venv; negativní kontrola chyby zachytila).
- `scripts/ReleaseTools/ReleaseTools.psm1` – funkce `*-Winget*` (čisté rozhodovací funkce + `Invoke-WingetRelease`, `Invoke-WingetReleaseOffer`); `Get-GitHubRelease` čte navíc `publishedAt` (datum vydání v manifestu).
- `scripts/release-winget.ps1` – samostatný krok (`-DryRun`, `-OutputDirectory` pro ruční ověření a revizi i na macOS). `release-publish.ps1` a `release-upload.ps1 -AddToPublished` ho po zveřejnění nabídnou (jen Windows, stabilní verze se `Setup.exe` ve vydání; jinak vypíšou proč ne). Selhání odeslání nemění výsledek zveřejnění.
- Token: `gh auth token` až po potvrzení, do `wingetcreate` jen přes `WINGET_CREATE_GITHUB_TOKEN` procesu po dobu běhu (pak předchozí hodnota), výstup maskovaný `***`, fine-grained token odmítnut.
- Testy: nový `scripts/ReleaseTools/ReleaseWinget.Tests.ps1` (30 testů, ~30 s; zaregistrovaný v `tests/LayerPeek.Scripts.Tests/PesterScriptTests.cs`), dvě nové kontroly v `ReleaseOrchestrator.Tests.ps1` (poznámka u `0.x` po zveřejnění i po `-AddToPublished`). Mutační kontrola: bez maskování výstupu test selže.
- Dokumentace: `docs/release.md` („Publikace do winget (T-059)“: rozhodnutí, ověřená pravidla winget-pkgs s odkazy, manifest, postup první a dalších verzí, token, odmítnutí, souhra Velopack a `winget upgrade`), `docs/prerequisites.md` (`wingetcreate`).

**Ověřená pravidla a fakta (2026-11-11, zdroje v `docs/release.md`):**
- Identifikátor `Publisher.Package`, rozlišuje velikost písmen, složka `manifests/3/3DPrint/LayerPeek/<verze>`; v indexu winget (`source2.msix`, 16 333 balíčků, 2026-11-11) není žádný `3DPrint.*` ani moniker `layerpeek`.
- `Publisher` má odpovídat `Publisher` v klíči Uninstall: Velopack 1.2.161 zapisuje `Publisher` = `--packAuthors` = `Petr Foltýnek` (`registry.rs`), `DisplayName` = `LayerPeek`, `DisplayVersion` = verze. **Shoduje se s rozhodnutím, metadata instalátoru se neměnila.** Pester test hlídá shodu šablony s `Get-VelopackSettings`.
- Tiché přepínače Velopacku ze zdroje `setup.rs` 1.2.161: `-s, --silent` (skryje dialogy, odpoví „ano“, aplikaci po instalaci nespustí), `-l, --log <FILE>`, `-t, --installto <DIR>`.
- Schéma podle šablony PR winget-pkgs: 1.28. PR = jedna verze, jen manifest; Microsoft CLA; `winget validate` a `winget install --manifest` před odesláním.
- Pravidlo pro předběžné verze jsem v dokumentaci winget-pkgs ani na Microsoft Learn nenašel; nepotřebujeme ho (jen stabilní `X.Y.Z`).
- `wingetcreate update` architekturu bere nejdřív z URL, `--token` dokumentace nedoporučuje (může skončit v logu), proměnnou `WINGET_CREATE_GITHUB_TOKEN` čte jako první (`TokenHelper.cs`). Nástroj je MIT, na Macu se nespouští.

**Doplnění 2026-11-11 na macOS (agent qa-reviewer, větev `feature/T-059-winget-followup`)** podle odpovědí na otázky 1–6. Nic se neodeslalo ani nepushnulo.
- **Token (otázka 1):** nové funkce `Get-GhTokenScopes` (scopes z hlavičky `X-OAuth-Scopes` odpovědi `gh api --include user`, token sám se nečte), `Get-WingetTokenScopeProblems`, `Get-WingetLoginInstructions` / `Write-WingetLoginInstructions`. `Invoke-WingetRelease` zkontroluje scopes ještě před plánem (i v `-DryRun`): bez hlavičky (fine-grained / aplikace GitHubu), bez `public_repo`/`repo` nebo s 401 odmítne a vypíše postup `Remove-Item Env:GH_TOKEN` (jen když je nastavený `GH_TOKEN`/`GITHUB_TOKEN`), `gh auth login --hostname github.com --web --git-protocol https`, spuštění skriptu, `gh auth logout --hostname github.com` a zpět `GH_TOKEN` (T-180). Po skutečném odeslání připomene `gh auth logout`. Potřebné scopes ověřené v winget-create: `doc/token.md` (klasický token se `public_repo`, `delete_repo` volitelně, fine-grained ne – issue #595), `GitHubOAuth.cs` (vlastní OAuth žádá `repo`); `gh auth login` má výchozí `repo`, `read:org`, `gist`.
- **Telemetrie (otázka 5):** `Get-WingetCreateSettingsPaths`, `ConvertTo-WingetCreateSettingsWithoutTelemetry`, `Set-WingetCreateTelemetryDisabled`, `Disable-WingetCreateTelemetry`. Ověřeno ve zdroji winget-create: `"Telemetry": { "disable": true }` (`doc/settings.md`, `UserSettings.cs`, `SettingsModel.cs`), soubor v `LocalAppStatePath` (`Common.cs`): MSIX `%LOCALAPPDATA%\Packages\Microsoft.WindowsPackageManagerManifestCreator_8wekyb3d8bbwe\LocalState\settings.json` (manifest `Microsoft.WingetCreate` nabízí `msix` i `portable`, který winget vybere, neověřeno – skript počítá s oběma), jinak `%LOCALAPPDATA%\Microsoft\WindowsPackageManagerManifestCreator\settings.json`; telemetrie se čte na začátku každého běhu (`Program.cs`), první běh bez souboru ho zapíše se zapnutou telemetrií. Skript ji vypne po potvrzení těsně před `wingetcreate`; chybějící soubor vytvoří, ostatní nastavení zachová, už vypnutou nemění, soubor, který není objekt JSON, nepřepíše a odeslání odmítne. `docs/prerequisites.md`: po instalaci `wingetcreate` nespouštět, nejdřív `Disable-WingetCreateTelemetry`.
- **`winget upgrade` (otázka 2):** nový úkol **T-201** (oprava hooku). `docs/release.md` („Velopack a `winget upgrade`“) a kapitola 12 všech 8 příruček: `winget upgrade` aplikaci násilně ukončí, nespustí ji znovu a smaže logy; doporučena aktualizace v aplikaci.
- Otázky 3, 4, 6 zapsané v „Rozhodnutí“ v `docs/release.md` (CLA i v postupu první verze).
- Testy: `ReleaseWinget.Tests.ps1` 38 testů (+8): čisté funkce (scopes, postup přihlášení, cesty `settings.json`, sloučení nastavení, idempotentní zápis, nepřepsání poškozeného souboru) a běhy s falešnými nástroji (falešný `gh api --include user` s hlavičkou podle `LAYERPEEK_FAKE_GH_SCOPES`, falešný `wingetcreate` zapíše, zda měl telemetrii vypnutou; `LOCALAPPDATA` v dočasné složce, nikdy skutečný profil). Mutační kontrola: bez volání `Disable-WingetCreateTelemetry` nebo bez kontroly scopes selžou 4 testy.

**Zbývá na Windows (úkol zůstává v `in-progress`, platforma `windows`; nejde přesunout do T-125, protože jde o nedokončenou práci a otevřené otázky):**
1. ~~Odpovědět na otevřené otázky 1–6~~ (zodpovězeno 2026-11-11); zbývají nové otázky 1–2. Po instalaci `wingetcreate` ho nespouštět, nejdřív `Disable-WingetCreateTelemetry` (`docs/prerequisites.md`); ověřit, že soubor vznikl tam, kde ho ukáže `wingetcreate info` (MSIX vs. nezabalený).
2. Po zveřejnění 1.0.0 včetně souborů Windows: `pwsh scripts/release-winget.ps1 -Version 1.0.0 -OutputDirectory <složka>`, `winget validate --manifest <složka>` (ověřit i návratový kód `winget validate` při pouhých varováních – skript bere jen 0 jako úspěch).
3. `winget settings --enable LocalManifestFiles` (správce), `winget install --manifest <složka>`: tichá instalace bez dotazů a bez UAC, v Aplikacích „LayerPeek“ / „Petr Foltýnek“ / 1.0.0; `winget uninstall --manifest <složka>`: složka i položka v Aplikacích zmizí.
4. Dočasné přihlášení podle `docs/release.md` („Token“): `Remove-Item Env:GH_TOKEN` (když je nastavený), `gh auth login --hostname github.com --web --git-protocol https`; `pwsh scripts/release-winget.ps1 -Version 1.0.0 -DryRun` (kontrola scopes projde; ověřit i odmítnutí s fine-grained `GH_TOKEN`), pak ostře: `wingetcreate submit` se složkou funguje, telemetrie je v `settings.json` vypnutá, ve výstupu ani v historii není token; PR se otevře; uživatel podepíše Microsoft CLA. Pak `gh auth logout --hostname github.com` a zpět `GH_TOKEN` (T-180).
5. Výsledek první validace winget-pkgs (Defender/SmartScreen u nepodepsaného instalátoru, ruční kontrola) a sloučení PR zapsat sem; zaškrtnout kritérium 1.
6. Po sloučení: `winget install --id 3DPrint.LayerPeek` / `winget install layerpeek` a `winget uninstall --id 3DPrint.LayerPeek` (kritérium 2); `winget list` po aktualizaci aplikací přes Velopack ukáže novou verzi; `winget upgrade` přes běžící instalaci ověřit proti popisu v `docs/release.md` a v příručkách (ukončení, nespuštění, smazané logy; autostart podle stavu T-201).
7. Při prvním vydání po 1.0.0: nabídka v `release-publish.ps1` / `-AddToPublished` → `wingetcreate update` (kritérium 3).
8. Po přijetí: README.md / README.cz.md (sekce Download a Roadmap) a příručky `manuals/*` doplnit o `winget install`.
