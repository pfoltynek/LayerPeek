---
id: T-148
title: Nahrávání kamery fáze 2 – „dashcam“ (kruhový buffer posledních minut, uložení při chybě tisku)
status: todo
owner: core-dev + ui-dev
priority: P3
platform: all
depends_on: [T-145, T-146]
---

## Cíl
Místo nahrávání celého tisku držet v paměti (nebo v dočasných segmentech na disku) jen **posledních N minut** streamu a uložit je jako nahrávku, když se stane něco zajímavého – hlavně při chybě tisku. Šetří disk a přitom zachytí, co se stalo těsně před problémem (nápad 1, fáze 2).

## Rozsah
- **core-dev:** kruhový buffer zakódovaných paketů (bez překódování, stejný princip jako T-144) pro každou tiskárnu, odběr paketů ze sdíleného spojení (T-143).
  - Buffer drží celé GOP (od klíčového snímku), aby šel uložit přehratelně.
  - Spouštěče uložení podle otázky 2 (`Error`, `Attention`, `Stopped`, detekce chyby z T-155 – jen pokud už bude hotová, není to závislost, ručně).
  - Uložení = buffer + volitelně ještě M sekund po události; jde do stejné složky a pod stejnou rotaci jako T-145.
  - Omezení paměti (datový tok z T-144 × délka bufferu); případně buffer v dočasných segmentech na disku (otázka 3).
- **ui-dev:** nastavení (zapnutí, délka bufferu) a ruční „Uložit posledních N minut“ podle návrhu T-146.

## Akceptační kritéria
- [ ] Buffer drží nejvýš nastavenou délku a začíná klíčovým snímkem (test s fixture / simulátorem a falešnými hodinami).
- [ ] Uložení po spouštěči vytvoří přehratelný soubor s obsahem před událostí (a po ní podle rozhodnutí) (test).
- [ ] Paměť bufferu je omezená a změřená (poznámky).
- [ ] Nové texty ve všech jazycích; testy po sobě uklidí soubory i po pádu.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. **Délka bufferu:** výchozí N minut (např. 2, 5, 10?) a rozsah v nastavení.
2. **Spouštěče uložení:** chyba tisku, „vyžaduje zásah“, zastavení uživatelem, detekce chyby (T-155), ručně – které a s jakým výchozím stavem? Ukládat i M sekund po události (kolik)?
3. **Buffer v paměti, nebo na disku** (dočasné segmenty)? Paměť je rychlá a nic nezapisuje, disk zvládne delší buffer.
4. **Dashcam místo, nebo vedle** automatického nahrávání celého tisku z T-145?

## Poznámky k implementaci
(vyplní agent po dokončení)
