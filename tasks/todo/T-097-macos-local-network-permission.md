---
id: T-097
title: macOS – oprávnění „Místní síť“ (NSLocalNetworkUsageDescription)
status: todo
owner: ui-dev
priority: P1
platform: macos
depends_on: [T-019, T-026]
---

## Cíl
Od macOS 15 potřebuje aplikace pro každé odchozí spojení na adresu v místní síti oprávnění „Místní síť“ (Apple TN3179). Týká se to PrusaLink i RTSP kamery, tedy celé aplikace. Bez popisu v `Info.plist` ukáže macOS obecný dialog. Zjištěno při T-026 (2026-10-29).

## Rozsah
- Klíč `NSLocalNetworkUsageDescription` v `build/macos/Info.plist` (anglický základ) a přeložené texty ve všech 8 jazycích aplikace přes `<jazyk>.lproj/InfoPlist.strings` v `Contents/Resources` (rozhodnuto 2026-10-29: přeložený).
- Návrh textu z T-026: „LayerPeek connects to your Prusa printers and their cameras on your local network to show the print status and the camera stream.“ Texty projde ui-designer.
- Sestavení `.lproj` v kroku `app` (`scripts/release-build.ps1`, `New-MacAppBundle`), podpis bundlu po přidání souborů, `codesign --verify --deep --strict`.
- Chování při odepření oprávnění: srozumitelný stav tiskárny / hláška místo obecné chyby spojení (ověřit, jakou chybu macOS vrací), záznam do logu. Případně odkaz do Nastavení systému → Soukromí a zabezpečení → Místní síť.
- Ověřit, zda macOS s ad-hoc podpisem po aktualizaci aplikace žádá o oprávnění znovu.
- Testy na loopbacku (simulátor) se oprávnění netýkají; procesy spuštěné z Terminálu jsou výjimkou, proto se dialog při vývoji neukáže.

## Akceptační kritéria
- [ ] `Info.plist` obsahuje `NSLocalNetworkUsageDescription`, `.app` obsahuje `InfoPlist.strings` pro všech 8 jazyků (test / Pester)
- [ ] Podepsaný bundle projde `codesign --verify --deep --strict`
- [ ] Dialog „Místní síť“ ukáže text v jazyce systému (ověřeno uživatelem na Macu)
- [ ] Odepřené oprávnění nezpůsobí pád a je srozumitelně ohlášeno
- [ ] Windows beze změny
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Konečné znění textu ve všech jazycích (revize ui-designerem).

## Poznámky k implementaci
(vyplní agent po dokončení)
