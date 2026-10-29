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

## Návrh textu (ui-designer 2026-10-29)
Text se zobrazí pod nadpisem systémového dialogu (anglicky „"LayerPeek" would like to find and connect to devices on your local network.“, přesné znění v ostatních jazycích neověřeno), proto o povolení nežádá, jen vysvětluje proč. Oslovení podle zbytku aplikace (de/es/fr vykání, it tykání; pl bez zájmena), termín pro kameru jako v aplikaci (it „telecamera“). Název oprávnění v Nastavení systému → Soukromí a zabezpečení (ověřeno v lokalizaci macOS 27.2, `SecurityPrivacyExtension.appex`): Local Network / Místní síť / Lokálna sieť / Lokales Netzwerk / Red local / Réseau local / Rete locale / Sieć lokalna; panel: Privacy & Security / Soukromí a zabezpečení / Súkromie a bezpečnosť / Datenschutz & Sicherheit / Privacidad y seguridad / Confidentialité et sécurité / Privacy e sicurezza / Prywatność i ochrona.

| Jazyk (`.lproj`) | `NSLocalNetworkUsageDescription` |
|---|---|
| en (základ v `Info.plist`) | LayerPeek connects to your Prusa printers and their cameras on your local network to show the print status and the camera view. |
| cs | LayerPeek se připojuje k vašim tiskárnám Prusa a jejich kamerám v místní síti, aby mohl zobrazit stav tisku a obraz z kamery. |
| sk | LayerPeek sa pripája k vašim tlačiarňam Prusa a ich kamerám v lokálnej sieti, aby mohol zobraziť stav tlače a obraz z kamery. |
| de | LayerPeek verbindet sich mit Ihren Prusa-Druckern und deren Kameras im lokalen Netzwerk, um den Druckstatus und das Kamerabild anzuzeigen. |
| es | LayerPeek se conecta a sus impresoras Prusa y a sus cámaras en la red local para mostrar el estado de la impresión y la imagen de la cámara. |
| fr | LayerPeek se connecte à vos imprimantes Prusa et à leurs caméras sur le réseau local pour afficher l'état de l'impression et l'image de la caméra. |
| it | LayerPeek si collega alle tue stampanti Prusa e alle loro telecamere sulla rete locale per mostrare lo stato della stampa e l'immagine della telecamera. |
| pl | LayerPeek łączy się z drukarkami Prusa i ich kamerami w sieci lokalnej, aby wyświetlać stan drukowania i obraz z kamery. |

Poznámky k implementaci textu: `InfoPlist.strings` v UTF-8 (nebo UTF-16), klíč a hodnota v uvozovkách, apostrof (fr, it) neescapovat, `"` uvnitř hodnoty escapovat. Text není v `Strings.*.resx` (nečte ho aplikace, ale macOS), proto ho nehlídá kontrola úplnosti překladů – přidat test/Pester, že všech 8 souborů existuje a obsahuje klíč. „Prusa“ je jen popisné označení tiskáren (jako „PrusaLink“ v aplikaci), ne logo ani ochranná známka v grafice.

## Otevřené otázky
- ~~Konečné znění textu ve všech jazycích (revize ui-designerem).~~ → navržené znění v 8 jazycích viz „Návrh textu“ výše (revize ui-designer 2026-10-29). Proti návrhu z T-026 jen „camera stream“ → „camera view“ (srozumitelnější pro běžného uživatele, ve všech jazycích „obraz z kamery“). Implementace v tomto úkolu.

## Poznámky k implementaci
(vyplní agent po dokončení)
