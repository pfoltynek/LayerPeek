---
id: T-033
title: Okno nastavení bez posuvníku a podzáložky tiskárny (Tiskárna / Kamera)
status: todo
owner: ui-designer + ui-dev
priority: P2
platform: all
depends_on: [T-014]
---

## Cíl
Okno nastavení se při prvním zobrazení vejde celé bez svislého posuvníku, a to i na obrazovce 800×600. Formulář tiskárny se rozdělí na dvě podzáložky: **Tiskárna** (připojení) a **Kamera** (nastavení kamery této tiskárny). Na podzáložce Kamera pak přibudou další volby z navazujících úkolů (T-034 až T-037).

## Současný stav (2026-10-19)
- `SettingsWindow.axaml`: `Width=780`, `Height=680`, `MinWidth=640`, `MinHeight=480`, `WindowStartupLocation=CenterScreen`, okraj 16 px. Dole pruh Uložit/Zrušit + stav (≈ 44 px), nahoře volitelný pruh upozornění (`HasNotice`) a text „Načítám…“.
- `TabControl` se dvěma záložkami (`SelectedTabIndex`: 0 = Tiskárny, 1 = Obecné). `SettingsViewModel.SaveAsync` při chybě přepne na záložku s chybou.
- Záložka **Tiskárny**: vlevo seznam (220 px) s tlačítky Přidat/Odebrat, vpravo `ScrollViewer` s jedním `StackPanel`: Název, Připojení, Adresa (+ nápověda), Uživatelské jméno (+ nápověda), Heslo (+ nápověda), Otestovat spojení + výsledek, sekce **Kamera** (hláška o odstraněných údajích, zapnutí, RTSP adresa + nápověda + chyba, Otestovat + výsledek).
- Proč je potřeba posuvník: obsah pravého panelu je zhruba 650–700 px vysoký (5 polí po ≈ 83 px, 2× tlačítko testu, sekce Kamera ≈ 200 px), viditelná výška je ≈ 550 px (680 − okraje − pruh tlačítek − záhlaví záložek). S chybovými hláškami nebo v češtině (dvouřádkové zaškrtávací pole kamery) je obsah ještě vyšší. Viz referenční snímky `tests/PrusaTrayDiagnostic.App.UiTests/Snapshots/settings-*.png` – posuvník je vidět ve všech.
- Záložka **Obecné** (interval, jazyk, 8 zaškrtávacích polí notifikací, zbývající čas, zvuk) má odhadem ≈ 580 px, takže se pravděpodobně také posouvá (ověřit měřením).
- Testy: `SettingsWindowSnapshotTests` (4 PNG, okno 780×680, škálování 1.0, en-US), `SettingsWindowTests`, `SettingsWindowCameraUiTests`, `SettingsWindowLanguageTests`; view modely v `tests/PrusaTrayDiagnostic.App.Tests/Settings`.

## Rozhodnuto
- **Cílová obrazovka (2026-10-19):** nejmenší podporovaná obrazovka je **800×600 při 100 %**. Pracovní plocha je ≈ 800×560 DIP, takže okno včetně titulku se musí vejít do ≈ **780×540 DIP** (klientská oblast ≈ 780×508 při titulku 32 DIP). Návrh se dělá pro tuto velikost.
- **Forma podzáložek (2026-10-19):** vnořené podzáložky **Tiskárna | Kamera** v pravém panelu záložky Tiskárny.
- **Co se musí vejít (2026-10-19):** bez posuvníku včetně **všech chybových hlášek a výsledků testů, ve všech 8 jazycích**. Posuvník smí vyvolat jen pruh upozornění nahoře (`HasNotice`).
- **Jak získat místo (2026-10-19):** podzáložky + menší okno, kde je potřeba, dva sloupce. Nápovědy zůstávají viditelné. Podrobné rozložení navrhne agent `ui-designer` (`.claude/agents/ui-designer.md`) do sekce `## Návrh UI` a **před implementací ho schválí uživatel**.
- **Přepnutí tiskárny (2026-10-19):** vybraná podzáložka zůstává stejná (Kamera → Kamera). Záhlaví podzáložky s chybným polem ukazuje značku `!`.
- **Záložka Obecné (2026-10-19):** musí se také vejít bez posuvníku (např. dva sloupce).
- **Velikost okna (2026-10-19):** pevná výchozí velikost, omezená pracovní plochou. `ScrollViewer` zůstává jako záchrana. Uživatel může okno zvětšit.
- **Závislost na T-014 (2026-10-19):** nové úkoly smí stavět na T-014, i když je zatím v `in-progress` (kód je sloučený v `develop`).

## Rozsah
1. **Návrh (ui-designer):** do sekce `## Návrh UI` tohoto souboru drátěné modely (ASCII) záložky Tiskárny (obě podzáložky) a záložky Obecné ve stavech: výchozí, všechny chyby, výsledky testů, nejdelší jazyk; velikosti a rozestupy; místo pro sekce z T-034 (náhled), T-035 + T-037 (jedna sekce „Okno kamery“) a T-036 (Překryv) – podzáložka Kamera s nimi musí zůstat bez posuvníku, případně návrh, jak je rozvrhnout. Návrh předložit uživateli ke schválení. **Implementace začne až po schválení.**
2. **Implementace (ui-dev, případně ui-designer pro XAML):**
   - Vnořený `TabControl` v pravém panelu (Tiskárna | Kamera); obsah podle schváleného návrhu.
   - Výchozí velikost okna podle návrhu (vejde se do 780×540 DIP včetně titulku), omezená pracovní plochou aktuální obrazovky; `MinWidth`/`MinHeight` podle návrhu; `ScrollViewer` jako záchrana.
   - Výběr podzáložky ve view modelu (např. `SettingsViewModel.SelectedPrinterTabIndex`), zachovaný při přepnutí tiskárny. Uložení s chybou přepne na záložku **i podzáložku** s první chybou (např. chybná adresa kamery → Kamera).
   - Značka `!` na záhlaví podzáložky s chybným polem (např. `PrinterEditorViewModel.HasConnectionErrors` / `HasCameraErrors`).
   - Záložka Obecné bez posuvníku podle návrhu.
   - `AutomationProperties.AutomationId` pro nové prvky, překlady nových textů do všech 8 jazyků (konvence T-029).
   - Bez nového NuGet balíčku.

## Akceptační kritéria
- [ ] Návrh UI v sekci `## Návrh UI` schválený uživatelem (datum schválení zapsané v Rozhodnuto)
- [ ] Podzáložky Tiskárna / Kamera u vybrané tiskárny; existující pole a testy spojení/kamery fungují beze změny chování
- [ ] Uložení s chybou přepne na záložku **a podzáložku** s první chybou; testy view modelu (App.Tests) pro chybu v poli tiskárny, v adrese kamery a na záložce Obecné
- [ ] Při přepnutí tiskárny zůstává vybraná podzáložka (test view modelu); značka `!` na záhlaví podzáložky s chybou (test view modelu + UI test)
- [ ] Headless UI test „bez posuvníku“: okno ve výchozí velikosti; u každé záložky/podzáložky platí pro každý viditelný `ScrollViewer` `Extent.Height <= Viewport.Height`. Stavy: tiskárna PrusaLink s uloženým heslem, Prusa Connect, všechny chyby validace, výsledek testu spojení (úspěch i dlouhá chyba), výsledek testu kamery, hláška o odstraněných údajích kamery; záložka Obecné včetně chyb. Vše ve všech 8 jazycích
- [ ] Test, že výchozí velikost okna + titulek (konstanta 32 DIP v testu) ≤ 780×540 DIP a že se velikost omezí na menší pracovní plochu (logika omezení testovaná bez skutečné obrazovky)
- [ ] Snímkové reference aktualizované (`PTD_UPDATE_SNAPSHOTS=1`, vizuálně zkontrolované) a nové snímky: podzáložka Kamera, záložka Obecné
- [ ] Testy přepnutí jazyka za běhu pro nové texty (záhlaví podzáložek), `XamlLocalizationTests` projde
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; `dotnet publish -c Release -r win-x64` (AOT) bez varování

## Návrh UI
(vyplní `ui-designer`, schválí uživatel)

## Otevřené otázky
- Detaily rozložení vzejdou z návrhu UI (ui-designer je zapíše sem a předloží uživateli).

## Poznámky k implementaci
(vyplní agent po dokončení)
