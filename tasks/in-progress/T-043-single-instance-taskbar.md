---
id: T-043
title: Jediná instance aplikace a jump list oken na hlavním panelu
status: in-progress
owner: ui-dev
priority: P1
platform: all
depends_on: [T-010]
---

## Cíl
Aplikace běží v jedné instanci na uživatele a relaci. Druhé spuštění (jump list, Start, dvojklik, autostart) nevytvoří další instanci a další ikonu v tray, jen aktivuje běžící aplikaci. Kontextové menu oken na hlavním panelu nabízí jen „Zavřít okno“. Nalezeno při ručním testu uživatele 2026-10-20 (dvě instance současně); rozbor a sonda ui-designera 2026-10-20.

## Rozsah
1. **Jediná instance (Windows):** v `Program.Main` před startem Avalonie pojmenovaný mutex `Local\PrusaTrayDiagnostic-<SID uživatele>`; první instance spustí server pojmenované roury (`PipeOptions.CurrentUserOnly`, název se SID a ID relace), druhá instance pošle příkaz (`activate`) a skončí s kódem 0. Opakování připojení 2–5 s kvůli souběhu s autostartem. Access denied (např. elevated instance) = „už běží“; selhání předání zalogovat a skončit. Příkaz se předá na UI vlákno; když se aplikace ještě spouští, počká ve frontě.
2. **Chování běžící instance po druhém spuštění:** když je otevřené nějaké pomocné okno (nastavení, diagnostika, kamera, O aplikaci, licence), naposledy použité se obnoví z minimalizace, aktivuje a případně blikne na hlavním panelu (`FlashWindowEx`, pokud Windows nedovolí převzít popředí); když žádné okno otevřené není, otevře se Nastavení.
3. **macOS:** reopen událost od LaunchServices (`IActivatableLifetime.Activated`, `ActivationKind.Reopen`) zpracovat stejně jako `activate`; ochrana zámkovým souborem (`FileStream` s `FileShare.None` v `~/Library/Application Support/PrusaTrayDiagnostic/instance.lock`) + Unix socket. Implementovat platformně oddělené; ověření na Macu v T-018/T-019 (zde jen kód a testy, které jdou spustit na Windows).
4. **Jump list oken (Windows):** všem pomocným oknům (kamera, nastavení, diagnostika, O aplikaci, licence) nastavit přes `SHGetPropertyStoreForWindow` vlastnost `System.AppUserModel.PreventPinning = true` a poté `System.AppUserModel.ID = PrusaTrayDiagnostic` (pořadí je důležité); při zavření okna vlastnosti vyčistit (VT_EMPTY). `AssemblyTitle`/`Product` = „Prusa Tray Diagnostic“, aby tlačítko na panelu mělo správný název. AOT: `[LibraryImport]`, `[GeneratedComInterface] IPropertyStore` (už v repozitáři z T-010), žádný `BuiltInComInteropSupport`, žádný nový balíček.
5. Bez vlastních úloh v jump listu.

## Rozhodnuto
- **Druhé spuštění (2026-10-20):** přenést naposledy použité otevřené okno dopředu, jinak otevřít Nastavení.
- **Jump list (2026-10-20):** u všech pomocných oken jen „Zavřít okno“ (PreventPinning).
- **Úlohy v jump listu (2026-10-20):** ne.
- **Rozsah instance (2026-10-20):** jedna instance na přihlášeného uživatele a relaci.
- **„visual beel“ (2026-10-20):** chápáno jako přepnout na otevřené okno (a bliknout), místo spuštění nové aplikace.

## Akceptační kritéria
- [x] Druhé spuštění nevytvoří druhou instanci; předá příkaz běžící instanci a skončí (testy logiky předání s injektovaným názvem mutexu/roury; integrační test dvou procesů, pokud jde spolehlivě a bez zanechání stavu)
- [x] Běžící instance po `activate` aktivuje naposledy použité okno, jinak otevře Nastavení (testy)
- [ ] Pomocná okna mají PreventPinning + AUMID; jump list jen „Zavřít okno“ (test vlastností; ruční ověření) — test vlastností hotový (falešné úložiště i skutečné skryté okno); čeká se na ruční ověření jump listu
- [x] Testy po sobě nic nenechají (mutexy, roury, soubory)
- [ ] Ruční ověření na Windows (uživatel): jump list okna kamery, druhé spuštění z jump listu/Startu
- [x] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish win-x64 bez varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci

### Návrh (2026-10-20, ui-dev)
- **Spuštění:** `Program.Main` nejdřív spustí `SingleInstanceGuard` (nic jiného před ním, ani AppUserModelID). Druhé spuštění pošle `activate` a skončí s kódem 0; Avalonia se vůbec nespustí. Protokol: jedno ASCII slovo + `\n`, klient zapíše a zavře spojení (bez odpovědi – malý zápis do roury/socketu nikdy neblokuje). Server přijímá klienty po jednom, čte max. 64 bajtů do 2 s, neznámé zprávy zaloguje a ignoruje.
- **Opakování:** celkem 5 s, pokusy o připojení po 1 s (mezi nimi se znovu zkouší zámek – když běžící instance mezitím skončí, druhé spuštění se stane primární instancí), po přerušeném spojení pauza 200 ms. Access denied u zámku i u roury = „už běží“ → log, konec s 0. Když zámek nejde vytvořit z jiného důvodu (např. jméno obsadil jiný typ objektu) nebo chybí SID/datová složka, aplikace poběží bez ochrany (lepší než nespustit vůbec); stejně tak, když selže jen naslouchání.
- **Fronta příkazů:** `InstanceCommandQueue` drží příkazy, dokud `App` nenačte nastavení a nespustí host; pak je předá na UI vlákno (`IUiDispatcher`). Stejný čekající příkaz se drží jen jednou. Při ukončování (`Quit`, konec relace) se fronta zavře a příkazy se zahazují.
- **Aktivace:** `AuxiliaryWindowTracker` prochází každé pomocné okno hned po vytvoření (Nastavení, Diagnostika, O aplikaci, licence přes `AboutWindow.ChildWindowCreated`, kamery přes parametr `CameraWindows(created:)`). `RecentWindows` řadí otevřená okna podle posledního použití (otevření nebo aktivace), zavřené vypadne. `InstanceActivation`: nejnovější okno → `BringToFront` (minimalizované se vrátí do stavu před minimalizací, tedy i do celé obrazovky kamery; `Activate`; když okno není v popředí, `FlashWindowEx` s `FLASHW_ALL | FLASHW_TIMERNOFG`, 3 bliknutí a pak zvýrazněné tlačítko, dokud uživatel okno neaktivuje); jinak Nastavení.
- **Popředí na Windows:** druhé spuštění (spustil ho uživatel, takže smí převzít popředí) po připojení zjistí `GetNamedPipeServerProcessId` a zavolá `AllowSetForegroundWindow` pro běžící instanci, aby aktivace okna opravdu prošla; blikání je jen záloha.
- **Hlavní panel (Windows):** `WindowsWindowShell.Prepare` při vytvoření okna (HWND existuje už po konstruktoru `Window`, ověřeno sondou s Avalonia 12.1.4) nastaví přes `SHGetPropertyStoreForWindow` nejdřív `PreventPinning = VT_BOOL 0xFFFF`, pak `System.AppUserModel.ID = PrusaTrayDiagnostic`; `Release` při `Closed` obě vlastnosti vyprázdní (VT_EMPTY, nejdřív ID). HWND se uloží při vytvoření – při `Closed` už ho `TryGetPlatformHandle()` nevrací, okno ale ještě existuje a úložiště vlastností jde získat (ověřeno sondou). `Commit` se nevolá (úložiště okna ho nepotřebuje; zpětné čtení v testu to potvrzuje). `AssemblyTitle`/`Product` = „Prusa Tray Diagnostic“ (popis souboru v AOT exe ověřen). Okno licencí je vlastněné oknem O aplikaci, takže vlastní tlačítko na panelu nemá; vlastnosti dostane stejně.
- **AOT:** jen `[LibraryImport]` (`SHGetPropertyStoreForWindow`, `GetForegroundWindow`, `FlashWindowEx`, `AllowSetForegroundWindow`, `GetNamedPipeServerProcessId`) a existující `[GeneratedComInterface] IPropertyStore` / `ComObjects` z T-010; žádný nový balíček, žádné potlačení varování.

### Chování podle platformy
| | Windows | macOS |
|---|---|---|
| Zámek | mutex `Local\PrusaTrayDiagnostic-<SID>` (jen `createdNew`, nikdy se nevlastní) | `~/Library/Application Support/PrusaTrayDiagnostic/instance.lock`, `FileStream` s `FileShare.None` (v .NET na Unixu `flock`, uvolní ho OS i po pádu); soubor zůstává na disku |
| Kanál | pojmenovaná roura `PrusaTrayDiagnostic-<SID>-<relace>`, `PipeOptions.CurrentUserOnly` na obou stranách | Unix socket `instance.sock` vedle zámku (při cestě delší než 103 bajtů v `$TMPDIR`); starý soubor po pádu nahradí nová primární instance, při ukončení se smaže |
| Znovuotevření | druhý proces | navíc `IActivatableLifetime.Activated` s `ActivationKind.Reopen` (klik v Docku/Finderu) → stejný příkaz `activate` |
| Hlavní panel | PreventPinning + AUMID, blikání | nic (T-018) |

### Ověření
- Windows 11 (26300): `dotnet build` 0 varování, `dotnet test` 2× zeleně (3535 testů, 1 přeskočený jako dřív), AOT publish win-x64 0 varování.
- Testy: logika strážce s falešným transportem a časem (první/druhá instance, opakování, timeout, access denied u zámku i roury, převzetí po skončení běžící instance, selhání naslouchání), fronta před připravením hostu, server (neznámé/dlouhé/mlčící zprávy), skutečná roura+mutex i Unix socket+zámkový soubor s vlastními jmény (Unix sockety jdou i na Windows 10+), odmítnutá roura s cizí DACL, pořadí a hodnoty vlastností přes falešné `IPropertyStore`, zápis/čtení/vyčištění vlastností na skutečném skrytém okně, MRU a politika aktivace, popis souboru exe.
- **Test dvou procesů:** test drží primární instanci s vlastními jmény a spustí skutečné `PrusaTrayDiagnostic.exe` (Debug) s proměnnými `PRUSATRAYDIAGNOSTIC_INSTANCE_SUFFIX` a `PRUSATRAYDIAGNOSTIC_INSTANCE_FORWARD_ONLY=1`; exe předá `activate` a skončí s 0. Obě proměnné fungují jen v Debug buildu (v Release se vůbec nepřekládají); s `FORWARD_ONLY` by se exe, kdyby se omylem stalo primární instancí, hned ukončilo s kódem 3 místo spuštění aplikace. Jen Windows (na macOS by zámkový soubor šel do skutečné datové složky).
- Úklid: mutexy a roury jsou objekty jádra, zaniknou s handly (nejpozději s koncem procesu testů); soubory zámku/socketu jen v rezervované dočasné složce testů (T-032).
- **Neověřeno:** celá aplikace se ručně nespouštěla (běží uživatelova instance a spuštění plné aplikace z worktree by přepsalo zástupce v nabídce Start na tento exe). Ruční ověření zůstává na uživateli: jump list okna kamery / nastavení (jen „Zavřít okno“, název „Prusa Tray Diagnostic“), druhé spuštění z jump listu, Startu a dvojklikem (přenese naposledy použité okno, jinak Nastavení; blikání, když Windows nepovolí popředí). macOS (zámkový soubor a socket na skutečném Macu, reopen z Docku/Finderu) se ověří v T-018/T-019.
