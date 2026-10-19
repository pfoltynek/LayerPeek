---
id: T-025
title: Ikona aplikace (exe, okna, tray)
status: in-progress
owner: ui-designer + ui-dev
priority: P2
platform: all
depends_on: [T-007]
---

## Cíl
Aplikace má vlastní ikonu v barvách Prusa se stylizovaným motivem 3D tiskárny. Ikona je u exe, ve všech oknech (titulek, hlavní panel) a v tray. Tray ikony stavů z T-007 převezmou nový motiv a stav dál zobrazují.

## Současný stav (2026-10-19)
- `src/PrusaTrayDiagnostic.App/Assets/tray-idle.png` – jediný obrázek, používají ho okna jako `Icon="/Assets/tray-idle.png"` (Settings, Camera, Diagnostics, About, Licenses).
- Exe nemá `ApplicationIcon` (obecná ikona Windows). Toasty (T-010) přebírají ikonu ze zástupce / exe.
- Tray ikony se kreslí za běhu (`Tray/Icons/TrayIconRenderer`, 32×32 px při 96 DPI, `ColorTrayIconPalette`): barevná tečka podle stavu, kroužek s progressem (tisk modře, pauza žlutě), přeškrtnutý kroužek (bez dat). Cache podle `TrayIconKey`. Testy `ColorTrayIconPaletteTests`, `TrayIconCacheTests`, `TrayIconPresenterTests`.

## Rozhodnuto
- **Vzhled (2026-10-19):** vlastní ikona v barvách Prusa (oranžová Prusa + tmavě šedá) se stylizovaným motivem 3D tiskárny. **Bez loga a ochranné známky Prusa.**
- **Kde (2026-10-19):** ikona exe, všechna okna (titulek, hlavní panel) a tray. Tray ikony stavů z T-007 převezmou nový motiv a zachovají zobrazení stavu (způsob navrhne ui-designer).
- **Postup (2026-10-19):** agent `ui-designer` připraví 2–3 návrhy, uživatel jeden schválí. Pak se dodá vektorový zdroj a všechny rastrové velikosti (včetně variant tray pro různé DPI).
- **Schválený návrh (2026-10-19):** varianta **B – Vrstvy** (ze tří návrhů ui-designera: A – Dlaždice, B – Vrstvy, C – Kroužek).
- **Barvy (2026-10-19):** oranžová **#FD5000** (aktuální primární barva prusa3d.com), tmavá **#1E1F24** tam, kde je potřeba. Bez loga Prusa.
- **Attention (2026-10-19):** zůstává jantarová z T-007 (#FB8C00) se symbolem „!“.
- **Tray podle velikosti (2026-10-19):** ikona se kreslí zvlášť pro každou cílovou velikost (16/20/24/32 px), geometrie 16 px zarovnaná na pixely; nejdřív ověřit, jak Avalonia 12 na Windows velikost vybírá.

## Rozsah
1. **Návrh (ui-designer):** 2–3 varianty do sekce `## Návrh UI` (SVG v pracovní složce mimo `src/`, náhledy PNG v 16, 24, 32, 256 px na světlém i tmavém pozadí). Pro každou variantu i návrh, jak tray ikona kombinuje motiv se stavem (např. motiv tiskárny + barevný odznak stavu v rohu, progress jako pruh / kroužek), čitelnost v 16 px, kontrast na světlém i tmavém hlavním panelu. Ověřit aktuální oranžovou Prusa podle veřejných materiálů (barva není ochranná známka, logo se nepoužije). Předložit uživateli. **Implementace začne až po schválení.**
2. **Assety (ui-designer):** vektorový zdroj (SVG) v `src/PrusaTrayDiagnostic.App/Assets/`, Windows `.ico` (16/20/24/32/48/64/256), PNG pro okna, zdroj pro macOS `.icns` (1024 px; balení řeší T-019).
3. **Zapojení (ui-dev):**
   - `ApplicationIcon` v App projektu (exe; ověřit i AOT publish).
   - `Icon` ve všech oknech (sjednotit na jeden zdroj, např. styl / resource).
   - Tray: nový vzhled stavových ikon podle schváleného návrhu. Vykreslení zůstává za běhu (geometrie motivu + stav), nebo předkreslené varianty podle návrhu; varianty pro škálování 100/125/150/200 % (16/20/24/32 px), pokud Avalonia `TrayIcon` na Windows velikost podle DPI vybírá – ověřit.
   - Toasty (T-010) a zástupce převezmou ikonu z exe – ověřit.
   - `ThirdPartyLicenses` se nemění (vlastní grafika).
4. Bez nového NuGet balíčku (pokud by byl potřeba převod SVG, jen jako ruční nástroj mimo build, nebo se zeptat).

## Akceptační kritéria
- [x] 2–3 návrhy předložené, jeden schválený uživatelem (datum v Rozhodnuto)
- [x] Vektorový zdroj a všechny rastrové velikosti v repozitáři; `.ico` obsahuje všechny požadované velikosti (test čte hlavičku ICO)
- [x] Test, že každý odkazovaný asset existuje jako `AvaloniaResource` a jde načíst (`AssetLoader`)
- [x] Test (headless), že každé okno aplikace (Settings, Camera, Diagnostics, About, Licenses a nová okna) má nastavenou ikonu aplikace
- [x] `ApplicationIcon` nastavená; ověřeno na `dotnet build` výstupu i AOT publish (ikona v exe – test čte prostředky PE, nebo ruční ověření zapsané v poznámkách)
- [x] Tray: pro každý `TrayStatus` (a vybrané hodnoty progressu) se ikona vykreslí s novým motivem; testy palety / vzhledu a snímkové testy vykreslených tray ikon (PNG reference, škálování podle návrhu); test cache beze změny
- [ ] Ručně na Windows: exe v Průzkumníku, hlavní panel, titulky oken, tray na světlém i tmavém panelu, 100 % a 150 %, toast (zapsat do poznámek)
- [x] `dotnet build` a `dotnet test` bez chyb a varování; `dotnet publish -c Release -r win-x64` (AOT) bez varování

## Návrh UI
Schválená varianta **B – Vrstvy** (ui-designer, 2026-10-19):

- **Ikona aplikace:** tmavě šedá zaoblená dlaždice #2B2C33 se světlejším okrajem #4A4C55; nahoře světle šedá tisková hlava (hotend: chladič, topný blok, tryska), pod ní tři oranžové vrstvy (#FD5000), horní vrstva je vytištěná jen zčásti. Pro 16–32 px zjednodušená varianta (`app-icon-small.svg`: hlava a dvě vrstvy), od 48 px detailní (`app-icon.svg`).
- **Tray:** bez dlaždice. Oranžová tisková hlava vlevo nahoře, pod ní tři vodorovné pruhy (vrstvy, šedé poloprůhledné). Stavy:
  - **Tisk:** pruhy se plní zleva doprava od spodní vrstvy (modře #1E88E5); bez odznaku – plnící se vrstvy jsou samy signálem.
  - **Pauza:** plnění žlutě (#FDD835) + odznak ❚❚.
  - **Hotovo:** všechny pruhy zelené (#43A047) + odznak ✓.
  - **Chyba:** všechny pruhy červené (#E53935) + odznak ×.
  - **Zaneprázdněná (Busy):** šedé pruhy + modrý odznak se třemi tečkami.
  - **Pozornost:** šedé pruhy + jantarový odznak (#FB8C00) s „!“.
  - **Offline:** šedá hlava, slabší pruhy + šedý odznak „−“.
  - **Nečinná:** šedé pruhy, bez odznaku.
- Odznak je vpravo nahoře (kotouč s tmavším obrysem, viditelný na světlém i tmavém panelu); stav nikdy nezávisí jen na barvě (každý odznak má symbol).
- Při 16 px je vše na celých pixelech, odznak je ručně kreslený 7 × 7 px.

## Otevřené otázky
(žádné – výběr návrhu a způsob zobrazení stavu rozhodnuty 2026-10-19)

## Poznámky k implementaci
- **Jak Avalonia 12.1.4 na Windows vybírá velikost** (dekompilace `Avalonia.Win32`): `TrayIconImpl` žádá ikonu o velikosti `ceil(16 × škálování monitoru s hlavním panelem)`, tj. 16/20/24/32 px při 100/125/150/200 %; titulek okna 16 × škálování, hlavní panel 24 × škálování (32 × na Windows starších než 10). `WindowIcon` z jedné bitmapy jde do shellu tak, jak je, a Windows ji škáluje. `WindowIcon` z dat `.ico` vybere nejbližší položku (při stejné vzdálenosti vyhrává první) a použije ji bez škálování. Proto se tray ikona kreslí pro každou velikost zvlášť a balí se do `.ico` (PNG položky, od největší, takže 175 % = 28 px vezme 32 px). Na macOS zůstává jedna 32px bitmapa (`ColorTrayIconSet(multiSize: false)`; menu bar řeší T-018). Od 250 % (40+ px) Windows zvětší 32px obrázek – případné 40/48px varianty lze doplnit.
- **Tray kreslení:** `Tray/Icons/TrayIconLayout` (geometrie pro 16/20/24/32 px, obdélníky na celých pixelech), `ColorTrayIconPalette` (stav → barvy, plnění, odznak), `TrayIconRenderer` (Avalonia `DrawingContext`, bez SVG knihovny, AOT bezpečné), `IconFile` (zápis `.ico` s PNG). 32 px = mřížka návrhu, 16 px = ruční varianta designera. **20 a 24 px jsem odvodil sám** (designer je jen škáloval s rozmazanými hranami): pruhy 2 px s mezerou 2 px (20 px), 3 px s mezerou 1 px (24 px), hlava na celých pixelech, odznak vektorový; obdélníkové symboly (❚❚, !, −) se při 20/24 px také zarovnají na pixely. Odznak nikdy nepřekrývá motiv, takže výřez kolem něj není potřeba (test).
- **Plnění:** počet pixelů = `floor(3 × šířka pruhu × progress)`, od spodní vrstvy; vrstvy jsou plné až při 100 % (designer zaokrouhloval, rozdíl nejvýš 1 sloupec). Krok progressu v `TrayIconKey` zůstává 5 % (asi 2 px při 16 px).
- **Porovnání s rendery designera** (pixely s rozdílem kanálu > 48): 16 px shodné (95 %: 2 px kvůli `floor`), 32 px 0–16 px (vyhlazení odznaku, `floor`), 20/24 px 8–30 % – záměrně kvůli zarovnání na pixely.
- **Assety** (`Assets/AppIcon/`): `app-icon.svg` (detail, 48–256 px), `app-icon-small.svg` (16–32 px), `app-icon.ico` (256 jako PNG, 64/48 z detailu, 32/24/20/16 ze zjednodušené varianty jako 32bit BMP, od největší), `app-icon-1024.png` (zdroj pro macOS `.icns`, T-019), `tray/*.svg` (referenční SVG návrhu tray ikon). SVG a 1024px PNG nejsou `AvaloniaResource`. Složka se nejmenuje `Icon`, protože `.gitignore` ignoruje macOS soubory `Icon\r` a s nimi i složky `Icon`.
- `.ico` a 1024px PNG jsou vygenerované jednorázově mimo repozitář (pomocný program se Svg.Skia v pracovní složce; nepatří do buildu ani do `Directory.Packages.props`). `tools/` obsahuje jen PowerShell skripty, vzor pro C# nástroj v repu není, proto ho nepřidávám. Při změně SVG je potřeba `.ico` vygenerovat znovu (velikosti a zdroje viz výše).
- **Okna:** jeden zdroj `AppIcon.Window` (načte `.ico` přes `AssetLoader`), nastavený stylem `Window` v `App.axaml`; atributy `Icon` v oknech odstraněny. Stejná ikona je statická tray ikona do prvního vykreslení stavu. `tray-idle.png` odstraněn.
- **Exe:** `ApplicationIcon` v csproj. Test čte Win32 prostředky `PrusaTrayDiagnostic.dll` (RT_GROUP_ICON se 7 velikostmi). AOT publish win-x64 bez varování; ikona v `PrusaTrayDiagnostic.exe` ověřena (`PrivateExtractIcons` vrátil 16/20/24/32/48/64/256, náhled 256 px odpovídá). Aplikace z AOT publish běžela 10 s bez pádu (Windows 11, 2560 px široký monitor).
- **Toasty (T-010):** zástupce ve Start menu nemá vlastní `IconLocation`, Windows bere ikonu z exe – kód beze změny. Existující zástupce se nepřepisuje (cesta i AUMID stejné); kdyby Windows držel starou ikonu v cache, pomůže obnova cache ikon – ověřit ručně.
- **Licence:** vlastní grafika, `ThirdPartyLicenses` beze změny; v assetech není žádné písmo ani cizí grafika (Svg.Skia jen rastrovala naše SVG).
- **Testy:** App.Tests – `ColorTrayIconPaletteTests`, `TrayIconLayoutTests`, `IconFileTests`, `ApplicationIconTests`; App.UiTests – `TrayIconRendererTests` (60 snímkových referencí `Snapshots/TrayIcons/<stav>-<velikost>.png`, ostré pixely při 16 px i motiv ve všech velikostech, plnění zdola zleva, odznaky, barva hlavy, `.ico` z rendereru, cache sady ikon) a `AppIconUiTests` (assety, velikosti v `.ico`, každé okno používá `AppIcon.Window`, statická tray ikona). `SnapshotAssert` umí porovnat i bitmapu (bez premultiplikace, limit 1 pixel). `TrayIconCacheTests` beze změny.
- **Ručně zbývá (uživatel):** exe v Průzkumníku, hlavní panel a titulky oken, tray na světlém i tmavém panelu při 100 % a 150 % (při spuštění byla ikona ve skrytých ikonách), toast s novou ikonou. macOS neověřeno (chování beze změny: jedna 32px bitmapa).
