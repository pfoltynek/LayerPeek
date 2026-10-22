---
id: T-070
title: E2E – ikony aplikace a tray na světlém/tmavém panelu při 100 % a 150 %
status: todo
owner: qa-reviewer
priority: P2
platform: windows
depends_on: [T-065, T-066]
---

## Cíl
Ověřit automaticky ruční kontroly T-025 (exe v Průzkumníku, hlavní panel, titulky oken, tray na světlém i tmavém panelu při 100 % a 150 %) a T-039 (ilustrace v tray menu při 150 %). Použije zásahy motiv a tray ikona na panelu (T-065) a měřítko (T-066). Toast s ikonou pokrývá T-068.

## Pokrytí
| Úkol | Kontrola | Test (návrh názvu) |
|---|---|---|
| T-025 | exe v Průzkumníku | `ExplorerShowsTheAppIconForTheExe` |
| T-025 | hlavní panel (tlačítko okna) | `TaskbarButtonShowsTheAppIcon` (100 %, 150 %) |
| T-025 | titulky oken | `TitleBarsShowTheAppIcon` (Nastavení, Diagnostika, O aplikaci, kamera; 100 %, 150 %) |
| T-025 | tray na světlém i tmavém panelu, 100 % a 150 % | `TrayIconIsCrispAndVisibleOnLightAndDarkTaskbar` (2 motivy × 2 měřítka × stavy Nečinná / Tisk / Chyba) |
| T-039 | ilustrace v tray menu při 150 % | `MenuIllustrationsAt150Percent` |
| T-025 / T-039 | ostrost při 150 % po přihlášení při 150 % | stejné testy; plně jen když systémové DPI relace = DPI monitoru (jinak **doporučená ruční kontrola**, T-065) |

## Rozsah
- **Průzkumník:** ikona exe přes shell, tedy totéž, co kreslí Průzkumník, včetně cache ikon: `SHGetFileInfo(SHGFI_ICON | SHGFI_LARGEICON / SMALLICON)` a `IShellItemImageFactory.GetImage` 16/32/48/256 px s `SIIGBF_ICONONLY`. Porovnání s položkami `app-icon.ico` (tolerance). Průzkumník se neotevírá.
- **Hlavní panel:** obdélník tlačítka okna aplikace z UIA hlavního panelu (název „Prusa Tray Diagnostic“). Ve snímku najít ikonu (`24 × škálování` px) a porovnat s nejbližší položkou `.ico` (výběr podle T-025: nejbližší, při shodě první).
- **Titulky:** výřez ikony v titulku okna (poloha z `WM_GETICON` / metrik titulku) porovnat s `16 × škálování` px položkou.
- **Tray:**
  - testovací ikona na panelu (propagace T-065), stav podle simulátoru (`idle`, `printing` s průběhem, `error`);
  - pro každý motiv (`SystemUsesLightTheme` 1 / 0) a měřítko primárního monitoru (100 %, 150 %, T-066): snímek oblasti ikony v oznamovací oblasti;
  - **velikost:** obrázek ikony ve snímku odpovídá rendereru aplikace v `ceil(16 × škálování)` px. Referenci vyrobí test: buď z PNG snímků `tests/PrusaTrayDiagnostic.App.UiTests/Snapshots/TrayIcons/<stav>-<velikost>.png` (T-025), nebo z ikony, kterou aplikace v testovacím režimu zapíše do testovací složky (pokud to bude potřeba, doplnit Debug-only výstup do T-067 – zapsat). Porovnání po složení na změřené pozadí panelu, s tolerancí;
  - **viditelnost:** kontrast motivu a odznaku proti pozadí panelu (WCAG poměr nebo rozdíl jasu nad prahem, zapsat), na světlém i tmavém panelu;
  - po změně měřítka za běhu: změna stavu ikony (přepnutí scénáře simulátoru) vynutí nové vykreslení. Ověří se, zda Avalonia požádá o novou velikost. Pokud ikonu nepřekreslí ve velikosti nového měřítka, je to nález. Zapsat ho a zeptat se uživatele (neopravovat bez rozhodnutí), test pak ověří jen viditelnost.
- **Tray menu při 150 %:** stejná pixelová kontrola jako T-069. Velikost bitmapy se řídí systémovým DPI (`SystemDpiNative`). Když systémové DPI relace ≠ DPI monitoru (měřítko změněné za běhu), test ověří přítomnost ilustrace a zapíše, že ostrost nejde ověřit bez odhlášení. Když je relace přihlášená při 150 %, ověří ostrou ilustraci 24 px.
- Všechny testy s motivem nebo měřítkem mají trait `SystemChanges`. Návrat zajistí T-065/T-066.
- Snímky všech kombinací do složky výsledků.

## Akceptační kritéria
- [ ] Ikona exe přes shell odpovídá `app-icon.ico` ve 4 velikostech (test)
- [ ] Ikona na tlačítku hlavního panelu a v titulcích oken odpovídá `.ico` při 100 % a 150 % (testy)
- [ ] Tray ikona: velikost a viditelnost na světlém i tmavém panelu při 100 % a 150 % pro vybrané stavy (testy); nález o překreslení po změně měřítka zapsaný
- [ ] Ilustrace v tray menu při 150 % (přítomnost; ostrost podle systémového DPI relace) (test)
- [ ] Motiv a měřítko se po běhu vrátí (T-065/T-066, kontrola na konci běhu)
- [ ] Ruční kontrola T-025 (kromě doporučených) a tray část T-039 zaškrtnuté podle výsledků
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; `tools/run-e2e.ps1` zelené

## Otevřené otázky
- (žádné – nálezy během implementace, např. nepřekreslená ikona po změně měřítka, se zapíšou a předají uživateli)

## Poznámky k implementaci
(vyplní agent po dokončení)
