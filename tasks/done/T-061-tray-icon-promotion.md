---
id: T-061
title: Připnutí tray ikony do viditelné části hlavního panelu (workaround)
status: done
owner: ui-dev
priority: P2
platform: windows
depends_on: [T-007]
---

## Cíl
Windows 11 dává nové ikony v oznamovací oblasti do přetečení (šipka `^`). Uživatel chce ikonu aplikace vidět hned, bez ručního přetažení.

**Workaround (rozhodnutí uživatele 2026-10-21):** Windows na to nemá veřejné API a Microsoft nechce, aby aplikace měnily volbu uživatele. Používá se proto nedokumentovaná hodnota registru `HKCU\Control Panel\NotifyIconSettings\<číslo>\IsPromoted = 1` (DWORD, Windows 11 22H2+). Pokud ji Microsoft změní nebo zruší, aplikace dál funguje, jen se ikona sama nepřipne.

## Rozsah
- Explorer vytvoří podklíč `NotifyIconSettings\<číslo>` (hodnota `ExecutablePath`) až po prvním zobrazení ikony. Instalátor tedy ikonu připnout nemůže, udělá to aplikace po zobrazení ikony.
- Aplikace počká, až se objeví její záznam (`ExecutablePath` = vlastní exe, přepočítat i tvar `{KNOWNFOLDERID}\…`). **Připne ho jen tehdy, když hodnota `IsPromoted` chybí** (uživatel o ikoně ještě nerozhodl). Pokud si ikonu uživatel schová, Windows zapíše `IsPromoted = 0` a aplikace na ni už nesahá.
- Platí pro každý `PrusaTrayDiagnostic.exe`: instalaci, zip i vývojové buildy, vždy jednou pro každou cestu k exe (rozhodnutí uživatele 2026-10-21). Spuštění přes `dotnet.exe` a testovací režim (T-044) se přeskočí.
- Úklid při odinstalaci: `TrayIconPromotion.ClearPromotions` smaže u záznamů vlastního exe hodnotu `IsPromoted`, záznamy samotné ponechá. Pokus ukázal, že smazaný záznam Explorer za běhu znovu nevytvoří (zda ho obnoví po restartu Exploreru nebo po přihlášení, se neověřovalo) a přeinstalovaná ikona by zůstala v přetečení. Zavolá ji hook `--veloapp-uninstall` v T-017 (doplněno do T-017). Po přeinstalování se ikona připne znovu.
- Na macOS se nic neděje (ikony v liště nabídek jsou vidět vždy).

## Akceptační kritéria
- [x] Po prvním spuštění nové cesty exe je ikona ve viditelné části hlavního panelu bez restartu Exploreru (ověřeno UI Automation: `IsPromoted = 1` za 2 s, ikona hned viditelná)
- [x] Existující `IsPromoted` (0 i 1) aplikace nepřepíše
- [x] Testovací režim a spuštění přes `dotnet.exe` registr nemění
- [x] Úklid smaže jen `IsPromoted` záznamů vlastního exe; testy píšou jen do vlastního testovacího klíče a uklidí ho i po pádu
- [x] T-017 obsahuje volání úklidu v hooku `--veloapp-uninstall`
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- `Platform/Windows/TrayIconPromotion.cs`: logika (čekání na záznam Exploreru po 1 s, nejvýš 30 pokusů; připnutí jen bez `IsPromoted`; `ClearPromotions` pro odinstalaci). Nikdy nevyhodí výjimku, chyby registru jen zaloguje.
- `Platform/Windows/NotifyIconSettingsRegistry.cs`: přístup do `HKCU\Control Panel\NotifyIconSettings` přes `Microsoft.Win32.Registry` a převod `{KNOWNFOLDERID}\…` přes `SHGetKnownFolderPath` (`[LibraryImport]`). Cesty v `%LocalAppData%` (instalace Velopack) Explorer ukládá jako běžnou cestu, převod se týká hlavně Program Files.
- Spouští `TrayHost` na pozadí po vytvoření tray ikony, jen na Windows, mimo testovací režim a jen pro `PrusaTrayDiagnostic.exe`. Při ukončení se ruší (`backgroundWork`). Hlídací test `AppEnvironmentTests` povoluje registr jen v `NotifyIconSettingsRegistry.cs` a jeho vytvoření jen v `TrayHost.cs`.
- Pokusy (Windows 11, 2026-10-21): Explorer vytvoří záznam pro novou cestu exe do několika sekund po zobrazení ikony. Změnu `IsPromoted` použije hned a hodnota vydrží restart aplikace. Smazaný záznam Explorer za běhu znovu nevytvoří, proto úklid maže jen hodnotu.
- Žádný nový NuGet balíček. Native AOT publish `win-x64` proběhl bez varování trim/AOT.
- Po pokusech zůstaly v `NotifyIconSettings` záznamy dočasných cest (scratchpad, worktree `PTD-T061`), stejně jako u jiných vývojových buildů. Windows je ponechává i pro odinstalované aplikace.
