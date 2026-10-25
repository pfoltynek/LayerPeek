---
id: T-087
title: Widget Název tisku – zobrazení náhledu G-code (M 40 px, L 72 px)
status: done
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
  - Pozor (T-086, `docs/api/prusalink.md`): `s` je PNG přesně 16 × 16 px a standardní profily Prusa ho mají jen jako QOI, takže skutečná tiskárna na `/thumb/s` běžně vrací `404`. Použitelný je prakticky jen `Large` (380 × 285 px, zmenšit v App). Simulátor `s` obsluhuje jen kvůli testům.
- `GetAsync` volat s úlohou z **aktuálního** snapshotu tiskárny a `Observe` volat ze `SnapshotUpdated` v pořadí snapshotů. Úloha, kterou `Observe` už viděl nahrazenou (zastaralý snapshot), dostane `null` a nic se nestáhne (pravidlo T-086 „nikdy zpět ke starší úloze“); widget s takovou úlohou nemá čekat na náhled, ale na další snapshot.
- Poškozený PNG = bez náhledu, bez výjimky do UI.
- `AutomationProperties.Name` náhledu podle návrhu.

## Akceptační kritéria
- [x] App.Tests: výběr velikosti, žádný požadavek při skrytém widgetu / velikosti S, uvolnění bitmapy, poškozená data
- [x] Headless UI testy: M a L s náhledem a bez náhledu, náhled zůstane po dokončení / zastavení tisku, začátek dalšího tisku ho vymění; snímky reference
- [x] Ruční / E2E kontrola se simulátorem (T-086) – zapsaná do T-093
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Rozhodnutí (uživatel, 2026-10-24)
- Náhled se zobrazuje **až do začátku dalšího tisku**, tedy i po dokončení nebo zastavení tisku a během vychladnutí. Drží se jen v paměti (T-086).

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- 2026-10-25, ui-dev. Kód v `App/Previews/` (nové) a ve widgetu `App/Cameras/Overlay/Widgets/PrintNameWidget*`. Drobné doplnění: `OverlayWidgetContext.Thumbnails`, poslední volitelný parametr `CameraViewModel` (`thumbnails`), registrace v `Composition/AppServices.cs` a `Composition/OverlayWidgets.cs` (widget dostává kontext). Popis v `docs/architecture.md` (oddíl Náhled G-code).
- **Zapojení cache (T-086):**
  - `PrintPreviewCache` je singleton nad `PrusaLinkPreviewFetcher`; při ukončení ho uvolní kontejner služeb.
  - `PrintPreviewObserver` je pozorovatel monitoringu (`IMonitoringObserver`, v `MonitoringSession` hned za historií telemetrie). Ze `SnapshotUpdated` volá `Observe` v pořadí, v jakém monitor snapshoty vydává. `RetainOnly(printers)` volá při každém spuštění monitoringu (`ApplySettings`) i při uložení bez restartu (`UpdateSettings`). Výpadek monitoringu náhledy nechá. `Dispose` odhlásí odběr.
- **Dekódování a sdílení (`PrintThumbnails`, jedna instance pro aplikaci):**
  - Bere se jen `PrintPreviewSize.Large`. `/thumb/s` vrací u standardních profilů `404` (16 × 16 jen jako QOI). Test `RequestAsksForTheLargePreviewOfTheGivenJobAndDecodesItToTheSize`.
  - PNG se dekóduje mimo UI vlákno i vlákno monitoru (`Task.Run`, `AvaloniaPrintThumbnailDecoder`: `Bitmap.DecodeToWidth/Height`, `HighQuality`). Dekóduje se rovnou na delší stranu = velikost karty × škálování monitoru (M 40 → 40 × 30 px při 100 %, 80 × 60 při 200 %; L 72 → 72 × 54, 108 × 81 při 150 %). Celých 380 × 285 px se nikde nedrží. Menší náhled se nezvětšuje.
  - Požadavky na stejný náhled (cache vrací pro úlohu stejnou instanci) a stejnou velikost sdílí jednu bitmapu s počítáním odkazů. Bitmapa se uvolní s posledním požadavkem. Když je ještě rozdekódovaná, uvolní se po dokončení dekódování.
  - Aktualizace jdou vždy přes `IUiDispatcher.Post` (nikdy přímo z volání) a po zrušení požadavku už nepřijdou.
  - Bez náhledu (404, přechodná chyba, timeout) = `null`. Požadavek se pak sám zeptá znovu po `RetryDelay` cache (30 s), dokud žije. U 404 je to jen dotaz do paměti, nic se nestahuje. Poškozené PNG (projde jen kontrola hlavičky) = `null`, zaloguje se jen typ výjimky (Debug) a znovu se nedekóduje.
- **Widget:**
  - Obsah `PrintNameContent` nese navíc úlohu z aktuálního snapshotu, definici tiskárny a to, zda se karty kreslí (ne v malém náhledu). Úloha se porovnává podle identity (`Id` + `PreviewReference`), tiskárna hodnotou s rychlou cestou přes odkaz. Dotaz téže úlohy tak dál nealokuje a nic neposílá na UI vlákno (`PollOfTheSameJobWithItsThumbnailAllocatesNothing`, 0 B na 1000 snapshotů, jeden požadavek).
  - Nový tisk téhož souboru (jiné `Id`) teď jednou pošle aktualizaci na UI vlákno, aby se náhled nové úlohy načetl znovu. Texty se nemění a název se znovu nerozebírá. Test T-085 `ReprintOfTheSameFileKeepsTheShownTexts` je upravený: místo „žádný post“ ověřuje „žádná změna textů, `ParseCount` 1“.
  - O náhled se žádá jen na UI vlákně, když se karta ukazuje ve velikosti M / L v normálním okně nebo na celé obrazovce a úloha má odkaz na náhled. Znovu jen při změně úlohy, definice tiskárny, vykreslené velikosti nebo škálování (`PrintNameWidgetView` předává `TopLevel.RenderScaling` a reaguje na `ScalingChanged`).
  - V S, v malém náhledu, se skrytou kartou (nečinná / offline tiskárna) a po zavření okna se požadavek zruší a bitmapa uvolní. Po návratu (např. krátký výpadek spojení) se náhled vezme znovu z paměti cache, bez stažení.
  - Další úloha starý náhled hned odstraní (nepatří k novému tisku) a nový se objeví po dekódování. Jiná velikost nebo škálování téže úlohy (a uložené nastavení tiskárny) nechá starý náhled, dokud nepřijde nový, takže nebliká.
  - Náhled zůstane po dokončení i zastavení tisku (úloha zůstává) až do další úlohy.
- **Vzhled podle T-076 2.4:**
  - Sloupec 0 mřížky: `Border` 40 × 40 (M) / 72 × 72 (L), poloměr 6 / 8, mezera k textu 10 / 12, pozadí `#14FFFFFF`, `ClipToBounds`, uvnitř `Image` `Stretch=Uniform` s `HighQuality`.
  - Zarovnání nahoru k řádku názvu (v ASCII návrhu začíná náhled na řádku názvu). Výšky karet zůstávají 68 / 102.
  - Bez náhledu (žádný v souboru, ještě se načítá, poškozený, S) se sloupec nevykreslí vůbec. Žádný zástupný rámeček ani ikona, karta je stejná jako v T-085, jak říká návrh („žádný prázdný rámeček, žádné poskočení“).
  - `AutomationId` `CameraOverlayWidget-print-name-Thumbnail`, jméno pro čtečku „Náhled tisku“ (`CameraOverlayThumbnailName`, nový text v 8 jazycích: Print preview, Druckvorschau, Vista previa de la impresión, Aperçu de l'impression, Anteprima di stampa, Podgląd wydruku, Náhľad tlače). Jméno karty (`AccessibleName`) se nemění.
- **Testy:**
  - App.Tests `Previews/PrintThumbnailsTests` (16): velikost `Large`, úloha od volajícího, dekódování neblokuje, sdílení mezi okny i souběžnými požadavky, jiné rozměry zvlášť, uvolnění s posledním požadavkem, pozdní bitmapa po zrušení, zahozená aktualizace po zrušení, zrušení během stahování, bez náhledu, poškozené PNG, opakování po 30 s, žádné opakování po zrušení, další úloha, argumenty.
  - App.Tests `Previews/PrintPreviewObserverTests` (6): `Observe` v pořadí snapshotů se skutečnou cache (starší úloha se nestáhne), `RetainOnly` při `ApplySettings` i `UpdateSettings`, změna adresy, výpadek monitoringu, odhlášení odběru.
  - App.Tests `Cameras/Overlay/Widgets/PrintNameWidgetThumbnailTests` (22 metod, 27 případů): M / L × škálování 1–2 (40 / 72 / 50 / 108 / 80 / 144 px), S, malý náhled, skrytá karta, úloha bez odkazu, bez služby, porovnání obsahu podle identity úlohy, jeden požadavek na úlohu, náhled po dokončení / zastavení, výměna další úlohou, nový tisk téhož souboru, změna velikosti a škálování, uložené nastavení tiskárny, chybějící a poškozený náhled, dvě okna = jedno dekódování, zavření oken, zavření během dekódování, jméno pro čtečku v 8 jazycích.
  - Dále `PrintNameWidgetAllocationTests.PollOfTheSameJobWithItsThumbnailAllocatesNothing` a `AppServicesTests.PreviewCacheIsWiredToMonitoringAndToTheCameraWindows`.
  - Headless `CameraWindowPrintThumbnailUiTests` (7 metod, 13 případů, skutečný dekodér Avalonie): velikost, poloměr, pozadí, `Uniform`, mezera, zarovnání a výška karty v M / L; dekódované pixely při škálování 1 / 1,5 / 2; S bez náhledu; bez náhledu i s poškozeným PNG karta jako v T-085; náhled zůstane po dokončení / zastavení a další tisk ho vymění; jméno pro čtečku. Nové snímky `camera-widget-print-name-thumbnail-m/l.png` (720 × 405, vizuálně zkontrolované). Stávající snímky T-085 se nezměnily.
  - Test se simulátorem jsem nepřidal: klient i cache proti simulátoru už pokrývá T-086 a celý průchod aplikací je v T-093. Kontrolu jsem do T-093 zapsal (rozsah, bod „Náhled G-code ve widgetu Název tisku“). E2E testy žádné.
- **Ověření:** `dotnet build` 0 varování, `dotnet test` celé řešení zelené (6947 testů, 117 přeskočených podle prostředí, např. E2E bez systémových zásahů). Aplikace spuštěná v izolovaném testovacím režimu (`LAYERPEEK_TEST_DATA_DIR` v dočasné složce, bez tiskáren) běžela 15 s bez chyby v logu; produkční režim se nespouštěl.
- **Ručně neověřeno:**
  - skutečný náhled z tiskárny a ze simulátoru v běžící aplikaci (T-093),
  - skutečné DPI 125–200 % a přesun okna mezi monitory s jiným DPI (pokryto jen headless testy se `SetRenderScaling`),
  - macOS (Avalonia / Skia stejné, neověřeno na Macu),
  - čtečka obrazovky (Narrator) a strom UI Automation u `Border` s náhledem.
- **AOT:** žádný nový balíček, žádná reflexe, kompilované bindingy, logování přes `[LoggerMessage]`. Lokální AOT publish jsem nespouštěl (T-093).

## Review
- 2026-10-25, qa-reviewer. Bez blokujících nálezů. Zkontrolováno: počítání odkazů a uvolnění bitmap (jednou, i po zrušení během dekódování), vlákna (dekódování na thread poolu, aktualizace přes `Post`), zapojení cache (`Observe` v pořadí snapshotů, `RetainOnly`, uvolnění kontejnerem), dekódování nedůvěryhodného PNG (512 KB + hlavička ≤ 2048 px = nejvýš 16 MB dočasně), DPI, nulové alokace, upravený test T-085 (stále smysluplný), snímky, AOT.
- Opraveno: `SettleAsync` v `PrintNameWidgetThumbnailTests` čekal na „40 ms bez nového postu“, takže na vytíženém thread poolu mohl skončit dřív, než dekódování doběhlo (nestabilní test). Nově `PrintThumbnails.BusyCount` (interní, pro testy: požadavky, které stahují nebo dekódují) a testy čekají na tento stav. Stejně v `PrintThumbnailsTests` a ve dvou headless testech (místo pevných `Task.Delay(50/100)`). Nový test `RequestIsBusyOnlyWhileFetchingOrDecoding`.
- Ke zvážení (neblokuje): ověřit v Narratoru, že `Border` s náhledem je ve stromu UI Automation jako obrázek (případně `AutomationProperties.ControlTypeOverride`) – zapsáno už v „Ručně neověřeno“.
