---
id: T-219
title: Nestabilní test CameraStreamHubKeyFrameTests.WithoutDueSubscribersNoKeyFrameIsWanted při plném dotnet test
status: done
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
- [x] Příčina popsaná v poznámkách
- [x] Test projde opakovaně i pod zátěží (např. 50× v cyklu souběžně s plným `dotnet test`)
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- **Příčina (chyba testu, ne kódu):** `CameraStreamHub.Dispatch` na vlákně streamu nejdřív předá snímek všem
  odběratelům (`Publish` → snímek je hned vidět přes `TakeLatest`) a teprve potom pod zámkem zavolá `ScheduleFrames`,
  které přepočítá `CameraStreamDemand` (`KeyFrameDue`, `GrayscaleDue`, `FullResolutionDue`). Smlouva kódu je „přeplánovat
  před dalším čtením z feedu“ a ta platí – poptávku čte jen feed a ten ji čte až po návratu z `Dispatch`. Test ale četl
  poptávku hned po `TakeLatest`, tedy v mikrosekundovém okně mezi doručením snímku a přepočtem; pod zátěží plného
  `dotnet test` mohl OS vlákno streamu přerušit právě tam. Nejde o `TimeProvider` (test běží na `FakeTimeProvider`, čas
  se mezi tím neposouvá) ani o sdílený stav mezi testy (každý test má vlastní hub, feed i hodiny).
- **Oprava:** před čtením poptávky test počká na `HandledAsync(feed, 1)` – feed požádal o další položku, takže `Dispatch`
  včetně přeplánování doběhl. Je to stejný deterministický vzor, jaký už používají `CameraStreamHubDisplayTests` a
  `CameraStreamHubFrameRateTests` (čekání na podmínku, žádné pevné prodlevy, aserce beze změny). Stejný souběh měly ještě
  dva testy téže třídy: `TheSamplesAreGrayscaleOfTheFeedAndNoFrameIsConvertedInFull` (`GrayscaleDue`) a
  `ASecondGrayscaleSizeIsServedFromFullFramesOnlyWhenItIsDue` (`FullResolutionDue`, `GrayscaleDue`) – opraveny stejně.
  Produkční kód beze změny.
- **Reprodukce:** samotná zátěž okno netrefila (400 běhů třídy, 30 souběžně, 40 vláken zátěže CPU – 0 selhání). Deterministicky
  prokázáno dočasným `Thread.Sleep(30)` v `Dispatch` mezi doručením a přeplánováním (jen místní pokus, necommitnuto): bez
  opravy selhaly 3 testy třídy (původní a dva výše), s opravou testů třída prošla 3× za sebou i s prodlevou.
- **Ověření po opravě:** 100 běhů třídy (10 souběžně, 12 vláken zátěže CPU) současně s plným `dotnet test` – 0 selhání.
  Následné plné `dotnet test` bez zátěže: `total: 10534, failed: 0, succeeded: 10267, skipped: 267`; `dotnet build` 0 varování.

