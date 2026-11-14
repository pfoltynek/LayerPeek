---
id: T-214
title: Headless testy rozložení se systémovým písmem San Francisco (macOS) ve všech jazycích
status: done
owner: qa-reviewer
priority: P3
platform: macos
depends_on: [T-031]
---

> **Rozhodnuto uživatelem 2026-11-13:** prohlídku Nastavení na MacBooku 13" (T-038 otázka 3 a body „se systémovým
> písmem nic neuříznuté“ v T-127) nahrazuje headless test se San Francisco. Písmo se načte z `/System/Library/Fonts`
> na Macu, kde test běží. **Nedodává se, necommituje se** a do snímků se nedostane.

> **Rozhodnuto uživatelem 2026-11-14:** (1) varianta a – kalibrovat proti systémovému písmu Avalonie (to, co aplikace
> kreslí), ne proti AppKitu; měření AppKitu zůstává jen jako informační výstup, který nikdy neselže. Body T-127 se
> uzavřou s odkazem na T-214 a do T-127 přibude bod pro uživatele: ověřit šířku jednoho textu v běžící aplikaci.
> (2) Rozdíl vzhledu SF proti nativním aplikacím posoudí ui-designer v novém úkolu T-222 (P3, bez změny UI bez
> schválení).

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
- [x] Testy rozložení se San Francisco běží v `dotnet test` na Macu, na Windows / bez písma se přeskočí s důvodem
- [x] Kalibrace šířek proti AppKitu v toleranci (hodnoty a verze macOS v poznámkách) – rozhodnutím uživatele 2026-11-14
      nahrazeno kalibrací proti systémovému písmu Avalonie (shoda na 0,01 DIP); odchylka od AppKitu je v poznámkách
      a jako informační výstup testu, potvrzení v běžící aplikaci → přesunuto do T-127 (bod 1k)
- [x] Všechny jazyky: Nastavení (všechny záložky a podzáložky), O aplikaci, Diagnostika a řádek dalšího pokusu
      kamery bez posuvníku a bez uříznutí. Uzavře v T-127 bod **T-038** otázka 3 (odpadá, nebo je založený úkol)
      a části „se systémovým písmem nic neuříznuté“ u bodů **T-146**, **T-148**, **T-154**, **T-159**, **T-161**
      a **T-188**
- [x] V repozitáři ani v artefaktech testů není soubor písma ani snímek vykreslený písmem SF
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. ~~**Kalibrace neprošla, proti čemu kalibrovat?**~~ **Rozhodnuto 2026-11-14: varianta a.** (qa-reviewer, 2026-11-14) Avalonia 12.1.4 u proměnného `SFNS.ttf`
   nepoužije ani osu váhy, ani optickou velikost: vždy výchozí instance (wght 400, opsz 28 = „Display“), SemiBold
   a Bold jen uměle ztuční se stejnými šířkami. Proti AppKitu je proto text o 7–13 % (běžná váha) a o 14–21 % (polotučná,
   tučná) užší, tolerance 2 % je nedosažitelná. Avalonia ale stejné šířky dává i u systémového písma, které na macOS sama
   najde přes CoreText (`.AppleSystemUIFont` → „System Font“, shoda na 0,01 DIP ve všech vzorcích, test
   `WidthsMatchTheSystemFontOfAvaloniaOnMacOS`). Aplikace tedy text nejspíš rozvrhuje přesně jako tento headless test,
   ne jako AppKit (neověřeno na běžící aplikaci: předpoklad, že Avalonia.Native na macOS používá správce písem
   z Avalonia.Skia jako headless).
   - a) Referencí bude systémové písmo Avalonie (to, co aplikace skutečně kreslí): test proti AppKitu se nahradí,
     výsledky rozložení níže platí a T-127 (T-038 otázka 3 a části „se systémovým písmem“ u T-146, T-148, T-154, T-159,
     T-161, T-188) se odškrtne. Doporučuji, nejlépe s krátkým ověřením šířky jednoho textu v běžící aplikaci (T-115).
   - b) Referencí zůstane AppKit: headless test s Avalonií 12.1.4 toleranci nesplní, prohlídka okem by zůstala.
   Do rozhodnutí byl test `WidthsMatchAppKitWithinTolerance` červený; po rozhodnutí je z něj informační
   `AppKitWidthsForInformation`.
2. ~~**Vedlejší zjištění pro ui-designer / uživatele:**~~ **Rozhodnuto 2026-11-14: úkol T-222.** pokud platí předpoklad z otázky 1, LayerPeek na macOS kreslí San
   Francisco jinak než nativní aplikace (užší „Display“ tvary i v malých velikostech, polotučné a tučné písmo jen
   uměle ztučněné). Založit úkol (posoudit vzhled, případně dodávat Inter i na macOS = varianta z T-038 otázky 3, nebo
   nahlásit Avalonii)? Rozhoduje uživatel.

## Poznámky k implementaci
- **Nový projekt `tests/LayerPeek.App.UiTests.SystemFont`** (v `LayerPeek.slnx`, `InternalsVisibleTo` v App). Testy
  rozložení se do něj odkazují z `LayerPeek.App.UiTests` (žádné kopie): `SettingsWindowLayoutTests`,
  `AboutWindowLayoutTests`, `DiagnosticsWindowLayoutTests`, `CameraWindowNextAttemptUiTests` a jejich harnessy,
  `HeadlessSession`, `Ui`, `UiTestScope`. Odkazované fakes obou projektů jsou v
  `tests/LayerPeek.App.UiTests/LinkedTestFiles.props` (importují ho oba projekty).
- **Písmo:** `SystemFont.cs` čte `/System/Library/Fonts/SFNS.ttf` za běhu testu (`SystemFontCollection`, rodina pod
  pevným jménem „San Francisco“, protože soubor se jmenuje ve 30 jazycích). Výchozí rodina = SF, bez náhradních písem.
  Projekt definuje `LAYERPEEK_SYSTEM_FONT`; s ním `HeadlessSession` nezakládá složku snímků a bez písma (Windows,
  Linux, macOS bez souboru) nespustí relaci a každý test přeskočí s důvodem (`Assert.Skip`). Ověřeno podvržením chybějící
  cesty: 186 přeskočeno, 0 selhalo, kód 0.
- **Žádné písmo ani snímek SF v repozitáři či artefaktech:** `SnapshotAssert` je v projektu náhrada, která porovnání
  snímku přeskočí s důvodem (3 testy O aplikaci; s Inter je porovnává `LayerPeek.App.UiTests`); obrázky se nezapisují.
  V gitu žádný `.ttf`/`.otf`/`.ttc`. CI nahrává jen TRX (text).
- **Úpravy sdíleného kódu (sémantika snímků beze změny):** `HeadlessTestApp` přesunut z `HeadlessSession.cs` do vlastního
  souboru (projekt SF má vlastní se SF); `HeadlessSession` má tři bloky `#if LAYERPEEK_SYSTEM_FONT`.
  `DiagnosticsWindowLayoutTests.LongName` prodloužen o „, by the north door“: v užším SF se původní jméno vešlo a test
  „dlouhé jméno se zkrátí“ selhal kvůli příliš krátkému vzorku, ne kvůli aplikaci (snímky ho nepoužívají).
- **Kalibrace (macOS 27.2.1, build 26B131, Avalonia 12.1.4, 2026-11-14):** referencí je podle rozhodnutí uživatele
  systémové písmo, které Avalonia na macOS sama najde (`.AppleSystemUIFont` přes CoreText → „System Font“). Test
  `WidthsMatchTheSystemFontOfAvaloniaOnMacOS`: 10 vzorků (en, cs, fr, de, es, pl, it), 12/13/14/16 pt, běžná, polotučná
  a tučná váha – shoda se `SFNS.ttf` na 0,01 DIP. `DefaultFontOfTheTestsIsSanFrancisco` hlídá, že výchozí rodina testů
  je opravdu SF.
- **AppKit jen informačně:** `CoreTextWidth` měří šířku přes CoreText (`CTLine` systémového UI písma s vahou
  `NSFont.Weight` 0 / 0,3 / 0,4 = to, co měří `NSString.size(withAttributes:)`), bez helperu a bez zapsaných hodnot.
  `AppKitWidthsForInformation` vypíše rozsah do výstupu testu (TRX), nikdy neselže: Avalonia proti AppKitu běžná
  −13,2 až −6,9 %, polotučná −18,0 až −11,6 %, tučná −20,7 až −14,1 % (např. 13 pt „Startup and updates“: Avalonia
  110,31 DIP pro všechny váhy, AppKit 123,65 / 129,11 / 132,28). Příčina: Avalonia nepoužije osy wght ani opsz
  proměnného písma (výchozí instance, tučné jen uměle) – vzhled posoudí T-222.
- **Potvrzení v běžící aplikaci:** `MainTabWidthsForTheCheckInTheRunningApp` vypíše šířky záhlaví hlavních záložek
  Nastavení v angličtině (Printers 82, Widgets 88, General 83, Startup and updates 177, Recording 101, Camera 84 pt;
  výška 36). Bod 1k v T-127: uživatel je porovná s rámečky z `helper ax` v běžící aplikaci.
- **Výsledek rozložení se SF:** všechny jazyky, Nastavení ve všech stavech, záložkách a podzáložkách (vč. Nahrávání,
  Detekce, Kamera, Timelapse a štítků „Experimentální“; záhlaví hlavních záložek na jednom řádku i ve francouzštině),
  O aplikaci, Diagnostika a řádek „Další pokus asi za …“ (960 × 540 až 320 × 180): bez posuvníku, bez uříznutí a bez
  překryvu tam, kde ho kontrolují testy s Inter (tlačítka pod nápovědami, stavové pole kamery; obecnou kontrolu
  překryvu všech prvků testy nemají). Žádné uříznutí, tedy žádný úkol pro ui-designer kvůli rozložení. T-127: T-038
  otázka 3 odškrtnuta (odpadá), části „se systémovým písmem“ u T-146, T-148, T-154, T-159, T-161 a T-188 odškrtnuty
  jako podbody.
- **Velikost Nastavení:** testováno v navržené klientské velikosti 780 × 532 DIP (okno se v ní otevírá,
  `AuxiliaryWindowPlacement`); s titulkem macOS 28 pt je okno 780 × 560 pt. Nejmenší pracovní plocha 13" MacBooku:
  volba „vypadá jako 1280 × 800“ minus menu bar 24 pt (37 pt s výřezem u Air M2+, výchozí 1470 × 956) a výchozí Dock
  ~70 pt ≈ 1280 × 706 pt, okno se tedy vejde na všech 13" MacBoocích bez zmenšení.
- **CI (T-218):** runner `macos-26` má `SFNS.ttf`, testy SF tam poběží (~1 min); na Windows se všechny přeskočí.
- **Doba běhu:** projekt SF ~55–80 s (Nastavení 42 s), jen na Macu; jinde se vše přeskočí za < 1 s.
- **Závislosti:** žádný nový balíček (Avalonia.Headless, Avalonia.Skia, xunit.v3 už jsou v `Directory.Packages.props`);
  P/Invoke přes `[LibraryImport]` jen v testech.
