---
id: T-120
title: E2E testy místo ručních kontrol okna kamery (špendlík, obrys, hlavní panel, menu)
status: done
owner: qa-reviewer
priority: P2
platform: windows
depends_on: [T-102, T-117, T-118, T-119]
---

## Cíl
Uživatel 2026-10-31 ručně ověřil body T-102, T-117 a T-118 v T-114 (vše v pořádku) a přeje si, aby je příště nemusel
kontrolovat znovu: „ideální by bylo, kdyby jsi i na toto dodal UI testy“. Ruční kontroly se nahradí automatickými testy
na skutečné aplikaci (E2E přes `tools/run-e2e.ps1`, simulátor, UI Automation), případně headless UI testy tam, kde
skutečná aplikace nic nepřidá. Pravidla: změny systému (měřítko, motiv) jen s obnovou i po pádu a s varováním
a odpočtem 5 s (`run-e2e.ps1`); testy po sobě uklidí vše; žádné globální klávesové zkratky; nikdy nepsat do cizí
aplikace ani ji nezavírat.

## Rozsah
Nejdřív zmapovat, co už pokrývají stávající testy (headless UI testy T-117/T-118, `BringToFrontTests` T-119,
`windows-verify.ps1 -Only win.taskbar`, testy měřítek T-071, `TrayMenuTests`), a doplnit jen chybějící.

1. **T-117 špendlík (skutečná aplikace):** špendlík v normálním okně i malém náhledu, na celé obrazovce není; klik
   přepne „vždy navrchu“ hned (Win32 `WS_EX_TOPMOST`, okno nad oknem jiného procesu) a tlačítko na hlavním panelu
   zmizí / objeví se; kontextové menu pravým tlačítkem, klávesou Menu a Shift+F10 (dvě položky v normálním okně,
   jedna zaškrtnutá v malém náhledu); přístupnost přes UIA (název, `Toggle` stav, mezerník/Enter přepíná, fokus
   zůstane); volba přežije restart aplikace a otevřené Nastavení ji ukáže.
2. **T-118 obrys (skutečná aplikace):** obrys jen v malém náhledu, viditelný na světlém i tmavém pozadí (snímek jen
   oblasti okna, ne celé plochy), zesvětlení při najetí; ostrost čáry při 100 / 125 / 150 % (1 px / 1 px / 2 px, bez
   půlpixelu) – se změnou měřítka přes stávající infrastrukturu T-071; úchopy mění velikost, tažení přesouvá okno.
3. **T-102 hlavní panel a menu:** tlačítko na hlavním panelu podle pravidla (normální okno a celá obrazovka ano,
   malý náhled ne, okno navrchu ne; Nastavení, Diagnostika, O aplikaci vždy); položky menu tiskárny s otevřenou kamerou
   a přístupové klávesy; přepnutí normální okno navrchu ↔ celá obrazovka bez ztráty fokusu, posunu a změny monitoru.
4. Co automatizovat nejde (např. subjektivní čitelnost ztlumeného špendlíku), zůstane jako nepovinná doporučená
   ruční kontrola v T-114.

## Akceptační kritéria
- [x] Zmapované pokrytí (co už testují stávající testy) v poznámkách
- [x] E2E / UI testy pro body 1–3, každý ruční bod T-114 (T-102, T-117, T-118) má odkaz na test, který ho nahrazuje
- [x] Testy po sobě uklidí vše (okna, procesy, nastavení, měřítko, motiv) i po pádu; jména PascalCase bez podtržítek
- [x] Běh `tools/run-e2e.ps1` na nové třídy prošel (výsledek v poznámkách)
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- ~~Q1: nahradit ruční `win.taskbar` ve `tools/windows-verify.ps1` během E2E?~~ **Rozhodnuto 2026-10-31 (uživatel):** ano –
  `win.taskbar` je teď kontrola druhu „oprávnění“ (jako `win.e2e`) a spouští
  `tools/run-e2e.ps1 -Class '*CameraTaskbarAndModeSwitchTests' -NoSystemChanges`; upraveno i `docs/windows-manual-testing.md`.
- ~~Q2: zbývající subjektivní kontroly?~~ **Rozhodnuto 2026-10-31:** zůstávají jako nepovinné doporučené body v T-114.
- ~~Q3: písmeno samotné v kontextovém menu okna kamery?~~ **Rozhodnuto 2026-10-31:** musí fungovat jako v menu
  v oznamovací oblasti (T-069) → samostatný úkol T-121 (změna produktu). Test zatím používá Alt+písmeno, s TODO(T-121)
  v `CameraPinTests.PressAltWith`.

## Poznámky k implementaci
Agent qa-reviewer, 2026-10-31, větev `feature/T-120-e2e-camera-window-checks` (necommitnuto).

### Zmapované pokrytí (před T-120)
- **Headless UI testy (`tests/LayerPeek.App.UiTests`)** – logika bez skutečného okna:
  `CameraWindowPinUiTests` (špendlík poslední v řadě 6 DIP od rohu, plný / obrysový, ztlumený 0,85 bez myši, žádný na celé
  obrazovce, klik / mezerník / Enter přepne `Topmost` a `ShowInTaskbar`, fokus zůstane, uloží se jen volba režimu,
  neúspěšné uložení vrátí stav, kontextové menu se dvěma / jednou položkou a zaškrtnutím),
  `CameraWindowMiniOutlineUiTests` (obrys jen v malém náhledu, 1 DIP `#73FFFFFF` / při najetí `#BFFFFFFF`, ostrý při
  vykreslovacím měřítku 1–2, úchopy pod obrysem berou stisky, změna velikosti úchopem), `AppPresenceUiTests` (macOS).
- **Testy aplikace (`tests/LayerPeek.App.Tests`)**: `SettingsCameraTopmostTests` – otevřené Nastavení převezme volbu
  špendlíku bez ztráty neuložených úprav a bez „změněno“.
- **E2E (`tests/LayerPeek.App.E2E.Tests`)**: `CameraWindowTests` (normální okno má tlačítko na hlavním panelu, malý náhled
  ne – jen pravidlo Win32), `SettingsAndDiagnosticsTests` (Nastavení a Diagnostika mají tlačítko – Win32),
  `IconsThemeAndScalingTests.TaskbarButtonShowsTheAppIcon` (skutečné tlačítko Nastavení na hlavním panelu, změna měřítka
  přes žurnál), `CameraTopmostTests` (pořadí nad oknem jiného procesu, volba z Nastavení, ne špendlík),
  `BringToFrontTests` (T-119: „Přenést kameru do popředí“ myší ve všech stavech, druhá volba Nastavení / Diagnostiky /
  O aplikaci), `TrayMenuTests` (položky podmenu s otevřenou kamerou), `TrayMenuDetailTests.MenuWorksFromTheKeyboardWithAccessKeys`
  (přístupové klávesy hlavního menu a podmenu se zavřenou kamerou, podtržení po Alt), `CameraLayoutModeTests` (pravé
  tlačítko → „Upravit rozložení“), `CameraMiniPreviewTests.ResizeGripCorrectsTheHeightOnceAfterTheReleaseAndTheStripMovesTheWindow`
  (úchop skutečnou myší, tažení pásu přesouvá okno – běží proti aplikaci už s obrysem), `CameraMiniPreviewTests` (přepínače
  pásu myší a klávesnicí), `CameraWidgetScaleTests` (malý náhled při 100–200 %).
- **Chybělo** (jen ruční kontrola): skutečný klik na špendlík a jeho vliv na `WS_EX_TOPMOST`, skutečné tlačítko na
  hlavním panelu a pořadí; vzhled ztlumeného špendlíku na skutečné obrazovce; kontextové menu klávesou Menu a Shift+F10,
  podtržení po Alt, zaškrtnutí přes UIA; UIA název a stav přepínače + mezerník / Enter ve skutečném okně; restart
  + otevřené Nastavení; obrys na světlém a tmavém pozadí, zesvětlení, ostrost při skutečném měřítku Windows, kurzory úchopů;
  pravidlo hlavního panelu se skutečným tlačítkem (normální okno navrchu, celá obrazovka, O aplikaci); přístupové klávesy
  podmenu s otevřenou kamerou a „písmeno samotné“; přepnutí navrchu ↔ celá obrazovka (fokus, místo, monitor, blikání).

### Nové testy (E2E, skutečná aplikace, spouští jen `tools/run-e2e.ps1`)
Adresa kamery nikam nevede (černý obraz) → pixely tlačítek a obrysu jsou jednoznačné. Snímky jen oken aplikace
(`PrintWindow`) nebo oblasti okna + 6 px kolem (`ScreenArea`, ≤ ¼ monitoru), nikdy celé plochy. Pozadí a „jiná aplikace“
jsou okna testovacího procesu (`CoverWindow`, nově i v jiné barvě a vložené pod okno kamery).
- `CameraPinTests`
  - `PinSitsInTheTopRightCornerAndStaysDimmedWithoutThePointer` – špendlík v normálním okně i malém náhledu vpravo nahoře
    (6 DIP, poslední tlačítko), na celé obrazovce není; bez myši je zapnutý špendlík vykreslený ztlumeně (jas 120–238)
    a ostatní tlačítka vůbec, s myší nad obrazem vše naplno.
  - `PinClickSwitchesAlwaysOnTopTheTaskbarButtonAndTheOrderAtOnce` – klik skutečnou myší v normálním okně i malém náhledu:
    `WS_EX_TOPMOST` do 2 s, UIA stav a název, tlačítko na hlavním panelu (pravidlo Win32 + skutečné tlačítko testovací
    aplikace přes UIA), okno nad / pod oknem jiného procesu, fokus zůstane na okně, po odjetí myši jen ztlumený špendlík /
    nic, uložená volba jen daného režimu.
  - `SpaceAndEnterSwitchThePinAndTheFocusStaysOnIt` – UIA: tlačítko, název „Always on top: on / off“, stav Toggle (to čte
    Předčítání); Tab na špendlík, mezerník a Enter přepnou, fokus zůstane na špendlíku, okno neztratí popředí (obě okna).
  - `ContextMenuOpensWithTheRightButtonTheMenuKeyAndShiftF10` – pravé tlačítko, klávesa Menu a Shift+F10 (jen do našeho
    okna): normální okno dvě položky (E, T, různé, podtržení jen po Alt – porovnání snímků menu), malý náhled jen „Vždy
    navrchu“; zaškrtnutí podle stavu (UIA Toggle), klik na položku a písmeno po Alt přepnou, Esc menu zavře a malý náhled
    zůstane.
  - `PinSurvivesARestartAndTheOpenSettingsShowIt` – špendlík při otevřeném Nastavení s neuloženou změnou: Nastavení hned
    ukáže volbu, neuložená změna zůstane a nezapíše se, Zrušit volbu nevrátí; po restartu okno navrchu bez tlačítka na
    hlavním panelu, špendlík i Nastavení stav ukazují.
- `CameraMiniOutlineTests`
  - `OutlineIsVisibleOnALightAndADarkBackgroundAndBrightensUnderThePointer` – bílé a tmavé (#202020) pozadí za náhledem:
    obrys `#73` na všech čtyřech hranách (dole nad pásem ~`#7E`), kontrast k pozadí ≥ 40, uvnitř hned černý obraz;
    myš nad obrazem, pásem a levým úchopem → `#BF`, po odjetí zpět; normální okno a celá obrazovka bez obrysu.
  - `GripsUnderTheOutlineShowTheSizingCursors` – myš na hranách a rozích (přímo na obrysu) ukáže systémový kurzor změny
    velikosti (`GetCursorInfo` vs. `IDC_SIZE*`), nad obrazem šipku.
  - `OutlineIsCrispInWholeDevicePixelsAtEveryScale` (trait `SystemChanges=DisplayScale`, se `-NoSystemChanges` se přeskočí)
    – 100 / 125 / 150 % primárního monitoru přes žurnál T-065/T-066: přesně 1 / 1 / 2 px obrysu, pak hned černá, venku
    pozadí (žádný půlpixel); ztlumený špendlík při každém měřítku (výřez do výsledků).
- `CameraTaskbarAndModeSwitchTests`
  - `TaskbarButtonFollowsAlwaysOnTopAndTheOtherWindowsAlwaysHaveOne` – W1: normální okno ano; „Vždy navrchu“ v Nastavení
    + Použít ne / zpět ano; celá obrazovka ano; malý náhled ne, přitom Nastavení, Diagnostika a O aplikaci ano; malý náhled
    s vypnutou volbou (Uložit) ano. Win32 i skutečné tlačítko na hlavním panelu.
  - `PrinterSubmenuOfAnOpenCameraHasUniqueAccessKeysAndTheLetterBringsItToFront` – W2: podmenu s otevřenou kamerou
    (otevřené myší, pak klávesy jen do menu aplikace – žádné Win+B), přístupové klávesy z prostředků, žádné dvě stejné;
    písmeno „F“ samotné přenese zakryté okno dopředu s fokusem.
  - `SwitchingThePinnedWindowToFullScreenAndBackKeepsFocusPlaceAndMonitor` – W3: F11 / F11, F11 / Esc z normálního okna
    navrchu a F11 / Esc z malého náhledu: fokus zůstane, stejný monitor, zpět přesně na místo, znovu navrchu bez tlačítka,
    celá obrazovka s tlačítkem; skrytí okna (WinEvent `EVENT_OBJECT_HIDE`) nejvýš jednou na přepnutí (přijaté blikání).

Úpravy infrastruktury: `CoverWindow` (barva, `PutBelow`), `CameraWindowChecks` (nový sdílený pomocník),
`WindowActivation.TypeChordInto` (Shift+F10 jen do okna v popředí), `WinEventRecorder.EventObjectHide`,
`Native.CurrentCursor` / `SystemCursor`.

### Úklid
Každá aplikace běží v izolovaném testovacím režimu (`AppInstance`, vlastní datová složka v %TEMP%, `await using` ji ukončí
a smaže i při výjimce, po pádu ji uklidí příští běh). Okna testu (`CoverWindow`) jsou v `using` a zaniknou nejpozději
s testovacím procesem. Měřítko jen přes `SystemChanges.DisplayScaleAsync` (žurnál s obnovou i po pádu, hlídací proces,
varování s odpočtem 5 s v `run-e2e.ps1`); motiv se nemění (světlé / tmavé pozadí jsou okna testu). Klávesy jen do okna
aplikace v popředí (`WindowActivation.TypeInto` / `TypeChordInto`, `TrayMenu.Press`), kliky jen na body ověřené
`WindowFromPoint` jako okno aplikace nebo testu. Názvy PascalCase bez podtržítek.

### První běh E2E (2026-10-31, hlavní agent na volné ploše)
`CameraTaskbarAndModeSwitchTests` 3/3, `CameraMiniOutlineTests` 3/3 (se změnou měřítka), regrese `CameraTopmostTests` 2/2
a `BringToFrontTests` 2/2 prošly. `CameraPinTests` 2/5 – rozbor tří selhání:
1. `PinClickSwitchesAlwaysOnTopTheTaskbarButtonAndTheOrderAtOnce` („normal-unpinned: pin 255, other buttons 255“) – **chyba
   testu**: po odepnutí Avalonia okno skryje a znovu ukáže (vrácení tlačítka na hlavní panel) a test přeskočil ukazatelem
   jedním skokem z okna ven – okno pak nedostalo pohyb myši ke sledování odchodu a zůstalo ve stavu „myš nad oknem“.
   Skutečná ruka se pohybuje plynule (uživatel ověřil ručně, že zůstane jen ztlumený špendlík). Oprava:
   `MovePointerBesideAsync` vede ukazatel po krocích (`GlideAsync`); do zprávy přibyl i fokus UIA.
2. `ContextMenuOpensWithTheRightButtonTheMenuKeyAndShiftF10` („letter T: the context menu to close“) – **chování
   produktu, ne ověřené uživatelem**: v kontextovém menu okna kamery písmeno po puštění Alt (ani samotné) položku nespustí;
   podtržení po Alt se ukáže (to test ověřil). Test nyní používá Alt+T (držený Alt), které Avalonia podporuje; otázka Q3.
3. `SpaceAndEnterSwitchThePinAndTheFocusStaysOnIt` („mini-enter: focus '' “) – **chyba testu (hlášení fokusu UIA)**:
   po změně vlastníka okna (skrytí tlačítka na hlavním panelu) UIA chvíli nehlásí fokus na špendlíku. Test nyní čeká až 2 s
   a jinak ověří skutečný fokus funkčně: další mezerník musí špendlík přepnout zpět (a pak se stav vrátí).

### Druhý běh E2E (2026-10-31) – dvě skutečné chyby produktu
`CameraMiniOutlineTests` 3/3, `CameraPinTests` 3/5. Testy zůstávají přísné (našly chyby); doplněna diagnostika
(fokus Win32 přes `GetGUIThreadInfo` ve zprávě i v hlášení selhání) a zprávy se ukládají i při selhání.
- **A – po odepnutí myší zůstanou vidět všechna tlačítka okna** (`PinClickSwitchesAlwaysOnTopTheTaskbarButtonAndTheOrderAtOnce`,
  „normal-unpinned: pin 255, other buttons 255“ i s plynulým pohybem myši). Příčina (Avalonia 12.1.4, dekompilováno):
  `CameraWindow.OnWindowButtonClick` (`src/LayerPeek.App/Windows/CameraWindow.axaml.cs:675`) po kliku myší volá
  `FocusManager.Focus(null)`; ten zruší fokus, ale v rozsahu fokusu (okně) nechá zapamatovaný prvek (`FocusManager`
  `FocusedElementProperty` = špendlík, větev `inputElement == Current`). Odepnutí vrací tlačítko na hlavní panel a Avalonia
  okno skryje a znovu ukáže s aktivací (`WindowImpl.UpdateWindowProperties`, větev `ShowInTaskbar` → `Hide()` / `Show(activate)`);
  při `WM_ACTIVATE` `WindowBase.HandleActivated` → `FocusManager.SetFocusScope` vrátí fokus zapamatovanému špendlíku →
  `StackPanel.windowButtons:focus-within` ukáže celou řadu bez ohledu na myš. Totéž po každém přepnutí zpět do okna
  (Alt+Tab). Návrh opravy: místo `Focus(null)` přesunout fokus na neutrální prvek (např. `Surface` s `Focusable="True"`,
  `IsTabStop="False"`, `Focus(NavigationMethod.Pointer)`), aby si rozsah pamatoval jeho, ne tlačítko; headless test:
  klik na špendlík, deaktivace a aktivace okna → řada skrytá.
- **B – po připnutí malého náhledu klávesnicí (Enter) se ztratí fokus klávesnice** (`SpaceAndEnterSwitchThePinAndTheFocusStaysOnIt`,
  „mini-enter: UIA focus '', Space does not switch the pin“; okno přitom zůstalo v popředí). Pravděpodobná příčina:
  připnutí skryje tlačítko na hlavním panelu tím, že Avalonia nastaví oknu skrytého vlastníka
  (`SetWindowLongPtr(GWLP_HWNDPARENT, OffscreenParentWindow)`), bez skrytí a bez nové aktivace; Windows přitom oknu
  vezme fokus (`WM_KILLFOCUS` → `TopLevel.PlatformImpl_LostFocus` zruší fokus Avalonie) a protože okno zůstane aktivní,
  žádné `WM_ACTIVATE` fokus nevrátí. Proč jen v malém náhledu (v normálním okně mezerník při stejném přechodu prošel), zatím
  nevím – rozhodne nová diagnostika (aktivní / fokusové HWND ve zprávě). Návrh opravy: v `CameraWindow.OnPropertyChanged`
  pro `TopmostProperty` (`CameraWindow.axaml.cs:131–137`) si před `ShowInTaskbar = …` zapamatovat `IsActive` a fokusovaný
  prvek a po změně (Dispatcher.Post) je-li okno aktivní a fokus pryč, `Activate()` a vrátit fokus tomu prvku (po kliku
  myší neutrálnímu prvku z opravy A). Obě chyby → nový úkol (zakládá hlavní agent).
- **Doplnění:** příčina B popsaná výše se ukázala jako chybná (připnutí fokus nebere, `WM_KILLFOCUS` nepřijde). Skutečnou
  příčinu i opravu obou chyb popisuje T-122.

### Zůstává ručně (nepovinné, doporučené – zapsáno v T-114)
- Subjektivní čitelnost ztlumeného špendlíku na světlém obraze kamery a při 200 % (test kontroluje jas a ukládá výřezy).
- Skutečný hlas Předčítání v češtině („Vždy navrchu: zapnuto / vypnuto“); test ověřuje jen to, co Předčítání čte (UIA).
- Vzhled obrysu nad skutečnou tapetou a Průzkumníkem (test používá vlastní bílé / tmavé okno).
- Obrys smí při tažení úchopem ztmavnout (systémová smyčka) – není chyba, netestuje se.

### Stav ověření
2026-10-31 (Windows 11 Pro 10.0.26300): `dotnet build` 0 varování, 0 chyb; `dotnet test` celý: 8927 testů, 0 selhání,
8653 prošlo, 274 přeskočeno (E2E projekt 320 / 147, nových 11 testů nalezeno a bez `LAYERPEEK_E2E=1` přeskočeno).
E2E (`run-e2e.ps1`) jsem nespouštěl – potřebuje volnou plochu, běh koordinuje hlavní agent s uživatelem:
`pwsh tools/run-e2e.ps1 -Class '*CameraPinTests' -NoSystemChanges`,
`pwsh tools/run-e2e.ps1 -Class '*CameraTaskbarAndModeSwitchTests' -NoSystemChanges`,
`pwsh tools/run-e2e.ps1 -Class '*CameraMiniOutlineTests'` (se změnou měřítka; s `-NoSystemChanges` se test měřítek
přeskočí) a kvůli změně `CoverWindow` regrese `-Class '*CameraTopmostTests'` a `-Class '*BringToFrontTests'`
(obě `-NoSystemChanges`).

**Konečné výsledky na skutečné ploše (hlavní agent, Windows 11 Pro 10.0.26300):** `CameraMiniOutlineTests` 3/3 i se změnou
měřítka 100 / 125 / 150 % (měřítko vráceno), `CameraTaskbarAndModeSwitchTests` 3/3, `CameraTopmostTests` 2/2,
`BringToFrontTests` 2/2 (2026-10-31); `CameraPinTests` 5/5 (2026-11-01, po opravě T-122 na této větvi, `d7b60a2`).
Review 2026-11-01 bez blokujících nálezů; `dotnet build` 0 varování, `dotnet test` 8932 testů, 0 selhalo.
Uzavřeno 2026-11-01.
