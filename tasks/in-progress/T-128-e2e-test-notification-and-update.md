---
id: T-128
title: E2E testy místo ručních kontrol – zkušební upozornění a aktualizace přes Velopack
status: in-progress
owner: qa-reviewer
priority: P2
platform: windows
depends_on: [T-058, T-101, T-125]
---

## Cíl
Uživatel 2026-11-01: „jsi schopen na T-125 napsat testy, ať to nemusím ručně testovat?“ – rozhodnuto napsat automatické
testy pro body T-101 (zkušební upozornění) a T-058 (aktualizace přes Velopack) z T-125. Body pak v T-125 odkazují na
testy, které je nahrazují.

## Rozsah
1. **T-101 – zkušební upozornění (E2E, skutečná aplikace):** tlačítko „Poslat zkušební upozornění“ v Nastavení → Obecné
   pošle toast (zvuk podle hodnoty vybrané v okně, i neuložené – ověřit v obsahu toastu, ne poslechem); druhý klik
   předchozí toast nahradí; po vypnutí upozornění ve Windows (systémová změna přes žurnál `run-e2e.ps1`, varování,
   odpočet 5 s, obnova i po pádu) se ukáže varování „Upozornění jsou ve Windows vypnutá.“ s tlačítkem, které otevře
   `ms-settings:notifications` (ověřit a okno Nastavení Windows zavřít jen pokud ho otevřel test); klik na zkušební toast
   v centru oznámení přenese okno Nastavení na záložku Obecné.
2. **T-058 – aktualizace přes Velopack (E2E):** izolovaná instalace starší verze z místního zdroje aktualizací (ne GitHub),
   položka menu „Je dostupná nová verze X.Y.Z“ a banner toastu, klik spustí aktualizaci, aplikace se znovu spustí v nové
   verzi; verze `.zip` (bez instalace) otevře stránku vydání (zachytit bez skutečného prohlížeče, pokud to jde).
   Na počítači s nainstalovanou LayerPeek uživatele se test přeskočí s důvodem (stejně jako `VelopackInstallerTests`);
   poprvé poběží spolu s T-124.
3. Testy po sobě uklidí vše i po pádu (instalace, zástupci, registrace toastů, nastavení Windows, procesy).

## Akceptační kritéria
- [ ] E2E pro T-101 (body 1) a T-058 (bod 2); v T-125 u obou bodů odkaz na testy
- [ ] Úklid i po pádu, systémové změny jen přes žurnál `run-e2e.ps1`; jména PascalCase bez podtržítek
- [ ] Běh `tools/run-e2e.ps1` pro T-101 prošel (koordinuje hlavní agent s uživatelem); T-058 → s T-124
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
(žádné)
