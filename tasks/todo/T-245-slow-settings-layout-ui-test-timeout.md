---
id: T-245
title: UI test EveryButtonIsAtLeast32DipHighInEveryState překračuje 30 s na runneru Windows (blokuje další UI testy)
status: todo
owner: qa-reviewer
priority: P2
platform: all
depends_on: []
---

## Cíl
Ruční běh CI 43756249903 (2026-11-15, commit 113a7757, `windows-2025`, 4 vCPU): test
`LayerPeek.App.UiTests.Settings.SettingsWindowLayoutTests.EveryButtonIsAtLeast32DipHighInEveryState` skončil po
limitu těla UI testu 30 s (`HeadlessSession.TestTimeout`, `TimeoutException`). Protože tělo dál blokovalo UI vlákno,
selhalo hned po něm dalších 5 testů téže třídy („UI thread still blocked by …“). Lokálně (16 vláken) test trvá 10–13 s,
na zatíženém runneru se 4 jádry přes 30 s. Je to test citlivý na zátěž, ne chyba aplikace.

## Rozsah
- Změřit, kde test tráví čas (počet stavů okna × jazyků, rozvržení a čekání), a zrychlit ho (např. rozdělit na theory
  po stavech / jazycích, aby každý běh měl vlastní limit, nebo ubrat zbytečné průchody rozvržením).
- Teprve když zrychlení nestačí: vlastní delší limit jen pro tento test (zdůvodněný), ne zvýšení globálního
  `HeadlessSession.TestTimeout`.
- Projít ostatní nejpomalejší UI testy (TRX z CI / lokálně) a zapsat, zda jim hrozí totéž.

## Akceptační kritéria
- [ ] Příčina a měření zapsané; test pod zátěží (4 jádra + souběžné běhy) bezpečně pod limitem
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; job Windows v CI zelený

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
