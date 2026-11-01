---
id: T-130
title: Zrušit podporu Windows ARM64 (win-arm64)
status: done
owner: qa-reviewer
priority: P2
platform: windows
depends_on: []
---

## Cíl
Uživatel 2026-11-01: „odstraň celou podporu pro win arm, nemám stroj, na kterém bych to mohl testovat, tak aktuálně to
nebudu ani podporovat.“ Windows se vydává a podporuje jen jako `win-x64`. macOS (`osx-arm64`, případně `osx-x64`) se
nemění.

## Rozsah
1. Skripty vydání a sestavení (`scripts/release-build.ps1`, `release-upload.ps1`, `release-publish.ps1`,
   `scripts/ReleaseTools/*`, `scripts/aot-smoke.ps1`, `build/FFmpeg.targets`, `build/update-ffmpeg*`): žádný runtime,
   kanál ani artefakt `win-arm64`; parametry, které ho přijímaly, ho odmítnou se srozumitelnou chybou (nebo ho už
   nenabízejí). Testy skriptů (Pester) upravit.
2. Aplikace a testy (`src/**`, `tests/**`): logika kanálu aktualizací / licencí / FFmpeg pro `win-arm64` odstranit nebo
   zjednodušit; fixtures upravit jen tam, kde testují win-arm64 (historické odpovědi GitHub Releases mohou zůstat, pokud
   testují ignorování neznámých souborů).
3. Dokumentace (`README.md`, `README.cz.md`, `docs/**`, `CHANGELOG.md` – záznam v Unreleased, `CLAUDE.md` pokud zmiňuje):
   podporované platformy jen Windows x64 a macOS; `docs/prerequisites.md` bez komponenty MSVC ARM64.
4. Úkoly: T-057 (FFmpeg win-arm64) a T-063 (build win-arm64) zamítnout (přesunout do `tasks/rejected/`
   se `status: rejected` a poznámkou „Zamítnuto (rozhodnutí uživatele 2026-11-01, T-130)“), bod T-017 win-arm64 v T-125 odškrtnout s poznámkou o zrušení; zmínky
   v T-059 a T-016 upravit. Uzavřené úkoly v `done/` jako historii neměnit.
5. macOS beze změny (zachovat všechny `osx-arm64` / Apple Silicon zmínky).

## Akceptační kritéria
- [x] Žádný `win-arm64` v kódu, skriptech, buildu a aktuální dokumentaci (kromě historie v `tasks/done/` a záznamu
      v CHANGELOG); grep v poznámkách
- [x] `pwsh scripts/release-build.ps1` (win-x64) projde; testy skriptů a `dotnet test` bez chyb a varování
- [x] T-057 a T-063 zrušené, T-125 upravený
- [x] macOS beze změny

## Otevřené otázky
(žádné)

## Poznámky k implementaci
- **Skripty:** `win-arm64` odstraněn z tabulky `$script:ReleaseRuntimes` (`scripts/ReleaseTools/ReleaseTools.psm1`)
  i z kontroly komponenty MSVC (`Get-NativeToolchainProblems`). `release-build.ps1 -Runtime win-arm64` teď skončí
  chybou `Unknown runtime 'win-arm64'. Released runtimes: win-x64, osx-arm64.`; značka sestavení s `win-arm64` hlásí
  `unknown runtime`. `scripts/aot-smoke.ps1` odmítne každý Windows RID kromě `win-x64` (`AOT smoke check NOT RUN:
  runtime 'win-arm64' is not supported …`, exit 1, nic nepublikuje); macOS RID (`osx-arm64`, Intel `osx-x64`) se
  záměrně neomezují. `release-upload.ps1` / `release-publish.ps1` parametr RID nemají, změnily se jen texty nápovědy.
  Obecný mechanismus `Uploadable = $false` / `BlockedReason` zůstal (žádný RID ho dnes nepoužívá); testy ho ověřují nad
  umělou tabulkou (`-Config`), takže pokrytí nekleslo.
- **Pester:** testy `win-arm64` přepsané na `win-x64` / `osx-arm64` nebo na umělé RID; nové testy: odmítnutí
  `win-arm64` v `Resolve-ReleaseRuntimes`, tabulka obsahuje přesně `win-x64` a `osx-arm64`, `win-arm64` ve značce
  sestavení = neznámý RID, `aot-smoke.ps1 -Runtime win-arm64` / `win-x86` skončí chybou a nic nespustí.
  `build/update-ffmpeg.Tests.ps1` už `winarm64` asset odmítá (negativní test, ponechán).
- **Aplikace a testy C#:** žádná logika `win-arm64` tam nebyla (`VelopackUpdateEngine` bere kanál z instalace,
  `ThirdPartyLicenses`, `FFmpegFactAttribute` a licence testy znají jen `win-x64` a `osx-arm64`); beze změny.
  `build/FFmpeg.targets` a `build/update-ffmpeg.ps1` `win-arm64` nikdy nepodporovaly; beze změny.
- **Dokumentace:** README.md / README.cz.md (řádek tabulky „❌ not supported / nepodporováno“, bod plánu odstraněn),
  CHANGELOG (Unreleased → Removed), `docs/prerequisites.md` (bez komponenty MSVC ARM64 a oddílu „ARM64 build tools“,
  nová řádka v Častých chybách), `docs/release.md` (oddíl „Povolení win-arm64“ nahrazen „Konfigurace RID“),
  `docs/velopack.md` (bod 8 označen jako zrušený záznam spiku), `docs/architecture.md`, `docs/windows-manual-testing.md`.
  `CLAUDE.md` `win-arm64` nezmiňuje, beze změny. Navíc (hlavní agent, mimo T-130): v README.md / README.cz.md
  odstraněn bod plánu „MMU3 status / Stav MMU3“ (T-023 zamítnutý) a odpověď FAQ o práci mimo domácí síť říká, že
  podpora Prusa Connect se neplánuje (bez veřejného API).
- **Vydání:** podle `CHANGELOG.md` a fixture `github-releases-2026-10-30.json` žádné zveřejněné vydání (0.1.0, 0.2.0
  jen `win-x64`; 0.3.0, 0.3.1 macOS / `win-x64`) soubory `win-arm64` neobsahovalo, takže žádní uživatelé nezůstanou
  bez aktualizací.
- **Úkoly:** T-057 a T-063 zamítnuté (`tasks/rejected/`, `status: rejected`, řádek „Zamítnuto …“ pod frontmatterem,
  nesplněná kritéria nezaškrtnutá s „→ zamítnuto (T-130)“; konvence podle develop). V T-125 bod T-017 `win-arm64`
  odškrtnutý s „→ zrušeno (T-130)“, T-059 jen architektura `x64`. T-016 záměrně neupraven (zamítá se na develop).
  Řádky v `tasks/README.md` upravené, přidán řádek T-130.
- **Grep `(?i)win-arm64|winarm|arm64` – co zůstává:** macOS (`osx-arm64` v celém repu, `build/macos/*`,
  `MacUnixNative.cs`, `ObjCRuntime.cs`, `MacUpdateE2ETests.cs`, `tools/macos-verify.ps1`, `FFmpegFactAttribute.cs`,
  `LicensesViewModelTests.cs`, `docs/macos-manual-testing.md`, macOS úkoly), obecná architektura (`Get-HostArchitecture`,
  výstup MTP `(net11.0|arm64)` v `tools/VerifyTools.Tests.ps1`), záměrná odmítnutí a vysvětlení zrušení (testy
  odmítnutí v `ReleaseTools.Tests.ps1` a `update-ffmpeg.Tests.ps1`, nápověda skriptů, CHANGELOG, README, docs
  s odkazem na T-130), záznam spiku v `docs/velopack.md` (bod 8, řádek 60), historie v `tasks/done/` a zamítnuté
  T-057 / T-063 v `tasks/rejected/`, T-125 (odškrtnutý bod) a tento úkol.
- **Ověření 2026-11-01 (Windows x64):** `dotnet build` 0 varování, 0 chyb; `dotnet test` 8946 celkem, 0 selhalo,
  8673 prošlo, 273 přeskočeno (vč. `LayerPeek.Scripts.Tests` / Pester: 220 prošlo, 0 selhalo). `pwsh
  scripts/release-build.ps1` (win-x64, verze 0.3.1, špinavý strom = jen lokálně, nic nenahráno) prošel napodruhé:
  dist = zip, `.nupkg`, feed `win-x64`, `Setup.exe`, `SHA256SUMS-windows.txt`, AOT smoke test prošel. První běh
  spadl na občasném selhání `StatusWidgetTests.StatusIsRegisteredWithItsNamePriorityAndSizes` (`Assert.Same` nad
  `StatusWidgetTemplate.Card`, Release) bez souvislosti s T-130; 3 samostatná opakování prošla (k samostatnému úkolu).
  `release-build.ps1 -Runtime win-arm64` a `aot-smoke.ps1 -Runtime win-arm64` skončí s kódem 1 a srozumitelnou hláškou.
- Review 2026-11-01 bez blokujících nálezů; uzavřeno 2026-11-01.
