# Vydání (release)

Vydání se dělá lokálně bez CI (GitHub Actions se nepoužívají, T-016 zamítnut 2026-11-01). Windows soubory se sestaví na Windows, macOS soubory na Macu (Native AOT nejde kompilovat napříč OS). Oba běhy nahrávají do **stejného konceptu** (draft) vydání pro tag `vX.Y.Z` na GitHubu. Zveřejnění je samostatný poslední krok, takže nikdo nevidí vydání s polovinou souborů. Od T-113 jde vydání pod jednou verzí rozdělit i v čase: jeden OS zveřejní (`-AllowIncomplete`) a druhý OS později na jiném počítači své soubory **doplní do zveřejněného vydání** (`release-upload.ps1 -AddToPublished`, viz „Vydání na dvou počítačích“).

| Skript | Co dělá | Mění GitHub? |
|---|---|---|
| `scripts/release.ps1` | **jeden příkaz** pro kroky 1–3 níže: větev `release/X.Y.Z`, sloučení do `main` + tag, sloučení zpět do `develop`, sestavení tagu a nahrání do konceptu; nikdy nezveřejňuje (viz „Vydání jedním příkazem“) | ano, jen push a koncept po potvrzení (kromě `-DryRun`) |
| `scripts/release-build.ps1` | build + testy, Native AOT publish pro RID tohoto OS, AOT smoke test, archivy, na Windows instalátor Velopack (T-017), na macOS `LayerPeek.app` od `vpk` + `.dmg` + `.tar.gz` + balíček a feed Velopacku (T-019, T-058), podpis feedu offline klíčem (T-181, viz „Podpis feedu aktualizací“), `SHA256SUMS-<os>.txt` | ne |
| `scripts/release-upload.ps1` | kontroly, vytvoření konceptu vydání (první běh) nebo nahrání do existujícího konceptu (druhý OS); s `-AddToPublished` doplnění chybějícího OS do zveřejněného vydání (T-113) | ano (kromě `-DryRun`) |
| `scripts/release-publish.ps1` | kontroly úplnosti konceptu a jeho zveřejnění | ano (kromě `-DryRun`) |
| `scripts/ReleaseTools/ReleaseTools.Tests.ps1` | Pester testy logiky skriptů bez sítě (spouští je i `dotnet test`, viz níže) | ne |

Všechny skripty jsou pro PowerShell 7.2 nebo novější (`#Requires -Version 7.2`) a běží na Windows i macOS. Režim `-DryRun` u uploadu a publish **čte** stav z GitHubu (`gh auth status`, `gh release view`, u publish i stažení `SHA256SUMS-*.txt` do dočasné složky), ale na GitHubu nic nemění. Prerekvizity (MSVC / Xcode Command Line Tools, `gh` CLI) jsou v [prerequisites.md](prerequisites.md).

## Rozhodnutí (2026-10-21)

- Vydávané RID: `win-x64`, `osx-arm64`. Windows ARM64 (`win-arm64`) se nepodporuje (rozhodnutí uživatele 2026-11-01, T-130): skripty ho odmítnou jako neznámý RID (`Unknown runtime 'win-arm64'`).
- Verze: jediný zdroj je `<Version>` v kořenovém `Directory.Build.props` (tvar `X.Y.Z`). Odtud ji má exe, okno O aplikaci, názvy souborů i tag `vX.Y.Z`.
- Vydává se jen z `main`: tagovaný commit musí být obsažený v `origin/main`.
- První nahrání vytvoří **koncept** (`--draft`). Verze `0.x` jsou na GitHubu označené jako předběžné (`--prerelease`).
- Poznámky k vydání jsou sekce verze z `CHANGELOG.md` (formát [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), anglicky podle `CLAUDE.md`).
- Bez placeného podpisu: Windows soubory jsou nepodepsané, macOS má jen ad-hoc podpis bez notarizace (T-019, viz „Balení pro macOS“).
- Feed aktualizací `releases.<RID>.json` je od T-181 podepsaný offline klíčem (ECDSA P-256) a aplikace aktualizaci bez platného podpisu odmítne (rozhodnuto 2026-11-06, viz „Podpis feedu aktualizací“).

## Vydání jedním příkazem (`scripts/release.ps1`, T-055)

Rozhodnutí uživatele (2026-10-24): jeden skript provede celé vydání až po **koncept** na GitHubu. Spouští ho ten OS, který jde první – Windows i macOS (T-113, rozhodnuto 2026-10-30); druhý OS už `release.ps1` nespouští (viz „Vydání na dvou počítačích“). Zveřejnění zůstává vždy samostatný ruční krok. Vše se spouští lokálně ručně, bez CI.

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

Na konci (i po chybě) se skript vrátí na `develop` a vypíše adresu konceptu a přesné další příkazy podle OS, na kterém běžel (příklad pro Mac):

```text
git switch --detach vX.Y.Z
pwsh scripts/release-publish.ps1 -DryRun
pwsh scripts/release-publish.ps1 -AllowIncomplete   # only the macOS files so far
git switch develop
The Windows files follow on a Windows machine (docs/release.md, release on two machines), without release.ps1:
  git fetch origin --tags; git switch --detach vX.Y.Z; pwsh scripts/release-build.ps1 -FeedSigningKey <key file>
  pwsh scripts/release-upload.ps1 -AddToPublished   # after the publish above
  (or before publishing: pwsh scripts/release-upload.ps1 into the draft, then publish without -AllowIncomplete)
```

- **Druhý OS:** buď ještě do konceptu (krok 4 v „Postup“) a zveřejnit bez `-AllowIncomplete`, nebo zveřejnit první OS s `-AllowIncomplete` a druhý OS doplnit později do zveřejněného vydání (viz „Vydání na dvou počítačích“). Spustí-li se `release.ps1` omylem na druhém OS po zveřejnění, všechny kroky jsou hotové a plán jen vypíše, jak soubory tohoto OS doplnit (`It has no SHA256SUMS-<os>.txt yet …`).
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
- **Podpis feedu (T-181):** krok 7 spouští `release-build.ps1` bez parametrů, klíč proto bere z proměnné prostředí. Před spuštěním připoj médium s klíčem a nastav `$env:LAYERPEEK_FEED_SIGNING_KEY = '<cesta ke klíči>'` ve stejném terminálu; build se zeptá na heslo dvakrát (před buildem a při podpisu na konci) a médium musí zůstat připojené až do konce buildu (viz „Podpis feedu aktualizací“). Že stačí proměnná prostředí a `release.ps1` vlastní parametr `-FeedSigningKey` nemá, rozhodl uživatel 2026-11-11 (otázka 20 v T-181). Bez proměnné krok 7 skončí chybou a další běh `release.ps1` začne znovu buildem. Když selže až podpis na konci buildu (médium odpojené a `abort`, 3× špatné heslo), nespouštěj celý build znovu: v čistém checkoutu tagu `git switch --detach vX.Y.Z`, `pwsh scripts/release-build.ps1 -SignOnly`, pak `git switch develop` a znovu `release.ps1 -Version X.Y.Z`; krok 7 už najde hotový podepsaný build a pokračuje nahráním (viz „Jen podepsat hotový `dist/`“, T-197).
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
| 7: `artifacts/release/X.Y.Z/build-info.json` je z commitu tagu, z čistého stromu, s podepsaným feedem (ne `-SkipFeedSignature` ani nedokončený podpis `pending`, T-181, T-197) a soubory sedí se součty |
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

## Vydání na dvou počítačích (OS 1 zveřejní, OS 2 doplní, T-113)

Rozhodnutí uživatele (2026-10-30): pod jednou verzí `vX.Y.Z` se smí nejdřív zveřejnit soubory jednoho OS (typicky macOS) a soubory druhého OS doplnit později, na jiném počítači a v jinou dobu. Text vydání se při částečném zveřejnění **nemění** (žádná dočasná věta; skripty text vydání nikdy neupravují) a příznak předběžného vydání zůstane, jak byl.

**Počítač s prvním OS** (Gitflow, build, koncept, zveřejnění):

```bash
git switch develop && git pull --ff-only
pwsh scripts/release.ps1 -Version X.Y.Z -DryRun
pwsh scripts/release.ps1 -Version X.Y.Z            # kroky 1–8: větev, main + tag, develop, build tagu, koncept
git switch --detach vX.Y.Z
pwsh scripts/release-publish.ps1 -DryRun            # odmítne chybějící SHA256SUMS druhého OS – to je v pořádku
pwsh scripts/release-publish.ps1 -AllowIncomplete   # zveřejní vydání jen se soubory tohoto OS
git switch develop
```

**Počítač s druhým OS** (později; žádný Gitflow, `release.ps1` se tu nespouští):

```powershell
git fetch origin --tags
git switch --detach vX.Y.Z                         # čistý strom, tag z origin
pwsh scripts/release-build.ps1 -FeedSigningKey <cesta ke klíči>   # build tohoto OS z tagu, podpis feedu (T-181)
pwsh scripts/release-upload.ps1 -AddToPublished -DryRun
pwsh scripts/release-upload.ps1 -AddToPublished    # doplní soubory tohoto OS do zveřejněného vydání
git switch develop
```

Co `-AddToPublished` dělá a hlídá:

- Platí všechny kontroly uploadu (tag na `HEAD` = `<Version>`, tag na `origin`, `HEAD` v `origin/main`, čistý strom, značka sestavení, součty).
- Vydání musí existovat a být **zveřejněné** (koncept: nahraj bez přepínače a zveřejni) a nesmí ještě obsahovat `SHA256SUMS-<os>.txt` tohoto OS. Soubory jednoho OS se doplní jen jednou; OS, jehož součty už ve vydání jsou, skript odmítne.
- **Nikdy nic nepřepíše** (s `-Force` se nekombinuje). Soubory se stejným SHA256 přeskočí (navázání po přerušení: stačí spustit znovu). Nedokončený soubor (GitHub ho hlásí ve stavu `starter`, bez `digest`) nebo soubor s jiným obsahem odmítne s návodem: soubor smaž ručně na stránce vydání (*Edit*) a spusť znovu.
- Feed tohoto OS musí mít podpis `.sig` (T-181), takže doplnit OS do vydání zveřejněného **před** T-181 nejde; přijato uživatelem 2026-11-11 (otázka 21 v T-181; T-180 má `-AddToPublished` stejně zrušit).
- Nejdřív nahraje balíčky (`.zip`, `Setup.exe`, `.nupkg`, `.dmg`, `.tar.gz`) a podpisy feedu (`releases.<RID>.json.sig`, T-181) a **až potom** soubory, podle kterých aplikace OS ve vydání pozná: `SHA256SUMS-<os>.txt` a feed Velopacku (`releases.<RID>.json`, `assets.<RID>.json`, `RELEASES-<RID>`). Během nahrávání tak žádná aplikace nedostane nabídku vydání, jehož balíčky tam ještě nejsou.
- Na konci zkontroluje celé zveřejněné vydání stejně jako `release-publish.ps1`: součty obou OS, feed každého vydávaného RID, každý soubor ze součtů se stejným SHA256, nic navíc. Při problému skončí s kódem 1 a vypíše ho (soubory už jsou nahrané, oprav ručně).
- Bez přepínače se chování nemění: nahrání do zveřejněného vydání skript odmítne (`Release vX.Y.Z is already published …`) a nově jen poradí `-AddToPublished`.

Co mezitím vidí aplikace:

- **Instalace přes Velopack** (Windows `Setup.exe`, macOS v Aplikacích) hledá vydání podle feedu `releases.<RID>.json`; vydání bez feedu svého RID přeskočí (ověřeno ve zdroji Velopack 1.2.161, `GitBase.GetReleaseFeed`).
- **Aplikace mimo Velopack** (`.zip` na Windows, `.app` mimo Aplikace) se ptá GitHubu přímo (`GitHubReleaseChecker`, T-058) a od T-113 nabídne jen vydání, které má úplně nahraný (`state` = `uploaded`) soubor `SHA256SUMS-windows.txt` / `SHA256SUMS-macos.txt` OS, na kterém běží. Vydání bez souborů tohoto OS přeskočí stejně jako koncept.

## Postup

Ruční postup (reference; `scripts/release.ps1` dělá kroky 1–3 sám). Kroky 3 a 4 jdou v libovolném pořadí – první je ten OS, na kterém běží `release.ps1`. Druhý OS může jít ještě do konceptu (kroky 3/4) nebo až po zveřejnění (`-AddToPublished`, viz „Vydání na dvou počítačích“).

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
pwsh scripts/release-build.ps1 -FeedSigningKey E:\layerpeek-feed-key.pem   # build, testy, publish win-x64, smoke test, zip, instalátor, podpis feedu, SHA256SUMS-windows.txt
pwsh scripts/release-upload.ps1 -DryRun         # jen kontroly a výpis, co by se nahrálo
pwsh scripts/release-upload.ps1                 # vytvoří koncept vydání a nahraje soubory
```

Smoke test spustí publikovanou aplikaci na 10 s s tvými skutečnými nastaveními (může ukázat skutečné notifikace) a odmítne běžet, když už aplikace běží. Před během ji ukonči.

### 4. Běh na Macu

Stejné příkazy na Macu (Apple Silicon), **až když běh prvního OS úspěšně skončil** (souběžné běhy nejsou ošetřené; jde-li Mac první, je to naopak a krok 3 přijde po něm). Terminál musí být ve fyzické cestě k repozitáři (`echo $PWD` = `pwd -P`, viz [prerequisites.md](prerequisites.md)).

```bash
git switch --detach vX.Y.Z
pwsh scripts/release-build.ps1 -FeedSigningKey /Volumes/LP-KEY/layerpeek-feed-key.pem   # build, testy, publish osx-arm64, smoke test, LayerPeek.app, .dmg, .tar.gz, podpis feedu, SHA256SUMS-macos.txt
pwsh scripts/release-upload.ps1 -DryRun         # kontroly a výpis, co by se přidalo do konceptu
pwsh scripts/release-upload.ps1                 # přidá soubory macOS do konceptu z běhu na Windows (jde-li Mac první, koncept vytvoří)
```

Ruční ověření macOS buildu (Gatekeeper, oprávnění, menu bar, monitory, přihlášení, uspaný displej) je souhrnně v [macos-manual-testing.md](macos-manual-testing.md).

Vznikne `LayerPeek-X.Y.Z-osx-arm64.dmg`, `LayerPeek-X.Y.Z-osx-arm64.tar.gz`, od T-058 i `LayerPeek-X.Y.Z-osx-arm64-full.nupkg`, `releases.osx-arm64.json`, `assets.osx-arm64.json`, `RELEASES-osx-arm64` (aktualizace přes Velopack) a `SHA256SUMS-macos.txt`, které `release-upload.ps1` přidá do existujícího konceptu (postup sestavení viz „Balení pro macOS“ níže). Archiv se na macOS balí s `--no-mac-metadata` a `COPYFILE_DISABLE=1`, takže neobsahuje položky AppleDouble `._*` (ověřeno v T-019 výpisem `tar -tzf`). Po běhu na Macu se zveřejňuje bez `-AllowIncomplete`.

### 5. Kontrola a zveřejnění

1. Na GitHubu (*Releases*, koncept je vidět jen pro správce repozitáře) zkontroluj text a soubory.
2. Zveřejni:

```powershell
pwsh scripts/release-publish.ps1 -DryRun
pwsh scripts/release-publish.ps1
```

`release-publish.ps1` zveřejní koncept jen tehdy, když obsahuje `SHA256SUMS-windows.txt` i `SHA256SUMS-macos.txt`, všechny soubory z nich jsou nahrané se stejným SHA256 (podle `digest`, který hlásí GitHub), žádný jiný soubor tam není a není tam soubor RID, který se nahrávat nesmí (`Uploadable = $false`, dnes žádný). Přepínač `-AllowIncomplete` dovolí zveřejnit vydání bez souborů některého OS (např. jen macOS); nahrané soubory se kontrolují i tak. Soubory chybějícího OS se pak doplní `release-upload.ps1 -AddToPublished` (viz „Vydání na dvou počítačích“). (Do T-113 `-AllowIncomplete` skončil chybou `Cannot bind argument to parameter 'ExpectedOs' it is null`; opraveno.)

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
  build-info.json       značka sestavení: verze, commit, čistý strom, RID, soubory a jejich SHA256, podpis feedu, výsledek smoke testu
```

- `build-info.json` zapíše build (s klíčem) poprvé **hned po zabalení, před podpisem** jako záznam se stavem podpisu `pending` (T-197): stejný tvar jako konečná značka, jen `Files` obsahuje zabalené soubory bez `.sig` a `FeedSignature` je `Status` `pending` s otiskem klíče ověřeného před buildem. Po úspěšném podpisu ho přepíše konečná značka (`signed`). Když podpis selže, záznam zůstane; `release-upload.ps1` ho odmítne a `release-build.ps1 -SignOnly` podle něj podepíše hotový `dist/` (viz „Jen podepsat hotový `dist/`“).

- Archiv: `LayerPeek-<verze>-<RID>.zip` (Windows, obsah publish složky bez nadřazené složky), `.tar.gz` (macOS, podepsaná `LayerPeek.app`, zachová spustitelný bit).
- macOS (T-019, T-058): `LayerPeek-<verze>-osx-arm64.dmg`, balíček a feed Velopacku (`LayerPeek-<verze>-osx-arm64-full.nupkg`, `releases.osx-arm64.json`, `assets.osx-arm64.json`, `RELEASES-osx-arm64`; `Portable.zip` ani `.pkg` se nevydávají) a podepsaná aplikace od `vpk` pro místní použití v `artifacts/release/<verze>/app/osx-arm64/portable/LayerPeek.app` (nenahrává se).
- Instalátor Velopack (jen Windows, T-017, viz „Instalátor pro Windows“ níže): `LayerPeek-<verze>-<RID>-Setup.exe` a soubory aktualizací `LayerPeek-<verze>-<RID>-full.nupkg`, `releases.<RID>.json`, `assets.<RID>.json`, `RELEASES-<RID>`. `vpk` pracuje ve vlastní složce `velopack-<RID>` vedle `dist/` a po zkopírování ji skript smaže.
- **Ladicí symboly se nevydávají** (rozhodnuto 2026-10-21): archiv neobsahuje soubory `*.pdb` ani nic ze složek `*.dSYM` a nikam se nenahrávají. V publish složce zůstanou pro lokální ladění. Zip `win-x64` má tak asi 70 MB místo 120 MB.
- Podpis feedu (T-181): `releases.<RID>.json.sig` ke každému `releases.<RID>.json` (jeden řádek base64 + LF). `build-info.json` má `FeedSignature`: `Status` `signed` (s `PublicKeySha256`, otiskem veřejného klíče) nebo `skipped` (`-SkipFeedSignature`).
- Kontrolní součty: jeden soubor na OS, formát `sha256sum` (`<hash>  <soubor>`, LF), obsahuje všechny soubory z `dist/` tohoto běhu (i `.sig`).
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
| výstup obsahuje neznámý RID (např. `win-arm64`) | `Build output contains unknown runtime …` |
| zkušební build `release-build.ps1 -SkipFeedSignature` (T-181) | `Build output was made with -SkipFeedSignature (a trial build) …` |
| podpis na konci buildu nedoběhl (`build-info.json` se stavem `pending`, T-197) | `Build output is not signed yet: the signing at the end of release-build.ps1 did not finish … release-build.ps1 -SignOnly` |
| `build-info.json` bez stavu podpisu (build starší než T-181) | `Build output has no update feed signature (made before T-181?) …` |
| `releases.<RID>.json` bez `releases.<RID>.json.sig` ve výstupu | `Build output has releases.<RID>.json without its signature …` |
| v `dist/` je soubor navíc, chybí nebo nesedí SHA256 | `dist contains files not produced by the build` / `… does not match its SHA256` |
| vydání už je zveřejněné (bez `-AddToPublished`) | `Release vX.Y.Z is already published … To add the files of an OS … -AddToPublished` |
| soubor se stejným názvem, ale jiným obsahem už je nahraný | `Already uploaded with different content … Re-run with -Force` |
| `-AddToPublished` s `-Force` | `-AddToPublished never replaces a file, so it cannot be combined with -Force` |
| `-AddToPublished`: vydání neexistuje / je koncept | `There is no release for vX.Y.Z. -AddToPublished only adds files to a published release` / `Release vX.Y.Z is still a draft` |
| `-AddToPublished`: součty tohoto OS už ve vydání jsou | `Published release vX.Y.Z already contains SHA256SUMS-<os>.txt` |
| `-AddToPublished`: soubor s jiným obsahem / nedokončený | `… is in release vX.Y.Z with different content … Nothing is overwritten` / `… its upload did not finish (state starter, no SHA256)` |
| `-AddToPublished`: kontrola celého vydání po nahrání | `Check of the published release vX.Y.Z REFUSED …` |

Navíc jen `release-publish.ps1` (a kontrola po `release-upload.ps1 -AddToPublished`):

| Kontrola | Hláška (začátek) |
|---|---|
| vydání neexistuje / už je zveřejněné | `There is no release for vX.Y.Z` / `Release vX.Y.Z is already published` |
| chybí `SHA256SUMS-<os>.txt` některého OS (bez `-AllowIncomplete`) | `Release vX.Y.Z has no SHA256SUMS-<os>.txt` |
| chybí feed `releases.<RID>.json` vydávaného RID | `Release vX.Y.Z has no releases.<RID>.json: installations of …` |
| feed `releases.<RID>.json` bez podpisu `releases.<RID>.json.sig` (T-181, i s `-AllowIncomplete`) | `Release vX.Y.Z has releases.<RID>.json but no releases.<RID>.json.sig …` |
| soubor chybí, nesedí SHA256 nebo není v žádném souboru součtů | `… is listed in … but not uploaded` / `Uploaded … does not match its SHA256` / `… is uploaded but not listed in any checksum file` |

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
- **`release-publish.ps1`** (i kontrola po `-AddToPublished`) odmítne vydání, kde u některého `releases.<RID>.json` chybí `.sig`, i s `-AllowIncomplete`.

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

## Bod rozšíření pro instalátory (T-017, T-019)

`scripts/release-build.ps1` má explicitní seznam `$packagingSteps` (žádné hledání skriptů podle masky). Každý krok má `Name`, `Os` (pro které OS se spouští) a `Invoke` – scriptblock, který dostane kontext:

| Vlastnost | Význam |
|---|---|
| `Version` | verze z `Directory.Build.props` |
| `Runtime` | RID, např. `win-x64` |
| `Os` | `windows` / `macos` |
| `PublishDirectory` | hotová a smoke testem ověřená publish složka RID |
| `OutputDirectory` | `dist/`, sem krok uloží své soubory |

Krok vrátí cesty souborů, které vytvořil přímo v `dist/`. Skript je sám zapíše do `SHA256SUMS-<os>.txt` a `build-info.json`, takže je `release-upload.ps1` nahraje a `release-publish.ps1` zkontroluje bez další změny. Kroky dnes: `archive` (Windows, `.zip`), `installer` (Windows, T-017, Velopack) a `app` (macOS, T-019: `LayerPeek.app`, `.dmg`, `.tar.gz`). Logika kroku `installer` je ve funkcích `Get-VelopackPackArguments`, `Get-VelopackOutputMap`, `Copy-VelopackOutput` a `Get-VelopackVersionProblems`, kroku `app` ve funkcích `New-MacIcon`, `New-MacAppBundle`, `Get-VelopackMacPackArguments`, `Expand-ZipWithDitto`, `Get-MacSignatureProblems`, `Get-MacVelopackBundleProblems`, `Get-MacHardenedRuntimeProblems` a `New-MacDiskImage` modulu `ReleaseTools.psm1` (Pester testy). Build, publish a smoke test publish složky se tím nezměnily.

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
5. **Smoke test bundlu:** `aot-smoke.ps1 -PublishDir LayerPeek.app/Contents/MacOS` (od T-058 `.app` z `Portable.zip`) spustí exe uvnitř bundlu (tedy s bundle identifikátorem) a ověří, že běží bez ikony v Docku (`lsappinfo`, typ `UIElement`). Bundle se nesmí změnit a podpis musí dál platit.
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
- **Nahrání:** soubory jdou na GitHub jako ostatní (`release-upload.ps1`, `gh`), `vpk upload` se nepoužívá. Aplikace hledá aktualizace v GitHub Releases bez tokenu (`GithubSource`, kanál = RID instalace). Koncept vydání anonymní klient nevidí, aktualizace se nabídne až po zveřejnění. Dokud je repozitář soukromý, kontrola skončí chybou 404 a aplikace ji bere jako „žádná aktualizace“.
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

## Navazující úkoly

- **T-058 (kontrola aktualizací):** vydání `0.x` jsou předběžná (`--prerelease`) a `release-publish.ps1` to při zveřejnění zachová, takže je `GET /releases/latest` nevrátí. Koncepty nejsou bez přihlášení vidět vůbec.
- **T-059 (winget):** odeslání manifestu (`wingetcreate update …`) navazuje až na zveřejnění (`release-publish.ps1`). Postup sem doplní T-059.

## Konfigurace RID

Konfigurace RID je na jednom místě: `$script:ReleaseRuntimes` v `scripts/ReleaseTools/ReleaseTools.psm1` (dnes `win-x64` a `osx-arm64`). RID, který v tabulce není, skripty odmítnou (`Unknown runtime …`). Windows ARM64 (`win-arm64`) se nepodporuje (rozhodnutí uživatele 2026-11-01, T-130), proto tam není; původní příprava (T-057, T-063) je zamítnutá. Mechanismus `Uploadable = $false` / `BlockedReason` (RID sestavit jen pro lokální ověření, nenahrávat) zůstává pro případné další RID, dnes ho žádný RID nepoužívá.

## Testy skriptů (Pester)

Rozhodovací logika skriptů má testy v Pesteru 5+ (`scripts/ReleaseTools/ReleaseTools.Tests.ps1`, pro `scripts/release.ps1` `scripts/ReleaseTools/ReleaseOrchestrator.Tests.ps1`, instalace v [prerequisites.md](prerequisites.md)):

```powershell
Invoke-Pester scripts/ReleaseTools/ReleaseTools.Tests.ps1 -Output Detailed
Invoke-Pester scripts/ReleaseTools/ReleaseOrchestrator.Tests.ps1 -Output Detailed
```

Spouští je i `dotnet test`: projekt `tests/LayerPeek.Scripts.Tests` (test `ScriptPesterTestsPass`, jeden případ na soubor testů) zavolá `pwsh` s Pesterem a selže při jakékoli chybě Pesteru. Bez `pwsh` nebo bez Pesteru 5+ se test **přeskočí** s důvodem (jako testy prohlížeče bez Edge). Dočasné soubory jdou do složky vyhrazené pro worktree, kterou test smaže i po pádu předchozího běhu.

Testy ověří bez sítě a bez GitHubu: čtení verze, tag, sekci `CHANGELOG.md` (i skutečného souboru), výběr RID (včetně odmítnutí `osx-arm64` na Windows), výběr remote pro `-Repo`, všechna odmítnutí podle stavu gitu (v dočasném repozitáři s lokálním „origin“ a „trial“: špinavý strom, chybějící tag, tag jiné verze, tag jen lokálně, tag přesunutý na remote, commit mimo `main`, nestažený `main`, nedostupný remote), značku sestavení, kontrolní součty, archivy bez ladicích symbolů, plán nahrání, kontroly před zveřejněním a balicí krok Velopack (argumenty `vpk`, názvy souborů, shoda verze `vpk` s NuGet balíčkem, odmítnutí souborů RID, který se nahrávat nesmí, a odmítnutí `win-arm64`). Balení pro macOS (T-019): `Info.plist` ze šablony (verze, `LSUIElement`, bundle identifikátor shodný s aplikací), sestavení bundlu bez ladicích symbolů, lokalizace `InfoPlist.strings` (T-097: kopie do bundlu, kontroly, parser `.strings`, na macOS `plutil -lint` a podpis s nimi), pořadí a argumenty podpisu, argumenty `hdiutil`, typ procesu z `lsappinfo` a registrace v LaunchServices; na macOS i skutečný ad-hoc podpis s `codesign --verify --deep --strict` (a rozbitá pečeť po změně souboru), `.icns` (16 a 32 px ze zjednodušené varianty beze změny pixelů, 64 px a víc z předlohy; odmítnutí PNG špatné velikosti) a ověřený `.dmg` a `.tar.gz` bez `._*` nad zástupným bundlem (nic se nepřipojuje). Záloha smoke testu: místa bundle identifikátoru a domény `defaults` nad zástupnou složkou se zástupným `defaults`. Volání `dotnet` a `gh` a samotné skripty `release-build/upload/publish.ps1` netestují, ty se ověřují `-DryRun` a zkušebním vydáním.

Podpis feedu (T-181) ověří `ReleaseTools.Tests.ps1`: sdílený testovací vektor `tests/Shared/Fixtures/UpdateFeed` (podpis z vektoru ověří PowerShell, podpis z PowerShellu ověří veřejný klíč vektoru; změněný feed, jiný RID, jiný klíč a poškozený `.sig` odmítnuty stejně jako v C#), čtení seznamu klíčů ze skutečného `UpdateFeedKeys.cs` (testovací klíč Debug = klíč vektoru, v `Production` nikdy), odmítnutí neznámého tvaru seznamu, prázdného seznamu, nedůvěryhodného klíče, nešifrovaného a chybějícího souboru klíče a špatného hesla, podpis a ověření celé složky `dist/`, vytvoření páru klíčů, pořadí nahrání (`.sig` před feedem) a značku `-SkipFeedSignature`. Skutečný `release-build.ps1` běží v dočasném repozitáři (jen skripty, verze a `UpdateFeedKeys.cs` s testovacím seznamem): bez klíče, s chybějícím / nešifrovaným klíčem, s prázdným seznamem (bez dotazu na heslo), s nedůvěryhodným klíčem, se špatným a chybějícím heslem skončí chybou ještě před buildem; s důvěryhodným klíčem ho přijme a vypíše jen otisk veřejného klíče. Ve výpisech ani souborech se nesmí objevit heslo ani žádný řádek soukromého klíče (ani šifrovaného). `ReleaseOrchestrator.Tests.ps1` proti falešnému `gh`: zkušební build odmítnut (i `-DryRun`), nový koncept vznikne prázdný a `.sig` se nahraje s balíčky před feedem, `release-publish.ps1` odmítne feed bez `.sig`. Celý podepsaný build (s `dotnet`) Pester nespouští, ověřuje ho zkušební vydání.

Jen podepsat hotový `dist/` (T-197): `ReleaseTools.Tests.ps1` ověří čistou funkcí `Get-SignOnlyProblems` každé odmítnutí záznamu (chybějící, neúplný, podepsaný, zkušební a neznámý stav, jiná verze / commit / OS, špinavý strom, RID, otisk klíče, `.sig` nebo soubor dvakrát v záznamu, chybějící feed, změněný, chybějící a přidaný soubor), stav `pending` v kontrole uploadu, `Get-ReleaseGitState -LocalOnly` (bez dotazu na remote), důvěru podle otisku a text výzvy na heslo. `ReleaseOrchestrator.Tests.ps1` spustí skutečný `release-build.ps1 -SignOnly` v dočasném repozitáři (otagovaný commit `v0.1.0`, `UpdateFeedKeys.cs` s testovacím klíčem, zástupný `dist/` se záznamem `pending`, testovací klíč z `tests/Shared/Fixtures/UpdateFeed`): podpis projde, zabalené soubory se nezmění, `.sig` ověří testovací veřejný klíč, součty a `build-info.json` (`signed`) jsou jako po buildu, skutečný `release-upload.ps1 -DryRun` (falešné `gh`) je přijme a druhé `-SignOnly` odmítne. Odmítnutí s „nic nepodepsáno“ (bez `.sig` a součtů, záznam dál `pending`): změněný, přidaný a chybějící soubor, jiný commit, commit bez tagu, špinavý strom, chybějící záznam, zkušební build, záznam ze špinavého stromu, nedovolené přepínače, bez klíče, prázdný seznam a nedůvěryhodný klíč (bez dotazu na heslo), nešifrovaný klíč, jiný klíč se stejným heslem, špatné a chybějící heslo. Ve výpisech ani v `artifacts/` se nesmí objevit heslo ani řádek soukromého klíče.

Doplnění OS do zveřejněného vydání (T-113) ověří `ReleaseTools.Tests.ps1` čistými funkcemi (`Get-AddToPublishedProblems`: chybějící vydání, koncept, součty OS už ve vydání, stejné soubory přeskočit, jiný obsah a stav `starter` odmítnout; `Test-ReleaseMarkerFileName`; `Get-PublishProblems -Published`) a `ReleaseOrchestrator.Tests.ps1` skutečnými skripty `release-upload.ps1` a `release-publish.ps1` proti falešnému `gh` (stav vydání v dočasné složce, i `release download` a `release edit`): první OS → koncept → `-AllowIncomplete`, druhý OS bez přepínače odmítnut, s `-Force` odmítnut, špinavý strom odmítnut, `-DryRun` nic nezmění, doplnění ve dvou voláních `gh release upload` (balíčky, pak součty a feed), text a příznaky beze změny, opakování odmítnuto, navázání po přerušení, jiný obsah a nedokončený soubor odmítnuty.

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
   pwsh scripts/release-build.ps1 -FeedSigningKey <klíč>
   pwsh scripts/release-upload.ps1 -Repo <owner>/LayerPeek-release-trial -DryRun
   pwsh scripts/release-upload.ps1 -Repo <owner>/LayerPeek-release-trial
   ```

   Kontrola: na GitHubu je koncept `v0.1.0` označený jako předběžný, s poznámkami z `CHANGELOG.md`, se zipem a `SHA256SUMS-windows.txt`.

   Build pro nahrání musí mít podepsaný feed (T-181): **i zkušební vydání se podepisuje produkčním klíčem** (rozhodnuto uživatelem 2026-11-11, otázka 19 v T-181), tedy `release-build.ps1 -FeedSigningKey <klíč>` s offline produkčním klíčem, jehož veřejný klíč je v `UpdateFeedKeys.Production` zkoušeného commitu (testovací klíč z `tests/Shared/Fixtures` v `Production` být nesmí). Platí stejný postup jako při vydání: médium připojené až do konce buildu, heslo dvakrát. Zkušební build `-SkipFeedSignature` upload odmítne (to je potřeba ověřit také). Ověření, že nainstalovaná verze s ověřením podepsanou aktualizaci přijme a nepodepsanou odmítne, patří do T-125 / T-127.

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

   **Zkouška doplnění druhého OS (T-113)** po zveřejnění s `-AllowIncomplete` (krok 5), na počítači s druhým OS v samostatném klonu se stejným remote `trial`:

   ```powershell
   git fetch trial --tags
   git switch --detach v0.1.0
   pwsh scripts/release-build.ps1 -FeedSigningKey <klíč>
   pwsh scripts/release-upload.ps1 -Repo <owner>/LayerPeek-release-trial               # musí odmítnout: already published, s radou -AddToPublished
   pwsh scripts/release-upload.ps1 -Repo <owner>/LayerPeek-release-trial -AddToPublished -DryRun
   pwsh scripts/release-upload.ps1 -Repo <owner>/LayerPeek-release-trial -AddToPublished
   pwsh scripts/release-upload.ps1 -Repo <owner>/LayerPeek-release-trial -AddToPublished   # musí odmítnout: already contains SHA256SUMS-<os>.txt
   ```

   Kontrola na GitHubu: vydání je pořád zveřejněné a předběžné, text se nezměnil, obsahuje soubory obou OS a skript na konci ohlásil „the release is complete“. Přerušené doplnění (Ctrl+C během nahrávání balíčků) a nové spuštění musí stejné soubory přeskočit; nedokončený soubor (`state` `starter`) musí odmítnout.

6. Úklid:

   ```powershell
   gh repo delete <owner>/LayerPeek-release-trial --yes   # potřebuje oprávnění: gh auth refresh -s delete_repo
   cd ..; Remove-Item -Recurse -Force $env:TEMP\layerpeek-trial
   ```

Hlavní repozitář se při zkoušce nemění (žádný tag, žádné vydání, `origin` se jen čte při klonování).
