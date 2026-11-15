---
id: T-237
title: macOS – malý náhled po Esc z celé obrazovky má titulkový pruh a je o 28 bodů vyšší (T-038 otázka 2)
status: todo
owner: ui-dev
priority: P2
platform: macos
depends_on: [T-038, T-215]
---

## Cíl
Okno kamery otevřené jako malý náhled (bez rámečku, „vždy navrchu“), přepnuté do celé obrazovky a vrácené klávesou
**Esc**, se po animaci vrátí na místo malého náhledu, ale **s titulkovým pruhem** (zavřít / minimalizovat / zvětšit)
a o výšku pruhu vyšší: 320 × 241 bodů místo 320 × 213, spodní okraj 10 bodů pod pracovní plochou (pod Dockem). Cestou
přes tlačítko malého náhledu v celé obrazovce (`CameraMiniButton`) je výsledek správný. Oprava „umístit malý náhled
znovu po animaci“ je schválená podmíněně v T-038 (otázka 2, 2026-10-30) – kontrola `mac.mini-after-fullscreen` ji při
`FAIL` předává do nového úkolu.

## Zjištění (první běh s odemčenou obrazovkou, 2026-11-15)
- Běh `artifacts/verify/20261115-155430/` (`-Preset release`, commit `24d73aa3`), Mac mini Mac14,12, macOS 27.2.1,
  hlavní displej „iPad Remote“ (Sdílení obrazovky) + „Virtuální 16:10“. Výňatek zprávy:
  „(b) Esc: size 320 x 241 pt at 951, 606, expected 320 x 213 pt at 951, 606 (off: height; tolerance 1 pt); (b) Esc:
  bottom distance -10 pt, expected 18 pt; (b) Esc: has a window frame (a title bar (close, minimize, zoom)), expected
  none“. Uložený malý náhled i cesta (a) „mini preview button“: 320 × 213 bodů, bez rámečku, vrstva 3.
- Důkazy: `evidence/mac.mini-after-fullscreen/placements.json`, `app-vcfd5aa8d.log`.
- Kód: `CameraViewModel.Escape()` nastaví `Mode = _fullScreenOrigin` (Mini) stejně jako `ShowMiniCommand`; rozdíl je
  zřejmě v načasování vůči animaci odchodu z celé obrazovky (styl okna bez rámečku se nastaví dřív, než AppKit animaci
  dokončí a obnoví titulkové okno).
- Uživatel mohl během běhu na Mac sáhnout (nevíme); chybu je proto vhodné nejdřív zopakovat samostatně:
  `caffeinate -d -u -i pwsh tools/macos-verify.ps1 -Unattended -Only mac.mini-after-fullscreen -Allow mac.mini-after-fullscreen`.

## Rozsah
- Po odchodu z celé obrazovky do malého náhledu (všemi cestami: tlačítko, Esc, ⌃⌘F, „Použít“ v Nastavení) po skončení
  animace znovu použít styl bez rámečku, velikost a roh uloženého malého náhledu (T-038 otázka 2).
- Bez změny chování na Windows.

## Akceptační kritéria
- [ ] `mac.mini-after-fullscreen` = `PASS` (všechny tři cesty: velikost ± 1 bod, roh, bez rámečku).
- [ ] Testy (UI / jednotkové) pro pořadí „konec animace → umístění malého náhledu“, kde to jde bez skutečného AppKitu.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování.

## Otevřené otázky
- (žádné – oprava schválena v T-038 otázka 2)

## Poznámky k implementaci
(vyplní agent po dokončení)
