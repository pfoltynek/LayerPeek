---
id: T-010
title: Systémové notifikace – Windows
status: in-progress
owner: ui-dev
priority: P1
platform: windows
depends_on: [T-009]
---

## Cíl
Implementace `INotificationService` pomocí nativních toast notifikací Windows. Implementace pro macOS je samostatný úkol T-020.

## Rozsah
- Windows: toast notifikace
- Kliknutí na notifikaci otevře detail / web tiskárny
- Registrace implementace v composition rootu jen na Windows (`OperatingSystem.IsWindows()`); macOS větev doplní T-020
- Řešení musí být kompatibilní s Native AOT (žádné `BuiltInComInteropSupport`, interop přes `[LibraryImport]` / `[GeneratedComInterface]`)

## Akceptační kritéria
- [x] Notifikace fungují na Windows (ověřeno) – uživatel 2026-10-18 ručně potvrdil, že se bannery toastů na Windows zobrazují
- [ ] Kliknutí na notifikaci otevře detail / web tiskárny – implementováno a pokryto testy, skutečné kliknutí ověří uživatel
- [x] Na jiném OS než Windows se Windows implementace neregistruje a build/spuštění nespadne – registrace ověřena testem (větev „jiný OS“), spuštění na macOS lokálně neověřeno (dle rozhodnutí uživatele postačuje test; reálný běh na macOS ověří T-020)
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
Vyřešené (rozhodnutí 2026-10-16):
- ~~Knihovna / přístup na Windows~~ → vlastní WinRT interop (`ToastNotificationManager` přes `[GeneratedComInterface]`), bez nového balíčku, bez změny TFM.
- ~~AppUserModelID pro nebalenou aplikaci~~ → registruje se při startu zástupcem ve Start menu uživatele (bez admin práv) + `SetCurrentProcessExplicitAppUserModelID`. Co po aplikaci zůstane, je v poznámkách (pro T-017).
- ~~Zvuk~~ → podle nastavení `notifications.sound` z T-009: `Off` (vše potichu) / `ImportantOnly` (výchozí; zvuk jen u chyby, „vyžaduje pozornost“ a odmítnutých přihlašovacích údajů) / `All`. Platí i pro macOS (T-020).
- ~~macOS před T-020~~ → `NullNotificationService` (notifikace se tiše neposílají).

Vyřešené po implementaci (rozhodnutí uživatele 2026-10-16):
- ~~Kliknutí, když aplikace neběží~~ → zatím nic (toast jen zmizí). COM aktivátor (`ToastActivatorCLSID`) patří do T-017, poznámka je v jeho rozsahu.
- ~~AppUserModelID a název zástupce~~ → `PrusaTrayDiagnostic` a „Prusa Tray Diagnostic“ jsou finální.
- ~~Texty notifikací~~ → schválené (klíče `Notification*` v `Resources/Strings*.resx`, uživatel je může později upravit).
- ~~Ikona v toastu~~ → řeší samostatný úkol (zakládá hlavní agent). Do té doby toast ukazuje obecnou ikonu, protože exe nemá `ApplicationIcon`.

## Poznámky k implementaci
- **Architektura (App):**
  - `Notifications/ToastNotificationService` (`INotificationService`, `IDisposable`):
    - `Show` jen zařadí notifikaci do fronty (max. `MaxPending` = 32, další se zahodí s varováním v logu) a nikdy neblokuje.
    - Jedno vlákno na pozadí (vlastní `Thread`, ne pool, kvůli COM apartmentu) líně otevře platformu, sestaví XML v jazyce aktivním v okamžiku sestavení (viz merge s T-029 níže) a toasty zobrazuje postupně.
    - Selhání otevření platformy se zaloguje jednou a služba se změní na no-op. Selhání jednoho toastu se zaloguje a další se zobrazí.
    - Po `Dispose` se volání ignorují. `Dispose` nečeká na rozběhnutý toast, vlákno uvolní WinRT objekty samo. Žádná výjimka z vlákna neunikne (shodila by proces).
    - `Start()` volá `TrayHost.Start`, takže registrace zástupce a notifier vzniknou hned po startu, ne až s první notifikací (Windows potřebuje chvíli na zaregistrování nového zástupce).
  - `Notifications/ToastContentBuilder`: XML ve schématu `ToastGeneric`.
    - Titulek = název tiskárny (když po vyčištění nic nezbude, id), text = událost, třetí řádek = název souboru (jen když je).
    - `RemainingTime` používá stejné formátování jako tooltip (`TrayToolTipFormatter.FormatDuration`).
    - `PlaySound == false` → `<audio silent="true"/>`, jinak výchozí zvuk Windows.
    - Bezpečnost: z textů od uživatele a tiskárny se odstraní znaky, které XML 1.0 nedovoluje (řídicí znaky, osamocené surrogaty), texty se zkrátí na 200 znaků a escapují (`& < > " '`). Nedá se tak podstrčit značka (akce, protokolové spuštění…). Ověřeno testy i skutečným parserem Windows.
    - `launch="printer=<id percent-encoded>"`, `activationType="foreground"`.
  - `Notifications/ToastLaunchArgument`: kódování a čtení id tiskárny v argumentu.
  - `Notifications/ToastActivationRouter` (`IMonitoringObserver`, zaregistrovaný v `MonitoringSession`):
    - Drží tiskárny aktuálního běhu. Kliknutí: argument → tiskárna (id bez ohledu na velikost písmen) → `PrinterWebAddress.For` → `IUrlLauncher`.
    - Starý toast tedy otevře aktuální adresu. Odebraná tiskárna, neplatná nebo nebezpečná adresa či neznámý argument nic neotevřou, jen se to zaloguje (adresa se neloguje, může obsahovat přihlašovací údaje).
  - `Notifications/IToastPlatform` / `IToastSession`: tenké rozhraní nad WinRT kvůli testům.
  - `Composition/AppServices`: `INotificationService` = `ToastNotificationService` jen když `OperatingSystem.IsWindows()`, jinak `NullNotificationService`.
    - Interní přetížení `Create(loggerFactory, windows)` umožní otestovat i větev „jiný OS“.
    - Vytvoření služby nic nespouští (žádné vlákno, zástupce ani toast), takže testy kompozice nic nezanechají.
  - `Program.Main`: na Windows `SetCurrentProcessExplicitAppUserModelID` před vytvořením oken.
- **Windows interop (`Platform/Windows`, vše `[SupportedOSPlatform("windows")]`):**
  - `ComNative`: `[LibraryImport]` pro `CoInitializeEx`/`CoUninitialize`, `CoCreateInstance`, `PropVariantClear`, `WindowsCreateString`/`WindowsDeleteString`/`WindowsGetStringRawBuffer`, `RoGetActivationFactory` a `SetCurrentProcessExplicitAppUserModelID`.
  - Pomocné typy:
    - `ComApartment`: vyvážený vstup do MTA; STA vlákno nechá, jak je.
    - `HString`.
    - `ComObjects`: `StrategyBasedComWrappers`, jedinečné RCW uvolňované deterministicky přes `ComObject.FinalRelease`, ne přes GC.
  - `WinRtToastInterop`: `[GeneratedComInterface]` pro `IInspectable`, `IActivationFactory`, `IToastNotificationManagerStatics`, `IToastNotifier`, `IToastNotificationFactory`, `IToastNotification`, `IToastActivatedEventArgs`, `IXmlDocument`, `IXmlDocumentIO` a delegát `TypedEventHandler<ToastNotification, Object>`.
    - IID a pořadí metod podle hlaviček Windows SDK 10.0.26100.
    - IID jsou kvůli AOT i jako hodnoty (`WinRtInterfaceIds`, bez reflexe). Test hlídá shodu s `[Guid]`.
  - `WinRtToast`:
    - `WinRtToastFactory`: activation factory toastu a `XmlDocument` se získají jednou a cachují.
    - `WinRtToastNotifier`.
    - `WinRtToast`: přihlášení a odhlášení `Activated`.
    - `ToastActivationCallback` + `[GeneratedComClass] ToastActivatedHandler`: nativní handler kliknutí, jeden pro všechny toasty.
  - `WindowsToastPlatform` / `WindowsToastSession`:
    - Při otevření zajistí zástupce (selhání se zaloguje, notifier se vytvoří i tak), vytvoří notifier pro AUMID a zobrazuje toasty.
    - Drží posledních 20 toastů (s odběrem kliknutí), starší uvolní. Toast, jehož objekt aplikace uvolnila, už kliknutí nehlásí.
    - Při ukončení se odběry odhlásí a vše se uvolní na vlákně služby.
    - Uvolňování je odolné (oprava po review): každý toast, notifier, handler i factory se uvolní samostatně (`Platform/Disposables.DisposeAll`). Jedno selhání tedy nezastaví uvolnění ostatních a vlákno vždy opustí COM apartment. `WinRtToast.Dispose` spolkne jakoukoli chybu odhlášení (RO_E_CLOSED → `ObjectDisposedException`, `ArgumentException`…) a referenci uvolní vždy. Selhání při uvolnění nejstaršího toastu se loguje zvlášť („Releasing a toast… failed“), ne jako selhání právě zobrazeného toastu.
    - `ComObjects.Wrap(0)` hlásí srozumitelnou `InvalidOperationException` (dřív ji zakryl `Marshal.Release(0)`).
  - `AppShortcutRegistration` + `IShortcutStore` + `WindowsShortcutStore` (IShellLinkW + IPersistFile + IPropertyStore, `PKEY_AppUserModel_ID`):
    - Zástupce `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Prusa Tray Diagnostic.lnk` → `Environment.ProcessPath`, AUMID `PrusaTrayDiagnostic`.
    - Nejdřív se čte. Zapisuje se, jen když zástupce chybí, cíl je jinde (exe se přesunulo; cesty se porovnávají normalizovaně a bez ohledu na velikost písmen), AUMID nesedí nebo soubor nejde přečíst. Běžný start tedy jen přečte jeden malý soubor.
- **Chování podle situace:**
  - Kliknutí, když aplikace běží: argument přijde přes `ToastNotification.Activated` na vlákně OS a otevře se web tiskárny.
  - Kliknutí, když aplikace neběží (nebo na toast z předchozího běhu v centru oznámení): nic se nestane, toast zmizí. Aplikace nemá COM aktivátor (rozhodnuto: řeší T-017).
  - Příliš dlouhé id tiskárny (argument po zakódování delší než `ToastLaunchArgument.MaxLength` = 512 znaků): toast se zobrazí bez argumentu `launch`, kliknutí nic neotevře a služba zaloguje varování (s délkou id, ne s id). Délka obsahu toastu tak zůstane pod limitem Windows (~5 kB).
  - První start (zástupce právě vznikl): Windows může první toasty chvíli ignorovat, než zástupce zaregistruje. Proto se registrace spouští hned po startu.
  - Dev (`dotnet run`) a publikované exe mají stejné AUMID. Zástupce se přepne na exe, které běželo naposledy.
- **Co aplikace zanechá v systému (pro odinstalaci / T-017):**
  - zástupce `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Prusa Tray Diagnostic.lnk`,
  - klíč, který vytvoří Windows: `HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\PrusaTrayDiagnostic` (uživatelovo nastavení oznámení aplikace),
  - toasty v centru oznámení (při odinstalaci je lze smazat přes `ToastNotificationManager.History.Clear("PrusaTrayDiagnostic")`).
  - Instalátor (T-017) by měl vytvořit zástupce se stejným AUMID; aplikace ho pak jen přečte a nepřepisuje.
- **Testy (App.Tests).** Žádný test nezobrazí toast ani nesáhne na Start menu či registr.
  - `ToastContentBuilderTests`: texty všech druhů událostí CZ/EN, zbývající čas, soubor, zvuk, launch argument, pokus o XML injection, neplatné znaky, zkracování.
  - `ToastLaunchArgumentTests`: round-trip (diakritika, emoji, `&<>"'`), odmítnutí cizích argumentů.
  - `ToastActivationRouterTests`:
    - otevření webu, id bez ohledu na velikost písmen, Prusa Connect, změna adresy,
    - neznámá a odebraná tiskárna,
    - nebezpečné adresy (`file:`, `ftp:`, údaje v URL; adresa se nezaloguje),
    - neplatný argument, selhání launcheru, souběh.
  - `ToastNotificationServiceTests` (falešná platforma):
    - vlákno na pozadí, jazyk, zvuk, 8 vláken × 4 notifikace,
    - neblokující `Show` a zahazování při plné frontě,
    - selhání toastu i inicializace (log jednou, pak no-op), `Start`,
    - po `Dispose` no-op; `Dispose` nečeká na zaseknutý toast a session se uvolní na svém vlákně,
    - předání kliknutí a selhání handleru.
  - `AppShortcutRegistrationTests` (paměťový store):
    - vytvořit / aktuální / aktualizovat (přesun exe, jiné či chybějící AUMID, nečitelný soubor),
    - selhání zápisu, cesta do Start menu uživatele.
  - `WindowsShortcutStoreTests`: skutečný IShellLinkW/IPropertyStore, ale jen v `%TEMP%\PrusaTrayDiagnostic.App.Tests.Shortcuts\<guid>`. Složku smaže test, po pádu běhu i assembly fixture.
  - `WinRtToastInteropTests`: skutečný WinRT bez notifieru – XML z builderu Windows přijme, přihlášení a odhlášení kliknutí funguje, chybné XML vyhodí výjimku, IID odpovídají `[Guid]`.
  - `AppServicesTests`: Windows → `ToastNotificationService`, jiný OS → `NullNotificationService`, router je pozorovatel session.
  - Po review:
    - `DisposablesTests`: uvolní se vše i při selhání, selhání se nahlásí, ani házející handler nezastaví zbytek.
    - `WinRtToastInteropTests`: `WinRtToast.Dispose` přežije selhání odhlášení (`ObjectDisposedException`, `ArgumentException`, jiná výjimka; falešný `IToastNotification`), `Wrap(0)` hlásí `InvalidOperationException`.
    - Testy omezení délky argumentu `launch` (`ToastLaunchArgumentTests`, `ToastContentBuilderTests`, `ToastNotificationServiceTests` včetně varování v logu).
- **Native AOT:**
  - Žádný nový balíček, žádná reflexe, žádné `BuiltInComInteropSupport`. `dotnet build` bez varování (analyzátory AOT/trim jsou zapnuté).
  - `scripts/aot-smoke.ps1` prošel: publish win-x64 bez varování z našeho kódu (dvě hlášky ILC pocházejí z Avalonia DComposition a byly tam i dřív). Exe běží 10 s a AOT binárka vytvořila zástupce přes generovaný COM interop.
  - Skript teď na Windows ověří, že zástupce vznikl a ukazuje na publikované exe, a pak vrátí předchozí stav (zástupce obnoví, nebo smaže).
  - Aplikace ve skriptu běží se skutečným nastavením uživatele, takže může ukázat skutečné toasty (např. chybu tiskárny při startu). Izolované umístění dat aplikace kód nenabízí (cesta jde přes `SHGetKnownFolderPath`) a nové nastavení se kvůli tomu nezavádělo.
  - Pokud centrum oznámení před během prokazatelně neobsahovalo žádné toasty aplikace, skript je po běhu smaže. Jinak je nechá (uživatelovy toasty nikdy nemaže) a vypíše varování.
  - Úpravy skriptu po druhém review:
    - Počet toastů se čte striktně: kontroluje se návratový kód a právě jeden řádek s číslem. Při jakémkoli selhání je stav neznámý a nic se nemaže.
    - Skript odmítne běžet, když už PrusaTrayDiagnostic běží. Pokud se během běhu objeví jiná instance, toasty nemaže.
    - Zástupce se obnovuje jako první. Chyba při úklidu toastů jen vypíše varování.
  - Po opravách z review skript prošel znovu: zástupce i historie toastů byly po běhu prázdné.
- **Ověření naživo (Windows 11, 2026-10-16):**
  - Aplikace spuštěná z `bin/Debug` s dočasným (už odstraněným) háčkem poslala dva tiché toasty („Print finished“ a „10 min remaining“) pro tiskárnu s názvem `MK4 <test> & "co"`.
  - Zástupce vznikl a Windows toasty přijal: jsou v `ToastNotificationManager.History` pro `PrusaTrayDiagnostic` s očekávaným, správně escapovaným XML a `silent="true"`. Nastavení aplikace je `Enabled`.
  - Banner na snímcích obrazovky vidět nebyl. Ikona oznámení na hlavním panelu zřejmě ukazovala režim Nerušit, ve kterém Windows toasty posílá rovnou do centra oznámení. Neověřeno.
  - Po ověření byla historie toastů smazána a zástupce odstraněn (při dalším startu vznikne znovu).
- **Merge `develop` a integrace s T-029 (jazyk aplikace), T-015 a T-013 (2026-10-18):**
  - Konflikty vyřešené tak, že zůstalo chování obou stran:
    - `AppServices`: pozorovatelé session z develop (`UiLanguageObserver` první, historie, `MonitoredPrinters`) a navíc `ToastActivationRouter`.
    - `TrayHost`: `session.Start(initialSettings)` z develop, hned po něm `ToastNotificationService.Start()`.
    - `MonitoringSession`: návrh z develop (`ApplyAsync`, `UpdateSettings`, `CurrentSettings`), ponechaná interní vlastnost `Observers` (používá ji test kompozice).
    - `Strings.resx` / `Strings.cs.resx`: sjednocení klíčů (13 klíčů `Notification*` + klíče z develop, žádné duplicity).
    - `tasks/README.md` a `docs/architecture.md`: řádky a body obou stran (T-010 dál rozpracovaný, body z develop přečíslované na 6–10).
  - **Překlady:** 13 klíčů `Notification*` doplněno do `Strings.{sk,de,es,fr,it,pl}.resx`. Terminologie a styl podle T-029 a podle přeložených textů upozornění v nastavení (`SettingsNotify*`): vykání (Sie, vous, usted), „offline/online“ jako ve stavech, ve francouzštině nezlomitelná mezera před dvojtečkou („Temps restant : {0}“), zbývající čas stejným obratem jako v tooltipu („Noch {0}“, „Quedan {0}“, „Mancano {0}“, „Pozostało {0}“, „Zostáva {0}“). Test shody klíčů a zástupných symbolů (`StringsTests`) prochází. Překlady by měl projít rodilý mluvčí (stejně jako ostatní texty T-029).
  - **Jazyk toastů za běhu:** `ToastNotificationService` už nedostává kulturu zachycenou při vytvoření. Dostává `Func<CultureInfo>` (v aplikaci `UiLanguage.CurrentTextCulture` = `Strings.Culture`, jinak UI kultura vlákna, tedy systému). Volá ji jednou pro každý toast těsně před sestavením XML a výsledek předá `ToastContentBuilder.Build(notification, culture)`. Builder čte každý text přes `Strings.ResourceManager.GetString(nameof(Strings.X), culture)`, jednotky zbývajícího času přes `TrayToolTipFormatter.FormatDurationIn(čas, culture)`. Chování traye se nemění, `FormatDuration(čas)` dál sleduje zvolený jazyk; nová metoda má jiný název, aby analyzátor CA1304 nevyžadoval kulturu u volání v trayi. Přepnutí jazyka během sestavování tak nemůže v jednom toastu smíchat jazyky. UI kultura vlákna toastů se vůbec nemění, takže „Podle systému“ i po předchozím přepnutí znamená jazyk systému. Formát čísel (`CurrentCulture`) se nemění, stejně jako v trayi. Selhání zdroje jazyka se zaloguje jako selhání toastu a další toast se zobrazí.
  - **Nastavení z T-015 a více tiskáren z T-013:** zvuk (`notifications.sound`), přepínače upozornění i názvy tiskáren jdou do toastu přes `PrintEventNotifier.Configure`. Volá ho `PrintNotificationObserver`:
    - při `UpdateSettings`: uložení, které mění jen upozornění (např. zvuk), bez restartu monitoringu;
    - při `ApplySettings`: změna tiskáren včetně přejmenování restartuje běh. Přejmenovaná tiskárna (stejné id a adresa) si stav ponechá.
    - Obojí platí pro další toast. Titulek toastu je název tiskárny, které se událost týká (u více tiskáren každá svým názvem).
  - **Testy:**
    - `ToastNotificationServiceTests`: jazyk podle zdroje, změna jazyka po vytvoření služby platí pro další toast (en → pl → it; jazyk se přepíná až po zobrazení předchozího toastu, jinak byl test nestabilní), selhání zdroje jazyka. Nově `SavedSettingsReachTheNextToast`: pozorovatel + notifier + služba s falešnou platformou, dvě tiskárny. Zvuk Off → All přes `UpdateSettings`, přejmenování přes `ApplySettings` → další toasty mají nový zvuk, nový (escapovaný) titulek, správný argument `launch` a obnovení tisku se po přejmenování hlásí jako přechod.
    - `ToastContentBuilderTests` a `WinRtToastInteropTests` předávají kulturu explicitně; kontrola `null` kultury.
    - `Localization/ToastLanguageSwitchTests` (skupina `GlobalLanguageGroup`, běží samostatně, vrátí jazyk i `DefaultThreadCurrentUICulture`): skutečné `UiLanguage.Apply` en → de → sk mění text dalšího toastu; návrat na „Podle systému“ po francouzštině dá zase jazyk systému; `CurrentTextCulture` na libovolném vlákně; explicitně předaná kultura vyhraje nad zvoleným jazykem (`Strings.Culture`) v textu i v jednotkách času, tray dál sleduje zvolený jazyk.
    - Falešná platforma toastů je přesunutá do `Notifications/ToastFakes.cs`.
  - **Ověření (Windows 11):** `dotnet build` 0 varování, `dotnet test` 1844 testů (1843 prošlo, 1 přeskočený existující), `PrusaTrayDiagnostic.App.Tests` 10× po sobě bez chyby. `scripts/aot-smoke.ps1` prošel: publish win-x64 (jen dvě známé hlášky ILC z Avalonia DComposition), exe běželo 10 s, zástupce ukazoval na publikované exe. Toast v jiném jazyce naživo neověřen.
- **Co ověří uživatel ručně:**
  1. Vypnout režim Nerušit, spustit aplikaci (`dotnet run --project src/PrusaTrayDiagnostic.App`) s nastavenou tiskárnou a vyvolat událost (např. pozastavit a obnovit tisk, nebo tiskárnu odpojit od sítě a počkat na „offline“). Měl by se ukázat banner s názvem tiskárny.
  2. Kliknout na banner (nebo na toast v centru oznámení, dokud aplikace běží) → v prohlížeči se otevře PrusaLink tiskárny.
  3. Volitelně: při výchozím nastavení zvuk u chyby a „vyžaduje pozornost“, ostatní potichu.
  4. Volitelně (T-029): v Nastavení přepnout jazyk (např. Deutsch), uložit a vyvolat událost → toast je v novém jazyce bez restartu aplikace.

- **Automatizace ruční kontroly (2026-10-22, architect):** kliknutí na toast (banner i v centru oznámení) a toast v novém jazyce ověří E2E testy T-068 (toasty testovací instance z T-067, infrastruktura zásahů T-065). Kritérium se zaškrtne, až testy projdou. Slyšitelný zvuk zůstává doporučená ruční kontrola (přehled pokrytí v T-065).
