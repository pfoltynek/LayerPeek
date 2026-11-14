---
id: T-220
title: Alokační testy ASampleDoesNotAllocate a SteadyStateTimelapseDoesNotAllocate nestabilní na 4jádrovém runneru Windows
status: todo
owner: qa-reviewer
priority: P2
platform: windows
depends_on: []
---

> **Založeno podle rozhodnutí uživatele 2026-11-14** (T-218, otázka 12). Do opravy se oba testy **jen v CI na Windows**
> přeskočí s důvodem odkazujícím na tento úkol (`LAYERPEEK_CI=1` z `.github/workflows/ci.yml`,
> `tests/Shared/ContinuousIntegration.cs`). Lokálně běží dál.

## Cíl
Oba testy na runneru `windows-2025` (4 vCPU, ostatní testovací sestavy běží současně) selhávají, lokálně na Windows
vývojáře téměř vždy projdou (známá citlivost na zátěž: T-165, T-168). Zjistit příčinu a změřit tak, aby test byl
spolehlivý i na pomalém stroji a pořád zachytil skutečnou alokaci na vzorek / paket.

## Zjištěno v CI (T-218)
- `LayerPeek.Core.Tests.Detection.PrintFailureDetectionAllocationTests.ASampleDoesNotAllocate`: běh 43501907104 –
  minimum ze všech platných průchodů **173 840 B** (test trval 41 s); běh 43513903970 znovu selhal. Velikost thread poolu
  se během platných průchodů neměnila, takže jde o jiný zdroj (časovače na systémových hodinách? tiered JIT? jiné
  vlákno procesu?).
- `LayerPeek.Core.Tests.Cameras.Timelapse.CameraTimelapseAllocationTests.SteadyStateTimelapseDoesNotAllocate`:
  „Packets not read.“ – čtení paketů se za 10 s (`TestTimeouts.Wait`) nedokončilo (vlákno čtení hladovělo vedle
  aktivního čekání testu).
- Pod extrémní zátěží na Macu (load ~120, 2026-11-13) selhal stejně i
  `CameraStreamHubAllocationTests.GrayscaleKeyFrameSamplesDoNotAllocate` (120 B); bez zátěže prošel.

## Rozsah
- Na Windows (ideálně s omezeným CPU, např. `start /affinity 3` nebo souběh s ostatními sestavami jako v CI) reprodukovat
  a určit původce alokace (`dotnet-trace` / `GC.GetAllocatedBytesForCurrentThread` po vláknech, události
  `GCAllocationTick`).
- Opravit kód, nebo měření (např. alokace jen vláken, která se vzorku účastní; čekání bez aktivního točení), ne mez.
- Pak odstranit přeskočení v CI (`ContinuousIntegration.IsWindowsRunner` u obou testů) a ověřit zelený běh CI.

## Akceptační kritéria
- [ ] Příčina obou selhání popsaná v poznámkách
- [ ] Oba testy spolehlivé v CI na Windows (aspoň 3 zelené běhy po sobě) bez přeskočení; záruka 0 B na vzorek / paket
      zůstává
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
