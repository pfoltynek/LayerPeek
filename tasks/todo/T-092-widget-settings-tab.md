---
id: T-092
title: Podzáložka Překryv v nastavení – tabulka viditelnosti widgetů, globální průhlednost, opětovné načtení rozložení
status: todo
owner: ui-dev
priority: P2
platform: all
depends_on: [T-077, T-081]
---

## Cíl
Podzáložka **Překryv** v nastavení tiskárny (T-036) přejde z tabulky 9 položek a volby rohu na nastavení widgetů:
- tabulka **jen s viditelností** každého widgetu,
- akce „Použít toto rozložení u všech tiskáren“,
- „Obnovit výchozí“,
- globální průhlednost pozadí karet.

Okno nastavení navíc převezme rozložení uložené z režimu rozložení v okně kamery, aniž by ztratilo jiné neuložené změny.

## Rozhodnutí (uživatel, 2026-10-24)
- **Tabulka (Q1):** nastavuje **jen viditelnost**. Kotva, pořadí, velikost a zvýraznění se mění jen v režimu rozložení v okně kamery (T-091).
- **Viditelnost** platí pro normální okno i celou obrazovku (sdílené rozložení). Sloupec pro malý náhled **není**, protože malý náhled nemá karty (pás pod obrazem, T-089 / T-090).
- **„Použít toto rozložení u všech tiskáren“:** zkopíruje celé rozložení tiskárny (viditelnost, kotvy, pořadí, velikosti, zvýraznění) do ostatních tiskáren v rozpracovaném nastavení. Uloží se s ostatními změnami.
- **Průhlednost pozadí:** jedno **globální** nastavení, 30–95 %, výchozí 72 %. Umístění podle návrhu T-076 (bod 8), včetně naznačení, že platí pro všechny tiskárny.
- **Vstup do režimu rozložení** je jen v okně kamery (tlačítko a kontextová nabídka, T-091). Tlačítko v nastavení **není**.
- **Souběh (T-077 Q3):** když se rozložení uloží z okna kamery a okno nastavení je otevřené, okno nastavení **znovu načte jen část s rozložením** dané tiskárny a ostatní neuložené změny si ponechá.
- Práh vychladnutí (u tiskárny) přidává T-080, ne tento úkol.

## Kontext
- Dnes: `SettingsViewModel.OverlayPrinterTab`, `PrinterEditorViewModel.OverlayRows` (`CameraOverlayItemRow`), `SelectedOverlayCorner`, `SettingsWindow.axaml`.
- Podzáložka musí být **bez posuvníku** ve výchozí velikosti 780 ve všech 8 jazycích (testy `SettingsWindowLayoutTests`).
- `AutomationId` `PrinterOverlay*` z T-036.
- Rozložení podzáložky navrhne T-076 (bod 8).

## Rozsah
- **Tabulka widgetů:** jeden řádek na registrovaný widget, s ikonou a názvem, a zaškrtávací pole „Zobrazit“. Hodnoty se mapují na `CameraOverlayLayout` přes čisté operace z T-077.
- **Tlačítka:**
  - „Obnovit výchozí“ (rozložení tiskárny),
  - „Použít u všech tiskáren“ (jen při 2 a více tiskárnách, jinak zakázané nebo skryté podle návrhu).
- **Globální průhlednost:** posuvník nebo číselné pole podle návrhu, 30–95 %, uložení do `AppSettings` (T-077). Změna se po uložení projeví ve všech otevřených oknech kamery.
- **Opětovné načtení rozložení:**
  - Při oznámení zapisovače z T-077 (rozložení uložené v okně kamery) nahradí `PrinterEditorViewModel` dané tiskárny jen své rozložení. Ostatní pole včetně neuložených změn zůstanou a stav „neuloženo“ se kvůli tomu nezmění.
  - Oznámení může přijít z jiného vlákna, proto se zpracuje na UI vlákně.
  - Odebraná nebo nová (neuložená) tiskárna oznámení ignoruje.
- Při vypnuté kameře je tabulka zakázaná a hodnoty zůstanou (jako T-036). Globální průhlednost zůstává dostupná.
- Odstranit staré `OverlayRows`, `SelectedOverlayCorner`, staré texty a `AutomationId`. Testy T-036 převést.
- Nové texty ve všech 8 jazycích, přepnutí jazyka za běhu.

## Akceptační kritéria
- [ ] App.Tests:
  - načtení rozložení do tabulky, změna viditelnosti, uložení, přenos do `PrinterDefinition`,
  - „Obnovit výchozí“ a „Použít u všech tiskáren“ (kopíruje celé rozložení, jiné hodnoty tiskáren nemění),
  - globální průhlednost: hranice 30 a 95, výchozí 72, uložení,
  - oznámení z okna kamery nahradí jen rozložení a neuložené změny jiných polí zůstanou; následné uložení zapíše nové rozložení.
- [ ] Headless UI testy:
  - výchozí hodnoty, uložení kliknutím, vypnutá kamera,
  - „Použít u všech tiskáren“ s 1 a 3 tiskárnami,
  - pořadí Tab, `AutomationId`,
  - **bez posuvníku ve všech 8 jazycích** × stavy z `SettingsWindowLayoutTests`,
  - snímky reference.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
