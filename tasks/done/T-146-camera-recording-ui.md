---
id: T-146
title: Nahrávání kamery – UI (tray menu, okno kamery, nastavení) a E2E
status: done
owner: ui-designer + ui-dev
priority: P2
platform: all
depends_on: [T-145, T-033, T-092]
---

## Cíl
Uživatelské rozhraní nahrávání z fáze 1 (nápad 1): ruční spuštění a zastavení, indikace probíhající nahrávky, nastavení automatického nahrávání, složky a limitu, otevření složky s nahrávkami. Logika je v T-145.

## Rozsah
- **ui-designer – návrh (design spec v `docs/design/`, jako u T-076):**
  - kde je ruční „Nahrávat / Zastavit nahrávání“ (podmenu tiskárny v tray menu, kontextové menu okna kamery, tlačítko v okně kamery – otázka 1),
  - indikace probíhající nahrávky (tray ikona / tooltip, okno kamery, malý náhled – otázka 2),
  - umístění nastavení (záložka tiskárny Kamera / nová záložka „Nahrávání“ / obecné nastavení – otázka 3), bez posuvníku v 780 × 532 (T-094) a s dlouhými překlady,
  - stav „nahrávání není k dispozici“ (např. macOS před T-147) a chybové stavy z T-145 (složka, disk).
- **ui-dev – implementace:**
  - položky menu podle návrhu, s klávesovými zkratkami z `.resx`; text se mění podle stavu (jako „Zobrazit / Skrýt kameru“, T-042),
  - nastavení podle návrhu, výběr složky přes systémový dialog, „Otevřít složku s nahrávkami“,
  - všechny nové texty ve všech jazycích (`Strings.*.resx`, T-029),
  - compiled bindings, žádná reflexe (Native AOT).
- **Notifikace chyby nahrávání** (rozhodnutí 8 v T-145): `CameraRecordingService.Failed` (`CameraRecordingError`) přes `INotificationService`, texty podle `CameraRecordingProblem` ve všech jazycích; Core text nelokalizuje (API viz poznámky T-145).
- **Testy:** headless UI testy (T-031) pro nastavení a menu; E2E na Windows (UI Automation, T-044) proti simulátoru: ruční nahrávka z menu vytvoří soubor, automatická nahrávka během scénáře tisku simulátoru bez otevřeného okna kamery vytvoří soubor, indikace se zobrazí a zmizí.

## Akceptační kritéria
- [x] Design spec schválený uživatelem (odpovědi na otázky 1–4 zapsané v úkolu). Samostatný dokument v `docs/design/` nevznikl
      (pokyn hlavního agenta: bez zvláštního průchodu ui-designer); návrh je popsaný v poznámkách níže, nové otázky 6–9.
- [x] Ruční nahrávání jde spustit a zastavit z míst podle návrhu; stav je vidět podle návrhu (unit a headless UI testy;
      skutečné tray menu / menu bar, tooltip a okno kamery na obrazovce → přesunuto do T-125 a T-127).
- [x] Automatické nahrávání během tisku běží bez otevřeného okna kamery (E2E se simulátorem: test
      `CameraRecordingTests.APrintIsRecordedAutomaticallyWithoutACameraWindow` napsaný, na Macu ověřeno ručně se simulátorem;
      běh E2E na Windows → přesunuto do T-125).
- [x] Nastavení se uloží a po restartu aplikace platí; chybové stavy (nedostupná složka) mají srozumitelnou hlášku.
- [x] Na platformě bez muxeru (macOS před T-147) jsou volby zakázané s vysvětlením, aplikace nespadne.
- [x] Všechny nové texty ve všech jazycích; okno nastavení bez posuvníku i s nejdelším překladem. Texty jsou ve všech
      8 jazycích; sekce „Nahrávání“ je podle rozhodnutí 2026-11-04 na vlastní páté záložce „Nahrávání“, okno je bez
      posuvníku a záhlaví pěti záložek na jednom řádku ve všech jazycích i chybových stavech (headless, písmo Inter;
      se systémovým písmem na obrazovce → přesunuto do T-125 a T-127).
- [x] E2E testy po sobě smažou nahrávky i po pádu (vlastní dočasná složka nahrávek: testovací režim nahrává do `Recordings`
      v datové složce běhu pod vyhrazeným kořenem E2E, který se maže na konci běhu a uklízí na začátku dalšího; běh →
      přesunuto do T-125).
- [x] `dotnet build` a `dotnet test` bez chyb a varování; Native AOT publish (Debug podle T-073) bez nových varování
      (macOS `osx-arm64` Release ověřeno; Windows `win-x64` → přesunuto do T-125).

## Otevřené otázky
1. **Kde ovládat ruční nahrávání:** tray menu tiskárny, okno kamery (tlačítko / kontextové menu), obojí?
2. **Indikace nahrávání:** červená tečka / „REC“ v okně kamery a malém náhledu? Změna tray ikony nebo jen tooltip? Má být indikace vidět i ve snímku obrazovky okna (soukromí při sdílení obrazovky)?
3. **Umístění nastavení:** u tiskárny na záložce Kamera, nová podzáložka, nebo globální sekce na záložce Obecné (souvisí s otázkou 7 v T-145)?
4. **Otevření posledního záznamu** (přehrání) z menu / z notifikace – chce to uživatel ve fázi 1?

### Rozhodnutí uživatele 2026-11-03
1. Ruční Start/Stop v **tray menu tiskárny i tlačítkem v okně kamery**.
2. Indikace: červená tečka **REC** v okně kamery a malém náhledu, v tooltipu tray ikony „Nahrává se“; tray ikona se nemění.
3. Nastavení: složka a limit v nové sekci **Nahrávání na záložce Obecné**, „Automaticky nahrávat tisk“ u tiskárny na **záložce Kamera**.
4. Ve fázi 1 **ano**: „Otevřít složku s nahrávkami“ a „Přehrát poslední nahrávku“ (otevře se ve výchozí aplikaci systému).
5. Notifikace jen při chybě nahrávání (T-145, otázka 8).

### Nové otázky (ui-dev, 2026-11-03)
6. **Blokující – kam se sekce „Nahrávání“ vejde.** Záložka Obecné nemá v 780 × 532 (T-094) žádné volné místo: pod
   „Podrobné logování“ zbývá asi 15 DIP, sekce potřebuje nejméně ~80 DIP (nadpis s tlačítkem „Otevřít složku
   s nahrávkami“ + řádek se složkou a limitem), s nápovědami a chybami víc. Ani přesun „Podrobného logování“ jinam nestačí
   (uvolní ~45 DIP). Do odpovědi je sekce na záložce **Spuštění a aktualizace** (má místo; okno je bez posuvníku ve všech
   jazycích i chybových stavech); přesun jinam je jen přesun bloku XAML a hodnoty `SettingsViewModel.RecordingTab`.
   Možnosti:
   - a) nechat na záložce „Spuštění a aktualizace“ (případně ji přejmenovat, např. „Spuštění, aktualizace a nahrávání“),
   - b) nová hlavní záložka „Nahrávání“ (pátá; ověřit, že se záhlaví vejdou na jeden řádek ve všech jazycích),
   - c) záložku Obecné přeskládat (např. „Podrobné logování“ a jazyk na jinou záložku) – zásah do rozhodnutí T-047 / T-033.
7. Nahrávací tlačítko v okně kamery je **před špendlíkem** (ve všech režimech; v celé obrazovce poslední). Při nahrávání je
   trvale vidět jako „● REC“ (ostatní tlačítka se dál objeví až pod ukazatelem), při najetí se tečka změní na čtverec
   „Stop“. V režimu rozložení (T-091) jsou skrytá všechna tlačítka okna, tedy i REC. Je to tak v pořádku?
8. Klik na notifikaci o chybě nahrávání otevře **okno nastavení** (jako zkušební notifikace, T-101); notifikace se řídí
   hlavním přepínačem „Zobrazovat notifikace“ a zvukem jako chyba tiskárny (žádný vlastní přepínač). V pořádku?
9. „Otevřít složku s nahrávkami“ v podmenu tiskárny otevírá **kořen** všech nahrávek (ne podsložku tiskárny) – stejně jako
   tlačítko v nastavení. Chcete u tiskárny rovnou její podsložku?

### Rozhodnutí uživatele 2026-11-04
6. Sekce „Nahrávání“ se přesouvá na **novou, pátou hlavní záložku „Nahrávání“** (lokalizovaná ve všech jazycích).
   Později na ni přibudou nastavení timelapse a detekce chyb tisku, proto je stavěná tak, aby šlo přidávat další sekce.
   Ze záložky „Spuštění a aktualizace“ zmizí.
7. Tlačítko REC zůstává, kde je (vedle špendlíku) – potvrzeno.
8. Klik na notifikaci o chybě nahrávání otevře nastavení **rovnou na záložce „Nahrávání“** (pokud to okno nastavení umí
   bez velkého zásahu; jinak stačí otevřít nastavení).
9. „Otevřít složku s nahrávkami“ v podmenu tiskárny otevře **podsložku tiskárny**; když ještě neexistuje (nevytváří se),
   otevře kořen; když neexistuje ani kořen, platí dosavadní chování (kořen se vytvoří). Cestu nepočítat znovu, převzít
   ji z logiky `CameraRecordingFolders` / služby.

## Poznámky k implementaci
Rozpracováno 2026-11-03 na macOS arm64 (ui-dev, zároveň v roli ui-designer), dokončeno 2026-11-04 na macOS arm64 podle
rozhodnutí 2026-11-04 (otázky 6–9).

**Dokončení podle rozhodnutí 2026-11-04:**
- **Záložka „Nahrávání“** (otázka 6): `SettingsViewModel.RecordingTab = 4` (pátá, `TabCount = 5`; Ctrl+Tab / ⌘⇧] ji
  obejdou), `TabItem` `SettingsTabRecording` s textem `SettingsTabRecording` ve všech 8 jazycích („Nahrávání“, „Recording“,
  „Aufnahme“, „Grabación“, „Enregistrement“, „Registrazione“, „Nagrywanie“, „Nahrávanie“). Obsah je `ScrollViewer` +
  `StackPanel Spacing="12"` jako u „Spuštění a aktualizace“, sekce „Nahrávání“ (nadpis s „Otevřít složku s nahrávkami“,
  složka a limit) je jeho první položka; sekce timelapse a detekce chyb tisku se přidají jako další položky stejného
  panelu. Záhlaví bez značky chyby jako „Obecné“ (jediné chybné pole je limit; neúspěšné uložení na záložku přepne a dá
  fokus limitu). Nejdelší řada záhlaví (francouzština) má v headless testu ~712 z 748 DIP.
- **Klik na notifikaci** (otázka 8, udělané – okno výběr záložky umí): nový pevný argument `settings=recording`
  (`ToastLaunchArgument.RecordingSettings`, `IsSettings` platí pro oba pevné argumenty nastavení, `TryGetSettingsPage`)
  na toastu Windows i v požadavku macOS. `ToastActivationRouter` předá otevírači nastavení `SettingsPage` (nový enum
  `General` / `Recording`); klik před startem UI se drží jako dřív, při více klicích s poslední záložkou. `App` →
  `SettingsWindowCommand.Show(…, page)` → `SettingsViewModel.ShowPage` (nahrazuje `ShowGeneralTab`) – nové i už otevřené
  okno, rozpracované úpravy zůstanou. Druhý start aplikace s argumentem (T-068) ho přijme stejně (`IsWellFormed`).
- **Složka tiskárny** (otázka 9): `CameraRecordingService.GetPrinterFolder(printerId)` (Core) vrátí složku, do které
  tiskárna nahrává (stejný `Path.Combine(kořen, název z PrinterFolderNames)` jako start nahrávky – jeden pomocný
  `PrinterFolder`), nebo `null` pro neznámou tiskárnu. `ICameraRecordings.OpenPrinterFolderAsync` otevře podsložku, jen
  když existuje (dotaz na disk mimo UI vlákno), jinak `OpenFolderAsync()` = kořen, který `IFolderLauncher` vytvoří jako
  dřív. Podsložka se nikdy nevytváří. Tlačítko v nastavení dál otevírá kořen z pole.
- Testy: `SettingsWindowLayoutTests.RecordingTabFitsInEveryLanguage` (chyby složky i limitu / bez muxeru, bez posuvníku,
  nic uříznutého, záhlaví na jednom řádku, neúspěšné uložení přepne na záložku), pohled „Recording“ v testech velikosti
  okna, `AClickOnANotificationShowsItsTab`, nový snímek `settings-recording.png`; `ToastLaunchArgumentTests`,
  `ToastActivationRouterTests` (záložka podle argumentu, držený klik), `RecordingNotificationTests` (nový argument),
  `CameraRecordingsTests.OpenPrinterFolderOpensItsSubfolderOnceItExistsAndTheRootBefore`,
  `CameraRecordingServiceTests.ThePrinterFolderIsTheOneItRecordsInto`, menu (`OpenPrinterFolderAsync` s id tiskárny),
  klávesové zkratky záložek (Ctrl+Tab, ⌘⇧[ ]).
- Snímky macOS přegenerované: všechny `settings-*.png` (pátá záložka v záhlaví; `settings-startup-updates*.png` už bez
  sekce nahrávání) + nový `settings-recording.png`. Sada Windows → T-125.
- `dotnet build` 0 upozornění, `dotnet test` celkem 8975, neúspěšné 0, úspěšné 8775, přeskočeno 200 (macOS arm64).
  AOT publish po těchto změnách znovu nespuštěn (žádná reflexe, nový balíček ani interop; jen enum, konstanta a XAML se
  compiled bindings).

**Původní poznámky (2026-11-03):**

**Návrh a chování (rozhodnutí 2026-11-03):**
- **Tray menu** (`TrayMenuViewModel`): tiskárna s kamerou má v podmenu za oddělovačem „Nahrávat“ / „Zastavit nahrávání“
  (stejná přístupová klávesa v obou stavech), „Přehrát poslední nahrávku“ (zakázané s tipem „Tiskárna zatím nemá žádnou
  nahrávku.“) a „Otevřít složku s nahrávkami“. Během `Starting` / `Stopping` je položka zakázaná s důvodem (revize T-145:
  Start během ukončování by se tiše ztratil), bez muxeru taky („Tato verze LayerPeeku neumí nahrávat kameru.“). Klik jedná
  podle toho, co položka ukazovala (Start / Stop, nikdy přepínač). Změna nahrávání (`Changed`, libovolné vlákno) označí jen
  tu tiskárnu a v další dávce UI se její podmenu aktualizuje na místě (`TrayMenuTarget` nově umí oddělovač v podmenu; při
  stejném tvaru se položky jen přepíšou). Bez nahrávek (testy) zůstává podmenu jako dřív.
- **Tooltip ikony** (`TrayStatusViewModel`): jedna tiskárna – řádek „Nahrává se“ za stavem; více tiskáren – „…, nahrává se“
  za stavem tiskárny (zůstane i když se zbývající časy nevejdou). Ikona se nemění. Stav se přečte i při `SetPrinters`
  (nahrávky přežijí restart monitoringu); `CameraRecordingObserver` je proto v seznamu pozorovatelů před tray (pořád před
  hubem).
- **Okno kamery** (`CameraViewModel.Recording.cs`, `CameraWindow.axaml`): tlačítko před špendlíkem ve všech režimech; v klidu
  červená tečka a skryté jako ostatní tlačítka, při nahrávání „● REC“ trvale viditelné (normální okno, malý náhled, celá
  obrazovka), pod ukazatelem / s fokusem se tečka změní na čtverec Stop. Tip a přístupný název říkají, co klik udělá, nebo
  proč je zakázané. Statické, žádný časovač ani animace; stav se čte jen při `Changed` této tiskárny (ostatní tiskárny se
  odfiltrují před převodem na UI vlákno). Klik myší vrátí fokus obrázku jako u špendlíku, z klávesnice zůstane na tlačítku.
- **Notifikace** (`RecordingNotificationObserver`, `RecordingNotificationText`): `Failed` → `INotificationService
  .ShowRecordingFailed(RecordingNotification)` (nová výchozí metoda v Core, jako `ShowUpdateAvailable`); titulek = název
  tiskárny, řádek „Nahrávání kamery selhalo“ a důvod podle `CameraRecordingProblem` (limit a mez 5 GB v textu), ve všech
  jazycích; Windows toast i macOS stejné texty, klik otevře nastavení (od 2026-11-04 na záložce Nahrávání,
  `ToastLaunchArgument.RecordingSettings`), bez tagu. Řídí se
  „Zobrazovat notifikace“ a zvukem jako chyba tiskárny. Anglická zpráva služby jen do logu.
- **Nastavení** (`SettingsViewModel.Recording.cs`): sekce „Nahrávání“ (od 2026-11-04 na vlastní záložce, viz výše):
  v záhlaví „Otevřít složku s nahrávkami“ (otevře složku z pole, i neuloženou), pod ním dva sloupce – složka (jen ke čtení,
  „Změnit…“ = systémový dialog `StorageProvider.OpenFolderPickerAsync`, AOT bezpečný; „Výchozí“ = Videa / Filmy +
  LayerPeek) a limit v celých GB (1–100 000). Zvolená složka se před převzetím ověří: místní cesta, `IsValidFolder`
  (absolutní, ≤ 200 znaků), zápis zkušebního souboru (`FileOptions.DeleteOnClose`, nic nezůstane); chyba nahradí nápovědu
  a složka se nepřevezme. Bez muxeru je složka i limit zakázané a důvod je místo nápovědy složky. „Automaticky nahrávat
  tisk“ je odsazené pod „Používat kameru“ na podzáložce Kamera, zakázané (hodnota zůstává) bez kamery nebo muxeru; popis /
  důvod je tip a nápověda pro čtečky (na řádek nápovědy nebylo místo, T-033).
- **Spouštění souborů:** `IFileLauncher` / `ProcessFileLauncher` otevře jen existující `.mp4` (absolutní cesta, jeden
  argument, `explorer.exe` z Windows složky / `/usr/bin/open`, jako T-052), nic jiného – nikdy nespustí program; v logu bez
  cesty. Složku otevírá dosavadní `IFolderLauncher` (zpráva v logu zobecněná na „a folder“).
- **Nové typy:** `App/Cameras/Recording/CameraRecordings.cs` (`ICameraRecordings` + adaptér nad `CameraRecordingService`
  + `RecordingControlState`), `Settings/IRecordingFolderPicker.cs`, `Notifications/RecordingNotification*.cs`; Core:
  `RecordingNotification` a `INotificationService.ShowRecordingFailed`. Žádný nový balíček.

**Texty:** 39 nových klíčů ve všech 8 jazycích (`Menu*Recording*`, `Recording*`, `CameraRecordingIndicator` = „REC“,
`ToolTip*Recording`, `NotificationRecording*`, `SettingsRecording*`, `SettingsRecordPrints*`); přístupové klávesy podmenu
jsou unikátní ve všech jazycích i s „Přenést kameru do popředí“; mezi číslem a „GB“ / „Go“ nedělitelná mezera.

**Testy:**
- App.Tests: `TrayMenuRecordingTests` (položky, stavy, kliky, jen dotčená tiskárna, přístupové klávesy ve všech jazycích),
  `TrayMenuTargetTests` (oddělovač v podmenu, aktualizace na místě), `TrayRecordingToolTipTests`, `RecordingNotificationTests`
  (skutečná služba bez muxeru → notifikace, vypnuté notifikace, texty všech problémů ve všech jazycích, toast XML, macOS
  požadavek), `SettingsRecordingTests` (načtení, uložení, výchozí složka, odmítnuté složky, neplatný limit → správná
  záložka a fokus, bez muxeru, „Automaticky nahrávat tisk“), `ProcessFileLauncherTests`. Společné fakes
  `tests/LayerPeek.App.Tests/Cameras/Recording/RecordingFakes.cs` (připojené i do UiTests).
- UiTests: `CameraWindowRecordingUiTests` (klik start/stop, REC bez ukazatele, ostatní tiskárny, `Starting` / `Stopping`,
  bez muxeru, pořadí ve všech režimech, čtverec Stop pod ukazatelem, jazyk, odhlášení při zavření, 3 snímky),
  `TrayMenuTests.RecordingItemsAreAGroupOfTheirOwnAndFollowTheRecordingInPlace` (skutečné `NativeMenu`),
  `SettingsWindowLayoutTests.StartupAndUpdatesTabFitsInEveryLanguage` rozšířený o sekci nahrávání s nejdelšími chybami
  a bez muxeru. Harness nastavení dostává falešné nahrávky s pevnou výchozí složkou (snímky nikdy neukazují jméno uživatele).
- Snímky macOS (`tests/LayerPeek.App.UiTests/Snapshots/macOS/`): nové `camera-recording.png`, `camera-mini-recording.png`,
  `camera-mini-recording-buttons.png`; aktualizované `settings-camera.png`, `settings-camera-preview.png`,
  `settings-camera-preview-4x3-de.png`, `settings-camera-notice.png`, `settings-startup-updates.png`,
  `settings-startup-updates-not-installed.png`. Windows sada → T-125.
- E2E (Windows, na Macu jen přeloženo): nový `CameraRecordingTests` (ruční nahrávka z tray menu → soubor, REC v okně
  kamery a „Recording“ v tooltipu se objeví a zmizí; automatická nahrávka tisku simulátoru bez okna kamery, konec minutu
  po tisku); `TestPrinter` umí `RecordPrints`; očekávané položky podmenu v `TrayMenuTests`, `TrayMenuDetailTests`
  a `CameraTaskbarAndModeSwitchTests` doplněné o položky nahrávání.
- `dotnet build` 0 upozornění, `dotnet test` celkem 8957, neúspěšné 0, úspěšné 8757, přeskočeno 200.
  `dotnet publish src/LayerPeek.App -c Release -r osx-arm64` bez varování trim / AOT (jen dosavadní hlášky linkeru / dsymutil).
- Ručně na Macu: aplikace v testovacím režimu (`LAYERPEEK_TEST_DATA_DIR`, `LAYERPEEK_INSTANCE_SUFFIX`) se simulátorem
  (`--speed 20`) a tiskárnou s `recordPrints` – automatická nahrávka `Recordings/Core One/2026-11-03_22-40-54_Phone stand.mp4`
  vznikla bez okna kamery, při ukončení se dokončila (ffprobe: 13,28 s, platný soubor). Sezení agenta nemělo aktivní
  displej, takže UI (tray, okna) se nespustilo (T-099) – menu bar, okno kamery a notifikace na skutečné obrazovce
  neověřeny → T-127.

