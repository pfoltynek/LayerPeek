---
id: T-078
title: Hezčí název tisku – rozbor názvu souboru z PrusaSliceru v Core
status: todo
owner: core-dev
priority: P2
platform: all
depends_on: []
---

## Cíl
Z názvu tiskového souboru udělat čitelný název a čipy pro widget Název tisku (T-085): např. `foldable_book_holder_v1_0.4n_0.2mm_PLA_COREONE_2h51m.bgcode` → název „Foldable book holder v1“ + materiál `PLA`, výška vrstvy `0.2 mm`, tryska `0.4 mm` (+ odhad sliceru `2h51m` pro velikost L; model tiskárny `COREONE` se z názvu vynechá a čip nemá). Neznámý tvar názvu se zobrazí rozumně bez čipů.

## Kontext
- Název souboru: `PrintJob.FileName` (PrusaLink: `file.display_name` z `GET /api/v1/job`, viz `docs/api/prusalink.md`).
- **Metadata souboru PrusaLink na tiskárnách Buddy neposílá:** specifikace OpenAPI (https://raw.githubusercontent.com/prusa3d/Prusa-Link-Web/master/spec/openapi.yaml) sice popisuje `file.meta` (`layer_height`, `filament_type`, `nozzle_diameter`, `estimated_print_time`…), ale firmware Buddy v `get_job_v1` (https://github.com/prusa3d/Prusa-Firmware-Buddy/blob/master/lib/WUI/link_content/basic_gets.cpp) vrací jen `refs`, `name`, `display_name`, `path`, `size`, `m_timestamp`. Jediným zdrojem čipů je proto název souboru.
- Výchozí šablona názvu v PrusaSliceru (`output_filename_format`) pro profily Prusa: `{input_filename_base}_{nozzle_diameter[0]}n_{layer_height}mm_{printing_filament_types}_{printer_model}_{print_time}.gcode` – přesný tvar ověřit v profilech PrusaSliceru (https://github.com/prusa3d/PrusaSlicer-settings-prusa-fff, soubor `PrusaResearch.ini`) a zdroj zapsat do poznámek.

## Rozsah
- `Core/Printers/PrintFileNameInfo` (nebo podobně): `Title`, `Material?`, `LayerHeightMm?`, `NozzleDiameterMm?`, `SlicerEstimate?` (`TimeSpan`), `Extension`; statická `Parse(string fileName)` bez výjimek a bez regexu s reflexí (zdrojově generovaný `[GeneratedRegex]` je v pořádku).
- Pravidla: odstranit příponu (`.bgcode`, `.gcode`, `.bgc`, `.g`… – bez ohledu na velikost písmen), rozpoznat koncové segmenty v pořadí šablony (zprava), segment s modelem tiskárny (`COREONE`, `MK4S`, `MINI`, `XL`…) se rozpozná a z názvu vynechá; zbytek je název; `_` a `-` v názvu → mezery; **velikost písmen:** název bez velkých písmen dostane velké první písmeno (sentence case, „foldable book holder v1“ → „Foldable book holder v1“), název, který už nějaké velké písmeno obsahuje, zůstane beze změny (např. „MyPart Final“); víc materiálů (`PLA,PETG` / `PLA;PETG`); čas sliceru ve tvarech `2h51m`, `51m`, `1d2h3m`, `42m42s`; desetinná čárka i tečka.
- Nerozpoznané koncové segmenty → zůstanou součástí názvu; zkrácené 8.3 názvy (`BENCHY~1.BGC`) a prázdné / jen přípona → název = vstup bez přípony, bez čipů.
- Formát čipů pro zobrazení (jednotky, desetinný oddělovač podle kultury) dělá App (T-085), Core vrací čísla.
- Testy s tabulkou skutečných názvů (Core One, MK4S, MINI, XL multi-material, vlastní názvy uživatelů, diakritika, velmi dlouhé názvy, názvy jen s částí šablony).

## Akceptační kritéria
- [ ] `Parse` pro výchozí šablonu Prusa vrací název a všechny čipy; ověřeno na ≥ 15 názvech včetně okrajových případů výše
- [ ] Velikost písmen: název bez velkých písmen → sentence case; název s velkým písmenem beze změny; model tiskárny není v názvu ani v čipech
- [ ] Neznámý tvar nikdy nehodí výjimku a vrátí čitelný název bez čipů
- [ ] Zdroj šablony názvu (URL do repozitáře profilů PrusaSliceru) zapsaný v poznámkách k implementaci a v `docs/api/prusalink.md`
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Rozhodnutí (uživatel, 2026-10-24)
- **Velikost písmen (Q1):** sentence case – jen první písmeno velké („Foldable book holder v1“). Název, který už obsahuje velké písmeno, zůstane, jak je.
- **Model tiskárny (Q2):** model tiskárny z názvu souboru se **vynechá**, čip nemá.

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
