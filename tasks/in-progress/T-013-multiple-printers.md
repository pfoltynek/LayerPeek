---
id: T-013
title: Více tiskáren
status: in-progress
owner: ui-dev
priority: P2
platform: all
depends_on: [T-007, T-002]
---

## Cíl
Monitorovat více tiskáren současně.

## Rozsah
- Agregovaný stav ikony (nejzávažnější stav vyhrává)
- Tiskárny v menu jako podmenu
- Notifikace s názvem tiskárny

## Akceptační kritéria
- [x] Pravidlo agregace pokryté testy (tabulka všech dvojic stavů včetně „ještě nedotázána“, shody, offline vs. neznámý stav, prázdná sada, pozice v seznamu)
- [x] `dotnet build` a `dotnet test` bez chyb a varování (0 varování, 1347 testů prošlo)

## Otevřené otázky
- ~~Jedna ikona pro všechny tiskárny, nebo ikona pro každou tiskárnu zvlášť?~~
- ~~Pořadí závažnosti stavů?~~

**Rozhodnutí (2026-10-16):** Jedna agregovaná ikona, tiskárny jako podmenu. Pořadí závažnosti: chyba > vyžaduje pozornost > offline > pozastaveno > tiskne > hotovo > idle (detaily stavů Busy/Stopped/Unknown/Unauthorized viz T-007). **Implementováno** (viz poznámky).

Nové otázky (implementace zvolila variantu v závorce, změna je snadná):
1. **Tooltip pro víc tiskáren:** jeden řádek na tiskárnu „Název: Stav – 42 %, zbývá 1 h 5 min“, nejzávažnější tiskárna první (při shodě pořadí z konfigurace). Když se vše nevejde do 127 znaků (limit Windows), vynechají se nejdřív zbývající časy, pak se méně závažné tiskárny shrnou do řádku „Další tiskárny: N“. Název souboru se nezobrazuje, názvy tiskáren se zkracují na 24 znaků. Je to v pořádku? Alternativa: pořadí podle konfigurace, nebo detail nejzávažnější tiskárny + počty podle stavů. (Takto.)
2. **Text zbývajícího času v řádku tiskárny:** „{0}, {1} left“ / „{0}, zbývá {1}“. (Takto.)
3. **Shoda závažnosti:** ikona ukazuje první tiskárnu v konfiguraci, takže u dvou tisknoucích tiskáren je v ikoně progress té první. Chcete raději tiskárnu, která skončí nejdřív, nebo nejnižší progress? (První v konfiguraci, jak už rozhodl T-007.)
4. **Tiskárna ještě nedotázaná** se počítá jako offline (závažnější než tisk a pauza), takže hned po startu může ikona chvíli (do první odpovědi všech tiskáren) ukazovat offline. Ponechat? (Ano – stejné pravidlo jako v T-007.)
5. **Jedna tiskárna:** zůstává plochá položka jako v T-008 (kliknutí otevře web, „Diagnostika…“ je v pevné části), podmenu se použije až od dvou tiskáren. Nebo podmenu vždy? (Plochá – nejméně překvapivé, beze změny proti ověřenému T-008.)
6. **Obsah podmenu:** jen „Otevřít PrusaLink v prohlížeči“ / „Otevřít Prusa Connect v prohlížeči“ a „Diagnostika…“ (otevře okno s předvybranou tiskárnou). Žádné zakázané řádky s detaily (zbývající čas, soubor). Chcete detailní řádky? (Ne.)
7. **Pevná položka „Diagnostika…“** zůstává i při víc tiskárnách; otevře okno s první tiskárnou, otevřené okno jen aktivuje bez změny výběru. (Ano.)
8. **Tiskárna s neplatnou adresou** má při víc tiskárnách povolený nadpis podmenu (kvůli diagnostice), zakázaná je jen položka „Otevřít … v prohlížeči“ s tooltipem „Adresa tiskárny není platná“. (Ano.)

## Poznámky k implementaci
- **Agregace ikony (`App/Tray`):**
  - Pravidla z T-007 (`TrayStatusRules`: mapování, `Severity`, `SelectPrimary`) zůstala. Ikona ukazuje nejzávažnější tiskárnu, při shodě první v konfiguraci. Offline, `Unknown` a ještě nedotázaná tiskárna mají stejnou závažnost (offline); `Unauthorized` = vyžaduje zásah; `Stopped` = nečinná; `Busy` mezi hotovo a tisk.
  - `ToolTipContent.Status` dává stav ikony i tiskárně bez snapshotu (offline).
  - `TrayPresentationKey` má nový druh zprávy `Summary` (víc tiskáren) a `SummaryVersion`. Pro jednu tiskárnu je klíč i tooltip beze změny (detail: stav, progress, zbývající čas, soubor).
- **Tooltip pro víc tiskáren (`TrayToolTipFormatter.FormatSummary`):** jeden řádek na tiskárnu „Název: Stav – 42 %, zbývá 1 h 5 min“, nejzávažnější první (stabilní řazení, při shodě pořadí z konfigurace). Postup při nedostatku místa: 1) všechny řádky se zbývajícím časem, 2) bez zbývajícího času, 3) tolik nejzávažnějších řádků, kolik se vejde, a „Další tiskárny: N“. Výsledek nikdy nepřekročí 127 znaků; názvy se zkracují na 24 znaků (`MaxSummaryNameLength`) přes `TrayText.Ellipsize`, takže se nerozdělí dvojice surrogate znaků. Data úlohy nedostupné tiskárny se nezobrazují (stejně jako u jedné tiskárny). Nový text `ToolTipLineRemaining` (CZ + EN).
- **Výkon (`TrayStatusViewModel`):** view model drží pro každou tiskárnu `ToolTipContent`. Dotaz porovná jen obsah své tiskárny (O(1)); při změně zvýší `SummaryVersion`. Výběr ikony je O(n) bez alokace. Klíč se nezmění → nic dalšího (žádné formátování ani post na UI vlákno). Kopie obsahu všech tiskáren (pole n položek) a formátování vzniknou jen při viditelné změně, mimo zámek; dávka změn = jedna UI aktualizace jako dřív. Ustálené dotazování 20 tiskáren nealokuje (test).
- **Menu (`App/Tray/Menu`):**
  - `TrayMenuEntry` má `PrinterId` a `Submenu` (`TrayMenuEntryList` – neměnný seznam s porovnáním hodnotou, takže záznam zůstává hodnotový).
  - `TrayMenuViewModel`: jedna tiskárna = plochá položka jako v T-008 (kliknutí otevře web). Víc tiskáren = nadpis podmenu „Název: Stav – 42 %“ (příkaz `None`, povolený) s položkami „Otevřít PrusaLink/Prusa Connect v prohlížeči“ (URL, zakázaná s tooltipem u neplatné adresy) a „Diagnostika…“ (`PrinterId`, povolení podle `TrayMenuOptions`). Seznam položek podmenu vzniká jednou pro každou tiskárnu a konfiguraci (na UI vlákně, lokalizovaný) a při změně stavu se znovu použije stejná instance – dotaz tak mění jen nadpis. Ostatní chování (porovnání `PrinterMenuContent` pod zámkem, sloučení dávky, minimum změn kolekce) je beze změny.
  - `TrayMenuTarget`: položka s podmenu vytvoří přes `INativeTrayMenuItem.CreateSubmenu()` vlastní položky a mění je na místě (vlastnost se zapíše jen při změně; položky podmenu se při stejné instanci seznamu vůbec neprocházejí). Jen změna struktury (plochá ↔ podmenu, jiný počet položek podmenu – tedy jen změna sady tiskáren) nahradí platformní položku na stejném indexu. Kliknutí na nadpis podmenu se ignoruje, odebraná položka odpojí i položky podmenu. `AvaloniaNativeTrayMenu` nastaví `NativeMenuItem.Menu`.
  - `TrayMenuPresenter`: „Diagnostika…“ volá `ITrayAppCommands.ShowDiagnostics(printerId)` (`null` z pevné části).
- **Diagnostika s předvybranou tiskárnou:** `DiagnosticsViewModel` má volitelné `initialPrinterId` (načte se jen informace té tiskárny, žádný zbytečný dotaz na první) a `ShowPrinter(id)` pro už otevřené okno. Neznámé id se zapamatuje do příchodu dalšího seznamu tiskáren (okno otevřené během startu), pak se zahodí. `AuxiliaryWindowSlot.ShowOrActivate` dostal volitelný callback `reuse`, který u otevřeného okna předá jeho view model před aktivací. `App` / `TrayHost` / `AppServices.CreateDiagnosticsViewModel` předávají id.
- **Notifikace:** `PrintNotification` z T-009 nese celou `PrinterDefinition`; nový test ověřuje, že mezi několika tiskárnami dostane každá událost definici (a název) své tiskárny. Na této větvi je zaregistrovaná `NullNotificationService`, text notifikace tedy nevzniká; toasty (T-010, samostatná nesloučená větev) mají jako titulek název tiskárny – po sloučení je potřeba ověřit.
- **Native AOT:** bez reflexe a bez nových balíčků. `scripts/aot-smoke.ps1` prošel (AOT aplikace běžela 10 s; hlášky ILC o Avalonia DComposition jsou starší a nesouvisí).
- **Testy (nové/změněné):**
  - `Tray/TrayStatusRulesTests`: tabulka všech dvojic stavů (12 × 12 včetně „nedotázána“) pro výběr i ikonu, nezávislost na pozici, shoda offline/neznámý/nedotázaná, shoda dvou tisknoucích tiskáren, prázdná sada, úplnost tabulky.
  - `Tray/TrayToolTipFormatterTests`: souhrn CZ/EN, pořadí při shodě, žádná stará úloha offline tiskárny, vynechání zbývajících časů, 20 tiskáren (CZ), dlouhé názvy, surrogate páry, limit pro 2–100 tiskáren a kontrola, že součet řádků a „Další tiskárny“ = počet tiskáren.
  - `Tray/TrayPresentationKeyTests`, `Tray/TrayStatusViewModelTests`: verze souhrnu, změna jiné než primární tiskárny mění jen tooltip, neviditelné změny neposílají nic na UI vlákno, změna tam a zpět, nulová alokace při ustáleném dotazování 20 tiskáren, 20 tiskáren v limitu.
  - `Tray/Menu/*`: podmenu (sestavení, obsah, neplatná adresa, Prusa Connect, CZ, volby), znovupoužití seznamu podmenu, přechod 1 ↔ víc tiskáren, porovnání hodnotou, nulová alokace, směrování kliknutí podle tiskárny (diagnostika s id, web, zakázaná položka, nadpis), aktualizace na místě, náhrada položky jen při změně struktury, odpojení položek podmenu.
  - `Diagnostics/DiagnosticsViewModelTests`: předvýběr (i před startem monitoringu, neznámé id), `ShowPrinter` (přepnutí, stejná tiskárna nic nemění, neznámé id, po `Dispose`). `Windows/AuxiliaryWindowSlotTests`: callback `reuse`. `Core.Tests/Notifications`: název tiskárny mezi několika tiskárnami.
  - Bez disku, sítě a procesů.
  - **Stabilita testů nulové alokace (review):** `GC.GetAllocatedBytesForCurrentThread()` občas započítá nevyužitý zbytek alokačního kontextu vlákna (~8 KB), když jiná vlákna alokují nebo spustí GC – proto občas selhal `SteadyStateUpdatesDoNotAllocate` (7 848 B). Oprava ve sdílené testovací infrastruktuře (`tests/Shared/AllocationProbe.cs`, kompiluje se do App.Tests i Core.Tests):
    - kolekce `AllocationSensitive` s `DisableParallelization = true` – testy alokací běží samostatně, bez jiných testů souběžně,
    - `AllocationProbe.MinAllocated(pass, attempts: 3)` spustí měřený blok až 3× (pokaždé s novým vstupem) a vrátí minimum; skutečná alokace se projeví v každém průchodu. Žádné prahy ani pouhé zahřívání.
    - Všechny testy nulové alokace jsou přesunuté do vlastních tříd v této kolekci: `App.Tests/Diagnostics/DiagnosticsAllocationTests` (graf teplot, cache popisků), `App.Tests/Tray/TrayAllocationTests` (ikona a menu, 20 tiskáren), `Core.Tests/History/TelemetryHistoryAllocationTests` (zápis, čtení, růst paměti), `Core.Tests/Events/PrintEventDetectorAllocationTests`. Pomocné metody původních tříd jsou kvůli tomu `internal`.
    - Ověřeno 8 koly, v každém běžely souběžně 2× App.Tests a 2× Core.Tests (32 běhů) – vše prošlo.
  - **Předvýběr v diagnostice (review):** ruční výběr tiskárny v okně (`SelectedPrinter`) zahodí čekající požadavek z menu, takže pozdější seznam tiskáren už výběr uživatele nepřepíše. Testy `ManualSelectionWinsOverAPendingMenuRequest` a `PendingMenuRequestIsAppliedWhenThePrinterArrives`.
- **Ověření na Windows 11:**
  - `dotnet build` 0 varování, `dotnet test` 1347 prošlo (1 přeskočený existující).
  - `dotnet run` s dočasným `%APPDATA%\PrusaTrayDiagnostic\settings.json` (složka předtím neexistovala): 3 PrusaLink tiskárny na nedosažitelných adresách `192.0.2.10–12` (jedna s názvem `MK4_test`) a jedna s neplatnou adresou. Aplikace běžela 30 s (stejné PID, ~97 MB), pak byla ukončena a složka smazána. Tooltip ani menu se přečíst nepodařilo – ikona je ve skrytých ikonách a bez kliknutí není přes UI Automation dostupná (syntetický vstup se nepoužívá).
  - **Ručně musí ověřit uživatel:** vzhled podmenu s více tiskárnami (Windows vykresluje Avalonia), živou změnu nadpisu podmenu, „Diagnostika…“ z podmenu (otevře okno s danou tiskárnou; při otevřeném okně přepne tiskárnu), otevření webu z podmenu, víceřádkový tooltip a přechod 1 ↔ víc tiskáren po změně nastavení.
- **Platformy:** logika je společná. macOS: podmenu přes `NativeMenuItem.Menu` (NSMenu) – **na macOS nic neověřeno** (T-018).
