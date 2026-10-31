---
id: T-122
title: Okno kamery – fokus při změně tlačítka na hlavním panelu
status: in-progress
owner: ui-dev
priority: P2
platform: windows
depends_on: [T-102, T-117]
---

## Cíl
E2E testy T-120 (`CameraPinTests`) na skutečné aplikaci 2026-10-31 našly dvě chyby, které vznikají, když Avalonia mění
přítomnost okna na hlavním panelu (T-102: okno navrchu nemá tlačítko). Rozbor v T-120, oddíl „Druhý běh E2E“.

- **A – po vypnutí špendlíku myší zůstanou tlačítka okna vykreslená naplno.** `CameraWindow.OnWindowButtonClick`
  (`CameraWindow.axaml.cs`) po kliknutí volá `FocusManager.Focus(null)`, okno (rozsah fokusu) si ale špendlík dál pamatuje.
  Vypnutí špendlíku vrátí tlačítko na hlavní panel skrytím a novým ukázáním okna s aktivací; `WM_ACTIVATE` vrátí fokus
  zapamatovanému špendlíku a `StackPanel.windowButtons:focus-within` ukáže celou řadu, ať je myš kdekoli. Totéž čekáme po
  každé nové aktivaci (Alt+Tab zpět) a v malém náhledu.
- **B – zapnutí špendlíku Enterem v malém náhledu ztratí fokus klávesnice.** Skrytí tlačítka na hlavním panelu přes
  skrytého vlastníka (`GWLP_HWNDPARENT`) vezme oknu fokus (`WM_KILLFOCUS`), okno zůstane aktivní, takže ho nic nevrátí;
  další mezerník nic nepřepne. Proč jen v malém náhledu, má ukázat diagnostika (Win32 fokus v reportu testu).

## Rozsah
1. Po kliknutí myší na tlačítko okna se fokus přesune na neutrální prvek (ne na tlačítko), takže ho nová aktivace okna
   tlačítku nevrátí (návrh: `Surface` `Focusable`, `IsTabStop="False"`).
2. Při změně přítomnosti na hlavním panelu (`Topmost` → `ShowInTaskbar` / vlastník) zůstane fokus tam, kde byl: bylo-li
   okno aktivní a fokus po změně zmizel, vrátí se stejnému prvku (klávesnicí ovládaný špendlík zůstane fokusovaný).
3. Chování macOS beze změny (tam se přítomnost řeší jinak, T-102); `Topmost` ani pravidla T-102 se nemění.

## Akceptační kritéria
- [x] Headless testy nejdřív (selžou před opravou): klik myší na špendlík, deaktivace a nová aktivace okna → řada tlačítek
      bez myši skrytá; Enter / mezerník na špendlíku v malém i normálním okně → fokus zůstane na špendlíku
- [x] Oprava A i B na Windows, macOS beze změny
- [ ] E2E `CameraPinTests` (T-120) projdou na skutečné aplikaci → běh koordinuje hlavní agent s uživatelem
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Poznámky k implementaci
- **Příčina A potvrzena** (dekompilovaná Avalonia 12.1.4): `FocusManager.Focus(null)` ve větvi „zapamatovaný prvek =
  aktuální“ jen zruší fokus klávesnice, `FocusedElementProperty` okna (rozsahu) zůstane na špendlíku; každá aktivace
  (`WindowBase.HandleActivated` → `FocusManager.SetFocusScope`) ho fokusuje znovu. Headless to ukáže už samotné
  `Activate()` (stejně jako Alt+Tab zpět nebo `Hide()`/`Show(activate)` při vrácení tlačítka na hlavní panel).
- **Oprava A:** `Surface` (`Border` obrazu) má `Focusable="True"`, `IsTabStop="False"`; klik myší na špendlík přesune
  fokus na `Surface` (`NavigationMethod.Pointer`, bez rámečku fokusu), takže si ho okno pamatuje místo špendlíku. Tab
  z obrazu jde dál na první tlačítko okna; F11, Esc, kontextová nabídka (klávesa Menu / Shift+F10) bublají z obrazu do okna
  jako dřív. Vedlejší (zamýšlený) efekt: klik na obraz teď vezme fokus tlačítku, které ho mělo z klávesnice.
- Ostatní tlačítka okna se po kliku myší skryjí, a tím je okno zapomene (ověřeno testem `ModeButtonClicked…` – prošel
  i před opravou); jejich chování se proto nemění.
- **Oprava B** (`CameraWindow.Focus.cs`, `ShowInTaskbarKeepingFocus`): před změnou `ShowInTaskbar` (jen při skutečné
  změně) si okno zapamatuje fokusovaný prvek a zda měl fokus z klávesnice (`:focus-visible`); bylo-li okno aktivní a je
  aktivní i po změně, ale bez fokusu, vrátí ho stejnému prvku hned a ještě jednou po zpracování zpráv správce oken
  (`Dispatcher.Post`, `Background`). Na Windows navíc vrátí fokus správce oken (`SetFocus` na HWND, jen je-li okno
  aktivním oknem vlákna a fokus jinde; nikdy neaktivuje) – nový `Platform/Windows/WindowFocusNative.cs`
  (`[LibraryImport]`, AOT v pořádku). Neaktivní okno se nikdy nefokusuje.
- **Proč jen malý náhled (B):** v kódu se cesty normálního okna a malého náhledu neliší – obě mění jen `Topmost` →
  `ShowInTaskbar` a Avalonia nastaví stejného skrytého vlastníka (`WindowImpl.UpdateWindowProperties`, `_parent == null`).
  V E2E se ale liší pořadí: v normálním okně je připnutí (mezerník) první změna po otevření, v malém náhledu (Enter) přijde
  až po odepnutí, tj. po `Hide()`/`Show(activate)` od Avalonie. Pravděpodobně tedy nejde o režim, ale o stav fokusu Win32
  po tomto skrytí a ukázání (Avalonia `WM_ACTIVATE` vrací 0 bez `DefWindowProc`, který jinak fokus nastavuje); ověřit to
  lze jen diagnostikou E2E na skutečné ploše. Oprava je na tom nezávislá: platí pro oba režimy i obě klávesy.
- macOS: žádná změna chování – fokus tam při změně Dock ikony nemizí, takže se nic nevrací; nativní část jen na Windows.
  Na macOS neověřeno (vyvíjeno na Windows).
- Testy: `tests/LayerPeek.App.UiTests/Cameras/CameraWindowFocusUiTests.cs` (10 případů). Před opravou selhalo 8
  (A: normální okno i malý náhled; B: mezerník / Enter v obou režimech, ztráta fokusu během změny i po ní), prošly 2
  (tlačítko režimu, neaktivní okno – hlídají regresi). Bez hlavního panelu headless simuluje ztrátu fokusu tím, že ho test
  zruší při změně `ShowInTaskbar`.
- Ověřeno 2026-10-31 na Windows 11 Pro 10.0.26300 (headless); E2E `CameraPinTests` nespuštěno (koordinuje hlavní agent).

### Stav ladění B (2026-10-31)
- E2E `CameraPinTests` na skutečné ploše: 4/5, A opravena; `SpaceAndEnterSwitchThePinAndTheFocusStaysOnIt` dál selhává
  v kroku „mini-enter“.
- Dočasná diagnostika (`CameraWindow.DiagTrace.cs`, záznam do `%TEMP%\layerpeek-t122-trace.log`: WM_ACTIVATE/SETFOCUS/
  KILLFOCUS/klávesy, GotFocus/LostFocus Avalonie) ukázala skutečnou příčinu: **fokus změna vlastníka okna vůbec nebere**
  (při připnutí žádné `WM_KILLFOCUS`). Odepnutí (skrytí a nové ukázání okna) vrátí fokus špendlíku při `WM_ACTIVATE`
  s `NavigationMethod.Unspecified`, tedy **bez `:focus-visible`**. Další Enter pak `OnWindowButtonClick` považuje za klik
  myší (rozhoduje podle `:focus-visible`) a fokus přesune na obraz (před T-122 ho `Focus(null)` zrušil). Proto jen
  „malý náhled + Enter“: je to první stisk po odepnutí (v normálním okně je Enter sám tím odepnutím).
- Domněnka s `WM_KILLFOCUS` / skrytým vlastníkem je tím vyvrácena; obnova fokusu v `CameraWindow.Focus.cs`
  a `WindowFocusNative` se v záznamu nikdy neuplatnila.
- Další krok: klávesnici poznávat podle toho, zda klik vznikl během zpracování klávesy (ne podle `:focus-visible`),
  odstranit neopodstatněnou obnovu fokusu, headless test pro skutečný mechanismus (fokus vrácený aktivací bez
  `:focus-visible`, pak Enter), odstranit diagnostiku, E2E znovu.

## Otevřené otázky
(žádné)
