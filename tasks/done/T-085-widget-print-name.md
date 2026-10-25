---
id: T-085
title: Widget Název tisku (hezčí název a čipy, bez náhledu G-code)
status: done
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
- Test `PrintFileNameInfoTests.RepeatedNameReusesLastResult` (T-078) ověřuje `Assert.Same` proti statické jednopoložkové cache. Jakmile widget volá `PrintFileNameInfo.Parse` během jiných testů, stane se nestabilním (zjištěno při hledání nestabilního testu 2026-10-25). V tomto úkolu ho upravit tak, aby nezávisel na tom, co parsují jiné souběžně běžící testy (např. kolekce bez paralelizace nebo ověření bez `Assert.Same`).

## Akceptační kritéria
- [x] App.Tests: velikosti S/M/L, čipy podle kultury cs/en/de, neznámý tvar, 8.3 název `BENCHY~1.BGC`, dlouhé názvy, viditelnost podle stavu, levné porovnání, přepnutí jazyka
- [x] Headless UI testy: widget ve všech velikostech v nejdelším jazyce bez přetečení; snímky reference
- [x] Nové texty ve všech 8 jazycích
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Rozhodnutí (uživatel, 2026-10-24)
- **Název:** sentence case, název s velkými písmeny zůstane, jak je. Model tiskárny se vynechá a čip nemá (T-078).
- **Zvýraznění:** klíčový údaj je název tisku (akcentová barva). Je to návrh architekta, designer ho potvrdí v T-076.
- **Po skončení tisku:** widget zůstane i po dokončení nebo zastavení tisku (např. během vychladnutí), dokud nezačne další tisk. Stejně se drží náhled G-code (T-086).

## Rozhodnutí (uživatel, 2026-10-25)
- **Název bez titulku:** když parser (T-078) vrátí prázdný titulek (název jen s příponou nebo oddělovači, např. `.bgcode`, `___.gcode`), widget ukáže **samotný název souboru bez složky** – tedy současné chování (`PrintNameFormat.Title`, test `NameWithoutATitleShowsTheFileName`). Žádný nový přeložený text.

## Otevřené otázky
Žádné.

## Poznámky k implementaci
- 2026-10-25, ui-dev. Kód v `App/Cameras/Overlay/Widgets/PrintNameWidget*` a `PrintNameCompactView*`, sdílené části `App/Cameras/Overlay/OverlayMiddleTrimming.cs` a `OverlayChipsPanel.cs`, registrace jedním řádkem v `Composition/OverlayWidgets.cs` (pořadí Časy → Název tisku; Stav a Teploty doplní T-082 / T-084). Popis v `docs/architecture.md` (Widgety v1).
- **Viditelnost:** `PrintNameWidgetViewModel.JobFileName` – tiskárna dosažitelná, úloha se známým názvem souboru a stav Tiskne, Pozastaveno, Vyžaduje zásah, Dokončeno nebo Zastaveno. Klient PrusaLink drží poslední úlohu u FINISHED / STOPPED, takže název zůstane až do dalšího tisku (pak se ukáže nový název). Připravena, Zaneprázdněna, Chyba, Nedostupná, Neplatné údaje a Neznámý stav = skryto, i kdyby snapshot úlohu nesl (Chyba po tisku podle návrhu „během úlohy a po dokončení nebo zastavení“ nepatří).
- **Obsah a výkon:** `PrintNameContent` = název souboru + výsledek `PrintFileNameInfo.Parse`. Widget si pamatuje poslední rozebraný název (nezávisle na statické cache parseru) a rozebírá jen při jeho změně (`ParseCount` v testech). Porovnání obsahu: řetězec ordinálně + výsledek rozboru odkazem, takže dotaz téže úlohy (i nová instance řetězce, jiný průběh, dokončení) nealokuje a nic neposílá na UI vlákno (`PrintNameWidgetAllocationTests` v kolekci `AllocationSensitive`, 0 B na 1000 dotazů). Texty se formátují jen při změně názvu a po přepnutí jazyka.
- **Texty (`PrintNameFormat`):** čísla podle regionu (`CurrentCulture`, „0,2 mm“ / „0.2 mm“, formát `0.###`, bez koncových nul), slova podle jazyka aplikace. Čip materiálu: materiály spojené „, “, nejvýš 16 znaků + „…“ (bez oddělovače na konci); čip vrstvy s ikonou `Layer` 12 („≋ 0,2 mm“), čip trysky „tryska 0,4“ (`CameraOverlayNozzleChip`); odhad sliceru zaokrouhlený nahoru na minuty (`CameraOverlaySlicerEstimate` + sdílené `OverlayFormat.Duration`). Jméno pro čtečku obsahuje celý název, všechny materiály a všechny známé údaje (i v S a i když čip zmizel).
- **Vzhled podle T-076 2.4:** S = ikona `Cube` 16 + název 13 SemiBold (výška 36); M = název 14 SemiBold / 20 → 4 → čipy 20 (výška 68); L = název 16 SemiBold / 22 až na 2 řádky → 6 → čipy 22 → 6 → odhad 13 Secondary (výška 102, s dvouřádkovým názvem 124). Šířky S max 280, M 240–420, L 300–480 (min. šířky v definici pro pravidlo kolize). Název je zvýrazněný údaj (`emphasized`). Sloupec 0 mřížky karty je rezervovaný pro náhled G-code (T-087); bez náhledu se nic nekreslí a nic se neposune.
- **Zkracování:** S a M jedna řádka zkrácená uprostřed podle šířky (`OverlayMiddleTrimming`: prvních 14 znaků + „…“ + konec, který se vejde; vlastní `TextTrimming` nad `TextLeadingPrefixCharacterEllipsis` z Avalonie 12.1.4, bez reflexe). Je to pravidlo T-036 „začátek + … + konec“ podle šířky místo počtu znaků; 14 znaků ≈ polovina nejužší řádky názvu (M, 212 DIP při 14 SemiBold). Slovo delší než karta se zkrátí na konci. L: 2 řádky a pak zkrácení na konci (`CharacterEllipsis`). Čipy se nezalamují ani nezkracují: `OverlayChipsPanel` vynechá zprava ty, které se nevejdou (tryska → vrstva → materiál), a umístí je za svůj ořezaný pravý okraj.
- **Kompaktní podoba (T-090):** `PrintNameCompactView.RowTemplate` nad stejným view modelem: ikona `Cube` 16, název 13 SemiBold zkrácený uprostřed, čipy materiálu a vrstvy výšky 18 (zprava mizí podle místa). Pravidla pásu (výška 34, odsazení 10, prahy šířky okna 320 / 400 pro čipy, skrytí bez úlohy) dodá T-090.
- **Nové texty (8 jazyků):** `CameraOverlayNozzleChip`, `CameraOverlayLayerHeightName`, `CameraOverlayNozzleDiameterName`, `CameraOverlaySlicerEstimate` (es „estimación del slicer {0}“ kvůli limitu 32 znaků; it „altezza layer“ podle terminologie Prusy).
- **Nestabilní test T-078:** `RepeatedNameReusesLastResult` přesunut do nové třídy `PrintFileNameInfoCacheTests` v kolekci `PrintFileNameCache` s `DisableParallelization = true` (běží sama po všech paralelních testech sestavy), přidán test `AnotherNameReplacesTheCachedResult`. Widget sám statickou cache nepotřebuje.
- **Testy:** App.Tests `PrintNameWidgetTests` (registrace, stavy, další tisk po dokončení / zastavení, opakovaný tisk téhož souboru, neznámý tvar, 8.3 název, název bez titulku, přípona kopie, šablona bez trysky a odhadu, víc materiálů, dlouhý seznam materiálů, sekundy v odhadu, dlouhý název, čipy cs/en/de, milimetry, přepnutí jazyka, texty v 8 jazycích s délkami, rozbor jen při změně, levné porovnání, velikosti, zvýraznění) a `PrintNameWidgetAllocationTests`. Headless `CameraWindowPrintNameWidgetUiTests`: části, písma, barvy a ikony S/M/L (kontrola skutečně vykreslených `TextBlock`), výšky 36 / 68 / 102 / 78 / 50 / 124, název bez šablony a bez odhadu, uložená změna velikosti, viditelnost po tisku a skrytí bez úlohy, zvýraznění, zkrácení uprostřed v S/M, 2 řádky v L, slovo delší než karta, mizení čipů zprava, nejdelší texty v 8 jazycích × S/M/L, přepnutí jazyka, kompaktní řádek; nové snímky `camera-widget-print-name-s/m/l.png` (720 × 405, vizuálně zkontrolované). E2E testy žádné (celý průchod T-093).
- Ručně neověřeno: skutečné DPI 125–200 %, macOS (SF Pro), čitelnost na skutečném videu, skutečný název z tiskárny přes simulátor (aplikace se nespouštěla, aby se nedotkla uživatelského nastavení).
- **Review (qa-reviewer, 2026-10-25):** opraveny dva nálezy (testy napřed, headless testy čtou skutečně vykreslenou řádku).
  1. `TextLeadingPrefixCharacterEllipsis` z Avalonie 12.1.4 skládá konec zkráceného řádku chybně, když má řádek víc běhů (emoji, CJK, cyrilice – jiné písmo nebo jiné písmo Unicode): po běhu, který se vejde jen zčásti, přidává ještě kousky předchozích běhů, takže vykreslí text, který na konci názvu není („…e 打印 v2“ místo „…打印 v2“, „… 🚀🚀 v2“). `OverlayMiddleTrimming` teď výsledek Avalonie projde a z konce nechá jen část souvislou s koncem řádku (běhy se hledají v textu podle paměti přes `MemoryMarshal.TryGetString`, bez reflexe). Začátek (14 kódových jednotek zaokrouhlených na konec shluku glyfů) i konec drží celé znaky (náhradní páry, kombinující diakritika, ZWJ emoji). Test `MiddleShorteningKeepsWholeCharacters` (9 názvů × šířky 40–420).
  2. Čipy vynechané zprava byly dál ve stromu přístupnosti (čtečka by přečetla nevykreslený čip). `OverlayChipsPanel` má vlastní automation peer, jehož potomci jsou jen vykreslené čipy; při změně se strom zneplatní. Ověřeno v `ChipsThatDoNotFitAreLeftOutFromTheRight`.
  - Text čipu materiálu se krátí po kódových jednotkách, ale parser připouští jen ASCII materiály, takže to nevadí.
- **Potvrzovací review (qa-reviewer, 2026-10-25):** bez nových nálezů v `OverlayMiddleTrimming` ani `OverlayChipsPanel`.
  - Ověřen i text zprava doleva (hebrejština, arabština – na konci, na začátku i celý název): test `MiddleShorteningKeepsWholeCharacters` teď kontroluje vykreslenou řádku v logickém pořadí (`DrawnLine.AssertMiddleShortened`; běhy řádku jsou v pořadí zobrazení, takže text zprava doleva je v nich obrácený): právě jedno „…“, začátek od začátku textu, konec souvislý až do konce textu, celé znaky.
  - Stejná chyba Avalonie byla i ve widgetu Stav (jméno tiskárny s `PrefixCharacterEllipsis`, např. „Prusa CO… ержатель книг на полке v2“). Jméno teď používá `OverlayMiddleTrimming.PrinterName` (začátek `OverlayFormat.NameHeadLength` = 8, stejný jako zkrácení textu na 40 znaků). Test `StatusWidgetUiTests.ShortenedNameEndsWithTheRealEndOfTheName` (8 jmen s emoji, CJK, cyrilicí a hebrejštinou × M/L). Snímky widgetu Stav se nezměnily.
  - Pomocné kontroly vykreslené řádky jsou sdílené v `tests/LayerPeek.App.UiTests/Infrastructure/DrawnLine.cs`.
