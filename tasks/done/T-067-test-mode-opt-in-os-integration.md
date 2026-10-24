---
id: T-067
title: Testovací režim – volitelné toasty, Správce pověření, záznam otevíraných adres, zpoždění startu a složka dialogu (jen Debug)
status: done
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
- **Trvání pověření v testovacím režimu (uživatel 2026-10-22, při revizi):** pověření pod `PRUSATRAYDIAGNOSTIC_TEST_CREDENTIAL_PREFIX` se zapisují s `CRED_PERSIST_SESSION` (nic na disk, nic neroamuje, po odhlášení zmizí samo i po pádu běhu), ne s `CRED_PERSIST_ENTERPRISE`. Produkce beze změny (`CRED_PERSIST_ENTERPRISE`).
- **Neplatné zpoždění startu (uživatel 2026-10-22, při revizi):** neplatná hodnota `PRUSATRAYDIAGNOSTIC_TEST_START_DELAY_MS` ukončí start kódem 4, stejně jako neplatná předpona pověření (fail fast, žádné tiché ignorování).

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
- [x] Toasty v testovacím režimu jen s přepínačem; testovací AUMID a testovací zástupce; produkční zástupce ani AUMID se nedotknou; při `quit` se historie a zástupce uklidí (unit testy s falešnou platformou a úložištěm zástupců; skutečný zástupce jen v rezervované testovací složce)
- [x] Debug-only režim `--ptd-e2e-clear-toasts`: vymaže historii a zástupce testovacího AUMID, produkčních se nedotkne, nespustí Avalonii ani instanci, skončí kódem 0 (test v podřízeném procesu); v Release neexistuje
- [x] Správce pověření v testovacím režimu jen s přepínačem a předponou `PrusaTrayDiagnostic.E2E/`; jiná předpona → kód 4 (test v podřízeném procesu)
- [x] Záznam otevíraných adres místo prohlížeče vždy v testovacím režimu (bez přepínače), bez adresy proxy v logu (testy)
- [x] Zpoždění startu monitorování (test)
- [x] Dialog vlastního obrázku začíná v testovací složce v testovacím režimu (test)
- [x] Release DLL a Release AOT exe neobsahují žádný nový přepínač ani název testovacího zástupce (rozšířený `IconAndBuildTests`)
- [x] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish win-x64 bez varování; žádný nový NuGet balíček

## Otevřené otázky
- (žádné – Q1 rozhodnuta 2026-10-22, viz Rozhodnuto)

## Poznámky k implementaci
Vše jen v Debug (`#if DEBUG`), Release se nemění. Žádný nový NuGet balíček, žádné potlačení varování AOT/trim.

**Přepínače** (`AppEnvironment`, Debug; hodnoty zrcadlí `AppSwitches` E2E testů):
| Přepínač | Hodnota | Neplatná hodnota |
|---|---|---|
| `PRUSATRAYDIAGNOSTIC_TEST_TOASTS` | `1` = zapnuto, cokoli jiného = vypnuto | – |
| `PRUSATRAYDIAGNOSTIC_TEST_CREDENTIAL_PREFIX` | musí začínat `PrusaTrayDiagnostic.E2E/` (velikost písmen přesně), za lomítkem aspoň jeden znak, bez mezer | kód 4 (jako neplatný testovací režim) |
| `PRUSATRAYDIAGNOSTIC_TEST_START_DELAY_MS` | celé číslo 0–60000 | kód 4 |
| argument `--ptd-e2e-clear-toasts` | režim úklidu | – |

- Bez `PRUSATRAYDIAGNOSTIC_TEST_DATA_DIR` se přepínače ignorují: `AppEnvironment.IgnoredTestSwitches`, `Program.Main` je zaloguje jako Warning. Parsování je v `AppEnvironment.FromVariables` (testovatelné bez procesu).
- **Toasty:** `AppServices.CreateNotificationService` v testovacím režimu s přepínačem vytvoří `WindowsToastPlatform` s AUMID `PrusaTrayDiagnostic.E2E` a zástupcem `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Prusa Tray Diagnostic E2E.lnk` (`AppShortcutRegistration.ShortcutPathFor`). `WindowsToastPlatform` dostal parametr AUMID (výchozí produkční). Při `quit` `TrayHost.StopAsync` po uvolnění služeb počká, až skončí vlákno toastů (`ToastNotificationService.Completion`, limit 2 s; jinak by registrace běžící při rychlém ukončení mohla zástupce zapsat znovu až po úklidu), a pak spustí `TestToastCleanup` (mimo vlákno UI, limit 1 s): `ToastNotificationHistory.ClearWithId("PrusaTrayDiagnostic.E2E")` a smazání testovacího zástupce. AUMID a název zástupce jsou v `TestToastCleanup` pevné, cesta s jiným názvem souboru se odmítne, takže produkční nejde smazat.
- **WinRT interop:** nové `IToastNotificationManagerStatics2` (`get_History`) a `IToastNotificationHistory` (pořadí metod podle `windows.ui.notifications.h`, SDK 10.0.26100), obal `WinRtToastHistory` (vstup do MTA na dobu volání). Ověřeno: `ClearWithId` pro AUMID bez toastů nevyhodí výjimku a nevytvoří klíč v `…\Notifications\Settings`.
- **`--ptd-e2e-clear-toasts`:** v `Program.Main` hned po omezení hledání DLL (T-052), ještě před testovacím režimem, logováním a `SingleInstanceGuard`. Nic dalšího nespouští, nepotřebuje testovací proměnné. Chyba: kód 1 a popis na stderr (mimo Windows taky kód 1).
- **Správce pověření:** `WindowsCredentialSecretStore(prefix, CRED_PERSIST_SESSION)` (rozhodnutí 2026-10-22, viz Rozhodnuto; produkce dál `CRED_PERSIST_ENTERPRISE`, obojí hlídá `TestModeSwitchesTests`). Položky přežijí restart aplikace v rámci přihlášení (načtení hesla po restartu v T-073 funguje), po odhlášení zmizí. Cíl je `<předpona>/<klíč>`, např. `PrusaTrayDiagnostic.E2E/run-1/printer:p1`. Mazání těchto položek po testu je dál na testech (T-073), aplikace je nemaže.
- **Záznam adres:** `RecordingUrlLauncher` nahradí v testovacím režimu `ProcessUrlLauncher` celé (menu, toasty, odkazy v okně O aplikaci a v licencích). Stejná kontrola adres (`PrinterWebAddress.IsSafeToLaunch`). Každý řádek (`AbsoluteUri` + `\n`, UTF-8) se zapíše jedním voláním `Write` pod zámkem, se sdílením `ReadWrite | Delete`, takže čtenář vidí jen celé řádky (kromě případného posledního). Do logu jde jen host. `IFolderLauncher` beze změny.
- **Zpoždění startu:** `MonitoringSession.Start(settings, startDelay)`. Ikona i menu vzniknou hned, smyčka monitorování nejdřív počká (`Task.Delay` s `TimeProvider` z DI a stop tokenem; testy běží na `FakeTimeProvider`, takže pomalý stroj nemůže start uspíšit). Stop během čekání nic nespustí, nastavení uložené během čekání se použije pro první běh.
- **Dialog obrázku:** `IImageFilePicker.PickAsync(startDirectory)` → `SuggestedStartLocation`; `SettingsViewModel.ImageStartDirectory` nastaví `AppServices.CreateSettingsViewModel` na testovací složku jen v testovacím režimu (jako `BundleStartDirectory` v T-064).
- **E2E (`TestModeOsIntegrationTests`)** běží jen v `tools/run-e2e.ps1` (sdílená nabídka Start a oznámení testovací aplikace; běhy všech worktree se střídají přes zámek plochy, takže běžný `dotnet test` v jiném worktree nikdy nesmaže zástupce probíhajícímu E2E běhu). Ověřují: zástupce testovací aplikace vznikne při startu s přepínačem (míří na testovaný exe, nese AUMID), `quit` ho smaže; bez přepínače žádný nevznikne; po zabité aplikaci ho `--ptd-e2e-clear-toasts` smaže, skončí 0 s prázdným stderr, nezapíše nic do testovací složky (aplikace neběžela) a druhé spuštění skončí taky 0; produkční zástupce je po běhu byte po bytu stejný; klíč `…\Notifications\Settings\PrusaTrayDiagnostic.E2E` po běhu bez toastu nevznikl. Testy na začátku i v `finally` uklidí pozůstatek předchozího pádu. Až bude hotová infrastruktura T-065, patří zástupce do jejího záznamu zásahů (T-068).
- `AppInstance.StartAsync` umí předat další přepínače (`switches`) a `StartInfo` zděděné přepínače T-067 odstraní (drobná změna infrastruktury E2E sdílené s T-065).
- `IconAndBuildTests` kontroluje navíc: tři nové proměnné, `PrusaTrayDiagnostic.E2E/`, `--ptd-e2e-clear-toasts`, `Prusa Tray Diagnostic E2E.lnk`, `Prusa Tray Diagnostic E2E` a `opened-urls.txt`.
- **Ověření (Windows 11, 2026-10-22):** `dotnet build` 0 varování; `dotnet test` 4663 testů, 0 selhání (37 přeskočených: E2E bez `PTD_E2E` a jeden dřívější); `PrusaTrayDiagnostic.App.Tests` v Release (`ReleaseBuildsHaveNoTestMode` rozšířený o nové typy) zelené; `tools/run-e2e.ps1 -PublishAot` zelené: 36 testů (1 přeskočený: skutečná kamera nedostupná) + 4 testy proti Debug AOT exe, Release AOT publish win-x64 bez varování a čerstvý Release AOT exe i Release DLL bez nových přepínačů (`IconAndBuildTests`). Po běhu v nabídce Start jen produkční `Prusa Tray Diagnostic.lnk`, klíč `…\Notifications\Settings\PrusaTrayDiagnostic.E2E` neexistuje. Skutečný toast testovací aplikace (a tím i skutečné vymazání neprázdné historie) ověří až T-068.
- **Revize (qa-reviewer, 2026-10-22):** pověření testovacího režimu na `CRED_PERSIST_SESSION` (+ test, že produkce zůstala `CRED_PERSIST_ENTERPRISE`); úklid při `quit` čeká na konec vlákna toastů (`ToastNotificationService.Completion`, + testy); zpoždění startu přes `TimeProvider` (testy na `FakeTimeProvider` místo reálného času). Po opravách: `dotnet build` 0 varování; `dotnet test` 4670 testů, 0 selhání (37 přeskočených); `PrusaTrayDiagnostic.App.Tests` v Release zelené; `tools/run-e2e.ps1 -PublishAot` zelené (36 testů, 1 přeskočený + 4 proti Debug AOT exe, Release AOT publish bez varování); v nabídce Start po běhu jen produkční zástupce.
