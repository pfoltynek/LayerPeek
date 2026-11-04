---
id: T-154
title: Detekce chyb tisku – UI (nastavení detektorů, notifikace poplachu, falešný poplach)
status: todo
owner: ui-designer + ui-dev
priority: P2
platform: all
depends_on: [T-153, T-033]
---

## Cíl
Uživatelské rozhraní detekce chyb tisku (nápad 3): zapnutí / vypnutí každého typu detektoru a jeho citlivost, notifikace poplachu se snímkem a možnost označit poplach jako falešný. Logika je v T-153, první detektor v T-155.

## Rozsah
- **ui-designer – návrh (design spec v `docs/design/`):**
  - umístění nastavení detektorů (otázka 1): seznam typů detektorů (rozšiřitelný, nové typy přibudou bez přepracování UI – po vzoru záložky Widgety, T-092), u každého zapnuto / vypnuto, citlivost a krátký popis, co detektor umí a neumí,
  - vysvětlení, že detekce běží na pozadí jen během tisku a jen s kamerou,
  - **editor výřezu podložky (rozhodnutí uživatele 2026-11-04):** v nastavení tiskárny obdélník nakreslený nad náhledem kamery (náhled T-034), přesun a změna velikosti myší, tlačítko „Celý snímek“ (výchozí) a u Core One „Výchozí pro Core One“ (předvyplněný výřez z T-153). Ovládání i klávesnicí a přístupné popisky (UI Automation). Výřez se ukládá v normalizovaných souřadnicích (model T-153),
  - **interval vzorkování 2–5 s** (výchozí 2 s, T-153) – ovládací prvek a umístění navrhne ui-designer (u tiskárny, nebo globálně – otázka 1),
  - notifikace poplachu (text, snímek v toastu – otázka 2) a kde je akce „Falešný poplach“ (otázka 3),
  - bez posuvníku v 780 × 532 (T-094) a s dlouhými překlady.
- **ui-dev – implementace:** nastavení, editor výřezu podložky, interval vzorkování, notifikace (Windows toast i macOS `UNUserNotificationCenter` – stejné texty jako u ostatních upozornění, T-010 / T-020), akce „Falešný poplach“, nové texty ve všech jazycích (T-029), compiled bindings.
- **Testy:** headless UI testy nastavení, E2E na Windows s falešným detektorem v testovacím režimu (jen Debug, jako T-067), který vyvolá poplach během scénáře tisku simulátoru.

## Akceptační kritéria
- [ ] Design spec schválený uživatelem (odpovědi na otázky zapsané v úkolu).
- [ ] Každý typ detektoru jde zapnout / vypnout a nastavit citlivost; nastavení se uloží a po restartu platí.
- [ ] Editor výřezu podložky nad náhledem kamery: nakreslení, přesun a změna velikosti obdélníku myší i klávesnicí, „Celý snímek“ a u Core One „Výchozí pro Core One“. Výřez se uloží u tiskárny a po restartu platí (headless UI test s falešným snímkem, převod souřadnic náhledu na normalizované souřadnice i při jiném poměru stran a letterboxu).
- [ ] Interval vzorkování jde nastavit jen v rozsahu 2–5 s (test).
- [ ] Poplach zobrazí notifikaci podle návrhu (E2E na Windows; macOS → T-127).
- [ ] „Falešný poplach“ funguje z místa podle návrhu a uloží záznam (T-153).
- [ ] Nové texty ve všech jazycích; okno nastavení bez posuvníku i s nejdelším překladem.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; Native AOT publish bez nových varování.

### Rozhodnutí uživatele 2026-11-04 (předáno při T-153)
- Po instalaci je detekce **vypnutá** (hlavní vypínač i zapnutí u tiskárny).
- Nastavení u tiskárny (vedle Kamery): zapnutí, citlivost, výřez. Globálně na záložce „Nahrávání“: hlavní vypínač, režim opakování poplachu a ukládání snímků poplachů.
- Toast poplachu ukáže snímek, klik otevře okno kamery, akce „Falešný poplach“ je přímo v toastu. Zvuk stejný jako u chyby tisku.
- Editor výřezu použije při nedostupné kameře poslední uložený snímek.

## Otevřené otázky
1. **Umístění nastavení:** *vyřešeno 2026-11-04, viz výše (interval vzorkování: otázka 8)* Původně: u tiskárny (podzáložka vedle Kamera), nebo globálně? Citlivost u tiskárny, nebo globálně?
2. **Notifikace:** *vyřešeno 2026-11-04: se snímkem, klik otevře okno kamery* Původně: se snímkem poplachu v toastu (na Windows jde obrázek v toastu jen ze souboru / URI), nebo jen text? Klik na notifikaci: otevřít okno kamery, snímek, nebo web PrusaLink?
3. **Falešný poplach:** *vyřešeno 2026-11-04: akce přímo v toastu (historie poplachů nerozhodnuta)* Původně: tlačítko přímo v notifikaci (akce toastu), v okně kamery, nebo v historii poplachů? Má aplikace mít seznam / historii poplachů?
4. **Výchozí stav detektorů:** *vyřešeno 2026-11-04: detekce po instalaci vypnutá* Původně: po instalaci vypnuté (uživatel je musí zapnout), nebo první detektor zapnutý?
5. **Zvuk** *vyřešeno 2026-11-04: stejný jako u chyby tisku* Původně: poplachu: stejný jako u chyby tisku, nebo výraznější?
6. ~~**Výřez podložky**~~ – *vyřešeno 2026-11-04 (otázka 8 v T-152):* editor výřezu patří do tohoto úkolu, už do první verze detekce, protože uživatel podporuje různé tiskárny a držáky kamery.
7. **Náhled bez spojení:** *vyřešeno 2026-11-04: s posledním uloženým snímkem (kde se ukládá: otázka 10)* Původně: má editor výřezu fungovat i s posledním uloženým snímkem, když kamera právě není dostupná, nebo jen s živým náhledem?
8. **Interval vzorkování – u tiskárny, nebo globálně?** (z T-153, 2026-11-04) T-153 ho drží globálně (`AppSettings.Detection.SampleInterval`, 2–5 s, výchozí 2 s), protože rozhodnutí 2026-11-04 ho nepřiřadilo ani k tiskárně, ani ke globálním volbám. Přesun k tiskárně je malá změna modelu a mapování nastavení.
9. **Obrázek v toastu bez uloženého snímku** (z T-153): s vypnutým „Ukládat snímky poplachů“ má poplach snímek jen v paměti (`PrintFailureAlarm.SnapshotJpeg`, `SnapshotPath` = `null`). Toast na Windows bere obrázek jen ze souboru nebo URI. Zapsat ho dočasně (např. do datové složky a po zavření toastu smazat), nebo toast bez obrázku?
10. **Poslední snímek pro editor výřezu** (z T-153): detekce drží jen šedotónové vzorky 320×180 a nic neukládá mimo poplachy. Poslední snímek pro editor (otázka 7) by mohl ukládat náhled v nastavení (T-034) při zavření, nebo by šlo použít nejnovější snímek poplachu ze složky tiskárny. Rozhodne návrh T-154; infrastruktura T-153 pro to nic neukládá.
- Podklady z T-153 pro UI: nastavení `PrintFailureDetectionSettings` (globální: `Enabled`, `AlarmRepeat`, `SaveSnapshots`, `SampleInterval`) a `PrinterDetectionSettings` (u tiskárny: `Enabled`, `Region`, `Detectors[id]` → `Enabled`, `Sensitivity` 0–100), výchozí výřez `PrinterDetectionSettings.RegionFor(printer)` (`DetectionRegion.CoreOneBed` pro tlačítko „Výchozí pro Core One“), seznam detektorů `PrintFailureDetectionService.Detectors`, notifikace `INotificationService.ShowPrintFailure(PrintFailureNotification)`, akce „Falešný poplach“ `TryGetAlarm(id)` a `ReportFalseAlarmAsync(alarm)`.

Podklad ze spiku T-152 pro popis detektoru v UI: „Změna na podložce“ pozná zmizení nebo posun dílu a novou hmotu na podložce. Nepozná kvalitu první vrstvy, problémy na trysce ani chyby uvnitř dílu. U tmavého filamentu na černém plátu je méně spolehlivý (známé omezení, řešení později v T-163). Běží jen během tisku a se svítícím osvětlením komory.

## Poznámky k implementaci
(vyplní agent po dokončení)
