---
id: T-070
title: E2E – ikony aplikace a tray na světlém/tmavém panelu při 100 % a 150 %
status: in-progress
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
- [x] Ikona exe přes shell odpovídá `app-icon.ico` ve 4 velikostech (test) – ověřeno 2026-10-22 během `-NoSystemChanges` (rozdíl 0,0–0,5 %)
- [ ] Ikona na tlačítku hlavního panelu a v titulcích oken odpovídá `.ico` při 100 % a 150 % (testy)
- [ ] Tray ikona: velikost a viditelnost na světlém i tmavém panelu při 100 % a 150 % pro vybrané stavy (testy); nález o překreslení po změně měřítka zapsaný
- [ ] Ilustrace v tray menu při 150 % (přítomnost; ostrost podle systémového DPI relace) (test)
- [ ] Motiv a měřítko se po běhu vrátí (T-065/T-066, kontrola na konci běhu)
- [ ] Ruční kontrola T-025 (kromě doporučených) a tray část T-039 zaškrtnuté podle výsledků
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; `tools/run-e2e.ps1` zelené – `dotnet build`, `dotnet test` a `-NoSystemChanges` zelené, běh se zásahy čeká na uživatele

## Otevřené otázky
- **Q1 – prahy viditelnosti tray ikony (návrh, potvrdit):** kontrast WCAG proti změřenému pozadí panelu, vždy nejkontrastnější neprůhledný pixel. Motiv ≥ 2:1, odznak ≥ 3:1 (WCAG 2.1 SC 1.4.11). Proč motiv jen 2:1: schválená oranžová hlava (#FD5000) má na světlém panelu (#EEEEEE–#F3F3F3) jen 2,86–2,99:1, takže prah 3:1 by odmítl schválený návrh T-025 (stav Nečinná). Spočtené hodnoty (tmavý / světlý panel): Nečinná 4,9 / 2,9, Tisk 4,4 / 3,2, Chyba motiv 4,9 / 3,6 a odznak 16,3 / 5,5. Pokud chcete 3:1 i pro motiv, jde o barvu návrhu (T-025), ne o test.
- **Q2 – nález z běhu se zásahy (doplní se po běhu):** pokud Avalonia po změně měřítka za běhu ikonu v tray nepřekreslí ve velikosti nového měřítka, test to zapíše jako nález (varování testu a „FINDING“ ve zprávě `icons-tray-150-report`), neselže a ověří jen viditelnost. Oprava jen po rozhodnutí uživatele.
- **Q3 – snímky celé plochy v dosavadních testech:** rozhodnutí 2026-10-22 („nikdy celou plochu“) porušuje dosavadní `Screenshots.SaveScreen` (diagnostika selhání v `TrayMenuTests` a v `LogAndSupportTests`). T-069 to zřejmě řeší pro tray menu (`CaptureScreenArea`). `LogAndSupportTests` je mimo rozsah T-070 – opravit v T-069, nebo samostatně?

## Poznámky k implementaci
- **Kód** (vše v `tests/PrusaTrayDiagnostic.App.E2E.Tests`, žádný nový NuGet balíček; `Microsoft.Extensions.TimeProvider.Testing` už je v `Directory.Packages.props`, licence MIT, testovací projekt se nepublikuje, AOT se ho netýká):
  - `Infrastructure/IconPixels.cs`: `Pixels` (ARGB bez premultiplikace přes `LockBits`, rychlé porovnání, výřez, uložení do výsledků) a `IconPixels`: složení na pozadí, bikubická změna velikosti, medián pozadí, hledání ikony ve výřezu. Rozdíl se počítá jen přes vlastní pixely reference s alfou ≥ 25 %, aby prázdné pozadí „nepasovalo“ na řídkou tray ikonu. Tolerance 10 % jako v T-044. Dále kontrast WCAG, adresář a položky `.ico` (i PNG položka 256 px, kterou `System.Drawing.Icon` přeskakuje), výběr nejbližší položky „při shodě první“ podle T-025 a rozpoznání premultiplikované alfy. Dále `AppIconFile`, `TraySizeCheck` (ikona v očekávané velikosti vs. roztažená ikona jiné velikosti; rozhoduje i to, která pasuje líp) a `TrayVisibility` (prahy viz Q1, oblast odznaku = pravých 7/16 horních 7/16 ikony podle `TrayIconLayout`).
  - `Infrastructure/ScreenArea.cs`: snímek obrazovky jen jednoho prvku (tlačítko na panelu, tray ikona). Oblast musí ležet na jednom monitoru a pokrýt nejvýš 25 % (rozhodnutí 2026-10-22). Okna aplikace se snímají přes `PrintWindow` (`Screenshots.Capture`). **Překryv s T-069:** T-069 přidává stejnou pojistku jako `Screenshots.CaptureScreenArea`; po sloučení jednu z nich odstranit.
  - `Infrastructure/ShellIcons.cs`: ikona exe tak, jak ji kreslí Průzkumník, včetně cache ikon: `SHGetFileInfo` (velká/malá) a `IShellItemImageFactory.GetImage(SIIGBF_ICONONLY)` 16/32/48/256 px. Bitmapa se čte přes `GetDIBits` shora dolů, premultiplikace se vrátí. Průzkumník se neotevírá.
  - `TrayMenuTests.OpenMenuAsync` je teď `internal` (používá ho test menu). **Překryv s T-069:** T-069 má vlastní `TrayMenu.OpenWithMouseAsync`; po sloučení přejít na něj.
- **Testy (`IconsThemeAndScalingTests`)**, každý ukládá výřezy a zprávu `icons-…-report.txt` do výsledků:
  - `ExplorerShowsTheAppIconForTheExe` (bez zásahu): 6 zdrojů ikony (SHGetFileInfo velká/malá, factory 16/32/48/256) proti položce `.ico` stejné velikosti (velikost bez položky = nejbližší položka zmenšená). Porovnává se celý obrázek složený na šedé.
  - `TaskbarButtonShowsTheAppIcon(100|150)` (`DisplayScale`): nejdřív měřítko primárního monitoru, pak start aplikace a okno Nastavení. Tlačítko se hledá podle automation id `Appid: <testovací AUMID>` (T-072), ne podle názvu. Výřez jen tlačítka, ikona `24 × měřítko` px. Kandidáti: nejbližší položka `.ico` beze změny (co Avalonia předá shellu) a každá položka zmenšená na slot. Test vyžaduje shodu nejlepšího kandidáta; pořadí kandidátů je ve zprávě. Ostrost se tu nevynucuje, jen identita ikony.
  - `TitleBarsShowTheAppIcon(100|150)` (`DisplayScale`): Nastavení, Diagnostika, O aplikaci, kamera (simulovaná RTSP kamera). Okno musí být na primárním monitoru s DPI měřítka. Oblast = levá část titulku (výška od rámečku po klientskou oblast), ikona `16 × měřítko` px.
  - `TrayIconIsCrispAndVisibleOnLightAndDarkTaskbar(100|150)` (`Theme`, `DisplayScale`, `TrayPromotion`): simulovaná tiskárna se zastaveným časem (`FakeTimeProvider`), tisk držený na 37,5 % → snímek `printing-35`. Aplikace startuje v původním měřítku a ikona se propaguje na panel (T-065). Pak se změní měřítko a pro světlý i tmavý panel se projdou stavy Tisk → Chyba → Nečinná. První přepnutí po změně měřítka vždy mění ikonu, takže ji aplikace musí vykreslit znovu. Velikost: v měřítku, ve kterém aplikace startovala, se vyžaduje ikona `ceil(16 × měřítko)` px. Po změně za běhu je roztažená ikona staré velikosti **nález** (Q2), cokoli jiného chyba. Viditelnost se ověřuje vždy. Navíc snímek ikony hned po změně měřítka (jen do zprávy).
  - `MenuIllustrationsAt150Percent` (`DisplayScale`): 150 %, tray menu skutečnou myší, ilustrace u položky tiskárny (core-one nebo obecná, podle toho, zda už je model rozpoznaný). Bitmapa má `16 DIP × systémové měřítko` px. Když se systémové DPI relace rovná DPI menu (přihlášení při 150 %), test vyžaduje ostrou 24px ilustraci (lepší shoda než roztažená 16px). Jinak ověří jen přítomnost a do zprávy i diagnostiky testu zapíše „ostrost jen po přihlášení při 150 %“ (doporučená ruční kontrola, T-065).
- Jednotkové testy `IconPixelsTests` (38 včetně případů teorií) běží v každém `dotnet test` a nic ze systému nečtou: složení, nalezení ikony, prázdné pozadí nepasuje, **nález jde rozlišit** (24px ikona vs. 16px roztažená na 24 px na světlém i tmavém panelu, skutečné snímky T-025), prahy viditelnosti platí pro schválený návrh na typickém světlém i tmavém panelu, oblast odznaku obsahuje celý odznak a nic z motivu (16/20/24/32 px), velikosti a dekódování všech položek `app-icon.ico`, výběr nejbližší položky, premultiplikace, pojistka snímku obrazovky.
- `docs/prerequisites.md`: řádek „primární monitor se 150 %“.
- **Ověřeno 2026-10-22 (bez zásahů):** `dotnet build` 0 varování; `dotnet test` 4855 testů, 0 selhalo (76 přeskočeno). `tools/run-e2e.ps1 -NoSystemChanges`: 221 testů, 0 selhalo, 23 přeskočeno (mezi nimi 7 nových se zásahy), „nothing pending“ před i po, „Differences from the start: none“. `ExplorerShowsTheAppIconForTheExe` prošel (SHGetFileInfo 32/16 px a factory 16/32/48/256 px, rozdíl 0,0–0,5 %).
- **Neověřeno (běh se zásahy spouští uživatel):** testy hlavního panelu, titulků, tray a menu. Rizika: (1) pozadí tlačítka na panelu (zvýraznění aktivního okna) a Mica v titulku – porovnání skládá referenci na medián výřezu; (2) okno otevřené na druhém monitoru → test titulků selže s důvodem (pokrytí 150 % by jinak nebylo správné); (3) kurzor myši nad tray ikonou mění pozadí – zapíše se do zprávy.
