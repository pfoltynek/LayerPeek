---
id: T-091
title: Režim rozložení v okně kamery (tažení do kotev, pořadí, popover, klávesnice)
status: todo
owner: ui-dev
priority: P2
platform: all
depends_on: [T-077, T-082, T-083]
---

## Cíl
Varianta **C** ze schváleného návrhu: rozložení widgetů se upravuje přímo v okně kamery.
- Obraz se ztlumí a 6 kotev se ukáže jako čárkované zóny.
- Widget se přetáhne do kotvy nebo na jiné místo v kotvě. Cílová zóna se zvýrazní a widget se přichytí.
- Klik na widget otevře popover s volbou „zvýraznit“ a velikostí S/M/L.
- Lišta nabízí „Obnovit výchozí“ a „Hotovo“.
- Vše jde ovládat i klávesnicí a má `AutomationId` pro UI testy.

## Rozhodnutí (uživatel, 2026-10-24)
- **Vstup:** **tlačítko v řadě tlačítek okna kamery** (`CameraWindowButtons`) a **položka kontextové nabídky** (pravé tlačítko na obraze).
- **Kde:** jen v **normálním okně**. V malém náhledu ani na celé obrazovce režim není (tlačítko ani položka se tam nezobrazí). Celá obrazovka sdílí rozložení normálního okna, takže úprava platí i pro ni.
- **Uložení:** uloží se až po **„Hotovo“**. **Esc** (mimo otevřený popover) změny zahodí a režim ukončí. Tlačítko „Zrušit“ jen podle návrhu T-076.
- **Pořadí v kotvě:** karty v jedné kotvě jde přeskládat **tažením**. Klávesová obdoba podle návrhu.
- **Zvýraznění** = klíčový údaj widgetu akcentovou barvou (T-076).
- **Jedna instance** každého typu widgetu, takže v režimu není „přidat widget“, jen přesun, velikost, zvýraznění a obnovení.
- **Souběh s oknem nastavení (T-077 Q3):** po „Hotovo“ si otevřené okno nastavení znovu načte jen část s rozložením dané tiskárny a ostatní neuložené změny si ponechá (implementace v T-092, tento úkol vyvolá oznámení přes zapisovač T-077).

## Rozhodnutí (uživatel, 2026-10-25) – vstup zvenku z okna nastavení (T-092)
- **Nahrazuje** dřívější pravidlo z 2026-10-24 (T-092), že tlačítko pro režim rozložení v nastavení **není**: oddíl „Všechny widgety“ na záložce Widgety (T-092) má tlačítko **„Upravit rozložení v okně kamery…“**. Vstup tlačítkem a kontextovou nabídkou v okně kamery (výše) zůstává.
- Režim rozložení proto jde spustit i **zvenku**: s tiskárnou a **volitelným počátečním rozložením** (rozpracované rozložení z okna nastavení).
- **Q4 – počáteční rozložení:** režim začne z předaného rozpracovaného rozložení (bez něj z uloženého). „Hotovo“ ho uloží přes zapisovač T-077 (tím se uloží i rozpracovaná viditelnost této tiskárny z nastavení) a vyvolá `LayoutSaved`. **Esc nic nezmění** – v souboru ani v okně kamery (okno dál ukazuje uložené rozložení).
- **Q5 – malé okno:** pokud je normální okno kamery menší než 480 × 300 (pod tím je režim zakázaný, T-076 Q4), na dobu režimu se **dočasně zvětší na 640 × 360** na stejném místě. Po „Hotovo“ i Esc se vrátí na původní velikost; dočasná velikost se nezapamatuje (`camera-windows.json`).
- Okno kamery dané tiskárny se při vstupu zvenku otevře, nebo aktivuje a přepne do normálního okna (z malého náhledu i z celé obrazovky), viz Návrh UI 5.4 v T-092.

## Kontext
- Model, čisté operace (přesun do kotvy a na pozici, velikost, zvýraznění, obnovení) a zapisovač sekce `overlay` jedné tiskárny s oznámením o zápisu dodá T-077.
- Framework a kotvy dodá T-081. Vzhled, klávesové zkratky a `AutomationId` určí návrh T-076.
- Vstup zvenku volá okno nastavení (T-092, Návrh UI 5.4). T-092 zjišťuje dostupnost tlačítka sám (vybraná, uložená tiskárna se zapnutou kamerou).

## Rozsah
- `CameraLayoutModeViewModel`:
  - pracovní kopie `CameraOverlayLayout`, vybraný widget, cílová kotva a pozice,
  - příkazy přesunu (kotva + pozice v kotvě), velikosti, zvýraznění, obnovení výchozích, Hotovo a Zrušit (Esc),
  - logika bez Avalonie, testovatelná v App.Tests. View jen předává gesta.
- **Vstup:**
  - nové tlačítko v `CameraWindowButtons`, jen v normálním okně, s tooltipem a `AutomationProperties.Name`, ikona z Fluent UI System Icons,
  - kontextová nabídka obrazu s položkou „Upravit rozložení“,
  - `AutomationId` podle návrhu, například `CameraLayoutButton`, `CameraLayoutMenuItem`,
  - přechod do malého náhledu nebo na celou obrazovku během režimu nejdřív režim zruší (jako Esc).
- **Vstup zvenku** (rozhodnutí 2026-10-25): rozhraní v App, např. `ICameraLayoutModeLauncher.Start(string printerId, CameraOverlayLayout? startingLayout)` (název navrhne ui-dev), registrované explicitně v composition rootu:
  - otevře okno kamery tiskárny, nebo ho aktivuje a přepne do normálního okna,
  - okno menší než 480 × 300 dočasně zvětší na 640 × 360 na stejném místě a po ukončení režimu (Hotovo, Esc, přepnutí režimu okna) vrátí původní velikost; dočasná velikost se neuloží do `camera-windows.json`,
  - spustí režim s pracovní kopií z `startingLayout` (nebo z uloženého rozložení, když je `null`),
  - běží-li v okně už režim rozložení, nespustí druhý (chování viz Otevřené otázky).
- **Překryv režimu:**
  - ztlumení obrazu a 6 čárkovaných zón (`AutomationId` `CameraLayoutAnchor{Kotva}`),
  - zvýraznění cíle během tažení včetně ukazatele pozice v kotvě, přichycení,
  - tažení přes `PointerPressed` / `PointerMoved` / `PointerReleased` v rámci okna, ne přes systémové drag & drop.
- **Popover** (`Flyout` / `Popup`) se zvýrazněním a S/M/L (`AutomationId` `CameraLayoutPopover`, `CameraLayoutEmphasize`, `CameraLayoutSize{S|M|L}`). Velikosti, které widget nepodporuje, jsou zakázané.
- **Lišta:** `CameraLayoutReset`, `CameraLayoutDone`, případně `CameraLayoutCancel` podle návrhu.
- **Klávesnice:**
  - výběr widgetu (Tab / šipky),
  - přesun do sousední kotvy a změna pořadí v kotvě (zkratky podle návrhu),
  - otevření popoveru (Enter / mezerník / F2 podle návrhu),
  - Esc = zavřít popover, druhé Esc = zrušit režim, Enter na „Hotovo“ = uložit,
  - fokus je vždy vidět,
  - změny se oznamují čtečce obrazovky, například „Stav přesunut: vpravo nahoře, 2. pozice“.
- Uložení přes zapisovač z T-077 a okamžité použití v otevřeném okně. Oznámení o zápisu dostane okno nastavení (T-092).
- Nové texty ve všech 8 jazycích.

## Akceptační kritéria
- [ ] App.Tests:
  - přesun do každé kotvy a na každou pozici v kotvě,
  - velikost (jen podporované), zvýraznění, obnovení výchozích,
  - Esc / Zrušit zahodí změny, Hotovo uloží jednou a vyvolá oznámení,
  - chyba zápisu se ukáže a nic se neztratí,
  - přepnutí režimu okna během úprav ji zruší,
  - vstup zvenku: s počátečním rozložením i bez něj, Hotovo uloží počáteční rozložení se změnami a vyvolá `LayoutSaved`, Esc nezmění soubor ani rozložení v okně,
  - dočasné zvětšení: okno pod 480 × 300 se zvětší na 640 × 360 a po Hotovo, Esc i přepnutí režimu okna se vrátí; okno od 480 × 300 výš se nemění; dočasná velikost se nezapamatuje.
- [ ] Headless UI testy (`LayerPeek.App.UiTests`):
  - vstup tlačítkem i kontextovou nabídkou jen v normálním okně (v malém náhledu a na celé obrazovce nejsou),
  - simulované tažení myší do každé kotvy a změna pořadí v kotvě se zvýrazněním cíle,
  - popover,
  - **celé ovládání jen klávesnicí**: výběr, přesun mezi všemi kotvami, pořadí, velikost, zvýraznění, Hotovo, Esc,
  - všechny `AutomationId`, nejdelší jazyk, snímky reference,
  - vstup zvenku: zavřené okno se otevře, malý náhled i celá obrazovka se přepnou do normálního okna, malé okno se dočasně zvětší a vrátí.
- [ ] Po „Hotovo“ je rozložení v `settings.json`, celá obrazovka ho používá také a po restartu (novém otevření okna) je stejné
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Rozhodnutí ke vstupu zvenku (uživatel, 2026-10-25)
Uživatel přijal návrhy architekta:
- **Q1 – dočasně zvětšené okno se nevejde na pracovní plochu:** okno 640 × 360 se na dobu režimu posune dovnitř pracovní plochy a po ukončení se vrátí původní poloha i velikost.
- **Q2 – návrat po vstupu z malého náhledu nebo celé obrazovky:** po „Hotovo“ / Esc okno zůstane v normálním režimu.
- **Q3 – vstup zvenku, když okno už je v režimu rozložení:** okno se jen aktivuje a rozpracovaná úprava pokračuje.

## Otevřené otázky
- Žádné.

## Poznámky k implementaci
(vyplní agent po dokončení)
