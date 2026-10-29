---
id: T-098
title: macOS – ikona aplikace s okrajem podle Applu
status: todo
owner: ui-designer
priority: P3
platform: macos
depends_on: [T-019, T-025]
---

## Cíl
Ikona aplikace (`app-icon.svg` / `app-icon-1024.png` z T-025) vyplňuje skoro celé plátno 1024 px. Ikony na macOS mají od verze 11 kolem těla okraj (tělo zhruba 824 × 824 px na plátně 1024 px, podle Apple Human Interface Guidelines), takže ikona LayerPeeku v Docku, Finderu a ⌘Tab působí větší než ostatní. Zjistil ui-designer při revizi textů (2026-10-29); úkol založen rozhodnutím uživatele.

## Rozsah
- Varianta ikony pro macOS s okrajem a tvarem podle aktuálních Apple Human Interface Guidelines (rozměr těla, případně stín); malé velikosti 16 a 32 px dál ze zjednodušené podoby (T-019).
- Sestavení `.icns` v kroku `app` (`New-MacIcon` v `scripts/ReleaseTools/ReleaseTools.psm1`) z nové varianty.
- Ikona pro Windows (`app-icon.ico`, exe, okna) beze změny.
- Porovnání vedle systémových aplikací v Docku a Finderu (ověří uživatel na Macu).

## Akceptační kritéria
- [ ] `.icns` obsahuje variantu s okrajem ve všech velikostech, test / Pester kontroluje rozměry
- [ ] V Docku a Finderu působí stejně velká jako systémové aplikace (ověřeno uživatelem)
- [ ] Windows ikona beze změny
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
