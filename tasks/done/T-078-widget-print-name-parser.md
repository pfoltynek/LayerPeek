---
id: T-078
title: Hezčí název tisku – rozbor názvu souboru z PrusaSliceru v Core
status: done
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
- [x] `Parse` pro výchozí šablonu Prusa vrací název a všechny čipy; ověřeno na ≥ 15 názvech včetně okrajových případů výše
- [x] Velikost písmen: název bez velkých písmen → sentence case; název s velkým písmenem beze změny; model tiskárny není v názvu ani v čipech
- [x] Neznámý tvar nikdy nehodí výjimku a vrátí čitelný název bez čipů
- [x] Zdroj šablony názvu (URL do repozitáře profilů PrusaSliceru) zapsaný v poznámkách k implementaci a v `docs/api/prusalink.md`
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Rozhodnutí (uživatel, 2026-10-24)
- **Velikost písmen (Q1):** sentence case – jen první písmeno velké („Foldable book holder v1“). Název, který už obsahuje velké písmeno, zůstane, jak je.
- **Model tiskárny (Q2):** model tiskárny z názvu souboru se **vynechá**, čip nemá.

## Rozhodnutí (uživatel, 2026-10-24, po dokončení)
- **Přípony kopie a stažení (rozhodnutí 6):** přípony, které za šablonu sliceru přidá prohlížeč nebo Průzkumník Windows, se před rozborem odříznou, aby název dostal čipy: ` (1)`, ` (12)`, ` - Copy`, ` - Copy (2)`, české ` - kopie`, ` - kopie (2)`. Např. `…_MK4S_2h51m (1).bgcode` se rozpozná stejně jako `…_MK4S_2h51m.bgcode`.
  - U názvu bez šablony (bez čipů) zůstane název, jak je, kromě odříznutí přípony, a to jen když přesně odpovídá jednomu z těchto tvarů přímo na konci názvu (`benchy (1).gcode` → „Benchy“).
  - Implementace ve větvi `feature/widget-core-followups` (viz Poznámky k implementaci, poslední bod).

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- `Core/Printers/PrintFileNameInfo` (sealed record): `Title`, `Materials` (`IReadOnlyList<string>`, prázdný = neznámý; místo jednoho `Material?` kvůli víc materiálům), `LayerHeightMm?`, `NozzleDiameterMm?`, `SlicerEstimate?` (`TimeSpan`), `Extension` (malými písmeny s tečkou, prázdný = žádná známá přípona), `HasChips`, `Empty` a statická `Parse(string?)`. Rovnost porovnává i materiály po prvcích, takže widget může levně porovnat výsledek.
- **Zdroj šablony:** `output_filename_format` v https://github.com/prusa3d/PrusaSlicer-settings-prusa-fff/blob/main/PrusaResearch/2.5.13.ini (nejnovější bundle k 2026-10-24) a `short_time()` v https://github.com/prusa3d/PrusaSlicer/blob/master/src/libslic3r/src/libslic3r/utils.cpp. Varianty z profilů (tryska napevno, bez trysky, `HT0.6`, model s tečkou i podtržítkem `COREONE_INDX4T`) jsou v `docs/api/prusalink.md`.
- **Pravidla:** cesta (`/`, `\`) a přípony `.bgcode`, `.gcode`, `.bgc`, `.gco`, `.g` (bez ohledu na velikost písmen) se odříznou. Zprava: volitelný čas (`d`/`h`/`m`/`s` v tomto pořadí, každá část nejvýš 5 číslic), model tiskárny (1 segment, 2 segmenty, nebo žádný; velká ASCII písmena, číslice, `.` a `+`), materiály (velká písmena, číslice, `+ - .`, oddělené `,` nebo `;`), výška vrstvy `…mm`, volitelná tryska `…n` nebo `HT…`. Čísla s tečkou i čárkou, invariantně, jen v rozsahu (0; 10] mm. Šablona platí, jen když zbude neprázdný základ názvu; jinak (i samotný čas, např. `benchy_2h51m`) je titulkem celý název bez přípony a čipy nejsou.
- **Titulek:** `_`, `-`, bílé a řídicí znaky → jedna mezera, ořezání; bez velkého písmene se první znak zvětší (`ToUpperInvariant`, jen když je to malé písmeno, takže `3d…` nebo emoji zůstanou). Prázdný vstup nebo jen přípona → prázdný titulek (náhradní text řeší UI, T-085).
- **Výkon:** bez regexu, jen spany; titulek přes `stackalloc` / `ArrayPool`. Poslední výsledek drží jednopoložková statická cache (neměnný objekt, bezpečné mezi vlákny), opakované volání se stejným názvem nic nealokuje.
- **Testy:** `tests/LayerPeek.Core.Tests/Printers/PrintFileNameInfoTests.cs` – 22 názvů podle šablony (Core One, INDX, MK4S, MINI, XL multi-material, MK3.9S, HT, bez trysky, desetinná čárka, sekundy, cesty, velká přípona, části šablony, vlastní materiály, emoji, diakritika), 25 neznámých tvarů (null, prázdné, 8.3, jen přípona, neznámá přípona, chybná čísla, řídicí znaky, osamocený surrogate), invariance kultury (tr-TR, cs-CZ, de-DE), velmi dlouhé názvy, 5 000 náhodných vstupů bez výjimky (pevný seed), rovnost, cache a souběžné volání z více vláken (cache nesmí vrátit výsledek jiného názvu). E2E test úkol nepotřebuje (čistý parser v Core).
- Žádné nové balíčky (AOT a licence beze změny).
- **Doplněk po dokončení (rozhodnutí 6, větev `feature/widget-core-followups`):** po odříznutí cesty a přípony se z konce názvu odřízne **nejvýš jedna** přípona kopie: ` (N)` (mezera, 1–4 číslice v závorce), ` - Copy`, ` - kopie`, nebo ` - Copy (N)` / ` - kopie (N)`. Velikost písmen musí přesně sedět (tak je píše Windows). Platí pro šablonu i pro název bez šablony.
  - Zůstane beze změny: bez mezery před závorkou (`benchy(1)`), nečíselný nebo prázdný obsah závorky, víc než 4 číslice, přípona uprostřed názvu (`benchy (1) final`), jiná velikost písmen (`- copy`, `- Kopie`) a název, ze kterého by zbyly jen oddělovače titulku, tedy mezery, `_` a `-` (`(1).gcode`, `/usb/ (1).gcode`, `_ (1).gcode`). Titulek tak po odříznutí nikdy není prázdný. U `benchy (1) (1)` se odřízne jen poslední ` (1)`.
  - Jen spany bez alokace navíc, bez výjimek; jednopoložková cache se nemění (klíčem je původní název).
  - Testy `CopySuffixAfterTheTemplateIsIgnored` (7 názvů), `CopySuffixOfANameWithoutTheTemplateIsRemoved` (9), `TextResemblingACopySuffixIsKept` (17), `CopySuffixInsideTheTemplateIsNotRemoved`.
