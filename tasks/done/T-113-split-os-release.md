---
id: T-113
title: Oddělené vydání macOS a Windows pod jednou verzí
status: done
owner: qa-reviewer
priority: P2
platform: all
depends_on: [T-055, T-058]
---

## Cíl
Uživatel chce pod jednou verzí (`vX.Y.Z`) vydat nejdřív soubory jednoho OS (typicky macOS) a později, na jiném počítači
a v jinou dobu, doplnit soubory druhého OS (Windows) do už **zveřejněného** vydání. Dnes `release-upload.ps1` do
zveřejněného vydání nahrát odmítne („already published“), takže nejde doplnit ani soubory Windows k vydané 0.3.0.

## Rozsah
- `scripts/release-upload.ps1`: nový přepínač (název navrhne implementace, např. `-AddToPublished`), který dovolí
  nahrát do zveřejněného vydání **jen soubory OS, jehož `SHA256SUMS-<os>.txt` ve vydání ještě není**. Nikdy nic
  nepřepíše (s `-Force` se nekombinuje), ostatní kontroly (tag, `origin/main`, čistý strom, značka sestavení, součty)
  platí dál. Bez přepínače se chování nemění. Vydání zůstane předběžné / nepředběžné jako bylo, text se nemění.
  Navázání po přerušení: soubory se stejným SHA256 přeskočit; nedokončený soubor (`starter`, bez `digest`) ani soubor
  s jiným obsahem nepřepisovat – odmítnout s návodem.
- Po doplnění ověřit úplnost stejně jako `release-publish.ps1` (všechny soubory ze součtů se stejným SHA256, nic
  navíc, žádný `win-arm64`), nebo nabídnout `release-publish.ps1 -DryRun` jako kontrolu zveřejněného vydání.
- `GitHubReleaseChecker` (T-058, aplikace mimo Velopack: `.zip` na Windows, `.app` mimo Aplikace): nabídnout vydání
  jen tehdy, když obsahuje soubor `SHA256SUMS-<os>.txt` OS, na kterém aplikace běží (pole `assets[].name` odpovědi
  GitHubu). Vydání bez souborů tohoto OS přeskočit jako koncept. Velopack (`GithubSource`) vydání bez
  `releases.<kanál>.json` přeskakuje sám (ověřeno ve zdroji Velopack 1.2.161, `GitBase.GetReleaseFeed`).
- `scripts/release.ps1` funguje i na Macu (Gitflow + build + koncept na tom OS, který jde první); ověřit testy,
  upravit hlášky a výpis „další kroky“ tak, aby neříkaly „jen Windows“.
- `docs/release.md` (a `README.md` / `README.cz.md`, pokud vydání popisují): postup „OS 1 zveřejní, OS 2 doplní“.
- `CHANGELOG.md` `[0.3.1]`: položka o kontrole aktualizací podle souborů ve vydání (Changed / Fixed).

## Rozhodnutí (uživatel, 2026-10-30)
- Úkol založit a udělat hned, verze 0.3.1.
- Text vydání se při částečném zveřejnění **nemění** (žádná dočasná věta, skripty text vydání neupravují).
- Gitflow (`release.ps1`) spouští ten OS, který jde první; druhý jen `git switch --detach vX.Y.Z`,
  `release-build.ps1` a upload s novým přepínačem.

## Akceptační kritéria
- [x] `release-upload.ps1` s novým přepínačem doplní soubory chybějícího OS do zveřejněného vydání; s přepínačem
      odmítne OS, jehož součty už ve vydání jsou, a nikdy nic nepřepíše (Pester).
- [x] Bez přepínače je chování beze změny (Pester).
- [x] `GitHubReleaseChecker` přeskočí vydání bez `SHA256SUMS-<os>.txt` aktuálního OS (unit testy s fixturou).
- [x] `release.ps1` na macOS projde testy orchestrace; výpis dalších kroků odpovídá OS.
- [x] `docs/release.md` popisuje postup na dvou počítačích.
- [x] `dotnet build` (Debug i Release) bez varování, `dotnet test` bez chyb, profil beze změny.
- [x] Ruční ověření uživatelem: doplnění souborů Windows k vydané 0.3.0 (nebo zkušební vydání v testovacím repozitáři). → přesunuto do T-114 a T-115 (ověří se ostře u 0.3.1, Q1)

## Otevřené otázky
- **Q1 – ruční ověření na 0.3.0:** tag `v0.3.0` obsahuje staré skripty (bez `-AddToPublished`; `release-upload.ps1` je
  z kontrolovaného checkoutu tagu, `git switch --detach v0.3.0` je tedy vrátí). Navíc macOS soubory 0.3.0 nemají feed
  Velopacku (`releases.osx-arm64.json` přišel až s T-058 v 0.3.1), takže závěrečná kontrola po doplnění by u 0.3.0
  skončila hláškou „Release v0.3.0 has no releases.osx-arm64.json“ (soubory by už byly nahrané, kód 1). Doplnění
  Windows k 0.3.0 tímto postupem proto nejde bez ručního obejití. Návrh: ověřit ručně zkušebním vydáním v testovacím
  repozitáři (docs/release.md, „Zkouška doplnění druhého OS“), nebo poprvé ostře u 0.3.1. Rozhodnutí: uživatel.
  **Rozhodnuto 2026-10-30 (uživatel): poprvé ostře u 0.3.1** (Mac zveřejní s `-AllowIncomplete`, Windows doplní
  `-AddToPublished`). K 0.3.0 se soubory Windows tímto postupem nedoplňují.
- **Q2 – staré vydání bez součtů:** zveřejněné `v0.1.0` a `v0.2.0` nemají `SHA256SUMS-windows.txt` (jen `.zip` a
  `Setup.exe`), takže je `GitHubReleaseChecker` už nikdy nenabídne. Regrese to není (kontrola aktualizací je až od
  0.3.0/0.3.1, tedy novější než obě), jen pro úplnost: doplnit jim součty, nebo nechat? Návrh: nechat.
  **Rozhodnuto 2026-10-30 (uživatel): nechat**, na stará vydání se nesahá.

## Poznámky k implementaci
- Uzavřeno 2026-10-30: zbývající ruční ověření přesunuto do T-114 a T-115 (rozhodnutí uživatele).
- **`release-upload.ps1 -AddToPublished`** (název přepínače zvolen implementací): stejné kontroly jako dřív
  (`Get-ReleasePreflight`, značka sestavení, součty), navíc čistá funkce `Get-AddToPublishedProblems` v
  `ReleaseTools.psm1`: vydání musí existovat a být zveřejněné, nesmí mít `SHA256SUMS-<os>.txt` OS buildu, soubor
  s jiným obsahem nebo nedokončený (`state` ≠ `uploaded` nebo bez `digest`) odmítne s návodem (smazat ručně na
  stránce vydání). Stejné soubory přeskočí (`Get-UploadPlan`, navázání). S `-Force` odmítne hned na začátku (bez
  volání `gh`). Nahrává ve dvou voláních `gh release upload` bez `--clobber`: nejdřív balíčky, pak soubory, podle
  kterých aplikace OS pozná (`Test-ReleaseMarkerFileName`: `SHA256SUMS-*`, `releases.*.json`, `assets.*.json`,
  `RELEASES-*`), aby kontrola aktualizací nenabídla vydání bez balíčků. Text vydání ani `--prerelease` se nemění
  (žádné `gh release edit`). Po nahrání stáhne součty (`Get-ReleaseChecksums`, sdílené s `release-publish.ps1`) a
  zkontroluje celé vydání `Get-PublishProblems -Published` (oba OS, feed každého RID, SHA256, nic navíc, žádný
  `win-arm64`).
- **Bez přepínače** se nic nemění kromě textu odmítnutí (`… already published …`), který nově radí `-AddToPublished`;
  ověřeno Pesterem (kód 1, stav vydání beze změny, žádné volání kromě čtení).
- **Nalezená chyba:** `release-publish.ps1 -AllowIncomplete` vždy skončil `Cannot bind argument to parameter
  'ExpectedOs' because it is null` (`if { @() }` vrací v PowerShellu `$null`); je to i v tagu `v0.3.0`. Opraveno
  (`@(if (...) {...})`), pokryto testem dvou počítačů.
- **`GitHubReleaseChecker`:** `assets[].name` a `assets[].state` v `GitHubReleaseDocument` (stále jen
  source-generated `GitHubJsonContext`). Vydání se počítá jen s úplně nahraným (`state` `uploaded` nebo chybí)
  `SHA256SUMS-<os>.txt`; OS je nový `ReleaseOs` (Windows / MacOS), výchozí podle `OperatingSystem`, v testech
  vstřikovaný parametrem konstruktoru. Jiný OS (Linux) nedostane nabídku žádnou. Testy: zaznamenaná fixtura
  (`github-releases-2026-10-30.json`, 0.3.0 jen macOS) a nová `github-releases-split-os.json` (odvozená ze záznamu:
  macOS první, Windows první, oba, předběžná s oběma, přerušené nahrání `starter`, vydání bez součtů). Nová
  závislost žádná.
- **`release.ps1` na macOS:** orchestrace už na Macu fungovala (oba soubory Pesteru prošly i před změnou, 318 + 1 přeskočený). Upraveny
  hlášky: `Get-ReleaseNextSteps` má `-HostOs` a `-Remote` a vypíše OS, který jde první, a postup pro druhý OS
  (`git fetch --tags`, `git switch --detach`, `release-build.ps1`, `release-upload.ps1 -AddToPublished`); plán
  zveřejněného vydání bez součtů tohoto OS vypíše, jak je doplnit. Nápověda `release.ps1` už neříká jen Windows.
- **Testy:** falešný `gh` v `ReleaseOrchestrator.Tests.ps1` si nově ukládá nahrané soubory a umí `release download`
  a `release edit` (a hlásí `state`), takže skutečné `release-upload.ps1` a `release-publish.ps1` projdou celý postup
  dvou počítačů (Describe „release on two machines“, 11 testů; s testy dalších kroků +14). Čisté funkce v `ReleaseTools.Tests.ps1` (+16). Pester celkem 349 (348 prošlo, 1 přeskočen jako dřív; předtím 318 + 1).
  Skutečný GitHub se nečetl ani neměnil (jen čtení `gh api` při analýze stavu vydání 0.1.0–0.3.0).
- **Dokumentace:** `docs/release.md` (nová sekce „Vydání na dvou počítačích“, tabulky odmítnutí, zkušební vydání,
  testy), `docs/architecture.md`, `README.md` / `README.cz.md`, `CHANGELOG.md` `[0.3.1]` (Changed).
