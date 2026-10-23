---
id: T-071
title: E2E – okno kamery na monitorech (rohy, celá obrazovka, odpojený monitor, DPI), vždy navrchu a okna při 125/150 %
status: in-progress
owner: qa-reviewer
priority: P2
platform: windows
depends_on: [T-065, T-066]
---

## Cíl
Ověřit automaticky ruční kontroly T-035 (monitor, rohy, celá obrazovka, odpojený monitor, názvy monitorů, okno neposkočí, mezera 16 DIP při 125/150 %), T-037 (skutečné pořadí oken nad jinou aplikací) a T-041 (Diagnostika při 125/150 % a v tmavém motivu). Staví na `CameraWindowTests` a `SettingsAndDiagnosticsTests` z T-044 a na zásazích T-065/T-066.

## Pokrytí
| Úkol | Kontrola | Test (návrh názvu) |
|---|---|---|
| T-035 | otevření na zvoleném monitoru v rohu (normální okno, malý náhled) | `CameraOpensInEveryCornerOfTheChosenMonitor` (5 umístění × 2 režimy, druhý monitor, okno pokaždé znovu otevřené) |
| T-035 | celá obrazovka na zvoleném monitoru | `FullScreenStartModeUsesTheChosenMonitor` |
| T-035 | odpojený monitor → hlavní, „nepřipojený“ | `MissingMonitorFallsBackToThePrimary` (neexistující monitor v `settings.json`, bez zásahu) + `DisconnectedMonitorFallsBackAndComesBack` (`MonitorDisconnectChange`) |
| T-035 | skutečné názvy monitorů v nastavení | `SettingsListsTheRealMonitorNames` |
| T-035 | 2 stejné monitory: dvě položky, otevře se na zvoleném | `TwoIdenticalMonitorsAreTwoChoices` (jen se 2 stejnými monitory, jinak přeskočeno) |
| T-035 | okno při otevření neposkočí | `CameraWindowDoesNotJumpWhenItOpens` (100/125/150 %) |
| T-035 | mezera přesně 16 DIP při 125/150 % | `CornerMarginIs16DipAtEveryScale` |
| T-037 | normální okno / náhled zůstane nad jinou aplikací, po vypnutí volby ne (bez zavření okna) | `TopmostKeepsTheCameraAboveAnotherApp` |
| T-037 | celá obrazovka nikdy navrchu | `FullScreenIsNeverAboveAnotherApp` |
| T-041 | bez posuvníku při 125/150 % (8 jazyků, hlavní stavy) | `DiagnosticsAndSettingsFitAtEveryScale` |
| T-041 | tmavý motiv, Esc a Tab skutečnou klávesnicí | `DiagnosticsInTheDarkThemeWithTheKeyboard` |

## Rozsah
- **Monitory:** test si sám zjistí monitory (`EnumDisplayMonitors`, `QueryDisplayConfig` + `DisplayConfigGetDeviceInfo` pro přátelský název a `monitorDevicePath`) a podle nich sestaví `camera.window.monitor` v `settings.json`. Testy, které potřebují 2 monitory, se bez nich přeskočí s důvodem. Tento počítač: 2560×1440 + 1440×2560 na výšku, 2× DELL U2719D.
- **Rohy:** pro každé umístění (4 rohy + střed) a režim (normální, malý náhled) zapsat nastavení, otevřít okno příkazem `camera:<id>`, ověřit viditelný rámeček (`DWMWA_EXTENDED_FRAME_BOUNDS`) 16 DIP od okrajů pracovní plochy zvoleného monitoru (střed: vycentrováno), zavřít. Toleranci převzít z T-044 (±2 px).
- **Celá obrazovka:** počáteční režim Celá obrazovka + druhý monitor → okno = celé `rcMonitor` druhého monitoru. Esc → normální okno na tomtéž monitoru.
- **Neexistující monitor:** `settings.json` s monitorem „E2E Missing Monitor“ (+ neexistující `devicePath`) → okno na primárním monitoru. V nastavení na podzáložce Okno kamery je volba „… – nepřipojený“ (UIA text v 8 jazycích podle `.resx`).
- **Skutečně vypnutý monitor (trait `SystemChanges`):** zvolený druhý monitor, aplikace běží. `MonitorDisconnectChange` → otevřít kameru → primární monitor, nastavení ukazuje „nepřipojený“. Po návratu monitoru (nové otevření okna nastavení, seznam se čte jednou za okno, T-035 Q10) je monitor zase připojený a kamera se otevře na něm.
- **Názvy:** položky výběru monitoru (UIA) = „Primární monitor“ + názvy z EDID zjištěné testem (formát podle `.resx`). V AOT průchodu (T-073) totéž ověří `MonitorNameNative` v Native AOT.
- **Neposkočí:** `SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_LOCATIONCHANGE)` mimo proces pro PID aplikace. Od prvního zobrazení okna kamery do ustálení (1 s) se poloha ani velikost nezmění (0 událostí změny polohy okna kamery po `SHOW`). Pro normální okno i náhled při 100, 125 a 150 % (`DisplayScaleChange` na monitoru okna).
- **16 DIP při 125/150 %:** stejné jako rohy, s `DisplayScaleChange` na zvoleném monitoru; DIP podle `GetDpiForMonitor`.
- **Vždy navrchu (z-order):**
  - pomocné okno **jiného procesu**: okno vytvořené testovacím procesem (vlastní vlákno se smyčkou zpráv, `CreateWindowEx`, výrazná barva, stejný obdélník jako okno kamery). Test ho přesune do popředí (`SetWindowPos(HWND_TOP)` + aktivace; když `SetForegroundWindow` nejde, klik skutečnou myší do pomocného okna);
  - volba zapnutá: v bodě uprostřed okna kamery `WindowFromPoint` vrátí okno kamery a v pořadí `EnumWindows` je kamera nad pomocným oknem; snímek obrazovky ukazuje obraz kamery, ne barvu pomocného okna;
  - v **otevřeném** okně vypnout volbu (nastavení → Okno kamery → zaškrtávací pole → Uložit, okno kamery se nezavírá), znovu aktivovat pomocné okno → nahoře je pomocné okno;
  - totéž pro malý náhled (výchozí zapnuto);
  - celá obrazovka: pomocné okno aktivované nad kamerou zůstane nahoře;
  - pomocné okno se na konci zavře (i po neúspěšném ověření).
- **Diagnostika a nastavení při 125/150 %:** existující kontroly „bez posuvníku“ (`WindowChecks.AssertNoScrolling`, Segoe UI) pro Diagnostiku (tiskne, offline, bez tiskáren) a hlavní podzáložky nastavení v 8 jazycích při 125 % a 150 % primárního monitoru. Okno 780×508 DIP se při 150 % vejde na 2560×1440. Pokud by se nevešlo (malý monitor), ověří se přizpůsobení pracovní ploše.
- **Tmavý motiv (T-065, `AppsUseLightTheme = 0`):** Diagnostika a nastavení v tmavém motivu: pozadí okna je tmavé (snímek), žádný text se neořízne (kontroly z T-044), Esc zavře Diagnostiku, Tab prochází výběr tiskárny → rozsah → Zavřít (skutečné klávesy přes `SendInput`).

## Akceptační kritéria
- [ ] Testy z tabulky Pokrytí procházejí; testy pro 2 monitory na tomto počítači neskončí přeskočením
- [ ] Neexistující i skutečně vypnutý monitor → primární, „nepřipojený“ v nastavení; po návratu monitoru znovu na něm
- [ ] Okno kamery neposkočí při 100/125/150 % (WinEvent hook)
- [x] Z-order nad oknem jiného procesu odpovídá volbě, i po změně v otevřeném okně; celá obrazovka nikdy navrchu (`CameraTopmostTests`, 2026-10-22)
- [ ] Diagnostika a nastavení bez posuvníku v 8 jazycích při 125 % a 150 %; tmavý motiv; Esc/Tab klávesnicí
- [ ] Měřítko, motiv a monitory jsou po běhu jako před ním (T-065/T-066)
- [ ] Ruční kontroly T-035, T-037 a T-041 (kromě „celkového dojmu“) zaškrtnuté podle výsledků (T-037 hotovo 2026-10-22, T-035 a T-041 čekají na běh se zásahy)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; `tools/run-e2e.ps1` zelené

## Otevřené otázky
- (okna jiných aplikací při vypnutí monitoru rozhodnuta v T-066, 2026-10-22)
- **Q1 – „neposkočí“ a oznámení beze změny polohy:** malý náhled dostane přibližně 80 ms po `EVENT_OBJECT_SHOW` jednu událost `EVENT_OBJECT_LOCATIONCHANGE` se **stejným** obdélníkem. Vzniká nejspíš tím, že `CameraWindow.OnOpened` volá `ApplyMode(Mini)` a znovu nastaví `WindowDecorations`/`Topmost`, takže Avalonia zavolá `SetWindowPos` bez posunu. Test teď za skok považuje jen událost se změněným obdélníkem. Oznámení se stejným obdélníkem jen zapíše do zprávy. Doslovné znění rozsahu („0 událostí změny polohy po SHOW“) by test shodilo pro všech 5 umístění náhledu. Mám to tak nechat, nebo má aplikace zbytečné `ApplyMode` po otevření vynechat (úprava aplikace, ui-dev) a test být přísný? Omezení: obdélník se čte až při doručení události (hook mimo proces), takže posun tam a zpět v rámci jednoho doručení by nebyl vidět.
- **Q2 – „žádný text se neořízne“:** E2E testy T-044 oříznutí textu nekontrolují, kontrolují jen posuvníky. Test proto ověřuje, že nic neroluje a každý text z UIA leží celý v klientské oblasti okna. Zkrácení „…“ uvnitř `TextBlock` UI Automation neprozradí, to dál hlídají headless testy (Inter). Stačí to?

## Poznámky k implementaci
- **Kód** (vše v `tests/PrusaTrayDiagnostic.App.E2E.Tests`, žádný nový NuGet balíček, interop přes `[LibraryImport]`):
  - `Infrastructure/TestMonitors.cs`: monitory, které si test zjistí sám (`EnumDisplayMonitors` + `GetMonitorInfo`, přátelský název a `monitorDevicePath` z `QueryDisplayConfig`, DPI z `GetDpiForMonitor`). Jsou seřazené jako v aplikaci (zleva doprava, pak shora dolů). `CameraMonitorJson()` vrací objekt `camera.window.monitor` (název, rozlišení, cesta). `ExpectedChoices` / `NotConnectedChoice` vracejí očekávané položky výběru monitoru podle `.resx` (stejná pravidla jako `PrinterEditorViewModel.CreateMonitorOptions`).
  - `Infrastructure/AppStrings.cs`: texty aplikace čtené přímo ze `Strings*.resx` ve worktree (testy aplikaci nereferencují).
  - `Infrastructure/CameraPlacement.cs`: očekávané umístění (roh / střed, okraj 16 DIP podle DPI monitoru, zaokrouhlení od nuly, tolerance z T-044: roh ±2 px, střed ±4 px) a sekce `camera.window` pro `settings.json` (pamatování vypnuté, aby každé otevření použilo přesně zadané hodnoty).
  - `Infrastructure/CameraWindowSettings.cs`: aplikace s jednou tiskárnou a zvolenou sekcí `camera.window`, podzáložka Okno kamery, výběr monitoru (nový `Ui.ComboItemsAsync`: všechny položky a vybraná), uložení / zrušení, čtení uloženého `settings.json`.
  - `Infrastructure/WinEventRecorder.cs`: `SetWinEventHook(EVENT_OBJECT_SHOW … EVENT_OBJECT_LOCATIONCHANGE)` mimo proces pro PID aplikace, na vlastním vlákně se smyčkou zpráv. Zapisuje jen události okna (`OBJID_WINDOW`, `CHILDID_SELF`) s obdélníkem okna. `MovesAfterShow` vrací všechna oznámení po prvním `SHOW`, `JumpsAfterShow` jen ta se změněným obdélníkem (viz Q1).
  - `Infrastructure/CoverWindow.cs`: „jiná aplikace“ pro test vždy navrchu. Je to okno testovacího procesu (vlastní vlákno se smyčkou zpráv, `CreateWindowEx`, `WS_POPUP`, purpurové, ne navrchu, bez tlačítka na panelu), nikdy okno uživatele. Má obdélník okna kamery zvětšený o 48 px na každou stranu a oříznutý na monitor, aby zůstal pruh, na který jde kliknout, i když je kamera navrchu (odchylka od „stejný obdélník“). `BringToFrontAsync`: `SetWindowPos(HWND_TOP)` + `SetForegroundWindow`. Když Windows popředí odmítne, klikne skutečná myš do místa okna, které nic nezakrývá (kontrola `WindowFromPoint`). `IsAbove` porovná pořadí z `EnumWindows`. Okno se vždy zavře (`using`).
  - `Infrastructure/ScreenArea.cs`: snímek malé oblasti obrazovky (`CopyFromScreen`). Pojistka: oblast musí ležet na jednom monitoru a zabrat nejvýš 25 % jeho plochy (rozhodnutí uživatele). Testy snímají jen 64 × 64 px uprostřed okna kamery. **Překryv s T-069** (`CaptureScreenArea`): po sloučení obou úkolů sjednotit do jedné pojistky.
  - `Infrastructure/WindowActivation.cs`: aktivace okna aplikace kliknutím skutečnou myší (titulek, bez titulku střed; jen když tam okno není zakryté) pro klávesy přes `SendInput`. `FollowsCycle` ověřuje pořadí Tab.
  - `Infrastructure/LayoutRecorder.cs`: posuvníky a texty mimo klientskou oblast se zapisují po stavech a ověří se na konci testu. Používá ho i `SettingsAndDiagnosticsTests` (dřív tam byly soukromé metody, chování se nemění).
- **Testy** (tabulka Pokrytí):
  - `CameraMonitorTests` (T-035): `CameraOpensInEveryCornerOfTheChosenMonitor` (5 umístění × normální okno / náhled na druhém monitoru, každý případ s novou aplikací, navíc kontrola skoku), `FullScreenStartModeUsesTheChosenMonitor` (celá obrazovka = `rcMonitor` druhého monitoru, ne navrchu, Esc skutečnou klávesnicí → normální okno uprostřed téhož monitoru), `MissingMonitorFallsBackToThePrimary` (8 jazyků: primární monitor, výběr = všechny monitory + „… – nepřipojený“, vybraný), `SettingsListsTheRealMonitorNames` (8 jazyků, přesný seznam položek), `TwoIdenticalMonitorsAreTwoChoices` (2× DELL U2719D: dvě položky, uložení přes UI zapíše `devicePath` zvoleného monitoru, kamera se otevře na něm, pro oba monitory), `CameraWindowDoesNotJumpWhenItOpens` (současné měřítko), `CameraWindowDoesNotJumpWhenItOpensAtEveryScale` (`DisplayScale`, 100/125/150 % zvoleného monitoru), `CornerMarginIs16DipAtEveryScale` (`DisplayScale`, 125/150 % × 5 umístění × 2 režimy, také bez skoku), `DisconnectedMonitorFallsBackAndComesBack` (`MonitorOff`: kamera na primárním, „nepřipojený“, po návratu nové okno nastavení s připojeným monitorem a kamera zase na něm; okna ostatních aplikací zpět jako v T-066). Normální okno má v testech velikost 640 × 360 DIP. Výchozích 960 × 540 DIP je při 150 % 1440 px, tedy celá šířka monitoru na výšku, a okraje 16 DIP by se nevešly.
  - `CameraTopmostTests` (T-037): `TopmostKeepsTheCameraAboveAnotherApp` (normální okno se zapnutou volbou nad pomocným oknem, vypnutí v otevřeném okně (Uložit, okno zůstane otevřené) → pomocné okno nahoře, totéž pro malý náhled), `FullScreenIsNeverAboveAnotherApp` (obě volby zapnuté, celá obrazovka pod aktivovaným pomocným oknem). Každý stav se ověřuje třikrát: `WindowFromPoint` uprostřed okna kamery, pořadí `EnumWindows` a podíl purpurové ve snímku 64 × 64 px (< 20 % / > 90 %).
  - `AuxiliaryWindowScaleAndThemeTests` (T-041): `DiagnosticsAndSettingsFitAtEveryScale` (`DisplayScale`, primární monitor 125/150 %, 8 jazyků: nastavení (4 podzáložky tiskárny + Obecné, 2 tiskárny) a Diagnostika (tiskne, offline), bez tiskáren (Diagnostika s hláškou, Tiskárny). DPI okna musí odpovídat měřítku a velikost 780 × 508 DIP, nebo přizpůsobení pracovní ploše), `DiagnosticsInTheDarkThemeWithTheKeyboard` (`Theme`, tmavý motiv: medián jasu klientské oblasti ≤ 110 jako u tmavého panelu T-065, bez posuvníku, texty v okně, Tab skutečnou klávesnicí prochází cyklus výběr tiskárny → rozsah → Zavřít, Esc zavře Diagnostiku, nastavení tmavé a bez posuvníku).
  - `MonitorWindowCheckTests` (47 jednotkových testů, běží v každém `dotnet test`, nic nemění): okraj podle DPI, tolerance rohů a středu, sekce `camera.window`, očekávané položky výběru monitoru ve všech 8 jazycích, řazení monitorů, nepřipojený monitor, detekce skoku (i oznámení beze změny), pojistka 25 % a monitoru, podíl barvy, geometrie pomocného okna, pořadí Tab.
- **Ověřeno 2026-10-22 (bez zásahů do systému):** `dotnet build` 0 varování, 0 chyb. `dotnet test` 4881 testů, 0 selhalo, 87 přeskočeno (E2E bez `PTD_E2E` a dřívější). `tools/run-e2e.ps1 -NoSystemChanges -Class '*CameraMonitorTests'`: 23 testů, 0 selhalo, 3 přeskočené (zásahy). `-Class '*CameraTopmostTests'`: 2 testy, 0 selhalo. Celý `tools/run-e2e.ps1 -NoSystemChanges`: 248 testů, 21 přeskočeno (zásahy, kanár, skutečná kamera, vstupy podřízeného procesu), 1 selhal: známý nestabilní `LogAndSupportTests.SaveDiagnosticBundleThroughTheRealSaveDialog` (`COMException: Operation timed out` v UIA, viz Review T-065). Samostatně hned prošel. Před i po běhu „nothing pending“. T-037 je díky tomu kompletní a je přesunutý do `done`. Tento počítač: DISPLAY1 2560×1440 primární, DISPLAY2 1440×2560 na výšku na pozici 2560,0, oba „DELL U2719D“ 100 %. Zprávy ve výsledcích: `camera-corners-report`, `camera-jump-report`, `camera-topmost-report` a další.
- **Čeká na řízený běh se zásahy (spouští uživatel):** `CameraWindowDoesNotJumpWhenItOpensAtEveryScale`, `CornerMarginIs16DipAtEveryScale`, `DiagnosticsAndSettingsFitAtEveryScale` (`DisplayScale`), `DisconnectedMonitorFallsBackAndComesBack` (`MonitorOff`), `DiagnosticsInTheDarkThemeWithTheKeyboard` (`Theme`). Rizika: aplikace se při startu nemusí řídit tmavým motivem systému (test to ukáže), okno nastavení / Diagnostiky se musí otevřít na primárním monitoru (test ověří DPI okna), po návratu monitoru se okno nastavení otevře až 3× (Avalonia se o monitoru dozví se zpožděním).
