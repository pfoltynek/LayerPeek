# Vydání (release)

Vydání se dělá lokálně bez CI (GitHub Actions jsou odložené, T-016). Windows soubory se sestaví na Windows, macOS soubory na Macu (Native AOT nejde kompilovat napříč OS). Oba běhy nahrávají do **stejného konceptu** (draft) vydání pro tag `vX.Y.Z` na GitHubu. Zveřejnění je samostatný poslední krok, takže nikdo nevidí vydání s polovinou souborů.

| Skript | Co dělá | Mění GitHub? |
|---|---|---|
| `scripts/release-build.ps1` | build + testy, Native AOT publish pro RID tohoto OS, AOT smoke test, archivy, `SHA256SUMS-<os>.txt` | ne |
| `scripts/release-upload.ps1` | kontroly, vytvoření konceptu vydání (první běh) nebo nahrání do existujícího konceptu (druhý OS) | ano (kromě `-DryRun`) |
| `scripts/release-publish.ps1` | kontroly úplnosti konceptu a jeho zveřejnění | ano (kromě `-DryRun`) |
| `scripts/ReleaseTools/ReleaseTools.Tests.ps1` | Pester testy logiky skriptů bez sítě (spouští je i `dotnet test`, viz níže) | ne |

Všechny skripty jsou pro PowerShell 7 a běží na Windows i macOS. Prerekvizity (MSVC / Xcode Command Line Tools, `gh` CLI) jsou v [prerequisites.md](prerequisites.md).

## Rozhodnutí (2026-10-21)

- Vydávané RID: `win-x64`, `osx-arm64`. `win-arm64` se sestaví jen na výslovné vyžádání a nenahrává se, dokud není hotový T-057 (FFmpeg pro ARM64).
- Verze: jediný zdroj je `<Version>` v kořenovém `Directory.Build.props` (tvar `X.Y.Z`). Odtud ji má exe, okno O aplikaci, názvy souborů i tag `vX.Y.Z`.
- Vydává se jen z `main`: tagovaný commit musí být obsažený v `origin/main`.
- První nahrání vytvoří **koncept** (`--draft`). Verze `0.x` jsou na GitHubu označené jako předběžné (`--prerelease`).
- Poznámky k vydání jsou sekce verze z `CHANGELOG.md` (formát [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), anglicky podle `CLAUDE.md`).
- Bez placeného podpisu: Windows soubory jsou nepodepsané, podpis macOS řeší T-019.

## Postup

### 1. Větev release (Gitflow)

```powershell
git switch develop
git pull
git switch -c release/X.Y.Z
```

- V `Directory.Build.props` nastav `<Version>X.Y.Z</Version>`.
- V `CHANGELOG.md` dej sekci verze skutečné datum vydání: `## [X.Y.Z] - YYYY-MM-DD` (zástupné `- unreleased` nahraď datem; změny z `## [Unreleased]` přesuň do ní a `## [Unreleased]` nech prázdnou nahoře). Skript vydání odmítne sekci bez platného data (i `unreleased`), prázdnou sekci i chybějící sekci.
- Commit, případné opravy jen stabilizační.

Volitelně ověř balení ještě před tagem: `pwsh scripts/release-build.ps1` jde spustit na libovolném commitu (výstup se ale nahraje jen z tagovaného commitu).

### 2. Sloučení do `main`, tag a push

```powershell
git switch main
git pull
git merge --no-ff release/X.Y.Z
git tag -a vX.Y.Z -m "Release X.Y.Z"
git push origin main vX.Y.Z
git switch develop
git merge --no-ff release/X.Y.Z
git push origin develop
git branch -d release/X.Y.Z
```

### 3. Běh na Windows

Na čistém checkoutu tagovaného commitu (pracovní strom nesmí obsahovat žádné změny ani nesledované soubory):

```powershell
git switch --detach vX.Y.Z
pwsh scripts/release-build.ps1                 # build, testy, publish win-x64, smoke test, zip, SHA256SUMS-windows.txt
pwsh scripts/release-upload.ps1 -DryRun         # jen kontroly a výpis, co by se nahrálo
pwsh scripts/release-upload.ps1                 # vytvoří koncept vydání a nahraje soubory
```

Smoke test spustí publikovanou aplikaci na 10 s s tvými skutečnými nastaveními (může ukázat skutečné notifikace) a odmítne běžet, když už aplikace běží. Před během ji ukonči.

### 4. Běh na Macu

Stejné příkazy na Macu (Apple Silicon). Vznikne `PrusaTrayDiagnostic-X.Y.Z-osx-arm64.tar.gz` a `SHA256SUMS-macos.txt`, které `release-upload.ps1` přidá do existujícího konceptu. Dokud T-019 nedodá `.app` bundle, obsahuje `.tar.gz` publish složku tak, jak je.

### 5. Kontrola a zveřejnění

1. Na GitHubu (*Releases*, koncept je vidět jen pro správce repozitáře) zkontroluj text a soubory.
2. Zveřejni:

```powershell
pwsh scripts/release-publish.ps1 -DryRun
pwsh scripts/release-publish.ps1
```

`release-publish.ps1` zveřejní koncept jen tehdy, když obsahuje `SHA256SUMS-windows.txt` i `SHA256SUMS-macos.txt`, všechny soubory z nich jsou nahrané se stejným SHA256 (podle `digest`, který hlásí GitHub), žádný jiný soubor tam není a není tam soubor `win-arm64`. Přepínač `-AllowIncomplete` dovolí zveřejnit vydání bez souborů některého OS (např. jen Windows); nahrané soubory se kontrolují i tak.

### 6. Ověření staženého souboru

```powershell
Get-FileHash .\PrusaTrayDiagnostic-X.Y.Z-win-x64.zip -Algorithm SHA256   # porovnat s SHA256SUMS-windows.txt
```

```bash
shasum -a 256 -c SHA256SUMS-macos.txt
```

## Výstupy sestavení

`artifacts/` je v `.gitignore`. Každý běh `release-build.ps1` složku verze nejdřív smaže, proto sestav všechny RID jednoho OS v jednom běhu.

```
artifacts/release/<verze>/
  publish/<rid>/        Native AOT publish složka (vstup balení)
  dist/                 soubory k nahrání: archivy, později instalátory, SHA256SUMS-<os>.txt
  build-info.json       značka sestavení: verze, commit, čistý strom, RID, soubory a jejich SHA256, výsledek smoke testu
```

- Archiv: `PrusaTrayDiagnostic-<verze>-<RID>.zip` (Windows, obsah publish složky bez nadřazené složky), `.tar.gz` (macOS, zachová spustitelný bit).
- **Ladicí symboly se nevydávají** (rozhodnuto 2026-10-21): archiv neobsahuje soubory `*.pdb` ani nic ze složek `*.dSYM` a nikam se nenahrávají. V publish složce zůstanou pro lokální ladění. Zip `win-x64` má tak asi 70 MB místo 120 MB.
- Kontrolní součty: jeden soubor na OS, formát `sha256sum` (`<hash>  <soubor>`, LF), obsahuje všechny soubory z `dist/` tohoto běhu.
- Build zastaví jakékoli varování (`-warnaserror` u `dotnet build` i `dotnet publish`, tedy i trim/AOT varování z ILC). Testy běží v konfiguraci Release.
- Na Windows skript ověří, že *Product version* exe odpovídá `<Version>`.
- Smoke test běží jen pro RID tohoto stroje (OS a architektura). Ostatní RID skript vypíše jako „not verified by running“. Smoke test běží nad hotovou publish složkou (`aot-smoke.ps1 -PublishDir`) a skript ověří, že ji nezměnil.

## Co skripty odmítnou

`release-upload.ps1` i `release-publish.ps1` vypíšou všechny důvody najednou a skončí s kódem 1:

| Kontrola | Hláška (začátek) |
|---|---|
| pracovní strom není čistý (i nesledované soubory) | `Working tree is not clean` |
| `HEAD` nemá tag `vX.Y.Z` | `HEAD has no tag vX.Y.Z` |
| tag na `HEAD` neodpovídá `<Version>` | `HEAD is tagged … but the version in Directory.Build.props is …` |
| tag není na `origin` / ukazuje jinam | `Tag vX.Y.Z is not on origin` / `Tag vX.Y.Z on origin points to …` |
| commit není v `origin/main` | `HEAD … is not contained in origin/main` |
| `origin` nejde načíst / `main` z `origin` není stažený | `git ls-remote origin failed` / `origin/main (…) is not in the local repository. Run: git fetch origin` |
| `gh` chybí / není přihlášený | `GitHub CLI (gh) is not installed` / `GitHub CLI is not logged in` |

Navíc jen `release-upload.ps1`:

| Kontrola | Hláška (začátek) |
|---|---|
| `CHANGELOG.md` nemá sekci verze, datum nebo text | `… has no section '## [X.Y.Z] - YYYY-MM-DD'` / `… has no release date` / `… is empty` |
| chybí výstup `release-build.ps1` | `No build output for version …` |
| výstup je z jiného commitu / verze / ze špinavého stromu | `Build output comes from commit …` / `… was made from a working tree with uncommitted …` |
| výstup obsahuje `win-arm64` | `Build output contains win-arm64, which must not be uploaded` |
| v `dist/` je soubor navíc, chybí nebo nesedí SHA256 | `dist contains files not produced by the build` / `… does not match its SHA256` |
| vydání už je zveřejněné | `Release vX.Y.Z is already published` |
| soubor se stejným názvem, ale jiným obsahem už je nahraný | `Already uploaded with different content … Re-run with -Force` |

Skripty `origin` jen čtou jedním `git ls-remote`, nic nestahují ani nepushují (lokální refy se nemění). `gh auth status` se volá bez výpisu.

## Opakování a chyby

| Situace | Co dělat |
|---|---|
| nahrání spadlo uprostřed (síť) | spusť `release-upload.ps1` znovu: soubory se stejným obsahem přeskočí, zbytek nahraje |
| po novém buildu stejného commitu hlásí „different content“ | AOT build nemusí být bajtově stejný. Buď nahraj původní výstup, nebo spusť s `-Force` (přepíše soubory v konceptu, `--clobber`) |
| v tagovaném commitu je chyba | koncept smaž (`gh release delete vX.Y.Z`), tag smaž (`git push origin :refs/tags/vX.Y.Z`, `git tag -d vX.Y.Z`), oprav to v `release/X.Y.Z` nebo `hotfix/X.Y.Z` a začni krokem 2. Už zveřejněné vydání neměň, vydej novou verzi |
| oba OS spustily `release-upload.ps1` současně a jeden skončil chybou při vytváření | spusť ho znovu, nahraje do konceptu, který vytvořil ten druhý |
| `release-build.ps1` hlásí chybějící komponentu MSVC | viz [prerequisites.md](prerequisites.md) |

## Bod rozšíření pro instalátory (T-017, T-019)

`scripts/release-build.ps1` má explicitní seznam `$packagingSteps` (žádné hledání skriptů podle masky). Každý krok má `Name`, `Os` (pro které OS se spouští) a `Invoke` – scriptblock, který dostane kontext:

| Vlastnost | Význam |
|---|---|
| `Version` | verze z `Directory.Build.props` |
| `Runtime` | RID, např. `win-x64` |
| `Os` | `windows` / `macos` |
| `PublishDirectory` | hotová a smoke testem ověřená publish složka RID |
| `OutputDirectory` | `dist/`, sem krok uloží své soubory |

Krok vrátí cesty souborů, které vytvořil přímo v `dist/`. Skript je sám zapíše do `SHA256SUMS-<os>.txt` a `build-info.json`, takže je `release-upload.ps1` nahraje a `release-publish.ps1` zkontroluje bez další změny. Příklad nového kroku (T-017):

```powershell
[pscustomobject]@{
    Name   = 'installer'
    Os     = @('windows')
    Invoke = {
        param($Context)
        # vytvoří instalátor z $Context.PublishDirectory do $Context.OutputDirectory a vrátí jeho cestu
    }
}
```

T-019 přidá krok pro macOS (`.app` + `.dmg`) a případně změní vstup archivu z publish složky na `.app` bundle. Build, publish a smoke test se tím nemění.

Poznámky pro navazující kroky:

- **Názvy souborů musí být v rámci jednoho běhu jedinečné** (všechny kroky zapisují do jednoho `dist/`, skript duplicitu odmítne). Velopack (T-017, `docs/velopack.md`) pojmenovává soubory podle kanálu (`releases.<RID>.json`, `RELEASES-<RID>`), ale `Setup.exe` / `Portable.zip` je potřeba při více RID přejmenovat (např. `PrusaTrayDiagnostic-<verze>-<RID>-Setup.exe`).
- **Poznámky k vydání** pro krok (např. `vpk --releaseNotes`) vrací `Get-ChangelogSection` z `scripts/ReleaseTools/ReleaseTools.psm1`; krok je zapíše do dočasného souboru a ten po sobě smaže.
- Podpis a notarizace macOS (T-060) nahradí ad-hoc podpis v balicím kroku T-019.

## Navazující úkoly

- **T-058 (kontrola aktualizací):** vydání `0.x` jsou předběžná (`--prerelease`) a `release-publish.ps1` to při zveřejnění zachová, takže je `GET /releases/latest` nevrátí. Koncepty nejsou bez přihlášení vidět vůbec.
- **T-059 (winget):** odeslání manifestu (`wingetcreate update …`) navazuje až na zveřejnění (`release-publish.ps1`). Postup sem doplní T-059.

## Povolení `win-arm64` (po T-057)

Konfigurace RID je na jednom místě: `$script:ReleaseRuntimes` v `scripts/ReleaseTools/ReleaseTools.psm1`. Po dokončení T-057 nastav u `win-arm64` `Default = $true` a `Uploadable = $true` a uprav samokontrolu (`ReleaseTools.Tests.ps1`, test „never uploads win-arm64 until T-057“). Do té doby:

```powershell
pwsh scripts/release-build.ps1 -Runtime win-x64, win-arm64   # sestaví a zabalí i win-arm64, jen pro lokální ověření
```

`release-upload.ps1` takový výstup odmítne celý (součty by obsahovaly i `win-arm64`), před nahráním je potřeba sestavit znovu bez `win-arm64`. Publish `win-arm64` potřebuje komponentu MSVC ARM64 (viz [prerequisites.md](prerequisites.md)). Smoke test `win-arm64` na x64 stroji neproběhne.

## Testy skriptů (Pester)

Rozhodovací logika skriptů má testy v Pesteru 5+ (`scripts/ReleaseTools/ReleaseTools.Tests.ps1`, instalace v [prerequisites.md](prerequisites.md)):

```powershell
Invoke-Pester scripts/ReleaseTools/ReleaseTools.Tests.ps1 -Output Detailed
```

Spouští je i `dotnet test`: projekt `tests/PrusaTrayDiagnostic.Scripts.Tests` (test `ReleaseScriptPesterTestsPass`) zavolá `pwsh` s Pesterem a selže při jakékoli chybě Pesteru. Bez `pwsh` nebo bez Pesteru 5+ se test **přeskočí** s důvodem (jako testy prohlížeče bez Edge). Dočasné soubory jdou do složky vyhrazené pro worktree, kterou test smaže i po pádu předchozího běhu.

Testy ověří bez sítě a bez GitHubu: čtení verze, tag, sekci `CHANGELOG.md` (i skutečného souboru), výběr RID (včetně odmítnutí `osx-arm64` na Windows), výběr remote pro `-Repo`, všechna odmítnutí podle stavu gitu (v dočasném repozitáři s lokálním „origin“ a „trial“: špinavý strom, chybějící tag, tag jiné verze, tag jen lokálně, tag přesunutý na remote, commit mimo `main`, nestažený `main`, nedostupný remote), značku sestavení, kontrolní součty, archivy bez ladicích symbolů, plán nahrání a kontroly před zveřejněním. Volání `dotnet` a `gh` a samotné skripty `release-*.ps1` netestují, ty se ověřují `-DryRun` a zkušebním vydáním.

## Zkušební vydání (testovací repozitář)

Celý postup s GitHubem (koncept, nahrání, opakování, zveřejnění) se ověřuje v **dočasném samostatném repozitáři**, ne v hlavním (rozhodnuto 2026-10-21). Upload a publish mají parametr `-Repo <owner/název>`: skript si najde git remote, který na tento repozitář ukazuje, a **stejný remote** použije pro kontrolu tagu a větve `main`. Kontroly i vydání se tak vždy týkají jednoho repozitáře; bez `-Repo` je to `origin`. Když žádný remote na repozitář neukazuje (nebo ukazuje víc), skript to odmítne s příkazem `git remote add`.

Postup (`<owner>` = tvůj účet, název např. `PrusaTrayDiagnostic-release-trial`):

1. Testovací repozitář (soukromý) a samostatný klon, aby se lokální tagy hlavního repozitáře nemíchaly se zkušebními:

   ```powershell
   gh repo create <owner>/PrusaTrayDiagnostic-release-trial --private
   git clone https://github.com/pfoltynek/PrusaTrayDiagnostic.git $env:TEMP\ptd-trial
   cd $env:TEMP\ptd-trial
   git switch feature/T-055-release-scripts     # nebo commit, který se má vyzkoušet
   git remote add trial https://github.com/<owner>/PrusaTrayDiagnostic-release-trial.git
   ```

2. Commit pro zkoušku: v `CHANGELOG.md` dej sekci verze datum (`## [0.1.0] - 2026-10-21`), commitni, pošli ho jako `main` testovacího repozitáře a otaguj:

   ```powershell
   git commit -am "Trial release notes"
   git push trial HEAD:refs/heads/main
   git tag -a v0.1.0 -m "Trial release 0.1.0"
   git push trial v0.1.0
   ```

3. Sestavení a koncept:

   ```powershell
   pwsh scripts/release-build.ps1
   pwsh scripts/release-upload.ps1 -Repo <owner>/PrusaTrayDiagnostic-release-trial -DryRun
   pwsh scripts/release-upload.ps1 -Repo <owner>/PrusaTrayDiagnostic-release-trial
   ```

   Kontrola: na GitHubu je koncept `v0.1.0` označený jako předběžný, s poznámkami z `CHANGELOG.md`, se zipem a `SHA256SUMS-windows.txt`.

4. Opakování: druhé spuštění `release-upload.ps1` musí soubory přeskočit („skip, identical file already uploaded“). Po novém `release-build.ps1` musí nahrání odmítnout jiný obsah a s `-Force` ho přepsat. Přidání souborů druhého OS se ověří během na Macu (T-019) stejným postupem.

5. Zveřejnění: `pwsh scripts/release-publish.ps1 -Repo <owner>/PrusaTrayDiagnostic-release-trial -DryRun` bez `-AllowIncomplete` musí odmítnout chybějící `SHA256SUMS-macos.txt`. Pak `pwsh scripts/release-publish.ps1 -Repo <owner>/PrusaTrayDiagnostic-release-trial -AllowIncomplete` a kontrola na GitHubu (zveřejněné, stále předběžné).

6. Úklid:

   ```powershell
   gh repo delete <owner>/PrusaTrayDiagnostic-release-trial --yes   # potřebuje oprávnění: gh auth refresh -s delete_repo
   cd ..; Remove-Item -Recurse -Force $env:TEMP\ptd-trial
   ```

Hlavní repozitář se při zkoušce nemění (žádný tag, žádné vydání, `origin` se jen čte při klonování).
