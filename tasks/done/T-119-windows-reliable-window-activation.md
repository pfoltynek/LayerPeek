---
id: T-119
title: Windows – spolehlivé vytažení otevřeného okna do popředí
status: done
owner: ui-dev
priority: P2
platform: windows
depends_on: [T-102, T-117]
---

## Cíl
Položka tray menu „Přenést kameru do popředí“ (T-102) na Windows okno kamery nevytáhne dopředu. Uživatel 2026-10-31:
vypnul špendlík (T-117), okno kamery zapadlo za jiné okno, zvolil „Přenést kameru do popředí“ a nic se nestalo.

Analýza (2026-10-31): cesta v kódu je správná (`CameraWindows.BringToFront` → `AuxiliaryWindowSlot` →
`AvaloniaAuxiliaryWindow.Activate` → `Window.Activate()`), ale Avalonia 12.1.4 na Windows aktivuje okno jen voláním
`SetForegroundWindow` (`Avalonia.Win32.WindowImpl.Activate`, ověřeno dekompilací). Windows ho kvůli zámku popředí
(foreground lock) často odmítne a okno zůstane za ostatními (nanejvýš zabliká tlačítko na hlavním panelu). Headless
testy kontrolují jen to, že se `Activate` zavolalo, chování Windows nesimulují.

Stejnou cestou se aktivují i už otevřená okna Nastavení, Diagnostiky a O aplikaci (další volba z menu) a okna
v `AuxiliaryWindowTracker.BringToFront` (T-043).

## Rozsah (rozhodnuto 2026-10-31)
1. **Položka zůstává** na obou OS (uživatel: „Ponechat a opravit“). Je jedinou cestou zpět na macOS (bez ikony v Docku)
   a na Windows u minimalizovaného okna se špendlíkem (bez tlačítka na hlavním panelu, T-102).
2. **Spolehlivé vytažení dopředu na Windows** pro všechna pomocná okna (kamera, Nastavení, Diagnostika, O aplikaci
   a jejich vlastněná okna), jedno společné řešení. Uživatel: „Ano, všechna okna“.
3. Řešení přes `[LibraryImport]` do `user32` (bez nového balíčku, AOT v pořádku), izolované v kódu pro Windows;
   macOS beze změny (tam dál `bringAppToFront`, T-019).
4. Nesmí měnit `Window.Topmost` ani dočasně: podle něj se řídí tlačítko na hlavním panelu a Spaces (T-102,
   `AppPresence`), blikalo by. Okno se špendlíkem po vytažení zůstane vždy nahoře, okno bez špendlíku nesmí zůstat
   vždy nahoře.
5. Minimalizované okno se obnoví jako dosud (celá obrazovka zpět do celé obrazovky).

## Akceptační kritéria
- [x] Test, který chybu zopakuje před opravou (Windows E2E/integrační: okno kamery překryté oknem jiného procesu,
      volba z tray menu, kamera je pak v popředí / nad oknem jiného procesu)
- [x] Kamera bez špendlíku i se špendlíkem, normální i minimalizovaná, malý náhled: po volbě vepředu a aktivní
- [x] Nastavení, Diagnostika, O aplikaci: druhá volba z menu vytáhne otevřené okno dopředu
- [x] Okno bez špendlíku po vytažení není vždy nahoře; `Topmost` se během vytažení nemění
- [x] Odmítne-li Windows aktivaci i tak, okno se aspoň přesune v pořadí nad ostatní okna (přijato uživatelem 2026-10-31:
      hned pod okno v popředí, nad všechna ostatní okna)
- [x] macOS beze změny (unit testy cesty pro jiné OS)
- [x] Žádné nové balíčky, žádné potlačené trim/AOT varování
- [x] `dotnet build` a `dotnet test` bez chyb a varování
- [x] Ruční ověření ve skutečné aplikaci (Windows) → přesunuto do T-114

## Otevřené otázky
1. ~~Co dělat, když Windows odmítne i připojení vstupu?~~ **Rozhodnuto uživatelem 2026-10-31:** nejdřív zkusit „nulový
   pohyb myši“ (`SendInput` s nulovým `INPUT_MOUSE`, pak znovu `SetForegroundWindow`). Proti VMware nepomohl (viz
   poznámky), proto ho uživatel nechal **úplně odstranit**; zůstává připojení vstupu a záložní `HWND_TOP`.
2. ~~Stačí záložní pořadí „hned pod oknem v popředí“?~~ **Rozhodnuto uživatelem 2026-10-31:** ano, přijato jako poslední
   možnost (kritérium 5 odškrtnuto).

## Poznámky k implementaci
**Stav 2026-10-31:** kód, unit a UI testy hotové, build a testy čisté, E2E skutečné aplikace s opravou prošlo a bez ní
selhalo, obě otevřené otázky rozhodnuté, review bez blokujících nálezů; ruční ověření ve skutečné aplikaci → T-114.
Uzavřeno 2026-10-31.

### Řešení
- Nové rozhraní `IWindowActivator` (`src/LayerPeek.App/Windows/WindowActivator.cs`) aktivuje okno, které má na žádost
  uživatele přijít dopředu. `AvaloniaWindowActivator` = dosavadní `Window.Activate()` (macOS a ostatní OS),
  `WindowsWindowActivator` = nativní vytažení. Výběr podle OS: `WindowsWindowActivator.ForCurrentPlatform` v `App.StartHost`.
- `Platform/Windows/ForegroundRaiser.cs` (čistá logika nad `IForegroundWindowApi`, testovatelná s falešným správcem oken)
  a `Platform/Windows/ForegroundNative.cs` (`[LibraryImport("user32.dll")]`): je-li okno už v popředí, nic; jinak se vstup
  vlákna okna na dobu volání připojí k vláknu okna v popředí (`AttachThreadInput`), zavolá se `BringWindowToTop`
  a `SetForegroundWindow` a vstup se ve `finally` vždy odpojí. Nepřipojuje se, když okno v popředí patří vlastnímu vláknu,
  když žádné okno v popředí není, nebo když vlákno v popředí nereaguje (`IsHungAppWindow`; připojení by zablokovalo
  i UI vlákno). Když Windows popředí i tak odmítne, okno se přesune `SetWindowPos(HWND_TOP, SWP_NOMOVE | SWP_NOSIZE |
  SWP_NOACTIVATE)`; Windows ho pak nechá hned pod oknem v popředí, nad všemi ostatními okny (přijato uživatelem
  2026-10-31 jako poslední možnost). Používá se jen `HWND_TOP`, nikdy `HWND_TOPMOST`/`HWND_NOTOPMOST`, takže příznak „vždy navrchu“ se
  na úrovni Win32 nemění a stav Avalonie (`Window.Topmost`, podle něj `AppPresence` a tlačítko na hlavním panelu) se
  nerozejde. Okno bez HWND (headless, ještě neukázané) a výjimka nativní vrstvy → `Window.Activate()`; nic se nevyhodí,
  odmítnutí i chyba jdou do logu na úrovni Debug.
- Zdůvodnění: Avalonia 12.1.4 na Windows v `WindowImpl.Activate` volá jen `SetForegroundWindow`, které zámek popředí
  odmítne, když proces nedostal poslední vstup a v popředí je jiný proces. Připojení vstupu je zavedený postup bez
  vkládání vstupu; `Topmost` přes Avalonii ani dočasně neměníme (rozsah bod 4).
- Minimalizovaná okna obnovují volající jako dřív (`CameraWindows.BringToFront`: celá obrazovka zpět do celé obrazovky;
  `AvaloniaRecentWindow.BringToFront`: vlastníci a stav před minimalizací), a to před aktivací (ověřeno UI testy).

### Změněná místa aktivace (`.Activate()`)
- `AvaloniaAuxiliaryWindow.Activate` (cesta `AuxiliaryWindowSlot.ShowOrActivate`: „Přenést kameru do popředí“, druhá
  volba Nastavení… / Diagnostika… / O aplikaci, i první otevření) → `IWindowActivator`; na macOS zůstává
  `bringAppToFront` (T-019) před aktivací.
- `AvaloniaRecentWindow.BringToFront` (druhý start / reopen přes `InstanceActivation`, i vlastněná okna jako licence)
  → `IWindowActivator`, potom jako dřív `IWindowShell.RequestAttention` (Windows: blikání, macOS: aktivace aplikace).
- Aktivátor se předává přes `AuxiliaryWindowTracker`, `AuxiliaryWindowFactory` a `CameraWindows` (nepovinné parametry).
- Mimo rozsah: `AboutWindow.OnLicensesRequested` (`_licenses.Activate()`) – vyvolá ho klik v okně O aplikaci, proces je
  tedy v popředí a dostal poslední vstup, Windows aktivaci nebrání. `App.OnInstanceCommand` → `InstanceActivation` už jde
  přes `AvaloniaRecentWindow`. Kód pro macOS (`MacAppActivation`, `MacWindowShell`) se nemění.

### Testy
- `tests/LayerPeek.App.Tests/Platform/Windows/ForegroundRaiserTests.cs` (falešný `FakeForegroundWindowApi`, linkovaný
  i do UI testů): pořadí volání, odpojení i při výjimce, bez připojení pro vlastní vlákno / žádné okno / zamrzlé vlákno,
  záložní `HWND_TOP` po odmítnutí, volba aktivátoru podle OS (Windows nativní, jinde Avalonia).
- `tests/LayerPeek.App.UiTests/Windows/WindowActivatorUiTests.cs` (headless): „Přenést kameru do popředí“ obnoví
  minimalizované okno a aktivuje ho přes aktivátor, `Topmost` se nezmění (bez špendlíku i připnutý malý náhled); druhá
  volba z menu jde přes aktivátor až po aktivaci aplikace (pořadí macOS); druhý start přes aktivátor a pak shell; bez
  aktivátoru `Window.Activate` (macOS beze změny); `WindowsWindowActivator` místo `Window.Activate` volá nativní vytažení,
  při chybě nativní vrstvy a bez HWND použije `Window.Activate` a nevyhodí výjimku.
- Reprodukce na vyžádání (`LAYERPEEK_E2E=1`, jen Windows; běžný `dotnet test` ji přeskočí, protože bere popředí):
  `tests/LayerPeek.App.Tests/Platform/Windows/ForegroundRaiserE2ETests.cs` – viditelné okno testovacího procesu bez
  aktivace, v popředí okno jiného procesu (to, ve kterém uživatel pracuje; na konci dostane popředí zpět). Nejdřív musí
  Windows odmítnout obyčejné `SetForegroundWindow` (chování Avalonie, tj. stav před opravou), jinak se test přeskočí (není
  co reprodukovat). Pak buď `ForegroundRaiser` dostane okno do popředí nad okno jiného procesu, nebo – jen když Windows
  odmítl i připojení vstupu – musí okno být v přijatém záložním stavu: hned pod oknem v popředí, nad všemi ostatními
  okny, která nejsou vždy navrchu. Nikdy vždy navrchu. Výstup jmenuje, který případ nastal („Case: …“), a zapíše výsledek
  každého volání a okna nad testovacím oknem. Výsledky 2026-10-31 (v popředí VMware, před úpravou testu): dvakrát
  obyčejné volání **odmítnuto** (chyba reprodukována), `AttachThreadInput` → `ERROR_ACCESS_DENIED`, výsledek `RaisedOnly`,
  okno hned pod oknem VMware; v ostatních bězích Windows obyčejné volání pustil (test přeskočen). Kontrola mimo test: připojení k vláknům hlavního panelu a okna Chromium (nejsou v popředí) projde, k vláknu
  VMware v popředí ne (obě aplikace na stejné úrovni integrity, VMware bez `uiAccess`).
- E2E skutečné aplikace `tests/LayerPeek.App.E2E.Tests/BringToFrontTests.cs` (myší otevřené tray menu, položka přes UI
  Automation, okno překryté aktivovaným `CoverWindow` testovacího procesu; kamera bez špendlíku, minimalizovaná, připnutá
  minimalizovaná, malý náhled připnutý i odepnutý; druhá volba Nastavení… / Diagnostika… / O aplikaci; ověřuje popředí,
  pořadí nad krycím oknem a nezměněné „vždy navrchu“). První pokus 2026-10-31 nedoběhl kvůli obsazené ploše (krycí okno
  nezískalo popředí od VMware, pohyb myši během běhu). **2026-10-31 spustil koordinátor na volné ploše** (VMware
  minimalizovaný, `pwsh tools/run-e2e.ps1 -Class '*BringToFrontTests' -NoSystemChanges`): s opravou (připojení vstupu, bez
  nulového vstupu myši) **2/2 prošly**; s dočasně vypnutou opravou (`ForCurrentPlatform` vracel
  `AvaloniaWindowActivator.Instance`, soubor pak vrácen; ověřeno, že je `WindowActivator.cs` v pořádku) **oba selhaly**.
  Chyba je tedy na skutečné aplikaci zopakovaná a připojení vstupu ji proti běžné aplikaci opravuje (kritéria 1–3).
  Pokrytí kritéria 2: normální okno bez špendlíku, totéž minimalizované, normální se špendlíkem minimalizované, malý
  náhled se špendlíkem a bez něj; netestuje se normální okno se špendlíkem bez minimalizace a minimalizovaný malý náhled
  (obojí stejná cesta).
- Nulový vstup myši (rozhodnutí 2026-10-31, otázka 1), běh `ForegroundRaiserE2ETests` koordinátorem 2026-10-31 s VMware
  v popředí: obyčejné volání odmítnuto; `attach 69916->5252` = False (chyba 5); `BringWindowToTop` True;
  `SetForegroundWindow` False; `SendInput` (nulový vstup myši) = True; znovu `SetForegroundWindow` False;
  `SetWindowPos(HWND_TOP)` True; výsledek `RaisedOnly`, okno hned pod oknem VMware (nad všemi ostatními). Nulový vstup
  tedy **nepomohl** a na rozhodnutí uživatele byl úplně odstraněn (kód, struktura `INPUT`, `SendInput`, členy falešného
  API, unit testy, výsledek `ForegroundAfterInput`). Test na vyžádání byl pak upraven na přijaté chování (viz výše);
  po úpravě 1× spuštěn s VS Code v popředí – Windows obyčejné volání pustil, test přeskočen.

### Ověření
- Windows 11 (10.0.26300): `dotnet build` 0 varování, 0 chyb; `dotnet test` 8914 testů, 8652 prošlo, 262 přeskočeno, 0 selhalo (po odstranění nulového vstupu myši). Aplikace (`dotnet run`) se spustila a běžela, pak ukončena.
- Žádný nový balíček NuGet (licence se neřeší). AOT: jen `[LibraryImport]` se zdrojově generovaným marshallingem
  a blitovatelnými typy, žádná reflexe ani `UnconditionalSuppressMessage`/`NoWarn`; analyzátory trim/AOT
  (`IsAotCompatible`) bez varování. Lokální AOT publish se nespouštěl.
- Review (qa-reviewer, 2026-10-31): lokální Native AOT publish `dotnet publish src/LayerPeek.App -c Release -r win-x64`
  prošel bez varování. Doplněny unit testy `DeniedAttachIsNotDetachedAndStillRaisesTheWindow` a
  `WindowWithoutAThreadIsNotAttached` (falešné API umí odmítnout připojení, `AttachAllowed`); výpis testu na vyžádání
  uvádí poslední chybu jen u volání se `SetLastError` (u ostatních by byla zastaralá).
- macOS: kód se tam nemění (výběr `AvaloniaWindowActivator`); na Macu neověřeno.
