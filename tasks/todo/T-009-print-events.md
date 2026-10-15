---
id: T-009
title: Detekce událostí tisku
status: todo
owner: core-dev
priority: P1
depends_on: [T-006]
---

## Cíl
Z posloupnosti snapshotů odvodit události, na které se posílají notifikace.

## Rozsah
- Události: tisk zahájen, dokončen, pozastaven, obnoven, zrušen, chyba, vyžaduje pozornost (např. došel filament), tiskárna offline/online
- Deduplikace (stejná událost se neposílá opakovaně)
- Abstrakce `INotificationService` v Core (implementace v App – T-010)

## Akceptační kritéria
- [ ] Tabulkové testy přechodů stavů
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Upozornit i na „zbývá X minut do konce“? Pokud ano, jaký práh?

## Poznámky k implementaci
