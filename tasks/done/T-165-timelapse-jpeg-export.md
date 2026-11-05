---
id: T-165
title: Timelapse – export sekvence snímků JPEG
status: done
owner: core-dev + ui-dev
priority: P3
platform: all
depends_on: [T-160, T-161]
---

> **Ze spiku T-149, rozhodnutí uživatele 2026-11-04 (otázka 9):** export jednotlivých snímků timelapse jako JPEG
> (např. pro vlastní střih) je samostatný úkol s nízkou prioritou. Podrobnosti: `docs/timelapse-spike.md`, varianty A1 / A2.

## Cíl
Kromě MP4 (T-158, bez dekódování) může uživatel získat snímky timelapse jako sekvenci souborů JPEG. Na rozdíl
od MP4 to potřebuje dekódování a kódování JPEG. Funkce patří k timelapse, a je proto označená jako experimentální.

## Rozsah
- **Kodér JPEG:** podle spiku bez nové závislosti – Avalonia (`Bitmap.Save` + `JpegBitmapEncoderOptions`, Skia +
  libjpeg-turbo, už v AOT publish; varianta A1) v App za rozhraním v Core, nebo FFmpeg `mjpeg` (varianta A2, na macOS
  +232 kB v `build-ffmpeg.sh`). Výběr core-dev zdůvodní v poznámkách (licence a AOT jsou u obou ověřené ve spiku).
- **Zdroj snímků** podle otázky 1: dekódování klíčových snímků z hotového timelapse MP4 (jen při exportu, nic za tisku
  navíc), nebo ukládání JPEG během tisku (dekódování po celý tisk, dnes ~0,15 % jádra jen klíčové snímky + kopie BGRA).
- **Výstup:** podsložka vedle timelapse (např. `…_timelapse_jpg/0001.jpg`), počítá se do limitu a rotace nahrávek
  (T-160). Kvalita JPEG (návrh ~85).
- **UI:** spuštění exportu (otázka 1), průběh / zrušení, notifikace, štítek „Experimentální“ (T-161), texty ve všech
  `Strings.*.resx`.

## Akceptační kritéria
- [x] Export z timelapse (fixture / simulátor) vytvoří tolik JPEG, kolik má timelapse snímků, ve správném pořadí
      a rozměru (test).
- [x] Ustálený stav bez alokací na snímek tam, kde to kodér dovolí; zrušení nezanechá napůl zapsaný soubor (test).
- [x] Složka exportu se počítá do limitu a rotace (test).
- [x] UI akce, štítek „Experimentální“, texty ve všech jazycích (headless UI test). Ruční ověření dialogu, průběhu
      a notifikací na obou OS → přesunuto do T-125 a T-127.
- [x] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish bez varování trim / AOT. Část Windows (testy, E2E,
      publish `win-x64`) → přesunuto do T-125.

## Otevřené otázky
Všechny zodpovězené (rozhodnutí uživatele 2026-11-04):
1. **Kdy se snímky JPEG vytváří:** jen dodatečně exportem z hotového timelapse MP4 na vyžádání, za tisku nic navíc.
2. **Kde se export spouští:** tray menu → sekce nahrávání tiskárny: „Exportovat timelapse do JPEG…“ otevře systémový
   dialog pro otevření souboru ve složce nahrávek (podsložka tiskárny, když existuje, jinak kořen), filtrovaný na
   timelapse MP4. Funguje pro jakýkoli starší soubor, žádný nový seznam nahrávek. T-164 přidá vedle stejně
   „Vytvořit timelapse z nahrávky…“ (dialog je znovu použitelný).
3. **Kodér:** A1 (Avalonia / Skia v App za rozhraním v Core) nebo A2 (FFmpeg `mjpeg`) vybere agent, přednost má varianta
   bez nativní velikosti navíc, pokud A1 nemá skutečnou nevýhodu. Kvalita ~85.
4. **Výstup:** podsložka vedle timelapse (`<název>_jpg/0001.jpg`), do limitu a rotace (pravidla T-160). Zrušení nesmí
   nechat napůl zapsaný soubor (zápis do dočasného místa + přejmenování). Průběh / zrušení, notifikace na konci (úspěch
   i chyba, krátce), štítek „Experimentální“, všechny texty ve všech jazycích.
5. **Dva exporty najednou:** nespouštět paralelně, nejjednodušší rozumné chování vybere agent (viz poznámky).
6. **Klik na notifikaci (vyřešeno, rozhodnutí uživatele 2026-11-04):** klik na notifikaci o konci exportu JPEG i timelapse
   z nahrávky (T-164), při úspěchu i chybě, otevře složku nahrávek tiskárny – stejná akce jako položka tray menu „Otevřít
   složku s nahrávkami“. Argument nese jen ID tiskárny (`recordings=<id>`, žádnou cestu), viz „Navazující opravy“.

## Poznámky k implementaci
- **Kodér: varianta A1** (Avalonia / Skia + libjpeg-turbo). Nic nativního navíc. A2 by přidala 232 kB do `libavcodec`
  na macOS a přebuild FFmpeg na každém Macu (jiný klíč cache), navíc kontext enkodéru a převod do `yuvj420p`. A1 nemá
  skutečnou nevýhodu: potřebuje platformu Avalonie, ta v App vždy běží. Licence (Avalonia MIT, SkiaSharp MIT, Skia BSD-3,
  libjpeg-turbo IJG / BSD-3 / zlib) jsou v aplikaci a v přehledu licencí už dnes. AOT ověřené publishem. Žádný nový
  balíček NuGet. Rozhraní v Core: `ICameraJpegSequenceEncoder(Factory)` (sekvence, buffery se drží mezi snímky; stávající
  `ICameraImageEncoder` pro jednotlivé snímky poplachu zůstal). Implementace `App/Cameras/Recording/AvaloniaJpegSequenceEncoder`:
  jedna `WriteableBitmap` na celý export (nová jen při změně rozměru), `Save` přímo do znovu použitého `MemoryStream`.
  Kvalita 85 (`CameraTimelapseJpegExporter.DefaultQuality`).
- **Core:** `Cameras/Timelapse/CameraTimelapseJpegExporter` (stav `CameraTimelapseExportStatus`, výsledek
  `CameraTimelapseExportResult` s `CameraTimelapseExportProblem`). Soubor se čte přes `ICameraFeedFactory` (`file://`)
  dvakrát. Nejdřív jen pakety, bez otevření dekodéru: počet snímků pro průběh a šířku čísel (`0001.jpg`, od 10 000 snímků
  pět číslic). Potom snímek po snímku: dekódovaný buffer → kodér → jedno volání `RandomAccess.Write` do nového souboru.
  Mimo export neexistuje žádné vlákno, úloha ani buffer. Práce běží na thread poolu a na vlákně dekodéru (inline
  pokračování). `Changed` nejvýš jednou za celé procento, `Completed` až po návratu do klidu. Odběratel, který vyhodí
  výjimku, se jen zaloguje. Do logu jdou jen názvy souborů, ne cesty.
- **Bez napůl zapsaných souborů:** snímky jdou do dočasné složky `.<název>_jpg.partial` vedle timelapse
  (`CameraRecordingFolders.UnfinishedJpegExportFolderName`, na macOS skrytá). Po posledním snímku se přejmenuje na
  `<název timelapse bez přípony>_jpg`; obsazený název (dřívější export) dostane `-2`, `-3`… a starý export se nemění.
  Zrušení, chyba zápisu i chyba kodéru dočasnou složku smažou. Dočasnou složku po pádu nahradí příští export téhož souboru,
  jinak ji po 10 min smaže rotace.
- **Poškozený soubor:** oříznutý timelapse (pád aplikace během tisku) vyexportuje snímky před místem poškození (test:
  60 % z 8 snímků → 4 snímky). Soubor bez snímků, náhodná data nebo prázdný soubor skončí `Unreadable` bez pozůstatků.
  Nic z toho nevyhodí výjimku ven. Soubor, jehož název neodpovídá timelapse (`IsTimelapseFileName`), se odmítne
  (`NotATimelapse`). Na macOS totiž dialog filtruje jen podle přípony a export běžné 25fps nahrávky by vytvořil desetitisíce
  JPEG.
- **FFmpeg:** pro `file://` je vedle `h264` povolený i demuxer `mov` (MP4). Na macOS je v buildu už dnes přes závislosti
  demuxeru RTSP, BtbN ho má: `build-ffmpeg.sh` se nemění. Protokol zůstává jen `file`, odkazy uvnitř MP4 na jiné soubory
  nebo URL se nenásledují, dekodér dál jen `h264`.
- **Limit a rotace:** `CameraRecordingStorage.FindRecordings` hlásí v označené složce tiskárny exportní složku jako jednu
  položku (`CameraRecordingFile.IsFolder`, velikost = očíslované `*.jpg`, čas = nejnovější snímek). Rotace ji řadí podle
  času v názvu (= začátek timelapse, tedy hned za její MP4) a maže celou: jen očíslované JPEG a systémová metadata, pak
  prázdnou složku. Soubory uživatele ve složce zůstanou i se složkou. Nedokončená složka (`IsUnfinished`) se nepočítá a po
  10 min se smaže jako pozůstatek. Během exportu drží nové `ICameraRecordingFileGuard.Hold` (implementuje
  `CameraRecordingService`) zdrojový timelapse i dočasnou složku mimo rotaci. Uvolnění vyžádá rotaci, takže se nová složka
  hned započítá. Dočasná složka se během exportu do limitu nepočítá (trvá sekundy). Export timelapse mimo kořen nahrávek
  (soubor jinde na disku) jde vedle souboru, ale do limitu se nepočítá: rotace bere jen označené složky aktuálního kořene.
- **Dva exporty najednou (otázka 5):** `Start` při běžícím exportu vrátí `false`. V UI se to nestane: během exportu je
  místo položky u všech tiskáren „Zrušit export do JPEG (42 %)“ (stejná klávesa). Druhý klik, dokud je dialog otevřený,
  nic neudělá.
- **UI:** položka „Exportovat timelapse do JPEG… (experimentální)“ v podmenu tiskárny za „Přehrát poslední timelapse“
  (`TrayMenuCommand.ExportTimelapseJpeg` / `CancelTimelapseJpegExport`, `TimelapseExportControlState`). Bez exportéru nebo
  dialogu je zakázaná s důvodem. `ICameraRecordings` má `IsTimelapseExportAvailable`, `TimelapseExport`,
  `TimelapseExportChanged`, `ExportTimelapseToJpegAsync(printerId)` a `CancelTimelapseExport()`. Změna exportu označí
  všechny tiskárny menu jedním odloženým překreslením, platformní položky se mění na místě. Klávesy: E (cs, sk, en, es,
  it, pl, de „_exportieren“), X ve francouzštině (E má „Enregistrer“).
- **Dialog (`App/Cameras/Recording/IRecordingFilePicker`, `RecordingFilePickRequest`, znovu použitelné pro T-164):**
  tray aplikace nemá okno, dialog Avalonie ale potřebuje `TopLevel`. Na macOS by ho Avalonia připojila jako list
  (`beginSheetModalForWindow`, ověřeno ve zdrojích Avalonia `StorageProvider.mm`), u neviditelného okna by nebyl vidět.
  Proto `Platform/MacOS/MacRecordingFilePicker`: volný `NSOpenPanel` (`beginWithCompletionHandler:` s globálním blokem přes
  stávající `ObjCRuntime` / `ObjCBlocks`, bez nové závislosti, AOT bez varování). Před ním se aplikace aktivuje, filtr je
  `UTType` z přípony `mp4` (starší systémy `setAllowedFileTypes:`). Jinde `StorageProviderRecordingFilePicker`: dialog
  Avalonie s vlastníkem, který se nikdy neukáže (Windows `IFileOpenDialog`), filtr `*_timelapse.mp4` a `*_timelapse-*.mp4`.
  Start ve složce tiskárny, když existuje, jinak v kořeni, jinak kde chce systém. Ukázat dialog v sezení agenta nešlo
  (žádný aktivní displej, Avalonia.Native nespustí render timer), ruční ověření je v T-127 a T-125.
- **Notifikace:** `TimelapseExportNotificationObserver` (pozorovatel monitoringu kvůli jménům tiskáren a nastavení) převede
  `Completed` na nové `INotificationService.ShowTimelapseExportFinished(TimelapseExportNotification)` (výchozí implementace
  nic). Zrušení se nehlásí. Zvuk jen s „všemi upozorněními“ (informace). Texty `TimelapseExportNotificationText`: titulek
  = tiskárna (bez ní „LayerPeek“), „Timelapse exportován do JPEG“ + „Snímků: N, ve složce vedle timelapse.“, nebo „Export
  timelapse do JPEG selhal“ + důvod. Klik otevře složku nahrávek tiskárny (otázka 6, doplněno
  v „Navazující opravy“); cesta se do argumentu nedává (nedůvěryhodný vstup z OS).
- **Texty:** 14 nových řetězců ve všech 8 `Strings.*.resx` (en s komentáři). Štítek „(experimentální)“ je v textu
  položky menu a v titulku dialogu.
- **Výkon (Mac mini M2 Pro, Debug, fixture Buddy3D 1280×720):** 300 snímků za 2,41–2,45 s, tj. **8,0–8,2 ms na snímek**
  celé cesty (počítání paketů, otevření, dekódování, převod do BGRA, JPEG, zápis), ~162 kB na JPEG při kvalitě 85. Samotný
  kodér na nejhorším obrazu (šum): 12,2 ms a **600 B spravovaných alokací na snímek** (obaly Avalonie / Skia, kodér sám
  pracuje v nativní paměti). Vlastní smyčka exportu (bez kodéru): medián **464 B na snímek** (název a cesta souboru,
  `SafeFileHandle`). Dekódovaný buffer, bitmapa, JPEG buffer a kodér se používají znovu. Paměť během exportu: buffer
  dekodéru 3,7 MB + bitmapa 3,7 MB + JPEG buffer 256 kB; v klidu nic.
- **Testy:** Core – `CameraTimelapseJpegExporterTests` (počet, pořadí a rozměr, průběh po procentech, číslice od 10 000,
  `-2`, jeden export najednou, zrušení bez pozůstatků, dispose, poškozený konec, žádné snímky, nečitelný soubor,
  `NotATimelapse`, chybějící soubor, bez dekodéru, chyba kodéru, nezapisovatelná složka, pozůstatek po pádu, odběratel
  s výjimkou, alokace na snímek, cesty s `#` / `%` / diakritikou) a `JpegExportFolderTests` (názvy, nalezení jako jedna
  položka, limit a rotace s nahrávkami, soubory uživatele zůstanou, nedokončená složka, cizí složka se nikdy nemaže,
  `Hold` služby nahrávání). Camera.FFmpeg – `FFmpegTimelapseJpegExportTests` (skutečné MP4 z klíčových snímků fixture
  přes demuxer `mov`: 7 snímků v pořadí podle pixelů, oříznutý soubor, náhodná data, prázdný soubor, zrušení na vlákně
  dekodéru). App – `CameraRecordingsExportTests` (počáteční složka dialogu, filtr, export vybraného souboru, jeden dialog,
  žádný dialog během exportu), `TimelapseExportNotificationTests` (pozorovatel, zvuk, vypnutá upozornění, texty ve všech
  jazycích, toast bez `launch`, notifikace macOS), `TrayMenuRecordingTests` (nová položka, zrušení s procentem u všech
  tiskáren, zakázaná položka, unikátní klávesy ve všech jazycích). UiTests – `TimelapseJpegExportUiTests` (kodér Avalonie:
  sekvence, změna rozměru, řádky s odsazením, stejný snímek = stejný soubor; celý export skutečného timelapse 12 snímků
  1280×720 přes FFmpeg + Avalonii), `TrayMenuTests.TheJpegExportItemStartsTheExportAndShowsItsProgressInPlace` (nativní
  menu). E2E (jen Windows) `TrayMenuDetailTests`, `TrayMenuTests` a `CameraTaskbarAndModeSwitchTests` upravené o novou
  položku → spustí se v T-125.
- **Ověření:** `dotnet build` 0 varování, 0 chyb. `dotnet test`: celkem 9625, neúspěšné 0, úspěšné 9425, přeskočeno 200
  (E2E a testy jen pro Windows). `dotnet publish src/LayerPeek.App -c Release -r osx-arm64` prošel bez varování trim /
  AOT (žádné `IL*`). Ve výpisu jsou jen dosavadní varování nativního linkeru k ladicím informacím (`dsymutil`, modulová
  cache Swiftu).
- Uzavřeno 2026-11-04: zbývající ruční ověření přesunuto do T-125 (Windows: testy, E2E, publish, dialog s hlavním panelem
  a toast) a T-127 (macOS: `NSOpenPanel`, průběh v otevřeném menu, notifikace).

## Review
Review qa-reviewer 2026-11-04: bez blokujících nálezů. Opraveno: `CameraRecordings.ExportTimelapseToJpegAsync` při prázdné
odpovědi dialogu nic nespustí (dřív by `Start` vyhodil výjimku v nesledované úloze z menu), test
`AnEmptyAnswerOfTheDialogStartsNothingAndDoesNotThrow`. Neblokující:
- `MacRecordingFilePicker.OnCompleted` uvolní panel (`release`) přímo v jeho dokončovacím handleru; bezpečnější je
  `autorelease` (uvolní se až po návratu z AppKitu). Ověřit při ručním ověření v T-127.
- `Hold` porovnává cesty doslova (bez rozlišení velikosti písmen): cesta z dialogu v jiném tvaru než kořen nahrávek
  (symbolický odkaz, namapovaný disk vs. UNC) zdroj nechrání; následek je jen řízené selhání exportu (`Unreadable`).
- Exportní složka, ve které zůstaly jen soubory uživatele, se po 10 min při každé rotaci znovu „smaže“ (`TryDelete` vrátí
  `true`, složka zůstane) – jen opakovaný záznam v logu.

## Navazující opravy (po revizi, 2026-11-04, větev `feature/review-followups`)
- **`MacRecordingFilePicker.OnCompleted`:** panel se v dokončovacím handleru už neuvolňuje `release`, ale `autorelease`
  (mimo vlastní autorelease pool, v `finally`): uvolní se, až se vyprázdní pool AppKitu kolem události, tedy po návratu
  z handleru. Panel se uvolní právě jednou. Bez displeje to nejde ukázat testem (ruční ověření dialogu zůstává v T-127).
- **Exportní složka jen se soubory uživatele:** `CameraRecordingStorage.FindRecordings` ji už nehlásí (nejsou v ní žádné
  snímky LayerPeeku, nic se nepočítá ani nemaže) a `TryDelete` pro exportní složku vrátí `true`, jen když odstranil snímek
  nebo složku. Rotace ji tak už každých 10 min znovu „nesmaže“. Složka jen se systémovými metadaty (`.DS_Store`) dál zmizí
  jako prázdný zbytek. Testy `JpegExportFolderTests.AnExportFolderWithOnlyFilesOfTheUserLeftIsNotDeletedAgainEveryRotation`,
  `AnExportFolderWithOnlySystemMetadataIsStillAnEmptyLeftover`. Rozhoduje počet snímků, ne jejich velikost: prázdný
  snímek (0 B) po pádu vedle souboru uživatele se dál najde a smaže (revize QA, test `EmptyFramesNextToFilesOfTheUserAreStillRemoved`).
- **`Hold`:** cesty se při držení normalizují stejně jako kořen nahrávek (`Path.GetFullPath`, bez koncového oddělovače),
  takže `..`, `.`, zdvojený nebo koncový oddělovač odpovídají cestě z výčtu rotace; porovnání dál bez rozlišení velikosti
  písmen. Symbolické odkazy se nerozlišují. Test `JpegExportFolderTests.AHeldPathInAnotherEquivalentFormIsKeptToo` (soubor
  i složka).
- **Klik na notifikaci (otázka 6):** nový argument `ToastLaunchArgument.TryForRecordingsFolder` / `TryGetRecordingsFolderPrinterId`
  (`recordings=` + kódované ID, stejně přísné jako `camera=`, přijímá ho i `IsWellFormed`). Toast Windows
  (`ToastContentBuilder.BuildTimelapseExport`) i notifikace macOS ho nesou (bez tiskárny nebo s příliš dlouhým ID žádný).
  `ToastActivationRouter` ho pro nakonfigurovanou tiskárnu předá akci `ICameraRecordings.OpenPrinterFolderAsync` (stejná
  jako v tray menu, získaná až při kliku, bez UI); odstraněná tiskárna nic neotevře, dvojí hlášení kliku otevře jednou.
  Testy `TimelapseNotificationActivationTests`, upravené `TimelapseExportNotificationTests`.
- **Nestabilní alokační testy (revize QA):** `FFmpegCameraFeedAllocationTests.SteadyStateFramesThroughTheHubDoNotAllocate`
  pod zátěží padal (≈3,7 MB = třetí buffer `CameraFrameExchange`, který dostane pixely až po několika odběrech; hladovějící
  odběratel v zahřátí nic neodebral). Zahřátí teď čeká na 3 odebrané snímky, měří se 30 snímků a průchod, kterému dojde
  fixture (konec streamu, reconnect), se opakuje. `PrintFailureDetectionAllocationTests.ASampleDoesNotAllocate` (360–600 B)
  zachytil alokace runtime při vzniku/rušení vlákna thread poolu; průchod, při kterém se změnil `ThreadPool.ThreadCount`,
  se opakuje (nejvýš 12, aspoň jeden platný). Záruka 0 B na snímek/vzorek zůstává.
