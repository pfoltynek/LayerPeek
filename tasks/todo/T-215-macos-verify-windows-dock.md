---
id: T-215
title: macOS ověření – okna, Dock a ⌘Tab (T-151, T-102, T-038 otázka 2, T-184, T-026, T-188)
status: todo
owner: qa-reviewer
priority: P3
platform: macos
depends_on: [T-209, T-210]
---

## Cíl
Automatizovat chování oken, které T-127 dnes ověřuje okem: Dock a ⌘Tab podle otevřených oken (T-102, T-151),
umístění malého náhledu po celé obrazovce (T-038 otázka 2), žádné černé snímky při změně velikosti (T-184), živý
obraz ve všech režimech (T-026, `mac.camera-look`) a odpočet dalšího pokusu kamery (T-188 / T-194). Vše na testovacím
bundlu se simulátorem.

## Rozsah
Nástroje: `helper windows` / `windowsat` (CGWindowList, pořadí oken), `lsappinfo info -only ApplicationType` (Dock
a ⌘Tab: `Foreground` vs. `UIElement`), `lsappinfo front` (aktivní aplikace), AX stromu Docku (`com.apple.dock`,
AXDockItem: AXPress, AXShowMenu) a `menupress` z T-209. Snímky oken (`screencapture -l`) jen se Záznamem obrazovky,
bez něj jsou dotčené části `SKIP` s důvodem.

- **`mac.dock-windows`** (Permission: Zpřístupnění, preset `release`) – **T-102** a **T-151**:
  - (1) Bez okna je typ `UIElement`. Okno Nastavení nebo kamery → `Foreground` (ikona v Docku, v ⌘Tab). Po zavření
    posledního okna zpět `UIElement`.
  - (2) Menu Docku (AXShowMenu na AXDockItem) obsahuje seznam otevřených oken. AXPress na položce Docku přenese okno
    do popředí s fokusem.
  - (3) „Přenést kameru do popředí“ z menu (T-209) dá oknu kamery fokus.
  - (4) T-102 otázka 3: po přepnutí na jinou běžnou aplikaci (`helper front`) a zpět je AXMenuBar LayerPeeku
    obnovený (zapsat do zprávy, při chybě nový úkol).
  - (5) T-151: okno kamery otevřené, `POST /_simulator/offline` → do ~30 s okno zmizí (a typ `UIElement`, když jiné
    okno nebylo). `POST /_simulator/online` → do ~10 s se okno objeví na stejném místě (bounds ±1 pt). Aktivní zůstane
    předchozí aplikace (`lsappinfo front`), LayerPeek je `Foreground`. Do zprávy zapsat pořadí oken (nad / pod okny
    aktivní aplikace). Malý náhled (vždy navrchu) se objeví navrchu aktuální plochy bez ikony v Docku. Žádná nová
    notifikace (výpis z T-212, je-li hotový; jinak počet z `--layerpeek-e2e-notification-status`).
- **`mac.mini-after-fullscreen`** (Permission, preset `release`) – **T-038 otázka 2 (J5):** z celé obrazovky rovnou do
  malého náhledu třemi cestami (tlačítko náhledu, Esc u celé obrazovky otevřené z náhledu, „Použít“ v nastavení během
  celé obrazovky). Po skončení animace (stabilní bounds 1 s) zapsat velikost, roh a rámeček. `PASS`, když odpovídá
  uloženému malému náhledu (velikost a roh ±1 pt). Jinak `FAIL` s naměřenými hodnotami. Schválená oprava (umístit znovu
  po animaci) = nový úkol.
- **`mac.camera-live`** (Permission + Záznam obrazovky, preset `release`) – **T-026** (`mac.camera-look`) a **T-184**:
  - Normální okno, celá obrazovka, malý náhled: dva snímky okna 1 s po sobě se v oblasti obrazu liší
    (`regiondiff`, živý obraz). Překryv stavu tisku je v AX ve všech třech režimech.
  - Během tažení úchytu malého náhledu, přepnutí režimu (i zeleným tlačítkem) a přesunu mezi monitory (je-li druhý
    monitor) snímá kontrola okno každých ~100 ms. Žádný snímek nemá průměrný jas oblasti obrazu pod prahem černé (nový
    příkaz helperu `luma <png> [<oblast>]`). Stream se nepřeruší: v logu žádné nové „Opening camera stream“.
  - Ostrost a barvy „okem“ (T-184) zůstávají ruční podbod.
- **`mac.camera-next-attempt`** (Permission, preset `release`) – **T-188 / T-194:** `camera/offline` (endpoint z T-210).
  Řádek „Další pokus asi za 5 s“ a další hodnoty 10, 20, 40 s a „asi za 1 min“ se čtou přes `helper axwatch`. Ověří se:
  - odpočet po sekundách pod minutou;
  - v malém náhledu řádek chybí a po návratu do okna je zpět s aktuální hodnotou;
  - minimalizace do Docku a obnovení během minutové pauzy → hned „Připojuji se ke kameře…“;
  - přepnutí režimu pauzu nezkrátí;
  - okno zavřené, během pauzy „Otestovat kameru“ v nastavení s `camera/online` uspěje hned;
  - po `camera/online` řádek zmizí a obraz běží.

  Délka textu ve všech jazycích je v T-214.
- Šetrnost: jeden testovací bundle a jeden simulátor pro všechny čtyři kontroly. Čekání podle stavu (polling 100 ms
  s limitem), ne pevné `sleep`. Kontrola T-188 využije zrychlení simulátoru jen tam, kde aplikace počítá čas podle
  simulátoru. Odpočet aplikace běží v reálném čase, celá kontrola proto potrvá ~2 min. Změřit a zapsat.

## Akceptační kritéria
- [ ] `mac.dock-windows` = `PASS` (uzavře v T-127 bod **T-102** včetně otázky 3 a bod **T-151** „ruční, Dock a ⌘Tab“)
- [ ] `mac.mini-after-fullscreen` = `PASS`, nebo `FAIL` s naměřenými hodnotami a novým úkolem (uzavře v T-127 bod
      **T-038** otázka 2)
- [ ] `mac.camera-live` = `PASS` (uzavře v T-127 bod **T-026** „Stream … ve všech režimech okna“, ruční část
      `mac.camera-look`, a v bodu **T-184** část „nic nebliká a nezčerná, stream se nepřeruší“)
- [ ] `mac.camera-next-attempt` = `PASS` (uzavře v T-127 bod **T-188** kromě délky textu (T-214))
- [ ] Pester / `-SelfTest` a `dotnet build` / `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
