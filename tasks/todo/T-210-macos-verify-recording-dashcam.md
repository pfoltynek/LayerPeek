---
id: T-210
title: macOS ověření – nahrávání a dashcam na testovacím bundlu a dekódování souborů přes AVFoundation (`decode`)
status: todo
owner: qa-reviewer
priority: P2
platform: macos
depends_on: [T-208, T-209]
---

> **Rozhodnuto uživatelem 2026-11-13:** přehrání v QuickTime Playeru nahrazuje úplné dekódování přes AVFoundation:
> každý snímek, délka, přeskakování (seek). Kontrola stavu přes Accessibility (popisek / stav prvku) nahrazuje
> poslech VoiceOveru.

## Cíl
Automatizovat simulátorové části bodů T-146 (ruční nahrávání) a T-148 (dashcam) v T-127 a přehrání nahrávek v QuickTime
(T-147, T-145) na testovacím bundlu `LayerPeek E2E.app` se simulátorem, bez skutečné tiskárny.

## Rozsah
- **Helper `decode <soubor.mp4>`** (AVFoundation, bez nových závislostí) vypíše JSON:
  - `isPlayable`, `isReadable`, kodek stopy (`avc1`), rozměry, `duration` a počet snímků;
  - průchod všemi vzorky přes `AVAssetReader` (dekodér QuickTime): počet dekódovaných snímků, chyby, monotónní PTS;
  - `seek`: `AVAssetImageGenerator` s nulovou tolerancí na 0 %, 25 %, 50 %, 75 % a konci délky;
  - průměrný jas a rozptyl každého vygenerovaného snímku (černý nebo jednobarevný snímek = chyba).
  Exit 1, když se některý snímek nedekóduje nebo seek selže. Stejný příkaz použijí T-211 a T-213.
- **Simulátor:** ovládací endpointy `POST /_simulator/camera/offline` a `POST /_simulator/camera/online` (dnes jen
  příkaz konzole `camera offline` / `camera online`). Odpojí klienty a nepřijímá nová spojení stejně jako příkaz
  konzole, platí stejná pravidla přihlášení jako u ostatních ovládacích endpointů. Testy v `tests/PrusaSimulator.Tests`,
  doplnit `docs/simulator.md`. Použijí je i T-213 (nahrávka s výpadkem, `…-2.mp4`) a T-215 (T-188).
- Nahrávka s výpadkem kamery: `camera/offline` na ~10 s simulovaného času během nahrávání → `…mp4` a `…-2.mp4`, oba
  `decode` OK.
- **Izolace:** složka nahrávek testovacího bundlu je v dočasné složce běhu (nastavení testovacích dat). Nikdy
  `~/Movies/LayerPeek`. Simulátor je jen na 127.0.0.1. Na konci se vše smaže.
- **Kontrola `mac.recording`** (Permission: Zpřístupnění, preset `release`):
  - simulátor s kamerou a `--speed 20`;
  - v podmenu tiskárny (`helper menu`) je za oddělovačem „Nahrávat“, „Přehrát poslední nahrávku“ (zakázané, dokud
    nahrávka neexistuje) a „Otevřít složku s nahrávkami“;
  - `menupress …>Nahrávat`: tooltip ikony je „Nahrává se“ (`statusitems`), v okně kamery je prvek „● REC“ (AX
    identifikátor) ve všech třech režimech okna a pod ukazatelem tlačítko Stop;
  - zastavení jednou z menu („Zastavit nahrávání“), podruhé tlačítkem v okně (AXPress), oba soubory `decode` = OK;
  - „Přehrát poslední nahrávku“: v testovacím režimu se ověří jen předání souboru systému. QuickTime se nespouští,
    viz otázka 1;
  - „Otevřít složku s nahrávkami“: Finder ukáže podsložku tiskárny. Finder AX okno, AXDocument = složka. Okno se potom
    zavře;
  - nezapisovatelná složka nahrávek (`chmod 0500` v dočasné složce, nastavená v `settings.json` testovacích dat) ukáže
    v Nastavení › Nahrávání hlášku (AX text), aplikace nespadne.
- **Kontrola `mac.dashcam`** (Permission, preset `release`):
  - tiskárna se zapnutým „Uchovávat poslední minuty (dashcam)“, bez automatického nahrávání;
  - mimo tisk je „Uložit posledních 2 min“ v podmenu zakázané (AXHelp zapsat), během tisku povolené;
  - totéž v místní nabídce okna kamery (`ctrlclick` + AX menu);
  - klik → za ~30 s simulovaného času soubor `…_dashcam-manual.mp4`, `decode` OK, délka v očekávaném rozsahu;
  - scénáře simulátoru `error` / `attention` / `stopped` přes HTTP během tisku → `…_dashcam-error.mp4` /
    `…-attention.mp4` / `…-stopped.mp4`, každý `decode` OK;
  - automatické nahrávání tisku (T-145, simulátorová část): scénář `printing` → `finished`, soubor
    `<datum>_<čas>_<název tisku>.mp4` vznikne a uzavře se asi minutu po konci, během nahrávání je „Uložit posledních
    2 min“ zakázané s tipem „Nahrává se celý tisk.“, další tisk = nový soubor, `decode` OK.
- Notifikace („Nahrávání kamery selhalo“ a klik na ni) sem nepatří: doplní je T-212 do těchto kontrol. Systémový dialog
  složky („Změnit…“) doplní T-213. Uříznutí textu se systémovým písmem pokryje T-214.
- Paměť dashcamu se skutečnou Buddy3D (+~17 MB) a obsah „~2 min před / ~30 s po“ se skutečnou kamerou zůstávají ruční
  (T-127).

## Akceptační kritéria
- [ ] `helper decode` na `~/LayerPeek-samples/timelapse/t158-same-idr-pic-id.mp4` vrátí 5 dekódovaných snímků bez
      chyby a seek OK; na uměle poškozeném souboru (oříznutý `mdat`, vytvořený v dočasné složce) exit 1
- [ ] Simulátor: `POST /_simulator/camera/offline|online` s testy v `tests/PrusaSimulator.Tests` a v `docs/simulator.md`
- [ ] `mac.recording` = `PASS` (uzavře v T-127 bod **T-146** kromě částí přesunutých do T-212 / T-213 / T-214)
- [ ] `mac.dashcam` = `PASS` (uzavře v T-127 simulátorovou část bodu **T-148** a simulátorovou část bodu **T-145**)
- [ ] Nahrávka simulátoru přes `decode` OK (uzavře v T-127 bod **T-147**, část „kamera simulátoru“; skutečná Buddy3D
      viz otázka 2)
- [ ] Po běhu žádný soubor mimo dočasnou složku (`mac.profile-unchanged` = `PASS`, `~/Movies/LayerPeek` beze změny)
- [ ] Pester / `-SelfTest` a `dotnet build` / `dotnet test` bez chyb a varování

## Otevřené otázky
1. „Přehrát poslední nahrávku“ / „Otevřít složku s nahrávkami“: stačí ověřit, že aplikace předala systému správnou
   cestu? Šlo by to přes záznam v logu testovacího režimu nebo okno Finderu. Nebo má kontrola skutečně spustit
   QuickTime Player a ověřit jeho okno s názvem souboru (pomalejší, mění stav QuickTime, např. nedávné soubory)?
2. Stačí jako náhrada „nahrávky ze skutečné Buddy3D“ (T-147) nahrávka simulátoru, který přehrává skutečný klip
   Buddy3D (`--camera-clip ~/LayerPeek-samples/clips/coreone-20261103-1905-60s.mp4`), dekódovaná přes `decode`?
   Bitstream je skutečné kamery, síť a kamera ne.

Rozhodnuto uživatelem 2026-11-13: 1 **stačí ověřit předání cesty systému** (bez spouštění QuickTime); 2 **ano**, nahrávka simulátoru se
skutečným klipem Buddy3D nahrazuje skutečnou kameru.

## Poznámky k implementaci
(vyplní agent po dokončení)
