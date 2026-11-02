---
id: T-136
title: Automatické kontroly okna kamery na Macu místo ručních bodů T-115 (T-122, T-117, T-118)
status: done
owner: qa-reviewer
priority: P1
platform: macos
depends_on: [T-115, T-117, T-118, T-122, T-135]
---

## Cíl
Uživatel 2026-11-01: „co T115, daji se na to dodelat testy at se muze taky uzavrit“ – rozhodnuto (varianta „Vše“):
existující automatické kontroly spustit, na ruční body T-122 / T-117 / T-118 (a T-099 sezení 8.2) dopsat kontroly do
`tools/macos-verify.ps1` se skutečnými událostmi myši a klávesnice (CGEvent) a stromem Accessibility, subjektivní
a fyzické body přesunout do T-127 a T-115 uzavřít.

## Rozsah
1. `tools/macos-verify-helper.swift`: `rightclick`, `ctrlclick` (Control skutečně stisknutý), `move`, `ax <pid>` (strom
   Accessibility oken procesu), `axpress <pid> <id>` (akce AXPress jako VoiceOver), `focusedid <pid>`.
2. Kontrola `mac.camera-pin` (Auto, potřebuje Zpřístupnění pro terminál, bez něj SKIP), normální okno i malý náhled:
   klik myší přepne „vždy navrchu“ hned (úroveň okna, typ LaunchServices = Dock / ⌘Tab podle pravidla T-102,
   `settings.json`); po kliku a odjetí myši ani po přepnutí do Finderu a zpět nemá žádné tlačítko okna fokus klávesnice
   (řada tlačítek se ukazuje jen při `Grid#Root:pointerover` nebo `:focus-within`, takže s ukazatelem mimo okno zbude jen
   ztlumený špendlík); po kliku na obraz Tab na první tlačítko okna; Tab na špendlík, mezerník a Enter přepínají a fokus
   zůstane na špendlíku; AXPress přepíná a přístupné jméno je „Always on top: on / off“; kontextové menu pravým tlačítkem
   i ⌃-klikem má položky režimu se zaškrtnutím podle špendlíku a Esc ho zavře; volba přežije restart aplikace.
3. Kontrola obrysu malého náhledu (T-118) ze snímku okna – potřebuje Nahrávání obrazovky pro terminál.
4. T-099 sezení 8.2 (událost tisku během spánku displeje → oznámení) – posoudit, zda jde rozšířit `mac.display-sleep`.

## Akceptační kritéria
- [x] Pomocník a `mac.camera-pin` (bod 1–2); Pester `tools/VerifyTools.Tests.ps1` a `LayerPeek.Scripts.Tests` projdou
- [x] `mac.camera-pin` = PASS (po opravě T-137 a T-138)
- [x] Kontrola obrysu (bod 3) = PASS
- [x] Bod 4 rozhodnut / hotový
- [x] T-115: body nahrazené kontrolami odkazují na ně, subjektivní a fyzické body přesunuté do T-127

## Otevřené otázky
(žádné)

## Poznámky k implementaci
- 2026-11-01 (macOS 27.2, Zpřístupnění pro Terminál): `mac.camera-pin` funguje; prochází vše kromě dvou chyb aplikace,
  které kontrola našla – T-137 (po zapnutí „vždy navrchu“ macOS aplikaci deaktivuje, fokus klávesnice zmizí, vpředu je
  Finder) a T-138 (⌃-klik neotevře kontextové menu). Syntetický ⌃-klik pomocníka ve Finderu kontextové menu otevře.
- 2026-11-02 (macOS 27.2, větev se sloučeným `develop` po T-137–T-139, zpráva `artifacts/verify/20261102-102650`):
  `mac.camera-pin` = `PASS` (normální okno i malý náhled: klik, mezerník, Enter, AXPress, fokus po kliku a přepnutí
  aplikace, Tab po obrazu, kontextové menu pravým tlačítkem i ⌃-klikem, restart) a `mac.camera-outline` = `PASS`
  (`mac.camera-outline`: snímek okna `screencapture -l`, obrys 2 px u hran a rohů, jas asi 115 / 191 bez / s ukazatelem,
  vnitřek asi 0; prvek `MiniOutline` ve stromu Accessibility jen v malém náhledu).
- Bod 4: `mac.display-sleep` spustí aplikaci se zapnutými oznámeními přes LaunchServices (`Start-TestAppViaOpen
  -NoWait`), když má bundle „LayerPeek E2E“ oznámení povolená, a vyžaduje, aby oznámení pauzy bylo doručené dřív, než
  se spustí UI (tedy během spánku displeje); jinak běží jako dřív a ve výsledku napíše, že část s oznámením neproběhla.
  Uzavírá tím T-099 kritéria 2 a 4 (sezení 8.2). Spustí ho uživatel (uspání displeje) v T-115.
- T-115: ověřené body odškrtnuté s odkazem na kontrolu, subjektivní a fyzické body (ikony, `mac.menu`,
  `mac.camera-look`, fyzická klávesnice a otázky 1–2 T-100, skutečný VoiceOver, vzhled špendlíku a obrysu, celý
  interaktivní běh T-105) přesunuté do T-127, oddíl 1b. `docs/macos-manual-testing.md`: řádky `mac.camera-pin`,
  `mac.camera-outline` a rozšířený `mac.display-sleep`.
- Pester `tools/VerifyTools.Tests.ps1` 36/36, `LayerPeek.Scripts.Tests` 12 prošlo, 1 přeskočen.
