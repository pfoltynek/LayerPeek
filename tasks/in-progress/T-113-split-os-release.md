---
id: T-113
title: Oddělené vydání macOS a Windows pod jednou verzí
status: in-progress
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
- [ ] `release-upload.ps1` s novým přepínačem doplní soubory chybějícího OS do zveřejněného vydání; s přepínačem
      odmítne OS, jehož součty už ve vydání jsou, a nikdy nic nepřepíše (Pester).
- [ ] Bez přepínače je chování beze změny (Pester).
- [ ] `GitHubReleaseChecker` přeskočí vydání bez `SHA256SUMS-<os>.txt` aktuálního OS (unit testy s fixturou).
- [ ] `release.ps1` na macOS projde testy orchestrace; výpis dalších kroků odpovídá OS.
- [ ] `docs/release.md` popisuje postup na dvou počítačích.
- [ ] `dotnet build` (Debug i Release) bez varování, `dotnet test` bez chyb, profil beze změny.
- [ ] Ruční ověření uživatelem: doplnění souborů Windows k vydané 0.3.0 (nebo zkušební vydání v testovacím repozitáři).

## Otevřené otázky

## Poznámky k implementaci
