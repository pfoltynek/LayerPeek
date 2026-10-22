---
id: T-046
title: Zápis neošetřených výjimek do logu
status: done
owner: core-dev
priority: P2
platform: all
depends_on: [T-024]
---

## Cíl
Když aplikace spadne na neošetřené výjimce, zůstane v logu (T-024) výjimka i se stack trace, aby šlo pád diagnostikovat. Dnes aplikace žádné globální handlery nemá a pád nezanechá žádnou stopu. **Chování při pádu se nemění:** aplikace spadne stejně jako dnes a při dalším startu se uživateli nic nehlásí.

## Rozsah
- Handlery zaregistrovat co nejdřív v `Program.Main`, hned po vytvoření logger factory z T-024 (ještě před `SingleInstanceGuard`). Platí tedy i pro druhé spuštění.
- **`AppDomain.CurrentDomain.UnhandledException`:** zapsat výjimku (úroveň `Critical`, celé `ToString()` se stack trace, příznak `IsTerminating`). Pak synchronně vyprázdnit frontu provideru (`Flush` s časovým limitem, návrh 2 s, aby zaseknutý disk nezdržel ukončení). Handler nesmí sám vyhodit výjimku.
- **`TaskScheduler.UnobservedTaskException`:** zapsat výjimku (úroveň `Error`). **Nevolat `SetObserved()`**, aby se chování nezměnilo. V .NET tato událost proces ve výchozím nastavení neukončí a tak to zůstane.
- **Dispatcher Avalonie** (`Dispatcher.UIThread.UnhandledException`; přesný název události v Avalonia 12 ověřit): zapsat výjimku (`Critical`) a vyprázdnit log. **Nenastavovat `Handled = true`**, takže výjimka dál ukončí aplikaci jako dnes. Registrace v `App` (ui-dev oblast) nebo v `Program.Main` po startu Avalonie, podle toho, kde je dispatcher dostupný.
- Výjimky se loguje přes `[LoggerMessage]` jako ve zbytku aplikace (kategorie např. `PrusaTrayDiagnostic.App.UnhandledExceptions`).
- Tajné údaje: zpráva výjimky se zapisuje celá. Revize T-024 už pohlídala texty výjimek z míst, která pracují s tajnými údaji. Tento úkol nic nemaskuje, ale pokud při testech najde výjimku nesoucí tajný údaj, opraví ji u zdroje.
- **Mimo rozsah:** nativní pády (např. access violation uvnitř FFmpeg), které .NET nezachytí. Hlášení pádu uživateli při dalším startu (rozhodnuto: ne).

## Akceptační kritéria
- [x] Neošetřená výjimka na pozadí i na UI vlákně skončí v logu se stack trace a log je vyprázdněný na disk ještě před ukončením procesu; proces pak skončí jako dřív
- [x] Nepozorovaná výjimka úlohy se zaloguje a aplikace běží dál (žádná změna chování)
- [x] Testy: logika handlerů (zápis, flush s časovým limitem, handler nehází ani při uvolněném provideru); `UnobservedTaskException` přes `GC.Collect` a `WaitForPendingFinalizers`; pokud to jde spolehlivě, integrační test v podřízeném procesu, který spadne a zanechá v testovací složce log se stack trace (např. testovací příkaz jen v Debug buildu); testy po sobě nic nenechají
- [x] Ruční kontrola v AOT publish win-x64: log po vynuceném pádu obsahuje názvy metod
- [x] `docs/architecture.md`, oddíl Logování: zmínka o handlerech
- [x] Žádný nový NuGet balíček; `dotnet build` a `dotnet test` bez chyb a varování; AOT publish win-x64 bez varování

## Rozhodnuto
- **Kontrola AOT (2026-10-22):** místo ruční kontroly E2E test pádu proti Native AOT exe konfigurace Debug (`tools/run-e2e.ps1 -PublishAot`), stejně jako u T-024 a T-064.
- **Protokol událostí (2026-10-22):** E2E testy pádu (UI vlákno, vlákno na pozadí) zapíší při každém běhu do protokolu událostí Windows (Aplikace) záznam „.NET Runtime 1026“ za každý pád. Jednotlivé záznamy smazat nejde; uživatel to přijal. Výpis paměti nebo hlášení WER, pokud je Windows vytvoří, test smaže.
- **Chování při pádu (2026-10-21):** jen zápis do logu a flush. Aplikace spadne jako dřív, při dalším startu se nic nehlásí.

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- **Kód:** `App/Logging/UnhandledExceptionLogger` (kategorie `PrusaTrayDiagnostic.App.UnhandledExceptions`, `[LoggerMessage]`):
  - Registrace v `Program.Main` hned po `AppLogging`, tedy před `SingleInstanceGuard`; platí i pro druhé spuštění.
  - `AppDomain.UnhandledException`: `Critical`, celá výjimka, příznak ukončení procesu.
  - `TaskScheduler.UnobservedTaskException`: `Error`, bez `SetObserved`.
  - Dispatcher se registruje přes `AppBuilder.AfterSetup` (`Dispatcher.UIThread.UnhandledException`), tedy ještě před `OnFrameworkInitializationCompleted` a `async void StartHost`. Úroveň `Critical`, bez `Handled`.
  - Po zápisu se fronta logu vyprázdní (`Flush`, nejvýš 2 s). Handlery nikdy nehází.
- **Avalonia 12.1.4 (ověřeno dekompilací při review):** neošetřenou výjimku úlohy dispatcheru vyhodí znovu (`throw;`), takže proces ji ohlásí jako tentýž objekt. Proto se zapíše jen jednou (porovnání referencí). Výjimky z `async void` na UI vlákně jdou stejnou cestou. Výjimky z WndProc (vstup, ikona v oznamovací oblasti) zachytí `AppDomain.UnhandledException`.
- **Text zprávy z UI vlákna je jen „Unhandled exception on the UI thread.“:** výjimku synchronního `Dispatcher.Send` může volající zachytit, takže „aplikace končí“ by nemuselo platit (aplikace `Send` nepoužívá).
- **Testovací příkaz (jen Debug build, jen testovací režim):** `crash:ui` (výjimka ve `Dispatcher.Post`), `crash:background` (vlákno na pozadí) a `crash:task` (nepozorovaná úloha, opakovaný `GC.Collect`). Výjimky vznikají v pojmenovaných metodách `TestCrash.Throw…`, jejichž názvy musí být ve stack trace. Kontrola Release binárek (`IconAndBuildTests`) hledá i text tohoto příkazu.
- **Testy:**
  - `UnhandledExceptionLoggerTests`: úroveň, zpráva, výjimka a flush (2 s); záznam z UI vlákna jen jednou, jiná výjimka po něm ano; objekt, který není výjimka; nepozorovaná úloha bez flush; handler nehází při chybě logu ani flush; zápis do souboru dřív, než handler vrátí; žádný zápis po uvolnění; skutečná událost `UnobservedTaskException` přes GC.
  - Testy protokolu pro `crash:*` (Release ho nezná).
  - E2E `CrashLoggingTests`: pád na UI vlákně a na pozadí ukončí aplikaci s nenulovým kódem a v logu je záznam se stack trace s názvy metod; nepozorovaná úloha se zaloguje a aplikace dál přijímá příkazy. To vše i proti Native AOT exe (`-PublishAot`). Windows při pádech nevytvořily hlášení WER ani dialog.
- **Ověřeno (2026-10-22):** `dotnet build` bez varování, `dotnet test` 4588 zelených, `tools/run-e2e.ps1 -PublishAot` zelené (32 E2E + 4 proti AOT; přeskočený byl jen test se skutečnou kamerou, která nebyla v síti), AOT publish win-x64 bez varování. Žádný nový NuGet balíček.
