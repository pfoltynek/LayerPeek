---
id: T-040
title: Úpravy okna nastavení a okna kamery po ručním testu
status: in-progress
owner: ui-dev
priority: P2
platform: all
depends_on: [T-033, T-035, T-037]
---

## Cíl
Úpravy podle prvního ručního testu uživatele (2026-10-20): okno nastavení bez minimalizace a maximalizace, tlačítka Tovární nastavení a Použít v podzáložce Okno kamery, malý náhled kamery se nezobrazuje na hlavním panelu, položka Kamera v nabídce funguje jako přepínač a malý náhled má tlačítko Celá obrazovka.

## Rozsah
1. **Okno nastavení:** v titulku zůstane jen tlačítko zavřít (bez minimalizace a maximalizace).
2. **Podzáložka Okno kamery:**
   - Tlačítko **Tovární nastavení**: vrátí hodnoty této podzáložky na výchozí (`CameraWindowDefaults.Default` – Jako minule, pamatovat poslední pozici, primární monitor, normální okno 960×540 uprostřed, malý náhled 320×180 vpravo dole, vždy navrchu jen malý náhled) a zapomene zapamatovanou pozici a velikost okna kamery dané tiskárny. Projeví se až po Uložit nebo Použít.
   - Tlačítko **Použít**: uloží nastavení (stejně jako Uložit, okno nastavení zůstane otevřené) a hned přenastaví otevřené okno kamery dané tiskárny podle nastavení, zavřené otevře (2026-10-21) – režim, monitor, velikost, pozice, vždy navrchu.
3. **Hlavní panel:** malý náhled kamery se nezobrazuje na hlavním panelu Windows (`ShowInTaskbar = false`). Normální okno a celá obrazovka na panelu zůstávají.
4. **Položka Kamera v nabídce jako přepínač:** zaškrtávací položka „Kamera“ (bez „…“, už neotevírá dialog), zaškrtnutá, dokud je okno kamery dané tiskárny otevřené. První klik okno otevře (jako dosud), druhý ho zavře (stream se zastaví, pozice a stav se uloží jako při běžném zavření). Zaškrtnutí sleduje okno i při zavření jinak (tlačítko zavřít, vypnutí kamery v nastavení, odebrání tiskárny, ukončení). Beze změny: kamera se nastavuje u tiskárny, tiskárna bez zapnuté kamery položku nemá, u více tiskáren je položka v podnabídce každé tiskárny.
5. **Malý náhled – tlačítko Celá obrazovka:** vedle tlačítka „zpět na normální okno“ vpravo nahoře je tlačítko Celá obrazovka se stejným vzhledem a chováním (zobrazení při najetí myší / fokusu) jako v normálním okně. Přepne na celou obrazovku; odchod z ní (Esc, F11, dvojklik, systém) vrátí malý náhled na původní místo a velikost (logika z T-037). Obě tlačítka se vejdou do nejmenšího náhledu 160×90 a nepřekrývají se; okraje překryvu počítají se dvěma tlačítky.

## Rozhodnuto
- **Použít (2026-10-20):** uloží a hned přenastaví otevřené okno kamery; okno nastavení zůstane otevřené. Tím se pro tlačítko Použít mění dřívější pravidlo T-035 Q21 „projeví se až při dalším otevření“ (Uložit se dál chová podle T-035).
- **Použít se zavřeným oknem kamery (2026-10-21, hlášení uživatele):** Použít okno kamery dané tiskárny otevře, i když bylo zavřené – v režimu, na monitoru, ve velikosti a v rohu podle uložených hodnot (zapamatovanou pozici ignoruje stejně jako u otevřeného okna). Okno se zobrazí bez převzetí fokusu – fokus zůstane v okně nastavení; výjimka: celá obrazovka (a obnovení minimalizovaného okna) fokus převezme, aby Esc/F11 šly do okna kamery, které nastavení překrývá. Okno otevřené bez fokusu se pro druhé spuštění aplikace (T-043) počítá jako naposledy použité až po aktivaci (vše rozhodnutí uživatele 2026-10-21). Uložit okno dál neotevírá ani nepřesouvá.
- **Tovární nastavení (2026-10-20):** jen podzáložka Okno kamery (Překryv ne), včetně zapomenutí zapamatované pozice; projeví se po Uložit/Použít.
- **Hlavní panel (2026-10-20):** skrýt jen malý náhled.
- **Kamera v nabídce (2026-10-20):** přepínač – zaškrtávací položka „Kamera“ bez „…“; druhý klik okno zavře. Uživatel chtěl „přepínač“, nativní nabídky (Windows i macOS) ale umí jen zaškrtnutí, proto zaškrtávací položka.
- **Celá obrazovka v malém náhledu (2026-10-20):** tlačítko vedle „zpět na normální okno“, návrat do malého náhledu na stejné místo.
- **Nápověda „Pamatovat…“ (2026-10-20, dříve Q1):** přeformulovat – Uložit změny použije při dalším otevření okna, Použít hned (všech 8 jazyků; raději zkrátit, než dopustit posuvník).
- **Potvrzení po Použít (2026-10-20, dříve Q2):** po úspěšném Použít krátce „Uloženo“ (en „Saved“) ve stavovém řádku nastavení, na několik sekund (řízeno `TimeProvider`), další úprava ho skryje.
- **Překlady T-040 (2026-10-20):** schválené.

## Akceptační kritéria
- [x] Okno nastavení nemá tlačítka minimalizace a maximalizace (Windows; macOS ověří T-038/T-018)
- [x] Tovární nastavení vrátí hodnoty podzáložky Okno kamery a po uložení zapomene zapamatovanou pozici (testy ViewModelu + headless)
- [x] Použít uloží a přenastaví otevřené okno kamery, zavřené otevře (bez převzetí fokusu) (režim, monitor, velikost, pozice, vždy navrchu) bez zavření nastavení (testy)
- [x] Malý náhled má `ShowInTaskbar = false`, normální okno a celá obrazovka `true`, i při přepínání režimů (headless testy)
- [x] Položka Kamera je zaškrtávací, otevírá i zavírá okno a zaškrtnutí sleduje okno i při zavření jinak (testy ViewModelu nabídky, presenteru/cíle a headless)
- [x] Malý náhled má tlačítko Celá obrazovka, obě tlačítka se vejdou do 160×90 bez překrytí, návrat na stejné místo (headless testy, snímek)
- [x] Nové texty ve všech 8 jazycích, okno se stále vejde bez scrollbaru (layout test všech jazyků), snapshoty aktualizované
- [ ] Ruční ověření na Windows (uživatel)
- [x] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish win-x64 bez varování

## Otevřené otázky
- (žádné – Q1 a Q2 rozhodnuty 2026-10-20, viz Rozhodnuto)

## Poznámky k implementaci
Stav 2026-10-20 (ui-dev): implementace hotová, všechna kritéria kromě ruční kontroly splněná; úkol zůstává `in-progress` kvůli ručnímu ověření. Doplněno po review 2026-10-20 (viz „Úpravy po review“).

**1. Okno nastavení**
- `SettingsWindow.axaml`: `CanMinimize="False"`, `CanMaximize="False"`. Velikost okna jde dál měnit (`CanResize` beze změny) – okno má `MinWidth/MinHeight` a při malé pracovní ploše se zmenšuje (T-033), takže tažení okraje dává smysl i bez maximalizace. Na Windows Avalonia odebere `WS_MINIMIZEBOX`/`WS_MAXIMIZEBOX`, v titulku zůstane jen zavřít. macOS neověřeno (T-018).

**2. Tovární nastavení a Použít**
- Tlačítka jsou pod tabulkou podzáložky Okno kamery, zarovnaná vpravo („Tovární nastavení“, pak „Použít“ s `MinWidth` 96 jako Uložit/Zrušit), `AutomationId` `PrinterCameraWindowReset`, `PrinterCameraWindowApply`; jméno pro čtečky = text tlačítka. Pořadí Tab: … malý náhled – vždy navrchu → Tovární nastavení → Použít → Uložit → Zrušit.
- **Zakázaná při vypnuté kameře** – stejně jako ostatní pole podzáložky (jsou v panelu `IsEnabled=CameraEnabled`, příkazy mají `CanExecute` = kamera zapnutá). Důvod: vypnutá kamera nemá okno, které by šlo přenastavit, a podzáložka je celá zašedlá; Tovární nastavení lze použít po zapnutí kamery.
- Aby se tlačítka vešla bez posuvníku, tabulka velikostí ztratila vlastní horní okraj 12 DIP (nadpisy sloupců mají třídu `section` s horním okrajem 16, takže mezera nad tabulkou je teď 16 jako u jiných sekcí), řádek tlačítek má okraj 8. Naměřeno (Inter, headless, plocha 358 DIP): nejvýš 352 DIP, rezerva 6 DIP.
- `PrinterEditorViewModel`: `ResetCameraWindowCommand` (hodnoty z `CameraWindowDefaults.Default`, chyby velikostí zmizí, Překryv ani kamera se nemění; nastaví příznak „zapomenout“), `ApplyCameraWindowCommand` → `SettingsViewModel.ApplyAsync(printer)`.
- `SettingsViewModel`: Uložit a Použít sdílí jeden postup; Použít navíc nezavře okno a po úspěchu zavolá `ICameraWindowPlacer.ApplySettings(uložená tiskárna)` (do 2026-10-21 `(id, uložené výchozí hodnoty)`) – **jen pro tiskárnu, jejíž podzáložka tlačítko má**, a jen u zapnuté kamery. Použít ukládá celé nastavení (i změny jiných tiskáren a záložky Obecné), stejně jako Uložit. Použít bez změn nic neukládá, ale okno kamery přenastaví. Chyby / neúspěšné uložení: nic se nepřenastaví. Varování po uložení (nesmazaná hesla) zůstane zobrazené a okno se přenastaví.
- Zapomenutí po Továrním nastavení: příznak u tiskárny; při nejbližším úspěšném Uložit/Použít se zavolá `ICameraWindowStateStore.Remove` **i když se hodnoty neliší od uložených** (pak se nic nezapisuje, jen se zapomene stav). Nastavení je mezitím „změněné“ (`IsDirty`). Neúspěšné uložení příznak ponechá. Pořadí: nejdřív zapomenutí, pak přenastavení okna.
- `CameraWindows` implementuje nové `ICameraWindowPlacer`; `App` předává instanci do view modelu nastavení (`TrayHost.CreateSettingsViewModel(screens, placer)`).
- `CameraViewModel.ApplyWindowSettings(defaults)`: převezme výchozí hodnoty, zahodí pozice zapamatované v otevřeném okně (i pro druhý režim – další přepnutí režimu použije nové výchozí hodnoty), režim podle „Otevřít jako“ („Jako minule“ = zůstane aktuální režim), převezme aktuální počet smazání stavu (okno se po Použít zase pamatuje při zavření) a vyvolá `PlacementResetRequested`. `CameraWindow` pak okno umístí podle výchozích hodnot na zvoleném monitoru (`CameraWindowPlanner.ForOpening` bez pamatované pozice; rám změřený, jen když je okno normální s rámem, jinak předpokládaný), nastaví rám, `Topmost`, `ShowInTaskbar`; celá obrazovka = nejdřív normální okno na zvoleném monitoru, pak `FullScreen`. Minimalizované okno se obnoví. Uložit dál okno nepřesouvá (T-035).

**3. Hlavní panel**
- `CameraWindow`: `ShowInTaskbar` se nastaví před zobrazením podle počátečního režimu a při každém přepnutí režimu (i při Použít): malý náhled `false`, normální okno a celá obrazovka `true`.
- Avalonia 12 na Windows (zdrojový kód `WindowImpl.UpdateWindowProperties`): skrytí z panelu = okno dostane skrytého vlastníka a ztratí `WS_EX_APPWINDOW`; zobrazení = vlastník se zruší a okno se jednou skryje a znovu ukáže (se zachovanou aktivací). Při přepnutí malý náhled → normální okno proto může okno krátce bliknout; skrytý malý náhled zřejmě nebude ani v Alt+Tab. Na skutečném Windows **neověřeno** (headless ověří jen hodnotu vlastnosti). macOS (Dock) neověřeno.

**4. Kamera v nabídce jako přepínač**
- `TrayMenuEntry.IsChecked` (`null` = běžná položka), `INativeTrayMenuItem.SetChecked` → `NativeMenuItem.ToggleType = CheckBox` + `IsChecked`. `TrayMenuTarget` zapisuje zaškrtnutí jen při změně a po kliknutí ho znovu nastaví podle položky (pro případ, že platforma zaškrtnutí při kliknutí sama přepne a okno se přitom neotevřelo).
- `TrayMenuViewModel.SetCameraWindowOpen(id, open)` (UI vlákno): množina otevřených oken; položka Kamera se vytváří se `IsChecked`; změní se jen položky dané tiskárny (její podnabídka se sestaví znovu). Stav přežije aktualizace stavu tiskárny, změnu jazyka i změnu nastavení.
- `ITrayAppCommands.ShowCamera` → `ToggleCamera`; `CameraWindows.Toggle` zavře otevřené okno (`Close()` – stejná cesta jako tlačítko zavřít: zapamatování pozice, zastavení streamu, uložení stavu), jinak otevře. `CameraWindows.OpenChanged` hlásí otevření i každé zavření (tlačítko, nastavení, odebrání tiskárny, ukončení); `App` ho předává `TrayHost.OnCameraWindowOpenChanged` → `TrayMenuViewModel` (po zahájení ukončení se ignoruje).
- Text „Kamera“ bez „…“ v 8 jazycích (Camera / Kamera / Kamera / Kamera / Cámara / Caméra / Telecamera / Kamera); odpovídajícím způsobem upravený text zaškrtávacího pole „Používat kameru (položka Kamera v nabídce …)“ ve všech jazycích.
- Vykreslení zaškrtnutí nativní nabídkou (Win32 nabídka Avalonie, NSMenu) headless testy neověří – ruční kontrola.

**5. Celá obrazovka v malém náhledu**
- `CameraWindow.axaml`: tlačítko celé obrazovky je vidět ve všech režimech (v malém náhledu vlevo od „zpět na normální okno“), stejný styl, zobrazení při najetí / fokusu, tip i jméno pro čtečky ze stávajícího `FullScreenToolTip` („Celá obrazovka (F11)“ / „Ukončit celou obrazovku (Esc)“ – žádný nový text). Nové `AutomationId`: `CameraWindowButtons`, `CameraFullScreenButton`, `CameraMiniButton`, `CameraNormalButton`.
- Rozměry: 2 × 30 DIP + 4 mezi + okraj 6 = 70 DIP ze 160 → při 160×90 se nepřekrývají; okraje překryvu (vlevo nahoře 74 DIP vpravo volných, vpravo nahoře od 40 DIP) už se dvěma tlačítky počítaly, beze změny.
- Odchod z celé obrazovky do malého náhledu na stejné místo zajišťuje logika T-037 (výchozí režim celé obrazovky, zapamatování pozice před přechodem).

**Nové texty (8 jazyků)**
- `SettingsCameraWindowReset`: Restore defaults / Tovární nastavení / Výrobné nastavenia / Standard wiederherstellen / Restablecer valores / Valeurs par défaut / Ripristina predefiniti / Przywróć domyślne
- `SettingsCameraWindowApply`: Apply / Použít / Použiť / Übernehmen / Aplicar / Appliquer / Applica / Zastosuj
- Změněné: `MenuCamera` bez „…“, `SettingsCameraEnabled` (odkaz na položku bez „…“).

**Testy**
- App.Tests: nové `Settings/CameraWindowActionsTests` (Tovární nastavení: hodnoty, Překryv beze změny, zmizení chyb, bez uložení nic, Uložit uloží výchozí a zapomene jen danou tiskárnu a nepřenastaví, zapomenutí i při stejných hodnotách, jen jednou, neúspěšné uložení; Použít: uložení + přenastavení jen dané tiskárny bez zavření, bez změn, po Továrním nastavení zapomenutí před přenastavením, ukládá i jiné změny, chyby / neúspěch nic nepřenastaví, varování, Uložit nikdy nepřenastaví, opakované Použít; tlačítka jen se zapnutou kamerou), `Cameras/CameraViewModelApplyTests` (režim podle „Otevřít jako“ v 9 kombinacích, výchozí hodnoty a zahození pozic, pořadí oznámení, `IsTopmost`, znovu pamatování po Použít, celá obrazovka z malého náhledu a zpět, zlikvidovaný view model, null); `Tray/Menu/TrayMenuTargetTests` (+5 zaškrtávací položky), `Tray/Menu/TrayMenuCameraTests` (+8: zaškrtnutí, jen daná tiskárna, přežití aktualizací / jazyka / nastavení, opakovaná hlášení, presenter → `ToggleCamera`); upravené texty „Camera…“ → „Camera“.
- UiTests: nové `Settings/SettingsWindowCameraWindowActionsUiTests` (titulek bez minimalizace/maximalizace a stále měnitelná velikost; umístění tlačítek; zakázaná při vypnuté kameře; klik na Tovární nastavení + Uložit; Použít přenastaví otevřené okno do malého náhledu na monitoru se 150 % v rohu, bez `Topmost`, bez panelu, okno druhé tiskárny se nehne; normální okno v rohu na zvoleném monitoru i bez změn; celá obrazovka na zvoleném monitoru + Esc; Uložit okno nepřesune; Použít bez otevřeného okna jen uloží), `Cameras/CameraWindowTaskbarUiTests` (`ShowInTaskbar` podle počátečního režimu, všechna přepnutí vč. F11/Esc, po znovuotevření, okna jiných tiskáren), `Tray/TrayMenuCameraToggleUiTests` (skutečná `NativeMenu` + skutečná okna kamery: `ToggleType` CheckBox, otevření / zavření, zastavení streamu a uložení pozice, tlačítko zavřít, vypnutí kamery a odebrání tiskárny v nastavení, více tiskáren, ukončení), `Cameras/CameraWindowMiniFullScreenUiTests` (obě tlačítka v náhledu 160×90 bez překrytí, klik → celá obrazovka → Esc → stejné místo a velikost, normální okno beze změny, snímek). Upravené pořadí Tab v `SettingsWindowCameraWindowUiTests`, texty v `SettingsWindowCameraUiTests`.
- Layout test „bez posuvníku“ (8 jazyků × všechny stavy × 4 pohledy) prošel.
- Snímky přegenerované a zkontrolované: `settings-camera-window`, `settings-camera-window-errors-fr`, `settings-camera-window-off-cs` (tlačítka, zašedlá u vypnuté kamery), `settings-camera`, `settings-camera-notice`, `settings-camera-preview`, `settings-camera-preview-4x3-de` (text zaškrtávacího pole bez „…“); nový `camera-mini-buttons` (náhled 160×90 s oběma tlačítky po najetí myší).
- `dotnet build` 0 varování; `dotnet test` 2× po sobě 3455 testů (3454 prošlo, 1 dřívější přeskočený). `dotnet publish -c Release -r win-x64` (AOT) bez varování; AOT exe i `dotnet run` se spustí a běží (25–30 s, pak ukončeno). Nabídku v oznamovací oblasti, okno nastavení ani okno kamery jsem v běžící aplikaci ručně neotevíral.

**Úpravy po review (ui-dev, 2026-10-20)**
- **Nápověda „Pamatovat…“** (rozhodnutí, dříve Q1) ve všech 8 jazycích, např. cs „Když je zapnuto, hodnoty níže se použijí, jen dokud okno nemá zapamatovanou pozici. Uložit je použije při dalším otevření okna, Použít hned.“, en „… Save applies changes the next time the window opens, Apply right away.“ Ve všech jazycích zůstává na 2 řádcích (layout test bez posuvníku prošel, rezerva beze změny 6 DIP); snímky `settings-camera-window*` přegenerované a zkontrolované.
- **„Uloženo“ po Použít** (rozhodnutí, dříve Q2): nový text `SettingsSavedConfirmation` (Saved / Uloženo / Uložené / Gespeichert / Guardado / Enregistré / Salvato / Zapisano) ve stavovém řádku vedle Uložit; zobrazí se po každém úspěšném Použít bez varování (i když se nic neukládalo), zmizí po `SavedConfirmationDuration` = 3 s (`Task.Delay` s `TimeProvider`, v aplikaci systémové hodiny z DI) nebo hned při další úpravě (pole tiskárny, záložka Obecné, přidání / odebrání tiskárny). Nové Použít čas obnoví; zavření okna časovač zruší. Uložit „Uloženo“ nikdy neukazuje (zavírá okno, případně ukáže varování). Při změně jazyka se text přeloží.
- **Oprava rámu po Použít:** když Použít udělá z okna bez rámu (malý náhled, celá obrazovka, minimalizované) normální okno, umístí se s předpokládaným rámem a po zpracování nového rámu (`Dispatcher.Post`, priorita Background) se jednou změří skutečný rám a okno se opraví stejně jako po otevření (`CorrectFrame`). Měření rámu jde v testech podvrhnout (`CameraWindow.ChromeMeasurement`, `CameraWindows(…, measureChrome)`). Na skutečném Windows neověřeno, zda je rám v okamžiku opravy už změřený.
- **Tlačítka jen mimo ukládání:** `CanExecute` Tovární nastavení a Použít vyžaduje i `SettingsViewModel.IsEditable` (ne během načítání a ukládání); změna se oznamuje (`OnOwnerEditableChanged`).
- Opravený komentář v `CameraHarness` (`ToggleCamera`).
- Nové testy: App.Tests `CameraWindowActionsTests` (+9: „Uloženo“ na 3 s s `FakeTimeProvider`, v jazyce aplikace i bez změn, skrytí při 3 druzích úprav, nové Použít obnoví čas, Uložit „Uloženo“ neukáže, zavření okna časovač zruší, tlačítka zakázaná během ukládání s oznámením a po něm povolená, povolená po načtení); UiTests `SettingsWindowCameraWindowActionsUiTests` (+2: Použít obnoví minimalizované okno kamery a jeho stream; Použít z malého náhledu opraví normální okno podle skutečného rámu; „Saved“ ve stavovém řádku).
- `dotnet build` 0 varování; `dotnet test` 2× po sobě 3467 testů (3466 prošlo, 1 dřívější přeskočený). Kvůli běžící aplikaci z této pracovní kopie (zamčený `bin`) proběhl build a testy s `--artifacts-path` uvnitř pracovní kopie (ignorovaná složka `artifacts/`, po běhu smazaná).

**Zbývá ručně (uživatel, Windows)**
- Podzáložka Okno kamery ve francouzštině s chybami obou velikostí na skutečném Windows (Segoe UI) – bez posuvníku (rezerva v headless testech jen 6 DIP).
- „Uloženo“ po Použít zmizí asi po 3 s nebo při další úpravě.
- Okno nastavení: v titulku jen zavřít; velikost jde měnit tažením okraje.
- Tovární nastavení + Použít s otevřeným oknem kamery (normální okno, malý náhled, celá obrazovka, minimalizované, 2 monitory): okno se přesune na zvolený monitor do zvoleného rohu a velikosti (z malého náhledu do normálního okna i s opravou rámu – viditelný okraj 16 DIP), vždy navrchu odpovídá volbě, nastavení zůstane otevřené.
- Malý náhled není na hlavním panelu (ani po přepnutí z normálního okna a zpět), normální okno a celá obrazovka na panelu jsou; zda přepnutí `ShowInTaskbar` nezpůsobí rušivé bliknutí a jak se malý náhled chová v Alt+Tab.
- Nabídka: nativní nabídka skutečně vykreslí zaškrtnutí u „Kamera“, dokud je okno otevřené (a po kliknutí ho sama nepřepne špatně); druhý klik okno zavře; zaškrtnutí zmizí i po zavření křížkem.
- Malý náhled: po najetí myší dvě tlačítka, Celá obrazovka funguje, Esc vrátí náhled na stejné místo.
- macOS (T-018): titulek okna nastavení, Dock / `ShowInTaskbar`, zaškrtávací položka v NSMenu – neověřeno.

**Oprava 2026-10-21 – Použít se zavřeným oknem kamery** (větev `feature/fix-camera-apply-mini-placement`)
- Hlášení: po Použít (Otevřít jako Malý náhled, Monitor 2) se náhled nezobrazil. Příčina: okno kamery bylo zavřené a Použít dosud přenastavovalo jen otevřené okno (s otevřeným oknem to fungovalo – ověřeno novým E2E testem i na monitoru na výšku). Rozhodnutí uživatele: Použít zavřené okno otevře.
- `ICameraWindowPlacer.ApplySettings(PrinterDefinition)` dostává celou uloženou tiskárnu. `CameraWindows` otevřené okno přenastaví jako dosud; zavřené otevře **z uložené definice** (název, adresa kamery, překryv, výchozí hodnoty okna) – monitorování předá uložené nastavení aplikaci až po restartu běhu (asynchronně), takže nové okno nesmí vycházet z `MonitoredPrinters` (stará adresa, nebo kamera ještě vypnutá → nic by se neotevřelo). Před předáním view modelu oknu se zavolá `CameraViewModel.ApplyWindowSettings`, takže se okno umístí podle výchozích hodnot ještě před zobrazením (bez skoku) a po zavření se zase pamatuje. Otevření hlásí `OpenChanged` (zaškrtnutí položky Kamera v nabídce).
- Továrna view modelu (`AppServices.CreateCameraViewModel`, `TrayHost`) má nepovinnou uloženou tiskárnu; bez ní beze změny z monitorovaných nastavení.
- Testy: App.Tests `ApplyHandsTheSavedPrinterToTheCameraWindows`, `CameraWindowOfApplyComesFromTheSavedPrinter`; UiTests místo `ApplyWithoutAnOpenCameraWindowOnlySaves` nové `ApplyOpensTheClosedCameraWindowOfThatPrinterBySavedSettings` (malý náhled na jiném monitoru, zapamatovaná pozice ignorována, `OpenChanged`, stream běží, druhé Použít neotevře druhé okno), `ApplyOpensTheClosedCameraWindowInFullScreenOnTheChosenMonitor`, `ApplyOpensTheCameraWindowFromTheSavedPrinterBeforeMonitoringHasIt`, `SaveDoesNotOpenAClosedCameraWindow`, `CameraWindowsOpenNothingForAPrinterWithoutCameraOrAfterShutdown`; E2E `CameraWindowTests.ApplyOpensAndPlacesTheMiniPreview` (zavřené okno → malý náhled 320×180 vpravo dole na druhém monitoru s živým obrazem, navrchu, bez tlačítka na panelu; pak otevřené okno → normální okno na primárním monitoru a zpět jedním Použít).
- `dotnet build` 0 varování, `dotnet test` 4379 testů (0 selhání), E2E `*CameraWindowTests` 3/3 prošly na Windows se dvěma monitory (2560×1440 + 1440×2560 na výšku).
- Po review: okno otevřené tlačítkem Použít se zobrazí **bez aktivace** (`Window.ShowActivated = false`, `AuxiliaryWindowSlot.ShowOrActivate(..., activate: false)`), fokus zůstane v okně nastavení (rozhodnutí uživatele 2026-10-21); otevření z nabídky v trayi okno aktivuje jako dosud. Testy `WithoutActivationTheWindowIsOnlyShown`, `TheTrayMenuStillActivatesTheCameraWindow` a kontrola fokusu v `ApplyOpensTheClosedCameraWindowOfThatPrinterBySavedSettings`; test, že po dohnání monitorování zůstane okno otevřené na stejném streamu.
- Známé omezení (review): nové okno odebírá změny `MonitoredPrinters`. Kdyby monitorování během restartu ohlásilo selhání se starými tiskárnami (dřív, než doručí uložené nastavení), okno tiskárny, které kameru zapnulo teprve toto Použít, se zavře; jinak dočasně platí staré hodnoty, dokud nový běh nedoručí uložené. Velmi nepravděpodobné (jen při selhání monitorování), kód se kvůli tomu nemění.
- Druhé kolo review: celá obrazovka po Použít fokus převezme (Avalonia na Windows aktivuje zobrazené okno při změně `WindowState`) – uživatel to 2026-10-21 schválil jako výjimku (Esc/F11 musí jít do okna kamery); totéž platí pro obnovení minimalizovaného okna. `RecentWindows.Track(window, used)`: okno zobrazené bez aktivace (`ShowActivated = false`) se zařadí jako nejdéle nepoužité a nejnovějším se stane až po aktivaci, takže druhé spuštění aplikace vytáhne okno nastavení, se kterým uživatel pracuje. Testy `AWindowOpenedWithoutFocusCountsOnlyOnceActivated`, `AnUnusedWindowIsStillBroughtToTheFrontWhenItIsTheOnlyOne`, `WindowShownWithoutActivationIsNotTheMostRecentUntilActivated`.
