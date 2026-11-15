---
id: T-186
title: Kamera – pozastavení streamu při zamčení počítače a vypnutém displeji (Windows)
status: done
owner: ui-dev
priority: P2
platform: windows
depends_on: []
---

## Cíl
Výkonnostní analýza (2026-11-06): stream okna kamery se pozastaví jen při minimalizaci
(`viewModel.SetActive(WindowState != WindowState.Minimized)` v
[CameraWindow.axaml.cs](../../src/LayerPeek.App/Windows/CameraWindow.axaml.cs)). Při zamčeném počítači, vypnutém
displeji nebo okně na jiné virtuální ploše se obraz dál dekóduje, převádí a vykresluje, i když ho nikdo nevidí.

## Rozsah
- Abstrakce v Core (např. `IDisplayActivityService`: „obrazovka je vidět“ + událost změny), implementace pro Windows
  v App, pro macOS v T-187. Do té doby má macOS výchozí implementaci „vždy vidět“.
- Windows: zamčení a odemčení relace (`WTSRegisterSessionNotification`, `WM_WTSSESSION_CHANGE`) a stav displeje
  (`RegisterPowerSettingNotification` s `GUID_CONSOLE_DISPLAY_STATE`) přes `[LibraryImport]`, bez reflexe (AOT).
- Když obrazovka není vidět, okno vypne jen svou poptávku po snímcích (`SetActive(false)`). Nahrávání, detekce
  a timelapse běží dál. Po návratu se obraz obnoví od nejbližšího klíčového snímku.
- Testy logiky s falešnou službou. Zamčení ověřit E2E podle pravidel pro systémové změny, jinak ručně (→ T-125).

## Akceptační kritéria
- [x] Při zamčení počítače a při vypnutí displeje okno kamery přestane přijímat snímky a po odemčení nebo zapnutí
      se obnoví. (Logika testy s falešnou službou; skutečné zamčení a vypnutí displeje → přesunuto do T-125)
- [x] Nahrávání, detekce a timelapse při zamčení pokračují (test).
- [x] Žádné varování trim/AOT, interop jen přes `[LibraryImport]`.
- [x] Ruční ověření na Windows → T-125 (pokud se neautomatizuje). → přesunuto do T-125
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
Rozhodnuto uživatelem 2026-11-11: 1 **ano** (odblokováno); 2 **ano**, pozastavit i okno na jiné virtuální ploše (`DWMWA_CLOAKED`); 3 **ano**, platí i pro malý náhled „vždy navrchu“. Detekce a nahrávání běží dál. Odpovědi platí i pro macOS (T-187).

1. **Mění to chování aplikace. Chcete to?** (Blokuje implementaci.)
2. Pozastavit i okno na jiné virtuální ploše (`DWMWA_CLOAKED`)? Okno zakryté jinými okny Windows spolehlivě nehlásí,
   takže tento případ se řešit nebude.
3. Platí to i pro malý náhled „vždy navrchu“, nebo jen pro běžné okno?

## Poznámky k implementaci
Implementováno 2026-11-15 na Windows 11 Pro (26300), agent ui-dev.

- **Core:** `Core/Display/IDisplayActivityService` („obrazovka je vidět“ + `ScreenVisibilityChanged`, libovolné vlákno)
  a výchozí `AlwaysVisibleDisplayActivity` (macOS do T-187, testy). T-187 existuje v `tasks/todo/`, neimplementováno.
- **Windows – zamčení a displej:** `App/Platform/Windows/WindowsDisplayActivityService` vytvoří skryté okno jen pro
  zprávy (`HWND_MESSAGE`, bez EnumWindows, bez tlačítka na hlavním panelu) na **vlastním vlákně s vlastní smyčkou zpráv**
  (jako `SystemEvents`) a na něj registruje `WTSRegisterSessionNotification` (`WTS_SESSION_LOCK` / `UNLOCK`)
  a `RegisterPowerSettingNotification` s `GUID_CONSOLE_DISPLAY_STATE` (0 vypnuto, 1 zapnuto, 2 ztlumeno = vidět).
  Registrace displeje hned pošle aktuální stav (ověřeno testem). **Počáteční stav zamčení** (po review): `Start()` ho
  zjistí synchronně přes `WTSQuerySessionInformationW(WTS_CURRENT_SERVER_HANDLE, WTS_CURRENT_SESSION, WTSSessionInfoEx)`
  → `WTSINFOEXW` úroveň 1 `SessionFlags` + `WTSFreeMemory` a vlákno okna ještě jednou po registraci (zamčení mezi
  dotazem a registrací se neztratí). Mapování je čistá funkce `DisplayActivityTracker.IsLockedSession`
  (`WTS_SESSIONSTATE_LOCK` 0 = zamčeno, `UNLOCK` 1 i `UNKNOWN` −1 = odemčeno; obrácené hodnoty Windows 7 se neřeší).
  Služba se dál spouští **líně s prvním oknem kamery** (`AppServices.CreateCameraViewModel`); spouštět ji při startu
  aplikace není potřeba, protože okno otevřené při už zamčené relaci (T-151 ho otevře, když se tiskárna zapne) dostane
  správný stav z dotazu. Spojení obou zdrojů do jednoho výsledku je čistá třída `App/Platform/DisplayActivityTracker`
  (testy). Okenní procedura hledá instanci podle HWND (statický slovník), takže se instance (testy) nepletou.
  `Dispose` z libovolného vlákna pošle oknu `WM_CLOSE`; vlákno okna odregistruje obě notifikace, zničí okno
  (`WM_DESTROY` → `PostQuitMessage`) a skončí, `Dispose` na něj počká (nejvýš 5 s). Uvolní ji DI kontejner.
  Událost změny přichází na vlákně služby, view model ji předá přes `IUiDispatcher`.
- **Windows – jiná virtuální plocha:** `App/Platform/Windows/WindowsWindowCloaking` (rozhraní `App/Platform/IWindowCloaking`).
  Návrh detekce: při otevření okna jednou `DwmGetWindowAttribute(DWMWA_CLOAKED)`, potom WinEventy
  `EVENT_OBJECT_CLOAKED` / `EVENT_OBJECT_UNCLOAKED` přes `SetWinEventHook` s `WINEVENT_OUTOFCONTEXT`. Windows je pošle
  při každém skrytí/odkrytí okna, i při přepnutí plochy nebo přesunu okna přes Win+Tab; callback přijde na UI vlákno
  z jeho smyčky zpráv. Bez dotazování (žádný časovač, nulová cena v klidu) a bez čekání na aktivaci okna (ta by
  nepřišla, když uživatel přepne plochu pryč). Hook poslouchá všechny procesy (není zdokumentované, který proces událost
  „vyvolá“, skrývá shell) a filtruje podle sledovaných oken; existuje jen při otevřeném okně kamery. Po události se čte
  znovu atribut, ne typ události. Okno jen zakryté jinými okny se neřeší (rozhodnutí 2).
- **Okno a view model:** `CameraViewModel` je aktivní jen když je okno zobrazené a ne minimalizované (`SetActive`),
  není skryté (`SetCloaked`) a obrazovka je vidět (služba, změna přes `IUiDispatcher`). Jinak pustí lease stejně jako
  při minimalizaci (`StopStream`), po návratu ho vezme znovu se `ShortensReconnectWait` (T-188, čekání na další pokus
  skončí) a obnoví časovač „Další pokus“ (T-194). Platí pro všechny režimy, i malý náhled navrchu (rozhodnutí 3).
  `CameraWindow.Cloaking.cs` sleduje skrytí od `OnOpened` (před spuštěním streamu) do `OnClosed`.
- **Nahrávání, dashcam, timelapse, detekce:** mají vlastní lease hubu a službu nesledují, takže běží dál (test
  `RecordingTimelapseAndDetectionLeasesGoOnWhileTheScreenIsLocked`: lease paketů + lease snímků dál dostávají data
  ze stejného spojení, okno nekreslí).
- **Vztah k T-203..T-207:** dekodér nic nového nepotřebuje. Když okno odejde a zbude jen detekce (jen klíčové snímky),
  `FFmpegDecoder` plné dekódování vypne a zapomene stav; po návratu okna začne od dalšího klíčového snímku
  (`_needKeyFrame`) se SPS/PPS zapamatovanými z paketů (T-207). Hardware nebo software se zvolí znovu při startu plného
  dekódování (T-203), uvolněné hardwarové zařízení se tak po odemčení otevře znovu. Bez jiného odběratele se spojení
  10 s po zamčení zavře (linger hubu) a po odemčení se připojí znovu.
- **AOT:** interop jen `[LibraryImport]` a `UnmanagedCallersOnly` ukazatele na funkce, žádná reflexe ani delegáty
  marshalované za běhu; `dotnet build` s analyzátory AOT/trim bez varování. Lokální `dotnet publish -r win-x64` jsem
  nespouštěl (zůstává na qa / T-125 běh `scripts/aot-smoke.ps1`). Žádný nový balíček.
- **Testy:** `CameraViewModelVisibilityTests` (9), `DisplayActivityTrackerTests` (12 včetně mapování `SessionFlags`
  a relace zamčené při startu), `WindowsDisplayActivityServiceTests` (5: skutečné okno a registrace na vlastním vlákně,
  stav displeje, úklid okna i vlákna po `Dispose` z jiného vlákna, dvě instance vedle sebe, dotaz na stav relace; bez
  zamykání), `AppServicesTests.DisplayActivityOfTheOsIsRegisteredAndNotStartedByResolving`; testy skládání, které
  vytvářejí view model okna kamery, dostanou `AlwaysVisibleDisplayActivity` (nový parametr `AppServices.Create(displayActivity:)`),
  takže skutečnou službu nespouštějí, `CameraWindowVisibilityUiTests` (4, headless okno s falešným skrytím
  a obrazovkou, i malý náhled), Core `AlwaysVisibleDisplayActivityTests` (1). Běhy: Core.Tests 3404/3404,
  App.Tests 4782 + 105 přeskočeno, App.UiTests 1378 + 105 přeskočeno, vše PASS; build 0 varování. Po review:
  Core.Tests 3404/3404, App.Tests 3× za sebou 4791 + 105 přeskočeno, App.UiTests 1378 + 105 přeskočeno, vše PASS; build
  0 varování. E2E testy ani celé
  `dotnet test` řešení jsem podle zadání nespouštěl (sdílená plocha), zamčení ani vypnutí displeje jsem nezkoušel.
- **Spuštění:** izolovaný testovací režim (`LAYERPEEK_TEST_DATA_DIR`, vlastní přípona instance) 25 s běžel bez chyby
  v logu; bez tiskárny se okno kamery neotevřelo, takže skutečné registrace za běhu aplikace ověří T-125.
- **Review 2026-11-15 (qa):** opraveno 1 (počáteční stav zamčení), 2 (vlastní vlákno služby, instance podle HWND,
  testy skládání bez skutečné služby), 3 (`CHANGELOG.md` „stop receiving and drawing“; příručky už říkají „přestane brát
  snímky“), 4 (test bez `Thread.Sleep`: služba pumpuje sama, test čeká na `Task` s limitem), 5 (filtr `idChild ==
  CHILDID_SELF`); 6 opravil reviewer.
- **Dokumentace:** odstavec v oddílu 5.1 všech 8 příruček, `CHANGELOG.md` (Changed), `docs/architecture.md`, body
  v T-125 (oddíl 2af). Žádné nové texty UI.

## Review
Review qa-reviewer 2026-11-15: **vyžaduje změny**. Build 0 varování; Core.Tests 3404/3404, App.Tests 4782 + 105
přeskočeno, App.UiTests 1378 + 105 přeskočeno, vše PASS.

1. **Blokující – okno otevřené při už zamčené relaci streamuje celou dobu zamčení.**
   `WindowsDisplayActivityService` bere při `Start()` stav „odemčeno“ (`DisplayActivityTracker` startuje jako vidět),
   ale služba se nespouští při startu aplikace, nýbrž líně při prvním okně kamery (`AppServices.CreateCameraViewModel`).
   Okna se přitom otevírají i bez uživatele (T-150/T-151 `CameraWindowAutoVisibilityObserver`: tiskárna se zapne v noci
   při zamčeném PC). Pak `WTS_SESSION_UNLOCK` jen potvrdí „odemčeno“ a celé zamčení se dekóduje a kreslí – přesně případ
   z kritéria 1. Odůvodnění „aplikace startuje po přihlášení“ tu neplatí. Návrh: v `Start()` po registraci zjistit
   počáteční stav `WTSQuerySessionInformationW(WTS_CURRENT_SERVER_HANDLE, WTS_CURRENT_SESSION, WTSSessionInfoEx)` →
   `WTSINFOEXW.Level1.SessionFlags` (`WTS_SESSIONSTATE_LOCK` = 0, `UNLOCK` = 1, `UNKNOWN` = −1 → odemčeno), `WTSFreeMemory`;
   levné, jen `[LibraryImport]`. Obrácené hodnoty platí jen pro Windows 7 / Server 2008 R2 (mimo podporu). Doplnit test
   mapování příznaku (čistá funkce) a test view modelu „otevřeno zamčené“ přes skutečnou službu nejde – stačí tracker.
   Případně navíc službu spustit už při startu UI.
2. **Střední – testy vytvářejí skutečné okno služby na vláknech bez smyčky zpráv a přepisují statické `s_current`.**
   `AppServicesTests` (`PreviewCacheIsWiredToMonitoringAndToTheCameraWindows`, `CameraWindowOfApplyComesFromTheSavedPrinter`,
   `CameraWindowNeedsAMonitoredPrinterWithAnEnabledCamera`) volají `CreateCameraViewModel` → `Start()` na vlákně xUnit:
   vznikne skutečné okno jen pro zprávy + registrace WTS/napájení na vlákně, které nikdy nepumpuje; po `await using` může
   `Dispose` běžet na jiném vlákně → jen `PostMessage(WM_CLOSE)` do fronty, kterou nikdo nečte → okno zůstane do konce
   procesu („testy nic nenechávají“). Hlavně: `Start()` nastaví `s_current` na novou instanci; běží-li souběžně
   `WindowsDisplayActivityServiceTests.StartedServiceGetsTheDisplayStateAndDisposeStopsListening`, `WindowProcedure` jeho
   okno už nepozná (`window == current._window` neplatí) a test náhodně selže (`reported` = false). Návrh: hledat instanci
   podle HWND (statický slovník / `GWLP_USERDATA`) místo jedné statické, a v `AppServicesTests` nespouštět skutečnou
   službu (např. `Start()` volat z `TrayHost`/UI startu místo z továrny view modelu, nebo injektovat `IDisplayActivityService`).
3. **Drobnost – `CHANGELOG.md`:** „stop decoding and drawing the picture“ – když běží detekce (nebo jiný odběratel snímků),
   dekodér dál dekóduje; přesnější „stop receiving and drawing“ (jako příručky).
4. **Drobnost – test se `Thread.Sleep(10)`** v pumpovací smyčce `WindowsDisplayActivityServiceTests`: není to časová
   podmínka výsledku (limit 5 s), ale čistší je `MsgWaitForMultipleObjects`/`GetMessage` s časovačem.
5. **Drobnost – `WindowsWindowCloaking.OnWinEvent`** nefiltruje `idChild == CHILDID_SELF` (0); neškodné, protože se
   čte atribut, jen pro úplnost.
6. Opraveno reviewerem: `docs/architecture.md` „nezakrytém okně“ (zakrytá okna se právě neřeší) → „neskrytém okně
   (ne `DWMWA_CLOAKED`)“.

Ověřeno bez nálezu: signatury `[LibraryImport]` a konstanty (WNDCLASSEXW, POWERBROADCAST_SETTING, GUID, WM_*/PBT/WTS/EVENT_*,
DWMWA_CLOAKED 14, HWND_MESSAGE), `UnmanagedCallersOnly` ukazatele (žádný delegát k udržování naživu), odregistrace
a `DestroyWindow`/`WM_CLOSE`, hook jen při otevřeném okně a odhákování po posledním, vlákna (WinEvent out-of-context
a okno zpráv na UI vlákně, změna obrazovky přes `IUiDispatcher`), stavový automat view modelu (retry, T-188, T-194),
Avalonia 12.1.6 sama `DWMWA_CLOAK` nenastavuje (žádné falešné pozastavení při přepínání režimů), příručky 8 jazyků, T-125 2af.
Lokální AOT publish nespuštěn (nebyl součástí zadání review).

**Opakovaná review qa-reviewer 2026-11-15: schváleno.** Nálezy 1–5 opraveny. Ověřeno: rozložení `WTSINFOEXW` /
`WTSINFOEX_LEVEL1_W` (unie na offsetu 8 kvůli `LARGE_INTEGER`, `SessionId` 8, `SessionState` 12, `SessionFlags` 16),
`WTSFreeMemory` na všech cestách s bufferem, vyhledání instance podle HWND, vlákno na pozadí (`IsBackground`), odregistrace
a zničení okna na jeho vlákně, po `Dispose` žádné události (vlákno skončí), testy bez skutečné služby v `AppServicesTests`.
Drobnosti opravené reviewerem ve `WindowsDisplayActivityService`: `Dispose` z obsluhy na vlákně okna jen pošle `WM_CLOSE`
a nečeká sám na sebe (dřív 5 s zablokování); vlákno, které doběhlo do čekání až po `Dispose`, hned uklidí místo trvalého
naslouchání; jakákoli výjimka na vlákně okna proces neshodí; celé `Dispose` má jeden limit 5 s (dřív až 2 × 5 s při
ukončení se zaseknutým vláknem). Build 0 varování; Core.Tests 3404/3404, App.Tests 2× 4791 + 105 přeskočeno
(a znovu po opravách), App.UiTests 1378 + 105 přeskočeno.
