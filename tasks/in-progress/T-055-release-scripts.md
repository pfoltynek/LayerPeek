---
id: T-055
title: Skripty pro vydání (build, AOT publish, archivy, kontrolní součty, GitHub Release)
status: in-progress
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
- [x] Na Windows skript ve výchozím nastavení vytvoří pro `win-x64` publish složku, `.zip` a `SHA256SUMS-windows.txt`. Součty odpovídají souborům (ověřeno `Get-FileHash`).
- [ ] `win-arm64` je ve výchozím stavu vypnutý a skript jeho soubory nenahraje. Po výslovném zapnutí ho sestaví a zabalí stejně jako `win-x64` (ověřeno lokálně, bez nahrání).
- [x] AOT publish Windows RID bez trim/AOT varování z kódu projektu. Smoke test nativního RID prošel.
- [x] `<Version>0.1.0</Version>` je v kořenovém `Directory.Build.props`. Verze ve výstupních souborech, ve vlastnostech exe a v okně O aplikaci je stejná a pochází odtud.
- [ ] Vydání verze `0.x` je na GitHubu označené jako předběžné.
- [x] Publikace odmítne špinavý strom, commit bez tagu, tag neodpovídající verzi i tag, který není na `origin` (ověřeno ručně nebo testem skriptu, výsledek zapsaný v poznámkách).
- [ ] Režim nanečisto vypíše plánované nahrání a na GitHubu nic nezmění.
- [ ] Zkušební vydání (např. předběžný tag na testovacím commitu, po ověření smazané i s vydáním) ukázalo vytvoření vydání a následné přidání souborů druhým během (na Windows lze simulovat druhým během s jiným RID).
- [x] Bod rozšíření pro instalátory je popsaný v `docs/release.md` (jak T-017 / T-019 přidají krok).
- [x] `docs/release.md` a `docs/prerequisites.md` aktuální (česky).
- [x] Běh na macOS (`osx-arm64`, `.tar.gz`, `SHA256SUMS-macos.txt`, přidání do existujícího vydání) se ověřuje na Macu v rámci T-019; zde stačí, že skript na Windows RID `osx-arm64` srozumitelně odmítne.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- ~~Zdroj verze~~ → `<Version>` v kořenovém `Directory.Build.props`, skript ověří shodu s tagem `vX.Y.Z`. První verze `0.1.0` (rozhodnuto 2026-10-21).
- ~~Kdo bude vydání stahovat?~~ → repozitář bude později veřejný, vydání publikuje jen uživatel. Do té doby stačí přístup pro ty, kdo mají přístup k repozitáři (rozhodnuto 2026-10-21).
- ~~`win-arm64` bez kamery~~ → `win-arm64` se vydává až po dokončení T-057, do té doby je ve skriptu vypnutý (rozhodnuto 2026-10-21).
- ~~Z jaké větve vydávat~~ → jen z `main`: skript odmítne tagovaný commit, který není obsažený v `origin/main` (rozhodnuto 2026-10-21).
- ~~Koncept vs. hned zveřejněné vydání~~ → první běh vytvoří vydání jako koncept (`--draft`), běhy na Windows i na Macu nahrávají do konceptu a zveřejnění je samostatný výslovný krok, aby nikdo neviděl vydání s polovinou souborů. Verze `0.x` jsou `--prerelease` (rozhodnuto 2026-10-21).
- ~~Mají se verze `0.x` označovat jako předběžné?~~ → ano, `--prerelease` (rozhodnuto 2026-10-21).
- ~~Poznámky k vydání~~ → ručně psaný `CHANGELOG.md` v kořeni repozitáře (česky). Skript z něj vezme sekci pro danou verzi a bez ní vydání odmítne (rozhodnuto 2026-10-21).
- ~~Verze~~ → `<Version>0.1.0</Version>` v kořenovém `Directory.Build.props` jako jediný zdroj (rozhodnuto 2026-10-21).
- ~~`win-arm64`~~ → ve výchozím stavu vypnutý a nahrání jeho souborů zablokované, dokud není hotový T-057 (rozhodnuto 2026-10-21).
- **Symboly v archivu:** publish složka obsahuje `.pdb` (≈230 MB), zip má ≈120 MB. Vynechat je z archivu (případně nahrát zvlášť), nebo nechat?
- **Automatická samokontrola skriptů:** přidat Pester, nebo xUnit test, který spustí `pwsh scripts/ReleaseTools/Test-ReleaseTools.ps1`? Zatím se spouští ručně.
- **Zkušební vydání:** s pravidlem „jen z `main`“ jde jen přes skutečný tag na `main` (koncept `v0.1.0`, pak smazat i s tagem), nebo přes dočasný testovací repozitář. Kterou cestu?
- **`-AllowIncomplete`:** ponechat možnost zveřejnit vydání bez souborů některého OS (např. jen Windows, dokud není T-019)?

## Poznámky k implementaci
Stav: **in-progress** – skripty hotové a ověřené na Windows lokálně, zbývá ověření s GitHubem (zkušební vydání), `win-arm64` (chybí MSVC ARM64) a Mac (T-019). Viz „Neověřeno“.

**Soubory**
- `scripts/release-build.ps1` – build (`-warnaserror`) + `dotnet test` (Release), AOT publish pro každý RID, kontrola *Product version* exe (Windows), smoke test nativního RID nad hotovou publish složkou (ověří, že ji nezměnil), balicí kroky, `SHA256SUMS-<os>.txt`, značka `build-info.json`. Výstup `artifacts/release/<verze>/` (gitignored), každý běh ho nejdřív smaže. Chybějící MSVC komponentu ohlásí před buildem.
- `scripts/release-upload.ps1` – kontroly + vytvoření konceptu (`--draft --verify-tag`, `--prerelease` pro 0.x, poznámky z `CHANGELOG.md`) nebo nahrání do existujícího konceptu. Idempotentní: stejný soubor (podle `digest` z GitHubu) přeskočí, jiný obsah jen s `-Force` (`--clobber`). Do zveřejněného vydání nenahrává. `-DryRun`.
- `scripts/release-publish.ps1` – samostatný krok zveřejnění konceptu (rozhodnutí 2026-10-21). Kontroluje úplnost (součty obou OS, SHA256 každého souboru proti `digest`, žádný soubor navíc ani `win-arm64`). `-AllowIncomplete`, `-DryRun`.
- `scripts/ReleaseTools/ReleaseTools.psm1` – sdílená logika; rozhodování jsou čisté funkce (verze, tag, changelog, RID, problémy gitu, značka buildu, plán nahrání, kontrola před zveřejněním). Konfigurace RID na jednom místě (`$script:ReleaseRuntimes`). Složka se nejmenuje `scripts/release/`, protože `.gitignore` ignoruje `[Rr]elease/`.
- `scripts/ReleaseTools/Test-ReleaseTools.ps1` – samokontrola bez testovacího frameworku (56 testů), bez sítě a bez `gh`; git kontroly v dočasném repozitáři s lokálním bare „origin“ a izolovanou konfigurací gitu. Dočasnou složku smaže i po chybě.
- `scripts/aot-smoke.ps1` – nový parametr `-PublishDir` (ověří hotovou složku, nemaže ji, logy aplikace do samostatné dočasné složky); příklad `osx-x64` nahrazen `osx-arm64`.
- `Directory.Build.props` – `<Version>0.1.0</Version>`. Test `AboutViewModelTests.VersionComesFromDirectoryBuildProps` hlídá, že verze v okně O aplikaci pochází odtud.
- `CHANGELOG.md` (Keep a Changelog 1.1.0, česky) – sekce `[Unreleased]` a kostra `[0.1.0] - RRRR-MM-DD`. Skript vyžaduje platné datum `YYYY-MM-DD`, takže zástupné datum vydání zablokuje, dokud ho uživatel nevyplní a text nezkontroluje.
- `docs/release.md` (nový), `docs/prerequisites.md` (`gh`, MSVC ARM64 + vswhere, `-PublishDir`), `README.md` (odkaz).

**Ověřeno (Windows 11 x64, 2026-10-21)**
- `pwsh scripts/release-build.ps1`: build 0 varování, `dotnet test -c Release` 4239 testů (4238 prošlo, 1 přeskočený), AOT publish `win-x64` bez varování (jen 2 známé hlášky ILC z Avalonia DComposition, nejsou to varování), Product version exe `0.1.0+cbbc1a0…`, smoke test prošel, `PrusaTrayDiagnostic-0.1.0-win-x64.zip` (≈120 MB) a `SHA256SUMS-windows.txt`. SHA256 ze souboru součtů = `Get-FileHash` zipu. Zip obsahuje obsah publish složky bez nadřazené složky a bez logů smoke testu.
- První pokus smoke testu selhal, protože během běhu jiná instance aplikace (jiný worktree / spike T-056) přepsala sdílený zástupce ve Start menu; opakovaný běh prošel.
- Odmítnutí: `-Runtime osx-arm64` → „cannot be built on windows: Native AOT does not compile across operating systems“; `osx-x64` → „Unknown runtime“; `-Runtime win-x64,win-arm64` → před buildem „MSVC component …ARM64 … is not installed“ (předchozí výstup zůstal).
- `release-upload.ps1 -DryRun` a `release-publish.ps1 -DryRun` na této větvi: odmítnuto se všemi důvody najednou (špinavý strom, chybí tag `v0.1.0`, tag není na `origin`, commit není v `origin/main`, CHANGELOG bez data, build ze špinavého stromu), exit 1; lokální refy beze změny (`git show-ref` před/po), na GitHubu nic.
- Samokontrola `Test-ReleaseTools.ps1`: 56/56, mimo jiné tag jiné verze, tag jen lokálně, tag přesunutý na `origin`, tagovaný commit mimo `main`, nestažený `origin/main`, nedostupný `origin`, build jiného commitu / se `win-arm64` / se souborem navíc, plán nahrání (nový / stejný / jiný / `-Force`), kontroly před zveřejněním.

- `dotnet build` (Debug): 0 varování, 0 chyb. `dotnet test` (Debug): Core.Tests 1290 a App.Tests 2119 (včetně nového testu verze) prošly, ale **`PrusaSimulator.Tests` se po 101 úspěšných testech zasekne** (32 min v celém běhu; samostatně s `--timeout 10m` znovu 101/101 úspěšných a pak přerušeno, exit 3). V Release běhu `release-build.ps1` prošlo všech 4239 testů včetně simulátoru za ~1 min. Změny tohoto úkolu simulátor nemění (souvislost s `<Version>` nenalezena); příčina nezjištěna, možná souběh s ostatními worktree. Proto kritérium „`dotnet build` a `dotnet test`“ zůstává nezaškrtnuté.

**Neověřeno**
- Skutečné vytvoření konceptu, nahrání druhým během a zveřejnění na GitHubu (zkušební vydání) – zadání zakazovalo zápis na GitHub. Tím ani `--prerelease` na GitHubu a výpis plánu v `-DryRun` v platném stavu (vyžaduje tagovaný commit na `main`). `gh release view/upload/download` nad konceptem podle tagu je chování `gh` (hledá i koncepty), lokálně neověřeno.
- `win-arm64` sestavení a balení: na stroji chybí komponenta `Microsoft.VisualStudio.Component.VC.Tools.ARM64`.
- macOS běh (`.tar.gz`, `SHA256SUMS-macos.txt`, `xcrun` kontrola) – v rámci T-019 na Macu. Vytvoření `.tar.gz` přes systémový `tar` ověřuje samokontrola (na Windows `System32\tar.exe`).
- Samokontrola není součástí `dotnet test` (Pester se nepřidával, viz otázky).

**Další otevřené body pro uživatele** (bez rozhodnutí se neimplementovaly)
- Archiv obsahuje celou publish složku včetně `.pdb` (≈230 MB nezabalených, zip ≈120 MB). Vynechat symboly z archivu (příp. nahrát zvlášť)?
- Má se samokontrola skriptů spouštět automaticky (Pester, nebo xUnit test, který spustí `pwsh`)? Zatím jen ručně.
- Zkušební vydání podle kritéria: s pravidlem „jen z `main`“ jde jen přes skutečný tag na `main` (např. `v0.1.0` jako koncept a pak ho smazat i s tagem), nebo dočasně jiný testovací repozitář. Jakou cestu zvolit?
- `-AllowIncomplete` u zveřejnění (vydání jen s Windows soubory, dokud není Mac) – ponechat?
