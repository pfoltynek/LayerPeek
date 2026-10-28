---
id: T-018
title: macOS – ikona a menu v menu baru
status: todo
owner: ui-dev
priority: P1
platform: macos
depends_on: [T-007, T-008, T-095]
---

## Cíl
Ikona stavu (T-007) a tray menu (T-008) fungují a jsou čitelné v menu baru macOS.

> **Pouze na macOS:** úkol implementuje a ověřuje uživatel později na Macu. Neblokuje žádný Windows úkol.

## Rozsah
- Monochromatická „template“ sada ikon pro menu bar (macOS ji sám přebarví pro světlý/tmavý režim), napojená na mapování stavu z T-007
- Výběr sady ikon podle OS (Windows barevná sada z T-007, macOS template sada)
- Ověření tooltipu a menu z T-008 v menu baru

## Akceptační kritéria
- [ ] Na macOS je ikona čitelná ve světlém i tmavém režimu
- [ ] Ikona se mění podle stavu z monitorovací služby i na macOS
- [ ] Menu z T-008 se v menu baru zobrazí, aktualizuje a „Ukončit“ korektně ukončí aplikaci
- [ ] Windows chování z T-007 zůstává beze změny
- [ ] `dotnet build` a `dotnet test` bez chyb a varování (na macOS i Windows)

## Otevřené otázky
- Zobrazovat na macOS progress i jako text vedle ikony v menu baru?
- Jak stavy rozlišit v monochromatické ikoně (tvar/symbol místo barvy)? Navazuje na vizuální styl z T-007.

## Poznámky k implementaci
