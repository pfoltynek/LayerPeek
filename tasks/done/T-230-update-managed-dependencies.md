---
id: T-230
title: Aktualizace všech NuGet balíčků a .NET nástrojů na nejnovější stabilní verze
status: done
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
- [x] Všechny přímé balíčky a nástroje na nejnovější stabilní verzi (nebo zdůvodněná výjimka v poznámkách)
- [x] `--vulnerable` bez nálezů
- [x] Licence a AOT ověřené a zapsané u změněných balíčků v `src/`
- [x] `dotnet build` a `dotnet test` bez chyb a varování na Macu; AOT publish osx-arm64 bez varování
- [x] Windows: build, test a AOT publish win-x64 (→ T-125, pokud se úkol dělá na Macu) → přesunuto do T-125

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- Aktualizováno v `Directory.Packages.props` (vše jen testy, nic z toho nejde do `src/` ani do aplikace):
  `Microsoft.Extensions.TimeProvider.Testing` 10.11.0 → 11.0.0, `Microsoft.Playwright` 1.63.0 → 1.64.1,
  `xunit.v3` 4.0.2 → 4.0.3. Licence beze změny (MIT); AOT/trim a `ThirdPartyLicenses` se netýkají (balíčky v `src/`
  se nezměnily). `CHANGELOG.md` beze změny – nic viditelného uživateli.
- TimeProvider.Testing 11.0.0: poznámky k vydání dotnet/extensions v11.0.0 (2026-11-11) – přidané cíle net11, pro
  `FakeTimeProvider` žádná zásadní změna; build i testy prošly bez úprav kódu.
- Playwright: na GitHubu už je v1.65.0, na NuGet zatím ne – nejnovější stabilní na NuGet je 1.64.1. E2E testy
  používají nainstalovaný Edge (`Channel = "msedge"`) a nic nestahují (`docs/prerequisites.md`), takže se po
  aktualizaci nic dalšího neinstaluje; ovladač je v NuGet balíčku.
- Beze změny (už nejnovější stabilní): Avalonia* 12.1.6, CommunityToolkit.Mvvm 8.4.2, Velopack 1.2.161 a nástroj
  `vpk` 1.2.161 (1.3.x jen předběžné), FFmpeg.AutoGen.* 9.0.1.1, FlaUI.* 5.0.0. `dotnet list package --outdated`
  po změně bez nálezů; `--vulnerable --include-transitive` bez nálezů ve všech projektech.
- Mac (2026-11-15, Edge 157.0.4341.60): `dotnet build` 0 varování; `dotnet test` 10 831 testů, 10 559 PASS, 271 SKIP,
  1 FAIL: `ProxyOriginGuardTests.ClassicFormPostOfTheProxiedPageReachesThePrinter` (`Assert.Single(FailedRequests)`
  – prázdné). Selhává stále i samostatně a **stejně s Playwright 1.63.0** (ověřeno dočasným vrácením verze), tedy
  nezávisle na tomto úkolu, pravděpodobně změnou v Edge 157 → nový úkol **T-233**. Ostatní E2E testy (21) PASS.
- `dotnet publish src/LayerPeek.App -c Release -r osx-arm64`: bez varování AOT/trim; vypisují se jen známé zprávy
  `dsymutil` / `xcodebuild` z nativního linkování (viz T-095, T-193).
- Uzavřeno 2026-11-15: zbývající ověření na Windows přesunuto do T-125 (rozhodnutí uživatele).
