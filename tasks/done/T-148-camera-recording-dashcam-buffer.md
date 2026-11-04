---
id: T-148
title: Nahrávání kamery fáze 2 – „dashcam“ (kruhový buffer posledních minut, uložení při chybě tisku)
status: done
owner: core-dev + ui-dev
priority: P3
platform: all
depends_on: [T-145, T-146]
---

## Cíl
Místo nahrávání celého tisku držet v paměti (nebo v dočasných segmentech na disku) jen **posledních N minut** streamu a uložit je jako nahrávku, když se stane něco zajímavého – hlavně při chybě tisku. Šetří disk a přitom zachytí, co se stalo těsně před problémem (nápad 1, fáze 2).

## Rozsah
- **core-dev:** kruhový buffer zakódovaných paketů (bez překódování, stejný princip jako T-144) pro každou tiskárnu, odběr paketů ze sdíleného spojení (T-143).
  - Buffer drží celé GOP (od klíčového snímku), aby šel uložit přehratelně.
  - Spouštěče uložení podle otázky 2 (`Error`, `Attention`, `Stopped`, detekce chyby z T-155 – jen pokud už bude hotová, není to závislost, ručně).
  - Uložení = buffer + volitelně ještě M sekund po události; jde do stejné složky a pod stejnou rotaci jako T-145.
  - Omezení paměti (datový tok z T-144 × délka bufferu); případně buffer v dočasných segmentech na disku (otázka 3).
- **ui-dev:** nastavení (zapnutí, délka bufferu) a ruční „Uložit posledních N minut“ podle návrhu T-146.

## Akceptační kritéria
- [x] Buffer drží nejvýš nastavenou délku a začíná klíčovým snímkem (test s fixture / simulátorem a falešnými hodinami).
- [x] Uložení po spouštěči vytvoří přehratelný soubor s obsahem před událostí (a po ní podle rozhodnutí) (test). Ruční
      ověření v aplikaci se skutečným oknem a přehrávačem → přesunuto do T-125 a T-127
- [x] Paměť bufferu je omezená a změřená (poznámky).
- [x] Nové texty ve všech jazycích; testy po sobě uklidí soubory i po pádu.
- [x] `dotnet build` a `dotnet test` bez chyb a varování (macOS); část Windows a snímky UI sady Windows → přesunuto do T-125

### Rozhodnutí uživatele 2026-11-04
1. **Buffer:** výchozí posledních **2 minuty v paměti** (~16 MB na tiskárnu při ~1,06 Mbit/s Buddy3D), nastavitelné 1–10 min.
   Buffer drží celé GOP (od klíčového snímku), aby šel uložený klip přehrát. Dokud se neukládá, nic se nezapisuje na disk.
2. **Spouštěče:** chyba tisku (`Error`), „vyžaduje zásah“ (`Attention`), zastavení uživatelem (`Stopped`), detekce chyby
   tisku (T-155 – ještě není hotová; háček, který detektor později zavolá, žádná závislost) a vždy **ručně** („Uložit
   posledních N minut“ z tray menu a okna kamery). Každý spouštěč jde v nastavení vypnout (výchozí všechny zapnuté) a celý
   dashcam jde trvale vypnout (hlavní vypínač; výchozí podle úsudku agenta – návrh: u tiskárny jako `recordPrints`,
   výchozí vypnuto).
3. **Po události** nahrávat ještě **30 s**, pak zapsat soubor (buffer + 30 s) do stejné složky (podsložka tiskárny) a pod
   stejný limit / rotaci jako nahrávky T-145 – použít `CameraRecordingFolders` / rotaci / zapisovač (`FFmpegRecordingWriter`,
   fragmentovaný MP4), nic nezdvojovat. Název souboru odlišit (např. `…_dashcam.mp4` nebo se spouštěčem) – volba agenta,
   zdokumentovat.
4. **Dashcam běží jen tehdy, když u tiskárny neběží nahrávání celého tisku** (když běží, dashcam stojí a nic nedrží – celá
   nahrávka to už má). Promyslet předání, když nahrávání celého tisku začne / skončí.
5. **Kdy je buffer aktivní:** jen během tisku (`Printing` / `Paused` / `Attention`…) – v klidu žádné spojení ani paměť.

## Otevřené otázky
Původní otázky 1–4 jsou zodpovězené rozhodnutím 2026-11-04 (výše): 1 → 2 min, 1–10 min; 2 → všechny spouštěče, každý
vypínatelný, 30 s po události; 3 → v paměti; 4 → vedle, ale jen když se nenahrává celý tisk.

**K potvrzení (neblokující – implementované je zvolené výchozí chování, změna je malá):**
1. **Hlavní vypínač:** u každé tiskárny („Uchovávat poslední minuty (dashcam)“ na podzáložce Kamera, ve stejném řádku jako
   „Automaticky nahrávat tisk“), **výchozí vypnuto** (konzervativní, jako `recordPrints`). Délka a spouštěče jsou společné
   pro všechny tiskárny (záložka Nahrávání).
2. **Spouštěč detekce chyby (T-155)** má zaškrtávací pole v nastavení už teď (výchozí zapnuto), i když detektor ještě není.
   Ponechat, nebo ho skrýt do T-155 / T-154?
3. **Ruční uložení během ukládání** je zakázané s tipem „Poslední minuty se ukládají…“ (automatická událost během ukládání
   ho prodlouží o 30 s od poslední události, ručně to v UI nejde). Stačí?
4. **Předání na nahrávání celého tisku:** když uprostřed tisku začne nahrávání (ručně), probíhající uložení dashcamu doběhne
   (nic se neztratí), ale obsah bufferu se do nové nahrávky nepřenese (ta začne svým prvním klíčovým snímkem) a buffer se
   uvolní. Po zastavení nahrávání během tisku začne dashcam znovu s prázdným bufferem.
5. **Nové spojení s kamerou** (výpadek, změna adresy) buffer vyprázdní (pakety nového spojení na staré nenavazují);
   ukládání pokračuje v dalším souboru `-2` jako u nahrávky.
6. **Změna délky bufferu za tisku** platí od dalšího tisku (běžící buffer drží původní délku); spouštěče platí hned.
7. **Bez notifikace o úspěšném uložení** (jen chyba, jako u nahrávek – rozhodnutí T-145 8); soubor je hned v „Přehrát
   poslední nahrávku“.

## Poznámky k implementaci
Implementováno 2026-11-04 na macOS arm64 (core-dev + ui-dev), větev `feature/T-148-camera-recording-dashcam-buffer`.

**Core (`src/LayerPeek.Core/Cameras/Recording/`):**
- `DashcamBuffer` (internal): kruh paketů jednoho spojení. Data se kopírují do bloků po 256 KiB za sebou (paket větší
  než blok dostane větší blok, mocninu dvou, který se také znovu použije); zahozením GOP se uvolní bloky, které se hned
  použijí pro další pakety, a nejvýš 4 volné bloky se drží v záloze. Seznam paketů je kruh, který roste zdvojením jen do
  limitu počtu. **Ustálený stav bez alokací** (testy `AnAddOfAFullBufferAllocatesNothing` a
  `CameraDashcamAllocationTests`). Tři meze: čas mezi prvním a posledním paketem (≤ `BufferDuration`, zahazují se celé
  GOP zepředu, takže buffer drží `BufferDuration` minus nejvýš jeden GOP), paměť bloků (`MaxBytesPerSecond` 512 KiB/s ×
  délka, tj. 60 MiB při 2 min – asi 4× datový tok Buddy3D, běžná kamera tedy narazí jen na čas) a počet paketů
  (120/s × délka). Buffer vždy začíná klíčovým snímkem; pakety před prvním klíčovým snímkem se zahodí. **Kamera bez
  klíčových snímků:** GOP, který sám překročí mez (paměť, počet nebo délku), se zahodí celý a buffer čeká na další klíčový
  snímek – paměť nikdy nepřekročí mez. Čas = čas příchodu paketu (`TimeProvider.GetTimestamp`), testy ho řídí falešnými
  hodinami. `Release()` vrátí všechno GC.
- `ICameraDashcam` / `CameraDashcam` (`ICameraRecorder.StartDashcam(printerId, CameraDashcamOptions, changed, saved)`,
  `CameraRecorder` dostal volitelný `TimeProvider`): vlastní odběr paketů sdíleného spojení hubu (žádné další spojení,
  žádné dekódování, **beze změny hubu**), čtení na thread poolu. Stavy `Buffering` / `Saving` / `Ended`. `Save(path)`
  zapíše buffer a pokračuje proudem `PostEventDuration` (30 s) po události; další `Save` během ukládání ho prodlouží (soubor
  stejný). Konec ukládání se kontroluje s každým paketem a jednorázovým časovačem, který existuje jen během ukládání
  (zaseknutá kamera ukládání ukončí včas). Požadavky z jiných vláken probudí čtenáře zrušením jeho aktuálního čtení
  (nový token na každé probuzení, odběr se čte dál); kontrola konce a jeho provedení jsou pod jedním zámkem, takže
  prodloužení se nikdy neztratí. `EndAfterSave()` uvolní buffer hned a dashcam skončí po dokončení ukládání (konec tisku,
  převzetí nahráváním). `StopAsync()` dokončí soubor s tím, co má. Chyba zápisu ukončí jen to ukládání (hlášené přes
  `saved` s `Failure`), dashcam bufferuje dál; konec odběru ho ukončí (`Overflow` → `TooSlow`, `CameraUnavailable`,
  `Failed` → `StreamFailed`, `HubStopped` = bez chyby). Bez muxeru `Ended` / `Unavailable` hned.
- **Sdílení s T-144 (žádné zdvojení):** zápis souborů je vyjmutý z `CameraRecording` do `RecordingFiles` (otevření souboru
  na klíčovém snímku, rezervace názvu `CreateNew`, přípony `-2`, `RecordingClock`, dokončení, smazání prázdného souboru při
  chybě otevření) a používá ho nahrávka i dashcam; zapisovač je dál `ICameraRecordingWriterFactory` (FFmpeg, fragmentovaný
  MP4). Chování nahrávky se nezměnilo (všechny testy T-144 / T-145 beze změny prochází).
- **Služba** (`CameraRecordingService.Dashcam.cs`, partial): dashcam u tiskárny s `CameraSettings.Dashcam` (+ zapnutá
  kamera + muxer) běží jen během tisku (`Printing`, `Paused`, `Attention`; `Offline` / `Unknown` / `Unauthorized` tolerované
  `OfflineGrace` = 1 min jedním časovačem jen během výpadku) a jen když se nenahrává celý tisk (`Activity` `Idle`, žádná
  nahrávka a žádné přání nahrávat). Spouštěče = přechod stavu do `Error` / `Attention` / `Stopped`, dokud dashcam existuje
  (aplikace spuštěná v `Attention` tedy nic neuloží, jen začne bufferovat); `SaveDashcam(printerId, trigger)` pro ruční
  uložení a háček detekce (T-155: `CameraDashcamTrigger.FailureDetected`; vrací `false`, když spouštěč je vypnutý nebo
  dashcam nic nedrží). Jeden pracovník na tiskárnu dělá v pořadí uložit → spustit → ukončit, takže konec tisku nikdy
  nepřeskočí uložení své události. Před uložením se jako u nahrávky připraví složka (značka) a zkontroluje 5 GB volného
  místa (chyba = `Failed` s `Mode = Dashcam`, nic se neuloží); soubor se chrání před rotací od naplánování, po uložení se
  spustí rotace a soubor je `LastFile`. Dashcam, který skončí sám (chyba), se do konce tisku znovu nespustí (žádná smyčka).
  Vypnutí dashcamu / kamery, odebrání tiskárny a ukončení aplikace ho ukončí hned (`StopAllAsync` počká na dokončení
  souboru). **Předání:** když začne nahrávání celého tisku, probíhající uložení doběhne a dashcam skončí; po konci nahrávání
  během tisku začne nový dashcam. V nečinnosti žádné spojení, paměť, úloha ani časovač.
- **Název souboru** (`CameraRecordingFolders.DashcamFileName`): `<yyyy-MM-dd_HH-mm-ss události>[_<název tisku>]_dashcam-<spouštěč>.mp4`,
  spouštěč `error` / `attention` / `stopped` / `failure` / `manual`, např. `2026-11-04_14-05-09_Benchy_dashcam-error.mp4`.
  Odpovídá vzoru `IsRecordingFileName`, takže ho rotace bere pod stejný limit jako nahrávky.
- **Stav pro UI:** `CameraRecordingStatus.Dashcam` (`Off` / `Waiting` / `Buffering` / `Saving`), `DashcamMinutes`,
  `DashcamBuffered`; `CameraRecordingMode.Dashcam` u chyb; `RecordingNotification.IsDashcam`.
- **Nastavení:** `CameraRecordingSettings.DashcamMinutes` (1–10, výchozí 2), `DashcamOnError` / `OnAttention` / `OnStopped`
  / `OnFailureDetection` (výchozí zapnuto), `SavesDashcamOn(trigger)`; `CameraSettings.Dashcam` (výchozí vypnuto).
  JSON: `recording.dashcamMinutes`, `recording.dashcamOn*` (jen `false`) a `printers[*].camera.dashcam` (jen `true`),
  výchozí hodnoty se nezapisují; čtení shovívavé jako T-145 (chybná hodnota = výchozí s varováním). Zdroj-generovaný
  `SettingsJsonContext` beze změny typů (jen nové vlastnosti `RecordingDocument` / `CameraDocument`).
- `CameraRecordingServiceOptions.DashcamPostEventDuration` (30 s).

**App (ui-dev):**
- Tray menu: u tiskárny s dashcamem hned pod „Nahrávat“ položka „Uložit posledních N min“ (`TrayMenuCommand.SaveDashcam`,
  vlastní přístupová klávesa, unikátní ve všech jazycích – test), povolená jen při `Buffering`; jinak zakázaná s tipem
  („Poslední minuty se uchovávají jen během tisku.“, „Nahrává se celý tisk.“, „Poslední minuty se ukládají…“). Pravidla
  sdílí `DashcamControlState` (vedle `RecordingControlState`).
- Okno kamery: stejná položka v kontextovém menu (normální okno, malý náhled), zakázaná s tipem; stav se čte jen při
  `Changed` této tiskárny (žádný časovač). Žádná změna rozložení okna.
- Nastavení: na záložce Nahrávání sekce „Poslední minuty tisku (dashcam)“ (délka v minutách s nápovědou o paměti a 30 s,
  chyba místo nápovědy; čtyři spouštěče a nápověda, že ručně jde uložit vždy), na podzáložce Kamera „Uchovávat poslední
  minuty (dashcam)“ ve stejném řádku jako „Automaticky nahrávat tisk“ (na další řádek podzáložka nemá místo, T-033 – headless
  test „vše bez posouvání“ s ním selhal o 3–18 DIP). Neplatná délka zablokuje uložení, přepne na záložku Nahrávání a dá
  fokus poli (`SettingsFieldIds.SettingsDashcamMinutes`). Bez muxeru zakázané s důvodem.
- Notifikace chyby dashcamu: „Uložení posledních minut selhalo“ + stejné důvody jako nahrávka.
- Texty: 16 nových klíčů ve všech 8 jazycích (`MenuSaveDashcam`, `Dashcam*`, `SettingsDashcam*`,
  `NotificationDashcamFailed`); mezi číslem a jednotkou nedělitelná mezera.

**Měření paměti a CPU (macOS arm64, Release, 2026-11-04):** simulovaná kamera (Buddy3D klip, 25 fps, ~1,0 Mbit/s) v tomtéž
procesu, dashcam s 2 min: po 140 s drží **119,7 s v 17,0 MB** (65 bloků po 256 KiB), nárůst spravované haldy 18,0 MB;
po `StopAsync` zbylo z haldy 0,59 MB (tj. paměť bufferu uvolněná). Alokace celého procesu za 30 s ustáleného stavu 13,7 MB
s dashcamem vs. 14,5 MB se samotným čtenářem paketů bez dashcamu – alokuje simulátor v témže procesu, dashcam nic
(Core test 0 B). CPU celého procesu (i se simulátorem) ~1,6 % jednoho jádra, bez měřitelného rozdílu proti čtenáři bez
dashcamu. Skutečná Buddy3D (60 s klip `coreone-20261103-1905-60s.mp4` = 7,79 MB → ~1,04 Mbit/s) → **~15,6 MB na 2 min**,
10 min ~78 MB; mez paměti 60 MiB (2 min) / 300 MiB (10 min) chrání jen proti vadné kameře.

**Testy:**
- Core: `DashcamBufferTests` (10: zahození před klíčovým snímkem, délka a začátek klíčovým snímkem po celou dobu, mez
  paměti, nula alokací, kamera bez klíčových snímků, příliš dlouhý GOP, mez počtu, velké pakety, paket větší než buffer,
  `Clear` / `Release`); `CameraDashcamTests` (14: buffer ≤ délka od klíčového snímku a uložení buffer + čas po události –
  falešné hodiny, prodloužení, zaseknutá kamera, konec po uložení s uvolněním paměti hned, konec bez uložení, stop během
  ukládání, nové spojení → prázdný buffer a soubor `-2`, chyba zápisu a bufferování dál, konce odběru, bez muxeru, meze
  z délky) a `CameraDashcamAllocationTests`; `CameraDashcamServiceTests` (21 případů: výchozí vypnuto, jen během tisku, bez
  časovačů v nečinnosti, `Error` / `Stopped` / `Attention` (s názvy souborů), prodloužení událostí i ručně, vypnuté spouštěče,
  háček detekce, bez bufferu nic, celé nahrávání → dashcam čeká, předání ruční nahrávce a zpět, selhání dashcamu bez
  opakování do dalšího tisku, chyba uložení, nezapisovatelná složka a málo místa, vypnutí dashcamu / kamery, výpadek s
  tolerancí, ukončení aplikace, rotace nesmaže ukládaný soubor, název souboru); `SettingsMapperDashcamTests` + fixture
  `settings-v2-dashcam.json`.
- Camera.FFmpeg: `FFmpegRecordingTests.TheDashcamSavesAPlayableFileWithTheTimeBeforeAndAfterTheEvent` (simulátor, 4 s buffer
  + 1 s po události, `Mp4Probe`: platný MP4 od klíčového snímku, délka = buffer + ~1 s, dekodér neotevřen).
- App: `TrayMenuRecordingTests` (+5, přístupové klávesy ve všech jazycích s dashcamem), `SettingsDashcamTests` (8),
  `RecordingNotificationTests` (+8 jazyků); UiTests: `CameraWindowDashcamUiTests` (4),
  `SettingsWindowLayoutTests.RecordingTabFitsInEveryLanguage` rozšířený o neplatnou délku dashcamu.
- Soubory testů v rezervovaných dočasných složkách (`TestDirectories`, úklid i po pádu).
- Snímky macOS přegenerované: `settings-recording.png`, `settings-camera.png`, `settings-camera-preview.png`,
  `settings-camera-preview-4x3-de.png`, `settings-camera-notice.png`. Sada Windows → T-125.
- `dotnet build` 0 upozornění, `dotnet test` celkem 9094, neúspěšné 0, úspěšné 8894, přeskočeno 200 (macOS arm64);
  testy `*Dashcam*` v Core 5× za sebou bez selhání. `dotnet publish src/LayerPeek.App -c Release -r osx-arm64` bez varování
  trim / AOT (jen dosavadní hlášky linkeru / dsymutil). Žádný nový balíček.

Uzavřeno 2026-11-04: zbývající ruční ověření přesunuto do T-125 a T-127 (rozhodnutí uživatele).

## Review
Review qa-reviewer 2026-11-04 (macOS arm64): schváleno po jedné opravě.
- **Opraveno:** jednorázový časovač konce ukládání mohl vystřelit o chvilku dřív, než podle časových značek (`Stopwatch`)
  nastal konec (časovače běží na hrubších hodinách, na Windows ~15,6 ms). Kontrola pak konec odložila a časovač se už znovu
  nenastavil, takže u zaseknuté kamery ukládání neskončilo (až s dalším paketem nebo zastavením). Teď časovač
  (`CameraDashcam.OnDeadlineTimer`) v takovém případě počká zbytek času. Test
  `AStalledCameraEndsTheSaveOnTimeAlsoWhenTheTimerFiresEarly` (hodiny, jejichž časovače vystřelí o 10 ms dřív).
- **Neblokující:** `BeginDashcamSave` při prodloužení (`State == Saving`) nekontroluje volné místo; když ukládání mezi
  kontrolou a voláním `Save` zrovna skončí, začne nové uložení bez kontroly 5 GB (složka už existuje). Okrajové, ponecháno.
- **Mez paměti 512 KiB/s** ponechána: nic se předem nealokuje (bloky po 256 KiB vznikají podle potřeby), běžná kamera
  (~1 Mbit/s, ~16 MB / 2 min) narazí jen na čas; mez jen chrání před vadnou kamerou. Kamera nad ~4,2 Mbit/s dostane
  kratší buffer (zahazují se nejstarší GOP), nikdy nic nepřehratelného. Přísnější mez (např. 2 Mbit/s) by zkracovala
  i běžné 1080p IP kamery na polovinu a paměť běžné kamery by neušetřila.
- `dotnet build` 0 upozornění; `dotnet test` celkem 9095, neúspěšné 0, přeskočeno 200; testy `*Dashcam*` (Core 66, App 52,
  UiTests 4, FFmpeg 1) opakovaně a souběžně bez selhání; `dotnet publish -r osx-arm64` bez varování trim / AOT.
