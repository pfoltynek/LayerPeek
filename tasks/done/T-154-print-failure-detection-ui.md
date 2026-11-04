---
id: T-154
title: Detekce chyb tisku – UI (nastavení detektorů, notifikace poplachu, falešný poplach)
status: done
owner: ui-designer + ui-dev
priority: P2
platform: all
depends_on: [T-153, T-033]
---

## Cíl
Uživatelské rozhraní detekce chyb tisku (nápad 3): zapnutí / vypnutí každého typu detektoru a jeho citlivost, notifikace poplachu se snímkem a možnost označit poplach jako falešný. Logika je v T-153, první detektor v T-155.

## Rozsah
- **ui-designer – návrh (design spec v `docs/design/`):**
  - umístění nastavení detektorů (otázka 1): seznam typů detektorů (rozšiřitelný, nové typy přibudou bez přepracování UI – po vzoru záložky Widgety, T-092), u každého zapnuto / vypnuto, citlivost a krátký popis, co detektor umí a neumí,
  - vysvětlení, že detekce běží na pozadí jen během tisku a jen s kamerou,
  - **editor výřezu podložky (rozhodnutí uživatele 2026-11-04):** v nastavení tiskárny obdélník nakreslený nad náhledem kamery (náhled T-034), přesun a změna velikosti myší, tlačítko „Celý snímek“ (výchozí) a u Core One „Výchozí pro Core One“ (předvyplněný výřez z T-153). Ovládání i klávesnicí a přístupné popisky (UI Automation). Výřez se ukládá v normalizovaných souřadnicích (model T-153),
  - **interval vzorkování 2–5 s** (výchozí 2 s, T-153) – ovládací prvek a umístění navrhne ui-designer (u tiskárny, nebo globálně – otázka 1),
  - notifikace poplachu (text, snímek v toastu – otázka 2) a kde je akce „Falešný poplach“ (otázka 3),
  - bez posuvníku v 780 × 532 (T-094) a s dlouhými překlady.
- **ui-dev – implementace:** nastavení, editor výřezu podložky, interval vzorkování, notifikace (Windows toast i macOS `UNUserNotificationCenter` – stejné texty jako u ostatních upozornění, T-010 / T-020), akce „Falešný poplach“, nové texty ve všech jazycích (T-029), compiled bindings.
- **Testy:** headless UI testy nastavení, E2E na Windows s falešným detektorem v testovacím režimu (jen Debug, jako T-067), který vyvolá poplach během scénáře tisku simulátoru.

## Akceptační kritéria
- [x] Design spec schválený uživatelem (odpovědi na otázky zapsané v úkolu). *Odpovědi 2026-11-04 jsou zapsané níže, návrh
      podle nich je v `docs/design/print-failure-detection-ui.md`; prohlédnutí návrhu a snímků (`settings-detection*.png`,
      `settings-recording.png`) uživatelem → přesunuto do T-127.*
- [x] Každý typ detektoru jde zapnout / vypnout a nastavit citlivost; nastavení se uloží a po restartu platí.
- [x] Editor výřezu podložky nad náhledem kamery: nakreslení, přesun a změna velikosti obdélníku myší i klávesnicí, „Celý snímek“ a u Core One „Výchozí pro Core One“. Výřez se uloží u tiskárny a po restartu platí (headless UI test s falešným snímkem, převod souřadnic náhledu na normalizované souřadnice i při jiném poměru stran a letterboxu).
- [x] Interval vzorkování jde nastavit jen v rozsahu 2–5 s (test).
- [x] Poplach zobrazí notifikaci podle návrhu (E2E na Windows; macOS → T-127). *E2E `PrintFailureAlarmTests` napsaný,
      na Macu jen přeložený: běh → přesunuto do T-125; macOS → přesunuto do T-127.*
- [x] „Falešný poplach“ funguje z místa podle návrhu a uloží záznam (T-153). *Unit testy hotové; v toastu na Windows (E2E)
      → přesunuto do T-125, na macOS → přesunuto do T-127.*
- [x] Nové texty ve všech jazycích; okno nastavení bez posuvníku i s nejdelším překladem.
- [x] `dotnet build` a `dotnet test` bez chyb a varování; Native AOT publish bez nových varování. *macOS hotovo; Windows
      (test, `win-x64` publish, snímky) → přesunuto do T-125.*

### Rozhodnutí uživatele 2026-11-04
- **Umístění (otázka 1, 8):** u tiskárny (podzáložka vedle Kamery) zapnutí detekce, citlivost a editor výřezu; globálně na
  záložce „Nahrávání“ nová sekce „Detekce chyb tisku“: hlavní vypínač, režim opakování poplachu (výchozí jednou na tisk
  a typ, nebo opakovat, dokud trvá), ukládání snímků poplachů (výchozí zapnuto, jde vypnout) a interval vzorkování 2–5 s
  (globální, jak ho drží T-153).
- **Výchozí stav (otázka 4):** po instalaci detekce vypnutá.
- **Notifikace (otázky 2, 3, 5, 9):** toast se snímkem; klik otevře okno kamery dané tiskárny; tlačítko „Falešný poplach“
  přímo v toastu uloží lokální záznam (citlivost se nemění). Na Windows potřebuje obrázek toastu soubor: s vypnutým
  ukládáním snímků se JPEG zapíše do dočasného souboru ve složce mezipaměti aplikace a později smaže (omezeně, jen pár
  posledních) – zdokumentovat. macOS: příloha `UNNotificationAttachment` (také ze souboru) stejným způsobem. Zvuk stejný jako
  u chyby tisku.
- **Editor výřezu (otázky 7, 10):** obdélník nad náhledem kamery v nastavení tiskárny, tlačítka „Celý snímek“ a „Výchozí
  pro Core One“ (předvyplnění podle modelu jako T-153). Bez kamery poslední uložený snímek: jeden malý snímek na tiskárnu
  levně (zmenšený JPEG v mezipaměti aplikace, obnovený jen tehdy, když editor nebo test kamery snímek dostane – žádná práce
  na pozadí). Volbu zapsat.
- **Popis detektoru v UI** (ze spiku): „Změna na podložce“ pozná zmizení nebo posun dílu a novou hmotu na podložce;
  nepozná kvalitu první vrstvy, problémy na trysce ani chyby uvnitř dílu; u tmavého filamentu na černém plátu méně
  spolehlivý (známé omezení – označit); běží jen během tisku a se svítícím osvětlením komory.
- **Automatické pozastavení (T-157)** je odložené – žádné UI.
- Další produktová otázka, kterou rozhodnutí nepokrývají: neodhadovat, zapsat do Otevřených otázek, mezitím nejkonzervativnější
  varianta.

### Rozhodnutí uživatele 2026-11-04 (předáno při T-153)
- Po instalaci je detekce **vypnutá** (hlavní vypínač i zapnutí u tiskárny).
- Nastavení u tiskárny (vedle Kamery): zapnutí, citlivost, výřez. Globálně na záložce „Nahrávání“: hlavní vypínač, režim opakování poplachu a ukládání snímků poplachů.
- Toast poplachu ukáže snímek, klik otevře okno kamery, akce „Falešný poplach“ je přímo v toastu. Zvuk stejný jako u chyby tisku.
- Editor výřezu použije při nedostupné kameře poslední uložený snímek.

## Otevřené otázky
1. **Umístění nastavení:** *vyřešeno 2026-11-04, viz výše (interval vzorkování: otázka 8)* Původně: u tiskárny (podzáložka vedle Kamera), nebo globálně? Citlivost u tiskárny, nebo globálně?
2. **Notifikace:** *vyřešeno 2026-11-04: se snímkem, klik otevře okno kamery* Původně: se snímkem poplachu v toastu (na Windows jde obrázek v toastu jen ze souboru / URI), nebo jen text? Klik na notifikaci: otevřít okno kamery, snímek, nebo web PrusaLink?
3. **Falešný poplach:** *vyřešeno 2026-11-04: akce přímo v toastu (historie poplachů nerozhodnuta)* Původně: tlačítko přímo v notifikaci (akce toastu), v okně kamery, nebo v historii poplachů? Má aplikace mít seznam / historii poplachů?
4. **Výchozí stav detektorů:** *vyřešeno 2026-11-04: detekce po instalaci vypnutá* Původně: po instalaci vypnuté (uživatel je musí zapnout), nebo první detektor zapnutý?
5. **Zvuk** *vyřešeno 2026-11-04: stejný jako u chyby tisku* Původně: poplachu: stejný jako u chyby tisku, nebo výraznější?
6. ~~**Výřez podložky**~~ – *vyřešeno 2026-11-04 (otázka 8 v T-152):* editor výřezu patří do tohoto úkolu, už do první verze detekce, protože uživatel podporuje různé tiskárny a držáky kamery.
7. **Náhled bez spojení:** *vyřešeno 2026-11-04: s posledním uloženým snímkem (kde se ukládá: otázka 10)* Původně: má editor výřezu fungovat i s posledním uloženým snímkem, když kamera právě není dostupná, nebo jen s živým náhledem?
8. ~~**Interval vzorkování – u tiskárny, nebo globálně?**~~ *vyřešeno 2026-11-04: globálně na záložce Nahrávání.* Původně: (z T-153, 2026-11-04) T-153 ho drží globálně (`AppSettings.Detection.SampleInterval`, 2–5 s, výchozí 2 s), protože rozhodnutí 2026-11-04 ho nepřiřadilo ani k tiskárně, ani ke globálním volbám. Přesun k tiskárně je malá změna modelu a mapování nastavení.
9. ~~**Obrázek v toastu bez uloženého snímku**~~ *vyřešeno 2026-11-04: dočasný soubor v mezipaměti, omezeně, viz Poznámky.* Původně: (z T-153): s vypnutým „Ukládat snímky poplachů“ má poplach snímek jen v paměti (`PrintFailureAlarm.SnapshotJpeg`, `SnapshotPath` = `null`). Toast na Windows bere obrázek jen ze souboru nebo URI. Zapsat ho dočasně (např. do datové složky a po zavření toastu smazat), nebo toast bez obrázku?
10. ~~**Poslední snímek pro editor výřezu**~~ *vyřešeno 2026-11-04: zmenšený JPEG na tiskárnu v mezipaměti, viz Poznámky.* Původně: (z T-153): detekce drží jen šedotónové vzorky 320×180 a nic neukládá mimo poplachy. Poslední snímek pro editor (otázka 7) by mohl ukládat náhled v nastavení (T-034) při zavření, nebo by šlo použít nejnovější snímek poplachu ze složky tiskárny. Rozhodne návrh T-154; infrastruktura T-153 pro to nic neukládá.
11. **Načíst snímek automaticky?** (neblokuje, 2026-11-04) Editor se teď ke kameře sám nepřipojuje (nejkonzervativnější:
    žádné spojení jen kvůli otevření podzáložky). Ukáže snímek testu kamery z okna, jinak poslední uložený snímek, jinak
    černou plochu; nový snímek dá tlačítko „Načíst snímek“. Má se při prvním otevření podzáložky Detekce snímek z kamery
    načíst sám (jedno krátké spojení, zrušené zavřením okna)?
12. **Potvrzení „Falešný poplach“?** (neblokuje) Po kliknutí se teď nic neukáže (záznam se uloží, v logu je řádek). Má
    přijít krátké potvrzení (např. nahrazení toastu textem „Uloženo jako falešný poplach“)? A když aplikace poplach už
    nezná (po restartu), má to uživateli říct?
13. **Historie poplachů** (neblokuje, z otázky 3): seznam poplachů v aplikaci zatím není. Chce ho uživatel (kde: okno kamery,
    Diagnostika)?
14. **Pořadí podzáložek** (neblokuje): „Detekce“ je čtvrtá, za „Okno kamery“ (indexy stávajících podzáložek se nemění).
    Má být hned za „Kamera“?
15. **Obrázek toastu na Windows** (neblokuje): vložený obrázek pod textem (celý snímek v poměru stran kamery). Alternativa je
    „hero“ obrázek nad textem, který Windows ořízne na 2 : 1. Platí vložený?
- Podklady z T-153 pro UI: nastavení `PrintFailureDetectionSettings` (globální: `Enabled`, `AlarmRepeat`, `SaveSnapshots`, `SampleInterval`) a `PrinterDetectionSettings` (u tiskárny: `Enabled`, `Region`, `Detectors[id]` → `Enabled`, `Sensitivity` 0–100), výchozí výřez `PrinterDetectionSettings.RegionFor(printer)` (`DetectionRegion.CoreOneBed` pro tlačítko „Výchozí pro Core One“), seznam detektorů `PrintFailureDetectionService.Detectors`, notifikace `INotificationService.ShowPrintFailure(PrintFailureNotification)`, akce „Falešný poplach“ `TryGetAlarm(id)` a `ReportFalseAlarmAsync(alarm)`.

Podklad ze spiku T-152 pro popis detektoru v UI: „Změna na podložce“ pozná zmizení nebo posun dílu a novou hmotu na podložce. Nepozná kvalitu první vrstvy, problémy na trysce ani chyby uvnitř dílu. U tmavého filamentu na černém plátu je méně spolehlivý (známé omezení, řešení později v T-163). Běží jen během tisku a se svítícím osvětlením komory.

## Poznámky k implementaci
Implementováno 2026-11-04 na macOS arm64 (ui-dev, zároveň v roli ui-designer), větev `feature/T-154-print-failure-detection-ui`.
Návrh: `docs/design/print-failure-detection-ui.md`, architektura: oddíl „Detekce chyb tisku – UI“ v `docs/architecture.md`.

**Nastavení:**
- **Podzáložka Detekce** (`SettingsViewModel.DetectionPrinterTab = 3`, `PrinterTabCount = 4`, `PrinterEditorViewModel.Detection.cs`):
  „Hlídat tisk této tiskárny kamerou“, jedna nápověda podle stavu (kamera vypnutá / globálně vypnuto / kdy detekce běží),
  editor výřezu 256 × 144 DIP s „Načíst snímek“ u nadpisu, „Celý snímek“ a jen u Core One „Výchozí pro Core One“; sloupec
  „Co hledat“ s položkou za každý registrovaný detektor (`DetectorSettingsViewModel`: zaškrtávací pole, posuvník citlivosti
  0–100 s číslem, popis a tučně známé omezení; texty podle id v `App/Detection/DetectorTexts`, neznámé id = „Detektor <id>“).
  Bez detektorů (do T-155) text „Tato verze LayerPeeku zatím nemá žádný detektor.“ Při vypnuté kameře je obsah zakázaný,
  hodnoty zůstávají. Nastavení detektorů, které tato verze nenabízí, se při uložení zachovají; výchozí hodnoty se nezapisují.
- **Výřez:** dokud ho uživatel neupraví, zůstává v nastavení `null` a řídí se modelem (`DetectionRegion.DefaultFor(PrinterImageChoice,
  PrinterModelCode?)` a `IsCoreOne` – malé rozšíření Core, aby editor bez definice tiskárny použil stejné pravidlo); každá
  úprava (myš, klávesnice, tlačítka) ho uloží jako vlastní.
- **Editor** (`Settings/DetectionRegionEditor`, vlastní `Control`, compiled bindings, bez reflexe): snímek vykreslený
  rovnoměrně s pruhy, ztmavení mimo výřez, obrys a úchyty; myš (kreslení / přesun / roh, kurzor podle místa, Esc zruší
  tažení), klávesnice (šipky 1 %, Shift + šipky velikost, Ctrl / ⌘ 10 %), název pro čtečky s procenty. Hodnota se zapíše až
  po uvolnění myši nebo po klávese, takže okno neporovnává nastavení při každém pohybu. Žádný časovač ani animace. Geometrie
  `DetectionRegionGeometry` je čistá (bez Avalonie).
- **Poslední snímek** (rozhodnutí 2026-11-04, volba návrhu): `ILastCameraFrames` / `LastCameraFrameStore` – jeden JPEG na
  tiskárnu (nejvýš 640 × 360, kvalita 80) v `<mezipaměť>/last-frames/<32 hex SHA-256 id>.jpg`; mezipaměť =
  `AppEnvironment.CacheDirectory()` = nová `AppDataPaths.GetDefaultCacheDirectory()` (Windows `%LOCALAPPDATA%\LayerPeek\cache`,
  nikdy roaming; macOS `~/Library/Caches/LayerPeek`; v testovacím režimu `cache` v datové složce). Zapisuje se jen po
  úspěšném testu kamery v okně nastavení (podzáložka Kamera nebo „Načíst snímek“; snímek testu je neměnný, zmenšení průměrem ploch
  a zápis přes `.partial` na thread poolu – `Bitmap.CreateScaledBitmap` Skia u `WriteableBitmap` neumí, test to odhalil), čte se jednou za okno, když se podzáložka Detekce poprvé ukáže pro danou tiskárnu
  (dekódování mimo UI vlákno, zavřené okno načtený snímek uvolní). Smaže se, když se uloží nastavení bez dané tiskárny.
  Editor se sám ke kameře nepřipojuje (otázka 11).
- **Sekce „Detekce chyb tisku“ na záložce Nahrávání** (`SettingsViewModel.Detection.cs`): hlavní vypínač, „Ukládat snímek
  každého poplachu“, „Poplach“ (`AlarmRepeatOption`: jednou za tisk a druh chyby / znovu každých 10 min, dokud trvá) a
  „Kontrolovat kameru každých (s)“ 2–5 (desetinná místa povolená, chyba pod polem, neúspěšné uložení přepne na záložku a dá
  fokus poli `SettingsFieldIds.SettingsDetectionInterval`). Nápovědy jsou tooltipy a nápovědy pro čtečky. Aby se sekce vešla
  (francouzština byla o 80 DIP delší): mezery mezi sekcemi 8 místo 12 DIP a nápověda „Ručně uložit jde vždy…“ dashcamu (T-148)
  je v levém sloupci pod nápovědou délky; každý řádek pravého sloupce má vlastní mřížku, aby dlouhý popis intervalu nezúžil
  výběr „Poplach“ (francouzský text kratší: „Toutes les 10 min tant qu'il dure“).
- **Opravené chyby z T-153:** uložení okna nastavení zahazovalo `AppSettings.Detection` i `PrinterDefinition.Detection`
  (obojí se teď skládá), a `AppSettings.Equals` / `GetHashCode` neporovnávaly `Detection` (změna jen globální detekce by se
  neuložila ani nespustila nový běh monitoringu). Test `AppSettingsTests.TheDetectionSettingsArePartOfEquality`.

**Notifikace poplachu:**
- Texty `PrintFailureNotificationText` (titulek = tiskárna, „Možná chyba tisku – <detektor>“, soubor úlohy), stejné na obou OS
  (`ToastContentBuilder.PrintFailureLines`, čištění a zkracení jako u ostatních).
- **Windows** (`ToastNotificationService.ShowPrintFailure`, `ToastContentBuilder.BuildPrintFailure`): `launch="camera=<id>"`,
  vložený `<image src="file:///…">`, `<actions><action content="Falešný poplach" arguments="false-alarm=<id>"
  activationType="foreground"/></actions>` (popředí jako klik: dorazí stejnou cestou přes událost toastu i COM aktivátor),
  bez tagu, zvuk jako chyba tisku.
- **macOS** (`UserNotificationService.ShowPrintFailure`, `MacUserNotificationCenter`): `UNNotificationAttachment` z kopie
  snímku, kategorie `layerpeek.with-action` s akcí `layerpeek.action` (bez `Foreground`, aplikace se nepřenese do popředí;
  registruje se před odesláním, znovu jen po změně jazyka), argument tlačítka v `userInfo` pod `layerpeek.action`; delegát
  předá argument tlačítka routeru stejně jako klik. Na Macu jen přeloženo a otestováno s falešným centrem (sezení bez
  displeje a bez bundlu) → T-127.
- **Obrázky** (`AlarmNotificationImages`, `<mezipaměť>/notification-images/alarm-<id>.jpg`): Windows použije uložený snímek
  z nahrávek, když existuje, jinak zapíše JPEG z paměti; macOS vždy kopii (systém přiložený soubor přesune). Zápis přes
  `.partial`, každý zápis smaže vše kromě 5 nejnovějších a pozůstatky; nad 3 MB bez obrázku; selhání = notifikace bez
  obrázku.
- **Kliky** (`ToastActivationRouter`, `ToastLaunchArgument`): jen přesné tvary `camera=<percent-encoded id>` (≤ 512 znaků,
  nakonfigurovaná tiskárna) a `false-alarm=<id>` (1–64 znaků `a-z 0-9 -`), `IsWellFormed` i pro druhý start aplikace.
  Okno kamery otevře opener z `App` (`AttachCameraOpener` → `CameraWindows.BringToFront`, obnoví i minimalizované); do
  připojení UI se podrží poslední klik. „Falešný poplach“ → `FalseAlarmReporter` (id znovu ověří, `TryGetAlarm` nikdy
  nedostane `null`; služba detekce se získá až při kliku kvůli cyklu závislostí notifikace → router → detekce →
  notifikace); poplach, který běh nezná, jen zapíše do logu. Stejný klik do 2 s se ignoruje.
- **Testovací režim (jen Debug):** `LAYERPEEK_TEST_FAKE_DETECTOR=1` (`AppEnvironment.TestFakeDetector`, mimo testovací
  režim ignorováno a hlášeno) přidá `TestAlarmDetector` (`e2e-alarm`, skóre 1 v každém vzorku) pro E2E
  `PrintFailureAlarmTests` (Windows: toast, snímek v nahrávkách, tlačítko „False alarm“ → záznam, klik v centru oznámení →
  okno kamery).

**Texty:** 36 nových klíčů ve všech 8 jazycích (`NotificationPrintFailure`, `NotificationFalseAlarm`, `Detector*`,
`SettingsPrinterTabDetection`, `SettingsDetection*`); mezi číslem a jednotkou (`%`, `min`, `s`) a ve francouzštině před `:`
nedělitelná mezera.

**Testy:**
- App.Tests: `SettingsDetectionTests` (výchozí stav vypnuto, uložení a načtení globálních i tiskárnových voleb, cizí
  detektory zůstanou, citlivost, interval 2–5 s, výřez podle modelu a vlastní, nápovědy, poslední snímek jednou a uvolnění,
  uložení po testu kamery, smazání s tiskárnou), `DetectionRegionGeometryTests` (pruhy 4:3 a 21:9, ořez na okraj, tažení,
  převrácení, klávesy), `PrintFailureNotificationTests` (texty ve všech jazycích, toast XML se snímkem a tlačítkem, Windows
  uložený snímek / kopie, macOS příloha a tlačítko, úklid složky obrázků, falešný poplach), `PrintFailureActivationTests`
  (argumenty a odmítnuté tvary, okno kamery, podržený klik, duplicita, ukončení, tlačítko), `TestModeSwitchesTests`
  (přepínač detektoru), `AppEnvironmentTests` (mezipaměť); upravené testy pořadí podzáložek. Core: `AppSettingsTests`,
  `AppDataPathsTests`.
- UiTests: `SettingsWindowDetectionUiTests` (myš: kreslení, přesun, roh, mimo snímek; pruhy 4:3; klávesnice a název pro
  čtečky; Esc; tlačítka a uložení; Core One jen u Core One; vypnutá kamera; poslední snímek a „Načíst snímek“; hlavní
  vypínač mění nápovědu; interval mimo rozsah s fokusem; jazyk), `SettingsWindowLayoutTests.DetectionSubTabFitsInEveryLanguage`
  (Core One, poslední snímek a nejdelší neúspěšné načtení / vypnutá kamera / zapnuto, 8 jazyků), pohled „Detection“ ve
  všech stavech `EverythingFitsWithoutScrollingInEveryState`, `RecordingTabFitsInEveryLanguage` s chybou intervalu a kontrolou,
  že se volby „Poplach“ vejdou; `LastCameraFrameStoreTests` (uložení zmenšeného JPEG, načtení, smazání, poškozený soubor,
  zmenšení průměrem). Klávesové testy podzáložek (Ctrl+PageDown, ⌘⌥←) se čtvrtou podzáložkou.
- E2E (Windows, na Macu jen přeloženo): `PrintFailureAlarmTests` → T-125.
- Snímky macOS (`tests/LayerPeek.App.UiTests/Snapshots/macOS/`): nové `settings-detection.png`,
  `settings-detection-camera-off-fr.png`; přegenerované `settings-recording.png` a všechny snímky podzáložek tiskárny
  (`settings-camera*.png`, `settings-camera-window*.png`, `settings-errors-*.png`, `settings-validation*.png`,
  `settings-prusalink.png`, `settings-prusaconnect.png`, `settings-printer-images.png`, `settings-invalid-address-focused.png`,
  `settings-maximized.png`). Windows sada → T-125.
- `dotnet build` 0 upozornění; `dotnet test` (macOS arm64) celkem 9321, úspěšné 9120, přeskočeno 200; dva dřívější celé
  běhy bez chyby, v posledním selhal jen `CameraDashcamTests.ANewConnectionClearsTheBufferAndASaveGoesOnInTheNextFile`
  (T-148, Core, tímto úkolem nedotčený: `BufferedDuration` hned po novém spojení je 0,9 s místo 0 – závod testu se čtenářem
  dashcamu pod zátěží stroje, při samostatném opakování selže zhruba 3× z 5; nahlášeno hlavnímu agentovi jako nový úkol).
  `dotnet publish src/LayerPeek.App -c Release -r osx-arm64` bez varování trim / AOT (jen dosavadní hlášky linkeru / dsymutil).
- Ručně na Macu: aplikace v testovacím režimu se spustila, monitoring běžel ~40 s a skončila čistě; sezení agenta nemělo
  aktivní displej (T-099), takže okno nastavení, menu bar ani notifikace na skutečné obrazovce neověřeny → T-127.
- Uzavřeno 2026-11-04: zbývající ruční ověření přesunuto do T-125 (Windows: E2E, obrázek toastu, snímky, `win-x64`) a T-127
  (macOS: notifikace s přílohou a tlačítkem, editor na obrazovce, prohlédnutí návrhu uživatelem).

