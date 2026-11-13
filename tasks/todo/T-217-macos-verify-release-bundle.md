---
id: T-217
title: macOS ověření – kontrola release bundlu z `.dmg` a `.tar.gz` (`mac.release-bundle`, start bez prostředí vývojáře)
status: todo
owner: qa-reviewer
priority: P3
platform: macos
depends_on: [T-208]
---

## Cíl
`release-build.ps1` už kontroluje rozbalenou `LayerPeek.app` z `Portable.zip` Velopacku:
- `codesign --verify --deep --strict` (`Get-MacSignatureProblems`);
- knihovny bez cest Homebrew / `/usr/local` (`Get-MacBundleLibraryProblems`, `otool -L`) a hardened runtime;
- smoke start přes `aot-smoke.ps1`.

Nekontroluje ale soubory, které si uživatel opravdu stáhne (`.dmg`, `.tar.gz`). Smoke start navíc běží v prostředí
vývojáře (`DOTNET_ROOT`, Homebrew v `PATH`). Bod T-127 „Podepsaný bundle se spustí … na čistém Macu“ (T-026) proto
čeká na jiný Mac nebo účet. Tento úkol přidá kontrolu, která se tomu co nejvíc přiblíží na tomtéž Macu.

## Rozsah
- **Kontrola `mac.release-bundle`** (Auto, preset `release`; `SKIP`, když chybí výstup `release-build.ps1` pro verzi
  z `Directory.Build.props`, s návodem):
  - `.dmg`: `hdiutil attach -nobrowse -readonly -mountpoint <dočasná>`, aplikace zkopírovaná `ditto` do dočasné
    složky (ne do `/Applications`), vždy `hdiutil detach`. `.tar.gz` rozbalený do dočasné složky. Obě kopie
    zkontrolovat stejně:
    - `codesign --verify --deep --strict`, hardened runtime a entitlements (stávající funkce `ReleaseTools.psm1`, žádná
      kopie logiky);
    - `otool -L` každého Mach-O bez cest mimo bundle a systém (Homebrew, `/usr/local`, `/opt`, cesty buildu
      `artifacts/`, `~/Library/Caches/LayerPeek.Build`);
    - `otool -l` bez `LC_RPATH` mimo `@executable_path` / `@loader_path`;
    - `Info.plist` (verze = verze buildu, identifikátor, `LSUIElement`);
    - SHA-256 souborů proti `SHA256SUMS-macos.txt`.
  - **Start bez prostředí vývojáře:** spustitelný soubor z kopie z `.dmg` přes `env -i HOME=$HOME
    PATH=/usr/bin:/bin:/usr/sbin:/sbin TMPDIR=$TMPDIR` (bez `DOTNET_ROOT`, `DOTNET_*`, `DYLD_*` a Homebrew). Profil se
    odsune a vrátí žurnálem `aot-smoke.ps1` (znovupoužít `aot-smoke.ps1 -PublishDir <kopie>/Contents/MacOS`
    s novým přepínačem pro čisté prostředí, např. `-CleanEnvironment`, ne druhá implementace). Aplikace musí běžet
    ≥ 10 s a typ v LaunchServices musí být `UIElement`. Log nesmí obsahovat chybu načtení nativních knihoven
    (FFmpeg, Avalonia), viz otázka 1. Při spuštění z `.dmg` (App Translocation) se ověří, že
    záložka Spuštění a aktualizace nabízí „Přesuňte LayerPeek do složky Aplikace…“ – jen pokud to jde bez skutečné
    karantény. Jinak to zůstává v T-127 (T-058).
- Pester testy nových čistých funkcí (sestavení prostředí `env -i`, seznam zakázaných prefixů cest) v
  `scripts/ReleaseTools/ReleaseTools.Tests.ps1`.
- Bez zápisu na GitHub, bez `/Applications`, bez Gatekeeperu (ten je v `mac.gatekeeper` / T-126).

## Akceptační kritéria
- [ ] `mac.release-bundle` = `PASS` na výstupu `release-build.ps1` (obě kopie: `.dmg` i `.tar.gz`); po běhu žádný
      připojený obraz, žádná kopie aplikace, profil vrácený (`mac.profile-unchanged` = `PASS`)
- [ ] Start bez `DOTNET_ROOT` / Homebrew v prostředí proběhne ≥ 10 s bez chyby. Jako částečná náhrada bodu **T-026**
      „Podepsaný bundle (T-019) se spustí a stream funguje na čistém Macu“ v T-127 (viz otázka 2)
- [ ] Pester testy (`scripts/ReleaseTools/*.Tests.ps1`) a `dotnet build` / `dotnet test` bez chyb a varování

## Otevřené otázky
1. Má start bez prostředí vývojáře ověřit i **stream kamery** v Release bundlu? Release nemá testovací režim, takže by
   kontrola musela do odsunutého profilu zapsat `settings.json` s tiskárnou simulátoru a heslo do Klíčenky pod
   produkčním prefixem (a pak ho smazat), nebo by aplikace musela při startu logovat načtení FFmpeg (změna kódu).
   Nebo stačí, že se aplikace spustí a nativní knihovny se načtou bez chyby?
2. Stačí start na tomtéž Macu s vyčištěným prostředím jako náhrada „čistého Macu / účtu bez Homebrew a .NET SDK“
   (T-026, bod v T-127), nebo zůstane ruční ověření na jiném účtu?

Rozhodnuto uživatelem 2026-11-13: 1 stačí start a načtení nativních knihoven bez chyby (**stream kamery v Release se nekontroluje**);
2 **ano**, start s vyčištěným prostředím na tomto Macu nahrazuje „čistý Mac / účet bez Homebrew“.

## Poznámky k implementaci
(vyplní agent po dokončení)
