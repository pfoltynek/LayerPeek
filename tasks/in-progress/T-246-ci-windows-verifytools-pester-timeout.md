---
id: T-246
title: CI Windows – Pester VerifyTools.Tests.ps1 překračuje 5 min (macos-verify -ListChecks 66 s na runneru, lokálně < 1 s)
status: in-progress
owner: qa-reviewer
priority: P2
platform: windows
depends_on: []
---

## Cíl
Ruční běh CI 43757948661 (2026-11-15, commit 113a7757, `windows-2025`): `PesterScriptTests.ScriptPesterTestsPass`
pro `tools/VerifyTools.Tests.ps1` skončil po limitu 5 min (`pwsh.exe did not finish within 00:05:00`). Test
„macos-verify.ps1 -Unattended -ListChecks lists checks of its OS…“ trval na runneru **65,9 s**, lokálně na Windows
celý soubor proběhne za 30 s (109 testů, tento test pod 1,1 s). Zjistit, co je na runneru tak pomalé (prostředí:
síť / DNS, hledání příkazů, Defender, první spuštění `pwsh` …), a opravit příčinu.

## Rozsah
- Z logu běhu (`gh run view 43757948661 --log-failed`, výstup Pester) určit pomalé testy; reprodukovat lokálně v
  podmínkách blízkých runneru (čistý profil, bez cache modulů, omezené CPU).
- Opravit příčinu ve skriptu nebo testu (např. zbytečné spouštění externích nástrojů, čekání na timeout sítě);
  zvýšit limit v `PesterScriptTests` jen se zdůvodněním, když jde čistě o pomalejší runner.
- Ověřit ručním během CI (spustí hlavní agent).

## Akceptační kritéria
- [ ] Příčina zapsaná a opravená; `VerifyTools.Tests.ps1` v CI na Windows bezpečně pod limitem (příčina a oprava
  hotové, viz poznámky; čeká na ruční běh CI, který spustí hlavní agent)
- [x] `dotnet build` a `dotnet test` bez chyb a varování (`dotnet build` 0 varování; `LayerPeek.Scripts.Tests`
  25 prošlo / 0 selhalo / 1 přeskočen na vyžádání, 5:45; ostatních sestav se změna nedotýká)

## Otevřené otázky
- Omezit v CI souběh testovacích sestav (`dotnet test --max-parallel-test-modules N`) na 4jádrovém Windows runneru?
  Zmírnilo by vyhladovění všech testů s potomky procesů (Pester, UI testy, T-245), ale může prodloužit celkový čas
  kroku. Rozhodnutí uživatele; T-246 to nemění.
  - **Rozhodnuto uživatelem 2026-11-15:** změřit v samostatném úkolu **T-247** a podle měření rozhodnout.

## Poznámky k implementaci
**Příčina.** Běh 43757948661: `VerifyTools.Tests.ps1` běžel na runneru 20:30:38–20:35:39, tedy přesně v okně, kdy
`dotnet test --solution` souběžně pouští ostatní testovací sestavy (UI testy, Detection, Core, E2E, App.Tests běžely do
~20:38) na 4 CPU. Sestavy běží paralelně, řádky teorie `PesterScriptTests` za sebou (podle TRX), takže
VerifyTools padá do špičky. V tomto zatížení stál holý potomek `pwsh -Command 'exit 3'` 5 s (lokálně 0,4 s) a každý
z ~18 potomků pwsh testového souboru 5–66 s. Historie téhož souboru v CI: 1:58, 2:21, 4:22, >5:01.

Konkrétní zesilovač pomalého `-ListChecks`: konzolový hostitel PowerShellu má náklad za **každý vypsaný objekt**
(nejspíš volání konzole/conhost, mechanismus jsem přímo neověřil), který při obsazených CPU silně roste. Lokální reprodukce
(pwsh s afinitou na 2 jádra + 2 procesy plně vytěžující CPU, `-NoProfile`): 200 řádků přes `Write-Output` po
jednom = 7,9 s, tytéž řádky jako jeden text = 0,27 s (bez zátěže 64 ms vs. 40 ms). `macos-verify.ps1 -ListChecks`
(204 řádků) v potomkovi: 13–14 s, s výstupem zahozeným 4,4 s; přesměrování do souboru nepomáhá (14 s), takže to
není rouru čtoucí rodič. Proto byl `-ListChecks` (204 řádků) na runneru výrazně pomalejší než `-SelfTest` (1 řádek,
3,4 s). Síť, git, auto-loading modulů (načtou se jen Management/Utility + vlastní moduly), dlouhý PATH ani prázdná
cache analýzy modulů (`PSModuleAnalysisCachePath`) lokálně nic nezpomalily.

**Oprava.**
- `Format-VerifyCheckList -AsText` (`tools/VerifyTools.psm1`) vrací tytéž řádky jako jeden text; `macos-verify.ps1` a
  `windows-verify.ps1` ho s `-ListChecks` vypisují. Výstup na stdout je bajt po bajtu stejný jako dřív (ověřeno
  `cmp` pro `macos-verify -ListChecks`, `-ListChecks -Preset release`, `windows-verify -ListChecks`), na macOS se
  pro uživatele nic nemění. Pod zátěží: `macos-verify -ListChecks` v potomkovi 13–14 s → 5,5 s. Nový Pester test
  „gives the same list as one text with -AsText…“ (napsaný předem, nejdřív červený).
- `VerifyTools.Tests.ps1`: stejný běh jen pro čtení `macos-verify.ps1 -ListChecks -Preset release` (4 testy) se
  spouští jednou (`Invoke-ScriptOnce`) – o 3 potomky pwsh méně.
- Test „ends when its parent process ends“: rodič se už neukončuje pevným `Start-Sleep -Seconds 6`, test ho zabije
  sám, až server ohlásí adresu. Rychlejší (8 s → 0,5–2,6 s) a bez souběhu: server, který by startoval déle než 6 s
  (zatížený runner), by rodiče už neviděl a test by selhal.
- Limit `PesterScriptTests` pro `VerifyTools.Tests.ps1` 5 → 10 min se zdůvodněním v komentáři: soubor dál spouští
  ~15 potomků pwsh, jejichž start je na zatíženém runneru čistě pomalejší (5 s na holý pwsh), a běží ve špičce
  ostatních sestav – stejný důvod jako 10 min u ReleaseTools (T-218). Limit je pojistka proti zaseknutí, ne měření
  rychlosti.

**Měření** (celý `VerifyTools.Tests.ps1`, „Tests completed“): bez zátěže 23,2 s → 13,6 s; se zátěží (2 jádra, 2
vytěžující procesy) 236 s (wall 4:02) → 166 s jen se zkrácením testů → 140 s (wall 2:26) i s `-AsText`. Jednotlivě
pod zátěží: `-ListChecks` mac 14,7 s → 6,2 s, test s presetem 26,7 s → 5,8 s, rodič fake GitHub API 8,1 s → 2,6 s,
tři další testy presetu po ~12 s → < 0,1 s.

**Ostatní dlouhé testy z logu:** „lists one release…“ (7,5 s) a „restores the environment…“ (5 s) jen startují
potomka pwsh – nutné, bez pevného čekání. „passes and leaves nothing behind“ (5,3 s) běží v procesu, čisté CPU.
Pozorování mimo rozsah: ostatní soubory Pester mají v CI stejný rozptyl (migrate 11 s – 2:13), příčinou je souběh
sestav na 4 CPU; omezení paralelismu `dotnet test` (např. `--max-parallel-test-modules`) by pomohlo všem, ale mění
CI a souvisí s T-245 – viz otevřené otázky.

**Zbývá:** ověření ručním během CI na Windows (spustí hlavní agent) – první kritérium nechávám nezaškrtnuté do jeho
výsledku.
