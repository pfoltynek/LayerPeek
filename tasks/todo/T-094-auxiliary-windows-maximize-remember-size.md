---
id: T-094
title: Okna Nastavení a Diagnostika – výchozí velikost 780 × 532, zvětšení na celou obrazovku a zapamatování velikosti
status: todo
owner: ui-dev
priority: P2
platform: all
depends_on: []
---

## Cíl
Pomocná okna **Nastavení** a **Diagnostika** dnes mají pevnou výchozí velikost (klient 780 × 508), nejdou maximalizovat a při každém otevření začínají znovu ve výchozí velikosti. Po úpravě:
- obě okna mají novou společnou výchozí velikost **780 × 532** (rozhodnutí uživatele 2026-10-25, T-092 otázka Q2), minimum zůstává **640 × 400**,
- obě okna jde **maximalizovat**,
- každé okno si **pamatuje** velikost a stav maximalizace, které nastavil uživatel, mezi otevřeními i po restartu aplikace,
- okno se vždy vejde na pracovní plochu aktuálního monitoru (`AuxiliaryWindowPlacement.Fit`),
- posuvník se ukáže jen tehdy, když se obsah nevejde.

Úkol nezávisí na sérii widgetů. Změna výchozí velikosti je tady, aby se do aplikace dostala dřív; T-092 na tento úkol nezávisí (když tento úkol ještě nebude hotový, T-092 změní výchozí velikost sám se stejnými hodnotami).

## Rozhodnutí (uživatel, 2026-10-25)
- Výchozí klient Nastavení i Diagnostiky **780 × 532** (stejný u obou), minimum **640 × 400** u obou.
- Přizpůsobení pracovní ploše (`AuxiliaryWindowPlacement.Fit`, `WorkAreaFitter`) zůstává a platí i pro zapamatovanou velikost.
- Posuvník jen tehdy, když je potřeba (každá záložka a Diagnostika už mají `ScrollViewer`).
- Cílové obrazovky: nejhorší běžná pracovní plocha notebooku **≈ 1093 × 580 DIP** (1366 × 768 při 125 %, 1920 × 1080 při 175 %). Nahrazuje cíl 800 × 600 při 100 % z T-033 (výpočet s hlavním panelem 48 DIP v T-092, Návrh UI 2).
- Okna jde maximalizovat (`CanMaximize`); velikost a stav maximalizace se pamatují pro každé okno zvlášť mezi otevřeními i restarty aplikace.

## Kontext
- `AuxiliaryWindowPlacement` (`DefaultClientSize`, `MinClientSize`, `Fit`, `SizeBeforeShowing`, `CorrectionAfterOpening`, `ChromeOf`) a `WorkAreaFitter` v `src/LayerPeek.App/Windows/`.
- `SettingsWindow.axaml` a `DiagnosticsWindow.axaml`: `Width="780"`, `Height="508"`, `MinWidth="640"`, `MinHeight="400"`, `CanMaximize="False"`, `WindowStartupLocation="CenterScreen"`; komentáře v XAML i v kódu odkazují na cíl 800 × 600 při 100 %.
- Diagnostika: graf vyplňuje zbývající výšku (min. 120), větší okno zvětší graf.
- Stav oken kamer se pamatuje v `camera-windows.json` (`ICameraWindowStateStore` / `JsonCameraWindowStateStore`): stav aplikace, ne nastavení; atomický zápis, zápisy na pozadí, poškozený soubor = výchozí hodnoty, opakování při zamčeném souboru (`FileRetry`). Okno nastavení ho nikdy neukládá, takže uložení nastavení nepřepíše polohu okna a naopak.
- Testy: `AuxiliaryWindowPlacementTests` (App.Tests), `SettingsWindowLayoutTests`, `DiagnosticsWindowLayoutTests`, `SettingsWindowSnapshotTests` a snímky `settings-*.png` / `diagnostics-*.png` (UiTests), E2E `SettingsAndDiagnosticsTests`, `AuxiliaryWindowScaleAndThemeTests`, `DisplayChangeTests`.

## Rozsah
- **Výchozí velikost 780 × 532:** `AuxiliaryWindowPlacement.DefaultClientSize`, `Height` v obou XAML, komentáře (nový cíl ≈ 1093 × 580 DIP místo 800 × 600 při 100 %), testy velikosti, přegenerované a zkontrolované snímky reference obou oken.
- **Maximalizace:** `CanMaximize="True"` u obou oken (`CanMinimize` beze změny). Obsah se při maximalizaci roztáhne podle stávajících sloupců `*` (viz Otevřené otázky Q3).
- **Zapamatování velikosti** pro každé okno zvlášť (klíč např. `settings`, `diagnostics`):
  - ukládá se **velikost klientu v DIP** v normálním stavu a příznak **maximalizováno**; poloha se neukládá, okno se dál centruje (`CenterScreen`) – viz Otevřené otázky Q2,
  - ukládá se při zavření okna (případně po dokončené změně velikosti nebo stavu, ne při každém kroku tažení), jen když ji změnil uživatel: velikost zmenšená kvůli malé pracovní ploše se neuloží jako nová preference,
  - u maximalizovaného okna se pamatuje i velikost, na kterou se vrátí po obnovení (poslední normální velikost),
  - úložiště: viz Otevřené otázky Q1 (návrh: nový soubor stavu aplikace vedle `settings.json` se stejnými zásadami jako `camera-windows.json`), explicitní registrace v DI, `System.Text.Json` jen přes zdrojově generovaný `JsonSerializerContext`,
  - zápis nikdy neblokuje UI vlákno a chyba souboru okno nerozbije (výchozí hodnoty, záznam do logu).
- **Obnovení při otevření:**
  - bez uloženého stavu: výchozí 780 × 532 jako dnes (přizpůsobená pracovní ploše),
  - uložená velikost se vždy prožene `AuxiliaryWindowPlacement.Fit` na pracovní plochu **aktuálního** monitoru (monitor, kde se okno otevře): větší než pracovní plocha → zmenší se, menší než minimum → minimum, nečíselné, nekonečné nebo záporné hodnoty → výchozí,
  - uložený stav maximalizace → okno se otevře maximalizované na monitoru, kde se otevírá; po obnovení má uloženou (a přizpůsobenou) normální velikost,
  - odpojený monitor nebo menší rozlišení / vyšší měřítko od posledního uložení → stejné pravidlo `Fit`, nic se nedostane mimo obrazovku.
- Čisté funkce pro výpočet (obnovení, přizpůsobení, rozhodnutí „uložit / neuložit“) v `AuxiliaryWindowPlacement` nebo vedle něj, testovatelné bez obrazovky; `WorkAreaFitter` je jen použije.
- Aktualizovat `docs/architecture.md` (nové úložiště stavu oken) a `docs/troubleshooting.md` (co se maže / co zůstává, podobně jako `camera-windows.json`); případně podpůrný balíček (`SupportBundle`), pokud do něj stav oken kamer patří.

## Akceptační kritéria
- [ ] App.Tests (čisté funkce a úložiště):
  - výchozí velikost 780 × 532 a minimum 640 × 400 v `AuxiliaryWindowPlacement`, shodné s XAML obou oken,
  - obnovení: bez stavu → výchozí; uložená velikost menší než pracovní plocha → beze změny; větší → zmenšená na pracovní plochu; menší než minimum / neplatná → minimum / výchozí; různá měřítka (100 / 125 / 175 %),
  - monitor odpojený nebo menší než při uložení (např. uloženo na 2560 × 1440, otevřeno na 1366 × 768 při 125 %) → okno celé na pracovní ploše, nejmenší velikost je minimum,
  - maximalizace: uložený příznak se obnoví, normální velikost po obnovení je uložená (přizpůsobená) velikost,
  - velikost zmenšená jen kvůli pracovní ploše se neuloží jako preference; velikost změněná uživatelem ano,
  - úložiště: zápis a čtení obou oken zvlášť, poškozený / příliš velký / chybějící soubor → výchozí hodnoty bez výjimky, atomický zápis, žádné soubory při samotném vytvoření úložiště, testy po sobě nic nenechají.
- [ ] Headless UI testy (`LayerPeek.App.UiTests`):
  - obě okna mají `CanMaximize` a dají se maximalizovat a obnovit; po zavření a novém otevření mají uloženou velikost i stav maximalizace,
  - **bez posuvníku ve výchozí velikosti 780 × 532 ve všech 8 jazycích** u všech záložek a podzáložek Nastavení (stavy z `SettingsWindowLayoutTests`) i u Diagnostiky,
  - **posuvník se objeví**, když se okno zmenší (např. na minimum 640 × 400), a obsah zůstane dosažitelný,
  - maximalizované okno: rozvržení bez přetečení a ořezů (snímek reference maximalizovaného okna u obou oken),
  - dosavadní testy rozvržení obou oken v nové velikosti, přegenerované a vizuálně zkontrolované snímky `settings-*.png` a `diagnostics-*.png`.
- [ ] E2E (Windows, `LayerPeek.E2E.Tests`, jen co headless neověří): maximalizace přes UI Automation (`WindowPattern`), zavření, **restart aplikace** a nové otevření → okno je znovu maximalizované; změněná velikost přežije restart; volitelně snížené rozlišení přes stávající infrastrukturu `DisplayChangeTests` (se zálohou, obnovou a varováním podle pravidel E2E) → okno se vejde na pracovní plochu.
- [ ] Žádná reflexní serializace ani bindingy, žádné trim/AOT varování
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- **Q1 – kam ukládat velikost oken:**
  - **nový soubor stavu aplikace vedle `settings.json`, např. `windows.json` (návrh architekta):** stejné zásady jako `camera-windows.json` (stav aplikace, ne nastavení; uložení nastavení ho nepřepíše), oddělený od oken kamer,
  - nová sekce v `camera-windows.json` (zvýšení verze souboru; míchá okna kamer a pomocná okna, soubor má dnes klíče podle tiskárny),
  - `settings.json` (nedoporučeno: okno nastavení ukládá nastavení a uložení velikosti by se s ním míchalo).
- **Q2 – pamatovat i polohu?** Zadání říká velikost a maximalizaci. Návrh architekta: polohu **ne**, okno se dál centruje na monitoru, kde se otevírá (po odpojení monitoru nehrozí okno mimo obrazovku). Alternativa: pamatovat i polohu a monitor (s kontrolou viditelnosti jako u oken kamer).
- **Q3 – obsah maximalizovaného okna:** návrh architekta: nechat roztáhnout podle stávajících sloupců (`*`), jen ověřit snímkem. Alternativa: omezit šířku obsahu (např. max. ≈ 1000 DIP, vycentrovat), aby na velkém monitoru nebyly řádky příliš dlouhé – případně rozhodne ui-designer.
- **Q4 – uložit velikost i po „Zrušit“ v Nastavení?** Návrh architekta: ano, velikost je stav okna, ne nastavení, a ukládá se při zavření bez ohledu na Uložit / Zrušit.

## Poznámky k implementaci
(vyplní agent po dokončení)
