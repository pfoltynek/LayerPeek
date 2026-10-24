# Architektura

## Přehled

LayerPeek je **tray-only** aplikace (Windows: oznamovací oblast, macOS: menu bar) bez hlavního okna. Z menu ikony lze otevřít jen pomocná okna (nastavení, diagnostika, kamera).

```
┌───────────────────────────────── LayerPeek.App (Avalonia) ────────────────────────────────┐
│  TrayIcon + NativeMenu │ ViewModels │ pomocná okna │ platformní služby (notifikace, autostart) │
└───────────────┬───────────────────────────────────────────────────────────────────────────┘
                │ DI (composition root)
┌───────────────▼──────────────┐   ┌──────────────────────────────┐
│ LayerPeek.PrusaLink          │   │ LayerPeek.PrusaConnect       │
│  REST klient (lokální síť)   │   │  cloudový klient             │
└───────────────┬──────────────┘   └───────────────┬──────────────┘
                └──────────────┬───────────────────┘
               ┌───────────────▼───────────────┐
               │        LayerPeek.Core         │
               │ doménový model, IPrinterClient│
               │ monitoring, detekce událostí  │
               │ abstrakce (INotificationService, ISecretStore…) │
               └───────────────────────────────┘
```

## Pravidla vrstev

- **Core** nezná Avalonii ani konkrétní HTTP API. Obsahuje doménu, rozhraní a logiku (polling, detekce událostí).
- **PrusaLink / PrusaConnect** implementují `IPrinterClient`; DTO (tvar JSON) jsou oddělené od doménových modelů.
- **Camera.FFmpeg** (T-014) implementuje `ICameraFeedFactory` z `Core/Cameras`: RTSP/H.264 přes FFmpeg (FFmpeg.AutoGen, P/Invoke) a převod snímků na BGRA. Závisí jen na Core (`App -> Camera.FFmpeg -> Core`). Nativní knihovny FFmpeg (LGPL build BtbN, připnutá URL + SHA256) stahuje a ověřuje `build/FFmpeg.targets` při buildu, zatím jen pro `win-x64` (macOS: T-026). Znovupřipojení, timeouty a stav připojení řeší `Core/Cameras/ReconnectingCameraFeed`; App jen vykresluje snímky do `WriteableBitmap` (okno kamery, malý náhled).
- **App** je composition root: registruje služby, implementuje platformní rozhraní, zobrazuje stav. UI pouze odebírá snapshoty z monitorovací služby.
- **tools/PrusaSimulator** (T-045) je samostatný vývojový nástroj mimo aplikaci: simulované tiskárny PrusaLink (Kestrel ze sdíleného frameworku ASP.NET Core) a RTSP kamera Buddy3D. Nereferencuje kód aplikace (tvar API napodobuje podle fixture), nepublikuje se s aplikací a neprochází AOT analýzou. Kameru simulátoru používají i testy FFmpeg. Návod: [simulator.md](simulator.md).

## Tok dat

1. `IPrinterMonitor` (Core) periodicky volá `IPrinterClient.GetSnapshotAsync` pro každou tiskárnu.
2. Nový `PrinterSnapshot` se publikuje odběratelům (tray ikona, menu, diagnostika).
3. Detektor událostí porovná předchozí a nový snapshot a při změně (dokončeno, chyba…) zavolá `INotificationService`.
   - `Core/Events/PrintEventDetector` je čistý stavový automat (bez I/O). `Core/Notifications/PrintEventNotifier` odebírá `IPrinterMonitor.SnapshotUpdated`, filtruje události podle `NotificationSettings` a volá `INotificationService` (T-009).
   - App předává nastavení každého běhu přes `IMonitoringObserver.ApplySettings` (`App/Notifications/PrintNotificationObserver`).
4. Na Windows je `INotificationService` = `App/Notifications/ToastNotificationService` (T-010): `Show` jen zařadí notifikaci do omezené fronty, vlastní vlákno sestaví XML toastu (`ToastContentBuilder`) a zobrazí ho přes WinRT (`Platform/Windows/WindowsToastPlatform`, vlastní interop přes `[GeneratedComInterface]`). Jazyk textů se určí jednou pro každý toast těsně před sestavením (`UiLanguage.CurrentTextCulture`, viz bod 10) a všechny texty toastu se čtou přímo v něm (`ToastContentBuilder.Build(notification, culture)`), takže přepnutí jazyka platí hned pro další toast a jeden toast nikdy nesměšuje jazyky. Zvuk (`PlaySound`) a název tiskárny v titulku určuje `PrintEventNotifier` podle nastavení aktuálního běhu: změna jen upozornění (např. zvuku) přijde bez restartu monitoringu přes `UpdateSettings`, přejmenování tiskárny restartuje běh a přijde přes `ApplySettings`; obojí platí pro další toast. Na ostatních OS je zaregistrovaná `NullNotificationService`, dokud neexistuje T-020.
5. Kliknutí na toast vrátí argument s id tiskárny; `App/Notifications/ToastActivationRouter` (také `IMonitoringObserver`) ho přeloží na aktuálně nastavenou tiskárnu a otevře její web přes `PrinterWebAddress` a `IUrlLauncher`. Kliknutí přichází dvěma cestami: událost `Activated` živého toastu (banner) a COM aktivátor toastů (T-068: `Platform/Windows/ToastActivatorServer`, `INotificationActivationCallback` přes `[GeneratedComClass]`, CLSID ve vlastnosti zástupce `System.AppUserModel.ToastActivatorCLSID` a registrace `HKCU\Software\Classes\CLSID\{clsid}\LocalServer32`). Jen přes aktivátor Windows hlásí kliknutí v centru oznámení a jen přes něj aplikaci spustí, když neběží (`-ToastActivated`/`-Embedding`). Spuštění kvůli kliknutí nejdřív zaregistruje aktivátor, počká na kliknutí (`ToastActivationInbox`) a pak ho buď předá běžící instanci příkazem `toast:<argument>` (T-043), nebo ho zpracuje samo, až aplikace běží. Router opakované hlášení téhož kliknutí do 2 s ignoruje a kliknutí, které aplikaci spustilo, počká na první nastavení monitoringu.
6. Historie telemetrie (T-012): `Core/History/TelemetryHistory` drží pro každou tiskárnu v paměti kruhový buffer vzorků (`TelemetrySample`, 48 B, bez objektů na haldě) za posledních 24 h, bez ukládání na disk. Plní ho `App/History/TelemetryHistoryObserver` ze `SnapshotUpdated` a při každém běhu mu předá tiskárny a interval. Graf teplot (T-011) čte rozsah přes `CopySamples` do vlastního bufferu a překresluje jen při změně `GetVersion` / události `Changed`.
7. Okno diagnostiky (T-011) z tray menu: `App/Diagnostics/DiagnosticsViewModel` vzniká při otevření okna a při zavření se uvolní (odhlásí se od `IPrinterMonitor` a `MonitoredPrinters`). Historii odebírá jen graf: k `TelemetryHistory.Changed` se přihlásí při připojení do okna a odhlásí při jeho zavření. Zavřené okno tak nic nestojí. Tiskárny aktuálního běhu bere z `App/Tray/MonitoredPrinters` (`IMonitoringObserver`), informace o tiskárně a firmwaru z `IPrinterInfoSource` (PrusaLink `/api/version` + `/api/v1/info`). Graf `App/Diagnostics/Chart/TemperatureChart` je vlastní Avalonia control; přípravu dat (kopie, decimace na sloupce pixelů, přerušení čar) dělá `TemperatureChartData` bez Avalonie. Jediná instance okna a jeho zavření při ukončení aplikace řeší `App/Windows/AuxiliaryWindowSlot` (stejně pro okno O aplikaci).

8. Více tiskáren (T-013): jedna agregovaná ikona. `App/Tray/TrayStatusRules` vybírá nejzávažnější tiskárnu (chyba > vyžaduje zásah > offline > pozastaveno > tiskne > zaneprázdněna > hotovo > nečinná; při shodě první v konfiguraci), tooltip má pro víc tiskáren jeden řádek na tiskárnu (nejzávažnější první, při nedostatku místa „Další tiskárny: N“). V menu má každá tiskárna vlastní podmenu, i když je jen jedna (T-042, dřív byla jedna tiskárna plochou položkou a kliknutí na ni otevřelo web): „Otevřít PrusaLink“ / „Otevřít Prusa Connect“ a u tiskárny se zapnutou kamerou „Zobrazit kameru“ / „Skrýt kameru“ (obyčejná položka, text podle toho, zda je okno kamery otevřené; od 2026-10-22 místo zaškrtávací „Kamera“). Řádek tiskárny jen otevírá podmenu. „Diagnostika…“ je jediná globální položka hlavního menu nad „Nastavení…“ (rozhodnutí uživatele 2026-10-21; okno má vlastní výběr tiskárny, už otevřené okno si tiskárnu ponechá). Povolená je jen tehdy, když menu ukazuje tiskárny; při „Spouštění…“, po selhání monitorování a bez tiskárny je zakázaná. Celé platformní menu se přestaví jen při změně jazyka (`FixedItemsChanged`). Přepnutí povolení „Diagnostika…“ změní jen tuto jednu položku na místě (`FixedItemChanged` → `ITrayMenuTarget.UpdateFixedItem`, rozhodnutí uživatele 2026-10-21), takže otevřené menu se nezavře ani nepřestaví; při dotazu na tiskárnu se pevné položky nemění vůbec. Bez tiskárny menu ukáže zakázaný text „Není nastavena žádná tiskárna“ a „Přidat tiskárnu…“ (otevře nastavení na záložce Tiskárny a přidá tiskárnu, `SettingsViewModel.AddPrinterFromTray`). Název tiskárny se v menu zkracuje na 32 znaků, celý je v tooltipu položky. Pevné položky a položky podmenu mají klávesové zkratky z `.resx` (`_`), tiskárny žádné (`NativeMenuText.EscapeAccessKeys`, obchází chybu `AccessText` v Avalonii 12). Dotaz jedné tiskárny stojí O(1) v menu a O(n) bez alokace u ikony; text se formátuje a menu mění jen při viditelné změně, strukturu menu mění jen změna sady tiskáren.

9. Okno nastavení (T-015) z tray menu: `App/Settings/SettingsViewModel` (+ `PrinterEditorViewModel` pro každou tiskárnu) vzniká při otevření okna a při zavření se uvolní (zruší rozběhnutý test spojení, zapomene napsaná hesla). Načítá a ukládá přes `ISettingsEditorStore` (`SettingsEditorStore`): nejdřív nová hesla do `ISecretStore` (s možností vrátit předchozí hodnotu), pak atomicky `settings.json`; při chybě se zapsaná hesla vrátí. Po uložení `ISettingsApplier` (= `MonitoringSession.ApplyAsync`) zruší běžící běh monitoru a spustí nový s novým nastavením; pozorovatelé (ikona, menu, notifikace, historie) dostanou nastavení přes `ApplySettings` a stav tiskáren se stejným id, adresou a typem připojení si ponechají. Když se tiskárny ani interval nemění (jen upozornění), monitor běží dál a pozorovatelé dostanou jen `UpdateSettings`. Hesla odebraných tiskáren se mažou až po uložení a po skončení předchozího běhu monitoru; snapshot, který doběhne po požadavku na zastavení běhu, se nepublikuje. „Otestovat spojení“ (`PrusaLinkConnectionTester`) volá `PrusaLinkClient.GetInfoAsync` s neuloženými hodnotami formuláře, napsané heslo předá klientovi jen v paměti. Důvod chyby vrací PrusaLink klient jako `PrusaLinkFailure`.

10. Jazyk aplikace (T-029): `AppSettings.Language` (kód z `Core/Settings/AppLanguages`, `null` = podle systému) se ukládá do `settings.json` jako `"language"`. Při startu `TrayHost.StartAsync` nejdřív načte nastavení (mimo UI vlákno), `App/Localization/UiLanguage` nastaví `Strings.Culture` a teprve pak vzniknou tray view modely, menu a tooltip; `MonitoringSession.Start(settings)` už nastavení znovu nenačítá. Za běhu jazyk aplikuje `UiLanguageObserver` (první `IMonitoringObserver`, `ApplySettings` i `UpdateSettings`) na UI vlákně. Texty v XAML jsou vázané přes `LocalizedStrings` (`{Binding [Klíč], Source={x:Static loc:LocalizedStrings.Instance}}`, kompilovaný indexer binding, slabý odběr), takže se otevřená okna přepnou sama; texty, které formátují view modely, přeformátuje `ILanguageAware.RefreshTexts` (tray přes `TrayHost`, otevřená okna přes `AuxiliaryWindowSlot.RefreshTexts`). `CurrentCulture` (čísla, data) se nemění.

9. Otevření webu PrusaLink přihlášený (T-028): `App/Tray/Menu/PrinterWebOpener` získá adresu z `Core/Web/IPrinterWebGateway.OpenAsync`. Výsledek `PrinterWebAccess` je buď adresa proxy (`IsProxied`), nebo holá adresa tiskárny s důvodem (`PrinterWebFallback`). Brána se volá mimo UI vlákno, jen když položka menu ukazuje aktuální adresu své tiskárny. Adresu proxy otevře `IUrlLauncher.OpenSecretAsync`, který do logu nezapíše adresu ani text výjimky (`Process.Start` do něj vkládá celou adresu s tokenem). Při záložní cestě se otevře holá adresa bez další notifikace a do logu jde jen název důvodu. Po uloženém nastavení zastaví `SettingsEditorStore` proxy odebraných tiskáren a tiskáren se změněnou adresou, jménem, typem připojení nebo heslem (`StopAsync`, souběžně s restartem monitoringu, nejvýš 5 s, před smazáním hesel odebraných tiskáren). Při ukončení aplikace `TrayHost` zavolá `StopAllAsync` (čeká nejvýš 1 s souběžně se zastavením monitoringu) a pak kontejner DI bránu uvolní přes `DisposeAsync`. Implementace `PrusaLink/WebProxy/PrusaLinkWebGateway` (registrovaná v `AddPrusaLink`) spouští líně pro každou tiskárnu vlastní lokální reverzní proxy. Proxy je Kestrel ze sdíleného frameworku ASP.NET Core na `127.0.0.1`, port `0`, má 256bitový token v první části cesty a kontroluje `Host`. Na Digest výzvy tiskárny odpovídá sama přes vlastní instanci `DigestAuthenticator`, takže heslo do prohlížeče nejde. Před otevřením prohlížeče proběhne přihlášený `GET /api/version`. Bez hesla, při odmítnutém heslu, nedostupné tiskárně nebo když proxy nejde spustit se otevře holá adresa. Proxy se zastaví po 10 minutách bez požadavku, přes `StopAsync`/`StopAllAsync` po změně nastavení tiskárny a při ukončení aplikace (`DisposeAsync`). Podrobnosti jsou v [api/prusalink.md](api/prusalink.md), oddíl „Lokální proxy“.

## Překryv okna kamery – widgety (plán T-076 až T-093)

Jediný `Border` s řádky z T-036 nahradí systém widgetů. Stav je **plán**. Otázky rozhodl uživatel 2026-10-24, implementace UI čeká na schválení návrhu T-076.

- **Core (`Core/Cameras/Overlay`, T-077):**
  - `CameraOverlayLayout` je seznam `OverlayWidgetPlacement`. Každé umístění má textové id widgetu, jednu ze 6 kotev `OverlayAnchor`, pořadí v kotvě (pořadí v seznamu), velikost S/M/L, zvýraznění a viditelnost.
  - Každý typ widgetu je v rozložení nejvýš jednou (v1). Normální okno a celá obrazovka sdílejí jedno rozložení.
  - Rozložení je **u tiskárny** v `printers[*].camera.overlay`. Akce „použít u všech tiskáren“ je čistá operace. Neznámé id widgetu se zachová a zapíše zpět.
  - **Průhlednost pozadí karet** je jediná **globální** hodnota v `AppSettings` (30–95 %, výchozí 72 %).
  - `settings.json` má `version` 2. Starý tvar T-036 (`corner` / `normal` / `mini`) se při načtení převede a už se nezapisuje:
    - viditelnost starých položek se zahodí, začíná se z výchozího rozložení,
    - starý roh určí, kam se výchozí rozložení zrcadlí (hlavní kotva Stav + Časy skončí ve starém rohu).
  - Operace nad rozložením jsou čisté funkce bez Avalonie: přesun do kotvy a na pozici, velikost, zvýraznění, viditelnost, obnovení, zrcadlení.
  - Mimo okno nastavení zapisuje rozložení samostatný zapisovač sekce (po vzoru `CameraMonitorPathWriter`). Zápis ohlásí, takže otevřené okno nastavení znovu načte jen rozložení dané tiskárny a ostatní neuložené změny si ponechá (T-092).
- **App – framework (T-081):**
  - `OverlayWidgetRegistry` se plní **explicitním seznamem** v composition rootu (Native AOT: žádné skenování ani `Activator`). Nový widget je jedna třída (`IOverlayWidgetDefinition` + view model + `DataTemplate` s `x:DataType`) a jeden řádek registrace.
  - Hostitel rozdělí viditelné widgety do 6 kotev nad obrazem normálního okna a celé obrazovky. V malém náhledu karty nejsou.
  - Widget formátuje texty jen při změně svého levně porovnatelného obsahu, stejně jako `CameraPrintContent`.
  - Zvýraznění vykreslí klíčový údaj widgetu akcentovou barvou `#FA6831`.
  - Ikony jsou z Fluent UI System Icons (MIT), vložené jako geometrie bez balíčku a fontu. Licence je v přehledu licencí třetích stran.
- **Widgety v1:** Stav (T-082), Časy (T-083), Teploty se sparkline (T-084), Název tisku (T-085, s náhledem G-code T-087), Vychladnutí (T-088).
  - Sparkline je kompaktní režim stávajícího `TemperatureChart` s pevným oknem 60 minut, ne druhý graf.
  - Barvy trysky (oranžová) a podložky (modrá) jsou sdílené zdroje a používá je i graf okna Diagnostika.
- **Data mimo snapshot:**
  - **Rozbor názvu souboru PrusaSliceru** (`Core`, T-078): sentence case, model tiskárny se vynechá. Firmware Buddy metadata souboru v `/api/v1/job` neposílá.
  - **Stav vychladnutí** (`Core/Cooldown/CooldownMonitor`, T-079):
    - stavový automat po vzoru `PrintEventDetector`, čas bere z `TimeProvider`,
    - začne po dokončeném i zastaveném tisku, skončí při dalším tisku, offline nebo znovu zapnutém topení trysky,
    - práh trysky je u tiskárny (30–100 °C, výchozí 50 °C).
  - **Událost „vychladlo“** jde přes `INotificationService` jako nový `PrintEventKind` (T-080). Ve výchozím stavu je vypnutá a je informační (zvuk jen při nastavení „všechny“). Nepřijde, když tisk skončil už pod prahem.
  - **Náhled G-code** (T-086, hotovo; zobrazení T-087):
    - Odkaz na náhled nese `PrintJob.PreviewReference` (`Core/Previews/PrintPreviewReference`). Je neprůhledný, rozumí mu jen klient, který ho vytvořil. PrusaLink ho plní z `file.refs` v `/api/v1/job`, spolu s názvem souboru jednou na úlohu.
    - `IPrintPreviewSource.GetAsync(tiskárna, úloha, velikost)` vrací `PrintPreview?`: bajty PNG a rozměry z hlavičky. Chyby se nevyhazují, znamenají „bez náhledu“. Dekódování na bitmapu dělá App.
    - Implementace je `Core/Previews/PrintPreviewCache` nad `IPrintPreviewFetcher` (PrusaLink: `PrusaLinkPreviewFetcher`, registrovaný v `AddPrusaLink`). Cache je jen v paměti, nic na disk.
    - Stahuje se líně při prvním `GetAsync`, jednou na úlohu a velikost. Souběžné požadavky sdílí jedno stažení, každý volající může čekání zrušit sám.
    - Náhled zůstává po dokončení nebo zastavení tisku i bez úlohy (nečinná / offline tiskárna) až do další úlohy. `Observe(snapshot)` ho uvolní, jakmile snapshot ukáže jinou úlohu. Další tisk téhož souboru se ptá s `If-None-Match` (ETag) a při `304` použije uložený náhled.
    - Chybějící náhled (`404`) se pamatuje pro úlohu. Přechodná chyba se zkusí znovu nejdřív po 30 s.
    - `RetainOnly` / `Remove` zahodí odebrané tiskárny a tiskárny se změněnou adresou nebo typem připojení, běžící stažení zruší. V cache je nejvýš 32 tiskáren (nejdéle nepoužitá jde pryč).
    - Zapojení do composition rootu a volání `Observe` / `RetainOnly` dělá App (T-087).
- **Malý náhled (T-089, T-090):**
  - Údaje nejsou přes obraz, ale v informačním pásu **pod** obrazem. Obraz má poměr streamu, výchozí 16:9.
  - Velikost 320×180 z nastavení je velikost obrazu, pás se přičte. Minimální šířka je 240.
  - Výška okna se dopočítá po změně velikosti úchytem a při rozbalení řádků (název, graf). Okno roste od nejbližšího okraje obrazovky a ořízne se na pracovní plochu.
  - Stav sbalení (výchozí sbaleno) je u tiskárny se stavem okna v `camera-windows.json` (`CameraWindowStateStore`), ne v `settings.json`. Ukládá se i při vypnutém „pamatovat poslední umístění“ a zapomene se spolu se stavem okna.
- **Režim rozložení (T-091):**
  - Je jen v normálním okně, vstup tlačítkem v řadě tlačítek okna a z kontextové nabídky.
  - Pracovní kopie rozložení je ve view modelu bez Avalonie. Tažení do kotev a v rámci kotvy, popover (zvýraznit, S/M/L), ovládání klávesnicí.
  - „Hotovo“ uloží přes zapisovač z T-077, Esc zahodí.

## Prusa Connect (cloud)

Podle T-004 nemá Prusa Connect oficiální API pro uživatelské aplikace. Podrobnosti a rozhodnutí, která čekají na uživatele, jsou v [api/prusa-connect.md](api/prusa-connect.md). Pokud se klient (T-005) bude implementovat, platí pro něj:

- Přihlašovací údaje patří k **účtu** Prusa Account, ne k tiskárně. Jeden účet obsluhuje víc `PrinterDefinition` (`Address` = UUID tiskárny).
- Klienti téhož účtu sdílejí jeden dotaz na seznam tiskáren za cyklus pollingu, aby se nepřetěžovala služba.
- Trvale se ukládá jen refresh token, a to přes `ISecretStore` pod klíčem pro účet. Access token zůstává jen v paměti.
- Kód pro OAuth/PKCE a obnovu tokenů je v `LayerPeek.PrusaConnect` a nezávisí na UI. UI přihlášení (T-022) je v App.
- Klient jen čte a je izolovaný za `IPrinterClient`, takže ho jde při změně nedokumentovaného API vypnout bez dopadu na PrusaLink.

## Platformní rozdíly

| Oblast | Windows | macOS |
|---|---|---|
| Ikona | oznamovací oblast, barevná ikona kreslená za běhu (`ITrayIconSet`) – T-007 | menu bar, doporučená monochromatická „template“ ikona – T-018 |
| Notifikace | toast přes vlastní WinRT interop, AppUserModelID přes zástupce ve Start menu uživatele – T-010 | User Notifications (vyžaduje .app bundle) – T-020 |
| Skrytí z docku/taskbaru | bez hlavního okna automaticky | `LSUIElement` v `Info.plist` – T-019 |
| Autostart, balení | vlastní zástupce aplikace ve složce Startup (`IAutostart`, volba v nastavení, výchozí vypnuto), instalátor Velopack (`docs/velopack.md`, T-056): hooky instalace/odinstalace v `Installer/VelopackStartup` jako první v `Main`, aktualizace `AppUpdates` (jen v instalaci) + `.zip`, bez podpisu – T-017 | Login Items, `.app` bundle, `.dmg` + `.tar.gz`, ad-hoc podpis – T-019 |
| Vydání | lokálně skripty T-055, RID `win-x64` a `win-arm64` (FFmpeg ARM64: T-057), nahrání do GitHub Releases | lokálně skripty T-055, RID `osx-arm64`, přidání do stejného GitHub vydání |
| Tajné údaje | Credential Manager (`[LibraryImport]`, roaming s profilem) – T-002 (hotovo) | Keychain (`Security.framework` přes `[LibraryImport]`) – T-021 |

Výběr platformní implementace (`ISecretStore`, `INotificationService`) probíhá v composition rootu v App podle OS (explicitní registrace kvůli Native AOT); registraci `ISecretStore` zavedl T-007 (`Composition/AppServices`): Windows → `WindowsCredentialSecretStore`, ostatní OS → `UnsupportedSecretStore` (hesla nedostupná, aplikace běží dál), dokud T-021 nedodá Keychain. `INotificationService` (T-010): Windows → `ToastNotificationService`, ostatní OS → `NullNotificationService`, dokud T-020 nedodá macOS notifikace. Úkoly s `platform: macos` se implementují až na Macu a Windows úkoly na nich nezávisí.

Konkrétní volby jsou otevřené otázky v příslušných úkolech (T-010, T-017, T-019, T-020, T-021, T-055, T-056).

## Logování

Aplikace loguje přes `Microsoft.Extensions.Logging` a `[LoggerMessage]`; jediný provider je vlastní souborový `FileLogProvider` (T-024, `App/Logging`, bez NuGet balíčku, kompatibilní s Native AOT).

- **Umístění:** Windows `%LOCALAPPDATA%\LayerPeek\logs`, macOS `~/Library/Logs/LayerPeek` (`AppDataPaths.GetDefaultLogDirectory`, vystavené jedním místem `AppEnvironment.LogDirectory()`). Testovací režim T-044 zapisuje do `logs` v testovací datové složce. Bez složky pro logy (OS ji neposkytne) aplikace běží dál, jen bez logu.
- **Formát:** `2026-10-21 14:03:12.345 +02:00 INF [pid 12345] Kategorie: zpráva`, úrovně `TRC DBG INF WRN ERR CRT`. Výjimka celá (`ToString()`, se stack trace) a další řádky zprávy pokračují na řádcích odsazených čtyřmi mezerami, takže každý neodsazený řádek je začátek záznamu. UTF-8 bez BOM, konce řádků `\n`.
- **Úroveň:** výchozí `Information`, v testovacím režimu `Debug`. Rozhoduje jen `LogLevelSwitch` (čtený při každém volání), takže úroveň jde přepnout za běhu bez nové factory. Volbu „Podrobné logování (Debug)“ v nastavení (T-047, `AppSettings.DebugLogging`) uplatňuje `DebugLoggingObserver`: načtené nastavení a každé uložení přepne úroveň na `Debug` / `Information` a změnu zaloguje. Do načtení nastavení a vždy u druhého spuštění platí `Information`, testovací režim zůstává na `Debug`.
- **Zápis:** volající jen naformátuje záznam a vloží ho do omezené fronty (10 000 záznamů); jedno vlákno na pozadí (ne thread pool) ji zapisuje po dávkách. Plná fronta nové záznamy zahodí. Když zápis dávky selže (plný disk, práva, druhý proces drží zámek souboru déle než 2 s), dávka se ztratí, ale aplikaci to neshodí. Počet ztracených záznamů uvede log v další úspěšné dávce. Smazaná složka se vytvoří znovu. `Flush(timeout)` počká na vše dosud zalogované (ukončení, T-046).
- **Dvě instance (T-043):** obě zapisují do stejného souboru. Každá dávka se zapíše jedním voláním na skutečný konec souboru pod pojmenovaným mutexem souboru (`FileMode.Append` v .NET by psal na vlastní pozici). Na Windows je mutex v globálním jmenném prostoru, protože týž uživatel přihlášený dvakrát (konzole + vzdálená plocha) má v každé relaci primární instanci, ale jeden soubor v `%LOCALAPPDATA%`. Záznam, který zůstal po přerušeném zápisu bez konce řádku, dostane `
` dřív, než se připíše další; soubor se otevírá na každou dávku se sdílením `ReadWrite | Delete`, takže ho nikdo nezamkne a po rotaci se píše vždy do aktuálního souboru.
- **Rotace:** podle velikosti, 1 MB × 5 souborů (`LayerPeek.log`, `LayerPeek.1.log` … `.4.log`, nejstarší se přepíše). Rotuje jen primární instance (zapne ji `Program.Main` po `SingleInstanceGuard`). Aktuální soubor se nejdřív jedním přejmenováním odsune na `LayerPeek.rotating.log`, teprve pak se posunou starší soubory (jen po první mezeru). Když aktuální soubor nejde přejmenovat (otevřený bez sdílení mazání), nic se nezmění a rotace se zkusí při dalším zápisu, aniž by přišel o starší soubory. Rotaci přerušenou pádem dokončí primární instance hned při prvním zápisu.
- **Životnost:** `AppLogging` (factory + provider) vzniká v `Program.Main` hned po `NativeLibrarySearch.Restrict()` a před `SingleInstanceGuard`, `Main` ho uvolní až po návratu z Avalonie. `App` ho předá `TrayHost`; kontejner DI factory nevlastní. `App.OnDesktopExit` jen vyprázdní frontu (při konci relace OS se `Main` nemusí dokončit). Po uvolnění se pozdní záznamy tiše zahodí.
- **Neošetřené výjimky (T-046):** `UnhandledExceptionLogger` se registruje v `Program.Main` hned po vytvoření logování (platí i pro druhé spuštění): `AppDomain.UnhandledException` (`Critical`, celá výjimka), `TaskScheduler.UnobservedTaskException` (`Error`, bez `SetObserved`) a po startu Avalonie `Dispatcher.UIThread.UnhandledException` (`Critical`, bez `Handled`). Výjimku z UI vlákna, kterou pak ukončující proces hlásí znovu, zapíše jen jednou. Po zápisu vyprázdní frontu logu (nejvýš 2 s) a sám nikdy nehází. Chování při pádu se nemění. Debug build má pro E2E testy testovací příkaz `crash:ui|background|task`.
- **Tajné údaje:** provider nic nemaskuje; volající nikdy nelogují hesla, klíče ani tokeny a adresy s přihlašovacími údaji redigují (`UriRedaction`).
- **Podpora (T-048, T-049):** okno O aplikaci nabízí uložení diagnostického balíčku, kopírování informací do schránky (`SupportText`: `SupportInfo` a posledních 200 řádků logu z `LogTail`, nejvýš asi 64 KB) a otevření složky s logy (`App/Support`). `SupportActions` vezme nastavení aktuálního běhu (před prvním během uložené), stavy tiskáren z `IPrinterMonitor` a log procesu (`SupportLog`: složka a `Flush`). `SupportInfo` sestaví anglický přehled (`info.txt`), `SettingsExport` (Core) nastavení bez dotazové části adres kamer a `SupportBundle` ZIP z logů (čtených se sdílením), `info.txt` a `settings.json`. Nedokončený ZIP se smaže. `UriRedaction.Redact` maskuje kromě přihlašovacích údajů i dotaz a fragment adresy (`?***`, `#***`), protože mohou nést token. Uživatelský návod je v [troubleshooting.md](troubleshooting.md).
