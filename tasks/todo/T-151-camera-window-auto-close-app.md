---
id: T-151
title: Okno kamery – automatické zavření a znovuotevření podle stavu tiskárny (App, nastavení, E2E)
status: todo
owner: ui-dev
priority: P2
platform: all
depends_on: [T-150]
---

## Cíl
Napojit stavový automat z T-150 na skutečná okna kamery: automaticky zavřít okno vypnuté tiskárny a po jejím zapnutí ho otevřít na posledním místě. Přidat volbu do okna nastavení (nápad 2).

## Rozsah
- Pozorovatel v App (`IMonitoringObserver`, po vzoru `PrintNotificationObserver`) předává snapshoty automatu a vykonává jeho příkazy na UI vlákně.
- **Automatické zavření** = stejná cesta jako zavření okna uživatelem (uloží umístění podle `RememberLastPlacement`, ukončí odběr streamu), jen s příznakem „automaticky“, aby automat zavření nepovažoval za ruční.
- **Automatické otevření** = stejná cesta jako „Zobrazit kameru“ z tray menu (`CameraWindowOpenPolicy`, `CameraWindowPlanner`): poslední umístění, nepřipojený monitor podle `CameraWindowDefaultPlacement.Relocate`. Automaticky otevřené okno **nesmí krást fokus** (otázka 1) – na Windows bez aktivace (`ShowActivated = false` nebo ekvivalent), na macOS bez aktivace aplikace (pozor na T-102 / T-137: ikona v Docku a ⌘Tab).
- Tray menu („Zobrazit / Skrýt kameru“) odpovídá skutečnému stavu okna i po automatickém zavření / otevření.
- Volba v okně nastavení podle rozhodnutí z T-150 (otázka 5), texty ve všech jazycích.
- Chování s malým náhledem (vždy navrchu, všechny plochy na macOS) a s režimem rozložení (T-091): okno v režimu rozložení se automaticky nezavře (otázka 2).
- **E2E (Windows, UI Automation) proti simulátoru:** `POST /_simulator/offline` → po T₁ okno zmizí; `POST /_simulator/online` → okno se objeví na stejném místě; ručně zavřené okno se po `online` neotevře. Testy s krátkými časy přes testovací přepínač (jen Debug, jako T-067), aby E2E nečekal minutu.

## Akceptační kritéria
- [ ] Vypnutí tiskárny (simulátor offline) okno zavře po debounce, zapnutí ho otevře na posledním místě (E2E).
- [ ] Ručně zavřené okno se znovu neotevře (E2E).
- [ ] Automaticky otevřené okno nebere fokus aktivnímu oknu jiné aplikace (E2E na Windows; macOS → přesunout do T-127 podle `tasks/README.md`).
- [ ] Tray menu ukazuje správný text položky kamery po automatickém zavření i otevření (headless / E2E).
- [ ] Volba v nastavení funguje, ukládá se a je ve všech jazycích; okno nastavení bez posuvníku.
- [ ] Stávající E2E okna kamery (T-071, T-120) prochází.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; Native AOT publish bez nových varování.

## Otevřené otázky
1. **Fokus a popředí:** má automaticky otevřené okno přijít do popředí (nad ostatní okna), nebo se jen tiše objevit (bez aktivace, případně pod aktivním oknem)? U okna „vždy navrchu“ se zobrazí navrchu vždy.
2. **Režim rozložení (T-091) a otevřené kontextové menu:** zavřít okno automaticky i během úprav rozložení, nebo odložit, dokud uživatel režim neukončí?
3. **Oznámení:** dát uživateli vědět (toast / tooltip), že okno bylo zavřeno, protože je tiskárna vypnutá? Nebo nic?

## Poznámky k implementaci
(vyplní agent po dokončení)
