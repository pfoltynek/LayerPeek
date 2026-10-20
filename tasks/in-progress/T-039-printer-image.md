---
id: T-039
title: Obrázek tiskárny (podle modelu nebo vlastní)
status: in-progress
owner: core-dev + ui-designer + ui-dev
priority: P3
platform: all
depends_on: [T-025, T-033]
---

## Cíl
Každá tiskárna má obrázek, aby se tiskárny daly rychle rozlišit. Obrázek se vybere automaticky podle modelu z PrusaLink (vlastní ilustrace), nebo si uživatel nastaví vlastní obrázek.

## Současný stav (2026-10-19)
- Model tiskárny zjišťuje jen test spojení a okno Diagnostika: `PrusaLinkClient` čte `GET /api/version`, pole `printer` = kód modelu ve tvaru `type.version.subversion` (např. `7.0.0`), **až od firmwaru 6.5**; starší firmware ho nevrací (`PrusaLinkInfoResult.PrinterModel` = `null`). Viz `docs/api/prusalink.md` a `src/PrusaTrayDiagnostic.PrusaLink/Api/PrusaLinkDtos.cs`. Diagnostika ho zobrazuje tak, jak je (`DiagnosticsViewModel.ModelText`), test spojení v nastavení také.
- Polling (`/api/v1/status`) model nevrací. Model se nikde neukládá (`PrinterDefinition` ho nemá).
- Prusa Connect (odložený T-005) vrací `printer_model` / `printer_type_name` (`docs/api/prusa-connect.md`).
- Tray menu: `NativeMenu` přes `AvaloniaNativeTrayMenu`; položky tiskáren zatím bez ikon. Avalonia `NativeMenuItem` má vlastnost `Icon` (bitmapa); jestli ji tray menu na Windows a macOS skutečně zobrazuje, je potřeba ověřit.
- Seznam tiskáren v nastavení (`SettingsWindow`, `ListBox` s názvem a značkou `!`), podzáložka Tiskárna vznikne v T-033. Okno Diagnostika ukazuje vybranou tiskárnu.

## Rozhodnuto
- **Zdroj obrázku (2026-10-19):** automaticky podle modelu z PrusaLink (MK4/MK4S, Core One, XL, MINI/MINI+, MK3.9…) s **vlastními ilustracemi** – žádné fotografie Prusa (autorská práva). Uživatel může nastavit vlastní obrázek (PNG/JPG), uložený ve složce dat aplikace, s omezenou velikostí a validací.
- **Kde se zobrazuje (2026-10-19):** v nastavení (seznam tiskáren + podzáložka Tiskárna), v okně Diagnostika u vybrané tiskárny a v tray menu jako ikona položky tiskárny, kde to platforma podporuje (Avalonia `NativeMenuItem.Icon` na Windows; rozdíly na macOS zapsat).
- **Styl (2026-10-19):** ilustrace navazují na vizuální styl ikony z T-025.
- **Detekce modelu (2026-10-19):** při testu spojení i při startu monitoringu (`GET /api/version`), výsledek se uloží do `settings.json` (obrázek funguje i offline).
- **Neznámý model / firmware < 6.5 / Prusa Connect (2026-10-19):** obecná ilustrace tiskárny; uživatel může model vybrat ručně nebo nahrát vlastní obrázek. U Prusa Connect bez automatické detekce.
- **Vlastní obrázek (2026-10-19):** PNG/JPG do 5 MB, při importu zmenšit na max. 512 px delší strana a uložit jako PNG; zobrazit celý ve čtvercovém rámečku (letterbox), bez editoru ořezu.
- **Úložiště a úklid (2026-10-19):** `printer-images/<id tiskárny>.png` vedle `settings.json`; smaže se při odebrání tiskárny (po uložení) a při návratu na automatický obrázek, Zrušit v nastavení zahodí nový import, při startu se smažou osiřelé soubory.
- **macOS (2026-10-19):** ikony položek tray menu na macOS zatím vynechat, ověří se v T-018.

## Rozsah

### Průzkum (core-dev, na začátku úkolu)
- Z primárního zdroje (Prusa-Firmware-Buddy, soubor s definicí modelů tiskáren, např. `printer_model`) zjistit mapování kódů `type.version.subversion` na modely (MINI, MK3.5, MK3.9, MK4, MK4S, XL, Core One, Core One L…) a zapsat ho s URL do `docs/api/prusalink.md`. Případně i hodnoty z Prusa Connect.

### Core (core-dev)
- Doménový výčet / identifikátor modelu (např. `PrinterModel`) a převod kódu z PrusaLink na model (neznámý kód → `Unknown`). Testy s kódy z firmwaru.
- Uložení zjištěného modelu a volby obrázku u tiskárny podle odpovědí na otázky (např. `PrinterDefinition.Model`, `PrinterDefinition.Image` = automaticky / vlastní soubor), nepovinná pole v `settings.json`, `version: 1`.
- Úložiště vlastních obrázků ve složce dat aplikace (`AppDataPaths`) za abstrakcí (např. `IPrinterImageStore`): import (kopie, validace formátu podle hlavičky souboru, limit velikosti, případně zmenšení), smazání. Atomické zápisy, žádné cesty od uživatele v logu kromě názvu souboru.
- Testy: převod kódů, uložení a načtení, import platného / neplatného / příliš velkého souboru, smazání, úklid (testy v dočasných složkách s úklidem i po pádu).

### Ilustrace (ui-designer)
- Vlastní vektorové ilustrace pro podporované modely + obecná ilustrace pro neznámý model, ve stylu T-025; rastrové velikosti pro seznam, podzáložku, Diagnostiku a menu (16/20/24/32 px a větší). Návrh ke schválení uživatelem.

### App (ui-dev)
- Zjištění modelu (podle odpovědi na otázku 1) a uložení.
- Zobrazení obrázku v seznamu tiskáren, na podzáložce Tiskárna (včetně volby automaticky / vlastní obrázek, výběr souboru přes Avalonia `StorageProvider`, náhled, odstranění vlastního obrázku), v okně Diagnostika a v tray menu (`NativeMenuItem.Icon`, kde je podporováno).
- Rozložení nastavení podle schváleného návrhu UI z T-033, bez posuvníku.
- Texty ve všech 8 jazycích.
- Bez nového NuGet balíčku (dekódování obrázků přes Avalonia/Skia); případný balíček jen po ověření AOT a souhlasu uživatele.

## Akceptační kritéria
- [x] Mapování kódů modelů zdokumentované v `docs/api/prusalink.md` s odkazem na zdroj ve firmwaru
- [x] Core: převod kódu na model, uložení modelu a volby obrázku, úložiště vlastních obrázků; testy (Core.Tests) včetně neplatných a příliš velkých souborů a úklidu
- [ ] Vlastní ilustrace pro všechny podporované modely + obecná, schválené uživatelem; jako `AvaloniaResource` (test, že každý model má ilustraci a ta jde načíst)
- [ ] App.Tests: view model vybere ilustraci podle modelu, vlastní obrázek má přednost, neznámý model → obecná ilustrace podle otázky 2, import a odstranění vlastního obrázku, odstranění tiskárny uklidí její vlastní obrázek (podle otázky 5)
- [ ] Headless UI testy: obrázek v seznamu tiskáren, na podzáložce Tiskárna a v okně Diagnostika; snímky reference aktualizované; test „bez posuvníku“ z T-033 projde ve všech 8 jazycích
- [ ] Tray menu: položka tiskárny má ikonu (test presenteru menu / falešného menu); ruční ověření na Windows, že se ikona v menu zobrazí (zapsat do poznámek)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish `win-x64` bez varování

## Otevřené otázky
1. **Velikost obrázku v jednotlivých místech** (seznam tiskáren, podzáložka, Diagnostika, tray menu) – navrhne ui-designer ke schválení.
2. **Ukládání modelu zjištěného monitoringem (core-dev, 2026-10-20):** Core model zjistí a ohlásí (`IPrinterMonitor.ModelDetected`), ale `settings.json` sám nepřepisuje. Pravidlo pro App je potřeba potvrdit:
   - Ukládat na pozadí jen při skutečné změně (`AppSettings.WithDetectedModel` vrátí jinou instanci)?
   - Restartovat kvůli tomu monitoring? Návrh: ne, změna jen modelu monitoring nerestartuje.
   - Co když je zrovna otevřené okno nastavení? Návrh: uložit hned; při uložení okna se převezme nejnovější `detectedModel` ze souboru, ne ten z doby otevření okna.
3. **Core One INDX a Core One L INDX (`7.10.0`, `8.10.0`), iX (`4.1.0`):** teď mají obecnou ilustraci (`Unknown`). Mají dostat ilustraci Core One / Core One L, vlastní ilustraci, nebo zůstat obecné?
4. **Varianty se stejným vzhledem:** MK3.5/MK3.5S, MK3.9/MK3.9S, MK4/MK4S, XL/XL+, Core One/Core One L. Má mít každý z 11 modelů vlastní ilustraci, nebo mohou varianty sdílet jednu? Výčet je rozlišuje všechny, sloučit jde jen v App.
5. **Změna adresy nebo typu připojení:** může jít o jinou tiskárnu. Smazat uložený `detectedModel`, aby do další detekce byla obecná ilustrace? Návrh: ano, smazat (dělá editor v App).

## Poznámky k implementaci

### Core (core-dev, 2026-10-20)
- **Mapování kódů** je v `docs/api/prusalink.md`, oddíl „Model tiskárny“. Zdrojem je Prusa-Firmware-Buddy `include/common/printer_model_data.hpp` (ověřeno v tazích v6.5.1–v6.10.3 a na `master`). Kód `7.0.0` z původních fixtures a příkladů ve firmwaru neexistuje, Core One je `7.1.0`. Opravena fixture `version.json` a test v PrusaLink.Tests. Texty v App testech (`"7.0.0"` jako zobrazený řetězec) jsem neměnil.
- **Model:**
  - `Core/Printers/PrinterModel` (výčet: `Unknown`, `Mini`, `Mk35`, `Mk35S`, `Mk39`, `Mk39S`, `Mk4`, `Mk4S`, `Xl`, `XlPlus`, `CoreOne`, `CoreOneL`);
  - `PrinterModelCode` (kód `type.version.subversion`, přísné `TryParse`);
  - `PrinterModels` (`FromCode`, `Selectable`, `Format`/`TryParse` pro soubor, `GetProductName` pro zobrazení, např. „Core One L“).
- **Volba obrázku:**
  - `PrinterImageChoice` (`Automatic` | `ForModel(model)` | `Custom`) jako `PrinterDefinition.Image`; zjištěný kód je v `PrinterDefinition.DetectedModel`.
  - `PrinterImage.Resolve(printer, customImageExists)` vrátí, co zobrazit (`Generic` / `Model` / `Custom`):
    - Prusa Connect bez ruční volby → obecná ilustrace;
    - chybějící vlastní soubor → automatický obrázek.
- **`settings.json`:**
  - Nepovinná pole `printers[*].detectedModel` (např. `"1.4.1"`) a `printers[*].image` (`{ "mode": "Model", "model": "Mk4S" }` nebo `{ "mode": "Custom" }`). Automatický obrázek a nezjištěný model se nezapisují.
  - `version` zůstává 1.
  - Neplatné hodnoty: varování bez opakování hodnoty a výchozí hodnota. Hodnota špatného JSON typu dál znamená poškozený soubor, stejně jako u ostatních polí.
  - Dobře utvořený, ale neznámý kód se zachová, novější verze aplikace ho může znát.
  - Čistič přihlašovacích údajů kamery nová pole nemění (test).
- **Detekce:**
  - `IPrinterModelSource` je volitelné rozhraní klienta, implementuje ho `PrusaLinkClient.DetectModelAsync`: jen `GET /api/version`, bez dalšího pollingu.
  - `PrinterMonitor` se zeptá jednou za běh, po prvním úspěšném dotazu na stav (snímek se zveřejní dřív), v rámci timeoutu dotazu a nikdy souběžně s pollingem. Selhání zkusí znovu nejvýš 3× za běh.
  - Výsledek je událost `IPrinterMonitor.ModelDetected` (výchozí prázdná implementace v rozhraní, takže falešné monitory v App se měnit nemusely).
  - `AppSettings.WithDetectedModel(id, kód)` vrátí tutéž instanci, když se nic nemění.
  - Pro test spojení je `PrusaLinkPrinterInfo.ModelCode`.
  - Ukládání do souboru z monitoringu čeká na otázku 2.
- **Vlastní obrázky (`Core/Images`):**
  - `PrinterImageFormats` (podpis PNG/JPEG, limity 5 MB / 512 px / 2 MB uloženého PNG).
  - `IPrinterImageProcessor`: dekódování a zmenšení implementuje App přes Avalonia/Skia, chyba = `PrinterImageDecodeException`.
  - `PrinterImageImporter`: kontrola velikosti a podpisu, převod, kontrola výsledku. Nic nezapisuje a nic neloguje.
  - `IPrinterImageStore` / `FilePrinterImageStore`: `printer-images/` vedle `settings.json`, atomický zápis, `ReadAsync` vrací jen platné PNG, `DeleteAsync`, `RemoveOrphansAsync`.
  - Název souboru je odvozený z id (`PrinterImageFileNames`): bezpečné id malými písmeny, jinak `h_<hash>.png`. Cesta tak nemůže opustit složku a nevznikne kolize ani rezervované jméno Windows.
- **Pro App (ui-dev):**
  - `PrinterEditorViewModel` musí `DetectedModel` a `Image` přenášet, jinak je uložení okna smaže.
  - Nový import držet v paměti (PNG z `PrinterImageImporter`) a uložit přes `SaveAsync` až po uložení nastavení. Zrušit = zahodit.
  - `DeleteAsync` volat po uloženém odebrání tiskárny nebo přepnutí z vlastního obrázku.
  - `RemoveOrphansAsync` volat při startu jen s nastavením ve stavu `Loaded`. Po neúspěšném načtení by smazal všechny obrázky.
  - Letterbox do čtverce je věc zobrazení (`Stretch="Uniform"`). Uložený PNG má původní poměr stran.
- **AOT:** bez nových balíčků, bez reflexe. `Enum.IsDefined<T>` je generické (AOT bezpečné), JSON jen přes `SettingsJsonContext`.
- **Testy:**
  - Core.Tests: `PrinterModelCodeTests`, `PrinterModelsTests`, `PrinterImageTests`, `SettingsMapperPrinterImageTests`, `PrinterMonitorModelDetectionTests`, `Images/*` (formáty, názvy souborů, úložiště, import).
  - PrusaLink.Tests: `PrusaLinkModelDetectionTests`.
  - Dočasné soubory jsou pod `TestDirectories`, uklidí se i po pádu.
