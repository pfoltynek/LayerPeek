---
id: T-077
title: Widgety překryvu – model rozložení v Core, uložení a převod nastavení z T-036
status: done
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
- [x] Testy v `LayerPeek.Core.Tests` pokrývají:
  - model a výchozí rozložení podle T-076,
  - validaci (včetně duplicitního id) a rovnost,
  - čisté operace: přesun do kotvy a na pozici v kotvě, velikost, zvýraznění, viditelnost, obnovení,
  - „použít u všech tiskáren“.
- [x] Globální průhlednost: výchozí hodnota 72, round-trip, hranice 30 a 95, hodnota mimo rozsah, starý soubor
- [x] Načtení a uložení nového tvaru:
  - round-trip všech kotev, velikostí a příznaků,
  - výchozí hodnoty se nezapisují,
  - neznámé id widgetu se zachová a zapíše zpět,
  - neznámé hodnoty → varování bez opisu.
- [x] **Převod schématu**, testy pro:
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
- [x] Zrcadlení kotev jako čistá funkce s testem všech 6 kotev × 4 rohy
- [x] Zapisovač sekce `overlay` jedné tiskárny mění jen ji a zapisuje atomicky. Ohlásí zapsané rozložení (pro opětovné načtení v okně nastavení, Q3)
- [x] Scrubber nemění sekci (bajt po bajtu)
- [x] Core nereferencuje Avalonii; vše kompatibilní s Native AOT (source-generated JSON)
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné – Q1–Q4 rozhodnuté 2026-10-24, viz Rozhodnutí)
- (qa-reviewer 2026-10-24, nepovinné) Má nečitelný text (osamocený surrogate, neplatné UTF-8) v typované hodnotě mimo sekci `overlay` – např. v názvu tiskárny nebo jazyce – dál znamenat poškozený soubor (`Corrupted`, nic z něj se nepoužije, soubor zůstane), jako každá hodnota špatného typu, nebo jen varování u té hodnoty? Druhé by vyžadovalo vlastní tolerantní převodník řetězců a rozhodnutí, čím hodnotu nahradit (výchozí / vynechání tiskárny).

## Poznámky k implementaci
- **Model (`Core/Cameras/Overlay/`):**
  - `OverlayAnchor` (6 kotev), `OverlayWidgetSize` (S/M/L), `OverlayWidgetIds` (konstanty v1, `Known`, `IsKnown`, `IsValid`, `MaxLength` 64).
  - `OverlayAnchors`: geometrie kotev (`IsTop/IsLeft/IsRight/IsCenter/IsCorner`), čisté funkce `Mirror(anchor, horizontally, vertically)` a `MirrorBetweenCorners(anchor, from, to)`.
  - `OverlayWidgetPlacement` (record): `WidgetId`, `Anchor`, `Size`, `Emphasized`, `Visible`. Neplatné id nebo neznámá hodnota enumu → výjimka. Viditelnost pro malý náhled nemá.
  - `CameraOverlayLayout` (record, neměnný):
    - konstruktor ověří `null`, duplicitní id (bez ohledu na velikost písmen) a nejvýš `MaxWidgets` = 32 widgetů (pojistka proti nesmyslnému souboru),
    - `Widgets` je seřazený podle kotev (pořadí enumu), v kotvě od okraje okna ke středu. Pořadí mezi kotvami proto neovlivní rovnost,
    - rovnost po prvcích, levný hash; operace beze změny vrací stejnou instanci,
    - `Default` podle T-076 3.3 (status TL zvýrazněný, print-name TC, times + cooldown BL, temperatures BR), `MainAnchor` = `BottomLeft`, `DefaultPlacement(id)`,
    - `Find`, `Contains`, `InAnchor`, `MoveTo(id, anchor, position)` (pozice = index mezi ostatními widgety cílové kotvy), `WithSize`, `WithEmphasized`, `WithVisible`, `ResetToDefault`, `Mirrored`, `FromLegacyCorner(corner)`,
    - `ApplyToAll(printers, layout)` + `AppSettings.WithCameraOverlayLayoutForAllPrinters` („použít u všech tiskáren“, i u vypnuté kamery) a `AppSettings.WithCameraOverlayLayout(printerId, layout)` (převzetí jednoho rozložení v okně nastavení, T-092).
  - `CameraOverlayOpacity` (30–95, výchozí 72) a `AppSettings.CameraOverlayOpacityPercent` (součást rovnosti).
- **Uložení (`settings.json` verze 2):**
  - `printers[*].camera.overlay` = `{ "widgets": [ { "id": "status", "anchor": "TopLeft", "size": "Medium", "emphasized": true, "visible": true }, … ] }`. Zapisuje se celý seznam, jen když se rozložení liší od výchozího. `cameraOverlay.backgroundOpacityPercent` se zapisuje, jen když není 72.
  - Hodnoty sekce `overlay` jsou v dokumentu `JsonElement?` a mapper je čte volně (bez reflexe, typy přes `SettingsJsonContext`). Špatný typ nebo neznámá hodnota v sekci je tak varování, ne poškozený soubor. `overlay`, které není objekt, poškodí soubor jako dřív.
  - Pravidla čtení (implementační detaily, ne produktová volba):
    - položka, která není objekt nebo nemá platné id → přeskočí se,
    - duplicitní id → první výskyt,
    - neznámá kotva / velikost / příznak → výchozí hodnota widgetu (u neznámého id hlavní kotva, M, nezvýrazněný, viditelný),
    - známý widget, který v seznamu chybí (ruční úprava), se doplní na své výchozí místo (na konec své kotvy),
    - neznámých id se drží nejvýš 27, aby se vešly všechny známé widgety.

    Každý druh problému dává jedno varování na tiskárnu bez opisu hodnot.
  - `ResetToDefault` ponechá widgety neznámé této verzi (data novější verze se neztratí).
- **Převod T-036:**
  - Spustí se, když sekce nemá `widgets` (chybí nebo `null`) a má aspoň jeden ze starých klíčů. Nezávisí na čísle verze.
  - `normal` / `mini` se zahodí bez varování (i nesmyslné hodnoty). Platný roh → `FromLegacyCorner`. Neplatný roh (i `TopCenter`, číslo, pole) → výchozí rozložení s varováním bez opisu.
  - Za celý soubor jde jedna informační zpráva s počtem tiskáren do nové vlastnosti `SettingsLoadResult.Notices`. `MonitoringSession` ji loguje na úrovni Information.
- **Zapisovač mimo okno nastavení:**
  - `ISettingsStore.SetCameraOverlayLayoutAsync` (výchozí implementace nic nezapíše) a `JsonSettingsStore` + `Storage/CameraOverlaySectionWriter`, po vzoru `CameraMonitorPathWriter`:
    - nahradí bajty každé hodnoty `overlay` v objektu `camera` každé položky se stejným id, nebo vlastnost přidá za poslední hodnotu objektu `camera`,
    - hodnota se formátuje jako při plném uložení (odsazená podle řádku a s konci řádků souboru, na jednom řádku u jednořádkového objektu). Soubor uložený aplikací se tak po zápisu a dalším plném uložení nezmění,
    - výchozí rozložení zapíše jako `{}`. Bez sekce nic nezapisuje,
    - `version` ani nic jiného nemění. Tiskárnu bez objektu `camera`, chybějící soubor a neplatný JSON nechá být,
    - zápis je atomický pod stejným zámkem jako ostatní zápisy.
  - `ICameraOverlayLayoutWriter` / `CameraOverlayLayoutWriter` po zápisu vyvolá `LayoutSaved` (`CameraOverlayLayoutSavedEventArgs`: id tiskárny, rozložení), jen když se opravdu zapsalo. Událost přijde na vlákně zápisu.
- **Mezistav do T-081 / T-092:**
  - `CameraSettings.Overlay` je nově `CameraOverlayLayout`. Starý překryv a podzáložka T-036 pracují s `CameraSettings.LegacyOverlay` (`CameraOverlayOptions`). Ten je jen v paměti (součást rovnosti, neukládá se), po spuštění vždy výchozí.
  - App: `CameraTarget` bere `LegacyOverlay`, `PrinterEditorViewModel` ukládá rozložení beze změny (`_savedOverlayLayout`), `SettingsViewModel` zachová průhlednost z načteného nastavení. Testy App přepnuté na `LegacyOverlay`.
  - Důsledek: volby podzáložky Překryv (T-036) platí jen do restartu. Rozložení převedené ze starého rohu se zatím nevykreslí. Doporučení: mezi T-077 a T-092 nevydávat verzi (T-081 už vydání mezi T-081 a T-085 zakazuje).
- **Testy (Core):**
  - `Cameras/Overlay/OverlayAnchorsTests` (mimo jiné 6 kotev × 4 rohy), `CameraOverlayLayoutTests`,
  - `Settings/AppSettingsCameraOverlayTests`, `SettingsMapperCameraOverlayTests` (přepsáno na nový tvar), `SettingsMapperOverlayMigrationTests`, `SettingsMapperOverlayOpacityTests`,
  - `JsonSettingsStoreOverlayLayoutTests`: fixture `Fixtures/settings-v1-0.1.0-overlay.json` (tvar souboru 0.1.0 se sekcemi `overlay` a s `cooldownThresholdCelsius` z T-079), zapisovač bajt po bajtu, souběh se zápisem okna nastavení, ohlášení,
  - scrubber (`JsonSettingsStoreScrubTests.CameraOverlayWidgetSectionIsLeftByteForByte`),
  - očekávaná verze 2 v dosavadních testech.

  Všechny soubory jsou pod `TestDirectories`.
- **AOT:** jen `SettingsJsonContext` (přidané `CameraOverlayDocument` a `List<OverlayWidgetDocument>`), žádná reflexe ani nový balíček. Core Avalonii nereferencuje.
- E2E: žádné nové (převod při startu aplikace pokryje T-093).

## Review
- qa-reviewer 2026-10-24: opraveno – řetězec, který nejde přečíst jako text (osamocený escapovaný surrogate, např. `"\ud800"`), v id, kotvě, velikosti nebo starém rohu sekce `overlay` (nebo jako název vlastnosti widgetu) vyhodil `InvalidOperationException` z `LoadAsync` a aplikace pak použila výchozí nastavení celého souboru. Mapper teď čte texty sekce přes `TryGetText` / `NameOf`: takový údaj je neplatná hodnota s varováním (název vlastnosti se ignoruje). Testy v `SettingsMapperCameraOverlayTests`, `SettingsMapperOverlayMigrationTests` a `JsonSettingsStoreOverlayLayoutTests`.
- qa-reviewer 2026-10-24, 2. kolo: oprava z 1. kola bez nálezů. Doplněno:
  - **Nečitelný text mimo sekci `overlay`:** prověřena celá cesta načtení (`JsonSettingsStore.LoadCore`, `SettingsMapper`, `CameraMonitorPathWriter`, `DetectedModelWriter`, `CameraOverlaySectionWriter`, `CameraCredentialScrubber`). Jiné místo s výjimkou mimo `JsonException` není: typované hodnoty (název, adresa, id, uživatel, adresa kamery, jazyk, zvuk, monitor, čísla v uvozovkách…) s osamoceným surrogatem nebo neplatným UTF-8 převede serializer na `JsonException`, tj. soubor je `Corrupted` a zůstane beze změny (stejně jako hodnota špatného typu); žádný zapisovač nevyhodí výjimku. Úklid přihlašovacích údajů kamery a cesta monitoru soubor nechají být; zapisovače jedné hodnoty pracující po bajtech (zjištěný model, sekce `overlay`) do syntakticky platného souboru dál zapíšou jen svou hodnotu (tak to `ISettingsStore` dokumentuje) a nečitelný text i vše ostatní zůstane bajt po bajtu. Nová regresní sada `JsonSettingsStoreUnreadableTextTests` (načtení i všechny zapisovače, obě varianty nečitelného textu).
  - **`CameraOverlayLayoutWriter`:** id tiskárny se ořízne (`Trim`) stejně jako v úložišti, takže ohlášené id najde `AppSettings.WithCameraOverlayLayout`. Výjimka odběratele `LayoutSaved` se zaloguje (Error), ostatní odběratelé dostanou událost a hotový zápis dál vrací `true` (vzor `CooldownMonitor`); konstruktor má volitelný `ILogger`.
  - **Novější formát souboru:** plné uložení souboru s `version` > 2 nic neodmítá – načtení jen varuje, že neznámé hodnoty se uložením ztratí, a uložení pak soubor přepíše. Zápis samotné sekce `overlay` ale probíhá bez toho, aby uživatel varování viděl, proto `CameraOverlaySectionWriter` soubor novějšího formátu (poslední kořenové `version` > 2, i v uvozovkách) nebo s nečitelnou verzí (desetinné číslo, text, bool, objekt) nechá beze změny a `SetCameraOverlayLayoutAsync` vrátí `false`.
- qa-reviewer 2026-10-24, 3. kolo (potvrzení 9ca1877): kód bez nálezů (kořenové `version` i duplicity a verze v uvozovkách odpovídají čtení serializeru, id tiskárny se loguje i jinde, vzor odběratelů jako `CooldownMonitor`). Opraven test `WritersLeaveAFileWithUnreadableTextAlone` → `WritersNeverThrowAndKeepAFileWithUnreadableTextByteForByte`: po úklidu údajů a zápisu zjištěného modelu nic nekontroloval a u sekce `overlay` jen "soubor se změnil". Teď ověřuje, že úklid a cesta monitoru soubor nemění a že zjištěný model i `overlay` změní jen svou hodnotu (vložení, příp. náhrada vlastní nečitelné hodnoty `detectedModel`). Výše opraveno tvrzení, že zapisovače takový soubor vždy nechají být.
