---
id: T-237
title: macOS – malý náhled po Esc z celé obrazovky má titulkový pruh a je o 28 bodů vyšší (T-038 otázka 2)
status: done
owner: ui-dev
priority: P2
platform: macos
depends_on: [T-038, T-215]
---

## Cíl
Okno kamery otevřené jako malý náhled (bez rámečku, „vždy navrchu“), přepnuté do celé obrazovky a vrácené klávesou
**Esc**, se po animaci vrátí na místo malého náhledu, ale **s titulkovým pruhem** (zavřít / minimalizovat / zvětšit)
a o výšku pruhu vyšší: 320 × 241 bodů místo 320 × 213, spodní okraj 10 bodů pod pracovní plochou (pod Dockem). Cestou
přes tlačítko malého náhledu v celé obrazovce (`CameraMiniButton`) je výsledek správný. Oprava „umístit malý náhled
znovu po animaci“ je schválená podmíněně v T-038 (otázka 2, 2026-10-30) – kontrola `mac.mini-after-fullscreen` ji při
`FAIL` předává do nového úkolu.

## Zjištění (první běh s odemčenou obrazovkou, 2026-11-15)
- Běh `artifacts/verify/20261115-155430/` (`-Preset release`, commit `24d73aa3`), Mac mini Mac14,12, macOS 27.2.1,
  hlavní displej „iPad Remote“ (Sdílení obrazovky) + „Virtuální 16:10“. Výňatek zprávy:
  „(b) Esc: size 320 x 241 pt at 951, 606, expected 320 x 213 pt at 951, 606 (off: height; tolerance 1 pt); (b) Esc:
  bottom distance -10 pt, expected 18 pt; (b) Esc: has a window frame (a title bar (close, minimize, zoom)), expected
  none“. Uložený malý náhled i cesta (a) „mini preview button“: 320 × 213 bodů, bez rámečku, vrstva 3.
- Důkazy: `evidence/mac.mini-after-fullscreen/placements.json`, `app-vcfd5aa8d.log`.
- Kód: `CameraViewModel.Escape()` nastaví `Mode = _fullScreenOrigin` (Mini) stejně jako `ShowMiniCommand`; rozdíl je
  zřejmě v načasování vůči animaci odchodu z celé obrazovky (styl okna bez rámečku se nastaví dřív, než AppKit animaci
  dokončí a obnoví titulkové okno).
- Uživatel mohl během běhu na Mac sáhnout (nevíme); chybu je proto vhodné nejdřív zopakovat samostatně:
  `caffeinate -d -u -i pwsh tools/macos-verify.ps1 -Unattended -Only mac.mini-after-fullscreen -Allow mac.mini-after-fullscreen`.

## Rozsah
- Po odchodu z celé obrazovky do malého náhledu (všemi cestami: tlačítko, Esc, ⌃⌘F, „Použít“ v Nastavení) po skončení
  animace znovu použít styl bez rámečku, velikost a roh uloženého malého náhledu (T-038 otázka 2).
- Bez změny chování na Windows.

## Akceptační kritéria
- [x] `mac.mini-after-fullscreen` = `PASS` (všechny tři cesty: velikost ± 1 bod, roh, bez rámečku).
- [x] Testy (UI / jednotkové) pro pořadí „konec animace → umístění malého náhledu“, kde to jde bez skutečného AppKitu.
- [x] `dotnet build` a `dotnet test` bez chyb a varování.

## Otevřené otázky
- (žádné – oprava schválena v T-038 otázka 2)

## Poznámky k implementaci
- **Zopakováno samostatně** (2026-11-15, Mac mini Mac14,12, macOS 27.2.1, hlavní displej „iPad Remote“ přes Sdílení
  obrazovky + „Virtuální 16:10“): `artifacts/verify/20261115-194512/` = `FAIL`, stejně jako v T-127 – (a) a (c)
  320 × 213 bez rámečku, (b) Esc 320 × 241 s titulkovým pruhem, spodní okraj −12 bodů. Bez zásahu uživatele.
- **Skutečná příčina (dočasná sonda v okně kamery, styleMask `NSWindow` a změny stavu):** nejde o načasování vůči
  animaci. Esc u okna na celé obrazovce se do Avalonie vůbec nedostane (žádný `KeyDown`), celou obrazovku ukončí sám
  AppKit. Avalonia.Native 12.1.6 (`WindowImpl`) si při vlastním vstupu do celé obrazovky nastaví příznak
  `_fullScreenActive` a smaže ho jen ve vlastním `ExitFullScreenMode` (když `WindowState` změní aplikace); při odchodu,
  který udělal AppKit (Esc, zelené tlačítko, menu), zůstane nastavený a `SetDecorations` pak každou změnu
  `WindowDecorations` jen uloží a nepoužije – až do dalšího vstupu do celé obrazovky přes Avalonii. Avalonia změnu stavu
  hlásí až po `windowDidExitFullScreen:` (po animaci, styleMask už bez `NSWindowStyleMaskFullScreen`), okno kamery pak
  správně přepne na malý náhled a nastaví `WindowDecorations.None`, ale okno si ponechá titulkový rámeček, který AppKit
  obnovil (normální okno, se kterým se do celé obrazovky vstupovalo). Cesty (a) a (c) mění `WindowState` samy, proto
  fungovaly. Stejnou chybu by měl i pozdější přechod normální okno → malý náhled po Esc z celé obrazovky otevřené
  z normálního okna (opraveno stejně).
- **Oprava (jen macOS):** `Platform/MacOS/MacWindowDecorations.cs` – `MacFullScreenDecorations` (čistá logika:
  po odchodu z celé obrazovky, který nezměnila aplikace, Avalonia dekorace ignoruje; konec při dalším vstupu do celé
  obrazovky) a `NativeWindowDecorations` (`IWindowDecorations` přes stávající `ObjCRuntime`, `[LibraryImport]`, bez
  nového balíčku, AOT beze změny): totéž, co `WindowImpl::SetDecorations` pro `Full` / `None` – styleMask
  (`StyleMask`: titulek, zavřít, změna velikosti / `FullSizeContentView`, minimalizace; ostatní příznaky beze změny),
  stín, viditelnost titulku, průhlednost titulkového pruhu a skrytí tlačítek. `Windows/CameraWindow.Decorations.cs`:
  okno si zapamatuje, kdo celou obrazovku opustil (`_applyingMode`), ještě než se režim přepne, a každou změnu
  `WindowDecorations`, kterou Avalonia ignoruje, předá nativnímu oknu; režim pak okno umístí jako vždy (`PlaceMini`:
  uložená velikost a roh), takže pořadí je „konec animace → bez rámečku → umístění“. Avalonia hodnotu uloží také, její
  pozdější vlastní aktualizace tedy souhlasí. Na Windows, v headless testech a u odchodů, o které požádala aplikace,
  se nic nevolá. Až Avalonia chybu opraví, je náhradní cesta jen nadbytečná (stejný výsledek).
- **Testy:** `App.Tests/Platform/MacOS/MacFullScreenDecorationsTests` (12: odchod AppKitu / aplikace, nový vstup
  oběma cestami, jiné změny stavu, styleMask jako Avalonia, ostatní příznaky zůstanou),
  `App.UiTests/Cameras/CameraWindowFullScreenDecorationsUiTests` (6, skutečné okno kamery s falešným nativním oknem:
  headless bez nativních dekorací; po odchodu AppKitu malý náhled jednou bez rámečku až po skončení celé obrazovky
  a teprve potom umístění na uložené místo; tlačítko, Esc a F11 přes aplikaci nic nativního; další režimy dostanou své
  dekorace, dokud aplikace nevstoupí do celé obrazovky; normální okno beze změny a pozdější náhled bez rámečku; po
  zavření nic).
- **Ověřeno na tomto Macu:** `mac.mini-after-fullscreen` = `PASS` (`artifacts/verify/20261115-195207/`): uložený
  náhled, (a) tlačítko, (b) Esc i (c) „Použít“ 320 × 213 bodů na 951, 600, roh vpravo dole (16 / 16 bodů), bez
  rámečku, vrstva 3. Hlavní displej byl „iPad Remote“ (Sdílení obrazovky); na výsledek to vliv nemělo (stejný displej
  i v chybných bězích). Zelené tlačítko a menu „Ukončit režim celé obrazovky“ jdou z kódu stejnou cestou jako Esc (odchod
  AppKitu); ⌃⌘F zpracuje buď aplikace (`OnMacFullScreenKeyDown`, jako F11), nebo – když ho AppKit vezme jako zkratku
  své položky celé obrazovky v menu Okno – AppKit; oprava pokrývá obě cesty. **Živě neověřeno:** zelené tlačítko,
  menu a ⌃⌘F (kontrola je neprochází).
  `dotnet build` 0 varování, 0 chyb; `dotnet test` 10984 testů, 0 selhání, 287 přeskočeno.
