---
id: T-219
title: Nestabilní test CameraStreamHubKeyFrameTests.WithoutDueSubscribersNoKeyFrameIsWanted při plném dotnet test
status: todo
owner: core-dev
priority: P2
platform: all
depends_on: []
---

## Cíl
Při ověření T-208 (2026-11-13, macOS 27.2.1, Mac14,12) selhal v kontrole `mac.test` (plné `dotnet test` celého řešení,
běh `artifacts/verify/20261113-210821/` ve worktree T-208) test
`LayerPeek.Core.Tests.Cameras.CameraStreamHubKeyFrameTests.WithoutDueSubscribersNoKeyFrameIsWanted`:

```
Assert.False() Failure
Expected: False
Actual:   True
  at ...CameraStreamHubKeyFrameTests.cs:163
```

Řádek 163 je první `Assert.False(demand.KeyFrameDue)` hned po `await NextFrameAsync(samples)`. Samostatně
(`--filter-class '*CameraStreamHubKeyFrameTests'`) prošel 3× za sebou – jde nejspíš o souběh: `KeyFrameDue` se
přepočítá až po doručení snímku odběrateli, test ho čte dřív (pod zátěží plného běhu testů).

## Rozsah
- Najít příčinu (pořadí doručení snímku a přepočtu `KeyFrameDue` v `CameraStreamHub` / `CameraStreamSubscription`).
- Pokud je chyba v testu: čekat na podmínku deterministicky (bez pevných prodlev). Pokud v kódu: opravit kód.
- Test nezeslabovat ani nemazat.

## Akceptační kritéria
- [ ] Příčina popsaná v poznámkách
- [ ] Test projde opakovaně i pod zátěží (např. 50× v cyklu souběžně s plným `dotnet test`)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
