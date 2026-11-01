---
id: T-122
title: Okno kamery – fokus při změně tlačítka na hlavním panelu
status: done
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
  (Skutečná příčina se ukázala jiná – fokus vrácený aktivací bez `:focus-visible`, viz Poznámky k implementaci.)

## Rozsah
1. Po kliknutí myší na tlačítko okna se fokus přesune na neutrální prvek (ne na tlačítko), takže ho nová aktivace okna
   tlačítku nevrátí (návrh: `Surface` `Focusable`, `IsTabStop="False"`).
2. Klávesnicí ovládaný špendlík zůstane fokusovaný i po změně přítomnosti na hlavním panelu a po nové aktivaci okna
   (původní návrh „vrátit ztracený fokus po změně `ShowInTaskbar`“ se ukázal zbytečný – fokus se neztrácí, viz poznámky).
3. Chování macOS beze změny (tam se přítomnost řeší jinak, T-102); `Topmost` ani pravidla T-102 se nemění.

## Akceptační kritéria
- [x] Headless testy nejdřív (selžou před opravou): klik myší na špendlík, deaktivace a nová aktivace okna → řada tlačítek
      bez myši skrytá; Enter / mezerník na špendlíku v malém i normálním okně → fokus zůstane na špendlíku
- [x] Oprava A i B na Windows, macOS beze změny (sdílený kód okna kamery: ověření na Macu → přesunuto do T-115)
- [x] E2E `CameraPinTests` (T-120) projdou na skutečné aplikaci → 5/5 dne 2026-11-01 (hlavní agent na volné ploše)
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
- **Příčina B potvrzena** (dočasná diagnostika na skutečné ploše 2026-10-31: záznam `WM_ACTIVATE` / `WM_SETFOCUS` /
  `WM_KILLFOCUS`, kláves a `GotFocus` / `LostFocus` Avalonie; po ladění odstraněna): změna vlastníka okna při připnutí
  fokus **nebere** (žádné `WM_KILLFOCUS`), domněnka z rozboru T-120 neplatí. Odepnutí vrací tlačítko na hlavní panel
  skrytím a novým ukázáním okna; jeho aktivace vrátí fokus zapamatovanému špendlíku s `NavigationMethod.Unspecified`,
  tedy **bez `:focus-visible`**. `OnWindowButtonClick` poznával klávesnici právě podle `:focus-visible`, takže další Enter
  na špendlíku bral jako klik myší a fokus přesunul na obraz (před T-122 ho `Focus(null)` zrušil); mezerník pak nic
  nepřepnul. Totéž po každé nové aktivaci (Alt+Tab zpět) – pro špendlík i tlačítka režimů.
- **Proč jen „malý náhled + Enter“:** nejde o režim, ale o pořadí v E2E. Je to první stisk po odepnutí (malý náhled
  odepnul mezerník); v normálním okně byl Enter sám tím odepnutím a mezerník před ním měl fokus z Tabu.
- **Oprava B:** z klávesnice je klik, který přijde během směrování události klávesy oknem (`_routingKey`: nastaví tunel
  `KeyDown` / `KeyUp` okna, zruší bublání téže události, obojí i pro obsloužené události; Enter kliká při stisku,
  mezerník při uvolnění), ne podle toho, jak tlačítko fokus dostalo. `ShowInTaskbar` se nastavuje přímo jako dřív; dřívější
  obnova fokusu po změně hlavního panelu (`CameraWindow.Focus.cs`, `WindowFocusNative.cs`) se v záznamu nikdy neuplatnila
  a je odstraněna (žádný nativní kód navíc).
- macOS: beze změny chování (stejné rozhodování klávesnice / myš, nic platformního). Na macOS neověřeno.
- Testy: `tests/LayerPeek.App.UiTests/Cameras/CameraWindowFocusUiTests.cs` (5 případů). A:
  `PinClickedWithTheMouseIsNotFocusedAgainByTheNextActivation` (normální okno, malý náhled) selhal před opravou A;
  `ModeButtonClickedWithTheMouseIsNotFocusedAgainByTheNextActivation` prošel i před ní (hlídá regresi). B:
  `KeysOnThePinFocusedAgainByAnActivationKeepTheFocusOnIt` (normální okno, malý náhled: Tab, mezerník,
  `Focus(null)` + `Hide()` / `Show()` → fokus vrácen aktivací bez `:focus-visible`, pak Enter, mezerník, Enter) selhal
  s rozhodováním podle `:focus-visible` (fokus skončil na obrazu – přesně jako E2E) a s opravou prochází.
- Ověřeno 2026-11-01 na Windows 11 Pro 10.0.26300 (headless, celý `dotnet test`); E2E `CameraPinTests` po opravě B
  zatím nespuštěno (koordinuje hlavní agent).

Review 2026-11-01 bez blokujících nálezů (spolu s T-120); uzavřeno 2026-11-01.

## Otevřené otázky
(žádné)
