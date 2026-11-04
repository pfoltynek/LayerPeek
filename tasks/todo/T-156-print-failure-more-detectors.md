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
  - **chyby první vrstvy** (pokud spike T-152 a data ukážou, že je to z kamery Core One proveditelné – např. referenční snímek prázdné podložky vs. snímek po první vrstvě, nebo zvláštní detail kamery). *Závěr T-152 (2026-11-03): stopa první vrstvy je při 1280×720 vidět jako světlý pruh ~8–10 px, kvalita (přimáčknutí, mezery) ne. Připadá v úvahu jen „stopa první vrstvy chybí / odtrhla se“ s analýzou při 640×360 (pozadí po první vrstvě proti pozadí před ní, pevný referenční snímek ne, podložka jezdí v Z). Rozhodnout až podle záběrů začátku tisku (otázka 6 v T-152).* **Blokováno daty (rozhodnutí uživatele 2026-11-04):** uživatel záběry první vrstvy ze skutečných tisků plánovaným způsobem nepořídí, detektor první vrstvy se nezaloží, dokud nebude jiný zdroj dat (otázka 3).
  - **ML klasifikátor** – **odloženo (rozhodnutí uživatele 2026-11-04, otázky 12 a 13 v T-152)**, včetně žádosti o licenci PrintGuard. Podklady pro případný návrat: ONNX Runtime (MIT, AOT bez varování), hotový model s kompatibilní licencí vah se v T-152 nenašel.
  - díl odlepený / posunutý (pokud nebyl prvním detektorem),
  - filament namotaný na trysce („blob“) – *T-152: špička trysky je z výchozí polohy kamery většinou zakrytá, šlo by jen o zvětšení obrysu hlavy z jednoho úhlu, nízká očekávaná spolehlivost,*
  - tisk ve vzduchu (tryska nad dílem bez extruze),
  - tmavý filament na tmavém plátu – samostatný úkol T-163,
  - automatické nalezení podložky v obraze (místo ručního výřezu z T-154) – možný pozdější úkol, zatím nezaložený (rozhodnutí uživatele 2026-11-04, otázka 8 v T-152),
  - další podle návrhu uživatele.
- Pro každý vybraný detektor založit úkol (owner `core-dev`, závislost T-155, případně UI do T-154 se nemusí měnit díky rozšiřitelnému seznamu).

## Akceptační kritéria
- [ ] Vyhodnocení provozu T-155 zapsané v tomto úkolu.
- [ ] Založené úkoly pro detektory vybrané uživatelem (s akceptačními kritérii a otázkami).

## Otevřené otázky
1. **Které další detektory a v jakém pořadí?** Rozhodne uživatel po T-155.
2. **První vrstva:** pokud ji z výchozí polohy kamery Core One spolehlivě detekovat nepůjde, je pro uživatele přijatelná jiná poloha / druhá kamera, nebo se detektor první vrstvy vypustí?
3. **Zdroj dat první vrstvy** (uživatel 2026-11-04: „záběry vytvoříme jinak“): odkud záběry prvních ~15 minut tisku budou? Možnosti: a) automatické nahrávání tisku (T-145), které záběr pořídí samo při běžném tisku, bez zvláštní práce uživatele, b) inscenovaný tisk jedné vrstvy (krátký testovací G-code), c) syntetické varianty (stopa vrstvy vymazaná / posunutá na snímcích po první vrstvě). Dokud zdroj není, detektor první vrstvy se nezakládá.

## Poznámky k implementaci
(vyplní agent po dokončení)
