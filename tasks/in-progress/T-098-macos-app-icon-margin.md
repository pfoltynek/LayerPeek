---
id: T-098
title: macOS – ikona aplikace s okrajem podle Applu
status: in-progress
owner: ui-designer
priority: P3
platform: macos
depends_on: [T-019, T-025]
---

## Cíl
Ikona aplikace (`app-icon.svg` / `app-icon-1024.png` z T-025) vyplňuje skoro celé plátno 1024 px. Ikony na macOS mají od verze 11 kolem těla okraj (tělo zhruba 824 × 824 px na plátně 1024 px, podle Apple Human Interface Guidelines), takže ikona LayerPeeku v Docku, Finderu a ⌘Tab působí větší než ostatní. Zjistil ui-designer při revizi textů (2026-10-29); úkol založen rozhodnutím uživatele.

## Rozsah
- Varianta ikony pro macOS s okrajem a tvarem podle aktuálních Apple Human Interface Guidelines (rozměr těla, případně stín); malé velikosti 16 a 32 px dál ze zjednodušené podoby (T-019).
- Sestavení `.icns` v kroku `app` (`New-MacIcon` v `scripts/ReleaseTools/ReleaseTools.psm1`) z nové varianty.
- Ikona pro Windows (`app-icon.ico`, exe, okna) beze změny.
- Porovnání vedle systémových aplikací v Docku a Finderu (ověří uživatel na Macu).

## Akceptační kritéria
- [x] `.icns` obsahuje variantu s okrajem ve všech velikostech, test / Pester kontroluje rozměry
- [ ] V Docku a Finderu působí stejně velká jako systémové aplikace (ověřeno uživatelem)
- [x] Windows ikona beze změny
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Návrh UI
ui-designer, 2026-10-29. Platí jen pro macOS (`.icns` v `LayerPeek.app`); Windows (`app-icon.ico`, exe, okna, tray) beze změny.

### Zdroje (ověřeno)
- **Apple HIG – App icons** (aktuální verze s Liquid Glass): na macOS jsou ikony čtvercové a tvar se zaoblenými rohy má odpovídat ostatním prvkům systému; pro umístění obsahu odkazuje na mřížky v šablonách *app icon production templates* (Apple Design Resources). Bitmapová ikona je dál podporovaná (u bitmapy se systém pokusí ikonu rozdělit na popředí a pozadí). https://developer.apple.com/design/human-interface-guidelines/app-icons
- **Mřížka macOS 11+** (Apple Developer Forums, odpovědi komunity s odkazem na šablonu Applu): plátno 1024 × 1024 px, tělo 824 × 824 px uprostřed (okraj 100 px), spojitě zaoblené rohy s poloměrem 185,4 px, stín. https://developer.apple.com/forums/thread/670578, https://developer.apple.com/forums/thread/761179
- **macOS 26+:** ikona, která tvar nedodrží, se zmenší a zavře do šedého „squircle“ (https://en.wikipedia.org/wiki/MacOS_Tahoe, https://daringfireball.net/2025/08/macos_26_tahoes_dead_canary_utility_app_icons).
- **Měření na tomto Macu (macOS 27.2, jen čtení):** `AppIcon.icns` Kalkulačky, Poznámek, TextEditu a Terminálu (`iconutil -c iconset` do pracovní složky): při 256 px tělo 206 px od 25 px (= 824/1024), při 128 px 102 px od 13 px, při 32 px tělo 3..28, při 16 px 2..13 – **malé velikosti mají stejnou mřížku zmenšenou, také s okrajem**. Stín je v `.icns` zapečený (od 128 px; při 32 px žádný): pod tělem silnější než nad ním. Fit na profil průhlednosti: Gaussův stín σ = 18 px, posun dolů 10 px, černá 22 % (na plátně 1024; shoda ±2 hodnoty alfa). Tvar rohů: spojité rohy s poloměrem 185,4 px se se systémovou maskou při 256 px liší ve 43 krajních pixelech z 40 500, kruhové oblouky ve 123 – proto spojité rohy.

### Rozhodnutí designera
- **Dlaždice se přerámuje na tvar macOS** (ne „současná ikona zmenšená do těla“): tmavá dlaždice z T-025 je přímo tělo ikony 824 × 824 px se spojitými rohy Applu. Důvody: (1) od macOS 26 systém ikony jiného tvaru zavírá do šedého squircle – dlaždice s kruhovými rohy z T-025 by se od masky lišila v rozích; (2) vedle systémových ikon má stejnou velikost, tvar rohů i stín; (3) dlaždice uvnitř těla by dala „rámeček v rámečku“. Obsah (hlava + 3 vrstvy), barvy a světlejší lem (#4A4C55) zůstávají; obsah je zmenšený ve stejném poměru jako tělo (824/960), lem 14 px uvnitř těla.
- **Stín** je zapečený jako u systémových ikon (od 64 px; malé velikosti bez stínu jako u Applu).
- **Malé velikosti 16 a 32 px:** zůstává zjednodušená varianta (T-019), ale na mřížce Applu (systémové ikony mají okraj i při 16/32 px) a ručně na celých pixelech: 32 px = tělo 26 px (3..29), 16 px = tělo 12 px (2..14), lem 1 px.

### Mřížka a rozměry
```
1024 px (app-icon-macos.svg)                32 px (app-icon-macos-32.svg)     16 px (app-icon-macos-16.svg)
+--------------------------------------+    0  3                  29 32      0 2          14 16
|  okraj 100 px (jen stín)             |    . . . . . . . . . . . . .        . . . . . . . .
|   .--------------------------------. |    .  .-lem 1 px------------.       .  .-lem----.
|  /  tělo 824 x 824, rohy r 185,4    \|    .  | 6..8   ▬▬▬ (6 px)   |       .  | 3  ▬▬    |  hlava bez mezery
|  |   lem #4A4C55 14 px, #2B2C33      ||    .  | 9..11 ▬▬▬▬▬ (10 px) |       .  | 4-5 ▬▬▬  |  (10 řádků uvnitř
|  |        hlava #F4F4F6              ||    .  | 12..13  ▬ (4 px)    |       .  | 6   ▬    |   lemu)
|  |   ███████░░░░░  vrstva 3 (část)   ||    .  | 18..20 ███████ 20px |       .  | 8-9 ████ |  vrstvy 8 px
|  |   ████████████  vrstva 2          ||    .  | 23..25 ███████      |       .  | 11-12████|
|  |   ████████████  vrstva 1          ||    .  '---------------------'       .  '---------'
|   \________________________________/ |    tělo 26 px, bez stínu             tělo 12 px, bez stínu
|      stín: σ 18, dolů 10, 22 % černá |
+--------------------------------------+
```
- `.icns`: 16 px a 32 px (16 bodů @1x/@2x, 32 bodů @1x) přesně z `app-icon-macos-16/32.png`; 64–1024 px zmenšené `sips` z `app-icon-macos-1024.png` (tělo vždy 824/1024, např. 64 px: 7..56, 128 px: 13..114 – stejně jako systémové ikony).
- Rizika: 16 px má hlavu bez mezery mezi chladičem a blokem (v 10 řádcích uvnitř lemu se T-025 hlava s mezerou a dvě vrstvy nevejdou). Lem 1 px při 16 px drží dlaždici odlišenou na tmavém pozadí. Jazykově nezávislé (bez textu).
- Náhledy (mimo repozitář): staré vs. nové ve všech velikostech na světlém i tmavém pozadí, vedle systémových ikon, malé velikosti zvětšené 8× – viz Poznámky k implementaci.

## Otevřené otázky
Vzhled (včetně 16 px) schválil uživatel 2026-10-29.
1. ~~**Staré soubory pro macOS:** `app-icon-1024.png`, `app-icon-16.png`, `app-icon-32.png` odstranit, nebo vrátit?~~ → zůstávají odstraněné (rozhodnuto 2026-10-29).
2. ~~**Pomocný renderer:** nechat mimo repozitář, nebo přidat `tools/render-macos-icon.swift`?~~ → přidat do repozitáře jako `tools/render-macos-icon.swift` (rozhodnuto 2026-10-29), viz Poznámky.
3. ~~**Icon Composer / Liquid Glass:** zůstat u `.icns`, nebo nový úkol na `.icon` + `Assets.car`?~~ → zůstat u `.icns`, úkol na Liquid Glass se nezakládá (rozhodnuto 2026-10-29).
4. ~~**16 px:** hlava bez mezery mezi chladičem a blokem (a), nebo bez chladiče (b)?~~ → varianta (a) (rozhodnuto 2026-10-29).

## Poznámky k implementaci
- **Assety** (`src/LayerPeek.App/Assets/AppIcon/`): `app-icon-macos.svg` (1024, tělo s lemem, obsah z `app-icon.svg` zmenšený maticí 3,43333 kolem středu, krk a tryska o kus překryté se sousedy, aby se mezi nimi nevykreslil šev vyhlazování; stín jako filtr `feGaussianBlur`/`feOffset`/`feFlood`), `app-icon-macos-32.svg` a `app-icon-macos-16.svg` (celé pixely, bez stínu) a z nich `app-icon-macos-1024.png`, `-32.png`, `-16.png`. Cesty spojitých rohů jsou rozepsané Bézierovy křivky (rozšířená rekonstrukce spojitého rohu `UIBezierPath`, koeficienty v násobcích poloměru), vnitřní hrana lemu má poloměr o šířku lemu menší (rovnoměrný lem). Barvy jako T-025. Žádná nová závislost, licence beze změny (vlastní grafika).
- **Jak vznikají PNG:** `swift tools/render-macos-icon.swift` (přidáno po rozhodnutí 2026-10-29; z libovolné složky, potřebuje jen macOS a Xcode Command Line Tools) vykreslí `app-icon-macos.svg` → `app-icon-macos-1024.png` a `app-icon-macos-32/16.svg` → přesné PNG systémovým rendererem (CoreSVG přes `NSImage`) do bitmapy sRGB 8 bit s alfou, bez převodu barev a bez metadat; `--out <složka>` zapíše jinam. Výstup je deterministický: na macOS 27.2 dal bajtově stejné PNG jako commitnuté. Pester test (`reproduces the committed macOS icon PNGs…`, jen na macOS a se `swift`) skript spustí do dočasné složky a porovná bajty; kdyby jiná verze macOS kódovala nebo vyhlazovala jinak, porovná pixely (±2 na kanál) a při rozdílu selže s výzvou PNG vygenerovat znovu (ověřeno úmyslně změněným PNG: test selhal, 216 rozdílných hodnot). Kontrola rendereru: `app-icon-small.svg` vykreslené takto se od PNG z T-025 (Svg.Skia) liší jen vyhlazením rohů (59 z 1 024 pixelů při 32 px), plné barvy shodné. Při změně SVG stačí skript spustit znovu.
- **Odstraněno:** `app-icon-1024.png`, `app-icon-16.png`, `app-icon-32.png` (jen zdroj `.icns`, viz otázka 1). Výjimka v csproj je teď `Assets\AppIcon\app-icon-macos-*.png` (nejsou `AvaloniaResource`).
- **`ReleaseTools.psm1`:** `IconMaster`/`IconExact` ukazují na macOS varianty, nové `IconBody` (100/824 na 1024 px). `ConvertFrom-BmpAlpha` (alfa z 32bit BMP od `sips`, oba směry řádků, čistá funkce), `Get-MacIconBodyProblems` (čistá funkce: krabice pixelů s alfou ≥ 128 = tělo na mřížce ±1 px, střed neprůhledný, v okraji alfa nanejvýš 64 = stín), `Get-MacIconImageProblems` (totéž pro PNG přes `sips`). `New-MacIcon` po sestavení `.iconset` zkontroluje každý obrázek (rozměr + mřížka) a ikonu, která mřížku nedodrží, odmítne – release se starou dlaždicí přes celé plátno by tedy skončil chybou. `release-build.ps1` beze změny (bere hodnoty z `Get-MacBundleSettings`).
- **Testy:** Pester – nastavení macOS ikony (cesty, `IconBody`), `ConvertFrom-BmpAlpha` (shora dolů i zdola nahoru, obě masky alfa, odmítnutí), `Get-MacIconBodyProblems` (správné tělo 1024/256/32/16, stará dlaždice T-025 → 4 chyby, plný čtverec, neprůhledný okraj, díra uprostřed, prázdný a nečtvercový obrázek); na macOS: `.icns` zpět přes `iconutil -c iconset` → všech 10 obrázků se správným rozměrem a tělem na mřížce, u 1024 px průhledný okraj 100 px kromě stínu (alfa < 64, pod tělem víc než nad ním, rohy plátna 0) a odmítnutí předlohy přes celé plátno; stávající test malých velikostí (16/32 px přesně z PNG, 64 px zmenšená předloha) dál platí s novými soubory. UI testy (`AppIconUiTests`): `MacIconImagesFollowTheAppleGrid` (16/32/1024: rozměr, tělo, okraj průhledný, u 1024 jen stín) a `MacIconSmallSizesAreTheSimplifiedDesignOnWholePixels` (lem, dlaždice, hlava, mezery a vrstvy přesně na pixelech v barvách T-025) nahradily test, že malé PNG jsou pixely `.ico`. Windows testy (`OnlyExpectedAssetsAreBundled` s přesným seznamem – macOS PNG v něm nejsou, `.ico`, okna, tray, App.Tests `ApplicationIconTests`, E2E) beze změny.
- **Náhledy pro uživatele** (mimo repozitář): `/private/tmp/claude-501/-Volumes-Data-SourceCode-LayerPeek/b4491783-8894-4028-8a4e-a642937bd4ca/scratchpad/t098/compare-old-new.png`, `next-to-system-apps.png`, `small-sizes-8x.png`.
- **Ověření (2026-10-29, macOS 27.2, Apple Silicon):** `dotnet build` 0 varování, 0 chyb; `dotnet test` celkem 7 490, úspěšné 7 310, neúspěšné 0, přeskočeno 180 (Pester přes `LayerPeek.Scripts.Tests` zelený). `tools/profile-snapshot.ps1 -Save/-Compare` kolem běhů: profil beze změny (Login Items bez oprávnění nečitelné, jako vždy).
- **Bundle:** `scripts/release-build.ps1` skončil už na kroku `dotnet build (Release)` chybou, která s tímto úkolem nesouvisí: `tests/LayerPeek.App.Tests` se v Release nepřeloží (`ServiceHostTests` volá `AppEnvironment.ForTests`, které je jen v `#if DEBUG`; `WaitingInstanceCommandsTests._waitOver` se v Release nepřiřadí → CS0649/IDE0044; obojí z T-099). Skript před tím smazal předchozí výstup `artifacts/release/0.2.0/`. Krok `app` jsem proto ověřil stejnými funkcemi v pomocném skriptu v pracovní složce: `dotnet publish -c Release -r osx-arm64 -warnaserror` (AOT), `aot-smoke.ps1` nad publish složkou (PASSED, UIElement), `New-MacIcon` (kontrola mřížky prošla), `New-MacAppBundle`, `plutil -lint`, kontrola knihoven, ad-hoc podpis, `codesign --verify --deep --strict` platný, `aot-smoke.ps1` nad bundlem (PASSED, podpis dál platný). `iconutil -c iconset` na `Contents/Resources/LayerPeek.icns`: 10 obrázků 16–1024 px, tělo 2..13 (16), 3..28 (32), 7..56 (64), 13..114 (128), 25..230 (256), 50..461 (512), 100..923 (1024) – při 128 a 256 px přesně jako systémové ikony; 32 a 1024 px bajtově shodné se zdrojovými PNG, 16 px (iconutil ukládá ARGB) liší se jen v průsvitných pixelech rohů.
- **Zbývá (uživatel):** porovnání ve Finderu, Launchpadu / Aplikacích a v Nastavení systému vedle systémových aplikací (LayerPeek nemá ikonu v Docku ani v ⌘Tab, `LSUIElement`), včetně toho, že ho macOS 26+ nezavře do šedého squircle; ikona v notifikacích (T-020) a v dialogu Místní síť. Neověřeno: macOS 14/15 (stín se tam kreslí jen ten zapečený).
