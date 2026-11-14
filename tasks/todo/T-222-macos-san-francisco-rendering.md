---
id: T-222
title: Vzhled písma San Francisco v aplikaci na macOS proti nativním aplikacím (užší tvary, umělé tučné)
status: todo
owner: ui-designer
priority: P3
platform: macos
depends_on: [T-214]
---

> **Rozhodnuto uživatelem 2026-11-14 (z T-214, otázka 2):** založit úkol pro ui-designer. Žádná změna UI bez schválení
> uživatelem.

## Cíl
T-214 zjistil, že Avalonia 12.1.4 u proměnného systémového písma `SFNS.ttf` nepoužije osu váhy ani optické velikosti:
vždy kreslí výchozí instanci (wght 400, opsz 28 = tvary „Display“), polotučné a tučné písmo jen uměle ztuční. Proti
AppKitu je text o 7–13 % (běžná váha) a o 14–21 % (polotučná, tučná) užší. LayerPeek proto na macOS nejspíš vypadá
jinak než nativní aplikace (užší, hustší text v malých velikostech, „rozmazaně“ tučná záhlaví). Úkol to posoudí
a navrhne řešení; rozhodne uživatel.

## Rozsah
- Porovnat vzhled textu LayerPeeku (Nastavení, O aplikaci, Diagnostika, okno kamery) s nativními aplikacemi macOS
  (např. Nastavení systému, Finder) ve stejné velikosti: šířka, tvary, tučná záhlaví. Snímky jen lokálně (písmo Applu
  se necommituje; snímky obrazovky do repozitáře nepatří).
- Ověřit předpoklad z T-127 bodu 1k (běžící aplikace rozvrhuje text stejně jako headless test T-214).
- Zhodnotit možnosti s dopadem na rozložení, velikost balíčku, licenci a AOT, např.:
  - dodávat Inter i na macOS (jako headless testy; varianta z T-038 otázky 3);
  - nahlásit chybu / požadavek Avalonii (osy proměnných písem, optická velikost) a počkat;
  - ponechat stav (rozložení je otestované, T-214).
- Výsledek: doporučení s podklady v `## Poznámky k implementaci` a otázky pro uživatele. **Žádná změna UI bez
  schválení uživatelem**; implementace případně jako nový úkol.

## Akceptační kritéria
- [ ] Porovnání s nativními aplikacemi popsané (velikosti, váhy, rozdíly), snímky jen mimo repozitář
- [ ] Možnosti zhodnocené (rozložení, balíček, licence, AOT) s doporučením
- [ ] Rozhodnutí uživatele zapsané, případná implementace založená jako nový úkol

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
