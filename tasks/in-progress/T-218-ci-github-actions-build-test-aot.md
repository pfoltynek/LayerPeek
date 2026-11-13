---
id: T-218
title: CI – GitHub Actions na Windows a macOS (build, testy bez E2E, Native AOT publish) při pushi do `develop` a v PR
status: in-progress
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
- [x] `.github/workflows/ci.yml`: `permissions: contents: read`, žádné `secrets`, všechny akce připnuté na SHA
      s komentářem verze, `concurrency` s `cancel-in-progress`, `timeout-minutes` u každého jobu
- [ ] Zelený běh na GitHubu pro **macOS** i **Windows** (build Debug + Release, `dotnet test` bez E2E, AOT publish);
      odkazy na běhy v poznámkách – *čeká na první běh po pushi (ověří hlavní agent)*
- [ ] Druhý běh bez změny FFmpeg použije cache FFmpeg (v logu žádný build / stažení FFmpeg) na obou OS; změna
      `build/macos/build-ffmpeg.sh` dá na macOS nový klíč – *čeká na runner (lokálně ověřeno, viz poznámky)*
- [ ] Úmyslně rozbitý test v PR (jen na zkoušku, pak zahodit) skončí červeně a nahraje log testů – *čeká na runner*
- [ ] Žádný E2E / interaktivní test v CI neběží (seznam přepínačů zapsaný); počet přeskočených testů zapsaný a zdůvodněný
      – *seznam přepínačů hotový (`docs/prerequisites.md`), přeskočené testy macOS lokálně zapsané; počty z runneru
      (hlavně Windows) chybí*
- [x] `docs/prerequisites.md` doplněné; vazba na T-202 zapsaná v T-202
- [x] `dotnet build` a `dotnet test` lokálně bez chyb a varování

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

Rozhodnuto uživatelem 2026-11-13:
1. **Pushovat z Macu** (token `gh` má scope `workflow`); na Windows `gh auth refresh -s workflow` až tehdy, kdyby se
   workflow měnilo odtud.
2. **Ano, povinná kontrola** v rulesetu (změnu rulesetu dělá uživatel nebo agent s jeho výslovným souhlasem; úkol
   připraví přesný postup). Proto **bez `paths-ignore`**: místo toho job, který při změně jen dokumentace / úkolů
   hned projde, aby povinná kontrola nikdy nečekala.
3. **Ano**, i PR do `main` a push do `release/*` / `hotfix/*`.
4. Neodpovězeno – neblokuje; implementace navrhne (doporučeno připnuté obrazy kvůli předvídatelnosti, Dependabot
   je neaktualizuje – zapsat do poznámek) a zeptá se při review.
5. **Ano**, odznak CI v README (EN i CZ).
6. **Ano**, Pester testy v CI.
7. **Ano**, `.github/dependabot.yml` pro `github-actions`.

Nové otázky z implementace (2026-11-13, neblokují):
8. **Obrazy runnerů (navazuje na otázku 4):** navrženo a použito připnuté `windows-2025` (dnes stejný obraz jako
   `windows-latest`: Windows Server 2025 s VS 2026), `macos-26` (dnes `macos-latest`, Apple Silicon) a `ubuntu-24.04`
   pro malé joby. Výhoda: obraz se nezmění pod rukama (macOS knihovny FFmpeg se překládají jeho Xcodem, obraz je proto
   i v klíči cache). Nevýhoda: Dependabot obrazy neaktualizuje, nový obraz je potřeba nastavit ručně, až GitHub ohlásí
   konec starého. Ponechat připnuté, nebo přejít na `*-latest`?
9. **Časové limity jobů:** zatím prozatímně 60 minut pro oba buildy (lokálně na M2 Pro 8 min, runner bude pomalejší).
   Po prvních bězích nastavit ~1,5× naměřené doby (úkol to předepisuje); hlavní agent / další úprava.

## Poznámky k implementaci
Stav 2026-11-13 (qa-reviewer, na Macu): workflow, Dependabot, test pravidel a dokumentace hotové a lokálně ověřené.
Úkol zůstává v `in-progress/`: zelený běh na GitHubu, cache při druhém běhu, červený běh s rozbitým testem a počty
přeskočených testů z runnerů ověří hlavní agent po pushi (nepřesouvá se do T-125 / T-127).

**Soubory**
- `.github/workflows/ci.yml` – tři joby:
  - `Changes` (`ubuntu-24.04`) zjistí změněné soubory: u PR `git diff HEAD^1 HEAD` slučovacího commitu (checkout
    `fetch-depth: 2`), u pushe `git diff <before> HEAD` (předchozí commit se stáhne `--depth=1`; nový branch, vynucený
    push nebo neúspěšné stažení = plný běh), `workflow_dispatch` = vždy plný běh.
  - `Build and test (Windows / macOS)` (matice, `fail-fast: false`) jen při změně kódu.
  - `CI result` (`if: always()`) = jediná povinná kontrola: projde, když build prošel, nebo nebyl potřeba; zrušený nebo
    neúspěšný build = červená (zrušený běh tedy povinnou kontrolu nesplní).
- `.github/dependabot.yml` – `github-actions`, týdně, PR do `develop`, `cooldown` 7 dní, jedna skupina, prefix `ci`.
- `tests/LayerPeek.Scripts.Tests/CiWorkflowTests.cs` – hlídá pravidla bezpečnosti workflow (SHA + komentář verze, jediný
  blok `permissions: contents: read`, žádné `secrets.` / `pull_request_target` / `workflow_run`, každý checkout
  `persist-credentials: false`, `timeout-minutes` u každého jobu, `cancel-in-progress`, existence Dependabotu).
  Ověřeno mutací: nepřipnutá akce, chybějící timeout a `persist-credentials: true` testy shodí.
- `docs/prerequisites.md` – nový oddíl „CI (GitHub Actions)“ (co běží, kde, cache, rychlé projití dokumentace,
  přepínače E2E, **postup pro povinnou kontrolu v rulesetu**), věta v úvodu o CI opravená.
- `README.md` / `README.cz.md` – odznak CI (`ci.yml`, větev `develop`). `CONTRIBUTING.md` – bod 6 o kontrole
  `CI result`. `tasks/todo/T-202-…` – stav vazby na CI.

**Akce připnuté na SHA** (zjištěno 2026-11-13 přes `gh api repos/<akce>/releases/latest` a
`gh api repos/<akce>/git/ref/tags/<tag>`; všechny tagy jsou lehké, ukazují přímo na commit; všechny běží na `node24`):

| Akce | Tag | Commit |
|---|---|---|
| `actions/checkout` | v7.0.1 | `3d3c42e5aac5ba805825da76410c181273ba90b1` |
| `actions/setup-dotnet` | v6.1.0 | `0f4a354dd5547a28c70b7853dbd95d5b67886e73` |
| `actions/cache` (+ `/restore`, `/save`) | v6.1.0 | `55cc8345863c7cc4c66a329aec7e433d2d1c52a9` |
| `actions/upload-artifact` | v7.0.2 | `cf430e030ddbb5b0abf93d22962f4752f3646cd9` |

**Rozhodnutí v implementaci**
- **Rychlé projití místo `paths-ignore` (rozhodnutí 2):** za „jen dokumentaci“ se bere, když je každý změněný soubor
  v `tasks/`, `docs/` nebo `*.md` v kořeni. **Odchylka od rozsahu:** `manuals/**` a `docs/troubleshooting.md` se
  počítají jako kód, protože je čtou testy (`UserManualTests` – kotvy příručky, `NotificationAndUpdateInfrastructureTests`
  – id testovací aplikace). S `paths-ignore` podle původního rozsahu by změna příručky mohla rozbít test bez povšimnutí.
  Pravidlo pro budoucí testy čtoucí dokumentaci je v `docs/prerequisites.md`. Klasifikátor ověřen lokálně skriptem
  s náhradním `git` (PR jen s úkoly/docs/README = přeskočit; příručka, `docs/troubleshooting.md`, `src/`, `.github/`,
  `global.json`, README ve `tests/`, prázdný diff, nový branch, neúspěšný fetch, `workflow_dispatch`, název s diakritikou
  v uvozovkách = plný běh).
- **Spouštění (rozhodnutí 3):** push do `develop`, `release/**`, `hotfix/**`; PR do `develop` a `main`; ruční.
- **Pořadí kroků:** Release build (stáhne / přeloží FFmpeg) → uložení cache FFmpeg hned po něm (`actions/cache/restore` +
  `/save`, takže selhání testů cache nezahodí) → Debug build → `dotnet test` Debug `--no-build` → AOT publish → při
  selhání artefakt. Testy jen v Debug (jako lokálně), Release se jen sestaví.
- **Vynechání `tests/LayerPeek.E2E.Tests`:** filtr řešení `ci-tests.slnf` se generuje při běhu z `dotnet sln list` bez
  tohoto projektu; krok selže, když projekt nenajde právě jednou (přejmenování by jinak potichu spustilo testy Edge).
  Nový testovací projekt se tak do CI dostane sám. `LayerPeek.App.E2E.Tests` na macOS není testovací aplikace
  (`IsTestProject=false`), na Windows se jeho testy bez `LAYERPEEK_E2E=1` přeskočí.
- **Pester (rozhodnutí 6):** běží uvnitř `dotnet test` (`ScriptPesterTestsPass`); krok `Check PowerShell and Pester`
  selže, když na runneru chybí Pester 5+ (obrazy mají Pester 5.9.0, PowerShell 7.6.9), aby se nepřeskočil potichu.
  Nic se neinstaluje.
- **Cache:** NuGet `~/.nuget/packages` (klíč OS + hash `Directory.Packages.props`, `**/*.csproj`, `global.json`,
  `restore-keys` podle OS; `NUGET_XMLDOC_MODE=skip`). FFmpeg v `$RUNNER_TEMP/ffmpeg` (přes `GITHUB_ENV`,
  `Join-Path` kvůli příkazu `move` v cmd), klíč `ffmpeg-v1-<obraz>-<rid>-<hash>`; hash z `build/FFmpeg.targets` a položky
  matice `ffmpeg-script` (macOS `build/macos/build-ffmpeg.sh`, Windows vzor `build/windows/build-ffmpeg.*` pro T-202,
  dnes bez shody). Bez `restore-keys`. `v1` v klíči dovolí cache ručně zneplatnit.
- **Runnery:** `windows-2025` (Visual Studio Enterprise 2026 18.11 s `Microsoft.VisualStudio.Workload.NativeDesktop`
  a `VC.Tools.x86.x64`, Edge 156; podle README obrazu 20261103), `macos-26` arm64 (macOS 26.7, Xcode CLT 26.6, obraz
  20261102). .NET 11 na obrazech není (nejvyšší 10.0.403), instaluje ho `setup-dotnet` podle `global.json`
  (`latestFeature` → nejvyšší 11.0.x GA). MSVC z VS 2026 pro AOT .NET 11 zatím neověřeno – ověří první běh.
- **Žádné tajné údaje, žádný token:** i zjišťování změn používá jen anonymní `git fetch` veřejného repozitáře (u
  soukromého by fetch selhal a proběhl by plný běh).

**Lokální ověření (macOS 27, M2 Pro, 2026-11-13)**
- `actionlint` není k dispozici (ani Docker / Go), nic se neinstalovalo. Místo něj skript v Ruby (systémový Psych):
  syntaxe YAML obou souborů, povinné klíče jobů a kroků, `uses` xor `run`, odkazy `needs.*`, `steps.<id>`, `matrix.*`
  a výstupy jobů na existující položky, vyvážené `${{ }}`. Vstupy a výstupy akcí ověřeny z jejich `action.yml` na
  připnutých SHA (`cache-primary-key` v `cache/restore`, `persist-credentials`, `global-json-file`, `retention-days`…).
- Příkazy jobu macOS spuštěné stejně jako ve workflow (pwsh, čerstvý klon, prázdná cache FFmpeg v dočasné složce,
  teplá NuGet cache): Pester 5 nalezen; `build -c Release` 116 s (vč. překladu FFmpeg), `build -c Debug` 14 s,
  `dotnet test` přes vygenerovaný `.slnf` 324 s, `publish -r osx-arm64` 28 s; vše 0 varování. TRX vznikly pro všech
  8 testovacích projektů (`--results-directory-layout per-module`). Druhý build se stejnou cache FFmpeg nic nepřekládá.
- Testy v CI simulaci (macOS): **10 507 celkem, 10 240 prošlo, 0 selhalo, 267 přeskočeno.** Důvody přeskočení:
  88 snímků README na požádání (`LAYERPEEK_README_SCREENSHOTS`), ~165 testů jen pro Windows (registr, Credential
  Manager, toasty, COM, mutexy/roury, zástupci, zamykání souborů…), 4 sweepy detektorů (`LAYERPEEK_DETECTION_SWEEP`),
  5 E2E macOS (`LAYERPEEK_E2E`), 1 `LAYERPEEK_VERIFY_UNATTENDED`, 1 bez nahraných odpovědí PrusaLink (fixtures),
  1 Local Network (macOS 15+). Žádný test Klíčenky se lokálně nepřeskočil (klíčenka odemčená); na runneru se počet
  může lišit (viz níže).

**Zbývá ověřit na GitHubu (hlavní agent po pushi)**
- zelený běh obou jobů, odkazy na běhy sem; doby jobů → `timeout-minutes` na ~1,5× (otázka 9);
- druhý běh: `Restore FFmpeg` s `cache-hit`, v logu buildu žádné „Downloading FFmpeg“ / „Building FFmpeg from source“;
- PR s úmyslně rozbitým testem: `CI result` červený, artefakt `test-results-<rid>`; PR jen s úkolem: build přeskočen,
  `CI result` zelený;
- počty přeskočených testů na obou runnerech (hlavně Windows: mělo by odpadnout ~165 testů „jen Windows“ a přibýt testy
  „jen macOS“; Klíčenka na `macos-26` – přeskočení s důvodem, ne selhání), MSVC z VS 2026 pro AOT;
- že Dependabot `dependabot.yml` přijme (Insights → Dependency graph → Dependabot, bez chyby konfigurace);
- pak povinná kontrola `CI result` v rulesetu 2 podle postupu v `docs/prerequisites.md` (dělá uživatel).
- Pozn. pro hlavního agenta: definice agenta `.claude/agents/qa-reviewer.md` (a případně `CLAUDE.md`) dosud říká, že CI
  je odložené (T-016) – po sloučení aktualizovat.
