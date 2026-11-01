---
id: T-121
title: Kontextové menu okna kamery – samotné písmeno spustí položku
status: in-progress
owner: ui-dev
priority: P3
platform: all
depends_on: [T-069, T-117]
---

## Cíl
V kontextovém menu okna kamery (pravé tlačítko, klávesa Menu, Shift+F10) funguje přístupová klávesa jen s drženým Alt
(Alt+T). Po uvolnění Alt (podtržení jsou vidět) samotné písmeno nic neudělá a menu zůstane otevřené. Avalonia 12.1 obsluhuje
přístupové klávesy okna jen při drženém Alt; v menu oznamovací oblasti to řeší `TrayMenuAccessKeys` (T-069 Q2,
`IsMenuWindow` = okno s `MenuFlyoutPresenter`), kontextové menu kamery (`ContextMenu`) tím pokryté není. Zjištěno E2E
testem T-120 (2026-10-31); uživatel: „Dodělat jako v tray menu“.

## Rozsah
1. V otevřeném kontextovém menu okna kamery (normální okno i malý náhled) samotné písmeno přístupové klávesy spustí
   položku a menu zavře, jako v menu oznamovací oblasti a v nabídkách Windows; Alt+písmeno funguje dál.
2. Řešení sdílené s `TrayMenuAccessKeys`, ne kopie; ostatní kontextová menu aplikace dostanou stejné chování, pokud je
   pokryje stejný mechanismus.

## Akceptační kritéria
- [ ] Headless UI testy: písmeno samotné i Alt+písmeno v obou režimech okna, menu se zavře, neznámé písmeno nic nedělá
- [ ] E2E `CameraPinTests.ContextMenuOpensWithTheRightButtonTheMenuKeyAndShiftF10` testuje písmeno samotné
      (`TODO(T-121)` v T-120)
- [ ] macOS: chování kontextového menu beze změny nebo ověřeno v T-115
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
(žádné)
