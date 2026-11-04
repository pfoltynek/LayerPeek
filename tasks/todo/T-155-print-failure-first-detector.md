---
id: T-155
title: Detekce chyb tisku – první detektor „Změna na podložce“ (bed-change)
status: todo
owner: core-dev
priority: P2
platform: all
depends_on: [T-152, T-153]
---

## Cíl
První skutečný detektor v `LayerPeek.Detection` nad infrastrukturou z T-153 (nápad 3, fáze 3). **Uživatel 2026-11-04 schválil** klasické CV v čistém C# bez nové závislosti a jako první detektor „Změna na podložce“ (`bed-change`, otázky 1, 2 a 5 v T-152).

## Doporučení spiku T-152 (2026-11-03, schváleno uživatelem 2026-11-04)
- **Detektor „Změna na podložce“** (`bed-change`): zmizení nebo posun dílu a nová hmota na podložce (špagety, odtržený díl). Klasické CV v čistém C# bez nové závislosti.
- **Algoritmus (prototyp ověřený na 214 skutečných snímcích, `docs/detection-spike.md` oddíl 4):** šedotón 320×180, přibližný běžící medián (±K úrovní na vzorek, O(1) na pixel, bez historie snímků) odstraní pohyblivou hlavu a most X. Skóre = podíl pixelů výřezu přední části podložky, kde se pozadí liší o víc než 25 úrovní od pozadí před ~5 min (kruhový seznam několika starších kopií pozadí). Pevný referenční snímek nepoužívat, podložka se pohybuje v Z.
- **Čísla z prototypu (orientační):** úspěšný tisk max 1,44 %, syntetické špagety 3,9 %, syntetický posunutý díl 19,7 %. Výchozí práh ~3 %, citlivost ~2–6 %, potvrzení 5 z 8 vzorků. Záběry skutečných selhání od uživatele nebudou (rozhodnutí 2026-11-04), konečné hodnoty se nastaví na syntetických / inscenovaných datech (viz Testovací data).
- **Výřez:** z nastavení tiskárny (model T-153, editor T-154). Výchozí je celý snímek, u Core One předvyplněný výřez přední části podložky. Detektor musí fungovat s libovolným výřezem (malým i celým snímkem) a skóre počítat jen v něm.
- **Interval vzorkování 2–5 s** (T-153): časové parametry (stáří pozadí ~5 min, rozjezd 1–2 min, potvrzení K z N) se odvozují z času, ne z počtu vzorků. Rychlost běžícího mediánu (±K úrovní na vzorek) se přizpůsobí intervalu.
- **Známé omezení:** tmavý filament na černém plátu (otázka 11 v T-152) – uvést v popisu detektoru (texty v T-154), řešení později v T-163.
- **Výkon:** prototyp 1,36 ms na vzorek (skalární kód, M2 Pro), 0 alokací v ustáleném stavu. Případná optimalizace přes `System.Numerics.Vector` / `Vector128` je bez závislostí.

## Rozsah
- Implementace `IPrintFailureDetector` podle rozhodnutí ze spiku (klasické CV nebo ML), registrace v App (explicitní DI).
- Nová závislost jen podle rozhodnutí ze spiku, s ověřenou licencí (GPL-3.0-only kompatibilní, včetně vah modelu a dat) a kompatibilitou s Native AOT – zapsat do poznámek; verze jen v `Directory.Packages.props`.
- Citlivost → práh skóre (mapování popsat v poznámkách).
- **Testovací data (rozhodnutí uživatele 2026-11-04):** práh a ověření spolehlivosti **nesmí záviset na nahrávkách selhání od uživatele**. Pozitivní případy se tvoří synteticky nebo inscenovaně, zdroj vybere uživatel (otázka 3). Negativní případy jsou úspěšné tisky: fixture `tests/LayerPeek.Detection.Tests/Fixtures/coreone/` (10 snímků) a lokálně celá série 214 snímků z `~/LayerPeek-samples/coreone-frames/` (test, který bez složky skončí jako přeskočený, data mimo repozitář). Synteticky generované varianty vznikají v testovacím kódu za běhu z existujících fixture (žádné nové soubory v repozitáři, limit ~10 snímků z rozhodnutí 2026-11-03 platí). Měřítka přesnosti (podíl odhalených syntetických selhání, falešné poplachy na hodinu úspěšného tisku) zapsat do poznámek. Test na celé sérii úspěšného tisku = žádný poplach při výchozí citlivosti.
- Výkon: CPU a paměť na win-x64 a osx-arm64 (macOS → T-127, pokud nejde ověřit na Windows).

## Akceptační kritéria
- [ ] Detektor odhalí syntetická / inscenovaná selhání z testovací sady a při výchozí citlivosti nehlásí poplach na úspěšných tiscích ze sady (cíle podle otázky 1).
- [ ] Detektor funguje při intervalu vzorkování 2 s i 5 s (stejná sada, časové parametry nezávislé na intervalu) a s výchozím výřezem celého snímku i s výřezem Core One (testy).
- [ ] Popis detektoru uvádí známá omezení (tmavý filament na černém plátu, kvalita první vrstvy, tryska) – předat do textů T-154.
- [ ] Licence všech nových závislostí, vah a dat ověřené a zapsané; Native AOT publish bez nových varování.
- [ ] Spotřeba CPU a paměti změřená a v mezích rozhodnutí z T-153 (otázka 6).
- [ ] Poplach z detektoru projde celou cestou T-153 / T-154 (notifikace, snímek, falešný poplach) – E2E se záznamem v simulátoru, pokud simulátor umí přehrát vlastní klip (jinak rozšířit simulátor v rámci úkolu).
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. ~~**Typ prvního detektoru**~~ *(vyřešeno 2026-11-04: „Změna na podložce“, klasické CV.)* **Cílová přesnost** zůstává otevřená: např. odhalit X % syntetických selhání, nejvýš Y falešných poplachů za 10 h úspěšného tisku.
2. **Simulátor:** má simulátor umět přehrávat vlastní klip (nahrávku selhání) místo vestavěné fixture, aby šly E2E testy detektoru? (Rozšíření `tools/PrusaSimulator` volbou typu `--camera-clip <soubor>`.)
3. **Zdroj syntetických / inscenovaných dat** (uživatel 2026-11-04: „záběry vytvoříme jinak“). Možnosti, rozhodne uživatel:
   a) varianty generované v testech ze snímků `~/LayerPeek-samples` a fixture (vložená „hmota“ / spleť čar, posunutý nebo zmizelý výřez dílu, změna jasu, šum H.264),
   b) inscenovaná scéna v nečinné tiskárně se zapnutým osvětlením a nahráváním (T-145): na podložku položit / odebrat předmět nebo chomáč filamentu, posunout hotový díl,
   c) simulátor přehrávající upravený klip (souvisí s otázkou 2),
   d) kombinace a) + b). Veřejná videa jiných autorů jen s ověřenou licencí (nedoporučeno).

## Poznámky k implementaci
(vyplní agent po dokončení)
