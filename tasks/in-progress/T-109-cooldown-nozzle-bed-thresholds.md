---
id: T-109
title: Vychladnutí – prahy trysky i podložky u každé tiskárny (widget Vychladnutí)
status: in-progress
owner: ui-dev + core-dev
priority: P2
platform: all
depends_on: [T-079, T-080, T-088, T-092, T-103]
---

## Cíl
Tiskárna se počítá jako vychladlá, až když **tryska i podložka** klesnou na svůj práh nebo pod něj. Dnes rozhoduje jen
tryska (T-079), takže „Vychladlo – můžete vypnout“ přijde, i když je podložka ještě teplá. Prahy se nastavují u každé
tiskárny zvlášť v panelu widgetu Vychladnutí na záložce Widgety (T-092).

## Rozhodnutí (uživatel, 2026-10-29)
1. **Dva prahy:** tryska **a** podložka. Vychladlo = tryska ≤ práh trysky **a** podložka ≤ práh podložky. Výchozí
   tryska **40 °C**, podložka **40 °C** (místo jednoho prahu trysky, výchozí 50 °C). Rozsah 30–100 °C zůstává pro oba.
2. **Kde se nastavují:** v nastavení widgetu Vychladnutí na záložce Widgety, **u každé tiskárny zvlášť** (každá má svou
   dvojici), s volbou „použít u všech tiskáren“. Pole prahu u tiskárny mizí (přesunuté do nastavení widgetu).
3. **Převod:** uložené prahy trysky se zahodí, každá tiskárna dostane 40 / 40 °C (verze 0.x smí měnit nastavení; uvést
   v CHANGELOG [0.3.1] Changed). Starý soubor se musí načíst bez varování navíc; testy.
4. **Odhad a průběh:** platí pomalejší z obou – odhad „cca X min“ = delší z odhadů trysky a podložky
   (`CooldownEstimator` pro každou řadu), průběh = menší z obou průběhů. Widget ukáže obě teploty s jejich prahy. Texty
   pásu malého náhledu (T-103) upravit konzistentně a v mezích pravidel šířky T-103.
5. **Vše, co používá událost vychladnutí, ji následuje:** notifikace „Vychladlo“ (T-079 / T-080), widget, pás malého
   náhledu, diagnostika / log. Událost nese obě měření a oba prahy.

## Rozsah
- Core: `CooldownThresholds` (tryska, podložka) v `PrinterDefinition`, tracker se dvěma řadami, monitor, událost,
  nastavení (`settings.json`) a převod starého prahu.
- App: panel widgetu Vychladnutí (dvě pole, „Použít prahy u všech tiskáren“), widget Vychladnutí, pás malého náhledu,
  notifikace (Windows toast i macOS přes `ToastContentBuilder.Lines`), texty v 8 jazycích.
- Testy (Core, App, headless UI), snímky, dokumentace, CHANGELOG.
- Mimo rozsah: jiné rozvržení záložky Widgety, nové volby notifikace.

## Akceptační kritéria
- [x] Core: vychladlo jen při splnění obou podmínek (2 platná měření každé řady na prahu nebo pod ním); vychladlá
      tryska s teplou podložkou i naopak zůstává „Chladne…“; znovu zapnuté topení trysky i podložky stav ukončí; offline
      delší než tolerance ho ukončí; změna kteréhokoli prahu během chladnutí platí hned (zvýšený práh, který měření splňují,
      vychladí s událostí; snížený „Vychladlo“ nezruší); tiskárna bez teploty podložky se řídí jen tryskou.
- [x] Odhad = delší z obou (žádný, když řada nad prahem nemá spolehlivý odhad), průběh = menší z obou.
- [x] Událost a log nesou obě měření a oba prahy.
- [x] Nastavení: výchozí 40 / 40, rozsah, round-trip, neplatné hodnoty s varováním, starý `cooldownThresholdCelsius` se
      zahodí jen s informačním záznamem (bez varování, žádný typ hodnoty soubor nerozbije), při dalším uložení zmizí.
- [x] Panel widgetu: dvě pole u vybrané tiskárny (výběr tiskárny nad seznamem), jedna chybová hláška, „Použít prahy u
      všech tiskáren“ kopíruje oba prahy; App.Tests i headless UI testy.
- [x] Widget, pás malého náhledu a notifikace podle oddílu Návrh; texty ve všech 8 jazycích (`StringsTests` projde).
- [x] Headless UI testy rozložení v 8 jazycích bez oříznutí a bez posuvníku (`SettingsWindowLayoutTests`,
      `CameraWindowCooldownWidgetUiTests`, `CameraWindowMiniStripFinishUiTests`).
- [ ] Snímky: macOS přegenerované a prohlédnuté; **Windows sadu přegenerovat na Windows** (seznam v Poznámkách).
- [x] Dokumentace (`docs/architecture.md`, README), CHANGELOG [0.3.1].
- [x] `dotnet build` (Debug i Release) a `dotnet test` bez chyb a varování.
- [x] Revize ui-designer (oddíl Otevřené otázky Q2–Q4) a potvrzení uživatele k Q1 (Q1 2026-10-30, Q2–Q4 2026-10-30
      po revizi ui-designer).

## Návrh (ui-dev, 2026-10-29) – k revizi ui-designer
- **Panel Vychladnutí na záložce Widgety:** zůstává rozvržení T-092 – výběr tiskárny nad seznamem widgetů (sdílený se
  záložkou Tiskárny) určuje, čí prahy panel ukazuje, takže „u každé tiskárny zvlášť“ nepotřebuje nový výběr tiskárny.
  Místo jednoho pole jsou dvě vedle sebe (120 DIP, mezera 12): nad nimi popisky „Tryska (°C)“ a „Podložka (°C)“,
  napravo na řádku polí tlačítko „Použít prahy u všech tiskáren“ (zalamuje se, max. 240 DIP). Pod tím nápověda „Až
  tryska i podložka klesnou na tyto teploty nebo pod ně, je tiskárnu bezpečné vypnout.“ nebo místo ní **jedna** chyba
  pro obě pole (pole s Minimum/Maximum neplatnou hodnotu zadat skoro nedovolí; zbývá prázdné pole). Jména pro čtečku:
  „Práh teploty trysky (°C)“ / „Práh teploty podložky (°C)“. Pořadí Tab: tryska → podložka → tlačítko → odkaz.
- **Widget Vychladnutí (M / L):** velké číslo zůstává tryska, podložka vedle (jako dnes). Řádek podrobností jmenuje
  oba prahy: „bezpečné vypnutí za cca 6 min (tryska pod 40 °C, podložka pod 40 °C)“, bez odhadu „bezpečné vypnutí, až
  tryska klesne pod 40 °C a podložka pod 40 °C“. Tiskárna bez teploty podložky: dnešní texty jen s tryskou.
- **Widget S a pás malého náhledu (T-103):** ukazují **jen omezující teplotu** (`CooldownState.Limiting`: ta s menším
  průběhem; po vychladnutí ta, která omezovala naposledy). Varianty C1–C3 a K2 („Chladne… 52 °C · cca 4 min“,
  „Vychladlo · 40 °C“) tak mají stejné šířky jako v T-103 a výběr první varianty, která se vejde, se nemění. Tooltip a
  jméno pro čtečku jmenují obě: „Chladne… tryska 38 °C, podložka 52 °C, cca 4 min“, „Vychladlo – můžete vypnout,
  tryska 33 °C, podložka 40 °C“. Horní pruh = menší z průběhů.
- **Notifikace:** 3. řádek „Můžete tiskárnu vypnout. Tryska 38 °C (práh 40 °C), podložka 39 °C (práh 40 °C)“; bez
  podložky dnešní „Tryska 38 °C, práh 40 °C“.

## Otevřené otázky
**Rozhodnuto 2026-10-30 (uživatel): Q1 A (obě topení), Q5 ano (jen tryska bez teploty podložky)** – potvrzuje implementaci.

**Rozhodnuto 2026-10-30 (uživatel, po revizi ui-designer):**
- **Q2 – A** (dvě pole vedle sebe, implementováno). ✔ vyřešeno.
- **Q3 – pás A** (jen omezující teplota bez označení, implementováno); **widget S: B** – před zvýrazněnou omezující
  teplotou ikona čidla (kužel trysky / čára podložky ve svých barvách, 12 DIP + mezera 2). ✔ provedeno ve větvi
  `feature/design-review-fixes` (viz Poznámky).
- **Q4 – A** (velké číslo zůstává tryska, implementováno). ✔ vyřešeno.
- **Nález 1 revize (zvýraznění chybného pole)** → samostatný úkol (založí architekt), zde se neimplementuje. ✔ vyřešeno.

Nové (neblokuje, k rozhodnutí uživatele / ui-designer), zjištěno při úpravě widgetu S 2026-10-30:
- **QS1 → rozhodnuto 2026-10-30 (uživatel): jiná ikona podložky na kartě S, kterou nejde splést se znaménkem (vybere ui-designer).** ✔ provedeno (ui-designer, větev `feature/T-109-bed-icon-s`): vyhřívaná podložka – deska s třemi vlnkami tepla (`IconHeatedBed16Filled`, vlastní cesta, viz Poznámky). Původně: **čára podložky před číslem připomíná znaménko minus.** Na snímku `camera-widget-cooldown-cooling-s-bed`
  (macOS) se ikona podložky 12 DIP s mezerou 2 DIP před „57 °C“ čte jako „−57 °C“ (záporná teplota). Kužel trysky
  (`camera-widget-cooldown-cooling-s`) je jednoznačný. Varianty: (A) ponechat podle revize / (B) větší mezera mezi
  ikonou a číslem (např. 4–6 DIP jako v M / L) / (C) jiná ikona podložky ve widgetu S (např. podložka s vlnkami
  tepla) – rozhodne ui-designer / uživatel.
- **QS2 → rozhodnuto 2026-10-30 (uživatel): ikona jen během chladnutí (ponechat).** Původně: S „Vychladlo“:** po vychladnutí je teplota ve widgetu S součástí textu stavu („⏻ Vychladlo · 40 °C“, jeden
  `TextBlock`), proto tam ikona čidla zatím není (pokyn mluvil o zvýrazněné teplotě, ta je jen během chladnutí). Má
  ikona být i v tomto stavu? (A) ne, stav je klíčová hodnota / (B) ano – rozdělit text a ikonu vložit před teplotu.
- **QS3 → rozhodnuto 2026-10-30 (uživatel): A, vyhřívaná podložka jen na kartě S.** Původně (ui-designer 2026-10-30): vyhřívaná podložka (QS1) je jen na kartě S; M / L ponechávají
  čáru podložky (vedlejší údaj za velkým číslem, 16 / 20 DIP, mezera 6 DIP – znaménko nepřipomíná; čára je legendou
  grafu widgetu Teploty). (A, doporučeno) ponechat / (B) vyhřívaná podložka i v M / L widgetu Vychladnutí (stejná ve
  všech velikostech, ale jiná než ve widgetu Teploty) / (C) všude včetně widgetu Teploty a pásu (ztratí vazbu na čáru
  grafu, potřebuje variantu 20 pro L).

**Doporučení ui-designera k Q2–Q4 (revize 2026-10-30, podrobnosti v Poznámkách; k potvrzení uživatelem):**
- **Q2 – (A, doporučeno, implementováno) dvě pole vedle sebe** s krátkými popisky a tlačítkem vpravo. Vejde se v 8
  jazycích, záložka Widgety je už na hraně výšky (nápovědy se kvůli 2 DIP musely zkrátit), varianta pod sebou (+~40 DIP)
  by vynutila posuvník nebo další zkracování. Vzor „dvě související číselná pole v řádku“ odpovídá Fluent i HIG.
- **Q3 – pás: (A, doporučeno, implementováno) jen omezující teplota bez označení**, obě teploty v tooltipu a ve jméně pro
  čtečku; číslo je v pásu bílé (neutrální), šířky variant T-103 zůstávají. **Widget S: doporučuji A + ikonu čidla (B)**:
  zvýrazněné číslo ve widgetu S má barvu akcentu `OverlayAccentBrush` #FA6831, prakticky stejnou jako barva trysky
  `TemperatureNozzleColor` #FF8A3D (widget Teploty, kužel ve widgetu M / L), takže když omezuje podložka, barva napovídá
  „tryska“. Ikona čidla (kužel trysky / čára podložky ve svých barvách, 12 DIP + mezera 2, jako v M / L) před číslem to
  řeší bez spoléhání na barvu; karta S se přizpůsobí obsahu (+14 DIP). Do pásu ikonu nedávat (+14 DIP by měnilo výběr
  variant při 240 / 320). Varianty: (A) beze změny všude / (B) A + ikona čidla jen ve widgetu S (doporučeno) / (C) ikona
  i v pásu (nové testy šířek T-103) / (D) „87 / 55 °C“ – nedoporučuji (častěji vypadne odhad v de / fr / it).
- **Q4 – (A, doporučeno, implementováno) velké číslo zůstává tryska.** Stabilní rozvržení shodné s widgetem Teploty
  (kužel = tryska, čára = podložka), průběh i řádek podrobností už omezení ukazují. (B) prohazovat zvýraznění podle
  omezující teploty – nedoporučuji (karta „skáče“, mění se význam velkého čísla). (C) A + značka „✓“ u teploty, která už
  je na prahu nebo pod ním (ne jen barvou) – levné vylepšení, kdyby uživatel chtěl; samostatný drobný úkol.

Implementace je hotová podle níže uvedených doporučení; nic z toho ji neblokuje, ale jde o UX / produktová rozhodnutí,
která má potvrdit uživatel (Q1, Q5) a zrevidovat ui-designer (Q2–Q4).
- **Q1 – ohřev podložky (uživatel):** T-079 ukončuje chladnutí, když se „znovu ohřívá tryska“ (cíl > 0), a sledovat ho
  začne až po vypnutí topení trysky. Teď rozhoduje i podložka, proto implementace bere stejně i **topení podložky**:
  (A, implementováno) chladnutí začne až po vypnutí obou topení a skončí bez události, když se znovu zapne kterékoli
  z nich / (B) jen tryska jako dnes (předehřev jen podložky by chladnutí nechal běžet a podložka by „chladla“ k cíli).
- **Q2 – rozvržení panelu (ui-designer):** dvě pole vedle sebe s krátkými popisky „Tryska (°C)“ / „Podložka (°C)“
  a tlačítko vpravo (viz Návrh). Alternativa: nadpis „Prahy teploty (°C)“ a pole pod sebou (o ~40 DIP vyšší).
- **Q3 – pás malého náhledu a widget S (ui-designer):** jen omezující teplota bez označení, která to je (tryska /
  podložka), obě teploty jen v tooltipu. Alternativa: obě teploty „87 / 55 °C“ v C1 (širší, v de/fr/it při 320 DIP by
  častěji vypadl odhad).
- **Q4 – widget M / L (ui-designer):** velké číslo je dál tryska, i když omezuje podložka. Alternativa: zvýraznit
  omezující teplotu.
- **Q5 – tiskárna bez teploty podložky (uživatel):** když tiskárna během chladnutí teplotu podložky nehlásí, rozhoduje
  jen tryska (jinak by nikdy „nevychladla“). Prusa tiskárny podložku hlásí vždy; jde o pojistku.

## Poznámky k implementaci
(ui-dev + core-dev, 2026-10-29, na macOS, větev `feature/T-109-cooldown-nozzle-bed-thresholds`)

- **Core (`Core/Cooldown/`):**
  - `CooldownThresholds(NozzleCelsius, BedCelsius)` (record struct, `Default` = 40 / 40, `IsValid`; `default` 0 / 0
    neplatí). `CooldownThreshold` má `MinCelsius` 30, `MaxCelsius` 100, `DefaultNozzleCelsius`, `DefaultBedCelsius`,
    `Validate(CooldownThresholds)` a `ApplyToAll(printers, thresholds)` (kopíruje oba najednou).
    `AppSettings.WithCooldownThresholdsForAllPrinters`.
  - `PrinterDefinition.CooldownThresholds` (místo `CooldownThresholdCelsius`), mimo rozsah výjimka.
  - `CooldownTracker(CooldownThresholds?)`, `SetThresholds`, `Thresholds`. Každá řada (tryska, podložka) má vlastní
    poslední dvě platná měření, nejvyšší teplotu od začátku chladnutí a vlastní `CooldownEstimator` (pevná pole, po
    vytvoření bez alokací – `CooldownAllocationTests` beze změny prochází).
    - Vychladlo: obě řady potvrzené dvěma měřeními na prahu nebo pod ním; bez měření podložky jen tryska (Q5).
    - Událost jen když aspoň jedna řada byla nad prahem; `EndedBelowThreshold`, když žádná.
    - Fáze `Cooling`, když aspoň jedna řada byla nad prahem; bez měření trysky stav `None` (jako dřív).
    - Průběh = menší z průběhů (řada, která nikdy nebyla nad prahem, má 1). Odhad = delší z odhadů; řada s posledním
      měřením na prahu nebo pod ním přispívá nulou (plochá tryska u okolní teploty by jinak odhad zrušila), řada nad
      prahem bez spolehlivého odhadu → žádný odhad.
    - `CooldownState.Limiting` (`CooldownSensor.Nozzle` / `Bed`): řada s menším průběhem, při shodě zůstává předchozí
      (na začátku tryska), po vychladnutí ta, která omezovala naposledy. `LimitingCelsius`.
    - Ohřev: `heating` = cíl trysky **nebo** podložky > 0 (Q1, varianta A).
  - `CooldownState`: `NozzleThresholdCelsius`, `BedThresholdCelsius` (místo `ThresholdCelsius`), `Limiting`,
    `Thresholds`. `CooldownEvent`: `NozzleCelsius`, `NozzleThresholdCelsius`, `BedCelsius` (`null` bez podložky),
    `BedThresholdCelsius`, `Thresholds`.
  - `CooldownMonitor` zapíše při každé události řádek `Information` do logu (obě měření a prahy, bez podložky „no bed
    temperature reported“) – diagnostika i bez zapnuté notifikace.
- **Nastavení (`settings.json`):** `printers[*].cooldownNozzleThresholdCelsius` a `cooldownBedThresholdCelsius`, celá
  čísla, výchozí hodnota se nezapisuje, neplatná hodnota → výchozí s varováním („nozzle“ / „bed“, rozsah, bez hodnoty).
  Formát zůstává `version` 2.
  - **Převod (rozhodnutí 3):** starý `cooldownThresholdCelsius` se čte jen jako `JsonElement?`
    (`PrinterDocument.LegacyCooldownThresholdCelsius`), takže ho žádný typ hodnoty nerozbije, a zahodí se. Za celý
    soubor přijde jedna informační zpráva (`SettingsLoadResult.Notices`, log, ne varování) „The former nozzle cool-down
    threshold of N printer(s) is no longer used; … 40 / 40 °C.“ Při dalším uložení pole zmizí. Testy
    `SettingsMapperCooldownThresholdTests` (+ fixture `settings-v2-0.3.0-cooldown.json`) a upravené
    `JsonSettingsStoreOverlayLayoutTests` (soubor 0.1.0 má teď dvě informační zprávy).
- **App:**
  - `PrinterEditorViewModel.Cooldown.cs`: `CooldownNozzleThresholdCelsius`, `CooldownBedThresholdCelsius`, jedna
    `CooldownThresholdError` pro obě, `ValidCooldownThresholds`, `ApplyCooldownThresholdsToAllCommand`,
    `SetCooldownThresholds`. `SettingsViewModel.ApplyCooldownThresholdsToAll`. `MonitoringSession.KeepsMonitoring`:
    změna prahů dál nerestartuje monitoring.
  - Šablona panelu (`Settings/Widgets/WidgetSettingsTemplates.axaml`): `WidgetCooldownNozzleLabel`,
    `WidgetCooldownBedLabel`, `WidgetCooldownNozzleThreshold` (dřív `WidgetCooldownThreshold`),
    `WidgetCooldownBedThreshold`, `WidgetCooldownApplyAll`, `WidgetCooldownThresholdHint` / `…Error`.
  - Widget (`CooldownWidgetViewModel`): `CooldownContent` má `NozzleThresholdCelsius`, `BedThresholdCelsius`,
    `LimitingCelsius`; `NozzleText` v S = omezující teplota; `CooldownFormat.Detail` s oběma prahy.
  - Pás (`CameraMiniStripViewModel`): `MiniStripContent.Celsius` (omezující teplota, dřív `NozzleCelsius`) +
    `NozzleCelsius` / `BedCelsius` pro tooltip.
  - Notifikace (`ToastContentBuilder.Lines`, Windows toast i macOS): `NotificationCooledDownDetailBed`.
  - **Texty (8 jazyků):** nové `SettingsCooldownBedThreshold`, `SettingsCooldownNozzleLabel`,
    `SettingsCooldownBedLabel`, `CameraOverlayCooldownEstimateBed`, `CameraOverlayCooldownNoEstimateBed`,
    `NotificationCooledDownDetailBed`, `CameraMiniCooledAccessibleBed`, `CameraMiniCoolingAccessible`,
    `CameraMiniCoolingAccessibleNoEstimate`; změněné `SettingsCooldownThresholdHint`, `SettingsCooldownApplyAll`,
    `SettingsCooldownAppliedAll`, `SettingsNotifyCooledDownHelp`, `CameraOverlayWidgetCooldownDescription` (≤ 110 znaků).
    Ostatní jazyky (de, es, fr, it, pl, sk) navrhl ui-dev, vykání podle zvyklostí jazyka.
- **Testy:** Core `CooldownTrackerBedTests` (obě podmínky, jedna splněná a druhá ne v obou směrech, ohřev podložky
  během chladnutí i po vychladnutí, čekání na vypnutí topení podložky, offline, krátký výpadek, změna prahu podložky
  i trysky během chladnutí, průběh = menší, odhad = delší, konvergence odhadu na křivce podložky, odhad podle podložky
  po vychladnutí trysky, žádný odhad pro stojící podložku, výkyv čidla podložky, neplatná měření, bez podložky,
  omezující teplota), upravené `CooldownTrackerTests` (dosavadní testy jen s tryskou: práh podložky 100 °C, který
  simulovaná podložka 60 °C nepřekročí), `CooldownMonitorTests` (+ oba prahy, změna prahu podložky, log),
  `CooldownThresholdTests`, `CooldownNotifierTests`, `CooldownAllocationTests`. App `PrinterCooldownThresholdSettingsTests`
  (obě pole, jedna chyba, „použít u všech“ kopíruje oba), `WidgetsTabSettingsTests` (dvojice vybrané tiskárny,
  chyba podložky), `CooldownWidgetTests` (S ukazuje podložku, průběh = menší, podrobnosti v 8 jazycích),
  `CameraMiniStrip*Tests` (omezující teplota, tooltip s oběma), `ToastContentBuilderCooledDownTests` (8 jazyků).
  UI `SettingsWindowWidgetsUiTests` (obě pole, popisky, pořadí Tab, chyba podložky, kopírování), `SettingsWindowLayoutTests`,
  `CameraWindowCooldownWidgetUiTests`. Testy widgetu a pásu, které sledují křivku trysky s podložkou kolem 40 °C, berou
  dosavadní práh trysky 50 °C a práh podložky 45 °C (`TestPrinters.NozzleOnlyThresholds`).
- **Snímky:** macOS (`Snapshots/macOS/`) přegenerované a prohlédnuté: `camera-widget-cooldown-cooling-l`,
  `camera-widget-cooldown-cooling-m`, `camera-widget-cooldown-cooling-m-de` (podrobnosti s oběma prahy na dva řádky),
  `settings-widgets-cooldown-de`, `settings-widgets-cooldown-error-fr`, `settings-widgets-dark` (dvě pole a tlačítko).
  Snímky pásu malého náhledu se nezměnily (řádek ukazuje dál jednu teplotu). **Windows sadu přegenerovat na Windows**
  (`LAYERPEEK_UPDATE_SNAPSHOTS=1 dotnet test --project tests/LayerPeek.App.UiTests`) a prohlédnout tytéž soubory
  v `Snapshots/`; do té doby tyto testy na Windows selžou.
- **Rozvržení:** delší nápovědy pod poli přetekly záložku Widgety o 2 DIP v de, es, fr, it, pl
  (`SettingsWindowLayoutTests`, `SettingsWindowWidgetsUiTests`); nápovědy v těchto jazycích jsou proto kratší (jeden
  řádek). Widget M / L: podrobnosti s oběma prahy se vejdou do dvou řádků ve všech 8 jazycích
  (`CameraWindowCooldownWidgetUiTests`).
- **Závislosti:** žádné nové balíčky; vše AOT kompatibilní (bez reflexe, JSON přes `SettingsJsonContext`, kompilované
  bindingy).
- **Výsledek (macOS, 2026-10-29):** `dotnet build` i `dotnet build -c Release` 0 varování, 0 chyb; `dotnet test`
  celkem 7908, neúspěšné 0, úspěšné 7726, přeskočené 182 (testy jen pro Windows).
- **Platformy:** ověřeno na macOS (build Debug i Release, celý `dotnet test`, headless UI testy). Windows: build a testy
  na Windows neověřené; E2E testy (jen Windows) upravené jen podle kódu (`WidgetRun`, `ToastNotificationTests` zapisují oba
  prahy, toast s podložkou), **nespuštěné**.

### Revize ui-designer (2026-10-30)
Podklady: snímky macOS `settings-widgets-cooldown-de`, `settings-widgets-cooldown-error-fr`, `settings-widgets-dark`,
`camera-widget-cooldown-cooling-s / -m / -m-de / -l`, `camera-mini-strip-cooling-240-it`, `camera-mini-strip-cooled-de`
(zvětšené výřezy), XAML `Settings/Widgets/WidgetSettingsTemplates.axaml` (`CooldownWidgetSettings`),
`Cameras/Overlay/Widgets/CooldownWidgetView.axaml`, `PrinterEditorViewModel.Cooldown.cs`, `Strings.*.resx`.

**Verdikt: OK s drobnými nedostatky.** Doporučení k Q2–Q4 viz Otevřené otázky (vše varianta A = implementovaný stav).

V pořádku: popisky nad poli, pole 120 DIP, mezera 12, tlačítko „Použít prahy u všech tiskáren“ zarovnané nahoru k polím
(de / fr na 2 řádky v `MaxWidth` 240, nic se neořízne), nápověda a chyba na jednom místě (vzor T-033), odkaz „Změnit
na záložce Obecné“ s `MinHeight` 32; tmavý motiv čitelný; jména pro čtečku polí jmenují čidlo („Práh teploty trysky
(°C)“ / „… podložky …“). Widget M / L: podrobnosti s oběma prahy na 2 řádky i v de; S a pás beze změny šířky.

Nálezy:
1. **Nízká – jedna chyba pro dvě pole neříká, které pole je špatně** (`CooldownWidgetSettings`, `WidgetCooldownThresholdError`;
   `SettingsCooldownThresholdError`). Na `settings-widgets-cooldown-error-fr` je chybné pole „Plateau 120“ vizuálně stejné
   jako platné (bez červeného okraje), patička přitom říká „Corrigez les champs mis en évidence“. Totéž platí v celém okně
   (vzor T-033, pole se neobarvují), v praxi nastane hlavně prázdné pole (NumericUpDown hodnotu mimo rozsah ořízne), proto
   nízká. Návrh: (a) ui-dev přidá `HasNozzleThresholdError` / `HasBedThresholdError` a chybnému poli třídu s okrajem
   `SettingsErrorBrush` (bez změny výšky), nebo (b) text chyby se jménem pole („Podložka: zadejte celé číslo od 30 do
   100.“; při obou chybách stávající text). Doporučuji (a) jako drobnou úpravu celého okna v samostatném úkolu.
2. **Nízká – tlačítko „Použít prahy…“ je při chybné hodnotě povolené** a klik jen odhalí chybu (`ApplyCooldownThresholdsToAll`
   → `RevealErrors`). Chování je bezpečné; jen konstatuji, nic neměnit (zakázané tlačítko by bez vysvětlení mátlo víc).
3. **Informace – neověřeno:** Windows snímky (Segoe UI je širší → tlačítko „Použít prahy…“ může mít 2 řádky i v en),
   skutečný toast s podložkou.

### Úpravy po revizi ui-designer (ui-dev, 2026-10-30, macOS; větev `feature/design-review-fixes`)
- **Widget S (Q3 B):** `CooldownContent.Limiting` (`CooldownSensor`; podložka jen když omezuje a tiskárna ji hlásí).
  `CooldownWidgetViewModel.ShowsNozzleIcon` (M / L vždy, S když omezuje tryska) a `ShowsSmallBedIcon` (S, když omezuje
  podložka). `CooldownWidgetView.axaml`: kužel trysky (`IconCone16Filled`, `OverlayNozzleBrush`, otočený) nebo čára
  podložky (`IconLineHorizontal1_16Filled`, `OverlayBedBrush`) před teplotou; v S 12 × 12 DIP, mezera 2 DIP (karta S
  o 14 DIP širší, max. šířka 240 DIP beze změny). Jen během chladnutí (viz QS2). M / L beze změny.
- **Testy:** App `CooldownWidgetTests` (ikona podle omezující teploty v S / M / L, `content.Limiting`), UI
  `CameraWindowCooldownWidgetUiTests`: S během chladnutí má kužel 12 × 12 v barvě trysky 2 DIP před číslem; nový
  `SmallCardShowsTheBedIconWhileTheBedLimits` (podložka omezuje → čára podložky v barvě podložky, ne kužel; karta se
  vejde ve všech 8 jazycích) + snímek. `LongestTextsFitInEveryLanguageAndSize` (8 jazyků × S / M / L) prochází s ikonou.
- **Snímky macOS přegenerované a prohlédnuté:** změněný `camera-widget-cooldown-cooling-s` (kužel před „150 °C“), nový
  `camera-widget-cooldown-cooling-s-bed` (čára podložky před „57 °C“ – viz QS1). `camera-widget-cooldown-cooled-s` beze
  změny.
- **Windows (ručně):** přegenerovat a prohlédnout v `tests/LayerPeek.App.UiTests/Snapshots/` vedle seznamu výše také
  změněný `camera-widget-cooldown-cooling-s.png` a nový `camera-widget-cooldown-cooling-s-bed.png`. Úkol zůstává
  v `in-progress` (Windows snímky).
- CHANGELOG [0.3.1] Changed: řádek o ikoně čidla ve widgetu S. `docs/architecture.md` (Vychladnutí T-088) doplněno.

### QS1 – ikona podložky na kartě S (ui-designer, 2026-10-30, macOS; větev `feature/T-109-bed-icon-s`)
- **Volba:** vyhřívaná podložka – deska 14 × 3 (zaoblená) a nad ní tři vlnky tepla (jedna perioda sinu, tah 1,75,
  kulaté konce) na plátně 16; klíč `IconHeatedBed16Filled` v `Cameras/Overlay/OverlayIcons.axaml`. Je to obvyklá
  značka vyhřívané podložky u 3D tiskáren; svislé vlnky nad deskou nejde přečíst jako znaménko. Barva podložky
  (`OverlayBedBrush`), 12 × 12 DIP, mezera 2 DIP beze změny, šířka karty S se nemění.
- **Zdroj a licence:** Fluent UI System Icons vhodný glyf nemají (`bed` = postel na spaní, `stove` = hrnec s párou,
  `fire` = oheň, `oven` jen od 20) – proto **vlastní cesta** ve stylu filled glyfů Fluent, vytvořená pro LayerPeek
  (GPL-3.0-only jako zbytek projektu), bez cizí licence; v hlavičce `OverlayIcons.axaml` je označená jako výjimka.
- **Karta S:** prvek ikony podložky dostal vlastní `Data` a třídu `cooldownHeatedBed` (místo `cooldownBedIcon`, aby na
  něj nepůsobil styl L s plátnem 20). QS2 beze změny: ikona jen během chladnutí.
- **M / L beze změny (čára podložky zůstává):** tam je teplota podložky vedlejší údaj za velkým číslem trysky,
  ikona má 16 / 20 DIP a mezeru 6 DIP, takže se nečte jako znaménko zvýrazněného čísla; čára je zároveň legenda čáry
  podložky v grafu widgetu Teploty (Q4: „kužel = tryska, čára = podložka“ shodně s widgetem Teploty). Sjednocení viz
  otázka níže.
- **Otevřená otázka QS3 (uživatel, neblokuje):** má být vyhřívaná podložka i v M / L (a ve widgetu Teploty / pásu
  malého náhledu)? (A, doporučeno) ne – čára tam znaménko nepřipomíná a je legendou grafu / (B) jen v M / L widgetu
  Vychladnutí – stejná ikona ve všech velikostech, ale jiná než ve widgetu Teploty / (C) všude včetně widgetu Teploty
  a pásu – nová ikona by potřebovala varianty 20 (L) a ztratila by vazbu na čáru grafu.
- **Testy:** `SmallCardShowsTheBedIconWhileTheBedLimits` ověřuje `IconHeatedBed16Filled` (12 × 12, barva podložky,
  2 DIP před číslem, 8 jazyků se vejde).
- **Snímky macOS přegenerované a prohlédnuté** (i zvětšený výřez 1×): změněný jen
  `camera-widget-cooldown-cooling-s-bed.png` (vlnky nad deskou před „57 °C“, už se nečte jako „−57 °C“).
- **Windows (ručně):** přegenerovat a prohlédnout `tests/LayerPeek.App.UiTests/Snapshots/camera-widget-cooldown-cooling-s-bed.png`
  (spolu s dříve uvedeným `camera-widget-cooldown-cooling-s.png`).
- **Neověřeno:** skutečné vykreslení při 125 / 150 % na Windows a na Retina displeji (snímky jsou 1×).
- CHANGELOG [0.3.1] a `docs/architecture.md` (Vychladnutí T-088) upraveny na novou ikonu.
