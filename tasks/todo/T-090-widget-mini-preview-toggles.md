---
id: T-090
title: Malý náhled – sbalitelný název tisku a graf teplot v pásu, zapamatování stavu
status: todo
owner: ui-dev
priority: P2
platform: all
depends_on: [T-084, T-085, T-089]
---

## Cíl
Do informačního pásu malého náhledu (T-089) přidat dva řádky, které jde nezávisle sbalit a rozbalit přímo v malém okně:
- **název tisku**: kompaktní podoba widgetu Název tisku z T-085,
- **graf teplot**: řádek se sparkline ≈ 60 px, kompaktní `TemperatureChart` z T-084.

Ovládají je dvě malá přepínací tlačítka v základním řádku pásu. Stav každého řádku se pamatuje i po restartu.

## Rozhodnutí (uživatel, 2026-10-24)
- **Přepínače:** malý náhled ukazuje sparkline teplot **i** název tisku. Každý řádek jde sbalit a rozbalit zvlášť malými přepínacími tlačítky přímo v malém okně. Tlačítka jdou ovládat klávesnicí a mají jména pro čtečku obrazovky i `AutomationId`.
- **Výchozí stav (Q1):** oba řádky jsou **sbalené**.
- **U tiskárny (Q2):** stav sbalení je **u tiskárny** a pamatuje se (přežije restart).
- **Zapomenutí (Q3):**
  - Stav sbalení se zapomíná **spolu s pamatovaným stavem okna**: `CameraWindowStateStore.Remove` po změně výchozího okna v nastavení ho zapomene také.
  - Stav sbalení se ale **ukládá i při vypnutém „pamatovat poslední umístění“** (`RememberLastPlacement = false`). Ta volba se týká jen polohy a velikosti.
- **Bez dat (Q4):** bez úlohy **řádek názvu zmizí** a tlačítko zůstane. Bez historie ukáže řádek grafu **„Žádná data“**.

## Rozsah
- **Přepínací tlačítka** (`ToggleButton`):
  - stav pro čtečku obrazovky (sbaleno / rozbaleno) a tooltip,
  - `AutomationId` podle návrhu, například `CameraMiniToggleName` a `CameraMiniToggleChart`, řádky `CameraMiniNameRow` a `CameraMiniChartRow`,
  - ovládání klávesnicí: Tab mezi tlačítky, mezerník / Enter. Malé okno je bez rámu, fokus musí být vidět,
  - kliknutí na tlačítko nezačne tažení okna.
- Rozbalení nebo sbalení změní výšku pásu a okna podle pravidel T-089: obraz drží poměr streamu, okno roste od nejbližšího okraje a zůstane na obrazovce. Zmizení řádku názvu (konec úlohy) okno také zmenší.
- **Uložení stavu:**
  - v `camera-windows.json` u tiskárny (`CameraWindowState`, `App/Cameras/CameraWindowStateStore`), vedle režimu a umístění,
  - pole jsou nepovinná, chybějící hodnota = sbaleno,
  - ukládá se při přepnutí, po vzoru změny režimu, a to i při `RememberLastPlacement = false`,
  - zapomene se v `Remove`.
- Odběr historie teplot jen při rozbaleném grafu a otevřeném okně. Sbalený graf nic nestojí.
- Nové texty (tooltipy, jména pro čtečku obrazovky, „Žádná data“, pokud ještě neexistuje) ve všech 8 jazycích.

## Akceptační kritéria
- [ ] App.Tests:
  - uložení a načtení stavu v `camera-windows.json`: nová pole, starý soubor bez nich (= sbaleno), neplatné hodnoty,
  - uložení i při `RememberLastPlacement = false`,
  - zapomenutí přes `Remove`,
  - změna výšky při přepnutí a při zmizení řádku názvu,
  - odběr historie jen při rozbaleném grafu.
- [ ] Headless UI testy:
  - výchozí stav (oba sbalené) a všechny 4 kombinace,
  - přepnutí myší i klávesnicí, stav tlačítek pro čtečku obrazovky,
  - okno roste a zmenšuje se a obraz drží poměr,
  - bez úlohy řádek názvu zmizí, bez historie graf ukáže „Žádná data“,
  - stav přežije zavření a nové otevření okna,
  - nejdelší jazyk při šířce 240, snímky reference.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné – Q1–Q4 rozhodnuté 2026-10-24, viz Rozhodnutí)

## Poznámky k implementaci
(vyplní agent po dokončení)
