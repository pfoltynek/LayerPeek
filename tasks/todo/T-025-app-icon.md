---
id: T-025
title: Ikona aplikace
status: todo
owner: ui-dev
priority: P2
platform: all
depends_on: [T-007]
---

## Cíl
Aplikace má vlastní ikonu (exe, okna, toast notifikace), aby nebyla zobrazena obecná ikona Windows.

## Rozsah
- Neutrální vlastní symbol odvozený z tray ikony (T-007), **bez loga a ochranné známky Prusa**
- Windows: `.ico` s více velikostmi (16–256 px), `ApplicationIcon` v App projektu, ikona oken (O aplikaci, další okna)
- Ikona zástupce ve Start menu / toastů (zástupce z T-010 přebírá ikonu z exe)
- macOS: `.icns` řeší T-019 (balení), zde jen připravit zdrojový obrázek

## Akceptační kritéria
- [ ] Exe, okna a toasty na Windows zobrazují vlastní ikonu
- [ ] Zdrojová grafika je v repozitáři (vektor nebo PNG ve vysokém rozlišení)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Vzhled ikony (návrh ke schválení uživatelem).

## Poznámky k implementaci
