---
id: T-054
title: Ověření, že pole hesla nevydá heslo přes UI Automation (I4)
status: todo
owner: qa-reviewer
priority: P3
platform: windows
depends_on: [T-044]
---

## Cíl
Ověřit nejistý informační nález I4 z bezpečnostní revize (2026-10-21). Nejisté je, zda `TextBox` Avalonie s `PasswordChar` (pole hesla PrusaLink a přihlašovacích údajů kamery v okně nastavení) nevydává skutečné heslo přes UI Automation. Kdyby ano, mohl by ho přečíst jakýkoli proces stejného uživatele přes UIA, nejen čtečka obrazovky. Revize to **nepotvrdila ani nevyvrátila**. Úkol je měření a hlášení, ne oprava.

## Rozsah
- E2E test nad infrastrukturou T-044 (FlaUI, testovací režim aplikace, spouštění na vyžádání): otevřít Nastavení, zadat známé heslo do pole hesla (a do pole hesla kamery) a přes UIA přečíst:
  - `ValuePattern.Value`, `Name`, `HelpText`, `LegacyIAccessiblePattern.Value` / `Name`, `TextPattern` (`DocumentRange.GetText`), pokud ho prvek nabízí;
  - vlastnost `IsPassword`.
- Pokud kterýkoli zdroj vrátí heslo v čitelné podobě, test selže a nález se zapíše do Poznámek k implementaci (verze Avalonie, které vlastnosti heslo vydávají). Návrh opravy, např. vlastní `AutomationPeer`, předat jako otevřenou otázku a novým úkolem; v tomto úkolu se neopravuje.
- Pokud heslo nevydá žádný zdroj, test zůstane v E2E sadě jako regresní kontrola, třeba při aktualizaci Avalonie.
- Bez nového NuGet balíčku (FlaUI už je v E2E projektu z T-044; jen testy, nedodává se).

## Akceptační kritéria
- [ ] E2E test čte všechny uvedené vlastnosti UIA polí hesla a ověřuje, že žádná neobsahuje zadané heslo
- [ ] Výsledek měření (vydává / nevydává, které vlastnosti, verze Avalonie) zapsaný v Poznámkách k implementaci; při úniku nový úkol s návrhem opravy
- [ ] Test po sobě nic nenechá (proces, testovací složka)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; E2E běh na Windows úspěšný

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
