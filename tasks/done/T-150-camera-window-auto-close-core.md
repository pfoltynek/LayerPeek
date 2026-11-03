---
id: T-150
title: Okno kamery – automatické zavření při vypnuté tiskárně a znovuotevření po zapnutí (logika v Core)
status: done
owner: core-dev
priority: P2
platform: all
depends_on: [T-035, T-006]
---

## Cíl
Když se tiskárna vypne (monitor ji hlásí jako `Offline`) a její okno kamery je otevřené, okno se samo **zavře** (zavření + ukončení streamu, ne jen skrytí). Když se tiskárna znovu zapne a okno bylo zavřené **automaticky**, otevře se znovu na posledním místě (stávající logika `CameraWindowDefaults.RememberLastPlacement` / `CameraWindowDefaultPlacement` / `CameraWindowOpenPolicy`, T-035). Okno, které zavřel uživatel, se znovu neotevře (nápad 2).

Tento úkol je čistá logika bez UI: stavový automat pro každou tiskárnu s falešnými hodinami. Napojení na okno, nastavení a E2E řeší T-151.

## Rozsah
- **`Core/Cameras`, stavový automat (pracovně `CameraWindowAutoVisibility`, název volí core-dev), po vzoru `CooldownTracker` (T-079) – bez I/O:**
  - vstupy: snapshot tiskárny (`PrinterState`), „okno je otevřené / uživatel ho zavřel / uživatel ho otevřel“, stav kamery (stream funguje – otázka 3), čas z `TimeProvider`,
  - výstupy: příkaz „zavřít okno (automaticky)“ / „otevřít okno (automaticky)“ / nic,
  - **debounce:** zavřít až po souvislém `Offline` po dobu T₁; otevřít až po stabilním online po dobu T₂ (a podle otázky 3 po prvním snímku kamery). Krátký výpadek Wi-Fi kratší než T₁ nic neudělá. Hodnoty T₁, T₂ podle otázky 1 (návrh v zadání: zavřít po 30–60 s offline).
  - **„Online“ znamená** každý stav kromě `Offline` (otázka 2: co `Unauthorized` a `Unknown`?).
  - příznak **„zavřeno automaticky“** pro každou tiskárnu; ruční zavření ho smaže, ruční otevření také (okno je otevřené). Příznak se ukládá jako stav aplikace do `camera-windows.json` (`CameraWindowStateStore`, stav, ne nastavení – stejné zásady jako dosavadní pamatované umístění), aby přežil restart podle otázky 4.
  - každá tiskárna má vlastní okno, automat je pro každou tiskárnu zvlášť; odebrání tiskárny / vypnutí kamery / vypnutí volby příznak smaže.
- **Nastavení:** volba zapnuto / vypnuto (otázka 5: globálně nebo u tiskárny, výchozí hodnota). Uložení v `settings.json` jako ostatní volby, výchozí hodnota se nezapisuje.
- Vztah k ostatním úkolům: automatické zavření okna uvolní jen odběr okna. Nahrávání (T-145) a detekce (T-153) drží vlastní odběr sdíleného spojení (T-143), takže běží dál, pokud je kamera dostupná.

## Akceptační kritéria
- [x] Offline kratší než T₁ okno nezavře; delší zavře právě jednou (testy s falešnými hodinami).
- [x] Po automatickém zavření a stabilním online (T₂, případně první snímek) přijde právě jeden příkaz otevřít; po ručním zavření nikdy (testy).
- [x] Ruční otevření během offline, ruční zavření během čekání na znovuotevření, změna nastavení, odebrání tiskárny a restart monitoringu mají otestované chování.
- [x] Příznak „zavřeno automaticky“ se uloží do `camera-windows.json` a načte; poškozený soubor = žádný příznak (testy s fixture).
- [x] Automat nezávisí na Avalonii ani na UI vlákně.
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. **Debounce:** po kolika sekundách offline zavřít (návrh 30, 45, 60 s?) a po kolika sekundách stabilního online otevřít? Pevné hodnoty, nebo nastavitelné?
2. **Které stavy se počítají jako „vypnutá tiskárna“:** jen `Offline`, nebo i `Unauthorized` / `Unknown`? (Doporučení architekta k rozhodnutí: jen `Offline`, protože `Unauthorized` znamená běžící tiskárnu se špatným heslem.)
3. **Podmínka znovuotevření:** stačí, že PrusaLink odpovídá, nebo se má čekat na funkční stream kamery (kamera Buddy3D se po zapnutí tiskárny může rozbíhat déle než PrusaLink)? Pokud čekat na stream, aplikace musí kameru zkoušet na pozadí bez okna (přes hub T-143 nebo `CameraSnapshot`) – je to v pořádku?
4. **Restart aplikace:** když okno bylo automaticky zavřené a aplikace se mezitím restartuje (např. autostart po zapnutí PC), má se po startu okno otevřít, jakmile je tiskárna online? A má se otevřít okno, které bylo **otevřené při ukončení aplikace** (dnes se po startu žádné okno kamery samo neotevírá)?
5. **Volba v nastavení:** globální, nebo u každé tiskárny? Výchozí hodnota (návrh v zadání: zapnuto)? Umístění v okně nastavení (souvisí s T-151)?
6. **Okno v režimu celé obrazovky:** má se po automatickém znovuotevření vrátit i do celé obrazovky, nebo jen do normálního okna / malého náhledu podle `CameraWindowStartMode`?

### Rozhodnutí uživatele 2026-11-03
1. Pevně **T₁ = 30 s** souvislého `Offline` → zavřít; **T₂ = 10 s** stabilního online → otevřít. V nastavení jen zapnuto / vypnuto; E2E zkrátí časy Debug přepínačem (T-151).
2. „Vypnutá tiskárna“ = **jen `Offline`**; `Unauthorized` a `Unknown` se počítají jako online (okno zůstane).
3. Znovuotevření, jakmile **odpovídá PrusaLink** (T₂ stabilního online); na stream kamery se nečeká, okno se připojuje samo (stávající opakování). Bez závislosti na T-143.
4. Příznak „zavřeno automaticky“ **přežije restart** (`camera-windows.json`); po startu se takové okno otevře, až bude tiskárna online (T₂). Okna otevřená při ukončení aplikace se po startu jako dosud **neotevírají**.
5. Volba **globální, výchozí zapnuto**, ve skupině voleb okna kamery (výchozí hodnoty okna kamery, T-035).
6. Celá obrazovka: znovuotevření **stejně jako z tray menu** (`CameraWindowStartMode` + uložené umístění), žádná výjimka.
7. Okno, které uživatel otevře, když tiskárna není známá jako online (`Offline` nebo ještě bez snapshotu), **zůstane otevřené**, dokud tiskárna znovu nebude online; pak platí běžné pravidlo (30 s offline → zavřít).
8. Zapnutí volby během výpadku: T₁ (30 s) se počítá **od okamžiku zapnutí volby**, okno se nezavře hned. Stejně při znovuzapnutí kamery tiskárny. (Nový běh monitoringu pozorování zapomíná vždy, odpočet začne prvním dalším snapshotem.)
9. Uspání / probuzení počítače: po probuzení (velký skok času) se **nesmí nic udělat podle stavu z doby před uspáním**; odpočty začnou znovu od prvního čerstvého snapshotu po probuzení.

## Poznámky k implementaci
- **Automat:** `Core/Cameras/CameraWindowAutoVisibility` (+ `CameraWindowAutoVisibilityOptions`, `CameraWindowAutoCommand`, `CameraWindowAutoAction`). Čistá logika bez I/O, časovačů a UI; čas z `TimeProvider` v okamžiku volání (ne z časů snapshotů). Není thread-safe – App volá vše na jednom vlákně (UI).
  - Vstupy: `ApplySettings(AppSettings)` (nový běh monitoringu – zapomene pozorování, příznaky/okna zůstanou), `UpdateSettings(AppSettings)` (běžící odpočty pokračují), `MonitoringStopped()`, `SystemResumed()`, `Process(PrinterSnapshot)`, `WindowOpened(id)`, `WindowClosedByUser(id)`, `SetCloseDeferred(id, bool)` (režim rozložení / kontextové menu, T-151 rozhodnutí 2), `Evaluate()`.
  - Výstupy: každé volání vrací seznam `CameraWindowAutoCommand(PrinterId, Action)`: `Close` (zavřít automaticky), `Open` (otevřít tiše jako z tray menu), `ForgetAutoClosed` (jen smazat příznak). `command.AutoClosed` = hodnota příznaku k uložení. `NextDueAt` = kdy zavolat `Evaluate()` (App si naplánuje jeden časovač; offline snapshoty chodí s backoffem až 1 min, proto nestačí jen snapshoty).
  - Časy: `Options.CloseAfterOffline` 30 s, `OpenAfterOnline` 10 s (výchozí); konstruktor `CameraWindowAutoVisibility(TimeProvider, CameraWindowAutoVisibilityOptions?, IEnumerable<string>? autoClosedPrinterIds)` – Debug přepínač E2E (T-151) předá kratší časy.
  - „Vypnutá“ = jen `Offline`; `Unauthorized`, `Unknown` a ostatní = online. Odpočet se měří od prvního offline / online snapshotu, každý opačný snapshot ho zruší.
  - Bez nastavení (před prvním `ApplySettings`), s vypnutou volbou nebo s vypnutou kamerou se nic neděje; vypnutí volby / kamery a odebrání tiskárny vrátí `ForgetAutoClosed`. Příznaky z minulého běhu nenakonfigurovaných tiskáren se zapomenou při prvním `ApplySettings`.
  - Po restartu: příznaky z `camera-windows.json` do konstruktoru; okno se otevře po 10 s online (rozhodnutí 4).
- **Nastavení:** `AppSettings.CameraWindowAutoClose` (globální, výchozí `true`), v `settings.json` jako `"cameraWindowAutoClose": false` jen když je vypnuto. Okno nastavení zatím hodnotu jen přenáší (aby ji uložení nepřepsalo), ovládací prvek přidá T-151.
- **Stav:** `ICameraWindowStateStore.GetAutoClosedPrinters()` / `SetAutoClosed(id, bool)` v App (`JsonCameraWindowStateStore`, `MemoryCameraWindowStateStore`), v `camera-windows.json` jako `"autoClosed": ["mk4"]` (nezapisuje se, když je prázdné). Čtení je tolerantní (`LenientStringListConverter`): jiný typ než pole = žádné příznaky, ne-řetězce se přeskočí, umístění oken zůstanou. `Remove` (změna výchozích hodnot okna, T-035) příznak nemaže. Úprava je v App, protože úložiště tam už bylo (T-014).
- **Jak to App (T-151) zapojí:** po `LoadAsync` úložiště vytvořit automat s `GetAutoClosedPrinters()`; pozorovatel (`IMonitoringObserver`) předává `ApplySettings`/`UpdateSettings`/`ReportMonitoringFailed`→`MonitoringStopped`, probuzení OS → `SystemResumed` a snapshoty (`IPrinterMonitor.SnapshotUpdated`) na UI vlákno; okno hlásí `WindowOpened` / `WindowClosedByUser` (při každém zavření kromě vykonání příkazu `Close`) a `SetCloseDeferred` při vstupu/výstupu z režimu rozložení a otevření/zavření kontextového menu. Pro každý příkaz: `store.SetAutoClosed(cmd.PrinterId, cmd.AutoClosed)` a podle `Action` zavřít / otevřít okno. Po každém volání přeplánovat časovač na `NextDueAt`.
- **`WindowClosedByUser` volat při KAŽDÉM zavření okna, které nevykonává příkaz `Close`** (zavření / skrytí uživatelem, odebrání tiskárny, vypnutí kamery, zavírání oken aplikací z jakéhokoli jiného důvodu); jinak by automat okno považoval za otevřené.
- **Rozhodnutí 7:** příznak `HoldUntilOnline` – okno otevřené bez známého online stavu se nezavře, dokud nepřijde online snapshot.
- **Rozhodnutí 8:** když se tiskárna v `UpdateSettings` nově stane způsobilou (volba zapnuta / kamera zapnuta) a je offline, odpočet T₁ začne v tu chvíli.
- **Rozhodnutí 9 (uspání, skoky hodin):** dvě pojistky.
  - *Stáří snapshotu (bez platformy):* `StaleAfter` = 2 × (max(interval dotazování, `PrinterMonitorOptions.MaxBackoff`) + `PollTimeout`) = 140 s s výchozími hodnotami (5 min interval → 620 s). Je-li poslední snapshot tiskárny starší (nebo v budoucnosti – hodiny šly zpět), čekající zavření / otevření se zahodí a snapshot po takové mezeře začne odpočet znovu. Běžné mezery (backoff offline až 1 min + timeout 10 s) pod limit spadají. Vlastní volby monitoru App předá v `CameraWindowAutoVisibilityOptions.Monitor`.
  - *`SystemResumed()`:* App (T-151) ho zavolá z události probuzení OS (Windows `PowerModeChanged`/`WM_POWERBROADCAST` PBT_APMRESUMEAUTOMATIC, macOS `NSWorkspaceDidWakeNotification`); okamžitě zapomene pozorování (příznaky, okna a odklady zůstanou). Je volitelné – stáří snapshotu zachytí i probuzení bez něj, jen po delší mezeře; s ním je to spolehlivé i pro krátké uspání.
  - Skok hodin dopředu (NTP) se chová jako uspání (nic se neudělá, začne se znovu), skok zpět také.
- Testy: `CameraWindowAutoVisibilityTests` (Core, FakeTimeProvider), `SettingsMapperTests`/`AppSettingsTests` (volba), `CameraWindowStateStoreAutoClosedTests` (App, fixture `Fixtures/camera-windows-auto-closed*.json`), `CameraWindowSettingsTests.SavingKeepsTheAutoCloseOption`. Žádné nové balíčky.
