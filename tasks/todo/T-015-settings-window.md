---
id: T-015
title: Okno nastavení
status: todo
owner: ui-dev
priority: P1
platform: all
depends_on: [T-002, T-003]
---

## Cíl
Uživatel přidá/upraví/odebere tiskárnu bez ruční editace souborů.

## Rozsah
- Formulář: název, typ (PrusaLink/Connect), adresa, přihlašovací údaje
- Tlačítko „Otestovat spojení“
- Obecná nastavení (interval, notifikace)
- Registrace `ISecretStore` v composition rootu (App) s výběrem platformní implementace: na Windows `WindowsCredentialSecretStore` (T-002), macOS větev (`Keychain`) doplní T-021. Pokud službu potřebuje zaregistrovat už dřívější úkol (např. při napojení PrusaLink klientů na monitoring), registrace se přesune tam a tento bod se jen ověří.

## Akceptační kritéria
- [ ] Validace vstupů, test spojení ukáže srozumitelnou chybu
- [ ] Tajné údaje se ukládají přes `ISecretStore`
- [ ] Composition root vybírá implementaci `ISecretStore` podle OS (explicitní registrace, bez skenování – Native AOT)
- [ ] Ověřeno na Windows
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Automatické vyhledání tiskáren v síti (mDNS/Zeroconf) – chceme?
- Co má aplikace dělat na OS, kde zatím není implementace `ISecretStore` (macOS před dokončením T-021): nespustit se s chybou, zakázat ukládání hesel v UI, nebo jiné chování?

## Poznámky k implementaci
