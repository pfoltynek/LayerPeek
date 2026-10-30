---
id: T-102
title: macOS – ikona v Docku a ⌘Tab při otevřeném okně, „Zobrazit kameru“ v menu
status: done
owner: ui-dev
priority: P1
platform: macos
depends_on: [T-018, T-019, T-038]
---

## Cíl
Při testování na Macu (2026-10-29) uživatel zjistil, že okno kamery (normální i na celé obrazovce), Nastavení ani
Diagnostika nemají na macOS nikde svou stopu: LayerPeek je aplikace jen pro menu bar (`LSUIElement`, T-019), takže nemá
ikonu v Docku ani v přepínači ⌘Tab. Na Windows má každé okno tlačítko na hlavním panelu. Jakmile okno zapadne za jinou
aplikaci, nedá se snadno vrátit.

## Rozsah (rozhodnuto 2026-10-29)
1. **Ikona v Docku a ⌘Tab, dokud je otevřené „skutečné“ okno.** Normální okno kamery, okno kamery na celé obrazovce,
   Nastavení, Diagnostika, O aplikaci, Licence: LayerPeek se přepne na běžnou aplikaci (`NSApplication
   setActivationPolicy:` Regular). Po zavření posledního takového okna zpět na Accessory (jen menu bar). Klik na ikonu
   v Docku (reopen) přivede okna aplikace dopředu; menu ikony v Docku vypisuje okna (AppKit to dělá u oken s titulkem –
   okna mají rozumné názvy, např. „Alpha – kamera“). Start bez oken = Accessory jako dosud (`LSUIElement` zůstává),
   čekání na displej (T-099) = nic. Změna nesmí nečekaně brát fokus ani přeskládat okna; právě otevřené okno zůstane
   vpředu.
2. **Pravidlo „vždy navrchu“ (rozhodnuto 2026-10-29, nahrazuje „malý náhled se nikdy nepočítá“).** Okno, které je
   nastavené jako vždy navrchu (`Topmost`), nepotřebuje přítomnost v Docku / na hlavním panelu; každé jiné okno ano.
   Platí stejně na **macOS** (počítadlo oken pro Dock a ⌘Tab počítá jen otevřená okna, která nejsou navrchu, a přepočítá
   se při přepnutí režimu i při změně volby „vždy navrchu“) i na **Windows** (okno navrchu nemá tlačítko na hlavním
   panelu, ostatní ano). Na Windows se mění jen to, co se od pravidla liší.
3. **Malý náhled podle stejného pravidla (rozhodnuto 2026-10-29).** Malý náhled se řídí přesně jako normální okno jen
   tím, zda je právě navrchu: s vypnutou volbou „vždy navrchu“ má ikonu v Docku / tlačítko na hlavním panelu, se
   zapnutou ne. Na obou OS stejně. Volba pro malý náhled už existuje (`TopmostMini`, T-037, výchozí zapnuto), nové
   nastavení se nepřidává.
4. **„Zobrazit kameru“ v menu.** U tiskárny s otevřeným oknem kamery položka „Zobrazit kameru“ / „Show camera“, která
   přivede okno dopředu (aktivace aplikace + okna), vedle stávající položky pro zavření. Texty v 8 jazycích. Původně jen
   macOS; **rozhodnuto 2026-10-29: i na Windows** (stejné menu na obou OS, otázka 1).

## Akceptační kritéria
- [x] Počítadlo oken: otevření / zavření / přepnutí režimu / změna „vždy navrchu“, okna navrchu se nepočítají, po
      zavření posledního okna zpět na Accessory (unit testy s falešnou přítomností, headless UI testy se skutečnými okny)
- [x] Windows: tlačítko na hlavním panelu přesně u oken, která nejsou navrchu, i po přepnutí režimu a po uložení volby
      s otevřeným oknem (headless UI testy)
- [x] „Zobrazit kameru“ v menu na macOS i na Windows (`TrayMenuOptions.ForCurrentPlatform`); přístupové klávesy
      jedinečné v 8 jazycích (testy)
- [x] Implementace přes ObjC runtime (`[LibraryImport]`, AOT), za rozhraním `IAppPresence` (macOS / no-op)
- [x] Texty ve všech 8 jazycích
- [x] Ověřeno uživatelem na Macu: ikona v Docku a ⌘Tab se objeví / zmizí, menu Docku, klik na ikonu v Docku, fokus
      a pořadí oken (`docs/macos-manual-testing.md`, sezení 2, krok 7) → přesunuto do T-115
- [x] Ověřeno na Windows: tlačítka na hlavním panelu podle pravidla, „Zobrazit kameru“, blikání (oddíl „Ruční ověření
      na Windows“, W1–W3), jinak beze změny → přesunuto do T-114
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. ~~**„Zobrazit kameru“ i na Windows?** Na Windows má otevřené okno (pokud není navrchu) tlačítko na hlavním panelu,
   takže položka není nutná; menu by se ale na obou OS lišilo.~~ → **ano** (rozhodnuto 2026-10-29): stejné menu na obou
   OS, „Zobrazit kameru“ i na Windows (obnoví minimalizované okno, aktivuje ho, i okno navrchu).
2. ~~**Fokus po odchodu z Docku při zbývajícím okně navrchu.** Když se poslední počítané okno přepne do malého náhledu
   (nebo zavře, zatímco je náhled otevřený), aplikace se přepne na Accessory a znovu se aktivuje, pokud byla aktivní,
   aby fokus zůstal u náhledu.~~ → **ano, ponechat** (rozhodnuto 2026-10-29): malý náhled zůstane aktivní s fokusem.
3. **Menu bar po přepnutí na běžnou aplikaci.** Známá vlastnost AppKitu: po `setActivationPolicy:` Regular u už aktivní
   aplikace se menu aplikace v menu baru někdy neobnoví, dokud se nepřepne na jinou aplikaci a zpět (Avalonia to při
   startu obchází aktivací Docku). Neimplementováno žádné obejití; zůstává otevřené, dokud ruční test neukáže, že se to
   děje (rozhodnuto 2026-10-29).
4. ~~**Ikona v Docku ve vývojovém sestavení** (`dotnet run`, bez bundlu) bude nejspíš obecná ikona spustitelného souboru,
   ne ikona LayerPeeku.~~ → **stačí** (rozhodnuto 2026-10-29), ikona se za běhu nenastavuje.
5. ~~**Windows: blikání při změně „vždy navrchu“ s otevřeným oknem.** Avalonia na Windows skrývá okno z hlavního panelu
   skrytým vlastníkem a okno při tom jednou skryje a znovu ukáže; nově i při uložení volby „vždy navrchu“ u otevřeného
   okna a při přepnutí normální okno navrchu ↔ celá obrazovka.~~ → **přijatelné** (rozhodnuto 2026-10-29), uživatel to
   ověří na Windows (krok W3 níže).

## Ruční ověření na Windows
Výchozí volby okna kamery (normální okno ne navrchu, malý náhled navrchu), tiskárna s kamerou (simulátor nebo Buddy3D).
- **W1 – tlačítko na hlavním panelu:** normální okno a celá obrazovka mají tlačítko, malý náhled ne (jako dřív).
  V Nastavení → Okno kamery zapnout „Vždy navrchu“ u normálního okna a Použít s otevřeným oknem → tlačítko zmizí;
  vypnout → vrátí se. U malého náhledu „Vždy navrchu“ vypnout → náhled má tlačítko. Nastavení, Diagnostika, O aplikaci
  mají tlačítko vždy.
- **W2 – „Zobrazit kameru“:** menu v oznamovací oblasti → tiskárna s otevřeným oknem: „Otevřít PrusaLink“, „Zobrazit
  kameru“, „Skrýt kameru“ (přístupové klávesy: písmeno samotné spustí položku, žádné dvě stejné). Okno za jinou aplikací,
  minimalizované (i z celé obrazovky) a malý náhled navrchu: „Zobrazit kameru“ ho přivede dopředu s fokusem (Esc
  v okně kamery reaguje). Po zavření okna položka zmizí.
- **W3 – blikání:** při kroku W1 a při přepnutí normální okno navrchu ↔ celá obrazovka se okno jednou krátce skryje
  a ukáže (Avalonia, přijatelné podle otázky 5) – zapsat, jestli nedělá nic horšího (ztráta fokusu, posun, jiný monitor).

## Poznámky k implementaci
- Uzavřeno 2026-10-30: zbývající ruční ověření přesunuto do T-114 a T-115 (rozhodnutí uživatele)
- **Avalonia 12.1.4 (Avalonia.Native) a aktivační politika:** `MacOSPlatformOptions.ShowInDock` se použije jednou při
  startu (`AvaloniaNativePlatform.DoInitialize` → `MacOptions::SetShowInDock`, ta nastaví politiku jen když se liší).
  Jinde `setActivationPolicy:` v `libAvaloniaNative.dylib` volaná není (ověřeno disassemblerem `otool` na balíčku
  12.1.4: jediný volající je `SetShowInDock`; `applicationWillFinishLaunching:` politiku jen porovnává). Avalonia tedy
  naši změnu za běhu nepřepisuje. `applicationShouldHandleReopen:hasVisibleWindows:` volá `OnReopen` a vrací `YES` –
  aplikace to už zpracovává jako druhé spuštění (T-043: nejnověji použité okno dopředu, obnovené z minimalizace).
  `ShowInDock = false` v `Program` zůstává (start bez ikony v Docku).
- **Rozhraní** `Platform/IAppPresence.cs` (`SetVisible(visible, keepActive)`, `NoAppPresence` pro Windows a testy);
  **macOS** `Platform/MacOS/MacAppPresence.cs`: `activationPolicy` / `setActivationPolicy:` (Regular = 0, Accessory = 1)
  přes `ObjCRuntime` (`[LibraryImport]` na `objc_msgSend`, nový tvar `SendBool(receiver, selector)` pro `isActive`), jen
  nad existujícím `NSApp` (`MacAppActivation.RunningApplication`, teď `internal`): bez AppKitu (headless testy, čekání na
  displej T-099) nic. Politika se mění jen když se liší; změna sama aplikaci neaktivuje ani nepřeskládá okna. Při
  přechodu na Accessory se zbývajícím oknem navrchu a aktivní aplikací se aplikace znovu aktivuje, pokud ji AppKit
  deaktivoval (otázka 2).
- **Počítadlo** `Windows/AppPresence.cs` (bez Avalonie): okno + zda je navrchu; platformě hlásí jen přechody 0 ↔ 1.
  Napojení v `AuxiliaryWindowTracker` → `AvaloniaRecentWindow`: každé pomocné okno (Nastavení, Diagnostika, O aplikaci,
  Licence přes `TrackChild`, okna kamery) se započítá při sledování **před zobrazením** (Dock je tedy hotový dřív, než
  `AvaloniaAuxiliaryWindow.Activate` aktivuje aplikaci a okno – právě otevřené okno zůstane vpředu), přepočítá se při
  změně `Window.Topmost` (přepnutí režimu, uložená volba T-037) a odebere se při zavření nebo když zobrazení selže.
  Kompozice: `App.StartHost` (`CreateAppPresence`: macOS `MacAppPresence`, jinak `NoAppPresence`).
- **Okno kamery (oba OS):** `ShowInTaskbar` se řídí `Topmost` (`CameraWindow.OnPropertyChanged`,
  `ShowsInTaskbar(topmost) => !topmost`) místo režimu. S výchozími volbami se nic nemění (náhled navrchu bez tlačítka,
  normální okno a celá obrazovka s tlačítkem); nově normální okno navrchu tlačítko nemá a náhled bez „vždy navrchu“ ho
  má. Ostatní okna (Nastavení…) navrchu nikdy nejsou a tlačítko mají jako dosud.
- **Menu:** `TrayMenuCommand.CameraToFront`, `TrayMenuOptions.CameraToFront` (`ForCurrentPlatform` = zapnuto na obou
  OS od rozhodnutí 2026-10-29; `Default` pro testy staršího menu vypnuto). Na Windows `BringToFront` stejně obnoví
  minimalizované okno a aktivuje ho (`Window.Activate`; aktivace aplikace jen na macOS), i okno navrchu bez tlačítka na
  hlavním panelu. Neověřeno na Windows, zda Windows aktivaci z menu v oznamovací oblasti vždy povolí (krok W2). Podmenu
  tiskárny s otevřeným oknem: „Otevřít PrusaLink“, **„Zobrazit kameru“**, „Skrýt kameru“ (nová položka nad skrytím;
  změna počtu položek nahradí položku tiskárny v nativním menu, což se děje jen při otevření / zavření okna). Klik →
  `ITrayAppCommands.BringCameraToFront` → `CameraWindows.BringToFront`: minimalizované okno obnoví (na celou obrazovku,
  pokud v ní bylo), pak jako `Show` aktivuje aplikaci a okno; okno zavřené mezitím otevře. Text `MenuCameraToFront` ve
  všech 8 jazycích má stejné znění jako `MenuShowCamera`, jen přístupová klávesa je jinde, aby byla v podmenu jedinečná
  (na Windows ji písmeno samotné spustí, T-069): en „_Show camera“, cs „_Zobrazit kameru“, sk „_Zobraziť kameru“, de „Kamera
  _anzeigen“, es „_Mostrar cámara“, fr „_Afficher la caméra“, it „_Mostra telecamera“, pl „_Pokaż kamerę“.
- **Názvy oken pro menu Docku:** okno kamery „{tiskárna} – kamera“, Nastavení, „Diagnostika – LayerPeek“, „O aplikaci
  LayerPeek“, „Licence třetích stran“ – beze změny. Malý náhled nemá rámeček, AppKit ho v seznamu oken nemusí ukázat.
- **Testy:** `AppPresenceTests` (počítadlo), `AppPresenceUiTests` (skutečná okna: pořadí Dock → aktivace aplikace,
  přepínání režimů, okna navrchu podle voleb, uložená volba s otevřeným oknem, Nastavení / O aplikaci vedle náhledu,
  okno bez aktivace, selhané zobrazení), `CameraWindowTaskbarUiTests` (tlačítko na hlavním panelu podle `Topmost` ve
  všech režimech a kombinacích voleb, při otevření i po uložení), `TrayMenuCameraToFrontTests` (menu obou OS
  s ikonami tiskáren i bez nich, bez volby menu T-040, klik, přístupové klávesy v 8 jazycích),
  `TrayMenuCameraToggleUiTests` (skutečné `NativeMenu`: obnovení z minimalizované celé obrazovky, aktivace aplikace,
  aktivace malého náhledu navrchu, zmizení položky). Upravené dva testy
  `SettingsWindowCameraWindowActionsUiTests` (náhled bez „vždy navrchu“ má teď tlačítko, normální okno navrchu ne).
- **Ověřeno (macOS 27.2, Apple Silicon, 2026-10-29, headless):** `dotnet build` a `dotnet build -c Release` 0 varování,
  `dotnet test` 7615 testů, 0 selhání (180 přeskočeno). Profil uživatele beze změny (stejná místa jako
  `tools/profile-snapshot.ps1`, kontrolováno ekvivalentním skriptem v bashi – pwsh nešlo v sandboxu agenta spustit;
  bez `sfltool`). **Neověřeno:** skutečné chování AppKitu (ikona v Docku, ⌘Tab, menu Docku, reopen,
  fokus a pořadí oken, menu bar po přepnutí) – ruční kroky v `docs/macos-manual-testing.md`, sezení 2, krok 7; běh na
  Windows (tlačítka na hlavním panelu, otázka 5). AOT publish nespuštěn (nové volání jsou jen další `[LibraryImport]`
  tvary stejného `objc_msgSend`, analyzátory AOT v buildu bez varování).
- **Navazující úprava (2026-10-29, větev `feature/T-102-follow-up`):** „Zobrazit kameru“ i na Windows (otázka 1),
  rozhodnutí k otázkám 2, 4 a 5 zapsána, kroky W1–W3. Ověřeno na macOS (headless): `dotnet build` a `dotnet build -c
  Release` 0 varování, `dotnet test` 7706 testů, 0 selhání (181 přeskočeno), profil uživatele beze změny (stejný
  skript v bashi). Na Windows neověřeno.
- Bez nových balíčků.
