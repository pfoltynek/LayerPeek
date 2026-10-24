---
id: T-087
title: Widget Název tisku – zobrazení náhledu G-code (M 40 px, L 72 px)
status: todo
owner: ui-dev
priority: P3
platform: all
depends_on: [T-085, T-086]
---

## Cíl
Ve widgetu Název tisku zobrazit náhled G-code z `IPrintPreviewSource` (T-086): velikost M = náhled 40 px + název + čipy, L = náhled 72 px + název + čipy + odhad sliceru. Bez náhledu widget vypadá jako dnes (T-085), bez prázdného rámečku (nebo podle návrhu T-076).

## Rozsah
- Dekódování PNG v App (`Avalonia.Media.Imaging.Bitmap` ze streamu, mimo UI vlákno), škálování na cílovou velikost s ohledem na DPI, uvolnění bitmapy při změně úlohy / zavření okna.
- Požadavek na náhled jen při viditelném widgetu ve velikosti M / L; malý (`s`) nebo velký (`l`) náhled podle ověřených rozměrů z T-086 a velikosti / DPI.
- Poškozený PNG = bez náhledu, bez výjimky do UI.
- `AutomationProperties.Name` náhledu podle návrhu.

## Akceptační kritéria
- [ ] App.Tests: výběr velikosti, žádný požadavek při skrytém widgetu / velikosti S, uvolnění bitmapy, poškozená data
- [ ] Headless UI testy: M a L s náhledem a bez náhledu, náhled zůstane po dokončení / zastavení tisku, začátek dalšího tisku ho vymění; snímky reference
- [ ] Ruční / E2E kontrola se simulátorem (T-086) – zapsaná do T-093
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Rozhodnutí (uživatel, 2026-10-24)
- Náhled se zobrazuje **až do začátku dalšího tisku**, tedy i po dokončení nebo zastavení tisku a během vychladnutí. Drží se jen v paměti (T-086).

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
