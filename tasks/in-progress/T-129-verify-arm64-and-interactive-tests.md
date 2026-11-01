---
id: T-129
title: windows-verify – kontrola buildu win-arm64 a testy interaktivního běhu
status: in-progress
owner: qa-reviewer
priority: P2
platform: windows
depends_on: [T-017, T-105, T-125]
---

## Cíl
Uživatel 2026-11-01: „jsi schopen na T-125 napsat testy, ať to nemusím ručně testovat?“ – rozhodnuto automatizovat
body T-017 (instalátor `win-arm64`) a T-105 (celý interaktivní běh `tools/windows-verify.ps1`) z T-125.

## Rozsah
1. **T-017 – `win-arm64`:** nová kontrola ve `tools/windows-verify.ps1` (např. `win.arm64-build`): `pwsh
   scripts/release-build.ps1 -Runtime win-x64,win-arm64` vytvoří i `LayerPeek-<verze>-win-arm64-Setup.exe` a soubory
   kanálu `win-arm64` bez kolize s `win-x64`; nic se nenahrává. Bez komponenty MSVC ARM64 (`docs/prerequisites.md`,
   T-063) se kontrola přeskočí s důvodem a postupem doinstalace. Spuštění na zařízení ARM64 se neověřuje (T-057).
2. **T-105 – interaktivní běh:** Pester testy průchodu `tools/windows-verify.ps1` v interaktivním režimu se simulovanými
   odpověďmi (y / n / přeskočit u kontrol druhu Permission a Manual), přerušení uprostřed a dokončení obnovy
   `-RestoreOnly`, správný souhrn a report; bez skutečných změn systému (podvržené akce kontrol).
3. Dokumentace (`docs/windows-manual-testing.md`) a T-125: body odkazují na nové kontroly / testy.

## Akceptační kritéria
- [ ] Kontrola `win-arm64` ve `windows-verify.ps1` (+ `-ListChecks`), přeskočení bez komponenty s důvodem
- [ ] Pester testy interaktivního běhu a `-RestoreOnly`, spouštěné i z `dotnet test` (`LayerPeek.Scripts.Tests`)
- [ ] V T-125 u bodů T-017 a T-105 odkaz na kontrolu / testy
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
(žádné)
