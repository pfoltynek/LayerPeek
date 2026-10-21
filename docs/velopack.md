# Velopack – ověření pro instalátor a aktualizace (T-056)

Výsledek spiku T-056 (2026-10-21). Ověřovalo se na Windows 11 (x64) s .NET SDK `11.0.100-rc.2`. Pokusy proběhly v kopii repozitáře mimo repozitář (`git archive` do dočasné složky). Do repozitáře nešla žádná změna kódu ani balíčku.

Značky u bodů: **(P)** ověřeno pokusem, **(D)** z dokumentace nebo ze zdrojového kódu Velopacku, **(neověřeno)** nešlo ověřit.

## Doporučení

- **Windows (T-017): Velopack ano, s podmínkami níže. Uživatel doporučení přijal 2026-10-21 (včetně závislosti na skryté volbě `--aumid`), WiX se nepoužije.** Všechny body, které šly ověřit pokusem, prošly: AOT publish bez varování, instalace bez admin práv, hooky před kontrolou jediné instance, aktualizace se zachováním cesty exe i `ToastActivatorCLSID` v zástupci, čtení GitHub Releases bez tokenu a úklid při odinstalaci.
  Podmínky:
  1. `VelopackApp.Build()…Run()` musí být první příkaz v `Main`, před `SingleInstanceGuard` (T-043).
  2. Balit s `--aumid PrusaTrayDiagnostic` (skrytá volba `vpk`, viz bod 4), `--packTitle "Prusa Tray Diagnostic"` a `--shortcuts StartMenuRoot` (jen Start menu, bez plochy, rozhodnuto 2026-10-21).
  3. `ToastActivatorCLSID` do zástupce a registraci COM aktivátoru zapisuje aplikace (v hooku `--veloapp-install` a při startu). Úklid registrace, klíče notifikací a historie toastů dělá hook `--veloapp-uninstall`.
  4. Kontrola aktualizací musí chytat výjimky. Dokud je repozitář soukromý, končí `HttpRequestException` 404.
  5. Kanály podle RID: `--channel win-x64` a `--channel win-arm64`.
- **macOS (T-019): s podmínkami, rozhodne pokus na Macu.** `vpk` pro macOS běží jen na Macu a vytváří `.app`, `.pkg` a `Portable.zip`, ne `.dmg`. Dokumentace považuje podpis a notarizaci za nutné. Chování s ad-hoc podpisem dokumentace nepopisuje a tady ho nešlo ověřit. Velopack pro macOS proto zatím nedoporučuju ani nezamítám. Rozhodne se v T-019 (viz otevřené otázky).
- **WiX** se nepoužije (rozhodnuto 2026-10-21), protože Velopack vyhověl. Velopack sám umí vyrobit i `.msi` (`vpk pack --msi`, interně vlastní fork WiX). Pro instalaci pro jednoho uživatele to potřeba není.

## Verze a nástroje

| Co | Hodnota |
|---|---|
| NuGet `Velopack` | `1.2.161` (2026-09-29), licence **MIT**, cílové frameworky `net8.0`, `net9.0`, `net10.0`, `netstandard2.0`, `net472`. Aplikace `net11.0` použije `net10.0` (ověřeno buildem). Pro `net8.0+` nemá žádné závislosti. |
| Nástroj `vpk` | `1.2.161`, dotnet tool, licence MIT, framework-dependent pro `net8.0`/`net9.0`/`net10.0` (verze pro `net11.0` není). Potřebuje runtime .NET 8, 9 nebo 10. Na stroji jen s .NET 11 je potřeba roll-forward (`dotnet tool install vpk --allow-roll-forward` nebo `"rollForward": true` v manifestu). |
| Instalace `vpk` | lokální manifest nástrojů: `dotnet new tool-manifest` + `dotnet tool install vpk --version 1.2.161`, spouští se `dotnet vpk …`. Pro T-017 doporučuju manifest `.config/dotnet-tools.json` v repozitáři s připnutou verzí shodnou s NuGet balíčkem. Pokud se verze knihovny a `vpk` liší, `vpk` hlásí chybu nebo varování. Doplnit do `docs/prerequisites.md` v T-017. |
| Instalátor a updater | `Setup.exe` a `Update.exe` jsou nativní programy v Rustu. Na cílovém PC nepotřebují .NET runtime (ověřeno: AOT aplikace bez runtime prerekvizit, `Package Runtime Dependencies` prázdné). |

## Výsledky po bodech

### 1. Native AOT a trimming (P)
- **Pokus:** `Velopack 1.2.161` přidaný přes `Directory.Packages.props` a `PackageReference` do kopie projektu App. Projekt má `IsAotCompatible=true`, `PublishAot=true` a `TreatWarningsAsErrors=true`. Žádné `NoWarn` ani potlačení.
  - `dotnet build`: 0 varování, 0 chyb.
  - `dotnet publish -c Release -r win-x64`: exit 0, **žádné varování IL2xxx/IL3xxx**. Dvě hlášky `ILC: Method … will always throw` pocházejí z Avalonia DComposition a stejně se objeví i u publish bez Velopacku (kontrolní publish ze stejného commitu).
  - Exe naroste z 32,06 MB na 34,45 MB (+2,3 MB).
  - V AOT binárce funguje `VelopackApp.Build().Run()`, všechny čtyři hooky, `OnFirstRun`, `OnRestarted`, `UpdateManager` s lokálním zdrojem (aktualizace se aplikovala) i `GithubSource` (načtení a deserializace feedu z GitHubu).
- **Analýza balíčku:** assembly má metadata `IsTrimmable` a `IsAotCompatible` a JSON deserializuje přes source-generated `JsonSerializerContext` (`CompiledJsonSourceGenerationContext`). Dřívější problém AOT s deserializací enumu ([#122](https://github.com/velopack/velopack/issues/122), 2024) je uzavřený.
- **Pozor pro T-017:** metody `OnAfter…FastCallback`/`OnBefore…FastCallback` mají `[SupportedOSPlatform("windows")]`. Při warnings-as-errors je potřeba volat je za `OperatingSystem.IsWindows()` a kód v hoocích, který volá Windows API (např. `Registry`), dát do metody s `[SupportedOSPlatform("windows")]` (CA1416).
- `vpk` u AOT exe nemůže zkontrolovat, že `Main` volá `VelopackApp.Run()`, protože exe není .NET assembly. Kontrolu tiše přeskočí ([CompatUtil.cs](https://github.com/velopack/velopack/blob/92d6a1c91716729d449034df5c50307dcce39493/src/vpk/Velopack.Packaging.Windows/CompatUtil.cs)). Pořadí v `Main` musí hlídat test nebo review.

### 2. Balení AOT výstupu (P)
- Příkaz (ověřený):
  ```
  dotnet vpk pack --packId PrusaTrayDiagnostic --packVersion 0.1.0 --packDir <publish win-x64> \
    --mainExe PrusaTrayDiagnostic.exe --packTitle "Prusa Tray Diagnostic" --packAuthors "<autor>" \
    --shortcuts StartMenuRoot --aumid PrusaTrayDiagnostic --icon src/PrusaTrayDiagnostic.App/Assets/AppIcon/app-icon.ico \
    --channel win-x64 -r win-x64 -o <výstup>
  ```
- Výstup pro `0.1.0`:

  | Soubor | Velikost |
  |---|---|
  | `PrusaTrayDiagnostic-win-x64-Setup.exe` | 84 MB |
  | `PrusaTrayDiagnostic-0.1.0-win-x64-full.nupkg` | 76 MB |
  | `PrusaTrayDiagnostic-win-x64-Portable.zip` | 76 MB |
  | `releases.win-x64.json`, `assets.win-x64.json`, `RELEASES-win-x64` | < 1 kB |

  Publish složka bez `.pdb` má 179 MB, hlavně FFmpeg.
- Nativní DLL (FFmpeg `avcodec-63.dll`…, `libSkiaSharp.dll`, `libHarfBuzzSharp.dll`, `av_libglesv2.dll`) a `FFmpeg-LICENSE.txt` jdou do balíčku beze změny. `.pdb` vynechá výchozí `--exclude .*\.pdb`. Balíček navíc obsahuje `Squirrel.exe` (= `Update.exe`, 5 MB) a spouštěcí stub `Prusa Tray Diagnostic.exe` (0,5 MB).
- Delta balíčky vznikají, jen když je ve výstupní složce předchozí plný `.nupkg`. 0.1.1 → 0.1.2 (změněné exe) mělo deltu 5,6 MB proti 76 MB. Klient deltu skutečně použil.
- `vpk` nekontroluje, že architektura exe odpovídá `-r`: x64 binárky zabalené s `-r win-arm64` prošly bez varování. Skript musí předat správný RID.

### 3. Instalace pro jednoho uživatele (P)
- `Setup.exe` instaluje bez UAC do `%LocalAppData%\PrusaTrayDiagnostic\` (`Update.exe`, stub `Prusa Tray Diagnostic.exe`, `current\`, `packages\`), zapíše `HKCU\…\Uninstall\PrusaTrayDiagnostic` (položka v Nastavení → Aplikace, `QuietUninstallString` = `Update.exe --uninstall --silent`) a spustí aplikaci.
- Cesta exe: `%LocalAppData%\PrusaTrayDiagnostic\current\PrusaTrayDiagnostic.exe`. **Zůstala stejná po dvou aktualizacích** (0.1.0 → 0.1.1 → 0.1.2), protože se vyměňuje obsah složky `current`. `AppShortcutRegistration` (T-010) proto zástupce po aktualizaci nepřepisuje. Windows ukládá i nastavení ikony v oznamovací oblasti podle cesty exe (`HKCU\Control Panel\NotifyIconSettings`), takže volba „vždy zobrazit“ aktualizaci přežije.
- **Kolize složky:** `%LocalAppData%\<packId>` patří Velopacku. Odinstalace ji celou smaže. Když složka při instalaci není prázdná, `Setup.exe` zobrazí dialog „přepsat/opravit“. Aplikace tam proto nesmí ukládat vlastní data. Dnes je nastavení v `%APPDATA%\PrusaTrayDiagnostic` (Roaming), což je v pořádku. **T-024 (logy „v AppData“) nesmí použít `%LocalAppData%\PrusaTrayDiagnostic`.**

### 4. Zástupce ve Start menu, AppUserModelID a `ToastActivatorCLSID` (P)
- Velopack pojmenuje zástupce podle `--packTitle`, takže vznikne `Prusa Tray Diagnostic.lnk` v kořeni Start menu (`--shortcuts StartMenuRoot`). AUMID nastaví z `--aumid` (ověřeno: `AUMID=PrusaTrayDiagnostic`, cíl `current\PrusaTrayDiagnostic.exe`, pracovní složka `current`). Bez `--aumid` použije `velopack.PrusaTrayDiagnostic` ([WindowsPackCommandRunner.cs](https://github.com/velopack/velopack/blob/92d6a1c91716729d449034df5c50307dcce39493/src/vpk/Velopack.Packaging.Windows/Commands/WindowsPackCommandRunner.cs)). **`--aumid` je v `vpk` 1.2.161 skrytá volba** (`SetHidden`, vypíše ji jen `vpk pack -H`). Dokumentace o ní mlčí, takže se může změnit. Uživatel tuto závislost přijal (2026-10-21).
- `VelopackApp.Run()` nastaví procesu AUMID z manifestu (`shortcutAumid`, jinak `velopack.<packId>`). S `--aumid PrusaTrayDiagnostic` je stejný jako ten z `WindowsAppIdentity`. Lze ho nastavit i přes `VelopackApp.SetAppUserModelId(...)`.
- **`ToastActivatorCLSID` Velopack nastavit neumí.** Nemá volbu v `vpk` a veřejné C# API pro zástupce v 1.2.161 obsahuje jen část `Velopack.Windows.ShellLink`, kterou dokumentace označuje jako „legacy“ bez záruky stability.
- **Co Velopack se zástupci dělá** ([shortcuts.rs](https://github.com/velopack/velopack/blob/92d6a1c91716729d449034df5c50307dcce39493/src/bins/src/windows/shortcuts.rs)):
  - Při instalaci vytvoří zástupce. **Existující soubor se stejným názvem bez dotazu přepíše** (ověřeno).
  - Při **aktualizaci** najde všechny `.lnk` na ploše, ve Startup, ve Start menu (rekurzivně) a v připnutých položkách, jejichž cíl vede do instalační složky (tedy i zástupce vytvořené aplikací). Přepíše jim ikonu a AUMID na hodnotu z manifestu. Ostatní vlastnosti nechá. **Pokus:** `ToastActivatorCLSID` zapsaný do zástupce po instalaci zůstal po aktualizaci zachovaný a AUMID zůstal `PrusaTrayDiagnostic`. Bez `--aumid` by aktualizace AUMID přepsala na `velopack.PrusaTrayDiagnostic` a aplikace by ho opravila až při dalším startu.
  - Zástupce, které uživatel smazal, Velopack při aktualizaci znovu nevytvoří.
  - Při **odinstalaci** smaže všechny zástupce s cílem v instalační složce, i ty, které vytvořila aplikace (ověřeno ve variantě `--shortcuts None`).
- **Varianta `--shortcuts None` (P):** aplikace si zástupce vytvoří při prvním startu (`Setup.exe` aplikaci po instalaci spustí). Odinstalace ho přesto smaže. Funguje, ale po tiché instalaci (`--silent`) aplikace nenaběhne a zástupce chybí, dokud ho nevytvoří hook `--veloapp-install`.
- **Doporučená varianta:** zástupce ve Start menu vytváří Velopack (`--shortcuts StartMenuRoot --aumid PrusaTrayDiagnostic --packTitle "Prusa Tray Diagnostic"`). Aplikace (`AppShortcutRegistration`, rozšířená v T-017) při startu a v hooku `--veloapp-install` doplní nebo opraví `ToastActivatorCLSID` a AUMID. Důvody: zástupce existuje hned po instalaci i po tiché instalaci, Velopack ho udržuje a uklízí a jeho jméno i AUMID jsou stejné jako u zástupce aplikace (žádná kolize, jde o tentýž soubor). Kontrola v aplikaci zůstává jako pojistka.
- **Okrajový případ:** když uživatel spustí i aplikaci z `.zip` (T-055), přepíše zástupce na cestu ze `.zip`. Velopack takového zástupce při aktualizaci ani odinstalaci nenajde, protože jeho cíl vede mimo instalační složku.

### 5. Hooky a odinstalace (P)
- **Pořadí v `Setup.exe`** ([install.rs](https://github.com/velopack/velopack/blob/92d6a1c91716729d449034df5c50307dcce39493/src/bins/src/commands/install.rs)): rozbalení → zástupci → hook `--veloapp-install` (limit 30 s) → klíč Uninstall → start aplikace s `VELOPACK_FIRSTRUN`. Když hook selže nebo vyprší, `Setup.exe` zobrazí varování a instalace pokračuje. Hooky nejdou použít ke zrušení operace a nesmí zobrazovat UI ([dokumentace hooků](https://docs.velopack.io/integrating/hooks)).
- **Aktualizace** ([apply_windows_impl.rs](https://github.com/velopack/velopack/blob/92d6a1c91716729d449034df5c50307dcce39493/src/bins/src/commands/apply_windows_impl.rs)): `--veloapp-obsolete` na staré verzi (15 s) → **násilné ukončení** všech procesů z instalační složky → výměna `current` → `--veloapp-updated` na nové verzi (15 s) → úprava zástupců → restart s `VELOPACK_RESTART`.
- **Odinstalace** ([uninstall.rs](https://github.com/velopack/velopack/blob/92d6a1c91716729d449034df5c50307dcce39493/src/bins/src/commands/uninstall.rs)): **násilné ukončení běžící aplikace** → `--veloapp-uninstall` (v kódu limit 60 s, dokumentace uvádí 30 s) → smazání zástupců → smazání `%LocalAppData%\PrusaTrayDiagnostic` a `%TEMP%\velopack_PrusaTrayDiagnostic` → smazání klíče Uninstall.
- **Jediná instance (T-043):** pokus s `VelopackApp.Run()` jako prvním příkazem v `Main`: hooky (`--veloapp-install`, `--veloapp-uninstall`) spuštěné **při běžící primární instanci** proběhly, skončily s kódem 0 za 20–190 ms a nic nepředaly běžící instanci. Normální druhé spuštění skončilo jako sekundární instance. Hook spuštěný instalátorem trval 91 ms.
- **Úklid v hooku `--veloapp-uninstall` (P):** hook smazal testovací klíč v `HKCU` přes `Microsoft.Win32.Registry` (AOT bez problému). Stejně jde smazat `HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\PrusaTrayDiagnostic`, registraci COM aktivátoru (`HKCU\Software\Classes\CLSID\{…}`) a autostart v `Run`. Skutečný klíč notifikací uživatele se v pokusu nemazal. **Historie toastů** (`ToastNotificationManager.History.Clear("PrusaTrayDiagnostic")`) potřebuje v aplikaci nový WinRT interop (`History`), dnes ho nemá. Patří do T-017.
- **Omezení:**
  - Při aktualizaci i odinstalaci Velopack běžící aplikaci zabije. Aplikace nestihne uložit stav a ikona v oznamovací oblasti může zůstat viset, dokud nad ni uživatel nenajede myší. Aktualizaci proto má spouštět sama primární instance (`ApplyUpdatesAndRestart` až po uložení stavu).
  - Velopack si vede log v `%LocalAppData%\velopack\velopack_PrusaTrayDiagnostic.log`. **Odinstalace ho nesmaže** (ověřeno) a hook to nevyřeší, protože `Update.exe` do logu zapisuje i po hooku.
  - Windows si samo pamatuje spuštěné programy (`MuiCache`, `AppListBackup` Start menu, `NotifyIconSettings`). To dělá u každé aplikace a Velopack to neuklízí.
  - Nastavení v `%APPDATA%\PrusaTrayDiagnostic` a hesla v Credential Manager Velopack neodstraní a hook je mazat nebude: odinstalace je ponechá a uklidí jen systémové registrace (klíč notifikací, COM aktivátor, autostart, historie toastů; rozhodnuto 2026-10-21).

### 6. Autostart (D)
- `--shortcuts Startup` vytvoří při instalaci zástupce ve složce Startup s AUMID a odinstalace ho smaže. Když ho uživatel smaže, aktualizace ho znovu nevytvoří ([shortcuts.rs](https://github.com/velopack/velopack/blob/92d6a1c91716729d449034df5c50307dcce39493/src/bins/src/windows/shortcuts.rs), [dokumentace zástupců](https://docs.velopack.io/integrating/shortcuts)).
- **Volitelný autostart v nastavení** (otázka T-017): Velopack sám přepínač nenabízí. Aplikace by si spravovala vlastní zástupce ve Startup (Velopack ho při odinstalaci smaže sám, protože cíl vede do instalační složky) nebo hodnotu `Run` v registru (tu musí smazat hook `--veloapp-uninstall`). `--shortcuts Startup` se hodí jen pro „autostart vždy po instalaci“.

### 7. Aktualizace z GitHub Releases (D + P bez tokenu)
- `new UpdateManager(new GithubSource("https://github.com/pfoltynek/PrusaTrayDiagnostic", accessToken: null, prerelease: true))`. Kanál se bere z nainstalovaného manifestu (`win-x64`) a hledá se soubor `releases.win-x64.json` ([GitBase.cs](https://github.com/velopack/velopack/blob/92d6a1c91716729d449034df5c50307dcce39493/src/lib-csharp/Sources/GitBase.cs), [GithubSource.cs](https://github.com/velopack/velopack/blob/92d6a1c91716729d449034df5c50307dcce39493/src/lib-csharp/Sources/GithubSource.cs)).
- **Pokus, soukromý repozitář bez tokenu:** `CheckForUpdatesAsync` vyhodí `System.Net.Http.HttpRequestException` (404). Výjimka je zachytitelná, aplikace nespadne a token nepotřebuje. Implementace v T-017 ji musí zachytit a brát jako „aktualizace nejsou k dispozici“.
- **Pokus, veřejný repozitář bez tokenu:** v AOT binárce proti cizímu veřejnému repozitáři s feedem Velopacku (`tgeorgiadis/quiver-launcher`) `GithubSource` načetl seznam vydání a feed a našel verzi 3.5.1. Anonymní přístup a deserializace v AOT fungují. Kontrola stáhla jen feed, nic se neinstalovalo. Feed s jiným kanálem (`releases.win-x64.json` tam není) vedl ke stavu „žádná aktualizace“ bez chyby.
- **Předběžná vydání:** verze `0.x` jsou na GitHubu označené `--prerelease` (T-055). S `prerelease: false` by klient během `0.x` nenašel žádnou aktualizaci. Proto je potřeba `prerelease: true`, viz otevřená otázka.
- **Limity:**
  - Klient čte jen **10 nejnovějších vydání** (`per_page=10`).
  - Bez tokenu platí limit GitHub API **60 požadavků za hodinu na IP adresu**. Jedna kontrola stojí jedno volání API, soubory se stahují přes `browser_download_url`.
  - **Koncepty vydání (draft) anonymní klient nevidí** („Only users with push access will receive listings for draft releases“, [GitHub REST API](https://docs.github.com/en/rest/releases/releases#list-releases)). Rozpracované vydání tedy aktualizace nespustí, dokud ho uživatel nezveřejní.
  - `GithubSource` nekontroluje `PackageId` ve feedu (pokus s cizím repozitářem). Bezpečnost stojí na tom, že vydání do repozitáře nahrává jen uživatel.
  - Integrita aktualizace se ověřuje jen SHA256/SHA1 z feedu. Velopack nemá vlastní podpis balíčků ([#975](https://github.com/velopack/velopack/issues/975)).

### 8. `win-arm64` (D + částečně P)
- Každý RID má mít vlastní kanál ([Channels](https://docs.velopack.io/packaging/channels): „every unique os/rid has its own channel“). Pokus `--channel win-arm64 -r win-arm64` (s x64 binárkami, jen kvůli názvům) dal soubory `PrusaTrayDiagnostic-0.1.2-win-arm64-full.nupkg`, `PrusaTrayDiagnostic-win-arm64-Setup.exe`, `PrusaTrayDiagnostic-win-arm64-Portable.zip`, `releases.win-arm64.json`, `assets.win-arm64.json` a `RELEASES-win-arm64`. **Názvy se s `win-x64` nekryjí**, oba RID můžou být ve stejném GitHub vydání. `RELEASES` bez přípony vzniká jen pro výchozí kanál `win`, který nepoužijeme.
- `Setup.exe` a `Update.exe` pro `win-arm64` jsou nativní ARM64 binárky (hlavička PE `0xAA64`).
- AOT publish `win-arm64` jsem neověřoval. Na tomto PC chybí MSVC ARM64 build tools a FFmpeg pro ARM64 řeší T-057. Spuštění na ARM64 nejde ověřit, uživatel nemá zařízení.
- Pojmenování: názvy souborů Velopacku jsou pevné. T-055 navrhuje `PrusaTrayDiagnostic-<verze>-<RID>.zip`. `Setup.exe` jde po zabalení přejmenovat (např. `PrusaTrayDiagnostic-<verze>-<RID>-Setup.exe`), protože ho feed nepotřebuje. `*.nupkg` a `releases.*.json` se přejmenovat **nesmí**.

### 9. Nepodepsané binárky (P částečně, D)
- `vpk pack` bez parametrů podpisu jen varuje („No signing parameters provided, 11 file(s) will not be signed“) a balíček vytvoří.
- **Aktualizace bez podpisu fungují** (ověřeno 2×). Kontroluje se jen hash z feedu. Velopack stahuje přes `HttpClient`, takže stažené balíčky by neměly mít značku Mark-of-the-Web a SmartScreen se u aktualizací neozve. To je odvozené z kódu, pokusem neověřené.
- **SmartScreen u `Setup.exe` staženého z internetu jsem pokusem neověřoval.** Lokálně sestavený soubor nemá Mark-of-the-Web a dialog SmartScreen by vyžadoval ruční kliknutí. Podle [dokumentace](https://docs.velopack.io/packaging/signing) jsou nepodepsané aplikace „likely to be flagged“. Očekává se dialog „Systém Windows ochránil váš počítač“ s volbou „Další informace → Přesto spustit“. Ověřit ručně při prvním skutečném vydání (T-017, doplnit do `docs/release.md`).

### 10. macOS pro T-019 (D, neověřeno)
- **Kde běží:** balíčky pro macOS jde vytvořit **jen na Macu** („It is not possible to create MacOS packages on Windows or Linux“, [Cross Compiling](https://docs.velopack.io/packaging/cross-compiling)). Windows balíčky jdou naopak vytvořit na jakémkoli OS.
- **Co vznikne** ([macOS](https://docs.velopack.io/packaging/operating-systems/macos), [Installer](https://docs.velopack.io/packaging/installer)): `.app` bundle (z publish složky + `.icns`, nebo hotová složka `.app`), `.pkg` (uživatel volí `/Applications` nebo `~/Applications`, po instalaci aplikaci spustí) a `Portable.zip` s `.app`. **`.dmg` Velopack nevytváří.** Dokumentace doporučuje rozbalit `Portable.zip` a `.dmg` sestavit sám (`hdiutil`).
- **Info.plist:** generuje ho `vpk` (`--bundleId`), nebo se předá vlastní (`--plist`, nejde kombinovat s `--bundleId`). `LSUIElement` dokumentace nezmiňuje, takže pro aplikaci bez ikony v Docku bude potřeba vlastní `--plist`.
- **Podpis a notarizace:** dokumentace je označuje za nutné („required by Apple … or your app won't run“). `vpk` podepisuje jen s `--signAppIdentity`/`--signInstallIdentity`/`--notaryProfile`. Chování s ad-hoc podpisem (`codesign -s -`) dokumentace nepopisuje. Issue [#975](https://github.com/velopack/velopack/issues/975) zmiňuje, že „plenty of velopack apps aren't signed“ a aktualizují se. **Neověřeno.**
- **`.dmg` + `.tar.gz` (rozhodnutí uživatele) a aktualizace přes Velopack:** možné, pokud oba archivy obsahují `.app` vytvořený přes `vpk` (obsahuje updater a manifest Velopacku). Vlastní `.app` sestavený mimo `vpk` by se aktualizovat neuměl. Aktualizace se stahují do `~/Library/Caches/velopack/<packId>/packages`. Když je aplikace v `/Applications`, updater si může vyžádat oprávnění správce (dialog přes AppleScript).
- **Rizika ad-hoc podpisu:** Gatekeeper u staženého `.dmg`/`.pkg` a u Keychainu (T-021) platí bez ohledu na Velopack. Jestli aktualizace stažená Velopackem nedostane atribut karantény, je potřeba ověřit na Macu.

### 11. Licence a údržba (D)
- Licence: **MIT** pro knihovnu, `vpk` i nativní `Setup.exe`/`Update.exe` (jeden repozitář [velopack/velopack](https://github.com/velopack/velopack), soubor LICENSE: Copyright © 2021 Caelan Sayler, © 2024 Velopack Ltd.). Do okna O aplikaci (licence třetích stran) bude potřeba doplnit text licence Velopacku (T-017).
- Aktivita (2026-10-21): 2,4 tis. hvězd, poslední push dnes, 36 otevřených issues. Stabilní vydání 1.0.1 (05/2026), 1.1.1, 1.2.0 (06/2026), 1.2.158, 1.2.161 (09/2026), předběžné 1.3.39 (2026-10-17). Vydání vycházejí zhruba měsíčně.
- .NET 11 preview: build i AOT publish s .NET 11 rc.2 prošly s `net10.0` assety (P). `vpk` vyžaduje runtime .NET 8–10 (viz tabulka nahoře).
- Velopack Flow (placená hostovaná služba) není potřeba. GitHub Releases stačí.

### 12. Napojení na skripty z T-055 (D, návrh)
Balicí krok „Velopack“ pro každý Windows RID v bodu rozšíření T-055 (vstup: hotová AOT publish složka, výstup: stejná výstupní složka):

```
dotnet tool restore                                   # vpk z .config/dotnet-tools.json (připnutá verze)
dotnet vpk pack -u PrusaTrayDiagnostic -v <Version z Directory.Build.props> -p <publish/RID> -e PrusaTrayDiagnostic.exe \
  --packTitle "Prusa Tray Diagnostic" --packAuthors "<autor>" --aumid PrusaTrayDiagnostic --shortcuts StartMenuRoot \
  --icon src/PrusaTrayDiagnostic.App/Assets/AppIcon/app-icon.ico --channel <RID> -r <RID> \
  --releaseNotes <úsek CHANGELOG.md pro verzi> --delta None --noPortable -o <dočasná složka>
→ do výstupní složky zkopírovat: *-full.nupkg, releases.<RID>.json, assets.<RID>.json, RELEASES-<RID>, Setup.exe (případně přejmenovaný)
```

- **Nahrávání:** `vpk upload github` není potřeba. Soubory nahraje `gh release create/upload` z T-055 jako ostatní. `GithubSource` potřebuje jen, aby `releases.<kanál>.json` a `.nupkg` byly ve stejném vydání.
- **Koncept (draft):** Windows běh může vytvořit koncept a Mac běh do něj přidat soubory. Klienti koncept nevidí, aktualizace se nabídne až po zveřejnění (bod 7).
- **Jen z `main`:** Velopack větev neřeší, rozhoduje skript T-055.
- **Poznámky k vydání:** `--releaseNotes` vloží Markdown do `.nupkg`. Aplikace je pak má v `UpdateInfo.TargetFullRelease.NotesMarkdown`, například pro dialog „co je nového“. Text vydání na GitHubu vzniká zvlášť ve skriptu T-055 a oba můžou brát stejný úsek `CHANGELOG.md`.
- **Delta:** pro delta balíček potřebuje `vpk` předchozí plný `.nupkg` ve výstupní složce (stáhnout ho z předchozího vydání, u soukromého repozitáře s tokenem z `gh auth token` jen při sestavení, ne v aplikaci). Rozhodnuto 2026-10-21: zatím `--delta None` (každá aktualizace stáhne asi 76 MB). Vrátit se k tomu, až bude repozitář veřejný.
- **`--noPortable`:** `Portable.zip` Velopacku (přenosná instalace se samoaktualizací) by duplikoval `.zip` z T-055. Viz otevřená otázka.
- Kontrolní součty (`SHA256SUMS-windows.txt`) zahrnou nové soubory automaticky přes bod rozšíření.
- Na macOS by balicí krok běžel jen na Macu (`vpk pack` s `--channel osx-arm64`) podle výsledku T-019.

## Pokusy a úklid

- **Kde:** kopie repozitáře (`git archive HEAD`) v `%TEMP%\ptdvelo` s lokálním manifestem nástrojů (`vpk` nebyl instalován globálně). Pracovní strom větve `feature/T-056-velopack-spike` zůstal beze změny kódu.
- **Pokusy:** instalace 0.1.0 (`--shortcuts StartMenuRoot`) → ruční spuštění hooků při běžící aplikaci → zápis `ToastActivatorCLSID` → aktualizace 0.1.1 (delta) a 0.1.2 (delta) přes `UpdateManager` → kontrola GitHub (soukromý i veřejný repozitář) → tichá odinstalace. Pak varianta `--shortcuts None`: instalace 0.1.2 → zástupce od aplikace → tichá odinstalace.
- **Záloha před pokusem:** zástupce `Prusa Tray Diagnostic.lnk` (vývojový, zálohovaný po bajtech), export klíče `Notifications\Settings\PrusaTrayDiagnostic` a počet toastů aplikace (0).
- **Ověření úklidu:**
  - neexistuje `%LocalAppData%\PrusaTrayDiagnostic`, `%LocalAppData%\velopack`, `%TEMP%\velopack`, `%TEMP%\velopack_PrusaTrayDiagnostic`, `%TEMP%\ptdvelo`;
  - neexistuje klíč `HKCU\…\Uninstall\PrusaTrayDiagnostic` ani testovací klíč;
  - `reg query HKCU /f` na `AppData\Local\PrusaTrayDiagnostic`, `ptdvelo` a `velopack` nenašel nic. Záznamy, které Windows vytvořilo při spouštění (`MuiCache`, `AppListBackup`, `NotifyIconSettings`), jsem smazal až po exportu;
  - klíč notifikací je shodný se zálohou;
  - historie toastů je prázdná (0);
  - `%APPDATA%\PrusaTrayDiagnostic` se nezměnil;
  - na ploše ani ve Startup nic nezůstalo;
  - balíčky `velopack`/`vpk` jsem smazal z cache NuGet i nástrojů;
  - neběží žádný proces aplikace.
- **Zástupce ve Start menu:** Velopack při instalaci vývojového zástupce přepsal (bod 4). Po pokusech jsem ho obnovil ze zálohy (shodný SHA256). Později ale zmizel. Pravděpodobně ho smazal souběžný běh `scripts/aot-smoke.ps1` jiného agenta (worktree `PTD-release`), který zástupce zálohuje a obnovuje stejně. To jsem neověřil. Pokus o opětovné vytvoření zamítlo oprávnění. Vývojový zástupce (cíl `D:\00-Source\PTD-logsec-b\…\bin\Debug\net11.0\PrusaTrayDiagnostic.exe`, AUMID `PrusaTrayDiagnostic`) se znovu vytvoří při příštím spuštění aplikace (`AppShortcutRegistration`).

## Zdroje

- NuGet: [Velopack 1.2.161](https://www.nuget.org/packages/Velopack/1.2.161), [vpk 1.2.161](https://www.nuget.org/packages/vpk/1.2.161)
- Zdrojový kód 1.2.161 (commit `92d6a1c9`): <https://github.com/velopack/velopack/tree/92d6a1c91716729d449034df5c50307dcce39493>, odkazy na jednotlivé soubory jsou u bodů výše, dále [VelopackApp.cs](https://github.com/velopack/velopack/blob/92d6a1c91716729d449034df5c50307dcce39493/src/lib-csharp/VelopackApp.cs) a [WindowsPackCommand.cs](https://github.com/velopack/velopack/blob/92d6a1c91716729d449034df5c50307dcce39493/src/vpk/Velopack.Vpk/Commands/Packaging/WindowsPackCommand.cs)
- Dokumentace: [Hooks](https://docs.velopack.io/integrating/hooks), [Shortcuts](https://docs.velopack.io/integrating/shortcuts), [Installer](https://docs.velopack.io/packaging/installer), [Signing](https://docs.velopack.io/packaging/signing), [Channels](https://docs.velopack.io/packaging/channels), [Cross Compiling](https://docs.velopack.io/packaging/cross-compiling), [macOS](https://docs.velopack.io/packaging/operating-systems/macos), [Distributing](https://docs.velopack.io/distributing/overview)
- Issues: [#122 AOT enum](https://github.com/velopack/velopack/issues/122), [#975 ověření podpisu na macOS](https://github.com/velopack/velopack/issues/975)
- GitHub: [List releases (koncepty)](https://docs.github.com/en/rest/releases/releases#list-releases)
