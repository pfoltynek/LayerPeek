---
id: T-214
title: Headless testy rozložení se systémovým písmem San Francisco (macOS) ve všech jazycích
status: todo
owner: qa-reviewer
priority: P3
platform: macos
depends_on: [T-031]
---

> **Rozhodnuto uživatelem 2026-11-13:** prohlídku Nastavení na MacBooku 13" (T-038 otázka 3 a body „se systémovým
> písmem nic neuříznuté“ v T-127) nahrazuje headless test se San Francisco. Písmo se načte z `/System/Library/Fonts`
> na Macu, kde test běží. **Nedodává se, necommituje se** a do snímků se nedostane.

## Cíl
Headless UI testy (`tests/LayerPeek.App.UiTests`, `HeadlessSession`) dnes měří rozložení jen s písmem Inter
(`fonts:Inter#Inter`, bez náhradních písem). Aplikace na macOS ale kreslí systémovým písmem (San Francisco), které má
jiné šířky. Několik bodů T-127 proto čeká na prohlídku okem („se systémovým písmem nic neuřízne“). Tento úkol přidá
tytéž kontroly rozložení se San Francisco ve všech jazycích UI.

## Rozsah
- **Druhá headless relace s písmem San Francisco:** Avalonia se inicializuje jednou za proces. Proto samostatný testovací
  projekt (např. `tests/LayerPeek.App.UiTests.SystemFont`) nebo samostatná sestava, ne přepínání písma za běhu. Sdílený
  kód testů rozložení přes odkazované soubory (`tests/Shared`, žádné kopie).
  - Písmo: `SFNS.ttf` (a `SFNSItalic.ttf`, je-li potřeba) z `/System/Library/Fonts`, načtené jako vlastní kolekce.
    Výchozí rodina = SF, bez náhradních písem (stejně jako Inter dnes).
  - Bez písma (Windows, CI, jiná verze macOS bez souboru): třída testů se **přeskočí** s důvodem, ne `FAIL`.
  - Jen kontroly rozložení: posuvník, uříznutý jednořádkový text, záhlaví na jednom řádku, překryv prvků. **Žádné
    snímky**, aby se písmo Applu nedostalo do repozitáře.
- **Kalibrace:** `SFNS.ttf` je proměnné písmo. Test ověří, že šířka několika vzorových řetězců (běžná a tučná váha
  použité v UI) se od šířky AppKitu (`NSString.size(withAttributes:)` se systémovým písmem stejné velikosti) liší
  nejvýš o ~2 %. Referenční šířky změří nový příkaz helperu `textwidth` (nebo jednorázově zapsané hodnoty s verzí
  macOS). Když Avalonia osu váhy nepoužije a odchylka je větší, zapsat do otázek a nepokračovat odhadem.
- **Pokrytí (všechny jazyky `Strings.*.resx`):**
  - Nastavení ve velikosti z návrhu na pracovní ploše MacBooku 13" (výška bez menu baru a Docku; přesné rozměry
    zapsat do poznámek): všechny hlavní záložky a podzáložky tiskárny včetně Nahrávání, Detekce, Kamera, Timelapse
    a štítků „Experimentální“;
  - O aplikaci, Diagnostika;
  - okno kamery s řádkem „Další pokus asi za …“ (T-188, nejdelší jazyky es / fr);
  - pět záhlaví hlavních záložek na jednom řádku i ve francouzštině (T-146).
- Nalezené uříznutí = nový úkol pro ui-designer (rozhodnutí T-038 otázka 3: přizpůsobit texty / rozložení, nebo dodávat
  Inter i na macOS, rozhodne uživatel). Test pro takové místo zůstane červený, žádné `Skip` bez rozhodnutí.

## Akceptační kritéria
- [ ] Testy rozložení se San Francisco běží v `dotnet test` na Macu, na Windows / bez písma se přeskočí s důvodem
- [ ] Kalibrace šířek proti AppKitu v toleranci (hodnoty a verze macOS v poznámkách)
- [ ] Všechny jazyky: Nastavení (všechny záložky a podzáložky), O aplikaci, Diagnostika a řádek dalšího pokusu
      kamery bez posuvníku a bez uříznutí. Uzavře v T-127 bod **T-038** otázka 3 (odpadá, nebo je založený úkol)
      a části „se systémovým písmem nic neuříznuté“ u bodů **T-146**, **T-148**, **T-154**, **T-159**, **T-161**
      a **T-188**
- [ ] V repozitáři ani v artefaktech testů není soubor písma ani snímek vykreslený písmem SF
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
