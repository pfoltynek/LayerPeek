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
- **Ilustrace (2026-10-20):** varianta **A (plochý pohled zepředu, průhledné pozadí)** z návrhu ui-designera. MK4, MK4S, MK3.9, MK3.9S, MK3.5 a MK3.5S sdílí **jednu** ilustraci; XL i XL+ mají vždy vzhled s pěti nástroji (`xl`); Core One L používá ilustraci Core One; Core One INDX, Core One L INDX a iX → obecná ilustrace; neznámý model → obecná ilustrace (motiv aplikace v čárkovaném rámečku). (Otázky 3 a 4.)
- **Model zjištěný monitoringem (2026-10-20):** ukládá se do `settings.json` na pozadí **jen při změně**; **monitoring se kvůli tomu nerestartuje**; je-li otevřené okno nastavení, převezme při uložení nejnovější `detectedModel`. (Otázka 2.)
- **Změna adresy nebo typu připojení (2026-10-20):** v editoru smaže `detectedModel` tiskárny do další detekce. (Otázka 5.)
- **Velikosti (2026-10-20, = T-033 Q8):** seznam tiskáren 24 DIP, podzáložka Tiskárna 56 DIP (tlačítko s nabídkou: Automaticky / výběr modelu / vlastní obrázek… / odebrat vlastní), Diagnostika 48 DIP, ikona položky tray menu 16 DIP (Windows; macOS vynechán podle rozhodnutí výše). (Otázka 1.)
- **Chyba importu vlastního obrázku (2026-10-20):** zůstává ve **stavovém řádku dole** (pod polem Název se nevejde bez posuvníku).
- **Prusa Connect (2026-10-20):** první položka nabídky obrázku „Obecná ilustrace“ je v pořádku.
- **Obrázek, který se nepodaří uložit (2026-10-20):** okno zůstane otevřené s upozorněním; import zůstane neuložený, takže další Uložit to zkusí znovu.
- **Překlady (2026-10-20):** všech 19 nových textů schváleno.
- **Text `SettingsSavedImageNotStoredPrevious` (2026-10-20):** schválen ve všech 8 jazycích (revize po review).
- **Úklid osiřelých obrázků (2026-10-20):** nesmí smazat obrázek tiskárny, jejíž položka je v `settings.json`, ale loader ji přeskočil (neplatná adresa, duplicitní id, neznámý typ připojení…); ponechávají se obrázky všech id v souboru.

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
- [x] Vlastní ilustrace pro všechny podporované modely + obecná, schválené uživatelem; jako `AvaloniaResource` (test, že každý model má ilustraci a ta jde načíst)
- [x] App.Tests: view model vybere ilustraci podle modelu, vlastní obrázek má přednost, neznámý model → obecná ilustrace podle otázky 2, import a odstranění vlastního obrázku, odstranění tiskárny uklidí její vlastní obrázek (podle otázky 5)
- [x] Headless UI testy: obrázek v seznamu tiskáren, na podzáložce Tiskárna a v okně Diagnostika; snímky reference aktualizované; test „bez posuvníku“ z T-033 projde ve všech 8 jazycích
- [ ] Tray menu: položka tiskárny má ikonu (test presenteru menu / falešného menu); ruční ověření na Windows, že se ikona v menu zobrazí (zapsat do poznámek) – testy hotové, **ruční ověření na Windows zbývá (uživatel)**; **2026-10-23: 150 % ověřeno E2E testem T-070** (ostrá 24px ilustrace u aplikace spuštěné při 150 %), **100 % čeká na T-069**
- [x] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish `win-x64` bez varování

## Otevřené otázky
Otázky z implementace (chyba importu, Prusa Connect, neuložený obrázek, překlady) rozhodl uživatel 2026-10-20, viz Rozhodnuto.

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

### App (ui-dev, 2026-10-20)
Stav: implementace hotová, automatické testy procházejí. Úkol zůstává `in-progress` kvůli ručnímu ověření ikon v tray menu na Windows (viz „Zbývá ručně“).

- **Ilustrace (`Assets/Printers/`):**
  - Varianta A od ui-designera, id `mk4`, `core-one`, `xl`, `mini`, `generic`.
  - Předrenderované PNG 16, 20, 24 (zjednodušená kresba), 32, 48, 56, 64, 96, 112, 128, 256 px (podrobná kresba). 96/112/128 jsem dorenderoval designérovým nástrojem; ostatní PNG i SVG jsou bajtově shodné s návrhem.
  - SVG zdroje (`<id>.svg`, `-24px`, `-20px`, `-16px`) jsou v repozitáři, do binárky nejdou (stejně jako u T-025).
- **Výběr velikosti podle fyzických pixelů:**
  - `PrinterIllustrations.SelectPixelSize` vybere nejmenší PNG s dostatkem pixelů (nikdy nezvětšuje), např. 24 DIP: 24/32/48/48 px při 100/125/150/200 %.
  - Ovládací prvek `PrinterImageView` (seznam, tlačítko, Diagnostika) čte `RenderScaling` okna při každém vykreslení.
  - Vlastní obrázek vykreslí celý ve čtverci (letterbox), dekóduje se jednou.
  - Mapování modelů na ilustrace: `PrinterIllustrations.IdFor` podle rozhodnutí 2026-10-20.
- **Zobrazení (model bez Avalonie):**
  - `PrinterPicture`: vyřešený obrázek + bajty vlastního PNG.
  - `CustomPrinterImages`: uložené vlastní obrázky v paměti pro tray a Diagnostiku.
  - `PrinterImageStartup`: při startu na pozadí smaže osiřelé soubory **jen při stavu načtení `Loaded`** a načte používané obrázky.
- **Zpracování importu:** `AvaloniaPrinterImageProcessor` (`IPrinterImageProcessor`) běží na thread poolu.
  - Velikost se nejdřív přečte z hlavičky (`ImageHeaders`: PNG IHDR, JPEG SOF). PNG nad 40 MPx a JPEG nad 400 MPx se odmítnou (chyba „nejde načíst“).
  - Velký JPEG se dekóduje rovnou zmenšený (Skia 1/2–1/8) na zhruba dvojnásobek cíle, pak se plynule zmenší.
  - Delší strana max. 512 px, nikdy se nezvětšuje, kratší strana se zaokrouhlí (min. 1 px).
  - **EXIF orientace JPEG** se aplikuje na pixely (`ImageHeaders.ReadJpegOrientation`, `PixelOrientation`, všech 8 hodnot).
  - Výstup je nové PNG bez metadat (EXIF, GPS, ICC profil). Chyby dekódování → `PrinterImageDecodeException`.
  - Bez nového balíčku, jen Avalonia API.
- **Okno nastavení:**
  - Seznam: obrázek 24 × 24 · 8 · „!“ · název.
  - Podzáložka Tiskárna: tlačítko 56 × 56 (`Grid` `56,12,*,16,320`, Název je užší), dole zarovnané s polem Název, malá šipka v rohu. `AutomationProperties.Name` „Obrázek tiskárny: MK4S (zjištěno)“, tooltip „Změnit obrázek tiskárny“.
  - Nabídka (`MenuFlyout`), zaškrtnutá je aktuální volba:
    - Automaticky podle modelu (MK4S / zatím nezjištěn); u Prusa Connect „Obecná ilustrace“.
    - Vybrat model ▸ (`PrinterModels.Selectable`, názvy z `GetProductName`).
    - Vlastní obrázek…
    - Odebrat vlastní obrázek (jen u vlastního).
  - Výběr souboru: `StorageProviderImageFilePicker` (filtr PNG/JPG, přečte max. 5 MB + 1 B, dokud je soubor otevřený – kvůli sandboxu na macOS).
  - Chyby importu (příliš velký, jiný formát, nejde načíst, nejde otevřít) jsou lokalizované. Zobrazí se ve **stavovém řádku dole** (rozhodnutí 2026-10-20), s další volbou zmizí.
  - Nový import zůstane v paměti (`PrinterEditorViewModel.PendingImage`, okno je „změněné“). Zrušit ho zahodí.
- **Zjištěný model a volba v editoru:**
  - `PrinterEditorViewModel` přenáší `DetectedModel` a `Image`, takže uložení okna nic neztratí.
  - Zjištěný model patří k adrese a typu připojení, pro které byl zjištěn. Jiná adresa nebo typ ho neuloží; návrat na původní adresu ho vrátí.
  - Úspěšný test spojení uloží `ModelCode` testované tiskárny (`ConnectionTestResult.ModelCode`). Firmware bez kódu kód smaže. Zapíše se s ostatním nastavením při Uložit.
- **Uložení (`SettingsEditorStore`):**
  - Po zapsání `settings.json` uloží nové obrázky (`IPrinterImageStore.SaveAsync`).
  - `DeleteAsync` volá u odebraných tiskáren a u přepnutí z vlastního obrázku.
  - Aktualizuje `CustomPrinterImages`, tray a Diagnostika hned ukážou nový obrázek.
  - Obrázek, který se nepodařilo uložit → `SettingsSaveResult.UnsavedImages` a upozornění. Při neúspěšném uložení nastavení se obrázků nic nedotkne.
  - Před zápisem převezme **nejnovější model z monitoringu** (`ISettingsApplier.CurrentSettings`) u tiskáren, jejichž model, adresu a typ připojení okno nezměnilo.
- **Model zjištěný monitoringem (`MonitoringSession`):**
  - Odebírá `IPrinterMonitor.ModelDetected`. Jen skutečná změna (`AppSettings.WithDetectedModel`) aktualizuje aktuální nastavení a pozorovatele (`UpdateSettings`; tray menu a Diagnostika ukážou ilustraci).
  - Na pozadí a postupně zapíše **jen tuto hodnotu** novou metodou Core `ISettingsStore.SetDetectedModelAsync` (`DetectedModelWriter`, jako čistič údajů kamery). Zbytek souboru zůstane bajt po bajtu; chybějící vlastnost se doplní za poslední hodnotu s odsazením první.
  - Hlášení z běhu, který se právě nahrazuje (nová adresa), se zahodí.
  - Monitoring se nerestartuje: `KeepsMonitoring` nově ignoruje `DetectedModel` a `Image`, takže ani změna obrázku v okně nastavení monitoring nerestartuje.
  - Při ukončení aplikace se počká na rozepsaný zápis (max. 1 s).
- **Diagnostika:** obrázek 48 × 48 vlevo od výběru tiskárny / názvu (`DiagnosticsViewModel.Picture`), sleduje změny definice i vlastních obrázků.
- **Tray menu (Windows):**
  - `TrayMenuEntry.Icon` u položek tiskáren (jedna tiskárna i záhlaví podmenu). Položky podmenu, Kamera a pevné položky ikonu nemají.
  - `INativeTrayMenuItem.SetIcon` → `NativeMenuItem.Icon` přes `PrinterMenuIcons`.
  - Avalonia na Windows kreslí tray menu sama (`MenuFlyoutPresenter`) a ikonu vměstná do 16 × 16 DIP (ověřeno headless testem). Bitmapa má proto 16 DIP pixelů při **systémovém měřítku** (`GetDpiForSystem`, `[LibraryImport]`): 16/20/24/32 px při 100/125/150/200 %.
  - Menu se otevírá jako nové okno u kurzoru, takže jeho monitor předem neznáme. Na monitoru s jiným měřítkem se bitmapa přeškáluje.
  - Na macOS ikony nejsou (`TrayMenuOptions.ForCurrentPlatform`).
  - Změna obrázku nebo modelu za běhu aktualizuje jen ikonu, stav tiskárny zůstane.
- **Core (malé změny):**
  - `ISettingsStore.SetDetectedModelAsync` (výchozí implementace nic nezapíše), `JsonSettingsStore` a `Storage/DetectedModelWriter`.
  - `PrinterImage.Resolve(choice, typ, kód, …)` pro editor.
  - `docs/api/prusalink.md` doplněn o ukládání.
- **Texty (8 jazyků):** 19 nových klíčů (`SettingsImage*`, `SettingsSavedImageNotStored`, `DiagnosticsPrinterImage`) podle konvencí T-029 (výpustka „…“, nezlomitelná mezera před MB a ve fr před „:“ a „;“, fr „Mo“).
- **Testy:**
  - Core.Tests: `JsonSettingsStoreDetectedModelTests`.
  - App.Tests:
    - `Printers/*`: ilustrace a výběr velikosti, `PrinterPicture`, `CustomPrinterImages` a start (úklid jen při `Loaded`), hlavičky a EXIF, orientace.
    - `Settings/PrinterImageSettingsTests`: přenos a mazání modelu, test spojení, nabídka, import a chyby, Zrušit, odebrání, neuložený obrázek.
    - `Settings/SettingsEditorStoreImageTests`: uložení a mazání po uložení, převzetí nejnovějšího modelu.
    - `Tray/MonitoringSessionModelTests`: uložení na pozadí bez restartu, jen při změně, postupně, zahození hlášení nahrazovaného běhu, stav načtení.
    - `Tray/Menu/TrayMenuIconTests`, `TrayMenuTargetTests` (ikony).
    - `Diagnostics/DiagnosticsPictureTests`.
  - UiTests:
    - `PrinterImageProcessorTests`: PNG/JPEG, zmenšení, nezvětšování, EXIF orientace 1/3/6/8, bez metadat, poškozená data, příliš mnoho pixelů.
    - `PrinterImageViewTests`: všechny ilustrace ve všech velikostech, pixely podle měřítka 100–200 %, letterbox.
    - `SettingsWindowImageUiTests`: 24 v seznamu, tlačítko 56, nabídka a výběr modelu, import přes dialog, chyba ve stavovém řádku.
    - `DiagnosticsWindowImageUiTests`: 48 a snímek.
    - `TrayMenuIconUiTests`: `NativeMenuItem.Icon` 16/20/24/32 px, vlastní obrázek, bez ikon, ikona v menu Avalonie 16 DIP.
    - Testy rozložení: pořadí Tab s tlačítkem obrázku, nový stav „všechny chyby + nejdelší chyba importu“ v 8 jazycích bez posuvníku.
    - `AppIconUiTests`: seznam assetů.
  - Snímky: přegenerované `settings-*`, nové `settings-printer-images` a `diagnostics-printer-image`; prohlédnuté.
  - Sdílené falešné objekty: `App.Tests/Printers/ImageFakes.cs` (nalinkované do UiTests).
- **Ověření (Windows 11, 2026-10-20):**
  - `dotnet build` 0 varování, 0 chyb.
  - `dotnet test` 2× bez chyby (3840 testů, 1 přeskočený existující).
  - `dotnet publish -c Release -r win-x64` bez varování.
  - Debug i AOT build spuštěné asi 25 s běžely. Obě skutečně zjistily model tiskárny uživatele (`7.1.0`, Core One) a monitoring do `settings.json` doplnil jen `"detectedModel": "7.1.0"`. Soubory uživatele jsem po ověření vrátil ze zálohy do původního stavu.
- **Na macOS nic neověřeno:** dialog souborů (`AppleUniformTypeIdentifiers`); ikony menu jsou tam vypnuté; AOT pro macOS jen na macOS.
- **Zbývá ručně (uživatel):**
  - Tray menu na Windows při 100 % a 150 %: u položky tiskárny je vidět ostrá ilustrace (16 / 24 px), u více tiskáren u záhlaví podmenu.
  - Nastavení → tlačítko obrázku: nabídka, výběr modelu, vlastní obrázek z telefonu (otočená fotka se zobrazí správně), Uložit / Zrušit.
  - Diagnostika: obrázek v záhlaví.
  - Po uložení se obrázek v tray menu změní bez „Připojování…“.

### Revize po review (ui-dev, 2026-10-20)
- **Úklid osiřelých obrázků:**
  - `SettingsLoadResult.StoredPrinterIds` (Core) obsahuje id všech položek `printers` v souboru, i těch, které loader přeskočil.
  - `MonitoringSession.LoadSettingsWithStatusAsync` je předá `PrinterImageStartup`, který je přidá do množiny ponechaných obrázků.
  - Obrázek přeskočené tiskárny tak zůstane, dokud ji uživatel neopraví nebo neodebere.
- **„Vlastní obrázek…“ po zrušeném nebo neúspěšném importu:** Avalonia při kliknutí zaškrtne položku typu radio sama. Editor po importu vždy znovu ohlásí `IsAutomaticImage`, `IsCustomImage` a zaškrtnutí modelů, takže nabídka ukáže platnou volbu. Test v UI kliká skutečným ukazatelem přes obslužnou rutinu menu.
- **Obrázek, který se nepodaří uložit:**
  - `SettingsSaveResult.UnsavedImages` je množina id. Okno u těchto tiskáren import ponechá neuložený, zůstane „změněné“ a další Uložit ho zkusí znovu.
  - Upozornění „…tiskárna proto zobrazuje ilustraci“ se ukáže, jen když tiskárna nemá starší uložený vlastní obrázek. Jinak nový text `SettingsSavedImageNotStoredPrevious` (schválen 2026-10-20):
    - en: Settings saved. A printer image could not be stored, so the printer keeps its previous image.
    - cs: Nastavení je uloženo. Obrázek tiskárny se nepodařilo uložit, tiskárna proto zobrazuje předchozí obrázek.
    - sk: Nastavenia sú uložené. Obrázok tlačiarne sa nepodarilo uložiť, tlačiareň preto zobrazuje predchádzajúci obrázok.
    - de: Einstellungen gespeichert. Ein Druckerbild konnte nicht gespeichert werden, der Drucker zeigt daher sein bisheriges Bild.
    - es: Configuración guardada. No se pudo guardar una imagen de impresora, por lo que la impresora muestra su imagen anterior.
    - fr: Paramètres enregistrés. Une image d'imprimante n'a pas pu être enregistrée ; l'imprimante affiche donc son image précédente.
    - it: Impostazioni salvate. Impossibile salvare un'immagine della stampante, quindi la stampante mostra l'immagine precedente.
    - pl: Ustawienia zostały zapisane. Nie udało się zapisać obrazu drukarki, dlatego drukarka pokazuje swój poprzedni obraz.
  - Pokud se zároveň nepodařilo smazat heslo, ukážou se obě upozornění za sebou.
- **Souběh se zjištěným modelem:**
  - Zápis na pozadí proběhne, jen když aktuální nastavení stále obsahuje tento kód. Zapisuje se jen do položky se stejnou adresou a typem připojení: `SetDetectedModelAsync` je dostane a `DetectedModelWriter` je porovná stejně jako loader.
  - Aktualizace bez restartu (uložení okna) převezme zjištěný model z běžícího nastavení u tiskáren se stejnou adresou a typem připojení a zapíše ho znovu, protože soubor okna ho neobsahuje.
- **Poškozený uložený vlastní PNG:**
  - `PrinterPicture.Fallback` nese automatický obrázek (např. ilustraci zjištěného modelu).
  - `PrinterImageView` a `PrinterMenuIcons` ho použijí místo obecné ilustrace.
- **Ikony v tray menu:** `PrinterMenuIcons` drží jednu bitmapu vlastního obrázku na tiskárnu (`INativeTrayMenuItem.SetIcon` dostává id tiskárny) a počítá položky, které ji používají (`Acquire` / `Release`; při přestavbě menu ji krátce sdílí stará a nová položka). Uvolní se, když ji nepoužívá žádná položka: tiskárna odebraná z menu (`Detach`), ikona bez vlastního obrázku nebo `SetIcon(null)`, nový obrázek tiskárny. Testy: `TrayMenuIconUiTests`.
- **Orientace v PNG:** chunk `eXIf` (PNG 1.5) se čte stejně jako EXIF v JPEG (`ImageHeaders.ReadPngOrientation`) a aplikuje se na pixely.
- **`docs/api/prusalink.md`:** řádky `7.10.0` / `8.10.0` uvádějí rozhodnutí (obecná ilustrace); ukládání modelu jen do položky stejného stroje.
- **Testy:**
  - Core: `JsonSettingsStoreDetectedModelTests` (jiná adresa / typ připojení, porovnání jako loader, `StoredPrinterIds`).
  - App.Tests: úklid ponechá přeskočené položky; neuložený obrázek zůstane neuložený a Uložit ho zkusí znovu; text s předchozím obrázkem; obě upozornění; obnovení zaškrtnutí; `Fallback`; zápis se jménem stroje; přeskočení zastaralého zápisu; převzetí modelu při aktualizaci bez restartu; `eXIf`.
  - UiTests: zrušený import přes skutečné kliknutí; náhradní ilustrace u poškozeného PNG (zobrazení i menu); uvolnění nahrazených bitmap; orientace `eXIf` v procesoru.
- **Ověření:** `dotnet build` 0 varování, `dotnet test` 2× bez chyby. Aplikaci jsem znovu nespouštěl (podle pokynu nad skutečnými daty uživatele ne).
- **Nestabilní UI test `CancelledCustomImageLeavesTheAutomaticChoiceChecked` (příčina):**
  - Test kontroloval zaškrtnutí položek nabídky až po jejím zavření. Zavřený `MenuFlyout` své položky odpojí: přijdou o `DataContext` a jejich vazby `IsChecked` se vrátí na výchozí `false` (ověřeno sondou).
  - `Assert.True(automatic.IsChecked)` proto selhal vždy. Při přidání ladicí hlášky se omylem přepsal (sed na jiném řádku), takže pak test „procházel“ a selhání vypadalo jako náhodné.
  - Oprava: položka „Vlastní obrázek…“ má v testu `StaysOpenOnClick`, takže nabídka po kliknutí zůstane otevřená a zaškrtnutí se čte, dokud mají položky `DataContext` tiskárny. Tak test opravu hlídá: bez `RefreshChoiceChecks()` selže (ověřeno dočasným odebráním volání). Pak se čte ještě jednou po znovuotevření. Čeká se na skutečné dokončení příkazu (`ExecutionTask`), ne na čas.
  - Zátěžově: třída 35× samostatně a celé UiTests 12× bez chyby.

- **Automatizace ruční kontroly (2026-10-22, architect):** ilustrace v tray menu při 100 %, nabídka obrázku s vlastní otočenou fotkou, záhlaví Diagnostiky a změna v menu po uložení ověří E2E testy T-069 (dialog ve složce testu z T-067); 150 % T-070. Kritérium se zaškrtne, až testy projdou. Ostrost při 150 % po novém přihlášení zůstává doporučená ruční kontrola (přehled pokrytí v T-065).
