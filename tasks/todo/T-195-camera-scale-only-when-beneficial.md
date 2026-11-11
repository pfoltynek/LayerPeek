---
id: T-195
title: Kamera – zmenšovat dekódovaný obraz jen tehdy, když to ušetří
status: todo
owner: core-dev + ui-dev
priority: P3
platform: all
depends_on: [T-184]
---

## Cíl
Navazuje na T-184 (otázka 3, rozhodnutí uživatele 2026-11-11: **„škálovat jen když to dává smysl“**). Okno jen o málo menší
než obraz kamery (měřeno 960 × 540 ze 1280 × 720) stojí po T-184 o ~1 procentní bod CPU víc než bez zmenšení, protože
dvouprůchodové zmenšení swscale je dražší, než kolik ušetří menší kopie.

## Rozsah
- Zavést práh: při malém zmenšení zůstat u plného rozlišení zdroje (zmenší renderer), při velkém zmenšení (malý náhled,
  malé okno) zmenšovat v dekodéru jako dnes. Hodnotu prahu **určit měřením** (CPU i paměť, 720p a 1080p, simulátor;
  několik velikostí okna mezi malým náhledem a plnou velikostí), ne odhadem; tabulku zapsat do poznámek.
- Práh s hysterezí, aby plynulá změna velikosti okna kolem prahu nepřepínala dekodér sem a tam (navazuje na zaokrouhlení
  na 32 px a debounce 250 ms z T-184).
- Export snímku dál v plném rozlišení (beze změny).

## Akceptační kritéria
- [ ] Naměřená tabulka CPU/paměti pro několik poměrů zmenšení (720p, 1080p) v `## Poznámky k implementaci` a z ní
      odvozený práh.
- [ ] Nad prahem dekodér nezmenšuje, pod ním ano; hystereze proti přepínání (unit testy výpočtu cílové velikosti).
- [ ] CPU okna jen o málo menšího než zdroj není vyšší než bez zmenšení; malý náhled si ponechá úsporu z T-184.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
(zatím žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
