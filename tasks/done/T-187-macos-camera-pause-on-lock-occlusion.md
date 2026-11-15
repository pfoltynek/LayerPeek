---
id: T-187
title: macOS – pozastavení streamu kamery při zamčení, spánku displeje a zakrytí okna
status: done
owner: ui-dev
priority: P3
platform: macos
depends_on: [T-186]
---

## Cíl
Protějšek T-186 pro macOS. Na macOS jde navíc spolehlivě zjistit, že je okno úplně zakryté nebo na jiné ploše
(`NSWindow.occlusionState`), takže se stream může pozastavit i v tomto případě.

## Rozsah
- Implementace abstrakce z T-186 pro macOS: zamčení obrazovky (`com.apple.screenIsLocked` /
  `com.apple.screenIsUnlocked` přes `NSDistributedNotificationCenter`) a spánek displeje
  (`NSWorkspaceScreensDidSleepNotification` / `NSWorkspaceScreensDidWakeNotification`).
- Zakrytí okna: `NSWindowDidChangeOcclusionStateNotification` a `occlusionState` okna kamery → okno vypne poptávku
  po snímcích.
- Interop bez reflexe (AOT), stejně jako ostatní macOS kód v App.

## Akceptační kritéria
- [x] Zamčení, spánek displeje a úplné zakrytí okna pozastaví příjem snímků a po návratu se obnoví. (Logika testy
      s falešnými službami; skutečná služba na Macu se zamčenou obrazovkou: okno otevřené při zamčení nestreamuje;
      odemčení, spánek displeje, zakrytí a jiný Space → přesunuto do T-127)
- [x] Nahrávání, detekce a timelapse pokračují. (Test T-186 `RecordingTimelapseAndDetectionLeasesGoOnWhileTheScreenIsLocked`
      platí pro obě platformy, macOS nic nového nesleduje; skutečné nahrávání při zamčení → přesunuto do T-127)
- [x] Ruční ověření na Macu → T-127. → přesunuto do T-127
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
Rozhodnuto uživatelem 2026-11-11 (v T-186): pozastavit ano, i okno na jiném Space a i malý náhled „vždy navrchu“; detekce a nahrávání běží dál.

- Stejné jako v T-186 (odpovědi platí pro obě platformy).

## Poznámky k implementaci
Implementováno 2026-11-15 na macOS 27 (Apple Silicon), agent ui-dev. Abstrakce z T-186 beze změny (`IDisplayActivityService`
v Core, `IWindowCloaking` v App); interop jen `[LibraryImport]` (`ObjCRuntime`, CoreGraphics, CoreFoundation) a globální
bloky kolem `UnmanagedCallersOnly` funkcí (`ObjCBlocks`), žádná reflexe, žádný nový balíček.

- **Zamčení a spánek displeje:** `App/Platform/MacOS/MacDisplayActivityService` (spojení stavů dál dělá
  `DisplayActivityTracker`, přibyla `SetDisplayAsleep`). Čtyři pozorovatelé `addObserverForName:object:queue:usingBlock:`
  na hlavní frontě: `com.apple.screenIsLocked` / `com.apple.screenIsUnlocked` z `NSDistributedNotificationCenter`
  a `NSWorkspaceScreensDidSleepNotification` / `DidWake` z centra `NSWorkspace` (jen když je AppKit už načtený, nic
  nenačítá). Žádné vlákno, časovač ani dotazování; událost přichází na hlavním (UI) vlákně. Bloky jsou globální a najdou
  spuštěné instance ve statickém seznamu (`ImmutableArray`), takže se instance v testech nepletou. **Počáteční stav** se
  čte po registraci (změna mezi tím přijde jako notifikace a vyhraje): zamčení z `CGSessionCopyCurrentDictionary` →
  `CGSSessionScreenIsLocked` (přes CoreFoundation, bez Objective-C; mimo relaci okenního serveru `null` = odemčeno),
  spánek z `CGDisplayIsAsleep(CGMainDisplayID())`. Spouští se líně s prvním oknem kamery jako na Windows
  (`AppServices.CreateCameraViewModel`), uvolní ji DI kontejner (odebere pozorovatele).
  Rychlé přepnutí uživatele (`NSWorkspaceSessionDidResignActive`) se zvlášť nesleduje (nebylo v rozsahu); okno v tu
  chvíli zakryté je, takže ho pozastaví sledování zakrytí.
- **Zakrytí a jiný Space:** `App/Platform/MacOS/MacWindowOcclusion` (výchozí `IWindowCloaking` okna kamery na macOS).
  Při startu sledování přečte `NSWindow.occlusionState`, pak pozorovatel `NSWindowDidChangeOcclusionStateNotification`
  s `object:` = to okno (jeden na okno, odebrán při zavření). Okno je zakryté, když chybí `NSWindowOcclusionStateVisible`
  (nic z něj není vidět: úplně zakryté, na jiném Space, za aplikací v celé obrazovce, zamčeno, displej spí). Po notifikaci
  se čte stav znovu, ne přepínač. Okno bez nativního `NSWindow` (headless) ani okno bez pozorovatele se nikdy nehlásí jako
  zakryté. Nativní okno z handle Avalonie: nová sdílená `NativeWindowCollectionBehavior.WindowOf` (T-038 ji používá taky).
- **Ochranná doba 2 s:** `App/Platform/OcclusionGrace` (čistá logika, testy s falešným plánovačem) ohlásí zakrytí až
  po 2 s trvání (`DispatcherTimer.RunOnce`), viditelnost hned. Správce oken hlásí okno na okamžik zakryté při animacích
  (přepnutí Space, celá obrazovka, Mission Control); pauza by tam stála jen nové připojení od klíčového snímku. Úspora
  se tím o 2 s opozdí; zamčení a spánek displeje (služba) pozastaví hned. Rozhodnutí implementace, ne UX – změnit jde
  konstantou `OcclusionGrace.Default`.
- **Malý náhled navrchu:** je na všech plochách (T-038), na jiném Space tedy nikdy není; pozastaví se při zamčení,
  spánku displeje a úplném zakrytí jako ostatní režimy (rozhodnutí 3 z T-186).
- **Měření (Debug, simulátor 25 fps, zamčená obrazovka, `caffeinate -d`, okno otevřené příkazem `camera:verify`,
  CPU procesu za 30 s):** tato větev 0 spojení RTSP, **1,3 % CPU**, RSS 248 MB; `develop` (bez T-187) 1 spojení, dekódování
  VideoToolbox, **13,8 % CPU**, RSS 278 MB. Odemčení, zakrytí ani jiný Space jsem neměřil (obrazovka byla celou dobu
  zamčená, uživatel přes SSH).
- **Testy:** `MacDisplayActivityServiceTests` (10: registrace a úklid pozorovatelů, notifikace přes `Report`, jen
  spuštěné instance, start po `Dispose`, počáteční stav proti `CGSessionCopyCurrentDictionary` / `CGDisplayIsAsleep`,
  mapování `occlusionState`), `OcclusionGraceTests` (6), `DisplayActivityTrackerTests` +2 (spánek displeje, probuzení na
  zamykací obrazovku), `AppServicesTests.DisplayActivityOfTheOsIsRegisteredAndNotStartedByResolving` (macOS větev),
  UI `MacWindowOcclusionUiTests` (headless okno nikdy zakryté). Distribuovaná notifikace zamčení se v testech neposílá
  (dostaly by ji všechny aplikace relace) a hlavní fronta v testovacím procesu nepumpuje, proto se notifikace podávají
  přes `Report` (to, co volá každý blok).
- **Automatická kontrola `macos-verify`:** nepřidána – obrazovka byla zamčená, kontrolu by nešlo ani jednou spustit
  (zakrytí potřebuje posun a velikost cizího okna přes AX, nová akce helperu). Navržena jako **T-235**
  (`mac.camera-pause-hidden`); zamčení a spánek displeje zůstávají ruční (T-127 1r).
- **Dokumentace:** odstavec v oddílu 5.1 všech 8 příruček, `CHANGELOG.md` (položka T-186 rozšířena o macOS),
  `docs/architecture.md`, T-127 oddíl 1r, nový T-235 v `tasks/README.md` (T-234 mezitím obsadil Windows). Žádné nové
  texty UI.
- **Ověření (po `git merge origin/develop`):** `dotnet build` Debug i `-c Release` 0 varování; celé `dotnet test`
  10 911 testů, 10 630 PASS, 281 přeskočeno, 0 selhání; `dotnet publish -c Release -r osx-arm64` (Native AOT) bez
  varování trim/AOT (jen obvyklé hlášky dsymutil / xcodebuild nástrojů). Release aplikaci ani skutečný profil jsem
  nespouštěl; Debug aplikace jen v izolovaném testovacím režimu.
