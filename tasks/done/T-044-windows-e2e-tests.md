---
id: T-044
title: End-to-end testy skutečné aplikace na Windows (UI Automation)
status: done
owner: qa-reviewer
priority: P2
platform: windows
depends_on: [T-040, T-041, T-042, T-043]
---

## Cíl
Co nejvíc ručních kontrol z úkolů T-014, T-025, T-033–T-043 ověřit automaticky na skutečném `PrusaTrayDiagnostic.exe` ve Windows přes UI Automation (FlaUI). Požadavek uživatele 2026-10-21.

## Rozsah
- **Izolace od uživatele (podmínka):** testovaná aplikace nesmí sahat na skutečná data ani ovlivnit běžící instanci uživatele:
  - testovací přepínač vlastní datové složky (`settings.json`, `camera-windows.json`, `printer-images/`, cache) – jen v Debug buildu, stejně jako přepínače z T-043;
  - vlastní názvy jediné instance (přípona z T-043);
  - v testovacím režimu žádná registrace zástupce v nabídce Start / AppUserModelID na exe z testů (T-010), žádný autostart, žádné toasty mimo test;
  - po testu vše uklidit (procesy, soubory, registrace) i po pádu (T-032 per-worktree složky).
- **Nový projekt** `tests/PrusaTrayDiagnostic.App.E2E.Tests` (nebo rozšíření `PrusaTrayDiagnostic.E2E.Tests`, podle konvence repozitáře), xUnit v3 na MTP, FlaUI (`FlaUI.Core` + `FlaUI.UIA3`, MIT, jen v testech – nedodává se, AOT se netýká). Spouštění **na vyžádání**: v běžném `dotnet test` se přeskočí (např. bez proměnné prostředí `PTD_E2E=1`); skript `tools/` pro spuštění.
- **Ovládání oken:** otevírat přes kanál jediné instance (T-043; případně testovací příkazy `settings`, `diagnostics`, `camera:<id>` jen v Debug), ne klikáním do tray. Tray menu ověřit přes model/headless jen tam, kde UIA na oznamovací oblast není spolehlivé; pokus o skutečné tray menu volitelně.
- **Kontroly (automaticky):**
  - jediná instance: druhé spuštění nevytvoří proces navíc, aktivuje poslední okno, jinak otevře Nastavení (T-043);
  - okna Nastavení a Diagnostika: bez minimalizace/maximalizace (styly okna), velikost 780×508 klient, bez viditelného scrollbaru ve skutečném vykreslení (Segoe UI) ve všech 8 jazycích a hlavních stavech (T-033, T-040, T-041);
  - vlastnosti oken pro hlavní panel: PreventPinning + AUMID (T-043), malý náhled kamery bez tlačítka na panelu (T-040);
  - okno kamery: režimy, vždy navrchu (WS_EX_TOPMOST, z-order), Tovární nastavení / Použít přenastaví otevřené okno (T-035–T-037, T-040);
  - se skutečnou kamerou (`rtsp://192.168.0.227/live`), když je dostupná: test v nastavení s náhledem, živý obraz v okně (obraz se mění), jinak přeskočit; bez kamery falešná RTSP kamera z T-014;
  - ikona aplikace na oknech a exe (T-025);
  - snímky obrazovky každého kroku do složky výsledků (pro kontrolu člověkem / Claude).
- **Nelze automatizovat (zůstává ručně):** jiné škálování než aktuální (125/150 % vyžaduje odhlášení), Narrator, celkový dojem.

## Rozhodnuto
- **E2E testy (2026-10-21):** ano, knihovna FlaUI (jen testy).
- **Spouštění (2026-10-21):** na vyžádání, mimo běžný `dotnet test`.
- **Skutečná kamera (2026-10-21):** ano, když je dostupná; jinak přeskočit.
- **Chyby nalezené E2E (2026-10-21, opravit v rámci T-044):** zkrátit text „kamera je vypnutá“ na podzáložce Okno kamery ve všech 8 jazycích na jeden řádek; přeložit detail „The printer is not reachable.“ do všech jazyků; patička Diagnostiky u offline tiskárny nemá viset na „Načítání…“.
- **Vlastní AppUserModelID testovacího režimu (2026-10-21):** `PrusaTrayDiagnostic.E2E` pro proces i okna, aby se testovací okna nikdy neseskupila s instancí uživatele; produkce dál `PrusaTrayDiagnostic`.
- **Skutečné tray menu (2026-10-21):** zkusit automatizovat přes UIA (testovací instance rozlišená prefixem tooltipu `[E2E]` jen v testovacím režimu); když to nepůjde spolehlivě, nechat ručně s odůvodněním.

## Akceptační kritéria
- [x] Testovací režim aplikace izoluje data, instanci, zástupce a toasty od uživatele (testy, že se nic nezapíše mimo testovací složku)
- [x] E2E projekt s FlaUI, spouštěný na vyžádání skriptem; v běžném `dotnet test` přeskočený
- [x] Automatické kontroly ze sekce Rozsah, se snímky obrazovky (včetně skutečného tray menu)
- [x] Testy po sobě nic nenechají ani po pádu (procesy, soubory, registrace)
- [x] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish win-x64 bez varování (produkční build bez testovacích přepínačů)
- [x] E2E běh na Windows úspěšný, výsledky a snímky zkontrolované

## Otevřené otázky
- (žádné)

## Poznámky k implementaci

### Testovací režim aplikace (jen Debug)
- `AppEnvironment` (App/Composition): `Production` (vždy v Release), v Debug buildu proměnná `PRUSATRAYDIAGNOSTIC_TEST_DATA_DIR` (absolutní cesta) zapne izolovaný testovací režim. V Release se přepínač vůbec nepřekládá (`#if DEBUG`); ověřeno: Release DLL ani AOT exe neobsahují názvy testovacích přepínačů (ani `PRUSATRAYDIAGNOSTIC_NATIVE_LIBRARY_PROBE` z T-052; test `ReleaseBuildsContainNoTestSwitches`).
- Testovací režim: `settings.json`, `camera-windows.json`, `printer-images/` (a na macOS zámek instance) jdou do testovací složky; hesla jen v paměti (`TestModeSecretStore`, volitelně naplněné souborem `test-secrets.txt`, nikdy se nezapisuje) – Správce pověření se nepoužije; žádné toasty (`NullNotificationService`), takže se nikdy nezapisuje zástupce v nabídce Start s AUMID; log do `test-log.txt` v testovací složce; vlastní AppUserModelID `PrusaTrayDiagnostic.E2E` (proces i okna); tooltip tray ikony začíná `[E2E] `. Autostart aplikace zatím nemá (T-017).
- Testovací start bez přípony instance (`PRUSATRAYDIAGNOSTIC_INSTANCE_SUFFIX`) nebo s relativní cestou skončí kódem 4 – nikdy se nepotká s běžící instancí uživatele. `NativeLibrarySearch.Restrict()` (T-052) zůstává první příkaz `Main`.
- Testovací příkazy kanálu jediné instance (jen Debug a jen v testovacím režimu): `settings`, `diagnostics[:id]`, `about`, `camera:<id>` (přepínač jako v tray), `quit`. `InstanceCommand` je `record struct` (druh + argument); produkční protokol beze změny (`activate`).
- Unit testy: `TestModeGuardTests` (skutečný exe: testovací režim bez přípony nebo s relativní cestou skončí kódem 4 dřív, než vznikne mutex/roura, a nic nezapíše), `AppEnvironmentTests` (cesty všech úložišť, zápis jen do testovací složky, paměťová hesla, politika hesel, log, AUMID produkce/testu, Release bez testovacího režimu, hlídač zdrojů – `GetFolderPath`, `AppDataPaths`, `CreateDefault`, `GetTempPath`, Credential Manager, zástupce a registr se v App smí objevit jen na povolených místech), `InstanceProtocolTests`.

### Opravy nalezených chyb
- Text „kamera je vypnutá“ (podzáložka Okno kamery, také boční sloupec Překryvu) zkrácen ve všech 8 jazycích (cs „Kamera je vypnutá – nastavení se použije po jejím zapnutí.“); E2E kontrola posuvníku projde ve všech jazycích, snímky headless testů `settings-camera-window-off-cs` a `settings-overlay-off-it` přegenerované a zkontrolované.
- Důvod nedostupnosti tiskárny: nový `PrinterProblem` v Core (`PrinterSnapshot.Problem`), plní ho klient PrusaLink (podle `PrusaLinkFailure`) a monitor (timeout); Diagnostika ukazuje přeložený text (nové texty `DiagnosticsProblem*` ve všech 8 jazycích, zbytek sdílený s testem připojení), technická anglická zpráva zůstává jen jako záloha. Testy: mapování všech chyb, texty ve všech jazycích, view model.
- Patička Diagnostiky: u tiskárny, jejíž poslední stav je offline, se informace vůbec nenačítají (odmítnuté spojení trvá ve Windows až 2 s) – hned „Nelze načíst: Offline“; načtou se při dalším dosažitelném stavu (test).

### E2E projekt `tests/PrusaTrayDiagnostic.App.E2E.Tests`
- FlaUI.Core + FlaUI.UIA3 5.0.0 (MIT, jen testy, AOT se netýká; projekt `net11.0-windows`, xUnit v3 na MTP funguje). Testy běží jen s `PTD_E2E=1` na Windows, jinak se přeskočí; sériově.
- Spuštění: `pwsh tools/run-e2e.ps1` (volitelně `-Class '*CameraWindowTests'`, `-Method ...`, `-PublishAot`). Skript sestaví Debug aplikaci (testovaný exe), Release (kontrola přepínačů), volitelně AOT publish, spustí testy a vypíše složku výsledků: `%TEMP%\PrusaTrayDiagnostic.App.E2E.Results.<id worktree>` (snímky `NNN-*.png` a log každé spuštěné aplikace `log-*.txt`; nahrazuje se dalším během). Během běhu (~2,5 min) nepoužívat myš a klávesnici.
- Izolace a úklid: každá aplikace má vlastní datovou složku v rezervované složce `%TEMP%\PrusaTrayDiagnostic.App.E2E.Tests.<id>` (T-032, zámek, úklid i po pádu), vlastní příponu instance a argument `--ptd-e2e-run=<složka>`; před smazáním složky (start i konec běhu) se zabijí jen procesy s touto značkou (WMI). Uživatelova instance se nikdy nedotkne. Aplikace se ukončuje příkazem `quit`, jinak kill + chyba testu.
- Tiskárna a kamera: simulátor z T-045 (`tools/PrusaSimulator`: `SimulatedPrinter` s Digest přihlášením, `RtspCamera` se smyčkou klipu Buddy3D v reálném čase); offline tiskárny na `127.0.0.1:1`.
- **macOS:** projekt je jen pro Windows (`net11.0-windows`, FlaUI). Mimo Windows má `EnableWindowsTargeting=true`, `IsTestProject=false` a `IsTestingPlatformApplication=false`, takže `dotnet build` / `dotnet test` řešení na Macu ho jen sestaví a testy přeskočí. **Neověřeno na Macu – ověřit v prvním úkolu `macos`.**
- Odolnost: tlačítko „Zobrazit skryté ikony“ se hledá podle automation id a pořadí (ne podle českého/anglického názvu); když je ikon s `[E2E]` víc (zbytky zabitých instancí), zkouší se postupně, dokud se neotevře menu této aplikace. Kontrola AOT exe na přepínače jen když je novější než Release build (publikovaný tímto během).

### Co je automatizované (běh 2026-10-21, Windows 11 26300, 100 % = 96 DPI, 2 monitory 2560×1440 + 1440×2560 na výšku) – vše OK
| Kontrola | Výsledek |
|---|---|
| Jediná instance: druhé spuštění skončí 0 bez dalšího procesu, bez oken otevře Nastavení; s minimalizovanými okny obnoví naposledy použité a druhé nechá minimalizované | OK |
| Nastavení a Diagnostika: jen tlačítko zavřít, klient 780×508 DIP, PreventPinning + AUMID testu (zpětně přes `SHGetPropertyStoreForWindow`), tlačítko na panelu, ikona | OK (8 jazyků) |
| Bez posuvníku (UIA ScrollPattern + viditelné ScrollBar): 0 / 1 / 3 tiskárny, všechny podzáložky, kamera zapnutá/vypnutá, chyby validace, Obecné, Diagnostika tisknoucí / offline / bez tiskáren | OK (8 jazyků) |
| Okno kamery (simulovaná kamera): živý obraz se mění, normální 960×540 uprostřed, není navrchu, tlačítko na panelu, AUMID/PreventPinning, ikona | OK |
| Malý náhled 320×180 vpravo dole (16 DIP), navrchu, bez tlačítka na panelu; celá obrazovka = celý monitor, ne navrchu; návrat na stejné místo | OK |
| Nastavení: Test kamery s náhledem; Použít (levý horní roh + navrchu) přenastaví otevřené okno; jiný monitor + Použít přesune okno; Tovární nastavení + Použít vrátí střed, primární monitor, ne navrchu; `camera-windows.json` jen v testovací složce | OK |
| Skutečné tray menu: ikona testovací instance (i v přetečení „Zobrazit skryté ikony“), pravý klik skutečnou myší (jediný pohyb myši v testech), podmenu tiskárny, Nastavení…/O aplikaci/Ukončit s přístupovými klávesami S/A/Q, zaškrtávací Kamera otevře a zavře okno, Nastavení… otevře okno | OK (3× po sobě) |
| Skutečná kamera `rtsp://192.168.0.227/live` (když odpoví do 2 s) | OK v prvním běhu, v pozdějších bězích nedostupná → přeskočeno |
| Ikona aplikace: exe a okna Nastavení, Diagnostika, O aplikaci odpovídají `app-icon.ico` | OK |
| Release DLL a AOT exe bez testovacích přepínačů | OK |

### Zůstává ručně
- Jiné škálování než aktuální (125/150 %), Narrator, celkový dojem, jump list (vlastnosti oken ověřené, samotný jump list ne), blikání tlačítka na panelu, chování při odpojeném monitoru, položka „Otevřít PrusaLink v prohlížeči“ v podmenu (UIA ji nehlásí; ověřena jen snímkem).
