---
id: T-062
title: Stabilizace E2E testu skutečného tray menu (TrayMenuTests)
status: todo
owner: qa-reviewer
priority: P3
platform: windows
depends_on: [T-044]
---

## Cíl
E2E test skutečné aplikace `tests/PrusaTrayDiagnostic.App.E2E.Tests/TrayMenuTests.cs` (test `RealTrayMenuShowsThePrinterSubmenuAndTogglesTheCamera`) je nestabilní. Běh qa-reviewera 2026-10-21 (`pwsh tools/run-e2e.ps1 -Class '*TrayMenuTests'`, `PTD_E2E=1`) prošel jen ve 3 z 5 opakování. Obě selhání byla `TimeoutException: Waited 10 s for the tray menu` v `OpenMenuAsync` (TrayMenuTests.cs, asi ř. 81) – jednou už při úplně prvním otevření menu (`OpenPrinterSubmenuAsync(app, "tray-menu-1")`, asi ř. 34), jednou při otevření menu pro „Diagnostika…“ (asi ř. 54). T-044 (hotovo) zaznamenal „OK (3× po sobě)“, nestabilita se tedy tehdy neprojevila nebo nebyla zachycena.

Druhý projev: běhy ui-dev 2026-10-21 (po změně „Diagnostika…“ na úpravu položky na místě) selhaly ve 2 z 8 opakování na `System.Runtime.InteropServices.COMException` z UI Automation (FlaUI UIA3) při otevírání menu v `OpenMenuAsync` – na řádku 34 (první otevření, `OpenPrinterSubmenuAsync`) a na řádku 48 (otevření podmenu pro zavření kamery), vždy před krokem „Diagnostika…“. Pravděpodobně jde o čtení prvku (ikony, flyoutu přetečení nebo okna menu), který mezitím zanikl nebo se mění; ověřit spolu s timeoutem výše.

Selhání nezpůsobila změna „Diagnostika… v hlavním menu“ (větev `feature/diagnostics-main-menu`) – timeout i `COMException` nastávají už při prvním otevření menu, ještě před položkou Diagnostika.

Cílem je najít skutečnou příčinu, opravit ji (v testu / infrastruktuře testů, případně v aplikaci, pokud jde o chybu aplikace) a doložit stabilitu opakovanými běhy.

## Rozsah
- Reprodukce: opakované běhy `pwsh tools/run-e2e.ps1 -Class '*TrayMenuTests'`; při timeoutu uložit diagnostiku (snímek obrazovky celé plochy / oblasti oznámení, seznam nalezených ikon s `[E2E]` včetně `BoundingRectangle`, `IsOffscreen`, zda byl otevřen flyout přetečení, okna procesu aplikace před/po pravém kliku).
- Analýza podezřelých příčin (ověřit, ne předpokládat):
  - Pravý klik na zastaralou („ghost“) ikonu ukončené testovací instance nebo na ikonu ve flyoutu přetečení (`TopLevelWindowForOverflowXamlIsland`), který se ještě animuje – souřadnice prvku z UI Automation pak neodpovídají místu na obrazovce. Viz `Infrastructure/NotificationArea.cs` (`FindTestIconsAsync`, `FindIcons`, `CloseOverflow`).
  - Když je nalezena právě jedna ikona a je to ghost, `OpenMenuAsync` nezkouší nic dalšího a po 10 s skončí timeoutem.
  - Flyout přetečení zůstal otevřený / zavřený z předchozího otevření menu (Escape v `CloseOverflow`, zavření menu po `Invoke` položky), takže další hledání ikony pracuje s neaktuálním stavem.
  - Ikona testovací instance ještě není zaregistrovaná nebo je přesunutá Windows mezi panelem a přetečením.
  - Menu se otevře, ale `app.Windows()` / filtr „nové okno s `MenuItem`“ ho nenajde (např. okno popupu znovu použité z předchozího otevření, tedy už v seznamu `before`).
  - `COMException` z UI Automation (viz Cíl): prvek z `FindAllDescendants` / `FindAllChildren` nebo vlastnost čtená na prvku, který mezitím zanikl (zavírající se flyout přetečení, zavírající se menu z předchozího kroku, okna procesu v `app.Windows()`). Zjistit HRESULT a přesné volání.
- Oprava podle zjištěné příčiny v `TrayMenuTests.cs` a/nebo `Infrastructure/NotificationArea.cs` (případně `Wait.cs`, `AppInstance.cs`). Pokud je příčina v aplikaci (`TrayHost`, tray menu), oprava v aplikaci + jednotkový/UI test.
- Zachovat smysl testu: skutečná ikona v oblasti oznámení, skutečný pravý klik myší (jediný pohyb myši v E2E testech, viz T-044), menu čtené přes UI Automation.
- Zapsat výsledek do `## Poznámky k implementaci` (příčina, oprava, počet běhů) a případně aktualizovat `docs/` popis E2E testů, pokud se mění postup nebo předpoklady prostředí.

## Akceptační kritéria
- [ ] Příčina timeoutu „Waited 10 s for the tray menu“ je zjištěna a doložena (snímek / log z reprodukce) a popsána v poznámkách k implementaci
- [ ] Příčina `COMException` z UI Automation v `OpenMenuAsync` (ř. 34 a 48) je zjištěna (HRESULT, volání) a odstraněna, ne jen potlačena obecným `catch`
- [ ] Oprava odstraňuje tuto příčinu (ne jen prodloužení timeoutu nebo slepé opakování celého testu)
- [ ] Při selhání otevření menu test uloží diagnostiku (snímek obrazovky + seznam nalezených ikon `[E2E]`), aby šlo budoucí selhání rozebrat bez reprodukce
- [ ] `pwsh tools/run-e2e.ps1 -Class '*TrayMenuTests'` projde 10× po sobě bez selhání (počet a datum zapsat do poznámek)
- [ ] Celá sada App E2E testů (`pwsh tools/run-e2e.ps1`) projde alespoň jednou po opravě
- [ ] Po bězích nezůstávají žádné procesy aplikace ani simulátoru, otevřený flyout přetečení ani testovací data (viz pravidlo „testy po sobě nic nenechávají“, T-032)
- [ ] Test dál používá skutečnou ikonu v oblasti oznámení a skutečný pravý klik myší
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Pokud se ukáže, že spolehlivé otevření menu z flyoutu přetečení nejde zajistit: je přijatelné, aby test před spuštěním připnul ikonu testovací instance na hlavní panel (zápis do nastavení Windows `HKCU\Control Panel\NotifyIconSettings`, s úklidem po testu)? Mění to nastavení uživatelova systému.
- Je přijatelné jako záložní řešení otevřít menu jinak než pravým klikem myší (např. přes UI Automation `Invoke`/klávesnici na ikoně v oblasti oznámení), pokud skutečný pravý klik zůstane jen v jedné části testu? Mění to rozsah ověření stanovený v T-044.
- Stačí 10 po sobě jdoucích úspěšných běhů jako důkaz stability, nebo uživatel požaduje jiný počet?

## Poznámky k implementaci
(vyplní agent po dokončení)
