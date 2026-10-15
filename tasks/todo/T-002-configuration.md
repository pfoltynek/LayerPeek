---
id: T-002
title: Konfigurace tiskáren a uložení tajných údajů
status: todo
owner: core-dev
priority: P1
depends_on: [T-001]
---

## Cíl
Uložit seznam tiskáren a nastavení aplikace mezi spuštěními.

## Rozsah
- Model nastavení (tiskárny, interval pollingu, povolené notifikace)
- Uložení JSON do `%APPDATA%/PrusaTrayDiagnostic` (Windows) a `~/Library/Application Support/PrusaTrayDiagnostic` (macOS)
- Abstrakce `ISecretStore` pro hesla / API klíče / tokeny

## Akceptační kritéria
- [ ] Načtení/uložení nastavení s testy (vč. chybějícího a poškozeného souboru)
- [ ] Tajné údaje nejsou v JSON v čitelné podobě
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Jak bezpečně ukládat tajné údaje? (Windows DPAPI / Credential Manager, macOS Keychain, nebo knihovna třetí strany?)
- Výchozí interval pollingu (např. 5 s)?

## Poznámky k implementaci
