---
id: T-000
title: Založení repozitáře a kostry řešení
status: done
owner: architect
priority: P1
platform: all
depends_on: []
---

## Cíl
Připravit repozitář, kostru .NET řešení, agenty a backlog.

## Akceptační kritéria
- [x] Řešení `PrusaTrayDiagnostic.slnx` s projekty Core, PrusaLink, PrusaConnect, App a testy
- [x] Avalonia tray aplikace se spustí (ikona v liště s položkou Quit)
- [x] Agenti v `.claude/agents/`, pravidla v `CLAUDE.md`
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- .NET 11 RC SDK připnuté v `global.json`, testy běží na Microsoft.Testing.Platform (xUnit v3 4.x už nepodporuje VSTest na .NET 10+ SDK).
- Verze balíčků centrálně v `Directory.Packages.props` (Avalonia 12.1.4, CommunityToolkit.Mvvm 8.4.2, xUnit v3 4.0.2).
- Ikona `Assets/tray-idle.png` je jen dočasná (viz T-007).
