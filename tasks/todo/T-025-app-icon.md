---
id: T-025
title: Ikona aplikace (exe, okna, tray)
status: todo
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
- [ ] 2–3 návrhy předložené, jeden schválený uživatelem (datum v Rozhodnuto)
- [ ] Vektorový zdroj a všechny rastrové velikosti v repozitáři; `.ico` obsahuje všechny požadované velikosti (test čte hlavičku ICO)
- [ ] Test, že každý odkazovaný asset existuje jako `AvaloniaResource` a jde načíst (`AssetLoader`)
- [ ] Test (headless), že každé okno aplikace (Settings, Camera, Diagnostics, About, Licenses a nová okna) má nastavenou ikonu aplikace
- [ ] `ApplicationIcon` nastavená; ověřeno na `dotnet build` výstupu i AOT publish (ikona v exe – test čte prostředky PE, nebo ruční ověření zapsané v poznámkách)
- [ ] Tray: pro každý `TrayStatus` (a vybrané hodnoty progressu) se ikona vykreslí s novým motivem; testy palety / vzhledu a snímkové testy vykreslených tray ikon (PNG reference, škálování podle návrhu); test cache beze změny
- [ ] Ručně na Windows: exe v Průzkumníku, hlavní panel, titulky oken, tray na světlém i tmavém panelu, 100 % a 150 %, toast (zapsat do poznámek)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; `dotnet publish -c Release -r win-x64` (AOT) bez varování

## Návrh UI
(vyplní `ui-designer`, schválí uživatel)

## Otevřené otázky
- Výběr z návrhů ui-designera (předloží uživateli).
- Jak přesně tray kombinuje motiv a stav – součást návrhů.

## Poznámky k implementaci
(vyplní agent po dokončení)
