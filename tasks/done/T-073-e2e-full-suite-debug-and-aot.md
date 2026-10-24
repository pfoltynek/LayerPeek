---
id: T-073
title: E2E – celá sada proti Debug i Native AOT (Debug) a test Správce pověření (uzavření T-052)
status: done
owner: qa-reviewer
priority: P2
platform: windows
depends_on: [T-067, T-068, T-069, T-070, T-071, T-072]
---

## Cíl
Uzavřít poslední kritérium T-052: tray, okna, toasty, Správce pověření a kamera (FFmpeg) fungují v Debug buildu i v Native AOT, a E2E testy procházejí. Hledání nativních knihoven je omezené (`SetDefaultDllDirectories`), takže je potřeba ověřit, že se nic nerozbilo ani v AOT. Dnes `tools/run-e2e.ps1 -PublishAot` pouští proti Native AOT exe jen testy logu a pádů (T-064, T-046). Tento úkol pustí proti němu **celou sadu** a přidá E2E test skutečného Správce pověření.

## Pokrytí
| Úkol | Kontrola | Jak |
|---|---|---|
| T-052 | tray v Debug i AOT | `TrayMenuTests` + T-069 v obou průchodech |
| T-052 | okna v Debug i AOT | `SettingsAndDiagnosticsTests`, `CameraWindowTests`, T-071 v obou průchodech |
| T-052 | toasty v Debug i AOT | T-068 v obou průchodech |
| T-052 | Správce pověření v Debug i AOT | nový `CredentialManagerTests` |
| T-052 | kamera (FFmpeg) v Debug i AOT | `CameraWindowTests` se simulovanou kamerou (živý obraz se mění) v obou průchodech; FFmpeg DLL ze složky publish |
| T-052 | web přes proxy | ručně ověřeno 2026-10-21 (Debug i AOT); navíc GET přes proxy v T-068/T-069 v obou průchodech |
| T-052 | E2E testy T-044 a T-030 procházejí | celý `tools/run-e2e.ps1 -PublishAot` + `dotnet test` (T-030 s Edge) |
| T-035 | názvy monitorů (`MonitorNameNative`) v AOT | `SettingsListsTheRealMonitorNames` v průchodu AOT |

## Rozhodnuto
- **Native AOT v konfiguraci Debug stačí (uživatel 2026-10-22, dřív Q1):** AOT část kritéria T-052 splní E2E sada proti Native AOT exe konfigurace Debug, stejně jako u T-064. Krátké ruční proklikání Release AOT exe je jen doporučená kontrola, která uzavření neblokuje.

## Rozsah
- **`tools/run-e2e.ps1 -PublishAot`:** po zeleném průchodu proti Debug exe spustit **celou vybranou sadu** (respektuje `-Class` / `-Method`) proti `bin/Debug/net11.0/win-x64/publish/PrusaTrayDiagnostic.exe`, s vlastní složkou výsledků (`…Results.<id>-aot`). Testy logu a pádů v něm zůstanou. Testy, které proti AOT nedávají smysl (např. kontrola Release DLL), se v AOT průchodu přeskočí s důvodem (`E2EFixture.IsNativeAot`). Varování T-065 se ukáže jednou na začátku a platí pro oba průchody. Souhrn na konci uvede oba průchody a počty prošlých, přeskočených a neúspěšných testů.
- **Porty:** simulátory běží na portech přidělených systémem (`0`), jako dnes, takže obsazený port 8080 (např. Aspire) nevadí. Pevné porty 18080/18554 z poznámky T-052 platí jen pro ruční spuštění simulátoru.
- **`CredentialManagerTests`** (T-067 `PRUSATRAYDIAGNOSTIC_TEST_CREDENTIAL_PREFIX`, předpona `PrusaTrayDiagnostic.E2E/<id worktree>/<běh>`):
  - start bez hesla, v nastavení zadat heslo simulátoru, Uložit → v Credential Manageru existuje `…/printer/<id>` (ověří test přes `CredRead`) a monitorování se přihlásí (stav ≠ odmítnuté přihlášení);
  - ukončit a spustit znovu se stejnou předponou → přihlásí se bez zadání hesla (heslo se načetlo z úložiště);
  - odebrat tiskárnu a Uložit → položka zmizí;
  - heslo se neobjeví v logu ani v `settings.json` (kontrola souborů testovací složky);
  - **úklid:** `CredEnumerate("PrusaTrayDiagnostic.E2E/*")` a smazání na konci testu, při startu a konci běhu (pod zámkem plochy). Ostatní položky uživatele se nečtou ani nemění (filtr jen na tuto předponu).
- **Release Native AOT:** dál jen kontrola, že neobsahuje testovací přepínače (T-044, T-067). Spuštění Release exe by sáhlo na data uživatele, proto se testy proti němu nespouští (rozhodnutí T-064 pro log a 2026-10-22 pro T-052). Krátké ruční proklikání Release AOT exe (tray, okno kamery se simulátorem, jeden toast) je jen **doporučená** kontrola a uzavření T-052 neblokuje.

## Akceptační kritéria
- [x] `tools/run-e2e.ps1 -PublishAot` spustí celou sadu proti Debug exe i proti Native AOT exe konfigurace Debug; oba průchody zelené, souhrn obou
- [x] `CredentialManagerTests` v obou průchodech: uložení, načtení po restartu, smazání; po běhu žádná položka `PrusaTrayDiagnostic.E2E/…`
- [x] Kamera se simulátorem (FFmpeg) ukazuje živý obraz v obou průchodech
- [x] `dotnet test` (včetně T-030) zelené
- [x] Kritérium T-052 „Ruční ověření na Windows v AOT publish i v Debug…“ zaškrtnuté podle výsledků obou průchodů (AOT část splňuje Native AOT v konfiguraci Debug, rozhodnutí 2026-10-22); v poznámkách T-052 zapsané proklikání Release AOT exe jako doporučená kontrola; T-052 přesunutý do `done/`
- [x] Ostatní úkoly řady (T-010, T-025, T-035, T-037, T-039, T-041, T-042, T-043) zkontrolované: kritéria zaškrtnutá podle výsledků obou průchodů, zbývající doporučené kontroly zapsané v jejich poznámkách, úkoly přesunuté do `done/`
- [x] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish win-x64 bez varování

## Otevřené otázky
- (žádné – Q1 rozhodnuta 2026-10-22, viz Rozhodnuto)

## Poznámky k implementaci

**`tools/run-e2e.ps1 -PublishAot`**
- Po zeleném průchodu proti Debug exe běží **stejný výběr** (`-Class` / `-Method`) proti `bin/Debug/net11.0/win-x64/publish/PrusaTrayDiagnostic.exe`, výsledky v `…Results.<id>-aot`. Varování T-065 se ukáže jednou a platí pro oba průchody.
- Každý průchod ukládá zprávu běhu (`-result-xml`) do své složky výsledků jako `test-results.xml`. Souhrn na konci (`tools/RunE2E.Results.psm1`, jednotkové testy v `RunE2EScriptTests`) vypíše pro každý průchod počty prošlých, přeskočených a neúspěšných testů, případně „not run (the Debug pass failed)“.
- Testy se spouští jako samostatný proces, který píše přímo do konzole, ne přes rouru. Roura (`| Out-Host`) čekala na každý proces, který ji zdědil. Testovací aplikace, která zůstala po zabitém testovacím procesu, tak zablokovala skript ještě před vrácením změn.
- V průchodu AOT se přeskočí jen `IconAndBuildTests.ReleaseBuildsContainNoTestSwitches`: AOT exe nemá spravovanou assembly, se kterou by se Release porovnal. Release se kontroluje v průchodu Debug.

**`CredentialManagerTests.PasswordIsSavedLoadedAfterARestartAndDeletedWithThePrinter`** (trait `Credentials`)
- Předpona `PrusaTrayDiagnostic.E2E/<id worktree>/<náhodná>`, simulátor s jedinečným heslem. Průběh testu:
  - Bez hesla diagnostika ukáže „Invalid credentials“.
  - Heslo zadané v nastavení a Uložit: `CredRead` najde `…/printer/<id>` s `CRED_PERSIST_SESSION` a správným heslem, stav je „Ready“.
  - Restart se stejnou předponou a uloženým `settings.json`: „Ready“ bez zadání hesla.
  - Odebrání tiskárny a Uložit: položka zmizí.
  - Ani v jednom kroku není heslo v žádném souboru testovací složky (log, `settings.json` aj., UTF-8 i UTF-16).
- `Infrastructure/TestCredentials.cs`: `CredEnumerate` volá jen s filtrem `PrusaTrayDiagnostic.E2E/*` a každý výsledek znovu kontroluje. Jiná předpona vyhodí výjimku ještě před voláním Windows. Hesla se jen porovnávají, nikdy se nevrací ani nelogují.
- Úklid:
  - Zásah `Credentials` v záznamu T-065 (klíč = předpona testu). Návrat smaže vše pod ní, i po pádu: při startu dalšího běhu, v hlídacím procesu nebo přes `-RestoreOnly`.
  - Fixture maže `PrusaTrayDiagnostic.E2E/*` na začátku a na konci běhu, pod zámkem plochy.
  - `system-state-*.txt` položky vypisuje. Položka, která zbude na konci, se počítá jako rozdíl a běh selže.
  - Ruční postup je v `docs/troubleshooting.md`.

**Win+N zachycené jiným programem (nález uživatele 2026-10-24)**
- Běh kolem 2:48 otevřel OneNote. Nástroj Office „Odeslat do OneNotu“ (`ONENOTEM.EXE /tsr`) si zaregistroval Win+N pro novou rychlou poznámku. Stisk Win+N v `NotificationCenter` (Nerušit T-065, toasty T-068) proto spustil OneNote místo centra oznámení.
- Oprava: centrum oznámení se otevírá **vyvoláním tlačítka hodin na hlavním panelu přes UI Automation** (`Shell_TrayWnd`, automation id `SystemTrayIcon`, třída `SystemTray.OmniButton`, ověřeno na buildu 26300).
  - Nestiskne se žádná klávesa, takže to žádný program nemůže zachytit.
  - Win+N už testy vůbec nepoužívají, hlídá to jednotkový test `NoTestPressesWinN`.
  - Okno jiného programu, které mezitím dostane popředí (např. okno za zavřeným flyoutem), test jen přečká a nikdy se ho nedotkne.
  - Escape jde jen do centra oznámení, když je v popředí.
- Win+B (testy tray menu z klávesnice): když po stisku přijde do popředí okno jiného programu, test hned skončí s chybou, která uvádí jen název procesu, a žádnou další klávesu nestiskne. Šipky, Enter a Shift+F10 se posílaly jen při fokusu v oznamovací oblasti už dřív. Jiné globální zkratky testy nepoužívají. OneNote uživatele testy nezavírají.
- Ruční postupy v textech záznamu a v `docs/troubleshooting.md` teď zní „klikněte na hodiny na hlavním panelu (nebo Win+N)“.

**Nestabilní testy – příčiny a opravy**
- `LogAndSupportTests.SaveDiagnosticBundleThroughTheRealSaveDialog`: `Wait.UntilAsync` bral timeout UI Automation (`UIA_E_TIMEOUT` 0x80131505) jako selhání testu. K timeoutu dochází, když je UI vlákno aplikace chvíli zaneprázdněné, pravděpodobně při zavírání systémového dialogu Uložit. Teď se takový timeout bere jako „ještě ne“ a čeká se dál do termínu; platí to pro `COMException` i pro `TimeoutException`, kterou z něj udělá runtime nebo FlaUI. Vlastní timeout `Wait` je `WaitTimeoutException` a vnější čekání ukončí. Testy: `WaitTests`.
- `TrayMenuTests.RealTrayMenuShowsThePrinterSubmenuAndTogglesTheCamera`: flyout přetečení zůstal z předchozího kroku zobrazený, ale bez popředí (to mělo právě otevřené okno kamery). Pravý klik na ikonu v něm menu neotevřel. Dokládá to stopa (flyout se znovu neotevíral) a snímek (tooltip ikony, žádné menu). Zastaralý flyout se teď nejdřív skryje a otevře znovu s popředím.
- `DisplayChangeTests.SwitchedOffMonitorOfAKilledProcessIsRestoredByTheStartCleanup`: hlídací proces předchozího testu (varianta s hlídacím procesem) ještě vracel záznamy. Vrací všechny záznamy mrtvých procesů, takže vzal i záznam dalšího testu. Test s hlídacím procesem teď čeká, až hlídací proces skončí (`SystemChangeWatchdog.WaitUntilGoneAsync`, WMI).
- `PrinterImageTests.SavedImageChangesTheMenuWithoutReconnecting`: čtení řádku menu ve chvíli, kdy ho aplikace pro nový obrázek přestavuje, jednou skončilo `COMException` z UIA. Takové čtení se teď zopakuje.
- `CameraTopmostTests.TopmostKeepsTheCameraAboveAnotherApp`: krycí okno nezískalo popředí. Okno kamery otevřené těsně předtím se aktivovalo opožděně a popředí si vzalo zpět. `CoverWindow.BringToFrontAsync` to teď zkusí až třikrát a ověří, že popředí vydrží 300 ms. Při selhání uvede proces a třídu okna v popředí.
- Jednorázové chyby, příčina nenalezena:
  - `SettingsAndDiagnosticsTests…(it)`: „window has no area to capture“. Okno nastavení zmizelo, v logu aplikace nic není. `Screenshots.Capture` teď vypisuje, zda okno existuje, zda je viditelné a minimalizované, a jeho obdélník.
  - V jednom průchodu AOT ve 4:30 v noci selhaly `TaskbarJumpListTests` (jump list se nezavřel po „Zavřít okno“ ani po Escape) a `ShellStartTests` (kurzor myši se během kliknutí přesunul na 0,1195). Vypadá to na vliv prostředí (shell, vstup); v dalších dvou plných bězích oba testy prošly.
  - Při ladění jednou po samostatném testu Nerušit hlásil runner xUnit „Foreground threads were left running“: 3× po sobě, pak už ani jednou v 6 dalších bězích. Příčinu se nepodařilo určit.

**Ověření (Windows 11 26300, 2 × DELL U2719D, po sloučení `develop` 3fb7d7e)**
- `pwsh tools/run-e2e.ps1 -PublishAot` dvakrát po sobě (2026-10-24 kolem 4:40 a 5:08), oba průchody zelené:

  | Průchod | Prošlo | Přeskočeno | Selhalo | Celkem |
  |---|---|---|---|---|
  | Debug | 406 | 6 | 0 | 412 |
  | Native AOT (Debug) | 405 | 7 | 0 | 412 |

  Pokaždé „System changes (after the run): nothing pending“, v obou průchodech „Differences from the start: none“ a „Credential Manager test entries: none“.
- Přeskočené testy: skutečná kamera je nedostupná; vstupy podřízených procesů a obnovy; dva testy stráže, které při potvrzeném varování nejdou spustit; v AOT navíc kontrola Release DLL.
- V obou průchodech prošly:
  - `CredentialManagerTests`;
  - `CameraWindowTests.FakeCameraWindowModesPlacementAndApply` a `ApplyOpensAndPlacesTheMiniPreview` (živý obraz simulátoru přes FFmpeg z publish složky);
  - toasty T-068 včetně COM aktivátoru, tray T-069, okna T-071;
  - `SettingsListsTheRealMonitorNames` (T-035, `MonitorNameNative` v AOT);
  - testy logu a pádů.
- Před sloučením (base 0edbe94) prošel jeden plný běh zeleně v obou průchodech: Debug 389/6/0, AOT 388/7/0 (z 395).
- `dotnet build` 0 varování, 0 chyb. `dotnet test`: 5315 testů, 0 selhalo, 114 přeskočeno (E2E bez `PTD_E2E`); `PrusaTrayDiagnostic.E2E.Tests` (T-030) 19 prošlo. AOT publish win-x64 (Release i Debug) bez varování.
- Řada T-010, T-025, T-035, T-037, T-039, T-041, T-042, T-043: všechny úkoly už jsou v `done/` se zaškrtnutými kritérii. Oba průchody je pokrývají, poznámky se neměnily.
- Žádný nový NuGet balíček: P/Invoke `advapi32` je jen v testech, WMI jde přes už používaný `System.Management`.

## Review
**2026-10-24, qa-reviewer – schváleno po opravách.**
- Bezpečnost Správce pověření v pořádku: `CredEnumerate` jen s filtrem `PrusaTrayDiagnostic.E2E/*` (filtr Windows nerozlišuje velikost písmen, výsledky se znovu kontrolují `Ordinal`, takže v nejhorším případě se něco nesmaže, nikdy naopak), `*` a mezery v předponě se odmítnou před voláním Windows, hesla se jen porovnávají. Úklid běhu je pod zámkem plochy, takže nesmaže položky souběžného běhu jiného worktree.
- Opraveno:
  - `Wait.UntilAsync`: časový limit po timeoutu UI Automation nese původní výjimku jako `InnerException` (se stopou), takže timeout, který nepochází z vytíženého okna aplikace, není skrytý. Test v `WaitTests`.
  - `SystemChangeWatchdog`: příkazový řádek hlídacího procesu a dotaz WMI v `WaitUntilGoneAsync` se skládají z jedné metody (`CommandLineEnd`). Při změně formátu by dotaz jinak tiše nic nenašel a závod z T-073 by se vrátil. Test `WatchdogIsFoundByTheCommandLineItIsStartedWith`.
  - Win+B: když okno jiného programu zůstane v popředí i po stisku (měl ho už předtím, takže kontrola „nové okno“ nezabrala), další Win+B se nestiskne a test skončí s chybou, která uvádí jen název procesu.
  - Ruční postup Nerušit ve varování skriptu (`RunE2E.SystemChanges.psm1`) zní „the clock on the taskbar (or Win+N)“ jako v záznamu a `docs/troubleshooting.md`.
- Ponecháno (nízké riziko, zapsané):
  - `NotificationCenter.CloseAsync` a `NotificationArea.CloseOverflowAsync` posílají Escape po kontrole popředí; mezi kontrolou a stiskem zbývá okno v řádu mikrosekund (stejný vzor jako dřív).
  - Hlídací proces průchodu AOT zapisuje `system-changes-watchdog.log` do složky výsledků průchodu Debug (skript ji počítá sám).
  - Ctrl+C během průchodu: skript čeká, až testovací proces (dostane Ctrl+C také) skončí, a teprve pak vrací změny. Proces, který Ctrl+C ignoruje, skript drží.
- T-052: zaškrtnuté kritérium 2 je podložené (rozhodnutí uživatele 2026-10-22, oba průchody zelené, web přes proxy ručně 2026-10-21, T-030 zelené).
- Ověření po opravách: `dotnet build` 0 varování; `dotnet test` 5316 testů, 0 selhalo, 114 přeskočeno. `pwsh tools/run-e2e.ps1 -PublishAot -Class '*CredentialManagerTests'`: Debug 1/0/0, AOT 1/0/0, publikace Release i Debug AOT bez varování, „Credential Manager test entries: none“, „Differences from the start: none“. `-Class '*TrayMenu*'` 39/39, `-Class '*SystemChangeTests'` 9/9. Vždy „System changes (after the run): nothing pending“.
