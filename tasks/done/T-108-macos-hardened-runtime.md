---
id: T-108
title: macOS – hardened runtime v ad-hoc podpisu LayerPeek.app
status: done
owner: ui-dev
priority: P2
platform: macos
depends_on: [T-019, T-026]
---

## Cíl
Bezpečnostní revize 2026-10-29, nález **N3** (uživatel rozhodl opravit): ad-hoc podpis `LayerPeek.app` z T-019 nemá hardened runtime. Malware, které už běží pod stejným uživatelem, proto může spustit aplikaci s `DYLD_INSERT_LIBRARIES`, vložit do ní vlastní knihovnu a číst hesla tiskáren z login Keychainu bez dotazu (položky Keychainu důvěřují aplikaci LayerPeek, ne vložené knihovně).

## Rozsah
- `Invoke-MacAdHocSign` (`scripts/ReleaseTools/ReleaseTools.psm1`): dál ad-hoc (`--sign -`) a zevnitř ven, nově `--options runtime` u každého souboru Mach-O (hlavní exe i knihovny) a entitlements hlavního exe z `build/macos/LayerPeek.entitlements`.
- Minimální entitlements ověřené skutečným spuštěním, ne odhadem (Native AOT, `libAvaloniaNative`, `libSkiaSharp`, `libHarfBuzzSharp`, knihovny FFmpeg z T-026).
- Kontrola v kroku `app` skriptu `release-build.ps1` (`Get-MacHardenedRuntimeProblems`): příznak runtime (0x10000) u každého Mach-O, přesně očekávané entitlements u hlavního exe.
- Testy Pester (čisté funkce + skutečný `codesign` a spuštění na macOS).
- Dokumentace: `docs/release.md` (podpis), T-019, `CHANGELOG.md` [0.3.1] Security.

## Akceptační kritéria
- [x] Každý soubor Mach-O bundlu je podepsaný ad-hoc s hardened runtime (`flags=0x10002(adhoc,runtime)`), hlavní exe s entitlements z `build/macos/LayerPeek.entitlements`
- [x] Entitlements jsou minimální a zdůvodněné skutečným během (viz poznámky): jen `com.apple.security.cs.disable-library-validation`
- [x] `DYLD_INSERT_LIBRARIES` se u podepsané aplikace ignoruje (ověřeno sondou Native AOT se skutečnými knihovnami aplikace i testem Pester; kontrolní běh bez hardened runtime knihovnu vloží)
- [x] Knihovny FFmpeg, Avalonia, Skia a HarfBuzz se pod hardened runtime načtou a fungují (sonda Native AOT podepsaná stejně jako vydání)
- [x] `codesign --verify --deep --strict` projde, `codesign -d --entitlements -` ukazuje zvolené entitlements; kontroly Pester
- [x] `caffeinate -d -u -i pwsh scripts/release-build.ps1` projde včetně smoke testu podepsaného `LayerPeek.app` – spustil hlavní agent 2026-10-29 na develop (`4746ac9`): exit 0, Release testy 7 670 / 0 selhání, „Hardened runtime on every Mach-O file, entitlements: com.apple.security.cs.disable-library-validation“, oba smoke testy PASSED, `flags=0x10002(adhoc,runtime)`, profil beze změny, žádná registrace `layerpeek-rt-` v LaunchServices
- [x] ~~Ruční ověření uživatelem: dialog Keychainu po přeinstalaci~~ → podle rozhodnutí uživatele (2026-10-29) není podmínkou uzavření; zůstává jako krok v `docs/macos-manual-testing.md`
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. ~~**Zbytkové riziko `disable-library-validation` – přijmout?**~~ → přijato jako dočasné, zapsáno do T-060 (Developer ID umožní library validation) (rozhodnuto 2026-10-29). Bez tohoto entitlementu aplikace s hardened runtime nenastartuje: library validation přijímá jen knihovny se stejným Team ID (nebo od Applu) a ad-hoc podpis Team ID nemá, dyld proto odmítne i vlastní knihovny bundlu („mapping process and mapped file (non-platform) have different Team IDs“, ověřeno). S entitlementem je hlavní cíl N3 splněný (`DYLD_INSERT_LIBRARIES` a ostatní `DYLD_*` proměnné se ignorují, protože chybí `allow-dyld-environment-variables`). Zůstává: kdo může **zapisovat do bundlu** (`/Applications/LayerPeek.app` přetažená z `.dmg` patří uživateli), může vyměnit např. `libSkiaSharp.dylib`; cdhash hlavního exe se tím nezmění, takže Keychain se nezeptá. Ochranu macOS „App Management“ (Ventura+) proti úpravám cizích aplikací jsem pro ad-hoc podepsanou aplikaci neověřil. Úplné řešení: podpis Developer ID (Team ID → library validation bez výjimky, T-060), nebo statické linkování všech nativních knihoven do exe (pro Avalonia/Skia/HarfBuzz NuGet balíčky statické knihovny pro macOS nedodávají, jen FFmpeg bychom uměli). Varianty: (a) přijmout jako dočasný stav do T-060 a zapsat do T-060, (b) jiné řešení.
2. ~~**Uzavření úkolu:**~~ → done po zeleném `release-build.ps1`, dialog Keychainu jen jako krok pro uživatele (rozhodnuto 2026-10-29). smí být T-108 `done` po zeleném `release-build.ps1` (spustí hlavní agent), s dialogem Keychainu jen jako zdokumentovaným krokem pro uživatele, nebo počkat na ruční ověření?

## Poznámky k implementaci
**Implementováno na Macu (2026-10-29, ui-dev; Apple Silicon, macOS 27.2, SDK `11.0.100-rc.3`).** Žádný nový NuGet balíček; nástroje (`codesign`, `clang` z Xcode Command Line Tools) jsou součástí macOS / už požadované pro Native AOT.

**Podpis.** `Get-MacAdHocSignArguments` = `--force --sign - --timestamp=none --options runtime` (+ `--entitlements <soubor>` jen u bundlu, tedy hlavního exe; knihovny entitlements nemají). Příznak runtime u knihoven proces nijak neovlivňuje (rozhoduje hlavní exe), ale notarizace (T-060) ho bude vyžadovat u všech souborů. `Invoke-MacAdHocSign -Entitlements` (výchozí `build/macos/LayerPeek.entitlements`, `Get-MacEntitlementsPath`). `Get-MacHardenedRuntimeProblems`: `codesign -d --verbose=4` u každého Mach-O (příznak `0x10000` a ad-hoc `0x2`, `Get-CodesignFlags`), `codesign -d --entitlements - --xml` u hlavního exe musí dát přesně `EntitlementKeys` z nastavení (`Get-EntitlementKeys`; hodnota jiná než `<true/>` se počítá jako rozdíl). Volá se v `release-build.ps1` hned po `codesign --verify --deep --strict`, před smoke testem.

**Volba entitlements – ověřeno sondou** (konzolová aplikace Native AOT v `$TMPDIR`, v bundlu se všemi 7 knihovnami z `dotnet publish -c Release -r osx-arm64` aplikace, podepsaná stejně jako vydání; `NativeLibrary.Load` každé knihovny, volání `avcodec_version`, `hb_version_string`, `sk_version_get_milestone`, zpětné volání `[UnmanagedCallersOnly]` a thunk `Marshal.GetFunctionPointerForDelegate`):

| Podpis | Knihovny | `DYLD_INSERT_LIBRARIES` (testovací dylib zapisuje do logu) |
|---|---|---|
| ad-hoc bez runtime (dosavadní stav) | všech 7 načteno, volání OK | **vložena** (log zapsán, proměnná viditelná v procesu) |
| ad-hoc + runtime, bez entitlements | **všech 7 odmítnuto**: „not valid for use in process: mapping process and mapped file (non-platform) have different Team IDs“ | ignorována |
| ad-hoc + runtime + `disable-library-validation` (zvoleno) | všech 7 načteno, volání, callback i thunk OK | **ignorována** (žádný log, dyld proměnnou z prostředí odstranil) |

- `allow-jit`, `allow-unsigned-executable-memory`: Native AOT je nepotřebuje (sonda i thunky běží bez nich). Výchozí entitlements .NET, které dává `vpk --signAppIdentity -` (`docs/velopack.md`, bod 10), jsou pro JIT runtime, ne pro AOT.
- `allow-dyld-environment-variables`: záměrně ne (to je právě N3). `get-task-allow`: ne.
- `automation.apple-events`, kamera, mikrofon apod.: aplikace je nepoužívá (`open` pro URL/složky je samostatný proces, Keychain, notifikace, `SMAppService` a místní síť entitlement nepotřebují).
- FFmpeg se načítá jen absolutní cestou ze složky aplikace (`FFmpegRuntime.ResolveFromApplicationFolder`), systémové frameworky absolutními cestami; `DYLD_LIBRARY_PATH` / `DYLD_FALLBACK_*` hardened runtime také ignoruje, takže `disable-library-validation` neotevírá podstrčení knihovny přes proměnné prostředí, jen zápisem do bundlu (otázka 1).

**Testy Pester** (`ReleaseTools.Tests.ps1`): argumenty podpisu, `Get-CodesignFlags`, `Get-EntitlementKeys`, soubor entitlements = přesně `EntitlementKeys`, pořadí kroků v `release-build.ps1`; na macOS skutečný podpis (runtime u exe i knihovny, entitlements jen u exe, nahlášení knihovny bez runtime i jiných entitlements) a test se skutečným během: `clang` sestaví exe, které `dlopen` knihovnu vedle sebe, a vkládanou knihovnu. Podepsáno jako vydání → knihovna se načte, vložená ne; bez entitlementů → „different Team IDs“; bez runtime (kontrola, že test vkládání vůbec funguje) → vložená knihovna zapíše log. Spuštění exe v bundlu ho zaregistruje v LaunchServices uživatele, proto test registraci po sobě vždy zruší (`lsregister -u`), ověří, že pod dočasnou složkou testů nic registrované nezůstalo, a `AfterAll` kontextu uklidí i po selhání. (Tři registrace z prvních běhů testu jsem zrušil `lsregister -u` s přesnými cestami; `lsregister -dump | grep layerpeek-rt-` je prázdné.)

**Výsledky:** `dotnet build` a `dotnet build -c Release` 0 varování, `dotnet test` 0 selhání (viz závěrečná zpráva), Pester 0 selhání.

**Neověřeno lokálně (sandbox agenta blokuje přímé spuštění `pwsh`):**
- `caffeinate -d -u -i pwsh scripts/release-build.ps1` – skutečný `LayerPeek.app` s hardened runtime, `Get-MacHardenedRuntimeProblems`, smoke test bundlu (start UI Avalonie, Skia, menu bar, typ `UIElement`) se zálohou profilu. Knihovny i runtime Native AOT jsou ověřené sondou, celá aplikace ne. Spustí hlavní agent.
- Kamera s FFmpeg v podepsaném bundlu proti simulátoru: Release build nemá testovací režim, mimo `aot-smoke.ps1` se nespouští; načtení a volání knihoven FFmpeg ověřila sonda.

**Keychain – krok pro uživatele.** Hardened runtime sám o sobě dialog Keychainu nepřidává ani neodstraňuje. Položka důvěřuje aplikaci podle designated requirement, u ad-hoc podpisu = cdhash hlavního exe (T-021). Nový podpis (s runtime a entitlements) mění cdhash stejně jako každé nové sestavení, takže po instalaci verze s T-108 se macOS jednou zeptá „LayerPeek chce použít důvěrné informace…“ (stejně jako po každé aktualizaci). Ověřit ručně: nainstalovat vydání bez T-108, uložit heslo tiskárny, přetáhnout přes něj `LayerPeek.app` s T-108, spustit → očekávaný jeden dialog; po „Vždy povolit“ další start bez dialogu. Dále ověřit, že dialog po aktualizaci nepřichází opakovaně u téhož sestavení.
