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
- Každá tiskárna má **vždy vlastní podmenu** (i když je jen jedna): „Otevřít PrusaLink v prohlížeči“, „Diagnostika…“, „✓ Kamera“ (přepínač z T-040, jen u tiskárny se zapnutou kamerou). Řádek tiskárny jen otevírá podmenu (mění rozhodnutí z T-008, kdy klik otevíral web).
- **Globální „Diagnostika…“** z hlavního menu odstranit.
- **Ikony:** na Windows jen obrázky tiskáren (T-039), akce bez ikon; na macOS žádné ikony (T-018).
- **Bez tiskárny:** šedý text „Není nastavena žádná tiskárna“ + nová položka „Přidat tiskárnu…“ (otevře nastavení a přidá tiskárnu).
- **Název tiskárny v menu** zkrátit na cca 32 znaků („…“), celý název zůstává v nastavení a Diagnostice.
- **Klávesové zkratky** (podtržená písmena) u pevných položek a položek podmenu ve všech 8 jazycích, test jedinečnosti; tiskárny bez zkratek.
- Tři tečky u „Nastavení…“ a „Diagnostika…“ zůstávají; Title Case pro macOS řeší T-018.

## Rozhodnuto
- **Struktura (2026-10-20):** varianta A – vždy podmenu tiskárny.
- **Globální Diagnostika (2026-10-20):** odstranit.
- **Ikony (2026-10-20):** Windows jen tiskárny, macOS žádné.
- **0 tiskáren (2026-10-20):** přidat „Přidat tiskárnu…“.
- **Délka názvu (2026-10-20):** cca 32 znaků.
- **Klávesové zkratky (2026-10-20):** ano, 8 jazyků, test jedinečnosti.
- **Tři tečky (2026-10-20):** ponechat; Title Case macOS → T-018.
- Návrh a ukázky: scratchpad `tray-menu-design/tray-menu-proposal.md` (ui-designer, 2026-10-20) – shrnuto níže v `## Návrh UI`.

## Návrh UI
Shrnutí návrhu ui-designera (varianta A), mocky byly vykreslené šablonou Avalonia 12.1.4 Fluent (Windows) a přibližně v HTML (macOS).

```
[ikona] CoreOne: Tiskne – 24 %  ▸ ┌ Otevřít PrusaLink v prohlížeči
[ikona] MK4S: Připravena        ▸ │ Diagnostika…
[ikona] MINI: Nedostupná        ▸ └ ✓ Kamera          ← jen u tiskárny se zapnutou kamerou
──────────
Nastavení…
──────────
O aplikaci
Ukončit
```

```
Není nastavena žádná tiskárna        (zakázaná informace)
Přidat tiskárnu…                     (otevře Nastavení a přidá tiskárnu)
──────────
Nastavení…
──────────
O aplikaci
Ukončit
```

- **Proč A:** menu je stejné pro 1 i N tiskáren (přidáním druhé tiskárny se nic nepřestaví), stav všech tiskáren je vidět na první úrovni, hlavní menu má jen N + 4 položky a dobře škáluje (Windows doporučuje do 15 položek, podmenu jen jedna úroveň). Hlavní menu nemá zaškrtávací položky, takže texty nejsou odsazené o sloupec zaškrtnutí. Cena: akce tiskárny je o krok dál (podmenu se otevře i najetím myší nebo klávesou →).
- **Pořadí:** tiskárny v pořadí z konfigurace → oddělovač → Nastavení… → oddělovač → O aplikaci, Ukončit. „Spouštění…“ a „Sledování se zastavilo kvůli chybě“ zůstávají jako zakázaná informace na místě tiskáren.
- **Ikony:** Windows jen ilustrace tiskáren z T-039 na řádcích tiskáren (sloupec ikon 16 × 16 DIP). Akční ikony ne (dvě sady pro motivy, nutná výměna při změně motivu, zakázané položky ikonu neztlumí). macOS žádné ikony, dokud T-018 neověří template obrázky v NSMenu.
- **Klávesové zkratky:** `_` v `.resx` pro každý jazyk (Windows je podtrhne, macOS je odstraní). Tiskárny zkratky nemají (číslování by zabíralo místo).
- **Šířka:** nejdelší texty jsou francouzské (podmenu cca 290 DIP), dlouhý název tiskárny by menu roztáhl (57 znaků ≈ 440 DIP), proto zkrácení na cca 32 znaků; stav se nezkracuje.
- **Přístupnost:** stav je vždy v textu, ikona tiskárny je dekorativní, Kamera je nativní zaškrtávací položka.

## Akceptační kritéria
- [x] Menu se stejnou strukturou pro 1 i N tiskáren, podmenu s akcemi, bez globální Diagnostiky (testy ViewModelu a presenteru)
- [x] „Přidat tiskárnu…“ při 0 tiskárnách (test)
- [x] Zkrácení názvu na cca 32 znaků (test)
- [x] Klávesové zkratky ve všech 8 jazycích, test jedinečnosti
- [x] Ikony tiskáren na Windows, žádné na macOS (testy)
- [ ] Ruční ověření na Windows (uživatel)
- [x] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish win-x64 bez varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- **Struktura:** `TrayMenuViewModel` dává každé tiskárně položku s podmenu (`TrayMenuCommand.None` + `Submenu`), i když je jen jedna. Plochá položka tiskárny a samostatná „Kamera“ za ní zmizely. Přidáním nebo odebráním tiskárny se mění jen její položka (test `AddingASecondPrinterKeepsTheFirstOneAsItIs`). Podmenu se dál staví jednou na konfiguraci, změna stavu přepíše jen nadpis.
- **Klik na řádek tiskárny** nic nedělá, jen otevře podmenu (mění chování T-008). Web se otevírá položkou „Otevřít … v prohlížeči“ v podmenu; přihlášení přes proxy (T-028) funguje stejně. Neplatná adresa: řádek zůstává povolený (Diagnostika jde otevřít), zakázaná je jen webová položka s tooltipem „Adresa tiskárny není platná“. Upravené testy T-008/T-013/T-014/T-040, `docs/architecture.md` (bod 8).
- **Globální „Diagnostika…“** odstraněna z pevných položek. `TrayMenuOptions.DiagnosticsAvailable` dál řídí položku v podmenu.
- **„Přidat tiskárnu…“** (`TrayMenuCommand.AddPrinter`, klíč `MenuAddPrinter`) je jen při 0 tiskárnách, pod zakázaným textem. Zapojení: presenter → `ITrayAppCommands.AddPrinter()` → `App.ShowSettings(addPrinter: true)` → `SettingsViewModel.AddPrinterFromTray()`. Ta přepne na záložku Tiskárny a podzáložku Tiskárna a spustí stávající `AddPrinterCommand`. Nové okno to udělá hned po načtení nastavení (příznak do konce `Populate`), už otevřené okno hned. Tiskárnu přidá jen tehdy, když okno žádnou nemá: menu nabízí položku, dokud se nová tiskárna neuloží, a opakované kliknutí tak nepřidá další prázdnou tiskárnu (okno jen ukáže tu neuloženou). Položka se řídí `SettingsAvailable`.
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
  | MenuOpenPrusaLink | _Open PrusaLink in the browser | _Otevřít PrusaLink v prohlížeči | _Otvoriť PrusaLink v prehliadači | _PrusaLink im Browser öffnen | _Abrir PrusaLink en el navegador | _Ouvrir PrusaLink dans le navigateur | _Apri PrusaLink nel browser | _Otwórz PrusaLink w przeglądarce |
  | MenuOpenPrusaConnect | _Open Prusa Connect in the browser | _Otevřít Prusa Connect v prohlížeči | _Otvoriť Prusa Connect v prehliadači | _Prusa Connect im Browser öffnen | _Abrir Prusa Connect en el navegador | _Ouvrir Prusa Connect dans le navigateur | _Apri Prusa Connect nel browser | _Otwórz Prusa Connect w przeglądarce |
  | MenuDiagnostics | _Diagnostics… | _Diagnostika… | _Diagnostika… | _Diagnose… | _Diagnóstico… | _Diagnostic… | _Diagnostica… | _Diagnostyka… |
  | MenuCamera | _Camera | _Kamera | _Kamera | _Kamera | _Cámara | _Caméra | _Telecamera | _Kamera |

  „Není nastavena žádná tiskárna“ je stávající `ToolTipNoPrinter` (bez zkratky). Tam, kde by se první písmeno opakovalo, je zkratka jinde (en „Add _printer…“ vs. „_About“, es „Añadir _impresora…“ vs. „_Acerca de“, fr „À pr_opos“ vs. „_Paramètres…“, it „I_nformazioni“ vs. „_Impostazioni…“).
- **Ověřeno (Windows 11):** `dotnet build` 0 varování, `dotnet test` dvakrát zelené, AOT publish win-x64 bez varování. Aplikaci jsem nespouštěl: nastavení se čte ze skutečného `%APPDATA%` (známá složka, proměnnou prostředí nejde přesměrovat) a uživatel má spuštěnou vlastní instanci. Struktura a chování menu jsou ověřené headless testy na skutečném `NativeMenu` aplikace.
- **Neověřeno / ruční kontrola (uživatel, Windows):** vzhled podmenu u jedné tiskárny, podtržení zkratek a jejich funkce v tray menu (otevření menu klávesnicí, Alt), tooltip celého názvu u zkráceného řádku tiskárny, „Přidat tiskárnu…“ s prázdným nastavením. macOS (zkratky odstraněné z NSMenu, U+2060 v názvu s `_`, žádné ikony) ověří T-018.
