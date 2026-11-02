---
id: T-135
title: Kontrola mac.menubar na macOS 27 nenajde stavovou položku
status: done
owner: qa-reviewer
priority: P2
platform: macos
depends_on: [T-105]
---

## Cíl
Na macOS 27.2 hlásil `mac.menubar` (T-115, 2026-11-01) FAIL „no status item (window layer 25) of the app … no process
has a status item window (layer 25)“, i když aplikace stavovou položku měla. macOS 27 kreslí stavové položky mimo okna
procesu: aplikace nemá v `CGWindowListCopyWindowInfo` žádné okno a žádný proces nemá okno na vrstvě 25 (v menu baru jsou
jen okna vrstvy 23–24 procesů Window Server, MenuBarAgent, Siri a Oznamovací centrum). Položku vidí jen Accessibility API
(`AXExtrasMenuBar` aplikace).

## Rozsah
- `tools/macos-verify-helper.swift`: příkaz `statusitems <pid>` – položky `AXExtrasMenuBar` (rámeček, titulek, popis),
  potřebuje Zpřístupnění.
- `tools/macos-verify.ps1`: `Test-StatusItem` – okno na vrstvě 25 (macOS 15) nebo položka `AXExtrasMenuBar`; použité
  v `mac.menubar` a `mac.display-sleep`. Bez Zpřístupnění a bez okna na vrstvě 25 `mac.menubar` skončí SKIP s návodem
  (ne FAIL); důkaz `status-items.json`.

## Akceptační kritéria
- [x] `mac.menubar` na macOS 27.2 se Zpřístupněním pro Terminál = PASS
- [x] Pester `tools/VerifyTools.Tests.ps1` a `LayerPeek.Scripts.Tests` projdou
- [x] `dotnet build` bez chyb a varování

## Otevřené otázky
(žádné)

## Poznámky k implementaci
- 2026-11-01 (macOS 27.2): `mac.menubar` PASS, `status-items.json` = jedna položka 24 × 24 pt v menu baru. Odpověď na
  otázku 6 z T-105: na macOS 27.2 nejsou v `menubar-windows.json` žádná okna vrstvy 25 (ani systémových položek);
  stavové položky jsou vidět jen přes Accessibility. Pester 36 / 36, `LayerPeek.Scripts.Tests` 12 prošlo, 1 přeskočen
  (běh na vyžádání).
