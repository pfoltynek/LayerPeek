---
id: T-084
title: Widget Teploty se sparkline a kompaktní režim TemperatureChart
status: todo
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
- [ ] App.Tests: obsah widgetu (cíl jen při ohřevu, chybějící podložka / tryska, offline), velikosti, zvýraznění, levné porovnání
- [ ] `TemperatureChartData` a kompaktní režim:
  - testy dat beze změny chování diagnostiky,
  - okno Diagnostika má stejné rozložení a nové barvy trysky a podložky ze sdílených zdrojů (snímky přegenerované a zkontrolované),
  - kompaktní graf má okno 60 minut a „Žádná data“ bez historie.
- [ ] Headless UI testy: widget ve všech velikostech, sparkline vykreslí data z `TelemetryHistory` za posledních 60 minut, přerušení při výpadku, odhlášení od `Changed` po zavření okna kamery i skrytí widgetu
- [ ] Žádná duplicitní implementace kreslení grafu
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Rozhodnutí (uživatel, 2026-10-24)
- **Časové okno (Q1):** sparkline má pevně **60 minut**, bez nastavení.
- **Barvy (T-076 Q10):** tryska oranžová, podložka modrá. Graf v okně Diagnostika používá stejné barvy.
- **Zvýraznění (T-076 Q8):** klíčový údaj je teplota trysky (akcentová barva).

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
