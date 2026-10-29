---
id: T-099
title: macOS – sledování tiskáren a notifikace během čekání na displej
status: in-progress
owner: ui-dev
priority: P2
platform: macos
depends_on: [T-096, T-020]
---

## Cíl
Když aplikace na macOS startuje se zhasnutým displejem, T-096 odkládá start Avalonie, dokud se displej nerozsvítí (bez časového limitu). Po tu dobu se dnes tiskárny nesledují a nepřijdou ani notifikace, protože composition root vzniká až v `App`. Uživatel rozhodl (2026-10-29), že sledování a notifikace mají běžet i během čekání; UI (menu bar, okna) se připojí po probuzení displeje.

## Rozsah
- Rozdělit start tak, aby monitoring (Core), nastavení, tajné údaje (Keychain) a notifikace (T-020, `UNUserNotificationCenter`) šly spustit bez Avalonie, a UI se k již běžícím službám připojilo později. Bez druhé inicializace Avalonie.
- Hlavní vlákno: čekání na displej nesmí blokovat zpracování notifikací ani kliknutí na ně (run loop / dispatch hlavní fronty), Avalonia musí po probuzení vzniknout na hlavním vlákně.
- Kliknutí na notifikaci během čekání: chování jako při kliknutí, které aplikaci spustilo (otevře web tiskárny).
- Jediná instance, signály (SIGTERM/SIGINT) a úklid fungují i v režimu čekání.
- Windows beze změny.
- Izolace testů podle `docs/architecture.md` („Izolace testů na macOS“).

## Akceptační kritéria
- [x] Během čekání na displej běží sledování tiskáren a doručují se notifikace (test s falešným displejem / falešnou platformou notifikací)
- [ ] Po probuzení displeje se objeví ikona v menu baru se stavem odpovídajícím průběžnému sledování – implementováno a pokryto testy (tray se připojí ke stejným instancím, prezentéry při vytvoření vykreslí aktuální stav view modelů); skutečný menu bar po probuzení displeje čeká na ověření uživatelem
- [x] Start s rozsvíceným displejem se chová stejně jako dosud (testy + AOT smoke test s probuzeným displejem)
- [ ] Skutečný start se zhasnutým displejem ověřen uživatelem (postup v T-096, doplněk níže)
- [x] Windows beze změny (z kódu: čekání a `EarlyServices` jen za `OperatingSystem.IsMacOS()`, běžný start ve stejném pořadí; na Windows nespuštěno)
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. Druhý start aplikace („activate“, např. dvojklik na aplikaci) během čekání na displej se teď **ignoruje** (zapíše se do logu). Alternativa: podržet ho a po probuzení displeje otevřít naposledy použité okno / Nastavení – to by ale mohlo okno otevřít i hodiny po pokusu. Ponechat ignorování?
2. Má se větev čekání ověřovat i automaticky skutečnou aplikací? Návrh: Debug přepínač testovacího režimu (např. „předstírat N s bez displeje“), aby E2E test spustil Debug aplikaci v testovacím režimu, ověřil sledování simulované tiskárny během čekání a pak start UI – bez uspání displeje. Není v rozsahu, nová testovací infrastruktura.

## Poznámky k implementaci

### Návrh
- **Rozdělení kompozice:** nový `Composition/ServiceHost` vlastní kontejner služeb (nastavení, tajné údaje/Keychain, `MonitoringSession`, detekce událostí, notifikace, proxy webu tiskárny, úlohy na pozadí při startu) a nepotřebuje žádný objekt UI. `CreateAsync` = kontejner + načtení stavu oken + nastavení (mimo UI vlákno) + použití jazyka; `StartMonitoring` = start sezení, toastů (Windows), načtení stavu oken kamer, obrázky tiskáren (jednou; po zastavení nic). `StopAsync` = dřívější nepoužitá část `TrayHost.StopAsync` (stejné pořadí čekání a úklidu, testovací úklidy notifikací/toastů přesunuty sem). `TrayHost` drží jen objekty UI: `TrayHost.Attach(core, trayIcon, …)` je vytvoří nad běžícími službami a sledování spustí, jen když ještě neběží – druhé sezení nikdy nevznikne (druhý `Start` by vyhodil výjimku). `TrayHost.StartAsync` (běžný start) = `CreateAsync` + `Attach` v původním pořadí (tray vznikne před startem sledování). Core se nemění; vrstvy zůstávají.
- **UI vlákno před Avalonií:** služby posílají práci na UI vlákno přes `IUiDispatcher` (view modely tray, `UiLanguageObserver`, náhledy…); `Dispatcher.UIThread` přímo používají jen okna, `TemperatureChart`, `TestCrash` a `AvaloniaUiDispatcher`, které před Avalonií nevznikají. `AppServices.Create` proto přijímá volitelný `uiDispatcher`. Před Avalonií je to `Tray/StartupUiDispatcher` (fronta pro hlavní vlákno + probuzení), k tomu `StartupSynchronizationContext` hlavního vlákna, takže pokračování `await` v `ServiceHost.CreateAsync` (`ConfigureAwait(true)`) se vrací na hlavní vlákno jako na UI vlákno Avalonie. `App.OnFrameworkInitializationCompleted` jako první zavolá `AttachTo(new AvaloniaUiDispatcher())`: co čekalo, proběhne hned na stejném (hlavním) vlákně, další práce jde do dispatcheru Avalonie, pořadí se zachová. Objekty služeb tak mají celou dobu jedno UI vlákno.
- **Hlavní vlákno během čekání:** místo blokující pauzy `Platform/StartupLoop.Pause` střídá práci z fronty a `CFRunLoopRunInMode` hlavního vlákna (`Platform/MacOS/MacStartupRunLoop`: výchozí režim, vlastní `CFRunLoopSource` verze 0, signál + `CFRunLoopWakeUp` z libovolného vlákna; signál se neztratí ani před během smyčky, na rozdíl od `CFRunLoopStop`). Běh run loopu hlavního vlákna obsluhuje i hlavní dispatch frontu, kam mohou chodit zpětná volání `UNUserNotificationCenter`. Interop: `[LibraryImport]` s absolutní cestou ke CoreFoundation, blittable, statický `[UnmanagedCallersOnly]` callback, `kCFRunLoopDefaultMode` přes `NativeLibrary.GetExport` – AOT safe, bez reflexe. Kdyby zdroj nešel vytvořit, `ManagedStartupRunLoop` obsluhuje jen frontu (zapíše se varování). Kontrola displeje zůstává každou 1 s, bez časového limitu; `StartupDisplayWait` se nemění (jen dostane jinou pauzu).
- **Orchestrace:** `Composition/DisplayWaitStartup<TServices>` (obecná kvůli testům): nainstaluje kontext synchronizace, při první kontrole bez displeje spustí služby (`Program.StartServicesAsync` = `CreateAsync` + `StartMonitoring` na hlavním vlákně), čeká, po probuzení vrátí běžící službu (`EarlyServices` → `App`), při zrušení (SIGTERM/SIGINT, testovací „quit“) služby zastaví (`ServiceHost.StopAsync(3 s)`, celkem nejdéle 5 s jako Quit v menu) a `Main` vrátí 0 (log se zapíše, guard uvolní). Selhání startu služeb ukončí čekání jejich výjimkou (pád do logu), jako dřív selhání startu UI. S aktivním displejem se nic nespouští – běžný start je beze změny (`EarlyServices` je `null`, `App` vytvoří služby jako dřív).
- **Pořadí v `Program.Main`:** centrum notifikací (`OpenMacNotifications`) se otevírá **před** čekáním (dřív po něm): služby spuštěné během čekání ho potřebují (`MacUserNotificationCenter.Shared`) a delegát musí existovat před dokončením spuštění AppKitu – to platí dál.
- **Příkazy instancí:** `InstanceCommandQueue.AttachProvisional` (nové) – během čekání je obsluhuje `SingleInstance/WaitingInstanceCommands`: klik na oznámení → `ServiceHost.ActivateToast` (počká na dokončení startu služeb) = web tiskárny jako při kliknutí, které aplikaci spustilo; „activate“ druhého startu se ignoruje (otázka 1); Debug testovací „quit“ ukončí čekání. `App` ji pak běžným `Attach` nahradí; `Deliver` nově doručí příkaz obsluze platné v okamžiku doručení, takže nic nepropadne ani se nedoručí dvakrát (dřív se obsluha neměnila, chování je stejné).
- **Jediná instance:** guard běží před čekáním jako dřív; druhý start předá „activate“ a skončí.
- **Windows:** čekání, `MacStartupRunLoop` a `EarlyServices` jsou jen za `OperatingSystem.IsMacOS()`; `App` na Windows volá `TrayHost.StartAsync` ve stejném pořadí jako dřív. Jediná viditelná změna: kategorie logu hlášek o ukončení se změnila z `TrayHost` na `ServiceHost`.
- **AOT/licence:** žádný nový balíček; vše `[LibraryImport]`/`UnmanagedCallersOnly`.

### Testy
- `Composition/DisplayWaitStartupTests` (fakes, všechny OS): falešný displej se „probudí“, až falešné sledování na pozadí třikrát aktualizovalo tray přes dispatcher a skutečná `UserNotificationService` doručila tři oznámení do falešného centra; ověřeno, že pokračování startu i práce UI běží na čekajícím vlákně, služby se spustily jednou, výsledek je ta samá instance a po `AttachTo` jde další práce téhož sledování do „Avalonie“; aktivní displej hned = nic se nespustí, fronta prázdná, žádné oznámení; quit během čekání zastaví tutéž instanci; zastavení se čeká jen do limitu; selhání startu se propaguje; kontext synchronizace se vždy obnoví.
- `Composition/ServiceHostTests` (skutečný kontejner v testovacím režimu, dočasná složka, bez tiskáren, bez sítě): služby na `StartupUiDispatcher` → práce UI čeká na hlavní vlákno → `AttachTo` → `TrayHost.Attach` používá tytéž instance (`MonitoringSession`, `TrayStatusViewModel`) bez druhého startu; běžný start; zastavení služeb, které nesledovaly.
- `Tray/StartupUiDispatcherTests`, `Platform/StartupLoopTests` (falešný run loop na `FakeTimeProvider`, `ManagedStartupRunLoop`), `Platform/MacOS/MacStartupRunLoopTests` (skutečný CFRunLoop na vlastním vlákně: probuzení z jiného vlákna, probuzení před během, timeout, dispose; jen čte, na jiných OS přeskočeno), `SingleInstance/InstanceCommandQueueTests` (prozatímní obsluha), `SingleInstance/WaitingInstanceCommandsTests`.

### Ověření (macOS 26 / Darwin 27, arm64, 2026-10-29)
- `dotnet build` 0 varování, 0 chyb; `dotnet test` celkem 7471, 0 selhání, 7293 úspěšných, 178 přeskočeno.
- `dotnet publish src/LayerPeek.App -c Release -r osx-arm64 -warnaserror`: bez varování trim/AOT; jen obvyklé hlášky linkeru/dsymutil/xcodebuild toolchainu.
- `tools/profile-snapshot.ps1 -Save` před testy, `caffeinate -d -u pwsh scripts/aot-smoke.ps1 -Runtime osx-arm64 -PublishDir <publish> -Seconds 15`: PASSED, `UIElement`; `-Compare` po testech a smoke testu: beze změny (Login Items nečitelné, nekontrolováno). Displej byl probuzený, větev čekání se ve skutečné aplikaci neprovedla.
- **Neověřeno lokálně** (vyžaduje uspaný displej, který agent nesmí vyvolat): skutečné sledování a oznámení během čekání v Release aplikaci, doručení kliknutí na oznámení bez běžícího `NSApplication` (callback delegáta přes hlavní dispatch frontu je předpoklad, ne ověření; když macOS doručí klik až po dokončení spuštění AppKitu, zpracuje ho běžná obsluha `App` – i tak otevře web), dotaz macOS na povolení oznámení, pokud první oznámení přijde během čekání (dialog/banner nemusí být vidět; služba na odpověď čeká až 10 min jako dřív), chování LaunchServices při dlouhém startu bez `NSApplication`, start Avalonie po probuzení na hlavním vlákně po běhu `CFRunLoop`.

### Ověření uživatelem (doplněk k postupu v T-096)
1. Postup T-096 body 1–3 (sestavit Release `LayerPeek.app`, `sleep 10; open -a …`, uspat displej `pmset displaysleepnow`), mít nastavenou aspoň jednu tiskárnu a během ~1–2 min spánku displeje nechat proběhnout událost tisku (např. pozastavit/dokončit tisk, nebo simulátor `tools/PrusaSimulator`).
2. Očekávání: oznámení přijde ještě se zhasnutým displejem (vidět po probuzení v Centru oznámení); v `~/Library/Logs/LayerPeek/` věty „No active display …“, „Starting printer monitoring and notifications while waiting for an active display.“, po probuzení „… starting the UI.“ a „The menu bar item attaches to the monitoring that already runs.“; ikona v menu baru hned ukazuje stav tiskárny (ne „Spouštění…“).
3. Volitelně (přes SSH, displej dál uspaný): `pkill -TERM LayerPeek` během čekání → v logu „Asked to quit …“, proces skončí s kódem 0 do ~5 s. Kliknutí na oznámení během čekání ručně ověřit prakticky nejde (kliknutí displej probudí); pokrývají ho testy.
