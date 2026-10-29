---
id: T-096
title: macOS – start aplikace bez aktivního displeje
status: in-progress
owner: ui-dev
priority: P2
platform: macos
depends_on: [T-019]
---

## Cíl
Aplikace na macOS nesmí spadnout, když se spouští ve chvíli, kdy není aktivní žádný displej. Při ověřování T-019 (2026-10-29) Release exe při startu skončil chybou `Avalonia.Native was not able to start the RenderTimer. Native error code is: -6661` (`kCVReturnInvalidDisplay`), protože displej byl uspaný. Smoke testy proto běží pod `caffeinate -d -u`.

Kdy to hrozí: „Otevřít při přihlášení“ (T-019) na Macu se zavřeným víkem bez monitoru, přihlášení přes Sdílení obrazovky, start během spánku displeje.

## Rozsah
- Reprodukovat (displej uspaný → start Release `LayerPeek.app`) a zjistit, kde přesně Avalonia.Native selže (`CVDisplayLink` při inicializaci platformy).
- Ošetřit v aplikaci, např. počkat na dostupný displej a start zopakovat, nebo odložit inicializaci renderování; pád bez hlášení není přijatelný.
- Pokud jde o chybu Avalonie, připravit hlášení do Avalonie (minimální reprodukce) a do té doby obejít v aplikaci.
- Záznam do `docs/troubleshooting.md`.
- Izolace testů podle `docs/architecture.md` („Izolace testů na macOS“); start Release aplikace jen přes `scripts/aot-smoke.ps1` se zálohou a obnovou profilu.

## Akceptační kritéria
- [ ] Start s uspaným displejem nezpůsobí pád; aplikace se po probuzení displeje objeví v menu baru (ověřeno) – implementováno a pokryto testy s fakes; skutečný start s uspaným displejem čeká na ověření uživatelem (postup níže)
- [x] Smoke test (`aot-smoke.ps1`) projde i bez `caffeinate`, nebo je důvod zdokumentovaný
- [x] Windows beze změny chování (z kódu: vše za `OperatingSystem.IsMacOS()`; na Windows nespuštěno)
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné; vzniklo rozhodnutím 2026-10-29 k otázce 8 v T-019)

## Poznámky k implementaci

### Příčina (Avalonia 12.1.4, ověřeno ve zdrojích tagu `12.1.4`, commit `2d79ba5`)
- `AvaloniaNativePlatform.DoInitialize` (`src/Avalonia.Native/AvaloniaNativePlatform.cs`) napevno registruje
  `RenderLoop.FromTimer(new ThreadProxyRenderTimer(new AvaloniaNativeRenderTimer(_factory.CreatePlatformRenderTimer())))`
  a hned poté vytvoří `new Compositor(...)`, který si render loop vezme z lokátoru.
- `ServerCompositor` v konstruktoru volá `_renderLoop.Add(this)` → `DefaultRenderLoop.Wakeup()` → `timer.Tick = …` →
  `ThreadProxyRenderTimer` → `AvaloniaNativeRenderTimer.EnsureRegistered()` → nativní `PlatformRenderTimer::RegisterTick`
  (`native/Avalonia.Native/src/OSX/PlatformRenderTimer.mm`) → `CVDisplayLinkCreateWithActiveCGDisplays`. Bez aktivního
  displeje vrátí `kCVReturnInvalidDisplay` (-6661) a C# vyhodí `InvalidOperationException("Avalonia.Native was not able to
  start the RenderTimer. Native error code is: -6661")`. Výjimka letí z inicializace platformy
  (`UseWindowingSubsystem` → `StartWithClassicDesktopLifetime`), tedy ještě před `App`.
- `_registered = true` se nastaví před voláním `RegisterTick`, takže neúspěch je v procesu trvalý; platformu Avalonie navíc
  v jednom procesu znovu inicializovat nejde (lokátor, `Dispatcher.InitializeUIThreadDispatcher`, nativní factory).
  Zachytit chybu a start zopakovat v tomtéž procesu proto nejde.
- Volba v Avalonii neexistuje: `AvaloniaNativePlatformOptions` má jen `RenderingMode` (Metal/OpenGl/Software – render timer
  se nemění), `OverlayPopups`, `AvaloniaNativeLibraryPath`, `AppSandboxEnabled`; `MacOSPlatformOptions` nic k timeru.
  Vlastní `IRenderLoop`/`IRenderTimer` nelze podstrčit (bind i `Compositor` vznikají uvnitř `DoInitialize`, `AfterSetup`
  je pozdě). Stejný kód je i v `master` (2026-10-29), chyba není opravená.

### Řešení v aplikaci
- `Platform/StartupDisplayWait` (logika, testovatelná fakes) + `Platform/IActiveDisplays` + `Platform/MacOS/MacActiveDisplays`
  (`CGGetActiveDisplayList(0, NULL, &count)` z CoreGraphics přes `[LibraryImport]` s absolutní cestou, blittable, AOT safe).
  „Aktivní“ displej v CoreGraphics = připojený, probuzený, kreslitelný, tedy stejná množina, jakou potřebuje
  `CVDisplayLinkCreateWithActiveCGDisplays`.
- `Program.Main` (jen macOS): po single-instance guardu (druhý start jen předá aktivaci čekající instanci) a před centrem
  notifikací a Avalonií počká, dokud není aspoň jeden aktivní displej. Kontrola každou 1 s, synchronně na hlavním vlákně
  (Avalonia/AppKit musí běžet na hlavním vlákně, proto ne `async`), bez časového limitu: aplikace se objeví v menu baru
  nejpozději ~1 s po probuzení displeje. SIGTERM/SIGINT (`PosixSignalRegistration`) čekání ukončí, `Main` vrátí 0
  (log se zapíše, guard uvolní). Když kontrola selže (chyba CoreGraphics, např. bez spojení s window serverem, nebo
  výjimka), zaloguje se varování a UI startuje jako dřív – čekání nikdy neblokuje kvůli nefunkční kontrole.
- Log: varování „No active display …“ jednou na začátku čekání, informace „N active display(s) after …; starting the UI.“
  po probuzení. Při okamžitě aktivním displeji se nic nezapisuje.
- Proč ne „zachytit a zopakovat“: v procesu to nejde (viz výše); znovuspuštění procesu by kolidovalo s single-instance
  guardem, centrem notifikací a Velopackem a pořád by potřebovalo zjistit, kdy je displej zpět. Kontrola předem je
  levná a míří přesně na podmínku, na které Avalonia selže.
- Zbytkové riziko: displej se uspí v okně milisekund mezi kontrolou a vytvořením `CVDisplayLink`. Pak aplikace skončí
  stejnou chybou (zásobník v logu přes `UnhandledExceptionLogger`). Zdokumentováno v `docs/troubleshooting.md`.
- Během čekání aplikace nemonitoruje tiskárny (kompoziční kořen vzniká až v `App`); start monitoringu bez UI by byl
  zásah do architektury a není v rozsahu.
- Windows: nic se nemění – volání je za `OperatingSystem.IsMacOS()`, `MacActiveDisplays` má `[SupportedOSPlatform("macos")]`.
- AOT/licence: žádný nový balíček; `PosixSignalRegistration` a `[LibraryImport]` jsou AOT kompatibilní.
- Testy: `tests/LayerPeek.App.Tests/Platform/StartupDisplayWaitTests.cs` (aktivní displej hned, čekání do probuzení,
  selhání/výjimka kontroly → start, signál ukončení během čekání, skutečná pauza se zruší), `Platform/MacOS/MacActiveDisplaysTests.cs`
  (skutečné volání CoreGraphics jen čte, na jiných OS přeskočeno).

### Smoke test a `caffeinate`
- `scripts/aot-smoke.ps1` se nemění funkčně, jen dokumentace: s uspaným displejem aplikace už nespadne, ale čeká bez
  UI/AppKitu, takže smoke test by ověřil jen čekání a kontrola typu `UIElement` v LaunchServices nejspíš neprojde
  (neověřeno). Smoke test (a `release-build.ps1`) proto dál běží s probuzeným displejem (`caffeinate -d -u`); důvod je
  v nápovědě skriptu a v `docs/troubleshooting.md`.

### Ověření (macOS 26 / Darwin 27, arm64, 2026-10-29)
- `dotnet build` 0 varování, 0 chyb; `dotnet test` celkem 7443, 0 selhání, 7265 úspěšných, 178 přeskočeno.
- `dotnet publish src/LayerPeek.App -c Release -r osx-arm64 -warnaserror`: bez varování trim/AOT (IL); jen obvyklé
  hlášky linkeru/dsymutil toolchainu.
- `tools/profile-snapshot.ps1 -Save`, pak `caffeinate -d -u pwsh scripts/aot-smoke.ps1 -Runtime osx-arm64 -PublishDir <publish> -Seconds 15`:
  PASSED, `UIElement`; `-Compare`: beze změny (Login Items nečitelné, nekontrolováno). Displej byl probuzený, větev
  čekání se tedy neprovedla.
- Reprodukce s uspaným displejem neproběhla: uspat displej (`pmset displaysleepnow`) by uživateli zhasnulo obrazovku
  během práce a nastavení napájení se nemění. Chování ověřují testy s fakes; skutečný start s uspaným displejem je krok
  uživatele (níže).

### Ověření uživatelem (start s uspaným displejem)
1. Sestavit Release `LayerPeek.app`: `caffeinate -d -u -i pwsh scripts/release-build.ps1` (bundle v `artifacts/release/<verze>/app/osx-arm64/`); LayerPeek nesmí běžet. Pozor: Release aplikace mimo smoke test používá skutečný profil (data, logy, nastavení).
2. V Terminálu: `sleep 10; open -a "<cesta>/LayerPeek.app"` a během 10 s uspat displej: `pmset displaysleepnow`
   (jen uspí displej, nic nemění).
3. Počkat ~30 s, pak displej probudit. Očekávání: ikona LayerPeek se objeví v menu baru do ~1 s po probuzení, aplikace
   běží; v `~/Library/Logs/LayerPeek/` věty „No active display …“ a „… active display(s) after …; starting the UI.“
4. Volitelně: během čekání (displej uspaný, přes SSH) `pkill -TERM LayerPeek` → v logu „Asked to quit … the UI was not started.“, proces skončí s kódem 0.

### Návrh hlášení do Avalonie (neodesláno)
> **macOS: app crashes at startup when no display is active (kCVReturnInvalidDisplay -6661 from CVDisplayLink)**
>
> *Version:* Avalonia 12.1.4 (Avalonia.Native); same code on `master` as of 2026-10-29. macOS 26 (Darwin 27), Apple Silicon.
>
> *Repro:* any Avalonia app on macOS. Put the display to sleep (`pmset displaysleepnow`, or close the lid without an
> external monitor) and start the app (e.g. `sleep 10; open -a MyApp.app`, then sleep the display). Login items hit
> this at login with the lid closed.
>
> *Result:* the process exits during platform initialization:
> ```
> System.InvalidOperationException: Avalonia.Native was not able to start the RenderTimer. Native error code is: -6661
>    at Avalonia.Native.AvaloniaNativeRenderTimer.EnsureRegistered()
>    at Avalonia.Native.AvaloniaNativeRenderTimer.set_Tick(Action`1)
>    at Avalonia.Rendering.ThreadProxyRenderTimer.set_Tick(Action`1)
>    at Avalonia.Rendering.DefaultRenderLoop.Wakeup()
>    at Avalonia.Rendering.DefaultRenderLoop.Add(IRenderLoopTask)
>    at Avalonia.Rendering.Composition.Server.ServerCompositor..ctor(...)
>    at Avalonia.Rendering.Composition.Compositor..ctor(...)
>    at Avalonia.Native.AvaloniaNativePlatform.DoInitialize(AvaloniaNativePlatformOptions)
>    ...
> ```
> (stack reconstructed from the sources; frames may be inlined in a Native AOT build.)
>
> *Cause:* `PlatformRenderTimer::RegisterTick` uses `CVDisplayLinkCreateWithActiveCGDisplays`, which returns
> `kCVReturnInvalidDisplay` when `CGGetActiveDisplayList` is empty (all displays asleep). `AvaloniaNativeRenderTimer`
> turns it into an exception; `_registered` is set before the call, so it never retries.
>
> *Proposed fix:* when no display is active, fall back to a timer that does not need a display (e.g. an
> `NSTimer`/`dispatch_source` at 60 Hz, or the managed `SleepLoopRenderTimer`) and switch to `CVDisplayLink` when
> `CGDisplayRegisterReconfigurationCallback`/`NSApplicationDidChangeScreenParametersNotification` reports a display;
> alternatively create the display link lazily on the first `Start()` and retry there instead of throwing. On macOS 14+
> `NSScreen/NSView displayLinkWithTarget:selector:` (CADisplayLink) is the replacement for the deprecated CVDisplayLink.
