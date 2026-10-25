---
id: T-094
title: Okna Nastavení a Diagnostika – výchozí velikost 780 × 532, zvětšení na celou obrazovku a zapamatování velikosti
status: done
owner: ui-dev
priority: P2
platform: all
depends_on: []
---

## Cíl
Pomocná okna **Nastavení** a **Diagnostika** dnes mají pevnou výchozí velikost (klient 780 × 508), nejdou maximalizovat a při každém otevření začínají znovu ve výchozí velikosti. Po úpravě:
- obě okna mají novou společnou výchozí velikost **780 × 532** (rozhodnutí uživatele 2026-10-25, T-092 otázka Q2), minimum zůstává **640 × 400**,
- obě okna jde **maximalizovat**,
- každé okno si **pamatuje** velikost a stav maximalizace, které nastavil uživatel, mezi otevřeními i po restartu aplikace,
- okno se vždy vejde na pracovní plochu aktuálního monitoru (`AuxiliaryWindowPlacement.Fit`),
- posuvník se ukáže jen tehdy, když se obsah nevejde.

Úkol nezávisí na sérii widgetů. Změna výchozí velikosti je tady, aby se do aplikace dostala dřív; T-092 na tento úkol nezávisí (když tento úkol ještě nebude hotový, T-092 změní výchozí velikost sám se stejnými hodnotami).

## Rozhodnutí (uživatel, 2026-10-25)
- Výchozí klient Nastavení i Diagnostiky **780 × 532** (stejný u obou), minimum **640 × 400** u obou.
- Přizpůsobení pracovní ploše (`AuxiliaryWindowPlacement.Fit`, `WorkAreaFitter`) zůstává a platí i pro zapamatovanou velikost.
- Posuvník jen tehdy, když je potřeba (každá záložka a Diagnostika už mají `ScrollViewer`).
- Cílové obrazovky: nejhorší běžná pracovní plocha notebooku **≈ 1093 × 580 DIP** (1366 × 768 při 125 %, 1920 × 1080 při 175 %). Nahrazuje cíl 800 × 600 při 100 % z T-033 (výpočet s hlavním panelem 48 DIP v T-092, Návrh UI 2).
- Okna jde maximalizovat (`CanMaximize`); velikost a stav maximalizace se pamatují pro každé okno zvlášť mezi otevřeními i restarty aplikace.

## Kontext
- `AuxiliaryWindowPlacement` (`DefaultClientSize`, `MinClientSize`, `Fit`, `SizeBeforeShowing`, `CorrectionAfterOpening`, `ChromeOf`) a `WorkAreaFitter` v `src/LayerPeek.App/Windows/`.
- `SettingsWindow.axaml` a `DiagnosticsWindow.axaml`: `Width="780"`, `Height="508"`, `MinWidth="640"`, `MinHeight="400"`, `CanMaximize="False"`, `WindowStartupLocation="CenterScreen"`; komentáře v XAML i v kódu odkazují na cíl 800 × 600 při 100 %.
- Diagnostika: graf vyplňuje zbývající výšku (min. 120), větší okno zvětší graf.
- Stav oken kamer se pamatuje v `camera-windows.json` (`ICameraWindowStateStore` / `JsonCameraWindowStateStore`): stav aplikace, ne nastavení; atomický zápis, zápisy na pozadí, poškozený soubor = výchozí hodnoty, opakování při zamčeném souboru (`FileRetry`). Okno nastavení ho nikdy neukládá, takže uložení nastavení nepřepíše polohu okna a naopak.
- Testy: `AuxiliaryWindowPlacementTests` (App.Tests), `SettingsWindowLayoutTests`, `DiagnosticsWindowLayoutTests`, `SettingsWindowSnapshotTests` a snímky `settings-*.png` / `diagnostics-*.png` (UiTests), E2E `SettingsAndDiagnosticsTests`, `AuxiliaryWindowScaleAndThemeTests`, `DisplayChangeTests`.

## Rozsah
- **Výchozí velikost 780 × 532:** `AuxiliaryWindowPlacement.DefaultClientSize`, `Height` v obou XAML, komentáře (nový cíl ≈ 1093 × 580 DIP místo 800 × 600 při 100 %), testy velikosti, přegenerované a zkontrolované snímky reference obou oken.
- **Maximalizace:** `CanMaximize="True"` u obou oken (`CanMinimize` beze změny). Obsah se při maximalizaci roztáhne podle stávajících sloupců `*` (viz Otevřené otázky Q3).
- **Zapamatování velikosti** pro každé okno zvlášť (klíč např. `settings`, `diagnostics`):
  - ukládá se **velikost klientu v DIP** v normálním stavu a příznak **maximalizováno**; poloha se neukládá, okno se dál centruje (`CenterScreen`) – viz Otevřené otázky Q2,
  - ukládá se při zavření okna (případně po dokončené změně velikosti nebo stavu, ne při každém kroku tažení), jen když ji změnil uživatel: velikost zmenšená kvůli malé pracovní ploše se neuloží jako nová preference,
  - u maximalizovaného okna se pamatuje i velikost, na kterou se vrátí po obnovení (poslední normální velikost),
  - úložiště: viz Otevřené otázky Q1 (návrh: nový soubor stavu aplikace vedle `settings.json` se stejnými zásadami jako `camera-windows.json`), explicitní registrace v DI, `System.Text.Json` jen přes zdrojově generovaný `JsonSerializerContext`,
  - zápis nikdy neblokuje UI vlákno a chyba souboru okno nerozbije (výchozí hodnoty, záznam do logu).
- **Obnovení při otevření:**
  - bez uloženého stavu: výchozí 780 × 532 jako dnes (přizpůsobená pracovní ploše),
  - uložená velikost se vždy prožene `AuxiliaryWindowPlacement.Fit` na pracovní plochu **aktuálního** monitoru (monitor, kde se okno otevře): větší než pracovní plocha → zmenší se, menší než minimum → minimum, nečíselné, nekonečné nebo záporné hodnoty → výchozí,
  - uložený stav maximalizace → okno se otevře maximalizované na monitoru, kde se otevírá; po obnovení má uloženou (a přizpůsobenou) normální velikost,
  - odpojený monitor nebo menší rozlišení / vyšší měřítko od posledního uložení → stejné pravidlo `Fit`, nic se nedostane mimo obrazovku.
- Čisté funkce pro výpočet (obnovení, přizpůsobení, rozhodnutí „uložit / neuložit“) v `AuxiliaryWindowPlacement` nebo vedle něj, testovatelné bez obrazovky; `WorkAreaFitter` je jen použije.
- Aktualizovat `docs/architecture.md` (nové úložiště stavu oken) a `docs/troubleshooting.md` (co se maže / co zůstává, podobně jako `camera-windows.json`); případně podpůrný balíček (`SupportBundle`), pokud do něj stav oken kamer patří.

## Akceptační kritéria
- [x] App.Tests (čisté funkce a úložiště):
  - výchozí velikost 780 × 532 a minimum 640 × 400 v `AuxiliaryWindowPlacement`, shodné s XAML obou oken,
  - obnovení: bez stavu → výchozí; uložená velikost menší než pracovní plocha → beze změny; větší → zmenšená na pracovní plochu; menší než minimum / neplatná → minimum / výchozí; různá měřítka (100 / 125 / 175 %),
  - monitor odpojený nebo menší než při uložení (např. uloženo na 2560 × 1440, otevřeno na 1366 × 768 při 125 %) → okno celé na pracovní ploše, nejmenší velikost je minimum,
  - maximalizace: uložený příznak se obnoví, normální velikost po obnovení je uložená (přizpůsobená) velikost,
  - velikost zmenšená jen kvůli pracovní ploše se neuloží jako preference; velikost změněná uživatelem ano,
  - úložiště: zápis a čtení obou oken zvlášť, poškozený / příliš velký / chybějící soubor → výchozí hodnoty bez výjimky, atomický zápis, žádné soubory při samotném vytvoření úložiště, testy po sobě nic nenechají.
- [x] Headless UI testy (`LayerPeek.App.UiTests`):
  - obě okna mají `CanMaximize` a dají se maximalizovat a obnovit; po zavření a novém otevření mají uloženou velikost i stav maximalizace,
  - **bez posuvníku ve výchozí velikosti 780 × 532 ve všech 8 jazycích** u všech záložek a podzáložek Nastavení (stavy z `SettingsWindowLayoutTests`) i u Diagnostiky,
  - **posuvník se objeví**, když se okno zmenší (např. na minimum 640 × 400), a obsah zůstane dosažitelný,
  - maximalizované okno: rozvržení bez přetečení a ořezů (snímek reference maximalizovaného okna u obou oken),
  - dosavadní testy rozvržení obou oken v nové velikosti, přegenerované a vizuálně zkontrolované snímky `settings-*.png` a `diagnostics-*.png`.
- [x] E2E (Windows, `LayerPeek.E2E.Tests`, jen co headless neověří): maximalizace přes UI Automation (`WindowPattern`), zavření, **restart aplikace** a nové otevření → okno je znovu maximalizované; změněná velikost přežije restart; volitelně snížené rozlišení přes stávající infrastrukturu `DisplayChangeTests` (se zálohou, obnovou a varováním podle pravidel E2E) → okno se vejde na pracovní plochu.
- [x] Žádná reflexní serializace ani bindingy, žádné trim/AOT varování
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Rozhodnutí (uživatel, 2026-10-25)
Uživatel přijal návrhy architekta:
- **Q1 – kam ukládat velikost oken:** nový soubor stavu aplikace **`windows.json`** vedle `settings.json`, se stejnými zásadami jako `camera-windows.json` (stav aplikace, ne nastavení; uložení nastavení ho nepřepíše).
- **Q2 – poloha:** **nepamatuje se**, okno se dál centruje na monitoru, kde se otevírá.
- **Q3 – obsah maximalizovaného okna:** obsah se **roztáhne** podle stávajících sloupců, ověří se snímkem.
- **Q4 – „Zrušit“ v Nastavení:** velikost se uloží **i po „Zrušit“**, je to stav okna, ne nastavení.

## Otevřené otázky
- Žádné.

## Poznámky k implementaci
- **Velikost:** `AuxiliaryWindowPlacement.DefaultClientSize` = 780 × 532, `Height="532"` a `CanMaximize="True"` v `SettingsWindow.axaml` i `DiagnosticsWindow.axaml`, komentáře s novým cílem ≈ 1093 × 580 DIP. Nové čisté funkce `IsUsable` (konečné, kladné, nejvýš 16384) a `PreferredClientSize` (uložená velikost, nejméně minimum; neplatná → výchozí).
- **Úložiště `windows.json`** (`App/Windows/AuxiliaryWindowStateStore.cs`): `IAuxiliaryWindowStateStore`, `JsonAuxiliaryWindowStateStore` (vzor `JsonCameraWindowStateStore`: atomický zápis přes `.tmp`, zápisy na pozadí za sebou, novější stav nahradí nezapsaný starší, `FileRetry`, nic na disku při vytvoření, nezměněný stav se nezapisuje), `MemoryAuxiliaryWindowStateStore` bez datové složky. Zdrojově generovaný `AuxiliaryWindowsJsonContext`. Poškozený, > 64 KB, bez `version` nebo s jinou verzí než 1 (i **novější**) → výchozí hodnoty a záznam do logu; další zápis soubor nahradí verzí 1 (stav oken je postradatelný, novější formát se nevykládá). Neplatná velikost se zapomene, příznak maximalizace zůstane. Registrace v DI explicitně (`AppServices`), `TrayHost` soubor čte souběžně s nastavením (čeká nejvýš 1 s, aby okno otevřené hned po startu už mělo uložený stav) a při ukončení počká na zápis jako u `camera-windows.json`. Klíče `settings` a `diagnostics`. Příklad:
  ```json
  { "version": 1, "windows": { "diagnostics": { "width": 700, "height": 450, "maximized": false }, "settings": { "maximized": true } } }
  ```
- **Co se uloží** (`AuxiliaryWindowSizeMemory`, čistá třída): jen velikost od uživatele v normálním stavu – události `Resized` s důvodem `User` nebo `Unspecified`, okno ve stavu `Normal`, velikost jiná než ta, kterou nastavila aplikace (`WorkAreaFitter.SetSize` před zobrazením i oprava po otevření, tolerance 1 DIP kvůli zaokrouhlení na pixely). `Application`, `Layout` a `DpiChange` se nepočítají. Windows hlásí `WM_SIZE` s maximalizovanou velikostí dřív než změnu stavu (ověřeno ve zdrojích Avalonia 12.1.4 `WindowImpl.AppWndProc`), proto se poslední zapamatovaná změna při přechodu do `Maximized`/`FullScreen` vrátí. Velikost se zaokrouhlí na 0,1 DIP. Ukládá se při `Closed` okna – tedy po „Uložit“, „Zrušit“, křížku i při ukončení aplikace (`AuxiliaryWindowSlot.Shutdown`).
- **Obnovení** (`WorkAreaFitter.Remember`, volá `AuxiliaryWindowFactory` s úložištěm z `TrayHost.WindowStates`): `PreferredClientSize` → stávající `SizeBeforeShowing` / `CorrectionAfterOpening` (Fit na monitoru, kde se okno otevírá), uložená maximalizace → `WindowState = Maximized` před zobrazením; poloha dál `CenterScreen`.
- **Testy:** App.Tests `AuxiliaryWindowPlacementTests` (přepočítané na 532, cílové obrazovky 100/125/150/175 %, uložená velikost menší/větší než plocha, monitor vlevo od hlavního, plocha menší než minimum, neplatné hodnoty), `AuxiliaryWindowSizeMemoryTests` (14 testů: zmenšení kvůli ploše se neuloží, pořadí událostí Windows i opačné, obnovení, full screen), `AuxiliaryWindowStateStoreTests` (formát, oba klíče zvlášť, poškozený / velký / novější / bez verze soubor, souběžné zápisy dvou oken ze dvou vláken, nezměněný stav nic nevytvoří, nezapisovatelné místo). UiTests `AuxiliaryWindowSizeUiTests` (maximalizace a obnovení obou oken, znovuotevření se stavem i po „Zrušit“, každé okno svůj stav, větší uložená velikost se přizpůsobí a nezapomene, menší než minimum → minimum, při 640 × 400 posuvník a dosažitelný konec obsahu na všech záložkách a podzáložkách i v Diagnostice), nové teorie `MaximizedWindowStretchesWithoutScrollingOrCutTexts` v obou `*LayoutTests` (8 jazyků, 1366 × 705 a 1920 × 1017), snímky `settings-maximized.png` a `diagnostics-maximized.png`; všechny snímky `settings-*` a `diagnostics-*` přegenerované na 780 × 532 a prohlédnuté. Headless okno samo uživatelskou změnu velikosti neohlásí, testy proto volají `WorkAreaFitter.OnResized(…, WindowResizeReason.User)` (interní vstup, který jinak volá událost `Resized`).
- **E2E** `AuxiliaryWindowSizeTests` (nový `AppInstance.RestartAsync`, `Native.IsZoomed`): maximalizace přes `WindowPattern`, zavření „Zrušit“, změna velikosti Diagnostiky přes `TransformPattern` na 700 × 450, restart → Nastavení maximalizované, po obnovení výchozí velikost uprostřed monitoru, Diagnostika 700 × 450; obnovené a zavřené → po dalším restartu ne maximalizované. `WindowChecks`: 532 a `AssertCloseAndMaximizeButtons` místo `AssertCloseButtonOnly`. Spuštěno lokálně na Windows 11 (`LAYERPEEK_E2E=1`, přímo `dotnet test`, ne přes `run-e2e.ps1`): `AuxiliaryWindowSizeTests` i `SettingsAndDiagnosticsTests` (18 testů, 8 jazyků, bez posuvníku při 780 × 532) zelené. Volitelný test se sníženým rozlišením (`DisplayChangeTests`, systémová změna) nepřidán ani nespuštěn; `AuxiliaryWindowScaleAndThemeTests` (systémové změny) nespuštěny.
- **Windows:** s maximalizací a bez minimalizace zobrazuje Windows v titulku i šedé (neaktivní) tlačítko minimalizace – chování systému pro `WS_MAXIMIZEBOX` bez `WS_MINIMIZEBOX`.
- **macOS: neověřeno.** Pořadí událostí zoom/resize a důvod změny velikosti (`WindowResizeReason`) na macOS jsem nemohl spustit; logika je na pořadí nezávislá (testy obou pořadí), ale chování zelené tlačítko „zoom“ = `Maximized` a ukládání velikosti po tažení je třeba ověřit na Macu.
- Podpůrný balíček `windows.json` neobsahuje (jako `camera-windows.json`); `docs/architecture.md` (nová sekce) a `docs/troubleshooting.md` doplněny.
