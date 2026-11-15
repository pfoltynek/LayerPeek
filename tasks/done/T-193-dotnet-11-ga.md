---
id: T-193
title: Přechod z .NET 11 RC na stabilní .NET 11 (GA)
status: done
owner: qa-reviewer
priority: P1
platform: all
depends_on: []
---

## Cíl
Vývoj běžel na .NET 11 RC (`global.json` připínal `11.0.100-rc.2.26504.105` s povoleným prerelease). 2026-11-10 vyšel
stabilní .NET 11.0.0 (SDK `11.0.100`). Jde o bezpečnostní vydání s opravami CVE a podporu má do 2028-11-14 (STS). Vydání
aplikace se publikují přes Native AOT, takže runtime z RC by byl zapečený přímo v binárce. Proto přejít na GA a zakázat
prerelease SDK.

## Rozsah
- `global.json`: `version` `11.0.100`, `rollForward: latestFeature`, `allowPrerelease: false`.
- Dokumentace: `docs/prerequisites.md` (verze SDK), `CLAUDE.md` (Tech stack).
- Balíčky v `Directory.Packages.props` zkontrolovat, jestli nemají verzi pro .NET 11 (`Microsoft.Extensions.TimeProvider.Testing`).
- Ověření na Macu: `dotnet build`, `dotnet test`, Native AOT publish + smoke (`scripts/aot-smoke.ps1`), celé sestavení
  vydání (`scripts/release-build.ps1`: `.app`, `.dmg`, balíček a feed Velopack).
- Ověření na Windows → T-125.

## Akceptační kritéria
- [x] `global.json` připíná GA SDK `11.0.100` a nepovoluje prerelease
- [x] Dokumentace verze SDK aktualizovaná (`docs/prerequisites.md`, `CLAUDE.md`)
- [x] `dotnet build` a `dotnet test` bez chyb a varování (macOS)
- [x] Native AOT publish osx-arm64 + AOT smoke test PASS na runtime 11.0.0
- [x] `scripts/release-build.ps1` na Macu projde
- [x] Windows: build, testy, AOT publish win-x64, `scripts/release-build.ps1` → přesunuto do T-125

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
Ověřeno na Macu 2026-11-10 (Apple Silicon, SDK `11.0.100`, `origin/develop` `df9b79fd`):
- `dotnet build`: 0 varování, 0 chyb.
- `dotnet test`: 9968 testů, 0 selhání, 267 přeskočených (stejné přeskakované testy jako dřív: snímky README, testy jen pro Windows).
- `scripts/aot-smoke.ps1`: PASS. Publish bere `microsoft.netcore.app.runtime.nativeaot.osx-arm64` 11.0.0 a aplikace běží
  bez ikony v Docku. Při linkování vypíše `dsymutil` varování o debug mapě statických knihoven runtime
  („skipping debug map object with duplicate name“, chybějící module cache Swiftu). Jsou to jen varování nástroje, ne
  buildu, a na funkci nemají vliv.
- `Microsoft.Extensions.TimeProvider.Testing` zůstává na `10.11.0`: na NuGetu zatím verze 11 není. Balíček používají jen
  testy a nemá vliv na AOT ani na licenci.
- `scripts/release-build.ps1` (0.5.0, osx-arm64): build a testy v Release, AOT smoke publish složky i `LayerPeek.app`
  PASS, `vpk pack` vytvořil `.dmg`, `.tar.gz`, `-full.nupkg`, `releases.osx-arm64.json` a `SHA256SUMS-macos.txt`.
- Žádný nový balíček nepřibyl (licence ani kompatibilitu s AOT tedy není co ověřovat).
