---
id: T-179
title: Hardwarové dekódování H.264 kamery – průzkum (spike)
status: todo
owner: architect
priority: P3
platform: all
depends_on: [T-171, T-172]
---

## Cíl
Výkonnostní analýza (2026-11-06): kamera se dekóduje jen softwarově. Hardwarové dekódování (D3D11VA / DXVA2
na Windows, VideoToolbox na macOS) by mohlo snížit zátěž CPU. Zkomplikuje ale build LGPL FFmpeg (T-026, T-162)
a kopírování snímků z GPU zpět do paměti může úsporu sníst. Nejdřív je potřeba změřit, jestli se to po T-171 a T-172
vůbec vyplatí.

## Rozsah
- Změřit CPU softwarového dekódování po T-171 a T-172 (720p a 1080p, simulátor): je dekódování ještě podstatná část zátěže?
- Zjistit, co by vyžadoval build FFmpeg (konfigurace, licence LGPL, velikost knihoven).
- Doporučení „vyplatí se / nevyplatí se“ s čísly. Žádná implementace.

## Akceptační kritéria
- [ ] Měření a rozbor dopadu na build jsou zapsané v `docs/`.
- [ ] Doporučení je předložené uživateli. Případná implementace vznikne jako nový úkol po jeho rozhodnutí.

## Otevřené otázky
1. Chcete hardwarové dekódování vůbec zkoumat, nebo úkol zamítnout? (Blokuje implementaci.)

## Poznámky k implementaci
(vyplní agent po dokončení)
