---
id: T-034
title: Náhled obrazu kamery na záložce Kamera v nastavení
status: todo
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

## Rozsah
- Rozšířit výsledek testu o obraz (např. `CameraTestResult.Image` typu `CameraImage`), `CameraSnapshotTester` ho předá.
- `PrinterEditorViewModel`: vlastnost náhledu (obraz nebo nic). Nastaví se po úspěchu; smaže se při změně adresy, při novém testu, po timeoutu/chybě. Případné další smazání (přepnutí tiskárny, zavření okna) podle uvolnění prostředků – bitmapa nesmí zůstat viset po zavření okna.
- Na podzáložce Kamera `Image` (`Stretch=Uniform`, šířka panelu, `MaxHeight≈180`) pod výsledkem testu; bez obrazu se nezobrazuje nic (nebo zástupný prvek podle návrhu UI z T-033).
- Převod `CameraImage` → `WriteableBitmap` jednou na výsledek, bitmapa se uvolní při smazání náhledu.
- Adresa jde do logu jen přes `UriRedaction` (beze změny pravidel T-014).
- Rozložení podle schváleného návrhu UI z T-033, bez posuvníku.
- Bez nového NuGet balíčku.

## Akceptační kritéria
- [ ] Po úspěšném testu se na podzáložce Kamera zobrazí snímek se zachovaným poměrem stran, šířka panelu, výška ≤ 180 DIP
- [ ] Testy view modelu (App.Tests, falešná továrna, `FakeTimeProvider`): úspěch nastaví náhled; timeout a chyba náhled smažou; změna adresy náhled smaže; nový test starý náhled smaže; zrušení testu; žádné visící spojení (dispose feedu ověřen); zavření okna uvolní bitmapu
- [ ] `CameraSnapshotTesterTests` rozšířené o předání obrazu
- [ ] Headless UI test: červený snímek z falešné kamery je po kliknutí na Otestovat vidět na podzáložce Kamera (kontrola pixelů), letterbox odpovídá poměru stran; po změně adresy zmizí
- [ ] Nový snímek reference podzáložky Kamera s náhledem; test „bez posuvníku“ z T-033 projde ve všech 8 jazycích (i s náhledem)
- [ ] Případné nové texty ve všech 8 jazycích, test přepnutí jazyka
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
