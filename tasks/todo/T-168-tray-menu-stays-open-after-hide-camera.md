---
id: T-168
title: Tray menu na Windows zůstane otevřené po „Hide camera“ (E2E TrayMenuTests)
status: todo
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
- [ ] Příčina zjištěná a zapsaná (aplikace / test)
- [ ] Po „Hide camera“ klikem myší menu nezůstane otevřené (ruční ověření nebo E2E)
- [ ] `pwsh tools/run-e2e.ps1 -Class '*TrayMenuTests'` = PASS
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
