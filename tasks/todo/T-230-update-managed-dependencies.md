---
id: T-230
title: Aktualizace všech NuGet balíčků a .NET nástrojů na nejnovější stabilní verze
status: todo
owner: qa-reviewer
priority: P2
platform: all
depends_on: []
---

## Cíl
Zadání uživatele 2026-11-15: aktualizovat všechny knihovny na Macu i na Windows. Tento úkol pokrývá spravované
závislosti společné oběma OS (NuGet balíčky v `Directory.Packages.props` a .NET nástroje v `.config/dotnet-tools.json`).
Nativní závislosti řeší T-231 (macOS) a T-232 (Windows).

Stav 2026-11-15 (`dotnet list LayerPeek.slnx package --outdated`, hlavní agent na Macu):
- `Microsoft.Extensions.TimeProvider.Testing` 10.11.0 → 11.0.0 (hlavní verze, jen testy)
- `Microsoft.Playwright` 1.63.0 → 1.64.1 (E2E testy; po aktualizaci ověřit instalaci prohlížečů / Edge)
- `xunit.v3` 4.0.2 → 4.0.3
- Avalonia už je na 12.1.6 (T-229). Velopack 1.2.161 je nejnovější stabilní (1.3.x jen předběžné) – beze změny.

## Rozsah
- Znovu spustit `dotnet list package --outdated` (i `--include-transitive` pro bezpečnostní opravy, `--vulnerable`)
  a aktualizovat všechny přímé balíčky na nejnovější **stabilní** verze; předběžné verze ne (pravidlo jako u SDK, T-193).
- U každé hlavní verze přečíst poznámky k vydání (breaking changes); ověřit licenci (GPL-3.0 kompatibilní) a AOT/trim
  kompatibilitu u balíčků, které jdou do aplikace (`src/`), a zapsat do poznámek.
- .NET nástroje (`vpk` a další v `.config/dotnet-tools.json`) – nejnovější stabilní; `vpk` musí mít stejnou verzi jako
  balíček `Velopack` (Pester test).
- `dotnet build` bez varování, `dotnet test`, `dotnet publish -c Release -r osx-arm64` bez varování AOT; na Windows
  totéž pro `win-x64` (→ T-125, pokud se dělá na Macu).
- `CHANGELOG.md` (jen změny viditelné uživateli, jinak nic), `ThirdPartyLicenses` u balíčků v aplikaci.

## Akceptační kritéria
- [ ] Všechny přímé balíčky a nástroje na nejnovější stabilní verzi (nebo zdůvodněná výjimka v poznámkách)
- [ ] `--vulnerable` bez nálezů
- [ ] Licence a AOT ověřené a zapsané u změněných balíčků v `src/`
- [ ] `dotnet build` a `dotnet test` bez chyb a varování na Macu; AOT publish osx-arm64 bez varování
- [ ] Windows: build, test a AOT publish win-x64 (→ T-125, pokud se úkol dělá na Macu)

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
