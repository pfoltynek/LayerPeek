---
id: T-132
title: run-e2e.ps1 – uchovat výsledky neúspěšných běhů
status: in-progress
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
- [x] Neúspěšný běh nechá zachovanou kopii výsledků, souhrn ukáže její cestu
- [x] Úklid starých kopií, nic mimo dočasnou složku výsledků se nemaže
- [x] Pester testy, `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
(žádné)

## Poznámky k implementaci
- Logika je v `tools/RunE2E.Results.psm1` (testovatelná bez E2E běhu): `Get-PassesToKeep` (které průchody uchovat:
  každý neúspěšný; když selhal jen návrat zásahů po běhu, všechny), `Save-FailedTestResults` (kopie do
  `<složka>-failed-<yyyyMMdd-HHmmss>`, při shodě v téže sekundě přípona `-2`, `-3`…), `Remove-OldFailedTestResults`
  (úklid) a `Get-DefaultFailedResultsToKeep` (10).
- `tools/run-e2e.ps1` volá uchování a úklid ve `finally` hned po návratu zásahů, takže kopie vznikne i po Ctrl+C;
  chyba kopie ani úklidu běh neshodí (jen varování). Souhrn u průchodu vypíše „kept copy of the failed run …“. Úspěšný
  běh nic nekopíruje. Nápověda skriptu (`.DESCRIPTION`) popisuje kopie i úklid.
- **Kopie, ne přejmenování:** do složky může ještě psát aplikace nebo hlídací proces zabitého testovacího procesu
  (`system-changes-watchdog.log`); přejmenování by pak selhalo nebo by zápis pokračoval do kopie. Soubory se čtou se
  sdílením pro zápis, takže se zkopírují i otevřené logy; nečitelný soubor se přeskočí s varováním. Odkazy (junction)
  uvnitř se nekopírují. Složku výsledků další běh beztak smaže a vytvoří znovu (`E2EFixture.PrepareResultsDirectory`).
- **Úklid – ponechat 10 nejnovějších na složku výsledků** (Debug a `-aot` zvlášť), podle razítka v názvu, na konci
  každého běhu. Počet místo stáří: místo na disku je omezené bez ohledu na to, jak často se běží, a podklady k selhání
  nezmizí jen proto, že se E2E dva týdny nespouštěly (např. dovolená); 10 pokryje i den s opakovanými pokusy. Hodnota je
  v nápovědě skriptu a v `docs/troubleshooting.md`; parametr skriptu pro změnu jsem nepřidával (lze doplnit).
- **Ochrana proti mazání cizích složek:** obě funkce odmítnou (výjimkou) cokoli kromě složky přímo v dočasné složce
  (`[IO.Path]::GetTempPath()`) s názvem přesně `LayerPeek.App.E2E.Results.<8 hex>[-aot]`; složka-odkaz se odmítne.
  Úklid maže jen složky přímo tam s názvem přesně `<složka>-failed-<8 číslic>-<6 číslic>[-<n>]` a se značkou testů
  `.layerpeek-e2e-results`; soubory, odkazy, složky bez značky, kopie druhého průchodu nebo jiného worktree zůstanou.
  Mazání vlastní procházkou: odkaz uvnitř kopie se odstraní sám, jeho cíl ne (`Directory.Delete(…, true)` na junction
  uvnitř padal s „Access denied“). Značka se maže až poslední a každá kopie má vlastní `try`: kopie se zamčeným
  souborem (např. antivir) jen vypíše varování, zůstane se značkou a smaže ji příští běh; starší kopie se smažou i tak
  (review).
- Testy: `tools/RunE2E.Results.Tests.ps1` (Pester, 21 testů: výběr průchodů při úspěchu/selhání, kopie včetně
  otevřeného logu a `-aot`, žádná kopie bez složky/značky, odmítnutí cizích názvů, cest mimo temp a odkazů, úklid
  podle razítka, nic cizího se nesmaže, junction uvnitř i jako kopie, zamčený soubor ve staré kopii, napojení v `run-e2e.ps1`), spouštěné z
  `tests/LayerPeek.Scripts.Tests/PesterScriptTests.cs`. Testy junction běží jen na Windows. Skutečný E2E běh se
  nespouštěl.
- Dokumentace: nová sekce „Kde jsou výsledky E2E běhu (i neúspěšného)“ v `docs/troubleshooting.md`.
