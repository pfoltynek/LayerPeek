# Úkoly (backlog)

Každý úkol = jeden Markdown soubor `T-XXX-kratky-nazev.md`. Stav úkolu určuje složka:

| Složka | Význam |
|---|---|
| `todo/` | připraveno k implementaci (nebo čeká na závislosti / odpovědi) |
| `in-progress/` | rozpracováno, případně blokováno otevřenými otázkami |
| `done/` | hotovo – build a testy procházejí, kritéria splněna |

Přesun mezi složkami vždy přes `git mv`, aby zůstala historie. Hodnota `status` ve frontmatteru musí odpovídat složce.

## Formát

Šablona: [_TEMPLATE.md](_TEMPLATE.md).

- `id` – `T-XXX`, nové ID = nejvyšší existující + 1
- `owner` – agent: `architect`, `core-dev`, `ui-designer`, `ui-dev`, `qa-reviewer`
- `priority` – `P1` (MVP), `P2`, `P3`
- `platform` – kde se úkol implementuje a ověřuje:
  - `all` – nezávislé na platformě (Core, klienti PrusaLink/Prusa Connect, sdílené Avalonia UI fungující na obou OS); implementuje se a ověřuje na OS, na kterém se právě vyvíjí
  - `windows` – kód nebo ověření specifické pro Windows; implementuje se jen na Windows
  - `macos` – kód nebo ověření specifické pro macOS; **implementuje je uživatel později, až bude pracovat na Macu**. Žádný úkol `all` ani `windows` na úkolu `macos` nezávisí.
- `depends_on` – seznam ID, které musí být v `done/`; úkoly `macos` závisí na svém sdíleném / Windows protějšku
- `## Otevřené otázky` – co musí rozhodnout uživatel. Úkol s nezodpovězenou otázkou, která blokuje implementaci, se neimplementuje.

## Zamítnuté úkoly (rozhodnutí uživatele 2026-11-01)

Úkol, který se nebude implementovat, se přesune (`git mv`) do `tasks/rejected/` se `status: rejected`. Pod frontmatter
přibude řádek `> **Zamítnuto (rozhodnutí uživatele RRRR-MM-DD):** <důvod>. Úkol se nebude implementovat.` a řádek
v tabulce dostane „(zamítnuto RRRR-MM-DD)“. Obsah úkolu zůstává jako historie; na zamítnutý úkol nesmí záviset žádný
otevřený úkol.

## Sběrné ověřovací úkoly (rozhodnutí uživatele 2026-10-30)

Kritérium, které už potřebuje jen ruční ověření uživatelem nebo kontrolu na druhém OS, se smí přesunout do sběrného
ověřovacího úkolu. Od 2026-11-01 (rozhodnutí uživatele): Windows → **T-124** (instalátor) nebo **T-125** (vše ostatní);
Mac → **T-115** (sezení s agentem na Macu), **T-126** (instalátor) nebo **T-127** (vše ostatní). Do 2026-11-01 to byly
**T-114 „Ověření na Windows“** (uzavřený) a **T-115 „Ruční ověření na Macu“**.

- V původním úkolu se kritérium odškrtne, původní text zůstane a na konec se připíše „→ přesunuto do T-114“ /
  „→ přesunuto do T-115“ (u obou OS „→ přesunuto do T-114 a T-115“). Do `## Poznámky k implementaci` přibude řádek
  „Uzavřeno RRRR-MM-DD: zbývající ruční ověření přesunuto do T-11x (rozhodnutí uživatele)“ a úkol se uzavře (`status: done`,
  `git mv` do `done/`).
- Ve sběrném úkolu má každý bod zaškrtávací pole, původní úkol a kritérium a krátký konkrétní postup (kontrola
  `tools/macos-verify.ps1` / `tools/windows-verify.ps1` nebo sezení v `docs/macos-manual-testing.md` /
  `docs/windows-manual-testing.md`). U přegenerování snímků UI seznam souborů podle původního úkolu.
- Přesunout nejde nedokončená práce (kód, dokumentace, rozhodnutí) ani kritérium úkolu, který blokuje nezodpovězená
  otevřená otázka – takový úkol zůstává v `in-progress/`. Chyba nalezená při ověření = nový úkol.
- Rozdělení 2026-11-01 (rozhodnutí uživatele): T-114 je uzavřený, jeho zbytek je v **T-124** (Windows – instalátor:
  instalace, upozornění po instalaci, autostart, odinstalace) a **T-125** (Windows – zbývající ruční ověření: celý
  interaktivní `tools/windows-verify.ps1`, tiskárna, vydání). Z T-115 se stejně oddělily **T-126** (macOS – instalátor)
  a **T-127** (macOS – zbývající ruční ověření); v T-115 zůstává sezení na Macu s agentem (snímky, build a testy, běh
  `tools/macos-verify.ps1`, kontroly T-117 / T-118 / T-122). Nově přesouvané body se píšou do těchto úkolů stejným
  způsobem („→ přesunuto do T-12x“).

## Větve (Gitflow)

- Každý úkol má vlastní větev `feature/T-XXX-kratky-nazev` (stejný název jako soubor úkolu), založenou z `develop`.
- Po dokončení úkolu se větev sloučí do `develop` (`git merge --no-ff`) a smaže.
- `main` obsahuje jen vydané verze. Dostává se do něj pouze sloučením větve `release/X.Y.Z` nebo `hotfix/X.Y.Z` a každé vydání má tag `vX.Y.Z`.
- Do `main` ani do `develop` se přímo necommituje.

## Jak přidat úkol

Napiš ho ručně podle šablony, nebo požádej Clauda např.: *„Použij agenta architect a rozpadni funkci X na úkoly.“*

## Přehled

Pořadí: nejdřív společné úkoly (`all`), pak Windows, nakonec macOS. Úkoly `macos` se dělají jen na Macu (uživatel je bude implementovat postupně později) a neblokují Windows.

### Společné (`all`)

| ID | Název | Platforma | Owner | Priorita | Závisí na |
|---|---|---|---|---|---|
| T-000 | Založení repozitáře a kostry řešení (hotovo) | all | architect | P1 | – |
| T-001 | Doménový model a abstrakce (hotovo) | all | core-dev | P1 | – |
| T-002 | Konfigurace tiskáren a uložení tajných údajů (hotovo; vč. Windows Credential Manager) | all | core-dev | P1 | T-001 |
| T-003 | PrusaLink klient (hotovo) | all | core-dev | P1 | T-001 |
| T-004 | Prusa Connect – průzkum API (spike) (hotovo) | all | architect | P1 | – |
| T-005 | Prusa Connect klient (vyřazeno – bez veřejného API, 2026-10-29) | all | core-dev | P3 | T-001, T-002, T-004 |
| T-006 | Monitorovací služba (polling) (hotovo) | all | core-dev | P1 | T-001 |
| T-007 | Tray ikona se stavem a progressem (hotovo) | all | ui-dev | P1 | T-006 |
| T-008 | Tray menu (hotovo) | all | ui-dev | P1 | T-007 |
| T-009 | Detekce událostí tisku (hotovo) | all | core-dev | P1 | T-006 |
| T-011 | Teploty a diagnostika (hotovo) | all | ui-dev | P2 | T-006 |
| T-012 | Historie telemetrie (hotovo) | all | core-dev | P3 | T-006 |
| T-013 | Více tiskáren (hotovo) | all | ui-dev | P2 | T-007, T-002 |
| T-014 | Kamera – živý stream a snímek | all | core-dev + ui-dev | P3 | T-003, T-015 |
| T-015 | Okno nastavení (+ registrace `ISecretStore` podle OS) (hotovo) | all | ui-dev | P1 | T-002, T-003 |
| T-023 | Stav MMU3 (průzkum zdroje dat) (zamítnuto 2026-11-01 – bez zdroje dat) | all | architect | P3 | T-003 |
| T-024 | Logování do souboru | all | core-dev | P2 | T-007, T-043, T-044 |
| T-025 | Ikona aplikace (exe, okna, tray) | all | ui-designer + ui-dev | P2 | T-007 |
| T-027 | PrusaLink – uživatelské jméno v nastavení (hotovo) | all | core-dev + ui-dev | P2 | T-003, T-015 |
| T-028 | Otevření webu PrusaLink s přihlášením (lokální proxy) (hotovo) | all | core-dev + ui-dev | P2 | T-027 |
| T-029 | Jazyk aplikace a další překlady (de, es, fr, it, pl, sk) (hotovo) | all | ui-dev | P2 | T-015 |
| T-030 | E2E testy webu přes proxy (Playwright + Edge) (hotovo) | all | qa-reviewer | P2 | T-028 |
| T-031 | Headless UI testy Avalonie (okno nastavení, tray menu) (hotovo) | all | qa-reviewer | P2 | T-015, T-028 |
| T-032 | Zámky a úklid testů zvlášť pro každý worktree (hotovo) | all | qa-reviewer | P3 | T-030, T-031 |
| T-033 | Okno nastavení bez posuvníku a podzáložky tiskárny (Tiskárna / Kamera) | all | ui-designer + ui-dev | P2 | T-014 |
| T-034 | Náhled obrazu kamery na záložce Kamera v nastavení | all | ui-dev | P2 | T-033 |
| T-035 | Výchozí velikost, monitor a umístění okna kamery | all | core-dev + ui-dev | P2 | T-033 |
| T-036 | Nastavení obsahu překryvu v okně kamery | all | core-dev + ui-dev | P2 | T-033 |
| T-037 | Okno kamery vždy navrchu (volba v nastavení) | all | core-dev + ui-dev | P2 | T-033 |
| T-039 | Obrázek tiskárny (podle modelu nebo vlastní) | all | core-dev + ui-designer + ui-dev | P3 | T-025, T-033 |
| T-040 | Úpravy okna nastavení a okna kamery po ručním testu | all | ui-dev | P2 | T-033, T-035, T-037 |
| T-041 | Okno Diagnostika ve velikosti nastavení bez scrollbaru | all | ui-designer + ui-dev | P2 | T-011, T-033 |
| T-042 | Jednotné tray menu (podmenu pro každou tiskárnu) | all | ui-dev | P2 | T-039, T-040 |
| T-043 | Jediná instance aplikace a jump list oken na hlavním panelu | all | ui-dev | P1 | T-010 |
| T-045 | Simulátor tiskárny (PrusaLink) a kamery (RTSP) pro vývoj a testy | all | core-dev | P2 | T-003, T-014 |
| T-046 | Zápis neošetřených výjimek do logu | all | core-dev | P2 | T-024 |
| T-047 | Nastavení „Podrobné logování (Debug)“ | all | core-dev + ui-designer + ui-dev | P2 | T-024, T-040 |
| T-048 | Podpora – otevření složky s logy a uložení diagnostického balíčku (ZIP) | all | core-dev + ui-designer + ui-dev | P2 | T-024, T-041 |
| T-049 | Podpora – kopírování informací do schránky | all | ui-dev | P3 | T-048 |
| T-050 | Bezpečnost – FFmpeg přijímá jen RTSP/TCP a H.264 (M1) | all | core-dev | P2 | T-014 |
| T-051 | Bezpečnost – proxy webu PrusaLink odmítá cizí zápisové požadavky (L1) | all | core-dev | P3 | T-028, T-030 |
| T-055 | Skripty pro vydání (build, AOT publish, archivy, kontrolní součty, GitHub Release) (hotovo; zkušební vydání v T-125 / T-127) | all | qa-reviewer | P2 | – |
| T-016 | CI – GitHub Actions (Windows + macOS) (zamítnuto 2026-11-01) | all | qa-reviewer | P3 | – |
| T-058 | Kontrola nové verze v aplikaci (GitHub Releases) + Velopack pro macOS (hotovo; ruční ověření v T-125 / T-127) | all | core-dev + ui-dev | P3 | T-008, T-029, T-055 |
| T-022 | Prusa Connect – přihlášení k účtu (zamítnuto 2026-11-01; dříve vyřazeno – bez veřejného API) | all | ui-dev | P3 | T-005, T-015 |
| T-074 | Přejmenování aplikace a repozitáře na LayerPeek | all | architect | P1 | T-065 |
| T-076 | Widgety překryvu – návrh UI (design spec) (hotovo) | all | ui-designer | P2 | – |
| T-077 | Widgety překryvu – model rozložení v Core, uložení a převod nastavení z T-036 (hotovo) | all | core-dev | P2 | T-076 |
| T-078 | Hezčí název tisku – rozbor názvu souboru z PrusaSliceru v Core (hotovo) | all | core-dev | P2 | – |
| T-079 | Vychladnutí po tisku – detekce v Core (hotovo) | all | core-dev | P2 | – |
| T-080 | Vychladnutí – volitelná notifikace a nastavení prahu (hotovo) | all | core-dev + ui-dev | P2 | T-076, T-079 |
| T-081 | Widgety překryvu – framework v okně kamery (registr, 6 kotev, karty) (hotovo) | all | ui-dev | P2 | T-076, T-077 |
| T-082 | Widget Stav (hotovo) | all | ui-dev | P2 | T-081 |
| T-083 | Widget Časy (hotovo) | all | ui-dev | P2 | T-081 |
| T-084 | Widget Teploty se sparkline (kompaktní `TemperatureChart`) (hotovo) | all | ui-dev | P2 | T-081 |
| T-085 | Widget Název tisku (bez náhledu G-code) (hotovo) | all | ui-dev | P2 | T-078, T-081 |
| T-086 | Náhled G-code – načtení z PrusaLink (Core + klient + simulátor) (hotovo) | all | core-dev | P3 | – |
| T-087 | Widget Název tisku – zobrazení náhledu G-code (hotovo) | all | ui-dev | P3 | T-085, T-086 |
| T-088 | Widget Vychladnutí (hotovo) | all | ui-dev | P2 | T-079, T-081, T-092 |
| T-089 | Malý náhled – informační pás pod obrazem a výška okna podle pásu (hotovo) | all | ui-dev | P2 | T-076, T-082, T-083 |
| T-090 | Malý náhled – sbalitelný název a graf v pásu, zapamatování stavu (hotovo) | all | ui-dev | P2 | T-084, T-085, T-089 |
| T-091 | Režim rozložení v okně kamery (jen normální okno, vstup i z nastavení) (hotovo) | all | ui-dev | P2 | T-077, T-082, T-083 |
| T-092 | Záložka Widgety v nastavení – seznam widgetů s vlastním nastavením, globální neprůhlednost, úprava rozložení (hotovo) | all | ui-dev | P2 | T-077, T-080, T-081, T-091 |
| T-094 | Okna Nastavení a Diagnostika – výchozí velikost 780 × 532, zvětšení na celou obrazovku a zapamatování velikosti (hotovo) | all | ui-dev | P2 | – |
| T-101 | Zkušební upozornění a nový dotaz na povolení notifikací (hotovo; ověření v T-125 / T-127) | all | ui-dev + ui-designer | P2 | T-010, T-020 |
| T-103 | Malý náhled – čas dokončení a stav vychladnutí v základním řádku pásu (hotovo; Windows snímky v T-114) | all | ui-designer + ui-dev | P2 | T-079, T-083, T-088, T-089, T-090, T-101 |
| T-104 | Malý náhled – nastavitelný obsah pásu (výběr položek jako u widgetů) – návrh (hotovo, rozhodnuto 2026-10-30, implementace T-110) | all | ui-designer | P3 | T-092, T-103 |
| T-106 | Kompaktní graf teplot – vlastní pás a měřítko pro každou řadu (hotovo; Windows snímky v T-114, tiskárna v T-127) | all | ui-dev | P2 | T-084, T-090 |
| T-105 | Ověřovací skripty pro uživatele (`tools/macos-verify.ps1`, `tools/windows-verify.ps1`) místo ručních kroků (hotovo; první běh v T-125 / T-115) | all | qa-reviewer | P1 | – |
| T-107 | Bezpečnost – proxy webu PrusaLink bez Service Workeru a s povolenými hlavičkami (N1) (hotovo; ruční ověření v T-125 / T-127) | all | core-dev | P2 | T-028, T-051 |
| T-109 | Vychladnutí – prahy trysky i podložky u každé tiskárny (widget Vychladnutí) (hotovo; Windows snímky v T-114) | all | ui-dev + core-dev | P2 | T-079, T-080, T-088, T-092, T-103 |
| T-110 | Malý náhled – nastavitelný obsah pásu (implementace návrhu T-104) (hotovo; Windows snímky v T-114) | all | ui-dev | P3 | T-104, T-103, T-109 |
| T-111 | Okno Nastavení – minimální výška tlačítek 32 DIP (z revize T-101) (hotovo; Windows snímky v T-114) | all | ui-dev | P3 | T-101, T-109, T-110 |
| T-112 | Okno Nastavení – zvýraznění chybného pole a přístupný stav chyby (z revize T-109) (hotovo; Windows snímky v T-114) | all | ui-designer + ui-dev | P3 | T-101, T-109, T-110, T-111 |
| T-113 | Oddělené vydání macOS a Windows pod jednou verzí (hotovo; ověření u 0.3.1 v T-125 / T-127) | all | qa-reviewer | P2 | T-055, T-058 |
| T-117 | Okno kamery – špendlík „Vždy navrchu“ (indikace a přepínání přímo v okně) (hotovo; ověření v T-114, nepovinný zbytek v T-125, macOS v T-115) | all | ui-dev | P2 | T-037, T-091, T-102 |
| T-118 | Malý náhled kamery – tenký rámeček okna (hotovo; ověření v T-114, nepovinný zbytek v T-125, macOS v T-115) | all | ui-dev | P3 | T-089, T-090, T-117 |
| T-119 | Windows – spolehlivé vytažení otevřeného okna do popředí (hotovo; ruční ověření v T-114) | windows | ui-dev | P2 | T-102, T-117 |
| T-120 | E2E testy místo ručních kontrol okna kamery (špendlík, obrys, hlavní panel, menu) (hotovo) | windows | qa-reviewer | P2 | T-102, T-117, T-118, T-119 |
| T-121 | Kontextové menu okna kamery – samotné písmeno spustí položku (hotovo) | all | ui-dev | P3 | T-069, T-117 |
| T-122 | Okno kamery – fokus při změně tlačítka na hlavním panelu (hotovo; macOS v T-115) | windows | ui-dev | P2 | T-102, T-117 |
| T-123 | Občasné selhání E2E „okno kamery zůstane otevřené při změně volby Vždy navrchu“ (hotovo) | windows | qa-reviewer | P3 | T-114 |
| T-131 | Občasné selhání StatusWidgetTests (Release, statická inicializace) (hotovo) | all | qa-reviewer | P3 | – |
| T-129 | windows-verify – testy interaktivního běhu (hotovo) | windows | qa-reviewer | P2 | T-105, T-125 |
| T-128 | E2E testy místo ručních kontrol – zkušební upozornění a aktualizace přes Velopack (hotovo; instalační případy v T-124) | windows | qa-reviewer | P2 | T-058, T-101, T-125 |
| T-132 | run-e2e.ps1 – uchovat výsledky neúspěšných běhů (hotovo) | windows | qa-reviewer | P3 | – |
| T-133 | E2E – návrat upozornění testovací aplikace, předpoklad testu instalátoru, aktivátor po AOT smoke | windows | qa-reviewer | P1 | T-128, T-017 |
| T-143 | Kamera – sdílené spojení se streamem na pozadí (okno, nahrávání, detekce) (hotovo) | all | core-dev + ui-dev | P2 | T-014, T-034 |
| T-144 | Nahrávání kamery – zápis streamu do souboru bez překódování (Camera.FFmpeg + abstrakce v Core) (hotovo; Windows v T-125) | all | core-dev | P2 | T-143 |
| T-145 | Nahrávání kamery – služba v Core (ruční a automatické nahrávání při tisku, složka, limit, rotace) (hotovo; Windows v T-125) | all | core-dev | P2 | T-144, T-009 |
| T-146 | Nahrávání kamery – UI (tray menu, okno kamery, nastavení) a E2E (hotovo; Windows v T-125, ruční macOS v T-127) | all | ui-designer + ui-dev | P2 | T-145, T-033, T-092 |
| T-148 | Nahrávání kamery fáze 2 – „dashcam“ (kruhový buffer, uložení při chybě tisku) (hotovo; Windows v T-125, ruční macOS v T-127) | all | core-dev + ui-dev | P3 | T-145, T-146 |
| T-149 | Timelapse tisku – průzkum (spike): způsob, licence, AOT (hotovo, rozhodnuto 2026-11-04) | all | architect | P3 | T-144 |
| T-150 | Okno kamery – automatické zavření při vypnuté tiskárně a znovuotevření (logika v Core) | all | core-dev | P2 | T-035, T-006 |
| T-151 | Okno kamery – automatické zavření a znovuotevření (App, nastavení, E2E) | all | ui-dev | P2 | T-150 |
| T-152 | Detekce chyb tisku z kamery – průzkum (spike): CV vs. ML, licence, AOT, Core One (hotovo, rozhodnuto 2026-11-04) | all | architect | P2 | – |
| T-153 | Detekce chyb tisku – infrastruktura (projekt LayerPeek.Detection, vzorkování, kontext tisku, události) (hotovo) | all | core-dev | P2 | T-143, T-152 |
| T-154 | Detekce chyb tisku – UI (nastavení detektorů, notifikace, falešný poplach) (hotovo; Windows v T-125, ruční macOS v T-127) | all | ui-designer + ui-dev | P2 | T-153, T-033 |
| T-155 | Detekce chyb tisku – první detektor „Změna na podložce“ (bed-change) (hotovo) | all | core-dev | P2 | T-152, T-153 |
| T-156 | Detekce chyb tisku – další detektory včetně první vrstvy (rozpad) (hotovo; vybrán „tisk do vzduchu“ → T-167) | all | architect | P3 | T-155 |
| T-157 | Automatické pozastavení tisku po detekci chyby (volitelné, PrusaLink) | all | core-dev + ui-dev | P3 | T-154, T-155 |
| T-158 | Timelapse – zápis z klíčových snímků bez překódování (Core + Camera.FFmpeg) (hotovo; ruční přehrání a Windows v T-125 / T-127) | all | core-dev | P3 | T-149, T-144, T-148 |
| T-159 | Detekce změny vrstvy z osy Z (PrusaLink `axis_z`) a spouštěč timelapse po vrstvě (hotovo; Windows a ruční ověření v T-125 / T-127) | all | core-dev + ui-dev | P3 | T-149, T-148, T-160, T-161 |
| T-160 | Timelapse – služba v Core (spouštěč po čase, automaticky i ručně, složka, rotace) (hotovo) | all | core-dev | P3 | T-158, T-145, T-148 |
| T-161 | Timelapse – UI (nastavení, tray menu, okno kamery, štítek Experimentální, notifikace) a E2E (hotovo; Windows a ruční ověření v T-125 / T-127) | all | ui-designer + ui-dev | P3 | T-160, T-146, T-148 |
| T-163 | Detekce chyb: tmavý filament na tmavém plátu (rozpracováno; režim „Tmavý filament“ hotový, otázka 6 – přijetí přesnosti – blokuje uzavření) | all | core-dev | P3 | T-155 |
| T-164 | Timelapse z hotové nahrávky (varianta F) (hotovo; Windows a ruční ověření v T-125 / T-127) | all | core-dev + ui-dev | P3 | T-158, T-161 |
| T-165 | Timelapse – export sekvence snímků JPEG (hotovo; Windows a ruční ověření v T-125 / T-127) | all | core-dev + ui-dev | P3 | T-160, T-161 |
| T-166 | Detekce chyb tisku – ladění citlivosti „Změna na podložce“ na více semínkách (hotovo) | all | core-dev | P2 | T-155, T-154 |
| T-167 | Detekce chyb tisku – detektor „Tisk do vzduchu“ (Z roste, díl neroste; fáze 1 měření, otázky 1 a 2 blokují fázi 2) | all | core-dev | P3 | T-155, T-166, T-154 |

### Windows (`windows`)

| ID | Název | Platforma | Owner | Priorita | Závisí na |
|---|---|---|---|---|---|
| T-010 | Systémové notifikace – Windows | windows | ui-dev | P1 | T-009 |
| T-056 | Velopack – ověření použitelnosti pro instalátor a aktualizace (spike) (hotovo) | windows | ui-dev | P2 | – |
| T-057 | FFmpeg pro win-arm64 (kamera na Windows ARM64) (zamítnuto 2026-11-01) | windows | core-dev | P2 | – |
| T-063 | win-arm64 – ověření sestavení a balení včetně Velopack (MSVC ARM64) (zamítnuto 2026-11-01) | windows | qa-reviewer | P2 | T-055, T-056 |
| T-017 | Balení a distribuce, autostart – Windows (hotovo; ověření v T-124 / T-125) | windows | ui-dev | P2 | T-008, T-055, T-056 |
| T-052 | Bezpečnost – omezené hledání nativních knihoven a absolutní cesta k `open` (L3, L4) | windows | core-dev | P3 | T-014 |
| T-054 | Ověření, že pole hesla nevydá heslo přes UI Automation (I4) | windows | qa-reviewer | P3 | T-044 |
| T-044 | End-to-end testy skutečné aplikace na Windows (UI Automation) (hotovo) | windows | qa-reviewer | P2 | T-040, T-041, T-042, T-043 |
| T-059 | Publikace do winget | windows | qa-reviewer | P3 | T-017, T-055 |
| T-061 | Připnutí tray ikony do viditelné části hlavního panelu (workaround přes `IsPromoted`) (hotovo) | windows | ui-dev | P2 | T-007 |
| T-062 | Stabilizace E2E testu skutečného tray menu (TrayMenuTests) | windows | qa-reviewer | P3 | T-044 |
| T-065 | E2E – infrastruktura zásahů do systému (varování, návrat i po pádu) a přehled pokrytí ručních kontrol | windows | qa-reviewer | P2 | T-044, T-064 |
| T-066 | E2E – zásahy do zobrazení: měřítko monitoru a dočasné vypnutí monitoru | windows | qa-reviewer | P2 | T-065 |
| T-067 | Testovací režim – volitelné toasty, Správce pověření, záznam adres, zpoždění startu, složka dialogu (jen Debug) | windows | ui-dev | P2 | T-044 |
| T-068 | E2E – toasty: banner, klik otevře web přes proxy, centrum oznámení, jazyk, ikona | windows | qa-reviewer | P2 | T-065, T-067 |
| T-069 | E2E – tray menu (klávesnice, tooltip, Přidat tiskárnu, Diagnostika při startu, Předčítání) a obrázky tiskáren | windows | qa-reviewer | P2 | T-065, T-067 |
| T-070 | E2E – ikony aplikace a tray na světlém/tmavém panelu při 100 % a 150 % | windows | qa-reviewer | P2 | T-065, T-066 |
| T-071 | E2E – okno kamery na monitorech, vždy navrchu a okna při 125/150 % | windows | qa-reviewer | P2 | T-065, T-066 |
| T-072 | E2E – jump list oken a druhé spuštění přes shell (popředí, blikání) | windows | qa-reviewer | P2 | T-044 |
| T-073 | E2E – celá sada proti Debug i Native AOT (Debug) a test Správce pověření | windows | qa-reviewer | P2 | T-067, T-068, T-069, T-070, T-071, T-072 |
| T-093 | E2E – widgety překryvu, malý náhled, režim rozložení + AOT (hotovo) | windows | qa-reviewer | P2 | T-080, T-087, T-088, T-090, T-091, T-092 |
| T-114 | Ověření na Windows (sběrný úkol: build a testy po macOS úkolech, Windows snímky, ruční kontroly) (hotovo 2026-11-01; zbytek v T-124 / T-125) | windows | qa-reviewer | P1 | T-017, T-055, T-058, T-101, T-103, T-105, T-106, T-107, T-109–T-112, T-117–T-119 |
| T-124 | Windows – ověření instalátoru (instalace, upozornění, autostart, odinstalace) (hotovo 2026-11-01; zbytek v T-125) (sběrný úkol, z T-114) | windows | qa-reviewer | P1 | T-017, T-114 |
| T-125 | Windows – zbývající ruční ověření (interaktivní windows-verify, tiskárna, vydání) (sběrný úkol, z T-114) | windows | qa-reviewer | P1 | T-017, T-055, T-058, T-101, T-105, T-107, T-114, T-117, T-118 |
| T-130 | Zrušit podporu Windows ARM64 (win-arm64) (hotovo) | windows | qa-reviewer | P2 | – |
| T-162 | FFmpeg pro Windows – připnutý build BtbN už neexistuje (404) (hotovo; ověření v T-125) | windows | core-dev | P1 | – |

### macOS (`macos`) – jen na Macu, později

| ID | Název | Platforma | Owner | Priorita | Závisí na |
|---|---|---|---|---|---|
| T-095 | macOS – zelený build a testy (přípravný úkol, první na Macu) (hotovo; Windows v T-114) | macos | qa-reviewer + ui-dev | P1 | – |
| T-018 | macOS – ikona a menu v menu baru (hotovo; ověření v T-114 / T-115, obrázky tiskáren v menu v T-116) | macos | ui-dev | P1 | T-007, T-008, T-095 |
| T-019 | macOS – .app bundle, distribuce a autostart (hotovo; ověření v T-115 / T-126 / T-127) | macos | ui-dev | P1 | T-008, T-015, T-055, T-056, T-095 |
| T-020 | Systémové notifikace – macOS (hotovo; ruční ověření v T-115 / T-126) | macos | ui-dev | P1 | T-010, T-019, T-095 |
| T-021 | macOS – úložiště tajných údajů v Keychainu (hotovo; ověření v T-114 / T-115, dialog Klíčenky po aktualizaci v T-127) | macos | core-dev | P1 | T-002, T-030, T-095 |
| T-026 | macOS – FFmpeg pro živý stream kamery (hotovo; ověření v T-114 / T-115, Buddy3D v T-127) | macos | ui-dev | P3 | T-014, T-019, T-095 |
| T-038 | macOS – ověření umístění okna kamery, monitoru a „vždy navrchu“ (hotovo; malý náhled na všech plochách; ověření v T-115 / T-127, otázky 2–3 v T-127) | macos | ui-dev | P3 | T-026, T-035, T-037 |
| T-053 | macOS – zabezpečení socketu jediné instance (L2) a ověření `/usr/bin/open` (L4) (hotovo; ověření v T-114 / T-115 / T-127) | macos | ui-dev | P3 | T-043, T-052, T-095 |
| T-096 | macOS – start aplikace bez aktivního displeje (hotovo; ruční ověření v T-115 / T-127) | macos | ui-dev | P2 | T-019 |
| T-097 | macOS – oprávnění „Místní síť“ (NSLocalNetworkUsageDescription) (hotovo; ověření v T-114 / T-127) | macos | ui-dev | P1 | T-019, T-026 |
| T-098 | macOS – ikona aplikace s okrajem podle Applu (hotovo; ruční ověření v T-115 / T-127) | macos | ui-designer | P3 | T-019, T-025 |
| T-099 | macOS – sledování tiskáren a notifikace během čekání na displej (hotovo; ruční ověření v T-115 / T-127) | macos | ui-dev | P2 | T-096, T-020 |
| T-100 | macOS – klávesové zkratky podle zvyklostí Applu (hotovo; ověření v T-114 / T-115, ⌘. opraveno v T-141, fyzická klávesnice a otázky 1–2 v T-127) | macos | ui-dev | P2 | T-018, T-038 |
| T-102 | macOS – ikona v Docku a ⌘Tab při otevřeném okně, „Zobrazit kameru“ v menu (od 2026-10-31 „Přenést kameru do popředí“; hotovo; ověření v T-114 / T-127) | macos | ui-dev | P1 | T-018, T-019, T-038 |
| T-108 | macOS – hardened runtime v ad-hoc podpisu LayerPeek.app (bezpečnostní revize, N3) (hotovo) | macos | ui-dev | P2 | T-019, T-026 |
| T-060 | macOS – podpis Developer ID, notarizace a Homebrew tap – **odloženo do rozhodnutí o Apple Developer (99 USD/rok)** | macos | ui-dev | P3 | T-019 |
| T-115 | Ruční ověření na Macu (sběrný úkol: snímky, build a testy, `tools/macos-verify.ps1`, T-117 / T-118 / T-122; instalátor v T-126, zbytek v T-127) (hotovo 2026-11-02; zbytek v T-127) | macos | qa-reviewer | P1 | T-020, T-021, T-026, T-053, T-096, T-098, T-099, T-105, T-116–T-118, T-122 |
| T-116 | macOS – obrázky tiskáren v menu v menu baru (hotovo; ruční ověření v T-115 / T-127) | macos | ui-dev | P3 | T-018, T-039, T-042 |
| T-126 | macOS – ověření instalátoru (Gatekeeper, upozornění, Otevřít při přihlášení, odinstalace) (sběrný úkol, z T-115) | macos | qa-reviewer | P1 | T-019, T-020, T-115 |
| T-127 | macOS – zbývající ruční ověření (Dock, monitory, skutečná tiskárna, vydání) (sběrný úkol, z T-115) | macos | qa-reviewer | P1 | T-019, T-020, T-021, T-026, T-038, T-053, T-055, T-058, T-097, T-101, T-102, T-106, T-107, T-115 |
| T-134 | Úklid testů Klíčenky na macOS 27 smaže jen jednu položku služby (hotovo) | macos | qa-reviewer | P2 | T-021 |
| T-135 | Kontrola `mac.menubar` na macOS 27 nenajde stavovou položku (hotovo) | macos | qa-reviewer | P2 | T-105 |
| T-136 | Automatické kontroly okna kamery na Macu místo ručních bodů T-115 (T-122, T-117, T-118) (hotovo) | macos | qa-reviewer | P1 | T-115, T-117, T-118, T-122, T-135 |
| T-137 | macOS – zapnutí „vždy navrchu“ deaktivuje aplikaci a okno ztratí fokus (hotovo) | macos | ui-dev | P1 | T-102, T-117, T-122 |
| T-138 | macOS – ⌃-klik v okně kamery neotevře kontextové menu (hotovo) | macos | ui-dev | P2 | T-117 |
| T-139 | macOS – ikona LayerPeeku v Docku i bez ikony v bundlu (hotovo) | macos | ui-dev | P3 | T-102, T-098 |
| T-140 | macOS – kontroly s oprávněním bez uživatele (`-Allow`, sudoers, zámek obrazovky) (hotovo) | macos | qa-reviewer | P2 | T-105, T-136 |
| T-141 | macOS – ⌘. (Zrušit) se k oknům aplikace nedostane (hotovo) | macos | ui-dev | P2 | T-100 |
| T-142 | E2E test aktualizace na Macu selže se zapnutým displejem (hotovo; dřív T-133, přečíslováno 2026-11-02) | macos | qa-reviewer | P2 | T-058, T-099 |
| T-147 | macOS – FFmpeg s muxerem pro nahrávání kamery (hotovo; QuickTime v T-127) | macos | ui-dev | P2 | T-144, T-026 |
