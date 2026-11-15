---
id: T-222
title: Vzhled písma San Francisco v aplikaci na macOS proti nativním aplikacím (užší tvary, umělé tučné)
status: in-progress
owner: ui-designer
priority: P3
platform: macos
depends_on: [T-214]
---

> **Rozhodnuto uživatelem 2026-11-14 (z T-214, otázka 2):** založit úkol pro ui-designer. Žádná změna UI bez schválení
> uživatelem.

## Cíl
T-214 zjistil, že Avalonia 12.1.4 u proměnného systémového písma `SFNS.ttf` nepoužije osu váhy ani optické velikosti:
vždy kreslí výchozí instanci (wght 400, opsz 28 = tvary „Display“), polotučné a tučné písmo jen uměle ztuční. Proti
AppKitu je text o 7–13 % (běžná váha) a o 14–21 % (polotučná, tučná) užší. LayerPeek proto na macOS nejspíš vypadá
jinak než nativní aplikace (užší, hustší text v malých velikostech, „rozmazaně“ tučná záhlaví). Úkol to posoudí
a navrhne řešení; rozhodne uživatel.

## Rozsah
- Porovnat vzhled textu LayerPeeku (Nastavení, O aplikaci, Diagnostika, okno kamery) s nativními aplikacemi macOS
  (např. Nastavení systému, Finder) ve stejné velikosti: šířka, tvary, tučná záhlaví. Snímky jen lokálně (písmo Applu
  se necommituje; snímky obrazovky do repozitáře nepatří).
- Ověřit předpoklad z T-127 bodu 1k (běžící aplikace rozvrhuje text stejně jako headless test T-214).
- Zhodnotit možnosti s dopadem na rozložení, velikost balíčku, licenci a AOT, např.:
  - dodávat Inter i na macOS (jako headless testy; varianta z T-038 otázky 3);
  - nahlásit chybu / požadavek Avalonii (osy proměnných písem, optická velikost) a počkat;
  - ponechat stav (rozložení je otestované, T-214).
- Výsledek: doporučení s podklady v `## Poznámky k implementaci` a otázky pro uživatele. **Žádná změna UI bez
  schválení uživatelem**; implementace případně jako nový úkol.

## Akceptační kritéria
- [x] Porovnání s nativními aplikacemi popsané (velikosti, váhy, rozdíly), snímky jen mimo repozitář
- [x] Možnosti zhodnocené (rozložení, balíček, licence, AOT) s doporučením
- [ ] Rozhodnutí uživatele zapsané, případná implementace založená jako nový úkol

## Otevřené otázky
1. **Které řešení?** (podklady v Poznámkách, tabulka možností)
   - A) Dodávat Inter na macOS (výchozí písmo aplikace jen na macOS) – správné rozestupy a skutečné řezy hned, rozložení
     = to, co už testují snímky s Inter; ale tvary nejsou nativní SF.
   - B) Ponechat San Francisco a nahlásit chybu Avalonii (návrh textu níže), po opravě přejít na novou verzi.
   - C) Ponechat stav bez dalšího kroku.
   - D) Ponechat SF a dorovnat šířky `LetterSpacing` jen na macOS – nedoporučuji (rovnoměrné prostrčení neopraví
     kolize tučných tvarů, jen je rozředí; každá velikost jiná hodnota).
   - **Doporučení: A + B** – Inter na macOS jako přechodné řešení a zároveň nahlásit chybu; až Avalonia vydá verzi, kde
     systémové písmo na macOS rozvrhuje stejně jako AppKit, vrátit se k SF (nový úkol). Pokud je pro vás nativní tvar
     písma důležitější než rozestupy, pak B.
2. **(jen při A) Jak Inter dodat?**
   - a) Balíček `Avalonia.Fonts.Inter` 12.1.4 (MIT, písmo SIL OFL 1.1; už je v `Directory.Packages.props` pro testy) –
     6 řezů, +1,89 MB DLL, stejná verze jako testy automaticky.
   - b) Jen 3 použité řezy (Regular, SemiBold, Bold, 0,94 MB) jako vlastní `avares` prostředky z téhož balíčku/verze –
     o 0,95 MB menší, ale vlastní kolekce písma a hlídání verze proti testům.
   - **Doporučení: a)** (jednodušší, testy a aplikace mají vždy totéž písmo); b) jen pokud je velikost balíčku priorita.
3. **(jen při A) Jen macOS, nebo i Windows?** Windows kreslí Segoe UI správně (statické písmo), změna tam není potřeba.
   **Doporučení: jen macOS.**
4. **(jen při B) Smím založit issue na GitHubu Avalonie** s textem níže (zápis na GitHub jen s vaším souhlasem)?
   Doporučení: ano.

## Poznámky k implementaci
**Stav (2026-11-14):** analýza hotová, čeká na rozhodnutí uživatele (kritérium 3) → úkol zůstává v `in-progress`.
Žádná změna UI ani kódu aplikace.

### Metoda (offscreen, obrazovka Macu byla zamčená)
- macOS 27.2.1 (26B131), Avalonia 12.1.4 headless se Skia (stejný vykreslovač jako aplikace), AppKit/CoreText ze
  Swiftu. Vzorky = skutečné texty UI: „Startup and updates“, „Spuštění a aktualizace“, „Autostart und Updates“,
  „Enregistrement“, „Verbindung testen“, „Testuj połączenie“, „À propos de LayerPeek“, „Diagnostika – LayerPeek“,
  „How often the printers are asked for their state.“, „Leave empty to keep the saved password.“; velikosti 11–17 pt
  (aplikace používá hlavně 11–16 pt, polotučné 34×, tučné 7×); váhy běžná / polotučná / tučná.
- Vykreslovače: **AppKit SF** (`NSFont.systemFont(ofSize:weight:)` = nativní aplikace), **Avalonia SF** (rodina
  `.AppleSystemUIFont`, kterou aplikace na macOS dostane; shodná šířka se `SFNS.ttf` z T-214 na 0,000 %), **Avalonia
  Inter** (balíček `Avalonia.Fonts.Inter`), kontrolně AppKit Inter a CoreText SF ve výchozí instanci (wght 400, opsz 28).
- Snímky jen lokálně ve scratchpadu agenta (písmo Applu do repozitáře nepatří), skripty tamtéž; nic se necommituje.

### Výsledky – šířka (Avalonia proti AppKitu, 10 vzorků × 7 velikostí)
| Vykreslení | běžná | polotučná | tučná |
|---|---|---|---|
| Avalonia SF (dnes) | −14,8 až −6,3 % (∅ −10,1 %) | −18,4 až −10,4 % (∅ −14,0 %) | −20,4 až −12,6 % (∅ −16,0 %) |
| Avalonia Inter | −1,4 až +6,6 % (∅ +2,6 %) | −3,2 až +4,1 % (∅ +0,7 %) | −4,5 až +2,9 % (∅ −0,6 %) |

- Rozdíl SF roste k malým velikostem: 11 pt běžná −13,0 % / polotučná −16,6 % / tučná −18,6 %, 17 pt −7,2 / −11,3 /
  −13,6 %. Příklad 13 pt „Startup and updates“ (AppKit / Avalonia SF / Avalonia Inter): běžná 123,6 / 110,3 / 126,0,
  polotučná 129,1 / 110,3 / 128,8, tučná 132,3 / 110,3 / 130,2 DIP.
- Avalonia Inter = AppKit Inter na 0,0 % ve všech případech → metoda je správná, chyba je jen v cestě proměnného
  systémového písma.
- Avalonia SF má posuny (advance) výchozí instance bez tabulky `trak` (sledování podle velikosti): proti CoreText ve
  výchozí instanci shoda přesně ve 12 pt, v 11 pt CoreText +1,3 %, v 17 pt −5,7 %.

### Výsledky – tvary a váha
- **Nová zjištění proti T-214:** běžící aplikace (rodina `.AppleSystemUIFont` přes Skia/CoreText) kreslí obrysy
  **skutečné** váhy (podíl „inkoustu“ polotučná/běžná 1,30, tučná/běžná 1,47 – AppKit 1,35 / 1,55), ale rozestupy bere
  z výchozí instance běžné váhy. Polotučné a tučné písmo proto nevypadá „rozmazaně uměle ztučněné“, ale **namačkané**:
  písmena se dotýkají (např. „nt“, „rt“ v „Enregistrement“, „pd“ v „updates“), mezery mezi slovy jsou zúžené. Umělé
  ztučnění (1,17 pro obě váhy, polotučná = tučná) dělá jen `SFNS.ttf` načtený ze souboru v testech T-214 – na šířky to
  vliv nemá (T-214 platí), na vzhled ano.
- Běžná váha: tvary podle inkoustu odpovídají textovým tvarům AppKitu, text je ale o ~10 % hustší (užší rozestupy,
  chybí `trak`); v 11–13 pt na první pohled „sražený“ text.
- Skia kreslí obecně o ~16 % tmavší (víc inkoustu) než Core Graphics – stejně u Inter i SF (rozdíl vykreslovače, ne
  písma); týká se jakékoli volby a řešení nevyžaduje.
- Inter: rozestupy i váhy odpovídají AppKitu (polotučná/běžná 1,35 jako AppKit), tvary jsou jiné (vyšší střední výška
  „x“, jiná „a“, „t“, „G“) – na první pohled „ne-Apple“ písmo, ale čisté.

### Možnosti
| | Rozložení | Balíček | Licence | AOT | Vzhled |
|---|---|---|---|---|---|
| A) Inter na macOS | = snímky a testy s Inter (všech 8 jazyků už prochází); proti dnešku text o 14–18 % širší, tedy vše, co dnes testuje projekt SF, má menší rezervu – ale to je přesně stav snímků. Projekt `LayerPeek.App.UiTests.SystemFont` by pak netestoval písmo aplikace (zrušit, nebo nechat pro návrat k SF – otázka do implementačního úkolu) | +1,89 MB (6 řezů), nebo +0,94 MB (3 řezy) | balíček MIT, písmo SIL OFL 1.1 – svobodná licence, distribuce spolu s GPL aplikací je v pořádku (písmo není kód linkovaný do programu); doplnit do seznamu licencí třetích stran (`Licenses/ThirdPartyLicenses.cs`) a do manuálů/README, pokud licence vypisují | bez rizika: jen vložené prostředky + `WithInterFont()`, bez reflexe | správné rozestupy i váhy, ne-nativní tvary |
| B) Nahlásit Avalonii a čekat | beze změny | beze změny | – | – | do opravy namačkané; oprava nejistá (viz níže) |
| C) Ponechat | beze změny (otestováno T-214) | – | – | – | namačkaný text, hlavně polotučná záhlaví 11–14 pt |
| D) `LetterSpacing` jen na macOS | nutné přepočítat T-214 testy | – | – | bez rizika | jen přibližné, tučné kolize zůstávají |

### Stav v Avalonii (GitHub jen čteno, 2026-11-14)
- Poslední vydání 12.1.6 (9. 11. 2026); poznámky k vydání 12.1.5 a 12.1.6 o proměnných písmech nic neuvádí → **novější
  12.1.x problém neopravuje** (nezkoušeno sestavením).
- `master` (verze 12.2.999) má rozpracovanou sérii „[Text] Glyph Outline“ (Gillibald): část 6/17 #22179 (osy fvar/avar,
  `GlyphTypeface.VariationAxes`, `FontVariationSettings`) sloučena 30. 10. 2026; části 6–12 podle popisu sloučeny;
  část 13/17 #22665 (výběr instance proměnného písma místo umělého ztučnění) **otevřená**. Žádná z nich nezmiňuje
  systémové písmo macOS, automatickou optickou velikost (opsz podle velikosti, jako CoreText) ani tabulku `trak`.
- Staré issue #11092 „FontWeight not working with fonts in Resources“ (proměnná písma) otevřené od 2023, bez plánu.
- Odhad: oprava pro SF na macOS nejdřív ve 12.2 / 13, a jen pokud se systémové písmo napojí na novou cestu.

### Návrh textu issue pro Avalonii (nezaloženo – čeká na souhlas uživatele)
> **macOS: system font (.AppleSystemUIFont / SF Pro variable) is laid out with default-instance advances while glyphs
> are drawn at the requested weight – SemiBold/Bold glyphs collide, text 7–20 % narrower than AppKit**
>
> Avalonia 12.1.4 (Skia), macOS 27.2.1. With the default font family on macOS (`.AppleSystemUIFont`, the variable
> `SFNS.ttf`), `TextLayout` widths are identical for Normal, SemiBold and Bold (e.g. 13 pt "Startup and updates":
> 110.31 DIP for all weights; AppKit `NSFont.systemFont(ofSize:weight:)`: 123.65 / 129.11 / 132.28). The advances match
> the font's default instance (wght 400, opsz 28) without the `trak` table, while the glyphs are rasterized by Skia at
> the requested weight. Result: semibold/bold text looks cramped, letters touch; regular text is ~10 % tighter than in
> native apps. Expected: shaping uses the same variation instance as rendering (wght from FontWeight, opsz = point size
> as CoreText's automatic optical sizing) and applies `trak` tracking for the size, so widths match AppKit. Repro: measure
> `new TextLayout("Startup and updates", new Typeface(".AppleSystemUIFont", FontStyle.Normal, w), 13, …)` for the three
> weights and compare with `CTLineGetTypographicBounds`. Related: #11092, #22179, #22665.

### Co ověřeno nebylo
- Běžící aplikace na skutečné obrazovce (T-127 bod 1k – šířky záložek přes `helper ax`) zůstává uživateli; obrazovka
  byla zamčená. Vykreslení v běžící aplikaci (Skia přes Metal) se může v antialiasingu lišit od headless Skia; šířky
  a výběr písma jsou tytéž (T-214).
- Nativní aplikace (Nastavení systému, Finder) nebyly snímány – za ně stojí AppKit `NSFont.systemFont` offscreen, což je
  písmo, které tyto aplikace v ovládacích prvcích používají.
- Novější Avalonia (12.1.6, `master`) nebyla sestavena ani vyzkoušena.
- Windows beze změny (Segoe UI je statické písmo, problém se ho netýká).
