# Vydání (release)

Vydání se dělá lokálně bez CI (GitHub Actions se nepoužívají, T-016 zamítnut 2026-11-01). Windows soubory se sestaví na Windows, macOS soubory na Macu (Native AOT nejde kompilovat napříč OS). Oba běhy nahrávají do **stejného konceptu** (draft) vydání pro tag `vX.Y.Z` na GitHubu. Zveřejnění je poslední krok, až má koncept soubory obou OS, takže nikdo nevidí vydání s polovinou souborů. Od T-200 se na obou počítačích spouští stejný příkaz `release.ps1` v libovolném pořadí a zveřejní ten běh, který koncept doplní (viz „Vydání ze dvou počítačů“).

> **Od 1.0.0 jsou zapnutá neměnná vydání** (immutable releases, T-180; rozhodnutí uživatele 2026-11-11, T-198 otázka 4):
> soubory zveřejněného vydání už nejde změnit ani doplnit. Vydání se proto zveřejňuje **jen se soubory obou OS v
> konceptu**. Přepínače `-AllowIncomplete` a `-AddToPublished` (tok T-113) skripty od T-200 vždy odmítnou s kódem 1
> a s postupem „Vydání ze dvou počítačů“.

| Skript | Co dělá | Mění GitHub? |
|---|---|---|
| `scripts/release.ps1` | **jeden příkaz na obou počítačích** (T-200): větev `release/X.Y.Z`, sloučení do `main` + tag, koncept hned po pushi tagu, sloučení zpět do `develop`, sestavení tagu, nahrání do konceptu a – když má koncept soubory obou OS – zveřejnění; na druhém počítači jen tag, build, nahrání a zveřejnění (viz „Vydání jedním příkazem“ a „Vydání ze dvou počítačů“) | ano, push, koncept, nahrání a zveřejnění po potvrzení (kromě `-DryRun`) |
| `scripts/release-build.ps1` | build + testy, Native AOT publish pro RID tohoto OS, AOT smoke test, archivy, na Windows instalátor Velopack (T-017), na macOS `LayerPeek.app` od `vpk` + `.dmg` + `.tar.gz` + balíček a feed Velopacku (T-019, T-058), podpis feedu offline klíčem (T-181, viz „Podpis feedu aktualizací“), `SHA256SUMS-<os>.txt` | ne |
| `scripts/release-upload.ps1` | kontroly a nahrání do konceptu (bez konceptu ho vytvoří); spouští ho `release.ps1`; `-AddToPublished` od T-200 jen odmítne | ano (kromě `-DryRun`) |
| `scripts/release-publish.ps1` | kontroly úplnosti konceptu a jeho zveřejnění; potom nabídne odeslání do winget (T-059); spouští ho `release.ps1` po napsání verze; `-AllowIncomplete` od T-200 jen odmítne | ano (kromě `-DryRun`) |
| `scripts/release-winget.ps1` | odeslání zveřejněného vydání do winget (`microsoft/winget-pkgs`), jen Windows, s potvrzením; s `-OutputDirectory` jen zapíše manifest (viz „Publikace do winget“) | ano, PR do `microsoft/winget-pkgs` po potvrzení (kromě `-DryRun` a `-OutputDirectory`) |
| `scripts/ReleaseTools/ReleaseTools.Tests.ps1` | Pester testy logiky skriptů bez sítě (spouští je i `dotnet test`, viz níže; winget v `ReleaseWinget.Tests.ps1`) | ne |

Všechny skripty jsou pro PowerShell 7.2 nebo novější (`#Requires -Version 7.2`) a běží na Windows i macOS. Režim `-DryRun` u uploadu a publish **čte** stav z GitHubu (`gh auth status`, `gh release view`, u publish i stažení `SHA256SUMS-*.txt` do dočasné složky), ale na GitHubu nic nemění. Prerekvizity (MSVC / Xcode Command Line Tools, `gh` CLI) jsou v [prerequisites.md](prerequisites.md).

## Rozhodnutí (2026-10-21)

- Vydávané RID: `win-x64`, `win-arm64`, `osx-arm64`. Windows ARM64 (`win-arm64`) se vydává od rozhodnutí uživatele 2026-11-12 (T-224 až T-226, ruší T-130): na Windows x64 se sestaví ve výchozím buildu vedle `win-x64` a **nahrává se s ním** od prvního vydání po 1.0.0 (rozhodnutí uživatele 2026-11-16: plná podpora, ne experimentální, i bez předchozího běhu na zařízení s ARM64; ověření na zařízení dělá T-227 dodatečně; 1.0.0 vyšla z existujícího tagu jen s macOS a `win-x64`). Build pro nahrání je proto na obou OS výchozí build bez `-Runtime`, viz „Konfigurace RID“.
- Verze: jediný zdroj je `<Version>` v kořenovém `Directory.Build.props` (tvar `X.Y.Z`). Odtud ji má exe, okno O aplikaci, názvy souborů i tag `vX.Y.Z`.
- Vydává se jen z `main`: tagovaný commit musí být obsažený v `origin/main`.
- První nahrání vytvoří **koncept** (`--draft`). Verze `0.x` jsou na GitHubu označené jako předběžné (`--prerelease`), od 1.0.0 vydání předběžná nejsou (`Test-PrereleaseVersion`: hlavní verze ≠ 0).
- Poznámky k vydání jsou sekce verze z `CHANGELOG.md` (formát [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), anglicky podle `CLAUDE.md`).
- Bez placeného podpisu: Windows soubory jsou nepodepsané, macOS má jen ad-hoc podpis bez notarizace (T-019, viz „Balení pro macOS“).
- Feed aktualizací `releases.<RID>.json` je od T-181 podepsaný offline klíčem (ECDSA P-256) a aplikace aktualizaci bez platného podpisu odmítne (rozhodnuto 2026-11-06, viz „Podpis feedu aktualizací“).
- Neměnná vydání (T-180) se zapnou před 1.0.0 a od 1.0.0 se zveřejňuje jen se soubory obou OS v konceptu (rozhodnutí uživatele 2026-11-11, T-198 otázka 4; na repozitáři zapnuta 2026-11-22). Od T-200 (2026-11-22) skripty `-AddToPublished` a `-AllowIncomplete` odmítnou; `release.ps1` se spouští na obou počítačích, koncept vzniká hned po pushi tagu a zveřejnění nabídne běh, který koncept doplní (potvrzení napsáním verze).
- Po zveřejnění 1.0.0 se skutečné stažení a ověření podepsaného feedu z GitHubu zkontroluje místním Release buildem s verzí 0.9.9 na obou OS (rozhodnutí uživatele 2026-11-11, T-198 otázka 3; viz „Kontrola aktualizace z GitHubu buildem 0.9.9“). Zkušební vydání v testovacím repozitáři před 1.0.0 se nedělá.
- winget (T-059, rozhodnuto 2026-11-11): balíček `3DPrint.LayerPeek`, vydavatel `Petr Foltýnek`, jen stabilní verze od 1.0.0, odeslání jako volitelný krok po zveřejnění (viz „Publikace do winget“).

## Vydání jedním příkazem (`scripts/release.ps1`, T-055)

Rozhodnutí uživatele (2026-10-24): jeden skript provede celé vydání. Od T-200 (rozhodnutí uživatele 2026-11-22) ho spouští **oba počítače** stejným příkazem, v libovolném pořadí; na druhém počítači přeskočí Gitflow hotový na `origin` a po nahrání druhého OS nabídne zveřejnění (viz „Vydání ze dvou počítačů“). Vše se spouští lokálně ručně, bez CI.

```powershell
git switch develop
git pull --ff-only                                 # develop musí být stejný jako origin/develop
pwsh scripts/release.ps1 -Version X.Y.Z -DryRun    # kontroly a plán, nic nezmění ani nestáhne (případně požádá o git fetch origin)
pwsh scripts/release.ps1 -Version X.Y.Z            # nejdřív sám git fetch --no-tags origin (a tag vX.Y.Z, když je jen na origin), pak provedení s potvrzením před každým krokem ven
```

Kroky (odpovídají ručním krokům v „Postup“ níže, které zůstávají jako reference):

| # | Krok | Ven? |
|---|---|---|
| 1 | `release/X.Y.Z` z `develop` s commitem `Release X.Y.Z`: `<Version>` v `Directory.Build.props` a datum vydání v `CHANGELOG.md`. Existuje-li větev už (dřívější běh, ručně), zkontroluje se (viz níže) a případně dostane commit s dnešním datem | ne |
| 2 | `merge --no-ff` do `main`, kontrola sloučených souborů (verze, datovaná sekce `CHANGELOG.md`) a anotovaný tag `vX.Y.Z` („Release X.Y.Z“), jen lokálně | ne |
| 3 | `git push --atomic origin <sloučení>:refs/heads/main refs/tags/vX.Y.Z` – **potvrzení napsáním verze** (otázka zmíní i koncept) | ano |
| 4 | prázdný **koncept** vydání s textem ze sekce `CHANGELOG.md` tagu (`gh release create --draft --verify-tag`), hned po pushi tagu, aby ho druhý počítač našel (T-200); bez kroku 3 v témže běhu potvrzení `y/N` | ano |
| 5 | `merge --no-ff` zpět do `develop`, jen lokálně | ne |
| 6 | `git push --atomic origin <sloučení>:refs/heads/develop` – potvrzení `y/N` | ano |
| 7 | `git branch -d release/X.Y.Z` | ne |
| 8 | `git switch --detach vX.Y.Z` (čistý strom) a `release-build.ps1` | ne |
| 9 | `release-upload.ps1 -DryRun` (výpis souborů), potvrzení `y/N`, pak `release-upload.ps1` → soubory tohoto OS v konceptu | ano |
| 10 | jen když má koncept soubory obou OS: `release-publish.ps1 -DryRun`, **potvrzení napsáním verze**, `release-publish.ps1` (neměnné vydání; na Windows pak nabídka winget). Jinak `[wait]` a blok `NEXT STEP` pro druhý počítač | ano |

Na druhém počítači jsou kroky 1–7 hotové na `origin` (viz „Vydání ze dvou počítačů“). Na konci (i po chybě) se skript vrátí na `develop`, vypíše stav souborů na GitHubu a blok `NEXT STEP` s přesnými příkazy a počítačem (příklad prvního běhu na Windows):

```text
GitHub vX.Y.Z (<URL of the draft>): macOS files missing, Windows files uploaded.
Draft release vX.Y.Z has the files of this machine; it waits for the macOS files.

NEXT STEP: on the macOS machine (the macOS files are missing in the draft vX.Y.Z), the same command:
  git switch develop
  git pull --ff-only
  $env:LAYERPEEK_FEED_SIGNING_KEY = '<key file, e.g. /Volumes/LP-KEY/layerpeek-feed-key.pem>'   # keep the key medium connected until the build ends; password asked twice
  caffeinate -d -i -u pwsh scripts/release.ps1 -Version X.Y.Z
  Remove-Item Env:LAYERPEEK_FEED_SIGNING_KEY
It skips the Gitflow steps (done on GitHub), builds vX.Y.Z, uploads the macOS files into this draft and then offers to publish.
Do not publish before: published releases are immutable (T-180), the macOS files could never be added (docs/release.md, release on two machines).
```

- **Druhý OS:** stejný příkaz na druhém počítači (viz „Vydání ze dvou počítačů“). Zveřejněné vydání bez souborů tohoto OS plán odmítne (`Release vX.Y.Z is already published without the <OS> files …`): soubory nejde doplnit, verze pro ten OS propadá.
- **`CHANGELOG.md`:**
  - Existuje-li sekce `## [X.Y.Z]` (s `- unreleased`, jiným zástupným textem, jiným datem, i s pomlčkou `–`/`—` místo `-`), dostane nadpis `## [X.Y.Z] - <den vydání>`; `## [Unreleased]` musí být prázdná. Položky v obou sekcích najednou skript odmítne (spoj je ručně, rozhodnuto 2026-10-24).
  - Jinak se položky z `## [Unreleased]` přesunou do nové sekce `## [X.Y.Z] - <den vydání>` pod ní a nahoře zůstane `## [Unreleased]`. Prázdné podnadpisy šablony (např. `### Fixed` bez položek) a úvod bez položek (jen komentář) zůstanou pod `[Unreleased]`, přesunou se jen podsekce s položkami. **Úvodní odstavec s textem** před prvním `###` se počítá jako položka a přesune se s nimi: stane se úvodem textu vydání. Tak je připravený úvod textu vydání 1.0.0 (T-198 otázka 7, ověřeno `ConvertTo-ReleaseChangelog` nad skutečným `CHANGELOG.md` 2026-11-11). Sekce `## [X.Y.Z] - unreleased` předem proto není potřeba.
  - Odkazy dole: `[Unreleased]: …/compare/vA.B.C...HEAD` se změní na `…/compare/vX.Y.Z...HEAD` (i když odkaz `[X.Y.Z]:` už existuje) a `[X.Y.Z]: …/compare/vA.B.C...vX.Y.Z` přibude, jen když chybí.
  - Každý řádek si ponechá svůj konec řádku (CRLF/LF); nové řádky dostanou v souboru nejčastější konec řádku. Chybějící konec řádku na konci souboru zůstane chybět.
  - **Datum = den vydání** (rozhodnuto 2026-10-24): už datovaná sekce dostane den vydání a plán změnu vypíše (`'## [X.Y.Z] - <staré>' -> '## [X.Y.Z] - <den vydání>' (redated)`). Pokračuje-li se jiný den s existující větví `release/X.Y.Z` a tag ještě není, přidá skript na větev commit `Release X.Y.Z: release date <den>` (v plánu krok 1 „Re-date release/X.Y.Z to the release day“). Commit vznikne jen na tom vrcholu větve, který prošel kontrolou; když se větev mezitím pohnula, skript nic nezmění. Když tag už existuje, datum se nemění a plán to jen poznamená.
- **Existující větev `release/X.Y.Z`** (z dřívějšího běhu nebo ručně) se kontroluje stejně jako nová: `<Version>` = X.Y.Z, sekce verze v `CHANGELOG.md` s platným datem a položkami (stejná kontrola jako upload, bez výjimky pro `unreleased`) a `develop` je její předek (jinak je větev zastaralá: sluč do ní `develop`, nebo ji smaž a spusť znovu). Commity větve, které `develop` ještě nemá (stabilizační opravy), plán vypíše jako poznámku – nejsou důvodem k odmítnutí.
- **Kontrola před tagem:** po sloučení do `main` a před vytvořením tagu se znovu zkontrolují soubory sloučení (verze, datovaná sekce), tím se odhalí i hotfix na `main`, který by vydání rozbil. Při chybě nevznikne tag a `main` se neposune.
- **Soubory z gitu, ne z pracovního stromu:** obsah commitu kroku 1 se bere bajt po bajtu z commitu `develop` (`git cat-file blob`), změny v pracovním stromu se do něj nedostanou.
- **Bez zásahu do pracovního stromu:** commit kroku 1 a obě sloučení vznikají bez checkoutu (vlastní dočasný index, `git merge-tree --write-tree`, proto git 2.38+). Při chybě tak nezůstane rozpracované sloučení. `main` se posouvá jen porovnáním a záměnou (`update-ref` se starou hodnotou), `develop` jen rychle vpřed (`merge --ff-only`) a jen když je `develop` vybraný a stále na očekávaném commitu. Konflikt při sloučení skript odmítne a nic nezmění.
- **Push podle SHA:** `main`, tag a `develop` se pushují jako přesné commity potvrzené v tomto běhu (při navázání z faktů). Těsně před pushem se lokální refy porovnají s potvrzenými SHA; když se mezitím pohnuly, push se neprovede. `--atomic`: buď projdou všechny refy, nebo žádný.
- **Nikdy:** force-push, smazání nebo přesunutí tagu, `gh release delete`, zveřejnění bez napsání verze a bez souborů všech OS (zveřejňuje jen `release-publish.ps1`). Mimo repozitář mění jen vlastní dočasnou složku (`%TEMP%\layerpeek-release-*`), kterou smaže i po chybě.
- **Stažení (rozhodnuto 2026-10-24, tag od T-200):** ostrý běh na začátku, ještě před kontrolami, spustí `git fetch --no-tags origin` (s `-Repo` remote toho repozitáře). Mění jen sledovací větve `origin/*`; žádná lokální větev se nevytvoří ani neposune. Je-li tag `vX.Y.Z` na `origin` a lokálně chybí (druhý počítač), stáhne **jen tento tag** (`git fetch --no-tags origin refs/tags/vX.Y.Z:refs/tags/vX.Y.Z`, bez `+`: existující lokální tag se nikdy nepřepíše, jiný ohlásí plán). Na prvním počítači musí být `develop` stejný jako `origin/develop`, jinak skript požádá o `git pull --ff-only`.
- **`-DryRun`:** nic nezmění v gitu ani na GitHubu a nic nestahuje, jen čte (`git ls-remote`, `gh auth status`, `gh release view`) a vypíše plán. Nestažený `origin/main` / `origin/develop` ohlásí s příkazem `git fetch origin`. Změnu `CHANGELOG.md` ověří na kopii v dočasné složce.
- **`-Repo owner/název`:** jako u uploadu a publish (zkušební vydání); push `main`, tagu a `develop` pak jde na remote tohoto repozitáře a kontroly se dělají proti němu. **Jen v samostatném dočasném klonu bez `origin`** (viz „Zkušební vydání“): klon, jehož `origin` je jiný repozitář, skript odmítne, aby jeho `main` a `develop` nedostaly sloučení cizího vydání.
- **Podpis feedu (T-181):** krok 7 spouští `release-build.ps1` bez parametrů, klíč proto bere z proměnné prostředí. Před spuštěním připoj médium s klíčem a nastav `$env:LAYERPEEK_FEED_SIGNING_KEY = '<cesta ke klíči>'` ve stejném terminálu; build se zeptá na heslo dvakrát (před buildem a při podpisu na konci) a médium musí zůstat připojené až do konce buildu (viz „Podpis feedu aktualizací“). Že stačí proměnná prostředí a `release.ps1` vlastní parametr `-FeedSigningKey` nemá, rozhodl uživatel 2026-11-11 (otázka 20 v T-181). Bez proměnné krok 7 skončí chybou a další běh `release.ps1` začne znovu buildem. Když selže až podpis na konci buildu (médium odpojené a `abort`, 3× špatné heslo), nespouštěj celý build znovu: v čistém checkoutu tagu `git switch --detach vX.Y.Z`, `pwsh scripts/release-build.ps1 -SignOnly`, pak `git switch develop` a znovu `release.ps1 -Version X.Y.Z`; krok 7 už najde hotový podepsaný build a pokračuje nahráním (viz „Jen podepsat hotový `dist/`“, T-197).
- **Mac usíná** (po 10 minutách nečinnosti) a build trvá asi 20 minut: na Macu spouštěj `release.ps1` i `release-build.ps1` pod `caffeinate -d -i -u` (např. `caffeinate -d -i -u pwsh scripts/release.ps1 -Version X.Y.Z`), při práci přes SSH navíc v `tmux`. Proměnné prostředí (`LAYERPEEK_FEED_SIGNING_KEY`, `GH_TOKEN`) i výzvy na heslo a potvrzení projdou, `caffeinate` jen spustí příkaz ve stejném terminálu. Samotné `caffeinate -i` nestačí: drží vzhůru systém, ne displej, a při spícím displeji aplikace ve smoke testu čeká na displej (T-096), takže `aot-smoke.ps1` selže s `LaunchServices type '', expected 'UIElement'` (ověřeno generálkou B4 2026-11-15). `-d -u` drží displej vzhůru.
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
| víc vydání pro tag (dva koncepty, T-200) | `GitHub has N releases for vX.Y.Z …` |
| vydání je zveřejněné bez souborů tohoto OS (T-200) | `Release vX.Y.Z is already published without the <OS> files …` |
| tag je jen na `origin` (jen `-DryRun` na druhém počítači; ostrý běh ho stáhne) | `Tag vX.Y.Z is on origin but not in the local repository (the other machine has started this release) …` |
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
| 2: existuje lokální tag `vX.Y.Z` (sloučení `release/X.Y.Z` do `main`, verze a sekce `CHANGELOG.md` sedí) a lokální `main` ho obsahuje, nebo je hotový krok 3; když tag existuje a `main` ještě ne (selhalo posunutí `main`), posune se jen `main` |
| 3: tag je na `origin` na stejném commitu a `origin/main` ho obsahuje |
| 4: na GitHubu je vydání pro tag (koncept nebo zveřejněné) |
| 5 / 6: lokální nebo `origin/develop` / `origin/develop` obsahuje commit vydání |
| 7: lokální `release/X.Y.Z` už neexistuje |
| 8: `artifacts/release/X.Y.Z/build-info.json` je z commitu tagu, z čistého stromu, s podepsaným feedem (ne `-SkipFeedSignature` ani nedokončený podpis `pending`, T-181, T-197) a soubory sedí se součty |
| 9: koncept obsahuje všechny soubory místního buildu se stejným SHA256 (nebo je vydání už zveřejněné) |
| 10: vydání je zveřejněné |

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
| poznámka „The draft … already has the files of this OS …, but this machine has no current build“ | soubory tohoto OS už jsou v konceptu (jiný stroj se stejným OS, smazané `artifacts/`). Build ani nahrání se neopakují (nový build by se lišil), běh pokračuje zveřejněním; `release-publish.ps1` soubory zkontroluje proti součtům |
| „The other machine has pushed main and vX.Y.Z, but not develop yet …“ | druhý počítač začal, než první pushnul `develop` (první čeká na „Push develop“ nebo skončil před ním). Dokonči běh na prvním počítači a pak spusť znovu tady; druhý počítač `develop` nikdy neslučuje sám (lokální `develop` prvního by se rozešel s `origin`) |
| „Tag vX.Y.Z on origin points to …“ po souběžném startu obou počítačů | druhý počítač vyhrál. Dokud lokální tag není na `origin`: `git tag -d vX.Y.Z`, `git branch -f main origin/main`, `git branch -D release/X.Y.Z` a spusť znovu – pokračuje jako druhý počítač |
| úplně zrušit vydání, dokud tag není na `origin` | `git switch develop`, `git tag -d vX.Y.Z`, `git branch -f main origin/main`, `git branch -D release/X.Y.Z` |

## Vydání ze dvou počítačů (Windows a Mac, T-200)

Rozhodnutí uživatele 2026-11-22 (T-200): na **obou počítačích se spouští stejný příkaz**
`pwsh scripts/release.ps1 -Version X.Y.Z`, v libovolném pořadí. Prvním počítačem může být jednou Windows, jindy Mac.
Co je hotové, skript pozná ze stavu (tag na `origin`, soubory v konceptu), ne ze záznamu minulého běhu, a udělá jen
zbytek. Neměnná vydání (T-180, od 1.0.0) nedovolí soubory zveřejněného vydání změnit ani doplnit, proto se zveřejňuje
jen koncept se soubory obou OS. Tok T-113 (jeden OS zveřejní s `-AllowIncomplete`, druhý doplní `-AddToPublished`)
skončil. Oba přepínače zůstaly jen kvůli vysvětlení: skripty je vždy odmítnou s kódem 1 a tímto postupem, i pro `-Repo`,
a GitHubu se přitom na nic neptají (rozhodnutí uživatele 2026-11-12, T-200 otázky 1–3).

| | První počítač | Druhý počítač |
|---|---|---|
| Gitflow (větev, `main` + tag, `develop`) | provede, s potvrzením | přeskočí: je hotový na `origin`, lokální `main` a `develop` se nemění |
| tag `vX.Y.Z` | vytvoří a pushne | ostrý běh stáhne jen tento tag (`git fetch --no-tags origin refs/tags/vX.Y.Z:refs/tags/vX.Y.Z`, bez `+`, takže existující lokální tag nepřepíše) |
| koncept | vytvoří **hned po pushi tagu** (prázdný, text ze sekce `CHANGELOG.md` tagu) a vypíše, že druhý počítač může začít | najde ho |
| build a nahrání | soubory svého OS | soubory svého OS |
| zveřejnění | `[wait]`: čeká na soubory druhého OS, běh skončí blokem `NEXT STEP` pro druhý počítač | koncept je úplný: `release-publish.ps1 -DryRun`, potvrzení **napsáním verze**, `release-publish.ps1` (na Windows pak nabídka winget) |

Zveřejňuje ten počítač, který nahraje poslední. Když uživatel zveřejnění odmítne, koncept zůstane úplný a soukromý.
Zveřejnit ho jde později na **kterémkoli** počítači stejným příkazem: ostatní kroky najde hotové. Druhý počítač smí
začít hned, jak první vypíše `Tag vX.Y.Z, develop and the draft are on GitHub` (po pushi `develop`), tedy i souběžně
s buildem prvního. Kdyby začal dřív, odmítne pokračovat, dokud první `develop` nepushne. Když
nahraje dřív, čeká se na soubory prvního počítače a zveřejní ten.

### Postup na každém počítači

Windows (PowerShell):

```powershell
git switch develop
git pull --ff-only
pwsh scripts/release.ps1 -Version X.Y.Z -DryRun                 # plán, stav na GitHubu a NEXT STEP; nic nemění ani nestahuje
$env:LAYERPEEK_FEED_SIGNING_KEY = 'E:\layerpeek-feed-key.pem'    # médium připojené až do konce buildu
pwsh scripts/release.ps1 -Version X.Y.Z                         # potvrzení, heslo ke klíči 2×
Remove-Item Env:LAYERPEEK_FEED_SIGNING_KEY
```

Mac (`pwsh` v Terminálu, ve fyzické cestě k repozitáři, přes SSH v `tmux`):

```powershell
git switch develop
git pull --ff-only
pwsh scripts/release.ps1 -Version X.Y.Z -DryRun
$env:LAYERPEEK_FEED_SIGNING_KEY = '/Volumes/LP-KEY/layerpeek-feed-key.pem'
caffeinate -d -i -u pwsh scripts/release.ps1 -Version X.Y.Z    # Mac by během asi 20 minut buildu usnul
Remove-Item Env:LAYERPEEK_FEED_SIGNING_KEY
```

`-DryRun` na druhém počítači před stažením tagu skončí hláškou `Tag vX.Y.Z is on origin but not in the local
repository (the other machine has started this release)`. To je v pořádku: ostrý běh tag stáhne sám (pro další
`-DryRun` stačí vypsaný příkaz `git fetch --no-tags origin refs/tags/vX.Y.Z:refs/tags/vX.Y.Z`).

### Co běh vypíše na konci (návod pro člověka i agenta)

Plán ukazuje u každého kroku `[done]`, `[todo]` nebo `[wait]` a pod ním stav na GitHubu, např.
`GitHub: draft vX.Y.Z: macOS files uploaded, Windows files missing.`. OS se počítá jako nahraný až se svým
`SHA256SUMS-<os>.txt`, který se nahrává poslední. Každý běh (i po chybě a odmítnutí) končí blokem s přesným dalším
krokem a počítačem, kde ho spustit:

| Konec běhu | Kód | Poslední blok |
|---|---|---|
| koncept čeká na druhý OS | 0 | `NEXT STEP: on the <OS> machine (the <OS> files are missing in the draft vX.Y.Z), the same command:` + `git switch develop`, `git pull --ff-only`, nastavení klíče, příkaz (na Macu s `caffeinate`) a `Do not publish before: published releases are immutable …` |
| zveřejnění odmítnuto | 1 | `NEXT STEP: the draft vX.Y.Z has the files of every OS but is not published. To publish, on either machine:` |
| odmítnuté potvrzení jiného kroku | 1 | `Stopped before: <krok> (not confirmed) …` a `NEXT STEP: re-run on this machine: …` |
| chyba | 1 | `Release FAILED: …` a `NEXT STEP: fix the cause and re-run on this machine: …` |
| problém v plánu (kontroly předem) | 1 | výpis problémů a `NEXT STEP: fix the problems above as they say, then re-run on this machine: …` |
| zveřejněno, nebo už zveřejněné se soubory všech OS | 0 | `Release vX.Y.Z is published with the files of every OS. Nothing left to do for this version.` |
| `-DryRun` | 0 / 1 | `NEXT STEP: the real run on this machine does the steps marked [todo] …`, nebo blok pro druhý počítač, když tu nic nezbývá |

Agent může spustit `-DryRun` a podle bloku `NEXT STEP` říct, co a na kterém počítači spustit. Heslo ke klíči feedu čte
skript z terminálu (T-181) a zadává ho vždy člověk. Potvrzení kroků ven (napsání verze, `y/N`) jsou otázky na uživatele.

### Když se něco pokazí

| Situace | Co dělat |
|---|---|
| `GitHub has 2 releases for vX.Y.Z …` | oba počítače vytvořily koncept ve stejném okamžiku (koncept vzniká hned po pushi tagu, okno je pár sekund). Skripty pak nic nenahrají ani nezveřejní. Na GitHubu (*Releases*) nech koncept se soubory, druhý smaž (*Edit* → *Delete*; `gh release delete` je podle tagu nerozliší) a spusť znovu |
| `Release vX.Y.Z is already published without the <OS> files …` | vydání je zveřejněné bez tohoto OS (např. 1.0.0 bez `win-arm64`, nebo zveřejněné ručně). Soubory už nejde doplnit, verze pro tento OS propadá: vydej další verzi z obou počítačů |
| druhý OS nejde sestavit kvůli chybě v kódu tagu | vydání nezveřejňuj. Tag `vX.Y.Z` smazat ani přesunout nejde (ruleset „Tags v*“ z T-180 bez bypassu), verze propadá: koncept smaž (`gh release delete vX.Y.Z`, dokud je jediný), chybu oprav v `develop` a vydej další verzi. Proto generálka podepsaného buildu na obou OS předem (T-198 B4) |
| `The other machine has pushed main and vX.Y.Z, but not develop yet …` | první počítač ještě nepushnul `develop`: dokonči jeho běh, pak spusť znovu tady |
| druhý počítač má zastaralý `develop` (skripty bez T-200) | `git switch develop`, `git pull --ff-only` a spusť znovu (`release.ps1` běží z `develop`, build a nahrání ze skriptů tagu) |

### Historie: doplnění do zveřejněného vydání (T-113, do 1.0.0)

Do 1.0.0 šlo vydání zveřejnit jen se soubory jednoho OS (`release-publish.ps1 -AllowIncomplete`) a soubory druhého OS
doplnit později do zveřejněného vydání (`release-upload.ps1 -AddToPublished`). Tak vyšly 0.3.1 až 0.5.0. S neměnnými
vydáními (T-180) to nejde a od T-200 oba přepínače jen vypíšou postup výše a skončí s kódem 1. Aplikace mimo Velopack
dál nabízí jen vydání s úplně nahraným `SHA256SUMS-<os>.txt` svého OS (`GitHubReleaseChecker`, T-113) a instalace přes
Velopack vydání bez feedu svého RID přeskočí (ověřeno ve zdroji Velopack 1.2.161, `GitBase.GetReleaseFeed`).

## Postup

Ruční postup (reference; `scripts/release.ps1` dělá všechny kroky sám, na obou počítačích). Kroky 3 a 4 jdou v libovolném pořadí. Zveřejňuje se (krok 5) jen se soubory obou OS v konceptu: zveřejněné vydání je neměnné (T-180).

### 1. Větev release (Gitflow)

```powershell
git switch develop
git pull
git switch -c release/X.Y.Z
```

- V `Directory.Build.props` nastav `<Version>X.Y.Z</Version>`.
- V `CHANGELOG.md` dej sekci verze skutečné datum vydání: `## [X.Y.Z] - YYYY-MM-DD` (zástupné `- unreleased` nahraď datem; změny z `## [Unreleased]` přesuň do ní a `## [Unreleased]` nech prázdnou nahoře). Skript vydání odmítne sekci bez platného data (i `unreleased`), prázdnou sekci i chybějící sekci.
- Commit, případné opravy jen stabilizační.

Volitelně ověř balení ještě před tagem: `pwsh scripts/release-build.ps1 -SkipFeedSignature` jde spustit na libovolném commitu bez klíče (takový výstup se nenahraje nikdy, výstup s podpisem jen z tagovaného commitu).

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
pwsh scripts/release-build.ps1 -FeedSigningKey E:\layerpeek-feed-key.pem   # build, testy, publish win-x64 a win-arm64, smoke test win-x64, zipy, instalátory, podpis feedů, SHA256SUMS-windows.txt
pwsh scripts/release-upload.ps1 -DryRun         # jen kontroly a výpis, co by se nahrálo
pwsh scripts/release-upload.ps1                 # vytvoří koncept vydání a nahraje soubory
```

Smoke test spustí publikovanou aplikaci na 10 s s tvými skutečnými nastaveními (může ukázat skutečné notifikace) a odmítne běžet, když už aplikace běží. Před během ji ukonči.

Výchozí build sestaví `win-x64` i `win-arm64` (nahrávají se oba, rozhodnutí uživatele 2026-11-16), takže stroj vydání potřebuje i komponentu MSVC ARM64 a při prvním buildu `win-arm64` stáhne llvm-mingw (viz [prerequisites.md](prerequisites.md), „Windows ARM64“); `release.ps1` komponentu zkontroluje před začátkem vydání. Build `-Runtime win-x64` (jen `win-x64`) `release-upload.ps1` nahraje, ale `release-publish.ps1` vydání bez `releases.win-arm64.json` odmítne (instalace ARM64 by aktualizaci nenašly). `release.ps1` a vypsané další kroky berou argumenty buildu z tabulky RID (`Get-ReleaseBuildRuntimeArguments`, dnes na obou OS prázdné).

### 4. Běh na Macu

Stejné příkazy na Macu (Apple Silicon), **až když běh prvního OS úspěšně skončil** (ruční `release-upload.ps1` bez konceptu vytvoří koncept sám, souběžné ruční běhy proto ne; `release.ps1` souběh zvládá, viz „Vydání ze dvou počítačů“; jde-li Mac první, je to naopak a krok 3 přijde po něm). Terminál musí být ve fyzické cestě k repozitáři (`echo $PWD` = `pwd -P`, viz [prerequisites.md](prerequisites.md)). Build spouštěj pod `caffeinate -d -i -u` (Mac jinak během asi 20 minut buildu usne), přes SSH v `tmux`.

```bash
git switch --detach vX.Y.Z
pwsh scripts/release-build.ps1 -FeedSigningKey /Volumes/LP-KEY/layerpeek-feed-key.pem   # build, testy, publish osx-arm64, smoke test, LayerPeek.app, .dmg, .tar.gz, podpis feedu, SHA256SUMS-macos.txt
pwsh scripts/release-upload.ps1 -DryRun         # kontroly a výpis, co by se přidalo do konceptu
pwsh scripts/release-upload.ps1                 # přidá soubory macOS do konceptu z běhu na Windows (jde-li Mac první, koncept vytvoří)
```

Ruční ověření macOS buildu (Gatekeeper, oprávnění, menu bar, monitory, přihlášení, uspaný displej) je souhrnně v [macos-manual-testing.md](macos-manual-testing.md).

Vznikne `LayerPeek-X.Y.Z-osx-arm64.dmg`, `LayerPeek-X.Y.Z-osx-arm64.tar.gz`, od T-058 i `LayerPeek-X.Y.Z-osx-arm64-full.nupkg`, `releases.osx-arm64.json`, `assets.osx-arm64.json`, `RELEASES-osx-arm64` (aktualizace přes Velopack) a `SHA256SUMS-macos.txt`, které `release-upload.ps1` přidá do existujícího konceptu (postup sestavení viz „Balení pro macOS“ níže). Archiv se na macOS balí s `--no-mac-metadata` a `COPYFILE_DISABLE=1`, takže neobsahuje položky AppleDouble `._*` (ověřeno v T-019 výpisem `tar -tzf`). Zveřejňuje se, až má koncept soubory obou OS.

### 5. Kontrola a zveřejnění

1. Na GitHubu (*Releases*, koncept je vidět jen pro správce repozitáře) zkontroluj text a soubory.
2. Zveřejni:

```powershell
pwsh scripts/release-publish.ps1 -DryRun
pwsh scripts/release-publish.ps1
```

Po zveřejnění skript nabídne odeslání do winget (jen Windows, stabilní verze se souborem `Setup.exe`; jinak jen vypíše, proč ne nebo jak to udělat později), viz „Publikace do winget“.

`release-publish.ps1` zveřejní koncept jen tehdy, když je jediným vydáním tagu, obsahuje `SHA256SUMS-windows.txt` i `SHA256SUMS-macos.txt`, všechny soubory z nich jsou nahrané se stejným SHA256 (podle `digest`, který hlásí GitHub), žádný jiný soubor tam není a není tam soubor RID, který se nahrávat nesmí (`Uploadable = $false`, dnes žádný). `-AllowIncomplete` od T-200 jen odmítne (neměnná vydání).

### 6. Ověření staženého souboru

```powershell
Get-FileHash .\LayerPeek-X.Y.Z-win-x64.zip -Algorithm SHA256   # porovnat s SHA256SUMS-windows.txt
```

```bash
shasum -a 256 -c SHA256SUMS-macos.txt
```

## Kontrola aktualizace z GitHubu buildem 0.9.9 (jen po zveřejnění 1.0.0)

Rozhodnutí uživatele 2026-11-11 (T-198 otázka 3). 1.0.0 je první verze s ověřením podpisu feedu (T-181) a 0.5.0
ověřovač nemá, takže skutečné stažení `releases.<RID>.json.sig` z GitHubu (adresy, přesměrování) a jeho ověření
produkčním klíčem by poprvé proběhlo až u 1.0.0 → 1.0.1. Release build má adresu repozitáře napevno
(`AppUpdates.RepositoryUrl`) a důvěřuje jen `UpdateFeedKeys.Production`, testovací zdroj existuje jen v Debug. Proto se
hned po zveřejnění 1.0.0 na **Macu i na Windows** nainstaluje místní Release build **s verzí 0.9.9 z commitu tagu
`v1.0.0`** a nechá se aktualizovat na 1.0.0 z GitHubu. Kód je stejný jako v 1.0.0, liší se jen číslo verze.

**Verze jen v pracovním stromu.** `release-build.ps1` parametr verze nemá a `-p:Version=0.9.9` nepřebírá: verzi čte jen
z `<Version>` v `Directory.Build.props` (exe, `Info.plist`, `vpk --packVersion`, názvy souborů, `artifacts/release/<verze>`).
Samotné `dotnet publish -p:Version=0.9.9` nevytvoří balíček Velopacku a dva testy, které porovnávají verzi se souborem
(`AboutViewModelTests.VersionComesFromDirectoryBuildProps`, `ThirdPartyLicensesTests.FFmpegMacOSBuildScriptLinkUsesTheApplicationVersion`),
by selhaly. Verze se proto změní jen v pracovním stromu dočasného worktree, **nikdy se necommituje** a `develop` ani
tag se nemění.

**Nikdy se nenahraje.** Build 0.9.9 odmítne `release-upload.ps1` třikrát: strom není čistý (`Working tree is not
clean`), `HEAD` má tag `v1.0.0`, ale `<Version>` je 0.9.9, a build je zkušební (`-SkipFeedSignature`, `Status`
`skipped`). V dočasném worktree se přesto nespouští `release.ps1`, `release-upload.ps1` ani `release-publish.ps1`.

Pořadí: až po C1–C3 z T-198 (aktualizace nainstalované 0.5.0 na 1.0.0 a stažení z GitHubu), protože instalace 0.9.9
nahradí nainstalovanou aplikaci. Na každém OS:

1. **Dočasný worktree z tagu** (mimo repozitář, na Macu ve fyzické cestě, `pwd -P`). LayerPeek ukonči (smoke test
   spouští aplikaci s tvými nastaveními):

   ```powershell
   git fetch origin --tags
   git worktree add --detach ../LayerPeek-0.9.9 v1.0.0
   cd ../LayerPeek-0.9.9
   ```

2. **Verze 0.9.9 a sekce `CHANGELOG.md`** jen v pracovním stromu. Sekci verze s textem vyžaduje balení (poznámky
   k vydání pro `vpk`) i Pester test skutečného `CHANGELOG.md`:

   ```powershell
   (Get-Content Directory.Build.props -Raw) -replace '<Version>1\.0\.0</Version>', '<Version>0.9.9</Version>' | Set-Content Directory.Build.props -NoNewline
   (Get-Content CHANGELOG.md -Raw) -replace '(?m)^## \[1\.0\.0\] - ', "## [0.9.9] - unreleased`n`nLocal test build of the 1.0.0 commit for the update check (T-198). Never published.`n`n## [1.0.0] - " | Set-Content CHANGELOG.md -NoNewline
   git status --short        # jen tyto dva soubory
   ```

3. **Build bez klíče** (podpis feedu 0.9.9 není potřeba, ověřuje se až stažený feed 1.0.0). Skript upozorní, že build
   nejde nahrát:

   ```powershell
   pwsh scripts/release-build.ps1 -SkipFeedSignature                 # Windows
   caffeinate -d -i -u pwsh scripts/release-build.ps1 -SkipFeedSignature   # Mac
   ```

4. **Instalace 0.9.9:**
   - **Windows:** nainstalovanou LayerPeek odinstaluj (Nastavení → Aplikace; nastavení a hesla zůstanou) a spusť
     `artifacts\release\0.9.9\dist\LayerPeek-0.9.9-win-x64-Setup.exe`.
   - **Mac:** `rm -rf /Applications/LayerPeek.app`, pak
     `ditto artifacts/release/0.9.9/app/osx-arm64/portable/LayerPeek.app /Applications/LayerPeek.app` a
     `open /Applications/LayerPeek.app` (aktualizace přes Velopack funguje jen v Aplikacích, ne z `.dmg`).

5. **Kontrola:**
   - O aplikaci ukazuje 0.9.9.
   - Nastavení → Spuštění a aktualizace → **Zkontrolovat teď**: „Je dostupná verze 1.0.0…“, v menu „Je dostupná nová
     verze 1.0.0“ a **ne** „Aktualizace odmítnuta…“.
   - Klik na položku v menu: aplikace stáhne aktualizaci, ukončí se a spustí 1.0.0; O aplikaci ukazuje 1.0.0.
   - Log (`%LOCALAPPDATA%\LayerPeek\logs\LayerPeek.log`, `~/Library/Logs/LayerPeek/LayerPeek.log`) nemá řádek
     `Update check: the update feed … was rejected` ani `Update 1.0.0 is not installed: the update feed is refused`.
   - Mac: dotazy Klíčenky, oznámení a Místní sítě po aktualizaci zapsat k bodu T-058 v T-127 (oddíl 3).
   - Výsledek (datum, OS, verze) zapsat do T-198, T-127 (oddíl 3, bod T-181) a T-125 (oddíl 4).

   **Když 0.9.9 feed odmítne** nebo aktualizace selže: stejný kód má i 1.0.0, takže by odmítl i 1.0.1. Nic dalšího
   nezveřejňovat (ani winget), uložit log a založit úkol s nejvyšší prioritou; další postup (hotfix) rozhodne
   uživatel. Aplikaci pak nainstaluj ručně z GitHubu (`Setup.exe` / `.dmg` 1.0.0).

6. **Úklid:** zpátky v repozitáři smaž worktree i s buildem (`artifacts/` leží v něm). Nainstalovaná zůstává 1.0.0
   z GitHubu:

   ```powershell
   cd <repozitář>
   git worktree remove --force ../LayerPeek-0.9.9     # --force: pracovní strom má záměrné změny
   git worktree prune
   ```

## Výstupy sestavení

`artifacts/` je v `.gitignore`. Každý běh `release-build.ps1` složku verze nejdřív smaže, proto sestav všechny RID jednoho OS v jednom běhu.

```
artifacts/release/<verze>/
  publish/<rid>/        Native AOT publish složka (vstup balení)
  dist/                 soubory k nahrání: archivy, instalátory, zdroj FFmpeg, SHA256SUMS-<os>.txt
  build-info.json       značka sestavení: verze, commit, čistý strom, RID, soubory a jejich SHA256, podpis feedu, výsledek smoke testu
```

- `build-info.json` zapíše build (s klíčem) poprvé **hned po zabalení, před podpisem** jako záznam se stavem podpisu `pending` (T-197): stejný tvar jako konečná značka, jen `Files` obsahuje zabalené soubory bez `.sig` a `FeedSignature` je `Status` `pending` s otiskem klíče ověřeného před buildem. Po úspěšném podpisu ho přepíše konečná značka (`signed`). Když podpis selže, záznam zůstane; `release-upload.ps1` ho odmítne a `release-build.ps1 -SignOnly` podle něj podepíše hotový `dist/` (viz „Jen podepsat hotový `dist/`“).

- Archiv: `LayerPeek-<verze>-<RID>.zip` (Windows, obsah publish složky bez nadřazené složky), `.tar.gz` (macOS, podepsaná `LayerPeek.app`, zachová spustitelný bit).
- macOS (T-019, T-058): `LayerPeek-<verze>-osx-arm64.dmg`, balíček a feed Velopacku (`LayerPeek-<verze>-osx-arm64-full.nupkg`, `releases.osx-arm64.json`, `assets.osx-arm64.json`, `RELEASES-osx-arm64`; `Portable.zip` ani `.pkg` se nevydávají) a podepsaná aplikace od `vpk` pro místní použití v `artifacts/release/<verze>/app/osx-arm64/portable/LayerPeek.app` (nenahrává se).
- Instalátor Velopack (jen Windows, T-017, viz „Instalátor pro Windows“ níže): `LayerPeek-<verze>-<RID>-Setup.exe` a soubory aktualizací `LayerPeek-<verze>-<RID>-full.nupkg`, `releases.<RID>.json`, `assets.<RID>.json`, `RELEASES-<RID>`. `vpk` pracuje ve vlastní složce `velopack-<RID>` vedle `dist/` a po zkopírování ji skript smaže.
- Windows ARM64 (T-226): výchozí build na Windows má vedle souborů `win-x64` i `LayerPeek-<verze>-win-arm64.zip`, `LayerPeek-<verze>-win-arm64-Setup.exe`, `LayerPeek-<verze>-win-arm64-full.nupkg`, `releases.win-arm64.json` (+ `.sig`), `assets.win-arm64.json` a `RELEASES-win-arm64` (kanál `win-arm64`, bez kolize názvů) a jeden `SHA256SUMS-windows.txt` pro obě architektury. Před balením skript ověří, že exe a všechny DLL publish složky jsou ARM64 (`Get-PublishArchitectureCheck`, T-224; `vpk` architekturu nekontroluje). Ověřeno 2026-11-15 (zkušební build 0.5.0 na Windows x64): zip, `.nupkg` (včetně `Squirrel.exe` a `LayerPeek_ExecutionStub.exe`) a `Setup.exe` `win-arm64` jsou celé ARM64 `0xAA64`, `win-x64` celé x64.
- **Ladicí symboly se nevydávají** (rozhodnuto 2026-10-21): archiv neobsahuje soubory `*.pdb` ani nic ze složek `*.dSYM` a nikam se nenahrávají. V publish složce zůstanou pro lokální ladění. Zip `win-x64` má tak asi 70 MB místo 120 MB.
- Zdrojový kód FFmpeg (T-199): `ffmpeg-source-<verze>-<os>.tar.xz` (viz „Zdrojový kód FFmpeg“ níže).
- Podpis feedu (T-181): `releases.<RID>.json.sig` ke každému `releases.<RID>.json` (jeden řádek base64 + LF). `build-info.json` má `FeedSignature`: `Status` `signed` (s `PublicKeySha256`, otiskem veřejného klíče) nebo `skipped` (`-SkipFeedSignature`).
- Kontrolní součty: jeden soubor na OS, formát `sha256sum` (`<hash>  <soubor>`, LF), obsahuje všechny soubory z `dist/` tohoto běhu (i `.sig`).
- Build zastaví jakékoli varování (`-warnaserror` u `dotnet build` i `dotnet publish`, tedy i trim/AOT varování z ILC). Testy běží v konfiguraci Release.
- Na Windows skript ověří, že *Product version* exe odpovídá `<Version>`.
- Smoke test běží jen pro RID tohoto stroje (OS a architektura). Ostatní RID skript vypíše jako „not verified by running“ (na Windows x64 `win-arm64`: `not verified by running (this machine is windows-x64)`; Windows na x64 ARM64 binárky spustit neumí, smoke test `win-arm64` na zařízení je v T-227 – `win-arm64` se nahrává i bez něj, rozhodnutí uživatele 2026-11-16). Smoke test běží nad hotovou publish složkou (`aot-smoke.ps1 -PublishDir`) a skript ověří, že ji nezměnil. `aot-smoke.ps1 -Runtime win-arm64` na Windows x64 skončí hláškou `AOT smoke check NOT RUN: runtime 'win-arm64' cannot run on this machine (windows-x64) …` a nic nepublikuje ani nespustí.

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
| výstup obsahuje neznámý RID (např. `win-x86`) | `Build output contains unknown runtime …` |
| výstup obsahuje RID s `Uploadable = $false` (od 2026-11-16 žádný; dříve `win-arm64`) | `Build output contains <RID>, which must not be uploaded: <BlockedReason>. Rebuild without it: pwsh scripts/release-build.ps1 -Runtime <nahratelné RID>` |
| zkušební build `release-build.ps1 -SkipFeedSignature` (T-181) | `Build output was made with -SkipFeedSignature (a trial build) …` |
| podpis na konci buildu nedoběhl (`build-info.json` se stavem `pending`, T-197) | `Build output is not signed yet: the signing at the end of release-build.ps1 did not finish … release-build.ps1 -SignOnly` |
| `build-info.json` bez stavu podpisu (build starší než T-181) | `Build output has no update feed signature (made before T-181?) …` |
| `releases.<RID>.json` bez `releases.<RID>.json.sig` ve výstupu | `Build output has releases.<RID>.json without its signature …` |
| v `dist/` je soubor navíc, chybí nebo nesedí SHA256 | `dist contains files not produced by the build` / `… does not match its SHA256` |
| vydání už je zveřejněné (neměnné) | `Release vX.Y.Z is already published … Files are uploaded only into a draft. It already has the <os> files.` / `It has no <os> files, and published releases are immutable (T-180): they can never be added …` |
| víc vydání pro tag (dva koncepty, T-200) | `GitHub has N releases for vX.Y.Z …` |
| soubor se stejným názvem, ale jiným obsahem už je nahraný | `Already uploaded with different content … Re-run with -Force` |
| `-AddToPublished` (s čímkoli, i `-Repo`; před všemi kontrolami, bez volání `gh`) | `-AddToPublished is no longer supported (T-200) … Upload the files of both OS into the draft first, then publish …` |

Navíc jen `release-publish.ps1`:

| Kontrola | Hláška (začátek) |
|---|---|
| vydání neexistuje / už je zveřejněné | `There is no release for vX.Y.Z` / `Release vX.Y.Z is already published` |
| víc vydání pro tag (dva koncepty, T-200) | `GitHub has N releases for vX.Y.Z …` |
| chybí `SHA256SUMS-<os>.txt` některého OS | `Release vX.Y.Z has no SHA256SUMS-<os>.txt: the <os> build is not uploaded yet. Upload it on the <os> machine first …` |
| chybí feed `releases.<RID>.json` vydávaného RID | `Release vX.Y.Z has no releases.<RID>.json: installations of …` |
| feed `releases.<RID>.json` bez podpisu `releases.<RID>.json.sig` (T-181) | `Release vX.Y.Z has releases.<RID>.json but no releases.<RID>.json.sig …` |
| `-AllowIncomplete` (s čímkoli; před všemi kontrolami, bez volání `gh`) | `-AllowIncomplete is no longer supported (T-200) …` |
| soubor chybí, nesedí SHA256 nebo není v žádném souboru součtů | `… is listed in … but not uploaded` / `Uploaded … does not match its SHA256` / `… is uploaded but not listed in any checksum file` |

Skripty `origin` jen čtou jedním `git ls-remote`, nic nestahují ani nepushují (lokální refy se nemění). `gh auth status` se volá bez výpisu.

## Opakování a chyby

| Situace | Co dělat |
|---|---|
| nahrání spadlo uprostřed (síť) | spusť `release-upload.ps1` znovu: soubory se stejným obsahem přeskočí, zbytek nahraje |
| po novém buildu stejného commitu hlásí „different content“ | AOT build nemusí být bajtově stejný. Buď nahraj původní výstup, nebo spusť s `-Force` (přepíše soubory v konceptu, `--clobber`) |
| v tagovaném commitu je chyba | koncept smaž (`gh release delete vX.Y.Z`), tag smaž (`git push origin :refs/tags/vX.Y.Z`, `git tag -d vX.Y.Z`), oprav to v `release/X.Y.Z` nebo `hotfix/X.Y.Z` a začni krokem 2. Už zveřejněné vydání neměň, vydej novou verzi |
| oba počítače běží souběžně | od T-200 v pořádku: koncept vzniká hned po pushi tagu a druhý počítač ho najde. Kdyby přesto vznikly dva koncepty (oba ve stejném okamžiku), skripty nic nenahrají ani nezveřejní a vypíšou `GitHub has 2 releases for vX.Y.Z …`: druhý koncept smaž na GitHubu a spusť znovu (viz „Vydání ze dvou počítačů“) |
| nahrání bylo přerušené (pád, Ctrl+C) | spusť `release-upload.ps1` znovu. Nedokončený soubor může na GitHubu zůstat ve stavu `starter` bez `digest`: skript ho bere jako „jiný obsah“ a bez `-Force` ho odmítne, s `-Force` ho přepíše. Chování ověřit při zkušebním vydání |
| `release-build.ps1` hlásí chybějící komponentu MSVC | viz [prerequisites.md](prerequisites.md) |
| `release-build.ps1` hlásí chybu klíče feedu (`No update feed signing key`, `… does not exist. Is the key medium connected?`, `… trusts no key yet`, `… is not trusted by …`, `cannot be decrypted`) | viz „Podpis feedu aktualizací“ níže (tabulka „Co `release-build.ps1` odmítne“) |
| podpis na konci buildu selhal (médium odpojené a `abort`, neinteraktivní relace, 3× špatné heslo, jiný klíč na cestě); `release-upload.ps1` hlásí `Build output is not signed yet` | build znovu nespouštěj: odstraň příčinu (připoj médium) a podepiš hotový `dist/` příkazem `pwsh scripts/release-build.ps1 -SignOnly -FeedSigningKey <cesta>` ve stejném čistém checkoutu tagu (viz „Jen podepsat hotový `dist/`“) |
| druhý OS nesestaví tag kvůli zastaralým referenčním snímkům UI svého OS (`Snapshot '…': N pixels differ`), protože první OS přegeneroval jen svoje | kód aplikace je v pořádku, ale tag už je pushnutý, takže ho neměň. Přegeneruj snímky v `develop` (`LAYERPEEK_UPDATE_SNAPSHOTS=1`, běžná větev `feature/*`). Pak v čisté kopii tagu nahraď jen rozdílné PNG z `develop`: seznam ulož do souboru (`git diff --name-only vX.Y.Z develop -- tests/LayerPeek.App.UiTests/Snapshots/<OS>/ > snapshots.txt`, soubor mimo repozitář), `xargs git update-index --skip-worktree < snapshots.txt` a soubory přepiš z `develop`, např. `while read f; do git show "develop:$f" > "$f"; done < snapshots.txt`. Pak `release-build.ps1`, `release-upload.ps1` a nakonec `xargs git update-index --no-skip-worktree < snapshots.txt` a `xargs git checkout -- < snapshots.txt`. Binárka vzniká jen z kódu tagu, referenční PNG testů se do ní nedostanou. Poprvé použito u 0.5.0 na Macu (2026-11-05, po T-170) s výslovným souhlasem uživatele. Před vydáním proto na obou OS spusť testy UI (T-125/T-127) |

## Podpis feedu aktualizací (T-181)

Velopack ověřuje stažený balíček jen proti SHA256 ve feedu `releases.<RID>.json`. Ten ale leží ve stejném vydání na GitHubu jako balíček, takže kdo převezme účet nebo token, mohl by vydat škodlivou aktualizaci. Proto feed podepisuje **offline soukromý klíč** (nikdy na GitHubu ani v repozitáři) a aplikace aktualizaci odmítne, když podpis `releases.<RID>.json.sig` chybí nebo neodpovídá veřejnému klíči zabudovanému v binárce (`src/LayerPeek.App/Installer/UpdateFeedKeys.cs`, seznam `Production`). Formát podpisu (ECDSA P-256 / SHA-256, podepsaná data `LayerPeek-feed-v1\n<RID>\n<bajty feedu>`, jeden řádek base64) popisuje [velopack.md](velopack.md), bod 13.

### Jak podepisují skripty

- **`release-build.ps1`** podepíše každý `releases.<RID>.json` do `releases.<RID>.json.sig` v `dist/`. Klíč je **šifrovaný PEM (PKCS#8)** na offline médiu; cestu dostane parametrem `-FeedSigningKey <cesta>` nebo z proměnné prostředí `LAYERPEEK_FEED_SIGNING_KEY` (tu používá `release.ps1`). Heslo se zadává do výzvy (nevypisuje se), **dvakrát**: před buildem a znovu při podpisu na konci. Pro volání z jiného skriptu PowerShellu jde předat `-FeedSigningKeyPassword` jako `SecureString` a použije se pro oba kroky (skript pracuje s kopiemi a každou hned zahodí); heslo nikdy nepiš jako text do příkazové řádky (zůstalo by v historii).
- **Kontroly ještě před dlouhým buildem**, v tomto pořadí: klíč je zadaný → soubor existuje a je šifrovaný (nešifrovaný soukromý klíč skript odmítne) → `UpdateFeedKeys.Production` není prázdný (bez dotazu na heslo) → heslo → dešifrování jen v paměti → veřejný klíč odvozený ze soukromého je v `UpdateFeedKeys.Production`. Pak skript vypíše otisk veřejného klíče (SHA-256) a `Keep the key medium connected …` a klíč, jeho šifrovaný text i heslo hned zahodí. **Během buildu (asi 20 minut) nemá skript v paměti nic tajného**, jen cestu ke klíči a otisk veřejného klíče (rozhodnuto uživatelem 2026-11-11, otázka 17 v T-181: na macOS není `SecureString` v paměti šifrovaný). **Médium s klíčem proto musí zůstat připojené až do podpisu na konci buildu.**
- **Po zabalení** skript soubor klíče **přečte znovu ze stejné cesty**. Když tam není (médium odpojené), build neztratí: vypíše `The update feed signing key … is not there (key medium disconnected?). Connect the key medium and press Enter to try again, or type 'abort'` a po Enteru to zkusí znovu. V neinteraktivní relaci (nebo po `abort`) skončí chybou; balíčky v `dist/` pak zůstanou bez podpisu se záznamem `build-info.json` ve stavu `pending` (zapsaným po zabalení, před podpisem), upload je odmítne a podepíše je `release-build.ps1 -SignOnly` bez nového buildu (T-197, viz „Jen podepsat hotový `dist/`“). Pak se **podruhé zeptá na heslo** (po špatném hesle z výzvy dovolí celkem 3 pokusy, heslo z `-FeedSigningKeyPassword` zkusí jen jednou), dešifruje klíč a odmítne ho, když jeho veřejný klíč není **stejný jako při kontrole na začátku** (jiné médium nebo vyměněný soubor; nic se nepodepíše). Podepíše feedy, každý zapsaný `.sig` přečte zpět a ověří proti klíčům z `UpdateFeedKeys.Production` stejně jako aplikace, a klíč i heslo hned zahodí (`Dispose`, pomocné pole znaků vynulované). `.sig` patří do `SHA256SUMS-<os>.txt` i `build-info.json` (`FeedSignature`: `Status` `signed` a `PublicKeySha256`, jen otisk veřejného klíče).
- **Seznam důvěryhodných klíčů čte skript přímo ze zdroje aplikace** (`UpdateFeedKeys.cs`, stejně jako jazyky z `AppLanguages.cs`): jediný zdroj pravdy, žádný druhý soubor, který by se mohl rozejít. Seznam, který skript nedokáže celý přečíst (jiný tvar než PEM v raw string literálech, čárky a komentáře `//`), je chyba, nikdy tiché „žádné klíče“.
- **Soukromý klíč ani heslo** skript nikam nekopíruje, nevypisuje ani nezapisuje (ani do `artifacts/`, ani do logu); hlídají to Pester testy.
- **Prázdný `UpdateFeedKeys.Production`** (např. omyl při rotaci) build bez `-SkipFeedSignature` odmítne (otázka 8 v T-181): nevznikne vydání, jehož aplikace by už žádnou další aktualizaci nepřijala. Produkční klíč je v seznamu od 2026-11-11 (otisk veřejného klíče `4cb047d19a472427db721e607c76c15ba0fad96e59d564114a62a7e5e7b91e26`).
- **Zkušební build** `-SkipFeedSignature` klíč nepotřebuje; `build-info.json` ho označí (`Status` `skipped`) a `release-upload.ps1` ho odmítne. Hodí se pro místní ověření balení (`macos-verify.ps1`, `windows-verify.ps1`, E2E test instalátoru).
- **`release-upload.ps1`** nahraje `.sig` spolu s balíčky v prvním volání `gh release upload` a feed až v druhém (nový koncept nejdřív vytvoří prázdný). Feed tak na GitHubu nikdy není bez svého podpisu.
- **`release-publish.ps1`** odmítne vydání, kde u některého `releases.<RID>.json` chybí `.sig`.

Co `release-build.ps1` odmítne (před buildem, kód 1):

| Kontrola | Hláška (začátek) |
|---|---|
| klíč nezadaný (ani `-FeedSigningKey`, ani `LAYERPEEK_FEED_SIGNING_KEY`) | `No update feed signing key. Connect the key medium and pass -FeedSigningKey …` |
| `-SkipFeedSignature` spolu s klíčem nebo heslem | `-SkipFeedSignature builds without the update feed signature; do not pass …` |
| soubor klíče neexistuje (médium nepřipojené) | `The update feed signing key … does not exist. Is the key medium connected?` |
| soubor je nešifrovaný soukromý klíč / není šifrovaný PEM | `… is an unencrypted private key …` / `… is no update feed signing key …` |
| `UpdateFeedKeys.Production` je prázdný | `… (UpdateFeedKeys.Production) trusts no key yet …` |
| seznam v `UpdateFeedKeys.cs` nejde celý přečíst | `UpdateFeedKeys.Production … has entries other than raw string PEM literals …` |
| neinteraktivní relace bez `-FeedSigningKeyPassword` / prázdné heslo | `No password for the update feed signing key: this session is not interactive …` |
| špatné heslo nebo poškozený soubor | `The update feed signing key cannot be decrypted: wrong password, or the key file is damaged.` |
| veřejný klíč není v `UpdateFeedKeys.Production` (jiné médium, klíč nepřidaný) | `The signing key (public key SHA-256 …) is not trusted by …` |

Co odmítne při podpisu na konci (po zabalení, kód 1, balíčky zůstanou bez podpisu a nejdou nahrát; podepíše je `release-build.ps1 -SignOnly`, viz níže):

| Kontrola | Hláška (začátek) |
|---|---|
| soubor klíče chybí a relace není interaktivní (nebo skončil vstup) | `The update feed signing key … is not there for signing (key medium disconnected?) and this session is not interactive …` |
| soubor klíče chybí a uživatel napsal `abort` | `Signing aborted: the update feed signing key … is not there.` |
| špatné heslo (3× z výzvy, nebo jednou z `-FeedSigningKeyPassword`) | `The update feed signing key cannot be decrypted: wrong password, or the key file is damaged.` |
| na cestě je jiný klíč než při kontrole na začátku | `The update feed signing key … (public key SHA-256 …) is not the key checked before the build …` |
| jen podepsat hotový `dist/` (`-SignOnly`) | viz „Jen podepsat hotový `dist/`“ |

### Jen podepsat hotový `dist/` (`-SignOnly`, T-197)

Když podpis na konci buildu selže, balíčky v `dist/` jsou hotové, jen bez `.sig`. Přepínač `-SignOnly` je podepíše **bez nového buildu** (rozhodnutí uživatele 2026-11-11: přepínač `release-build.ps1`, ne samostatný skript). Nic nesestavuje ani nebalí; podepíše jen `dist/`, o kterém ví, že vznikl z čistého checkoutu tagu a od buildu se nezměnil.

```powershell
git switch --detach vX.Y.Z      # stejný checkout, ve kterém build běžel (čistý strom)
pwsh scripts/release-build.ps1 -SignOnly -FeedSigningKey /Volumes/LP-KEY/layerpeek-feed-key.pem   # Windows: -FeedSigningKey E:\layerpeek-feed-key.pem
pwsh scripts/release-upload.ps1 -DryRun
```

- **Záznam buildu:** build zapisuje po zabalení a před podpisem `artifacts/release/<verze>/build-info.json` se stavem `pending` (verze, commit, čistý strom, OS, RID, každý zabalený soubor s SHA-256, otisk klíče ověřeného před buildem, výsledek smoke testu). Velikost souborů záznam nemá: SHA-256 ji pokrývá. `-SignOnly` podle něj ověří, že:
  - aktuální checkout je **čistý** (ani nesledované soubory) a `HEAD` má tag `vX.Y.Z` podle `<Version>` (stejná kontrola jako `release-upload.ps1`; tag na `origin` a `main` kontroluje až upload, `-SignOnly` nic nečte ze sítě);
  - `HEAD` je **commit, ze kterého build vznikl**, build byl z čistého stromu, na tomto OS a pro známé RID tohoto OS;
  - v `dist/` je **přesně** seznam souborů záznamu, každý se stejným SHA-256 (žádný chybějící, přebývající ani změněný; i `.DS_Store` je soubor navíc).
- **Klíč:** stejné kontroly jako podpis na konci buildu a sdílený kód (`Invoke-FeedSigning`): cesta z `-FeedSigningKey` nebo `LAYERPEEK_FEED_SIGNING_KEY`; ještě před heslem `UpdateFeedKeys.Production` není prázdný a důvěřuje klíči, který build ověřil před buildem; pak soubor je šifrovaný PEM PKCS#8 (nešifrovaný odmítne), heslo **jednou** z výzvy (3 pokusy) nebo z `-FeedSigningKeyPassword` (jeden pokus), veřejný klíč musí být **stejný jako klíč ověřený před buildem** (otisk ze záznamu; jiný klíč, i důvěryhodný, odmítne), podpis každého `releases.<RID>.json`, ověření zpětným čtením proti `UpdateFeedKeys.Production` a okamžité zahození klíče a hesla. Když médium chybí, zeptá se na jeho připojení jako build.
- **Výstup** je stejný jako po podepsaném buildu: `releases.<RID>.json.sig`, `SHA256SUMS-<os>.txt` (všechny soubory i `.sig`) a `build-info.json` se stavem `signed`; `release-upload.ps1` a `release-publish.ps1` se nemění. Když podpis selže, `.sig` zapsané v tomto běhu se smažou a záznam zůstane `pending`, takže jde `-SignOnly` spustit znovu.
- **Už podepsaný `dist/` se znovu nepodepisuje** – `-SignOnly` ho odmítne a odkáže na upload. Zkušební build `-SkipFeedSignature` podepsat nejde (nemá ověřený klíč ani záznam `pending`).
- Záznam leží vedle `dist/` ve stejné složce, chrání tedy proti omylu (jiný commit, přepsaný nebo přidaný soubor, starý build), ne proti někomu, kdo může měnit soubory na stroji vydání.
- S `-SignOnly` nejde zadat `-Runtime`, `-SmokeSeconds` ani `-SkipFeedSignature` (RID bere ze záznamu).

Co `-SignOnly` odmítne (kód 1, nic nepodepíše; pak je potřeba nový build):

| Kontrola | Hláška (začátek) |
|---|---|
| `-SignOnly` spolu s `-Runtime`, `-SmokeSeconds` nebo `-SkipFeedSignature` | `-SignOnly signs the finished build as it is; do not pass …` |
| pracovní strom není čistý / `HEAD` bez tagu verze | `Working tree is not clean …` / `HEAD has no tag vX.Y.Z …` / `HEAD is tagged … but the version …` |
| chybí záznam buildu (`build-info.json`) nebo je neúplný / není JSON | `There is no build record (build-info.json) of version … to sign` / `The build record build-info.json is incomplete …` / `… is no build record (not JSON)` |
| `dist/` už je podepsaný | `The build of version … is already signed …; a signed dist/ is never signed again. Upload it …` |
| zkušební build `-SkipFeedSignature` / jiný stav podpisu | `… is a trial build of -SkipFeedSignature; it cannot be signed afterwards` / `… has no pending signature …` |
| záznam jiné verze, z jiného commitu, ze špinavého stromu, z jiného OS | `The build record is of version …` / `dist/ was built from commit …, HEAD is …` / `… from a working tree with uncommitted or untracked files …` / `dist/ was built on …` |
| neznámý RID / RID jiného OS / jiný soubor součtů / chybí otisk klíče / chybí feed / záznam uvádí `.sig` nebo soubor dvakrát | `… unknown runtime …` / `… a runtime of …` / `… names the checksum file …` / `… does not name the public key …` / `… has no update feed …` / `… which only the signing writes` / `… lists a file twice` |
| soubor v `dist/` chybí / přebývá / změnil se | `Files of the build are missing in dist/ …` / `dist/ contains files not produced by the build …` / `Files in dist/ changed after the build …` |
| klíč nezadaný / chybí / nešifrovaný | `No update feed signing key …` / `… is not there for signing …` / `… is an unencrypted private key …` |
| `UpdateFeedKeys.Production` je prázdný / nedůvěřuje klíči ze záznamu (bez dotazu na heslo) | `… trusts no key yet …` / `The signing key (public key SHA-256 …) is not trusted by …` |
| neinteraktivní relace bez hesla / špatné heslo | `No password … this session is not interactive …` / `… cannot be decrypted: wrong password …` |
| na cestě je jiný klíč, než build ověřil | `… is not the key checked before the build …` |

### Vytvoření páru klíčů (jednou)

Na stroji vydání, nejlépe bez připojení k síti, s připojeným médiem pro klíč (USB 1). Soukromý klíč se zapisuje rovnou na médium, nikdy na disk počítače ani do cloudu:

```powershell
Import-Module ./scripts/ReleaseTools/ReleaseTools.psm1
New-UpdateFeedSigningKey -Path /Volumes/LP-KEY/layerpeek-feed-key.pem -PublicKeyPath /Volumes/LP-KEY/layerpeek-feed-key.public.pem
# Windows: New-UpdateFeedSigningKey -Path E:\layerpeek-feed-key.pem -PublicKeyPath E:\layerpeek-feed-key.public.pem
```

- Funkce se dvakrát zeptá na heslo (musí se shodovat, aspoň 16 znaků; přijato uživatelem 2026-11-11, otázka 18 v T-181). Silné heslo: dlouhá heslová fráze z náhodných slov, nikde jinde nepoužitá. Ulož ho **odděleně od klíče** (např. ve správci hesel); kdo má médium i heslo, může podepsat aktualizaci.
- Vytvoří klíč ECDSA P-256 a zapíše ho jako šifrovaný PEM PKCS#8 (`ENCRYPTED PRIVATE KEY`: PBES2, PBKDF2-SHA256 s 600 000 iteracemi, AES-256-CBC; stejně jako testovací klíč v `tests/Shared/Fixtures/UpdateFeed`). Na macOS a Linuxu nastaví právo jen pro vlastníka (na FAT/exFAT se to nedá; soubor je šifrovaný tak jako tak). Veřejný klíč (PEM `PUBLIC KEY`, není tajný) uloží vedle a vrátí ho i s otiskem SHA-256. Existující soubor nikdy nepřepíše.

### Uložení a záloha

- **Primární médium (USB 1):** šifrovaný klíč a veřejný klíč. Připojuje se jen při vydání.
- **Záloha (USB 2) na jiném místě:** zkopíruj šifrovaný soubor z média na médium (ne přes disk počítače ani cloud) a ověř, že jde dešifrovat a má stejný otisk:

  ```powershell
  Import-Module ./scripts/ReleaseTools/ReleaseTools.psm1
  $key = Import-FeedSigningKey -EncryptedPem (Read-FeedSigningKeyFile /Volumes/LP-KEY-2/layerpeek-feed-key.pem) -Password (Read-Host -AsSecureString 'Heslo')
  Get-FeedKeyFingerprint $key.ExportSubjectPublicKeyInfo(); $key.Dispose()
  ```

- **Ztráta klíče** (obou médií nebo hesla): nainstalované verze už žádnou aktualizaci nepřijmou. Postup je stejný jako při kompromitaci (nový klíč, nová verze, ruční přeinstalace).

### Veřejný klíč do aplikace

Produkční klíč vytvořil uživatel 2026-11-11 na offline USB (s ověřenou zálohou) a jeho veřejný klíč je v `UpdateFeedKeys.Production` (otisk `4cb047d19a472427db721e607c76c15ba0fad96e59d564114a62a7e5e7b91e26`, uvedený i v komentáři u seznamu). Postup níže platí pro každý další klíč (rotace, kompromitace):

1. Na větvi úkolu vlož PEM nového veřejného klíče do `UpdateFeedKeys.Production` v `src/LayerPeek.App/Installer/UpdateFeedKeys.cs` jako raw string literál (stejný tvar jako testovací klíč v `Test`; při rotaci vedle starého, viz „Rotace klíče“) a v komentáři u seznamu uveď datum a otisk.
2. `dotnet test`: Pester test ověří, že seznam jde přečíst a testovací klíč v něm není.
3. Sloučit do `develop` běžným postupem. Od první verze s tímto klíčem musí mít každé vydání `.sig` (skripty to vynutí) a podepisuje se jen klíčem ze seznamu této verze.

### Podepisování při vydání

- Jen z **čistého checkoutu tagu** na stroji vydání (`git switch --detach vX.Y.Z`); podepisuje se na Windows i na Macu stejným klíčem (rozhodnuto 2026-11-06).
- Médium připoj těsně před buildem a **nech ho připojené až do konce buildu**: na začátku se klíč jen ověří, podepisuje se až po zabalení a skript ho pak čte z média znovu. Odpoj ho hned po skončení buildu. Heslo se zadává **dvakrát** (před buildem a při podpisu), vždy jen do výzvy skriptu. Kdo od buildu odejde, musí se vrátit k druhé výzvě na heslo; bez ní build nedoběhne.

```powershell
pwsh scripts/release-build.ps1 -FeedSigningKey /Volumes/LP-KEY/layerpeek-feed-key.pem   # Windows: -FeedSigningKey E:\layerpeek-feed-key.pem
```

S `release.ps1` nastav proměnnou ve stejném terminálu a po vydání ji smaž:

```powershell
$env:LAYERPEEK_FEED_SIGNING_KEY = '/Volumes/LP-KEY/layerpeek-feed-key.pem'
pwsh scripts/release.ps1 -Version X.Y.Z
Remove-Item Env:LAYERPEEK_FEED_SIGNING_KEY
```

### Rotace klíče (plánovaná výměna)

Aplikace má kvůli rotaci seznam klíčů:

1. Vytvoř nový pár klíčů (nové médium a záloha, viz výše).
2. **Verze N:** přidej nový veřejný klíč do `UpdateFeedKeys.Production` vedle starého. Verzi N podepiš ještě **starým** klíčem: instalace před N znají jen starý klíč.
3. **Verze N+1 a další:** podepisuj **novým** klíčem. Instalace od N ho přijmou.
4. Starý klíč ze seznamu odeber až v některé pozdější verzi a pak zlikviduj obě jeho kopie.

**Instalace, která verzi N přeskočí, potřebuje ruční přeinstalaci.** Aplikace čte jen feed nejnovějšího vydání (otázka 9 v T-181), takže instalace starší než N, která N vynechá (např. byla v době N vypnutá), uvidí rovnou N+1 podepsanou novým klíčem, který nezná, a aktualizaci odmítne; sama se už nikdy neaktualizuje. Uživatel musí novou verzi stáhnout a nainstalovat ručně. Přijato uživatelem 2026-11-11 (otázka 16 v T-181). Mezi N a N+1 proto nech dost času (uživatelé se mají stihnout aktualizovat na N) a v poznámkách k vydání N+1 to uveď.

### Kompromitace (únik) soukromého klíče

Bezpečná rotace aktualizací **nejde**: útočník se starým klíčem může podepsat i „rotaci“, tedy verzi s vlastním klíčem v seznamu. Postup:

1. Nový pár klíčů. V `UpdateFeedKeys.Production` nahraď starý klíč novým (starý odeber hned, ne až později).
2. Nová verze podepsaná novým klíčem.
3. Oznámení v `README.md` / `README.cz.md` a v textu vydání (a `### Security` v `CHANGELOG.md`): nainstalované verze tuto aktualizaci odmítnou a mohou přijmout aktualizaci podepsanou uniklým klíčem; uživatelé musí novou verzi **stáhnout a nainstalovat ručně** z GitHubu a ověřit ji podle `SHA256SUMS-*.txt`.
4. Zkontroluj účet GitHub (tokeny, 2FA, vydání, která nevydal správce) a podezřelá vydání smaž.

### Rozsah ochrany

- Chrání jen **aktualizace přes Velopack** (Windows instalace ze `Setup.exe`, macOS aplikace v Aplikacích).
- **První stažení** instalátoru (`Setup.exe`, `.dmg`, `.zip`, `.tar.gz`) z GitHubu podpis nemá; tam pomáhá jen T-180 (neměnná vydání) a ruční kontrola `SHA256SUMS-*.txt`, které jsou ale ve stejném vydání.
- Aplikace mimo Velopack (`GitHubReleaseChecker`, T-058) jen otevře stránku vydání, nic neinstaluje.
- **Už nainstalované verze bez ověření** (vydané před T-181) přijmou jakoukoli aktualizaci, i nepodepsanou; chráněná je instalace až od chvíle, kdy běží verze s ověřením. Od prvního vydání s ověřením musí mít **každé** vydání `.sig` (jinak by ho verze s ověřením odmítly); `release-build.ps1`, `release-upload.ps1` a `release-publish.ps1` to vynucují.
- **Opětovné nahrání staršího podepsaného feedu (replay)** k instalaci starší verze nevede (Velopack instaluje jen vyšší verzi), jen „zmrazí“ aktualizace. Přijatelné zbytkové riziko. Podpis feedu jednoho RID nejde použít pro jiný RID (RID je součástí podepsaných dat).
- **Příznak stabilní / předběžné vydání podpis nekryje:** podepsaná data jsou jen prefix, RID a bajty feedu, příznak `--prerelease` vydání na GitHubu ne. Kdo má přístup k vydáním, může pravý podepsaný feed předběžné verze (např. `0.5.0-rc.1`) znovu nahrát jako stabilní vydání. Aplikace ale nabídku filtruje podle přípony verze (semver), ne podle příznaku na GitHubu, takže uživatel bez volby „Nabízet předběžné verze“ ji stejně nedostane; podstrčit jde jen pravý, námi podepsaný build. Přijato jako zbytkové riziko (rozhodnuto uživatelem 2026-11-11, otázka 23 v T-181).

## Zdrojový kód FFmpeg (T-199)

Rozhodnutí uživatele 2026-11-11 (T-198, otázka 9): ke každému vydání se přiloží **přesně použitý zdrojový kód FFmpeg**
a okno O aplikaci má větu z kontrolního seznamu LGPL na https://ffmpeg.org/legal.html (bod 10). Kontrolní seznam
doporučuje zdroj hostovat u binárky (body 3–8); dřív aplikace jen odkazovala na cizí servery a build BtbN už jednou
zmizel (T-162).

**Co vydání obsahuje.** Obě OS od T-202 překládají stejný oficiální tarball (`LayerPeekFFmpegSource*` v
`build/FFmpeg.targets`), liší se skriptem buildu. Každý OS má svůj soubor `ffmpeg-source-<verze>-<os>.tar.xz` (jedna
složka nejvyšší úrovně stejného názvu, xz). Windows má **jeden archiv pro obě architektury** (`win-x64` a `win-arm64`,
rozhodnutí uživatele 2026-11-12, T-225): stejný tarball i skript, `README.txt` uvádí řádek configure každé vydávané
architektury.

| OS | Obsah |
|---|---|
| macOS | `ffmpeg-9.0.2.tar.xz` (oficiální tarball beze změny, ověřitelný podpisem `.asc` z ffmpeg.org), `build-ffmpeg.sh` (`build/macos/build-ffmpeg.sh`, Xcode Command Line Tools) a `README.txt` |
| Windows | `ffmpeg-9.0.2.tar.xz` (týž tarball), `build-ffmpeg.sh` (`build/windows/build-ffmpeg.sh`: `win-x64` MSYS2 + MinGW-w64, `win-arm64` llvm-mingw ze shellu MSYS2) a `README.txt` |

Žádné skripty ani zdroje cizích buildů (do T-202 měl archiv Windows commit FFmpeg a skripty BtbN jako tary
`git archive`; build BtbN obsahoval ~50 knihoven, jejichž zdroje se nepřikládaly). `README.txt` popisuje obsah, původ
(URL, SHA-256), postup buildu (Windows: toolchain a staticky linkovaný runtime MinGW-w64; u `win-arm64` i URL a SHA-256
připnutého zipu llvm-mingw a příkaz s šestým argumentem `<llvm-mingw folder>`) a **řádek configure přečtený z vydávané
knihovny `avcodec` každého RID** (prefix instalace nahrazený `<prefix>`, aby v něm nebyla cesta stroje buildu). Zip
llvm-mingw se nepřikládá (je to překladač; z něj jsou v DLL jen runtime MinGW-w64 a pár funkcí compiler-rt, viz
`docs/prerequisites.md`, „Windows ARM64 navíc“).

**Jak to dělá `release-build.ps1`.**
1. **Před dlouhým buildem** (po kontrole klíče feedu) `Get-FFmpegSourceInputs` přečte připnuté hodnoty
   (`Get-FFmpegSourcePins`, i kontrola souladu URL tarballu a jeho složky) a připraví tarball v cache
   **`LayerPeek.Build/ffmpeg-source`** (macOS `~/Library/Caches/LayerPeek.Build/ffmpeg-source`, Windows
   `%LOCALAPPDATA%\LayerPeek.Build\ffmpeg-source`, vedle cache buildu FFmpeg). Soubor v cache se připnutým SHA-256 se
   použije bez stahování; jinak se stáhne (HTTPS) do pracovní složky `tmp-…` v cache, ověří a přesune na místo. Při neshodě SHA-256 skript skončí chybou a stažené zahodí. **Cache se
   nikdy nemaže**; soubor se špatným hashem se nahradí.
2. **Krok balení `ffmpeg-source`** (oba OS, po smoke testu publish složky): běží jednou za OS, s posledním RID běhu
   (T-225), až jsou hotové publish složky všech RID. Z každé publish složky přečte knihovnu `avcodec-<n>.dll` /
   `libavcodec.<n>.dylib` a ověří, že je z připnutého zdroje (`FFmpeg version` v knihovně přesně `9.0.2`). Pak sbalí
   tarball, skript buildu OS a `README.txt` (řádek configure každého RID) do `dist/` (pracovní složka
   `ffmpeg-source-<os>` vedle `dist/`, po zabalení smazaná) a výsledek vypíše a porovná s očekávaným seznamem souborů.
   Výchozí build Windows (`win-x64` i `win-arm64`, od 2026-11-16 i build pro nahrání) má v `README.txt` oba řádky; build `-Runtime win-x64` jen řádek `win-x64`.
3. Soubor je jako ostatní kroky v `build-info.json` (krok `ffmpeg-source`, i v záznamu `pending` před podpisem, takže
   `-SignOnly` z T-197 funguje beze změny) a v `SHA256SUMS-<os>.txt`; `release-upload.ps1` ho nahraje v první dávce
   (není to značka OS) a `release-publish.ps1` ho zkontroluje proti součtům. Seznam povolených názvů nahrání nemá:
   nahrává se přesně to, co je v `build-info.json`.

**Potřeba:** síť jen při prvním běhu dané verze FFmpeg (~12 MB z ffmpeg.org) a `tar`, který zapisuje xz (macOS
`/usr/bin/tar`; Windows `System32\tar.exe`, ověření v T-125). Krok trvá pár sekund (tarball už je komprimovaný).

**Okno O aplikaci** má pod licencí a copyrightem drobným písmem větu „This software uses libraries from the FFmpeg
project under the LGPLv3 or later.“ (přeložená do všech 8 jazyků) a odkaz „FFmpeg source code of this version“ na
`https://github.com/pfoltynek/LayerPeek/releases/download/v<verze>/ffmpeg-source-<verze>-<os>.tar.xz`
(`ThirdPartyLicenses.FFmpeg.SourceArchiveUri`, OS podle běžící aplikace). Šablona na ffmpeg.org uvádí „LGPLv2.1“;
oba buildy jsou ale `--enable-version3`, tedy LGPL 3 nebo novější, proto věta uvádí skutečnou verzi licence
(schváleno uživatelem 2026-11-12). Sestavení verze, která nebyla vydaná, má odkaz bez souboru (stejně jako odkaz na skript
buildu podle tagu).

**Text vydání na GitHubu** (rozhodnutí uživatele 2026-11-12, bod 9 kontrolního seznamu: věta na každé stránce se
stažením): `release-upload.ps1` při vytvoření konceptu připojí za sekci `CHANGELOG.md` oddělovač `---` a větu
„This software uses code of FFmpeg licensed under the LGPLv3 or later and its source can be downloaded here: …“
s odkazy na oba archivy vydání (`Get-ReleaseNotes`, `Get-FFmpegSourceNotice`; adresy
`https://github.com/<repozitář>/releases/download/v<verze>/ffmpeg-source-<verze>-windows.tar.xz` a `…-macos.tar.xz`,
repozitář podle `-Repo`). `-DryRun` celý text vypíše. Do balíčku Velopack jde dál jen sekce `CHANGELOG.md`; manifest
winget větu nedostává. README (EN i CZ) má stejnou větu v oddílu stažení. Vydání zveřejněná jen s jedním OS (`-AllowIncomplete`,
do 1.0.0) mají odkaz druhého OS nefunkční; od T-200 se jen s jedním OS nezveřejňuje.

**Nová verze FFmpeg:** `build/update-ffmpeg.ps1 -Update` připne nový tarball, který je zároveň zdrojem vydání; nic
dalšího se nepřipíná (postup v `docs/prerequisites.md`, „Nová verze FFmpeg“).

## Bod rozšíření pro instalátory (T-017, T-019)

`scripts/release-build.ps1` má explicitní seznam `$packagingSteps` (žádné hledání skriptů podle masky). Každý krok má `Name`, `Os` (pro které OS se spouští) a `Invoke` – scriptblock, který dostane kontext:

| Vlastnost | Význam |
|---|---|
| `Version` | verze z `Directory.Build.props` |
| `Runtime` | RID, např. `win-x64` |
| `Os` | `windows` / `macos` |
| `PublishDirectory` | hotová a smoke testem ověřená publish složka RID |
| `OutputDirectory` | `dist/`, sem krok uloží své soubory |

Krok vrátí cesty souborů, které vytvořil přímo v `dist/`. Skript je sám zapíše do `SHA256SUMS-<os>.txt` a `build-info.json`, takže je `release-upload.ps1` nahraje a `release-publish.ps1` zkontroluje bez další změny. Kroky dnes: `archive` (Windows, `.zip`), `installer` (Windows, T-017, Velopack), `app` (macOS, T-019: `LayerPeek.app`, `.dmg`, `.tar.gz`) a `ffmpeg-source` (oba OS, T-199: `ffmpeg-source-<verze>-<os>.tar.xz`). Logika kroku `installer` je ve funkcích `Get-VelopackPackArguments`, `Get-VelopackOutputMap`, `Copy-VelopackOutput` a `Get-VelopackVersionProblems`, kroku `app` ve funkcích `New-MacIcon`, `New-MacAppBundle`, `Get-VelopackMacPackArguments`, `Expand-ZipWithDitto`, `Get-MacSignatureProblems`, `Get-MacVelopackBundleProblems`, `Get-MacHardenedRuntimeProblems` a `New-MacDiskImage` modulu `ReleaseTools.psm1` (Pester testy). Build, publish a smoke test publish složky se tím nezměnily.

Poznámky pro navazující kroky:

- **Názvy souborů musí být v rámci jednoho běhu jedinečné** (všechny kroky zapisují do jednoho `dist/`, skript duplicitu odmítne). Velopack (T-017, `docs/velopack.md`) pojmenovává soubory podle kanálu (`releases.<RID>.json`, `RELEASES-<RID>`), ale `Setup.exe` / `Portable.zip` je potřeba při více RID přejmenovat (např. `LayerPeek-<verze>-<RID>-Setup.exe`).
- **Poznámky k vydání** pro krok (např. `vpk --releaseNotes`) vrací `Get-ChangelogSection` z `scripts/ReleaseTools/ReleaseTools.psm1`; krok je zapíše do dočasného souboru a ten po sobě smaže.
- Podpis a notarizace macOS (T-060) nahradí ad-hoc podpis v balicím kroku `app` (od T-058 podepisuje `vpk`: `--signAppIdentity`, `--notaryProfile` v `Get-VelopackMacPackArguments`).

## Balení pro macOS (T-019, T-058)

Krok `app` v `scripts/release-build.ps1` pro každý RID macOS (dnes jen `osx-arm64`), nad publish složkou, kterou už ověřil smoke test. Od T-058 (verze 0.3.1, rozhodnutí Q9 a) bundle podepisuje a balí `vpk` (Velopack, aktualizace v aplikaci):

1. **Ikona:** `LayerPeek.icns` (`iconutil`) z varianty ikony pro macOS (T-098): mřížka Applu – plátno 1024 px, tělo 824 × 824 px uprostřed (okraj 100 px) se spojitě zaoblenými rohy (poloměr 185,4 px) a jemným stínem, ve kterém je jen okraj kolem těla. Zdroje jsou `src/LayerPeek.App/Assets/AppIcon/app-icon-macos.svg` (detail) a `app-icon-macos-16.svg` / `app-icon-macos-32.svg` (zjednodušená varianta z T-025 na celých pixelech: tělo 12 px, resp. 26 px, bez stínu), vykreslené do `app-icon-macos-1024.png`, `app-icon-macos-16.png` a `app-icon-macos-32.png` (nejsou `AvaloniaResource`). 16 a 32 px (16 bodů @1x a @2x, 32 bodů @1x) se kopírují beze změny, od 64 px (32 bodů @2x) se zmenšují `sips` z 1024px předlohy (rozhodnuto 2026-10-29, T-019). Před `iconutil` `New-MacIcon` zkontroluje každý obrázek (`Get-MacIconImageProblems`: rozměr, tělo na mřížce ±1 px, v okraji nanejvýš stín); ikona, která mřížku nedodrží (např. dlaždice přes celé plátno jako u Windows), krok zastaví. Windows (`app-icon.ico`, exe, okna) má dál dlaždici přes celé plátno z `app-icon.svg` / `app-icon-small.svg`. Po změně SVG se PNG vygenerují znovu jedním příkazem `swift tools/render-macos-icon.swift` (systémový SVG renderer macOS, stačí Xcode Command Line Tools; `--out <složka>` zapíše jinam). Výstup je deterministický: Pester na macOS ověřuje, že skript z SVG vytvoří přesně commitnuté PNG.
2. **Bundle** `LayerPeek.app`: `Contents/MacOS` = kód publish složky (exe a nativní knihovny, tedy soubory Mach-O, i knihovny FFmpeg z T-026) bez ladicích symbolů (`.pdb`, `.dSYM`), `Contents/Resources` = ostatní soubory publish složky (`FFmpeg-LICENSE.txt`; `codesign` data v `Contents/MacOS` odmítne jako nepodepsaný kód), `Contents/Info.plist` = šablona `build/macos/Info.plist` s verzí z `Directory.Build.props` (`@VERSION@`), `Contents/Resources/LayerPeek.icns`, `Contents/PkgInfo` a (T-097) `Contents/Resources/<jazyk>.lproj/InfoPlist.strings` z `build/macos/Localizations/` (přeložený text dialogu „Místní síť“ pro každý jazyk aplikace). Lokalizace se zkontrolují už před sestavením (`Get-MacLocalizationProblems`: každý jazyk z `AppLanguages.Supported`, neprázdný `NSLocalNetworkUsageDescription`, anglický shodný s `Info.plist`, `CFBundleLocalizations`) a hotový bundle před podpisem projde `plutil -lint` (`Info.plist` a každý `.strings`, `Get-MacPlistLintProblems`).
   - `CFBundleIdentifier` je v šabloně (jediné místo; `AppEnvironment.ProductionBundleIdentifier` musí být stejný, hlídá unit test): **`io.github.pfoltynek.LayerPeek`, trvalý** (rozhodnuto 2026-10-29). Podle něj macOS vede předvolby, Login Item a oprávnění notifikací (T-020), proto se už nesmí měnit.
   - `LSUIElement` = `true`: aplikace nemá ikonu v Docku ani vlastní menu aplikace, jen ikonu v menu baru. Aplikace navíc nastavuje `MacOSPlatformOptions.ShowInDock = false`, protože Avalonia při startu jinak přepne aplikaci na běžnou s ikonou v Docku.
   - `LSMinimumSystemVersion` = `14.0` (minimum AOT binárky .NET 11, `LC_BUILD_VERSION minos`).
3. **Kontrola knihoven (T-026):** v bundlu musí být knihovny FFmpeg (`Contents/MacOS/libavformat.63.dylib`, `libavcodec.63.dylib`, `libavutil.61.dylib`, `libswscale.10.dylib`) a `Contents/Resources/FFmpeg-LICENSE.txt` a žádný soubor Mach-O nesmí načítat knihovnu z `/opt/homebrew` ani `/usr/local` (`otool -L`, kromě vlastního install name; `libAvaloniaNative` ho má `/usr/local/lib/…`). Jinak krok skončí chybou (`Get-MacBundleLibraryProblems`).
4. **`vpk pack` hotového bundlu (T-058):** `dotnet vpk --skip-updates pack --packId LayerPeek --packVersion <verze> --packDir <…>/LayerPeek.app --mainExe LayerPeek --packTitle LayerPeek --packAuthors … --channel osx-arm64 -r osx-arm64 --releaseNotes <úsek CHANGELOG.md> --delta None --noInst --signAppIdentity - --signEntitlements build/macos/LayerPeek.entitlements` (sestaví `Get-VelopackMacPackArguments`). `vpk` doplní `Contents/MacOS/UpdateMac` (updater) a `Contents/Resources/sq.version` (manifest, v `Contents/MacOS` jako odkaz) a podepíše bundle ad hoc `codesign --deep` s hardened runtime a přesně entitlements T-108 (ověřeno, [velopack.md](velopack.md), bod 10). Do `dist/` jde `.nupkg` a feed `osx-arm64` (`Copy-VelopackOutput`), `Portable.zip` zůstane v pracovní složce a rozbalí se (`ditto`): jeho `LayerPeek.app` je to, co dostanou uživatelé. Ta projde `codesign --verify --deep --strict`, `plutil -lint`, kontrolou manifestu (`Get-MacVelopackBundleProblems`: `UpdateMac`, `sq.version` s id, verzí a kanálem) a `Get-MacHardenedRuntimeProblems`; stejnou kontrolu hardened runtime dostane i `.app` uvnitř `.nupkg` (to instaluje aktualizace). `.pkg` se nevydává (`--noInst`, rozhodnuto 2026-10-29). Pravidla podpisu (do T-058 je dělal skript sám, `Invoke-MacAdHocSign`) zůstávají: **ad-hoc podpis zevnitř ven** (`codesign --force --sign - --timestamp=none`): nejdřív každá vnořená Mach-O knihovna (`libAvaloniaNative`, `libSkiaSharp`, `libHarfBuzzSharp`, knihovny FFmpeg; podpis vydavatele nebo linkeru se nahradí), nakonec celý bundle (podepíše hlavní exe a zapečetí `Info.plist` a prostředky). **Hardened runtime** (T-108, `--options runtime`) u každého souboru Mach-O, aby dyld ignoroval `DYLD_INSERT_LIBRARIES` a ostatní proměnné `DYLD_*` (jinak by malware pod stejným uživatelem mohlo vložit knihovnu a číst hesla z Keychainu). Hlavní exe má entitlements z `build/macos/LayerPeek.entitlements`, jen `com.apple.security.cs.disable-library-validation`: library validation přijímá jen knihovny se stejným Team ID, ad-hoc podpis žádné nemá, a bez výjimky by dyld odmítl i vlastní knihovny bundlu. `allow-jit` ani `allow-unsigned-executable-memory` Native AOT nepotřebuje a `allow-dyld-environment-variables` se záměrně nedává (ověření a zbytkové riziko v T-108). Hned potom `codesign --verify --deep --strict` a `Get-MacHardenedRuntimeProblems` (příznak runtime `0x10000` u každého Mach-O, u hlavního exe přesně tyto entitlements; ručně `codesign -d --entitlements - --verbose=4 LayerPeek.app`).
5. **Smoke test bundlu:** `aot-smoke.ps1 -PublishDir LayerPeek.app/Contents/MacOS` (od T-058 `.app` z `Portable.zip`) spustí exe uvnitř bundlu (tedy s bundle identifikátorem) a ověří, že běží bez ikony v Docku (`lsappinfo`, typ `UIElement`). Bundle se nesmí změnit a podpis musí dál platit. Soubory ke stažení (`.dmg`, `.tar.gz`) a start bez prostředí vývojáře (`aot-smoke.ps1 -CleanEnvironment`: `env -i`, bez `DOTNET_ROOT` a Homebrew) kontroluje až `tools/macos-verify.ps1` (`mac.release-bundle`, T-217) nad hotovým výstupem.
6. **`.tar.gz`:** `LayerPeek.app` z `Portable.zip` (cesty `./LayerPeek.app/…`, odkaz `sq.version` zůstane odkazem), bez `._*`, `.dSYM` a `.pdb`.
7. **`.dmg`:** `hdiutil create -fs HFS+ -format UDZO` ze složky s kopií aplikace (`ditto`, podpis zůstane) a odkazem `Applications` → `/Applications` (rozhodnuto 2026-10-29), název svazku `LayerPeek`, pak `hdiutil verify`. Skript nic nepřipojuje.

Velopack pro macOS od 0.3.1 (T-058): každá `.app` od `vpk` (z `.dmg`, `.tar.gz` i `Portable.zip`) se umí aktualizovat sama, pokud běží na trvalém místě (ne přeložená App Translocation, ne z připojeného `.dmg`; jinak záložka Spuštění a aktualizace řekne „Přesuňte LayerPeek do složky Aplikace“). **Důsledek pro 0.3.0:** `.app` z vydání 0.3.0 (vlastní kroky skriptu, bez Velopacku) aktualizace nedostane; verzi 0.3.1 si uživatel jednou nainstaluje ručně (přetáhne novou `LayerPeek.app` z `.dmg` přes starou). Věta je v `CHANGELOG.md` u 0.3.1, a tedy i v textu vydání.

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
- **Nahrání:** soubory jdou na GitHub jako ostatní (`release-upload.ps1`, `gh`), `vpk upload` se nepoužívá. Aplikace hledá aktualizace v GitHub Releases bez tokenu (`GithubSource`, kanál = RID instalace). Koncept vydání anonymní klient nevidí, aktualizace se nabídne až po zveřejnění. Repozitář je veřejný (od 2026-10-30). U soukromého repozitáře by kontrola skončila chybou 404 a aplikace by ji brala jako „žádná aktualizace“.
- **Předběžné verze:** volba „Nabízet předběžné verze“ v nastavení (záložka Spuštění a aktualizace). Dokud ji uživatel nezmění, je na `0.x` zapnutá a od `1.0` vypnutá (rozhodnuto 2026-10-24). Nabídku aktualizace v aplikaci doplní T-058.

### Co uživatel uvidí

1. **Stažení v prohlížeči:** Edge může nepodepsaný `Setup.exe` s malou reputací označit jako neběžně stahovaný soubor a chtít potvrzení „Ponechat“. Neověřeno, ověřit při prvním vydání a doplnit sem.
2. **SmartScreen při spuštění** (ověřeno ručně v T-056 se značkou Mark-of-the-Web): „Systém Windows ochránil váš počítač“ jen s tlačítkem „Nespouštět“. Po „Další informace“ se ukáže „Vydavatel: Neznámý vydavatel“ a tlačítko „Přesto spustit“. Důvod: binárky ani instalátor nejsou podepsané (rozhodnuto 2026-10-21). Popis je v README a patří i do textu vydání.
3. **Instalace** bez dotazu UAC pro aktuálního uživatele do `%LOCALAPPDATA%\LayerPeek` (exe v `current\LayerPeek.exe`), zástupce jen ve Start menu (bez plochy), položka v Nastavení → Aplikace. Po instalaci se aplikace spustí (kromě tiché instalace `--silent`).
   - **Po `.zip` verzi** (logy v `%LOCALAPPDATA%\LayerPeek\logs`, rozhodnuto 2026-10-24) složka není prázdná a `Setup.exe` se nejdřív zeptá dialogem „přepsat/opravit“ (*Repair* / *Open install directory* / *Cancel*). *Cancel* nic nezmění a nic nenainstaluje. *Repair* nainstaluje a dosavadní obsah složky, tedy **logy `.zip` verze, smaže**. `--silent` odpoví *Repair* bez dotazu. Podrobnosti a ověření v [velopack.md](velopack.md#neprázdná-instalační-složka-logy-zip-verze).
4. **Spouštění po přihlášení:** volba v nastavení (záložka Spuštění a aktualizace), ve výchozím stavu zapnutá (zapne ji instalace, aktualizace volbu uživatele nemění; rozhodnuto 2026-10-26) a **jen v nainstalované verzi**. Ve `.zip` je vidět neaktivní s vysvětlením (rozhodnuto 2026-10-24). Aplikace vytvoří vlastního zástupce `LayerPeek.lnk` ve složce Po spuštění (`%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup`). Jeho cíl leží v instalační složce, takže ho Velopack při odinstalaci smaže.
5. **Odinstalace** (Nastavení → Aplikace, nebo `Update.exe --uninstall --silent`) ukončí běžící aplikaci a smaže instalační složku **včetně logů** (`%LOCALAPPDATA%\LayerPeek\logs`, přijato 2026-10-24) a zástupce. Hook odinstalace smaže oznámení aplikace z centra oznámení, registraci aktivátoru toastů, nastavení oznámení aplikace ve Windows, volbu „zobrazit na hlavním panelu“ u ikony aplikace a zástupce ve Startup.
   - **Zůstane:** nastavení `%APPDATA%\LayerPeek` a hesla ve Správci pověření (rozhodnuto 2026-10-21), log Velopacku `%LOCALAPPDATA%\velopack\velopack_LayerPeek.log` a `velopack.log` (Velopack do něj píše i po hooku, **známý zbytek**, přijato 2026-10-24) a záznamy, které si Windows vede o každém spuštěném programu (`MuiCache`, `AppListBackup`, položky v `NotifyIconSettings`).

### Ověření instalátoru (E2E)

Skutečnou tichou instalaci a odinstalaci na vývojovém stroji dělá E2E test `VelopackInstallerTests` (druh zásahu `Installer`), jen přes `tools/run-e2e.ps1` s varováním a žurnálem obnovy:

```powershell
pwsh scripts/release-build.ps1 -SkipFeedSignature  # vytvoří artifacts/release/<verze>/dist/LayerPeek-<verze>-win-x64-Setup.exe (bez klíče, jen místně)
pwsh tools/run-e2e.ps1 -Class '*VelopackInstallerTests'
```

Test zálohuje vývojového zástupce `LayerPeek.lnk`, obsah složky `%LOCALAPPDATA%\LayerPeek` (logy, kopie v `%LOCALAPPDATA%\LayerPeek.E2E.InstallerBackup`), klíče `Uninstall\LayerPeek`, `CLSID\{EC47413C-984D-447A-866A-5F780E408DE7}` a `Notifications\Settings\LayerPeek`, zástupce ve Startup a položky ikony v `NotifyIconSettings` a po testu je vrátí (i po pádu, viz [troubleshooting.md](troubleshooting.md)). Bez složky s logy si ji test vytvoří, aby ověřil i instalaci po `.zip` verzi. Ověří, že zrušený dialog „přepsat/opravit“ nic nenainstaluje a logy nechá, a že tichá instalace logy smaže a nainstaluje bez UAC. Dále ověří AUMID a `ToastActivatorCLSID` zástupce, registraci COM `LocalServer32` a to, že po odinstalaci nic z toho nezůstane. Přeskočí se, když instalátor chybí, když je LayerPeek opravdu nainstalovaný, když nějaký LayerPeek běží (jeho log by byl otevřený) a když testy běží jako správce. Odinstalace vymaže oznámení LayerPeeku z centra oznámení a ta vrátit nejde, varování skriptu to uvádí (přijato 2026-10-24).

## Publikace do winget (T-059)

Aplikace se dá nainstalovat a aktualizovat příkazem `winget` z komunitního repozitáře manifestů [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs). Manifest odkazuje na `LayerPeek-X.Y.Z-win-x64-Setup.exe` ve zveřejněném vydání na GitHubu; winget ho stáhne odtud a ověří jeho SHA256. Uzel `arm64` (`LayerPeek-X.Y.Z-win-arm64-Setup.exe`) dostane manifest jen u vydání, které ten soubor obsahuje (rozhodnutí uživatele 2026-11-12, otázka 3 v T-226), tedy od prvního vydání po 1.0.0, které `win-arm64` nahraje (rozhodnutí uživatele 2026-11-16). První odeslání (T-059, verze 1.0.0) je jen `x64`.

```powershell
winget install --id 3DPrint.LayerPeek     # nebo: winget install layerpeek
winget upgrade --id 3DPrint.LayerPeek
winget uninstall --id 3DPrint.LayerPeek
```

### Rozhodnutí (uživatel 2026-11-11)

- Začíná se verzí **1.0.0** (první stabilní, s .NET 11 GA). Repozitář je s 1.0.0 veřejný, první odeslání proběhne hned po zveřejnění 1.0.0. Verze `0.x` se do winget neposílají (skripty je odmítnou).
- Identifikátor balíčku **`3DPrint.LayerPeek`**, vydavatel (`Publisher`) **`Petr Foltýnek`**.
- Odeslání je **volitelný krok skriptu vydání**: po zveřejnění se skript zeptá, zda spustit odeslání. Token je z `gh auth token` jen za běhu, nikdy se neukládá, nevypisuje ani nepředává na příkazové řádce.
- Doplněno 2026-11-11 (odpovědi na otázky v T-059):
  - **Token:** pro odeslání **dočasné `gh auth login`** (OAuth v prohlížeči) a po odeslání `gh auth logout`, pak zase obvyklé přihlášení (T-180: `GH_TOKEN`). Postup v části [Token](#token).
  - **`winget upgrade`** přes běžící instalaci: zapnutí autostartu při přeinstalaci opravil úkol **T-201** (hook instalace respektuje volbu uloženou v nastavení), zbytek chování je zdokumentovaný v části [Velopack a `winget upgrade`](#velopack-a-winget-upgrade) a v příručkách (doporučená je aktualizace v aplikaci).
  - Repozitář `pfoltynek/LayerPeek` **už je veřejný** (ověřeno `gh repo view`), nic dalšího se nečeká.
  - **Microsoft CLA** podepíše uživatel svým účtem u prvního PR.
  - **Telemetrie `wingetcreate` je vypnutá:** skript ji před každým spuštěním `wingetcreate` vypne v jeho `settings.json` (viz [Telemetrie `wingetcreate`](#telemetrie-wingetcreate)).
  - Žádná další volitelná pole manifestu (`ElevationRequirement`, `InstallLocation`, `PrivacyUrl`, `RequireExplicitUpgrade`).

### Pravidla winget-pkgs (ověřeno 2026-11-11)

| Pravidlo | Zdroj | U nás |
|---|---|---|
| Identifikátor obvykle `Publisher.Package`, rozlišuje velikost písmen, složka `manifests/<malé první písmeno>/<segmenty>/<verze>` | [Authoring.md](https://github.com/microsoft/winget-pkgs/blob/master/doc/Authoring.md), [schéma version 1.28.0](https://github.com/microsoft/winget-pkgs/blob/master/doc/manifest/schema/1.28.0/version.md) | `3DPrint.LayerPeek` → `manifests/3/3DPrint/LayerPeek/<verze>`; v indexu winget (2026-11-11, 16 333 balíčků) žádný balíček `3DPrint.*` není |
| `Publisher` má odpovídat hodnotě `Publisher` v klíči Uninstall (Aplikace a funkce), `PackageName` hodnotě `DisplayName`, verze `DisplayVersion`; jinak `AppsAndFeaturesEntries` | [schéma defaultLocale 1.28.0](https://github.com/microsoft/winget-pkgs/blob/master/doc/manifest/schema/1.28.0/defaultLocale.md), [CONTRIBUTING.md](https://github.com/microsoft/winget-pkgs/blob/master/CONTRIBUTING.md) („Testing“) | Velopack 1.2.161 zapisuje `Publisher` = `--packAuthors` (`Petr Foltýnek`), `DisplayName` = `--packTitle` (`LayerPeek`), `DisplayVersion` = verze balíčku, klíč `HKCU\…\Uninstall\LayerPeek` ([registry.rs](https://github.com/velopack/velopack/blob/1.2.161/src/bins/src/windows/registry.rs)). Shoduje se, `AppsAndFeaturesEntries` netřeba; Pester test hlídá shodu s `Get-VelopackSettings` |
| `Moniker` je volitelný; jen když ho má jediný balíček, `install`/`upgrade` podle něj balíček najdou | [schéma defaultLocale 1.28.0](https://github.com/microsoft/winget-pkgs/blob/master/doc/manifest/schema/1.28.0/defaultLocale.md) | `layerpeek`; v indexu winget (2026-11-11) ho nemá žádný balíček |
| Instalace bez zásahu uživatele (výchozí režim „silent with progress“), podporovat `silent` i `silentWithProgress` | [Policies.md](https://github.com/microsoft/winget-pkgs/blob/master/doc/Policies.md), [schéma installer 1.28.0](https://github.com/microsoft/winget-pkgs/blob/master/doc/manifest/schema/1.28.0/installer.md) (`InstallModes`, `InstallerSwitches`) | Velopack `Setup.exe`: `-s, --silent` „Hides all dialogs and answers 'yes' to all prompts“, `-l, --log <FILE>` ([setup.rs 1.2.161](https://github.com/velopack/velopack/blob/1.2.161/src/bins/src/setup.rs)) |
| Jen vícesouborový manifest (version + defaultLocale + installer) se záhlavím `# yaml-language-server: $schema=…`, schéma podle šablony PR (dnes **1.28**) | [FirstContribution.md](https://github.com/microsoft/winget-pkgs/blob/master/doc/FirstContribution.md), [šablona PR](https://github.com/microsoft/winget-pkgs/blob/master/.github/PULL_REQUEST_TEMPLATE.md) | tři soubory v `build/winget`, schéma 1.28.0 |
| Jeden PR = jedna verze jednoho balíčku, jen soubory manifestu; před odesláním `winget validate --manifest` a `winget install --manifest` | [FirstContribution.md](https://github.com/microsoft/winget-pkgs/blob/master/doc/FirstContribution.md) | skript posílá jednu verzi; ověření první verze ručně (níže) |
| URL instalátoru z oficiálního místa vydavatele, nejlépe jiná pro každou verzi | [Policies.md](https://github.com/microsoft/winget-pkgs/blob/master/doc/Policies.md) („Manifest URLs“), [Microsoft Learn – zásady 1.1.4](https://learn.microsoft.com/windows/package-manager/package/windows-package-manager-policies) | `https://github.com/pfoltynek/LayerPeek/releases/download/vX.Y.Z/LayerPeek-X.Y.Z-win-x64-Setup.exe` |
| `LicenseUrl` u open source má odkazovat na licenci dané verze; nejvýš 16 tagů | [schéma defaultLocale 1.28.0](https://github.com/microsoft/winget-pkgs/blob/master/doc/manifest/schema/1.28.0/defaultLocale.md), [zásady 1.1.2](https://learn.microsoft.com/windows/package-manager/package/windows-package-manager-policies) | `…/blob/vX.Y.Z/LICENSE`, 8 tagů |
| Instalátor projde bezpečnostními kontrolami validace; balíček označený kterýmkoli skenem nejde přijmout | [Policies.md](https://github.com/microsoft/winget-pkgs/blob/master/doc/Policies.md), [Validation.md](https://github.com/microsoft/winget-pkgs/blob/master/doc/Validation.md) | nepodepsaný instalátor (rozhodnuto 2026-10-21): riziko, výsledek první validace zapsat do T-059 |
| PR do winget-pkgs vyžaduje podepsanou Microsoft CLA | [šablona PR](https://github.com/microsoft/winget-pkgs/blob/master/.github/PULL_REQUEST_TEMPLATE.md) | podepisuje uživatel u prvního PR (bot se ozve v PR) |
| Předběžné verze | v dokumentaci winget-pkgs ani na Microsoft Learn jsme pravidlo nenašli | neřešíme: posíláme jen stabilní `X.Y.Z` od 1.0.0 |

`wingetcreate` (MIT, [microsoft/winget-create](https://github.com/microsoft/winget-create)): `update` potřebuje už existující balíček a stejný počet URL jako uzlů instalátoru; architekturu bere nejdřív z URL (`…-win-x64-…`), proto ji nepřepisujeme (`|x64`). Token čte nejdřív z proměnné `WINGET_CREATE_GITHUB_TOKEN`, pak ze Správce pověření ([TokenHelper.cs](https://github.com/microsoft/winget-create/blob/main/src/WingetCreateCLI/TokenHelper.cs)); `--token` na příkazové řádce dokumentace sama nedoporučuje („may result in the token being logged“, [doc/token.md](https://github.com/microsoft/winget-create/blob/main/doc/token.md)). **Fine-grained tokeny nepodporuje** (tamtéž), potřebuje klasický token nebo přihlášení OAuth s přístupem k veřejným repozitářům (forkuje `microsoft/winget-pkgs`).

### Manifest (`build/winget`)

Tři revidované šablony (UTF-8 bez BOM), ze kterých skript vytvoří manifest první verze. Zástupné hodnoty `@VERSION@`, `@RELEASE_DATE@` (datum zveřejnění vydání na GitHubu, UTC) a `@INSTALLER_SHA256@` (SHA256 instalátoru, jak ho hlásí GitHub u souboru vydání) doplní `ConvertTo-WingetManifests`.

| Soubor | Obsah |
|---|---|
| `3DPrint.LayerPeek.yaml` | `version`: identifikátor, verze, `DefaultLocale: en-US` |
| `3DPrint.LayerPeek.installer.yaml` | `InstallerType: exe`, `Scope: user`, `InstallModes` interactive/silent/silentWithProgress, `Silent` a `SilentWithProgress`: `--silent`, `Log: --log "<LOGPATH>"`, `UpgradeBehavior: install`, `ProductCode: LayerPeek` (název klíče Uninstall), `ReleaseDate`, instalátor `x64` a mezi řádky `# @ARM64_BEGIN@` a `# @ARM64_END@` uzel `arm64` (`@INSTALLER_SHA256_ARM64@`, T-226): když vydání má `…-win-arm64-Setup.exe`, `ConvertTo-WingetManifests` značky odstraní a doplní SHA256, jinak odstraní celý blok (manifest jen `x64`) |
| `3DPrint.LayerPeek.locale.en-US.yaml` | `Publisher: Petr Foltýnek`, `PackageName: LayerPeek`, `License: GPL-3.0-only`, `LicenseUrl` a `ReleaseNotesUrl` dané verze, `Moniker: layerpeek`, tagy, popis s větou, že nejde o oficiální produkt Prusa Research, odkaz na příručku |

`InstallLocation` (`--installto`) záměrně chybí: instalace jinam než do `%LOCALAPPDATA%\LayerPeek` se neověřovala. Vykreslené soubory 1.0.0 prošly kontrolou proti JSON schématům 1.28.0 z `microsoft/winget-cli` (na Macu, Python `jsonschema`, 2026-11-11); `winget validate` je potřeba na Windows.

Další verze se posílají `wingetcreate update`: vezme manifest předchozí verze z winget-pkgs a změní jen verzi, URL, SHA256, `ReleaseNotesUrl` a `ReleaseDate`. Ostatní pole (i `LicenseUrl` s tagem 1.0.0) zůstanou z předchozí verze; změna šablon se proto do winget dostane jen ručně (PR s celým manifestem nebo `wingetcreate update --interactive`).

**Windows ARM64 ve winget (T-226):** vydání se souborem `…-win-arm64-Setup.exe` dostane `wingetcreate update` se dvěma URL (x64 a arm64, architekturu bere `wingetcreate` z URL), vydání bez něj s jednou. Protože `update` potřebuje stejný počet URL jako uzlů předchozího manifestu, **první vydání s `win-arm64`** (první vydání po 1.0.0; předchozí verze ve winget-pkgs, 1.0.0, má jen `x64`) projde jen ručně: `pwsh scripts/release-winget.ps1 -Version X.Y.Z -OutputDirectory <složka>` (manifest z šablon s uzly `x64` a `arm64`), `winget validate --manifest <složka>` a odeslání té složky `wingetcreate submit <složka>` (token jako v části [Token](#token)). Když automatické `update` v té situaci selže, skript to v chybě vypíše. Totéž obráceně: vydání bez `…-win-arm64-Setup.exe` po vydání s ním (předchozí manifest má dva uzly) `update` s jednou URL odmítne; skript poradí ruční odeslání manifestu jen `x64` (soubory `win-arm64` do zveřejněného vydání doplnit nejde, T-180). Obě varianty šablony prošly 2026-11-15 `winget validate` (winget 1.30, schéma 1.28.0); `winget install --manifest` na ARM64 je v T-227.

### Postup: první verze 1.0.0 (Windows)

Po zveřejnění 1.0.0 včetně souborů Windows (`release-publish.ps1`), v čistém checkoutu tagu:

```powershell
git switch --detach v1.0.0
pwsh scripts/release-winget.ps1 -Version 1.0.0 -OutputDirectory $env:TEMP\winget-1.0.0   # jen zapíše manifest (SHA256 z GitHubu)
winget validate --manifest $env:TEMP\winget-1.0.0
winget settings --enable LocalManifestFiles                       # jednou, jako správce
winget install --manifest $env:TEMP\winget-1.0.0                  # tichá instalace bez dotazů a bez UAC
```

Kontrola po instalaci: Nastavení → Aplikace ukazuje **LayerPeek**, vydavatel **Petr Foltýnek**, verze **1.0.0**; aplikace se po tiché instalaci sama nespustí (Velopack `--silent`), zástupce je ve Start menu a spouštění po přihlášení je zapnuté (hook instalace). Pak odinstalace (`winget uninstall --manifest $env:TEMP\winget-1.0.0`, nebo v Nastavení → Aplikace) a kontrola, že zmizela instalační složka i položka v Aplikacích. Když je vše v pořádku:

```powershell
pwsh scripts/release-winget.ps1 -Version 1.0.0 -DryRun   # plán: "first version", příkaz wingetcreate bez tokenu
pwsh scripts/release-winget.ps1 -Version 1.0.0           # winget validate, potvrzení, wingetcreate submit → PR
git switch develop
```

Před oběma příkazy se pro odeslání přihlas podle části [Token](#token) (dočasné `gh auth login`) a po odeslání se odhlas (`gh auth logout`). Skript ověří oprávnění tokenu `gh` hned na začátku, takže s nevhodným tokenem (fine-grained `GH_TOKEN` podle T-180) skončí ještě před plánem a vypíše přesný postup přihlášení.

Skript vytvoří manifest v dočasné složce (po odeslání ji smaže), spustí `winget validate`, zeptá se `y/N`, teprve pak přečte token (`gh auth token`), vypne telemetrii `wingetcreate` a spustí `wingetcreate submit --prtitle "New package: 3DPrint.LayerPeek version 1.0.0" <složka>`. `wingetcreate` otevře PR v prohlížeči. V PR: **podepsat Microsoft CLA** (bot se ozve v PR; podepisuje uživatel svým účtem, souhlas 2026-11-11, jen u prvního PR), sledovat štítky validace ([ValidationFailureGuide.md](https://github.com/microsoft/winget-pkgs/blob/master/doc/ValidationFailureGuide.md)) a výsledek první validace (Defender/SmartScreen u nepodepsaného instalátoru, ruční kontrola) zapsat do T-059.

### Postup: další verze (volitelný krok vydání)

`release-publish.ps1` po zveřejnění (i spuštěný z `release.ps1`) vypíše řádek `winget: …`:

| Situace | Co se stane |
|---|---|
| verze `0.x` | `winget: 0.x is a pre-release, it is not submitted` – nic dalšího |
| vydání jiného repozitáře (`-Repo`, zkušební vydání) | nic se neodesílá |
| vydání nemá `Setup.exe` (zveřejněné bez Windows před T-200) | `winget: the release has no …-Setup.exe, so this version is not submitted to winget` (soubor už nejde doplnit) |
| běží na macOS | příkaz `pwsh scripts/release-winget.ps1 -Version X.Y.Z` pro Windows |
| Windows, stabilní verze, `Setup.exe` ve vydání | otázka `Submit LayerPeek X.Y.Z to winget (microsoft/winget-pkgs) now? [y/N]`; po `y` stejný postup jako `release-winget.ps1`: výpis plánu a druhé potvrzení před otevřením PR |

Selhání odeslání vydání nerozbije (zveřejnění skončí s kódem 0), skript vypíše chybu a příkaz pro opakování. Samostatně (např. později): `pwsh scripts/release-winget.ps1 -Version X.Y.Z [-DryRun]`. Balíček už ve winget-pkgs je, takže se volá `wingetcreate update 3DPrint.LayerPeek --version X.Y.Z --urls <Setup.exe> --release-notes-url <vydání> --release-date <datum> --prtitle "Update: 3DPrint.LayerPeek to X.Y.Z" --submit`.

### Token

- Až po potvrzení skript zavolá `gh auth token` a výstup si nechá v proměnné. `wingetcreate` ho dostane jen v proměnné prostředí `WINGET_CREATE_GITHUB_TOKEN` tohoto procesu po dobu svého běhu; pak se vrátí předchozí hodnota. Na příkazové řádce (seznam procesů, historie) ani v souboru nikdy není.
- Výstup `wingetcreate` (stdout i stderr) skript vypisuje řádek po řádku a token v něm nahradí `***`. Pester testy ověřují, že token není ve výstupu, v argumentech ani v prostředí po běhu.
- **Jaký token (rozhodnutí uživatele 2026-11-11): dočasné `gh auth login` (OAuth v prohlížeči) jen pro odeslání, pak `gh auth logout`.** `wingetcreate` potřebuje klasický token nebo přihlášení OAuth se scope **`public_repo`** („to allow access to public repositories“; volitelně `delete_repo` jen pro smazání forku po neúspěšném PR – nepotřebujeme), fine-grained tokeny nepodporuje ([doc/token.md](https://github.com/microsoft/winget-create/blob/main/doc/token.md), [issue #595](https://github.com/microsoft/winget-create/issues/595)); vlastní přihlášení OAuth `wingetcreate` žádá scope `repo` ([GitHubOAuth.cs](https://github.com/microsoft/winget-create/blob/main/src/WingetCreateCLI/GitHubOAuth.cs)). `gh auth login` žádá výchozí scopes `repo`, `read:org`, `gist`; `repo` zahrnuje `public_repo`, nic dalšího se přidávat nemusí.
- **Kontrola před plánem:** skript zjistí scopes tokenu, který `gh` používá, z hlavičky `X-OAuth-Scopes` odpovědi `gh api --include user` (token sám nečte, posílá ho `gh`). Bez hlavičky (fine-grained token, např. `GH_TOKEN` podle T-180, nebo token aplikace GitHubu), bez `public_repo`/`repo` nebo s odpovědí 401 skript odmítne pokračovat (i v `-DryRun`) a vypíše postup níže; v něm je i `Remove-Item Env:GH_TOKEN`, když je `GH_TOKEN` (nebo `GITHUB_TOKEN`) v terminálu nastavený – `gh` ho upřednostní před přihlášením a `gh auth login` s ním odmítne běžet. Druhá kontrola po potvrzení: `gh auth token` musí vrátit token a nesmí být `github_pat_…`.
- **Postup (Windows, PowerShell, jen v tomto terminálu):**
  ```powershell
  Remove-Item Env:GH_TOKEN -ErrorAction SilentlyContinue      # jen když se používá GH_TOKEN (T-180); stejně GITHUB_TOKEN
  gh auth login --hostname github.com --web --git-protocol https
  # OAuth v prohlížeči (jednorázový kód); na „Authenticate Git with your GitHub credentials?“ odpovědět No
  pwsh scripts/release-winget.ps1 -Version X.Y.Z               # nebo nabídka po zveřejnění (release-publish.ps1 / release.ps1)
  gh auth logout --hostname github.com
  $env:GH_TOKEN = Read-Host -MaskInput 'GH_TOKEN'               # obvyklé přihlášení podle T-180, jen když se dál vydává
  ```
  Po skutečném odeslání skript sám připomene `gh auth logout`. `gh auth logout` smaže přihlášení jen z počítače; OAuth token na GitHubu zůstane platný, dokud ho uživatel nezruší (GitHub → Settings → Applications → Authorized OAuth Apps → GitHub CLI → Revoke) – doporučeno, když se `gh` jinak nepoužívá. Klasický token se scope `public_repo` jen v `$env:GH_TOKEN` tohoto terminálu by fungoval také, ale uživatel zvolil OAuth.
- `wingetcreate token --store` se nepoužívá (token by zůstal ve Správci pověření). Pozor: když je token z proměnné neplatný, `wingetcreate` smaže svůj případný uložený token ze Správce pověření (`BaseCommand.cs`, `DeleteTokenCache`).

### Telemetrie `wingetcreate`

`wingetcreate` má podle své dokumentace telemetrii zapnutou ve výchozím stavu ([doc/settings.md](https://github.com/microsoft/winget-create/blob/main/doc/settings.md), README „Data/Telemetry“). Rozhodnutí uživatele 2026-11-11: **vypnout**. Ověřeno ve zdroji (2026-11-11):

- Nastavení je `"Telemetry": { "disable": true }` v `settings.json` ([UserSettings.cs](https://github.com/microsoft/winget-create/blob/main/src/WingetCreateCLI/UserSettings.cs), model `SettingsModel.cs`; názvy vlastností Newtonsoft porovnává bez ohledu na velikost písmen, chybějící sekce mají výchozí hodnoty).
- Umístění ([Common.cs](https://github.com/microsoft/winget-create/blob/main/src/WingetCreateCLI/Common.cs), `LocalAppStatePath`): zabalená aplikace MSIX (manifest `Microsoft.WingetCreate` 1.12.13.0 nabízí `msix` i `portable`; který z nich `winget install` vybere, jsem neověřil, proto skript počítá s oběma) → `%LOCALAPPDATA%\Packages\Microsoft.WindowsPackageManagerManifestCreator_8wekyb3d8bbwe\LocalState\settings.json`; nezabalený `wingetcreate.exe` → `%LOCALAPPDATA%\Microsoft\WindowsPackageManagerManifestCreator\settings.json`. Skutečnou cestu ukáže `wingetcreate info`.
- Telemetrie se čte na začátku **každého** spuštění (`Program.cs`) a první spuštění bez `settings.json` ho zapíše se zapnutou telemetrií. Proto se `wingetcreate` nemá spouštět dřív, než se telemetrie vypne (ani `wingetcreate --version`).

Skript (`Disable-WingetCreateTelemetry` v `ReleaseTools.psm1`) ji vypne po potvrzení a těsně před spuštěním `wingetcreate`, v souboru zabalené aplikace (když existuje složka balíčku) i nezabalené (když `wingetcreate` na `PATH` není alias `…\Microsoft\WindowsApps\wingetcreate.exe` nebo balíček chybí). Opakovaně bezpečné: už vypnutou telemetrii nemění, ostatní nastavení zachová, chybějící soubor i složku vytvoří (UTF-8 bez BOM). Soubor, který není objekt JSON, nepřepíše a odeslání odmítne (`wingetcreate` by takový soubor ignoroval a běžel s telemetrií). Ručně hned po instalaci `wingetcreate`:

```powershell
pwsh -NoProfile -Command "Import-Module ./scripts/ReleaseTools/ReleaseTools.psm1; Disable-WingetCreateTelemetry"
```

### Co `release-winget.ps1` odmítne

| Kontrola | Hláška (začátek) |
|---|---|
| verze `0.x` / ne `X.Y.Z` | `Version … is a pre-release (0.x). winget gets stable versions only` / `… is not a version in the form X.Y.Z` |
| jiný repozitář než `pfoltynek/LayerPeek` | `winget installs the releases of pfoltynek/LayerPeek only` |
| ne Windows (kromě `-OutputDirectory`) | `The winget submission runs on Windows only (wingetcreate and winget are Windows tools)` |
| `gh` chybí / nepřihlášený, `wingetcreate` chybí, u první verze chybí `winget` | `GitHub CLI (gh) is not installed` / `wingetcreate is not installed. Install it: winget install --id Microsoft.WingetCreate` |
| vydání neexistuje / je koncept / nemá `Setup.exe` / nedokončený soubor / bez SHA256 (i u `…-win-arm64-Setup.exe`, když ho vydání má) | `There is no release vX.Y.Z` / `… is still a draft` / `… has no LayerPeek-X.Y.Z-win-x64-Setup.exe yet` / `… is not completely uploaded` / `GitHub reports no SHA256 …` |
| otevřený PR pro stejnou verzi / první verze ještě nesloučená | `An open pull request for 3DPrint.LayerPeek X.Y.Z already exists` / `The package is not in winget yet and its pull request is still open` |
| GitHub neodpoví, zda balíček existuje (síť, limit; jen 404 znamená „nový“) | výjimka `gh api repos/microsoft/winget-pkgs/contents/manifests/3/3DPrint/LayerPeek failed` – nikdy druhý PR „New package“ |
| token `gh` bez scopes (fine-grained, aplikace GitHubu) / bez `public_repo` / odmítnutý (401); před plánem, i v `-DryRun` | `The token of gh has no OAuth scopes` / `The token of gh lacks the public_repo scope` / `GitHub does not accept the token of gh (HTTP 401` + postup přihlášení |
| žádný / fine-grained token (po potvrzení) | `gh auth token returned no token` / `The token of gh is a fine-grained personal access token` + postup přihlášení |
| `settings.json` `wingetcreate` není objekt JSON | `wingetcreate telemetry could not be switched off: … Nothing was submitted.` |
| `winget validate` první verze selže | `winget validate failed (exit code N); nothing was submitted` |
| `wingetcreate` skončí chybou | `wingetcreate failed with exit code N. Check the pull requests …` (u vydání s `arm64` i návod na ruční první odeslání, viz výše) |

### Velopack a `winget upgrade`

Ověřeno ve zdroji Velopacku 1.2.161 ([install.rs](https://github.com/velopack/velopack/blob/1.2.161/src/bins/src/commands/install.rs), [registry.rs](https://github.com/velopack/velopack/blob/1.2.161/src/bins/src/windows/registry.rs), [apply_windows_impl.rs](https://github.com/velopack/velopack/blob/1.2.161/src/bins/src/commands/apply_windows_impl.rs)); na Windows ještě neověřeno pokusem:

- **Aktualizace v aplikaci (Velopack)** po instalaci přes winget funguje stejně jako po ručním `Setup.exe` (winget spouští tentýž instalátor). Po aktualizaci Velopack přepíše klíč Uninstall novou `DisplayVersion`, takže `winget list` ukáže novou verzi a `winget upgrade` ji nenabízí znovu.
- **`winget upgrade`** (`UpgradeBehavior: install`, i v rámci `winget upgrade --all`) spustí nový `Setup.exe --silent` přes stávající instalaci. Velopack to bere jako přeinstalaci:
  - **násilně ukončí běžící aplikaci** (bez uložení stavu, jako ukončení ve Správci úloh),
  - celou složku `%LOCALAPPDATA%\LayerPeek` nahradí – **logy v `%LOCALAPPDATA%\LayerPeek\logs` se smažou** (stejně jako u instalace přes `.zip` verzi),
  - znovu vytvoří zástupce a spustí hook `--veloapp-install`; ten od T-201 **respektuje vypnuté spouštění po přihlášení** (volba uložená v nastavení, `AppSettings.StartAtLogin`): když ho uživatel vypnul, zůstane vypnuté, když ho měl zapnuté nebo ho nikdy neměnil, zapne ho (podrobnosti v `docs/velopack.md`, bod 3),
  - aplikaci po tiché instalaci **nespustí**; uživatel ji spustí ze Start menu (nebo se spustí po dalším přihlášení).
  - Nastavení (`%APPDATA%\LayerPeek`) a hesla zůstanou. Při chybě Velopack vrátí původní složku.
- **Doporučení pro uživatele: aktualizovat v aplikaci** (položka „Je dostupná nová verze X.Y.Z“ v menu ikony): aplikace před aktualizací uloží stav, běžně se ukončí, nová verze se sama spustí a logy zůstanou. Popsáno v příručkách (`manuals/*/manual.md`, kapitola 12 Aktualizace, Windows).
- Nové vydání se ve winget objeví až po sloučení PR (validace a kontrola trvají hodiny až dny); aplikace nabídne aktualizaci dřív.
- `RequireExplicitUpgrade` (vyřazení z `winget upgrade --all`) manifest nemá: rozhodnutí uživatele 2026-11-11 – žádná další volitelná pole manifestu; místo toho oprava hooku (T-201) a doporučení aktualizovat v aplikaci.

## Navazující úkoly

- **T-058 (kontrola aktualizací):** vydání `0.x` jsou předběžná (`--prerelease`), od 1.0.0 ne a `release-publish.ps1` to při zveřejnění zachová, takže je `GET /releases/latest` nevrátí. Koncepty nejsou bez přihlášení vidět vůbec.
- **T-059 (winget):** odeslání navazuje na zveřejnění (`release-publish.ps1`, i z `release.ps1`), viz „Publikace do winget“. Ověření na Windows (první odeslání, `winget install`/`uninstall`) zbývá v T-059.

## Konfigurace RID

Konfigurace RID je na jednom místě: `$script:ReleaseRuntimes` v `scripts/ReleaseTools/ReleaseTools.psm1`. RID, který v tabulce není, skripty odmítnou (`Unknown runtime …`).

| RID | OS | `Default` | `Uploadable` | `BlockedReason` |
|---|---|---|---|---|
| `win-x64` | windows | ano | ano | – |
| `win-arm64` | windows | ano | ano (od 2026-11-16) | – |
| `osx-arm64` | macos | ano | ano | – |

`win-arm64` (T-226): výchozí build na Windows x64 ho sestaví a zabalí (AOT publish s komponentou MSVC ARM64 z T-224, FFmpeg přes llvm-mingw z T-225, kontrola ARM64 PE). Do 2026-11-16 měl `Uploadable = $false` (`BlockedReason` `not verified on an ARM64 device (T-227)`, rozhodnutí uživatele 2026-11-12); **rozhodnutím uživatele 2026-11-16 se vydává naplno od prvního vydání po 1.0.0** (`Uploadable = $true`; 1.0.0 vyšla z existujícího tagu bez něj), ještě před během na zařízení s ARM64, který ověří T-227 dodatečně. `release-publish.ps1` tedy ve vydání Windows čeká i feed `releases.win-arm64.json` (+ `.sig`). Mechanismus pro RID, který se sestavuje, ale nenahrává (`Uploadable = $false` + `BlockedReason`), zůstává a je testovaný nad umělou tabulkou: `Get-UploadableReleaseRuntimes` (výchozí a nahratelné RID OS) a `Get-ReleaseBuildRuntimeArguments` (dnes na obou OS nic, jinak `-Runtime <nahratelné RID>`) používá `release.ps1`, plán kroků (`Get-ReleaseFlowPlan`, `Format-ReleaseFlowPlan`), další kroky po konceptu (`Get-ReleaseNextSteps`) i hláška kontroly uploadu; kontrola toolchainu `release.ps1` počítá s nahratelnými RID, takže chce na Windows i komponentu MSVC ARM64.

## Testy skriptů (Pester)

Rozhodovací logika skriptů má testy v Pesteru 5+ (`scripts/ReleaseTools/ReleaseTools.Tests.ps1`, pro `scripts/release.ps1` `scripts/ReleaseTools/ReleaseOrchestrator.Tests.ps1`, instalace v [prerequisites.md](prerequisites.md)):

```powershell
Invoke-Pester scripts/ReleaseTools/ReleaseTools.Tests.ps1 -Output Detailed
Invoke-Pester scripts/ReleaseTools/ReleaseOrchestrator.Tests.ps1 -Output Detailed
```

Spouští je i `dotnet test`: projekt `tests/LayerPeek.Scripts.Tests` (test `ScriptPesterTestsPass`, jeden případ na soubor testů) zavolá `pwsh` s Pesterem a selže při jakékoli chybě Pesteru. Bez `pwsh` nebo bez Pesteru 5+ se test **přeskočí** s důvodem (jako testy prohlížeče bez Edge). Dočasné soubory jdou do složky vyhrazené pro worktree, kterou test smaže i po pádu předchozího běhu.

Testy ověří bez sítě a bez GitHubu: čtení verze, tag, sekci `CHANGELOG.md` (i skutečného souboru), výběr RID (včetně odmítnutí `osx-arm64` na Windows), výběr remote pro `-Repo`, všechna odmítnutí podle stavu gitu (v dočasném repozitáři s lokálním „origin“ a „trial“: špinavý strom, chybějící tag, tag jiné verze, tag jen lokálně, tag přesunutý na remote, commit mimo `main`, nestažený `main`, nedostupný remote), značku sestavení, kontrolní součty, archivy bez ladicích symbolů, plán nahrání, kontroly před zveřejněním a balicí krok Velopack (argumenty `vpk`, názvy souborů, shoda verze `vpk` s NuGet balíčkem, odmítnutí souborů RID, který se nahrávat nesmí) a `win-arm64` (T-226: výchozí a nahratelný RID Windows, vydání Windows bez feedu `win-arm64` odmítnuté, odmítnutí výstupu i souborů RID s `Uploadable = $false` nad umělou tabulkou, `release.ps1` a další kroky bez `-Runtime`, `aot-smoke.ps1 -Runtime win-arm64` na x64, `release-upload.ps1 -DryRun` proti falešnému `gh`: výchozí build s `win-x64` i `win-arm64` přijme, nic nenahraje). Balení pro macOS (T-019): `Info.plist` ze šablony (verze, `LSUIElement`, bundle identifikátor shodný s aplikací), sestavení bundlu bez ladicích symbolů, lokalizace `InfoPlist.strings` (T-097: kopie do bundlu, kontroly, parser `.strings`, na macOS `plutil -lint` a podpis s nimi), pořadí a argumenty podpisu, argumenty `hdiutil`, typ procesu z `lsappinfo` a registrace v LaunchServices; na macOS i skutečný ad-hoc podpis s `codesign --verify --deep --strict` (a rozbitá pečeť po změně souboru), `.icns` (16 a 32 px ze zjednodušené varianty beze změny pixelů, 64 px a víc z předlohy; odmítnutí PNG špatné velikosti) a ověřený `.dmg` a `.tar.gz` bez `._*` nad zástupným bundlem (nic se nepřipojuje). Záloha smoke testu: místa bundle identifikátoru a domény `defaults` nad zástupnou složkou se zástupným `defaults`. Volání `dotnet` a `gh` a samotné skripty `release-build/upload/publish.ps1` netestují, ty se ověřují `-DryRun` a zkušebním vydáním.

Podpis feedu (T-181) ověří `ReleaseTools.Tests.ps1`: sdílený testovací vektor `tests/Shared/Fixtures/UpdateFeed` (podpis z vektoru ověří PowerShell, podpis z PowerShellu ověří veřejný klíč vektoru; změněný feed, jiný RID, jiný klíč a poškozený `.sig` odmítnuty stejně jako v C#), čtení seznamu klíčů ze skutečného `UpdateFeedKeys.cs` (testovací klíč Debug = klíč vektoru, v `Production` nikdy), odmítnutí neznámého tvaru seznamu, prázdného seznamu, nedůvěryhodného klíče, nešifrovaného a chybějícího souboru klíče a špatného hesla, podpis a ověření celé složky `dist/`, vytvoření páru klíčů, pořadí nahrání (`.sig` před feedem) a značku `-SkipFeedSignature`. Skutečný `release-build.ps1` běží v dočasném repozitáři (jen skripty, verze a `UpdateFeedKeys.cs` s testovacím seznamem): bez klíče, s chybějícím / nešifrovaným klíčem, s prázdným seznamem (bez dotazu na heslo), s nedůvěryhodným klíčem, se špatným a chybějícím heslem skončí chybou ještě před buildem; s důvěryhodným klíčem ho přijme a vypíše jen otisk veřejného klíče. Ve výpisech ani souborech se nesmí objevit heslo ani žádný řádek soukromého klíče (ani šifrovaného). `ReleaseOrchestrator.Tests.ps1` proti falešnému `gh`: zkušební build odmítnut (i `-DryRun`), nový koncept vznikne prázdný a `.sig` se nahraje s balíčky před feedem, `release-publish.ps1` odmítne feed bez `.sig`. Celý podepsaný build (s `dotnet`) Pester nespouští, ověřuje ho zkušební vydání.

Jen podepsat hotový `dist/` (T-197): `ReleaseTools.Tests.ps1` ověří čistou funkcí `Get-SignOnlyProblems` každé odmítnutí záznamu (chybějící, neúplný, podepsaný, zkušební a neznámý stav, jiná verze / commit / OS, špinavý strom, RID, otisk klíče, `.sig` nebo soubor dvakrát v záznamu, chybějící feed, změněný, chybějící a přidaný soubor), stav `pending` v kontrole uploadu, `Get-ReleaseGitState -LocalOnly` (bez dotazu na remote), důvěru podle otisku a text výzvy na heslo. `ReleaseOrchestrator.Tests.ps1` spustí skutečný `release-build.ps1 -SignOnly` v dočasném repozitáři (otagovaný commit `v0.1.0`, `UpdateFeedKeys.cs` s testovacím klíčem, zástupný `dist/` se záznamem `pending`, testovací klíč z `tests/Shared/Fixtures/UpdateFeed`): podpis projde, zabalené soubory se nezmění, `.sig` ověří testovací veřejný klíč, součty a `build-info.json` (`signed`) jsou jako po buildu, skutečný `release-upload.ps1 -DryRun` (falešné `gh`) je přijme a druhé `-SignOnly` odmítne. Odmítnutí s „nic nepodepsáno“ (bez `.sig` a součtů, záznam dál `pending`): změněný, přidaný a chybějící soubor, jiný commit, commit bez tagu, špinavý strom, chybějící záznam, zkušební build, záznam ze špinavého stromu, nedovolené přepínače, bez klíče, prázdný seznam a nedůvěryhodný klíč (bez dotazu na heslo), nešifrovaný klíč, jiný klíč se stejným heslem, špatné a chybějící heslo. Ve výpisech ani v `artifacts/` se nesmí objevit heslo ani řádek soukromého klíče.

Odeslání do winget (T-059) ověří `ReleaseWinget.Tests.ps1` bez sítě: šablony v `build/winget` (tři soubory, UTF-8 bez BOM, záhlaví schématu 1.28.0, `Publisher`/`PackageName`/`ProductCode` shodné s nastavením Velopacku, tichá instalace `--silent`, `Scope: user`, jen `x64`, URL dané verze, licence, moniker, nejvýš 16 tagů), čisté funkce (kontroly verze, repozitáře a OS, vydání bez `Setup.exe` / koncept / bez SHA256, datum vydání, otevřené PR, argumenty `wingetcreate` bez tokenu, odmítnutí fine-grained tokenu, maskování výstupu, nabídka po zveřejnění) a `Invoke-WingetRelease` proti falešným `gh`, `wingetcreate` a `winget` na `PATH`: aktualizace (`wingetcreate update`), první verze (`winget validate` a `wingetcreate submit` se složkou manifestu, která se pak smaže), bez potvrzení se token nečte, `-DryRun`, `-OutputDirectory` i na macOS, selhání `gh api` (žádný druhý „New package“), selhání `wingetcreate` a `winget validate`. Falešný `wingetcreate` vypíše token, který dostal, na stdout i stderr; testy ověří, že výstup obsahuje jen `***`, že token není v argumentech žádného volání, že ho `wingetcreate` dostal v `WINGET_CREATE_GITHUB_TOKEN` a že se pak vrátila předchozí hodnota proměnné. `ReleaseOrchestrator.Tests.ps1` ověří, že `release-publish.ps1` (i spuštěný z `release.ps1`) u `0.x` vypíše jen poznámku. Skutečné `wingetcreate`, `winget` ani GitHub testy nevolají.

Vydání ze dvou počítačů a neměnná vydání (T-200) ověří `ReleaseTools.Tests.ps1` čistými funkcemi (`Get-RetiredSwitchProblem`, `Get-DuplicateReleaseProblems`, `Get-ReleaseOsStatus`, `Get-ReleaseRunCommand`, `Get-PublishProblems` s radou, kde nahrát chybějící OS; `Test-ReleaseMarkerFileName`) a `ReleaseOrchestrator.Tests.ps1`: plán (krok konceptu a zveřejnění, `[wait]` na druhý OS, Gitflow hotový na `origin` bez lokálního `main`/`develop`, dva koncepty, zveřejněné vydání bez tohoto OS, `NEXT STEP` každé situace bez `-AllowIncomplete`/`-AddToPublished`), skutečné `release-upload.ps1` a `release-publish.ps1` proti falešnému `gh` (stav vydání v dočasné složce, i `release list`, `release download` a `release edit`): oba přepínače v každém stavu a s `-DryRun`, `-Force`, `-Repo` odmítnuté s kódem 1 bez jediného volání `gh`, první OS do nového konceptu (prázdný koncept, balíčky, pak součty a feed), zveřejnění odmítne koncept bez druhého OS, druhý OS do stejného konceptu a zveřejnění, nahrání do zveřejněného vydání odmítnuté, dva koncepty odmítnuté. **Celý tok na dvou počítačích:** dva klony jednoho `origin`, `release.ps1` jako Windows i Mac (testovací přepínač `LAYERPEEK_RELEASE_TEST_HOST_OS`, platí jen pro skripty uvnitř `LAYERPEEK_PESTER_TESTS_TEMP`), v obou pořadích OS: první počítač skončí `NEXT STEP` pro druhý, `-DryRun` druhého ohlásí nestažený tag, ostrý běh stáhne jen tag, přeskočí Gitflow (lokální větve beze změny), nahraje a zveřejní (nebo po odmítnutí zveřejní první počítač), pak oba počítače hlásí „Nothing left to do“; druhý počítač nahraje dřív než první; dva koncepty zastaví zveřejnění.

Testy `release.ps1` (`ReleaseOrchestrator.Tests.ps1`) ověří bez sítě: kontrolu verze proti tagům, změnu `<Version>` a `CHANGELOG.md` (datum `- unreleased`, přesun z `[Unreleased]`, šablona prázdných podnadpisů, odkazy, smíšené konce řádků, pomlčky `–`/`—`, odmítnutí), plán a navázání pro každý mezistav (čistě z připravených faktů) a skutečné kroky Gitflow v dočasných repozitářích: bare „origin“ je dostupný přes adresu `https://github.com/test-owner/…` přepsanou `url.<base>.insteadOf` a konfigurace gitu je izolovaná. `gh` je falešný skript na `PATH`: `auth status` projde, `release view` vrátí „release not found“, nebo – s `LAYERPEEK_FAKE_GH_STATE` – koncept, který vytvořil `release create` (soubory s `digest`); každé volání zapíše a testy ověří, co se volalo. Mimo jiné: větev release ručně bez data, s jinou verzí, zastaralá, se stabilizačními commity a s dřívějším datem (nový commit s datem), kontrola sloučení před tagem, selhané posunutí `main` po tagu a navázání, push odmítnutý kvůli posunutému `origin/main` (atomicky, bez tagu), ref posunutý po potvrzení, sloučení zpět bez vybraného `develop`, `-Repo` v klonu s cizím `origin`, push URL jiného repozitáře. Skript `release.ps1` se spouští i celý: `-DryRun` nic nezmění ani nestáhne, ostrý běh stáhne (`--no-tags`), bez potvrzení se nic nepushne, selhání zástupného buildu vrátí strom na `develop` a další běh začne buildem, a úspěšný běh se zástupným buildem a skutečným `release-upload.ps1` skončí konceptem (falešný `gh`), na `develop` s čistým stromem, a další běh najde kroky 1–9 hotové a zveřejnění `[wait]` na druhý OS. Testy-only přepínač `LAYERPEEK_RELEASE_TEST_SKIP_TOOLCHAIN=1` vypne kontrolu MSVC / Xcode / `vpk`, aby testy nezávisely na stroji. Platí jen spolu s `LAYERPEEK_PESTER_TESTS_TEMP` a jen pro repozitář uvnitř této složky (proměnné, které zůstanou v interaktivním `pwsh` po přerušeném `Invoke-Pester`, tak skutečný repozitář neovlivní); když platí, plán vypíše řádek `TEST MODE: toolchain check skipped`. Falešný `gh` odpovídá na čtení přímo ze shimu (`gh.cmd` / `gh`) a `pwsh` spouští jen pro stavový koncept, takže testy běží rychleji (≈ 1 min 20 s). xUnit má pro tento soubor časový limit 15 min (ostatní soubory 5 min). Skutečný build a nahrání na GitHub testy nedělají.

## Zkušební vydání (testovací repozitář)

Celý postup s GitHubem (koncept, nahrání, opakování, zveřejnění) se ověřuje v **dočasném samostatném repozitáři**, ne v hlavním (rozhodnuto 2026-10-21). Upload a publish mají parametr `-Repo <owner/název>`: skript si najde git remote, který na tento repozitář ukazuje, a **stejný remote** použije pro kontrolu tagu a větve `main`. Kontroly i vydání se tak vždy týkají jednoho repozitáře; bez `-Repo` je to `origin`. Když žádný remote na repozitář neukazuje (nebo ukazuje víc), skript to odmítne s příkazem `git remote add`.

Postup (`<owner>` = tvůj účet, název např. `LayerPeek-release-trial`):

1. Testovací repozitář (soukromý) a samostatný klon, aby se lokální tagy hlavního repozitáře nemíchaly se zkušebními:

   ```powershell
   gh repo create <owner>/LayerPeek-release-trial --private
   git clone https://github.com/pfoltynek/LayerPeek.git $env:TEMP\layerpeek-trial
   cd $env:TEMP\layerpeek-trial
   git switch develop                           # nebo commit, který se má vyzkoušet
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
   pwsh scripts/release-build.ps1 -FeedSigningKey <klíč>
   pwsh scripts/release-upload.ps1 -Repo <owner>/LayerPeek-release-trial -DryRun
   pwsh scripts/release-upload.ps1 -Repo <owner>/LayerPeek-release-trial
   ```

   Kontrola: na GitHubu je koncept `v0.1.0` označený jako předběžný, s poznámkami z `CHANGELOG.md`, se zipem a `SHA256SUMS-windows.txt`.

   Build pro nahrání musí mít podepsaný feed (T-181): **i zkušební vydání se podepisuje produkčním klíčem** (rozhodnuto uživatelem 2026-11-11, otázka 19 v T-181), tedy `release-build.ps1 -FeedSigningKey <klíč>` s offline produkčním klíčem, jehož veřejný klíč je v `UpdateFeedKeys.Production` zkoušeného commitu (testovací klíč z `tests/Shared/Fixtures` v `Production` být nesmí). Platí stejný postup jako při vydání: médium připojené až do konce buildu, heslo dvakrát. Zkušební build `-SkipFeedSignature` upload odmítne (to je potřeba ověřit také). Přijetí aktualizace aplikací ve zkušebním repozitáři ověřit **nejde**: Release build má adresu repozitáře napevno (`AppUpdates.RepositoryUrl`) a testovací zdroj `LAYERPEEK_TEST_UPDATE_SOURCE` (jen Debug) důvěřuje jen testovacímu klíči. Přijetí podepsaného feedu ze skutečného GitHubu ověří build 0.9.9 po vydání 1.0.0 (viz „Kontrola aktualizace z GitHubu buildem 0.9.9“), odmítnutí nepodepsaného nebo změněného feedu E2E testy `UpdateTests` (T-181, T-125 2v). Před 1.0.0 se zkušební vydání nedělá (rozhodnutí uživatele 2026-11-11, T-198 otázka 3): skriptovou část pokrývají Pester testy, generálka podepsaného buildu bez GitHubu (T-198 B4) a `-DryRun` v den vydání.

4. Opakování: druhé spuštění `release-upload.ps1` musí soubory přeskočit („skip, identical file already uploaded“). Po novém `release-build.ps1` musí nahrání odmítnout jiný obsah a s `-Force` ho přepsat. Soubory druhého OS do stejného konceptu nahraje `release.ps1 -Repo …` na druhém počítači (viz zkouška níže).

   Ověřit také hraniční případy, které zatím ověřené nejsou:
   - **přerušené nahrání:** během uploadu ukonči skript (Ctrl+C). Zjisti, v jakém stavu soubor na GitHubu zůstal (`gh release view v0.1.0 --repo … --json assets` – `state`, `digest`), že ho další běh bez `-Force` odmítne a s `-Force` přepíše, a že `release-publish.ps1` nedokončený soubor nezveřejní;
   - **souběžné vytvoření:** dva běhy `release-upload.ps1` spuštěné současně (např. dva klony s různým RID, nebo Windows a Mac) – vznikne jeden koncept, nebo dva? Od T-200 vzniká koncept už v `release.ps1` hned po pushi tagu a dva koncepty skripty odmítnou (`GitHub has 2 releases …`). Výsledek zapsat sem a do T-055.

5. Zveřejnění: `pwsh scripts/release-publish.ps1 -Repo <owner>/LayerPeek-release-trial -DryRun` musí odmítnout chybějící `SHA256SUMS-macos.txt` a `-AllowIncomplete` musí skončit kódem 1 (T-200). Zveřejní se až po nahrání druhého OS (zkouška druhého počítače níže); kontrola na GitHubu (zveřejněné, stále předběžné).

   **Zkouška `release.ps1`** (místo ručních kroků 2–3, v novém testovacím repozitáři nebo po úklidu tagu a vydání z předchozí zkoušky). Testovací repozitář potřebuje `main` a `develop`, klon musí být na `develop` s `release.ps1` a **nesmí mít `origin`** (jinak ho `release.ps1 -Repo` odmítne, aby se sloučení zkoušky nedostala k `main` a `develop` hlavního repozitáře):

   ```powershell
   git switch develop
   git remote remove origin                       # klon zkoušky zná jen remote trial
   git push trial main develop
   git fetch trial
   pwsh scripts/release.ps1 -Version 0.1.0 -Repo <owner>/LayerPeek-release-trial -DryRun
   pwsh scripts/release.ps1 -Version 0.1.0 -Repo <owner>/LayerPeek-release-trial
   ```

   Kontrola: v `trial` je `main` se sloučením a tagem `v0.1.0`, `develop` se sloučením zpět, koncept jako v kroku 3. Navázání: během buildu přeruš skript (Ctrl+C) a spusť ho znovu – musí ukázat kroky 1–6 jako hotové a začít buildem; po úspěšném konci musí další běh skončit bez práce (vše `[done]`). Zveřejnění nabídne až běh, který nahraje druhý OS (níže).

   **Zkouška druhého počítače (T-200)** na počítači s druhým OS, v samostatném klonu bez `origin` (remote jen `trial`):

   ```powershell
   git clone --origin trial https://github.com/<owner>/LayerPeek-release-trial.git $env:TEMP\layerpeek-trial-2
   cd $env:TEMP\layerpeek-trial-2
   git switch develop
   pwsh scripts/release.ps1 -Version 0.1.0 -Repo <owner>/LayerPeek-release-trial -DryRun   # ohlásí tag na trial, který tu ještě není
   pwsh scripts/release.ps1 -Version 0.1.0 -Repo <owner>/LayerPeek-release-trial          # stáhne tag, přeskočí Gitflow, build, nahrání, nabídne zveřejnění
   pwsh scripts/release-upload.ps1 -Repo <owner>/LayerPeek-release-trial -AddToPublished   # musí odmítnout s kódem 1 (T-200)
   ```

   Kontrola na GitHubu: jediné vydání `v0.1.0`, zveřejněné a předběžné, se soubory obou OS; další běh na obou počítačích vypíše „Nothing left to do“. Souběžná varianta: druhý počítač spustit hned po hlášce `Tag v0.1.0 and the draft are on GitHub` prvního.

6. Úklid:

   ```powershell
   gh repo delete <owner>/LayerPeek-release-trial --yes   # potřebuje oprávnění: gh auth refresh -s delete_repo
   cd ..; Remove-Item -Recurse -Force $env:TEMP\layerpeek-trial
   ```

Hlavní repozitář se při zkoušce nemění (žádný tag, žádné vydání, `origin` se jen čte při klonování).
