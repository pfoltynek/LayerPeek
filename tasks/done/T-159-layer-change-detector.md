---
id: T-159
title: Detekce změny vrstvy z osy Z (PrusaLink `axis_z`) a spouštěč timelapse po vrstvě
status: done
owner: core-dev + ui-dev
priority: P3
platform: all
depends_on: [T-149, T-148, T-160, T-161]
---

> **Návrh ze spiku T-149 (2026-11-03).** Uživatel 2026-11-04 schválil postup (otázka 2 v T-149): spouštěč „po vrstvě“
> přijde až po spouštěči po čase (T-160, T-161) a po ověření na záznamu ze skutečného tisku. Celá funkce timelapse je
> v UI experimentální (T-161). Podrobnosti: `docs/timelapse-spike.md`, oddíl „Spouštěč po vrstvě“.

## Cíl
PrusaLink na Buddy (`GET /api/v1/status`) číslo vrstvy ani událost změny vrstvy nehlásí. Jediný údaj je `axis_z`
(`marlin_vars().logical_curr_pos[2]`, tedy cíl posledního zařazeného pohybu, 1 desetinné místo) při každém pollingu
(výchozí 5 s). Z-hop (Core One: `retract_lift = 0.2`, rampovaný zdvih při přejezdu až `travel_max_lift = 1.5` mm)
se v něm objeví jako krátký skok nahoru. Úkol přidá do Core čistou, testovatelnou logiku, která z řady vzorků Z odhadne
změnu vrstvy, a nejdřív ji ověří na záznamu ze skutečného tisku.

## Rozsah
- **Záznam ze skutečné tiskárny (uživatel spustí, agent připraví):** skript nebo režim stávajícího
  `tools/record-prusalink-fixtures.ps1`, který během celého tisku každou 1 s uloží jen `čas, state, axis_z, job.progress`
  (žádné sériové číslo, IP, heslo) do CSV. Aspoň dva tisky Core One (výška vrstvy 0,2 a 0,1 nebo 0,05 mm), ideálně
  i s „complete objects“ (sekvenční tisk, Z jde dolů). Záznam jde jako fixture do `tests/LayerPeek.Core.Tests/Fixtures/`.
- **Core (název volí core-dev, např. `LayerChangeDetector` v `Core/Printers`):** čistá funkce / stavový objekt bez
  časovačů: vstup vzorek (`čas`, `PrinterState`, `AxisZ`, `JobId`), výstup „nová vrstva“ (odhadnutá Z).
  Navržený algoritmus ze spiku (core-dev ho ověří na záznamu a smí upravit):
  - jen ve stavu `Printing` a u téže úlohy; nová úloha / stav mimo tisk = reset,
  - „Z vrstvy“ = minimum posledních *k* vzorků (výchozí *k* = 2, při pollingu 5 s zpoždění ≤ 10 s). Z-hop trvá jen
    po dobu přejezdu; když ho zachytí *k* vzorků po sobě, vznikne navíc snímek téže vrstvy (ne vynechaná vrstva).
    Jak často to nastává, ukáže až záznam (podíl vzorků se z-hopem *p* → falešná vrstva s pravděpodobností ~*p*^*k*),
  - změna vrstvy, když minimum vzroste nad poslední Z vrstvy aspoň o 0,05 mm; pokles minima (konec z-hopu po falešné
    vrstvě, sekvenční tisk, nový objekt) = nový základ bez snímku,
  - limit: nejvýš jeden snímek za polling; vrstvy kratší než polling se sloučí (timelapse má pak méně snímků).
- **Napojení (core-dev):** pozorovatel snímků monitoru (`IMonitoringObserver`) předá změnu vrstvy službě timelapse
  (T-160) jako druhý spouštěč vedle spouštěče po čase.
- **UI (ui-dev):** volba spouštěče „po vrstvě“ v nastavení timelapse (T-161), s krátkým vysvětlením, že jde o odhad
  z osy Z, a se stejným označením „Experimentální“ jako ostatní volby timelapse. Nové texty do všech jazyků `Strings.*.resx`.
- Záznam ze skutečné tiskárny (skript + spuštění uživatelem) jde udělat kdykoli dřív, nezávisle na T-160 / T-161.

## Akceptační kritéria
- [x] Záznam aspoň jednoho skutečného tisku je ve fixtures; poznámky uvádí zjištěný podíl vzorků se z-hopem
      a přesnost detektoru (zachycené / vynechané / falešné vrstvy proti očekávanému počtu z výšky dílu / výšky vrstvy).
      Další záznamy (0,1 / 0,05 mm, sekvenční tisk, MMU) → přesunuto do T-127
- [x] Testy na záznamu i na syntetických řadách: z-hop 0,2 i 1,5 mm, vrstvy 0,05 / 0,1 / 0,2 / 0,3 mm, zaokrouhlení
      na 0,1 mm, sekvenční tisk, pauza, nová úloha, výpadek tiskárny.
- [x] Žádné časovače ani alokace na vzorek v ustáleném stavu.
- [x] Služba timelapse pořídí se spouštěčem po vrstvě snímek na každou zjištěnou vrstvu (test s falešným monitorem
      a falešným streamem); volba je v UI a uloží se. Ruční ověření na skutečném displeji a se skutečným tiskem → přesunuto
      do T-127, Windows → přesunuto do T-125
- [x] `dotnet build` a `dotnet test` bez chyb a varování. (macOS; Windows → přesunuto do T-125)

## Otevřené otázky
- ~~**Každá N-tá vrstva:** nabízet u spouštěče po vrstvě i „každou N-tou vrstvu“?~~ **Rozhodnuto uživatelem
  2026-11-04:** ano, N nastavitelné, výchozí N = 1.
- ~~Pokud záznam ukáže, že při pollingu 5 s je detekce nepoužitelná …~~ **Rozhodnuto uživatelem 2026-11-04:** v tom
  případě zkrátit polling na 2 s jen u tiskárny, která právě tiskne s aktivním spouštěčem po vrstvě; zavést jen tehdy,
  když to data ukážou. **Data to neukázala** (nejkratší vrstva 27 s, při 5 s 276/276 vrstev) → polling se nemění,
  nic se nezavádí (poznámky).
- **Celá funkce timelapse zůstává „Experimentální“** (rozhodnutí uživatele 2026-11-04) – i spouštěč po vrstvě je pod štítkem
  sekce Timelapse a v textech.

**K potvrzení uživatelem (neblokující, implementované je konzervativní výchozí chování, změna je malá):**
1. ~~**Snímek na začátku a na konci tisku.** Spouštěč po vrstvě vezme snímek při začátku timelapse (prázdná podložka /
   startovní sekvence) a jeden při `Finished` / `Stopped` (hotový díl – poslední vrstva změnu nemá, hlava odjede nahoru).
   Po obnovení z pauzy snímek nebere (na rozdíl od spouštěče po čase). Vyhovuje?~~
   **Rozhodnuto uživatelem 2026-11-05:** ano, a koncový snímek vzít i tehdy, když tisk skončí z `Attention` (např.
   `Attention` → `Stopped`). Doplněno ve `CameraTimelapseLayerTrigger` + test
   `AnEndAfterThePrinterWaitedForTheUserTakesTheLastFrameToo`.
2. ~~**Rozsah N = 1–100** (volné celé číslo jako ostatní čísla záložky). Stačí?~~ **Rozhodnuto uživatelem 2026-11-05:** stačí.
3. **Spouštěč je globální** (jako interval, `recording.timelapseTrigger`), ne u tiskárny – souvisí s otázkou 1 v T-161.
4. **Skript záznamu `record-z.sh`** je jen v `~/LayerPeek-samples/tools` (uživatele, bash, curl s `-K` konfigurací).
   Přidat ho do `tools/` v repozitáři (případně jako režim `tools/record-prusalink-fixtures.ps1`)?

## Poznámky k implementaci
Implementováno 2026-11-05 (core-dev + ui-dev, macOS arm64), větev `feature/T-159-layer-change-detector`.

**Záznam a fixture:** `~/LayerPeek-samples/z-recordings/coreone-job354-0.2mm.csv` (uživatel, `record-z.sh`, Core One s MMU3,
úloha 354, PLA, vrstva 0,2 mm, ~2,8 h, vzorek ~1 s: 9 011 × `PRINTING` + 120 × `FINISHED`, 343 kB). Do repozitáře jde
**bez ztráty pro vzorkování** zkomprimovaný jako `tests/LayerPeek.Core.Tests/Fixtures/z-recordings/coreone-job354-0.2mm.csv`
(9 kB): řádek `sekundy od začátku, state, axis_z` jen při změně stavu nebo Z (+ poslední vzorek); hodnota platí do dalšího
řádku, takže „co uvidí dotaz v čase t“ je stejné jako u původních vzorků. Bez sériového čísla, IP, názvu úlohy (sloupce
`progress` a `job_id` vypuštěné, úloha je 354 v hlavičce). Kopíruje se do výstupu (`Fixtures\**\*.csv`).

**Co záznam ukázal (1 s):**
- Tiskárna hlásí `PRINTING` ~4 min před první vrstvou: Z 40 → 2 → 0 → 5 → čistící linka 0,3–0,7 → 15 (54 s) → první
  vrstva 0,2 mm v 235. s. Na konci (stále `PRINTING`) zdvih 55,4 → 105,4, pak `FINISHED` 225,6.
- **Výška dílu 55,4 mm** (poslední plató před zdvihem) / 0,2 mm = **277 vrstev = 276 změn vrstvy** (první vrstva se
  dosáhne shora ze startovní sekvence, změnou není).
- **Z-hop v 1,05 % vzorků** (92 z 8 764 mezi první vrstvou a koncem): +0,1 mm 50×, +0,2 9×, +0,3 12×, +0,4 6×, +0,5 6×,
  +0,6 9×, jednou −0,1; trvá 1 vzorek (68×), 2 (11×), 3 (1×). Hodnoty 1,5 mm se v záznamu neobjevily.
- **Délka vrstev:** min 27 s, 5. percentil 28 s, medián 29 s, max 170 s (první vrstva); **0 vrstev kratších než 5 s
  i než 2 s**. Výměna nástroje MMU, pauza ani sekvenční tisk v záznamu nejsou (jednobarevný tisk).
- Kamera (snímky posledních 45 min) se nepoužila; ověření proti času vrstev je přesnější.

**Algoritmus ze spiku nestačil** (Python ve scratchpadu, podvzorkování na 5 s a 2 s, všechny fáze): minimum *k* = 2
vzorků dalo při 5 s 1 378/1 380 zachycených, ale **19 falešných uvnitř tisku + 37 ve startu a na konci** (součet přes 5 fází);
při 2 s 15 + 32 na 2 fáze. Příčiny: dva z-hopy za sebou, přechodová hodnota (11,8 → **11,9** → 12,0 – minimum vystoupá ve
dvou krocích = dvě události), startovní sekvence a zdvih na konci.

**Návrh `Core/Printers/LayerChangeDetector`** (čistý stavový automat, 4 `double` + počítadla, bez časovače, bez alokací):
- Počítají se jen vzorky `Printing` se známou Z. Jiná úloha (známé `Job.Id` se liší) a konec tisku (`Idle`, `Finished`,
  `Stopped`, `Error`) = od začátku. `Paused`, `Attention`, `Busy`, `Offline`, `Unknown`, `Unauthorized` jen přeruší řadu
  vzorků: vrstva zůstává (zaparkovaná hlava pauzy není vrstva, vrstvy vytištěné během výpadku se po něm ukážou jako jedna
  změna). Neznámá Z (`null`, NaN) přeruší řadu.
- **Plató** = stejná Z (±0,025 mm) ve 3 vzorcích za sebou → výška, ve které se tiskne. Nižší plató jen nastaví vrstvu
  (první vrstva, další objekt sekvenčního tisku, konec z-hopu).
- **Rovnoměrné stoupání** pro vrstvy kratší než plató (drobné díly): bez plató, ale žádný krok posledních 4 vzorků nestoupl
  o víc než 1 mm → jejich minimum je dosažená výška (z-hop zvedne jen jednotlivé vzorky). Nejvýš jedna změna za vzorek.
- **Změna vrstvy** = výška o **0,05–1,0 mm** nad současnou vrstvou. Větší skok (start, zdvih na konci, stabilní přejezd)
  jen nastaví vrstvu bez změny.
- Zvážené a zamítnuté varianty: plató 2 vzorků (při 2 s 2 vynechané + 3 falešné), plató podle času (nepomohlo při 1 s),
  monotónní stoupání (o 1–3 % méně vrstev u krátkých vrstev, falešné stejné).

**Přesnost na záznamu** (`LayerChangeDetectorTests`, vše přes všechny fáze pollingu; očekáváno 276 změn 0,4 … 55,4 mm):

| Polling | Zachycené | Vynechané | Falešné | Zpoždění za začátkem vrstvy |
|---|---|---|---|---|
| 2 s (2 fáze) | 276 / 276 | 0 | 0 | medián 5 s, max 7 s |
| 3 s (3 fáze) | 276 / 276 | 0 | 0 | medián 7 s, max 11 s |
| **5 s (5 fází, výchozí)** | **276 / 276** | **0** | **0** | **medián 12 s, max 19 s** |
| 1 s | 276 / 276 | 0 | 4 | medián 2 s |
| 10 s (10 fází) | 275,9 průměr | 1 celkem | 10 celkem (≤ 3 na fázi) | medián 25 s |

Startovní sekvence ani zdvih na konci nedají žádnou změnu. Syntetické vrstvy (Python, 200 vrstev, z-hop 1 % s rozdělením
ze záznamu) při 5 s: vrstvy 3 s → 115 / 119 možných (sloučení), 6 s → 194 / 199, 10 s → 198 / 199, ≥ 15 s → 199 / 199;
falešné ≤ 1 na 200 vrstev.

**Rozhodnutí o pollingu (otázka 2):** nezaváděno. Data: nejkratší vrstva 27 s, žádná pod 5 s ani 2 s, při výchozích 5 s
276/276 bez falešných. Kratší polling by jen zvýšil počet dotazů na tiskárnu. Detektor navíc zvládá i vrstvy kratší
než polling (sloučí je). Rozhodnutí se dá znovu otevřít po záznamech drobných dílů (T-127).

**Spouštěč `Core/Cameras/Timelapse/CameraTimelapseLayerTrigger`** (`ICameraTimelapseTrigger`, žádný časovač, žádný
zvláštní `IMonitoringObserver` – služba už předává každý snímek tiskárny přes `OnSnapshot`): snímek při prvním
`SetCapturing(true)` (začátek), při každé N-té změně vrstvy (`LayerChanges % N == 0`), a jednou při přechodu
z `Printing` / `Paused` do `Finished` / `Stopped` (hotový díl). Během pauzy nic a čekající požadavek odvolá
(`CancelFrameRequest`), obnovení snímek nebere. Bez dat Z (např. budoucí Prusa Connect) jen snímek začátku a konce.
`CameraRecordingService.StartTrigger` vybírá podle `CameraRecordingSettings.TimelapseTrigger`; změna spouštěče, intervalu
(u spouštěče po čase) nebo N (u spouštěče po vrstvě) vymění spouštěč běžícího timelapse (`TakesTimelapseFramesLike`),
změna `Changed` hlásí.

**Nastavení:** `CameraRecordingSettings.TimelapseTrigger` (`CameraTimelapseTriggerKind.Time` / `Layer`,
`recording.timelapseTrigger` = `"layer"`, výchozí se nezapisuje) a `TimelapseEveryNthLayer` (1–100, výchozí 1,
`recording.timelapseEveryNthLayer` jen když ≠ 1). Chybné hodnoty = výchozí s varováním bez ozvěny hodnoty.

**UI:** v řádku nadpisu „Timelapse“ (záložka Nahrávání, štítek „Experimentální“ platí pro celou sekci) nahradilo pole
„Každých N sekund“ / „Každou N. vrstvu (odhad)“ popisek intervalu; vedle je číslo zvoleného spouštěče (interval nebo
počet vrstev, stejná šířka 120 DIP). Vysvětlení odhadu z osy Z je tip a nápověda pro čtečky (na záložce není místo na
další řádek, T-033). Skryté číslo se neověřuje a jeho neplatná hodnota se při uložení nahradí načtenou. Neplatné N =
„Zadejte celé číslo od 1 do 100.“, uložení zablokované, fokus poli (`SettingsFieldIds.SettingsTimelapseEveryNthLayer`).
Šest nových klíčů ve všech 8 jazycích (`SettingsTimelapseTrigger`, `…TriggerTime`, `…TriggerLayer`, `…TriggerHint`,
`SettingsTimelapseEveryNthLayer`, `…EveryNthLayerHint`); cs / sk / de / fr položky zkrácené na řadovou tečku („Každou N.
vrstvu (odhad)“, „Jede N. Schicht (geschätzt)“, „Toutes les N couches (env.)“), aby se vešly do pole (234 DIP).
Snímek `settings-recording.png` (macOS) přegenerovaný.

**Výkon:** detektor ~50 ns na vzorek (měřeno 50 M vzorků, Release, M2 Pro, i s `Math.Round` ve smyčce), 0 B na vzorek
(testy `LayerChangeDetectorTests.ASampleAllocatesNothing` a `CameraTimelapseLayerTriggerTests.ASnapshotAllocatesNothing`),
žádný časovač (`CameraTimelapseServiceLayerTests`: během timelapse po vrstvě běží jen kontrola disku). Polling a počet
dotazů na tiskárnu beze změny.

**Testy:** Core `Printers/LayerChangeDetectorTests` (35 případů: záznam při 2 / 3 / 5 s všechny fáze přesně 276 změn ve
správném pořadí a nejvýš 4 dotazy po začátku vrstvy, 1 s a 10 s s mezemi, start a zdvih na konci bez změny; z-hop 0,2
a 1,5 mm na 1–2 dotazy, rampa z-hopu, z-hop přes celé plató (např. výměna nástroje MMU 0,6 mm nad tiskem = jeden snímek
navíc, další vrstva se najde), park víc než 1 mm nad tiskem, vrstvy 0,05 / 0,1 / 0,15 / 0,2 / 0,3 mm se zaokrouhlením
na 0,1 mm (float jako firmware), vrstvy kratší než dotaz, sekvenční tisk, pauza / `Attention` / `Busy`, výpadek
`Offline` / `Unknown` / `Unauthorized` (i delší než 1 mm), konec tisku, nová úloha, neznámá Z, snímek, 0 B na vzorek),
`ZRecording` (načtení a vzorkování záznamu), `CameraTimelapseLayerTriggerTests` (13), `CameraTimelapseServiceLayerTests`
(6: celý záznam přes službu = 1 + 276 + 1 požadavků bez časovače, N = 3 a změna N za běhu, přepínání čas ↔ vrstva,
pauza a nová úloha, výpadek, **skutečný `CameraTimelapseRecorder` nad hubem s falešným proudem** – každá N-tá vrstva zapíše
další klíčový snímek, nevyžádané klíčové snímky ne), `SettingsMapperTimelapseLayerTests` (+ fixture
`settings-v2-timelapse-layer.json`); App `SettingsTimelapseTriggerTests` (7); UiTests `SettingsWindowTimelapseUiTests`
(výběr spouštěče ve všech 8 jazycích, texty se vejdou, uložení a načtení, neplatné N a fokus, bez rekordéru) a
`SettingsWindowLayoutTests.RecordingTabFitsInEveryLanguage` (texty spouštěče se vejdou).

**Výsledek (macOS arm64):** `dotnet build` 0 upozornění, 0 chyb; `dotnet test` celkem 9800, neúspěšné 0, úspěšné 9599,
přeskočeno 201. `dotnet publish src/LayerPeek.App -c Release -r osx-arm64` bez varování trim / AOT (jen dosavadní hlášky
nástrojů Xcode: dsymutil, `ModuleCache`). Žádný nový balíček. Pozn.: v jednom dřívějším plném běhu UiTests selhal jednou
`SettingsWindowLayoutTests.GeneralTabWithTheTestNotificationFitsInEveryLanguage(de)` (záložka Obecné, T-159 ji nemění);
samostatně i v dalších dvou plných bězích prošel – nejspíš nestabilní pod zátěží.

Uzavřeno 2026-11-05: zbývající ověření přesunuto do T-125 a T-127 (sběrné úkoly podle `tasks/README.md`): Windows (testy,
snímky, AOT `win-x64`, ruční pohled) → T-125 oddíl 2n; ruční pohled na displeji a tisk se skutečnou Core One (počet snímků
proti počtu vrstev, další záznamy 0,1 / 0,05 mm, sekvenční tisk, MMU) → T-127 (oddíl 1, oddíl 2 a krok 3 pořadí před
vydáním 0.5.0).
