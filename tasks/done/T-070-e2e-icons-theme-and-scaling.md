---
id: T-070
title: E2E – ikony aplikace a tray na světlém/tmavém panelu při 100 % a 150 %
status: done
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
- [x] Ikona na tlačítku hlavního panelu a v titulcích oken odpovídá `.ico` při 100 % a 150 % (testy) – titulky 0,0 % (16 px při 100 %, 24 px při 150 %); hlavní panel 100 % 3,5 %, 150 % 2,3 % ve 24px slotu (Průzkumník po změně za běhu kreslí ikony v DPI svého startu, viz poznámky)
- [x] Tray ikona: velikost a viditelnost na světlém i tmavém panelu při 100 % a 150 % pro vybrané stavy (testy); nález o překreslení po změně měřítka zapsaný – **nález není** (Q2)
- [x] Ilustrace v tray menu při 150 % (přítomnost; ostrost podle systémového DPI aplikace) (test) – ostrá 24px ilustrace ověřena (0,0 % proti 12,1 % u roztažené 16px)
- [x] Motiv a měřítko se po běhu vrátí (T-065/T-066, kontrola na konci běhu) – „nothing pending“ a „Differences from the start: none“ po každém běhu
- [x] Ruční kontrola T-025 (kromě doporučených) a tray část T-039 zaškrtnuté podle výsledků – zapsáno do T-025 a T-039; jejich kritéria zůstávají otevřená jen kvůli toastu (T-068) a ilustraci při 100 % (T-069)
- [x] `dotnet build` a `dotnet test` bez chyb a varování; `tools/run-e2e.ps1` zelené

## Rozhodnuto
- **Q1 (uživatel, 2026-10-23):** prahy viditelnosti tray ikony – motiv ≥ 2:1, odznak ≥ 3:1 (WCAG 2.1 SC 1.4.11); schválený návrh T-025 zůstává.
- **Q2 (uživatel, 2026-10-23):** rozhodnout až po skutečném běhu; skutečný nález zapsat s důkazy, aplikaci tady neopravovat. **Výsledek: nález není** – aplikace po změně 100 % → 150 % za běhu ikonu sama znovu vykreslí ve 24 px, ještě před změnou stavu (viz poznámky).
- **Q3 (uživatel, 2026-10-23):** snímky celé plochy odstraňuje T-069 (zatím nesloučený); T-070 si zatím ponechá vlastní pojistku `ScreenArea`, sjednotí se při sloučení po T-069.
- **Q4 (2026-10-23, technické rozhodnutí):** hlavní panel a titulky ověřují jen „je to ikona aplikace“ (nejlepší kandidát ze všech položek `.ico`); skóre všech kandidátů je ve zprávě. Přísnější kontrola konkrétní položky se nevyžaduje.

## Otevřené otázky
- žádné

## Poznámky k implementaci
- **Kód** (vše v `tests/PrusaTrayDiagnostic.App.E2E.Tests`, žádný nový NuGet balíček; `Microsoft.Extensions.TimeProvider.Testing` už je v `Directory.Packages.props`, licence MIT, testovací projekt se nepublikuje, AOT se ho netýká):
  - `Infrastructure/IconPixels.cs`: `Pixels` (ARGB bez premultiplikace přes `LockBits`, rychlé porovnání, výřez, uložení do výsledků) a `IconPixels`: složení na pozadí, bikubická změna velikosti, medián pozadí, hledání ikony ve výřezu. Rozdíl se počítá jen přes vlastní pixely reference s alfou ≥ 25 %, aby prázdné pozadí „nepasovalo“ na řídkou tray ikonu. Tolerance 10 % jako v T-044. Dále kontrast WCAG, adresář a položky `.ico` (i PNG položka 256 px, kterou `System.Drawing.Icon` přeskakuje), výběr nejbližší položky „při shodě první“ podle T-025 a rozpoznání premultiplikované alfy. Dále `AppIconFile`, `TraySizeCheck` (ikona v očekávané velikosti vs. roztažená ikona jiné velikosti; rozhoduje i to, která pasuje líp) a `TrayVisibility` (prahy viz Q1, oblast odznaku = pravých 7/16 horních 7/16 ikony podle `TrayIconLayout`).
  - `Infrastructure/ScreenArea.cs`: snímek obrazovky jen jednoho prvku (tlačítko na panelu, tray ikona). Oblast musí ležet na jednom monitoru a pokrýt nejvýš 25 % (rozhodnutí 2026-10-22). Okna aplikace se snímají přes `PrintWindow` (`Screenshots.Capture`). **Překryv s T-069:** T-069 přidává stejnou pojistku jako `Screenshots.CaptureScreenArea`; po sloučení jednu z nich odstranit.
  - `Infrastructure/ShellIcons.cs`: ikona exe tak, jak ji kreslí Průzkumník, včetně cache ikon: `SHGetFileInfo` (velká/malá) a `IShellItemImageFactory.GetImage(SIIGBF_ICONONLY)` 16/32/48/256 px. Bitmapa se čte přes `GetDIBits` shora dolů, premultiplikace se vrátí. Průzkumník se neotevírá.
  - `TrayMenuTests.OpenMenuAsync` je teď `internal` (používá ho test menu). **Překryv s T-069:** T-069 má vlastní `TrayMenu.OpenWithMouseAsync`; po sloučení přejít na něj.
- **Testy (`IconsThemeAndScalingTests`)**, každý ukládá výřezy a zprávu `icons-…-report.txt` do výsledků:
  - `ExplorerShowsTheAppIconForTheExe` (bez zásahu): 6 zdrojů ikony (SHGetFileInfo velká/malá, factory 16/32/48/256) proti položce `.ico` stejné velikosti (velikost bez položky = nejbližší položka zmenšená). Porovnává se celý obrázek složený na šedé.
  - `TaskbarButtonShowsTheAppIcon(100|150)` (`DisplayScale`): nejdřív měřítko primárního monitoru, pak start aplikace a okno Nastavení. Tlačítko se hledá podle automation id `Appid: <testovací AUMID>` (T-072), ne podle názvu. Výřez jen tlačítka, ikona `24 × měřítko` px. Kandidáti: nejbližší položka `.ico` beze změny (co Avalonia předá shellu) a každá položka zmenšená na slot. Test vyžaduje shodu nejlepšího kandidáta; pořadí kandidátů je ve zprávě. Ostrost se tu nevynucuje, jen identita ikony (Q4). Výřez se opakuje, dokud ikona nesedí (nejvýš 10 s): UI Automation hlásí konečnou polohu nového tlačítka, zatímco ho panel ještě animuje, takže první snímek ukázal sousední tlačítko (cizí aplikace). Snímá se jen tehdy, když je v bodě tlačítka opravdu tlačítko testovací aplikace (`TaskbarJumpList.IsTestButtonAt`). Sloty: `24 × měřítko` a `24 × systémové DPI Průzkumníku` – Průzkumník spuštěný při 100 % kreslí po změně na 150 % za běhu ikony dál ve 24 px (`GetSystemDpiForProcess`), 36 px až po přihlášení při 150 % (zpráva a diagnostika testu to zapíšou, doporučená ruční kontrola).
  - `TitleBarsShowTheAppIcon(100|150)` (`DisplayScale`): Nastavení, Diagnostika, O aplikaci, kamera (simulovaná RTSP kamera). Okno musí být na primárním monitoru s DPI měřítka. Oblast = levá část titulku (výška od rámečku po klientskou oblast), ikona `16 × měřítko` px.
  - `TrayIconIsCrispAndVisibleOnLightAndDarkTaskbar(100|150)` (`Theme`, `DisplayScale`, `TrayPromotion`): simulovaná tiskárna se zastaveným časem (`FakeTimeProvider`), tisk držený na 37,5 % → snímek `printing-35`. Aplikace startuje v původním měřítku a ikona se propaguje na panel (T-065). Pak se změní měřítko a pro světlý i tmavý panel se projdou stavy Tisk → Chyba → Nečinná. První přepnutí po změně měřítka vždy mění ikonu, takže ji aplikace musí vykreslit znovu. Velikost: v měřítku, ve kterém aplikace startovala, se vyžaduje ikona `ceil(16 × měřítko)` px. Po změně za běhu je roztažená ikona staré velikosti **nález** (Q2), cokoli jiného chyba. Viditelnost se ověřuje vždy. Navíc ikona Nečinná hned po změně měřítka, ještě před změnou stavu (jen do zprávy, Q2); snímá se až ve chvíli, kdy ji panel ukazuje (panel ikony po změně přesouvá a jeden snímek po pevné 1 s zachytil i ikonu cizí aplikace).
  - `MenuIllustrationsAt150Percent` (`DisplayScale`): 150 %, tray menu skutečnou myší, ilustrace u položky tiskárny (core-one nebo obecná, podle toho, zda už je model rozpoznaný). Bitmapa má `16 DIP × systémové měřítko` px. **Systémové DPI se čte z procesu aplikace** (`GetSystemDpiForProcess`), ne z testovacího procesu: proces spuštěný po změně měřítka za běhu má systémové DPI nového měřítka (144), testovací proces a Průzkumník dál DPI svého startu (96). Původní verze to brala z testovacího procesu a selhala (porovnávala roztaženou 16px ilustraci s ostrou 24px). Když se systémové DPI aplikace rovná DPI menu (aplikace spuštěná při 150 %, stejně jako po přihlášení při 150 %), test vyžaduje ostrou 24px ilustraci (lepší shoda než roztažená 16px). Jinak ověří jen přítomnost a zapíše „ostrost jen u aplikace spuštěné při 150 %“. Neověřený zůstává jen případ, kdy aplikace běží už před změnou měřítka (bitmapa 16 px roztažená) – to je chování T-039, ne chyba.
- Jednotkové testy `IconPixelsTests` (43 včetně případů teorií) běží v každém `dotnet test` a nic ze systému nečtou: složení, nalezení ikony, prázdné pozadí nepasuje, **nález jde rozlišit** (24px ikona vs. 16px roztažená na 24 px na světlém i tmavém panelu, skutečné snímky T-025), prahy viditelnosti platí pro schválený návrh na typickém světlém i tmavém panelu, oblast odznaku obsahuje celý odznak a nic z motivu (16/20/24/32 px), velikosti a dekódování všech položek `app-icon.ico`, výběr nejbližší položky, premultiplikace, pojistka snímku obrazovky.
- `docs/prerequisites.md`: řádek „primární monitor se 150 %“.
- **Ověřeno 2026-10-22 (bez zásahů):** `dotnet build` 0 varování; `dotnet test` 4855 testů, 0 selhalo (76 přeskočeno). `tools/run-e2e.ps1 -NoSystemChanges`: 221 testů, 0 selhalo, 23 přeskočeno (mezi nimi 7 nových se zásahy), „nothing pending“ před i po, „Differences from the start: none“. `ExplorerShowsTheAppIconForTheExe` prošel (SHGetFileInfo 32/16 px a factory 16/32/48/256 px, rozdíl 0,0–0,5 %).
- **Review a opravy (qa-reviewer, 2026-10-23):**
  - Hlavní panel: první skutečný běh selhal při 100 % i 150 % – výřez podle UI Automation ukázal sousední tlačítko (ikonu cizí aplikace), protože panel nové tlačítko ještě animoval. Oprava: opakovaný výřez s kontrolou, že v bodě tlačítka je tlačítko testovací aplikace (jinak se nesnímá), a slot podle systémového DPI Průzkumníku (viz výše).
  - Tray menu při 150 %: systémové DPI se bralo z testovacího procesu (96) místo z aplikace (144) → test čekal roztaženou 16px bitmapu, menu ale ukazuje ostrou 24px. Nový `DisplayConfig.SystemDpiOf(processId)` (`GetSystemDpiForProcess`).
  - Snímek „hned po změně měřítka“ po pevné 1 s zachytil i ikonu cizí aplikace (animace panelu) → nyní se čeká na ikonu aplikace.
  - Výkon: porovnání skládalo referenci na pozadí znovu na každé zkoušené pozici (přes `Color`); nyní se složí jednou (`ComposedIcon`) a porovnává se nad poli `int`. `DifferenceAt` odmítne pozici mimo snímek (dřív by tiše četla vedlejší řádek). Nové jednotkové testy: `DifferenceCountsOnlyTheIconsOwnPixelsComposedOverTheBackground`, `DifferenceAtAPlaceBeyondTheCaptureIsRefused` (4 případy).
  - Spouštění: běh přes `run-e2e.ps1` z procesu na pozadí bez konzole dostal uprostřed Ctrl+C („Cancelling…“, kód 130; systém se přesto vrátil, „nothing pending“). Běhy se zásahy proto spouštět ve viditelné konzoli (jako dosud uživatel).
- **Ověřeno 2026-10-23 (se zásahy, `pwsh tools/run-e2e.ps1 -Class '*IconsThemeAndScalingTests'`, 2 × 2560×1440, Windows 26300):** dva poslední běhy po sobě zelené (8/8, 0 selhalo), po každém „nothing pending“ a „Differences from the start: none“.
  - Průzkumník: SHGetFileInfo 32/16 px a factory 16/32/48/256 px, rozdíl 0,0–0,5 %.
  - Titulky (Nastavení, Diagnostika, O aplikaci, kamera): 100 % – 16px položka 0,0 %; 150 % – 24px položka 0,0 % (okna 144 DPI).
  - Hlavní panel: 100 % – 24px položka 2,5–3,5 %; 150 % – ikona ve 24px slotu 2,3–4,6 % (36px slot 13 %: Průzkumník spuštěný při 100 % kreslí ikony tlačítek dál ve 24 px – chování Windows, 36 px až po přihlášení při 150 %, doporučená ruční kontrola).
  - Tray (světlý panel #D4DCE8, tmavý #1C232E): velikost ve všech 12 kombinacích 0,0–0,2 %; kontrast motivu světlý 2,38–3,07:1, tmavý 4,29–4,82:1, odznak Chyba 4,60–4,77:1 / 15,8–16,0:1 (prahy Q1 splněny).
  - **Q2 – nález není:** po změně 100 % → 150 % za běhu ukazuje panel ikonu Nečinná ve 24 px ještě před změnou stavu (0,9 % proti 7,6 % u roztažené 16px; v dalším běhu během animace 9,2 % proti 15,4 %) a všechny stavy potom 0,0–0,2 % (roztažená 16px 6,6–12,3 %). Aplikace tedy na změnu DPI reaguje sama.
  - Tray menu při 150 %: ilustrace core-one 24 px 0,0 % (roztažená 16px 12,1 %), systémové DPI aplikace 144.
  - `dotnet build` 0 varování; `dotnet test` 4902 testů, 0 selhalo (76 přeskočeno).
- **Doporučené ruční kontroly (zůstávají):** ostrost ikon hlavního panelu a bitmapy tray menu při 150 % v relaci přihlášené při 150 % (bitmapa menu aplikace, která běžela už před změnou měřítka, je 16 px roztažená – chování T-039); cache ikon Průzkumníku po aktualizaci.
