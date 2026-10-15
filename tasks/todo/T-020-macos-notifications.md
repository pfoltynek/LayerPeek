---
id: T-020
title: Systémové notifikace – macOS
status: todo
owner: ui-dev
priority: P1
platform: macos
depends_on: [T-010, T-019]
---

## Cíl
Implementace `INotificationService` pro macOS přes User Notifications framework, se stejným chováním jako Windows implementace z T-010.

> **Pouze na macOS:** úkol implementuje a ověřuje uživatel později na Macu. Neblokuje žádný Windows úkol.

## Rozsah
- macOS: User Notifications (`UNUserNotificationCenter`, vyžaduje bundle identifikátor a podepsanou `.app` – T-019)
- Žádost o oprávnění k notifikacím a chování při jeho odepření
- Kliknutí na notifikaci otevře detail / web tiskárny (stejně jako T-010)
- Registrace implementace v composition rootu pro macOS
- Řešení musí být kompatibilní s Native AOT (interop přes `[LibraryImport]` / source-generated interop, žádná reflexe)

## Akceptační kritéria
- [ ] Notifikace fungují na macOS ze sestavené `.app` (ověřeno)
- [ ] Kliknutí na notifikaci otevře detail / web tiskárny
- [ ] Odepřené oprávnění nezpůsobí pád; stav je srozumitelně ohlášen
- [ ] Windows implementace (T-010) beze změny
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Jaký přístup použít (vlastní interop na Objective-C runtime / `UserNotifications.framework`, nebo AOT kompatibilní knihovna)? Musí být ověřeně AOT/trim kompatibilní.
- Zvuk u notifikací – platí rozhodnutí z T-010.

## Poznámky k implementaci
