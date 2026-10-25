---
id: T-083
title: Widget Časy (zbývá, čas dokončení, doba tisku)
status: done
owner: ui-dev
priority: P2
platform: all
depends_on: [T-081]
---

## Cíl
Widget **Časy**: „Zbývá“ jako velké číslo, čas dokončení a doba tisku s ikonami (bez textových popisků). Velikosti S/M/L a zvýraznění podle návrhu T-076.

## Kontext
- Formáty a pravidla z T-036 (sdílené formátovače přesunuté v T-081): zbývající čas zaokrouhlený nahoru na minutu, čas dokončení v místním čase z `TimeProvider.LocalTimeZone`, jiný den „Hotovo 17. října v 8:00“ (slova a pořadí podle jazyka aplikace, čas podle regionu), absurdní odhad (`TimeSpan.MaxValue`) nepřeteče. Údaje jen během úlohy.
- Při ikonách místo popisků musí čtečka obrazovky dostat celý text („Zbývá 1 h 5 min“, „Hotovo v 13:05“).

## Rozsah
- View model + šablona, jedna registrace v `OverlayWidgetRegistry`.
- Testy pravidel zobrazení z T-036 týkající se časů (časová pásma, letní čas, jiný den, přetečení) přenesené k widgetu.
- Ikony z Fluent UI System Icons podle návrhu T-076 (geometrie ze sdíleného `ResourceDictionary` z T-081).

## Akceptační kritéria
- [x] App.Tests: každý údaj jen se známou hodnotou a během úlohy, časová pásma a přechod letního času, jiný den, přetečení, kultury cs/en/de/fr, velikosti S/M/L, zvýraznění, levné porovnání, přepnutí jazyka
- [x] Headless UI testy: widget ve všech velikostech v nejdelším jazyce bez přetečení, tabulková čísla (šířka se při změně číslic nemění), `AutomationProperties.Name` s celým textem; snímky reference
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Rozhodnutí (uživatel, 2026-10-24)
- Zvýraznění: klíčový údaj = **„Zbývá“**, vykreslený akcentovou barvou `#FA6831`.
- Ikony z Fluent UI System Icons (T-076, T-081).

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- 2026-10-25, ui-dev. Kód v `App/Cameras/Overlay/Widgets/` (`TimesWidgetViewModel`, `TimesWidgetView.axaml`), registrace jedním řádkem v `Composition/OverlayWidgets.CreateRegistry` (id `times`, název `CameraOverlayWidgetTimes`, priorita `TimesPriority`, min. šířka karty M 180 / L 220, všechny velikosti).
- **Obsah:** `TimesContent` (zbývající minuty, `OverlayFinishTime`, minuty doby tisku; každý údaj `null`, když není známý) počítaný sdílenými `OverlayValues` – údaje jen během úlohy, zaokrouhlení nahoru na minutu, čas dokončení v pásmu `TimeProvider.LocalTimeZone` s posunem v okamžiku dokončení (letní čas), „jiný den“ vůči času snapshotu, absurdní odhad nepřeteče (řádek dokončení chybí, zbývající čas se ořízne). Texty jen sdílenými `OverlayFormat` (pravidla T-036 beze změny). Obsah se mění zhruba jednou za minutu, mezi tím se nic neposílá na UI vlákno; dotaz bez viditelné změny nealokuje (ověřeno i v pásmu s letním časem).
- **Odpočet mezi dotazy:** zadání ani návrh ho nevyžadují, každý dotaz (po několika sekundách) přináší nový odhad. Widget proto nemá časovač ani `RequestUpdate`.
- **Velikosti (T-076 2.2):** S = přesýpací hodiny + zbývající čas (15 Bold); M/L = „Zbývá“ (12/14 `Secondary`) → 2 → zbývající čas (26/34 Bold) → 8 → vlajka + čas dokončení → 4 → hodiny + doba tisku (ikony 16, v L 20 – geometrie `_20_regular`). Výšky ověřené testy: S 36, M 122. Šířky karty S max 200, M 180–260, L 220–320. Neznámý údaj = řádek chybí (mezery `StackPanel` skryté prvky přeskakují), bez „Zbývá“ začíná M/L dalším řádkem.
- **Karta bez řádku se skryje** (obecné pravidlo T-076 2): v M/L bez všech tří údajů, v S bez zbývajícího času (S jiný řádek nemá). Protože o tom rozhoduje vykreslená velikost (malé okno zmenšuje M na S), přepočítá widget při změně `RenderedSize` i `HasContent`; obsah, který byl v S skrytý, se při zvětšení naformátuje v aktuálním jazyce.
- **Oprava frameworku (T-081):** uložená změna velikosti karty, která v rozložení zůstane na místě, se v otevřeném okně neprojevila – vykreslenou velikost nastavuje až měření panelu kotev a nic ho nevyvolalo (projevilo se až změnou velikosti okna). `OverlayAnchorItems` teď sleduje `Size` svých widgetů a při změně zneplatní měření `OverlayAnchorsPanel` (odběr jen v okně, při odpojení se zruší). Test `SavedSizeChangesTheOpenCard`.
- **Zvýraznění:** třída `emphasized` jen na zbývajícím čase (akcent `#FA6831`), řádky zůstávají v barvách sdíleného stylu.
- **Čtečka obrazovky:** `AutomationProperties.Name` karty = „Zbývá 1 h 5 min, Hotovo v 13:05, Doba tisku: 2 h 10 min“ (`ToolTipRemaining`, `CameraOverlayFinishTime`/`FinishDateTime`, `CameraOverlayPrintTime`), jen známé údaje, i v S. `AutomationId` částí `CameraOverlayWidget-times-Remaining`/`-Finish`/`-Elapsed`.
- **Nový text (8 jazyků):** `CameraOverlayRemainingLabel` – Remaining / Zbývá / Zostáva / Verbleibend / Restante / Restant / Rimanente / Pozostało (max. 14 znaků podle T-076 10, test).
- **Testy:** App.Tests `Cameras/Overlay/Widgets/TimesWidgetTests` (registrace, stavy s úlohou a bez ní, neznámé údaje, extrapolace, zaokrouhlení, přetečení, levné porovnání, pásmo, půlnoc, konec i začátek letního času, cs/en/de/fr, přepnutí jazyka, velikosti a skrytí v S, zvýraznění) a `TimesWidgetAllocationTests` (kolekce `AllocationSensitive`, 0 B na 1000 dotazů). Headless `CameraWindowTimesWidgetUiTests`: části a písma S/M/L, výšky, neznámé údaje, skrytí v S a návrat po uložení M, uložená změna velikosti, konec úlohy, zvýraznění, tabulková čísla, nejdelší texty (jiný den, dny) ve všech 8 jazycích × S/M/L bez zkrácení a nad 2 řádky, přepnutí jazyka; nové snímky `camera-widget-times-s/m/l.png` (720 × 405, vizuálně zkontrolované). E2E testy žádné (celý průchod T-093).
- Ručně neověřeno: macOS (SF Pro s `tnum`), skutečné DPI 125–200 %, čitelnost na skutečném videu.
