---
id: T-151
title: Okno kamery – automatické zavření a znovuotevření podle stavu tiskárny (App, nastavení, E2E)
status: done
owner: ui-dev
priority: P2
platform: all
depends_on: [T-150]
---

## Cíl
Napojit stavový automat z T-150 na skutečná okna kamery: automaticky zavřít okno vypnuté tiskárny a po jejím zapnutí ho otevřít na posledním místě. Přidat volbu do okna nastavení (nápad 2).

## Rozsah
- Pozorovatel v App (`IMonitoringObserver`, po vzoru `PrintNotificationObserver`) předává snapshoty automatu a vykonává jeho příkazy na UI vlákně.
- **Automatické zavření** = stejná cesta jako zavření okna uživatelem (uloží umístění podle `RememberLastPlacement`, ukončí odběr streamu), jen s příznakem „automaticky“, aby automat zavření nepovažoval za ruční.
- **Automatické otevření** = stejná cesta jako „Zobrazit kameru“ z tray menu (`CameraWindowOpenPolicy`, `CameraWindowPlanner`): poslední umístění, nepřipojený monitor podle `CameraWindowDefaultPlacement.Relocate`. Automaticky otevřené okno **nesmí krást fokus** (otázka 1) – na Windows bez aktivace (`ShowActivated = false` nebo ekvivalent), na macOS bez aktivace aplikace (pozor na T-102 / T-137: ikona v Docku a ⌘Tab).
- Tray menu („Zobrazit / Skrýt kameru“) odpovídá skutečnému stavu okna i po automatickém zavření / otevření.
- Volba v okně nastavení podle rozhodnutí z T-150 (otázka 5), texty ve všech jazycích.
- Chování s malým náhledem (vždy navrchu, všechny plochy na macOS) a s režimem rozložení (T-091): okno v režimu rozložení se automaticky nezavře (otázka 2).
- **E2E (Windows, UI Automation) proti simulátoru:** `POST /_simulator/offline` → po T₁ okno zmizí; `POST /_simulator/online` → okno se objeví na stejném místě; ručně zavřené okno se po `online` neotevře. Testy s krátkými časy přes testovací přepínač (jen Debug, jako T-067), aby E2E nečekal minutu.

## Akceptační kritéria
- [x] Vypnutí tiskárny (simulátor offline) okno zavře po debounce, zapnutí ho otevře na posledním místě (E2E). → přesunuto do T-125 (běh Windows E2E `CameraWindowAutoCloseTests`; na Macu ověřeno headless testy a E2E `MacCameraWindowAutoCloseE2ETests`)
- [x] Ručně zavřené okno se znovu neotevře (E2E). → přesunuto do T-125 (běh Windows E2E; headless ověřeno)
- [x] Automaticky otevřené okno nebere fokus aktivnímu oknu jiné aplikace (E2E na Windows; macOS → přesunout do T-127 podle `tasks/README.md`). → přesunuto do T-125 a T-127
- [x] Tray menu ukazuje správný text položky kamery po automatickém zavření i otevření (headless / E2E).
- [x] Volba v nastavení funguje, ukládá se a je ve všech jazycích; okno nastavení bez posuvníku. (macOS; snímky Windows → přesunuto do T-125)
- [x] Stávající E2E okna kamery (T-071, T-120) prochází. → přesunuto do T-125 (jen Windows)
- [x] `dotnet build` a `dotnet test` bez chyb a varování; Native AOT publish bez nových varování. (macOS `osx-arm64`; `win-x64` → přesunuto do T-125)

## Otevřené otázky
1. **Fokus a popředí:** má automaticky otevřené okno přijít do popředí (nad ostatní okna), nebo se jen tiše objevit (bez aktivace, případně pod aktivním oknem)? U okna „vždy navrchu“ se zobrazí navrchu vždy.
2. **Režim rozložení (T-091) a otevřené kontextové menu:** zavřít okno automaticky i během úprav rozložení, nebo odložit, dokud uživatel režim neukončí?
3. **Oznámení:** dát uživateli vědět (toast / tooltip), že okno bylo zavřeno, protože je tiskárna vypnutá? Nebo nic?

### Rozhodnutí uživatele 2026-11-03
1. Automaticky otevřené okno se objeví **tiše, bez aktivace** (Windows bez aktivace, macOS bez aktivace aplikace); okno „vždy navrchu“ je navrchu vždy.
2. V **režimu rozložení (T-091)** nebo s otevřeným kontextovým menu se automatické zavření **odloží**; po ukončení režimu / zavření menu se okno zavře, pokud je tiskárna stále `Offline` (déle než T₁).
3. **Bez oznámení** – žádný toast ani tooltip při automatickém zavření / otevření.
4. **Implementace primárně na macOS** (rozhodnutí uživatele 2026-11-03): úkol se dělá v session na Macu (implementace pro obě platformy, headless testy, ověření macOS části – tiché otevření bez aktivace aplikace, Dock, ⌘Tab). Windows E2E (UI Automation proti simulátoru) a ověření Windows části se dodělají v další session na Windows; pokud tam nebudou hotové, přesunou se do T-125 podle `tasks/README.md`.
(Rozhodnutí k T-150: T₁ 30 s, T₂ 10 s pevně, jen `Offline`, globální volba výchozí zapnuto – viz T-150.)

### Poznámky k zapojení z T-150 (review 2026-11-03)
- API: `CameraWindowAutoVisibility` (Core/Cameras), popis v poznámkách T-150; volat na UI vlákně, jeden časovač na `NextDueAt` → `Evaluate()`.
- `WindowClosedByUser` volat při **každém** zavření okna, které nevykonává příkaz `Close` (uživatel, odebraná tiskárna, vypnutá kamera, jiné zavření aplikací).
- Do `CameraWindowAutoVisibilityOptions.Monitor` předat skutečné `PrinterMonitorOptions` monitoru, jinak může být `StaleAfter` krátké.
- `SystemResumed()` volat z události probuzení systému (Windows `PowerModeChanged` / `WM_POWERBROADCAST`, macOS `NSWorkspaceDidWakeNotification`).
- Důsledek rozhodnutí 9: když se po probuzení Wi-Fi připojuje déle než 30 s, okno se oprávněně zavře podle čerstvých snímků `Offline`.

## Poznámky k implementaci
Implementováno na macOS (session 2026-11-03), kód pro obě platformy.

- **Pozorovatel:** `App/Cameras/CameraWindowAutoVisibilityObserver` (`IMonitoringObserver`, v seznamu `MonitoringSession`). Předává automatu z T-150 `ApplySettings` / `UpdateSettings` / `ReportMonitoringFailed` → `MonitoringStopped`, snímky `IPrinterMonitor.SnapshotUpdated` a probuzení systému → `SystemResumed`; vše se posílá na UI vlákno (`IUiDispatcher`). Jeden časovač na `TimeProvider` (`NextDueAt` → `Evaluate`). Příkazy: `store.SetAutoClosed(id, cmd.AutoClosed)`, pak `Close` → `CameraWindows.CloseAutomatically`, `Open` → `CameraWindows.ShowAutomatically`. Když se okno otevřít nedá (kamera mezitím vypnutá, ukončování), hlásí se `WindowClosedByUser`, takže se příznak zapomene a nic se neopakuje. Do logu jde řádek Information při každém automatickém zavření a otevření (jen id tiskárny).
- **Start:** automat vznikne, až je načtený `camera-windows.json` (příznaky z minula) **a** App připojí okna (`TrayHost.AttachCameraWindows` v `App.StartHost`). Do té doby si pozorovatel drží poslední nastavení a snímky zahazuje, takže odpočet začne dalším snímkem. Už otevřená okna nahlásí jako `WindowOpened`. Při ukončování `App.StopHostAsync` volá `DetachCameraWindows` dřív, než `CameraWindows.Shutdown` okna zavře, takže zavření při ukončení není „ruční zavření“ a příznaky zůstanou uložené.
- **Okna:** `CameraWindows` implementuje `ICameraWindowAutoTarget`. `CloseAutomatically` = `Window.Close()` jako zavírací tlačítko (umístění se uloží podle `RememberLastPlacement`, stream skončí), `OpenChanged` pak nese `Automatic = true`. Každé jiné zavření (uživatel, tray menu, ⌘W, vypnutá kamera, odebraná tiskárna) má `Automatic = false` → `WindowClosedByUser`. `ShowAutomatically` = stejná cesta jako „Zobrazit kameru“ (`CameraWindowOpenPolicy`, `CameraWindowPlanner`, poslední umístění), jen `ShowActivated = false` a bez `Activate()`, tedy bez `bringAppToFront` (macOS `MacAppActivation`) i bez `WindowsWindowActivator`. Tray menu sleduje `OpenChanged` jako dosud, text „Zobrazit / Skrýt kameru“ proto odpovídá i po automatickém zavření a otevření.
- **Odklad (rozhodnutí 2):** `CameraViewModel.DefersAutoClose` = režim rozložení (`IsLayoutModeActive`) nebo otevřené kontextové menu (`CameraWindow` hlásí `ContextMenu.Opened/Closed` přes `SetContextMenuOpen`). `CameraWindows` z toho dělá `CloseDeferralChanged` a `IsCloseDeferred`, pozorovatel volá `SetCloseDeferred`. Okno otevřené rovnou do režimu rozložení (T-092) se zjistí při otevření. Po skončení režimu / zavření menu se okno zavře hned, pokud je tiskárna stále offline déle než T₁.
- **Bez oznámení (rozhodnutí 3).** Žádný toast ani tooltip.
- **Chování podle platformy:**
  - *Windows:* okno se ukáže bez aktivace (`ShowActivated = false` → Avalonia `SW_SHOWNOACTIVATE`), popředí zůstane oknu jiné aplikace. Tlačítko na hlavním panelu se objeví jako dosud (T-043), u okna vždy navrchu ne (T-102). Probuzení: `Platform/Windows/WindowsSystemResumeEvents` (`PowerRegisterSuspendResumeNotification` s callbackem, `PBT_APMRESUMEAUTOMATIC` / `PBT_APMRESUMESUSPEND`, `[LibraryImport("powrprof.dll")]` + `UnmanagedCallersOnly`, bez okna). **Na Windows zatím neověřeno** (T-125). Okrajový případ: start v režimu celé obrazovky – Avalonia na Windows aktivuje zobrazené okno, jehož stav se změní (poznámka v `CameraWindows.Show`, T-040), takže takové okno může fokus vzít. Ověří T-125.
  - *macOS:* okno se ukáže bez `makeKeyAndOrderFront` a bez aktivace aplikace (Avalonia.Native `ShowActivated = false`). Normální okno (ne vždy navrchu) přepne aplikaci do Docku a ⌘Tab jako každé jiné okno (T-102, `AppPresence`), aktivní aplikace se ale nemění. Malý náhled vždy navrchu Dock nemění a je na všech plochách (T-038). Probuzení: `Platform/MacOS/MacSystemResumeEvents` (`NSWorkspaceDidWakeNotification` z `NSWorkspace.notificationCenter`, globální blok na hlavní frontě, registrace až po startu UI, bez načtení AppKitu v procesech bez něj). `ObjCRuntime.Send` má nové přetížení se 4 argumenty.
- **Nastavení:** zaškrtávátko `CameraWindowAutoClose` v podzáložce „Okno kamery“ pod tlačítky Obnovit výchozí / Použít, mimo pole tiskárny. Je proto použitelné i s vypnutou kamerou a Obnovit výchozí ho nemění (globální volba, rozhodnutí 5). Texty `SettingsCameraWindowAutoClose` a `SettingsCameraWindowAutoCloseHint` jsou ve všech 8 jazycích. Kvůli výšce podzáložky (`SettingsWindowLayoutTests`: bez posuvníku ani se všemi chybami velikosti) je nápověda „Platí pro všechny tiskárny…“ jen v tooltipu a v `AutomationProperties.HelpText`, zaškrtávátko má `MinHeight` 28 (Fluent 32) a mezera nad tlačítky Obnovit / Použít je 4 místo 8 DIP. Snímky macOS jsou přegenerované, snímky Windows ne (T-125).
- **Debug přepínače pro E2E (jen testovací režim):** `LAYERPEEK_TEST_CAMERA_CLOSE_AFTER_MS` a `LAYERPEEK_TEST_CAMERA_OPEN_AFTER_MS` (0–120 000 ms; `AppEnvironment.TestCameraCloseAfter` / `TestCameraOpenAfter` → `CameraWindowAutoVisibilityOptions`). Mimo testovací režim se ignorují a zalogují jako ostatní, v Release neexistují. `PrinterMonitorOptions` monitoru jsou výchozí, takže `Monitor` se nepředává.
- **Testy:**
  - `App.Tests/Cameras/CameraWindowAutoVisibilityObserverTests` (16): zavření po 30 s právě jednou, krátký výpadek, tiché znovuotevření, ruční zavření je konečné, odklad menu / režimu rozložení, příznak z minulého běhu, vypnutá volba, okno, které nejde otevřít, probuzení, start až po připojení oken, `Detach`, vlákna, `Dispose`.
  - `TestModeSwitchesTests` (přepínače), `AppServicesTests.CameraWindowAutoVisibilityIsWiredToMonitoring`, `CameraWindowSettingsTests` (volba, dirty, uložení, Obnovit výchozí ji nemění).
  - UI headless `Cameras/CameraWindowAutoCloseUiTests` (6, skutečné `CameraWindows` a tray menu): zavření jako zavírací tlačítko (stream stojí, umístění uložené, text „Zobrazit kameru“), znovuotevření na stejné pozici s `ShowActivated = false`, `IsActive = false` a bez volání aktivátoru i `bringAppToFront`, ruční zavření z tray menu je konečné, kontextové menu a režim rozložení zavření odloží, vypnutá volba, ukončování příznaky nemaže. `SettingsWindowCameraWindowUiTests` (volba použitelná s vypnutou kamerou, tooltip, uložení, pořadí Tab).
  - E2E macOS na vyžádání `App.Tests/Platform/MacOS/MacCameraWindowAutoCloseE2ETests` (`LAYERPEEK_E2E=1`, testovací `LayerPeek E2E.app`, simulátor tiskárny a RTSP kamery): **2026-11-03 na tomto Macu prošlo zavření (stream skončil, příznak uložen) i znovuotevření (stream znovu běží, příznak smazán) a ukončení s kódem 0.** Fokus test neověřil, protože obrazovka byla zamčená (vpředu `loginwindow`), proto test skončil jako „vynecháno“ (→ T-127).
  - E2E Windows `App.E2E.Tests/CameraWindowAutoCloseTests` (UI Automation, simulátor, `ForegroundHolder` s právem popředí pro aplikaci): napsáno a přeloženo na Macu, **na Windows zatím neběželo** (→ T-125).
- **Ověřeno na macOS:** `dotnet build` (0 varování) a celý `dotnet test` (0 chyb), skutečná Debug aplikace v E2E výše. `dotnet publish src/LayerPeek.App -c Release -r osx-arm64` projde bez varování trimmeru / AOT (IL); vypíše jen dosavadní hlášky nástrojů Xcode (dsymutil „duplicate name“, chybějící `ModuleCache` `.pcm`, `xcodebuild` s Command Line Tools). Release binárka se nespouštěla, protože testovací režim existuje jen v Debug.
- Nové balíčky: žádné.
- Uzavřeno 2026-11-03: zbývající ověření na Windows (E2E, snímky, AOT `win-x64`, probuzení, celá obrazovka) přesunuto do T-125, ruční ověření na macOS (fokus s odemčenou obrazovkou, Dock / ⌘Tab, probuzení) do T-127 (rozhodnutí uživatele 4 a `tasks/README.md`).
