---
id: T-218
title: CI – GitHub Actions na Windows a macOS (build, testy bez E2E, Native AOT publish) při pushi do `develop` a v PR
status: todo
owner: qa-reviewer
priority: P2
platform: all
depends_on: []
---

> **Rozhodnuto uživatelem 2026-11-13:** ano, CI v GitHub Actions na macOS **i** Windows: build + testy (bez E2E
> a interaktivních UI testů) + Native AOT publish při pushi do `develop` a v pull requestech.
>
> **Pozor na platformu:** úkol je `all`. Workflow se ale ověří jen během na runnerech GitHubu. Část Windows musí
> projít na runneru `windows-*` (nebo ji předem vyzkoušet na Windows), stejně část macOS na runneru `macos-*`. Úkol
> je hotový až po zeleném běhu obou jobů na GitHubu, ne po lokálním buildu.

## Cíl
Regrese se dnes zjistí, až když někdo pustí `dotnet build` / `dotnet test` / AOT publish na druhém OS. Typicky dny
po sloučení (viz sběrné úkoly T-125 / T-127). CI spustí totéž na obou OS po každém pushi do `develop` a v každém PR.

## Rozsah
- **Soubor** `.github/workflows/ci.yml` (jeden workflow, matice `windows` / `macos` nebo dva joby):
  - Spouštění: `push` do `develop`, `pull_request` do `develop` (a `main`, viz otázka 3), `workflow_dispatch`.
    Změny jen v `tasks/**`, `docs/**`, `manuals/**` a `*.md` CI nespouštějí (`paths-ignore`, šetří minuty runnerů),
    pozor na interakci s povinnou kontrolou (otázka 2).
  - **Bezpečnost:** `permissions: contents: read` na úrovni workflow. Žádné tajné údaje (`secrets.*` se nepoužívá).
    `pull_request` (ne `pull_request_target`). Všechny akce třetích stran i `actions/*` **připnuté na SHA commitu**
    s komentářem verze (`uses: actions/checkout@<40 znaků> # vX.Y.Z`). SHA zjistit z tagu vydání akce
    (`gh api repos/actions/checkout/git/ref/tags/vX.Y.Z`) a zapsat do poznámek. `actions/checkout`
    s `persist-credentials: false`.
  - `concurrency: group: ci-${{ github.workflow }}-${{ github.ref }}`, `cancel-in-progress: true`.
  - `timeout-minutes` u každého jobu: macOS první build s FFmpeg ze zdrojů je delší. Změřit a nastavit ~1,5× naměřené
    doby, ne neomezeně.
  - **.NET SDK** z `global.json` (`actions/setup-dotnet` s `global-json-file`), jen stabilní (T-193). Proměnné
    `DOTNET_NOLOGO`, `DOTNET_CLI_TELEMETRY_OPTOUT`. Cache NuGet (`~/.nuget/packages`, klíč = hash
    `Directory.Packages.props` + `**/*.csproj` + `global.json`).
- **FFmpeg cache** (`actions/cache`, `LayerPeekFFmpegCacheRoot` přes proměnnou prostředí do složky runneru):
  - macOS: klíč = OS + hash `build/macos/build-ffmpeg.sh` + `build/FFmpeg.targets` (zdroj a SHA-256 tarballu).
    Stejné vstupy jako klíč cache v `build/FFmpeg.targets` (`StableStringHash` skriptu a `LayerPeekFFmpegMacSourceSha256`).
  - Windows dnes: klíč = hash `build/FFmpeg.targets` (připnutý zip BtbN, ~77 MB stažení).
  - **Vazba na T-202:** T-202 změní Windows na build ze zdrojů (nový skript `build/windows/build-ffmpeg.*`, toolchain
    podle T-202 otázky 1). Klíč pro Windows se pak rozšíří o hash toho skriptu a runner potřebuje toolchain T-202 (např.
    MSYS2 / MinGW, nebo MSVC). Který úkol se dokončí druhý, ten upraví druhou stranu (workflow, nebo poznámku v T-202).
    T-218 na T-202 nečeká.
- **Kroky v každém jobu:**
  1. `dotnet build -c Debug` a `-c Release` bez varování (warnings as errors už platí);
  2. `dotnet test` bez E2E. Projekt `tests/LayerPeek.E2E.Tests` se vynechá **výslovně**: runner Windows má Edge, test
     by se tedy nepřeskočil. E2E testy v `LayerPeek.App.Tests` / `LayerPeek.App.E2E.Tests` se neaktivují
     (`LAYERPEEK_E2E` nenastavené). Ověřit a zapsat seznam všech přepínačů, které E2E / interaktivní testy zapínají. Headless
     UI testy (`LayerPeek.App.UiTests`) **běží**, jsou deterministické s Inter. Testy Klíčenky se na runneru bez
     odemčené klíčenky přeskočí s důvodem. Ověřit, že se nepřeskočí potichu ani nespadnou;
  3. Native AOT publish `win-x64` / `osx-arm64` (`dotnet publish src/LayerPeek.App -c Release -r <rid>`) bez
     varování. Bez spuštění aplikace a bez `aot-smoke.ps1`: ten mění profil a čeká na displej;
  4. při selhání upload logů testů (`TestResults`, `*.trx` / MTP výstup) jako artefakt s krátkou retencí (např.
     3 dny). Při úspěchu žádné artefakty.
- **Runnery:** `windows-latest` (VS s workloadem „Desktop development with C++“ pro AOT; ověřit, že verze MSVC na obrazu
  stačí pro AOT .NET 11) a `macos-*` s Apple Silicon (arm64, Xcode Command Line Tools). Konkrétní obraz zapsat (`macos-15` / novější),
  viz otázka 4.
- **Vazba na T-180 (rulesety) a CodeQL:**
  - ruleset 2 („pull request required“, `develop` + `main`) dnes vyžaduje bránu CodeQL code scanning a admin ho
    obchází. CI se **nepřidává jako povinná kontrola** bez rozhodnutí uživatele (otázka 2);
  - CodeQL běží jako *default setup* (C#, JS/TS, nastaveno 2026-11-06). Workflow nesmí přidat vlastní CodeQL (pokročilé
    nastavení by kolidovalo s default setup) ani měnit jeho nastavení.
- **Dokumentace:** `docs/prerequisites.md` (oddíl CI: co běží, kde, cache), `CONTRIBUTING.md` / README EN + CZ (odznak
  CI jen po rozhodnutí, otázka 5). `CLAUDE.md` jen přes hlavního agenta.
- **Push workflow:** pushnout `.github/workflows/*` přes HTTPS vyžaduje token se scope `workflow`. Na Macu má token
  `gh` (pomocník přihlašování gitu = `gh auth git-credential`) 2026-11-13 scope `workflow` (`gh auth status`: `gist`,
  `read:org`, `repo`, `workflow`). Na Windows neověřeno. Postup rozhodne uživatel (otázka 1, souvisí s otázkou T-059
  ke scope `workflow`).

## Akceptační kritéria
- [ ] `.github/workflows/ci.yml`: `permissions: contents: read`, žádné `secrets`, všechny akce připnuté na SHA
      s komentářem verze, `concurrency` s `cancel-in-progress`, `timeout-minutes` u každého jobu
- [ ] Zelený běh na GitHubu pro **macOS** i **Windows** (build Debug + Release, `dotnet test` bez E2E, AOT publish);
      odkazy na běhy v poznámkách
- [ ] Druhý běh bez změny FFmpeg použije cache FFmpeg (v logu žádný build / stažení FFmpeg) na obou OS; změna
      `build/macos/build-ffmpeg.sh` dá na macOS nový klíč
- [ ] Úmyslně rozbitý test v PR (jen na zkoušku, pak zahodit) skončí červeně a nahraje log testů
- [ ] Žádný E2E / interaktivní test v CI neběží (seznam přepínačů zapsaný); počet přeskočených testů zapsaný a zdůvodněný
- [ ] `docs/prerequisites.md` doplněné; vazba na T-202 zapsaná v T-202
- [ ] `dotnet build` a `dotnet test` lokálně bez chyb a varování

## Otevřené otázky
1. **Push workflow souborů:** HTTPS push `.github/workflows/*` potřebuje scope `workflow`. Na Macu ho token `gh` už má.
   Má se workflow pushovat z Macu (nejjednodušší), nebo na Windows `gh auth refresh -s workflow`, nebo SSH remote,
   nebo vložit soubor přes webové UI? Souvisí s otázkou T-059 ke scope `workflow` (přihlašovat rovnou se `workflow`?).
2. **Povinná kontrola:** má se CI přidat do rulesetu 2 (T-180) jako povinný status check pro PR do `develop` / `main`?
   Admin ho dnes obchází, takže by se týkal jen PR cizích přispěvatelů. S `paths-ignore` by PR jen s dokumentací
   čekal na kontrolu, která se nespustí. Pak by bylo potřeba `paths-ignore` vynechat, nebo přidat job, který při
   změně jen dokumentace hned projde.
3. Spouštět CI i pro PR do `main` a pro push do `release/*` / `hotfix/*` (stabilizace vydání), nebo jen `develop`?
4. Obraz runneru macOS: připnout konkrétní (`macos-15`, novější), nebo `macos-latest`? Připnutý = předvídatelný,
   ale časem zastará. Stejně u Windows (`windows-2025` vs. `windows-latest`).
5. Přidat odznak stavu CI do README (EN + CZ)?
6. Spouštět v CI i Pester testy skriptů (`scripts/ReleaseTools/*.Tests.ps1`, `tools/*.Tests.ps1`)? Jsou rychlé
   a běží na obou OS, ale nejsou součástí `dotnet test`.
7. Dependabot pro `github-actions` (`.github/dependabot.yml`), aby se připnuté SHA aktualizovaly? T-180 má Dependabot
   security updates bez `dependabot.yml`.

## Poznámky k implementaci
(vyplní agent po dokončení)
