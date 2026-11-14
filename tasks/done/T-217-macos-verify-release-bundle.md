---
id: T-217
title: macOS ověření – kontrola release bundlu z `.dmg` a `.tar.gz` (`mac.release-bundle`, start bez prostředí vývojáře)
status: done
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
- [x] `mac.release-bundle` = `PASS` na výstupu `release-build.ps1` (obě kopie: `.dmg` i `.tar.gz`); po běhu žádný
      připojený obraz, žádná kopie aplikace, profil vrácený (`mac.profile-unchanged` = `PASS`)
- [x] Start bez `DOTNET_ROOT` / Homebrew v prostředí proběhne ≥ 10 s bez chyby. Jako částečná náhrada bodu **T-026**
      „Podepsaný bundle (T-019) se spustí a stream funguje na čistém Macu“ v T-127 (viz otázka 2)
- [x] Pester testy (`scripts/ReleaseTools/*.Tests.ps1`) a `dotnet build` / `dotnet test` bez chyb a varování

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
- **Kontrola `mac.release-bundle`** (`tools/macos-verify.ps1`, `Auto`, v presetu `release` hned za `mac.gatekeeper`)
  s deklarovanými předběžnými podmínkami `ReleaseBuild` (`.dmg`, `.tar.gz` a `SHA256SUMS-macos.txt` v
  `artifacts/release/<verze>/dist`, jinak `SKIP` s návodem `caffeinate -d -u -i pwsh scripts/release-build.ps1
  -SkipFeedSignature`) a `AppNotRunning` (spuštěná LayerPeek = `SKIP`, `aot-smoke.ps1` by vedle ní odmítl běžet). Obě
  podmínky nepotřebují Swift helper (`$script:HelperFreeNeeds`) a kontrola je ověřuje i sama (interaktivní běh).
- Postup: SHA-256 celého `dist/` proti `SHA256SUMS-macos.txt` (`Get-Sha256SumsProblems`, obě stažitelné soubory musí
  být uvedené); `.dmg` přes `hdiutil attach -nobrowse -readonly -noautoopen` do dočasné složky `….noindex` (Spotlight ji
  neindexuje), `ditto` aplikace, vždy `detach` (při chybě `-force`); `.tar.gz` přes `tar -xzf`. Obě kopie:
  `Get-MacSignatureProblems`, `Get-MacHardenedRuntimeProblems`, `Get-MacBundleLibraryProblems` (stávající funkce),
  nově `Get-MacBundleLoadPathProblems` (`otool -L` jen `@rpath` / `@loader_path` / `@executable_path`, `/usr/lib`,
  `/System/Library`; zakázané prefixy s popisem: `/opt/homebrew`, `/usr/local`, `/opt`, `<repo>/artifacts`,
  `~/Library/Caches/LayerPeek.Build`; vlastní install name se nepočítá; `LC_RPATH` z `otool -l` jen
  `@executable_path` / `@loader_path`) a `Get-MacReleasePlistProblems` (`plutil -convert xml1`: obě verze = verze
  buildu, identifikátor z `build/macos/Info.plist`, `LSUIElement` true). Na konci se ověří, že v `hdiutil info` nic
  nezůstalo a dočasná složka je pryč. PASS vypíše commit z `build-info.json`.
- **Start bez prostředí vývojáře:** `scripts/aot-smoke.ps1 -CleanEnvironment` (jen macOS) spustí exe přes
  `/usr/bin/env -i HOME=… PATH=/usr/bin:/bin:/usr/sbin:/sbin TMPDIR=…` (`Get-SmokeCleanEnvironmentArguments`;
  `env` exec = stejné PID, takže kontrola `lsappinfo` i ukončení fungují). Profil odsouvá a vrací stávající žurnál.
  Po ≥ 10 s a kontrole typu `UIElement` se aplikace ukončí a výstup + logy aplikace se projdou na chyby načtení
  nativních knihoven (`Get-SmokeNativeLoadErrors`: `dyld[…]`, `Library not loaded`, `DllNotFoundException`,
  `Unable to load shared library`, `BadImageFormatException`, `EntryPointNotFoundException`,
  `TypeInitializationException`, „video decoder (FFmpeg) is not available“).
- **FFmpeg:** `lsof` ukázal, že aplikace při startu s prázdným profilem načte jen `libAvaloniaNative` a
  `libSkiaSharp`; FFmpeg (a HarfBuzz) až pro kameru. Proto kontrola navíc načte **všech 7 knihoven** kopie z `.dmg`
  přes `dlopen(RTLD_NOW)` (nový příkaz helperu `macos-verify-helper dlopen`) ve stejném `env -i` prostředí – tím se
  ověří, že se FFmpeg i se všemi závislostmi načte bez Homebrew. Stream kamery se podle rozhodnutí (otázka 1)
  nekontroluje.
- **Oprava v `aot-smoke.ps1`:** LaunchServices eviduje fyzickou cestu (`/private/var/folders/…`), skript porovnával
  `/var/folders/…`, takže registraci bundlu z dočasné složky neodebral (`mac.profile-unchanged` hlásil 5 změn
  LaunchServices). Cesta bundlu jde nově přes `Resolve-PhysicalPath`. Zbylou registraci z ladicího běhu jsem odebral
  (`lsregister -u`).
- **Oprava parsování `otool -L`:** univerzální soubory (SkiaSharp, HarfBuzz, AvaloniaNative) mají hlavičku
  `<cesta> (architecture …):` pro každou architekturu; berou se jen odsazené řádky.
- **App Translocation** („Přesuňte LayerPeek do složky Aplikace…“) bez skutečné karantény nastat nemůže, proto se
  nekontroluje; zůstává v T-127 (bod T-058, Gatekeeper / App Translocation).
- Pester: nový blok „Downloaded macOS release bundle (T-217, pure)“ (pravidla a zakázané prefixy, `otool -L` / `-l`
  včetně univerzálních souborů, `Info.plist`, argumenty `env -i`, rozpoznání chyb načtení) a v bloku `aot-smoke.ps1`
  dva testy se zástupným exe (prostředí bez `DOTNET_ROOT` / markeru, cesta s mezerou; chyba v logu = `FAILED`, profil
  vrácený) + odmítnutí `-CleanEnvironment` mimo macOS.
- Dokumentace: `docs/macos-manual-testing.md` (podmínky presetu, řádek tabulky), `docs/release.md` (krok 5).
  T-127: bod T-026 „čistý Mac“ doplněn o rozhodnutí 2026-11-13; odškrtne se podle pravidla z `tasks/README.md` po
  `PASS` v běhu `-Preset release` (ten jsem celý nespouštěl).
- Ověření: `caffeinate -d -u -i pwsh scripts/release-build.ps1 -SkipFeedSignature` (0.5.0, smoke obou testů PASS),
  pak `caffeinate -d -u -i pwsh tools/macos-verify.ps1 -Unattended -Only mac.release-bundle,mac.profile-unchanged`:
  oba `PASS` (`artifacts/verify/20261114-100115/`, čistý strom s tímto kódem), žádný připojený obraz, žádná kopie, žádná registrace
  v LaunchServices. `SKIP` s návodem ověřen dočasným přejmenováním `SHA256SUMS-macos.txt`. Pozn.: první
  `release-build.ps1` pod samotným `caffeinate -i` selhal ve smoke testu (typ v LaunchServices prázdný) – při spícím
  displeji aplikace čeká na displej (T-096); proto `-d -u` (jak píše `docs/release.md`).
- Žádná nová závislost (licence / AOT beze změny); podpis jen ad hoc z `vpk`, produkční klíč feedu se nepoužil
  (`-SkipFeedSignature`), nic na GitHub.
