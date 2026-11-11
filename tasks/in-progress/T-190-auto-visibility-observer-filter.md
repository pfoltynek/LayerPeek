---
id: T-190
title: Automatické zobrazení okna kamery – nebudit UI vlákno, když se stav nezměnil
status: in-progress
owner: ui-dev
priority: P3
platform: all
depends_on: []
---

## Cíl
Výkonnostní analýza (2026-11-06): `CameraWindowAutoVisibilityObserver` (T-151) posílá na UI vlákno
(`_dispatcher.Post(... automaton.Process(snapshot))`) každý stav tiskárny, i když se pro automat nic nezměnilo.
Je to jediné nefiltrované místo mezi monitorem a UI. Dopad je zanedbatelný, ale oprava je malá.

## Rozsah
- Před `Post` porovnat jen údaje, na které automat reaguje (stav tiskárny, dosažitelnost…), s posledním odeslaným
  stavem dané tiskárny. Beze změny nic neposílat.
- Chování automatu se nemění, včetně časových prodlev. Pokud automat potřebuje pravidelný „tik“ času, musí ho
  dostat jinou cestou.

## Akceptační kritéria
- [ ] Opakovaný stejný stav tiskárny se na UI vlákno nepošle (test s falešným dispečerem).
- [ ] Stávající testy automatického zavření a znovuotevření procházejí beze změny.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
Analýza ui-dev (2026-11-11), implementace zastavena – prosté filtrování by změnilo chování automatu:

- Automat (`CameraWindowAutoVisibility`, T-150) **potřebuje i opakované stejné snímky**: každý `Process` posune
  `LastSnapshotAt` a z něj `IsFresh`/`StaleAfter` (140 s s výchozím nastavením) pozná spánek počítače nebo zaseknutý
  monitoring (rozhodnutí 9). Kdyby pozorovatel neměněné snímky zahazoval, „stav“ by po 140 s bez změny zastaral:
  - okno odložené během offline (režim rozložení, kontextové menu) by se po skončení odkladu delším než 140 s **nezavřelo**
    (pokrývá test `LongDeferralWithOfflineBackOffSnapshotsClosesWhenItEnds`);
  - okno otevřené uživatelem u tiskárny, která je dlouho online beze změny, by dostalo `HoldUntilOnline` a při výpadku
    by se **nezavřelo** (`HoldUntilOnline` maže až další online snímek, který by se zahodil).
- Jednorázový časovač to nenahradí: zastarání má právě poznat, že snímky přestaly chodit, a to časovač nezjistí.

Možnosti (rozhodnutí uživatele):
1. **Udržovací snímek s omezením**: neměněný stav se pošle nejvýš jednou za N (např. `StaleAfter / 4` ≈ 35 s místo každých
   5 s). Změny stavu se posílají hned. Za běžného provozu se chování nezmění; rozdíl je jen v toleranci spánku: zastarání
   by mohlo nastat už při mezeře mezi 105 a 140 s (nyní až nad 140 s), tj. rezerva 2× nejdelší pravidelná mezera klesne na
   1,5×. Malá změna jen v pozorovateli.
2. **Přesné zachování s úpravou Core**: pozorovatel by si na vlákně monitoru zapisoval čas posledního snímku každé tiskárny
   a automat by dostal nové API (např. `Process(snapshot, receivedAt)` nebo zdroj „kdy přišel poslední snímek“), aby se
   doručení dalo odložit na chvíli, kdy UI vlákno stejně běží. Chování zůstane přesně stejné, ale mění se kontrakt automatu
   („čas z `TimeProvider` při volání“) a je to výrazně složitější než přínos (dopad je podle zadání zanedbatelný).
3. **Úkol zrušit / nechat beze změny**: jedno `Post` na tiskárnu za interval dotazování (výchozí 5 s) je levné.

Doporučení ui-dev: 3, případně 1, pokud je změna tolerance spánku přijatelná.

## Poznámky k implementaci
(vyplní agent po dokončení)
