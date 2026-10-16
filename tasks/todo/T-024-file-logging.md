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

## Akceptační kritéria
- [ ] Logy se zapisují do souboru v AppData, rotace funguje
- [ ] Testy provideru (formát, rotace, souběžný zápis, flush při ukončení); testy po sobě nic nenechají
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Výchozí úroveň logování a limit velikosti/počtu souborů?

## Poznámky k implementaci
