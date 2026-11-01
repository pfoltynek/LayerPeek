---
id: T-123
title: Občasné selhání E2E „okno kamery zůstane otevřené při změně volby Vždy navrchu“
status: todo
owner: qa-reviewer
priority: P3
platform: windows
depends_on: [T-114]
---

## Cíl
Při celém běhu `win.e2e` (T-114, 2026-11-01, develop 7b40a09) selhal jednou test
`CameraTopmostTests.TopmostKeepsTheCameraAboveAnotherApp` s hlášením „The camera window stays open when the option
changes.“ Dva samostatné opakované běhy třídy hned potom prošly (2/2, 2/2) a třída prošla i 2026-10-31. Snímky a log
selhání se přepsaly opakováním (stejná složka výsledků).

## Rozsah
1. Zjistit, zda jde o skutečnou občasnou chybu aplikace (okno kamery se při Použít / změně „Vždy navrchu“ v Nastavení
   zavře nebo znovu vytvoří, T-040 / T-102 / T-117), nebo o časování v testu (okno se při změně přítomnosti na hlavním
   panelu krátce skryje – `Hide()` / `Show()` Avalonie – a test ho v tu chvíli nenajde).
2. Podle výsledku opravit aplikaci, nebo test (čekání na stav místo okamžitého dotazu), a zachovat výsledky selhání
   (vlastní složka nebo název podle běhu, aby je opakování nepřepsalo).

## Akceptační kritéria
- [ ] Příčina zjištěna a zapsána v poznámkách
- [ ] Oprava (aplikace nebo test); třída `CameraTopmostTests` projde 10× za sebou (`run-e2e.ps1`)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
(žádné)
