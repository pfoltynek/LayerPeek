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

### Rozhodnutí uživatele 2026-11-03
1. Automaticky otevřené okno se objeví **tiše, bez aktivace** (Windows bez aktivace, macOS bez aktivace aplikace); okno „vždy navrchu“ je navrchu vždy.
2. V **režimu rozložení (T-091)** nebo s otevřeným kontextovým menu se automatické zavření **odloží**; po ukončení režimu / zavření menu se okno zavře, pokud je tiskárna stále `Offline` (déle než T₁).
3. **Bez oznámení** – žádný toast ani tooltip při automatickém zavření / otevření.
4. **Implementace primárně na macOS** (rozhodnutí uživatele 2026-11-03): úkol se dělá v session na Macu (implementace pro obě platformy, headless testy, ověření macOS části – tiché otevření bez aktivace aplikace, Dock, ⌘Tab). Windows E2E (UI Automation proti simulátoru) a ověření Windows části se dodělají v další session na Windows; pokud tam nebudou hotové, přesunou se do T-125 podle `tasks/README.md`.
(Rozhodnutí k T-150: T₁ 30 s, T₂ 10 s pevně, jen `Offline`, globální volba výchozí zapnuto – viz T-150.)

### Poznámky k zapojení z T-150 (review 2026-11-03)
- API: `CameraWindowAutoVisibility` (Core/Cameras), popis v poznámkách T-150; volat na UI vlákně, jeden časovač na `NextDueAt` → `Evaluate()`.
- `WindowClosedByUser` volat při **každém** zavření okna, které nevykonává příkaz `Close` (uživatel, odebraná tiskárna, vypnutá kamera, jiné zavření aplikací).
- Do `CameraWindowAutoVisibilityOptions.Monitor` předat skutečné `PrinterMonitorOptions` monitoru, jinak může být `StaleAfter` krátké.
- `SystemResumed()` volat z události probuzení systému (Windows `PowerModeChanged` / `WM_POWERBROADCAST`, macOS `NSWorkspaceDidWakeNotification`).
- Důsledek rozhodnutí 9: když se po probuzení Wi-Fi připojuje déle než 30 s, okno se oprávněně zavře podle čerstvých snímků `Offline`.

## Poznámky k implementaci
(vyplní agent po dokončení)
