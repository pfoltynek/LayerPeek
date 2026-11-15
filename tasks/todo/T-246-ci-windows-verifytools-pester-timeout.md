---
id: T-246
title: CI Windows – Pester VerifyTools.Tests.ps1 překračuje 5 min (macos-verify -ListChecks 66 s na runneru, lokálně < 1 s)
status: todo
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
- [ ] Příčina zapsaná a opravená; `VerifyTools.Tests.ps1` v CI na Windows bezpečně pod limitem
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
