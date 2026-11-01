---
id: T-131
title: Občasné selhání StatusWidgetTests (Release, statická inicializace)
status: todo
owner: qa-reviewer
priority: P3
platform: all
depends_on: []
---

## Cíl
Při `pwsh scripts/release-build.ps1` (Release `dotnet test`, 2026-11-01, T-130) jednou selhal
`LayerPeek.App.Tests.Cameras.Overlay.Widgets.StatusWidgetTests.StatusIsRegisteredWithItsNamePriorityAndSizes` na
`Assert.Same(StatusWidgetTemplate.Card, definition.CardTemplate)` (`tests/LayerPeek.App.Tests/Cameras/Overlay/Widgets/StatusWidgetTests.cs:91`).
Tři samostatné opakované běhy v Release prošly. Pravděpodobně souběh statické inicializace (šablona karty / registr
widgetů) mezi paralelně běžícími testy. Release build se kvůli tomu zastavil, takže chyba vadí i vydání.

## Rozsah
1. Zjistit příčinu (statická pole, `Lazy`, pořadí inicializace, sdílený registr widgetů mezi testy) a zda může nastat
   i v aplikaci (dva různé objekty šablony za běhu), nebo jen v testech.
2. Opravit produkt, nebo test; ověřit opakovaným během (např. 50× třídy v Release).

## Akceptační kritéria
- [ ] Příčina zapsaná v poznámkách
- [ ] Oprava; `StatusWidgetTests` a sousední testy widgetů projdou opakovaně v Debug i Release
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
(žádné)
