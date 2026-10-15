---
id: T-015
title: Okno nastavení
status: todo
owner: ui-dev
priority: P1
depends_on: [T-002, T-003]
---

## Cíl
Uživatel přidá/upraví/odebere tiskárnu bez ruční editace souborů.

## Rozsah
- Formulář: název, typ (PrusaLink/Connect), adresa, přihlašovací údaje
- Tlačítko „Otestovat spojení“
- Obecná nastavení (interval, notifikace)

## Akceptační kritéria
- [ ] Validace vstupů, test spojení ukáže srozumitelnou chybu
- [ ] Tajné údaje se ukládají přes `ISecretStore`
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Automatické vyhledání tiskáren v síti (mDNS/Zeroconf) – chceme?

## Poznámky k implementaci
