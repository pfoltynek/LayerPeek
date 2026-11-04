---
id: T-161
title: Timelapse – UI (nastavení, tray menu, okno kamery, štítek Experimentální, notifikace) a E2E
status: done
owner: ui-designer + ui-dev
priority: P3
platform: all
depends_on: [T-160, T-146, T-148]
---

> **Návrh ze spiku T-149 (2026-11-03), rozsah upřesněný rozhodnutím uživatele 2026-11-04** (otázky 2–6 a 10 v T-149).
> Implementuje se až po dashcamu T-148.

## Cíl
Ovládání timelapse v aplikaci po vzoru UI nahrávání (T-146): volby v nastavení, ruční start / stop z tray menu a z okna
kamery, indikátor pořizování v okně kamery, otevření posledního timelapse a notifikace chyb. Celá funkce je označená
jako **experimentální**.

## Rozsah
- **ui-designer:** umístění voleb (záložka Kamera u tiskárny / globální sekce nahrávání), ovládací prvek intervalu
  (pevné volby 10 / 30 / 60 s, nebo volné číslo 5–600 s – vybere designer, výchozí 30 s), podoba štítku
  „Experimentální“, indikátor timelapse v okně kamery a v malém náhledu vedle indikátoru REC (T-146), délka textů
  v překladech.
- **ui-dev:**
  - nastavení: automatický timelapse u tiskárny (výchozí vypnuto) a interval (výchozí 30 s). Výstup je pevně 30 fps,
    bez volby,
  - **štítek „Experimentální“** (rozhodnutí uživatele 2026-11-04) u každé volby timelapse v nastavení a všude, kde
    se timelapse zapíná (položka tray menu, ovládání v okně kamery). Nový lokalizovaný text ve všech `Strings.*.resx`,
  - tray menu: ruční start / stop timelapse u tiskárny, indikace „pořizuje se timelapse“, „Přehrát poslední
    timelapse“, „Otevřít složku“ (sdílená s nahrávkami),
  - okno kamery: zapnutí / vypnutí timelapse (tlačítko nebo položka kontextového menu podle návrhu) a **indikátor
    pořizování timelapse** po vzoru indikátoru REC (okno kamery i malý náhled),
  - notifikace chyb ze služby (T-160) přes `INotificationService`, texty ve všech `Strings.*.resx`,
  - E2E / headless UI testy po vzoru T-146.
- Volbu spouštěče „po vrstvě“ přidá až T-159.

## Akceptační kritéria
- [x] Volby se uloží a načtou, chybné hodnoty se odmítnou jako u ostatních polí nastavení (T-112).
- [x] Štítek „Experimentální“ je u všech voleb timelapse v nastavení, u položky tray menu i u ovládání v okně kamery;
      text je ve všech jazycích (headless UI test, okno nastavení bez posuvníku i s nejdelším překladem).
- [x] Ruční start / stop z tray menu i z okna kamery funguje a obě místa ukazují stejný stav (test). Ruční ověření
      na skutečné obrazovce → přesunuto do T-125 a T-127
- [x] Okno kamery i malý náhled ukazují indikátor, když se timelapse pořizuje, a skryjí ho po skončení (headless UI test, E2E).
      E2E běží jen na Windows → přesunuto do T-125
- [x] Tray menu ukazuje stav a otevře poslední timelapse v přehrávači systému. Ruční ověření se skutečným přehrávačem
      → přesunuto do T-125 a T-127
- [x] Notifikace chyb lokalizované ve všech jazycích.
- [x] Headless UI testy a E2E prochází; snímky UI přegenerované, pokud se mění. (macOS: headless testy a sada snímků
      macOS; E2E a sada snímků Windows → přesunuto do T-125)
- [x] `dotnet build` a `dotnet test` bez chyb a varování. (macOS; Windows → přesunuto do T-125)

## Otevřené otázky
- (žádné blokující – otázky 2–6 v T-149 rozhodnuté 2026-11-04)

**K potvrzení uživatelem (neblokující, implementované je konzervativní výchozí chování, změna je malá):**
1. **Interval u každé tiskárny?** (z T-160) Interval je globální (`recording.timelapseIntervalSeconds`, záložka Nahrávání),
   automatický timelapse je volba u tiskárny (`camera.timelapsePrints`). Výchozí bez rozhodnutí: globální.
2. **Zhuštěná sekce dashcamu na záložce Nahrávání.** Sekce timelapse se na záložku jinak nevešla (záložka byla plná,
   nejplnější stav měl rezervu 5 DIP, T-033). Délka dashcamu je proto v řádku nadpisu, čtyři spouštěče ve dvou sloupcích
   a popisek „Ukládat automaticky“ a nápověda „Ručně jde uložit vždy …“ už nemají vlastní řádek (jsou přístupný název
   sloupců a tip / nápověda pro čtečky každého spouštěče); nápověda intervalu timelapse je jen tip. Francouzská nápověda
   dashcamu a limitu nahrávek je kratší. Vyhovuje to, nebo raději jiné řešení (např. posuvník na záložce Nahrávání, větší
   okno, nebo detekce / timelapse jinam)? Snímek `settings-recording.png`.
3. **Popisek adresy kamery vlevo od pole** (podzáložka Kamera), aby se vešel řádek „Pořizovat timelapse tisku“. Vyhovuje?
4. **Tooltip ikony v tray** neříká „pořizuje se timelapse“ (jen položka menu „Zastavit timelapse“ a „TL“ v okně kamery);
   Windows má limit 127 znaků a tooltip už nese „Nahrává se“. Přidat „Timelapse“ i do tooltipu?
5. **„TL“** jako indikátor v okně kamery (vedle „REC“, ve všech jazycích stejně jako „REC“). Vyhovuje, nebo jen ikona /
   jiný text?
6. **Bez notifikace o hotovém timelapse** (jen chyby, jako nahrávky – T-145 8). Stačí?

## Podklady z T-160
- Služba: `CameraRecordingService.StartTimelapse(id)` / `StopTimelapse(id)` (vrací `false`, když to nejde vůbec zkusit),
  `TimelapseAvailability` (bez muxeru zakázat s důvodem), stav `GetStatus(id).Timelapse` (`CameraTimelapseStatus`:
  `Activity`, `IsActive`, `Mode`, `IsCapturing` – `false` během `Paused` / `Attention`, `FrameCount`, `Duration`,
  `LastFile`, `LastError`, `TimelapsePrints`, `Interval`); `Changed` přijde i při pauze / obnovení.
- Chyby timelapse chodí stejnou událostí `Failed` s `CameraRecordingError.IsTimelapse = true` (`Mode` = ruční /
  automatický). `RecordingNotificationObserver` je zatím ukazuje jako chybu nahrávání – T-161 je musí odlišit (vlastní texty).
- „Přehrát poslední nahrávku“ timelapse nevybere (`CameraRecordingStatus.LastFile` je jen nahrávka / dashcam);
  „Přehrát poslední timelapse“ = `CameraTimelapseStatus.LastFile`.
- Editor nastavení zatím hodnoty timelapse jen přenáší beze změny (`PrinterEditorViewModel._timelapsePrints`,
  `SettingsViewModel._timelapseIntervalSeconds`); T-161 z nich udělá ovládací prvky.

## Poznámky k implementaci
Implementováno 2026-11-04 na macOS arm64 (ui-dev, zároveň v roli ui-designer – návrh `docs/design/timelapse-ui.md`),
větev `feature/T-161-timelapse-ui`.

**Návrh a chování:**
- **Štítek „Experimentální“** (`SettingsExperimental`, tip `SettingsExperimentalHint`): v nastavení jantarový štítek
  (`Border.badge`, barvy upozornění, `ExperimentalBrush` pro světlý i tmavý motiv) vpravo od nadpisu sekce „Timelapse“
  (záložka Nahrávání) a od „Pořizovat timelapse tisku“ (podzáložka Kamera). Tray menu a okno kamery ho mají v textu
  („Spustit / Zastavit timelapse (experimentální)“ – položka menu, tip a přístupný název tlačítka), protože nativní menu
  ani řada tlačítek malého náhledu štítek nepojmou.
- **Nastavení:** „Pořizovat timelapse tisku“ (`PrinterEditorViewModel.TimelapsePrints`, výchozí vypnuto) ve vlastním řádku
  pod nahráváním a dashcamem, zakázané (hodnota zůstává) bez kamery nebo bez rekordéru timelapse
  (`ICameraRecordings.IsTimelapseAvailable`), důvod / popis je tip a nápověda. Interval
  (`SettingsViewModel.TimelapseIntervalSeconds`) jako `NumericUpDown` celých sekund 5–600, krok 5, v řádku nadpisu sekce
  „Timelapse“ – volné číslo, protože služba přijme celý rozsah a ostatní čísla záložky jsou také volná čísla s ověřením;
  pevné volby by zakázaly krátké i dlouhé intervaly. Chybná hodnota (mimo rozsah, desetinná, prázdná): text
  „Zadejte celé číslo od 5 do 600.“ pod řádkem, uložení zablokované, přepnutí na záložku Nahrávání a fokus poli
  (`SettingsFieldIds.SettingsTimelapseInterval`, T-112). Nápověda (bez snímků během pauzy, 30 fps) je tip a nápověda pro
  čtečky. Bez rekordéru pole zakázané a místo chyby důvod. Výstup pevně 30 fps, bez volby.
- **Místo na záložce Nahrávání a podzáložce Kamera (T-033):** obě byly plné (rezerva 5 DIP, resp. −18 DIP s novým řádkem).
  Sekce dashcamu (T-148) je proto zhuštěná (délka v řádku nadpisu, nápověda přes celou šířku, spouštěče 2 × 2, „Ukládat
  automaticky“ jen jako přístupný název sloupců, „Ručně jde uložit vždy …“ jako tip spouštěčů), francouzská nápověda
  dashcamu a limitu je kratší, na podzáložce Kamera je popisek adresy vlevo od pole. Nejplnější stav záložky Nahrávání
  (všechny chyby, fr / it / es) má 419 z 422 DIP. Otázky 2 a 3 výše.
- **Tray menu** (`TrayMenuViewModel.AddRecordingEntries`): pod „Nahrávat“ (a „Uložit posledních N min“) položka „Spustit
  timelapse (experimentální)“ / „Zastavit timelapse (experimentální)“ se stejnou přístupovou klávesou, pod „Přehrát
  poslední nahrávku“ „Přehrát poslední timelapse“ (zakázané s tipem „Tiskárna zatím nemá žádný timelapse.“), složka
  sdílená („Otevřít složku s nahrávkami“). Pravidla sdílí s oknem kamery `TimelapseControlState` (Start jen v klidu, Stop
  během běhu, během `Starting` / `Stopping` a bez rekordéru zakázané s důvodem; během pauzy tisku Stop s tipem
  „Během pauzy tisku se snímky nepořizují.“). Klik jedná podle toho, co položka ukazovala. Změnu hlásí stejná událost
  `Changed` jako nahrávání, aktualizuje se jen podmenu dané tiskárny na místě. „Přehrát poslední timelapse“ →
  `ICameraRecordings.PlayLastTimelapseAsync` (`CameraTimelapseStatus.LastFile` přes `IFileLauncher`, jen existující `.mp4`).
- **Okno kamery** (`CameraViewModel.Timelapse.cs`, `CameraWindow.axaml`): tlačítko se stopkami před tlačítkem nahrávání
  ve všech režimech; v klidu skryté jako ostatní, během timelapse jantarové stopky s „TL“ (`CameraTimelapseIndicator`)
  trvale viditelné (normální okno, malý náhled, celá obrazovka), během pauzy tisku ztlumené (60 %); pod ukazatelem /
  s fokusem čtverec Stop. Statické, bez časovače; stav se čte jednou na `Changed` této tiskárny společně s nahráváním
  a dashcamem (jedno `GetStatus` pro všechny tři ovládací prvky). Klik myší vrátí fokus obrázku jako u REC.
- **Notifikace:** `RecordingNotification.IsTimelapse` (Core) z `CameraRecordingError.IsTimelapse`; `RecordingNotificationText`
  dá „Timelapse selhal“ a vlastní důvody tam, kde důvod nahrávky mluví o nahrávce (bez muxeru, zápis souboru, limit),
  ostatní důvody společné. Windows toast i macOS stejně, klik otevře záložku Nahrávání.
- **Texty:** 22 nových klíčů ve všech 8 jazycích (`MenuStartTimelapse`, `MenuStopTimelapse`, `MenuPlayLastTimelapse`,
  `MenuNoTimelapseYet`, `Timelapse*`, `CameraTimelapseIndicator`, `NotificationTimelapse*`, `SettingsTimelapse*`,
  `SettingsExperimental*`); přístupové klávesy podmenu unikátní ve všech jazycích (test). Upravené fr
  `SettingsDashcamMinutesHint` a `SettingsRecordingLimitHint` (kratší).

**Testy:** App.Tests `SettingsTimelapseTests` (výchozí hodnoty, uložení, načtení, neplatný interval → záložka a fokus,
bez kamery, bez rekordéru), `TrayMenuRecordingTests` (položky, stavy, pauza, kliky, přístupové klávesy ve všech jazycích
s 10 položkami), `RecordingNotificationTests` (timelapse jako timelapse, vlastní texty ve všech jazycích),
`CameraRecordingsTests.PlayLastTimelapse…`; UiTests `CameraWindowTimelapseUiTests` (12: klik, indikátor bez ukazatele,
pauza, `Starting` / `Stopping`, bez rekordéru, jiné tiskárny bez čtení stavu, pořadí ve všech režimech, čtverec Stop,
jazyk, 3 snímky), `SettingsWindowTimelapseUiTests` (štítek a volby ve všech jazycích, chyba a fokus, bez rekordéru),
`TrayMenuTests.TheTrayMenuAndTheCameraWindowShowTheSameTimelapse` (oba ukazují stejný stav),
`TrayMenuTests.RecordingItems…` s timelapse, `SettingsWindowLayoutTests.RecordingTabFitsInEveryLanguage` s chybou
intervalu a bez rekordéru. E2E (Windows, na Macu jen přeloženo): nový `CameraTimelapseTests` (ruční z tray menu → soubor,
„TL“ v okně kamery i malém náhledu, stop z okna kamery; automatický timelapse tisku), očekávané položky podmenu v
`TrayMenuTests`, `TrayMenuDetailTests`, `CameraTaskbarAndModeSwitchTests`; `TestPrinter.TimelapsePrints`.
Snímky macOS: nové `camera-timelapse.png`, `camera-mini-timelapse.png`, `camera-mini-timelapse-buttons.png`; změněné
`camera-mini-recording-buttons.png`, `settings-recording.png`, `settings-camera.png`, `settings-camera-preview.png`,
`settings-camera-preview-4x3-de.png`, `settings-camera-notice.png`. Sada Windows → T-125.

**Výsledek (macOS arm64):** `dotnet build` 0 upozornění, 0 chyb; `dotnet test` celkem 9533, neúspěšné 0, úspěšné 9333,
přeskočeno 200. `dotnet publish src/LayerPeek.App -c Release -r osx-arm64` bez varování trim / AOT (jen dosavadní hlášky
nástrojů Xcode: dsymutil, `ModuleCache`, `xcodebuild`). Publikovaná aplikace se spustila a běžela (15 s, pak ukončena);
sezení agenta nemělo aktivní displej, takže UI (menu bar, okna) se nespustilo (T-099) – menu bar, okno kamery,
notifikace a přehrávač na skutečné obrazovce neověřeny → T-127. Žádný nový balíček.

Uzavřeno 2026-11-04: ruční ověření na skutečné obrazovce (macOS → T-127, Windows → T-125), E2E, testy a sada snímků
Windows a AOT `win-x64` přesunuty do T-125 (rozhodnutí uživatele, sběrné úkoly).
