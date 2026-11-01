---
id: T-132
title: run-e2e.ps1 – uchovat výsledky neúspěšných běhů
status: todo
owner: qa-reviewer
priority: P3
platform: windows
depends_on: []
---

## Cíl
Snímky, logy aplikace a `test-results.xml` neúspěšného E2E běhu přepíše hned další běh, protože `tools/run-e2e.ps1`
používá stále stejnou složku výsledků (`%TEMP%\LayerPeek.App.E2E.Results.<id>`). Při T-123 a T-128 (2026-11-01) se tak
ztratily podklady k selhání dřív, než je šlo rozebrat. Uživatel 2026-11-01: „Ano, nový úkol“.

## Rozsah
1. Když běh skončí selháním, `run-e2e.ps1` zkopíruje (nebo přejmenuje) jeho složku výsledků na
   `<složka>-failed-<RRRRMMDD-HHmmss>` a cestu vypíše v souhrnu; úspěšné běhy beze změny.
2. Staré kopie se uklízejí (např. ponechat posledních N, nebo starší než X dní) – rozumná výchozí hodnota, popsaná
   v nápovědě skriptu.
3. Testy skriptu (Pester) pro kopii při selhání, žádnou kopii při úspěchu a úklid starých kopií; dokumentace
   (`docs/windows-manual-testing.md` nebo kde se popisují výsledky E2E).

## Akceptační kritéria
- [ ] Neúspěšný běh nechá zachovanou kopii výsledků, souhrn ukáže její cestu
- [ ] Úklid starých kopií, nic mimo dočasnou složku výsledků se nemaže
- [ ] Pester testy, `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
(žádné)
