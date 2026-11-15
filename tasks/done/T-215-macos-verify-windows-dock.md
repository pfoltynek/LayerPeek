---
id: T-215
title: macOS ověření – okna, Dock a ⌘Tab (T-151, T-102, T-038 otázka 2, T-184, T-026, T-188)
status: done
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
- [x] `mac.dock-windows` = `PASS` (uzavře v T-127 bod **T-102** včetně otázky 3 a bod **T-151** „ruční, Dock a ⌘Tab“)
      → přesunuto do T-127 (oddíl 1p)
- [x] `mac.mini-after-fullscreen` = `PASS`, nebo `FAIL` s naměřenými hodnotami a novým úkolem (uzavře v T-127 bod
      **T-038** otázka 2) → přesunuto do T-127 (oddíl 1p)
- [x] `mac.camera-live` = `PASS` (uzavře v T-127 bod **T-026** „Stream … ve všech režimech okna“, ruční část
      `mac.camera-look`, a v bodu **T-184** část „nic nebliká a nezčerná, stream se nepřeruší“) → přesunuto do T-127 (oddíl 1p)
- [x] `mac.camera-next-attempt` = `PASS` (uzavře v T-127 bod **T-188** kromě délky textu (T-214)) → přesunuto do T-127
      (oddíl 1p)
- [x] Pester / `-SelfTest` a `dotnet build` / `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
Implementováno 2026-11-15 na Macu (qa-reviewer, worktree `LayerPeek-t215`), postupem T-210–T-213. Terminál bez
Zpřístupnění, obrazovka během běhů zamčená (Nahrávání obrazovky Terminál má, ale `screencapture -l` v zamčené relaci
selže) – skutečné běhy všech čtyř kontrol jsou bod 1p v T-127.

- **Aplikace beze změny** (žádný nový přepínač; zkrácené časy automatického zavření / otevření okna
  `LAYERPEEK_TEST_CAMERA_CLOSE_AFTER_MS` / `_OPEN_AFTER_MS` z T-151 už existují a jsou jen v Debug). Bez nových balíčků.
- **Helper** (`tools/macos-verify-helper.swift`, AX volání s limitem 1 s, bez Zpřístupnění exit 3):
  - `axwindows` navíc `subrole`, `main`, `focused` (klíčové okno = `AXFocusedWindow` aplikace), `minimized`, `buttons`
    (tlačítka titulku: close / minimize / zoom / fullscreen; prázdné = okno bez rámečku);
  - `axwin <pid> <titulek|*> minimize|restore|raise|fullscreen|close` (AXMinimized, AXRaise, zelené tlačítko
    AXFullScreenButton, jinak zoom, zavírací tlačítko);
  - `focusedmenubar` – pid aktivní aplikace a položky jejího AXMenuBar (T-102 otázka 3);
  - `dock <cesta bundlu|titulek> info|menu|press|menupress <titulek>` – AXDockItem v AX stromu `com.apple.dock` (podle
    AXURL nebo titulku), menu Docku přes AXShowMenu (přečte se a zavře AXCancel / Esc), AXPress na ikoně nebo na položce
    menu; exit 1 = položka / menu / položka menu chybí, 2 = stisk selhal;
  - `drag x1 y1 x2 y2 [ms]` – tažení levým tlačítkem v rovnoměrných krocích;
  - `luma <png> [levá horní pravá dolní]` – průměrný jas a odchylka části obrázku (podíly stran); `lumawatch <okno> <ms>
    <s> <složka>` – snímá okno (`screencapture -l`) po <ms> a vypisuje JSON řádek s jasem středu (20–80 % stran) nebo
    chybou; `regiondiff` navíc `slightShare` (podíl pixelů s rozdílem > 8); `front … regular` – běžná náhradní aplikace
    s ikonou v Docku a vlastním menu barem.
- **Kontroly** (`tools/macos-verify.ps1`, všechny Permission s `Session` + `Accessibility`, `mac.camera-live` navíc
  `ScreenRecording`; preset `release` za `mac.camera-outline`, teď 35 kontrol). Jeden testovací bundle a jeden simulátor
  (rychlost 1) pro všechny čtyři; čekání podle stavu (dotaz po 100–250 ms s limitem), stabilní rámec = beze změny 1 s.
  - `mac.dock-windows`: (1) typ `lsappinfo` bez okna `UIElement`, Nastavení / okno kamery `Foreground`, po zavření
    posledního `UIElement`; (2) `dock info / menu` (obě okna v menu), `dock press` a `dock menupress <okno kamery>` s Finderem
    vpředu → aplikace vpředu, okno `main` + `focused`; (3) „Přenést kameru do popředí“ přes `menupress`; (4) `focusedmenubar`
    před, během (`front … regular`) a po návratu ikonou v Docku, stejné položky = obnoveno (jinak `FAIL` „nový úkol“);
    (5) okno kamery, přes něj běžná náhradní aplikace vpředu, `POST /_simulator/offline` → log „Closing the camera window …“
    a okno pryč (limit 90 s, změřený čas), `UIElement`; `online` → log „Opening … again“ a okno (limit 60 s), rámec ±1 pt,
    vpředu pořád náhradní aplikace, `Foreground`, pořadí oken z `windowsat` (nad / pod) do zprávy. Oznámení zapnutá kromě
    `offline` (`New-TestData -NotificationOptions`), s povolenými oznámeními start přes `open` s `LAYERPEEK_TEST_TOASTS=1`
    a výpis `--layerpeek-e2e-notification-list` před / po: žádná nová; bez nich `SKIP` s důvodem, nikdy `PASS`. Malý
    náhled se zkrácenými časy (3 s / 2 s): znovu navrchu (vrstva > 0, nad oknem aktivní aplikace), `UIElement`.
  - `mac.mini-after-fullscreen`: náhled 320 × 180 vpravo dole (`rememberLastPlacement` vypnuté), změřený jako vzor; pak
    (a) celá obrazovka a tlačítko náhledu, (b) celá obrazovka a Esc (nejdřív se ověří fokus okna), (c) celá obrazovka,
    Nastavení › Tiskárny › Okno kamery › „Použít“ (AXPress). Po animaci velikost, roh se vzdálenostmi od pracovní plochy,
    rámeček (`buttons`), vrstva; `PASS` = velikost a obě vzdálenosti rohu ±1 pt a bez rámečku, `FAIL` s naměřenými
    hodnotami a odkazem na schválenou opravu z T-038 (nový úkol). Důkaz `placements.json`.
  - `mac.camera-live`: normální okno, celá obrazovka (AXPress), malý náhled: dva snímky 1 s po sobě, `regiondiff` přes
    `Surface` (`Get-VerifyLiveImageVerdict`: ≥ 0,2 % pixelů o > 8 úrovní nebo průměr ≥ 0,3), překryv `CameraOverlay`
    (v náhledu `CameraMiniSummary`) s textem v AX. T-184: `lumawatch` po 100 ms během tažení levého horního úchytu
    náhledu ven, náhled → okno, zelené tlačítko tam i zpět, tlačítko celé obrazovky, tlačítko náhledu a tažení náhledu na
    druhý monitor (je-li; jinak poznámka). `Get-VerifyBlackFrames`: práh = větší z 8 a čtvrtiny mediánu prvních 5 snímků;
    aspoň 20 snímků; nejtmavší černý snímek do důkazů, ostatní se smažou. Stream: stejná množina RTSP spojení z `lsof`
    před / po a v logu žádné „Camera stream … failed / ended / sent no frame“ (řádek „Opening camera stream“ na úrovni
    Information aplikace nemá, jen při chybě).
  - `mac.camera-next-attempt`: nahrávání tisku zapnuté (drží stream při zavřeném okně), `axwatch CameraNextAttempt`
    po celou dobu do `next-attempt-watch.jsonl`, `camera/offline`. V pauze 20 s malý náhled (řádek zmizí do 3 s, po návratu
    aktuální hodnota ±2 s), celá obrazovka a zpět; v pauze „1 min“ minimalizace a obnovení (`axwin`) → do 3 s „Connecting
    to the camera…“ v AX nebo nová pauza ≤ 5 s; `camera/online` v krátké pauze → řádek zmizí, RTSP spojení, `CameraOverlay`.
    Pak znovu `offline`, v pauze 20 s zavřít okno, `online`, Nastavení › Tiskárny › Kamera › „Otestovat kameru“ →
    „The camera works (…)“ dřív, než by pauza skončila (změřeno). Z časové osy `Get-VerifyNextAttemptPauses`
    a `Get-VerifyCountdownProblems`: pauzy 5, 10, 20, 40, 60 s (start nebo o 1 s méně), pod minutou kroky po 1 s za
    600–1400 ms, pauzy 5–40 s nezkrácené (i ta s přepnutím režimů). Celková doba do zprávy (odhad asi 3 min).
  - Čisté funkce ve `VerifyTools.psm1` (oddíl T-215): `Format-VerifyBounds`, `Get-VerifyBoundsProblem`,
    `Get-VerifyWindowCorner`, `Get-VerifyScreenOf`, `Get-VerifyDockMenuProblems`, `Get-VerifyWindowOrder`,
    `Get-VerifyRtspPeers`, `Get-VerifyLiveImageVerdict`, `Get-VerifyBlackFrames`, `ConvertFrom-VerifyNextAttemptText`,
    `Get-VerifyNextAttemptPauses`, `Get-VerifyCountdownProblems`; Pester blok „Windows, the Dock and the camera image“
    (10 testů), test presetu obsahuje nové kontroly. Dokumentace: tabulka kontrol, Zpřístupnění / Nahrávání obrazovky,
    oznámení a pozůstatky v `docs/macos-manual-testing.md`; T-127 oddíl 1p a tabulka automatizace.
- **Neověřeno proti skutečnému systému** (bez Zpřístupnění a se zamčenou obrazovkou nešlo): AX strom Docku na macOS 27
  (AXDockItem, AXURL, AXShowMenu, položky oken v menu Docku), `focusedmenubar`, `axwin` (AXMinimized, AXFullScreenButton),
  `drag` po úchytu náhledu, chování okna kamery na celé obrazovce (stejné číslo okna, Nastavení otevřené během celé
  obrazovky), snímky `lumawatch` a prahy živého / černého obrazu. Ověřeno bez oprávnění: `luma` a `regiondiff`
  na vygenerovaných PNG (černá 0, půl bílá 127,5, oblast vpravo 255, rozdíl 15 úrovní → `slightShare` 0,5), chybné
  argumenty exit 1, `lumawatch` zapíše chybu `screencapture` do řádku (zamčená relace: „could not create image from
  window“), nové příkazy bez Zpřístupnění exit 3. Texty logu automatického zavření / otevření odpovídají
  `CameraWindowAutoVisibilityObserver`.
- **Ověřeno 2026-11-15:** `pwsh tools/macos-verify.ps1 -Unattended -Only mac.dock-windows,mac.mini-after-fullscreen,
  mac.camera-live,mac.camera-next-attempt,mac.profile-unchanged -Allow …`, běh `artifacts/verify/20261115-121808/`: čtyři
  `SKIP` (předběžné podmínky Session – zamčená obrazovka – a Accessibility), `PASS mac.profile-unchanged`. `-SelfTest`
  PASSED (45 kontrol), `-ListChecks -Preset release` 35 kontrol, nové mezi povolenými předem. Žádná Release aplikace
  se nespouštěla, profil uživatele beze změny. Nic na GitHub.
- **Build a testy 2026-11-15** (na `205587fe` = aktuální `develop` + tato změna): `dotnet build` Debug i `-c Release`
  0 varování, 0 chyb. Plný `dotnet test`: 10 848 testů, 10 577 prošlo, 271 přeskočeno, 0 selhalo. Pester
  `tools/VerifyTools.Tests.ps1` 93 prošlo / 1 přeskočen (Windows) / 0 selhalo. AOT publish nespuštěn: `src/` beze změny.
- Uzavřeno 2026-11-15: zbývající ruční ověření (běhy čtyř kontrol se Zpřístupněním, Nahráváním obrazovky a odemčenou
  obrazovkou) přesunuto do T-127, oddíl 1p (podle zadání úkolu, postup T-210–T-213)
