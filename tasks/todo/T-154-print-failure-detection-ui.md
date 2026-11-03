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
  - notifikace poplachu (text, snímek v toastu – otázka 2) a kde je akce „Falešný poplach“ (otázka 3),
  - bez posuvníku v 780 × 532 (T-094) a s dlouhými překlady.
- **ui-dev – implementace:** nastavení, notifikace (Windows toast i macOS `UNUserNotificationCenter` – stejné texty jako u ostatních upozornění, T-010 / T-020), akce „Falešný poplach“, nové texty ve všech jazycích (T-029), compiled bindings.
- **Testy:** headless UI testy nastavení, E2E na Windows s falešným detektorem v testovacím režimu (jen Debug, jako T-067), který vyvolá poplach během scénáře tisku simulátoru.

## Akceptační kritéria
- [ ] Design spec schválený uživatelem (odpovědi na otázky zapsané v úkolu).
- [ ] Každý typ detektoru jde zapnout / vypnout a nastavit citlivost; nastavení se uloží a po restartu platí.
- [ ] Poplach zobrazí notifikaci podle návrhu (E2E na Windows; macOS → T-127).
- [ ] „Falešný poplach“ funguje z místa podle návrhu a uloží záznam (T-153).
- [ ] Nové texty ve všech jazycích; okno nastavení bez posuvníku i s nejdelším překladem.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; Native AOT publish bez nových varování.

## Otevřené otázky
1. **Umístění nastavení:** u tiskárny (podzáložka vedle Kamera), nebo globálně? Citlivost u tiskárny, nebo globálně?
2. **Notifikace:** se snímkem poplachu v toastu (na Windows jde obrázek v toastu jen ze souboru / URI), nebo jen text? Klik na notifikaci: otevřít okno kamery, snímek, nebo web PrusaLink?
3. **Falešný poplach:** tlačítko přímo v notifikaci (akce toastu), v okně kamery, nebo v historii poplachů? Má aplikace mít seznam / historii poplachů?
4. **Výchozí stav detektorů:** po instalaci vypnuté (uživatel je musí zapnout), nebo první detektor zapnutý?
5. **Zvuk** poplachu: stejný jako u chyby tisku, nebo výraznější?

## Poznámky k implementaci
(vyplní agent po dokončení)
