---
id: T-121
title: Kontextové menu okna kamery – samotné písmeno spustí položku
status: done
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
- [x] Headless UI testy: písmeno samotné i Alt+písmeno v obou režimech okna, menu se zavře, neznámé písmeno nic nedělá
- [x] E2E `CameraPinTests.ContextMenuOpensWithTheRightButtonTheMenuKeyAndShiftF10` testuje písmeno samotné
      (`TODO(T-121)` v T-120) – test upravený, čeká na běh na ploše uživatele (`tools/run-e2e.ps1`)
- [x] macOS: chování kontextového menu beze změny nebo ověřeno v T-115
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
(žádné)

## Poznámky k implementaci
- **Sdílený mechanismus:** `TrayMenuAccessKeys` přejmenovaný a zobecněný na `src/LayerPeek.App/Windows/MenuAccessKeys.cs`
  (`git mv`). Obsluha třídy pro `KeyDown` je nově na `TopLevel` (dřív `Window`), `IsMenuRoot` pozná dva případy:
  okno, jehož obsahem je `MenuFlyoutPresenter` (menu oznamovací oblasti z Avalonia.Win32, beze změny), a `PopupRoot`,
  jehož obsahem je `ContextMenu` nebo `MenuFlyoutPresenter` (kontextové menu, `MenuFlyout`). Písmeno/číslice bez Alt,
  Ctrl a Win/Cmd, které menu neobsloužilo, se znovu vyvolá na tomto top levelu jako Alt+písmeno; vše ostatní dělá
  obsluha přístupových kláves Avalonie (jen viditelné a povolené položky, položka se spustí a menu zavře, víc položek se
  stejným písmenem se střídá ve fokusu bez spuštění – jako nabídky Windows; neznámé písmeno nic neudělá).
- **Kořenová příčina u kontextového menu:** `ContextMenu` se otevírá ve vlastním `PopupRoot` (na Windows i macOS
  vlastní nativní okno), jehož výchozí obsluha přístupových kláves bere písmeno jen s drženým Alt – stejně jako okno
  menu oznamovací oblasti.
- **Pokrytá menu aplikace:** kontextové menu okna kamery (normální okno i malý náhled) a `MenuFlyout` obrázku tiskárny
  v nastavení (jeho položky zatím přístupové klávesy nemají, takže se v praxi nic nemění). Jiná kontextová menu ani
  flyouty s menu aplikace nemá. Flyout, který není menu (např. popover rozvržení), zůstává beze změny (písmeno je písmeno).
- **Podmenu:** nové ověření, že klávesa pochází z tohoto top levelu (`IsWithin`): písmeno napsané v podmenu (vlastní
  popup, jehož klávesy probublávají do menu) se nechá podmenu a nespustí položku hlavního menu se stejným písmenem.
  Platí i pro menu oznamovací oblasti (dřív by takové písmeno mohlo spustit položku hlavního menu; podmenu tiskárny
  v současnosti žádnou kolizi nemá).
- **macOS:** beze změny. Menu macOS přístupové klávesy nemají (psaní vybírá, Return spouští) a menu oznamovací oblasti je
  tam nativní NSMenu, takže `MenuAccessKeys.Register(popupMenus: !OperatingSystem.IsMacOS())` – na Macu kontextová menu
  a menu flyouty písmeno samotné nespouští (jako dosud); Alt(Option)+písmeno je vlastní chování Avalonie jako dřív.
  Proto žádná nová položka do T-115; UI testy počítají s tím, že na macOS písmeno samotné nic neudělá.
- **Headless UI testy:** `HeadlessTestApp` nově zapíná `OverlayPopups = false` – popupy se otevírají ve vlastním top
  levelu jako ve skutečné aplikaci (výchozí headless overlay vrstva směruje klávesy jinak a ani Alt+písmeno v ní
  v kontextovém menu nefungovalo, tedy by testy neodpovídaly realitě). Jediný dotčený existující test
  (`CameraWindowLayoutModeUiTests.PressIn`) už neuvolňuje klávesu v popupu, který se klávesou zavřel.
- **Testy:** `tests/LayerPeek.App.UiTests/Cameras/CameraWindowContextMenuKeyboardUiTests.cs` (písmeno samotné v en/cs,
  písmeno po stisku a uvolnění Alt v menu otevřeném z klávesnice, malý náhled, Alt+písmeno v obou režimech, neznámé
  písmeno / skrytá / zakázaná položka nic nedělá, Ctrl+písmeno není přístupová klávesa) a
  `tests/LayerPeek.App.UiTests/Windows/PopupMenuAccessKeysUiTests.cs` (obecné `ContextMenu`, `MenuFlyout`, střídání
  stejných písmen bez spuštění, podmenu, flyout bez menu; review: písmeno žádné položky `MenuFlyout` nespustí přístupovou
  klávesu vlastníka okna, i když se znovu vyvolané Alt+písmeno z `PopupRoot` šíří dál do okna). Před opravou selhávaly právě testy písmena samotného (5 + 3),
  testy Alt+písmeno, neznámého písmena a Ctrl procházely. `TrayMenuKeyboardUiTests` beze změny a zelené.
  Otevření z klávesnice (Menu, Shift+F10) headless platforma neumí, test vyvolá `ContextRequested` bez ukazatele jako
  Avalonia; skutečné klávesy ověřuje E2E.
- **E2E:** `CameraPinTests.ContextMenuOpensWithTheRightButtonTheMenuKeyAndShiftF10` – v normálním okně menu otevřené
  pravým tlačítkem a samotné písmeno bez Alt (popup otevřený myší má klávesnici), po uvolnění Alt samotné písmeno, v malém náhledu písmeno bez Alt, navíc jedno otevření klávesou Menu s Alt+písmeno (Alt+písmeno dál
  funguje). Nespuštěno (potřebuje plochu uživatele).

- **Ověření na skutečné ploše (hlavní agent, 2026-11-01, Windows 11 Pro 10.0.26300):** `CameraPinTests` 5/5 (kontextové menu: pravé tlačítko + písmeno samotné, Alt uvolněný + písmeno, klávesa Menu, Shift+F10, malý náhled i Alt+písmeno), regrese `TrayMenuDetailTests.MenuWorksFromTheKeyboardWithAccessKeys` 1/1 a `CameraLayoutModeTests.ContextMenuKeyboardPopoverEscapeAndRestoreDefaults` 1/1. Review bez blokujících nálezů (doplněn test `LetterOfNoMenuItemDoesNotRunAnAccessKeyOfTheWindow`), AOT publish win-x64 bez varování. Uzavřeno 2026-11-01.
