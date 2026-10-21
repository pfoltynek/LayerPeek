---
id: T-046
title: Zápis neošetřených výjimek do logu
status: todo
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
- [ ] Neošetřená výjimka na pozadí i na UI vlákně skončí v logu se stack trace a log je vyprázdněný na disk ještě před ukončením procesu; proces pak skončí jako dřív
- [ ] Nepozorovaná výjimka úlohy se zaloguje a aplikace běží dál (žádná změna chování)
- [ ] Testy: logika handlerů (zápis, flush s časovým limitem, handler nehází ani při uvolněném provideru); `UnobservedTaskException` přes `GC.Collect` a `WaitForPendingFinalizers`; pokud to jde spolehlivě, integrační test v podřízeném procesu, který spadne a zanechá v testovací složce log se stack trace (např. testovací příkaz jen v Debug buildu); testy po sobě nic nenechají
- [ ] Ruční kontrola v AOT publish win-x64: log po vynuceném pádu obsahuje názvy metod
- [ ] `docs/architecture.md`, oddíl Logování: zmínka o handlerech
- [ ] Žádný nový NuGet balíček; `dotnet build` a `dotnet test` bez chyb a varování; AOT publish win-x64 bez varování

## Rozhodnuto
- **Chování při pádu (2026-10-21):** jen zápis do logu a flush. Aplikace spadne jako dřív, při dalším startu se nic nehlásí.

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
