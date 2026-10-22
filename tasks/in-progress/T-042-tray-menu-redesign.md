---
id: T-042
title: Jednotné tray menu (podmenu pro každou tiskárnu)
status: in-progress
owner: ui-dev
priority: P2
platform: all
depends_on: [T-039, T-040]
---

## Cíl
Tray menu vypadá stejně pro jednu i více tiskáren, nabízí u každé tiskárny všechny akce a má klávesové zkratky. Návrh ui-designera (2026-10-20) schválený uživatelem.

## Rozsah
- Každá tiskárna má **vždy vlastní podmenu** (i když je jen jedna): „Otevřít PrusaLink“, „Zobrazit kameru“ / „Skrýt kameru“ (obyčejná položka, text podle toho, zda je okno kamery otevřené; od 2026-10-22 místo zaškrtávací „✓ Kamera“ z T-040; jen u tiskárny se zapnutou kamerou). Řádek tiskárny jen otevírá podmenu (mění rozhodnutí z T-008, kdy klik otevíral web).
- **„Diagnostika…“** je jediná položka hlavního menu nad „Nastavení…“ (změna 2026-10-21, viz Rozhodnuto; původně odstranit z hlavního menu a dát do každého podmenu).
- **Ikony:** na Windows jen obrázky tiskáren (T-039), akce bez ikon; na macOS žádné ikony (T-018).
- **Bez tiskárny:** šedý text „Není nastavena žádná tiskárna“ + nová položka „Přidat tiskárnu…“ (otevře nastavení a přidá tiskárnu).
- **Název tiskárny v menu** zkrátit na cca 32 znaků („…“), celý název zůstává v nastavení a Diagnostice.
- **Klávesové zkratky** (podtržená písmena) u pevných položek a položek podmenu ve všech 8 jazycích, test jedinečnosti; tiskárny bez zkratek.
- Tři tečky u „Nastavení…“ a „Diagnostika…“ zůstávají; Title Case pro macOS řeší T-018.

## Rozhodnuto
- **Struktura (2026-10-20):** varianta A – vždy podmenu tiskárny.
- **Globální Diagnostika (2026-10-20):** odstranit – **nahrazeno rozhodnutím z 2026-10-21 níže.**
- **Ikony (2026-10-20):** Windows jen tiskárny, macOS žádné.
- **0 tiskáren (2026-10-20):** přidat „Přidat tiskárnu…“.
- **Délka názvu (2026-10-20):** cca 32 znaků.
- **Klávesové zkratky (2026-10-20):** ano, 8 jazyků, test jedinečnosti.
- **Tři tečky (2026-10-20):** ponechat; Title Case macOS → T-018.
- **„Přidat tiskárnu…“ v otevřeném okně (2026-10-20, uživatel):** tiskárnu přidá jen tehdy, když seznam v okně je prázdný, jinak ukáže tu neuloženou – potvrzeno.
- **Klávesové zkratky a překlady (2026-10-20, uživatel):** všechny zkratky a nové překlady `MenuAddPrinter` (tabulka v poznámkách) schváleny.
- **Obejití chyby `AccessText` v Avalonii 12 (2026-10-20, uživatel):** neviditelný znak U+2060 v názvu tiskárny s `_` přijat.
- **Diagnostika v hlavním menu (2026-10-21, uživatel):** okno diagnostiky má vlastní výběr tiskárny, proto „Diagnostika…“ není v podmenu tiskáren, ale jako jediná položka hlavního menu přímo nad „Nastavení…“. Klik otevře okno bez předvybrané tiskárny (už otevřené okno si tiskárnu ponechá). Povolená jen tehdy, když menu ukazuje tiskárny; zakázaná bez tiskáren, při „Spouštění…“ a po selhání monitorování. Polská zkratka `D_iagnostyka…` (`_Diagnostyka…` by kolidovala s „_Dodaj drukarkę…“).
- **Německá zkratka „Diagnose…“ (2026-10-21, uživatel):** `D_iagnose…` (zkratka I); `_Diagnose…` by v hlavním menu bez tiskáren kolidovala s „_Drucker hinzufügen…“ (D).
- **Přepnutí „Diagnostika…“ bez přestavění menu (2026-10-21, uživatel):** při povolení nebo zakázání „Diagnostika…“ se nepřestavuje celé menu, mění se jen tato jedna položka na místě (otevřené menu se nezavře ani nepřestaví). Celé menu se přestavuje jen při změně jazyka.
- **Kamera jako „Zobrazit kameru“ / „Skrýt kameru“ (2026-10-22, uživatel):** zaškrtávací položka „✓ Kamera“ (T-040) je nahrazena obyčejnou položkou bez zaškrtnutí, jejíž text se mění se stavem okna: zavřené → „Zobrazit kameru“, otevřené → „Skrýt kameru“. Klik okno dál otevírá/zavírá. Zkratka je na podstatném jménu, takže je v obou stavech stejná. Překlady v tabulce níže.
- Návrh a ukázky: scratchpad `tray-menu-design/tray-menu-proposal.md` (ui-designer, 2026-10-20) – shrnuto níže v `## Návrh UI`.

## Návrh UI
Shrnutí návrhu ui-designera (varianta A), mocky byly vykreslené šablonou Avalonia 12.1.4 Fluent (Windows) a přibližně v HTML (macOS).

Stav po rozhodnutí z 2026-10-21 („Diagnostika…“ v hlavním menu):

```
[ikona] CoreOne: Tiskne – 24 %  ▸ ┌ Otevřít PrusaLink
[ikona] MK4S: Připravena        ▸ └ Zobrazit kameru    ← jen u tiskárny se zapnutou kamerou; při otevřeném okně „Skrýt kameru“
[ikona] MINI: Nedostupná        ▸
──────────
Diagnostika…
Nastavení…
──────────
O aplikaci
Ukončit
```

```
Není nastavena žádná tiskárna        (zakázaná informace)
Přidat tiskárnu…                     (otevře Nastavení a přidá tiskárnu)
──────────
Diagnostika…                         (zakázaná, stejně při „Spouštění…“ a po selhání monitorování)
Nastavení…
──────────
O aplikaci
Ukončit
```

- **Proč A:** menu je stejné pro 1 i N tiskáren (přidáním druhé tiskárny se nic nepřestaví), stav všech tiskáren je vidět na první úrovni, hlavní menu má jen N + 4 položky a dobře škáluje (Windows doporučuje do 15 položek, podmenu jen jedna úroveň). Hlavní menu nemá zaškrtávací položky, takže texty nejsou odsazené o sloupec zaškrtnutí. Cena: akce tiskárny je o krok dál (podmenu se otevře i najetím myší nebo klávesou →).
- **Pořadí:** tiskárny v pořadí z konfigurace → oddělovač → Diagnostika… (od 2026-10-21) → Nastavení… → oddělovač → O aplikaci, Ukončit. „Spouštění…“ a „Sledování se zastavilo kvůli chybě“ zůstávají jako zakázaná informace na místě tiskáren.
- **Ikony:** Windows jen ilustrace tiskáren z T-039 na řádcích tiskáren (sloupec ikon 16 × 16 DIP). Akční ikony ne (dvě sady pro motivy, nutná výměna při změně motivu, zakázané položky ikonu neztlumí). macOS žádné ikony, dokud T-018 neověří template obrázky v NSMenu.
- **Klávesové zkratky:** `_` v `.resx` pro každý jazyk (Windows je podtrhne, macOS je odstraní). Tiskárny zkratky nemají (číslování by zabíralo místo).
- **Šířka:** nejdelší texty jsou francouzské (podmenu cca 290 DIP), dlouhý název tiskárny by menu roztáhl (57 znaků ≈ 440 DIP), proto zkrácení na cca 32 znaků; stav se nezkracuje.
- **Přístupnost:** stav je vždy v textu, ikona tiskárny je dekorativní, položka kamery je obyčejná položka, jejíž text říká, co klik udělá („Zobrazit kameru“ / „Skrýt kameru“, od 2026-10-22; předtím nativní zaškrtávací položka).

## Akceptační kritéria
- [x] Menu se stejnou strukturou pro 1 i N tiskáren, podmenu s akcemi, globální „Diagnostika…“ nad „Nastavení…“ povolená jen s tiskárnami (2026-10-21; testy ViewModelu, presenteru a headless UI)
- [x] „Přidat tiskárnu…“ při 0 tiskárnách (test)
- [x] Zkrácení názvu na cca 32 znaků (test)
- [x] Klávesové zkratky ve všech 8 jazycích, test jedinečnosti (2026-10-21 i s „Diagnostika…“ v hlavním menu; pl `D_iagnostyka…`, de `D_iagnose…`)
- [x] Ikony tiskáren na Windows, žádné na macOS (testy)
- [ ] Ruční ověření na Windows (uživatel), viz seznam níže; mimo jiné Předčítání (Narrator) na řádku tiskárny s `_` v názvu (VoiceOver na macOS ověří T-018)
- [x] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish win-x64 bez varování (znovu ověřeno 2026-10-21 po přesunu „Diagnostika…“ do hlavního menu: 4387 testů, 0 selhání, 26–28 přeskočených – 25 E2E na vyžádání, 1 bez nahraných odpovědí, 0–2 `TestModeGuardTests` podle toho, zda běží jiná instance aplikace; AOT publish win-x64 bez varování ověřen i druhou revizí)

## Otevřené otázky
- ~~**Text zaškrtávacího pole v nastavení (2026-10-22):**~~ Vyřešeno 2026-10-22, rozhodl uživatel: `SettingsCameraEnabled` teď odkazuje na položku „Zobrazit kameru“ („Používat kameru (položka „Zobrazit kameru“ v nabídce v oznamovací oblasti)“, en „Use the camera ("Show camera" item in the tray menu)“, obdobně ve všech 8 jazycích).

## Poznámky k implementaci
- **Struktura:** `TrayMenuViewModel` dává každé tiskárně položku s podmenu (`TrayMenuCommand.None` + `Submenu`), i když je jen jedna. Plochá položka tiskárny a samostatná „Kamera“ za ní zmizely. Přidáním nebo odebráním tiskárny se mění jen její položka (test `AddingASecondPrinterKeepsTheFirstOneAsItIs`). Podmenu se dál staví jednou na konfiguraci, změna stavu přepíše jen nadpis.
- **Klik na řádek tiskárny** nic nedělá, jen otevře podmenu (mění chování T-008). Web se otevírá položkou „Otevřít PrusaLink“ / „Otevřít Prusa Connect“ v podmenu; přihlášení přes proxy (T-028) funguje stejně. Neplatná adresa: řádek zůstává povolený (podmenu ukáže zakázanou webovou položku s tooltipem „Adresa tiskárny není platná“). Upravené testy T-008/T-013/T-014/T-040, `docs/architecture.md` (bod 8).
- **„Diagnostika…“ (změna 2026-10-21):** z podmenu tiskáren odstraněna, místo toho pevná položka `TrayMenuCommand.Diagnostics` (bez `PrinterId`, se zkratkou) přímo nad „Nastavení…“. Presenter ji předá jako `ITrayAppCommands.ShowDiagnostics(null)`; otevřené okno si tiskárnu ponechá. Parametr `printerId` zůstává pro příkazy instance (`App.axaml.cs`). Povolená je, když `TrayMenuOptions.DiagnosticsAvailable` a menu ukazuje tiskárny (ne „Spouštění…“, ne „Sledování se zastavilo…“, ne 0 tiskáren). `TrayMenuViewModel.Apply()` (vlákno UI) po synchronizaci položek tiskáren při skutečném přepnutí povolení (start, 0 ↔ N tiskáren, selhání) nahradí v `FixedItems` jen položku „Diagnostika…“ a vyvolá novou událost `ITrayMenuSource.FixedItemChanged` s jejím indexem; nikdy při dotazu na tiskárnu ani při změně sady tiskáren, která povolení nemění. Presenter položku změní na místě (`ITrayMenuTarget.UpdateFixedItem(index, entry)`, obdoba `UpdatePrinterItem`; index je index v seznamu pro `SetFixedItems` včetně oddělovačů, `TrayMenuTarget` přepíše jen změněné vlastnosti existující platformní položky). Když úprava na místě selže (`ArgumentException`, položka už neodpovídá – chyba v kódu), presenter to zaloguje jako varování a přestaví celé menu, místo aby výjimka shodila aplikaci (test `FailedDiagnosticsUpdateRebuildsTheMenuAndLogs`). Celé menu (`FixedItemsChanged` → `SetFixedItems` + vložení tiskáren) se přestaví jen při změně jazyka (rozhodnutí 2026-10-21, viz Rozhodnuto). `RefreshTexts` volá `Apply` bez události o položce a vyvolá jen `FixedItemsChanged`, takže změna jazyka přestaví menu jednou, i když čeká přepnutí. Testy: `TrayMenuViewModelTests` (pořadí, povolení ve všech stavech, `DiagnosticsItemChangesExactlyWhenItFlipsWithoutReplacingTheFixedItems`, `FixedItemChangedIsRaisedOnTheUiThreadWithTheNewEntry`, `SnapshotPollsAndUpdatedSettingsDoNotChangeFixedItems`, `RefreshTextsWithPendingFlipRaisesFixedItemsChangedOnce`, `PendingFlipAfterDisposeRaisesNothing`), `TrayMenuTargetTests` (`UpdateFixedItemChangesOnlyThatItemInPlace`, `UpdateFixedItemRejectsSeparatorsAndBadIndexes`, `UpdateFixedItemAfterARebuildUsesTheNewItems`), `TrayMenuPresenterTests` (klik → `ShowDiagnostics(null)`, zakázaná nic nedělá, `DiagnosticsFlipUpdatesOnlyItsItem` – žádné přestavění ani vyprázdnění, jen jedna úprava pevné položky, `LanguageSwitchWithPendingDiagnosticsFlipRebuildsTheMenuOnce`, `PendingDiagnosticsFlipAfterViewModelDisposeTouchesNothing`), headless `TrayMenuTests.DiagnosticsInTheMainMenuFollowsThePrintersAndOpensWithoutAPrinter` (stejné objekty `NativeMenuItem` pevných položek před i po přepnutí), E2E `TrayMenuTests` (položka v hlavním menu, zkratka D, otevře okno diagnostiky; běží jen v E2E prostředí, `tools/run-e2e.ps1`; při revizi 2026-10-21 prošel 3 z 5 běhů, 2 selhání „Waited 10 s for the tray menu“ při pravém kliku na ikonu – i v kroku před „Diagnostika…“, tedy nestabilita infrastruktury, ne této změny). Po změně na úpravu položky na místě (2026-10-21) prošly 3 z 5 zaznamenaných běhů; 2 selhání `COMException` z UI Automation při otevírání menu (řádek 34, resp. 48 testu), vždy před krokem „Diagnostika…“.
- **„Přidat tiskárnu…“** (`TrayMenuCommand.AddPrinter`, klíč `MenuAddPrinter`) je jen při 0 tiskárnách, pod zakázaným textem. Zapojení: presenter → `ITrayAppCommands.AddPrinter()` → `App.ShowSettings(addPrinter: true)` → `SettingsViewModel.AddPrinterFromTray()`. Ta přepne na záložku Tiskárny a podzáložku Tiskárna a spustí stávající `AddPrinterCommand`. Nové okno to udělá hned po načtení nastavení (příznak do konce `Populate`), už otevřené okno hned. Tiskárnu přidá jen tehdy, když okno žádnou nemá: menu nabízí položku, dokud se nová tiskárna neuloží, a opakované kliknutí tak nepřidá další prázdnou tiskárnu (okno jen ukáže tu neuloženou). Položka se řídí `SettingsAvailable`.
- **Okno nastavení z menu:** logika „otevřít nebo aktivovat, případně přidat tiskárnu“ je v `Settings/SettingsWindowCommand.Show`, `App.ShowSettings` ji jen volá (kvůli testu). Headless test `TrayMenuAddPrinterUiTests` kliká na skutečnou položku „Přidat tiskárnu…“ přes `ITrayAppCommands.AddPrinter()` dvakrát: poprvé vznikne nové okno s novou tiskárnou, podruhé se použije otevřené okno (žádná další tiskárna, přepnutí na záložku Tiskárny). Druhý test: „Nastavení…“ nic nepřidá, následné „Přidat tiskárnu…“ přidá do otevřeného okna.
- **Zkrácení názvu:** `TrayMenuFormatter.MaxNameLength = 32` včetně „…“ (31 znaků + „…“), stav se nezkracuje. Delší název má celý v tooltipu položky (`NativeMenuItem.ToolTip`; Avalonia uvádí, že ho nemusí podporovat každý nativní poskytovatel – ruční ověření). Dřívější limit 100 znaků na celý nadpis zmizel. Limit 100 znaků v nastavení zůstává.
- **Klávesové zkratky:** `_` přímo v hodnotách `.resx` (Menu*, nový `MenuAddPrinter`). `TrayMenuEntry.HasAccessKey` odliší texty z prostředků od prostého textu. `NativeMenuText.HeaderOf` texty s klávesou předá beze změny, prostý text escapuje. Test `TrayMenuAccessKeyTests` pro všech 8 jazyků (`AppLanguages.Supported`) hlídá: každá pevná položka a položka podmenu má právě jednu zkratku (písmeno nebo číslice), zkratky jsou na každé úrovni jedinečné (hlavní menu bez tiskáren i s nimi, podmenu PrusaLink i Prusa Connect s kamerou), tiskárny a zprávy žádnou nemají a název s `_` se zobrazí beze změny. Headless test na skutečném `MenuItem`/`AccessText` ověřuje klávesy, které Avalonia zaregistruje.
- **Chyba Avalonie 12 (zjištěno při implementaci):** `AccessText` zobrazí `__` jako `_`, ale jako klávesovou zkratku zaregistruje znak za **prvním** `_`, tedy u `MK4__dílna` samotné `_` (a při zobrazení zkratek ho podtrhne). Dosavadní escapování (zdvojení `_`) tedy u názvu s podtržítkem zkratku nevylučovalo. `NativeMenuText.EscapeAccessKeys` proto před první `_` vloží skutečnou značku na neviditelném znaku U+2060 WORD JOINER: `MK4_dílna` → `MK4_` + U+2060 + `__dílna`, zobrazí se `MK4_dílna`, zkratka je znak, který nejde napsat. Text bez `_` se nemění. macOS podle kódu Avalonia.Native používá `AccessText.RemoveAccessKeyMarker`, takže v NSMenu bude `MK4` + U+2060 + `_dílna` (neviditelný znak) – neověřeno.
- **Ikony:** beze změny T-039 – Windows ikony jen u řádků tiskáren (`TrayMenuOptions.ForCurrentPlatform`), položky podmenu, „Přidat tiskárnu…“ a pevné položky bez ikon, macOS bez ikon. Testy ve `TrayMenuIconTests` a `TrayMenuIconUiTests`.
- **Nové a změněné texty** (všech 8 jazyků; zkratka je písmeno za `_`):

  | Klíč | en | cs | sk | de | es | fr | it | pl |
  |---|---|---|---|---|---|---|---|---|
  | MenuAddPrinter (nový) | Add _printer… | _Přidat tiskárnu… | _Pridať tlačiareň… | _Drucker hinzufügen… | Añadir _impresora… | _Ajouter une imprimante… | _Aggiungi stampante… | _Dodaj drukarkę… |
  | MenuSettings | _Settings… | _Nastavení… | _Nastavenia… | _Einstellungen… | _Configuración… | _Paramètres… | _Impostazioni… | _Ustawienia… |
  | MenuAbout | _About | _O aplikaci | _O aplikácii | _Über | _Acerca de | À pr_opos | I_nformazioni | _O programie |
  | MenuQuit | _Quit | U_končit | U_končiť | _Beenden | _Salir | _Quitter | _Esci | _Zakończ |
  | MenuOpenPrusaLink | _Open PrusaLink | _Otevřít PrusaLink | _Otvoriť PrusaLink | _PrusaLink öffnen | _Abrir PrusaLink | _Ouvrir PrusaLink | _Apri PrusaLink | _Otwórz PrusaLink |
  | MenuOpenPrusaConnect | _Open Prusa Connect | _Otevřít Prusa Connect | _Otvoriť Prusa Connect | _Prusa Connect öffnen | _Abrir Prusa Connect | _Ouvrir Prusa Connect | _Apri Prusa Connect | _Otwórz Prusa Connect |
  | MenuDiagnostics | _Diagnostics… | _Diagnostika… | _Diagnostika… | D_iagnose… (2026-10-21, dřív _Diagnose…) | _Diagnóstico… | _Diagnostic… | _Diagnostica… | D_iagnostyka… (2026-10-21, dřív _Diagnostyka…) |
  | MenuShowCamera (nový 2026-10-22, nahrazuje MenuCamera) | Show _camera | Zobrazit _kameru | Zobraziť _kameru | _Kamera anzeigen | Mostrar _cámara | Afficher la _caméra | Mostra _telecamera | Pokaż _kamerę |
  | MenuHideCamera (nový 2026-10-22) | Hide _camera | Skrýt _kameru | Skryť _kameru | _Kamera ausblenden | Ocultar _cámara | Masquer la _caméra | Nascondi _telecamera | Ukryj _kamerę |

  „Není nastavena žádná tiskárna“ je stávající `ToolTipNoPrinter` (bez zkratky). Tam, kde by se první písmeno opakovalo, je zkratka jinde (en „Add _printer…“ vs. „_About“, es „Añadir _impresora…“ vs. „_Acerca de“, fr „À pr_opos“ vs. „_Paramètres…“, it „I_nformazioni“ vs. „_Impostazioni…“, od 2026-10-21 pl „D_iagnostyka…“ vs. „_Dodaj drukarkę…“ a de „D_iagnose…“ vs. „_Drucker hinzufügen…“).
- **„Zobrazit kameru“ / „Skrýt kameru“ (2026-10-22):** `TrayMenuViewModel.CameraEntry` vybere `MenuShowCamera` nebo `MenuHideCamera` podle množiny otevřených oken (`SetCameraWindowOpen`), položka už nemá `IsChecked`. Podpora zaškrtávacích položek v menu zmizela úplně, protože ji nic jiného nepoužívalo: `TrayMenuEntry.IsChecked`, `INativeTrayMenuItem.SetChecked` (a `ToggleType`/`IsChecked` v `AvaloniaNativeTrayMenu`) a obejití v `TrayMenuTarget` (po kliku znovu nastavit zaškrtnutí, které Windows samo přepnulo). Změnu textu přenáší `TrayMenuTarget.Slot.Update` jako u každé jiné položky: `SetCameraWindowOpen` zahodí uložené podmenu tiskárny, nový seznam položek se porovná a existující `NativeMenuItem` dostane jen nový `Header` (žádné přestavění menu). Stejná cesta platí pro Windows (Avalonia kreslí menu sama) i macOS (Avalonia.Native přenese změnu `Header` do NSMenuItem) – macOS neověřeno, ověří T-018. Testy: `TrayMenuCameraTests` (text v obou stavech, jen dané tiskárny, přežije změnu stavu, jazyka a nastavení, presenter), `TrayMenuTargetTests` (`CameraItemChangesOnlyItsHeaderInPlace`, `CameraItemInASubmenuSwitchesItsHeaderInPlace`, `ClickedCameraItemShowsTheHeaderSetDuringTheClick`, `ClickedCameraItemThatOpenedNothingKeepsItsHeader`), `TrayMenuAccessKeyTests.CameraItemKeepsAUniqueAndStableAccessKeyWhenItsWindowOpensAndCloses` (8 jazyků, podmenu PrusaLink i Prusa Connect v obou stavech), headless `TrayMenuCameraToggleUiTests` (stejný `NativeMenuItem`, `ToggleType.None`, text podle okna) a E2E `TrayMenuTests` (hledá „Show camera“ a „Hide camera“; běží jen přes `tools/run-e2e.ps1`).
- **Ověřeno (Windows 11):** `dotnet build` 0 varování, `dotnet test` dvakrát zelené, AOT publish win-x64 bez varování. Aplikaci jsem nespouštěl: nastavení se čte ze skutečného `%APPDATA%` (známá složka, proměnnou prostředí nejde přesměrovat) a uživatel má spuštěnou vlastní instanci. Struktura a chování menu jsou ověřené headless testy na skutečném `NativeMenu` aplikace.
- **Ruční kontrola (uživatel, Windows):**
  1. Vzhled podmenu u jedné i více tiskáren, ikony tiskáren, žádné ikony u akcí.
  2. Podtržení zkratek a jejich funkce v tray menu (otevření menu klávesnicí, Alt, písmeno zkratky).
  3. Tooltip s celým názvem u zkráceného řádku tiskárny.
  4. „Přidat tiskárnu…“ s prázdným nastavením (okno se otevře na záložce Tiskárny s novou tiskárnou).
  5. Předčítání (Narrator) na řádku tiskárny s `_` v názvu: přečte název normálně a neviditelný U+2060 nijak neohlásí.
  6. „Diagnostika…“ v hlavním menu nad „Nastavení…“: zakázaná při „Spouštění…“ a bez tiskárny, povolená s tiskárnou, otevře okno diagnostiky (už otevřené okno si tiskárnu ponechá). Změna stavu (např. uložení první tiskárny nebo dokončení startu při otevřeném menu) otevřené menu nezavře ani nepřestaví, jen položku povolí/zakáže.
  7. Položka kamery v podmenu tiskárny: bez zaškrtnutí, „Zobrazit kameru“ otevře okno, při dalším otevření menu je tam „Skrýt kameru“ a zavře ho; po zavření okna křížkem je tam znovu „Zobrazit kameru“.
- **macOS (ověří T-018):** zkratky odstraněné z NSMenu, U+2060 v názvu s `_` (vzhled a VoiceOver), žádné ikony, změna textu „Zobrazit kameru“ / „Skrýt kameru“ v NSMenu.
