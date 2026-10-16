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
