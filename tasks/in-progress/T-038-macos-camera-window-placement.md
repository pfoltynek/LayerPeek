---
id: T-038
title: macOS – ověření umístění okna kamery, monitoru a „vždy navrchu“
status: in-progress
owner: ui-dev
priority: P3
platform: macos
depends_on: [T-026, T-035, T-037]
---

## Cíl
Ověřit a případně doladit na macOS chování z T-035 a T-037: výběr monitoru, rohy, celá obrazovka na zvoleném monitoru a `Topmost`.

## Rozsah
- Co vrací `Screen.DisplayName` na macOS a jestli je stabilní mezi spuštěními (identifikace monitoru z T-035).
- Pracovní plocha s menu barem a Dockem, rohy okna, okno bez rámečku (malý náhled).
- Celá obrazovka na zvoleném monitoru (macOS přesouvá okno do vlastního Space).
- `Topmost` nad jinými aplikacemi a nad aplikacemi na celé obrazovce.
- Nastavení bez posuvníku (T-033) na MacBooku 13" (1440×900 / 1470×956 bodů).

## Akceptační kritéria
- [ ] Ruční ověření na macOS s jedním a se dvěma monitory, výsledky v poznámkách
- [x] Případné opravy pokryté testy (App.Tests / App.UiTests) – identifikace monitoru UUID displeje (`AvaloniaScreenCatalogTests`, `MacDisplayUuidTests`), změna velikosti malého náhledu na macOS (`CameraMiniGeometryTests`, `CameraWindowMiniOwnResizeUiTests`), velikost nastavení na MacBooku 13" (`AuxiliaryWindowPlacementTests`)
- [x] `dotnet build` a `dotnet test` na macOS bez chyb a varování

## Otevřené otázky
1. **Malý náhled „vždy navrchu“ nad aplikací na celé obrazovce a na všech plochách (Spaces).** Avalonia 12.1.4 dává
   oknu s `Topmost` úroveň `NSFloatingWindowLevel` a běžnému oknu `collectionBehavior` jen
   `NSWindowCollectionBehaviorFullScreenPrimary` (zdroje `WindowBaseImpl.mm`, `WindowImpl.mm`). Okno proto zůstane nad
   okny všech aplikací **na stejné ploše**, ale nad jinou aplikací na celé obrazovce (ta má vlastní Space) ani na jiné
   ploše vidět nebude (z kódu, ručně neověřeno – krok J4). Na Windows je okno `HWND_TOPMOST` nad vším na jedné ploše.
   Varianty:
   - (a) nechat, jak to dělá Avalonia (náhled zůstává na své ploše);
   - (b) **doporučeno:** malému náhledu se zapnutým „vždy navrchu“ nastavit nativně
     `NSWindowCollectionBehaviorCanJoinAllSpaces | NSWindowCollectionBehaviorFullScreenAuxiliary` (přes existující
     interop `ObjCRuntime`, `[LibraryImport]`, bez nového balíčku), takže bude jako obraz v obraze vidět na každé ploše
     i nad aplikací na celé obrazovce; normální okno a celá obrazovka beze změny;
   - (c) totéž i pro normální okno se zapnutým „vždy navrchu“.
   Rozhoduje uživatel (chování UI). Doporučuji nejdřív projít krok J4, ať je jasné, co dnes macOS skutečně dělá.
2. **Přepnutí z celé obrazovky rovnou do malého náhledu na macOS** (tlačítko malého náhledu v celé obrazovce, Esc
   u celé obrazovky otevřené z náhledu, „Použít“ v nastavení během celé obrazovky). Avalonia odchod z celé obrazovky
   spustí `toggleFullScreen:` (animace, asynchronní) a hned vrací stav `Normal`; aplikace pak okamžitě odebere rámeček
   a nastaví polohu a velikost náhledu. AppKit na konci animace obnovuje rámec okna z doby před celou obrazovkou, takže
   náhled může skončit ve velikosti normálního okna nebo na jiném místě (z kódu, neověřeno). Oprava bez ručního
   ověření by byla naslepo (okamžik konce animace Avalonia nehlásí), proto jen krok J5. Když se chyba potvrdí: smím
   na macOS po odchodu z celé obrazovky umístění náhledu zopakovat až po skončení animace (např. po první změně
   velikosti okna, nejpozději po 1 s)? Jde o technickou opravu bez změny chování, ptám se jen kvůli zvolenému způsobu.
3. **Písmo v nastavení na macOS.** Testy rozložení (bez posuvníku, nic uříznuté) běží headless s přibaleným písmem
   Inter; skutečná aplikace na macOS kreslí systémovým písmem (San Francisco), které má jiné šířky. Na MacBooku 13"
   okno nastavení zůstává 780 × 532 (test), takže rozhoduje jen šířka textů – krok J7. Pokud se tam něco uřízne:
   přizpůsobit texty / rozložení (ui-designer), nebo aplikaci dodávat Inter i na macOS (rozhodnutí produktu)?

## Poznámky k implementaci

### Zjištění ze zdrojů Avalonie 12.1.4 (tag `12.1.4`, `native/Avalonia.Native/src/OSX`, `src/Avalonia.Native`)
- **`Screen.DisplayName`** = `NSScreen.localizedName` (`Screens.mm`, od macOS 10.15). Je to název, který ukazuje
  Nastavení systému → Displeje, **v jazyce systému** (vestavěný displej např. „Vestavěný displej Retina“ /
  „Built-in Retina Display“; externí monitor podle EDID, např. „DELL U2723QE“). Dvěma stejným monitorům macOS přidá
  „(1)“, „(2)“ a podle zkušeností jiných projektů (mpv, commit `f7a32b5`) **nemusí čísla přiřadit stejně** po dalším
  startu nebo připojení. Jen podle názvu (stav po T-035 mimo Windows) by se tak okno mohlo otevřít na druhém ze dvou
  stejných monitorů a vestavěný displej by se po změně jazyka systému jevil jako „nepřipojený“.
- **Platformní handle obrazovky** je `CGDirectDisplayID` (`AvnScreen`, deskriptor `"CGDirectDisplayID"`). Číslo samo se
  mezi starty mění (nelze ho ukládat), ale ColorSync k němu vrací **trvalé UUID displeje**
  (`CGDisplayCreateUUIDFromDisplayID`), stejné po restartu i novém připojení; pro dva stejné monitory různé (podle
  sériového čísla / připojení, macOS je u monitorů bez sériového čísla může prohodit – ověří krok D5).
- **Souřadnice:** Avalonia.Native hlásí `Screen.Scaling` = 1 a `DesktopScaling` = 1, `Bounds`, `WorkingArea` a
  `Window.Position` jsou v **bodech** (ne v pixelech Retiny). Celé umístění z T-035 tedy na macOS počítá v bodech:
  mezera 16 DIP = 16 bodů, uložené „rozlišení“ monitoru je velikost v bodech (např. 1512 × 982), změna „Vypadá jako“
  v Nastavení systému ho změní (shoda pak jde přes UUID / název). Kód nic nepřepočítává podle `RenderScaling`, takže je
  to konzistentní.
- **Pracovní plocha** = `NSScreen.visibleFrame`: bez menu baru (i pod výřezem displeje) a bez Docku (dole i po
  stranách; skrytý Dock jen tenký pruh). Rohy okna kamery z T-035 počítají s pracovní plochou, takže okno pod menu bar
  ani pod Dock nezajede (z kódu; krok J2).
- **Okno bez rámečku** (malý náhled, `WindowDecorations.None`) = `NSWindowStyleMaskBorderless` **bez**
  `NSWindowStyleMaskResizable`. `BeginResizeDrag` je v Avalonia.Native prázdná metoda (`WindowImplBase.cs`,
  `WindowBaseImpl::BeginResizeDrag`) a `BeginMoveDrag` = `performWindowDragWithEvent:` funguje. **Malý náhled tedy na
  macOS nešel zvětšit ani zmenšit** úchyty (kritérium T-026 „jde táhnout a měnit velikost“ to předpokládalo) – opraveno, viz níže.
- **`Topmost`** = `NSFloatingWindowLevel` (`WindowBaseImpl::GetBaseWindowLevel`), `collectionBehavior` běžného okna
  `FullScreenPrimary`; `CanJoinAllSpaces | FullScreenAuxiliary` dostávají jen dialogy a vlastněná okna. Viz otázka 1.
- **`WindowState.FullScreen`** = nativní celá obrazovka `toggleFullScreen:` (animace, vlastní Space) na monitoru, na
  kterém okno právě je (`[NSWindow screen]`). Aplikace okno nejdřív umístí jako normální na zvolený monitor
  (`PlaceBeforeShowing` / „Použít“) a teprve pak přepne, takže celá obrazovka vznikne na zvoleném monitoru (z kódu;
  krok D1). Při zobrazení okna Avalonia dočasně odebere `FullScreenPrimary` (kvůli jiné aplikaci na celé obrazovce)
  a pak vrátí. Odchod je také animovaný – viz otázka 2. Zelené tlačítko a menu „Okno → Ukončit režim celé obrazovky“
  změní `WindowState`, okno kamery se podle něj přepne (stávající `OnWindowFullScreenChanged`).
- **Titulek normálního okna:** předpoklad 0 × 32 DIP (`CameraWindowPlanner.AssumedNormalChrome`), skutečný se změří po
  zobrazení a okno se nejvýš jednou posune (`CorrectFrame`). Na macOS 11–15 bývá titulek 28 bodů, na macOS 26+
  neověřeno; případný posun po otevření o pár bodů zachytí krok J2.

### Opravy (2026-10-29, ui-dev)
- **Identifikace monitoru na macOS (v rámci návrhu T-035 Q8/Q11, formát nastavení beze změny):**
  `Platform/MacOS/MacDisplayUuid.cs` (`[LibraryImport]` do ColorSync a CoreFoundation s absolutní cestou, blittable,
  AOT safe; `CGDisplayCreateUUIDFromDisplayID` → `CFUUIDCreateString` → `CFStringGetCString`, vše uvolněno). UUID
  (velká písmena, `XXXXXXXX-XXXX-…`) je na macOS **cesta zařízení** monitoru (`CameraScreen.DevicePath`), tedy stejné
  pole jako `monitorDevicePath` na Windows: ukládá se do existujícího nepovinného
  `printers[*].camera.window.monitor.devicePath` a pravidla shody z T-035/T-071 (cesta rozhoduje, dvojčata,
  `Relocate`, zápis na pozadí `CameraMonitorPathUpdater`) platí beze změny. Starší nastavení z macOS jen s názvem
  dál funguje: monitor se najde podle názvu a cesta se na pozadí doplní, když je shoda jednoznačná (stávající
  chování). `kCGNullDirectDisplay` (0) dává ColorSync UUID hlavního displeje – proto se odmítá. Šev pro testy:
  `AvaloniaScreenCatalog(topLevel, macDisplayPath)`, čistá funkce `MacDevicePathOf(handle, …)`. Když ColorSync chybí,
  zůstává jen název (jako dřív). Pojistka `WithoutSharedDevicePaths`: cestu, kterou má víc obrazovek najednou
  (porovnání bez ohledu na velikost písmen), katalog všem odebere – takové monitory se pak rozliší jen názvem
  („(1)“, „(2)“), jinak by se uložený monitor vždy vyhodnotil na první z nich (platí na všech OS; na Windows cesty
  kolidovat nemohou). UI (seznam monitorů v nastavení, texty) beze změny – stejné monitory měly na macOS různé názvy
  už dřív.
- **Změna velikosti malého náhledu na macOS:** `CameraWindow.ResizesItself` (výchozí jen na macOS) – úchyt zachytí
  ukazatel, tažené hrany sledují ukazatel v souřadnicích obrazovky (`CameraMiniGeometry.Resized`: protější hrany
  stojí, minimum `MinWidth` × `MinHeight`), po uvolnění (nebo ztrátě zachycení) se výška dopočítá z šířky a poměru
  stran (`CorrectMiniSize`, jako na Windows po modální smyčce). Během tažení se výška neopravuje. Windows beze změny
  (`BeginResizeDrag` se systémovou smyčkou), jiné platformy dál `BeginResizeDrag` + časovač ustálení.
- Dokumentace: `docs/architecture.md` (řádek „Okno kamery – monitor, vždy navrchu, celá obrazovka“ v tabulce
  platformních rozdílů), komentáře `CameraScreen.DevicePath`, `CameraMonitorId.DevicePath`,
  `PrinterEditorViewModel.CreateMonitorOptions`, `AvaloniaScreenCatalog`.
- Licence / AOT: žádný nový balíček; ColorSync a CoreFoundation jsou součást macOS, interop jen `[LibraryImport]`.

### Testy
- `App.Tests/Cameras/AvaloniaScreenCatalogTests` (14): handle `CGDirectDisplayID` → cesta displeje, celý rozsah
  `uint`, jiný deskriptor / 0 / záporné / větší než `uint` / bez handle → žádná cesta; sdílená cesta odebrána všem
  (i při jiné velikosti písmen), jedinečné cesty a obrazovky bez cest beze změny; scénáře macOS přes pravidla Core:
  prohozené číslování „(1)“/„(2)“ → stejný fyzický monitor, vypnutý zvolený monitor se nenahradí dvojčetem
  („nepřipojený“), vestavěný displej po změně jazyka systému nalezen.
- `App.Tests/Platform/MacOS/MacDisplayUuidTests` (13): normalizace textu UUID; skutečný ColorSync (jen macOS, jen čte):
  displej 0 a neznámé displeje bez UUID, hlavní displej má pokaždé stejné UUID (přeskočí se bez spojení s window
  serverem; na tomto Macu prošel).
- `App.Tests/Cameras/CameraMiniGeometryTests` (+14): všech 8 hran, pohyb podél hrany, minimum (i z levého horního rohu),
  přepočet při 150 %, žádný pohyb.
- `App.UiTests/Cameras/CameraWindowMiniOwnResizeUiTests` (6, headless, `ResizesItself` zapnuté na každém OS): zapnuto
  právě na macOS; pravý dolní úchyt sleduje ukazatel a výška se dopočítá po uvolnění, okno zůstane na pracovní
  ploše; levý úchyt ve třech krocích drží pravou hranu; minimum šířky; klik bez pohybu nic nemění; bez
  `ResizesItself` úchyt nechává změnu na systému. Stávající testy malého náhledu (`CameraWindowMiniStripUiTests`,
  `…TogglesUiTests`, `…FullScreenUiTests`) na macOS teď jdou novou cestou a prošly beze změny.
- `App.Tests/Windows/AuxiliaryWindowPlacementTests.DefaultSizeIsKeptOnMacBook13InchWorkAreas` (7): pracovní plochy
  1440 × 900 a 1470 × 956 bodů (menu bar 24 / 37 bodů, Dock výchozí / největší / skrytý / vlevo) a 1280 × 832 („Větší
  text“): okno nastavení zůstane 780 × 532 celé na pracovní ploše, takže platí testy „bez posuvníku“ z T-033
  (`SettingsWindowLayoutTests`, 780 × 532, 8 jazyků; s výhradou písma, otázka 3).

### Ověření na tomto Macu (macOS 27.2, Apple Silicon, 2026-10-29)
- `dotnet build --no-incremental` (Debug) a `dotnet build -c Release --no-incremental`: 0 varování, 0 chyb.
- `dotnet test` (celé řešení, dva běhy): **7544 testů, 0 selhání, 7364 úspěšných, 180 přeskočených** (dosavadní
  přeskočené: Windows-only, E2E bez `LAYERPEEK_E2E`); UI testy 991 / 0 / 0, nové testy T-038 bez přeskočení
  (i skutečný ColorSync na hlavním displeji).
- `dotnet publish src/LayerPeek.App -c Release -r osx-arm64 -warnaserror` (do scratchpadu, smazáno): úspěch, bez
  varování trim/AOT; jen obvyklé hlášky clang/dsymutil o cache modulů. Exe se nespouštělo.
- `tools/profile-snapshot.ps1 -Save` / `-Compare` kolem obou běhů testů: „No change in the watched places of the user
  profile.“ (Login Items „not checked“ jako vždy).
- Aplikace se nespouštěla (ani `dotnet run`), nic se neměnilo v systému; ColorSync se jen četl (UUID hlavního
  displeje). **Neověřeno lokálně:** vše, co vyžaduje skutečné okno AppKitu – rohy a pracovní plocha, okno bez rámečku
  a jeho změna velikosti na skutečném macOS (headless jen logiku), `Topmost` nad jinými aplikacemi, celá obrazovka
  a odchod z ní, dva monitory, stabilita UUID mezi restarty a u stejných monitorů.

### Ruční ověření (uživatel)
Souhrnně v [`docs/macos-manual-testing.md`](../../docs/macos-manual-testing.md), sezení 6 (krok J7 i v sezení 3).

Předpoklad: `LayerPeek.app` z `.dmg` v Aplikacích (sezení 1), tiskárna s kamerou (Buddy3D v LAN, nebo simulátor
`dotnet run --project tools/PrusaSimulator` s tiskárnou `http://127.0.0.1:<port>` a kamerou z `docs/simulator.md`).
Výsledky zapsat sem (pod „Výsledky ručního ověření“) s verzí macOS a monitory; při chybě přiložit výňatek
`~/Library/Logs/LayerPeek/LayerPeek.log`.

**Jeden monitor (vestavěný displej MacBooku):**
- **J1 – název a rozlišení.** Nastavení → tiskárna → Okno kamery → Monitor: zapsat přesný text položky „Monitor 1: …“.
  Očekávání: název jako v Nastavení systému → Displeje, rozlišení v bodech = „Vypadá jako“ (např. 1512 × 982).
  Ukončit a znovu spustit LayerPeek: stejný text.
- **J2 – rohy a pracovní plocha.** Pro normální okno i malý náhled postupně všechny 4 rohy a střed, vždy „Použít“:
  okno je v rohu 16 bodů od kraje pracovní plochy, nikdy pod menu barem ani pod Dockem. Zopakovat s Dockem vlevo
  a se skrytým Dockem (Nastavení systému → Plocha a Dock). Zapsat, jestli se normální okno po otevření o kousek
  posune (titulek jiný než předpokládaných 32 bodů).
- **J3 – malý náhled bez rámečku.** Bez rámečku a titulku; tažením obrazu se přesouvá; **všemi 8 hranami a rohy jde
  měnit velikost** (kurzor se mění, hrana sleduje ukazatel, protější hrana stojí), po uvolnění se výška přizpůsobí
  poměru stran obrazu; užší než minimum nejde. Dvojklik přepne režim.
- **J4 – vždy navrchu.** Normální okno s vypnutým / zapnutým „vždy navrchu“: kliknout do Finderu nebo Safari – okno
  zůstane vpředu jen se zapnutou volbou. Malý náhled (výchozí zapnuto) zůstane nad okny jiných aplikací. Pak Safari
  na celou obrazovku (⌃⌘F) a přepnout na jeho plochu: **zapsat, jestli je malý náhled vidět** (podle zdrojů ne –
  otázka 1). Totéž na jiné ploše (Mission Control → přidat plochu).
- **J5 – celá obrazovka.** F11, dvojklik i tlačítko: okno se animací přesune do vlastní plochy na tomtéž monitoru,
  menu bar skrytý, obraz přes celý displej (i pod výřezem – zapsat). Esc vrátí předchozí režim i polohu. Odchod zeleným
  tlačítkem nebo ⌃⌘F: okno kamery ukáže správná tlačítka (normální režim). **Z celé obrazovky rovnou tlačítko malého
  náhledu:** náhled má mít svou velikost a roh, bez rámečku (otázka 2 – zapsat, co se stalo). Totéž „Použít“
  v nastavení, když je okno na celé obrazovce. Počáteční režim „Celá obrazovka“: okno kamery z menu se otevře rovnou
  na celé obrazovce.
- **J6 – zapamatování.** S „Pamatovat poslední umístění“ okno přesunout, zavřít, ukončit LayerPeek, spustit: okno se
  otevře na stejném místě; po „Obnovit výchozí“ v nastavení zase podle výchozích hodnot.
- **J7 – nastavení na MacBooku 13".** Nastavení systému → Displeje → „Vypadá jako“ 1440 × 900 (MacBook Air 13"
  Intel/M1), resp. 1470 × 956 (M2 a novější); pokud displej tyto volby nemá, zapsat nejbližší. Otevřít Nastavení
  LayerPeeku: všechny záložky a podzáložky tiskárny bez posuvníku, nic uříznuté, okno celé na ploše. Aspoň čeština
  a jeden dlouhý jazyk (němčina nebo francouzština, Nastavení → Obecné → Jazyk). Pak „Vypadá jako“ vrátit.

**Dva monitory (vestavěný + externí; pro D5 dva stejné externí):**
- **D1 – zvolený monitor.** V seznamu „Monitor 1 / 2“ s názvy; zvolit externí, „Použít“: normální okno i malý náhled
  v nastaveném rohu externího monitoru; celá obrazovka vznikne na externím monitoru (ne na vestavěném).
- **D2 – uspořádání.** Externí monitor nad / vlevo od vestavěného (Nastavení systému → Displeje → Uspořádat): rohy
  dál sedí (záporné souřadnice).
- **D3 – samostatné plochy.** Nastavení systému → Plocha a Dock → „Displeje mají samostatné plochy“ zapnuto i vypnuto
  (vyžaduje odhlášení): celá obrazovka a rohy (menu bar na každém displeji).
- **D4 – odpojení a návrat.** Zvolený externí monitor odpojit: okno kamery se otevře na hlavním monitoru a nastavení
  ukáže „… – nepřipojený“. Připojit zpět: okno se otevře zase na něm. Restart Macu s připojeným monitorem: dál ten
  zvolený.
- **D5 – dva stejné monitory.** Názvy v seznamu s „(1)“ a „(2)“ (zapsat). Zvolit jeden, „Použít“, zapamatovat si
  fyzický monitor. Prohodit kabely (nebo porty dokovací stanice) a restartovat: okno na **stejném fyzickém** monitoru?
  Zapsat, jestli macOS čísla prohodil. Zvolený vypnout: okno na hlavním monitoru, „nepřipojený“ (ne na dvojčeti).
- **D6 – uložená cesta.** `grep -n '"devicePath"' ~/Library/Application\ Support/LayerPeek/settings.json` ukáže UUID
  zvoleného monitoru (soubor neobsahuje hesla, výpis jde poslat; i tak ho celý do chatu nevkládat).
- **D7 – různé hustoty.** Malý náhled přetáhnout z Retiny na externí monitor bez Retiny: velikost v bodech zůstane,
  obraz ostrý, úchyty fungují na obou.

### Výsledky ručního ověření
(doplní uživatel)
