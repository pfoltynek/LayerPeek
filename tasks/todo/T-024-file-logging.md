---
id: T-024
title: Logování do souboru
status: todo
owner: core-dev
priority: P2
platform: all
depends_on: [T-007]
---

## Cíl
Logy aplikace se ukládají do souboru, aby šly diagnostikovat problémy u uživatele (dnes `AddLogging()` nemá žádný provider a logy se zahazují).

## Rozsah
- Vlastní jednoduchý `ILoggerProvider` zapisující do souboru v AppData (bez nového NuGet balíčku, Native AOT kompatibilní)
- Rotace podle velikosti (omezený počet souborů)
- Zápis mimo volající vlákno (neblokovat polling ani UI), bezpečné ukončení při Quit
- Nikdy nelogovat tajné údaje (hesla, API klíče, tokeny)
- Životnost logger factory musí přesahovat uvolnění DI kontejneru při ukončení, aby se zaznamenaly i chyby a timeouty z „Ukončit“. Factory dnes vlastní `TrayHost` (z T-008), kontejner ji jen používá a nedisposuje. Uvolní se až v `App.OnDesktopExit` přes `TrayHost.ReleaseLogging()`. Provider tam musí zapsat zbytek logu. Pozor: při ukončení ze strany OS monitoring jen dostane pokyn k zastavení a může ještě chvíli logovat. Provider proto nesmí po uvolnění házet výjimky.
- Logger ochrany jediné instance (T-043): `Program.Main` dnes vytváří vlastní `LoggerFactory` bez providerů (`startupLogging`) pro `SingleInstanceGuard` a jeho server roury/socketu, ještě před Avalonií a `TrayHost`. Provider se musí napojit i sem (druhé spuštění loguje předání nebo jeho selhání a hned končí, takže zápis musí proběhnout před návratem z `Main`); server běží po celou dobu aplikace, factory proto musí žít až do konce `Main`.

## Akceptační kritéria
- [ ] Logy se zapisují do souboru v AppData, rotace funguje
- [ ] Testy provideru (formát, rotace, souběžný zápis, flush při ukončení); testy po sobě nic nenechají
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Výchozí úroveň logování a limit velikosti/počtu souborů?
- Kam logovat ze `Program.Main` (ochrana jediné instance, T-043), ještě než existuje `TrayHost`: stejný soubor jako aplikace (sdílený provider), nebo samostatný? Druhá instance běží souběžně s první – nesmí jí přepisovat ani zamykat soubor.

## Poznámky k implementaci
