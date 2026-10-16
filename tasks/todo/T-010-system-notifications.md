---
id: T-010
title: Systémové notifikace – Windows
status: todo
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
- [ ] Notifikace fungují na Windows (ověřeno)
- [ ] Kliknutí na notifikaci otevře detail / web tiskárny
- [ ] Na jiném OS než Windows se Windows implementace neregistruje a build/spuštění nespadne
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Jakou knihovnu/přístup použít na Windows (Avalonia nemá nativní systémové notifikace; Windows App SDK / toast knihovna / vlastní interop)? Zvolené řešení musí být ověřeně AOT/trim kompatibilní.
- Toasty z nebalené (unpackaged) aplikace vyžadují registrovaný AppUserModelID (zástupce ve Start menu nebo registry) – souvisí s volbou formátu balení v T-017.
- Zvuk u notifikací ano/ne? (Rozhodnutí platí i pro macOS – T-020.)
- Co má aplikace dělat na macOS, dokud neexistuje T-020 (no-op implementace, nebo notifikace vypnuté v UI)?

**Rozhodnutí (2026-10-16):**
- Vlastní WinRT interop (`ToastNotificationManager` přes `[GeneratedComInterface]`), bez nového balíčku a bez změny TFM; AppUserModelID registrovat při startu (zástupce ve Start menu, HKCU, bez admin práv).
- Zvuk nastavitelný; výchozí zvuk jen u chyby a „vyžaduje pozornost“, ostatní potichu; v nastavení jde zvuk vypnout úplně.
- macOS před T-020: no-op implementace (notifikace se tiše neposílají).

## Poznámky k implementaci
