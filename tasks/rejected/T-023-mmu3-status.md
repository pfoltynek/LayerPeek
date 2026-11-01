---
id: T-023
title: Stav MMU3
status: rejected
owner: architect
priority: P3
platform: all
depends_on: [T-003]
---

> **Zamítnuto (rozhodnutí uživatele 2026-11-01):** stav slotů, filamentů ani chyby MMU3 neposkytuje PrusaLink (jen `info.mmu`) a Prusa Connect nemá veřejné API pro aplikace třetích stran (T-005). Úkol se nebude implementovat, dokud nebude dostupný oficiální zdroj dat.

## Cíl
Zobrazit stav jednotky MMU3 (aktivní slot, filamenty, chyby MMU) u tiskáren, které ji mají (uživatel: Core One a MK4S s MMU3).

## Rozsah
- Průzkum, odkud data získat. PrusaLink v1 API na firmwaru Buddy 6.x vrací jen `info.mmu` (bool, MMU zapnuta); stav slotů ani chyby MMU neposkytuje (viz `docs/api/prusalink.md`).
- Podle výsledku průzkumu rozšířit doménový model a klienta, nebo úkol uzavřít jako neproveditelný.

## Akceptační kritéria
- [ ] Zdokumentovaný zdroj dat (nebo zdůvodnění, proč to nejde)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Které informace o MMU jsou pro uživatele důležité (aktivní slot, typ/barva filamentu, chyby, počítadla)?
- Pokud data poskytuje jen Prusa Connect (vyřazený – bez veřejného API, T-005), stojí to za to?

## Poznámky k implementaci
