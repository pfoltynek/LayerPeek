---
id: T-245
title: UI test EveryButtonIsAtLeast32DipHighInEveryState překračuje 30 s na runneru Windows (blokuje další UI testy)
status: done
owner: qa-reviewer
priority: P2
platform: all
depends_on: []
---

## Cíl
Ruční běh CI 43756249903 (2026-11-15, commit 113a7757, `windows-2025`, 4 vCPU): test
`LayerPeek.App.UiTests.Settings.SettingsWindowLayoutTests.EveryButtonIsAtLeast32DipHighInEveryState` skončil po
limitu těla UI testu 30 s (`HeadlessSession.TestTimeout`, `TimeoutException`). Protože tělo dál blokovalo UI vlákno,
selhalo hned po něm dalších 5 testů téže třídy („UI thread still blocked by …“). Lokálně (16 vláken) test trvá 10–13 s,
na zatíženém runneru se 4 jádry přes 30 s. Je to test citlivý na zátěž, ne chyba aplikace.

## Rozsah
- Změřit, kde test tráví čas (počet stavů okna × jazyků, rozvržení a čekání), a zrychlit ho (např. rozdělit na theory
  po stavech / jazycích, aby každý běh měl vlastní limit, nebo ubrat zbytečné průchody rozvržením).
- Teprve když zrychlení nestačí: vlastní delší limit jen pro tento test (zdůvodněný), ne zvýšení globálního
  `HeadlessSession.TestTimeout`.
- Projít ostatní nejpomalejší UI testy (TRX z CI / lokálně) a zapsat, zda jim hrozí totéž.

## Akceptační kritéria
- [x] Příčina a měření zapsané; test pod zátěží (4 jádra + souběžné běhy) bezpečně pod limitem
- [x] `dotnet build` a `dotnet test` bez chyb a varování; job Windows v CI zelený – ruční běh 43761629956 (72811eab, 2026-11-15) celý zelený
  (lokálně splněno: build 0 varování, App.UiTests 2× zelené, z toho 1× omezeně; zbývá zelený job Windows v CI)

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- **Příčina (měřeno dočasnou sondou se `Stopwatch`, odstraněnou):** test procházel 32 stavů (26 stavů layout testu +
  6 stavů tlačítek) × 10 pohledů v jednom těle. Lokálně ~10 s s JIT, z toho ~6,9 s `Settle()` po přepnutí záložky
  (téměř vše v prvním `Dispatcher.UIThread.RunJobs()` = rozvržení nově zobrazené záložky, ~20 ms na pohled; samotné
  `UpdateLayout` ~0 ms, vykreslení snímku ~0,3 s celkem), ~3 s otevření oken stavů (`OpenAsync`), procházení stromu
  tlačítek ~0,1 s, zavírání ~0,3 s. Žádný zbytečný průchod rozvržením: každý pohled se rozvrhuje poprvé, takže
  zrychlit jde jen rozdělením.
- **Oprava:** `EveryButtonIsAtLeast32DipHighInEveryState` a `ButtonsInsideTheTemplatesOfOtherControlsKeepTheirSize` jsou
  teď theory přes stav (`ButtonCheckStates`, 32 případů), `EverythingFitsWithoutScrollingInEveryState` theory přes
  jazyk × stav (`LanguagesAndStates`, 8 × 26 = 208 případů). Každý případ má vlastní limit 30 s; globální
  `HeadlessSession.TestTimeout` beze změny, žádný zvláštní delší limit. Stav se dohledá podle jména (`OpenState`).
  Pokrytí stejné: stejné stavy × pohledy × jazyky. Kontrola „test dosáhl všech druhů tlačítek“ (dřív jedna sjednocená
  množina) je teď v `ReachedButtons`: každé z 21 očekávaných tlačítek má stav, jehož případ ho musí najít, a fakt
  `ButtonCheckReachesEveryKindOfButton` ověřuje, že mapa pokrývá celý původní seznam, že jmenované stavy existují a jména
  stavů jsou jedinečná. Ověřeno mutací (tlačítko přiřazené špatnému stavu → případ selže).
- Soubor je linkovaný i do `LayerPeek.App.UiTests.SystemFont` (na Windows se přeskakuje, na macOS se rozdělení
  projeví stejně).
- **Měření (TRX):**
  - CI 43756249903 (`windows-2025`, 4 vCPU): `EveryButton…` 30,09 s (timeout), `EverythingFits…(pl)` **26,73 s**,
    (sk) 19,07 s, (fr) 16,08 s, (es) 14,80 s, `ButtonsInsideTheTemplates…` 11,17 s, ostatní jazyky 9–11 s. Hrozil tedy
    i test „bez posuvníku“ (pl na 89 % limitu) – proto rozdělen také.
  - Lokálně (16 vláken, souběžně jiné sezení) před: 5,25 / 5,17 / 4,46–4,77 s; po: nejdelší případ 0,36 s, součet
    `EverythingFits…` 37,0 → 41,7 s, `EveryButton…` 5,3 → 6,2 s, `ButtonsInside…` 5,2 → 6,9 s (režie nové aplikace na
    případ ~15–20 %).
  - Omezeně (proces s afinitou na 4 jádra + 4 vytěžující procesy na stejných jádrech): před `ButtonsInside…` 11,70 s,
    `EveryButton…` 10,18 s, `EverythingFits…` 8,5–9,6 s; po: nejdelší případ 0,55 s (`EveryButton…`), 0,51 s
    (`EverythingFits…`), 0,49 s (`ButtonsInside…`). Celý běh App.UiTests 1759 testů, 0 chyb, 5 min 58 s.
- **Ostatní nejpomalejší UI testy** (CI / lokálně omezeně): `PrinterImageProcessorTests.ImporterAcceptsARealPhoto`
  4,1 s / 4,9 s, `SettingsWindowImageUiTests.CustomImageIsImportedThroughTheFileDialog` 3,4 / 2,9 s,
  `StartupAndUpdatesTabFitsInEveryLanguage` až 3,3 s na CI, `DiagnosticsWindowLayoutTests.*` do 2,3 s, ostatní pod 2 s.
  Nejbližší limitu je tak ~14 % (4,1 / 30 s) – nehrozí jim totéž; žádný další zásah.
- Ověření: `dotnet build` 0 varování; `dotnet test --project tests/LayerPeek.App.UiTests` 1759 testů, 0 chyb (3 min 15 s);
  `LayerPeek.App.UiTests.SystemFont` 0 chyb (na Windows přeskočeno). E2E neběžely (RDP sezení). Testy nic nenechávají
  (beze změny infrastruktury; okna zavírá `UiTestScope`).
- Úkol zůstává v `in-progress/`: jediná otevřená položka je zelený job Windows v CI (ověří hlavní agent).
