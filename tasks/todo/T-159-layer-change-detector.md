---
id: T-159
title: Detekce změny vrstvy z osy Z (PrusaLink `axis_z`) a spouštěč timelapse po vrstvě
status: todo
owner: core-dev + ui-dev
priority: P3
platform: all
depends_on: [T-149, T-160, T-161]
---

> **Návrh ze spiku T-149 (2026-11-03).** Implementuje se jen tehdy, když uživatel v T-149 ponechá spouštěč „po vrstvě“
> (otázka 2 v T-149). Podrobnosti: `docs/timelapse-spike.md`, oddíl „Spouštěč po vrstvě“.

## Cíl
PrusaLink na Buddy (`GET /api/v1/status`) číslo vrstvy ani událost změny vrstvy nehlásí. Jediný údaj je `axis_z`
(`marlin_vars().logical_curr_pos[2]`, tedy cíl posledního zařazeného pohybu, 1 desetinné místo) při každém pollingu
(výchozí 5 s). Z-hop (Core One: `retract_lift = 0.2`, rampovaný zdvih při přejezdu až `travel_max_lift = 1.5` mm)
se v něm objeví jako krátký skok nahoru. Úkol přidá do Core čistou, testovatelnou logiku, která z řady vzorků Z odhadne
změnu vrstvy, a nejdřív ji ověří na záznamu ze skutečného tisku.

## Rozsah
- **Záznam ze skutečné tiskárny (uživatel spustí, agent připraví):** skript nebo režim stávajícího
  `tools/record-prusalink-fixtures.ps1`, který během celého tisku každou 1 s uloží jen `čas, state, axis_z, job.progress`
  (žádné sériové číslo, IP, heslo) do CSV. Aspoň dva tisky Core One (výška vrstvy 0,2 a 0,1 nebo 0,05 mm), ideálně
  i s „complete objects“ (sekvenční tisk, Z jde dolů). Záznam jde jako fixture do `tests/LayerPeek.Core.Tests/Fixtures/`.
- **Core (název volí core-dev, např. `LayerChangeDetector` v `Core/Printers`):** čistá funkce / stavový objekt bez
  časovačů: vstup vzorek (`čas`, `PrinterState`, `AxisZ`, `JobId`), výstup „nová vrstva“ (odhadnutá Z).
  Navržený algoritmus ze spiku (core-dev ho ověří na záznamu a smí upravit):
  - jen ve stavu `Printing` a u téže úlohy; nová úloha / stav mimo tisk = reset,
  - „Z vrstvy“ = minimum posledních *k* vzorků (výchozí *k* = 2, při pollingu 5 s zpoždění ≤ 10 s). Z-hop trvá jen
    po dobu přejezdu; když ho zachytí *k* vzorků po sobě, vznikne navíc snímek téže vrstvy (ne vynechaná vrstva).
    Jak často to nastává, ukáže až záznam (podíl vzorků se z-hopem *p* → falešná vrstva s pravděpodobností ~*p*^*k*),
  - změna vrstvy, když minimum vzroste nad poslední Z vrstvy aspoň o 0,05 mm; pokles minima (konec z-hopu po falešné
    vrstvě, sekvenční tisk, nový objekt) = nový základ bez snímku,
  - limit: nejvýš jeden snímek za polling; vrstvy kratší než polling se sloučí (timelapse má pak méně snímků).
- **Napojení (core-dev):** pozorovatel snímků monitoru (`IMonitoringObserver`) předá změnu vrstvy službě timelapse
  (T-160) jako druhý spouštěč vedle spouštěče po čase.
- **UI (ui-dev):** volba spouštěče „po vrstvě“ v nastavení timelapse (T-161), s krátkým vysvětlením, že jde o odhad
  z osy Z. Nové texty do všech jazyků `Strings.*.resx`.
- Záznam ze skutečné tiskárny (skript + spuštění uživatelem) jde udělat kdykoli dřív, nezávisle na T-160 / T-161.

## Akceptační kritéria
- [ ] Záznam aspoň jednoho skutečného tisku je ve fixtures; poznámky uvádí zjištěný podíl vzorků se z-hopem
      a přesnost detektoru (zachycené / vynechané / falešné vrstvy proti očekávanému počtu z výšky dílu / výšky vrstvy).
- [ ] Testy na záznamu i na syntetických řadách: z-hop 0,2 i 1,5 mm, vrstvy 0,05 / 0,1 / 0,2 / 0,3 mm, zaokrouhlení
      na 0,1 mm, sekvenční tisk, pauza, nová úloha, výpadek tiskárny.
- [ ] Žádné časovače ani alokace na vzorek v ustáleném stavu.
- [ ] Služba timelapse pořídí se spouštěčem po vrstvě snímek na každou zjištěnou vrstvu (test s falešným monitorem
      a falešným streamem); volba je v UI a uloží se.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování.

## Otevřené otázky
- Pokud záznam ukáže, že při pollingu 5 s je detekce nepoužitelná (např. drobné díly s vrstvou pod 5 s), rozhodne
  uživatel: zkrátit polling během timelapse po vrstvě (víc dotazů na tiskárnu), nebo spouštěč po vrstvě nenabízet.

## Poznámky k implementaci
(vyplní agent po dokončení)
