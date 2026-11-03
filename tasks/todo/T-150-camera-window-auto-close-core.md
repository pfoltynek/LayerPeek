---
id: T-150
title: Okno kamery – automatické zavření při vypnuté tiskárně a znovuotevření po zapnutí (logika v Core)
status: todo
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
- [ ] Offline kratší než T₁ okno nezavře; delší zavře právě jednou (testy s falešnými hodinami).
- [ ] Po automatickém zavření a stabilním online (T₂, případně první snímek) přijde právě jeden příkaz otevřít; po ručním zavření nikdy (testy).
- [ ] Ruční otevření během offline, ruční zavření během čekání na znovuotevření, změna nastavení, odebrání tiskárny a restart monitoringu mají otestované chování.
- [ ] Příznak „zavřeno automaticky“ se uloží do `camera-windows.json` a načte; poškozený soubor = žádný příznak (testy s fixture).
- [ ] Automat nezávisí na Avalonii ani na UI vlákně.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. **Debounce:** po kolika sekundách offline zavřít (návrh 30, 45, 60 s?) a po kolika sekundách stabilního online otevřít? Pevné hodnoty, nebo nastavitelné?
2. **Které stavy se počítají jako „vypnutá tiskárna“:** jen `Offline`, nebo i `Unauthorized` / `Unknown`? (Doporučení architekta k rozhodnutí: jen `Offline`, protože `Unauthorized` znamená běžící tiskárnu se špatným heslem.)
3. **Podmínka znovuotevření:** stačí, že PrusaLink odpovídá, nebo se má čekat na funkční stream kamery (kamera Buddy3D se po zapnutí tiskárny může rozbíhat déle než PrusaLink)? Pokud čekat na stream, aplikace musí kameru zkoušet na pozadí bez okna (přes hub T-143 nebo `CameraSnapshot`) – je to v pořádku?
4. **Restart aplikace:** když okno bylo automaticky zavřené a aplikace se mezitím restartuje (např. autostart po zapnutí PC), má se po startu okno otevřít, jakmile je tiskárna online? A má se otevřít okno, které bylo **otevřené při ukončení aplikace** (dnes se po startu žádné okno kamery samo neotevírá)?
5. **Volba v nastavení:** globální, nebo u každé tiskárny? Výchozí hodnota (návrh v zadání: zapnuto)? Umístění v okně nastavení (souvisí s T-151)?
6. **Okno v režimu celé obrazovky:** má se po automatickém znovuotevření vrátit i do celé obrazovky, nebo jen do normálního okna / malého náhledu podle `CameraWindowStartMode`?

## Poznámky k implementaci
(vyplní agent po dokončení)
