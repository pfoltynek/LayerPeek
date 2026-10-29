---
id: T-106
title: Kompaktní graf teplot – vlastní pás a měřítko pro každou řadu
status: in-progress
owner: ui-dev
priority: P2
platform: all
depends_on: [T-084, T-090]
---

## Cíl
Malé grafy teplot v okně kamery (sparkline widgetu Teploty, T-084, a řádek grafu malého náhledu, T-090) vypadají jako rovné čáry, přestože teplota trysky a podložky kolísá o 1–2 °C. Příčina: `ChartScale.ValueAxis` dává ose hodnot rozsah od 0 °C po zaokrouhlený krok nad maximem (aspoň 50), takže při ~215 °C na grafu vysokém ~60 px je 1–2 °C zlomek pixelu. Kompaktní graf má kolísání ukázat; graf v okně Diagnostika si osu od 0 °C ponechá.

## Rozhodnutí (uživatel, 2026-10-29; Windows i macOS)
- V kompaktním grafu má každá řada (tryska, podložka; komora, pokud existuje) **vlastní vodorovný pás**: tryska nahoře, podložka pod ní, komora pod ní.
- Každý pás je škálovaný na **vlastní minimum a maximum řady** v zobrazeném okně (60 minut kompaktního grafu), s **minimálním rozsahem** (výchozí 6 °C, pojmenovaná konstanta s odůvodněním) **vycentrovaným na data** – kolísání 1–2 °C je vidět, šum 0,1 °C nedělá velké vlny.
- Hodnoty v textu vedle grafu zůstávají beze změny. Barvy řad zůstávají.

## Rozsah
- `TemperatureChart` v kompaktním režimu (`IsCompact`): pásy místo osy od 0 °C. Graf Diagnostiky beze změny.
- Ošetřit: žádná data, jediný vzorek, mezery (NaN), čáry cílových teplot, náběh ohřevu (velký rozsah → pás se jen přeškáluje), objevení / zmizení řady (rozložení pásů nesmí skákat při každém překreslení), výkon (žádné alokace navíc při překreslení).
- Testy: jednotkové testy škálování pásů (minimální rozsah, centrování, NaN, jediný vzorek, náběh), headless UI / snímkové testy s viditelným kolísáním, test neměnného grafu Diagnostiky.
- CHANGELOG [0.3.1] (Changed).

## Akceptační kritéria
- [x] Kompaktní graf: pás pro každou řadu s hodnotami v okně (tryska nahoře, podložka dole), měřítko podle vlastního minima a maxima, minimální rozsah `CompactBands.MinimumSpan` = 6 °C vycentrovaný na data
- [x] Bez dat „Žádná data“ jako dosud; jediný vzorek uprostřed pásu; mezery přerušují čáru jako dosud; náběh ohřevu jen přeškáluje pás
- [x] Cílové teploty: kreslí se v pásu své řady, oříznuté na pás (chování zdokumentované)
- [x] Rozložení pásů se mění jen při objevení / zmizení řady v 60minutovém okně
- [x] Žádné alokace navíc při překreslení (pásy se počítají jen při změně dat nebo velikosti, do pevných polí)
- [x] Graf v okně Diagnostika beze změny (osa od 0 °C pro všechny řady; test, snímky Diagnostiky beze změny)
- [x] Jednotkové testy `CompactBandsTests`, `TemperatureChartDataTests` (rozsah řad)
- [x] Headless UI testy a snímky (widget M, řádek grafu malého náhledu) s viditelným kolísáním; snímky macOS přegenerované a zkontrolované
- [ ] Snímky Windows přegenerované na Windows (viz Poznámky) – na Macu to nejde
- [x] Popisek rozsahu pásu („212–218 °C“) jen ve widgetu Teploty velikosti L (rozhodnutí Q2), sekundární barva, 12, ve sloupci vpravo od grafu; v M / S a v malém náhledu žádný
- [ ] Ruční krok (uživatel): na skutečné tiskárně ověřit, že kolísání s minimálním rozsahem 6 °C nepůsobí příliš dramaticky (Q4)
- [x] CHANGELOG [0.3.1] (Changed), `docs/architecture.md`
- [x] `dotnet build` (Debug i Release) a `dotnet test` bez chyb a varování (macOS)

## Otevřené otázky
Úroveň návrhu (ui-designer); všechny rozhodnuté:
1. ~~**Oddělení pásů:** mezi pásy je jen prázdná mezera 4 px (`CompactBands.Gap`), bez dělicí čáry. Má tam být tenká čára / jiné pozadí pásu, nebo jiná velikost mezery?~~ (rozhodnuto 2026-10-29: ponechat mezeru 4 px bez čáry)
2. ~~**Popisky pásů:** pásy nemají žádný popisek rozsahu (např. „212–218 °C“). Stačí to tak, nebo přidat nenápadnou indikaci měřítka?~~ (rozhodnuto 2026-10-29: nenápadný popisek rozsahu min–max u každého pásu **jen ve velikosti L** widgetu Teploty, např. „212–218 °C“, sekundární barvou a malým písmem, nesmí překrývat čáru; v M / S ani v malém náhledu žádný)
3. ~~**Cílová teplota mimo pás:** dnes se cíl mimo rozsah skutečných hodnot nekreslí (oříznutí pásem). Ponechat oříznutí?~~ (rozhodnuto 2026-10-29: ponechat oříznutí)
4. ~~**Minimální rozsah 6 °C** – ověřit na skutečné tiskárně, zda nepůsobí kolísání příliš dramaticky (případně 8–10 °C).~~ (rozhodnuto 2026-10-29: ponechat 6 °C; uživatel ověří na skutečné tiskárně – ruční krok v akceptačních kritériích)

## Poznámky k implementaci
- 2026-10-29, ui-dev (macOS).
- **Řady:** historie (`TelemetrySample`) má jen trysku a podložku (aktuální a cílovou), komora v modelu není – pásy jsou nejvýš dva. Přidání komory = další řada v `TemperatureChartData` a její pás pod podložkou (smyčka v `LayOutCompactBands` jde v pořadí výčtu `TemperatureSeries`).
- **`ValueBand`** (`App/Diagnostics/Chart/ValueBand.cs`): mapování hodnoty na Y (`Bottom`, `Height`, `Low`, `High`). Graf Diagnostiky používá jeden pás `0 … top` přes celou plochu – výpočet je bitově shodný s dřívějším `ValueToY`, snímky Diagnostiky se nezměnily.
- **`CompactBands`**: `MinimumSpan` = 6 °C – Prusa drží trysku i podložku zhruba ±1 °C od cíle a hlásí desetiny stupně; v nejmenším grafu (řádek malého náhledu 52 px, dva pásy po 24 px) je 1 °C ≈ 4 px, šum 0,1 °C pod půl pixelu. `Gap` = 4 px mezi pásy. `TryGetRange` (data, nebo 6 °C kolem středu dat; bez konečné hodnoty žádný pás), `Slot` (rovnoměrné rozdělení výšky, nejméně 1 px), `TryCreate` (pás zmenšený nahoře a dole o polovinu tloušťky čáry, aby se čára v extrému neořízla).
- **`TemperatureChartData.GetRange(series)`**: minimum a maximum každé řady v okně, počítané v jediném průchodu decimace (bez alokace). `MaxValue` pro Diagnostiku beze změny.
- **Rozložení:** pás má řada se skutečnou hodnotou v 60minutovém okně; cíl sdílí pás své řady. Počet pásů se tak mění, jen když řada do okna vstoupí nebo z něj zcela odejde (např. tiskárna bez podložky, test `OnlySeriesWithValuesGetABand`); jednotlivý vzorek bez hodnoty rozložení nemění (`BandsStayPutWhileTheSameSeriesHaveValues`). Měřítko pásu se přepočítá s každou změnou dat (posun okna) – to je požadované chování (náběh ohřevu, který z okna odejde, pás opět zúží).
- **Cíle:** rozsah pásu určují jen skutečné hodnoty; čárkovaný cíl se kreslí v pásu své řady a je oříznutý na pás (`PushClip` pro každou řadu jen v kompaktním režimu). V ustáleném stavu je cíl uprostřed pásu (užitečná referenční čára), při náběhu leží v rozsahu; když je daleko mimo (ohřev právě zapnut), nekreslí se – nestlačí čáru skutečné teploty (`TargetFarFromTheActualValuesIsClippedToTheBand`). Cíl řady bez skutečných hodnot se nekreslí; bez skutečných hodnot vůbec graf ukáže „Žádná data“.
- **Výkon:** pásy se počítají v `RebuildSeries` (jen při změně dat, velikosti nebo štětců/tloušťky) do pevných polí `_bands` / `_bandClips`; překreslení bez nových dat jen přehraje uložené geometrie. Alokační testy (`DiagnosticsAllocationTests`) beze změny procházejí.
- **Testy:** App.Tests `CompactBandsTests` (minimální rozsah, centrování, jediný vzorek, NaN / ∞ / obrácené meze, náběh, přesně 6 °C, inset, sloty, neplatné indexy, osa od 0) a `TemperatureChartDataTests.RangeOfEachSeriesIgnoresMissingValuesAndSamplesOutsideTheWindow`. UI testy: `CameraWindowTemperaturesWidgetUiTests` (pásy a viditelné kolísání ve widgetu M, náběh, jen tryska, stabilní rozložení, cíl mimo pás, snímek), `CameraWindowMiniTogglesUiTests` (pásy 24 + 4 + 24 px v řádku malého náhledu, viditelné kolísání, snímek), `DiagnosticsChartColoursUiTests.FullChartKeepsOneAxisFromZeroForAllSeries`.
- **Snímky macOS** (přegenerované a vizuálně zkontrolované): změněné `camera-widget-temperatures-m.png`, `camera-widget-temperatures-l.png`, `camera-mini-strip-rows.png`; nové `camera-widget-temperatures-steady-m.png`, `camera-mini-strip-chart-steady.png`. Beze změny `camera-widget-temperatures-s.png`, `-no-data.png`, `camera-mini-strip-rows-240-de.png` (bez historie) a všechny snímky Diagnostiky.
- **Snímky Windows – přegenerovat na Windows** (`LAYERPEEK_UPDATE_SNAPSHOTS=1 dotnet test --project tests/LayerPeek.App.UiTests`, pak zkontrolovat a vrátit případné nesouvisející změny): `Snapshots/camera-widget-temperatures-m.png`, `camera-widget-temperatures-l.png`, `camera-mini-strip-rows.png` (změněné) a `camera-widget-temperatures-steady-m.png`, `camera-mini-strip-chart-steady.png` (nové). Do té doby na Windows tyto testy selžou (`SnapshotOfTheCard` M / L, `SnapshotOfSteadyTemperatures`, `SnapshotOfBothRowsExpanded`, `SnapshotOfTheChartRowWithSteadyTemperatures`).
- Ověřeno na macOS (build Debug / Release, všechny testy, headless snímky). Na Windows neověřeno (jen snímky; logika je stejná pro obě OS). Aplikace se nespouštěla.
- **Popisky rozsahu pásů v L** (2026-10-29, rozhodnutí Q2, ui-dev, macOS): `TemperatureChart` má nové vlastnosti `BandRangeWidth` (šířka sloupce vpravo od plochy grafu; 0 = bez popisků, výchozí), `BandRangeFormat`, `BandRangeBrush`, `BandRangeFontSize`. Popisky se kreslí jen v kompaktním režimu se sloupcem a formátem, takže je má jen widget L (styl `Border.overlayCard.large`): sloupec 80 px (`TemperatureChartRangeWidth`; nejširší možný text „800–888 °C“ má v Inter 12 asi 68 px, zbývá ≥ 8 px mezi plochou a popiskem), písmo 12 jako popisky osy v L, barva `OverlayTextSecondaryBrush`. Plocha grafu je o sloupec užší, takže popisek nikdy nepřekryje čáru; popisek „teď“ pod grafem končí s plochou (`TemperatureChartRangeMargin`). Popisek je zarovnaný vpravo a vycentrovaný na svůj pás, hodnoty jsou celé stupně rozsahu pásu (zaokrouhlení od nuly), tedy i minimální rozsah 6 °C („212–218 °C“ při 215 ± 1,5). Bez dat žádné popisky; „Žádná data“ je v kompaktním grafu uprostřed celého controlu. Text popisku se vytváří jen při změně zobrazených stupňů, formátu, barvy nebo jazyka.
- **Nový text** `CameraOverlayChartRange` = `{0}–{1}&#160;°C` ve všech 8 jazycích (jen čísla, pomlčka a nedělitelná mezera před °C jako u ostatních teplot); `TemperaturesWidgetViewModel.ChartRange`.
- **Testy:** App.Tests `TemperaturesWidgetTests` (`ChartTextsAreSet`, `ChartRangeIsNumbersOnlyInEveryLanguage` – 8 jazyků). UI testy `CameraWindowTemperaturesWidgetUiTests`: `BandRangesAreShownOnlyInTheLargeCard` (S / M / L; jen L, hodnoty = rozsah pásu, barva, velikost, ve sloupci a vycentrované na pás), `BandRangeOfAHeatingRampShowsTheWholeRange`, `WidestBandRangeFitsItsColumnInEveryLanguage` (8 jazyků, nic se neořízne, karta se vejde), upravený `CardShowsThePartsOfItsSize` („teď“ končí s plochou). Malý náhled beze změny (snímky malého náhledu se nezměnily).
- **Snímky macOS:** změněný `camera-widget-temperatures-l.png`, nový `camera-widget-temperatures-steady-l.png` (zkontrolováno). **Na Windows přegenerovat** (`LAYERPEEK_UPDATE_SNAPSHOTS=1 dotnet test --project tests/LayerPeek.App.UiTests`): navíc k výše uvedeným `Snapshots/camera-widget-temperatures-l.png` (znovu změněný) a nový `Snapshots/camera-widget-temperatures-steady-l.png` (test `SnapshotOfSteadyTemperatures` L).
