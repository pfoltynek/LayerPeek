---
id: T-251
title: Malý náhled kamery – štítek se skutečným a nastaveným fps v levém horním rohu (volitelný)
status: todo
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
- [ ] Malý náhled ukazuje v levém horním rohu „skutečné / nastavené fps“, u Plynulého lokalizovaný text místo limitu
- [ ] Skutečné fps odpovídá zobrazeným snímkům (test s řízenými hodinami / testovacím zdrojem snímků) a obnovuje se nejvýš 1× za s
- [ ] Štítek není vidět bez obrazu; neruší přetažení okna, úchyty ani tlačítka; normální okno a celá obrazovka beze změny
- [ ] Přepínač v Nastavení > Kamera (výchozí zapnuto) se ukládá a projeví se hned; starý soubor nastavení se načte se zapnutým štítkem
- [ ] Texty ve všech jazycích, manuály, CHANGELOG, snímky Windows přegenerované; macOS → T-127
- [ ] UI test (headless) rozmístění štítku při minimální velikosti malého náhledu
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné – obsah štítku, umístění a volitelnost rozhodnuty uživatelem 2026-11-22; přesné znění a vzhled navrhne
  `ui-designer`)

## Poznámky k implementaci
(vyplní agent po dokončení)
