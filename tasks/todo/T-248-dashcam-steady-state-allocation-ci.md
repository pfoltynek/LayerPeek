---
id: T-248
title: Dashcam – SteadyStateBufferingDoesNotAllocate naměřil v CI na Windows 4 120 B ve všech průchodech
status: todo
owner: core-dev
priority: P2
platform: all
depends_on: []
---

## Cíl
CI běh 43766166248 (2026-11-15, větev měření T-247, `windows-2025`): v samostatném kroku alokačních testů (T-220,
procesy bez jiných testů) selhal `LayerPeek.Core.Tests.Cameras.Recording.CameraDashcamAllocationTests.
SteadyStateBufferingDoesNotAllocate` – „Allocated 4120 B (expected 0)“ bez poznámky o neklidném procesu. Test bere
**minimum ze 3 průchodů**, takže alokoval každý průchod – spíš skutečná alokace v ustáleném stavu dashcamu (zvětšení
bufferu, časovač, `Lock`, výjimka …) než šum. Ve třech předchozích bězích T-220 a lokálně test prochází.

## Rozsah
- Reprodukovat (opakované běhy, omezené CPU jako runner) a najít původce: `GC.GetAllocatedBytesForCurrentThread` po
  vláknech, `GCAllocationTick` přes `EventListener` (dočasně), velikost 4 120 B napovídá pole nebo buffer.
- Opravit kód dashcamu (`Core/Cameras/Recording`), pokud jde o skutečnou alokaci v ustáleném stavu; jinak opravit
  měření (bez zvýšení meze 0 B).
- Ověřit ručním během CI (hlavní agent).

## Akceptační kritéria
- [ ] Původce 4 120 B zjištěný a zapsaný; oprava v kódu nebo měření s odůvodněním, mez 0 B zůstává
- [ ] Test opakovaně prochází lokálně pod zátěží a v CI (aspoň 3 běhy)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
