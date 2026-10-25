---
id: T-089
title: Malý náhled – informační pás pod obrazem (průběh + čas) a výška okna podle pásu
status: done
owner: ui-dev
priority: P2
platform: all
depends_on: [T-076, T-082, T-083]
---

## Cíl
Malý náhled kamery přestane kreslit údaje přes obraz. Pod obrazem bude **informační pás**:
- obraz má poměr stran streamu, výchozí 16:9,
- sbalený pás je jeden řádek ≈ 30 px: pruh průběhu na horní hraně pásu, text „19 % · 2 h 35 min“ a místo pro dvě přepínací tlačítka (tlačítka, řádek názvu a řádek grafu dodá T-090),
- okno mění výšku podle výšky pásu a obraz si drží poměr stran.

## Rozhodnutí (uživatel, 2026-10-24)
- **Pás pod obrazem:** uživatel povoluje změnu poměru stran malého okna. Pás je **pod** obrazem, ne přes něj. **Přes obraz malého náhledu se nekreslí žádné karty widgetů.**
- **Řádky pásu:** sbalený pás = obraz + jeden řádek ≈ 30 px. Rozbalený pás = navíc řádek s názvem a/nebo řádek ≈ 60 px s grafem (T-090). Okno podle toho roste a zmenšuje se na výšku.
- **Změna velikosti úchyty (Q1):** varianta **(a)**. OS mění velikost jako dosud (`BeginResizeDrag`). Po skončení tažení se **výška dopočítá** z šířky tak, aby obraz měl poměr stran streamu a pod ním byl pás.
- **Směr růstu (Q2):** okno roste **od nejbližšího okraje obrazovky**. Je-li blíž spodnímu okraji pracovní plochy, spodní hrana stojí a okno roste nahoru. Je-li blíž hornímu, horní hrana stojí a okno roste dolů. Výsledek se vždy **ořízne na obrazovku**: posune se tak, aby celé okno zůstalo na pracovní ploše monitoru.
- **Význam velikosti (Q3):** 320×180 v nastavení (T-035) je **velikost obrazu**. Pás se k ní přičte. Stejně se chápe pamatovaná velikost.
- **Minimální šířka (Q4):** minimální šířka malého náhledu se zvyšuje na **240** DIP.
- **Poměr stran (Q5):** podle **skutečného streamu**. Do prvního snímku (a bez snímku) platí 16:9.

## Kontext (dnešní chování, T-014 / T-035 / T-037)
- Malý náhled je okno bez rámu, vždy navrchu (`TopmostMini`). Přesouvá se tažením obrazu.
- Velikost se mění úchyty na okrajích (`Border.grip`, `BeginResizeDrag`) bez zámku poměru stran. Obraz se do okna vejde se zachovaným poměrem.
- Výchozí velikost je v nastavení `printers[*].camera.window.mini` = 320×180 v rohu `BottomRight` (`CameraWindowDefaults`, `CameraWindowSize`: šířka 160–16384, výška 90–16384).
- Pamatovaná poloha a velikost je v `camera-windows.json` (`App/Cameras/CameraWindowStateStore`, `CameraWindowPlacement` = poloha v px, velikost v DIP). `Remove(printerId)` stav zapomene, když se v nastavení změní výchozí hodnoty okna.
- Umístění hlídá `CameraWindowPlanner` / `CameraWindowPlacementRules` (dosažitelná horní hrana, monitor, DPI). E2E T-071 ověřuje malý náhled na monitorech.

## Rozsah
- **Rozvržení malého náhledu:** řádek obrazu s poměrem stran streamu a pod ním pás (`Grid`).
  - Pás ukazuje průběh a čas z dat widgetů Stav a Časy (T-082, T-083): stejné view modely, kompaktní šablona.
  - Stavy bez úlohy, offline a během vychladnutí podle návrhu T-076.
  - Karty widgetů se v malém náhledu nekreslí.
- **Výška okna:**
  - výška = šířka × (výška / šířka streamu, výchozí 9/16) + výška pásu,
  - přepočítá se při změně výšky pásu (T-090), po skončení změny velikosti úchyty, při prvním snímku se známým poměrem a při změně poměru streamu,
  - směr růstu a oříznutí na pracovní plochu podle Rozhodnutí (hlavní panel, DPI monitoru, více monitorů).
- **Logika bez Avalonie:** výpočet geometrie jako čistá funkce, např. rozšíření `CameraWindowPlanner`. Vstupy: obdélník okna, pracovní plocha, poměr stran, výška pásu. Výstup: nový obdélník.
- Tažení za pás okno přesouvá stejně jako tažení za obraz, ale ne přes tlačítka v pásu.
- **Velikost v nastavení a pamatovaná velikost** = velikost obrazu.
  - Minimální šířka malého náhledu je 240 DIP. Validace v nastavení (T-035, `CameraWindowSize` / pole „Malý náhled – Velikost“) a `MinWidth` okna.
  - Uložené menší hodnoty se při načtení zvětší na 240 (bez chyby, jedno varování).
  - Normální okno zůstává s minimem 160.
- **Převod starého pamatovaného stavu:** dosavadní velikost okna se bere jako velikost obrazu. Okno pak o pás vyroste podle pravidla růstu a oříznutí, takže neposkočí mimo obrazovku.
- Nové `AutomationId` podle návrhu, například `CameraMiniInfoStrip`, `CameraMiniProgress`, `CameraMiniSummary`.

## Akceptační kritéria
- [x] App.Tests – výpočet geometrie:
  - výška z šířky, poměru (16:9, 4:3, bez snímku) a výšky pásu,
  - růst nahoru u spodního okraje a dolů u horního,
  - oříznutí na pracovní plochu ve všech 4 rozích, s hlavním panelem a na více monitorech s různým DPI,
  - dopočet výšky po změně velikosti úchyty.
- [x] App.Tests – nastavení a stav:
  - minimální šířka 240 (validace v nastavení, načtení menší uložené hodnoty),
  - 320×180 = velikost obrazu,
  - převod starého pamatovaného stavu.
- [x] Headless UI testy:
  - obraz má poměr streamu (tolerance 1 DIP) při různých šířkách, pás je pod obrazem,
  - po simulované změně velikosti se výška dopočítá,
  - žádné karty přes obraz,
  - text ve všech stavech tiskárny, nejdelší jazyk bez přetečení při šířce 240,
  - tlačítka okna nepřekrývají pás,
  - snímky reference.
- [x] Testy umístění z T-035 a T-071 upravené a zelené. E2E kontroly na skutečném okně jsou zapsané do T-093
- [x] Nové texty ve všech 8 jazycích (validace minimální šířky)
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné – Q1–Q5 rozhodnuté 2026-10-24, viz Rozhodnutí)


## Poznámky k implementaci
- 2026-10-25, ui-dev. Popis v `docs/architecture.md` (oddíl Malý náhled).
- **Geometrie** (čisté funkce bez Avalonie, `App/Cameras/CameraMiniGeometry`):
  - výška okna = šířka × poměr streamu (do prvního snímku 16:9) + výška pásu (33 = 3 + 30),
  - `Correct` dopočítá výšku okna ze šířky, `FromImage` / `ToImage` převádějí pamatovanou nebo výchozí polohu na okno a zpět, `Grow` je pravidlo růstu,
  - růst od nejbližšího okraje pracovní plochy (u spodního stojí spodní hrana, u horního horní). Levá hrana stojí vždy, mění se jen výška,
  - pak posun (a nejvýš velikost pracovní plochy), aby celé okno bylo na pracovní ploše monitoru, na kterém okno je (hlavní panel, DPI toho monitoru),
  - minimum šířky 240; okno vyšší než monitor se zúží, aby se vešlo (ne pod 240),
  - výsledná velikost v DIP je přesná (např. 190,5), porovnání „okno už je správně“ se dělá v celých pixelech, takže se správné okno znovu nenastavuje.
- **Pamatovaná a výchozí poloha = obdélník obrazu** (Q3):
  - `CameraWindowPlacement` malého náhledu v `camera-windows.json` i z výchozího nastavení je obraz (poloha v px, velikost obrazu v DIP), okno z něj vyroste o pás pravidlem růstu,
  - pamatuje se inverzí růstu (u spodního okraje je obraz pod pásem), okno se tedy otevře přesně tam, kde bylo zavřené,
  - **převod starého stavu:** dosavadní pamatované okno (bez pásu) je přesně takový obdélník obrazu. Vyroste o pás od nejbližšího okraje a zůstane na obrazovce. Formát souboru se nemění, žádná verze ani příznak,
  - výchozí roh (např. vpravo dole) tak dál drží okraj 16 od pracovní plochy i s pásem.
- **Dopočet výšky** (`Windows/CameraWindow.Mini.cs`, chráněno proti opakování):
  - po změně velikosti úchytem: na Windows běží `BeginResizeDrag` jako modální smyčka systému, výška se dopočítá jednou po puštění (během tažení nic, žádné blikání). Jinde (macOS) se dopočítá 300 ms po poslední změně velikosti po stisku úchytu (neověřeno),
  - při prvním snímku se známým poměrem a při změně poměru (`CameraViewModel.VideoAspectRatio`, mění se jen při změně poměru, ne každým snímkem),
  - při změně výšky pásu (`CameraMiniStripViewModel.StripHeight`, připraveno pro T-090).
- **Pás** (`App/Cameras/Mini/CameraMiniStripViewModel`, `CameraMiniStrip.axaml`; v `CameraWindow.axaml` jen řádek `Grid` pod obrazem):
  - obsah počítaný sdílenými pravidly widgetů Stav a Časy (`OverlayValues`, `OverlayFormat`, `OverlayStateBrushes`): číslo první, „19 % · 2 h 35 min“, „19 % · Pozastaveno“, bez úlohy název stavu, offline / chyba / neplatné údaje, „Připojování…“,
  - vychladnutí z `CooldownMonitor` (T-079, nově předaný do `CameraViewModel`): „Chladne… 87 °C · cca 6 min“ (bez odhadu bez části za tečkou) s ikonou vločky, „Vychladlo · 48 °C“ s ikonou vypínače,
  - snapshot beze změny zobrazeného obsahu nealokuje a nic neposílá na UI vlákno (test 0 B na 1000 snapshotů), texty se formátují jen při změně a po přepnutí jazyka,
  - pruh průběhu 3 DIP na horní hraně, bez úlohy jen podklad s alfou 6 %,
  - celé znění je tooltip i jméno pro čtečku řádku (`CameraMiniSummary`), např. „Tiskne, 19 %, Zbývá 2 h 35 min“,
  - `AutomationId`: `CameraMiniInfoStrip`, `CameraMiniProgress`, `CameraMiniSummary`, místo pro přepínače `CameraMiniToggles`,
  - **přepínače:** podle Rozsahu tohoto úkolu je dodá T-090. Pás pro ně drží místo 2 × 30 + 2, 6 od pravého okraje (text má při šířce 240 právě 140 DIP). Zatím v pásu nic nebere fokus,
  - tažení za pás přesouvá okno jako tažení za obraz, dvojklik na pás vrátí normální okno jako dvojklik na obraz,
  - karty widgetů se v malém náhledu nekreslí (beze změny z T-081), tlačítka okna zůstávají nad obrazem.
- **Minimální šířka 240** (Q4):
  - `CameraWindowSize.MinMiniWidth` / `IsValidMini` v Core,
  - validace pole „Malý náhled – Velikost“ (`CameraWindowSizeText.ParseMini`) se stávající větou `SettingsCameraWindowSizeError` s minimem 240 (8 jazyků beze změny textu),
  - menší uložená šířka se při načtení zvětší na 240 s jedním varováním (`SettingsMapper.ToMiniLayout`, výška obrazu zůstane), ověřeno i spuštěním aplikace v testovacím režimu,
  - `MinWidth` okna je 240 jen v malém náhledu, normální okno má dál 160.
- **Nové texty (8 jazyků):** `CameraMiniCooling` („Chladne… {0} °C · cca {1}“), `CameraMiniCoolingNoEstimate`, `CameraMiniCooled` („Vychladlo · {0} °C“, max. 22 znaků podle návrhu). Nezlomitelná mezera před °C, test ve všech jazycích.
- **Výklad zadání (k potvrzení uživatelem, implementaci neblokuje):**
  - Při otevření a přepnutí do malého náhledu má obraz velikost z nastavení / pamatovanou (tedy i její poměr). Na poměr streamu se výška dopočítá až podle spouštěčů ze zadání (první snímek, změna poměru, úchyty, pás). Pamatovaný obraz kamery 4:3 se tak otevře bez poskočení. Nastavení např. 320 × 240 se u streamu 16:9 po prvním snímku zmenší na 320 × 213.
  - Po změně adresy kamery platí poslední známý poměr až do prvního snímku nové kamery, aby okno neměnilo výšku dvakrát.
  - Okno přesně uprostřed pracovní plochy (± 1 px, např. výchozí roh „Na střed“) roste na obě strany stejně, aby zůstalo na středu.
  - Starý pamatovaný malý náhled užší než 240 se rozšíří doprava a posune na pracovní plochu (levá hrana stojí, pokud se vejde).
- **Testy:**
  - App.Tests `Cameras/CameraMiniGeometryTests` (poměry 16:9 / 4:3 / bez snímku, růst nahoru / dolů / na obě strany, 4 rohy, hlavní panel, monitor 150 %, zpáteční převod při 100 / 125 / 150 / 200 %, starý stav, minimum, příliš vysoké a široké okno, idempotence),
  - `Cameras/Mini/CameraMiniStripViewModelTests` (všechny stavy, vychladnutí přes skutečný `CooldownMonitor`, cizí tiskárna, jeden post při zaneprázdněném UI, jazyky, uvolnění, texty v 8 jazycích) a `CameraMiniStripAllocationTests` (kolekce `AllocationSensitive`), `CameraViewModelMiniTests` (poměr ze snímků, změna kamery, pás v okně),
  - nastavení: `CameraWindowSettingsTests` (240 v malém náhledu, 160 v normálním, 320 × 180 = obraz), Core `SettingsMapperCameraWindowTests` a `CameraWindowDefaultsTests`,
  - headless `CameraWindowMiniStripUiTests`: poměr obrazu ± 1 DIP při šířce 240 / 320 / 480 pro 16:9 i 4:3, pás pod obrazem, dopočet po změně úchytem a růst od okraje, okno se správnou velikostí se znovu nenastaví, minimum 240, první snímek 4:3, žádné karty, tlačítka okna nad obrazem, `AutomationId` a jména, všechny stavy × 8 jazyků při 240 bez přetečení, zavření a nové otevření, starý pamatovaný stav, dvojklik na pás,
  - upravené testy umístění T-035 / T-040 / T-071 (headless) a E2E očekávání velikosti malého náhledu (320 × 213 se simulátorem 16:9; E2E se v tomto úkolu nespouštěly, jejich průchod je T-093, kam jsou zapsané i kontroly skutečného tažení úchytu a převodu starých souborů),
  - nové snímky `camera-mini-strip-printing.png`, `camera-mini-strip-cooled-de.png`, `camera-mini-strip-240-pl.png`, přegenerované `camera-mini-buttons.png` (240 × 168 s pásem) a `settings-camera-window-errors-fr.png` (chyba s minimem 240), všechny vizuálně zkontrolované.
- Ověřeno na Windows 11: build bez varování, všechny testy zelené, aplikace v izolovaném testovacím režimu se spustí a běží (varování o rozšíření úzkého malého náhledu v logu jednou). Neověřeno: macOS (změna velikosti bez modální smyčky, `BeginResizeDrag` v Avalonia.Native), skutečné tažení úchytu a DPI 125–200 % na skutečném okně (T-093), Native AOT publish (nový kód je bez reflexe, kompilované bindingy).
