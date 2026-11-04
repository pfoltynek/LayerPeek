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

## Otevřené otázky
1. **Umístění nastavení:** u tiskárny (podzáložka vedle Kamera), nebo globálně? Citlivost u tiskárny, nebo globálně?
2. **Notifikace:** se snímkem poplachu v toastu (na Windows jde obrázek v toastu jen ze souboru / URI), nebo jen text? Klik na notifikaci: otevřít okno kamery, snímek, nebo web PrusaLink?
3. **Falešný poplach:** tlačítko přímo v notifikaci (akce toastu), v okně kamery, nebo v historii poplachů? Má aplikace mít seznam / historii poplachů?
4. **Výchozí stav detektorů:** po instalaci vypnuté (uživatel je musí zapnout), nebo první detektor zapnutý?
5. **Zvuk** poplachu: stejný jako u chyby tisku, nebo výraznější?
6. ~~**Výřez podložky**~~ – *vyřešeno 2026-11-04 (otázka 8 v T-152):* editor výřezu patří do tohoto úkolu, už do první verze detekce, protože uživatel podporuje různé tiskárny a držáky kamery.
7. **Náhled bez spojení:** má editor výřezu fungovat i s posledním uloženým snímkem, když kamera právě není dostupná, nebo jen s živým náhledem?

Podklad ze spiku T-152 pro popis detektoru v UI: „Změna na podložce“ pozná zmizení nebo posun dílu a novou hmotu na podložce. Nepozná kvalitu první vrstvy, problémy na trysce ani chyby uvnitř dílu. U tmavého filamentu na černém plátu je méně spolehlivý (známé omezení, řešení později v T-163). Běží jen během tisku a se svítícím osvětlením komory.

## Poznámky k implementaci
(vyplní agent po dokončení)
