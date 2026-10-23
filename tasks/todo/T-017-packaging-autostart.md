---
id: T-017
title: Balení a distribuce, autostart – Windows
status: todo
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

- Instalátor Velopack pro `win-x64` a `win-arm64`, instalace pro aktuálního uživatele bez admin práv (`%LocalAppData%\PrusaTrayDiagnostic`, exe v `current\`, cesta je stejná i po aktualizaci)
- NuGet `Velopack` (MIT, ověřeně AOT/trim kompatibilní v T-056) v `Directory.Packages.props`, ve stejné verzi jako nástroj `vpk`. `vpk` jako lokální dotnet tool v `.config/dotnet-tools.json` s připnutou verzí. `vpk` potřebuje runtime .NET 8–10 nebo roll-forward. Doplnit do `docs/prerequisites.md`.
- `VelopackApp.Build()…Run()` jako **první příkaz v `Main`**, před `SingleInstanceGuard` (T-043). Fast-callback hooky jen za `OperatingSystem.IsWindows()` (CA1416). `vpk` u AOT exe pořadí nekontroluje, hlídá ho test nebo review.
- Balicí krok Velopack ve skriptech T-055 (návrh příkazu v `docs/velopack.md`, bod 12): `--packId PrusaTrayDiagnostic`, `--packTitle "Prusa Tray Diagnostic"`, `--aumid PrusaTrayDiagnostic`, `--shortcuts StartMenuRoot` (jen Start menu, bez plochy), `--channel <RID> -r <RID>`, `--delta None` (zatím bez delt), `--releaseNotes` z `CHANGELOG.md`. Výstup (`*-full.nupkg`, `releases.<RID>.json`, `assets.<RID>.json`, `RELEASES-<RID>`, `Setup.exe`) jde do kontrolních součtů a do GitHub vydání přes `gh` (bez `vpk upload`).
- Aktualizace: `UpdateManager` + `GithubSource("https://github.com/pfoltynek/PrusaTrayDiagnostic", accessToken: null, prerelease: true)`, bez tokenu v aplikaci. Každou výjimku kontroly (dokud je repozitář soukromý, `HttpRequestException` 404) brát jako „aktualizace nejsou k dispozici“. Aktualizaci spouští primární instance až po uložení stavu (`ApplyUpdatesAndRestart`), protože Velopack běžící procesy z instalační složky násilně ukončí.
- Autostart po přihlášení (zástupce ve složce Startup, který aplikace vytváří sama a Velopack ho při odinstalaci smaže, nebo registry `Run`, kterou musí smazat hook `--veloapp-uninstall`; `--shortcuts Startup` jen pro „vždy po instalaci“). Podle odpovědi na otázku níže.
- Toasty (navazuje na T-010):
  - zástupce ve Start menu se stejným AppUserModelID `PrusaTrayDiagnostic` a názvem „Prusa Tray Diagnostic“ (obojí je finální). Vytváří ho Velopack (`--shortcuts StartMenuRoot --aumid PrusaTrayDiagnostic`). `AppShortcutRegistration` zůstává jako pojistka při startu a v hooku `--veloapp-install` (doporučení T-056, bod 4);
  - COM aktivátor (`ToastActivatorCLSID` v zástupci a registrace v `HKCU\Software\Classes\CLSID`), aby kliknutí na toast fungovalo, i když aplikace neběží. **Aplikace ho už má (T-068, rozhodnutí uživatele 2026-10-23):** pevné CLSID `{E8389DCA-851B-498C-8082-69B1AB263C14}` (`AppEnvironment.ProductionToastActivatorClsid`), při startu zapíše `HKCU\Software\Classes\CLSID\{E8389DCA-851B-498C-8082-69B1AB263C14}\LocalServer32` = `"<exe>" -ToastActivated` a vlastnost zástupce `System.AppUserModel.ToastActivatorCLSID` (obojí jen když se liší). Velopack `ToastActivatorCLSID` nastavit neumí, ale při aktualizaci ho zachová (ověřeno v T-056). Hook `--veloapp-install` má zavolat stejnou registraci jako start (`WindowsToastPlatform.Register`). Cesta exe ve `LocalServer32` je `current\PrusaTrayDiagnostic.exe` a po aktualizaci se nemění;
  - **odinstalace musí smazat registraci produkčního COM aktivátoru** `HKCU\Software\Classes\CLSID\{E8389DCA-851B-498C-8082-69B1AB263C14}` (celý klíč; hook `--veloapp-uninstall`). Jinak by klik na starý toast po odinstalaci zkoušel spustit neexistující exe (Windows klik jen zahodí, ale klíč by v registru zůstal);
  - odinstalace uklidí jen systémové registrace: zástupce (Velopack sám), klíč `HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\PrusaTrayDiagnostic`, registraci COM aktivátoru, autostart a historii toastů. Vše kromě zástupce dělá hook `--veloapp-uninstall` (limit 30 s, bez UI). Historie toastů potřebuje nový WinRT interop (`ToastNotificationManager.History.Clear`). Nastavení v `%APPDATA%\PrusaTrayDiagnostic` a hesla v Credential Manager odinstalace **ponechá** (rozhodnuto 2026-10-21).
- Připnutí tray ikony (T-061, workaround): hook `--veloapp-uninstall` zavolá `TrayIconPromotion.ClearPromotions(Environment.ProcessPath)` (s `NotifyIconSettingsRegistry`). Smaže jen hodnotu `IsPromoted` u záznamů vlastního exe v `HKCU\Control Panel\NotifyIconSettings`, samotné záznamy ponechá (Explorer smazaný záznam za běhu znovu nevytvoří a přeinstalovaná ikona by zůstala v přetečení). Hook `--veloapp-install` nic nedělá, záznam vzniká až po prvním zobrazení ikony a připne ho aplikace sama.
- Instalační hooky musí proběhnout dřív než kontrola jediné instance (T-043) a bez UI (ověřeno v T-056).
- Licence Velopacku (MIT) do okna O aplikaci (licence třetích stran).
- Aplikace nesmí ukládat vlastní soubory do `%LocalAppData%\PrusaTrayDiagnostic` (spravuje ji Velopack), týká se i logů z T-024.

## Akceptační kritéria
- [ ] Postup sestavení a vydání pro Windows zdokumentovaný v `docs/release.md` (z T-055), včetně varování SmartScreen u nepodepsané aplikace
- [ ] Instalátor `win-x64` nainstaluje aplikaci bez admin práv, zástupce má AUMID `PrusaTrayDiagnostic` a `ToastActivatorCLSID` (ověřeno)
- [ ] Instalátor `win-arm64` vznikne stejným skriptem, pokud je `win-arm64` zapnutý (vydává se až po T-057; spuštění na ARM64 zatím nejde ověřit, uživatel nemá zařízení)
- [ ] Odinstalace nezanechá zástupce, klíč notifikací, toasty ani hodnotu `IsPromoted` z T-061 (ověřeno)
- [ ] Odinstalace smaže registraci COM aktivátoru toastů `HKCU\Software\Classes\CLSID\{E8389DCA-851B-498C-8082-69B1AB263C14}` z T-068 (ověřeno); po instalaci kliknutí na toast v centru oznámení otevře web tiskárny, i když aplikace neběží (ověřeno)
- [ ] Autostart po přihlášení funguje na Windows (ověřeno)
- [ ] Nový NuGet balíček (Velopack) je ověřeně AOT/trim kompatibilní (T-056), AOT publish bez varování
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- ~~Formát pro Windows~~ → instalátor (preferovaný Velopack, čeká na T-056) + `.zip` (rozhodnuto 2026-10-21).
- ~~Podepisování kódu na Windows~~ → bez podpisu, SmartScreen varování se akceptuje (rozhodnuto 2026-10-21).
- ~~Odkud automatické aktualizace~~ → z veřejných GitHub Releases bez tokenu v aplikaci (repozitář bude později veřejný, rozhodnuto 2026-10-21). Implementace podle výsledku T-056. Platí i pro macOS (T-019).
- Má být autostart volitelný v nastavení (T-015), nebo ho řeší jen instalátor?
- ~~Pokud T-056 Velopack nedoporučí~~ → Velopack přijatý, WiX se nepoužije (rozhodnuto 2026-10-21).
- Z T-056 (uzavřen 2026-10-21, zbylé otázky se rozhodují zde; podrobnosti v `tasks/done/T-056-velopack-spike.md`):
  - ~~zástupce na ploše~~ → jen Start menu, bez plochy (rozhodnuto 2026-10-21);
  - ~~data při odinstalaci~~ → nastavení a hesla se ponechají, uklidí se jen systémové registrace (rozhodnuto 2026-10-21);
  - ~~skrytá volba `vpk pack --aumid`~~ → přijatelná (rozhodnuto 2026-10-21);
  - ~~delta aktualizace~~ → zatím `--delta None`, vrátit se k tomu po zveřejnění repozitáře (rozhodnuto 2026-10-21);
  - `.zip` z T-055, `Portable.zip` Velopacku, nebo oba?
  - hledat předběžná vydání i po verzi 1.0?
  - je přijatelný log Velopacku, který zůstane po odinstalaci v `%LocalAppData%\velopack`?
- SmartScreen u nepodepsaného `Setup.exe` se značkou Mark-of-the-Web ověřil T-056 ručně (2026-10-21): „Systém Windows ochránil váš počítač“ → „Další informace“ → „Neznámý vydavatel“, „Přesto spustit“ (`docs/velopack.md`, bod 9). Neověřené zůstává skutečné stažení z GitHubu prohlížečem (SmartScreen v Edge při stahování). Ověřit při prvním vydání a obojí popsat v `docs/release.md`.

## Poznámky k implementaci
