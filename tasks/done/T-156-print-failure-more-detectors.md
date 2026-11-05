---
id: T-156
title: Detekce chyb tisku – další detektory včetně první vrstvy (rozpad po prvním detektoru)
status: done
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
  - tisk ve vzduchu (tryska nad dílem bez extruze) – **vybráno jako první další detektor (rozhodnutí uživatele 2026-11-04), úkol T-167**,
  - tmavý filament na tmavém plátu – samostatný úkol T-163,
  - automatické nalezení podložky v obraze (místo ručního výřezu z T-154) – možný pozdější úkol, zatím nezaložený (rozhodnutí uživatele 2026-11-04, otázka 8 v T-152),
  - další podle návrhu uživatele.
- Pro každý vybraný detektor založit úkol (owner `core-dev`, závislost T-155, případně UI do T-154 se nemusí měnit díky rozšiřitelnému seznamu).

## Akceptační kritéria
- [x] Vyhodnocení provozu T-155 zapsané v tomto úkolu.
- [x] Založené úkoly pro detektory vybrané uživatelem (s akceptačními kritérii a otázkami) – T-167.

## Otevřené otázky
1. **Které další detektory a v jakém pořadí?** Rozhodne uživatel po T-155. **Rozhodnuto pro tuto chvíli (uživatel
   2026-11-04):** první další detektor je „tisk do vzduchu“ (T-167). Ostatní kandidáti zatím vybraní nejsou: první vrstva
   je blokovaná daty (otázka 3), ML je odložené, „blob“ má nízkou očekávanou spolehlivost a automatické nalezení podložky
   přijde případně později.
2. **První vrstva:** pokud ji z výchozí polohy kamery Core One spolehlivě detekovat nepůjde, je pro uživatele přijatelná jiná poloha / druhá kamera, nebo se detektor první vrstvy vypustí?
3. **Zdroj dat první vrstvy** (uživatel 2026-11-04: „záběry vytvoříme jinak“): odkud záběry prvních ~15 minut tisku budou? Možnosti: a) automatické nahrávání tisku (T-145), které záběr pořídí samo při běžném tisku, bez zvláštní práce uživatele, b) inscenovaný tisk jedné vrstvy (krátký testovací G-code), c) syntetické varianty (stopa vrstvy vymazaná / posunutá na snímcích po první vrstvě). Dokud zdroj není, detektor první vrstvy se nezakládá.

## Vyhodnocení provozu T-155
Skutečná data z provozu detektoru `bed-change` zatím nejsou. Detektor nikdo nenechal běžet u skutečných tisků
s uživatelskými poplachy, takže chybí záznamy falešných poplachů (`detection/false-alarms`) i minutých chyb. Vyhodnotit
jde jen to, co bylo změřeno při vývoji:
- **Syntetická data (T-155, T-166):** po ladění v T-166 je při výchozí citlivosti odhaleno ≥ 91,1 % selhání na každém
  ze 30 semínek ve výřezu Core One při 2 s i 5 s. Výjimkou je celý snímek při 5 s se semínkem 16 (89,3 %). Falešné
  poplachy: 0 za 112 h ve výřezu Core One, 0,45 / 10 h v celém snímku při 5 s. Neodhalené zůstávají hlavně hroudy
  s jasem blízkým plátu a hroudy na logu nebo nápisu (otázka 4 v T-166).
- **Skutečná data:** jen jedna úspěšná série Core One (~4,4 h, snímek po 30 s, jeden jednoduchý díl) a jeden klip 60 s.
  Ve výřezu Core One žádný poplach, rezerva 9× pod výchozím prahem. V celém snímku dělá most X skóre nad prahem
  (otázka 2 v T-166, otázka 4 v T-155). Skutečné selhání zachycené kamerou nemáme.
- **Závěr:** výběr dalšího detektoru stojí na rozboru spiku T-152 a na požadavku uživatele, ne na datech z provozu.
  Delší záznam se skutečnou tiskárnou při vzorkování 2–5 s a ruční ověření detekce zůstávají v T-127 a doporučení
  z otázky 3 v T-166 platí dál. Nově se nahrává záznam Z úlohy 354 (`~/LayerPeek-samples/z-recordings`, pro T-159
  a T-167).

## Poznámky k implementaci
- Uzavřeno 2026-11-04 (architekt): uživatel vybral „tisk do vzduchu“ a vznikl úkol T-167 (owner core-dev, fáze 1 =
  měření a brána proveditelnosti, fáze 2 = detektor). Otevřené otázky jsou zapsané v T-167.
- Ostatní kandidáti (první vrstva, ML, posunutý díl nad rámec `bed-change`, „blob“, automatické nalezení podložky) zůstávají
  nevybraní. Otázky 2 a 3 (první vrstva) zůstávají otevřené. Další rozpad udělá architekt nově, až uživatel vybere
  dalšího kandidáta. Tmavý filament řeší T-163.
