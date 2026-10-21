---
id: T-055
title: Skripty pro vydání (build, AOT publish, archivy, kontrolní součty, GitHub Release)
status: todo
owner: qa-reviewer
priority: P2
platform: all
depends_on: []
---

## Cíl
Vydání zadarmo a bez CI: lokální skripty sestaví, otestují a zabalí aplikaci pro všechny cílové RID a nahrají výsledek do GitHub Releases. Windows binárky vznikají na Windows, macOS binárky na Macu (Native AOT nejde kompilovat napříč OS). Oba běhy přidávají soubory do **stejného** vydání pro tag `vX.Y.Z`.

Rozhodnutí uživatele (2026-10-21):
- Žádné GitHub Actions (T-016 zůstává odložený). Repozitář je soukromý, hostované runnery by spotřebovaly 2 000 minut zdarma za měsíc (macOS se počítá 10×). Soubory přiložené k vydání (release assets) jsou zdarma.
- Cílové RID: `win-x64`, `win-arm64`, `osx-arm64`. Žádný `osx-x64` ani universal binary.
- Formáty: přednostně instalátory (dodají T-017 a T-019), k tomu archivy: `.zip` pro Windows, `.tar.gz` pro macOS.
- Bez placeného podpisu: Windows nepodepsané, macOS ad-hoc podpis (`codesign -s -`). Podpis řeší T-017 / T-019, tento úkol nic nepodepisuje.
- **Verze:** jediný zdroj je `<Version>` v kořenovém `Directory.Build.props`. Skript ověří, že odpovídá tagu `vX.Y.Z`. První verze je `0.1.0`. Vydání verzí `0.x` se na GitHubu označují jako předběžná (`--prerelease`).
- **Distribuce:** vydání publikuje jen uživatel. Repozitář bude později veřejný, takže dlouhodobě se počítá se stahováním (i aktualizacemi) z GitHub Releases bez tokenu. Dokud je soukromý, stačí, že soubory stáhne jen ten, kdo má do repozitáře přístup.
- **`win-arm64`:** vydává se až po dokončení T-057 (FFmpeg pro ARM64). Do té doby skript soubory `win-arm64` nenahrává.

## Rozsah
- Skripty v `scripts/` v PowerShell 7 (`#Requires -Version 7.0`, `[CmdletBinding()]`, `$ErrorActionPreference = 'Stop'`, kontrola `$LASTEXITCODE` po každém nativním příkazu, komentářová nápověda `.SYNOPSIS`/`.EXAMPLE`), stejně jako `scripts/aot-smoke.ps1`. Stejné skripty běží na Windows i macOS.
- **Sestavení** (bez sítě kromě NuGet / FFmpeg cache):
  1. `dotnet build` (Release) a `dotnet test` celé solution. Při chybě nebo varování konec.
  2. Native AOT `dotnet publish src/PrusaTrayDiagnostic.App -c Release -r <RID>` do samostatné složky pro každý RID pod `artifacts/release/<verze>/` (složka `artifacts/` je v `.gitignore`). Výchozí RID podle OS: na Windows `win-x64`, na macOS `osx-arm64`. `win-arm64` je ve výchozím stavu vypnutý (seznam RID je nastavitelný, např. parametrem nebo jednou konfigurační hodnotou ve skriptu) a zapne se až po dokončení T-057. Parametrem jde zvolit jen některé RID. RID jiného OS skript odmítne se srozumitelnou chybou.
  3. AOT smoke test (`scripts/aot-smoke.ps1` nebo jeho varianta nad hotovou publish složkou) jen pro RID, který jde na tomto stroji spustit. Ostatní RID skript vypíše jako „neověřeno spuštěním“.
  4. Archivy: Windows `.zip` obsahu publish složky, macOS `.tar.gz`. Dokud T-019 nedodá `.app` bundle, obsahuje `.tar.gz` publish složku `osx-arm64` tak, jak je (rozhodnutí architekta: macOS běh skriptu jde vyzkoušet hned a T-019 jen vymění vstup balení). Navrhovaný název: `PrusaTrayDiagnostic-<verze>-<RID>.zip` / `.tar.gz`.
  5. Kontrolní součty SHA256: jeden soubor na OS (např. `SHA256SUMS-windows.txt`, `SHA256SUMS-macos.txt`) ve formátu `sha256sum` (`<hash>  <soubor>`), aby se běhy na dvou strojích navzájem nepřepisovaly. Soubor obsahuje všechny soubory nahrávané z tohoto stroje (archivy i instalátory).
- **Bod rozšíření pro instalátory:** krok balení volá pro každý RID seznam „balicích kroků“ (archiv je první z nich). T-017 přidá krok Velopack (nebo jiný instalátor podle T-056), T-019 krok `.app` + `.dmg`. Přidání kroku nesmí vyžadovat změnu publikační části. Seznam kroků je explicitní (žádné hledání skriptů podle masky). Výstup každého kroku jde do stejné složky a automaticky do kontrolních součtů i do nahrávání.
- **Verze z jednoho zdroje:** verze aplikace dnes není nikde definovaná (v `Directory.Build.props` ani v projektech není `Version`, SDK použije `1.0.0`; okno O aplikaci čte `AssemblyInformationalVersion`). V rámci tohoto úkolu přidat `<Version>0.1.0</Version>` do kořenového `Directory.Build.props` (rozhodnutí uživatele 2026-10-21). Skript verzi odtud přečte, takže exe, okno O aplikaci, názvy souborů i tag mají stejnou hodnotu.
- **Publikace** (`gh`):
  - Odmítne pokračovat, když pracovní strom není čistý (`git status --porcelain` včetně nesledovaných souborů není prázdný).
  - Odmítne pokračovat, když `HEAD` nemá přesně tag `vX.Y.Z`, verze v tagu nesouhlasí s verzí ze zdroje, tag není na `origin` nebo na `origin` ukazuje na jiný commit.
  - Odmítne pokračovat, když chybí `gh` nebo není přihlášený (`gh auth status`).
  - Nahrává jen soubory, které vznikly ze stejného commitu (např. značka s hashem commitu ve výstupní složce), aby se nenahrál starší build.
  - Odmítne nahrát soubory `win-arm64`, dokud není T-057 hotový (`win-arm64` je ve výchozím seznamu RID vypnutý, zapnutí je vědomý krok po T-057).
  - Vydání pro tag neexistuje → `gh release create vX.Y.Z --verify-tag ...` se soubory, pro verze `0.x` s `--prerelease`. Existuje → `gh release upload vX.Y.Z ...` (běh na druhém OS přidá své soubory). Přepsání už nahraného souboru jen s výslovným přepínačem (např. `-Force` → `--clobber`).
  - Režim „nanečisto“ (`-WhatIf` nebo `-DryRun`), který vše zkontroluje a vypíše, co by nahrál, bez volání `gh release`.
- **Dokumentace** (česky): nový `docs/release.md` s celým postupem vydání (Gitflow: `release/X.Y.Z` → `main` + tag, běh na Windows, běh na Macu, kontrola vydání na GitHubu, co dělat při chybě). Aktualizovat `docs/prerequisites.md`: `gh` CLI (instalace přes `winget install --id GitHub.cli` / `brew install gh`, `gh auth login`) a pro `win-arm64` komponenta Visual Studia **MSVC ARM64 build tools** (`Microsoft.VisualStudio.Component.VC.Tools.ARM64`) včetně příkazu winget a ověření přes `vswhere`.
- Skripty nesmí měnit nic mimo repozitář kromě dočasných složek, které po sobě uklidí (i po chybě), a vydání na GitHubu.
- V nápovědě `scripts/aot-smoke.ps1` nahradit příklad `osx-x64` (už se nevydává) podporovaným RID.

## Akceptační kritéria
- [ ] Na Windows skript ve výchozím nastavení vytvoří pro `win-x64` publish složku, `.zip` a `SHA256SUMS-windows.txt`. Součty odpovídají souborům (ověřeno `Get-FileHash`).
- [ ] `win-arm64` je ve výchozím stavu vypnutý a skript jeho soubory nenahraje. Po výslovném zapnutí ho sestaví a zabalí stejně jako `win-x64` (ověřeno lokálně, bez nahrání).
- [ ] AOT publish Windows RID bez trim/AOT varování z kódu projektu. Smoke test nativního RID prošel.
- [ ] `<Version>0.1.0</Version>` je v kořenovém `Directory.Build.props`. Verze ve výstupních souborech, ve vlastnostech exe a v okně O aplikaci je stejná a pochází odtud.
- [ ] Vydání verze `0.x` je na GitHubu označené jako předběžné.
- [ ] Publikace odmítne špinavý strom, commit bez tagu, tag neodpovídající verzi i tag, který není na `origin` (ověřeno ručně nebo testem skriptu, výsledek zapsaný v poznámkách).
- [ ] Režim nanečisto vypíše plánované nahrání a na GitHubu nic nezmění.
- [ ] Zkušební vydání (např. předběžný tag na testovacím commitu, po ověření smazané i s vydáním) ukázalo vytvoření vydání a následné přidání souborů druhým během (na Windows lze simulovat druhým během s jiným RID).
- [ ] Bod rozšíření pro instalátory je popsaný v `docs/release.md` (jak T-017 / T-019 přidají krok).
- [ ] `docs/release.md` a `docs/prerequisites.md` aktuální (česky).
- [ ] Běh na macOS (`osx-arm64`, `.tar.gz`, `SHA256SUMS-macos.txt`, přidání do existujícího vydání) se ověřuje na Macu v rámci T-019; zde stačí, že skript na Windows RID `osx-arm64` srozumitelně odmítne.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- ~~Zdroj verze~~ → `<Version>` v kořenovém `Directory.Build.props`, skript ověří shodu s tagem `vX.Y.Z`. První verze `0.1.0` (rozhodnuto 2026-10-21).
- ~~Kdo bude vydání stahovat?~~ → repozitář bude později veřejný, vydání publikuje jen uživatel. Do té doby stačí přístup pro ty, kdo mají přístup k repozitáři (rozhodnuto 2026-10-21).
- ~~`win-arm64` bez kamery~~ → `win-arm64` se vydává až po dokončení T-057, do té doby je ve skriptu vypnutý (rozhodnuto 2026-10-21).
- **Z jaké větve vydávat:** má skript vyžadovat, aby tagovaný commit byl na `main` (podle Gitflow), nebo stačí jakýkoli tagovaný commit (např. kvůli zkušebním vydáním)?
- **Koncept vs. hned zveřejněné vydání:** má první běh (Windows) vytvořit vydání jako koncept (`--draft`) a zveřejnit ho až ručně / běh na Macu, nebo ho zveřejnit hned?
- ~~Mají se verze `0.x` označovat jako předběžné?~~ → ano, `--prerelease` (rozhodnuto 2026-10-21).
- **Poznámky k vydání:** odkud se vezmou (ruční text, `--generate-notes` z GitHubu, soubor `CHANGELOG.md`)?

## Poznámky k implementaci
(vyplní agent po dokončení)
