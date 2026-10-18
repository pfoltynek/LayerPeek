# Architektura

## Přehled

PrusaTrayDiagnostic je **tray-only** aplikace (Windows: oznamovací oblast, macOS: menu bar) bez hlavního okna. Z menu ikony lze otevřít jen pomocná okna (nastavení, diagnostika, kamera).

```
┌──────────────────────────── PrusaTrayDiagnostic.App (Avalonia) ───────────────────────────┐
│  TrayIcon + NativeMenu │ ViewModels │ pomocná okna │ platformní služby (notifikace, autostart) │
└───────────────┬───────────────────────────────────────────────────────────────────────────┘
                │ DI (composition root)
┌───────────────▼──────────────┐   ┌──────────────────────────────┐
│ PrusaTrayDiagnostic.PrusaLink│   │ PrusaTrayDiagnostic.PrusaConnect │
│  REST klient (lokální síť)   │   │  cloudový klient             │
└───────────────┬──────────────┘   └───────────────┬──────────────┘
                └──────────────┬───────────────────┘
               ┌───────────────▼───────────────┐
               │   PrusaTrayDiagnostic.Core    │
               │ doménový model, IPrinterClient│
               │ monitoring, detekce událostí  │
               │ abstrakce (INotificationService, ISecretStore…) │
               └───────────────────────────────┘
```

## Pravidla vrstev

- **Core** nezná Avalonii ani konkrétní HTTP API. Obsahuje doménu, rozhraní a logiku (polling, detekce událostí).
- **PrusaLink / PrusaConnect** implementují `IPrinterClient`; DTO (tvar JSON) jsou oddělené od doménových modelů.
- **App** je composition root: registruje služby, implementuje platformní rozhraní, zobrazuje stav. UI pouze odebírá snapshoty z monitorovací služby.

## Tok dat

1. `IPrinterMonitor` (Core) periodicky volá `IPrinterClient.GetSnapshotAsync` pro každou tiskárnu.
2. Nový `PrinterSnapshot` se publikuje odběratelům (tray ikona, menu, diagnostika).
3. Detektor událostí porovná předchozí a nový snapshot a při změně (dokončeno, chyba…) zavolá `INotificationService`.
   - `Core/Events/PrintEventDetector` je čistý stavový automat (bez I/O). `Core/Notifications/PrintEventNotifier` odebírá `IPrinterMonitor.SnapshotUpdated`, filtruje události podle `NotificationSettings` a volá `INotificationService` (T-009).
   - App předává nastavení každého běhu přes `IMonitoringObserver.ApplySettings` (`App/Notifications/PrintNotificationObserver`). Dokud neexistují T-010 / T-020, je zaregistrovaná `NullNotificationService` (notifikace se tiše neposílají).
4. Historie telemetrie (T-012): `Core/History/TelemetryHistory` drží pro každou tiskárnu v paměti kruhový buffer vzorků (`TelemetrySample`, 48 B, bez objektů na haldě) za posledních 24 h, bez ukládání na disk. Plní ho `App/History/TelemetryHistoryObserver` ze `SnapshotUpdated` a při každém běhu mu předá tiskárny a interval. Graf teplot (T-011) čte rozsah přes `CopySamples` do vlastního bufferu a překresluje jen při změně `GetVersion` / události `Changed`.
5. Okno diagnostiky (T-011) z tray menu: `App/Diagnostics/DiagnosticsViewModel` vzniká při otevření okna a při zavření se uvolní (odhlásí se od `IPrinterMonitor` a `MonitoredPrinters`). Historii odebírá jen graf: k `TelemetryHistory.Changed` se přihlásí při připojení do okna a odhlásí při jeho zavření. Zavřené okno tak nic nestojí. Tiskárny aktuálního běhu bere z `App/Tray/MonitoredPrinters` (`IMonitoringObserver`), informace o tiskárně a firmwaru z `IPrinterInfoSource` (PrusaLink `/api/version` + `/api/v1/info`). Graf `App/Diagnostics/Chart/TemperatureChart` je vlastní Avalonia control; přípravu dat (kopie, decimace na sloupce pixelů, přerušení čar) dělá `TemperatureChartData` bez Avalonie. Jediná instance okna a jeho zavření při ukončení aplikace řeší `App/Windows/AuxiliaryWindowSlot` (stejně pro okno O aplikaci).

6. Více tiskáren (T-013): jedna agregovaná ikona. `App/Tray/TrayStatusRules` vybírá nejzávažnější tiskárnu (chyba > vyžaduje zásah > offline > pozastaveno > tiskne > zaneprázdněna > hotovo > nečinná; při shodě první v konfiguraci), tooltip má pro víc tiskáren jeden řádek na tiskárnu (nejzávažnější první, při nedostatku místa „Další tiskárny: N“). V menu je jedna tiskárna plochou položkou, víc tiskáren jsou podmenu („Otevřít … v prohlížeči“, „Diagnostika…“ s předvybranou tiskárnou). Dotaz jedné tiskárny stojí O(1) v menu a O(n) bez alokace u ikony; text se formátuje a menu mění jen při viditelné změně, strukturu menu mění jen změna sady tiskáren.

7. Okno nastavení (T-015) z tray menu: `App/Settings/SettingsViewModel` (+ `PrinterEditorViewModel` pro každou tiskárnu) vzniká při otevření okna a při zavření se uvolní (zruší rozběhnutý test spojení, zapomene napsaná hesla). Načítá a ukládá přes `ISettingsEditorStore` (`SettingsEditorStore`): nejdřív nová hesla do `ISecretStore` (s možností vrátit předchozí hodnotu), pak atomicky `settings.json`; při chybě se zapsaná hesla vrátí. Po uložení `ISettingsApplier` (= `MonitoringSession.ApplyAsync`) zruší běžící běh monitoru a spustí nový s novým nastavením; pozorovatelé (ikona, menu, notifikace, historie) dostanou nastavení přes `ApplySettings` a stav tiskáren se stejným id, adresou a typem připojení si ponechají. Když se tiskárny ani interval nemění (jen upozornění), monitor běží dál a pozorovatelé dostanou jen `UpdateSettings`. Hesla odebraných tiskáren se mažou až po uložení a po skončení předchozího běhu monitoru; snapshot, který doběhne po požadavku na zastavení běhu, se nepublikuje. „Otestovat spojení“ (`PrusaLinkConnectionTester`) volá `PrusaLinkClient.GetInfoAsync` s neuloženými hodnotami formuláře, napsané heslo předá klientovi jen v paměti. Důvod chyby vrací PrusaLink klient jako `PrusaLinkFailure`.

8. Jazyk aplikace (T-029): `AppSettings.Language` (kód z `Core/Settings/AppLanguages`, `null` = podle systému) se ukládá do `settings.json` jako `"language"`. Při startu `TrayHost.StartAsync` nejdřív načte nastavení (mimo UI vlákno), `App/Localization/UiLanguage` nastaví `Strings.Culture` a teprve pak vzniknou tray view modely, menu a tooltip; `MonitoringSession.Start(settings)` už nastavení znovu nenačítá. Za běhu jazyk aplikuje `UiLanguageObserver` (první `IMonitoringObserver`, `ApplySettings` i `UpdateSettings`) na UI vlákně. Texty v XAML jsou vázané přes `LocalizedStrings` (`{Binding [Klíč], Source={x:Static loc:LocalizedStrings.Instance}}`, kompilovaný indexer binding, slabý odběr), takže se otevřená okna přepnou sama; texty, které formátují view modely, přeformátuje `ILanguageAware.RefreshTexts` (tray přes `TrayHost`, otevřená okna přes `AuxiliaryWindowSlot.RefreshTexts`). `CurrentCulture` (čísla, data) se nemění.

## Prusa Connect (cloud)

Podle T-004 nemá Prusa Connect oficiální API pro uživatelské aplikace. Podrobnosti a rozhodnutí, která čekají na uživatele, jsou v [api/prusa-connect.md](api/prusa-connect.md). Pokud se klient (T-005) bude implementovat, platí pro něj:

- Přihlašovací údaje patří k **účtu** Prusa Account, ne k tiskárně. Jeden účet obsluhuje víc `PrinterDefinition` (`Address` = UUID tiskárny).
- Klienti téhož účtu sdílejí jeden dotaz na seznam tiskáren za cyklus pollingu, aby se nepřetěžovala služba.
- Trvale se ukládá jen refresh token, a to přes `ISecretStore` pod klíčem pro účet. Access token zůstává jen v paměti.
- Kód pro OAuth/PKCE a obnovu tokenů je v `PrusaTrayDiagnostic.PrusaConnect` a nezávisí na UI. UI přihlášení (T-022) je v App.
- Klient jen čte a je izolovaný za `IPrinterClient`, takže ho jde při změně nedokumentovaného API vypnout bez dopadu na PrusaLink.

## Platformní rozdíly

| Oblast | Windows | macOS |
|---|---|---|
| Ikona | oznamovací oblast, barevná ikona kreslená za běhu (`ITrayIconSet`) – T-007 | menu bar, doporučená monochromatická „template“ ikona – T-018 |
| Notifikace | toast – T-010 | User Notifications (vyžaduje .app bundle) – T-020 |
| Skrytí z docku/taskbaru | bez hlavního okna automaticky | `LSUIElement` v `Info.plist` – T-019 |
| Autostart, balení | registry `Run` / Startup, instalátor – T-017 | Login Items, `.app` bundle – T-019 |
| Tajné údaje | Credential Manager (`[LibraryImport]`, roaming s profilem) – T-002 (hotovo) | Keychain (`Security.framework` přes `[LibraryImport]`) – T-021 |

Výběr platformní implementace (`ISecretStore`, `INotificationService`) probíhá v composition rootu v App podle OS (explicitní registrace kvůli Native AOT); registraci `ISecretStore` zavedl T-007 (`Composition/AppServices`): Windows → `WindowsCredentialSecretStore`, ostatní OS → `UnsupportedSecretStore` (hesla nedostupná, aplikace běží dál), dokud T-021 nedodá Keychain. Úkoly s `platform: macos` se implementují až na Macu a Windows úkoly na nich nezávisí.

Konkrétní volby jsou otevřené otázky v příslušných úkolech (T-010, T-017, T-019, T-020, T-021).
