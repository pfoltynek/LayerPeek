---
id: T-164
title: Timelapse z hotové nahrávky (varianta F)
status: done
owner: core-dev + ui-dev
priority: P3
platform: all
depends_on: [T-158, T-161]
---

> **Ze spiku T-149, rozhodnutí uživatele 2026-11-04 (otázka 8):** dodatečné vytvoření timelapse z hotové nahrávky
> je samostatný úkol s nízkou prioritou. Podrobnosti: `docs/timelapse-spike.md`, varianta F.

## Cíl
Uživatel může z hotové nahrávky tisku (T-144 / T-145) dodatečně vytvořit timelapse, i když během tisku timelapse
nepořizoval. Stejný princip jako varianta D (T-158): z MP4 nahrávky se vyberou klíčové snímky a přebalí se s novými
razítky (30 fps) bez dekódování a bez enkodéru. Funkce patří k timelapse, a je proto také označená jako experimentální.

## Rozsah
- **Camera.FFmpeg:** čtení paketů z MP4 nahrávky (demuxer `mov` už je v buildu pro macOS, BtbN ho má) za abstrakcí
  v Core (Core nezná FFmpeg). Pakety jdou do výběru klíčových snímků a zapisovače z T-158 (sdílená logika, žádná kopie).
- **Core:** výběr snímků po čase podle intervalu timelapse (T-160, výchozí 30 s) v čase nahrávky. Nahrávka rozdělená
  do souborů `-2`, `-3`… (výpadek spojení, T-144) se zpracuje jako jeden celek, pokud to jde (stejné SPS/PPS).
  Zpracování na pozadí s průběhem a zrušením (`CancellationToken`), nízká priorita, neblokuje sledování tiskáren.
  Prototyp T-149: 5 min nahrávky → 0,10 s CPU, 10 h ≈ 12 s CPU + čtení ~4,9 GB.
- **Výstup:** do stejné podsložky tiskárny jako nahrávky, název podle vzoru T-160 (`…_timelapse.mp4`), počítá se do
  společného limitu a rotace. Rotace nesmí smazat nahrávku, ze které se právě timelapse vytváří.
- **UI:** akce „Vytvořit timelapse“ u nahrávky (umístění – otázka 1), průběh, zrušení, notifikace dokončení / chyby,
  štítek „Experimentální“ (T-161), texty ve všech `Strings.*.resx`.

## Akceptační kritéria
- [x] Z nahrávky (fixture `buddy3d.h264` přebalená do MP4 a nahrávka ze simulátoru) vznikne timelapse jen z klíčových
      snímků; `Mp4Probe`: jedna stopa H.264, všechny snímky klíčové, DTS přísně roste, 30 fps (test).
- [x] Snímky jsou bitově shodné s klíčovými snímky nahrávky (test jako v T-158).
- [x] Zrušení uprostřed zpracování nezanechá poškozený soubor (smaže nebo dokončí podle návrhu), rotace nesmaže
      zpracovávanou nahrávku (testy).
- [x] Akce v UI, průběh, notifikace a štítek „Experimentální“ (headless UI test), texty ve všech jazycích. Ruční ověření
      dialogu, průběhu a notifikací na obou OS → přesunuto do T-125 a T-127.
- [x] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish bez varování trim / AOT. Část Windows (testy, E2E,
      publish `win-x64`) → přesunuto do T-125.

## Otevřené otázky
Všechny zodpovězené (rozhodnutí uživatele 2026-11-04):
1. **Kde je akce „Vytvořit timelapse“:** v tray menu → sekce nahrávání tiskárny: „Vytvořit timelapse z nahrávky…
   (experimentální)“ vedle „Exportovat timelapse do JPEG…“. Otevře stejný systémový dialog jako T-165 (složka nahrávek,
   podsložka tiskárny, když existuje), filtrovaný na běžné nahrávky MP4. Soubor, který není nahrávka, se odmítne jako
   v T-165.
2. **Interval:** stejný jako u živého timelapse (nastavení T-160, výchozí 30 s), žádný další dialog. Výstup 30 fps jako
   živý timelapse.
3. **Automaticky po každé nahrávce:** ne, jen ručně.
4. **Výstup:** vedle nahrávky, název ve stylu timelapse, který pozná „Přehrát poslední timelapse“ i export do JPEG. Nesmí
   kolidovat s živým timelapse téže úlohy (řešení viz poznámky). Počítá se do limitu a rotace.

## Poznámky k implementaci
- **Core:** `Cameras/Timelapse/CameraTimelapseFromRecordingMaker` se stavem `CameraTimelapseFromRecordingStatus` (snímky,
  procento) a výsledkem `CameraTimelapseFromRecordingResult` (výstupní soubory, snímky, problém). Výčet problémů je
  společný s T-165 (`CameraTimelapseExportProblem`, nově `NotARecording`). Nahrávka se čte přes `ICameraFeedFactory`
  (`file://`) **jen jako pakety, dekodér se neotevře** (test kontroluje `DecoderOpened`). Klíčový snímek se zapíše beze
  změny stávajícím `ICameraRecordingWriter` (T-144 / T-158) s razítky `RecordingClock.AtFrameRate` (30 fps). Camera.FFmpeg
  se neměnilo: demuxer `mov` pro `file://` je povolený od T-165. Mimo úlohu neexistuje vlákno, úloha ani buffer.
- **Výběr snímků:** čas nahrávky = DTS (jinak PTS) paketu od prvního paketu segmentu. Vezme se první klíčový snímek, jehož
  čas dosáhl dalšího kroku intervalu; další krok je `(⌊t / I⌋ + 1) · I`. Kroky tak zůstávají na jedné mřížce a jeden krok
  nikdy nedá dva snímky. Buddy3D má klíčový snímek každých ~2,2 s (spike), snímek je tedy 0–2,4 s po kroku, jako u živého
  timelapse. Když jsou klíčové snímky řidší než interval, dá každý jeden snímek a skutečný interval je rozestup klíčových
  snímků. Paket bez razítka se přeskočí.
- **Segmenty `-2`, `-3`… (T-144):** `FindSegments` najde k vybranému souboru (jakémukoli segmentu) ostatní soubory téže
  nahrávky ve složce a seřadí je podle čísla. Chybějící čísla přeskočí. `Název-2` je segment `Název`, ledaže existují soubory
  `Název-2-N` (pak jde o titul končící na `-2`). Číslo se nikdy nehledá uvnitř časového razítka: sekundy nahrávky bez titulu
  (`…_16-00-05`) nejsou číslo segmentu a nahrávky dvě sekundy po sobě se nespojí. Čas dalšího segmentu začíná na konci
  předchozího, výpadek mezi nimi není známý a nepočítá se. Má-li segment jiné parametry streamu (extradata = avcC se SPS/PPS,
  kodek, rozměr), jeho snímky začnou další výstupní soubor `-2` (jako živý timelapse). Při stejných parametrech je výstup
  jeden soubor (test se simulátorem s výpadkem kamery).
- **Název výstupu (otázka 4):** `<název nahrávky bez přípony a bez -N>_recording_timelapse.mp4`
  (`CameraRecordingFolders.RecordingTimelapseFileName`), např. `2026-11-04_14-05-09_Benchy_recording_timelapse.mp4`. Splňuje
  `IsTimelapseFileName`, takže ho pozná „Přehrát poslední timelapse“ (přes rotaci, `NewestTimelapseByFolder`), export do JPEG
  i rotace. V rotaci se řadí hned za svou nahrávku. S živým timelapse téže úlohy (`<začátek timelapse>_<titul>_timelapse.mp4`)
  kolidovat nemůže, protože ten nemá `_recording` před `_timelapse`. Obsazený název (dřívější timelapse téže nahrávky) dostane
  `-2`, `-3`…, existující soubor se nikdy nepřepíše. „Přehrát poslední timelapse“ ho přehraje, když je podle času v názvu
  (= začátek nahrávky) nejnovější timelapse tiskárny a v tomto běhu neskončil novější živý timelapse.
- **Bez napůl zapsaného souboru:** výstup se píše jako `.<název>.partial` (`CameraRecordingFolders.UnfinishedFileName`,
  stejný vzor jako složka exportu T-165, na macOS skrytý). Před zápisem se rezervuje prázdný (smlouva zapisovače), pozůstatek
  po pádu téže úlohy se tím nahradí. Po posledním paketu se soubor dokončí a přejmenuje. Zrušení, chyba zápisu i chyba
  dokončení dočasný soubor zavřou a smažou. `CameraRecordingStorage.FindRecordings` hlásí `.…_timelapse….partial` v označené
  složce tiskárny jako `IsUnfinished`: nepočítá se do limitu a rotace ho smaže jako pozůstatek po 10 min (pravidlo T-165).
  Zrušení přijde do úlohy u dalšího paketu (demuxer ho kontroluje sám) nebo u dalšího zapisovaného snímku. Zrušení až po
  posledním paketu už úlohu nezastaví, dokončí ji („smaže nebo dokončí“).
- **Rotace:** po celou úlohu drží `ICameraRecordingFileGuard.Hold` (T-165, `CameraRecordingService`) všechny segmenty
  nahrávky a každý dočasný soubor mimo rotaci. Uvolnění vyžádá rotaci, takže se nový timelapse hned započítá (test se
  skutečnou službou: nahrávka nad limitem zůstane, dokud se čte, a po konci úlohy ji rotace smaže).
- **Poškozená nahrávka:** výjimka demuxeru uprostřed segmentu ukončí jen ten segment, jeho dřívější snímky zůstanou a další
  segmenty pokračují. Žádný snímek → `Unreadable`, bez pozůstatků. Změřeno s FFmpeg macOS buildu: nahrávka useknutá uprostřed
  dat fragmentu (běžný pád: zapisovač posílá každý fragment celý) dá snímky před řezem (22 z 39, řez prochází klíčovým snímkem,
  ten se nepoužije, všechny snímky výstupu se dekódují bez chyby). Nahrávka useknutá uvnitř hlavičky fragmentu (`moof`)
  demuxer `mov` při otevření odmítne celou („trun sample count … exceeds“), úloha skončí `Unreadable` bez pozůstatků. Opravit
  by to šlo jen vlastním čtením struktury MP4 (mimo rozsah; hlavička má ~200–300 B z ~200 kB fragmentu, takový řez je vzácný).
  Náhodná data a prázdný soubor → `Unreadable`. Nic z toho nevyhodí výjimku ven.
- **Co je „nahrávka“:** `IsRecordingFileName` a zároveň ne `IsTimelapseFileName`; jinak `NotARecording` bez čtení souboru.
  Na macOS filtruje `NSOpenPanel` jen podle přípony. Uložení dashcamu (T-148) jsou nahrávky, timelapse z nich jde vytvořit
  (je krátký).
- **Současný běh (zadání):** export do JPEG a timelapse z nahrávky smí běžet současně. Každý má vlastní jednu úlohu
  a vlastní položku menu se zrušením. Nejjednodušší rozumná varianta: oba jsou krátké a nezávislé (jiný zdroj, jiný výstup),
  společné čekání by potřebovalo frontu. Otevřený může být jen jeden dialog najednou pro obě akce (`CameraRecordings.PickAsync`
  se sdíleným příznakem). `Start` při běžící úloze téhož druhu vrátí `false`, v UI k tomu nedojde (místo položky je „Zrušit…“).
- **Sdílení s T-165 místo kopie:** stejný dialog (`IRecordingFilePicker` / `RecordingFilePickRequest`, výběr počáteční složky
  vytažený do `PickAsync`), `ICameraRecordingFileGuard.Hold`, `CameraTimelapseJpegExporter.FileUri`, výčet problémů, celá cesta
  notifikací (`TimelapseExportNotificationObserver` odebírá i tuto úlohu, `TimelapseExportNotification` má nové `Kind`
  s výchozí hodnotou `Jpeg`, `INotificationService.ShowTimelapseExportFinished` beze změny, toast i macOS) a událost
  `ICameraRecordings.TimelapseExportChanged` pro obě úlohy (menu se překreslí stejně). Životní cyklus úlohy (`Start` /
  `Cancel` / `Dispose` / `Status`) má každá třída vlastní: práce i průběh se liší (snímky proti bajtům). Společná základní
  třída by znamenala přepsat hotový a zrevidovaný exportér T-165, a kód by tím jednodušší nebyl.
- **Nízká priorita, neblokuje sledování:** práce běží na thread poolu a na vlastním vlákně demuxeru (na pozadí). Na CPU je
  zanedbatelná (viz výkon) a omezuje ji hlavně čtení z disku. Prioritu vlákna nemění: vlákno patří feedu a sdílí ho
  inline pokračování.
- **Výkon (Mac mini M2 Pro, Debug, fixture přebalená do MP4 ~23 fps, 1280×720):** 5 min nahrávky (308 s, 35,7 MB, soubor
  v cache) → 11 snímků za **19–30 ms** (1,2–1,9 GB/s; spike T-149: 0,10 s CPU). Smyčka paketů **nealokuje nic na paket ani
  na zapsaný snímek** (test: 10 min paketů, 300 snímků, 0 B). Odhad pro **1 h** nahrávky Buddy3D (~490 MB/h, T-144):
  ~0,3–0,4 s z cache. Z disku rozhoduje čtení: SSD ~0,2–0,5 s, HDD (~150 MB/s) ~3–4 s, síťový disk 1 Gbit/s ~4–5 s. Výstup
  ~65 kB na snímek, při 30 s 120 snímků ≈ 8 MB. Paměť během úlohy: buffer paketu feedu (roste na největší klíčový snímek,
  ~100–200 kB), index demuxeru `mov` (~24 B na vzorek, 1 h ≈ 83 000 vzorků ≈ 2 MB), fragment muxeru (~1 snímek). Celkem
  pod ~5 MB, v klidu nic.
- **UI:** položka „Vytvořit timelapse z nahrávky… (experimentální)“ za „Exportovat timelapse do JPEG…“ v podmenu každé tiskárny
  s kamerou (`TrayMenuCommand.CreateTimelapseFromRecording` / `CancelTimelapseFromRecording`,
  `TimelapseFromRecordingControlState`). Během úlohy je u všech tiskáren místo ní „Zrušit timelapse z nahrávky (42 %)“ (stejná
  klávesa) s tipem „Vytváří se timelapse z <soubor>“. Bez muxeru MP4, dialogu nebo úlohy je položka zakázaná s důvodem.
  `ICameraRecordings`: `IsTimelapseFromRecordingAvailable`, `TimelapseFromRecording`, `CreateTimelapseFromRecordingAsync`,
  `CancelTimelapseFromRecording`. Interval čte `CameraRecordingService.TimelapseInterval` (nová vlastnost, nastavení běhu).
  Filtr dialogu je `*.mp4`, titulek „Vyberte nahrávku pro vytvoření timelapse (experimentální)“. Klávesy: V (cs, sk),
  M (en, es), T (de „ers_tellen“), R (fr „C_réer“), D (it), U (pl). Unikátnost v nejdelším podmenu ověřuje test pro všech 8
  jazyků.
- **Notifikace:** „Timelapse z nahrávky vytvořen“ + „Snímků: N, vedle nahrávky.“, nebo „Vytvoření timelapse z nahrávky selhalo“
  + důvod (není nahrávka, nejde přečíst, chybí knihovna videa, do složky nejde zapisovat, plný disk, jiná chyba). Zrušení se
  nehlásí. Zvuk jen se „všemi upozorněními“. Bez argumentu spuštění (jako T-165).
- **Texty:** 13 nových řetězců ve všech 8 `Strings.*.resx` (en s komentáři), procento s pevnou mezerou jako u T-165.
- **Testy:** Core – `CameraTimelapseFromRecordingMakerTests` (výběr a razítka 30 fps, řídké klíčové snímky, razítka
  nezačínající nulou, tři segmenty s vybraným prostředním a změnou parametrů → dva soubory, hledání segmentů včetně mezery,
  titulu `-2` a sekund bez titulu, poškozený segment a pokračování dalším, nečitelný soubor / bez klíčových snímků / bez razítek,
  `NotARecording` bez čtení, chybějící soubor, bez muxeru, bez knihovny, zrušení bez pozůstatků a jedna úloha najednou, dispose,
  obsazený název a pozůstatek po pádu, chyby zápisu / dokončení / vytvoření, nezapisovatelná složka, průběh po procentech
  a odběratel s výjimkou, cesty s `#`, `%`, diakritikou, argumenty), `TimelapseFromRecordingFolderTests` (názvy, nedokončený
  soubor, limit a rotace, nejnovější timelapse, skutečná služba drží nahrávku), `TimelapseFromRecordingAllocationTests`.
  Camera.FFmpeg – `FFmpegTimelapseFromRecordingTests` (fixture 20× za sebou jako nahrávka: `Mp4Probe` a bitová shoda paketů
  i dekódovaných snímků; nahrávka simulátoru s výpadkem → jeden timelapse z obou segmentů; řez daty fragmentu; řez hlavičkou
  fragmentu; náhodná data a prázdný soubor; zrušení na vlákně demuxeru; 5 min za zlomek sekundy). App –
  `CameraRecordingsTimelapseFromRecordingTests` (dialog, filtr, interval z nastavení, jeden dialog s exportem JPEG, žádný
  dialog během úlohy, prázdná odpověď, dostupnost, událost), `TimelapseExportNotificationTests` (nový druh, texty ve všech
  jazycích, toast, macOS), `TrayMenuRecordingTests` (pořadí, zrušení s procentem u všech tiskáren, zakázaná položka, klávesy
  ve všech jazycích). UiTests – `TrayMenuTests.TheTimelapseFromRecordingItemStartsItAndShowsItsProgressInPlace` (nativní menu,
  štítek). E2E (jen Windows) `TrayMenuDetailTests`, `TrayMenuTests`, `CameraTaskbarAndModeSwitchTests` upravené o novou
  položku → spustí se v T-125.
- **Ověření:** `dotnet build` 0 varování, 0 chyb. `dotnet test`: celkem 9705, neúspěšné 0, úspěšné 9504, přeskočeno 201 (po opravě z revize) (E2E a testy jen pro Windows). `dotnet publish src/LayerPeek.App -c Release -r
  osx-arm64` prošel bez varování trim / AOT (žádné `IL*`), ve výpisu jsou jen dosavadní varování nativního linkeru k ladicím
  informacím (`dsymutil`, modulová cache Swiftu).
- **Oprava z revize (qa-reviewer):** sdílený `CameraTimelapseJpegExporter.FileUri` (T-165) neuměl cestu, která obsahuje
  `#` a zároveň platnou sekvenci `%XX` (např. složka tiskárny „Tiskárna #1 %20“, titul „Benchy #2 %41“): vrátil `null`,
  T-165 pak skončil `Unreadable` a tady se segment přeskočil. Nově se jako třetí pokus sestaví adresa `file://`
  s každým segmentem cesty escapovaným zvlášť (i `%` a `#`, písmeno disku a hostitel UNC beze změny) a stejně jako dřív
  se ověří, že `LocalPath` vrátí přesně původní cestu. Testy: `CameraTimelapseJpegExporterTests.TheFileAddressGivesThePathBackAlsoWithAHashAndAnEscapeSequence`
  a nový řádek `AFileWithSpecialCharactersInItsPathIsRead`. Varianta Windows (disk, UNC) se ověří s testy v T-125.
- Uzavřeno 2026-11-04: zbývající ruční ověření přesunuto do T-125 (Windows: testy, E2E, publish, dialog a toast) a T-127
  (macOS: `NSOpenPanel`, průběh v otevřeném menu, notifikace).
