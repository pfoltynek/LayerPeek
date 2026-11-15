---
id: T-241
title: macOS – menu ikony v Docku nevypisuje okna aplikace (Nastavení, okno kamery) (T-102)
status: done
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
- [x] Menu Docku vypisuje Nastavení a okno kamery; výběr okna ho přivede dopředu s fokusem (`mac.dock-windows` (2)).
- [x] Malý náhled (bez rámečku) se v seznamu ukázat nemusí (T-102).
- [x] `dotnet build` a `dotnet test` bez chyb a varování.

## Otevřené otázky
1. ~~Je výpis oken v menu Docku potřeba?~~ **Rozhodnutí uživatele 2026-11-15: ano, opravit** – menu Docku má vypisovat Nastavení a okno kamery (jak počítá T-102).

## Poznámky k implementaci
- **Příčina:** aplikace nemá menu Okno (`NSApp.windowsMenu` je `nil`). AppKit bere okna do menu Docku ze seznamu menu
  Okno; bez něj tam dá jen systémové položky. Avalonia 12.1.6 menu Okno nenastavuje (v `libAvaloniaNative` není
  `setWindowsMenu:` ani `setExcludedFromWindowsMenu:`). Okna `excludedFromWindowsMenu` nemají a přepnutí politiky
  Accessory → Regular za běhu (T-102) na věc vliv nemá.
- **Ověření příčiny:** malá AppKit aplikace (Swift, `LSUIElement`, start jako Accessory, po 1 s okno s titulkem a
  přepnutí na Regular jako T-102), menu Docku čtené helperem `dock <bundle> menu`: bez menu Okno jen systémové položky
  (stejně jako LayerPeek), s `NSApp.windowsMenu` – i takovým, které není v žádném menu baru – položka „Probe Window“ ✓.
- **Oprava:** `Platform/MacOS/MacWindowsMenu.cs` – při startu (vedle `MacCommandPeriod` / `MacTabShortcutKeys`
  v `App.StartHost`) dostane aplikace přes ObjC runtime prázdné `NSMenu` jako `NSApp.windowsMenu`, mimo menu bar (nic
  viditelného se nemění). Položky oken v něm udržuje AppKit sám, výběr okna z menu Docku ho přivede dopředu s fokusem.
  Už nastavené menu Okno (pozdější Avalonia) zůstane; bez `NSApp` (headless testy) se nic nestane; nikdy nevyhodí.
  Žádný nový text v UI, žádný nový balíček.
- **Testy:** `MacWindowsMenuTests` (bez `NSApp` nic nemění a nevyhodí, i opakovaně). Skutečné menu Docku jen živě.
- **Živé ověření (Mac mini, macOS 27.2.1):** `mac.dock-windows` (`artifacts/verify/20261115-193533/`): část (2) v
  pořádku – menu Docku vypisuje „LP verify … – Camera“ a „Settings“ (✓) a nic dalšího, stisk ikony dal fokus Nastavení,
  položka okna kamery z menu Docku dala fokus oknu kamery; (1), (3), (4), (5) také v pořádku. Celkový výsledek `SKIP`
  jen kvůli části (5) „žádná nová notifikace“: oznámení „LayerPeek E2E“ jsou `NotDetermined` (nesouvisí s tímto úkolem).
- Malý náhled (bez rámečku) v menu Docku zvlášť ověřen nebyl (kritérium ho nevyžaduje; s ním samotným aplikace ikonu
  v Docku nemá).
