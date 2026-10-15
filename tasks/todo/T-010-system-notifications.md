---
id: T-010
title: Systémové notifikace (Windows + macOS)
status: todo
owner: ui-dev
priority: P1
depends_on: [T-009]
---

## Cíl
Implementace `INotificationService` pomocí nativních notifikací.

## Rozsah
- Windows: toast notifikace
- macOS: User Notifications (vyžaduje bundle identifikátor a podepsanou .app)
- Kliknutí na notifikaci otevře detail / web tiskárny

## Akceptační kritéria
- [ ] Notifikace fungují na Windows (ověřeno)
- [ ] macOS implementace + jasně uvedeno, co nebylo ověřeno
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Jakou knihovnu/přístup použít (Avalonia nemá nativní systémové notifikace; Windows App SDK / toast knihovna, na macOS nativní interop)?
- Zvuk u notifikací ano/ne?

## Poznámky k implementaci
