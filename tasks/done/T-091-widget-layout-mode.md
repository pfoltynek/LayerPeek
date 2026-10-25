---
id: T-091
title: Režim rozložení v okně kamery (tažení do kotev, pořadí, popover, klávesnice)
status: done
owner: ui-dev
priority: P2
platform: all
depends_on: [T-077, T-082, T-083]
---

## Cíl
Varianta **C** ze schváleného návrhu: rozložení widgetů se upravuje přímo v okně kamery.
- Obraz se ztlumí a 6 kotev se ukáže jako čárkované zóny.
- Widget se přetáhne do kotvy nebo na jiné místo v kotvě. Cílová zóna se zvýrazní a widget se přichytí.
- Klik na widget otevře popover s volbou „zvýraznit“ a velikostí S/M/L.
- Lišta nabízí „Obnovit výchozí“ a „Hotovo“.
- Vše jde ovládat i klávesnicí a má `AutomationId` pro UI testy.

## Rozhodnutí (uživatel, 2026-10-24)
- **Vstup:** **tlačítko v řadě tlačítek okna kamery** (`CameraWindowButtons`) a **položka kontextové nabídky** (pravé tlačítko na obraze).
- **Kde:** jen v **normálním okně**. V malém náhledu ani na celé obrazovce režim není (tlačítko ani položka se tam nezobrazí). Celá obrazovka sdílí rozložení normálního okna, takže úprava platí i pro ni.
- **Uložení:** uloží se až po **„Hotovo“**. **Esc** (mimo otevřený popover) změny zahodí a režim ukončí. Tlačítko „Zrušit“ jen podle návrhu T-076.
- **Pořadí v kotvě:** karty v jedné kotvě jde přeskládat **tažením**. Klávesová obdoba podle návrhu.
- **Zvýraznění** = klíčový údaj widgetu akcentovou barvou (T-076).
- **Jedna instance** každého typu widgetu, takže v režimu není „přidat widget“, jen přesun, velikost, zvýraznění a obnovení.
- **Souběh s oknem nastavení (T-077 Q3):** po „Hotovo“ si otevřené okno nastavení znovu načte jen část s rozložením dané tiskárny a ostatní neuložené změny si ponechá (implementace v T-092, tento úkol vyvolá oznámení přes zapisovač T-077).

## Rozhodnutí (uživatel, 2026-10-25) – vstup zvenku z okna nastavení (T-092)
- **Nahrazuje** dřívější pravidlo z 2026-10-24 (T-092), že tlačítko pro režim rozložení v nastavení **není**: oddíl „Všechny widgety“ na záložce Widgety (T-092) má tlačítko **„Upravit rozložení v okně kamery…“**. Vstup tlačítkem a kontextovou nabídkou v okně kamery (výše) zůstává.
- Režim rozložení proto jde spustit i **zvenku**: s tiskárnou a **volitelným počátečním rozložením** (rozpracované rozložení z okna nastavení).
- **Q4 – počáteční rozložení:** režim začne z předaného rozpracovaného rozložení (bez něj z uloženého). „Hotovo“ ho uloží přes zapisovač T-077 (tím se uloží i rozpracovaná viditelnost této tiskárny z nastavení) a vyvolá `LayoutSaved`. **Esc nic nezmění** – v souboru ani v okně kamery (okno dál ukazuje uložené rozložení).
- **Q5 – malé okno:** pokud je normální okno kamery menší než 480 × 300 (pod tím je režim zakázaný, T-076 Q4), na dobu režimu se **dočasně zvětší na 640 × 360** na stejném místě. Po „Hotovo“ i Esc se vrátí na původní velikost; dočasná velikost se nezapamatuje (`camera-windows.json`).
- Okno kamery dané tiskárny se při vstupu zvenku otevře, nebo aktivuje a přepne do normálního okna (z malého náhledu i z celé obrazovky), viz Návrh UI 5.4 v T-092.

## Kontext
- Model, čisté operace (přesun do kotvy a na pozici, velikost, zvýraznění, obnovení) a zapisovač sekce `overlay` jedné tiskárny s oznámením o zápisu dodá T-077.
- Framework a kotvy dodá T-081. Vzhled, klávesové zkratky a `AutomationId` určí návrh T-076.
- Vstup zvenku volá okno nastavení (T-092, Návrh UI 5.4). T-092 zjišťuje dostupnost tlačítka sám (vybraná, uložená tiskárna se zapnutou kamerou).

## Rozsah
- `CameraLayoutModeViewModel`:
  - pracovní kopie `CameraOverlayLayout`, vybraný widget, cílová kotva a pozice,
  - příkazy přesunu (kotva + pozice v kotvě), velikosti, zvýraznění, obnovení výchozích, Hotovo a Zrušit (Esc),
  - logika bez Avalonie, testovatelná v App.Tests. View jen předává gesta.
- **Vstup:**
  - nové tlačítko v `CameraWindowButtons`, jen v normálním okně, s tooltipem a `AutomationProperties.Name`, ikona z Fluent UI System Icons,
  - kontextová nabídka obrazu s položkou „Upravit rozložení“,
  - `AutomationId` podle návrhu, například `CameraLayoutButton`, `CameraLayoutMenuItem`,
  - přechod do malého náhledu nebo na celou obrazovku během režimu nejdřív režim zruší (jako Esc).
- **Vstup zvenku** (rozhodnutí 2026-10-25): rozhraní v App, např. `ICameraLayoutModeLauncher.Start(string printerId, CameraOverlayLayout? startingLayout)` (název navrhne ui-dev), registrované explicitně v composition rootu:
  - otevře okno kamery tiskárny, nebo ho aktivuje a přepne do normálního okna,
  - okno menší než 480 × 300 dočasně zvětší na 640 × 360 na stejném místě a po ukončení režimu (Hotovo, Esc, přepnutí režimu okna) vrátí původní velikost; dočasná velikost se neuloží do `camera-windows.json`,
  - spustí režim s pracovní kopií z `startingLayout` (nebo z uloženého rozložení, když je `null`),
  - běží-li v okně už režim rozložení, nespustí druhý (chování viz Otevřené otázky).
- **Překryv režimu:**
  - ztlumení obrazu a 6 čárkovaných zón (`AutomationId` `CameraLayoutAnchor{Kotva}`),
  - zvýraznění cíle během tažení včetně ukazatele pozice v kotvě, přichycení,
  - tažení přes `PointerPressed` / `PointerMoved` / `PointerReleased` v rámci okna, ne přes systémové drag & drop.
- **Popover** (`Flyout` / `Popup`) se zvýrazněním a S/M/L (`AutomationId` `CameraLayoutPopover`, `CameraLayoutEmphasize`, `CameraLayoutSize{S|M|L}`). Velikosti, které widget nepodporuje, jsou zakázané.
- **Lišta:** `CameraLayoutReset`, `CameraLayoutDone`, případně `CameraLayoutCancel` podle návrhu.
- **Klávesnice:**
  - výběr widgetu (Tab / šipky),
  - přesun do sousední kotvy a změna pořadí v kotvě (zkratky podle návrhu),
  - otevření popoveru (Enter / mezerník / F2 podle návrhu),
  - Esc = zavřít popover, druhé Esc = zrušit režim, Enter na „Hotovo“ = uložit,
  - fokus je vždy vidět,
  - změny se oznamují čtečce obrazovky, například „Stav přesunut: vpravo nahoře, 2. pozice“.
- Uložení přes zapisovač z T-077 a okamžité použití v otevřeném okně. Oznámení o zápisu dostane okno nastavení (T-092).
- Nové texty ve všech 8 jazycích.

## Akceptační kritéria
- [x] App.Tests:
  - přesun do každé kotvy a na každou pozici v kotvě,
  - velikost (jen podporované), zvýraznění, obnovení výchozích,
  - Esc / Zrušit zahodí změny, Hotovo uloží jednou a vyvolá oznámení,
  - chyba zápisu se ukáže a nic se neztratí,
  - přepnutí režimu okna během úprav ji zruší,
  - vstup zvenku: s počátečním rozložením i bez něj, Hotovo uloží počáteční rozložení se změnami a vyvolá `LayoutSaved`, Esc nezmění soubor ani rozložení v okně,
  - dočasné zvětšení: okno pod 480 × 300 se zvětší na 640 × 360 a po Hotovo, Esc i přepnutí režimu okna se vrátí; okno od 480 × 300 výš se nemění; dočasná velikost se nezapamatuje.
- [x] Headless UI testy (`LayerPeek.App.UiTests`):
  - vstup tlačítkem i kontextovou nabídkou jen v normálním okně (v malém náhledu a na celé obrazovce nejsou),
  - simulované tažení myší do každé kotvy a změna pořadí v kotvě se zvýrazněním cíle,
  - popover,
  - **celé ovládání jen klávesnicí**: výběr, přesun mezi všemi kotvami, pořadí, velikost, zvýraznění, Hotovo, Esc,
  - všechny `AutomationId`, nejdelší jazyk, snímky reference,
  - vstup zvenku: zavřené okno se otevře, malý náhled i celá obrazovka se přepnou do normálního okna, malé okno se dočasně zvětší a vrátí.
- [x] Po „Hotovo“ je rozložení v `settings.json`, celá obrazovka ho používá také a po restartu (novém otevření okna) je stejné
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Rozhodnutí ke vstupu zvenku (uživatel, 2026-10-25)
Uživatel přijal návrhy architekta:
- **Q1 – dočasně zvětšené okno se nevejde na pracovní plochu:** okno 640 × 360 se na dobu režimu posune dovnitř pracovní plochy a po ukončení se vrátí původní poloha i velikost.
- **Q2 – návrat po vstupu z malého náhledu nebo celé obrazovky:** po „Hotovo“ / Esc okno zůstane v normálním režimu.
- **Q3 – vstup zvenku, když okno už je v režimu rozložení:** okno se jen aktivuje a rozpracovaná úprava pokračuje.

## Otevřené otázky
- Žádné.

## Poznámky k implementaci
- 2026-10-25, ui-dev. Kód v `App/Cameras/Layout/`, napojení v `CameraViewModel.LayoutMode.cs`, `Windows/CameraWindow.LayoutMode.cs` a `Windows/CameraWindows.cs`; popis v `docs/architecture.md` (oddíl Režim rozložení).
- **View model (bez Avalonie):** `CameraLayoutModeViewModel` (`CameraViewModel.LayoutMode`) drží pracovní kopii, karty (`CameraLayoutCardViewModel`) a 6 zón (`CameraLayoutZoneViewModel`).
  - Pozice v zóně se počítají tak, jak je vidí uživatel (shora dolů; dolní kotvy rostou od okraje nahoru). Přesuny jdou přes čisté operace T-077 (`MoveTo`, `WithSize`, `WithEmphasized`, `ResetToDefault`).
  - Karty má jen widget, který je v pracovní kopii viditelný a registrovaný (po sloučení T-084 a T-085 Stav, Časy, Teploty a Název tisku; Vychladnutí přibude s T-088 samo). Skryté a neregistrované widgety kotvy zůstávají v modelu na svém místě vůči kartám (karta puštěná za poslední kartu jde hned za ni).
  - Klávesnice: mezerník zvedne kartu, šipky / Home / End ji posouvají v pracovní kopii hned (←/→ do sousední kotvy řádku na stejnou pozici od okraje nebo na konec, ↑/↓ o pozici a za koncem do druhé kotvy sloupce), mezerník / Enter pustí, Esc vrátí kartu na místo zvednutí. Šipky na nezvednuté kartě přesunou fokus na nejbližší kartu daným směrem (čistá funkce `CameraLayoutGeometry.Nearest`).
  - Myš: tažení začne po posunu > 4 DIP, cíl = zóna pod ukazatelem, jinak nejbližší; nad lištou žádný cíl (karta se vrátí). Pracovní kopie se mění až puštěním. Esc během tažení zruší jen tažení.
  - „Hotovo“ zapíše jednou (`IsSaving` blokuje další stisk i Esc), chyba zápisu (i `false` zapisovače nebo chybějící zapisovač) zobrazí `CameraLayoutSaveError` a režim i pracovní kopie zůstanou. Po zápisu okno hned použije nové rozložení.
  - Oznámení pro čtečku (`Announcement`) podle návrhu 5.5; stejný text dvakrát po sobě dostane mezeru navíc, aby ho živá oblast oznámila znovu.
- **Překryv během režimu:** `CameraOverlayViewModel.BeginPreview/Preview/EndPreview` – widgety ukazují pracovní kopii, karty překryvu jsou skryté (`AreCardsVisible`), uložené nastavení mezitím mění jen `SavedLayout`.
- **Robustnost:**
  - zavření okna, odebrání tiskárny nebo vypnutí kamery (okno se zavře) = úprava se zahodí; rozběhnutý zápis doběhne;
  - nastavení uložené jinde během úpravy: pracovní kopie si nechá své změny a převezme jen změněnou viditelnost (`WithSavedVisibility`), Esc pak vrátí nově uložené rozložení; zvednutá / vybraná karta skrytého widgetu se pustí;
  - přepnutí do malého náhledu nebo na celou obrazovku (tlačítka, F11, OS) režim ukončí ještě před přepnutím (okno tak dostane zpět svou velikost); během zápisu se režim ukončí také a zápis doběhne.
- **Zápis a převzetí:** `ICameraOverlayLayoutWriter` je v composition rootu `DeferredCameraOverlayLayoutWriter` (úložiště vznikne při prvním zápisu; bez datové složky skončí zápis chybou v liště). `MonitoringSession` odebírá `LayoutSaved` a vezme rozložení do běžícího nastavení bez restartu (`UpdateSettings` pozorovatelům), takže ho má i znovu otevřené okno. Okno nastavení (T-092) dostane stejné oznámení.
- **Vstup zvenku (T-092):** `ICameraLayoutModeLauncher.Start(string printerId, CameraOverlayLayout? startingLayout)` implementuje `CameraWindows` (instance v `App`, `_cameraWindows`; T-092 ji předá oknu nastavení stejně jako dnes `ICameraWindowPlacer`). Zavřené okno se otevře rovnou v normálním okně a v režimu, otevřené se přepne do normálního okna a aktivuje, běžící úprava pokračuje beze změny (Q3).
  - Dočasné zvětšení (Q5, Q1): okno pod 480 × 300 dostane aspoň 640 × 360 (rozměr, který je už větší, zůstane – okno se nikdy nezmenší) na stejném místě, posunuté jen tolik, aby viditelné okno zůstalo v pracovní ploše (`CameraLayoutSpace.Enlarged`). Po Hotovo / Esc / přepnutí režimu se vrátí poloha i velikost; okno si mezitím pamatuje (a do `camera-windows.json` zapíše) jen původní umístění.
- **Vzhled podle T-076 5:** ztlumení 50 % s náběhem 150 ms, zóny (čárkovaně, cíl plně akcentem), „Pustit sem“ jen při tažení myší (na volném konci zóny, zóna roste dovnitř, karty se nehnou; v prázdné zóně na střed), ukazatel pozice jako linka 3 v mezeře, zvednutá karta 1,03 se stínem, obrys na původním místě, výběr akcentem, dvojitý rámeček fokusu jen z klávesnice. Lišta má tmavý motiv (je vždy nad videem), pořadí tlačítek Windows `[Obnovit výchozí] … [Hotovo] [Zrušit]`, macOS `[Zrušit] [Hotovo]`; při nedostatku místa jdou tlačítka na druhý řádek (`CameraLayoutToolbarPanel`). Popover je Fluent `Flyout` v motivu aplikace, velikosti jako jedna zarážka Tab (šipky mezi S/M/L).
- **Odchylky / doplnění návrhu (implementační detaily):**
  - šířka zón je v úzkém okně omezená na třetinu místa mezi okraji (vzorec `clamp(…, 160, 260)` by pod 528 DIP zóny překryl);
  - zvednutí klávesnicí ukazuje cílovou zónu a stín; samostatnou linku pozice nekreslí, protože karta už na nové pozici je;
  - karty v režimu mají velikost z pracovní kopie i v okně, kde překryv kreslí o stupeň menší (návrh 5.2 „skutečná velikost z pracovní kopie“);
  - kontextová nabídka je na celém okně (Menu / Shift+F10 funguje i bez fokusu); otevře se jen v normálním okně mimo režim.
- **Přístupnost:** karta je pro UI Automation tlačítko se vzorem Expand/Collapse (popover), jméno „Stav: Vlevo nahoře, pozice 1 z 1“, nápověda s klávesami; zóny, kořen režimu a lišta jsou v obsahovém stromu UIA (`AccessibilityView=Content`). Živá oblast `CameraLayoutAnnouncement` (`LiveSetting=Polite`) zůstává i po skončení režimu. Avalonia 12.1.4 při změně jména prvku s `LiveSetting` vyvolá na Windows `LiveRegionChanged` (ověřeno v `Avalonia.Win32.Automation`); skutečné čtení čtečkou (Narrator/NVDA) ručně neověřeno.
- **Texty:** 27 nových klíčů `CameraLayout*` ve všech 8 jazycích (cs vykání), „Zrušit“ = `SettingsCancel`, rohy = `SettingsCameraWindowCorner*`.
- **Testy:**
  - App.Tests: `Cameras/Layout/CameraLayoutModeViewModelTests` (přesun tažením do každé kotvy a na každou pozici, pořadí, skryté widgety, klávesnice přes všechny kotvy, Home/End/Esc, popover, jen podporované velikosti, obnovení, Zrušit/Esc, Hotovo jednou + oznámení, chyby zápisu, přepnutí režimu i během zápisu, zavření okna, nastavení uložené jinde, vstup zvenku s počátečním rozložením i bez něj, z malého náhledu a celé obrazovky, opakovaný vstup, jazyk), `CameraLayoutSpaceTests`, `CameraLayoutGeometryTests`, `DeferredCameraOverlayLayoutWriterTests`, `Tray/MonitoringSessionLayoutTests`.
  - UI testy: `Cameras/CameraWindowLayoutModeUiTests` (tlačítko a kontextová nabídka jen v normálním okně a zakázané pod 480 × 300, zóny a všechna `AutomationId`, tažení myší do každé kotvy se zvýrazněním cíle a ukazatelem, pořadí v kotvě, puštění nad lištou, popover myší, Expand/Collapse, celé ovládání klávesnicí včetně uložení, Esc, Zrušit, chyba zápisu, živá oblast, přepnutí režimu, celá obrazovka a nové otevření okna s uloženým rozložením, vstup zvenku včetně dočasného zvětšení a vrácení po Hotovo / Esc / F11 / malém náhledu a nezapamatování dočasné velikosti, 8 jazyků v nejmenším okně) a snímky `camera-layout-mode.png`, `camera-layout-mode-de-480.png` (vizuálně zkontrolované). `CameraWindowMiniFullScreenUiTests` počítá s třetím tlačítkem normálního okna.
  - E2E (`LayerPeek.App.E2E.Tests/CameraLayoutModeTests`, jen v izolovaném testovacím režimu přes `LAYERPEEK_E2E=1`): skutečná aplikace s falešnou RTSP kamerou, tlačítko přes UI Automation, tažení karty Stav skutečnou myší do kotvy vpravo dole, „Hotovo“ a kontrola `settings.json`. Lokálně prošel (Windows 11, 100 %).
- **Ověřeno:** Windows 11 (headless UI testy, E2E se skutečnou aplikací v testovacím režimu, snímky obrazovky průběhu tažení a výsledku). **Neověřeno:** macOS (pořadí tlačítek `[Zrušit][Hotovo]`, Ctrl+klik, Fn+F2), skutečná čtečka obrazovky, DPI 125–200 % a Native AOT publish (bez nového balíčku a reflexe; AOT/trim analyzátory v buildu čisté).

## Review
- 2026-10-25, qa-reviewer (po sloučení `develop` s T-084, T-085, T-087 a T-089):
  - Konflikty vyřešeny: `CameraViewModel` má parametry `widgets, history, cooldown, thumbnails, layoutWriter` (v tomto pořadí, upravené `AppServices` a `CameraHarness`); okno si v malém náhledu pamatuje obraz bez lišty (T-089), v normálním okně dál umístění před dočasným zvětšením (T-091 Q5); překryv režimu je v řádku 0 mřížky `Root` (oblast obrazu).
  - Opraveno: zápis „Hotovo“, který doběhl až poté, co uživatel režim opustil (přepnutí na celou obrazovku / malý náhled) a znovu spustil, ukončil novou úpravu jako uloženou, nebo do ní promítl starou chybu zápisu. Režim teď čísluje úpravy a výsledek starého zápisu novou úpravu nechá být (test `ASaveFinishingAfterTheModeWasLeftAndStartedAgainDoesNotEndTheNewEdit`).
  - Doplněno: UI test režimu s produkční registrací widgetů (`ProductionWidgetsTemperaturesAndPrintNameAreCardsThatMoveAndSave`) – karty Teploty a Název tisku, přesun klávesnicí i myší a uložení.
  - Známé omezení (řeší T-092): okno nastavení otevřené během „Hotovo“ zatím rozložení nepřevezme a jeho pozdější „Uložit“ by uložené rozložení přepsalo svou starší kopií.
