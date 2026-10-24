---
id: T-085
title: Widget Název tisku (hezčí název a čipy, bez náhledu G-code)
status: todo
owner: ui-dev
priority: P2
platform: all
depends_on: [T-078, T-081]
---

## Cíl
Widget **Název tisku**: čitelný název z `PrintFileNameInfo` (T-078) a čipy (materiál, výška vrstvy, tryska; u L i odhad sliceru). Náhled G-code přidá až T-087 – tento úkol na něj nechá místo podle návrhu (velikost M: 40 px, L: 72 px), bez náhledu se rozložení nesmí rozpadnout.

## Rozsah
- Widget (registrace v T-081): S = čip s názvem, M = název + čipy, L = název + čipy + odhad sliceru (podle návrhu T-076); viditelný během úlohy a po dokončení nebo zastavení tisku až do začátku dalšího tisku. Používá poslední známou úlohu, kterou klient PrusaLink drží u FINISHED / STOPPED.
- Kompaktní řádek v malém náhledu bez úlohy zmizí (T-090).
- Formát čipů podle kultury (desetinný oddělovač, „0,2 mm“, „⌀ 0,4 mm“ apod. podle návrhu), dlouhý název zkrácený podle návrhu (dnešní pravidlo T-036: začátek + „…“ + konec), neznámý tvar názvu = název bez čipů.
- Rozbor názvu se počítá jen při změně názvu souboru (cache jako `CameraOverlayTextCache`).
- Kompaktní podoba pro řádek názvu v malém náhledu (T-090) – stejný view model, jiná šablona; zde jen připravit, použití v T-090.

## Akceptační kritéria
- [ ] App.Tests: velikosti S/M/L, čipy podle kultury cs/en/de, neznámý tvar, 8.3 název `BENCHY~1.BGC`, dlouhé názvy, viditelnost podle stavu, levné porovnání, přepnutí jazyka
- [ ] Headless UI testy: widget ve všech velikostech v nejdelším jazyce bez přetečení; snímky reference
- [ ] Nové texty ve všech 8 jazycích
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Rozhodnutí (uživatel, 2026-10-24)
- **Název:** sentence case, název s velkými písmeny zůstane, jak je. Model tiskárny se vynechá a čip nemá (T-078).
- **Zvýraznění:** klíčový údaj je název tisku (akcentová barva). Je to návrh architekta, designer ho potvrdí v T-076.
- **Po skončení tisku:** widget zůstane i po dokončení nebo zastavení tisku (např. během vychladnutí), dokud nezačne další tisk. Stejně se drží náhled G-code (T-086).

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
