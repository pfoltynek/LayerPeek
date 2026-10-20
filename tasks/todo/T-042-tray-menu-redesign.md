---
id: T-042
title: Jednotné tray menu (podmenu pro každou tiskárnu)
status: todo
owner: ui-dev
priority: P2
platform: all
depends_on: [T-039, T-040]
---

## Cíl
Tray menu vypadá stejně pro jednu i více tiskáren, nabízí u každé tiskárny všechny akce a má klávesové zkratky. Návrh ui-designera (2026-10-20) schválený uživatelem.

## Rozsah
- Každá tiskárna má **vždy vlastní podmenu** (i když je jen jedna): „Otevřít PrusaLink v prohlížeči“, „Diagnostika…“, „✓ Kamera“ (přepínač z T-040, jen u tiskárny se zapnutou kamerou). Řádek tiskárny jen otevírá podmenu (mění rozhodnutí z T-008, kdy klik otevíral web).
- **Globální „Diagnostika…“** z hlavního menu odstranit.
- **Ikony:** na Windows jen obrázky tiskáren (T-039), akce bez ikon; na macOS žádné ikony (T-018).
- **Bez tiskárny:** šedý text „Není nastavena žádná tiskárna“ + nová položka „Přidat tiskárnu…“ (otevře nastavení a přidá tiskárnu).
- **Název tiskárny v menu** zkrátit na cca 32 znaků („…“), celý název zůstává v nastavení a Diagnostice.
- **Klávesové zkratky** (podtržená písmena) u pevných položek a položek podmenu ve všech 8 jazycích, test jedinečnosti; tiskárny bez zkratek.
- Tři tečky u „Nastavení…“ a „Diagnostika…“ zůstávají; Title Case pro macOS řeší T-018.

## Rozhodnuto
- **Struktura (2026-10-20):** varianta A – vždy podmenu tiskárny.
- **Globální Diagnostika (2026-10-20):** odstranit.
- **Ikony (2026-10-20):** Windows jen tiskárny, macOS žádné.
- **0 tiskáren (2026-10-20):** přidat „Přidat tiskárnu…“.
- **Délka názvu (2026-10-20):** cca 32 znaků.
- **Klávesové zkratky (2026-10-20):** ano, 8 jazyků, test jedinečnosti.
- **Tři tečky (2026-10-20):** ponechat; Title Case macOS → T-018.
- Návrh a ukázky: scratchpad `tray-menu-design/tray-menu-proposal.md` (ui-designer, 2026-10-20) – při implementaci shrnout do `## Návrh UI`.

## Akceptační kritéria
- [ ] Menu se stejnou strukturou pro 1 i N tiskáren, podmenu s akcemi, bez globální Diagnostiky (testy ViewModelu a presenteru)
- [ ] „Přidat tiskárnu…“ při 0 tiskárnách (test)
- [ ] Zkrácení názvu na cca 32 znaků (test)
- [ ] Klávesové zkratky ve všech 8 jazycích, test jedinečnosti
- [ ] Ikony tiskáren na Windows, žádné na macOS (testy)
- [ ] Ruční ověření na Windows (uživatel)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish win-x64 bez varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
