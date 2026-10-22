---
id: T-067
title: Testovací režim – volitelné toasty, Správce pověření, záznam otevíraných adres, zpoždění startu a složka dialogu (jen Debug)
status: todo
owner: ui-dev
priority: P2
platform: windows
depends_on: [T-044]
---

## Cíl
Testovací režim T-044 (jen Debug build) záměrně vypíná všechno, co sahá na stav Windows uživatele: toasty, a s nimi zástupce v nabídce Start, Správce pověření a spuštění prohlížeče. Kvůli tomu E2E testy nemohou ověřit toasty (T-010), Správce pověření (T-052) a otevření webu. Tento úkol přidá **volitelné** přepínače testovacího režimu, které tyto části zapnou **izolovaně** (vlastní AUMID, vlastní název zástupce, vlastní předpona pověření). Přidá také dva pomocné přepínače pro deterministické testy menu a dialogu. Release build se nemění, přepínače se v něm vůbec nepřekládají (`#if DEBUG`), stejně jako v T-043, T-044 a T-046.

## Rozhodnuto
- **Prohlížeč v testovacím režimu (uživatel 2026-10-22, dřív Q1):** otevírané adresy se v testovacím režimu **vždy** (bez dalšího přepínače) zapisují do souboru a test je otevře sám přes `HttpClient`. Prohlížeč se nespouští.
- **Úklid toastů po pádu (uživatel 2026-10-22, T-068 Q2):** Debug-only režim aplikace (např. `PrusaTrayDiagnostic.exe --ptd-e2e-clear-toasts`), který použije vlastní WinRT interop aplikace. Cílová platforma testovacího projektu se nemění.

## Rozsah
Všechny přepínače platí jen v testovacím režimu (`PRUSATRAYDIAGNOSTIC_TEST_DATA_DIR`). Bez testovacího režimu se ignorují a zalogují. Názvy jsou návrh, implementace je může upravit a zapsat do `AppSwitches` testů.

1. **Toasty** – `PRUSATRAYDIAGNOSTIC_TEST_TOASTS=1`:
   - místo `NullNotificationService` skutečná `ToastNotificationService` / `WindowsToastPlatform` s AUMID testovacího režimu `PrusaTrayDiagnostic.E2E` (už existuje pro okna a proces);
   - zástupce s tímto AUMID v nabídce Start uživatele pod **jiným názvem** než produkční (`Prusa Tray Diagnostic E2E.lnk`), ukazující na testovaný exe. Produkční zástupce `Prusa Tray Diagnostic.lnk` se nikdy nečte ani nemění (test);
   - při `quit` aplikace vymaže svou historii toastů (`ToastNotificationHistory.Clear` pro testovací AUMID přes existující vlastní WinRT interop) a smaže svého zástupce;
   - **úklid po pádu (rozhodnutí 2026-10-22):** Debug-only režim `--ptd-e2e-clear-toasts` (název jde upravit). Exe v něm nespustí Avalonii ani `SingleInstanceGuard`, jen vymaže historii toastů testovacího AUMID `PrusaTrayDiagnostic.E2E`, smaže testovacího zástupce `Prusa Tray Diagnostic E2E.lnk` a skončí s kódem 0 (chyba = nenulový kód a popis na stderr). Na produkční AUMID ani zástupce nikdy nesáhne. Volá ho úklid T-068 (záznam T-065);
   - nic z toho nesmí mít vliv na produkční AUMID `PrusaTrayDiagnostic` (test).
2. **Správce pověření** – `PRUSATRAYDIAGNOSTIC_TEST_CREDENTIAL_PREFIX=<předpona>`:
   - místo `TestModeSecretStore` skutečný `WindowsCredentialSecretStore` s danou předponou cíle (konstruktor předponu už podporuje);
   - povolená je jen předpona začínající `PrusaTrayDiagnostic.E2E/`, jinak aplikace skončí kódem 4 jako u neplatného testovacího režimu (test). Produkční předpona `PrusaTrayDiagnostic` tak nejde použít.
3. **Záznam otevíraných adres** – v testovacím režimu vždy, bez přepínače (rozhodnutí 2026-10-22):
   - `IUrlLauncher` (a `OpenSecretAsync`) nespustí prohlížeč. Adresu připíše do `opened-urls.txt` v testovací datové složce (jeden řádek na adresu, zápis atomicky po řádcích). Test si adresu přečte, otevře ji sám přes `HttpClient` (proxy T-028 je na `127.0.0.1`) a soubor se smaže s testovací složkou;
   - do logu se adresa proxy dál nezapisuje (token, T-028);
   - `IFolderLauncher` (T-048, otevření složky logů) se nemění, test T-064 skutečný Průzkumník potřebuje.
4. **Zpoždění startu** – `PRUSATRAYDIAGNOSTIC_TEST_START_DELAY_MS=<0–60000>`: monitorování se spustí o tolik později, takže tray menu ukazuje „Spouštění…“ dost dlouho na to, aby test otevřel menu a sledoval přepnutí „Diagnostika…“ při otevřeném menu (T-042 bod 6). Ikona i menu existují hned, zpožděný je jen `MonitoringSession.Start`.
5. **Výchozí složka dialogu pro vlastní obrázek** (T-039): v testovacím režimu `IImageFilePicker` nastaví `SuggestedStartLocation` na testovací datovou složku, stejně jako `BundleStartDirectory` v T-064. Psaní cesty do dialogu přes UIA není spolehlivé (T-064), test proto vybere soubor v seznamu.
6. **Kontrola Release:** `IconAndBuildTests` (seznam textů, které se nesmí objevit v Release DLL ani v Release AOT exe) doplnit o nové názvy přepínačů, argument `--ptd-e2e-clear-toasts` a název testovacího zástupce.

## Akceptační kritéria
- [ ] Toasty v testovacím režimu jen s přepínačem; testovací AUMID a testovací zástupce; produkční zástupce ani AUMID se nedotknou; při `quit` se historie a zástupce uklidí (unit testy s falešnou platformou a úložištěm zástupců; skutečný zástupce jen v rezervované testovací složce)
- [ ] Debug-only režim `--ptd-e2e-clear-toasts`: vymaže historii a zástupce testovacího AUMID, produkčních se nedotkne, nespustí Avalonii ani instanci, skončí kódem 0 (test v podřízeném procesu); v Release neexistuje
- [ ] Správce pověření v testovacím režimu jen s přepínačem a předponou `PrusaTrayDiagnostic.E2E/`; jiná předpona → kód 4 (test v podřízeném procesu)
- [ ] Záznam otevíraných adres místo prohlížeče vždy v testovacím režimu (bez přepínače), bez adresy proxy v logu (testy)
- [ ] Zpoždění startu monitorování (test)
- [ ] Dialog vlastního obrázku začíná v testovací složce v testovacím režimu (test)
- [ ] Release DLL a Release AOT exe neobsahují žádný nový přepínač ani název testovacího zástupce (rozšířený `IconAndBuildTests`)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish win-x64 bez varování; žádný nový NuGet balíček

## Otevřené otázky
- (žádné – Q1 rozhodnuta 2026-10-22, viz Rozhodnuto)

## Poznámky k implementaci
(vyplní agent po dokončení)
