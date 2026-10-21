---
id: T-024
title: Logování do souboru
status: todo
owner: core-dev
priority: P2
platform: all
depends_on: [T-007, T-043, T-044]
---

## Cíl
Logy aplikace se ukládají do souboru, aby šly diagnostikovat problémy u uživatele. Dnes se v Release buildu všechno zahazuje: aplikace všude loguje přes `Microsoft.Extensions.Logging` a `[LoggerMessage]`, ale nemá registrovaný žádný provider (`AppServices.Create`, komentář „No logging provider yet“; `AppEnvironment.CreateLoggerFactory` přidává provider jen v testovacím režimu Debug buildu – `TestModeLogProvider` z T-044).

Na tento úkol navazují T-046 (neošetřené výjimky), T-047 (přepínač Debug v nastavení), T-048 a T-049 (podpůrné akce pro uživatele).

> **Návaznost na T-044:** `AppEnvironment`, `TestModeLogProvider` a úpravy `Program.Main` vznikly ve větvi T-044, která zatím není ve `develop`. Proto je T-044 v `depends_on` (rozhodnuto 2026-10-21), přestože T-044 je úkol jen pro Windows.

## Rozsah
- **Vlastní `ILoggerProvider`** zapisující do souboru. Bez nového NuGet balíčku, kompatibilní s Native AOT: bez reflexe a bez `JsonSerializer` bez typových informací. Formátování přes `formatter(state, exception)`.
- **Umístění:** standardní místa pro logy podle OS, **ne** vedle `settings.json`:
  - Windows: `%LOCALAPPDATA%\PrusaTrayDiagnostic\logs` (`Environment.SpecialFolder.LocalApplicationData`, ne Roaming);
  - macOS: `~/Library/Logs/PrusaTrayDiagnostic`.

  `AppDataPaths`, nebo nová sousední třída v `Core/Settings`, dostane metodu pro složku s logy, např. `GetDefaultLogDirectory()`, se stejnou kontrolou plně kvalifikované cesty jako `GetDefaultDirectory()`. `AppEnvironment` ji vystaví jedním místem (např. `AppEnvironment.LogDirectory()`), protože ji potřebují T-048 a T-049. V testovacím režimu T-044 jdou logy do vlastní testovací datové složky (např. její podsložky `logs`), nikdy do složky uživatele. Tyto cesty musí uvádět `docs/troubleshooting.md` (T-048).
- **Úroveň:** výchozí minimální úroveň `Information`. Provider má přepínač minimální úrovně měnitelný za běhu (např. `LogLevelSwitch` s `volatile` hodnotou, kterou čte `IsEnabled`), aby ho T-047 mohl přepínat bez nové factory. V testovacím režimu Debug buildu zůstává `Debug` jako dnes.
- **Formát řádku** (návrh architekta): `2026-10-21 14:03:12.345 +02:00 INF [pid 12345] Kategorie: zpráva`. Každý řádek nese PID, aby šly rozlišit dvě instance. Výjimka se zapisuje celá (`exception.ToString()`: typ, zpráva, vnitřní výjimky, **stack trace**) na dalších řádcích s odsazením. Kódování UTF-8 bez BOM, konce řádků `\n`.
  - Native AOT: ověřit, že stack trace obsahuje názvy metod, tedy že `StackTraceSupport` není vypnutý a `UseSystemResourceKeys` nezkracuje zprávy výjimek. Čísla řádků v AOT nebudou, to je v pořádku.
- **Rotace podle velikosti: 1 MB × 5 souborů** (aktuální + 4 starší), např. `PrusaTrayDiagnostic.log` a `PrusaTrayDiagnostic.1.log` … `PrusaTrayDiagnostic.4.log`. Nejstarší se maže.
- **Dvě instance, jeden soubor (T-043):** druhé spuštění (ochrana jediné instance v `Program.Main`) zapisuje do **stejného** souboru, otevřeného pro připisování se sdíleným přístupem (`FileShare.ReadWrite | FileShare.Delete`). Nesmí soubor zamknout ani přepsat řádky první instance. **Rotovat smí jen primární instance**, protože role se zjistí až po `SingleInstanceGuard.Start`, takže provider začíná v režimu bez rotace a rotaci zapne až primární instance. Selhání rotace (sdílení, antivir) nesmí shodit aplikaci; zkusí se znovu při dalším překročení.
  - **Pozor:** `FileStream` s `FileMode.Append` v .NET si drží vlastní pozici a zapisuje na ni. Neodpovídá to `O_APPEND` / `FILE_APPEND_DATA`, takže dva procesy by si navzájem přepisovaly řádky. Zajistit opravdové připisování na konec, nebo krátký meziprocesový zámek kolem zápisu. Každý řádek, případně celý záznam s výjimkou, se zapisuje jedním voláním. Ověřit testem se dvěma nezávislými zapisovateli.
- **Zápis mimo volající vlákno:** fronta (`Channel<T>`) a jedno vlákno na pozadí. Neblokovat polling ani UI. Fronta je omezená. Při přetečení se zprávy zahodí a do logu se zapíše počet zahozených zpráv. Synchronní `Flush(TimeSpan timeout)` pro ukončení a pro T-046.
- **Životnost (návrh):** jedna logger factory na proces vznikne v `Program.Main` ještě před `SingleInstanceGuard`. Nahradí dnešní `startupLogging` a také factory, kterou si dnes vytváří `TrayHost` (`environment.CreateLoggerFactory()`). Primární instance ji předá přes `App` do `TrayHost` a `Program.Main` ji uvolní až po návratu z `StartWithClassicDesktopLifetime`. `TrayHost.ReleaseLogging()` (z `App.OnDesktopExit`) pak jen synchronně vyprázdní frontu. Důvod: při ukončení ze strany OS (konec relace) se `Main` nemusí dokončit. Druhé spuštění zapíše své řádky a před `return` z `Main` je vyprázdní (`using`).
  - Platí dál: kontejner DI factory nevlastní ani neuvolňuje. Monitoring může po pokynu k zastavení ještě chvíli logovat, proto provider po uvolnění nesmí házet výjimky. Pozdní zprávy tiše zahodí.
- **Tajné údaje:** nikdy nelogovat hesla, API klíče ani tokeny. Provider nic nemaskuje; odpovědnost mají volající (jako dnes, např. `IUrlLauncher.OpenSecretAsync`, `UriRedaction`). V rámci úkolu projít existující `[LoggerMessage]` a ověřit, že žádný nezapisuje tajný údaj ani adresu proxy s tokenem. Pozor i na texty výjimek, které se teď nově dostanou do souboru celé.
- **`TestModeLogProvider` (T-044):** buď ho nahradí nový provider (soubor v `logs` testovací složky), nebo zůstane vedle něj. Rozhodne implementátor, podmínka: E2E testy T-044 dál fungují a dál po sobě nic nenechají.
- **Dokumentace:** oddíl „Logování“ v `docs/architecture.md` (umístění, formát, rotace, dvě instance, životnost).

## Akceptační kritéria
- [ ] Release build zapisuje logy do `%LOCALAPPDATA%\PrusaTrayDiagnostic\logs` (Windows) / `~/Library/Logs/PrusaTrayDiagnostic` (macOS; skutečné ověření v T-019), testovací režim do testovací složky; výchozí úroveň `Information`; rotace 1 MB × 5 souborů funguje
- [ ] Každý řádek obsahuje čas s posunem, úroveň, PID a kategorii; výjimky celé se stack trace (ověřeno i v AOT publish win-x64 ruční kontrolou souboru)
- [ ] Druhé spuštění zapíše své řádky do stejného souboru před ukončením `Main`; první instance o žádné řádky nepřijde a soubor nezamkne; rotuje jen primární instance
- [ ] Minimální úroveň jde přepnout za běhu (API pro T-047), testy
- [ ] Testy provideru: formát, rotace, dva nezávislí zapisovatelé do jednoho souboru (žádné ztracené ani prolnuté řádky), souběžný zápis z více vláken, přetečení fronty, flush při ukončení, zápis po uvolnění nehází; testy po sobě nic nenechají (per-worktree složky z T-032), ani po pádu
- [ ] Revize existujících log zpráv: žádné tajné údaje (výsledek v Poznámkách k implementaci)
- [ ] `docs/architecture.md` doplněna (oddíl Logování)
- [ ] Žádný nový NuGet balíček; `dotnet build` a `dotnet test` bez chyb a varování; AOT publish win-x64 bez varování

## Rozhodnuto
- **Výchozí úroveň (2026-10-21):** `Information`. Podrobné logování (`Debug`) jde zapnout v nastavení (T-047).
- **Rotace (2026-10-21):** podle velikosti, 1 MB × 5 souborů.
- **Druhá instance (2026-10-21):** zapisuje do stejného souboru připisováním se sdíleným přístupem, každý řádek nese PID, rotuje jen primární instance.
- **Umístění logů (2026-10-21):** standardní místa podle OS. Windows `%LOCALAPPDATA%\PrusaTrayDiagnostic\logs`, macOS `~/Library/Logs/PrusaTrayDiagnostic`, ne vedle `settings.json`. Testovací režim (T-044) používá vlastní testovací datovou složku.

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
