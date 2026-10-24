---
id: T-034
title: Náhled obrazu kamery na záložce Kamera v nastavení
status: done
owner: ui-dev
priority: P2
platform: all
depends_on: [T-033]
---

## Cíl
Při nastavování kamery uživatel po kliknutí na **Otestovat** vidí snímek z kamery, aby poznal, že adresa patří správné kameře a že obraz je v pořádku.

## Současný stav (2026-10-19)
- Tlačítko **Otestovat** (`PrinterEditorViewModel.TestCameraCommand`) volá `ICameraTester.TestAsync` → `CameraSnapshotTester` → `CameraSnapshot.CaptureAsync` (Core). Ten vrátí `CameraImage` (BGRA kopie prvního snímku), ale tester z něj předá jen `CameraTestResult(Outcome, Width, Height, Reason)`. Obraz se zahodí.
- Timeout testu 15 s, test jde zrušit, ruší ho i změna adresy nebo zavření okna.
- UI testy mají falešný `ICameraFeedFactory` s jednobarevnými snímky (`SendColor`) a pomocníky pro čtení pixelů (`Infrastructure/Ui.cs`).

## Rozhodnuto
- **Druh náhledu (2026-10-19):** jen jednorázový snímek z tlačítka Otestovat. Žádné tlačítko Obnovit, žádný živý náhled.
- **Kdy (2026-10-19):** jen po kliknutí na Otestovat, nikdy automaticky při otevření podzáložky.
- **Velikost (2026-10-19):** šířka panelu, výška nejvýš ≈ 180 DIP, poměr stran zachován, bez zvětšení po kliknutí.
- **Smazání (2026-10-19):** změna adresy i neúspěšný test (timeout, chyba) náhled smaže.
- **Překryv (2026-10-19):** přes náhled se překryv stavu tisku nezobrazuje.
- **Rozlišení se nezalamuje (2026-10-19, po revizi):** v `SettingsCameraTestSuccess` (a v každém dalším textu s rozlišením) pevné mezery kolem „ד a před „px“ ve všech 8 jazycích, aby se „1920 × 1080 px“ nikdy nezalomilo.
- **Přístupný název náhledu (2026-10-19, po revizi):** překlady `SettingsCameraPreview` schválené tak, jak byly navržené (viz poznámky).

## Rozsah
- Rozšířit výsledek testu o obraz (např. `CameraTestResult.Image` typu `CameraImage`), `CameraSnapshotTester` ho předá.
- `PrinterEditorViewModel`: vlastnost náhledu (obraz nebo nic). Nastaví se po úspěchu; smaže se při změně adresy, při novém testu, po timeoutu/chybě. Případné další smazání (přepnutí tiskárny, zavření okna) podle uvolnění prostředků – bitmapa nesmí zůstat viset po zavření okna.
- Na podzáložce Kamera `Image` (`Stretch=Uniform`, šířka panelu, `MaxHeight≈180`) pod výsledkem testu; bez obrazu se nezobrazuje nic (nebo zástupný prvek podle návrhu UI z T-033).
- Převod `CameraImage` → `WriteableBitmap` jednou na výsledek, bitmapa se uvolní při smazání náhledu.
- Adresa jde do logu jen přes `UriRedaction` (beze změny pravidel T-014).
- Rozložení podle schváleného návrhu UI z T-033, bez posuvníku.
- Bez nového NuGet balíčku.

## Akceptační kritéria
- [x] Po úspěšném testu se na podzáložce Kamera zobrazí snímek se zachovaným poměrem stran, šířka panelu, výška ≤ 180 DIP (podle schváleného návrhu T-033: rámeček 320 × 180 DIP vlevo od tlačítka a výsledku, viz poznámky)
- [x] Testy view modelu (App.Tests, falešná továrna, `FakeTimeProvider`): úspěch nastaví náhled; timeout a chyba náhled smažou; změna adresy náhled smaže; nový test starý náhled smaže; zrušení testu; žádné visící spojení (dispose feedu ověřen); zavření okna uvolní bitmapu
- [x] `CameraSnapshotTesterTests` rozšířené o předání obrazu
- [x] Headless UI test: červený snímek z falešné kamery je po kliknutí na Otestovat vidět na podzáložce Kamera (kontrola pixelů), letterbox odpovídá poměru stran; po změně adresy zmizí
- [x] Nový snímek reference podzáložky Kamera s náhledem; test „bez posuvníku“ z T-033 projde ve všech 8 jazycích (i s náhledem)
- [x] Případné nové texty ve všech 8 jazycích, test přepnutí jazyka
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné; ruční ověření se skutečnou kamerou zůstává na uživateli – viz poznámky, úkol proto zůstává v `in-progress`)

## Poznámky k implementaci
**Stav 2026-10-19 (ui-dev):** implementováno, všechna kritéria splněná; chybí jen ruční ověření s kamerou Buddy3D (`rtsp://192.168.0.227/live`).

**Tok dat**
- `CameraTestResult` má nový parametr `Image` (`CameraImage`, jen při úspěchu). `CameraSnapshotTester` ho předá; timeout, chyba ani zrušení obraz nevrací.
- `PrinterEditorViewModel.CameraPreview` (`IImage?`) + `HasCameraPreview`. Obraz se převede **jednou na výsledek** na vlákně UI (pokračování příkazu `TestCameraCommand`) přes `ICameraPreviewRenderer`; produkční `BitmapCameraPreviewRenderer` kopíruje BGRA do nového `WriteableBitmap` (Bgra8888, neprůhledný, 96 DPI; stejný kód kopírování jako okno kamery). Bez reflexe, kompatibilní s AOT (publikace bez varování).
- View model náhled vlastní: při nahrazení/smazání nejdřív oznámí změnu (obrázek ho přestane zobrazovat), pak bitmapu uvolní (`Dispose`).
- Text výsledku (pro přepnutí jazyka) si drží kopii výsledku **bez** obrazu, aby pixely nezůstaly v paměti.
- Když vykreslení selže (např. bez grafické platformy), test zůstává úspěšný, jen bez náhledu; selhání se zapíše do logu (varování s typem a zprávou výjimky, nikdy adresa – renderer adresu nezná). Logger dostává `SettingsViewModel` z DI (`ILogger<SettingsViewModel>`), zprávy jsou v `SettingsLog`.
- Kopírování BGRA řádků (rychlá cesta při stejném `RowBytes`, jinak po řádcích do zarovnaných řádků bitmapy) je jediný sdílený pomocník `Cameras/BgraPixels` – používá ho okno kamery i náhled v nastavení.

**Kdy se náhled smaže (podle Rozhodnuto)**
- změna adresy; začátek každého nového testu (i odmítnutého kvůli neplatné adrese) – proto po timeoutu, chybě, výjimce testeru i po zrušení náhled není;
- odebrání tiskárny a zavření okna (`Detach` → bitmapa uvolněna);
- obraz, který dorazí až po zrušení / zavření okna, se vůbec nevykreslí.
- **Nesmaže** se: přepnutím jazyka, vypnutím kamery, uložením, přepnutím na jinou tiskárnu (každá tiskárna si do zavření okna drží svůj poslední snímek; bitmapa má plné rozlišení kamery, např. 1920 × 1080 ≈ 8 MB, uvolní se nejpozději při zavření okna). Vypnutí kamery není v Rozhodnuto mezi důvody ke smazání, proto náhled zůstává.

**Rozložení**
- Podle schváleného návrhu T-033: rámeček **320 × 180 DIP** (černé pozadí jako okno kamery, `Image Stretch=Uniform`, `BitmapInterpolationMode=HighQuality`) vlevo, mezera 16, vpravo tlačítko Otestovat kameru / Zrušit test a výsledek. Bez snímku se rámeček nezobrazuje a místo se nerezervuje (tlačítko začíná u levého okraje). Úkol píše „šířka panelu“ – zadání úkolu (2026-10-19) určilo dodržet návrh T-033, tedy 320 DIP.
- Poměr stran: 16:9 vyplní rámeček celý, 4:3 má černé pruhy vlevo a vpravo (40 DIP), širší obraz nahoře a dole.
- Bez posuvníku: obsah podzáložky Kamera s náhledem a výsledkem měří **319 z 358 DIP** ve všech 8 jazycích (shodně s měřením návrhu T-033). Stav „hláška o odstraněných údajích + náhled“ nenastane (úspěšný test hlášku skryje, T-033 Q3) – test to ověřuje.
- Přes náhled se nic nepřekrývá.

**Přístupnost a texty**
- `AutomationProperties.Name` obrázku = nový text `SettingsCameraPreview` ve všech 8 jazycích (en „Camera snapshot from the test“, cs „Snímek z testu kamery“, sk „Snímka z testu kamery“, de „Kamerabild aus dem Test“, es „Imagen de la cámara de la prueba“, fr „Image de la caméra issue du test“, it „Immagine della telecamera dal test“, pl „Obraz z kamery z testu“). AutomationId: `PrinterCameraPreviewFrame` (rámeček), `PrinterCameraPreview` (obrázek).

**Testy**
- `CameraSnapshotTesterTests`: obraz se vrací jako vlastní kopie prvního snímku (pixely ověřené, další snímek ho nezmění); timeout, chyba a konec streamu obraz nevrací; zrušení beze změny.
- `CameraPreviewTests` (App.Tests, skutečný `CameraSnapshotTester` nad falešnou továrnou a `FakeTimeProvider`, falešný renderer počítající `Dispose`): 22 testů – úspěch, timeout, chyba, výjimka testeru, změna adresy, nový test, odmítnutý test, zrušení, pozdní obraz po zrušení i po zavření okna, zavření okna, odebrání tiskárny, přepnutí tiskárny, přepnutí jazyka, vypnutí kamery + uložení, úspěch bez obrazu, selhání vykreslení; u streamu ověřené uzavření feedu.
- Headless UI (`SettingsWindowCameraUiTests`): červený snímek po kliknutí na Otestovat – kontrola vykreslených pixelů v rámečku 320 × 180, letterbox 4:3 (černé boční pruhy) i 32:9 (pruhy nahoře a dole), zmizení po změně adresy a po neúspěšném testu, nic se nerezervuje před testem, lokalizovaný název obrázku a zachování obrázku po přepnutí jazyka (cs), uvolnění při zavření okna.
- `SettingsWindowLayoutTests`: nové stavy „úspěšný test + náhled“ a „odstraněné údaje + úspěšný test + náhled“, ve všech 8 jazycích bez posuvníku.
- Nové referenční snímky `settings-camera-preview.png` (16:9, en) a `settings-camera-preview-4x3-de.png` (4:3, de); vizuálně zkontrolované. Ostatní snímky beze změny.
- Mutace (dočasně vypnuté mazání při změně adresy a `Dispose`) testy odhalily (9 selhání), kód vrácen.

**Úpravy po revizi (2026-10-19)**
- Pevné mezery (`&#xA0;`) v `SettingsCameraTestSuccess` ve všech 8 jazycích: „({0} × {1} px)“ se nezalomí. `StringsTests.ResolutionsUseNonBreakingSpaces` hlídá každý text všech jazyků (žádná běžná mezera vedle „ד ani před „px“ za číslem). Snímky `settings-camera-preview*.png` přegenerované a zkontrolované – rozlišení je celé na druhém řádku; ostatní snímky beze změny.
- Testy pořadí: změna `CameraPreview` se oznámí, dokud stará bitmapa ještě není uvolněná (smazání změnou adresy, nahrazení novým testem, zavření okna); uvolní se až potom.
- Selhání vykreslení se loguje (test na zprávu, úroveň, výjimku a nepřítomnost adresy); úspěšný náhled nic neloguje.
- `BgraPixelsTests`: stejné rozložení řádků, zarovnané cílové řádky (výplň zůstane netknutá), větší cíl, neplatné rozměry. Testy okna kamery (vykreslení, letterbox, změna rozlišení) dál procházejí.

**Ověřeno (Windows 11, 2026-10-19)**
- `dotnet build` 0 varování, `dotnet test` 2× (2751 testů, po revizi 2771, 1 přeskočený – chybějící záznamy PrusaLink, beze vztahu), `dotnet publish -c Release -r win-x64` (AOT) bez varování; Debug i AOT aplikace se spustí a běží.
- Skutečná kamera: pokus o snímek ze scratch programu (`CameraSnapshot` + FFmpeg, mimo repozitář) na `rtsp://192.168.0.227/live` skončil po 15 s bez obrazu – kamera na síti neodpovídala (ping ani TCP 554, brána 192.168.0.1 odpovídala). Cesta se skutečnou kamerou tedy ověřená **není**.
- macOS neověřeno (kód je multiplatformní, bez platformních rozdílů).

**Ruční kontrola pro uživatele:** Nastavení → tiskárna → Kamera → Otestovat kameru s `rtsp://192.168.0.227/live`: náhled se zobrazí vlevo od tlačítka, změna adresy ho smaže.

**Uzavřeno 2026-10-21:** všechna akceptační kritéria jsou splněná. Úkol se uzavírá na rozhodnutí uživatele. Ruční kontroly uvedené výše zůstávají jako doporučení a ověří se při ruční kontrole navazujících úkolů.
