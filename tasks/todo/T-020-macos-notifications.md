---
id: T-020
title: Systémové notifikace – macOS
status: todo
owner: ui-dev
priority: P1
platform: macos
depends_on: [T-010, T-019, T-095]
---

## Cíl
Implementace `INotificationService` pro macOS přes User Notifications framework, se stejným chováním jako Windows implementace z T-010.

> **Pouze na macOS:** úkol implementuje a ověřuje uživatel později na Macu. Neblokuje žádný Windows úkol.

## Rozsah
- macOS: User Notifications (`UNUserNotificationCenter`, vyžaduje bundle identifikátor a podepsanou `.app` – T-019)
- Žádost o oprávnění k notifikacím a chování při jeho odepření
- Kliknutí na notifikaci otevře detail / web tiskárny (stejně jako T-010)
- Registrace implementace v composition rootu pro macOS
- **Izolace testů (pravidlo v `docs/architecture.md`, „Izolace testů na macOS“):** testy a testovací režim posílají notifikace pod vlastním bundle identifikátorem (např. s příponou `.E2E`), nikdy pod produkčním. Doručené testovací notifikace se odstraní při ukončení aplikace a v režimu úklidu po pádu (obdoba `--layerpeek-e2e-clear-toasts` z T-067). Stav oprávnění produkčního bundle identifikátoru se nemění.
- **Testovací `.app` jen pro E2E (rozhodnuto 2026-10-28):** jednotkové testy používají falešnou platformu notifikací (bez systému). Skutečné notifikace ověřují jen E2E testy na vyžádání: sestaví Debug `LayerPeek E2E.app` s `CFBundleIdentifier` = produkční identifikátor + `.E2E`, ad-hoc podpis, v dočasné / testovací složce (ne v `/Applications`). Při prvním běhu uživatel jednou ručně povolí notifikace pro E2E aplikaci (postup v dokumentaci E2E). Bez povolení se E2E test notifikací přeskočí s důvodem. Ověřit a zapsat, jestli jde položku E2E aplikace v Nastavení systému → Oznámení odstranit skriptem; pokud ne, uvést ji jako známý zbytek (jako log Velopacku u T-017). Běžný `dotnet test` žádný bundle nesestavuje a dialog oprávnění nevyvolá.
- Řešení musí být kompatibilní s Native AOT (interop přes `[LibraryImport]` / source-generated interop, žádná reflexe)

## Akceptační kritéria
- [ ] Notifikace fungují na macOS ze sestavené `.app` (ověřeno)
- [ ] Kliknutí na notifikaci otevře detail / web tiskárny
- [ ] Odepřené oprávnění nezpůsobí pád; stav je srozumitelně ohlášen
- [ ] Testy a testovací režim používají jen testovací bundle identifikátor; produkční identifikátor ani jeho oprávnění se nemění (test / kontrola před/po)
- [ ] Doručené testovací notifikace se odstraní při ukončení i v režimu úklidu po pádu (ověřeno)
- [ ] Windows implementace (T-010) beze změny
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Jaký přístup použít (vlastní interop na Objective-C runtime / `UserNotifications.framework`, nebo AOT kompatibilní knihovna)? Musí být ověřeně AOT/trim kompatibilní.
- Zvuk u notifikací – platí rozhodnutí z T-010.
- ~~Testovací `.app` a identifikátor~~ → jen pro E2E na vyžádání, identifikátor produkční + `.E2E` (rozhodnuto 2026-10-28).
- Riziko: aplikace bude mít jen ad-hoc podpis (rozhodnuto 2026-10-21, T-019). Ověřit, že `UNUserNotificationCenter` s ad-hoc podepsanou `.app` funguje a jestli macOS po každém novém sestavení znovu nežádá o povolení notifikací.

## Poznámky k implementaci
