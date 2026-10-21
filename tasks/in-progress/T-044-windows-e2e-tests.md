---
id: T-044
title: End-to-end testy skutečné aplikace na Windows (UI Automation)
status: in-progress
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

## Akceptační kritéria
- [x] Testovací režim aplikace izoluje data, instanci, zástupce a toasty od uživatele (testy, že se nic nezapíše mimo testovací složku)
- [x] E2E projekt s FlaUI, spouštěný na vyžádání skriptem; v běžném `dotnet test` přeskočený
- [x] Automatické kontroly ze sekce Rozsah, se snímky obrazovky (skutečné tray menu zůstává ručně, viz níže)
- [x] Testy po sobě nic nenechají ani po pádu (procesy, soubory, registrace)
- [x] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish win-x64 bez varování (produkční build bez testovacích přepínačů)
- [ ] E2E běh na Windows úspěšný, výsledky a snímky zkontrolované — běh proběhl a snímky jsou zkontrolované, ale 6 testů padá na skutečnou chybu rozložení (viz Nalezené chyby); zelený bude po její opravě

## Otevřené otázky
- (žádné k samotným testům; k nalezené chybě viz Nalezené chyby – rozhoduje uživatel / ui-dev)

## Poznámky k implementaci

### Testovací režim aplikace (jen Debug)
- `AppEnvironment` (App/Composition): `Production` (vždy v Release), v Debug buildu proměnná `PRUSATRAYDIAGNOSTIC_TEST_DATA_DIR` (absolutní cesta) zapne izolovaný testovací režim. V Release se přepínač vůbec nepřekládá (`#if DEBUG`), ověřeno: Release DLL ani AOT exe neobsahují názvy přepínačů (test `ReleaseBuildsContainNoTestSwitches`).
- Testovací režim: `settings.json`, `camera-windows.json`, `printer-images/` (a na macOS zámek instance) jdou do testovací složky; hesla jen v paměti (`TestModeSecretStore`, volitelně naplněné souborem `test-secrets.txt` v testovací složce, nikdy se nezapisuje) – Správce pověření se nepoužije; žádné toasty (`NullNotificationService`), takže se nikdy nezapisuje zástupce v nabídce Start s AUMID; log do `test-log.txt` v testovací složce. Autostart aplikace zatím nemá (T-017). AUMID procesu (`SetCurrentProcessExplicitAppUserModelID`) zůstává – nic neregistruje a testy čtou AUMID oken.
- Testovací start bez přípony instance (`PRUSATRAYDIAGNOSTIC_INSTANCE_SUFFIX`) nebo s relativní cestou skončí kódem 4 – nikdy se tak nepotká s běžící instancí uživatele.
- Testovací příkazy kanálu jediné instance (jen Debug a jen v testovacím režimu): `settings`, `diagnostics[:id]`, `about`, `camera:<id>` (přepínač jako v tray), `quit`. `InstanceCommand` je teď `record struct` (druh + argument); produkční protokol beze změny (`activate`).
- Unit testy: `AppEnvironmentTests` (cesty všech úložišť, zápis jen do testovací složky, paměťová hesla, politika hesel, log, Release bez testovacího režimu, hlídač zdrojů – `GetFolderPath`, `AppDataPaths`, `CreateDefault`, `GetTempPath`, Credential Manager, zástupce a registr se v App smí objevit jen na povolených místech), `InstanceProtocolTests`.

### E2E projekt `tests/PrusaTrayDiagnostic.App.E2E.Tests`
- FlaUI.Core + FlaUI.UIA3 5.0.0 (MIT, jen testy, AOT se netýká; projekt `net11.0-windows`, xUnit v3 na MTP funguje). Testy běží jen s `PTD_E2E=1` na Windows, jinak se přeskočí; sériově.
- Spuštění: `pwsh tools/run-e2e.ps1` (volitelně `-Class '*CameraWindowTests'`, `-Method ...`, `-PublishAot`). Skript sestaví Debug aplikaci (testovaný exe), Release (kontrola přepínačů), volitelně AOT publish, spustí testy a vypíše složku výsledků: `%TEMP%\PrusaTrayDiagnostic.App.E2E.Results.<id worktree>` (snímky `NNN-*.png` a log každé spuštěné aplikace `log-*.txt`; nahrazuje se dalším během).
- Izolace a úklid: každá aplikace má vlastní datovou složku v rezervované složce `%TEMP%\PrusaTrayDiagnostic.App.E2E.Tests.<id>` (T-032, zámek, úklid i po pádu), vlastní příponu instance a argument `--ptd-e2e-run=<složka>`; před smazáním složky (start i konec běhu) se zabijí jen procesy s touto značkou (WMI). Uživatelova instance se nikdy nedotkne. Aplikace se ukončuje příkazem `quit`, jinak kill + chyba testu.
- Tiskárna a kamera: simulátor z T-045 (`tools/PrusaSimulator`: `SimulatedPrinter` s Digest přihlášením, `RtspCamera` se smyčkou klipu Buddy3D v reálném čase); offline tiskárny na `127.0.0.1:1`.

### Co je automatizované (běh 2026-10-21, Windows 11 26300, 100 % = 96 DPI, 2 monitory 2560×1440 + 1440×2560 na výšku)
| Kontrola | Výsledek |
|---|---|
| Jediná instance: druhé spuštění skončí 0 bez dalšího procesu, bez oken otevře Nastavení; s minimalizovanými okny obnoví naposledy použité (Nastavení) a O aplikaci nechá minimalizované | OK |
| Nastavení a Diagnostika: jen tlačítko zavřít, klient 780×508 DIP, PreventPinning + AUMID (zpětně přes `SHGetPropertyStoreForWindow`), tlačítko na panelu, ikona | OK (8 jazyků) |
| Bez posuvníku (UIA ScrollPattern + viditelné ScrollBar): 0 / 1 / 3 tiskárny, všechny podzáložky tiskárny, kamera zapnutá/vypnutá, chyby validace (název, adresa, adresa kamery), Obecné, Diagnostika tisknoucí / offline / bez tiskáren | OK kromě Okno kamery u vypnuté kamery v de, es, fr, it, pl (viz chyby) |
| Okno kamery (simulovaná kamera): živý obraz se mění, normální 960×540 uprostřed, není navrchu, má tlačítko na panelu, AUMID/PreventPinning, ikona | OK |
| Malý náhled 320×180 vpravo dole (16 DIP), navrchu, bez tlačítka na panelu; celá obrazovka = celý monitor, ne navrchu; návrat do náhledu na stejné místo | OK |
| Nastavení: Test kamery s náhledem; Použít (levý horní roh + navrchu) přenastaví otevřené okno; jiný monitor + Použít přesune okno na druhý monitor; Tovární nastavení + Použít vrátí střed, primární monitor, ne navrchu; `camera-windows.json` jen v testovací složce | OK |
| Skutečná kamera `rtsp://192.168.0.227/live` (když odpoví do 2 s): živý obraz v okně, náhled v nastavení | OK v prvním běhu, v dalších bězích kamera nedostupná → přeskočeno |
| Ikona aplikace: exe a okna Nastavení, Diagnostika, O aplikaci odpovídají `app-icon.ico` | OK |
| Release DLL a AOT exe bez testovacích přepínačů | OK |

### Nalezené chyby (pro ui-dev / uživatele)
- **Podzáložka Okno kamery u tiskárny s vypnutou kamerou má posuvník v de, es, fr, it, pl** (skutečné písmo Segoe UI, 100 %): text „Kamera je vypnutá…“ se zalomí na 2 řádky a obsah přeteče o ~2 % (tlačítka Tovární nastavení / Použít jsou uříznutá). Headless test T-033 to nezachytí (písmo Inter). Snímky `settings-de-3p-camera-off-PrinterTabCameraWindow.png` apod. Oprava = zkrátit text nebo upravit rozložení – rozhodnutí uživatele (T-040: „raději zkrátit, než dopustit posuvník“).
- Drobnosti (bez testu): detail stavu offline tiskárny „The printer is not reachable.“ je anglicky i v jiných jazycích; patička Diagnostiky offline tiskárny ukazuje chvíli „Wczytywanie…“ (načítání informací).

### Zůstává ručně
- Skutečné tray menu v oznamovací oblasti (ikona testovací instance vypadá stejně jako uživatelova, ve Windows 11 bývá v přetečení, otevření vyžaduje klik myší) – pokryto modelem, presenterem a headless testy (T-031, T-042); test `TrayMenuTests` je přeskočený s odůvodněním.
- Jiné škálování než aktuální (125/150 %), Narrator, celkový dojem, jump list (vlastnosti oken ověřené, samotný jump list ne), blikání tlačítka na panelu, chování při odpojeném monitoru.
