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

## Platformní rozdíly

| Oblast | Windows | macOS |
|---|---|---|
| Ikona | oznamovací oblast, barevná ikona | menu bar, doporučená monochromatická „template“ ikona |
| Notifikace | toast | User Notifications (vyžaduje .app bundle) |
| Skrytí z docku/taskbaru | bez hlavního okna automaticky | `LSUIElement` v `Info.plist` |
| Autostart | registry `Run` / Startup | Login Items |
| Tajné údaje | DPAPI / Credential Manager | Keychain |

Konkrétní volby jsou otevřené otázky v příslušných úkolech (T-002, T-010, T-017).
