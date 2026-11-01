---
id: T-117
title: Okno kamery – špendlík „Vždy navrchu“ (indikace a přepínání přímo v okně)
status: done
owner: ui-dev
priority: P2
platform: all
depends_on: [T-037, T-091, T-102]
---

## Cíl
Uživatel teď v okně kamery nevidí, zda je okno „vždy navrchu“. Volbu může změnit jen v Nastavení → tiskárna → Okno kamery
(zvlášť normální okno a malý náhled, T-037). Okno má stav ukázat a umožnit ho přepnout přímo (požadavek uživatele
2026-10-31). Implementace na Windows i macOS.

## Rozsah (rozhodnuto 2026-10-31)
1. **Tlačítko se špendlíkem** v řadě tlačítek okna vpravo nahoře (`CameraWindowButtons`), v normálním okně i v malém
   náhledu. Na celé obrazovce se neukazuje, protože ta navrchu není nikdy (T-037). Zapnuto = plný špendlík, vypnuto =
   obrysový. Ovládací prvek je přepínač (`ToggleButton`): popisek a přístupný název říkají stav, čtečka hlásí
   zapnuto / vypnuto. Pořadí v řadě a vzhled navrhne `ui-designer`.
2. **Trvalá indikace:** ostatní tlačítka se dál ukazují jen při najetí myší nebo s fokusem. Špendlík zůstane při zapnutém
   „vždy navrchu“ vidět i bez myši, ztlumený na svém místě v řadě. Při najetí se řada ukáže celá a špendlík v plné
   síle. Vypnutý špendlík se bez myši neukazuje.
3. **Kliknutí uloží volbu** tiskárny pro aktuální režim (`TopmostNormal` v normálním okně, `TopmostMini` v malém
   náhledu) cíleným zápisem do nastavení, jako `ISettingsStore.SetCameraOverlayLayoutAsync` (mění se jen ta hodnota).
   Okno se přepne hned. Otevřené Nastavení ukáže novou hodnotu stejně jako u ostatních cílených zápisů, bez ztráty
   neuložených úprav jinde.
4. **Kontextové menu obrazu:** zaškrtávací položka „Vždy navrchu“. Kontextové menu se rozšíří i na malý náhled
   (teď je jen v normálním okně, T-091). V malém náhledu nabízí jen tuto položku, rozložení widgetů dál jen v normálním
   okně.
5. Navazující chování T-102 (tlačítko na hlavním panelu, ikona v Docku / ⌘Tab, Spaces na macOS) už reaguje na změnu
   `Window.Topmost` a zůstává beze změny. Ověřit, že platí i při přepnutí z okna.

## Akceptační kritéria
- [x] Špendlík v normálním okně i malém náhledu, ne na celé obrazovce; stav odpovídá `CameraViewModel.IsTopmost`
      i po přepnutí režimu a po uložení volby v Nastavení
- [x] Zapnutý špendlík vidět bez myši (ztlumený), vypnutý ne; při najetí / fokusu celá řada (headless UI testy)
- [x] Klik a položka kontextového menu přepnou `Window.Topmost` hned a uloží jen `TopmostNormal` / `TopmostMini`
      dané tiskárny (ostatní nastavení byte po bytu beze změny, jako u `SetCameraOverlayLayoutAsync`)
- [x] Selhání zápisu: okno se vrátí do uloženého stavu, chyba v logu bez citlivých údajů, aplikace nespadne
- [x] Otevřené Nastavení se zobrazenou tiskárnou ukáže novou hodnotu a neztratí neuložené úpravy
- [x] Tlačítko na hlavním panelu (Windows) a ikona v Docku (macOS) následují přepnutí z okna (UI testy s `AppPresence`)
- [x] Přístupnost: přepínač s názvem a stavem pro čtečky, ovladatelný klávesnicí; kontextové menu i v malém náhledu
- [x] Texty ve všech 8 jazycích (popisky zapnuto / vypnuto, položka menu), přístupové klávesy jedinečné
- [x] Snímky UI: okno kamery s ikonou (Windows → T-114, macOS → T-115) → přesunuto do T-114 a T-115
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Návrh vzhledu
(ui-designer 2026-10-31; podklad: `CameraWindow.axaml`, `CameraMiniStrip.axaml`, `OverlayIcons.axaml`,
`OverlayArrangement.cs`, snímek `camera-mini-buttons.png` – tlačítka řady mají 30 × 30 DIP, mezera 4, okraj 6.)

### 1. Ikona
- Fluent UI System Icons, stejný zdroj a verze jako ostatní ikony (`@fluentui/svg-icons` 1.1.344, MIT, licence už je
  v `Licenses/Texts/mit-fluentui-system-icons.txt`). Obě ikony v této verzi existují, do `OverlayIcons.axaml` se
  **přidají** (zatím tam nejsou), s komentářem s názvem souboru jako u ostatních:
  - `IconPin16Regular` ← `ic_fluent_pin_16_regular.svg` (vypnuto, obrysový)
  - `IconPin16Filled` ← `ic_fluent_pin_16_filled.svg` (zapnuto, plný)
  - cesty (z unpkg, svgo-zkrácené; geometrie shodná s originálem, prefix `F1 ` jako u ostatních):
    - Regular: `M10.06 2.45a1.5 1.5 0 0 0-2.39.35L5.65 6.59l-2.8.94a.5.5 0 0 0-.2.82l2.14 2.15-2.64 2.65L2 14l.85-.15 2.65-2.64 2.15 2.14a.5.5 0 0 0 .82-.2l.94-2.8 3.78-2.03a1.5 1.5 0 0 0 .35-2.38zm-1.5.82a.5.5 0 0 1 .8-.12l3.48 3.5a.5.5 0 0 1-.12.8l-3.96 2.1a.5.5 0 0 0-.23.3l-.75 2.22-3.85-3.85 2.23-.75a.5.5 0 0 0 .28-.23z`
    - Filled: `M10.06 2.45a1.5 1.5 0 0 0-2.39.35L5.65 6.59l-2.8.94a.5.5 0 0 0-.2.82l2.14 2.15-2.64 2.65L2 14l.85-.15 2.65-2.64 2.15 2.14a.5.5 0 0 0 .82-.2l.94-2.8 3.78-2.03a1.5 1.5 0 0 0 .35-2.38z`
- Vykreslení: `ov:OverlayIcon` 16 × 16, `CanvasSize` 16, `Foreground` White (jako tlačítko Rozložení). Ikona se mění
  podle `IsChecked` (dva `OverlayIcon` s `IsVisible` vázaným na `IsChecked` / `!IsChecked` vlastního `ToggleButton`,
  nebo styl `:checked` měnící `Data`). Stav nese **tvar** (plný / obrysový), ne barva.
- Čitelnost 16 px: šikmý špendlík s jehlou; obrys 1 px v regular variantě je na černém pozadí 60 % dostatečný
  (stejná tloušťka jako `IconLayoutCellFour16Regular`).

### 2. Pozice v řadě
Řada je zarovnaná vpravo; špendlík je **vždy poslední (úplně vpravo, v rohu)**. Skrytá tlačítka mají jen `Opacity 0`
(layout zůstává), takže při odhalení řady se nic neposouvá, a roh je stejný ve všech režimech – ztlumený špendlík
sedí po přepnutí normální okno ↔ malý náhled na stejném místě vůči pravému hornímu rohu.

```
Normální okno (najetí myší):            Malý náhled (najetí):        Celá obrazovka:
┌──────────────────────────────────┐     ┌──────────────────────┐     ┌───────────────────────┐
│          [▦][⛶][▣][📌]  ←6→│     │      [⛶][▭][📌] ←6→│     │           [▭][▣] │ (bez špendlíku)
│                                  │     │                      │     │                       │
Normální okno bez myši, navrchu:       Malý náhled bez myši, navrchu:
│                     [📌]    │     │               [📌]   │
```
`▦` Upravit rozložení, `⛶` Celá obrazovka, `▭` Normální okno, `▣` Malý náhled, `📌` špendlík. Pořadí ostatních
tlačítek se nemění. `AutomationId` = `CameraTopmostButton`; `IsVisible="{Binding !IsFullScreen}"`. V režimu úprav
rozložení (T-091) je skrytá celá řada včetně špendlíku (beze změny).

Tab pořadí = pořadí v řadě (špendlík poslední).

**Důsledek pro rozložení widgetů:** v normálním okně jsou teď 4 tlačítka: 4 × 30 + 3 × 4 = 132 →
`OverlayArrangement.WindowButtonsReserve` = 6 + 132 + 8 = **146** (bylo 112; komentář upravit na „four buttons“).
`TopRightStart` (44) se nemění. Středová karta horní řady tím může být v úzkém okně o 68 DIP užší / o stupeň menší –
upravit dotčené testy a snímky.

### 3. Ztlumený stav (navrchu, bez myši a fokusu)
- Průhlednost se přesune ze `StackPanel.windowButtons` na jednotlivá tlačítka (rodičovská `Opacity` by se násobila
  a špendlík by nešel ukázat samostatně). Styly v tomto pořadí (pozdější vyhrává):
  1. `StackPanel.windowButtons > :is(Button)` – `Opacity 0`, `DoubleTransition` Opacity 0,15 s (jako dnes);
  2. `StackPanel.windowButtons > ToggleButton.topmostPin:checked` – `Opacity 0.85`;
  3. `Grid#Root:pointerover StackPanel.windowButtons > :is(Button)`, `StackPanel.windowButtons:focus-within > :is(Button)`
     – `Opacity 1`.
- Pozadí zůstává `#99000000`, ikona bílá – ztlumení je jen celková průhlednost 0,85 a to, že je špendlík sám.
- Proč 0,85 (ne 0,5–0,7): nejhorší případ je čistě bílý obraz kamery. Efektivní pozadí = bílá + černá 0,6 × 0,85
  → šedá ≈ 125, ikona ≈ 235 → kontrast ≈ 3,4 : 1 (WCAG 1.4.11 pro grafické prvky ≥ 3 : 1). Při 0,75 vyjde jen
  ≈ 2,6 : 1. Na tmavém obraze ≈ 14 : 1.
- Přechod mezi stavy 0,15 s (stejná `DoubleTransition`); zapnutí / vypnutí kliknutím mění jen ikonu, ne polohu.

### 4. Stavy přepínače
- `ToggleButton` s třídou `topmostPin`; selektor `StackPanel.windowButtons > Button` na `ToggleButton` nesedí, proto
  vlastní styl se stejnými hodnotami: `Background #99000000`, `Padding 6`, `MinWidth 0` → 30 × 30 jako ostatní.
- Fluent `ToggleButton` by v `:checked` vybarvil pozadí akcentní barvou systému – **potlačit**: pro `:checked`,
  `:checked:pointerover`, `:checked:pressed` nastavit `/template/ ContentPresenter#PART_ContentPresenter` stejné pozadí
  a popředí jako nezaškrtnutý stav. Najetí / stisk: pozadí `#B3000000` / `#CC000000` (ve všech variantách motivu,
  okno kamery je vždy černé), aby bílá ikona nikdy neležela na světlém Fluent pozadí.
- Fokus: výchozí Fluent fokusový rámeček (bílý/černý dvojitý) jako u ostatních tlačítek; fokus odhalí celou řadu
  (`:focus-within`). Mezerník / Enter přepíná.
- Pozn. pro ui-dev: `OnWindowButtonClick` bere `e.Source is Button` – `ToggleButton` je potomek `Button`; špendlík se
  po stisku klávesou neskrývá, fokus má zůstat na něm (vyřadit ho z přesunu fokusu).
- Cíl zásahu 30 DIP (< 32 doporučených) – shodně s ostatními tlačítky řady, záměrně neměnit.

### 5. Malý náhled
Nejmenší náhled 240 DIP (`CameraWindowSize.MinMiniWidth`), obraz 16 : 9 → 135 DIP vysoký. Řada: 3 × 30 + 2 × 4 = 98,
s okraji 110 ≤ 240; výška 6 + 30 = 36 ≤ 135. Okraj 6 drží tlačítka mimo úchop pro změnu velikosti (6 DIP). Karty
widgetů v malém náhledu nejsou, rezervu neřešit. Přepínače pruhu (`MiniToggleTheme`: průhledné, akcentní čárka)
se **nepoužijí** – špendlík patří do rodiny tlačítek okna nad obrazem, ne do pruhu.

### 6. Texty (8 jazyků)
Klíče: `CameraTopmostOn` (popisek + `AutomationProperties.Name`, zapnuto), `CameraTopmostOff` (totéž, vypnuto),
`CameraTopmostMenu` (položka kontextového menu, s přístupovou klávesou `_`). Tón jako `CameraNormalPlain` /
`CameraFullScreen`: krátké, bez „klikněte“. Název vychází z existujícího `SettingsCameraWindowTopmost`.

| jazyk | `CameraTopmostOn` | `CameraTopmostOff` | `CameraTopmostMenu` |
|---|---|---|---|
| en | Always on top: on | Always on top: off | Always on _top |
| cs | Vždy navrchu: zapnuto | Vždy navrchu: vypnuto | Vždy _navrchu |
| sk | Vždy navrchu: zapnuté | Vždy navrchu: vypnuté | Vždy _navrchu |
| de | Immer im Vordergrund: ein | Immer im Vordergrund: aus | Immer im _Vordergrund |
| es | Siempre encima: activado | Siempre encima: desactivado | _Siempre encima |
| fr | Toujours au premier plan: activé | Toujours au premier plan: désactivé | _Toujours au premier plan |
| it | Sempre in primo piano: attivo | Sempre in primo piano: disattivato | Sempre in _primo piano |
| pl | Zawsze na wierzchu: włączone | Zawsze na wierzchu: wyłączone | Zawsze na _wierzchu |

- fr: dvojtečka bez mezery podle stávající konvence `Strings.fr.resx` (žádný řetězec tam nepoužívá „ : “).
- Přístupové klávesy: `CameraLayoutEdit` („Upravit rozložení“ atd.) dnes **žádnou nemá** (řetězec slouží i jako
  popisek tlačítka, podtržítko tam být nesmí), takže zvolené klávesy jsou v menu jedinečné. Zvoleny tak, aby se
  nekryly ani s pravděpodobnou klávesou „Upravit rozložení“ (en E, cs/sk U, de B, es E, fr M, it M, pl E), viz otázka
  níže; bez písmen s diakritikou.
- Délky: popisky jsou tooltipy (zalamují se), položka menu nejdelší fr 24 znaků – v menu bez rizika.
- `CameraMini` („Malý náhled, vždy navrchu“) zůstává, popisuje volbu malého náhledu, ne aktuální okno.

### 7. Kontextové menu
- Normální okno: `[Upravit rozložení]`, `[✓ Vždy navrchu]` (bez oddělovače, dvě položky). Malý náhled: jen
  `[✓ Vždy navrchu]`. `MenuItem ToggleType="CheckBox"`, `IsChecked` = `IsTopmost`, **bez ikony** (sloupec ikony
  zabere zaškrtnutí), `AutomationId` = `CameraTopmostMenuItem`. Vždy povolená (i pod 480 × 300, kde je
  „Upravit rozložení“ zakázaná).
- Na celé obrazovce se položka neukazuje (`IsVisible="{Binding !IsFullScreen}"`), pokud tam menu vůbec otevřete.

### 8. Platformy
- **Windows:** menu přes pravé tlačítko, klávesu Menu, Shift+F10; přístupové klávesy se podtrhnou po Alt. Vysoký
  kontrast: okno kamery má pevně černé pozadí a bílé ikony (stávající stav); stav je čitelný tvarem ikony, tooltip
  a menu kreslí systémový motiv. Tmavý / světlý motiv nemá vliv (okno je vždy černé, viz bod 4).
- **macOS:** menu přes pravé tlačítko i ⌃-klik (i v borderless malém náhledu); přístupové klávesy se nezobrazují ani
  nepoužívají (podtržítko jen skryje Avalonia), texty nezmiňují klávesy Windows. VoiceOver hlásí přepínač
  se jménem ze `CameraTopmostOn/Off`.
- Čtečky: `ToggleButton` hlásí stav sám (zapnuto/vypnuto) a název ho obsahuje také (rozhodnuto v rozsahu) – stav
  zazní dvakrát, akceptováno.

### Neověřeno
Reálné DPI na Windows (125/150/200 %), macOS, kontrast na skutečném obraze kamery – snímky T-114 / T-115.

## Otevřené otázky
- (žádné – rozhodnuto 2026-10-31: trvale viditelný špendlík, ukládá do nastavení, i v kontextovém menu; bez klávesové
  zkratky a bez položky v menu v oznamovací oblasti)
- Q-D1 a Q-D2 → **rozhodnuto 2026-10-31: (a) u obou** – menu-only klíč `CameraLayoutEditMenu` s přístupovou klávesou
  v 8 jazycích; jednotné tmavé pozadí při najetí / stisku pro všechna tlačítka řady.
- **Q-D1 (ui-designer): přístupová klávesa i pro „Upravit rozložení“?** Dnes nemá žádnou, nová položka by ji měla
  sama. (a) Přidat menu-only klíč `CameraLayoutEditMenu` s klávesou (en `_Edit layout`, cs/sk `_Upravit…`,
  de `Layout _bearbeiten`, es `_Editar diseño`, fr `_Modifier la disposition`, it `_Modifica layout`, pl `_Edytuj układ`),
  `CameraLayoutEdit` dál pro tooltip – **doporučeno**, menu bude konzistentní (Windows guidelines: všechny položky
  menu s klávesou). (b) Nechat bez klávesy (mimo rozsah T-117).
- **Q-D2 (ui-designer): pozadí při najetí u stávajících tlačítek řady.** Špendlík dostane explicitní `#B3000000` /
  `#CC000000`; ostatní tlačítka používají výchozí Fluent stav najetí (ve světlém motivu Windows může být světlé pod bílou
  ikonou – neověřeno vizuálně). (a) Sjednotit stejné hodnoty pro všechna tlačítka řady – **doporučeno**, malá změna
  stylu. (b) Jen špendlík, ostatní beze změny.

## Poznámky k implementaci
- **Zápis (Core):** `ISettingsStore.SetCameraWindowTopmostAsync(printerId, mode, topmost)` + `JsonSettingsStore` přes
  `Storage/CameraWindowTopmostValueWriter` – stejná pravidla jako `SetCameraOverlayLayoutAsync` (jen ta hodnota, byte po
  bytu, všechny duplicitní záznamy, BOM, konce řádků, novější / nečitelný formát se nemění). Chybějící sekce `window`
  (plné uložení ji vynechá, když je vše výchozí) nebo `"window": null` se doplní jen s touto hodnotou; chybějící hodnota
  rovná výchozí (`topmostNormal: false`, `topmostMini: true`) se nezapisuje. `window` jiného typu (pole, text, číslo,
  bool) = soubor se nemění (jinak by vznikl duplicitní klíč). Výsledek je `CameraWindowTopmostStoreResult`:
  `Written`, `AlreadyStored` (soubor hodnotu už má – zapsalo ji třeba Uložit v Nastavení, než ji převzal běžící
  monitoring, nebo ruční úprava souboru) a `LeftAlone`. Ohlášení: `ICameraWindowTopmostWriter` / `CameraWindowTopmostWriter`
  (událost `TopmostSaved` i u `AlreadyStored`, jako potvrzení `ConfirmAsync` u rozložení – běžící nastavení a otevřené
  Nastavení se srovnají), v aplikaci `DeferredCameraWindowTopmostWriter` se sdíleným `SettingsWriteGate`. Modelově
  `CameraWindowDefaults.WithTopmost`, `AppSettings.WithCameraWindowTopmost`.
- **Šíření:** `MonitoringSession` převezme hodnotu do běžícího nastavení (i čekajícího na restart), okno kamery ji dostane
  přes `MonitoredPrinters`. Nastavení: `AnnouncedCameraTopmost` + `SettingsChange.LatestCameraTopmost` /
  `SettingsSaveResult.KeptCameraTopmost` + `PrinterEditorViewModel.TakeSavedTopmost` – stejná cesta jako u rozložení
  (převezme se jen ta volba toho režimu, jako uložená; souběžné uložení Nastavení ji neztratí, pokud ji okno samo nezměnilo).
  Klik na špendlík přepíše i neuloženou úpravu téže volby v otevřeném Nastavení (platí poslední akce, stejně jako
  u rozložení z okna kamery); ostatní neuložené úpravy zůstanou.
- **Okno kamery:** `CameraViewModel.ToggleTopmostCommand` přepne `IsTopmost` hned (okno → `Topmost`, `ShowInTaskbar`,
  `AppPresence`, Spaces beze změny T-102), zápisy jdou po sobě v pořadí kliknutí; dokud zápis běží, nastavení použitá
  mezitím (uložení i „Použít“ T-040) přepnutou hodnotu nepřepíšou. Selhání (výjimka nebo `LeftAlone`) vrátí volbu
  uloženou v běžícím nastavení a zaloguje (Warning / Error s výjimkou; jen id tiskárny a režim); `AlreadyStored` je
  úspěch.
- **Odchylka od návrhu (drobná):** klik myší na špendlík mu dá fokus a `:focus-within` by pak nechal celou řadu viditelnou
  i po odjetí myši; po kliknutí myší se proto fokus zruší (`FocusManager.Focus(null)`), z klávesnice zůstává na špendlíku.
  Starší chování beze změny (nález 6 z review): po Tab na tlačítko řady a následném kliknutí myší zůstane fokus
  „viditelný“ (`:focus-visible`), takže řada zůstane ukázaná i po odjetí myši – platí pro všechna tlačítka řady už před
  T-117, neměněno.
- Styly podle návrhu: průhlednost po tlačítkách, zaškrtnutý špendlík bez akcentní barvy, najetí / stisk `#B3000000` /
  `#CC000000` pro všechna tlačítka řady (Q-D2 a). Menu: nový klíč `CameraLayoutEditMenu` (Q-D1 a), test
  `CameraContextMenuAccessKeyTests`. `OverlayArrangement.WindowButtonsReserve` = 146.
- **Snímky testů:** 12 referencí malého náhledu (`camera-mini-*`) pro Windows přegenerováno (ztlumený špendlík, výchozí
  `TopmostMini` = zapnuto). Sadu `Snapshots/macOS/` stejných 12 snímků je nutné přegenerovat na Macu (T-115, do té doby
  tam tyto testy selžou). Obrázky README se generují na macOS (`docs/readme-screenshots.md`); přegenerování na Windows
  změnilo i nesouvisející obrázky (jiné vyhlazení), proto vráceno a přesunuto do T-115 (`camera-mini.jpg` se špendlíkem).
- Neověřeno: skutečné z-pořadí, čtečky obrazovky (Předčítání, VoiceOver), macOS (Dock, ⌃-klik v borderless náhledu),
  vzhled při reálném DPI – snímky / ruční ověření T-114 a T-115.
- Uzavřeno 2026-10-31: build 0 varování, `dotnet test` 8875 testů, 0 selhání (270 přeskočeno); review (qa-reviewer) dvakrát, nálezy opraveny; zbývající ruční ověření a snímky přesunuty do T-114 a T-115 (rozhodnutí uživatele).
