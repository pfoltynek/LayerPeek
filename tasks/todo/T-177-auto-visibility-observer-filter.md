---
id: T-177
title: Automatické zobrazení okna kamery – nebudit UI vlákno, když se stav nezměnil
status: todo
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
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
