---
id: T-104
title: Malý náhled – nastavitelný obsah pásu (výběr položek jako u widgetů) – návrh
status: todo
owner: ui-designer
priority: P3
platform: all
depends_on: [T-092, T-103]
---

## Cíl
Uživatel si sám vybere, co ukazuje informační pás malého náhledu, podobně jako u widgetů překryvu na záložce Widgety
(T-092). Kandidátní položky: průběh (horní pruh a procento), zbývající čas, čas dokončení, stav vychladnutí, řádek
názvu tisku, řádek grafu teplot.

Zatím **jen zástupný úkol pro návrh** (rozhodnutí uživatele 2026-10-29). Začne se až po T-103, který do základního řádku
přidá čas dokončení a zvýrazněné vychladnutí; návrh T-104 na jeho pravidla priority (T-103, oddíl 3) naváže.

## Rozsah
- Návrh UI (ui-designer, Česky, oddíl `## Návrh UI`): kde se pás nastavuje, jak se volby promítnou do základního řádku
  a řádků pod ním, výchozí stav (= dnešní chování po T-103), chování při úzkém okně (240), texty v 8 jazycích.
- Rozdělení na implementační úkoly (Core: model a uložení nastavení; App: UI nastavení, pás) – po odpovědích na otázky.
- Mimo rozsah: implementace v tomto úkolu.

## Akceptační kritéria
- [ ] Návrh UI s drátovými modely (nastavení i pás při 240 / 320 / 400) a pravidly priority.
- [ ] Odpovědi uživatele na Otevřené otázky zapsané v `## Rozhodnutí`.
- [ ] Navazující implementační úkoly v `tasks/todo/` a v `tasks/README.md`.

## Otevřené otázky
- **Kde se pás nastavuje?** Na záložce Widgety (T-092) jako další položka seznamu „Malý náhled“ s vlastním nastavením,
  nebo u kamery tiskárny (Nastavení → tiskárna → Kamera), nebo přímo v malém okně (kontextová nabídka pásu)?
- **Globálně, nebo u každé tiskárny?** Widgety jsou globální (T-092); stav sbalení řádků je u tiskárny (T-090 Q2).
- **Které položky jdou vypnout?** Průběh (pruh / procento zvlášť?), zbývající čas, čas dokončení, vychladnutí, řádek
  názvu, řádek grafu. Smí uživatel vypnout všechno (pás jen s přepínači), nebo zůstává minimum (stav)?
- **Vztah k přepínačům ⬡ / 📈 (T-090):** vypnutý řádek v nastavení = schované přepínací tlačítko? Nebo nastavení určuje
  jen výchozí stav rozbalení a přepínače zůstávají?
- **Pořadí položek:** pevné (podle priority T-103), nebo nastavitelné (přetažení jako rozložení widgetů)?
- **Priorita při úzkém okně:** platí pevná pravidla T-103 (procento > zbývající čas > čas dokončení), nebo si ji uživatel
  zvolí (T-103 Q4 alternativa C)?
- **Formát času dokončení:** jen pravidla widgetu Časy, nebo volba „jen čas / se slovem Hotovo v“?
- **Náhled v nastavení:** živý náhled pásu na záložce (jako náhled rozložení), nebo jen seznam voleb?
- **Uložení a migrace:** chybějící nastavení = dnešní chování; zapomíná se spolu se stavem okna (T-090 Q3), nebo ne?

## Poznámky k implementaci
(vyplní agent po dokončení)
