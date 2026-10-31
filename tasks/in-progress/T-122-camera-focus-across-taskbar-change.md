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
- [ ] Headless testy nejdřív (selžou před opravou): klik myší na špendlík, deaktivace a nová aktivace okna → řada tlačítek
      bez myši skrytá; Enter / mezerník na špendlíku v malém i normálním okně → fokus zůstane na špendlíku
- [ ] Oprava A i B na Windows, macOS beze změny
- [ ] E2E `CameraPinTests` (T-120) projdou na skutečné aplikaci → běh koordinuje hlavní agent s uživatelem
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
(žádné)
