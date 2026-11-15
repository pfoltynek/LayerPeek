---
id: T-241
title: macOS – menu ikony v Docku nevypisuje okna aplikace (Nastavení, okno kamery) (T-102)
status: todo
owner: ui-dev
priority: P3
platform: macos
depends_on: [T-102, T-215]
---

## Cíl
T-102 počítá s tím, že menu ikony LayerPeeku v Docku (pravé tlačítko / podržení) vypisuje otevřená okna s titulkem
(„{tiskárna} – kamera“, Nastavení …) a výběr okna ho přivede dopředu – „AppKit to dělá u oken s titulkem“. Ve skutečném
běhu menu Docku žádná okna nevypisuje, jen systémové položky („Volby“, „Zobrazit všechna okna“, „Skrýt“, „Skrýt
ostatní“, „Ukončit“, „Vynutit ukončení“). Ikona v Docku, ⌘Tab a klik na ikonu (reopen) fungují.

## Zjištění (2026-11-15, Mac mini Mac14,12, macOS 27.2.1, Avalonia 12.1.6, testovací bundle „LayerPeek E2E“)
- Běh `-Preset release` `artifacts/verify/20261115-155430/` (commit `24d73aa3`):
  `evidence/mac.dock-windows/dock-menu.txt` (menu Docku s otevřeným Nastavením a normálním oknem kamery: jen systémové
  položky) a `dock-menupress.txt` („no item 'LP verify a25f0361 – Camera' in the Dock menu“). Zprávu tehdy přebila
  chyba kontroly v bodě (4) (zástupná aplikace nešla dopředu, opraveno na větvi `feature/macos-verify-first-real-run`).
- Opakování na té větvi: `mac.dock-windows` = `FAIL` „(2) the Dock menu does not list the window 'LP verify … – Camera'
  …; (2) the Dock menu does not list the window 'Settings' …“ – stejné, tedy ne náhoda ani zásah uživatele.
- Možné příčiny k ověření: okna Avalonie mají `excludedFromWindowsMenu`, aplikace nemá menu Okno (`NSApp.windowsMenu`),
  nebo se Dock po přepnutí politiky Accessory → Regular za běhu (T-102) o oknech nedozví. Porovnat s malou AppKit
  aplikací, která politiku přepíná stejně.

## Rozsah
- Rozbor příčiny; oprava (např. `addWindowsItem:title:filename:` / `excludedFromWindowsMenu = NO` přes ObjC runtime,
  případně skryté menu Okno), nebo rozhodnutí uživatele, že výpis oken v menu Docku není potřeba (pak upravit T-102
  a kontrolu `mac.dock-windows` (2)).

## Akceptační kritéria
- [ ] Menu Docku vypisuje Nastavení a okno kamery; výběr okna ho přivede dopředu s fokusem (`mac.dock-windows` (2)).
- [ ] Malý náhled (bez rámečku) se v seznamu ukázat nemusí (T-102).
- [ ] `dotnet build` a `dotnet test` bez chyb a varování.

## Otevřené otázky
1. ~~Je výpis oken v menu Docku potřeba?~~ **Rozhodnutí uživatele 2026-11-15: ano, opravit** – menu Docku má vypisovat Nastavení a okno kamery (jak počítá T-102).

## Poznámky k implementaci
(vyplní agent po dokončení)
