---
id: T-168
title: Tray menu na Windows zůstane otevřené po „Hide camera“ (E2E TrayMenuTests)
status: done
owner: ui-dev
priority: P2
platform: windows
depends_on: []
---

## Cíl
E2E `TrayMenuTests.RealTrayMenuShowsThePrinterSubmenuAndTogglesTheCamera` na Windows opakovaně selhává (nalezeno při
ověření T-125, 2026-11-05). Zjistit, jestli jde o chybu aplikace, nebo jen testu, a opravit ji.

## Rozsah
- Pozorování: po „Show camera“ (UIA Invoke) se okno kamery otevře a menu se zavře. Po „Hide camera“ (UIA Invoke) se okno
  kamery zavře, ale **hlavní tray menu zůstane otevřené** (okno 203×178 v rohu u ikony). Další pravý klik na ikonu trefí
  roh tohoto menu a test nového menu se nedočká (`Waited 10 s for the tray menu`, krok `tray-menu-4`).
- Na commitu před T-146 (`5818bc1c`) test prochází, s T-146 (`87570d02`) a novějšími selhává (2× v řadě na develop
  `dc7d46e0`). T-146 přidal do podmenu tiskárny oddělovač a položky nahrávání. Při zavření kamery se položka tiskárny
  nahrazuje (jiná struktura podmenu) v obou verzích, přesný mechanismus zatím neznámý.
- Ověřit skutečnou cestu uživatele: klik myší a Enter z klávesnice na „Hide camera“. Pokud se menu zavře, jde jen o UIA
  Invoke a stačí upravit test (zavřít menu nebo použít myš). Pokud zůstane otevřené, opravit aplikaci.

## Akceptační kritéria
- [x] Příčina zjištěná a zapsaná (aplikace / test)
- [x] Po „Hide camera“ klikem myší menu nezůstane otevřené (ruční ověření nebo E2E)
- [x] `pwsh tools/run-e2e.ps1 -Class '*TrayMenuTests'` = PASS
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- **Příčina (aplikace):** Avalonia na Windows po kliknutí na položku menu vyvolá `Click` a pak zavírá menu tak, že od
  kliknuté položky prochází nahoru přes rodiče až ke kořeni. „Hide camera“ ještě během `Click` synchronně zavře okno kamery
  a `TrayMenuViewModel.SetCameraWindowOpen` hned změní podmenu tiskárny (zmizí „Bring camera to front“, jiná struktura →
  `TrayMenuTarget` položku tiskárny nahradí). Kliknutá položka tím ztratí rodiče, zavře se jen podmenu a hlavní menu
  zůstane otevřené v popředí. Diagnostický E2E (dočasný, odstraněný) to ukázal stejně pro UIA Invoke i pro skutečný klik
  myší, takže nešlo jen o test.
- **Oprava:** `AvaloniaNativeTrayMenu.Item.OnClick` spouští akci položky přes `Dispatcher.UIThread.Post`, tedy až po
  dokončení kliknutí; platí pro všechny položky tray menu (na macOS jen nepatrné zpoždění, `NSMenu` je v tu chvíli zavřené).
  Headless testy, které volají `RaiseClicked()`, prošly beze změny.
- Proč to před T-146 procházelo, se nezjišťovalo do detailu: změna struktury podmenu i synchronní `Apply()` byly i dřív.
  Oprava je nezávislá na tom, co přesně kliknutí změní.
- **Nový E2E** `TrayMenuHideCameraTests.HideCameraClickedWithTheMouseClosesTheWholeMenu`: skutečný klik myší na „Hide camera“,
  pak se musí zavřít okno kamery, podmenu i hlavní menu.
- E2E `-Class '*TrayMenu*'` s opravou: 40 PASS, 1 SKIP, 0 FAIL (včetně `TrayMenuTests`, předtím 2× FAIL). `dotnet test`:
  10 434 testů, 0 chyb.
- Navíc: `CameraTimelapseAllocationTests.SteadyStateTimelapseDoesNotAllocate` na Windows selhával zhruba v 1 z 15 běhů
  (náhodné alokace jiných vláken procesu: 56, 784 a 4176 B ve třech průchodech). Test teď zkouší až 10 průchodů, skončí
  u prvního s 0 B a průchod se změnou velikosti thread poolu nepočítá. Alokace na paket by se objevila v každém průchodu,
  takže regresi pořád zachytí. Po úpravě 30/30 PASS.
- Závěrečný E2E 2026-11-05: `-Class '*TrayMenu*'` 40/40 PASS (i nový `TrayMenuHideCameraTests`), `-Class '*Camera*'` 57 PASS, 2 FAIL v `CameraMiniPreviewTests` (`TogglesGrow…`, `ResizeGrip…`). Samostatně hned potom 6/6 PASS, nestabilní a s tray menu nesouvisí.
