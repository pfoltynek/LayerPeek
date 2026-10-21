---
id: T-062
title: Stabilizace E2E testu skutečného tray menu (TrayMenuTests)
status: done
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
- [x] Příčina timeoutu „Waited 10 s for the tray menu“ je zjištěna a doložena (snímek / log z reprodukce) a popsána v poznámkách k implementaci
- [x] Příčina `COMException` z UI Automation v `OpenMenuAsync` (ř. 34 a 48) je zjištěna (HRESULT, volání) a odstraněna, ne jen potlačena obecným `catch`
- [x] Oprava odstraňuje tuto příčinu (ne jen prodloužení timeoutu nebo slepé opakování celého testu)
- [x] Při selhání otevření menu test uloží diagnostiku (snímek obrazovky + seznam nalezených ikon `[E2E]`), aby šlo budoucí selhání rozebrat bez reprodukce
- [x] `pwsh tools/run-e2e.ps1 -Class '*TrayMenuTests'` projde 10× po sobě bez selhání (počet a datum zapsat do poznámek)
- [x] Celá sada App E2E testů (`pwsh tools/run-e2e.ps1`) projde alespoň jednou po opravě
- [x] Po bězích nezůstávají žádné procesy aplikace ani simulátoru, otevřený flyout přetečení ani testovací data (viz pravidlo „testy po sobě nic nenechávají“, T-032)
- [x] Test dál používá skutečnou ikonu v oblasti oznámení a skutečný pravý klik myší
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Pokud se ukáže, že spolehlivé otevření menu z flyoutu přetečení nejde zajistit: je přijatelné, aby test před spuštěním připnul ikonu testovací instance na hlavní panel (zápis do nastavení Windows `HKCU\Control Panel\NotifyIconSettings`, s úklidem po testu)? Mění to nastavení uživatelova systému.
- Je přijatelné jako záložní řešení otevřít menu jinak než pravým klikem myší (např. přes UI Automation `Invoke`/klávesnici na ikoně v oblasti oznámení), pokud skutečný pravý klik zůstane jen v jedné části testu? Mění to rozsah ověření stanovený v T-044.
- Stačí 10 po sobě jdoucích úspěšných běhů jako důkaz stability, nebo uživatel požaduje jiný počet?

## Poznámky k implementaci
Reprodukce 2026-10-21 opakovanými běhy `run-e2e.ps1 -Class '*TrayMenuTests'` s novou diagnostikou (záznam pokusu + snímek celé plochy). Bez zátěže 8/8 OK; selhání se projevila jen za třech podmínek:

1. **`COMException` 0x80131505 (`UIA_E_TIMEOUT`)** – volání `desktop.FindFirstChild(ByClassName("TopLevelWindowForOverflowXamlIsland"))` v `NotificationArea.FindTestIconsAsync` (tedy už při prvním otevření menu, ř. 34/48). Výčet potomků plochy UI Automation se ptá providera *každého* top-level okna všech procesů; pod zátěží CPU (paralelní buildy jiných sessions) některé neodpoví do 2 s. I v úspěšném běhu trvalo nalezení ikony 16 s. Oprava: panel (`Shell_TrayWnd`) a flyout přetečení se hledají přes Win32 `FindWindow` a UIA se ptá jen Exploreru (`NotificationArea.Taskbar/Overflow`); nalezení ikony pod zátěží teď 34–263 ms, pod stejnou zátěží 5/5 OK.
2. **„Waited 10 s for the tray menu“ a `COMException` 0x80004005 (`E_FAIL`)** – reprodukováno souběžným během App E2E z druhého worktree: menu se otevřelo, ale okno druhého běhu („Configuración“) převzalo popředí a popup Avalonie se při deaktivaci zavřel; čtení zanikajícího okna pak dalo `E_FAIL` (při otevírání menu i při čtení podmenu), jinak timeout. Zámek fixture byl jen pro worktree. Oprava: zámek pro celý stroj `%TEMP%\PrusaTrayDiagnostic.App.E2E.Desktop.lock` (`E2EFixture`, čekání až 30 min), běhy různých worktree se střídají. Souběh bez zámku 1/3, se zámkem 3/3 (druhý běh 18/18).
3. **Timeout při použití myši během běhu** – kurzor skončil nad jiným oknem na druhém monitoru (zachyceno 7× v sérii 10 běhů, kdy se na počítači pracovalo). Oprava: `NotificationArea.RightClick` přesune kurzor na ikonu okamžitě (místo animovaného pohybu FlaUI přes ostatní ikony) a pokud kurzor po kliknutí není na ikoně, test selže s jasným důvodem „someone used the mouse“. Pravý klik zůstává skutečným klikem myši na skutečnou ikonu.

Dále: `AppInstance.DeleteRunDirectoryAsync` vyhazoval výjimku z `DisposeAsync` (soubor ještě držený po ukončení aplikace pod zátěží), která v `await using` **přepsala původní selhání testu**; teď čeká až 10 s a jinak složku nechá závěrečnému úklidu fixture (smazání `E2EFixture.Root`, ten při neúspěchu selže). Při selhání otevření menu se ukládá `NNN-tray-menu-failure.txt` (průběh: ikony s `BoundingRectangle`/`IsOffscreen`, kliknutí, kurzor, okno v popředí, nová a zaniklá okna aplikace, stav ikon po selhání, log aplikace) a `NNN-tray-menu-failure-screen.png` (celá plocha); cesta je ve zprávě výjimky.

Hypotézy z rozsahu, které se nepotvrdily: ghost ikony a animace flyoutu přetečení (ikona testovací instance byla v obou reprodukcích na panelu, menu se otevřelo do 110 ms po kliknutí). Ghost ikony dál pokrývá zkoušení všech nalezených `[E2E]` ikon (souřadnice se čtou při každém kliknutí); kurzor už nepřejíždí přes ostatní ikony, takže je Explorer cestou neodstraňuje. Po review (qa-reviewer) jsou chyby při sběru diagnostiky ošetřené tak, aby nepřepsaly původní selhání; ty úpravy se týkají jen chybové cesty a po nich proběhl jen build a `dotnet test`, ne další E2E běh. Připnutí ikony ani jiné otevření menu nebylo potřeba (otevřené otázky 1 a 2 tím odpadají); stabilita doložena požadovanými 10 běhy.

Ověření 2026-10-21 (22:50–22:54, bez zásahu do myši): `run-e2e.ps1 -Class '*TrayMenuTests'` **10× po sobě OK**, celá sada `run-e2e.ps1` OK (26 testů, 1 přeskočený – skutečná kamera v síti nedostupná). Po bězích nezůstaly procesy aplikace ani simulátoru, rezervovaná složka smazána, flyout přetečení se neotevíral. `dotnet build` 0 varování, `dotnet test` 4445 testů, 0 selhání. Postup popsán v nápovědě `tools/run-e2e.ps1` (střídání worktree).
