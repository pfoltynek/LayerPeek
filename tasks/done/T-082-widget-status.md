---
id: T-082
title: Widget Stav (název tiskárny, pilulka stavu, průběh)
status: done
owner: ui-dev
priority: P2
platform: all
depends_on: [T-081]
---

## Cíl
Widget **Stav**: název tiskárny, „pilulka“ stavu s barevnou tečkou, velké procento průběhu a pruh průběhu. Velikosti S/M/L a zvýraznění klíčového údaje podle schváleného návrhu T-076.

## Kontext
- Framework a sdílené prvky karet: T-081. Pravidla z T-036: název tiskárny vždy (zkrácený na 40 znaků), stav po prvním dotazu, průběh jen u dostupné tiskárny s běžící / pozastavenou / čekající úlohou (`HasActiveJob` nebo `Attention`).
- Barvy tečky stavu navázat na barvy tray ikony (`App/Tray`, T-007) – podle návrhu T-076.

## Rozsah
- View model + šablona widgetu, jedna registrace v `OverlayWidgetRegistry`.
- Obsah ve všech stavech `PrinterState` (Unknown, Idle, Busy, Printing, Paused, Finished, Stopped, Error, Attention, Offline, Unauthorized); bez úlohy jen název + pilulka (nebo podle návrhu).
- Testy pravidel zobrazení přenesené z T-036 (`CameraPrintContentTests`), které se týkají názvu, stavu a průběhu.
- Nové texty ve všech 8 jazycích (pokud návrh nějaké přidá).

## Akceptační kritéria
- [x] App.Tests: obsah ve všech stavech, chybějící data, velikosti S/M/L, zvýraznění, kultury cs/de/fr, levné porovnání, přepnutí jazyka
- [x] Headless UI testy: widget ve všech velikostech, se zvýrazněním i bez, nejdelší jazyk a dlouhý název tiskárny bez přetečení, barvy tečky pro každý stav; snímky reference (vizuálně zkontrolované)
- [x] `AutomationId` / `AutomationProperties.Name` podle návrhu (čtečka přečte stav a průběh)
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Rozhodnutí (uživatel, 2026-10-24)
- Zvýraznění: klíčový údaj = **procento průběhu**, vykreslený akcentovou barvou `#FA6831`.
- Normální okno a celá obrazovka sdílejí rozložení; v malém náhledu se karta nekreslí (průběh ukazuje pás pod obrazem, T-089).

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- 2026-10-25, ui-dev. Kód v `App/Cameras/Overlay/Widgets/` (`StatusWidgetViewModel`, `StatusWidgetTemplate.axaml`) a `App/Cameras/Overlay/OverlayStateBrushes.cs`, registrace jedním řádkem v `Composition/OverlayWidgets.CreateRegistry` (min. šířky karty S 0 / M 180 / L 220, všechny velikosti, priorita Stav).
- **Obsah** (`StatusWidgetContent`, levně porovnatelný):
  - název tiskárny zkrácený na 40 znaků uprostřed (prvních 8 znaků + „…“ + konec, `OverlayFormat.ShortenName`) přes `OverlayTextCache` (stejný text = stejná instance),
  - stav (`null` před prvním dotazem = „Připojování…“),
  - celé procento (zaokrouhlené dolů) jen u aktivní úlohy podle `OverlayValues.ActiveJob` (tiskne, pozastaveno, vyžaduje zásah; tiskárna dostupná).
  - Prázdný název nenastane, `PrinterDefinition` ho odmítne, proto ho widget neřeší. Karta se nikdy neskryje (offline, neplatné údaje, chyba).
- **Texty:** stav = názvy stavů tray ikony (`OverlayFormat.StateName`), procento `OverlayFormat.Percent` (nezlomitelná mezera podle jazyka). Žádné nové texty, návrh pro Stav žádné nepřidal. Jméno karty pro čtečku „MK4 dílna: Tiskne, 19 %“ / „MK4 dílna: Připravena“ skládá kód (`": "`, `", "`), pilulka má jméno stavu, pruh jméno procenta.
- **Tečka:** `OverlayStateBrushes` (neměnné štětce) nad konstantami `ColorTrayIconPalette`. Ty jsou kvůli tomu nově veřejné (`Blue`, `Yellow`, `Green`, `Red`, `Amber`, `BadgeGrey`), žádné kopie hex hodnot. Mapování podle návrhu 1.3:
  - tiskne / zaneprázdněna modrá, pozastaveno žlutá, dokončeno zelená, chyba červená,
  - vyžaduje zásah / neplatné údaje `#FB8C00`,
  - připravena / zastaveno `#8A8D96`, nedostupná / neznámý stav / připojování `#9E9E9E`.
- **Šablona:** `DataTemplate` s `x:DataType` ve vlastním `ResourceDictionary` s `x:Class`.
  - Načítá se přes `AvaloniaXamlLoader.Load(this)`, který XAML kompilátor nahradí vygenerovaným kódem (AOT v pořádku). Slovník se vytvoří jednou a funguje i bez platformy Avalonie (App.Tests).
  - Jeden strom prvků pro S/M/L, takže každé `AutomationId` (`CameraOverlayWidget-status-Name/-State/-Percent/-Progress`) existuje jednou. Velikosti řídí styly podle tříd karty: v S se procento přesune vedle stavu (`Grid.Row/Column` ve stylu), pilulka je bez pozadí a název se nekreslí.
  - Výšky podle návrhu: S 46 / 36 bez úlohy, M 126 / 70, L 158 / 84. U L návrh uvádí jen celkovou výšku 158, mezery jsou proto 8 / 10 / 8 (M 6 / 8 / 6).
  - Název se zkracuje uprostřed (`TextTrimming.PrefixCharacterEllipsis`).
- **Oprava sdíleného prvku (T-081):** `OverlayStatusPill` měl text v horizontálním `StackPanel`. Ten dává textu nekonečnou šířku, takže se dlouhý stav nikdy nezkrátil se třemi tečkami. Nově `Grid` `Auto,*`.
- **Výkon:** snapshot beze změny zobrazeného obsahu nealokuje (`StatusWidgetAllocationTests`, kolekce `AllocationSensitive`: 1000 snapshotů = 0 B, žádný post, žádná změna vlastnosti).
- **Testy:**
  - App.Tests `Cameras/Overlay/Widgets/StatusWidgetTests`: registrace, všech 11 stavů + před prvním dotazem, barvy tečky, stav bez úlohy, zaokrouhlení procent, konec úlohy, dlouhý název a jeho opětovné použití, přejmenování, velikost a zvýraznění z rozložení beze změny textů, kultury cs/de/fr, přepnutí jazyka, levné porovnání. K tomu `StatusWidgetAllocationTests`.
  - Headless `StatusWidgetUiTests`: části a výšky v S/M/L s úlohou i bez, karta před prvním dotazem a offline, zvýraznění jen procenta ve všech velikostech a jeho změna za běhu, barva tečky ve všech stavech, `AutomationId` a jména pro čtečku, dlouhý název a všech 8 jazyků ve všech velikostech uvnitř karty (zkrácené místo přetečení).
  - Nové snímky `camera-widget-status-medium.png`, `camera-widget-status-small.png`, `camera-widget-status-large-pl.png` (vizuálně zkontrolované).
  - E2E specifické pro widget žádné (celý průchod je T-093).
- **Zkracování názvu (opraveno v review):** původně se název delší než 40 znaků zkrátil na konci a pak ještě uprostřed podle šířky, takže měl dvě výpustky („Prusa CO…ne w pracowni na piętrze – …“) a ztratil konec, v rozporu s návrhem 1.4 (začátek + „…“ + konec). Nově se zkrátí na 40 znaků uprostřed a začátek má právě 8 znaků (UTF-16, zaokrouhleno nahoru na celý znak) jako ořez `PrefixCharacterEllipsis`, takže ořez podle šířky odstraní nejdřív tuto výpustku a zůstane jediná.
- **Chyba frameworku (opravená v T-083, sloučeno):** změna jen velikosti karty v uloženém nastavení se v otevřeném okně projeví až po změně velikosti okna. Opravu přinesla T-083 (`OverlayAnchorItems` sleduje `Size`); testy tohoto úkolu velikost za běhu nemění.
- Ověřeno na Windows 11: build, testy, aplikace se spustí a běží. Neověřeno: macOS (SF Pro), skutečné DPI 125–200 %, čitelnost na skutečném videu (T-093).

## Review
- 2026-10-25, qa-reviewer (e28beb8, po sloučení develop s T-083 97d15fc):
  - **Dvě výpustky v názvu (opraveno):** viz Poznámky. Testy `NameLongerThan40CharactersAndTheCardShowsOneEllipsisAndItsEnd` (headless, kontroluje skutečně vykreslený řádek) a `OverlayFormatTests` (zkrácení uprostřed, náhradní páry).
  - **Písmo pilulky (chyba T-081, opravená v T-083):** obecné styly `Border.overlayCard TextBlock` přebíjely `TemplateBinding` textu v šabloně `OverlayStatusPill` (M 13 místo 12, L 15 místo 13). Oprava přišla sloučením develop (T-083, styly `/template/ TextBlock`). Nový test `DrawnTextsHaveTheDesignedTypography` kontroluje vykreslené `TextBlock` Stavu ve všech velikostech (pilulka, procento, název). Snímky `camera-widget-status-medium` a `camera-widget-status-large-pl` přegenerované a vizuálně zkontrolované (menší písmo pilulky, název s jednou výpustkou).
  - `OverlayWidgets.CreateRegistry`: po sloučení Stav i Časy (v tomto pořadí), `<remarks>` aktualizované.
- 2026-10-25, qa-reviewer (potvrzující review 3042409):
  - Ověřeno v Avalonia 12.1.4 (dekompilace `TextTrimming`, `TextLeadingPrefixCharacterEllipsis`, `ShapedBuffer.Split`): `PrefixCharacterEllipsis` = prefix 8 **UTF-16 kódových jednotek**, dělení se zaokrouhlí **nahoru** na konec shluku glyfů (HarfBuzz, výchozí úroveň shluků = grafémy); u úzké karty, kam se nevejde ani 8 znaků, se prefix zkrátí a konec se nekreslí (jedna výpustka).
  - **Dvě výpustky při znaku přes 8. pozici (opraveno):** náhradní pár na pozici 7–8 zkrátil začátek na 7, Avalonia ale ponechala 9 („Prusa C…“ + „…“ + konec); kombinující diakritika (e + ◌́) se rozdělila a „é“ ztratilo čárku. Nově se oba řezy dělají na hranicích grafémů (`StringInfo`): začátek názvu se zaokrouhlí nahoru na konec znaku (jako Avalonia), začátek konce názvu vždy nahoru (žádná osiřelá diakritika za „…“). Znak delší než celý limit (desítky kombinujících znaků) se rozřízne, jen ne uvnitř náhradního páru. Testy: `ShortenedNameKeepsTheWholeCharacterAtTheCutOfItsStart` (náhradní pár, diakritika, emoji se ZWJ), `ShortenedNameDoesNotStartItsEndInsideACharacter`, `ShorteningANameWithOneHugeCharacterStaysWithinTheLimit`, headless `NameLongerThan40CharactersAndTheCardShowsOneEllipsisAndItsEnd` rozšířený o náhradní pár a diakritiku (před opravou selhal: 2 výpustky).
  - Mez: ligatury písma přes 8. pozici (shluk glyfů delší než grafém) textová funkce znát nemůže; běžná písma (Inter, Segoe UI, SF Pro) je v latince ve výchozím stavu nepoužívají.
  - `ShortenFileName`: výstup pro náhradní páry nezměněn, nově připnutý přesnými testy (`ShorteningAFileNameKeepsSurrogatePairsWhole`). Změna jen u kombinující diakritiky na řezu (dřív se rozdělila, teď celý znak odpadne se začátkem / koncem).
  - Snímky `camera-widget-status-medium` / `-large-pl` zkontrolované proti 97d15fc: rozdíl jen menší písmo pilulky (T-083) a název s jednou výpustkou a koncem; přegenerování oprávněné.
