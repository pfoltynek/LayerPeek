---
id: T-056
title: Velopack – ověření použitelnosti pro instalátor a aktualizace (spike)
status: todo
owner: ui-dev
priority: P2
platform: windows
depends_on: []
---

## Cíl
Uživatel preferuje pro instalátor Windows **Velopack** (rozhodnutí 2026-10-21). Před integrací (T-017, T-019) ověřit, že ho jde v tomto projektu opravdu použít. Výstupem je **písemné doporučení v `docs/velopack.md` (česky)** s odkazy na primární zdroje a s výsledky pokusů. **Žádná produkční integrace:** pokusy probíhají v dočasné větvi nebo ve složce mimo repozitář. Do `develop` jde jen dokument (a případné úpravy úkolů).

Když Velopack použít nejde, náhradní možností je WiX. Uživatel o WiX chce nejdřív diskutovat: spike WiX **nevybírá ani neprototypuje**, jen zapíše důvody a otevřenou otázku.

## Rozsah
Ověřit pokusem (P) nebo z dokumentace / zdrojového kódu (D) a ke každému bodu zapsat výsledek a zdroj:

1. **Native AOT a trimming (P):** NuGet `Velopack` v projektu App s `IsAotCompatible=true`, `PublishAot=true` a warnings-as-errors. `dotnet build` a AOT publish `win-x64` bez trim/AOT varování (žádné `NoWarn` ani potlačení). Zapsat verzi balíčku, licenci a cílové frameworky. Ověřit, že `VelopackApp.Build().Run()` na začátku `Main` funguje v AOT binárce.
2. **Balení AOT výstupu (P):** `vpk pack` nad AOT publish složkou (`dotnet publish -c Release -r win-x64`, nebo skript z T-055, pokud už je hotový; `--packDir`, `--mainExe PrusaTrayDiagnostic.exe`, `--packId`, `--packTitle "Prusa Tray Diagnostic"`). Velikost výsledku, jak `vpk` zachází s nativními DLL (FFmpeg, Avalonia), zda nevyžaduje .NET runtime na cílovém PC. Jak se `vpk` instaluje (dotnet tool) a jaké má požadavky na stroj (doplnit do `docs/prerequisites.md` až v T-017).
3. **Instalace pro jednoho uživatele (P):** `Setup.exe` instaluje do `%LocalAppData%\{packId}` bez admin práv. Zjistit skutečnou cestu exe (složka `current\`?) a zda zůstává stejná i po aktualizaci (aplikace porovnává cestu zástupce s `Environment.ProcessPath`, T-010).
4. **Zástupce ve Start menu, AppUserModelID a `ToastActivatorCLSID` (P):** T-010 a T-017 vyžadují zástupce `Prusa Tray Diagnostic.lnk` v kořeni Start menu uživatele s AUMID `PrusaTrayDiagnostic` a s `ToastActivatorCLSID` (kliknutí na toast, když aplikace neběží). Ověřit:
   - jestli Velopack umí tyto vlastnosti zástupce nastavit (`--shortcuts`, `Velopack.Windows.Shortcuts` / `ShellLink`, jiné volby);
   - pokud ne: co se stane, když Velopack vytvoří svého zástupce se stejným názvem a aplikace ho při startu přepíše (`AppShortcutRegistration`, viz `scripts/aot-smoke.ps1` a poznámky T-010) – přepíše ho Velopack znovu při aktualizaci? Smaže ho při odinstalaci?
   - variantu `--shortcuts None` (zástupce vytváří jen aplikace při prvním startu nebo v hooku `--veloapp-install`) a co pak s odinstalací.
   Výsledek: doporučená varianta s důvody.
5. **Hooky a odinstalace (P):** `--veloapp-install`, `--veloapp-updated`, `--veloapp-obsolete`, `--veloapp-uninstall` (limity 15–30 s, bez UI). Ověřit, že hook proběhne dřív, než zasáhne jediná instance aplikace (T-043), a že při odinstalaci jde uklidit: zástupce, klíč `HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\PrusaTrayDiagnostic`, historii toastů (`ToastNotificationManager.History.Clear("PrusaTrayDiagnostic")`), případně registraci COM aktivátoru a autostart. Co se má stát s nastavením a uloženými hesly (Credential Manager) při odinstalaci, je otevřená otázka.
6. **Autostart (D, případně P):** umí Velopack zástupce ve složce `Startup` (`--shortcuts Startup`) a jak se to slučuje s otázkou „autostart volitelný v nastavení“ z T-017.
7. **Aktualizace z GitHub Releases (D + P bez tokenu):** cílem je aktualizace z **veřejných** GitHub Releases bez tokenu v aplikaci (rozhodnutí uživatele 2026-10-21: repozitář bude později veřejný). Ověřit `GithubSource` (adresa repozitáře, předběžná vydání – verze `0.x` jsou na GitHubu `--prerelease`, viz T-055) a chování klienta, dokud je repozitář soukromý (aktualizace nenajde nebo selže – musí to být bez pádu a bez tokenu). Token se do aplikace neukládá.
8. **`win-arm64` (P nebo D):** `vpk pack` pro AOT publish `win-arm64`, kanály (`--channel`) pro x64 a arm64 ve stejném vydání, kolize názvů souborů (`RELEASES`, `Setup.exe`) mezi RID ve stejném GitHub vydání a jak to sladit s pojmenováním z T-055.
9. **Nepodepsané binárky (P):** chování SmartScreen u nepodepsaného `Setup.exe` a aktualizace bez podpisu (podpis rozhodnut: žádný).
10. **macOS pro T-019 (D):** co `vpk` vytvoří na macOS (`.pkg`, ne `.dmg`), požadavky na podpis a notarizaci, chování s ad-hoc podpisem (Gatekeeper, karanténa), zda jde kombinovat `.dmg` + `.tar.gz` (rozhodnutí uživatele pro macOS) s aktualizacemi přes Velopack, a kde `vpk` pro macOS běží (jen na Macu?).
11. **Licence a údržba (D):** licence Velopacku i `vpk`, aktivita projektu, četnost vydání, kompatibilita s .NET 11 preview.
12. Napojení na bod rozšíření skriptů z T-055 (jak by vypadal balicí krok Velopack). Pokud T-055 ještě není hotový, popsat podle jeho rozsahu.

Primární zdroje (výchozí body, doplnit konkrétní odkazy):
- https://docs.velopack.io/ (Packaging, Installers, Shortcuts, Hooks, Distributing)
- https://docs.velopack.io/integrating/hooks
- https://docs.velopack.io/integrating/shortcuts
- https://docs.velopack.io/packaging/installer
- https://github.com/velopack/velopack (zdrojový kód, licence, issues k AOT, AUMID a toastům)
- https://www.nuget.org/packages/Velopack

## Akceptační kritéria
- [ ] `docs/velopack.md` (česky) obsahuje ke každému bodu rozsahu výsledek, způsob ověření (pokus / dokumentace) a odkaz na zdroj
- [ ] Jasné doporučení: Velopack pro Windows ano / ne / s podmínkami, a zvlášť pro macOS (T-019)
- [ ] Nový NuGet balíček ověřený na AOT/trim kompatibilitu podle `CLAUDE.md` (výsledek pokusu, ne jen tvrzení dokumentace)
- [ ] Pokusné změny nejsou v `develop`. Pokusné instalace jsou odinstalované a po pokusech nezůstaly zástupci, klíče v registru, historie toastů ani soubory v `%LocalAppData%` (zapsáno, jak se ověřilo)
- [ ] Rozsah a závislosti T-017 a T-019 upravené podle výsledku. Nové otázky jsou v `## Otevřené otázky` tohoto úkolu a T-017 / T-019
- [ ] `dotnet build` a `dotnet test` bez chyb a varování (repozitář beze změny kódu)

## Otevřené otázky
- ~~Aktualizace ze soukromého repozitáře~~ → cíl jsou aktualizace z veřejných GitHub Releases bez tokenu v aplikaci. Repozitář bude později veřejný, do té doby se aktualizace přes token neřeší (rozhodnuto 2026-10-21).
- **WiX jako náhrada:** pokud Velopack nevyhoví, uživatel chce o WiX nejdřív diskutovat. Spike WiX nevybírá.
- **Data při odinstalaci:** má odinstalace smazat i nastavení aplikace, logy a uložená hesla (Credential Manager), nebo je ponechat pro případnou opětovnou instalaci?
- **Zástupce na ploše:** Velopack ho vytváří ve výchozím stavu. Chceš ho, nebo jen zástupce ve Start menu?

## Poznámky k implementaci
(vyplní agent po dokončení)
