---
id: T-076
title: Widgety překryvu okna kamery – návrh UI (design spec)
status: todo
owner: ui-designer
priority: P2
platform: all
depends_on: []
---

## Cíl
Podrobný návrh nového překryvu okna kamery složeného z **widgetů**. Nahradí jeden `Border` s řádky z T-036. Návrh je podklad pro úkoly T-077 až T-093. Uživatel ho **musí schválit před začátkem implementace UI** (stejně jako u T-033).

## Kontext
- Uživatel schválil směr podle makety: https://claude.ai/artifact/CJpK1mM9XsWXWT9DpCzrF1. Je to soukromé plátno uživatele, agenti ho nemusí umět otevřít. Schválené jsou varianty **„A“ + „C“ + malý náhled s grafem + další widgety**. Varianta **„B“ (panel / šuplík) je zamítnutá** – neplánovat ani nenavrhovat.
- Dnešní stav:
  - `src/LayerPeek.App/Windows/CameraWindow.axaml` (`PrintOverlay`, ~ř. 121), `App/Cameras/CameraViewModel.cs`, `App/Cameras/CameraPrintContent.cs`.
  - Nastavení `Core/Cameras/CameraOverlayOptions.cs`: položky `CameraOverlayItems`, roh `CameraOverlayCorner`, zvlášť normální okno + celá obrazovka a malý náhled. V nastavení je podzáložka Překryv (T-036).
  - Tlačítka okna jsou vpravo nahoře (`CameraWindowButtons`, 6 + 30 + 4 = 40 DIP).
- Graf teplot z diagnostiky: `App/Diagnostics/Chart/TemperatureChart`. Cíle kreslí čárkovaně, čáry přerušuje při výpadku dat.

### Popis schváleného návrhu slovy (pro agenty bez přístupu k maketě)
- **Varianta A – karty v kotvách:**
  - Každá skupina údajů je samostatná tmavá poloprůhledná karta: pozadí ≈ `rgba(14,14,16,0.72)`, poloměr rohů 10, světlý text.
  - Čísla jsou tabulková (`tabular-nums`). Místo textových popisků jsou ikony. Akcentová barva je oranžová `#FA6831`.
  - Karty se umisťují do jedné ze **6 kotev**: vlevo nahoře, nahoře uprostřed, vpravo nahoře, vlevo dole, dole uprostřed, vpravo dole. Volné umístění na pixely není.
  - Víc karet v jedné kotvě se skládá pod sebe (v dolních kotvách nad sebe).
- **Varianta C – režim rozložení:**
  - V okně kamery se ztlumí obraz a 6 kotev se ukáže jako čárkované zóny.
  - Widget se přetáhne do kotvy. Cílová zóna se zvýrazní a widget se do ní přichytí.
  - Klik na widget otevře popover s volbou „zvýraznit“ a velikostí S/M/L.
  - Nástrojová lišta nabízí „Obnovit výchozí“ a „Hotovo“.
- **Widgety v1:**
  - **Stav:** název tiskárny, „pilulka“ stavu s barevnou tečkou, velké procento průběhu, pruh průběhu.
  - **Časy:** „Zbývá“ jako velké číslo, čas dokončení a doba tisku s ikonami.
  - **Teploty + sparkline:** tryska a podložka (aktuální / cílová) a kompaktní graf posledních 60 minut. Graf nemá osy, cíle jsou čárkovaně, tryska oranžová, podložka modrá.
  - **Název tisku („hezčí název souboru“):** např. `foldable_book_holder_v1_0.4n_0.2mm_PLA_COREONE_2h51m.bgcode` → „Foldable book holder v1“ + čipy (materiál, výška vrstvy, tryska). Velikosti:
    - S: čip,
    - M: náhled 40 px + název + čipy,
    - L: náhled 72 px + název + čipy + odhad sliceru.
  - **Vychladnutí:** objeví se po dokončení nebo zastavení tisku a má dva stavy:
    - „Chladne…“: teploty, průběh k prahu, odhad času,
    - „Vychladlo – můžeš vypnout“.

    Rozhoduje jen tryska (výchozí práh 50 °C), podložka se jen zobrazuje.
- **Malý náhled (platí podle doplnění uživatele 2026-10-24):**
  - Přes obraz malého náhledu **nejsou žádné karty**. Pod obrazem je **informační pás**.
  - Sbalený pás je obraz + jeden řádek ≈ 30 px: pruh průběhu na horní hraně pásu, text „19 % · 2 h 35 min“ a dvě přepínací tlačítka.
  - Rozbalit jde navíc řádek s názvem tisku a/nebo řádek ≈ 60 px s grafem teplot. Každý zvlášť, přímo v malém okně, malými přepínacími tlačítky. Tlačítka jdou ovládat klávesnicí a mají jména pro čtečku obrazovky i `AutomationId`.
  - Stav sbalení se pamatuje. Okno podle něj roste a zmenšuje se na výšku a obraz si drží poměr stran streamu.

## Rozhodnutí (uživatel, 2026-10-24)
- **Ikony (Q1):** **Fluent UI System Icons** (https://github.com/microsoft/fluentui-system-icons, licence MIT, © 2020 Microsoft Corporation, kompatibilní s GPL-3.0).
  - Ikony se do projektu vloží jako vektorové geometrie (`StreamGeometry` / `Path.Data` v `ResourceDictionary`), **bez NuGet balíčku a bez fontu**. Za běhu tak nevzniká žádná závislost a nic nebrání AOT.
  - V návrhu uvést přesné názvy použitých ikon a jejich variantu (Regular / Filled, velikost 16 / 20 / 24).
  - Licenční text MIT doplní ui-dev do přehledu licencí třetích stran (`App/Licenses/ThirdPartyLicenses.cs`, `Licenses/Texts`) v prvním úkolu, který ikony použije (T-081).
- **Víc instancí (Q2):** v1 = **každý typ widgetu nejvýš jednou**.
- **Karty v malém náhledu (Q3):** **žádné karty přes obraz malého náhledu**, jen pás pod obrazem. Volba „viditelný v malém náhledu“ u widgetů **zaniká**.
- **Pořadí v kotvě (Q5):** pořadí karet v jedné kotvě jde v režimu rozložení **měnit tažením**. Klávesová obdoba patří k požadavku na ovládání klávesnicí, navrhne ji designer.
- **Vstup do režimu rozložení (Q6):**
  - Vstupuje se **tlačítkem v řadě tlačítek okna kamery** a **položkou kontextové nabídky** (pravé tlačítko na obraze).
  - Režim je dostupný jen v **normálním okně**. V malém náhledu ani na celé obrazovce není. Celá obrazovka sdílí rozložení normálního okna (Q4), takže úprava v normálním okně platí i pro ni.
- **Celá obrazovka (Q4):** celá obrazovka používá **stejné rozložení jako normální okno** (jako dosud u T-036).
- **Uložení (Q7):** rozložení se uloží až po **„Hotovo“**, **Esc** změny zahodí.
- **Zvýraznění (Q8):** klíčový údaj widgetu se vykreslí **akcentovou barvou** (`#FA6831`):
  - Stav → procento průběhu,
  - Časy → „Zbývá“,
  - Teploty → teplota trysky,
  - Název tisku → název (návrh architekta, designer potvrdí v návrhu),
  - Vychladnutí → aktuální teplota trysky, ve stavu „Vychladlo“ text stavu (návrh architekta, designer potvrdí v návrhu).
- **Průhlednost pozadí (Q9):** **jedno globální nastavení** pro všechny karty i tiskárny. Rozsah **30–95 %**, výchozí **72 %**.
- **Barvy (Q10):** tryska **oranžová**, podložka **modrá**. **Graf v okně Diagnostika používá stejné barvy.** Přesné odstíny (kontrast na tmavé kartě i v diagnostice) navrhne designer v návrhu.

## Rozsah
Do sekce `## Návrh UI` tohoto souboru patří ASCII drátěné modely a text, jako u T-033:
1. **Karty a styl:**
   - rozměry, odsazení, mezery mezi kartami v kotvě, poloměr,
   - písmo a velikosti (S/M/L pro každý widget),
   - barvy: pozadí, text, akcent, stavové barvy tečky (navázat na barvy tray ikony), tryska oranžová a podložka modrá,
   - průhlednost pozadí 30–95 %, výchozí 72 %, globální,
   - tabulková čísla, čitelnost na světlém i tmavém obraze,
   - chování v malém okně (co se zkrátí / skryje), dlouhé texty a nejdelší jazyk (de, it).
2. **Každý widget ve velikostech S/M/L:**
   - obsah,
   - zvýraznění klíčového údaje akcentovou barvou (viz Rozhodnutí),
   - co se ukazuje bez dat (tiskárna nečinná, offline, neznámá teplota, bez náhledu G-code),
   - kdy se widget sám skryje.
3. **Kotvy:**
   - odsazení od okrajů,
   - vztah k tlačítkům okna vpravo nahoře (kotva vpravo nahoře a nahoře uprostřed),
   - směr skládání, zarovnání textu v pravých kotvách, středové kotvy,
   - chování při překryvu kotev v úzkém okně.
4. **Malý náhled (pás pod obrazem):**
   - výšky řádků pásu (≈ 30 px základní řádek, řádek názvu, ≈ 60 px řádek grafu),
   - pruh průběhu na horní hraně pásu (výška, barva),
   - text „19 % · 2 h 35 min“ a jeho stavy bez úlohy, offline a během vychladnutí,
   - dvě přepínací tlačítka (ikony, stav sbaleno / rozbaleno, fokus, tooltip),
   - kompaktní podoba názvu tisku, zástupný text grafu „Žádná data“, pořadí řádků,
   - minimální šířka 240,
   - vztah k úchytům pro změnu velikosti (okraje bez rámu) a k tlačítkům okna.
5. **Režim rozložení (C), jen v normálním okně:**
   - tlačítko v řadě tlačítek okna a položka kontextové nabídky,
   - ztlumení obrazu, zóny kotev, zvýraznění cíle, tažení a přichycení,
   - **pořadí v kotvě tažením**,
   - popover (zvýraznit, S/M/L),
   - lišta „Obnovit výchozí“ / „Hotovo“ (Esc = zrušit),
   - **ovládání klávesnicí**: výběr widgetu, přesun mezi kotvami, pořadí v kotvě, otevření popoveru, Enter / Esc,
   - fokus a jeho vizuál, čtečka obrazovky (`AutomationProperties.Name`), návrh všech `AutomationId`.
6. **Ikony:** seznam potřebných ikon z Fluent UI System Icons s přesnými názvy a variantou. Potřeba jsou například: zbývá, dokončení, doba tisku, tryska, podložka, materiál, vrstva, průměr trysky, vychladnutí, rozložení, přepínače malého náhledu.
7. **Výchozí rozložení:** kotva, pořadí, velikost, zvýraznění a viditelnost pro každý widget.
   - Určit „hlavní kotvu“ výchozího rozložení, tj. kotvu widgetů Stav a Časy.
   - Starý roh T-036 přesune výchozí rozložení zrcadlením tak, aby hlavní kotva skončila ve starém rohu (pravidlo v T-077). Výchozí rozložení musí po zrcadlení dávat smysl ve všech 4 rozích, i s ohledem na tlačítka okna vpravo nahoře.
8. **Podzáložka Překryv v nastavení:**
   - tabulka widgetů **jen s viditelností**, bez posuvníku ve výchozí velikosti 780 i ve všech 8 jazycích,
   - akce „Použít toto rozložení u všech tiskáren“,
   - globální průhlednost pozadí (kde v nastavení a jak naznačit, že platí pro všechny tiskárny),
   - umístění prahu vychladnutí (u tiskárny) a akce „Použít práh u všech tiskáren“.
9. **Notifikace „vychladlo“:** text titulku a těla toastu (cs + en, ostatní jazyky navrhne ui-dev), umístění přepínače v záložce upozornění.
10. Seznam všech nových textů (klíče `Strings.*`) s českou a anglickou podobou.

## Akceptační kritéria
- [ ] Sekce `## Návrh UI` pokrývá body 1–10. Obsahuje drátěné modely pro:
  - normální okno (výchozí rozložení),
  - plné rozložení všech widgetů ve všech velikostech,
  - malý náhled (sbalený, s názvem, s grafem, s obojím, bez dat),
  - režim rozložení (tažení, změna pořadí, popover, fokus z klávesnice),
  - stav vychladnutí (oba stavy),
  - podzáložku Překryv v nastavení (výchozí a nejdelší jazyk).
- [ ] Varianta B (panel / šuplík) se v návrhu nevyskytuje
- [ ] Návrh je v souladu se sekcí Rozhodnutí
- [ ] Ikony: názvy z Fluent UI System Icons, licence MIT ověřená (odkaz na `LICENSE`), žádná závislost za běhu
- [ ] Návrh schválený uživatelem (datum schválení v `## Rozhodnutí`)

## Otevřené otázky
- (žádné – Q1–Q10 rozhodnuté 2026-10-24, viz Rozhodnutí; zbývá schválení hotového návrhu)

## Poznámky k implementaci
(vyplní agent po dokončení)
