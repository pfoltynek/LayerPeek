---
id: T-185
title: Kamera – omezení snímkové frekvence v malém náhledu
status: done
owner: core-dev + ui-dev
priority: P2
platform: all
depends_on: []
---

## Cíl
Výkonnostní analýza (2026-11-06): malý náhled kamery (T-089) zpracovává plnou snímkovou frekvenci zdroje (např. 22 fps),
přestože drobnému obrázku v rohu obrazovky stačí 5–10 fps. Omezení ušetří u většiny snímků převod do BGRA, kopie
a vykreslení na UI vlákně.

## Rozsah
- V režimu malého náhledu dostane okno nejvýš N snímků za sekundu (N z nastavení, viz rozhodnutí 3–5). H.264 se dál dekóduje celé (P-snímky závisí
  na předchozích). Vynechává se až převod, kopie a předání na UI vlákno.
- Omezení platí jen pro proud snímků pro okno. Detekce, nahrávání a timelapse se nemění.
- Po přepnutí zpět do normálního okna nebo na celou obrazovku se hned vrátí plná frekvence.
- Změřit CPU v malém náhledu před a po a zapsat do poznámek.

## Akceptační kritéria
- [x] V malém náhledu dostává okno nejvýš N snímků za sekundu (test s falešným `TimeProvider`).
- [x] Normální okno a celá obrazovka mají plnou frekvenci a přepnutí režimu se projeví okamžitě.
- [x] Nahrávání, detekce a timelapse nejsou ovlivněné (testy hubu).
- [x] Nastavení > Kamera má profily Úsporný / Vyvážený / Plynulý / Vlastní a posuvník 1–30 fps (výchozí Vyvážený,
      10 fps); hodnota se ukládá do nastavení a změna platí hned i pro otevřený malý náhled. (Nová hlavní záložka
      **Kamera**, rozhodnutí 6 a 10; „hned“ = po Uložit, rozhodnutí 9.)
- [x] Texty ve všech `Strings.*.resx`, popis ve všech `manuals/<lang>/manual.md`, rozvržení s dlouhými překlady.
      (Snímky UI sady Windows a kontrola se Segoe UI → přesunuto do T-125.)
- [x] Naměřené hodnoty jsou v `## Poznámky k implementaci`.
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
Rozhodnuto uživatelem 2026-11-10:
1. Malý náhled: **10 fps**. Uživatel zvažuje, že by hodnota mohla být nastavitelná v aplikaci; to chce probrat
   před implementací (zatím pevná hodnota, nastavení případně samostatně).
2. Normální okno bez fokusu: **beze změny** (plná frekvence).

Rozhodnuto uživatelem 2026-11-11 (nastavitelnost, odblokováno):
3. Frekvence malého náhledu **je nastavitelná** posuvníkem, jen pro malý náhled (normální okno a celá obrazovka mají
   dál plnou frekvenci zdroje).
4. **Doporučené profily** Úsporný / Vyvážený / Plynulý = 5 / 10 fps / plná frekvence zdroje; výchozí **Vyvážený (10 fps)**.
   Ke každému profilu krátký popis dopadu na CPU a paměť. Posuvník 1–30 fps; ruční posun mimo hodnotu profilu přepne
   výběr na „Vlastní“. „Plynulý“ = bez omezení.
5. Umístění: **Nastavení > Kamera**, jedno globální nastavení pro všechny kamery. Změna se projeví hned (i u otevřeného
   malého náhledu), nové texty do všech `Strings.*.resx` a do všech manuálů.

Otázky z implementace (ui-dev, 2026-11-11), úkol zůstává `in-progress`:
6. **Kam s ovládacími prvky – na podzáložku Kamera se nevejdou.** Okno nastavení nesmí v žádném stavu a jazyce rolovat
   (`SettingsWindowLayoutTests`, 780 × 532). Změřeno: podzáložka Kamera má v nejvyšších stavech volné místo
   **1 DIP** („odebrané přihlašovací údaje + nejdelší chyba testu kamery“, němčina) a **12 DIP** („úspěšný test + náhled
   snímku“). Celý návrh (nadpis, řádek s profilem, posuvníkem a hodnotou, popis profilu) potřebuje ~100 DIP, nejúspornější
   jeden řádek (popisek, profil, posuvník, hodnota; popis jen jako tooltip) ~40 DIP – v obou případech podzáložka v těchto
   stavech roluje, v polštině a slovenštině se jednořádková varianta nevejde ani na šířku. Podzáložka **Okno kamery**
   (tam je velikost a umístění malého náhledu) je také plná (o 14–48 DIP). Možnosti:
   a) nová hlavní záložka **Kamera** pro globální nastavení kamer (jako záložka „Spuštění a aktualizace“, která vznikla,
      když se nevešla na Obecné; sem by časem mohlo přejít i „Zavřít, když se tiskárna vypne“) – nejdoslovnější „Nastavení >
      Kamera“, místa dost; nutno ověřit, že se 6 záložek vejde do jednoho řádku ve všech jazycích;
   b) záložka **Widgety**, položka **Malý náhled** (T-110, nastavení pásu malého náhledu) – tematicky blízko, ale tam je
      nastavení po tiskárnách;
   c) záložka **Nahrávání** (globální nastavení kamer: složka, timelapse, detekce) – místo neověřeno;
   d) zůstat na podzáložce Kamera a udělat místo jinde (např. náhled snímku 256 × 144 místo 320 × 180 ušetří 36 DIP,
      chyba testu nejvýš na 2 řádky s tooltipem), nebo v těch vzácných stavech povolit rolování.
   Doporučení ui-dev: a). Ovládací prvky jsou připravené ve view modelu (`SettingsViewModel.MiniFrameRate.cs`), XAML je
   malá práce po rozhodnutí.
7. **Posuvník u profilu „Plynulý“ (bez omezení).** Profil nemá hodnotu fps; implementováno: posuvník se postaví na pravý
   konec (30) a vedle něj je „Bez omezení“; ruční posun kamkoli (i zpět na 30) vybere „Vlastní“ s danou hodnotou (30 fps
   je skutečný limit, u kamer do 30 fps prakticky stejné jako bez omezení). Alternativa: pravý konec posuvníku = „Bez
   omezení“ (pak by šlo nastavit nejvýš 29 fps jako limit). Výběr „Vlastní“ v seznamu ponechá hodnotu posuvníku (u
   „Plynulý“ tedy 30 fps).
8. **Texty popisů profilů** (návrh, en/cs, ostatní jazyky obdobně): Úsporný „5 fps: nejnižší zátěž procesoru a paměti,
   pohyb v obraze je trhanější.“, Vyvážený „10 fps (doporučeno): pro malý obraz dost plynulé při zlomku zátěže
   procesoru a paměti.“, Plynulý „Plná snímková frekvence kamery jako v normálním okně: nejvyšší zátěž procesoru a
   paměti.“, Vlastní „Vlastní hodnota: méně snímků za sekundu znamená menší zátěž procesoru a paměti.“ Pozor: obsazená
   paměť (RSS) se s frekvencí prakticky nemění (měřeno), klesá jen paměťový provoz (kopie za sekundu); „paměť“ v popisu
   tedy znamená provoz, ne velikost. Hodnota vedle posuvníku je všude „N fps“ (francouzsky „i/s“, polsky „kl./s“).
   Ponechat, nebo upravit?
9. **„Změna platí hned“** je implementována jako u ostatních nastavení: po **Uložit** se otevřený malý náhled přepne bez
   znovuotevření a bez nového spojení. Během posouvání před uložením se nic nemění (okno nastavení nemá živý náhled
   jiných voleb). Stačí to?


Rozhodnuto uživatelem 2026-11-11 (k otázkám 6–9):
6. **a) nová hlavní záložka „Kamera“** pro globální nastavení kamer – s podmínkou, že se šest záložek vejde do jednoho
   řádku ve všech 8 jazycích při minimální šířce okna (bez rolování, dlouhé překlady); když ne, zastavit a předložit
   možnosti. → **Nevejde se, viz otázka 10.**
7. Ponechat: pravý konec posuvníku = 30, u „Plynulý“ se ukazuje „Bez omezení“; ruční posun vybere „Vlastní“, až 30 fps
   včetně.
8. Popisy: ano, zmínit CPU a **množství kopírovaných dat** (ne obsazenou paměť). → Texty ve všech 8 jazycích upraveny.
9. Platí po **Uložit** jako ostatní nastavení (současné chování).

Otázka z implementace (ui-dev, 2026-11-11, blokuje dokončení):
10. **Šest hlavních záložek se nevejde do jednoho řádku.** Změřeno headless (Inter, stejné jako `SettingsWindowLayoutTests`)
    se šestou záložkou „Kamera / Caméra / Telecamera“ na konci; šířka pro záhlaví je 748 DIP při výchozí šířce okna 780
    a 608 DIP při minimální šířce 640:

    | Jazyk | 5 záložek dnes | 6 záložek | při 780 | při 640 |
    |---|---|---|---|---|
    | en | 578 | 668 | vejde se | 2 řádky |
    | cs | 601 | 689 | vejde se | 2 řádky |
    | de | 615 | 703 | vejde se | 2 řádky |
    | es | 630 | 719 | vejde se | 2 řádky |
    | pl | 649 | 737 | vejde se (11 DIP rezerva) | 2 řádky |
    | sk | 660 | 748 | přesně 748, se značkou „!“ chyby na záložce 2 řádky | 2 řádky |
    | it | 643 | 765 | **2 řádky (+17)** | 2 řádky |
    | fr | 708 | 798 | **2 řádky (+50)** | 2 řádky |

    Pozor: už dnešních 5 záložek se při minimální šířce 640 vejde jen v en a cs; dosavadní pravidlo a testy hlídají
    jeden řádek při výchozí velikosti 780 × 532. Nejširší záhlaví jsou „Démarrage et mises à jour“ (245 DIP),
    „Uruchamianie i aktualizacje“ (253), „Enregistrement“ (152), „Registrazione“ (139). Možnosti:
    a) kratší záhlaví ve fr/it (např. fr „Démarrage, MàJ“ / „Mises à jour“, „Enregistrer“; it „Avvio e aggiornamenti“ →
       „Aggiornamenti“) – vejde se při 780, při 640 ne; pravidlo „při 780“ by zůstalo;
    b) menší vodorovné odsazení záhlaví hlavních záložek (dnes ~45 DIP na záložku z šablony Fluent; −8 DIP na záložku
       ušetří 48 DIP – fr by bylo na hraně);
    c) nastavení kamery jako oddíl na existující globální záložce, např. **Nahrávání** přejmenovaná na „Kamera“ /
       „Kamera a nahrávání“ (žádná nová záložka; místo na záložce ověřit);
    d) sloučit „Spuštění a aktualizace“ zpět s Obecné není možné (Obecné je plné, T-017), ale lze přesunout aktualizace
       jinam a uvolnit šířku;
    e) povolit záhlaví na dvou řádcích jen v úzkém okně (pod výchozí šířkou) a ve výchozí velikosti řešit a)/b).
    Bez rozhodnutí je záložka odstraněná (větev je zelená): view model, texty a chování jsou hotové, v okně ovládací prvky
    zatím nejsou.

    Rozhodnuto uživatelem 2026-11-11: **a) kratší záhlaví hlavních záložek** ve francouzštině a italštině (a podle potřeby
    i v ostatních jazycích, přirozeně a správně), aby se šest záložek včetně nové „Kamera“ vešlo do jednoho řádku při
    výchozí šířce 780 ve všech 8 jazycích, s rezervou i se značkou „!“ chyby; minimální šířka 640 zůstává jako dnes
    (vejít se nemusí).

## Poznámky k implementaci

- Uzavřeno 2026-11-11: zbývající ověření na Windows (snímky UI sady Windows, testy se Segoe UI, volitelně měření)
  přesunuto do T-125 (oddíl 2v) podle `tasks/README.md`.

### Stav (ui-dev + core-dev, 2026-11-11)
Hotovo: jádro, nastavení, okno kamery, nová hlavní záložka **Kamera** v nastavení, texty, manuály, snímky a měření.

### Záložka Kamera a kratší záhlaví (ui-dev, 2026-11-11, rozhodnutí 6 a 10)
- Nová hlavní záložka **Kamera** (`SettingsTabCamera`, `SettingsViewModel.CameraTab = 5`, poslední za Nahráváním, aby se
  neměnily indexy ostatních; počet hlavních záložek 6). Obsah: nápověda „Platí pro všechny kamery…“, nadpis
  **Snímková frekvence malého náhledu**, řádek profil (ComboBox) – posuvník 1–30 – hodnota („10 fps“ / „Bez omezení“)
  a pod ním popis vybraného profilu (CPU a množství kopírovaných dat, rozhodnutí 8). Kompilované bindingy na
  `SettingsViewModel.MiniFrameRate.cs`. Nadpis bez „(všechny kamery)“, protože celá záložka je globální.
- **Změněná záhlaví** (jen tyto čtyři; ostatní jazyky beze změny):

  | Jazyk | Klíč | Dřív | Nyní |
  |---|---|---|---|
  | fr | `SettingsTabStartupUpdates` | Démarrage et mises à jour | Démarrage, MàJ |
  | it | `SettingsTabRecording` | Registrazione | Riprese |
  | pl | `SettingsTabStartupUpdates` | Uruchamianie i aktualizacje | Start i aktualizacje |
  | sk | `SettingsTabStartupUpdates` | Spustenie a aktualizácie | Štart a aktualizácie |

  V italštině se k tomu upravily odkazy na záložku („scheda Riprese“) ve třech textech aplikace a v manuálu; nadpisy
  oddílů 11.5 (fr, pl, sk) a 11.3 (it) manuálů odpovídají novým názvům.
- Změřeno se značkou „!“ na záložce Widgety (jediná hlavní záložka se značkou), šířka pro záhlaví 748 DIP při 780:
  en 678, cs 699, sk 716, de 713, es 729, fr 726, it 723, pl 673 → rezerva 19–75 DIP. Hlídá nový test
  `SettingsWindowLayoutTests.SixMainTabHeadersFitOnOneRowWithTheErrorMarker` (8 jazyků, jeden řádek, rezerva ≥ 12 DIP,
  žádný useknutý text); záložka Kamera je i v pohledech `EverythingFitsWithoutScrollingInEveryState` a
  `AuxiliaryWindowSizeUiTests`. Při 640 se záhlaví zalomí (jako dosud u 5 záložek ve většině jazyků, rozhodnutí 10).
- Referenční snímky UI (macOS sada `tests/LayerPeek.App.UiTests/Snapshots/macOS/`, 40 souborů nastavení) přegenerované
  (`LAYERPEEK_UPDATE_SNAPSHOTS=1`), změna jen v řádku záložek. Sada Windows a běh testů se Segoe UI → přesunuto do
  T-125 (oddíl 2v).
- Oprava během práce: `SettingsViewModel` omezoval `SelectedTabIndex` na 5 záložek (`TabCount`), teď 6.
- UI testy `SettingsWindowCameraTabUiTests` (výchozí stav s texty a přístupnými názvy, Plynulý = posuvník na konci
  a „Bez omezení“, posun posuvníku = Vlastní, uložení).
- Manuály (8 jazyků): nový oddíl **11.6 Kamera** a v oddílu 5.1 odkaz na nastavení; README.md a README.cz.md (přehled
  nastavení), CHANGELOG.
- Snímky přegenerované podle `docs/readme-screenshots.md` (macOS): změnily se jen snímky nastavení (`settings-camera.png`,
  `settings-detection.jpg`, `settings-printers.png`, `settings-recording.png`, `settings-widgets.png` ve všech 8
  jazycích – nová záložka a kratší záhlaví v řádku záložek); zkontrolováno (fr: jeden řádek, nic useknuté).

### Core + dekodér
- `CameraFrameSubscriptionOptions.MaxFrameRate` / `CameraFrameSubscription.SetMaxFrameRate(int?)` (`null` = každý snímek,
  z libovolného vlákna, platí od dalšího snímku bez nového spojení; stejná hodnota nic nedělá).
- Tempo: pevná mřížka 1/N v tikách `TimeProvider` (žádný časovač, žádná alokace; jen pár `Interlocked` na snímek).
  Zdroj, který není násobkem N (22 fps), má v průměru přesně N; snímek až o čtvrt intervalu dřív platí jako včasný
  (jinak by kolísání sítě u kamery s frekvencí blízkou N frekvenci půlilo); snímek pozdě o celý interval (první snímek,
  výpadek) založí novou mřížku, takže po výpadku nepřijde dávka. Nová hodnota vezme hned další snímek. V libovolné sekundě
  tak přijde nejvýš N + 1 snímků (první snímek mřížky), v průměru N.
- `CameraStreamDemand.DisplayDue` + `RequestDisplay(timestamp)` (výchozí: vždy): hub v `ScheduleFrames` spočítá, kdy chce
  další snímek nejbližší odběratel pro zobrazení (bez omezení = každý snímek), a po každém převzatém snímku přeplánuje
  (stávající cesta `IsThrottled`). `FFmpegDecoder.Convert` dál dekóduje **každý** snímek, ale snímek pro zobrazení převede
  jen když je `DisplayDue` – vynechaný snímek nestojí převod, kopii ani předání na UI. Snímek převáděný v plném rozlišení
  pro jiného odběratele (test kamery, snímek poplachu) se převede i pro zobrazení, aby malý náhled nikdy nedostal snímek
  v plném rozlišení místo svého (jinak by okno kvůli jiné velikosti dvakrát měnilo bitmapu).
- Detekce (šedotón, `MinFrameInterval`), snímek poplachu, nahrávky a timelapse (pakety) mají vlastní tempo, omezení se
  jich netýká. `OnDisplaySizeChanged` hubu přejmenován na `OnScheduleChanged` (volá ho velikost i frekvence).

### Nastavení
- `AppSettings.MiniPreviewFrameRate` (`int?`, 1–30 nebo `null` = bez omezení, výchozí 10, součást rovnosti),
  `MiniPreviewFrameRate` (konstanty, `ProfileOf`, `RateOf`) a `MiniPreviewFrameRateProfile` v `Core/Cameras`.
- Soubor: `miniPreviewFrameRate` (číslo; `0` = bez omezení; výchozí 10 se nezapisuje; chybějící = 10; zlomek nebo hodnota
  mimo rozsah = varování a 10). Typované pole `SettingsDocument` ve zdrojově generovaném `SettingsJsonContext` (AOT).
- `MonitoredPrintersState.MiniPreviewFrameRate` nese hodnotu do otevřených oken (jako průhlednost karet).
- `SettingsViewModel.MiniFrameRate.cs`: `MiniFrameRateProfiles` (Úsporný / Vyvážený / Plynulý / Vlastní),
  `SelectedMiniFrameRateProfile`, `MiniFrameRateSlider` (1–30, zaokrouhlení a omezení), `MiniFrameRateText` („10 fps“ /
  „Bez omezení“), `MiniFrameRateDescription` (popis vybraného profilu), sledování změn, uložení, přepnutí jazyka.
- Texty (12 klíčů `SettingsMiniFrameRate*`) ve všech osmi `Strings.*.resx`.

### Okno kamery
`CameraViewModel.FrameRate.cs`: lease okna má `MaxFrameRate` jen v režimu malého náhledu (hodnota z nastavení), jinak
`null`. Přepnutí režimu (`Mode`, i celá obrazovka z malého náhledu a zpět) a uložené nastavení (`ApplyPrinters`) volají
`SetMaxFrameRate` na běžícím lease – bez nového spojení, hned od dalšího snímku. Nový běh (obnovení z minimalizace) začíná
s limitem aktuálního režimu. Do `CameraViewModel.cs` jen čtyři řádky (po sloučení develop s T-194 bez konfliktu) (konstruktor, `Mode`, `StartStream`, `ApplyPrinters`).

### Dokumentace
`docs/architecture.md` (odstavec T-185), `CHANGELOG.md`, všech 8 `manuals/<lang>/manual.md` (oddíl 5.1: malý náhled
ukazuje nejvýš 10 snímků za sekundu, normální okno a celá obrazovka každý snímek; nastavení v oddílu 11.6).

### Měření (macOS 27.2 / Apple M2 Pro, 10 jader, Debug JIT, 2026-11-11)
Simulátor (`tools/PrusaSimulator`, scénář `idle`, kamera 1280 × 720 při 25 fps, porty 18080 / 18554), aplikace
v izolovaném testovacím režimu (`LAYERPEEK_TEST_DATA_DIR` + přípona instance), tiskárna s `startMode: Mini`, okno
otevřené příkazem `camera:sim1`; malý náhled 320 × 180 DIP na Retina (snímek 640 × 384 px, T-184). Po 15 s zahřátí CPU
času procesu aplikace za 30 s (`ps -o time`), RSS. „Před“ = `develop` `b811398b`, „po“ = tato větev; 3 kola, pořadí
v kolech střídané. Zátěž stroje během měření: load average 1,4–1,9 na 10 jader (souběžní agenti nestavěli; běžela
systémová animace tapety zamčené obrazovky, ~41 % jednoho jádra ve WindowServeru, ve všech bězích stejně – WindowServer
proto jako ukazatel nepoužívám). Displej byl probuzený přes `caffeinate -d -u`.

| Varianta | CPU kolo 1 / 2 / 3 (% jednoho jádra) | průměr | RSS |
|---|---|---|---|
| před (plných 25 fps) | 17,8 / 18,1 / 17,8 | **17,9 %** | 245–246 MB |
| po, Plynulý (bez omezení) | 17,8 / 17,8 / 17,9 | 17,8 % | 245–246 MB |
| po, Vyvážený 10 fps (výchozí) | 11,9 / 12,6 / 12,5 | **12,3 % (−31 %)** | 245–246 MB |
| po, Úsporný 5 fps | 10,5 / 10,2 / 10,5 | **10,4 % (−42 %)** | 246–248 MB |

„Plynulý“ = stejné jako před (bez omezení se nic nemění). Zbylých ~10 % při 5 fps je hlavně dekódování všech 25 snímků
za sekundu (nutné, P-snímky závisí na předchozích), příjem RTSP a monitorování; hardwarové dekódování řeší T-192.
Obsazená paměť se nemění (buffery jsou stejné), klesá paměťový provoz: u 640 × 384 px ~0,98 MB na snímek × 2 (převod
+ kopie do bitmapy) ≈ 49 MB/s při 25 fps → ≈ 20 MB/s při 10 fps → ≈ 10 MB/s při 5 fps, plus nahrání textury v rendereru.
Na Windows (x64) neměřeno (kritérium je splněné měřením na macOS).

### Testy
- Core: `CameraFrameRateLimitTests` (tempo na falešných hodinách: průměr N a nejvýš N + 1 za libovolnou sekundu pro
  22 / 25 / 30 fps zdroje a limity 1, 5, 10, 12, 20, 30; limit nad frekvencí zdroje = každý snímek; kamera s frekvencí
  limitu a kolísáním se nepůlí; nová hodnota a `null` hned; po výpadku žádná dávka; neplatné hodnoty; souběh
  s `MinFrameInterval`), `CameraStreamHubFrameRateTests` (malý náhled 10 fps: vynechané snímky dekódované, nepřevedené;
  přepnutí na normální okno a zpět okamžitě bez nového spojení; nová hodnota z nastavení; odběratel v plném rozlišení
  vedle dostane každý snímek a malý náhled nikdy plný; detekce a snímek poplachu se svým intervalem; nahrávka a timelapse
  všechny pakety), `MiniPreviewFrameRateSettingsTests` (výchozí, rovnost, rozsah, profily, zápis, načtení, neplatné hodnoty).
- App: `CameraViewModelFrameRateTests` (jen malý náhled je omezený, celá obrazovka z malého náhledu ne, přepnutí bez nového
  spojení, start v malém náhledu, uložené nastavení hned i v normálním okně pro pozdější přepnutí, nový běh, normální okno
  zobrazí každý snímek), `MiniFrameRateSettingsTests` (profily, posuvník ↔ profil, Vlastní, zaokrouhlení, NaN, změny,
  uložení, jazyk). Falešné feedy v testech Core i App převádějí snímek pro zobrazení podle `DisplayDue` jako dekodér.
- Bez nových balíčků; AOT: žádná reflexe, JSON přes zdrojově generovaný kontext, žádný nový XAML (bindingy až s UI).
- `dotnet build` 0 varování, `dotnet test` (po záložce Kamera a sloučení develop) 10 227 testů: 9 960 prošlo, 267 přeskočeno,
  0 chyb (macOS 27.2 / Apple M2 Pro).
  Aplikace (Debug) při měření pokaždé nastartovala, otevřela malý náhled se streamem simulátoru a běžela až do `quit`.
- Texty s číslem a jednotkou mají nezlomitelnou mezeru („10 fps“, `StringsTests`), francouzština i před dvojtečkou.
