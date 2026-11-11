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
- **Bez CI** (T-016 odložený): aktualizace manifestu z lokálního postupu vydání – po zveřejnění vydání skriptem T-055 volitelný krok `wingetcreate update <id> --version X.Y.Z --urls <url> … --submit`. Postup v `docs/release.md`. Token pro `wingetcreate` jen lokálně (`gh auth token` za běhu), nikdy v repozitáři ani v logu.
- Ověření manifestu `winget validate` a lokální instalace `winget install --manifest <složka>` před odesláním.
- **Nepodepsaný instalátor:** podle rozhodnutí uživatele (žádný placený podpis) se odesílá nepodepsaný. Riziko: automatická validace winget-pkgs kontroluje instalátor Microsoft Defenderem / SmartScreen a nepodepsaný, málo stahovaný instalátor může být označen nebo PR zdržen ruční kontrolou. Výsledek první validace zapsat do poznámek.
- Souhra s aktualizacemi Velopacku (aplikace aktualizovaná Velopackem vs. `winget upgrade`) – zdokumentovat.
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
- **Nové (2026-11-11), k rozhodnutí uživatele:**
  1. **Token pro `wingetcreate` vs. T-180.** T-180 plánuje pro vydání krátkodobý **fine-grained** token v `GH_TOKEN` omezený na repozitář LayerPeek. `wingetcreate` fine-grained tokeny nepodporuje (winget-create `doc/token.md`) a potřebuje forkovat `microsoft/winget-pkgs` a otevřít v něm PR. Skript takový token odmítne. Čím se má odesílat: (a) dočasné `gh auth login` (OAuth) jen pro odeslání a pak `gh auth logout`, (b) samostatný klasický token se scope `public_repo` jen v tomto terminálu (`$env:GH_TOKEN`), smazaný po odeslání, (c) jinak?
  2. **`winget upgrade` přes běžící instalaci** (ověřeno ve zdroji Velopacku 1.2.161, ne pokusem): nový `Setup.exe --silent` aplikaci násilně ukončí, nahradí celou `%LOCALAPPDATA%\LayerPeek` (smaže logy), aplikaci po instalaci nespustí a hook instalace **znovu zapne spouštění po přihlášení**, i když ho uživatel vypnul. Přijmout a jen zdokumentovat (stav teď), nebo upravit hook (nezapínat autostart při přeinstalaci, když už nastavení existuje – nový úkol), případně dát do manifestu `RequireExplicitUpgrade: true` (vyřadí balíček z `winget upgrade --all`, jednotlivý `winget upgrade` dál funguje)?
  3. **Viditelnost repozitáře:** `gh repo view` hlásí repozitář `pfoltynek/LayerPeek` už teď jako **veřejný** (stejně `docs/velopack.md`: „od 2026-10-30 je veřejný“). Zadání říká „veřejný s 1.0.0“ – jen potvrdit, že se nic dalšího (např. zveřejnění vydání) nečeká.
  4. **Microsoft CLA:** PR do `microsoft/winget-pkgs` vyžaduje podepsanou Contributor License Agreement Microsoftu (šablona PR winget-pkgs). Podepisuje ji uživatel svým účtem u prvního PR – souhlas?
  5. **Telemetrie `wingetcreate`:** nástroj má podle své dokumentace telemetrii; vypnout ji před prvním použitím (`wingetcreate settings`, v `settings.json` `"telemetry": { "disable": true }`, winget-create `doc/settings.md`) a zapsat to do `docs/prerequisites.md`?
  6. Volitelná pole manifestu, která jsem záměrně **nedal** (rozhodnutí produktu): `ElevationRequirement` (např. `elevationProhibited`: instalace z elevovaného terminálu by šla do profilu správce), `InstallLocation` (`--installto`, instalace jinam se neověřovala), `PrivacyUrl`. Přidat některé?

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

**Zbývá na Windows (úkol zůstává v `in-progress`, platforma `windows`; nejde přesunout do T-125, protože jde o nedokončenou práci a otevřené otázky):**
1. Odpovědět na otevřené otázky 1–6 (hlavně token, otázka 1).
2. Po zveřejnění 1.0.0 včetně souborů Windows: `pwsh scripts/release-winget.ps1 -Version 1.0.0 -OutputDirectory <složka>`, `winget validate --manifest <složka>` (ověřit i návratový kód `winget validate` při pouhých varováních – skript bere jen 0 jako úspěch).
3. `winget settings --enable LocalManifestFiles` (správce), `winget install --manifest <složka>`: tichá instalace bez dotazů a bez UAC, v Aplikacích „LayerPeek“ / „Petr Foltýnek“ / 1.0.0; `winget uninstall --manifest <složka>`: složka i položka v Aplikacích zmizí.
4. `pwsh scripts/release-winget.ps1 -Version 1.0.0 -DryRun`, pak ostře: `wingetcreate submit` se složkou funguje, ve výstupu ani v historii není token; PR se otevře; podepsat CLA.
5. Výsledek první validace winget-pkgs (Defender/SmartScreen u nepodepsaného instalátoru, ruční kontrola) a sloučení PR zapsat sem; zaškrtnout kritérium 1.
6. Po sloučení: `winget install --id 3DPrint.LayerPeek` / `winget install layerpeek` a `winget uninstall --id 3DPrint.LayerPeek` (kritérium 2); `winget list` po aktualizaci aplikací přes Velopack ukáže novou verzi; `winget upgrade` přes běžící instalaci ověřit proti popisu v `docs/release.md` (otázka 2).
7. Při prvním vydání po 1.0.0: nabídka v `release-publish.ps1` / `-AddToPublished` → `wingetcreate update` (kritérium 3).
8. Po přijetí: README.md / README.cz.md (sekce Download a Roadmap) a příručky `manuals/*` doplnit o `winget install`.
