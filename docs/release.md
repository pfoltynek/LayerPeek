# Vydání (release)

Vydání se dělá lokálně bez CI (GitHub Actions jsou odložené, T-016). Windows soubory se sestaví na Windows, macOS soubory na Macu (Native AOT nejde kompilovat napříč OS). Oba běhy nahrávají do **stejného konceptu** (draft) vydání pro tag `vX.Y.Z` na GitHubu. Zveřejnění je samostatný poslední krok, takže nikdo nevidí vydání s polovinou souborů.

| Skript | Co dělá | Mění GitHub? |
|---|---|---|
| `scripts/release.ps1` | **jeden příkaz** pro kroky 1–3 níže: větev `release/X.Y.Z`, sloučení do `main` + tag, sloučení zpět do `develop`, sestavení tagu a nahrání do konceptu; nikdy nezveřejňuje (viz „Vydání jedním příkazem“) | ano, jen push a koncept po potvrzení (kromě `-DryRun`) |
| `scripts/release-build.ps1` | build + testy, Native AOT publish pro RID tohoto OS, AOT smoke test, archivy, na Windows instalátor Velopack (T-017), na macOS `LayerPeek.app` + `.dmg` (T-019), `SHA256SUMS-<os>.txt` | ne |
| `scripts/release-upload.ps1` | kontroly, vytvoření konceptu vydání (první běh) nebo nahrání do existujícího konceptu (druhý OS) | ano (kromě `-DryRun`) |
| `scripts/release-publish.ps1` | kontroly úplnosti konceptu a jeho zveřejnění | ano (kromě `-DryRun`) |
| `scripts/ReleaseTools/ReleaseTools.Tests.ps1` | Pester testy logiky skriptů bez sítě (spouští je i `dotnet test`, viz níže) | ne |

Všechny skripty jsou pro PowerShell 7.2 nebo novější (`#Requires -Version 7.2`) a běží na Windows i macOS. Režim `-DryRun` u uploadu a publish **čte** stav z GitHubu (`gh auth status`, `gh release view`, u publish i stažení `SHA256SUMS-*.txt` do dočasné složky), ale na GitHubu nic nemění. Prerekvizity (MSVC / Xcode Command Line Tools, `gh` CLI) jsou v [prerequisites.md](prerequisites.md).

## Rozhodnutí (2026-10-21)

- Vydávané RID: `win-x64`, `osx-arm64`. `win-arm64` se sestaví jen na výslovné vyžádání a nenahrává se, dokud není hotový T-057 (FFmpeg pro ARM64).
- Verze: jediný zdroj je `<Version>` v kořenovém `Directory.Build.props` (tvar `X.Y.Z`). Odtud ji má exe, okno O aplikaci, názvy souborů i tag `vX.Y.Z`.
- Vydává se jen z `main`: tagovaný commit musí být obsažený v `origin/main`.
- První nahrání vytvoří **koncept** (`--draft`). Verze `0.x` jsou na GitHubu označené jako předběžné (`--prerelease`).
- Poznámky k vydání jsou sekce verze z `CHANGELOG.md` (formát [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), anglicky podle `CLAUDE.md`).
- Bez placeného podpisu: Windows soubory jsou nepodepsané, macOS má jen ad-hoc podpis bez notarizace (T-019, viz „Balení pro macOS“).

## Vydání jedním příkazem (`scripts/release.ps1`, T-055)

Rozhodnutí uživatele (2026-10-24): jeden skript provede celé vydání pro Windows až po **koncept** na GitHubu. Zveřejnění zůstává vždy samostatný ruční krok. Vše se spouští lokálně ručně, bez CI.

```powershell
git switch develop
git pull --ff-only                                 # develop musí být stejný jako origin/develop
pwsh scripts/release.ps1 -Version X.Y.Z -DryRun    # kontroly a plán, nic nezmění ani nestáhne (případně požádá o git fetch origin)
pwsh scripts/release.ps1 -Version X.Y.Z            # nejdřív sám git fetch --no-tags origin, pak provedení s potvrzením před každým krokem ven
```

Kroky (odpovídají ručním krokům 1–3 v „Postup“ níže, které zůstávají jako reference):

| # | Krok | Ven? |
|---|---|---|
| 1 | `release/X.Y.Z` z `develop` s commitem `Release X.Y.Z`: `<Version>` v `Directory.Build.props` a datum vydání v `CHANGELOG.md`. Existuje-li větev už (dřívější běh, ručně), zkontroluje se (viz níže) a případně dostane commit s dnešním datem | ne |
| 2 | `merge --no-ff` do `main`, kontrola sloučených souborů (verze, datovaná sekce `CHANGELOG.md`) a anotovaný tag `vX.Y.Z` („Release X.Y.Z“), jen lokálně | ne |
| 3 | `git push --atomic origin <sloučení>:refs/heads/main refs/tags/vX.Y.Z` – **potvrzení napsáním verze** | ano |
| 4 | `merge --no-ff` zpět do `develop`, jen lokálně | ne |
| 5 | `git push --atomic origin <sloučení>:refs/heads/develop` – potvrzení `y/N` | ano |
| 6 | `git branch -d release/X.Y.Z` | ne |
| 7 | `git switch --detach vX.Y.Z` (čistý strom) a `release-build.ps1` | ne |
| 8 | `release-upload.ps1 -DryRun` (výpis souborů), potvrzení `y/N`, pak `release-upload.ps1` → koncept | ano |

Na konci (i po chybě) se skript vrátí na `develop` a vypíše adresu konceptu a přesné další příkazy:

```powershell
git switch --detach vX.Y.Z
pwsh scripts/release-publish.ps1 -DryRun
pwsh scripts/release-publish.ps1 -AllowIncomplete   # jen Windows, dokud T-019 nedodá macOS
git switch develop
```

- **macOS:** `release.ps1` dělá jen běh na Windows. Mezi krok 8 a zveřejnění se vloží běh na Macu (krok 4 v „Postup“, T-019) a zveřejní se bez `-AllowIncomplete`. Dokud skutečné nahrání z Macu do konceptu neověří uživatel (T-019), zůstává možnost zveřejnit jen Windows s `-AllowIncomplete`.
- **`CHANGELOG.md`:**
  - Existuje-li sekce `## [X.Y.Z]` (s `- unreleased`, jiným zástupným textem, jiným datem, i s pomlčkou `–`/`—` místo `-`), dostane nadpis `## [X.Y.Z] - <den vydání>`; `## [Unreleased]` musí být prázdná. Položky v obou sekcích najednou skript odmítne (spoj je ručně, rozhodnuto 2026-10-24).
  - Jinak se položky z `## [Unreleased]` přesunou do nové sekce `## [X.Y.Z] - <den vydání>` pod ní a nahoře zůstane `## [Unreleased]`. Prázdné podnadpisy šablony (např. `### Fixed` bez položek) a úvod bez položek (komentář) zůstanou pod `[Unreleased]`, přesunou se jen podsekce s položkami.
  - Odkazy dole: `[Unreleased]: …/compare/vA.B.C...HEAD` se změní na `…/compare/vX.Y.Z...HEAD` (i když odkaz `[X.Y.Z]:` už existuje) a `[X.Y.Z]: …/compare/vA.B.C...vX.Y.Z` přibude, jen když chybí.
  - Každý řádek si ponechá svůj konec řádku (CRLF/LF); nové řádky dostanou v souboru nejčastější konec řádku. Chybějící konec řádku na konci souboru zůstane chybět.
  - **Datum = den vydání** (rozhodnuto 2026-10-24): už datovaná sekce dostane den vydání a plán změnu vypíše (`'## [X.Y.Z] - <staré>' -> '## [X.Y.Z] - <den vydání>' (redated)`). Pokračuje-li se jiný den s existující větví `release/X.Y.Z` a tag ještě není, přidá skript na větev commit `Release X.Y.Z: release date <den>` (v plánu krok 1 „Re-date release/X.Y.Z to the release day“). Commit vznikne jen na tom vrcholu větve, který prošel kontrolou; když se větev mezitím pohnula, skript nic nezmění. Když tag už existuje, datum se nemění a plán to jen poznamená.
- **Existující větev `release/X.Y.Z`** (z dřívějšího běhu nebo ručně) se kontroluje stejně jako nová: `<Version>` = X.Y.Z, sekce verze v `CHANGELOG.md` s platným datem a položkami (stejná kontrola jako upload, bez výjimky pro `unreleased`) a `develop` je její předek (jinak je větev zastaralá: sluč do ní `develop`, nebo ji smaž a spusť znovu). Commity větve, které `develop` ještě nemá (stabilizační opravy), plán vypíše jako poznámku – nejsou důvodem k odmítnutí.
- **Kontrola před tagem:** po sloučení do `main` a před vytvořením tagu se znovu zkontrolují soubory sloučení (verze, datovaná sekce), tím se odhalí i hotfix na `main`, který by vydání rozbil. Při chybě nevznikne tag a `main` se neposune.
- **Soubory z gitu, ne z pracovního stromu:** obsah commitu kroku 1 se bere bajt po bajtu z commitu `develop` (`git cat-file blob`), změny v pracovním stromu se do něj nedostanou.
- **Bez zásahu do pracovního stromu:** commit kroku 1 a obě sloučení vznikají bez checkoutu (vlastní dočasný index, `git merge-tree --write-tree`, proto git 2.38+). Při chybě tak nezůstane rozpracované sloučení. `main` se posouvá jen porovnáním a záměnou (`update-ref` se starou hodnotou), `develop` jen rychle vpřed (`merge --ff-only`) a jen když je `develop` vybraný a stále na očekávaném commitu. Konflikt při sloučení skript odmítne a nic nezmění.
- **Push podle SHA:** `main`, tag a `develop` se pushují jako přesné commity potvrzené v tomto běhu (při navázání z faktů). Těsně před pushem se lokální refy porovnají s potvrzenými SHA; když se mezitím pohnuly, push se neprovede. `--atomic`: buď projdou všechny refy, nebo žádný.
- **Nikdy:** force-push, smazání nebo přesunutí tagu, `gh release edit/delete`, zveřejnění. Mimo repozitář mění jen vlastní dočasnou složku (`%TEMP%\layerpeek-release-*`), kterou smaže i po chybě.
- **Stažení (rozhodnuto 2026-10-24):** ostrý běh na začátku, ještě před kontrolami, spustí `git fetch --no-tags origin` (s `-Repo` remote toho repozitáře). Mění jen sledovací větve `origin/*`; žádná lokální větev ani tag se nevytvoří ani neposune. `develop` musí být dál stejný jako `origin/develop`, jinak skript požádá o `git pull --ff-only`.
- **`-DryRun`:** nic nezmění v gitu ani na GitHubu a nic nestahuje, jen čte (`git ls-remote`, `gh auth status`, `gh release view`) a vypíše plán. Nestažený `origin/main` / `origin/develop` ohlásí s příkazem `git fetch origin`. Změnu `CHANGELOG.md` ověří na kopii v dočasné složce.
- **`-Repo owner/název`:** jako u uploadu a publish (zkušební vydání); push `main`, tagu a `develop` pak jde na remote tohoto repozitáře a kontroly se dělají proti němu. **Jen v samostatném dočasném klonu bez `origin`** (viz „Zkušební vydání“): klon, jehož `origin` je jiný repozitář, skript odmítne, aby jeho `main` a `develop` nedostaly sloučení cizího vydání.
- **Potvrzení** čte `Read-Host`, takže mohou přijít i z roury, jeden řádek na otázku (tak to dělají Pester testy: `'X.Y.Z', 'y', 'y' | pwsh scripts/release.ps1 -Version X.Y.Z`). Neinteraktivní relace znamená „ne“.

### Co `release.ps1` zkontroluje předem

Všechny problémy vypíše najednou spolu s plánem a skončí s kódem 1:

| Kontrola | Hláška (začátek) |
|---|---|
| verze není `X.Y.Z` / není větší než všechny tagy `v*` (lokální i na `origin`) / je menší než `<Version>` na `develop` | `… is not a version in the form X.Y.Z` / `… is not greater than the latest release tag …` / `… is lower than <Version> …` |
| `HEAD` není `develop` | `Run the release from develop` |
| pracovní strom není čistý (i nesledované soubory) | `Working tree is not clean` |
| `develop` není stejný jako `origin/develop` | `Local develop has commits that are not on origin/develop` / `… is behind … git pull --ff-only` / `… have diverged` |
| `origin/main` / `origin/develop` není stažený (jen `-DryRun`, ostrý běh stahuje sám) | `origin/main (…) is not in the local repository. Run: git fetch origin` |
| lokální `main` má commity navíc (pozadu nevadí, sloučení vychází z `origin/main`) | `Local main has commits that are not on origin/main` |
| `CHANGELOG.md` nemá pro verzi žádné položky / má je ve dvou sekcích / chybí `## [Unreleased]` | `… has no entries for X.Y.Z` / `… entries both under [Unreleased] and under [X.Y.Z]` |
| existující `release/X.Y.Z`: jiná verze / sekce bez data, prázdná nebo chybí / není založená na aktuálním `develop` | `release/X.Y.Z exists, but its Directory.Build.props has version …` / `release/X.Y.Z: Section [X.Y.Z] … has no release date` / `… is not based on the current develop` |
| existující tag: sekce `CHANGELOG.md` neprojde kontrolou uploadu | `Tag vX.Y.Z: … The tag is never changed automatically` |
| na GitHubu už je vydání pro tag, který ještě neexistuje | `A GitHub release for vX.Y.Z already exists … although the tag does not` |
| `-Repo` v klonu, jehož `origin` je jiný repozitář | `This clone's origin is …, not -Repo …` |
| push URL remote ukazuje jinam než fetch URL (`pushurl`, `pushInsteadOf`) | `Remote origin fetches from … but pushes to …` |
| `main` nebo `release/X.Y.Z` je otevřená v jiném worktree | `Branch main is checked out in another worktree` |
| `gh` chybí / není přihlášený | `GitHub CLI (gh) is not installed` / `… is not logged in` |
| chybí MSVC (Windows) / Xcode CLT (macOS), nesedí verze `vpk` – jen dokud build není hotový | `The MSVC component … is not installed` |
| git starší než 2.38 – jen dokud zbývá některé sloučení | `git … is too old` |

### Opakování po chybě (navázání)

Opakované spuštění **se stejnou verzí** zjistí, co je hotové, a pokračuje prvním chybějícím krokem (v plánu `[done]` / `[todo]`). Rozhoduje podle stavu, ne podle záznamu z minulého běhu:

| Krok je hotový, když… |
|---|
| 1: existuje lokální `release/X.Y.Z`, která projde kontrolou (verze, datovaná sekce, založená na `develop`) a má datum dne vydání (jinak dostane commit s dnešním datem), nebo už tag |
| 2: existuje lokální tag `vX.Y.Z` (sloučení `release/X.Y.Z` do `main`, verze a sekce `CHANGELOG.md` sedí) a lokální `main` ho obsahuje; když tag existuje a `main` ještě ne (selhalo posunutí `main`), posune se jen `main` |
| 3: tag je na `origin` na stejném commitu a `origin/main` ho obsahuje |
| 4 / 5: lokální / `origin/develop` obsahuje commit vydání |
| 6: lokální `release/X.Y.Z` už neexistuje |
| 7: `artifacts/release/X.Y.Z/build-info.json` je z commitu tagu, z čistého stromu a soubory sedí se součty |
| 8: koncept obsahuje všechny soubory místního buildu se stejným SHA256 (nebo je vydání už zveřejněné) |

| Situace | Co dělat |
|---|---|
| potvrzení odmítnuto (nebo neinteraktivní relace) | nic z toho kroku se neprovedlo, lokální kroky před ním zůstanou. Spusť znovu, pokračuje od potvrzení |
| build selhal po pushi tagu | oprav příčinu (např. MSVC) a spusť znovu se stejnou verzí: Gitflow kroky přeskočí a začne buildem. Chyba v kódu tagu: viz „v tagovaném commitu je chyba“ níže |
| nahrání spadlo | spusť znovu: build se znovu nedělá (je aktuální), upload přeskočí stejné soubory |
| „release/X.Y.Z: Section … has no release date“ / „… exists, but its Directory.Build.props has version …“ | ručně založená nebo upravená větev neodpovídá vydání: oprav ji na větvi (commit), nebo ji smaž (`git branch -D release/X.Y.Z`) a spusť znovu – skript ji založí sám |
| „release/X.Y.Z … is not based on the current develop“ | na `develop` přibyly commity po založení větve: sluč `develop` do `release/X.Y.Z`, nebo větev smaž a spusť znovu |
| „The merge of release/X.Y.Z into main would not be releasable“ | sloučení s `main` (např. hotfix) rozbilo verzi nebo sekci `CHANGELOG.md`. Tag nevznikl, `main` se neposunul: vyřeš to ručně na větvi release a spusť znovu |
| „… is …, but … was confirmed. It moved after the confirmation“ | lokální `main`, tag nebo `develop` se mezi potvrzením a pushem změnil (jiný proces). Nic se nepushnulo: zkontroluj ref a spusť znovu |
| „Tag … on origin points to …“ / „… is not a merge commit“ | tag někdo změnil nebo vytvořil ručně. Skript tagy nikdy neposouvá ani nemaže: vyřeš ručně (ruční kroky níže) |
| „origin/main has moved since the release merge“ / push odmítnutý jako non-fast-forward | někdo mezitím pushnul do `main`. Nic se nepushuje silou (`--atomic`: ani tag): lokální sloučení zahoď (`git tag -d vX.Y.Z`, `git branch -f main origin/main`, jen dokud tag **není** na `origin`) a spusť znovu |
| „origin/develop has moved since the merge back“ | někdo mezitím pushnul do `develop` a lokálně je navíc jen sloučení zpět. Zahoď ho (`git reset --keep origin/develop` na `develop`; ztratí se jen to sloučení, další běh ho udělá znovu, `--keep` nesmaže neodevzdané změny) a spusť znovu |
| „Local develop and origin/develop have diverged after the merge back“ | lokální `develop` má kromě sloučení i další commity, které na `origin` nejsou: vyřeš ručně (rada s resetem by je zahodila) |
| „Local main … has commits after the release merge of tag …“ | lokální `main` má po tagovaném sloučení další commity; pushnulo by se jen sloučení, proto skript odmítne. Přesuň ty commity jinam (větev) a `git branch -f main vX.Y.Z`, pak spusť znovu |
| „Local develop has other commits besides the merge“ | na `develop` přibyl lokální commit po sloučení: pushni nebo odstraň ho ručně, pak spusť znovu |
| „The draft … already contains SHA256SUMS-windows.txt, but there is no matching local build“ | soubory Windows už jsou v konceptu, ale místní build chybí (jiný stroj, smazané `artifacts/`). Nový build by se lišil: zkontroluj koncept (`release-publish.ps1 -DryRun`) a dokonči ručně, případně `release-upload.ps1 -Force` |
| úplně zrušit vydání, dokud tag není na `origin` | `git switch develop`, `git tag -d vX.Y.Z`, `git branch -f main origin/main`, `git branch -D release/X.Y.Z` |

## Postup

Ruční postup (reference; `scripts/release.ps1` dělá kroky 1–3 sám).

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
pwsh scripts/release-build.ps1                 # build, testy, publish win-x64, smoke test, zip, instalátor, SHA256SUMS-windows.txt
pwsh scripts/release-upload.ps1 -DryRun         # jen kontroly a výpis, co by se nahrálo
pwsh scripts/release-upload.ps1                 # vytvoří koncept vydání a nahraje soubory
```

Smoke test spustí publikovanou aplikaci na 10 s s tvými skutečnými nastaveními (může ukázat skutečné notifikace) a odmítne běžet, když už aplikace běží. Před během ji ukonči.

### 4. Běh na Macu

Stejné příkazy na Macu (Apple Silicon), **až když běh na Windows úspěšně skončil** (souběžné běhy nejsou ošetřené). Terminál musí být ve fyzické cestě k repozitáři (`echo $PWD` = `pwd -P`, viz [prerequisites.md](prerequisites.md)).

```bash
git switch --detach vX.Y.Z
pwsh scripts/release-build.ps1                 # build, testy, publish osx-arm64, smoke test, LayerPeek.app, .dmg, .tar.gz, SHA256SUMS-macos.txt
pwsh scripts/release-upload.ps1 -DryRun         # kontroly a výpis, co by se přidalo do konceptu
pwsh scripts/release-upload.ps1                 # přidá soubory macOS do konceptu z běhu na Windows
```

Vznikne `LayerPeek-X.Y.Z-osx-arm64.dmg`, `LayerPeek-X.Y.Z-osx-arm64.tar.gz` a `SHA256SUMS-macos.txt`, které `release-upload.ps1` přidá do existujícího konceptu (postup sestavení viz „Balení pro macOS“ níže). Archiv se na macOS balí s `--no-mac-metadata` a `COPYFILE_DISABLE=1`, takže neobsahuje položky AppleDouble `._*` (ověřeno v T-019 výpisem `tar -tzf`). Po běhu na Macu se zveřejňuje bez `-AllowIncomplete`.

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
Get-FileHash .\LayerPeek-X.Y.Z-win-x64.zip -Algorithm SHA256   # porovnat s SHA256SUMS-windows.txt
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

- Archiv: `LayerPeek-<verze>-<RID>.zip` (Windows, obsah publish složky bez nadřazené složky), `.tar.gz` (macOS, podepsaná `LayerPeek.app`, zachová spustitelný bit).
- macOS (T-019): `LayerPeek-<verze>-osx-arm64.dmg` a podepsaná aplikace pro místní použití v `artifacts/release/<verze>/app/osx-arm64/bundle/LayerPeek.app` (nenahrává se).
- Instalátor Velopack (jen Windows, T-017, viz „Instalátor pro Windows“ níže): `LayerPeek-<verze>-<RID>-Setup.exe` a soubory aktualizací `LayerPeek-<verze>-<RID>-full.nupkg`, `releases.<RID>.json`, `assets.<RID>.json`, `RELEASES-<RID>`. `vpk` pracuje ve vlastní složce `velopack-<RID>` vedle `dist/` a po zkopírování ji skript smaže.
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
| druhý OS spustil `release-upload.ps1` dřív, než první doběhl | nedělej to: druhý OS spouštěj až po úspěšném konci prvního běhu. Souběžné vytvoření konceptu ze dvou strojů není ošetřené ani ověřené (viz zkušební vydání). Když se to stane, zkontroluj na GitHubu, že vznikl jen jeden koncept (případný druhý smaž), a spusť upload znovu |
| nahrání bylo přerušené (pád, Ctrl+C) | spusť `release-upload.ps1` znovu. Nedokončený soubor může na GitHubu zůstat ve stavu `starter` bez `digest`: skript ho bere jako „jiný obsah“ a bez `-Force` ho odmítne, s `-Force` ho přepíše. Chování ověřit při zkušebním vydání |
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

Krok vrátí cesty souborů, které vytvořil přímo v `dist/`. Skript je sám zapíše do `SHA256SUMS-<os>.txt` a `build-info.json`, takže je `release-upload.ps1` nahraje a `release-publish.ps1` zkontroluje bez další změny. Kroky dnes: `archive` (Windows, `.zip`), `installer` (Windows, T-017, Velopack) a `app` (macOS, T-019: `LayerPeek.app`, `.dmg`, `.tar.gz`). Logika kroku `installer` je ve funkcích `Get-VelopackPackArguments`, `Get-VelopackOutputMap`, `Copy-VelopackOutput` a `Get-VelopackVersionProblems`, kroku `app` ve funkcích `New-MacIcon`, `New-MacAppBundle`, `Invoke-MacAdHocSign`, `Get-MacSignatureProblems` a `New-MacDiskImage` modulu `ReleaseTools.psm1` (Pester testy). Build, publish a smoke test publish složky se tím nezměnily.

Poznámky pro navazující kroky:

- **Názvy souborů musí být v rámci jednoho běhu jedinečné** (všechny kroky zapisují do jednoho `dist/`, skript duplicitu odmítne). Velopack (T-017, `docs/velopack.md`) pojmenovává soubory podle kanálu (`releases.<RID>.json`, `RELEASES-<RID>`), ale `Setup.exe` / `Portable.zip` je potřeba při více RID přejmenovat (např. `LayerPeek-<verze>-<RID>-Setup.exe`).
- **Poznámky k vydání** pro krok (např. `vpk --releaseNotes`) vrací `Get-ChangelogSection` z `scripts/ReleaseTools/ReleaseTools.psm1`; krok je zapíše do dočasného souboru a ten po sobě smaže.
- Podpis a notarizace macOS (T-060) nahradí ad-hoc podpis v balicím kroku `app` (`Get-MacAdHocSignArguments`).

## Balení pro macOS (T-019)

Krok `app` v `scripts/release-build.ps1` pro každý RID macOS (dnes jen `osx-arm64`), nad publish složkou, kterou už ověřil smoke test:

1. **Ikona:** `LayerPeek.icns` (`iconutil`) z varianty ikony pro macOS (T-098): mřížka Applu – plátno 1024 px, tělo 824 × 824 px uprostřed (okraj 100 px) se spojitě zaoblenými rohy (poloměr 185,4 px) a jemným stínem, ve kterém je jen okraj kolem těla. Zdroje jsou `src/LayerPeek.App/Assets/AppIcon/app-icon-macos.svg` (detail) a `app-icon-macos-16.svg` / `app-icon-macos-32.svg` (zjednodušená varianta z T-025 na celých pixelech: tělo 12 px, resp. 26 px, bez stínu), vykreslené do `app-icon-macos-1024.png`, `app-icon-macos-16.png` a `app-icon-macos-32.png` (nejsou `AvaloniaResource`). 16 a 32 px (16 bodů @1x a @2x, 32 bodů @1x) se kopírují beze změny, od 64 px (32 bodů @2x) se zmenšují `sips` z 1024px předlohy (rozhodnuto 2026-10-29, T-019). Před `iconutil` `New-MacIcon` zkontroluje každý obrázek (`Get-MacIconImageProblems`: rozměr, tělo na mřížce ±1 px, v okraji nanejvýš stín); ikona, která mřížku nedodrží (např. dlaždice přes celé plátno jako u Windows), krok zastaví. Windows (`app-icon.ico`, exe, okna) má dál dlaždici přes celé plátno z `app-icon.svg` / `app-icon-small.svg`. Při změně SVG se PNG vygenerují znovu (postup v T-098, Poznámky k implementaci).
2. **Bundle** `LayerPeek.app`: `Contents/MacOS` = kód publish složky (exe a nativní knihovny, tedy soubory Mach-O, i knihovny FFmpeg z T-026) bez ladicích symbolů (`.pdb`, `.dSYM`), `Contents/Resources` = ostatní soubory publish složky (`FFmpeg-LICENSE.txt`; `codesign` data v `Contents/MacOS` odmítne jako nepodepsaný kód), `Contents/Info.plist` = šablona `build/macos/Info.plist` s verzí z `Directory.Build.props` (`@VERSION@`), `Contents/Resources/LayerPeek.icns`, `Contents/PkgInfo` a (T-097) `Contents/Resources/<jazyk>.lproj/InfoPlist.strings` z `build/macos/Localizations/` (přeložený text dialogu „Místní síť“ pro každý jazyk aplikace). Lokalizace se zkontrolují už před sestavením (`Get-MacLocalizationProblems`: každý jazyk z `AppLanguages.Supported`, neprázdný `NSLocalNetworkUsageDescription`, anglický shodný s `Info.plist`, `CFBundleLocalizations`) a hotový bundle před podpisem projde `plutil -lint` (`Info.plist` a každý `.strings`, `Get-MacPlistLintProblems`).
   - `CFBundleIdentifier` je v šabloně (jediné místo; `AppEnvironment.ProductionBundleIdentifier` musí být stejný, hlídá unit test): **`io.github.pfoltynek.LayerPeek`, trvalý** (rozhodnuto 2026-10-29). Podle něj macOS vede předvolby, Login Item a oprávnění notifikací (T-020), proto se už nesmí měnit.
   - `LSUIElement` = `true`: aplikace nemá ikonu v Docku ani vlastní menu aplikace, jen ikonu v menu baru. Aplikace navíc nastavuje `MacOSPlatformOptions.ShowInDock = false`, protože Avalonia při startu jinak přepne aplikaci na běžnou s ikonou v Docku.
   - `LSMinimumSystemVersion` = `14.0` (minimum AOT binárky .NET 11, `LC_BUILD_VERSION minos`).
3. **Kontrola knihoven (T-026):** v bundlu musí být knihovny FFmpeg (`Contents/MacOS/libavformat.63.dylib`, `libavcodec.63.dylib`, `libavutil.61.dylib`, `libswscale.10.dylib`) a `Contents/Resources/FFmpeg-LICENSE.txt` a žádný soubor Mach-O nesmí načítat knihovnu z `/opt/homebrew` ani `/usr/local` (`otool -L`, kromě vlastního install name; `libAvaloniaNative` ho má `/usr/local/lib/…`). Jinak krok skončí chybou (`Get-MacBundleLibraryProblems`).
4. **Ad-hoc podpis zevnitř ven** (`codesign --force --sign - --timestamp=none`): nejdřív každá vnořená Mach-O knihovna (`libAvaloniaNative`, `libSkiaSharp`, `libHarfBuzzSharp`, knihovny FFmpeg; podpis vydavatele nebo linkeru se nahradí), nakonec celý bundle (podepíše hlavní exe a zapečetí `Info.plist` a prostředky). Bez hardened runtime a bez entitlements (důležité až pro notarizaci, T-060). Hned potom `codesign --verify --deep --strict`.
5. **Smoke test bundlu:** `aot-smoke.ps1 -PublishDir LayerPeek.app/Contents/MacOS` spustí exe uvnitř bundlu (tedy s bundle identifikátorem) a ověří, že běží bez ikony v Docku (`lsappinfo`, typ `UIElement`). Bundle se nesmí změnit a podpis musí dál platit.
6. **`.tar.gz`:** `LayerPeek.app` (cesty `./LayerPeek.app/…`), bez `._*`, `.dSYM` a `.pdb`.
6. **`.dmg`:** `hdiutil create -fs HFS+ -format UDZO` ze složky s kopií aplikace (`ditto`, podpis zůstane) a odkazem `Applications` → `/Applications` (rozhodnuto 2026-10-29), název svazku `LayerPeek`, pak `hdiutil verify`. Skript nic nepřipojuje.

Velopack se pro macOS zatím nepoužívá (pokus v [velopack.md](velopack.md), bod 10), takže macOS nemá automatické aktualizace ani `.pkg`. Velopack pro macOS přijde s T-058 (rozhodnuto 2026-10-29). **Důsledek:** kdo si nainstaluje `.app` z vydání bez Velopacku, nedostane aktualizace automaticky; verzi s Velopackem si jednou nainstaluje ručně (přetáhne novou `LayerPeek.app` z `.dmg` přes starou). Patří to do textu prvního vydání pro macOS s Velopackem.

### Co uživatel uvidí na Macu

Aplikace není notarizovaná (jen ad-hoc podpis, rozhodnuto 2026-10-21), takže Gatekeeper stažený `.dmg` / `.tar.gz` nepustí bez ručního povolení:

1. Otevřít `.dmg` a přetáhnout `LayerPeek` do složky Aplikace (nebo rozbalit `.tar.gz`).
2. Spustit LayerPeek. macOS ukáže, že aplikaci nelze ověřit, a nabídne jen „Hotovo“ / „Přesunout do koše“. Zvolit „Hotovo“.
3. **Nastavení systému → Soukromí a zabezpečení**, dole u „LayerPeek byl zablokován…“ kliknout **Přesto otevřít** a potvrdit heslem nebo Touch ID. Pak znovu **Otevřít**. Další spuštění už projdou.
4. Alternativa pro pokročilé (Terminál, jen pro staženou aplikaci): `xattr -dr com.apple.quarantine /Applications/LayerPeek.app`.

Spuštění přímo z `.dmg` nebo ze složky Stažené soubory macOS přesune aplikaci do náhodné složky jen pro čtení (App Translocation). Volba „Otevřít při přihlášení“ je tam proto neaktivní s vysvětlením; aplikaci je potřeba nejdřív přesunout do Aplikací. Přesný text dialogů na macOS 15+ a chování se staženým souborem z GitHubu ověřuje uživatel (kroky v T-019).

## Instalátor pro Windows (Velopack, T-017)

Balicí krok `installer` zabalí ověřenou publish složku každého Windows RID nástrojem `vpk` (verze připnutá v `.config/dotnet-tools.json`, stejná jako NuGet `Velopack`, viz [prerequisites.md](prerequisites.md)). Podrobnosti a ověření z T-056 jsou v [velopack.md](velopack.md).

Příkaz (sestaví ho `Get-VelopackPackArguments`):

```
dotnet vpk --skip-updates pack --packId LayerPeek --packVersion <verze> --packDir <publish/RID> --mainExe LayerPeek.exe
  --packTitle LayerPeek --packAuthors "Petr Foltýnek" --aumid LayerPeek --shortcuts StartMenuRoot
  --icon src/LayerPeek.App/Assets/AppIcon/app-icon.ico --channel <RID> -r <RID>
  --releaseNotes <úsek CHANGELOG.md> --delta None --noPortable --outputDir <dočasná složka>
```

| Soubor v `dist/` | Co to je |
|---|---|
| `LayerPeek-<verze>-<RID>-Setup.exe` | instalátor pro uživatele (Velopack ho pojmenuje `LayerPeek-<RID>-Setup.exe`, skript ho přejmenuje jako archiv) |
| `LayerPeek-<verze>-<RID>-full.nupkg` | plný balíček aktualizace |
| `releases.<RID>.json`, `assets.<RID>.json`, `RELEASES-<RID>` | feed aktualizací kanálu `<RID>`; názvy se měnit **nesmí**, klient je hledá podle nich |

- **Poznámky k vydání** v balíčku jsou úsek verze z `CHANGELOG.md`. Build je vezme i z úseku bez data (`- unreleased`), nahrání (`release-upload.ps1`) datum vyžaduje dál.
- **Bez delt a bez `Portable.zip`** (rozhodnuto 2026-10-21 a 2026-10-24). Každá aktualizace stáhne celý balíček (asi 76 MB). `.zip` dělá krok `archive`.
- **`win-arm64`** zabalí stejný krok, když se RID sestavuje (`-Runtime win-x64, win-arm64`). Názvy se s `win-x64` nekryjí. Do vydání se `win-arm64` nedostane, dokud není hotový T-057: `release-upload.ps1` odmítne celý výstup a `release-publish.ps1` odmítne každý soubor s `win-arm64` v názvu, tedy i `releases.win-arm64.json` a `RELEASES-win-arm64`. Spuštění na ARM64 zatím nejde ověřit.
- **Nahrání:** soubory jdou na GitHub jako ostatní (`release-upload.ps1`, `gh`), `vpk upload` se nepoužívá. Aplikace hledá aktualizace v GitHub Releases bez tokenu (`GithubSource`, kanál = RID instalace). Koncept vydání anonymní klient nevidí, aktualizace se nabídne až po zveřejnění. Dokud je repozitář soukromý, kontrola skončí chybou 404 a aplikace ji bere jako „žádná aktualizace“.
- **Předběžné verze:** volba „Nabízet předběžné verze“ v nastavení (záložka Spuštění a aktualizace). Dokud ji uživatel nezmění, je na `0.x` zapnutá a od `1.0` vypnutá (rozhodnuto 2026-10-24). Nabídku aktualizace v aplikaci doplní T-058.

### Co uživatel uvidí

1. **Stažení v prohlížeči:** Edge může nepodepsaný `Setup.exe` s malou reputací označit jako neběžně stahovaný soubor a chtít potvrzení „Ponechat“. Neověřeno, ověřit při prvním vydání a doplnit sem.
2. **SmartScreen při spuštění** (ověřeno ručně v T-056 se značkou Mark-of-the-Web): „Systém Windows ochránil váš počítač“ jen s tlačítkem „Nespouštět“. Po „Další informace“ se ukáže „Vydavatel: Neznámý vydavatel“ a tlačítko „Přesto spustit“. Důvod: binárky ani instalátor nejsou podepsané (rozhodnuto 2026-10-21). Popis je v README a patří i do textu vydání.
3. **Instalace** bez dotazu UAC pro aktuálního uživatele do `%LOCALAPPDATA%\LayerPeek` (exe v `current\LayerPeek.exe`), zástupce jen ve Start menu (bez plochy), položka v Nastavení → Aplikace. Po instalaci se aplikace spustí (kromě tiché instalace `--silent`).
   - **Po `.zip` verzi** (logy v `%LOCALAPPDATA%\LayerPeek\logs`, rozhodnuto 2026-10-24) složka není prázdná a `Setup.exe` se nejdřív zeptá dialogem „přepsat/opravit“ (*Repair* / *Open install directory* / *Cancel*). *Cancel* nic nezmění a nic nenainstaluje. *Repair* nainstaluje a dosavadní obsah složky, tedy **logy `.zip` verze, smaže**. `--silent` odpoví *Repair* bez dotazu. Podrobnosti a ověření v [velopack.md](velopack.md#neprázdná-instalační-složka-logy-zip-verze).
4. **Spouštění po přihlášení:** volba v nastavení (záložka Spuštění a aktualizace), ve výchozím stavu vypnutá a **jen v nainstalované verzi**. Ve `.zip` je vidět neaktivní s vysvětlením (rozhodnuto 2026-10-24). Aplikace vytvoří vlastního zástupce `LayerPeek.lnk` ve složce Po spuštění (`%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup`). Jeho cíl leží v instalační složce, takže ho Velopack při odinstalaci smaže.
5. **Odinstalace** (Nastavení → Aplikace, nebo `Update.exe --uninstall --silent`) ukončí běžící aplikaci a smaže instalační složku **včetně logů** (`%LOCALAPPDATA%\LayerPeek\logs`, přijato 2026-10-24) a zástupce. Hook odinstalace smaže oznámení aplikace z centra oznámení, registraci aktivátoru toastů, nastavení oznámení aplikace ve Windows, volbu „zobrazit na hlavním panelu“ u ikony aplikace a zástupce ve Startup.
   - **Zůstane:** nastavení `%APPDATA%\LayerPeek` a hesla ve Správci pověření (rozhodnuto 2026-10-21), log Velopacku `%LOCALAPPDATA%\velopack\velopack_LayerPeek.log` a `velopack.log` (Velopack do něj píše i po hooku, **známý zbytek**, přijato 2026-10-24) a záznamy, které si Windows vede o každém spuštěném programu (`MuiCache`, `AppListBackup`, položky v `NotifyIconSettings`).

### Ověření instalátoru (E2E)

Skutečnou tichou instalaci a odinstalaci na vývojovém stroji dělá E2E test `VelopackInstallerTests` (druh zásahu `Installer`), jen přes `tools/run-e2e.ps1` s varováním a žurnálem obnovy:

```powershell
pwsh scripts/release-build.ps1                     # vytvoří artifacts/release/<verze>/dist/LayerPeek-<verze>-win-x64-Setup.exe
pwsh tools/run-e2e.ps1 -Class '*VelopackInstallerTests'
```

Test zálohuje vývojového zástupce `LayerPeek.lnk`, obsah složky `%LOCALAPPDATA%\LayerPeek` (logy, kopie v `%LOCALAPPDATA%\LayerPeek.E2E.InstallerBackup`), klíče `Uninstall\LayerPeek`, `CLSID\{EC47413C-984D-447A-866A-5F780E408DE7}` a `Notifications\Settings\LayerPeek`, zástupce ve Startup a položky ikony v `NotifyIconSettings` a po testu je vrátí (i po pádu, viz [troubleshooting.md](troubleshooting.md)). Bez složky s logy si ji test vytvoří, aby ověřil i instalaci po `.zip` verzi. Ověří, že zrušený dialog „přepsat/opravit“ nic nenainstaluje a logy nechá, a že tichá instalace logy smaže a nainstaluje bez UAC. Dále ověří AUMID a `ToastActivatorCLSID` zástupce, registraci COM `LocalServer32` a to, že po odinstalaci nic z toho nezůstane. Přeskočí se, když instalátor chybí, když je LayerPeek opravdu nainstalovaný, když nějaký LayerPeek běží (jeho log by byl otevřený) a když testy běží jako správce. Odinstalace vymaže oznámení LayerPeeku z centra oznámení a ta vrátit nejde, varování skriptu to uvádí (přijato 2026-10-24).

## Navazující úkoly

- **T-058 (kontrola aktualizací):** vydání `0.x` jsou předběžná (`--prerelease`) a `release-publish.ps1` to při zveřejnění zachová, takže je `GET /releases/latest` nevrátí. Koncepty nejsou bez přihlášení vidět vůbec.
- **T-059 (winget):** odeslání manifestu (`wingetcreate update …`) navazuje až na zveřejnění (`release-publish.ps1`). Postup sem doplní T-059.

## Povolení `win-arm64` (po T-057)

Konfigurace RID je na jednom místě: `$script:ReleaseRuntimes` v `scripts/ReleaseTools/ReleaseTools.psm1`. Po dokončení T-057 nastav u `win-arm64` `Default = $true` a `Uploadable = $true` a uprav samokontrolu (`ReleaseTools.Tests.ps1`, test „never uploads win-arm64 until T-057“). Do té doby:

```powershell
pwsh scripts/release-build.ps1 -Runtime win-x64, win-arm64   # sestaví a zabalí i win-arm64, jen pro lokální ověření
```

`release-upload.ps1` takový výstup odmítne celý (součty by obsahovaly i `win-arm64`), před nahráním je potřeba sestavit znovu bez `win-arm64`. Publish `win-arm64` potřebuje komponentu MSVC ARM64 (viz [prerequisites.md](prerequisites.md)). Smoke test `win-arm64` na x64 stroji neproběhne. Lokální ověření tohoto postupu (a instalátoru Velopack pro `win-arm64`) je úkol T-063.

## Testy skriptů (Pester)

Rozhodovací logika skriptů má testy v Pesteru 5+ (`scripts/ReleaseTools/ReleaseTools.Tests.ps1`, pro `scripts/release.ps1` `scripts/ReleaseTools/ReleaseOrchestrator.Tests.ps1`, instalace v [prerequisites.md](prerequisites.md)):

```powershell
Invoke-Pester scripts/ReleaseTools/ReleaseTools.Tests.ps1 -Output Detailed
Invoke-Pester scripts/ReleaseTools/ReleaseOrchestrator.Tests.ps1 -Output Detailed
```

Spouští je i `dotnet test`: projekt `tests/LayerPeek.Scripts.Tests` (test `ScriptPesterTestsPass`, jeden případ na soubor testů) zavolá `pwsh` s Pesterem a selže při jakékoli chybě Pesteru. Bez `pwsh` nebo bez Pesteru 5+ se test **přeskočí** s důvodem (jako testy prohlížeče bez Edge). Dočasné soubory jdou do složky vyhrazené pro worktree, kterou test smaže i po pádu předchozího běhu.

Testy ověří bez sítě a bez GitHubu: čtení verze, tag, sekci `CHANGELOG.md` (i skutečného souboru), výběr RID (včetně odmítnutí `osx-arm64` na Windows), výběr remote pro `-Repo`, všechna odmítnutí podle stavu gitu (v dočasném repozitáři s lokálním „origin“ a „trial“: špinavý strom, chybějící tag, tag jiné verze, tag jen lokálně, tag přesunutý na remote, commit mimo `main`, nestažený `main`, nedostupný remote), značku sestavení, kontrolní součty, archivy bez ladicích symbolů, plán nahrání, kontroly před zveřejněním a balicí krok Velopack (argumenty `vpk`, názvy souborů, shoda verze `vpk` s NuGet balíčkem, `win-arm64` ve feedu). Balení pro macOS (T-019): `Info.plist` ze šablony (verze, `LSUIElement`, bundle identifikátor shodný s aplikací), sestavení bundlu bez ladicích symbolů, lokalizace `InfoPlist.strings` (T-097: kopie do bundlu, kontroly, parser `.strings`, na macOS `plutil -lint` a podpis s nimi), pořadí a argumenty podpisu, argumenty `hdiutil`, typ procesu z `lsappinfo` a registrace v LaunchServices; na macOS i skutečný ad-hoc podpis s `codesign --verify --deep --strict` (a rozbitá pečeť po změně souboru), `.icns` (16 a 32 px ze zjednodušené varianty beze změny pixelů, 64 px a víc z předlohy; odmítnutí PNG špatné velikosti) a ověřený `.dmg` a `.tar.gz` bez `._*` nad zástupným bundlem (nic se nepřipojuje). Záloha smoke testu: místa bundle identifikátoru a domény `defaults` nad zástupnou složkou se zástupným `defaults`. Volání `dotnet` a `gh` a samotné skripty `release-build/upload/publish.ps1` netestují, ty se ověřují `-DryRun` a zkušebním vydáním.

Testy `release.ps1` (`ReleaseOrchestrator.Tests.ps1`) ověří bez sítě: kontrolu verze proti tagům, změnu `<Version>` a `CHANGELOG.md` (datum `- unreleased`, přesun z `[Unreleased]`, šablona prázdných podnadpisů, odkazy, smíšené konce řádků, pomlčky `–`/`—`, odmítnutí), plán a navázání pro každý mezistav (čistě z připravených faktů) a skutečné kroky Gitflow v dočasných repozitářích: bare „origin“ je dostupný přes adresu `https://github.com/test-owner/…` přepsanou `url.<base>.insteadOf` a konfigurace gitu je izolovaná. `gh` je falešný skript na `PATH`: `auth status` projde, `release view` vrátí „release not found“, nebo – s `LAYERPEEK_FAKE_GH_STATE` – koncept, který vytvořil `release create` (soubory s `digest`); každé volání zapíše a testy ověří, co se volalo. Mimo jiné: větev release ručně bez data, s jinou verzí, zastaralá, se stabilizačními commity a s dřívějším datem (nový commit s datem), kontrola sloučení před tagem, selhané posunutí `main` po tagu a navázání, push odmítnutý kvůli posunutému `origin/main` (atomicky, bez tagu), ref posunutý po potvrzení, sloučení zpět bez vybraného `develop`, `-Repo` v klonu s cizím `origin`, push URL jiného repozitáře. Skript `release.ps1` se spouští i celý: `-DryRun` nic nezmění ani nestáhne, ostrý běh stáhne (`--no-tags`), bez potvrzení se nic nepushne, selhání zástupného buildu vrátí strom na `develop` a další běh začne buildem, a úspěšný běh se zástupným buildem a skutečným `release-upload.ps1` skončí konceptem (falešný `gh`), na `develop` s čistým stromem, a další běh najde všech 8 kroků hotových. Testy-only přepínač `LAYERPEEK_RELEASE_TEST_SKIP_TOOLCHAIN=1` vypne kontrolu MSVC / Xcode / `vpk`, aby testy nezávisely na stroji. Platí jen spolu s `LAYERPEEK_PESTER_TESTS_TEMP` a jen pro repozitář uvnitř této složky (proměnné, které zůstanou v interaktivním `pwsh` po přerušeném `Invoke-Pester`, tak skutečný repozitář neovlivní); když platí, plán vypíše řádek `TEST MODE: toolchain check skipped`. Falešný `gh` odpovídá na čtení přímo ze shimu (`gh.cmd` / `gh`) a `pwsh` spouští jen pro stavový koncept, takže testy běží rychleji (≈ 1 min 20 s). xUnit má pro tento soubor časový limit 15 min (ostatní soubory 5 min). Skutečný build a nahrání na GitHub testy nedělají.

## Zkušební vydání (testovací repozitář)

Celý postup s GitHubem (koncept, nahrání, opakování, zveřejnění) se ověřuje v **dočasném samostatném repozitáři**, ne v hlavním (rozhodnuto 2026-10-21). Upload a publish mají parametr `-Repo <owner/název>`: skript si najde git remote, který na tento repozitář ukazuje, a **stejný remote** použije pro kontrolu tagu a větve `main`. Kontroly i vydání se tak vždy týkají jednoho repozitáře; bez `-Repo` je to `origin`. Když žádný remote na repozitář neukazuje (nebo ukazuje víc), skript to odmítne s příkazem `git remote add`.

Postup (`<owner>` = tvůj účet, název např. `LayerPeek-release-trial`):

1. Testovací repozitář (soukromý) a samostatný klon, aby se lokální tagy hlavního repozitáře nemíchaly se zkušebními:

   ```powershell
   gh repo create <owner>/LayerPeek-release-trial --private
   git clone https://github.com/pfoltynek/LayerPeek.git $env:TEMP\layerpeek-trial
   cd $env:TEMP\layerpeek-trial
   git switch feature/T-055-release-scripts     # nebo commit, který se má vyzkoušet
   git remote add trial https://github.com/<owner>/LayerPeek-release-trial.git
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
   pwsh scripts/release-upload.ps1 -Repo <owner>/LayerPeek-release-trial -DryRun
   pwsh scripts/release-upload.ps1 -Repo <owner>/LayerPeek-release-trial
   ```

   Kontrola: na GitHubu je koncept `v0.1.0` označený jako předběžný, s poznámkami z `CHANGELOG.md`, se zipem a `SHA256SUMS-windows.txt`.

4. Opakování: druhé spuštění `release-upload.ps1` musí soubory přeskočit („skip, identical file already uploaded“). Po novém `release-build.ps1` musí nahrání odmítnout jiný obsah a s `-Force` ho přepsat. Přidání souborů druhého OS se ověří během na Macu (T-019) stejným postupem.

   Ověřit také hraniční případy, které zatím ověřené nejsou:
   - **přerušené nahrání:** během uploadu ukonči skript (Ctrl+C). Zjisti, v jakém stavu soubor na GitHubu zůstal (`gh release view v0.1.0 --repo … --json assets` – `state`, `digest`), že ho další běh bez `-Force` odmítne a s `-Force` přepíše, a že `release-publish.ps1` nedokončený soubor nezveřejní;
   - **souběžné vytvoření:** dva běhy `release-upload.ps1` spuštěné současně (např. dva klony s různým RID, nebo Windows a Mac) – vznikne jeden koncept, nebo dva? Výsledek zapsat sem a do T-055.

5. Zveřejnění: `pwsh scripts/release-publish.ps1 -Repo <owner>/LayerPeek-release-trial -DryRun` bez `-AllowIncomplete` musí odmítnout chybějící `SHA256SUMS-macos.txt`. Pak `pwsh scripts/release-publish.ps1 -Repo <owner>/LayerPeek-release-trial -AllowIncomplete` a kontrola na GitHubu (zveřejněné, stále předběžné).

   **Zkouška `release.ps1`** (místo ručních kroků 2–3, v novém testovacím repozitáři nebo po úklidu tagu a vydání z předchozí zkoušky). Testovací repozitář potřebuje `main` a `develop`, klon musí být na `develop` s `release.ps1` a **nesmí mít `origin`** (jinak ho `release.ps1 -Repo` odmítne, aby se sloučení zkoušky nedostala k `main` a `develop` hlavního repozitáře):

   ```powershell
   git switch develop
   git remote remove origin                       # klon zkoušky zná jen remote trial
   git push trial main develop
   git fetch trial
   pwsh scripts/release.ps1 -Version 0.1.0 -Repo <owner>/LayerPeek-release-trial -DryRun
   pwsh scripts/release.ps1 -Version 0.1.0 -Repo <owner>/LayerPeek-release-trial
   ```

   Kontrola: v `trial` je `main` se sloučením a tagem `v0.1.0`, `develop` se sloučením zpět, koncept jako v kroku 3. Navázání: během buildu přeruš skript (Ctrl+C) a spusť ho znovu – musí ukázat kroky 1–6 jako hotové a začít buildem; po úspěšném konci musí další běh skončit bez práce (vše `[done]`). Zveřejnění pak podle kroku 5 (`release.ps1` nezveřejňuje).

6. Úklid:

   ```powershell
   gh repo delete <owner>/LayerPeek-release-trial --yes   # potřebuje oprávnění: gh auth refresh -s delete_repo
   cd ..; Remove-Item -Recurse -Force $env:TEMP\layerpeek-trial
   ```

Hlavní repozitář se při zkoušce nemění (žádný tag, žádné vydání, `origin` se jen čte při klonování).
