---
id: T-211
title: Windows – 47 selhávajících testů na develop (snapshoty UI, proxy, porty E2E, FFmpeg, Pester)
status: todo
owner: qa-reviewer
priority: P1
platform: windows
depends_on: []
---

## Cíl
Při práci na T-201 (2026-11-12, Windows 11 Pro 10.0.26300) skončil plný `dotnet test` se 47 selháními, která selhávají
už na `develop` `2ce7be01` bez změn T-201 (ověřeno v dočasném worktree). `develop` má být na Windows zelený; úkol má
přednost před T-202 (rozhodnutí uživatele 2026-11-12).

## Rozsah
Selhání (10 996 testů, 47 FAIL):
- `LayerPeek.App.UiTests`: `SettingsWindowSnapshotTests` (40×) a `AboutWindowLayoutTests` (3×) – snapshoty Windows
  nejspíš neodpovídají posledním změnám (např. snímky obrazovky přegenerované na macOS, změny rozvržení).
- `LayerPeek.PrusaLink.Tests`: `WebProxySecurityTests.ListenerIsNotReachableOnOtherAddresses` (síťová rozhraní).
- `LayerPeek.E2E.Tests`: `BrowserLifecycleTests.DisposedRigReleasesThePrinterAndProxyPorts` (porty).
- `LayerPeek.Camera.FFmpeg.Tests`: `GrayscaleSampleEquivalenceTests` (rozdíl 1,72, limit 1,5).
- `LayerPeek.Scripts.Tests`: Pester běh `scripts/ReleaseTools/ReleaseTools.Tests.ps1`.

Postup: u každého určit příčinu (chyba aplikace / zastaralý snapshot / závislost na prostředí stroje / nestabilní test)
a opravit ji. Snapshoty přegenerovat jen tehdy, když je změna vzhledu záměrná (dohledat commit); nečekaná změna vzhledu =
chyba. Změnu tolerance nebo přeskakování testu nejdřív odůvodnit, při nejistotě otázka uživateli.

## Akceptační kritéria
- [ ] Příčina každé skupiny selhání zjištěná a zapsaná v poznámkách (s commitem, který ji způsobil, pokud jde dohledat)
- [ ] Plný `dotnet test` na Windows bez selhání (2× za sebou)
- [ ] `dotnet build` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
