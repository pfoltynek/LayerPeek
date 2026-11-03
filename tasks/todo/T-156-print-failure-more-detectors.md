---
id: T-156
title: Detekce chyb tisku – další detektory včetně první vrstvy (rozpad po prvním detektoru)
status: todo
owner: architect
priority: P3
platform: all
depends_on: [T-155]
---

## Cíl
Uživatel chce detektory přidávat postupně (nápad 3, fáze 4). Tento úkol je **zástupný**: po dokončení prvního detektoru (T-155) a se zkušeností ze skutečných tisků (poplachy, falešné poplachy z T-153, nahrávky z T-145) architekt rozpadne další detektory na samostatné implementační úkoly.

## Rozsah
- Vyhodnotit data z provozu T-155 (falešné poplachy, minuté chyby).
- Kandidáti (pořadí a výběr rozhodne uživatel):
  - **chyby první vrstvy** (pokud spike T-152 a data ukážou, že je to z kamery Core One proveditelné – např. referenční snímek prázdné podložky vs. snímek po první vrstvě, nebo zvláštní detail kamery),
  - díl odlepený / posunutý (pokud nebyl prvním detektorem),
  - filament namotaný na trysce („blob“),
  - tisk ve vzduchu (tryska nad dílem bez extruze),
  - další podle návrhu uživatele.
- Pro každý vybraný detektor založit úkol (owner `core-dev`, závislost T-155, případně UI do T-154 se nemusí měnit díky rozšiřitelnému seznamu).

## Akceptační kritéria
- [ ] Vyhodnocení provozu T-155 zapsané v tomto úkolu.
- [ ] Založené úkoly pro detektory vybrané uživatelem (s akceptačními kritérii a otázkami).

## Otevřené otázky
1. **Které další detektory a v jakém pořadí?** Rozhodne uživatel po T-155.
2. **První vrstva:** pokud ji z výchozí polohy kamery Core One spolehlivě detekovat nepůjde, je pro uživatele přijatelná jiná poloha / druhá kamera, nebo se detektor první vrstvy vypustí?

## Poznámky k implementaci
(vyplní agent po dokončení)
