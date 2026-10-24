---
id: T-064
title: E2E testy logování a podpůrných akcí, scénář simulátoru „malformed“
status: done
owner: qa-reviewer
priority: P2
platform: windows
depends_on: [T-024, T-044, T-045, T-048, T-049]
---

## Cíl
Ruční kontroly z T-024, T-048 a T-049 ověřit automaticky na skutečné aplikaci (E2E testy T-044), aby je nebylo nutné opakovat ručně:
- **T-024:** log v testovacím režimu má správný formát a zalogovaná výjimka má celý stack trace s názvy metod, a to i v Native AOT buildu.
- **T-048:** O aplikaci → „Otevřít složku s logy“ otevře Průzkumníka se složkou (i když ještě neexistovala); „Uložit diagnostický balíček…“ přes skutečný dialog uloží ZIP s logy, `info.txt` a `settings.json` bez tajných údajů.
- **T-049:** „Kopírovat info do schránky“ vloží do schránky text s informacemi a posledními řádky logu a tlačítko pro UI Automation ohlásí „✓ Zkopírováno“.

## Rozsah
- **Simulátor (`tools/PrusaSimulator`):** nový scénář `malformed`: tiskárna odpovídá na stavové dotazy poškozeným JSONem, takže aplikace zaloguje výjimku (`Printer '…' sent a malformed response.`) i se stack trace. Přepínatelný za běhu jako ostatní scénáře, popsaný v `docs/simulator.md`, s testy simulátoru.
- **AOT:** `tools/run-e2e.ps1 -PublishAot` navíc publikuje Native AOT v konfiguraci Debug (má testovací režim) a E2E testy logu poběží i proti němu.
- **E2E testy (`tests/PrusaTrayDiagnostic.App.E2E.Tests`):**
  - log: formát řádku, PID, výjimka se stack trace s názvy metod (proti Debug buildu a s `-PublishAot` i proti AOT),
  - „Otevřít složku s logy“: najít okno Průzkumníka se složkou logů testovacího režimu, pak ho zavřít,
  - „Uložit diagnostický balíček…“: ovládat skutečný dialog Uložit jako přes UI Automation, uložit do testovací složky, zkontrolovat obsah ZIP,
  - „Kopírovat info do schránky“: obsah schránky a název tlačítka přes UI Automation.
- Testy po sobě nic nenechají (testovací složky T-032, zavřená okna Průzkumníka, obnovená schránka).

## Akceptační kritéria
- [x] Scénář `malformed` v simulátoru (příkazová řádka, přepnutí za běhu, `docs/simulator.md`, testy simulátoru)
- [x] E2E test logu: formát a výjimka se stack trace s názvy metod; s `-PublishAot` i proti Native AOT buildu
- [x] E2E test „Otevřít složku s logy“ (i když složka neexistovala), okno Průzkumníka se zavře
- [x] E2E test „Uložit diagnostický balíček…“ přes skutečný dialog; ZIP bez tajných údajů
- [x] E2E test „Kopírovat info do schránky“; textový obsah schránky uživatele se po testu obnoví
- [x] `dotnet build` a `dotnet test` bez chyb a varování; `tools/run-e2e.ps1` i `-PublishAot` zelené

## Rozhodnuto
- **AOT (2026-10-22):** E2E proti Native AOT publish v konfiguraci Debug (testovací režim existuje jen v Debug). Umístění logu v Release buildu pokrývá unit test cesty.
- **Výjimka v logu (2026-10-22):** nový scénář simulátoru `malformed`, žádný testovací příkaz v aplikaci.
- **Schránka (2026-10-22):** test si textový obsah schránky uloží a po testu ho vrátí (ne-textový obsah se ztratí).
- **Předčítání (Narrator):** automaticky nejde; E2E ověří název tlačítka a `LiveSetting` přes UI Automation, ze kterých čtečka čte.
- **Historie schránky (2026-10-22):** testovací režim (jen Debug) přidá ke kopírovanému textu formát `ExcludeClipboardContentFromMonitorProcessing`, takže se nedostane do historie schránky Windows (Win+V) ani do cloudové schránky. Release se nemění.
- **Průzkumník (2026-10-22):** test zavře jen okno, ve kterém je složka logů jedinou záložkou. Když se složka otevře jako záložka ve tvém okně Průzkumníka, nechá ji otevřenou (složka se po testu stejně smaže) a vypíše upozornění.

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- **Simulátor:** scénář `malformed` (`PrinterScenario.Malformed`) se chová jako `idle`, ale `/api/v1/status` vrátí uříznutý JSON (`SimulatedPrinter.MalformedStatus`). Verze, informace o tiskárně a přihlášení fungují normálně. Přepíná se z příkazové řádky (`--scenario malformed`), přes `POST /_simulator/scenario/malformed` i příkazem konzole. Testy: zpracování všech názvů scénářů, přepnutí přes HTTP a surová odpověď, klient aplikace hlásí `UnexpectedResponse` a po návratu na `idle` se zotaví.
- **Úpravy aplikace (jen testovací režim, Release beze změny):**
  - Dialog Uložit jako se v testovacím režimu otevírá v testovací datové složce (`SupportActions.BundleStartDirectory` → `SuggestedStartLocation`). Bez toho první verze testu uložila balíček do uživatelových Dokumentů; ověřený soubor jsem hned smazal. Psaní cesty do dialogu přes UI Automation nebylo spolehlivé.
  - Kopie do schránky dostane formát pro vyloučení z historie schránky.
  - Zápisy testu do schránky (zkušební text i obnovený text uživatele) mají stejný formát, takže v historii schránky nezůstane nic navíc.
- **E2E (`LogAndSupportTests`):**
  - Log: každý řádek je záznam (čas s posunem, úroveň, PID, kategorie) nebo odsazené pokračování. Varování „sent a malformed response“ má celou výjimku se stack trace s názvy metod (`System.Text.Json…`, `PrusaLinkClient`). S `-PublishAot` běží tento test i proti Native AOT exe konfigurace Debug (se svou vlastní složkou výsledků).
  - Otevřít složku s logy: složka se nejdřív smaže; po kliknutí ji Průzkumník ukáže (`Shell.Application`) a test ho pak zavře.
  - Uložit balíček: skutečný dialog, Uložit s výchozím názvem, kontrola obsahu ZIP a nepřítomnosti hesla PrusaLink a přihlašovacích údajů i tokenu kamery. Pojistka smaže balíček tohoto testu, kdyby se ocitl v Dokumentech, na Ploše nebo ve Stažených souborech.
  - Kopírovat info: zkušební zápis a čtení schránky (ověří, že obnova funguje), pak název tlačítka „✓ Copied“, `LiveSetting=Polite` přes UIA, formát vyloučení z historie a obsah textu. Na konci se textový obsah schránky obnoví a test to ověří.
- **Infrastruktura:**
  - `E2EFixture` přijme i Native AOT exe (bez `.dll`), pokud obsahuje přepínač testovacího režimu.
  - `tools/run-e2e.ps1 -PublishAot` publikuje i Debug AOT do `bin/Debug/net11.0/win-x64/publish`.
  - `PTD_E2E_APP` se po běhu vrátí na původní hodnotu.
- **Co testy zanechají:** dialog Uložit jako si zapamatuje poslední složku (MRU záznam pro Debug exe) a soubor se může objevit v Nedávných, stejně jako smazaná testovací složka logů v Rychlém přístupu. To jsou vedlejší účinky skutečného shellu, které test nemůže vrátit.
- **Ověřeno (2026-10-22):** `dotnet build` bez varování; `dotnet test` 4571 zelených; `tools/run-e2e.ps1 -PublishAot`: 30/30 E2E a test logu proti Native AOT exe zelené. Výsledky obou běhů zůstaly (`…Results.<id>` a `…Results.<id>-aot`). Žádný nový NuGet balíček.
- **Návaznost:** ruční kontroly T-024 a T-048 tím odpadly (oba úkoly přesunuté do done). U T-049 zbývá jen skutečné poslechnutí Předčítání, které automatizovat nejde.
