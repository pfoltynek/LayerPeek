---
id: T-145
title: Nahrávání kamery – služba v Core (ruční a automatické nahrávání při tisku, složka, limit, rotace)
status: done
owner: core-dev
priority: P2
platform: all
depends_on: [T-144, T-009]
---

## Cíl
Logika, **kdy** a **kam** se nahrává, bez UI a testovatelná s falešnými hodinami a falešným rekordérem (nápad 1, fáze 1). Staví na zápisu z T-144 a na sdíleném spojení z T-143, takže nahrávání během tisku běží **i bez otevřeného okna kamery**.

## Rozsah
- **`Core/Cameras/Recording`, služba (pracovně `CameraRecordingService`), po vzoru `CooldownMonitor` / `PrintEventNotifier`:**
  - odebírá `IPrinterMonitor.SnapshotUpdated`, `Configure(printers, settings)` při každém běhu monitoringu (stav tiskáren se stejným strojem zachová),
  - **ruční nahrávání:** `Start(printerId)` / `Stop(printerId)` / stav nahrávání tiskárny + událost `Changed(printerId)` (pro tray menu a okno kamery v T-146),
  - **automatické nahrávání během tisku:** začne při přechodu do `Printing`, skončí po `Finished` / `Stopped` / `Error` (podle otázky 2 i po `Offline`), chování při `Paused` / `Attention` podle otázky 3. Využít `Core/Events/PrintEventDetector` nebo stejná pravidla.
  - souběh ručního a automatického nahrávání (otázka 4),
  - název souboru: tiskárna + čas začátku + případně název tisku (`Core` už umí hezčí název tisku z T-078); jen bezpečné znaky pro souborové systémy Windows i macOS, bez kolizí,
  - **složka** (otázka 5) – při startu nahrávání se ověří, že existuje a jde do ní zapisovat; jinak stav chyby s důvodem, ne výjimka,
  - **limit velikosti a rotace:** po dokončení nahrávky (a při startu služby) se smažou nejstarší nahrávky LayerPeeku, dokud celkem nejsou pod limitem; mažou se **jen soubory, které vytvořil LayerPeek** (podle vzoru názvu ve vlastní složce), nikdy cizí soubory,
  - ochrana disku: při volném místě pod mezí (otázka 6) se nahrávání nezačne / ukončí s chybou,
  - nahrávka, která se nedala dokončit (chyba, pád), se při dalším startu označí / opraví podle kontejneru z T-144.
- **Nastavení** v `settings.json` (`AppSettings` / `PrinterDefinition` podle otázky 7), čtené volně jako ostatní sekce (chybná hodnota = výchozí hodnota s varováním, výchozí hodnoty se nezapisují). `System.Text.Json` jen přes source generator.
- Volitelně událost „nahrávání skončilo chybou“ pro notifikaci (otázka 8).
- Diagnostický balíček (T-048) **nesmí** obsahovat nahrávky.

## Akceptační kritéria
- [x] Automatické nahrávání začne a skončí podle pravidel z rozhodnutí (testy se sekvencí snapshotů a falešnými hodinami pro všechny přechody včetně `Paused`, `Error`, `Offline` a restartu monitoringu).
- [x] Ruční start / stop a souběh s automatickým nahráváním podle rozhodnutí (test).
- [x] Rotace smaže nejstarší nahrávky LayerPeeku nad limitem a nikdy nesmaže cizí soubor ve stejné složce (test v dočasné složce).
- [x] Nedostupná složka, plný disk (simulovaný) a nedostupný muxer (T-144) skončí stavem chyby s důvodem, bez pádu (test).
- [x] Nastavení se načte, uloží a s chybnými hodnotami spadne na výchozí (testy s fixture `settings.json`).
- [x] Testy po sobě uklidí všechny soubory i po pádu.
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. **Režimy spouštění:** jen ručně, jen automaticky během tisku, nebo obojí s volbou u každé tiskárny? Výchozí hodnota (doporučení architekta k rozhodnutí: automatické nahrávání **vypnuté** ve výchozím stavu kvůli místu na disku a soukromí – rozhodne uživatel)?
2. **Konec automatické nahrávky při `Offline`:** ukončit hned, po toleranci (jako 1 min u vychladnutí, T-079), nebo nahrávat dál, dokud běží stream kamery (kamera má vlastní napájení / síť)?
3. **Pauza tisku (`Paused`, `Attention`):** nahrávat dál, pozastavit, nebo ukončit?
4. **Ruční nahrávání během automatického:** jedna nahrávka (ruční start jen „připne“ běžící), nebo dvě samostatné? Může ruční „Stop“ ukončit automatickou nahrávku?
5. **Složka:** výchozí umístění (např. Videa/LayerPeek na Windows `%USERPROFILE%\Videos\LayerPeek`, na macOS `~/Movies/LayerPeek`) a podsložka pro každou tiskárnu ano / ne? Volitelná vlastní složka?
6. **Výchozí limit velikosti a rotace:** limit celkové velikosti (GB), počtu nahrávek, nebo stáří (dní)? Kolik? Mez volného místa na disku? Podklad: datový tok změřený v T-144.
7. **Nastavení globálně, nebo u každé tiskárny?** (složka a limit spíš globálně, zapnutí automatického nahrávání spíš u tiskárny – rozhodne uživatel.)
8. **Notifikace** o začátku / konci / chybě nahrávání: žádná, jen při chybě, nebo volitelně vše?

### Rozhodnutí uživatele 2026-11-03
1. **Ruční i automatické** nahrávání; automatické nahrávání během tisku je ve výchozím stavu **vypnuté**. Tlačítko nahrávání má být i v okně kamery (T-146). Soubory na jednom místě, rozlišené podle tiskárny.
2. Konec tisku: nahrávat ještě **1 min** po konci tisku; `Offline` ukončí nahrávku po toleranci **1 min** (jako T-079).
3. `Paused` / `Attention`: **nahrávat dál**.
4. Ruční nahrávání během automatického: **jedna nahrávka** (ruční Start nic nepřidá); ruční Stop ukončí i automatickou a ta se do konce tisku znovu nespustí.
5. Složka: Windows `%USERPROFILE%\Videos\LayerPeek`, macOS `~/Movies/LayerPeek`, **podsložka pro každou tiskárnu**, volitelná vlastní složka v nastavení.
6. Limit: **50 GB celkem** (mazat nejstarší nahrávky), mez volného místa **5 GB** (pod ní se nahrávání zastaví s chybou); limit jde změnit v nastavení.
7. Nastavení **smíšeně**: složka a limit globálně, „Automaticky nahrávat tisk“ u každé tiskárny.
8. Notifikace **jen při chybě** nahrávání.

### Rozhodnutí uživatele 2026-11-04
1. **Každý nový tiskový úkol do vlastního souboru.** Když během minuty po konci tisku (nebo během tolerance výpadku) začne tisknout jiný úkol než ten nahrávaný, automatická nahrávka hned skončí a nový úkol se nahrává do nového souboru (s názvem nového tisku). Pokračování stejného úkolu (kolísání stavu, pauza / `Attention`, `Offline` do 1 min) nahrává dál do stejného souboru jako dosud. Ručního nahrávání se to netýká.
2. Vypnutí „Automaticky nahrávat tisk“ ukončí běžící automatickou nahrávku – **potvrzeno** (beze změny).
3. Při překročení limitu velikosti se zastaví všechny běžící nahrávky – **potvrzeno** (beze změny, k nálezu revize níže).

## Poznámky k implementaci
Implementováno 2026-11-03 na macOS arm64 (core-dev).

**API pro T-146** (`src/LayerPeek.Core/Cameras/Recording/`, singleton `CameraRecordingService` v DI):
- `Start(printerId)` → `bool` (`false` jen u nenastavené tiskárny, vypnuté kamery nebo při ukončování; během nahrávání nic nepřidá), `Stop(printerId)`.
- `GetStatus(printerId)` → `CameraRecordingStatus`: `Activity` (`Idle`, `Starting`, `Recording`, `Stopping`), `IsActive` (indikace REC / tooltip „Nahrává se“), `Mode` (`Manual` / `Automatic`), `StartedAt`, `CurrentFile`, `BytesWritten`, `Duration`, `LastFile` (nejnovější dokončená nahrávka tiskárny, i z dřívějších běhů – „Přehrát poslední nahrávku“), `LastError`, `RecordsPrints`, `CanRecord`.
- Událost `Changed(printerId)` (změna stavu, ne rostoucí velikost) a `Failed(CameraRecordingError)` (`PrinterId`, `Problem`, anglický `Message` bez cesty, `Timestamp`, `Mode`) – jediná notifikovaná událost (rozhodnutí 8). `CameraRecordingProblem`: `Unavailable`, `CameraUnavailable`, `FolderUnavailable`, `DiskSpaceLow`, `DiskFull`, `WriteFailed`, `TooSlow`, `StreamFailed`, `SizeLimitReached`. Text notifikace lokalizuje App (zapsáno do rozsahu T-146), Core žádné texty UI nemá.
- `Availability` (bez muxeru zakázat volby), `RecordingsFolder` (aktuální kořen – „Otevřít složku s nahrávkami“). Události chodí na libovolném vlákně, UI si je převede samo.
- Nastavení: `AppSettings.Recording` (`CameraRecordingSettings`: `Folder` – `null` = výchozí, `SizeLimitGigabytes` 1–100 000, výchozí 50) a `CameraSettings.RecordPrints` (u tiskárny, výchozí vypnuto). Okno nastavení je zatím jen drží beze změny (`SettingsViewModel._recording`, `PrinterEditorViewModel._recordPrints`), aby je uložení neztratilo; ovládací prvky přidá T-146.

**Pravidla (rozhodnutí 2026-11-03):**
- Automaticky (jen `RecordsPrints` = kamera zapnutá + `recordPrints`): začne s `Printing` nebo `Paused` (i když tisk už běží při startu aplikace nebo po zapnutí volby uprostřed tisku – od dalšího snímku), přes `Paused` / `Attention` nahrává dál. Konec tisku = `Finished`, `Stopped`, `Error`, `Idle`, `Busy`: nahrává ještě 1 min; tisk, který během té minuty pokračuje (`Printing`), si nahrávku nechá. `Offline`, `Unknown`, `Unauthorized` během tisku: tolerance 1 min (jako T-079), pak konec; když se tiskárna vrátí a tiskne dál, začne nová nahrávka (nový soubor). Výpadek po konci tisku minutu neprodlouží.
- Ruční Start během automatické nahrávky nic nepřidá; Stop ukončí ruční i automatickou a automatická se do konce tisku (konec tisku viz výše, nebo uplynutí minuty po něm) znovu nespustí. Ruční nahrávka běží, dokud ji uživatel nezastaví (konec tisku ji neukončí), i když tisk mezitím začne a skončí.
- Nahrávka, která skončila chybou (nebo sama, např. kamera natrvalo pryč), se znovu nespouští ani u stejného tisku – žádná smyčka chyb na plném disku. Další tisk nebo další ruční Start to zkusí znovu; `LastError` vymaže až další nahrávka, která opravdu začala zapisovat.
- Změny nastavení za běhu: odebraná tiskárna, vypnutá kamera nebo vypnuté `recordPrints` u automatické nahrávky ji ukončí bez chyby (pozorovatel `CameraRecordingObserver` je v seznamu před hubem, souběžný konec odběru hubem `CameraUnavailable` se nehlásí jako chyba). Jiná adresa / typ připojení = jiný stroj, stav začne znovu. Přejmenování, nová adresa kamery (T-144: další soubor s příponou) a nová složka platí od další nahrávky, běžící nahrávka pokračuje. Restart monitoringu (uložení nastavení) stav zachová.
- Ukončení aplikace: `ServiceHost.StopCameraStreamsAsync` nejdřív počká na `CameraRecordingService.StopAllAsync()` (všechny nahrávky dokončí poslední fragment a konec souboru), teprve pak zavře hub, obojí dohromady v původním limitu ukončení (poznámka revize T-144). Po `StopAllAsync` už nic nezačne.

**Každý tisk do vlastního souboru (rozhodnutí 2026-11-04, větev `feature/recording-file-per-print-job`):**
- Služba si drží identitu sledovaného tisku (`JobIdentity`: `PrintJob.Id` a název souboru, doplňuje se z dalších snímků, např. název, který přijde o dotaz později). Při každém snímku `Printing` / `Paused` během sledovaného tisku (běžící tisk, minuta po konci, tolerance výpadku) ji porovná s úkolem ve snímku. Jiný úkol = starý tisk skončil: běžící **automatická** nahrávka se hned zastaví a po jejím dokončení začne nová (nový soubor s názvem nového úkolu; když název nového úkolu není známý, soubor je bez názvu tisku, ne se starým). Ruční nahrávka běží dál. Funguje i ve stavu `Starting` (nahrávka čeká na první klíčový snímek).
- **Jak se pozná jiný úkol** (od nejspolehlivějšího signálu): 1) obě `Id` známá → rozhoduje jen `Id` (PrusaLink ho dává u každého běžícího úkolu, opakovaný tisk stejného souboru má nové). 2) Jinak různé názvy souborů → jiný úkol (běžící tisk název nemění). 3) Jinak (identita na jedné nebo obou stranách neznámá, nebo stejný název bez `Id`) → jiný úkol **jen tehdy, když sledovaný tisk mezitím hlásil `Finished` nebo `Stopped`** – takový úkol podle modelu (`PrinterState`: „poslední úkol skončil / byl zrušen“) už nepokračuje. Po `Busy`, `Idle`, `Error`, `Attention`, pauze nebo výpadku se při neporovnatelné identitě bere stejný úkol.
- Proč takhle: chybějící data sama nikdy nerozdělí jeden tisk (to by udělalo pravidlo „neznámá identita = jiný úkol“) a dva tisky se sloučí jen tehdy, když je nerozliší `Id` ani název a první skončil bez viditelného `Finished` / `Stopped` (např. skončil během výpadku a nový úkol nemá `Id` ani název) – s PrusaLink (vždy `Id`) to v praxi nenastane. Základ „neznámá identita = stejný úkol“ odpovídá `PrintEventDetector` a `CooldownTracker`; navíc je jen signál `Finished` / `Stopped`, díky kterému se pozná i opakovaný tisk stejného souboru bez `Id`. Při rozporu vyhrává `Id` (stejné `Id` po `Finished` = stejný úkol). `Error` jsem za konečný stav nepovažoval, protože nevím, jestli po něm tisk nemůže pokračovat.
- Vedlejší účinek (k nálezu revize „Potlačení po ručním Stop / chybě přežije výpadek“): jiný úkol zruší i potlačení po ručním Stop nebo po chybě, takže tiskárna, která se po dlouhém výpadku vrátí s novým úkolem, ho nahraje. Stejný úkol zůstane potlačený.
- Log: `Printer '…' prints another job; its automatic recording continues in a new file.` (jen ID tiskárny).
- Testy (`CameraRecordingServiceTests`, 15 nových metod / 28 případů): jiný úkol během minuty → nový soubor s názvem nového tisku a s vlastní minutou; opakovaný tisk stejného souboru s novým `Id` → nový soubor; stejné `Id` po `Busy` / `Idle` / `Error` / `Finished` → stejný soubor; jiný úkol po krátkém výpadku → nový soubor; stejný úkol po pauze / `Attention` / `Offline` → stejný soubor; neznámá identita po `Finished` / `Stopped` → nový soubor bez názvu tisku (pozdější `Id` už nic nerozdělí); stejný název bez `Id` po `Finished` → nový soubor; neznámá identita nebo stejný název bez `Id` po `Busy` / `Idle` / `Error` → stejný soubor; jiný název při neporovnatelném `Id` → nový soubor; název, který přijde později → stejný tisk; jiný úkol po ručním Stop (i po dlouhém výpadku) se nahraje, stejný ne; ruční nahrávka se nerozdělí; jiný úkol během `Starting` → nový soubor. S původním kódem selže 11 případů (ostatní hlídají, že se stejný tisk nerozdělí).

**Soubory a složky (`CameraRecordingFolders`):**
- Kořen: vlastní složka z nastavení, jinak `Environment.SpecialFolder.MyVideos` + `LayerPeek` (Windows známá složka Videa = `%USERPROFILE%\Videos\LayerPeek`, i přesunutá; macOS `~/Movies/LayerPeek`, ověřeno). Testovací režim (T-044) nahrává do `Recordings` v datové složce, nikdy do videí uživatele.
- Podsložka tiskárny = název tiskárny upravený pro souborové systémy (neplatné znaky Windows `<>:"/\|?*` a řídicí znaky → `_`, sloučené mezery, bez teček a mezer na konci a tečky na začátku, rezervované názvy `CON`, `NUL`, `COM1`… s `_` na začátku, nejvýš 60 znaků). Tiskárny, jejichž názvy dají stejnou složku (bez ohledu na velikost písmen), dostanou všechny příponu ` (abc123)` z SHA-256 svého ID – stabilní, nezávislá na pořadí.
- Název souboru: `yyyy-MM-dd_HH-mm-ss[_<název tisku>].mp4` v místním čase, název tisku = `PrintFileNameInfo.Title` (T-078) upravený stejně, nejvýš 60 znaků. Řadí se podle času; kolize a nové spojení řeší rekordér příponou `-2`, `-3`… (T-144).
- Každá složka tiskárny dostane značku `.layerpeek-recordings` (krátký text, co složka je; na Windows skrytá). Jejím zápisem se zároveň ověří, že jde do složky zapisovat; chyba = `FolderUnavailable` (bez výjimky).
- V logu jen ID tiskárny a název souboru, nikdy složka. Diagnostický balíček (T-048) bere jen soubory složky logů, nahrávky v něm nejsou.

**Limit, rotace, místo na disku:**
- `CameraRecordingRotation`: bere jen soubory podle vzoru názvu (`IsRecordingFileName`: platné datum a čas na začátku, pak nic, `_` nebo `-`, přípona `.mp4`) přímo v podsložkách kořene, které mají značku – nic v kořeni, nic hlouběji, nic ve složce bez značky, žádné odkazy (reparse pointy se přeskočí), nikdy soubor běžící nahrávky. Maže nejstarší (podle času v názvu, pak podle zápisu), dokud součet + běžící nahrávky nejsou pod limitem; soubor, který nejde smazat (otevřený v přehrávači), přeskočí. Prázdné soubory podle vzoru starší než 10 min (zbytek po pádu před prvním fragmentem) smaže vždy – jediná „oprava“ po pádu: fragmentovaný MP4 z T-144 je čitelný až po poslední fragment a nic dalšího nepotřebuje.
- Rotace běží na thread poolu, jedna naráz: při prvním `Configure` (start), při změně složky nebo limitu, po každé nahrávce a když průběžná kontrola zjistí překročení limitu.
- Průběžná kontrola: jeden periodický časovač (30 s) jen během nahrávání. Kontroluje volné místo (`DriveInfo`; Windows kořen disku, Unix nejbližší existující složka; UNC = neznámé, bez kontroly) a limit podle `BytesWritten` + součtu z poslední rotace – žádné procházení souborů ani nic na paket. Pod 5 GB volného místa se nahrávání nezačne / zastaví s `DiskSpaceLow`. Když je nad limitem sama běžící nahrávka (vše starší už je smazané), zastaví se s `SizeLimitReached` – limit je celkový (rozhodnutí 6).
- Nečinnost: bez nahrávání a bez tisku tiskárny s automatickým nahráváním žádný časovač ani úloha (test `IdleCostsNoTimers` počítá časovače). Tiskárny bez `recordPrints` se ve snímcích vůbec nesledují.

**Úklid kódu T-144 (poznámky revize):** `RecordingClock` bere za délku snímku jen kroky (a délky paketů) do 1 s (`MaxFrameDuration`), skutečná mezera do 10 s zůstane mezerou, ale už se z ní nestane délka snímku pro další chybějící razítko. `CameraRecording`: očekávaná `CameraRecordingException` při dokončení souboru po předchozí chybě (např. pořád plný disk) se loguje jako `Debug` „could not complete its file after the failure either“, ne jako `Error` „failed unexpectedly“.

**Zaseknutí `dotnet test` (poznámka revize):** při mých bězích (plný `dotnet test` 2×, testy služby 6× za sebou) se neobjevilo. Nové testy čekají jen omezeně (`Eventually.TrueAsync` s 10 s, `WaitAsync(TestTimeouts.Wait)`), falešná nahrávka dokončuje `Completion` asynchronně jako skutečná. Kandidáti z T-143 (nekonečné `await feed.Started`, `await frames.Ended`, `MoveNextAsync` v `CameraStreamHubTests` / `CameraStreamHubAllocationTests`) jsem neměnil – bez reprodukce je to odhad; vhodné pro qa-reviewer.

**Testy:**
- `CameraRecordingServiceTests` (49 případů): všechny přechody (`Printing`, `Paused`, `Attention`, `Finished`, `Stopped`, `Error`, `Idle`, `Busy`, `Offline`, `Unknown`, `Unauthorized`, krátký výpadek, pokračování tisku v minutě, výpadek po konci tisku), výchozí vypnuto, restart monitoringu, vypnutí / zapnutí `recordPrints`, ruční start / stop, souběh ručního a automatického, nedostupná složka (kořen je soubor; bez oprávnění), málo místa před startem i za běhu, nahrávka nad limitem, chybějící muxer, chyby rekordéru (bez opakování), vypnutá kamera, odebraná tiskárna, jiný stroj, stejné názvy tiskáren, nová složka, rotace při startu a po nahrávce, poslední nahrávka z dřívějšího běhu, ukončení (i nahrávky čekající na klíčový snímek), odhlášení od monitoru, padající odběratel, log bez složky, žádné časovače v nečinnosti.
- `CameraRecordingRotationTests` (dočasná složka, skutečné soubory): nejstarší napříč tiskárnami, cizí soubory (jiné názvy, `.mkv`, neplatné datum, kořen, složka bez značky, hlubší úroveň) zůstanou, běžící nahrávka se nemaže, ale počítá, prázdné zbytky po pádu, pořadí segmentů, chybějící kořen, nesmazatelný soubor, značka, volné místo.
- `CameraRecordingFoldersTests`: výchozí kořen podle OS, bezpečné názvy, zkrácení (i bez rozdělení páru surrogate), přípony pro stejné názvy, názvy souborů, rozpoznání vzoru.
- `SettingsMapperRecordingTests` + fixture `settings-v2-recording.json` a `settings-v2-recording-invalid.json`: výchozí hodnoty se nezapisují, round-trip, chybné hodnoty (sekce, složka, limit, `recordPrints`) = výchozí s varováním bez ozvěny složky.
- `RecordingClockTests` (2 nové), `CameraRecordingTests` (1 nový), App: `AppServicesTests.CameraRecordingServiceIsWiredBeforeTheHub`, `CameraWindowSettingsTests.SavingKeepsTheRecordingSettings`.
- `dotnet build` 0 upozornění, `dotnet test` celkem 8873, neúspěšné 0, úspěšné 8673, přeskočeno 200. `dotnet publish src/LayerPeek.App -c Release -r osx-arm64` bez varování trim / AOT (jen dosavadní hlášky linkeru / dsymutil). Žádný nový balíček.
- Na Windows (výchozí složka Videa, `DriveInfo`, skrytá značka) se testy spustí v T-125 (bod 2d, přidáno).


## Review
Revize 2026-11-03 (qa-reviewer), bez blokujících nálezů po opravách. Opraveno při revizi:
- **Bezpečnost rotace – cizí složka:** `CameraRecordingStorage.PrepareFolder` dřív zapsal značku do jakékoli existující složky s názvem tiskárny, takže vlastní kořen např. v domovské složce a tiskárna „Documents“ by z `~/Documents` udělaly složku LayerPeeku a rotace by v ní mazala soubory podle vzoru názvu. Teď se existující složka bez značky převezme jen prázdná (kromě `.DS_Store`, `Thumbs.db`, `desktop.ini`), jinak `ForeignRecordingFolderException` → `FolderUnavailable` s důvodem bez cesty. Testy `AnExistingFolderOfTheUserIsNeverMarked`, `AnExistingEmptyFolderIsMarked`, `AFolderOfTheUserWithThePrintersNameIsNeverTakenOver`.
- **Bezpečnost rotace – soubor začínající nahrávky:** rekordér (T-144) vytvoří soubor dřív, než ho nahrávka uvede v `Files` (první klíčový snímek, další segment `-2` po novém spojení). Rotace nad limitem ho mohla smazat (na macOS se otevřený soubor smaže a nahrávka pak zapisuje „do ničeho“). Služba si teď plánovaný soubor drží (`Entry.PlannedFile`) od naplánování startu do konce nahrávky a rotace chrání i jeho segmenty `název-N.mp4`. Testy `TheFileOfAStartingRecordingIsNeverDeleted`, `FurtherSegmentsOfARunningRecordingAreNeverDeleted`.
- **Bezpečnost rotace – nenormalizovaná cesta:** složka z ručně upraveného `settings.json` (`…/x/..`, zdvojené nebo koncové oddělovače, na Windows `/`) dávala cesty běžící nahrávky jiné než výčet rotace, takže běžící nahrávka nebyla chráněná. Kořen se teď normalizuje (`Path.GetFullPath`, bez koncového oddělovače). Test `TheRotationNeverDeletesTheRunningRecordingOfAFolderWrittenWithDots`.
- **Synchronní dispose kontejneru:** `CameraRecordingService` byl jen `IAsyncDisposable`, takže `ServiceProvider.Dispose()` v `ServiceHost.DisposeAfterFailedStart` (po startu monitoringu) by vyhodil `InvalidOperationException`. Přidán `IDisposable` (odhlásí se a zastaví nahrávky bez čekání, jako hub). Testy `DisposingSynchronouslyStopsTheRecordings`, `TheContainerWithTheRecordingServiceCanBeDisposedSynchronously`.
- Nový test `LinksAreNeverFollowed` (odkaz na označenou složku mimo kořen i odkaz na soubor se přeskočí; na Windows bez vývojářského režimu se přeskočí).
- `AppServices`: dokumentační komentář `AddCameras` byl omylem nad `CameraRecordingOptions` (dvě `<summary>`).
- Zaseknutí `dotnet test` (T-143): čtení odběrů a čekání v `CameraStreamHubTests` (`ReadAsync`, `MoveNextAsync`, `Ended`, `Started`, `DisposeAsync`) a v `CameraStreamHubAllocationTests` jsou omezená (token se zrušením po 30 s / 60 s, `WaitAsync(TestTimeouts.Wait)`), chyba teď test shodí místo zaseknutí běhu.

Nálezy bez opravy (k rozhodnutí / pro T-146):
- *(Rozhodnutí 2026-11-04: chování potvrzeno, beze změny.)* Nad limitem se zastaví **všechny** běžící nahrávky v kořeni s textem „The running recording alone exceeds…“, i když limit překročí až jejich součet (několik tisků najednou). Zvážit zastavení jen té největší / text „recordings together“.
- `Start` během `Stopping` nic neudělá (vrátí `true`), rychlé Stop → Start se tiše ztratí; T-146 má tlačítko během `Stopping` zakázat, nebo služba přání zapamatovat.
- *(Od 2026-11-04 vyřešeno pro nový úkol, který jde rozpoznat, viz „Každý tisk do vlastního souboru“.)* Potlačení po ručním Stop / chybě (`Suppressed`) přežije výpadek delší než tolerance; když se tiskárna vrátí rovnou s **novým** tiskem (konec předchozího nebylo vidět), nový tisk se nenahraje.
- Windows: soubory a složky na OneDrive (Files On-Demand) mají atribut reparse point, rotace je přeskočí (bezpečný směr, ale limit se pak neuplatní) – ověřit v T-125.

Ověření při revizi (macOS arm64): `dotnet build` 0 upozornění; plný `dotnet test` 2× po opravách (8882 celkem, 0 neúspěšných, 200 přeskočeno) a `LayerPeek.Core.Tests` 20× za sebou bez zaseknutí (každý běh ~10 s) – zaseknutí se nepodařilo zopakovat, skutečná příčina zůstává neznámá. Před opravami jeden plný běh hlásil 1 neúspěšný test v `LayerPeek.App.Tests` (název se nezachoval, při dalších 3 bězích se neopakoval). `dotnet publish src/LayerPeek.App -c Release -r osx-arm64` bez varování trim / AOT. Nové testy revize bez oprav selžou (ověřeno dočasným vrácením oprav).
