---
id: T-084
title: Widget Teploty se sparkline a kompaktní režim TemperatureChart
status: done
owner: ui-dev
priority: P2
platform: all
depends_on: [T-081]
---

## Cíl
Widget **Teploty**: tryska a podložka aktuální / cílová a kompaktní graf posledních ~60 minut (bez os, cíle čárkovaně, tryska oranžová, podložka modrá). Graf vzniká **rozšířením** stávajícího `TemperatureChart` o kompaktní režim – žádný druhý kód grafu.

## Kontext
- `App/Diagnostics/Chart/TemperatureChart` (vlastní `Control`; vlastnosti `History`, `PrinterId`, `TimeWindow`, `Clock`, štětce, osy, mřížka, `NoDataText`; cíle čárkovaně jen při ohřevu, přerušení čar), příprava dat `TemperatureChartData` bez Avalonie, `ChartScale`, `ChartLabelCache`. Data z `Core/History/TelemetryHistory` (T-012; odběr `Changed` jen při připojení do okna).
- Vzhled a velikosti S/M/L podle T-076; tvar čísel podle T-036 Q2 („215 / 215 °C“, cíl jen při zapnutém topení).

## Rozsah
- `TemperatureChart` dostane kompaktní režim (např. vlastnost `IsCompact` nebo `ShowAxes` / `ShowGrid` / `ShowLabels`):
  - bez os, popisků a mřížky, tenčí čáry podle návrhu, průhledné pozadí,
  - časové okno pevně **60 minut**.

  Rozložení okna Diagnostika se nemění.
- Graf se může přesunout do sdíleného jmenného prostoru (např. `App/Charts`), protože ho používají dvě místa.
- **Barvy:** tryska oranžová, podložka modrá, přesné odstíny z návrhu T-076. **Stejné barvy dostane i graf v okně Diagnostika** (rozhodnutí 2026-10-24). Barvy jsou sdílené zdroje (jeden `ResourceDictionary`), ne kopie. Snímky reference Diagnostiky se přegenerují a vizuálně zkontrolují.
- **Zvýraznění:** teplota trysky akcentovou barvou.
- Widget Teploty (registrace v registru T-081): S = jen hodnoty, M = hodnoty + sparkline, L = větší graf (podle návrhu); widget se skryje, když teploty nejsou známé (offline).
- Odběr historie jen při připojení karty do stromu a jen pro viditelný widget; zavřené / skryté okno nic nestojí.
- Připravit kompaktní graf i pro řádek grafu ≈ 60 px v pásu malého náhledu (T-090). Musí to být stejný control, jen v jiné šabloně. Bez historie zobrazí „Žádná data“ (`NoDataText`).

## Akceptační kritéria
- [x] App.Tests: obsah widgetu (cíl jen při ohřevu, chybějící podložka / tryska, offline), velikosti, zvýraznění, levné porovnání
- [x] `TemperatureChartData` a kompaktní režim:
  - testy dat beze změny chování diagnostiky,
  - okno Diagnostika má stejné rozložení a nové barvy trysky a podložky ze sdílených zdrojů (snímky přegenerované a zkontrolované),
  - kompaktní graf má okno 60 minut a „Žádná data“ bez historie.
- [x] Headless UI testy: widget ve všech velikostech, sparkline vykreslí data z `TelemetryHistory` za posledních 60 minut, přerušení při výpadku, odhlášení od `Changed` po zavření okna kamery i skrytí widgetu
- [x] Žádná duplicitní implementace kreslení grafu
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Rozhodnutí (uživatel, 2026-10-24)
- **Časové okno (Q1):** sparkline má pevně **60 minut**, bez nastavení.
- **Barvy (T-076 Q10):** tryska oranžová, podložka modrá. Graf v okně Diagnostika používá stejné barvy.
- **Zvýraznění (T-076 Q8):** klíčový údaj je teplota trysky (akcentová barva).

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- 2026-10-25, ui-dev. Widget `App/Cameras/Overlay/Widgets/TemperaturesWidget*` (view model, šablona karty), registrace jedním řádkem v `Composition/OverlayWidgets.cs` (za Časy). Popis v `docs/architecture.md` (oddíl widgety a bod 7 Diagnostika).
- **Obsah:** `TemperaturesContent` = tryska a podložka v celých stupních (`OverlayValues.Temperature`, cíl jen při zapnutém topení). Formátuje se jen při změně celého stupně; test 0 B alokací na 1000 snapshotů (`TemperaturesWidgetAllocationTests`, kolekce `AllocationSensitive`). Teploty se ukazují, kdykoli je snapshot má (pravidlo T-036), tedy ne offline / před prvním dotazem; bez obou se karta skryje. Chybí-li jen tryska, zůstane podložka (návrh řeší jen chybějící podložku, symetrické chování).
- **Zvýraznění:** jen číslo aktuální teploty trysky. `OverlayFormat.TemperatureParts` rozdělí text za aktuální hodnotou („215“ + „ / 215 °C“); jazyk, který by číslo nedal na začátek, by zvýraznil celý text (dnes takový není). Velikosti hodnot podle návrhu 2.3 (S 13, M 14, L 16, polotučně); obecná poznámka 1.3 „zvýrazněné údaje ≥ 15 Bold“ tu podle schváleného návrhu widgetu neplatí.
- **Velikosti (T-076 2.3):** S jeden řádek (výška 36), M řádek + graf 256 × 64 + popisky osy (karta 284 × 134), L řádky se slovy ve sdíleném sloupci (`SharedSizeGroup`) + graf 360 × 110 (karta 392 × 216). Ikony: kužel otočený o 180° (`OverlayNozzleBrush`), vodorovná čára (`OverlayBedBrush`, v L 20). Čitelnost ikon zkontrolovaná na snímcích – barevné čárky jako záloha nejsou potřeba. Jméno pro čtečku obsahuje „graf posledních 60 minut“ jen v M / L, kde graf je; při změně velikosti se přepočítá jen jméno.
- **Kompaktní `TemperatureChart`** (stejný control jako v Diagnostice, jmenný prostor ponechán `Diagnostics.Chart` kvůli menšímu zásahu): nové vlastnosti `IsCompact` (bez os, popisků a mřížky, plocha = celý control, „Žádná data“ 12), `LineThickness` (výchozí 1,75 = Diagnostika; překryv 1,5 / L 2), `TargetOpacity` (výchozí 1; překryv 0,7), pevné okno `CompactTimeWindow` = 60 min. Cíle čárkovaně 1 (4 / 3) a přerušení čar beze změny. Pro T-090 stačí stejný control v jiné šabloně.
- **Výkon:** graf se překresluje jen při `Changed` své tiskárny (test počtu přestaveb geometrie), překreslení bez nových dat použije uložené geometrie. Kompaktní graf drží jen vzorky své hodiny (`TemperatureChartData.SizeBufferToRange`: buffer podle počtu vzorků v rozsahu + 25 %, nejvýš kapacita; posun okna nealokuje – test v `DiagnosticsAllocationTests`), místo bufferu celé 24h historie (až ~4 MB na okno). Diagnostika se chová beze změny.
- **Odběr historie jen při zobrazení:** graf odebírá `TelemetryHistory.Changed` jen když je připojený ve stromu a viditelný on i všichni předkové. Avalonia 12 nemá veřejnou událost změny `IsEffectivelyVisible` (`IsEffectivelyVisibleChanged` je `internal`), proto graf po dobu připojení sleduje `IsVisible` svých předků. Odhlásí se po zavření okna, při skryté kartě (offline), v S, v malém náhledu a po skrytí widgetu v rozložení; po opětovném zobrazení překreslí s aktuální historií. Minimalizované okno se počítá jako zobrazené (jako dosud v Diagnostice). Platí i pro graf Diagnostiky (skrytý bez tiskárny).
- **Barvy:** sdílený `Resources/ChartColors.axaml` – `TemperatureNozzleColor` `#FF8A3D` / `TemperatureBedColor` `#6CB4FF` (z nich `OverlayNozzleBrush` / `OverlayBedBrush` v `OverlayStyles.axaml`) a `ChartNozzleBrush` / `ChartBedBrush` s `ThemeDictionaries` pro Diagnostiku (tmavý = barvy překryvu, světlý `#D9480F` / `#1864AB`). Hex hodnoty jsou jen na jednom místě.
- **Snímky:** nové `camera-widget-temperatures-s/m/l.png` a `camera-widget-temperatures-no-data.png`; přegenerovaný `diagnostics-idle-dark.png` (jediný snímek Diagnostiky v tmavém motivu – rozložení stejné, jen nové barvy čar a legendy). Světlé snímky Diagnostiky se při přegenerování nezměnily. Vše vizuálně zkontrolováno.
- **Texty (8 jazyků):** `CameraOverlayChartStart` („−60 min“, znak mínus U+2212), `CameraOverlayChartNow` (fr „maintenant“ má 10 znaků místo doporučených 8, vejde se – test ve všech jazycích a velikostech), `CameraOverlayChartNoData` („Žádná data“), `CameraOverlayChartName`. Znovu použité `DiagnosticsNozzle`, `DiagnosticsBed`, `CameraOverlayTemperatureWithTarget`, `DiagnosticsTemperatureValue`.
- **Testy:** App.Tests `TemperaturesWidgetTests` (registrace, obsah všech stavů, NaN / ∞, chybějící tryska / podložka, offline, levné porovnání, zvýraznění, velikosti, texty a přepnutí jazyka, rozdělení textu), `TemperaturesWidgetAllocationTests`, rozšířené `TemperatureChartDataTests` a `DiagnosticsAllocationTests`. UI testy `CameraWindowTemperaturesWidgetUiTests` (vykreslené vlastnosti textů S/M/L, výšky a šířky karet, ikony, zvýraznění, `tnum`, data za 60 min, přerušení, „No data“, překreslení jen pro svou tiskárnu, odhlášení po zavření / skrytí / S / malém náhledu / skrytí v rozložení, nejdelší texty 8 jazyků × 3 velikosti, přepnutí jazyka, snímky) a `DiagnosticsChartColoursUiTests` (světlý / tmavý motiv, legenda, přepnutí motivu). E2E specifické pro widget žádné (celek T-093).
- Ověřeno na Windows (headless testy, krátké spuštění aplikace v izolovaném testovacím režimu `LAYERPEEK_TEST_DATA_DIR` – start bez chyb v logu). macOS neověřeno (SF Pro, vykreslení ikon).
