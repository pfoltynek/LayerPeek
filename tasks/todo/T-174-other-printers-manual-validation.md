---
id: T-174
title: Ruční ověření dalších modelů tiskáren (MK4/MK4S, MK3.9, MK3.5, XL, MINI)
status: todo
owner: qa-reviewer
priority: P2
platform: all
depends_on: []
---

> Zadání uživatele 2026-11-05: jen ruční validace na skutečných tiskárnách, bez vývoje. Každá nalezená chyba = nový
> úkol. Uživatel má přístup jen k CORE One, ostatní modely závisí na hlášeních komunity (otázka 1).

## Cíl
README a příručka (`manuals/`) uvádějí jako otestovanou jen Prusa CORE One. Ostatní tiskárny s firmwarem Buddy 6.x
a PrusaLinkem jsou vedené jako „should work, not tested yet“. Ověřit je ručně na skutečných tiskárnách a podle výsledku
upravit tabulku podporovaných tiskáren.

## Rozsah
Modely (podle `PrinterModel`): MK4, MK4S, MK3.9, MK3.9S, MK3.5, MK3.5S, XL (jedno- i vícenástrojová), XL+ (pokud
existuje), MINI, MINI+. Ověřit ty, ke kterým je přístup (viz otevřené otázky); stačí jeden průchod na model, na Windows
nebo macOS.

Pro každý model zapsat do tabulky níže verzi firmwaru, OS a výsledek těchto kontrol:

1. **Připojení:** „Přidat tiskárnu…“ s IP adresou i host name, „Otestovat spojení“ ohlásí kód modelu a firmware;
   výchozí uživatel `maker`, Digest heslo; špatné heslo → stav „Neplatné přihlašovací údaje“.
2. **Rozpoznání modelu:** obrázek tiskárny v nastavení a v diagnostice odpovídá modelu (automatický výběr).
3. **Stavy a ikona:** nečinná, tisk (ikona se plní), pozastaveno, dokončeno, zastaveno, vyžaduje zásah (např. výměna
   filamentu / M600), chyba, nedostupná (vypnutá tiskárna nebo odpojená síť); bublina ikony s průběhem, zbývajícím časem
   a názvem souboru.
4. **Upozornění:** začátek, konec, pozastavení, pokračování, zastavení tisku, „zbývá N minut“, „tiskárna vychladla“.
5. **Diagnostika:** teploty trysky a podložky, ventilátory, rychlost, průtok, výška Z, firmware, graf teplot.
   **XL vícenástrojová:** co se ukáže u více trysek (zapsat, případně nový úkol).
6. **Widgety okna kamery bez kamery i s kamerou** (pokud model kameru má): stav, časy, teploty, název tisku s náhledem
   z G-code (`.bgcode`), vychladnutí po tisku.
7. **Otevřít PrusaLink:** web se otevře přihlášený.
8. **Detekce chyb tisku (jen s kamerou):** u XL je nabízený detektor „Tisk do vzduchu“, u MK4/MK3.x/MINI ne (podložka
   se u nich nepohybuje dolů).
9. **Log** s podrobným logováním: žádné chyby ani varování navíc oproti CORE One.

| Model | Firmware | OS | Výsledek | Poznámky / nové úkoly |
|---|---|---|---|---|
| MK4 / MK4S | | | | |
| MK3.9 / MK3.9S | | | | |
| MK3.5 / MK3.5S | | | | |
| XL (1 nástroj) | | | | |
| XL (více nástrojů) | | | | |
| MINI / MINI+ | | | | |

## Akceptační kritéria
- [ ] Každý dostupný model prošel kontrolami 1–9 a má vyplněný řádek tabulky
- [ ] Nalezené chyby zapsané jako nové úkoly
- [ ] Tabulka „Supported printers“ v `README.md` a `README.cz.md` a kapitola 1 všech příruček `manuals/*/manual.md`
  upravené podle výsledku (✅ tested / známá omezení)
- [ ] Modely, které nebylo na čem ověřit, zůstávají „not tested yet“ a jsou tak uvedené v poznámkách

## Otevřené otázky
1. ~~Ke kterým modelům je přístup?~~ **Odpověď uživatele 2026-11-05: jen k CORE One** (ta je už ověřená). Ostatní modely
   tedy může ověřit jen komunita. Otevřené: má se k tomu přidat šablona GitHub issue „Hlášení kompatibility tiskárny“
   s kontrolami 1–9 a výzva v README / příručce?
2. Stačí pro „✅ tested“ jeden průchod na jednom OS, nebo na obou?

## Poznámky k implementaci
(vyplní agent po dokončení)
