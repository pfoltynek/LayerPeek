---
id: T-163
title: Detekce chyb: tmavý filament na tmavém plátu
status: todo
owner: core-dev
priority: P3
platform: all
depends_on: [T-155]
---

## Cíl
První detektor „Změna na podložce“ (`bed-change`, T-155) porovnává jas pozadí ve výřezu podložky. U tmavého (černého, tmavě šedého) filamentu na černém tiskovém plátu je kontrast malý a detektor je méně spolehlivý. Spike T-152 to uvedl jako známé omezení (otázka 11). Uživatel 2026-11-04 rozhodl: zatím jen známé omezení v popisu detektoru, ale později se má vyřešit – to je tento úkol.

## Rozsah
- Změřit kontrast tmavého filamentu na černém plátu na záběru z Core One (Buddy3D, standardní držák, LED komory) proti světlému filamentu (prototyp T-152: skóre a podíl změněných pixelů při prahu 25 úrovní).
- Navrhnout a ověřit úpravu bez nové závislosti (čistý C#, Native AOT), např.:
  - lokální normalizace kontrastu ve výřezu nebo adaptivní práh podle šumu pozadí místo pevných 25 úrovní,
  - příznaky z hran / gradientu (textura spleti filamentu, okraje dílu) místo samotného jasu,
  - odraz světla LED na lesklém filamentu (malé jasné body),
  - případně rozšíření `DetectionFrameSpec` (např. větší rozlišení výřezu), pokud to výkon dovolí.
- Úprava buď v detektoru `bed-change` (volba / automatické přepnutí podle kontrastu pozadí), nebo samostatný detektor – vybere core-dev podle měření a zapíše do poznámek.
- Výkon a alokace v mezích T-153 / T-155 (ustálený stav bez alokací, vzorek po 2–5 s).
- ML odložil uživatel 2026-11-04 (otázka 12 v T-152), tento úkol ho nepoužívá.

## Akceptační kritéria
- [ ] Změřený kontrast a skóre tmavého filamentu na černém plátu zapsané v poznámkách (data podle otázky 1).
- [ ] Syntetická / inscenovaná selhání s tmavým filamentem na černém plátu se odhalí, úspěšné tisky (světlý i tmavý filament) nehlásí poplach při výchozí citlivosti (testy).
- [ ] Výsledky T-155 pro světlý filament se nezhorší (stejná testovací sada).
- [ ] Popis detektoru v UI (T-154) upravený, známé omezení odstraněné nebo zpřesněné, texty ve všech jazycích.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. **Data:** odkud záběry tmavého filamentu na černém plátu? Záběry z vlastních tisků uživatel plánovaným způsobem nepořizuje (rozhodnutí 2026-11-04 k T-152). Možnosti: inscenovaná scéna (tmavý díl nebo chomáč tmavého filamentu na plátu v nečinné tiskárně se zapnutým osvětlením a nahráváním T-145), automatická nahrávka běžného tisku tmavým filamentem, syntetické varianty (přebarvení dílu na fixture snímcích – méně věrné kvůli odleskům).
2. **Které kombinace podporovat:** jen černý filament na černém (texturovaném / hladkém) plátu, nebo i další málo kontrastní kombinace (např. bílý filament na světlém plátu)?

## Poznámky k implementaci
(vyplní agent po dokončení)
