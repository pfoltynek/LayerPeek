---
id: T-077
title: Widgety překryvu – model rozložení v Core, uložení a převod nastavení z T-036
status: todo
owner: core-dev
priority: P2
platform: all
depends_on: [T-076]
---

## Cíl
Úkol dodá v Core:
- model rozložení widgetů překryvu okna kamery (náhrada `CameraOverlayOptions`),
- jeho uložení v `settings.json`,
- **testovaný převod** starého nastavení T-036 (roh + položky) na rozložení widgetů při prvním načtení.

## Kontext
- Dnešní stav:
  - `Core/Cameras/CameraOverlayOptions` (`Normal`, `Mini` = `CameraOverlayItems` flags, `Corner` = 4 rohy) je součást `CameraSettings.Overlay`.
  - Ukládá se do `printers[*].camera.overlay` jako `{ "corner": "TopRight", "normal": ["PrinterName", …], "mini": [ … ] }`. Zapisuje se jen to, co se liší od výchozích hodnot. `version` dokumentu je 1 (`Core/Settings/Storage/SettingsDocument.cs`, `SettingsMapper.cs`).
  - Scrubber (`CameraCredentialScrubber`) sekci nemění.
- Výchozí rozložení a podrobnosti pravidla převodu určí schválený návrh T-076.

## Rozhodnutí (uživatel, 2026-10-24)
- **Rozložení u tiskárny (Q1):** rozložení je **u tiskárny** (`printers[*].camera.overlay`, jako T-036). K tomu akce **„Použít toto rozložení u všech tiskáren“**. Core pro ni dodá čistou operaci, UI ji dostane v T-092.
- **Jedna instance:** v1 má každý typ widgetu v rozložení nejvýš jednou. Duplicitní id v souboru → ponechá se první výskyt, jedno varování.
- **Malý náhled bez karet:** přes obraz malého náhledu se karty nekreslí. `OverlayWidgetPlacement` proto **nemá** viditelnost pro malý náhled. Stav sbalení řádků pásu malého náhledu je pamatovaný stav okna (`camera-windows.json`, T-090), ne součást rozložení.
- **Průhlednost pozadí:** jedno **globální** nastavení pro všechny tiskárny, rozsah **30–95 %**, výchozí **72 %**. Patří do `AppSettings`, ne do rozložení tiskárny.
- **Pořadí v kotvě** jde měnit (tažením v T-091). Model proto potřebuje operaci „přesunout na pozici v kotvě“.
- **Celá obrazovka (T-076 Q4):** celá obrazovka používá stejné rozložení jako normální okno. Rozložení má proto jen jednu viditelnost.
- **Verze souboru (Q2):** `SettingsDocument.CurrentVersion` = **2**. Staré klíče (`corner` / `normal` / `mini`) se **nezapisují**. Po návratu k verzi 0.1.0 ukáže starší aplikace výchozí překryv (a varování o novější verzi souboru). To je přijatelné.
- **Převod (Q4):** **žádný ztrátový převod viditelnosti.** Viditelnost starých položek (`normal` i `mini`) se zahodí a widgety začínají z výchozího rozložení. Přenese se jen starý **roh** (pravidlo viz Rozsah).
- **Souběh uložení (Q3):** když se rozložení uloží z okna kamery a okno nastavení je otevřené, okno nastavení **znovu načte jen část s rozložením** dané tiskárny. Ostatní neuložené změny si ponechá. Při následném uložení z okna nastavení se tak zapíše nové rozložení, ne to staré. App část dodá T-092, Core dodá událost / notifikaci o zapsaném rozložení.

## Rozsah (návrh struktury; přesné názvy může core-dev upravit)
- `Core/Cameras/Overlay/`:
  - `OverlayAnchor` – 6 kotev: `TopLeft`, `TopCenter`, `TopRight`, `BottomLeft`, `BottomCenter`, `BottomRight`.
  - `OverlayWidgetSize` – `Small`, `Medium`, `Large`.
  - `OverlayWidgetIds` – konstanty stabilních textových id widgetů v1: `status`, `times`, `temperatures`, `print-name`, `cooldown`.
    - Id je **text**, ne enum. Nový widget = nová konstanta + třída a jedna registrace v App (T-081), Core se jinak nemění.
    - Neznámé id ze souboru (zapsané novější verzí aplikace) se při načtení **zachová** a při uložení zapíše zpět beze změny, jen se nevykreslí.
  - `OverlayWidgetPlacement` (record): `WidgetId`, `Anchor`, `Size`, `Emphasized`, `Visible` (normální okno i celá obrazovka – sdílejí rozložení). Pořadí v kotvě = pořadí v seznamu.
  - `CameraOverlayLayout` (record):
    - seznam umístění, každé id nejvýš jednou, `Default`,
    - validace: neznámé hodnoty enumů → výjimka v modelu, při načtení výchozí hodnota s varováním,
    - hodnotová rovnost (porovnání seznamu po prvcích).
  - Čisté funkce pro App bez Avalonie, každá vrací nový `CameraOverlayLayout`:
    - widgety dané kotvy v pořadí,
    - přesun widgetu do jiné kotvy a **na danou pozici v kotvě**,
    - změna velikosti, zvýraznění, viditelnost,
    - obnovení výchozích.

    Použije je režim rozložení (T-091) a nastavení (T-092).
  - Operace „použít rozložení u všech tiskáren“ nad seznamem `PrinterDefinition`: rozložení zkopíruje i k tiskárnám s vypnutou kamerou a nic jiného nezmění.
- `AppSettings`: globální průhlednost pozadí karet, např. `CameraOverlayOpacityPercent`, 30–95, výchozí 72.
  - Uloží se do `settings.json` (např. `cameraOverlay.backgroundOpacity`). Výchozí hodnota se nezapisuje.
  - Hodnota mimo rozsah → výchozí s varováním.
- `CameraSettings.Overlay` přejde na `CameraOverlayLayout`. `CameraOverlayOptions`, `CameraOverlayItems` a `CameraOverlayCorner` zůstanou jen tak dlouho, jak je potřeba pro převod. App je přestane používat v T-081 a odstranění starých typů je součástí T-081.
- **Uložení rozložení:** `printers[*].camera.overlay` v novém tvaru, např. `{ "widgets": [ { "id": "status", "anchor": "BottomLeft", "size": "Medium", "emphasized": true, "visible": true } ] }`.
  - Zapisuje se jen to, co se liší od výchozích hodnot. Starý soubor bez sekce se uložením nezmění.
  - Názvy hodnot se čtou bez ohledu na velikost písmen.
  - Neznámé hodnoty → jedno varování bez opisu hodnot (stejně jako T-036).
- **Převod T-036 → widgety** se provede při načtení sekce se starými klíči `corner` / `normal` / `mini` a bez `widgets`:
  - Výběry položek `normal` a `mini` se **zahodí** (žádný převod viditelnosti).
  - Výsledek = **výchozí rozložení** (T-076), jen přesunuté podle starého rohu.
  - **Pravidlo pro roh:** výchozí rozložení má „hlavní kotvu“ (kotva widgetů Stav a Časy, určí T-076; očekává se `BottomLeft`, výchozí roh T-036).
    - Starý roh určí, kam se hlavní kotva přesune.
    - Celé rozložení se **zrcadlí**: vodorovně, když se liší strana (vlevo / vpravo), a svisle, když se liší výška (nahoře / dole).
    - Zrcadlením se kotvy mapují takto: `Left` ↔ `Right`, `Top` ↔ `Bottom`, `TopCenter` ↔ `BottomCenter`. Středové kotvy se při vodorovném zrcadlení nemění.
    - Pořadí a velikosti widgetů zůstanou. Vzájemné uspořádání karet se tak zachová a hlavní skupina je tam, kde ji měl uživatel.
    - Příklad: výchozí Stav + Časy `BottomLeft` a Teploty `TopLeft`, starý roh `TopRight` → Stav + Časy `TopRight`, Teploty `BottomRight`.
    - Starý roh = hlavní kotva (výchozí `BottomLeft`) → výsledek je přesně výchozí rozložení a sekce se nezapíše.
    - Neplatný starý roh → výchozí rozložení s varováním.
  - Převod je deterministický a idempotentní.
  - Nový tvar se zapíše až při dalším uložení nastavení. Při uložení se zapíše jen nový tvar (`version` 2), staré klíče ne.
  - Do logu jde jedna informační zpráva bez hodnot.
- `SettingsDocument.CurrentVersion` = 2. Soubor verze 1 se načte s převodem. Soubor vyšší verze → stávající varování.
- Core událost nebo výsledek zápisu z okna kamery (např. `ISettingsChangeNotifier` / událost zapisovače s id tiskárny a novým rozložením). Okno nastavení z ní znovu načte část s rozložením (Q3, UI v T-092).
- **Zápis rozložení mimo okno nastavení** (z režimu rozložení v okně kamery, T-091):
  - zapisovač po vzoru `CameraMonitorPathWriter` / `DetectedModelWriter`,
  - atomicky přepíše jen sekci `overlay` jedné tiskárny a nic jiného (ověřeno testem bajt po bajtu pro zbytek souboru).
- Scrubber novou sekci nemění (test bajt po bajtu).

## Akceptační kritéria
- [ ] Testy v `LayerPeek.Core.Tests` pokrývají:
  - model a výchozí rozložení podle T-076,
  - validaci (včetně duplicitního id) a rovnost,
  - čisté operace: přesun do kotvy a na pozici v kotvě, velikost, zvýraznění, viditelnost, obnovení,
  - „použít u všech tiskáren“.
- [ ] Globální průhlednost: výchozí hodnota 72, round-trip, hranice 30 a 95, hodnota mimo rozsah, starý soubor
- [ ] Načtení a uložení nového tvaru:
  - round-trip všech kotev, velikostí a příznaků,
  - výchozí hodnoty se nezapisují,
  - neznámé id widgetu se zachová a zapíše zpět,
  - neznámé hodnoty → varování bez opisu.
- [ ] **Převod schématu**, testy pro:
  - každý ze 4 starých rohů → očekávané zrcadlené rozložení (každá kotva každého widgetu),
  - výchozí roh `BottomLeft` → přesně výchozí rozložení a nic se nezapíše,
  - libovolné výběry `normal` / `mini` (žádná položka, všechny, jen jedna) → viditelnost je vždy výchozí,
  - neplatný roh → výchozí rozložení s varováním,
  - soubor z verze 0.1.0 (fixture skutečného `settings.json`, verze 1),
  - soubor bez sekce `overlay`,
  - soubor s oběma tvary (nový má přednost),
  - opakované načtení a uložení (idempotence),
  - uložený soubor má `version` 2 a žádné staré klíče,
  - soubor verze 3 → varování jako dosud.
- [ ] Zrcadlení kotev jako čistá funkce s testem všech 6 kotev × 4 rohy
- [ ] Zapisovač sekce `overlay` jedné tiskárny mění jen ji a zapisuje atomicky. Ohlásí zapsané rozložení (pro opětovné načtení v okně nastavení, Q3)
- [ ] Scrubber nemění sekci (bajt po bajtu)
- [ ] Core nereferencuje Avalonii; vše kompatibilní s Native AOT (source-generated JSON)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné – Q1–Q4 rozhodnuté 2026-10-24, viz Rozhodnutí)

## Poznámky k implementaci
(vyplní agent po dokončení)
