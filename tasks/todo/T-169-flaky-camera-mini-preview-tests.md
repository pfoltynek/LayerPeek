---
id: T-169
title: Občasné selhání E2E malého náhledu kamery (CameraMiniPreviewTests)
status: todo
owner: qa-reviewer
priority: P3
platform: windows
depends_on: []
---

## Cíl
Dva E2E testy malého náhledu kamery na Windows občas selžou a hned potom samostatně projdou. Zjistit, jestli jde
o nestabilitu testu, nebo o chybu aplikace (souběh při změně velikosti okna), a opravit ji. Podobně jako T-123.

## Rozsah
Pozorováno 2026-11-05 (develop `7047eef5`–`21c66c63`, Windows 11 Pro 10.0.26300):
- `CameraMiniPreviewTests.ResizeGripCorrectsTheHeightOnceAfterTheReleaseAndTheStripMovesTheWindow`: 2× FAIL (jednou při
  samostatném běhu třídy, jednou v `-Class '*Camera*'`). Zpráva: „The window kept changing after the correction:
  (960,590)-(1440,893) 480x303, (960,545)-(1440,848) 480x303, …“. Okno se po opravě výšky jednou posune o 45 px nahoru
  (stejná velikost) a pak už je stabilní. Test asi bere jako změnu i poslední posun, nebo aplikace okno posune
  s opožděním.
- `CameraMiniPreviewTests.TogglesGrowTheWindowFromTheBottomEdgeAndSurviveARestart`: 1× FAIL v `-Class '*Camera*'`:
  „Waited 15 s for the name row expanded: 320 × 247 (now (320, 213))“. Řádek s názvem se po přepnutí nerozbalil.
- Ve všech případech hned následující samostatný běh `-Class '*CameraMiniPreviewTests'` 6/6 PASS. Výsledky selhání
  jsou v `%TEMP%\LayerPeek.App.E2E.Results.<id>-failed-*` (snímky a logy aplikace).
- Postup: z uložených výsledků a logů určit příčinu; opakovat `pwsh tools/run-e2e.ps1 -Class '*CameraMiniPreviewTests'
  -NoSystemChanges` 10× (jako u T-123). Chyba aplikace = oprava v aplikaci, jinak oprava čekání v testu.

## Akceptační kritéria
- [ ] Příčina obou selhání zjištěná a zapsaná (test / aplikace)
- [ ] `pwsh tools/run-e2e.ps1 -Class '*CameraMiniPreviewTests' -NoSystemChanges` 10× za sebou PASS na Windows
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
