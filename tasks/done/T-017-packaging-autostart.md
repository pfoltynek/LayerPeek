---
id: T-017
title: Balení a distribuce, autostart – Windows
status: done
owner: ui-dev
priority: P2
platform: windows
depends_on: [T-008, T-055, T-056]
---

## Cíl
Instalovatelná aplikace pro Windows, spouštění po přihlášení. Balení a autostart pro macOS je samostatný úkol T-019.

Rozhodnutí uživatele (2026-10-21):
- **Instalátor:** **Velopack**, přijatý po spiku T-056 včetně závislosti na skryté volbě `vpk pack --aumid`. WiX se nepoužije (rozhodnuto 2026-10-21).
- **RID:** `win-x64` a `win-arm64`. `win-arm64` se vydává až po dokončení T-057 (FFmpeg pro ARM64), viz T-055.
- **Formáty:** instalátor a k tomu `.zip` (archiv vytváří skript z T-055).
- **Podpis:** žádný placený certifikát, binárky i instalátor zůstanou **nepodepsané**. Uživatelé uvidí varování SmartScreen („Systém Windows ochránil váš počítač“), které je potřeba popsat v dokumentaci vydání.
- **Sestavení a vydání:** lokálně skripty z T-055 (CI odloženo, T-016). Instalátor se připojí jako balicí krok do bodu rozšíření z T-055.

## Rozsah
Podklad: `docs/velopack.md` (T-056). Velopack pro Windows je přijatý (rozhodnuto 2026-10-21).

- Instalátor Velopack pro `win-x64` a `win-arm64`, instalace pro aktuálního uživatele bez admin práv (`%LocalAppData%\LayerPeek`, exe v `current\`, cesta je stejná i po aktualizaci)
- NuGet `Velopack` (MIT, ověřeně AOT/trim kompatibilní v T-056) v `Directory.Packages.props`, ve stejné verzi jako nástroj `vpk`. `vpk` jako lokální dotnet tool v `.config/dotnet-tools.json` s připnutou verzí. `vpk` potřebuje runtime .NET 8–10 nebo roll-forward. Doplnit do `docs/prerequisites.md`.
- `VelopackApp.Build()…Run()` jako **první příkaz v `Main`**, před `SingleInstanceGuard` (T-043). Fast-callback hooky jen za `OperatingSystem.IsWindows()` (CA1416). `vpk` u AOT exe pořadí nekontroluje, hlídá ho test nebo review.
- Balicí krok Velopack ve skriptech T-055 (návrh příkazu v `docs/velopack.md`, bod 12): `--packId LayerPeek`, `--packTitle "LayerPeek"`, `--aumid LayerPeek`, `--shortcuts StartMenuRoot` (jen Start menu, bez plochy), `--channel <RID> -r <RID>`, `--delta None` (zatím bez delt), `--releaseNotes` z `CHANGELOG.md`. Výstup (`*-full.nupkg`, `releases.<RID>.json`, `assets.<RID>.json`, `RELEASES-<RID>`, `Setup.exe`) jde do kontrolních součtů a do GitHub vydání přes `gh` (bez `vpk upload`).
- Aktualizace: `UpdateManager` + `GithubSource("https://github.com/pfoltynek/LayerPeek", accessToken: null, prerelease: true)`, bez tokenu v aplikaci. Každou výjimku kontroly (dokud je repozitář soukromý, `HttpRequestException` 404) brát jako „aktualizace nejsou k dispozici“. Aktualizaci spouští primární instance až po uložení stavu (`ApplyUpdatesAndRestart`), protože Velopack běžící procesy z instalační složky násilně ukončí.
- Autostart po přihlášení (zástupce ve složce Startup, který aplikace vytváří sama a Velopack ho při odinstalaci smaže, nebo registry `Run`, kterou musí smazat hook `--veloapp-uninstall`; `--shortcuts Startup` jen pro „vždy po instalaci“). Podle odpovědi na otázku níže.
- Toasty (navazuje na T-010):
  - zástupce ve Start menu se stejným AppUserModelID `LayerPeek` a názvem „LayerPeek“ (obojí je finální). Vytváří ho Velopack (`--shortcuts StartMenuRoot --aumid LayerPeek`). `AppShortcutRegistration` zůstává jako pojistka při startu a v hooku `--veloapp-install` (doporučení T-056, bod 4);
  - COM aktivátor (`ToastActivatorCLSID` v zástupci a registrace v `HKCU\Software\Classes\CLSID`), aby kliknutí na toast fungovalo, i když aplikace neběží. **Aplikace ho už má (T-068, rozhodnutí uživatele 2026-10-23):** pevné CLSID `{EC47413C-984D-447A-866A-5F780E408DE7}` (`AppEnvironment.ProductionToastActivatorClsid`), při startu zapíše `HKCU\Software\Classes\CLSID\{EC47413C-984D-447A-866A-5F780E408DE7}\LocalServer32` = `"<exe>" -ToastActivated` a vlastnost zástupce `System.AppUserModel.ToastActivatorCLSID` (obojí jen když se liší). Velopack `ToastActivatorCLSID` nastavit neumí, ale při aktualizaci ho zachová (ověřeno v T-056). Hook `--veloapp-install` má zavolat stejnou registraci jako start (`WindowsToastPlatform.Register`). Cesta exe ve `LocalServer32` je `current\LayerPeek.exe` a po aktualizaci se nemění;
  - **odinstalace musí smazat registraci produkčního COM aktivátoru** `HKCU\Software\Classes\CLSID\{EC47413C-984D-447A-866A-5F780E408DE7}` (celý klíč; hook `--veloapp-uninstall`). Jinak by klik na starý toast po odinstalaci zkoušel spustit neexistující exe (Windows klik jen zahodí, ale klíč by v registru zůstal);
  - odinstalace uklidí jen systémové registrace: zástupce (Velopack sám), klíč `HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\LayerPeek`, registraci COM aktivátoru, autostart a historii toastů. Vše kromě zástupce dělá hook `--veloapp-uninstall` (limit 30 s, bez UI). Historie toastů potřebuje nový WinRT interop (`ToastNotificationManager.History.Clear`). Nastavení v `%APPDATA%\LayerPeek` a hesla v Credential Manager odinstalace **ponechá** (rozhodnuto 2026-10-21).
- Připnutí tray ikony (T-061, workaround): hook `--veloapp-uninstall` zavolá `TrayIconPromotion.ClearPromotions(Environment.ProcessPath)` (s `NotifyIconSettingsRegistry`). Smaže jen hodnotu `IsPromoted` u záznamů vlastního exe v `HKCU\Control Panel\NotifyIconSettings`, samotné záznamy ponechá (Explorer smazaný záznam za běhu znovu nevytvoří a přeinstalovaná ikona by zůstala v přetečení). Hook `--veloapp-install` nic nedělá, záznam vzniká až po prvním zobrazení ikony a připne ho aplikace sama.
- Instalační hooky musí proběhnout dřív než kontrola jediné instance (T-043) a bez UI (ověřeno v T-056).
- Licence Velopacku (MIT) do okna O aplikaci (licence třetích stran).
- Aplikace nesmí ukládat vlastní soubory do `%LocalAppData%\LayerPeek` (spravuje ji Velopack). Výjimka jsou logy z T-024 (`%LOCALAPPDATA%\LayerPeek\logs`, rozhodnuto 2026-10-24): zůstávají tam a odinstalace je smaže.

## Akceptační kritéria
- [x] Testy (rozhodnuto 2026-10-24): unit testy (pořadí `VelopackApp.Run()` v `Main`, logika hooků, autostart, volba předběžných verzí), Pester testy balicího kroku Velopack a E2E test skutečné instalace a odinstalace `Setup.exe --silent` přes `tools/run-e2e.ps1` (se žurnálem obnovy). E2E ověří AUMID, `ToastActivatorCLSID`, registraci COM a úklid po odinstalaci.
- [x] Postup sestavení a vydání pro Windows zdokumentovaný v `docs/release.md` (z T-055), včetně varování SmartScreen u nepodepsané aplikace
- [x] Instalátor `win-x64` nainstaluje aplikaci bez admin práv, zástupce má AUMID `LayerPeek` a `ToastActivatorCLSID` (ověřeno)
- [x] Instalátor `win-arm64` vznikne stejným skriptem, pokud je `win-arm64` zapnutý (vydává se až po T-057; spuštění na ARM64 zatím nejde ověřit, uživatel nemá zařízení) → přesunuto do T-114
- [x] Odinstalace nezanechá zástupce, klíč notifikací, toasty ani hodnotu `IsPromoted` z T-061 (ověřeno)
- [x] Odinstalace smaže registraci COM aktivátoru toastů `HKCU\Software\Classes\CLSID\{EC47413C-984D-447A-866A-5F780E408DE7}` z T-068 (ověřeno); po instalaci kliknutí na toast v centru oznámení otevře web tiskárny, i když aplikace neběží (ověřeno) → přesunuto do T-114
- [x] Autostart po přihlášení funguje na Windows (ověřeno) → přesunuto do T-114
- [x] Nový NuGet balíček (Velopack) je ověřeně AOT/trim kompatibilní (T-056), AOT publish bez varování
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- ~~Formát pro Windows~~ → instalátor (preferovaný Velopack, čeká na T-056) + `.zip` (rozhodnuto 2026-10-21).
- ~~Podepisování kódu na Windows~~ → bez podpisu, SmartScreen varování se akceptuje (rozhodnuto 2026-10-21).
- ~~Odkud automatické aktualizace~~ → z veřejných GitHub Releases bez tokenu v aplikaci (repozitář bude později veřejný, rozhodnuto 2026-10-21). Implementace podle výsledku T-056. Platí i pro macOS (T-019).
- ~~Má být autostart volitelný v nastavení (T-015), nebo ho řeší jen instalátor?~~ → volba v okně nastavení, výchozí stav **vypnuto**. Aplikace sama vytváří a maže zástupce ve složce Startup (cíl v instalační složce, takže ho Velopack při odinstalaci smaže), `--shortcuts Startup` se nepoužije (rozhodnuto 2026-10-24).
- ~~Závislost na T-055~~ → T-017 může začít, i když T-055 čeká jen na zkušební vydání na GitHubu (rozhodnuto 2026-10-24).
- ~~Pokud T-056 Velopack nedoporučí~~ → Velopack přijatý, WiX se nepoužije (rozhodnuto 2026-10-21).
- Z T-056 (uzavřen 2026-10-21, zbylé otázky se rozhodují zde; podrobnosti v `tasks/done/T-056-velopack-spike.md`):
  - ~~zástupce na ploše~~ → jen Start menu, bez plochy (rozhodnuto 2026-10-21);
  - ~~data při odinstalaci~~ → nastavení a hesla se ponechají, uklidí se jen systémové registrace (rozhodnuto 2026-10-21);
  - ~~skrytá volba `vpk pack --aumid`~~ → přijatelná (rozhodnuto 2026-10-21);
  - ~~delta aktualizace~~ → zatím `--delta None`, vrátit se k tomu po zveřejnění repozitáře (rozhodnuto 2026-10-21);
  - ~~`.zip` z T-055, `Portable.zip` Velopacku, nebo oba?~~ → jen `.zip` z T-055, balit s `--noPortable` (rozhodnuto 2026-10-24);
  - ~~hledat předběžná vydání i po verzi 1.0?~~ → nová volba v nastavení „nabízet předběžné verze“. Dokud ji uživatel nezměnil, platí výchozí hodnota podle běžící verze: na `0.x` zapnuto, od `1.0` vypnuto. Výslovná volba uživatele má vždy přednost (rozhodnuto 2026-10-24);
  - ~~je přijatelný log Velopacku, který zůstane po odinstalaci v `%LocalAppData%\velopack`?~~ → přijatelné, zdokumentovat jako známý zbytek (`docs/release.md`, README), E2E test ho toleruje (rozhodnuto 2026-10-24).
- SmartScreen u nepodepsaného `Setup.exe` se značkou Mark-of-the-Web ověřil T-056 ručně (2026-10-21): „Systém Windows ochránil váš počítač“ → „Další informace“ → „Neznámý vydavatel“, „Přesto spustit“ (`docs/velopack.md`, bod 9). Neověřené zůstává skutečné stažení z GitHubu prohlížečem (SmartScreen v Edge při stahování). Ověřit při prvním vydání a obojí popsat v `docs/release.md`.
- Nové otázky z implementace (2026-10-24, ui-dev):
  1. ~~Kam s logy? Logy z T-024 jsou v `%LOCALAPPDATA%\LayerPeek\logs`, uvnitř instalační složky Velopacku.~~ → logy **zůstávají** v `%LOCALAPPDATA%\LayerPeek\logs`, nic se nepřesouvá. Smazání logů při odinstalaci se přijímá. Pravidlo „nic vlastního v `%LocalAppData%\LayerPeek`“ platí dál pro všechno ostatní. Chování dialogu „přepsat/opravit“ u neprázdné složky ověřit a zdokumentovat (`docs/release.md`, `docs/velopack.md`), bez změny kódu. E2E se kvůli složce nepřeskakuje, logy zálohuje v žurnálu a vrátí (rozhodnuto 2026-10-24).
  2. ~~Umístění voleb v okně nastavení~~ → třetí záložka „Spuštění a aktualizace“ zůstává. Layout, název a texty projde ui-designer (zařídí hlavní agent), layout se zatím dál nemění (rozhodnuto 2026-10-24).
  3. ~~Autostart u `.zip` a vývojového sestavení~~ → jen pro nainstalovanou verzi. V nenainstalovaném exe je volba vidět neaktivní s vysvětlením (všech 8 jazyků) (rozhodnuto 2026-10-24).
  4. ~~Název instalátoru~~ → `LayerPeek-<verze>-<RID>-Setup.exe` (rozhodnuto 2026-10-24).
  5. ~~`--packAuthors`~~ → „Petr Foltýnek“ (rozhodnuto 2026-10-24).
  6. ~~Pořadí v `Main`~~ → nejdřív `NativeLibrarySearch.Restrict()`, hned potom `VelopackStartup.Run(args)`. Přesně tohle pořadí hlídá test (rozhodnuto 2026-10-24).
  7. ~~E2E na vývojovém stroji (vymazaná historie toastů, dočasná falešná položka `NotifyIconSettings`)~~ → přijatelné (rozhodnuto 2026-10-24).
- Otázky z review (ui-designer, qa-reviewer, 2026-10-24):
  - ~~Nápověda k předběžným verzím~~ → výslovně uvést, že vypnutí na `0.x` znamená žádné aktualizace (rozhodnuto 2026-10-24).
  - ~~„Nabízet předběžné verze“ v nenainstalované verzi~~ → také neaktivní, se stejným důvodem jako autostart (společný text `SettingsNeedsInstallation` v obecném znění), důvod i jako HelpText (rozhodnuto 2026-10-24).
  - ~~Hláška `SettingsSavedAutostartFailed`~~ → s úvodem „Nastavení je uloženo.“ jako ostatní hlášky po uložení (rozhodnuto 2026-10-24).
  - ~~Návrhy designéra N1–N7~~ → přijaty všechny (rozhodnuto 2026-10-24).
  - ~~Název a viditelnost záložky na macOS~~ → řeší T-019 (položka doplněna do `tasks/todo/T-019-macos-packaging-autostart.md`), teď beze změny kódu (rozhodnuto 2026-10-24).
  - ~~Volba předběžných verzí před T-058~~ → zůstává viditelná (rozhodnuto 2026-10-24).
  - ~~Varování komponent během hooku~~ → jen se zapíšou do logu Velopacku, hook selže jen při skutečně selhaném kroku (rozhodnuto 2026-10-24).
  - ~~Přepis zástupce~~ → u existujícího zástupce měnit jen AUMID a `ToastActivatorCLSID` (popis, ikona a další vlastnosti Velopacku zůstanou), s testem (rozhodnuto 2026-10-24).

## Poznámky k implementaci
- Uzavřeno 2026-10-30: zbývající ruční ověření přesunuto do T-114 (rozhodnutí uživatele)

**Hotovo (2026-10-24, ui-dev; nic necommitováno):**

- **Velopack 1.2.161** (MIT) v `Directory.Packages.props`, `vpk` 1.2.161 v `.config/dotnet-tools.json` s `"rollForward": true`. Na tomto stroji jsou runtime .NET 8–10, `vpk` tedy běží i bez roll-forwardu. Licence Velopacku je v okně O aplikaci (`Licenses/Texts/mit-velopack.txt`, doslovný text s dvojitými mezerami; test MIT textu teď mezery normalizuje).
- **Start:** `Installer/VelopackStartup` (pořadí viz otázka 6). Velopack se vytvoří jen uvnitř instalace, jinak by lokátor při každém startu zapsal log do `%LOCALAPPDATA%\velopack`. Start s `--veloapp-*` mimo instalaci skončí s kódem 0 bez akce. `SetAutoApplyOnStartup(false)`. Hooky jen za `OperatingSystem.IsWindows()` (`[SupportedOSPlatform]`).
- **Hooky** (`Installer/InstallerHooks`, produkční identita): instalace = `WindowsToastPlatform.Register` (COM `LocalServer32` + `ToastActivatorCLSID` v zástupci). Odinstalace: historie toastů, klíč CLSID `{EC47413C-…}`, `Notifications\Settings\LayerPeek` (nový `WindowsNotificationSettingsRegistry`), `TrayIconPromotion.ClearPromotions` a zástupce autostartu. Každý krok běží i po chybě předchozího. Chyby a varování komponent (`HookLog`) hook vyhodí jako výjimku, Velopack ji zapíše do logu a pokračuje.
- **Aktualizace** (`Installer/AppUpdates`, `VelopackUpdateEngine`): `GithubSource(RepositoryUrl, accessToken: null, prerelease: <efektivní volba>)`. Každá chyba kontroly = „žádná aktualizace“ (log Information). Použití: stáhnout, `WaitExitThenApplyUpdates`, pak běžné ukončení předané volajícím (uloží stav). Jen v instalaci, nikdy v testovacím režimu. V DI je registrované, ale volá ho až T-058 (položka menu, notifikace, frekvence kontroly).
- **Předběžné verze:** `AppSettings.OfferPrereleaseUpdates` (`bool?`, do souboru se zapíše až výslovná volba) a `Core/Updates/PrereleaseUpdates` (`0.x` zapnuto, od `1.0` vypnuto, nečitelná verze = zapnuto). Když uživatel volbu přepne a vrátí, zůstane nezvolená.
- **Autostart:** `IAutostart` + `StartupShortcutAutostart` (zástupce `LayerPeek.lnk` ve složce Startup, cíl běžící exe, AUMID a `ToastActivatorCLSID` jako zástupce ve Start menu). Testovací režim má složku `Startup` ve své datové složce (`AppEnvironment.AutostartShortcutPath`). Mění se až tlačítkem Uložit a jen po úspěšném uložení nastavení. Při chybě zůstane okno otevřené s hláškou a změna k uložení. Nabízí se jen nainstalovanému `LayerPeek.exe` na Windows, testhost ho nikdy nedostane.
- **UI:** záložka „Spuštění a aktualizace“ (otázka 2), 5 nových textů ve všech 8 jazycích (`SettingsTabStartupUpdates`, `SettingsStartWithWindows`, `SettingsOfferPrereleaseUpdates`, `…Hint`, `SettingsSavedAutostartFailed`). 17 snímků okna nastavení přegenerováno (nová záložka v hlavičce) a přibyl `settings-startup-updates.png`.
- **Skripty:** krok `installer` v `scripts/release-build.ps1` (Windows). Před buildem kontrola shody verzí a `dotnet tool restore`. Funkce `Get-VelopackPackArguments`, `Get-VelopackOutputMap`, `Copy-VelopackOutput`, `Get-VelopackVersionProblems`, `Test-RuntimeFileName` a `Get-ChangelogSection -AllowUndated` (build balí i nedatované poznámky, upload datum vyžaduje dál). Kontrola `win-arm64` při zveřejnění teď pozná i `releases.win-arm64.json`, `assets.win-arm64.json` a `RELEASES-win-arm64` (dřív jen `-win-arm64[.-]`).
- **E2E:** `VelopackInstallerTests` + druh zásahu `Installer` (`InstallerKind`, žurnál: zástupce ve Start menu a ve Startup po bajtech, klíče Uninstall/CLSID/Notifications rekurzivně, položky `NotifyIconSettings` instalace, logy a dočasná složka Velopacku). Varování je v `tools/RunE2E.SystemChanges.psm1`, ruční obnova v `docs/troubleshooting.md`. Test se přeskočí bez instalátoru, při skutečné instalaci, když běží LayerPeek, mimo x64, při běhu jako správce a v AOT průchodu.
- **Dokumentace:** `docs/release.md` (sekce „Instalátor pro Windows“: příkaz, soubory, SmartScreen, co zůstane po odinstalaci, E2E), `docs/velopack.md` (implementace), `docs/prerequisites.md` (`vpk`), `docs/architecture.md`, `docs/troubleshooting.md`, `README.md` + `README.cz.md` (instalátor, SmartScreen, log Velopacku jako známý zbytek), `CHANGELOG.md`.
- **Autostart jen v instalaci** (rozhodnuto 2026-10-24): `AppServices.CreateAutostart` vrací `AutostartOption(Autostart, NeedsInstallation)`. Autostart dostane jen exe uvnitř instalace Velopacku. `.zip` a vývojové sestavení mají volbu vidět neaktivní s vysvětlením `SettingsNeedsInstallation` (8 jazyků). Testy: `AutostartExistsOnlyForTheInstalledApp`, `WithoutAnInstallationTheOptionIsDisabledWithTheReason` a další, snímek `settings-startup-updates-not-installed.png`.
- **E2E po rozhodnutí 2026-10-24:** složka s logy už test nepřeskočí. Test ji zkopíruje do `%LOCALAPPDATA%\LayerPeek.E2E.InstallerBackup` a obnova soubory vrátí (log, který mezitím znovu vznikl, dostane starý obsah na začátek) a zálohu smaže. Bez složky si test vytvoří zástupný log. Interaktivní `Setup.exe` zruší dialog „přepsat/opravit“ zprávou WM_CLOSE (uloží snímek), potom proběhne tichá instalace. Test se přeskočí, když běží nějaký LayerPeek (jeho log by byl otevřený). Obnova uklidí i složky `LayerPeek.<16 znaků>`, které nechal `Setup.exe`.
- **Oprava `release-build.ps1`:** výstup `vpk` šel do návratové hodnoty kroku a skript pak spadl na `Cannot find drive '[12'`. Teď jde přes `| Out-Host` a hlídá to Pester test.

**Ověřeno (Windows 11 x64, 2026-10-24, po ukončení uživatelovy instance):**

- `dotnet build` (in-place): 0 chyb, 0 varování. `dotnet test`: 5475, 0 selhání, 118 přeskočeno (E2E bez `LAYERPEEK_E2E` a podobně). Pester: 128 prošlo.
- `pwsh scripts/release-build.ps1`: exit 0. Release build a testy (5353, 0 selhání), AOT publish, smoke test prošel, výstup: `LayerPeek-0.1.0-win-x64.zip` (70,4 MB), `-full.nupkg` (72,8 MB), `releases.win-x64.json`, `assets.win-x64.json`, `RELEASES-win-x64`, `LayerPeek-0.1.0-win-x64-Setup.exe` (80,0 MB), `SHA256SUMS-windows.txt`. Strom byl špinavý, takže se výstup nedá nahrát (očekávané).
- `pwsh tools/run-e2e.ps1 -Class '*VelopackInstallerTests'`: 1 prošlo, 0 přeskočeno, „System changes (after the run): nothing pending“, „Differences from the start: none“. Na tomto stroji existovala složka `%LOCALAPPDATA%\LayerPeek\logs` (log vývojového sestavení). Ověřeno:
  - **dialog „přepsat/opravit“** (P, snímek `002-installer-overwrite-dialog.png`): „LayerPeek Setup – LayerPeek is already installed – This application is already installed on your computer. If it is not working correctly, you can try repairing it by reinstalling. Installed at: …\Local\LayerPeek“, tlačítka *Cancel* / *Open Install Directory* / *Repair*. Zavření (= *Cancel*) nic nenainstalovalo a logy zůstaly beze změny;
  - **tichá instalace přes neprázdnou složku** (P): exit 0, logy smazané, žádná zbylá složka `LayerPeek.<16 znaků>`. *Repair* v interaktivním dialogu dělá podle zdrojového kódu totéž (D, neklikal jsem ho);
  - instalace bez UAC (`UseShellExecute = false`, neelevovaný proces), zástupce ve Start menu s cílem `current\LayerPeek.exe`, AUMID `LayerPeek` a `ToastActivatorCLSID` `{EC47413C-…}` (hook instalace), bez zástupce na ploše, `LocalServer32` = `"…\current\LayerPeek.exe" -ToastActivated`, aplikace se po tiché instalaci nespustila;
  - odinstalace (`Update.exe --uninstall --silent`, hook proběhl za 75 ms bez chyby, tedy i vymazání historie toastů): složka, klíč Uninstall, zástupce ve Start menu i ve Startup, klíč CLSID a `Notifications\Settings\LayerPeek` zmizely, položka `NotifyIconSettings` zůstala bez `IsPromoted`;
  - po obnově byly SHA256 logu a vývojového zástupce stejné jako před během, klíče CLSID a `Notifications\Settings\LayerPeek` měly stejné hodnoty a záloha i `%LOCALAPPDATA%\velopack` zmizely.
- Dřív, s `--artifacts-path`: `vpk pack` ručně, AOT exe s `--veloapp-uninstall` mimo instalaci (exit 0, žádný log Velopacku) a testovací režim Debug exe.

**Neověřeno:**

- Klik na toast po instalaci, když aplikace neběží, a autostart po skutečném přihlášení (ruční kontrola; instalovanou aplikaci testy nespouštějí). Postup: skript `tools/windows-verify.ps1` (T-105) – kontroly `win.e2e-installer`, `win.install`, `win.toast-click`, `win.autostart`, `win.uninstall`; souhrnně v [`docs/windows-manual-testing.md`](../../docs/windows-manual-testing.md).
- Interaktivní tlačítko *Repair* (jen podle zdrojového kódu Velopacku) a návratový kód zrušeného `Setup.exe` (test ho jen vypisuje).
- ~~Smazání toastů v centru oznámení přímo~~ → ověřeno po review: testovací toast byl v historii a po odinstalaci zmizel (viz „Ověřeno po opravách“).
- `win-arm64` (chybí MSVC ARM64, T-063/T-057). Stažení z GitHubu (SmartScreen v Edge) až při prvním vydání.
- Postřeh mimo T-017: `aot-smoke.ps1` vrací zástupce ve Start menu, ale ne registraci produkčního aktivátoru. Po smoke testu ukazuje `LocalServer32` na exe v `artifacts/release/<verze>/publish`, dokud se znovu nespustí vývojové sestavení. Stav z dřívějška (T-068/T-055), nic jsem neměnil.

**Opravy po review (2026-10-24):**

- Texty: nápověda k předběžným verzím (vypnutí na `0.x` = žádné aktualizace), `SettingsSavedAutostartFailed` s „Nastavení je uloženo.“, obecné znění `SettingsNeedsInstallation`, N1–N5 (en/pl popisek autostartu, pl „wersje wstępne“, de „Autostart und Updates“, sk „Spustenie a aktualizácie“), N6 komentář v XAML.
- „Nabízet předběžné verze“ je v nenainstalované verzi neaktivní se stejným důvodem. `AutostartHelpText` a `PrereleaseHelpText` ve VM (obnovují se při změně jazyka) slouží jako HelpText obou voleb (M3).
- N7: nová teorie layoutu `StartupAndUpdatesTabFitsInEveryLanguage` (8 jazyků: bez instalace, a s hláškou o neúspěšném autostartu vedle Uložit/Zrušit). Přegenerovány 3 snímky (`settings-startup-updates`, `…-not-installed`, `settings-camera-preview-4x3-de` kvůli německému názvu záložky).
- L9: `HookLog` už nepřidává varování k chybám. Informace a varování komponent jdou do logu Velopacku (`VelopackLocator.Current.Log`), hook selže jen při selhaném kroku.
- L2: `WindowsShortcutStore.Write` existující zástupce otevře (`STGM_READWRITE`) a změní jen AUMID a `ToastActivatorCLSID`, cíl a pracovní složku jen při jiném cíli. Vynechaná hodnota se smaže. Testy `AnExistingShortcutKeepsItsOtherPropertiesWhenTheIdentityIsAdded` (popis a ikona zůstanou) a `AnExistingShortcutOfAnotherTargetIsPointedToTheNewOne`.
- L1: chyba `VelopackApp.Run()` při běžném startu aplikaci nezastaví (`Run(args, out failure)`, zapíše se do logu, jakmile log existuje). Start s argumentem hooku vrátí -1. L3: zrušené `ApplyAsync` = Information + `false`. L4: komentář k `WaitAsync`.
- E2E (QA M1–M3, L5–L8):
  - po instalaci test zobrazí jeden tichý toast `LayerPeek` (Windows PowerShell 5.1, WinRT) a ověří, že je v historii, a po odinstalaci, že historie je prázdná;
  - zástupce autostartu míří na neexistující soubor mimo instalaci, takže ho smaže jen hook (Velopack maže jen zástupce do instalace) a restart mezi tím nic nespustí;
  - obnova odinstaluje jen instalaci verze testu vytvořenou po začátku zásahu (verze z `current\sq.version` a čas vytvoření `current`), jinak nahlásí konflikt a nic nesmaže;
  - odstranění instalace je krok, jehož selhání nezastaví ostatní kroky obnovy (záznam zůstane v žurnálu);
  - logy se porovnávají jako bajtový prefix (SHA-256 začátku souboru), původní obsah se nikdy neztratí;
  - text varování v `RunE2E.SystemChanges.psm1` opraven (velikost písmen, dočasný zástupce, položka `NotifyIconSettings`, testovací toast, vymazání všech toastů).

**Ověřeno po opravách (2026-10-24, in-place, žádný cizí proces LayerPeek):** `dotnet build` 0 chyb a 0 varování; `dotnet test` 5491, 0 selhání, 117 přeskočeno; Pester 128 prošlo; `pwsh scripts/release-build.ps1` exit 0 (Release testy 5369, 0 selhání, AOT publish win-x64 s `-warnaserror` bez varování IL, smoke test prošel, stejné soubory v `dist/`). `pwsh tools/run-e2e.ps1 -Class '*VelopackInstallerTests'`: 1 prošlo, „nothing pending“, „Differences from the start: none“. SHA-256 logu a vývojového zástupce, `LocalServer32` i klíč `Notifications\Settings\LayerPeek` jsou po běhu stejné jako před ním, nezůstal zástupce ve Startup, záloha ani `%LOCALAPPDATA%\velopack`. Při prvním pokusu selhalo čtení historie toastů v testu (chyba testu, opraveno). Obnova ze žurnálu přitom instalaci sama odinstalovala a vrátila stav, tím je ověřená i cesta obnovy po selhání.

**Druhé kolo review (2026-10-24):**

- R1: nápověda k předběžným verzím je bez instalace skrytá (`IsVisible="{Binding IsPrereleaseOptionAvailable}"`), zobrazí se jen důvod. UI test to ověřuje a snímek `settings-startup-updates-not-installed.png` je přegenerovaný.
- R2: klíč `SettingsStartWithWindowsNotInstalled` je přejmenovaný na `SettingsNeedsInstallation` (8 resx, XAML, VM, testy). AutomationId řádku u autostartu zůstal `SettingsStartWithWindowsNotInstalled`, řádek u předběžných verzí má `SettingsOfferPrereleaseUpdatesNotInstalled`.
- N1: testovací toast má `SuppressPopup = $true` (jen do centra oznámení, žádný banner, na který by šlo kliknout), v historii se počítá dál.
- N2: `InstallerKind.InstallationConflict` má čistou variantu. Napůl smazanou instalaci (bez `current\sq.version`, se zamčeným `Update.exe`) bere jako testovou, když `Update.exe` vznikl po začátku zásahu. Nové unit testy `InstallerKindTests` (shodná verze, jiná verze, instalace z doby před zásahem, tolerance zaokrouhlení času, napůl smazaná instalace testu a cizí instalace).
- Drobnosti: test `ValuesLeftOutAreClearedOnALoadedShortcut` pro mazání AUMID/CLSID (VT_EMPTY) u načteného zástupce. V `WindowsShortcutStore` se nový ShellLink vytvoří dřív, než se uvolní starý (žádné dvojí uvolnění). `Matches` bere záznam staršího formátu `path|size` jako neobnovený (zůstane v žurnálu s popisem), místo aby spadl na `IndexOutOfRange`.
- Ověřeno in-place (bez cizího procesu LayerPeek): `dotnet build` 0 chyb a 0 varování; `dotnet test` 5501, 0 selhání, 117 přeskočeno; Pester 128 prošlo; `release-build.ps1` exit 0 (Release testy 5379, 0 selhání, smoke test prošel, nový `Setup.exe`); `run-e2e.ps1 -Class '*VelopackInstallerTests'` 1 prošlo, „nothing pending“, „Differences from the start: none“, SHA-256 logu a vývojového zástupce jsou před i po běhu stejné.
