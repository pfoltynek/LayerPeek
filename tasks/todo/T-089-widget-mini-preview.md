---
id: T-089
title: Malý náhled – informační pás pod obrazem (průběh + čas) a výška okna podle pásu
status: todo
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
- [ ] App.Tests – výpočet geometrie:
  - výška z šířky, poměru (16:9, 4:3, bez snímku) a výšky pásu,
  - růst nahoru u spodního okraje a dolů u horního,
  - oříznutí na pracovní plochu ve všech 4 rozích, s hlavním panelem a na více monitorech s různým DPI,
  - dopočet výšky po změně velikosti úchyty.
- [ ] App.Tests – nastavení a stav:
  - minimální šířka 240 (validace v nastavení, načtení menší uložené hodnoty),
  - 320×180 = velikost obrazu,
  - převod starého pamatovaného stavu.
- [ ] Headless UI testy:
  - obraz má poměr streamu (tolerance 1 DIP) při různých šířkách, pás je pod obrazem,
  - po simulované změně velikosti se výška dopočítá,
  - žádné karty přes obraz,
  - text ve všech stavech tiskárny, nejdelší jazyk bez přetečení při šířce 240,
  - tlačítka okna nepřekrývají pás,
  - snímky reference.
- [ ] Testy umístění z T-035 a T-071 upravené a zelené. E2E kontroly na skutečném okně jsou zapsané do T-093
- [ ] Nové texty ve všech 8 jazycích (validace minimální šířky)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné – Q1–Q5 rozhodnuté 2026-10-24, viz Rozhodnutí)

## Poznámky k implementaci
(vyplní agent po dokončení)
