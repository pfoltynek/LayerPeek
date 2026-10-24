---
id: T-079
title: Vychladnutí po tisku – detekce v Core (práh trysky, průběh, odhad času)
status: todo
owner: core-dev
priority: P2
platform: all
depends_on: []
---

## Cíl
Core logika, která po dokončení nebo zastavení tisku sleduje chladnutí trysky. Pro každou tiskárnu poskytuje stav **Chladne…** (teploty, průběh k prahu, odhad času) a **Vychladlo**. Je podkladem pro widget Vychladnutí (T-088) a volitelnou notifikaci (T-080). Je bez UI a testovatelná s falešnými hodinami.

## Rozhodnutí (uživatel, 2026-10-24)
- Rozhoduje **jen tryska**. Podložka se jen zobrazuje. Notifikace „vychladlo, můžeš vypnout“ je volitelná (T-080).
- **Kdy začne (Q1):** po **dokončeném** (`Finished`) **i zastaveném** (`Stopped`) tisku. Po chybě (`Error`) ne.
- **Kdy skončí (Q2):** stav (i „Vychladlo“) skončí, když:
  - **začne další tisk**,
  - tiskárna přejde do **offline**,
  - se **tryska znovu ohřívá**: cílová teplota trysky > 0, tedy topení zapnuté, např. předehřev.

  Pak stav zmizí bez události. Bez těchto podmínek „Vychladlo“ trvá.
- **Práh (Q3, Q4):** **u každé tiskárny**, rozsah **30–100 °C** po 1 °C, výchozí **50 °C**. K tomu akce „Použít práh u všech tiskáren“ (čistá operace v Core, UI v T-080).
- **Tisk skončil už chladný (Q5):** tryska je už na prahu nebo pod ním. Stav jde rovnou do **„Vychladlo“**, ale **bez události**, takže bez notifikace.

## Rozsah
- `Core/Cooldown/`:
  - `CooldownTracker` – čistý stavový automat po vzoru `Core/Events/PrintEventDetector` (bez I/O, pod zámkem volajícího).
    - `Process(PrinterSnapshot)` vrátí stav tiskárny `None` / `Cooling` / `CooledDown` a případně událost přechodu `Cooling` → `CooledDown` (nejvýš jednou na úlohu).
    - Začátek a konec podle Rozhodnutí.
  - `CooldownState` (record struct):
    - fáze,
    - teplota trysky a podložky,
    - práh,
    - průběh 0–1 (od teploty trysky na začátku chladnutí k prahu),
    - odhad zbývajícího času (`TimeSpan?`),
    - čas začátku.
  - Odhad času se počítá z posledních vzorků teploty trysky (časy ze snapshotů).
    - Doporučený je model ochlazování 1. řádu (Newton) s odhadem okolní teploty.
    - Nesmyslný odhad se nevrací: když teplota neklesá nebo je méně než N vzorků, odhad je `null`.
    - Přesný algoritmus volí core-dev a popíše ho v poznámkách.
    - Testy s křivkou ze simulátoru (`tools/PrusaSimulator`, `PrinterSimulation`: chladnutí trysky τ = 60 s, podložky τ = 300 s) i se zašuměnými daty.
  - `CooldownMonitor` (služba, po vzoru `PrintEventNotifier`):
    - odebírá `IPrinterMonitor.SnapshotUpdated` a drží tracker pro každou tiskárnu,
    - `Configure(printers, settings)` při každém běhu; stav tiskáren se stejným strojem zachová (jako `PrintEventNotifier.IsSameMachine`),
    - práh se bere z každé tiskárny; změna prahu za běhu se projeví hned,
    - `GetState(printerId)` a událost `Changed(printerId)`,
    - čas „teď“ z `TimeProvider` (falešné hodiny v testech).
- Nastavení prahu u tiskárny (`PrinterDefinition`, např. `CooldownThresholdCelsius`, výchozí 50, 30–100):
  - uloží se do `printers[*]` v `settings.json` (`version` 2 z T-077; pole je nepovinné, výchozí hodnota se nezapisuje),
  - neplatná hodnota → výchozí s varováním,
  - čistá operace „použít práh u všech tiskáren“.
  - UI prahu je v T-080.
- Registrace v composition rootu App (explicitní) a předání nastavení přes `IMonitoringObserver`. Jen napojení, bez UI.
- Testy v `LayerPeek.Core.Tests` s `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`, pokud už je v testech; jinak vlastní falešné hodiny).

## Akceptační kritéria
- [ ] Tracker:
  - dokončení i zastavení tisku spustí chladnutí, chyba ne;
  - průběh a odhad se mění s teplotou;
  - pokles trysky na práh nebo pod něj → `CooledDown` a právě jedna událost;
  - nový tisk, offline a znovu zapnuté topení trysky stav ukončí bez události;
  - tisk skončený pod prahem → rovnou `CooledDown` bez události;
  - chybějící teplota nevyvolá výjimku ani falešný přechod.
- [ ] Odhad času:
  - konverguje na simulované křivce (chyba v testu < 20 % v druhé polovině chladnutí);
  - při nedostatku dat, rostoucí teplotě a šumu vrací `null`;
  - nikdy není záporný ani nekonečný.
- [ ] `CooldownMonitor`:
  - konfigurace běhu, zachování stavu při přejmenování tiskárny, zapomenutí při změně adresy,
  - práh u každé tiskárny zvlášť a jeho změna za běhu,
  - thread-safety (souběžné snapshoty různých tiskáren),
  - uvolnění.
- [ ] Nastavení prahu: výchozí 50, hranice 30 a 100, round-trip, neplatné hodnoty, starý soubor, „použít u všech tiskáren“
- [ ] Core bez Avalonie, AOT kompatibilní
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné – Q1–Q5 rozhodnuté 2026-10-24, viz Rozhodnutí)

## Poznámky k implementaci
(vyplní agent po dokončení)
