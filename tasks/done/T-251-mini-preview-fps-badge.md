---
id: T-251
title: Malý náhled kamery – štítek se skutečným a nastaveným fps v levém horním rohu (volitelný)
status: done
owner: ui-dev
priority: P2
platform: all
depends_on: [T-185]
---

## Cíl
Poznatek z používání (uživatel 2026-11-22): v malém náhledu není vidět, jaké fps platí. Do **levého horního rohu**
obrazu malého náhledu přidat malý štítek se **skutečným i nastaveným fps**, např. „14 / 15 fps“. Štítek jde
**vypnout v Nastavení > Kamera** a ve výchozím stavu je zapnutý (rozhodnutí uživatele 2026-11-22).

Ověřeno při zadání: levý horní roh malého náhledu je volný. Překryvné karty se v malém náhledu nekreslí
(`CameraOverlayViewModel.AreCardsVisible`, rozhodnutí 2026-10-24), tlačítka okna jsou vpravo nahoře
(`Windows/CameraWindow.axaml`) a stav připojení je uprostřed.

## Rozsah
- Skutečné fps: průběžně měřená frekvence snímků, které malý náhled opravdu zobrazí (po omezení T-185), klouzavé okno
  zhruba 1–2 s. Hodnota se nemá viditelně „třást“: zaokrouhlit na celá čísla a obnovovat nejvýš 1× za sekundu. Na UI
  vlákně žádné alokace navíc pro každý snímek.
- Nastavené fps: `AppSettings.MiniPreviewFrameRate`. U profilu Plynulý (bez omezení) místo čísla lokalizovaný text
  (návrh „bez limitu“, např. „24 fps / bez limitu“, přesné znění ověří `ui-designer`).
- Štítek je vidět jen při běžícím obrazu, ne při připojování, chybě nebo bez signálu. Nesmí zachytávat kliknutí
  (přetažení okna dál funguje za obraz) ani zasahovat do úchytů pro změnu velikosti (6 DIP od okraje). Vzhled ladí
  s ostatními prvky přes obraz (`Border.overlay`) a je čitelný při minimální velikosti malého náhledu i na
  high-DPI. Nekoliduje s tlačítky vpravo nahoře ani při nejužší šířce.
- Nastavení: nový přepínač v Nastavení > Kamera u snímkové frekvence malého náhledu (výchozí zapnuto) a nová hodnota
  v `AppSettings`, `SettingsDocument` a `SettingsMapper`. Výchozí hodnota se do souboru nezapisuje, stejně jako
  u ostatních hodnot. Změna se projeví hned v otevřeném okně.
- Přístupnost: štítek má `AutomationProperties.Name` (např. „Snímková frekvence 14 z 15 fps“), ale nemá být živá
  oblast, která by čtečku zahlcovala.
- Texty do všech `Strings.*.resx` (cs, de, en, es, fr, it, pl, sk). Manuály všech jazyků, `CHANGELOG.md` (Unreleased).
  Snímky v `docs/images/<lang>/`, které malý náhled nebo záložku Kamera ukazují, přegenerovat (sada Windows, macOS →
  T-127).
- Návrh vzhledu (velikost, odsazení, průhlednost pozadí) od agenta `ui-designer` před implementací.

## Akceptační kritéria
- [x] Malý náhled ukazuje v levém horním rohu „skutečné / nastavené fps“, u Plynulého lokalizovaný text místo limitu
- [x] Skutečné fps odpovídá zobrazeným snímkům (test s řízenými hodinami / testovacím zdrojem snímků) a obnovuje se nejvýš 1× za s
- [x] Štítek není vidět bez obrazu; neruší přetažení okna, úchyty ani tlačítka; normální okno a celá obrazovka beze změny
- [x] Přepínač v Nastavení > Kamera (výchozí zapnuto) se ukládá a projeví se hned; starý soubor nastavení se načte se zapnutým štítkem
- [x] Texty ve všech jazycích, manuály, CHANGELOG, snímky Windows přegenerované; macOS → T-127 (sada macOS a `docs/images/<lang>/`, které se generují na macOS → přesunuto do T-127, oddíl 1y)
- [x] UI test (headless) rozmístění štítku při minimální velikosti malého náhledu
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné – obsah štítku, umístění a volitelnost rozhodnuty uživatelem 2026-11-22; přesné znění a vzhled navrhl
  `ui-designer`, `docs/design/mini-preview-fps-badge.md`)
- Rozhodnuto uživatelem 2026-11-22: když se štítek vedle tlačítek nevejde, plynule zmizí a vrátí se, až je místo
  (varianta A); po startu obrazu se ukáže hned s pomlčkou místo hodnoty, skutečnou hodnotu doplní první obnovení po 1 s.

## Poznámky k implementaci
- Návrh: `docs/design/mini-preview-fps-badge.md` (ui-designer) včetně rozhodnutí uživatele.
- Nastavení: `AppSettings.ShowMiniPreviewFrameRate` (výchozí `true`), v souboru `showMiniPreviewFrameRate` jen `false`
  (starý soubor = zapnuto), `MonitoredPrintersState`, `SettingsViewModel.ShowMiniFrameRateBadge`, přepínač
  `MiniFrameRateBadge` s nápovědou na záložce Kamera pod popisem profilu.
- Měření: `FrameRateMeter` (App/Cameras) – pevný kruhový buffer 256 časových značek, okno 2 s; rychlost = (n − 1) /
  rozpětí snímků, po zastavení se rozpětí prodlužuje o čas nad průměrný interval, takže hodnota plynule klesne k 0.
  `CameraViewModel.FrameRateBadge.cs`: snímek zobrazený v `PresentFrame` se zapíše jen při viditelném štítku (bez
  alokací – test `ShowingFramesWithTheBadgeOnDoesNotAllocateOnTheUiThread`), periodický časovač `TimeProvider` 1× za s
  obnoví zaokrouhlenou hodnotu; časovač existuje jen při viditelném štítku (malý náhled, aktivní okno, běžící obraz,
  volba zapnutá). Do první obnovy „‒“ (figure dash U+2012, šířka číslice), hodnota pod 10 doplněná U+2007 – šířka
  štítku se nemění (headless test). Plynulý: „24 fps / no limit“.
- Okno: štítek v `CameraWindow.axaml` (Border.overlay, není cíl ukazatele, `AutomationProperties.Name`, bez live
  region), `CameraWindow.FrameRateBadge.cs` po rozvržení porovná štítek s tlačítky (v klidu jen připínáček / REC / TL,
  při najetí / fokusu všechna) a kde chybí 8 DIP, nastaví třídu `cramped` (Opacity 0, přechod 0,15 s). Kvůli REC + TL
  + připínáčku v klidu při 240 DIP je vodorovné odsazení 5 DIP místo navržených 6 (jinak chyběl 1 DIP). Testy měří
  s Inter, které aplikace kreslí na macOS (T-223); Windows kreslí Segoe UI, které je užší (text „‒ / 15 fps“ ≈ 51 DIP
  proti 58 DIP, změřeno přes GDI+), takže tam má štítek rezervu.
- Po code review: konec běhu z hubu bez chyby skryje štítek; obnovení naplánované pro dřívější zobrazení se ignoruje
  (počítadlo zobrazení v `FrameRateBadgeTick`); vnitřní text má `AccessibilityView="Raw"`; čtečka před prvním
  měřením dostane „Frame rate: measuring, limit 15 fps“ (2 nové klíče).
- Texty: 6 klíčů ve všech 8 jazycích; manuály (odstavec v oddílu o malém náhledu + položka v 11.6), README (en/cs),
  CHANGELOG (Unreleased › Added), `docs/architecture.md`.
- Testy: Core `MiniPreviewFrameRateBadgeSettingsTests`; App `FrameRateMeterTests`, `CameraViewModelFrameRateBadgeTests`,
  `MiniFrameRateSettingsTests`; UI `CameraWindowMiniFrameRateBadgeUiTests` (rozmístění při 240 DIP v 8 jazycích s
  limitem i bez, skrytí při najetí s TL + REC, fokus, hit test, stálá šířka, viditelnost podle režimu / stavu /
  nastavení, snímek `camera-mini-fps-badge`), `SettingsWindowCameraTabUiTests`. `CameraHarness.SkipWindowButtonFade`
  vypíná i přechod štítku. Přegenerované Windows reference: `camera-mini-*.png` (štítek navíc), `settings-camera-tab.png`.
