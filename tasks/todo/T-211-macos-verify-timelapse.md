---
id: T-211
title: macOS ověření – timelapse na testovacím bundlu (ruční start / stop, po vrstvách, indikátor, dekódování)
status: todo
owner: qa-reviewer
priority: P2
platform: macos
depends_on: [T-210]
---

> **Rozhodnuto uživatelem 2026-11-13:** přehrání v QuickTime Playeru nahrazuje úplné dekódování přes AVFoundation
> (`helper decode` z T-210). Kontrola přes Accessibility nahrazuje poslech VoiceOveru.

## Cíl
Automatizovat simulátorové části bodů T-161 (timelapse z menu a z okna kamery) a T-159 (volba „Každou N. vrstvu“)
v T-127 a přehrání timelapse v QuickTime (T-160).

## Rozsah
- **Kontrola `mac.timelapse`** (Permission: Zpřístupnění, preset `release`):
  - testovací bundle a simulátor s kamerou, `--speed 20`, složka nahrávek v dočasné složce (jako T-210);
  - `menupress …>Spustit timelapse (experimentální)` → položka se změní na „Zastavit timelapse (experimentální)“;
  - v okně kamery i v malém náhledu je indikátor „TL“ (AX identifikátor a popisek);
  - během scénáře `paused` je indikátor ztlumený. Ztlumení se pozná podle stavu v AX, pokud ho aplikace vystavuje.
    Jinak porovnáním dvou snímků oblasti indikátoru (`regiondiff`, jen se Záznamem obrazovky, bez něj `SKIP` této
    části);
  - zastavení tlačítkem v okně kamery: AXPress, AX popisek „Zastavit timelapse (experimentální)“;
  - „Přehrát poslední timelapse“ je pak povolené (předání souboru systému jako v T-210, otázka 1 tam);
  - soubor `…_timelapse.mp4` projde `decode` OK a počet snímků odpovídá intervalu a době tisku.
- **Kontrola `mac.timelapse-layers`** (Permission, preset `release`): na záložce Nahrávání přepnout volbu „Pořizovat
  snímek“ na „Každou N. vrstvu (odhad)“. Číslo vedle se vymění (interval ↔ „Vrstev mezi snímky“, AX popisky) a tip
  vysvětlí odhad z osy Z (AXHelp). Uložit, znovu otevřít: volba zůstala, je i v `settings.json`. Tisk simulátoru,
  jehož `axis_z` roste po vrstvách: `decode` a počet snímků ≈ počet vrstev + 2 (tolerance podle T-159). Pauza snímek
  nepřidá.
- Selhání timelapse s notifikací („Timelapse selhal“) doplní T-212, uříznutí textu se systémovým písmem T-214.
- Skutečná tiskárna (T-159 vrstvy 0,1 mm, záznam `record-z.sh`) a měření CPU / velikosti s Buddy3D (T-160) zůstávají
  ruční. Přehrání vzniklého `…_timelapse.mp4` z T-160 nahrazuje `helper decode`.

## Akceptační kritéria
- [ ] `mac.timelapse` = `PASS` (uzavře v T-127 bod **T-161** „Ruční start / stop …, indikátor …, tray menu …“ kromě
      notifikace (T-212) a uříznutí (T-214); zjištění AXHelp zakázané položky zapsané)
- [ ] `mac.timelapse-layers` = `PASS` (uzavře v T-127 bod **T-159** „volba je v UI a uloží se“; VoiceOver nahrazený
      AX popisky)
- [ ] U bodu **T-160** v T-127 je přehrání v QuickTime nahrazené `helper decode` (část měření se skutečnou kamerou
      zůstává)
- [ ] Pester / `-SelfTest` a `dotnet build` / `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
