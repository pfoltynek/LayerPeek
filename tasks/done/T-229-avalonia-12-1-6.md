---
id: T-229
title: Aktualizace Avalonia 12.1.4 → 12.1.6
status: done
owner: ui-dev
priority: P2
platform: all
depends_on: []
---

> **Požadavek uživatele 2026-11-15:** aktualizovat Avalonii na nejnovější verzi a pak ověřit, zda chyba San Francisco
> z T-222 trvá a zda už je nahlášená v Avalonii (o nahlášení rozhodne uživatel).

## Cíl
Aplikace a testy na nejnovější stabilní Avalonii 12.x (12.1.6) s opravami z 12.1.5 a 12.1.6; ověřit, že se nic
nerozbilo (testy, snímky, Native AOT), a změřit chybu San Francisco z T-222 znovu na nové verzi.

## Rozsah
- Všechny balíčky `Avalonia.*` v `Directory.Packages.props` na 12.1.6; ověřit, že novější stabilní verze není.
- Projít poznámky k vydání 12.1.5 a 12.1.6 (změny, opravy pro nás: TrayIcon, NativeMenu, text, headless) a změny
  tranzitivních balíčků (SkiaSharp, HarfBuzzSharp a další).
- Seznam licencí třetích stran (verze komponent) a testy, které hlídají verzi Avalonie.
- Build, celý `dotnet test`, snímky macOS přegenerovat jen při změně vykreslení, `dotnet publish -c Release -r
  osx-arm64` bez varování AOT, krátké spuštění Debug aplikace v testovacím režimu; `CHANGELOG.md`.
- Znovu změřit T-222 (`.AppleSystemUIFont` Normal / SemiBold / Bold proti AppKitu) a vyhledat existující hlášení
  v AvaloniaUI/Avalonia (jen čtení); výsledky do poznámek T-222.

## Akceptační kritéria
- [x] Všechny balíčky `Avalonia.*` na 12.1.6, novější stabilní verze neexistuje; poznámky k vydání a tranzitivní
      balíčky prošlé a zapsané v poznámkách
- [x] Seznam licencí třetích stran a testy hlídající verzi Avalonie aktualizované
- [x] Snímky UI zkontrolované (přegenerované jen při změně vykreslení); ověření na Windows → přesunuto do T-125
- [x] `dotnet publish -c Release -r osx-arm64` bez varování AOT; Debug aplikace v testovacím režimu nastartuje
- [x] Měření T-222 na 12.1.6 a hledání existujícího hlášení zapsané v T-222 (oddíl 2026-11-15)
- [x] `CHANGELOG.md` doplněný
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- **Verze:** NuGet 2026-11-15 – nejnovější stabilní `Avalonia` je **12.1.6** (9. 11. 2026, po 12.1.5 z 2. 11.), stejně
  `Avalonia.Desktop`, `.Themes.Fluent`, `.Fonts.Inter`, `.Headless`, `.Skia`; 12.2 existuje jen jako CI / noční
  sestavení. Všech 6 `PackageVersion` v `Directory.Packages.props` 12.1.4 → 12.1.6. Licence beze změny (MIT), AOT
  beze změny (stejné balíčky, publikace bez varování IL/AOT).
- **Tranzitivní balíčky beze změny:** SkiaSharp 3.119.4, HarfBuzzSharp 8.3.1.3, Avalonia.Angle.Windows.Natives
  2.1.27548.20260419, MicroCom.Runtime 0.11.6, Tmds.DBus.Protocol 0.94.1, Avalonia.BuildServices 11.3.2 (nuspec 12.1.4
  a 12.1.6 se liší jen vlastní verzí Avalonie; `project.assets.json`). Proto se nemění ani zbytek seznamu licencí.
- **Poznámky k vydání 12.1.5 / 12.1.6 – co se nás týká:** text (Bidi W2, konec odstavce v Bidi řádku, hledání
  zalomení jen v dosahu řádku, `HitTestPoint`, uvolnění bufferu `ShapeTogether`, `Typeface.Normalize` toleruje mezery),
  „menu se samo zavírá při znovuotevření“ (#22399, ovládací prvky Avalonie, ne `NativeMenu`), arrange `Window` při
  omezení velikosti platformou (#22488), `IsActive` před `Window.Activated` (#22716), opravy nativní paměti
  a prostředků (#22757), Skia geometrie (souběh mezí cesty), macOS přístupnost (AXMenu, AXFocused, live region),
  `MACOSX_DEPLOYMENT_TARGET` 12.0 (naše minimum je 14.0 → bez dopadu), Windows `Screen.DisplayName` (#22593, → T-125),
  `GetDpiForMonitor` jen x86. **Žádná změna API, která by nás rozbila** (build bez úprav kódu). Strom zdrojů mezi
  tagy: v `native/Avalonia.Native` se změnily jen `automation.mm` a cíl nasazení v `project.pbxproj` – `menu.mm`
  (`AvnAppMenuItem::SetIcon`, velikost ikon menu, T-116), okna, Dock i `cancelOperation:` beze změny; v
  `src/Avalonia.Base/Media/Fonts`, `src/HarfBuzz` a vykreslování textu Skia beze změny; headless beze změny.
- **Kód / testy:** `Licenses/ThirdPartyLicenses.cs` (verze komponenty Avalonia a jejích 12 balíčků), testy verze
  v `LicensesViewModelTests` a `LicensesWindowUiTests`, hlídací test `PrinterMenuIconSizeTests` →
  `TheMacOsSizingIsCheckedAgainstAvaloniaNative1216` (ověřeno, že `menu.mm` je beze změny; komentáře
  `PrinterMenuIconSize` na „12.1.4–12.1.6“). Ostatní komentáře „Avalonia 12.1.4“ popisují chování ověřené na 12.1.4
  a zůstávají (nativní kód, kterého se týkají, je ve 12.1.6 stejný).
- **Snímky:** nepřegenerovány – `dotnet test` (UI testy včetně všech snímků macOS) prošel s dosavadními PNG, tedy
  vykreslení se nezměnilo; odpovídá nezměněnému kódu písem a Skia a bajtově shodnému měření T-222. Windows: build,
  testy, AOT a krátká ruční kontrola → **T-125 oddíl 2ad**.
- **Ověřeno (macOS 27.2.1, Apple Silicon, 2026-11-15):** `dotnet build` 0 varování; `dotnet test` celé řešení
  (první běh: 6 selhání = jen očekávané verze „12.1.4“ v testech; po úpravě oba projekty zelené; závěrečný běh
  10 729 testů: 10 458 OK, 270 přeskočeno, 1 selhání `CameraStreamHubAllocationTests.GrayscaleKeyFrameSamplesDoNotAllocate`
  (840 B, Core bez Avalonie, souběžně běžel `macos-verify.ps1` jiné relace; samotná třída 3× a celé `LayerPeek.Core.Tests`
  znovu zelené → nestabilita alokačního testu pod zátěží, ne T-229); `dotnet publish src/LayerPeek.App -c Release -r osx-arm64` bez varování IL/AOT (jen varování nativního linkeru /
  `dsymutil` o modulové cache a hláška `xcodebuild` o Command Line Tools – nástroje Xcode, ne IL/AOT, jako v T-223), binárka 37 914 104 → 37 930 680 B (+16,6 kB);
  Debug aplikace v testovacím režimu (`LAYERPEEK_TEST_DATA_DIR`, `LAYERPEEK_INSTANCE_SUFFIX`) nastartovala, běžela
  18 s bez varování a chyb v logu, ukončena `kill`.
- **T-222:** měření na 12.1.6 – chyba SF trvá, výsledky bajtově shodné s 12.1.4; hledání existujícího hlášení
  (nejbližší #21565, přesné hlášení neexistuje) – oddíl „2026-11-15“ v T-222.
- Uzavřeno 2026-11-15: ověření na Windows přesunuto do T-125 (oddíl 2ad).
- **Neověřeno:** Windows (→ T-125 2ad); běžící okna na obrazovce Macu (bez oprávnění Zpřístupnění, jen start a log);
  `main` / noční sestavení Avalonie jen čteny.
