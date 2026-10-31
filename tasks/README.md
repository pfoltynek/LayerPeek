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

## Sběrné ověřovací úkoly (rozhodnutí uživatele 2026-10-30)

Kritérium, které už potřebuje jen ruční ověření uživatelem nebo kontrolu na druhém OS, se smí přesunout do sběrného
ověřovacího úkolu: **T-114 „Ověření na Windows“** (Windows) nebo **T-115 „Ruční ověření na Macu“** (Mac).

- V původním úkolu se kritérium odškrtne, původní text zůstane a na konec se připíše „→ přesunuto do T-114“ /
  „→ přesunuto do T-115“ (u obou OS „→ přesunuto do T-114 a T-115“). Do `## Poznámky k implementaci` přibude řádek
  „Uzavřeno RRRR-MM-DD: zbývající ruční ověření přesunuto do T-11x (rozhodnutí uživatele)“ a úkol se uzavře (`status: done`,
  `git mv` do `done/`).
- Ve sběrném úkolu má každý bod zaškrtávací pole, původní úkol a kritérium a krátký konkrétní postup (kontrola
  `tools/macos-verify.ps1` / `tools/windows-verify.ps1` nebo sezení v `docs/macos-manual-testing.md` /
  `docs/windows-manual-testing.md`). U přegenerování snímků UI seznam souborů podle původního úkolu.
- Přesunout nejde nedokončená práce (kód, dokumentace, rozhodnutí) ani kritérium úkolu, který blokuje nezodpovězená
  otevřená otázka – takový úkol zůstává v `in-progress/`. Chyba nalezená při ověření = nový úkol.

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
| T-023 | Stav MMU3 (průzkum zdroje dat) | all | architect | P3 | T-003 |
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
| T-055 | Skripty pro vydání (build, AOT publish, archivy, kontrolní součty, GitHub Release) (hotovo; zkušební vydání v T-114 / T-115) | all | qa-reviewer | P2 | – |
| T-016 | CI – GitHub Actions (Windows + macOS) – **odloženo, přeskočit** (náklady u soukromého repozitáře, vydání přes T-055; rozbor variant 2026-10-29) | all | qa-reviewer | P3 | – |
| T-058 | Kontrola nové verze v aplikaci (GitHub Releases) + Velopack pro macOS (hotovo; ruční ověření v T-114 / T-115) | all | core-dev + ui-dev | P3 | T-008, T-029, T-055 |
| T-022 | Prusa Connect – přihlášení k účtu (UI, vyřazeno – bez veřejného API, 2026-10-29) | all | ui-dev | P3 | T-005, T-015 |
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
| T-101 | Zkušební upozornění a nový dotaz na povolení notifikací (hotovo; ověření v T-114 / T-115) | all | ui-dev + ui-designer | P2 | T-010, T-020 |
| T-103 | Malý náhled – čas dokončení a stav vychladnutí v základním řádku pásu (hotovo; Windows snímky v T-114) | all | ui-designer + ui-dev | P2 | T-079, T-083, T-088, T-089, T-090, T-101 |
| T-104 | Malý náhled – nastavitelný obsah pásu (výběr položek jako u widgetů) – návrh (hotovo, rozhodnuto 2026-10-30, implementace T-110) | all | ui-designer | P3 | T-092, T-103 |
| T-106 | Kompaktní graf teplot – vlastní pás a měřítko pro každou řadu (hotovo; Windows snímky v T-114, tiskárna v T-115) | all | ui-dev | P2 | T-084, T-090 |
| T-105 | Ověřovací skripty pro uživatele (`tools/macos-verify.ps1`, `tools/windows-verify.ps1`) místo ručních kroků (hotovo; první běh v T-114 / T-115) | all | qa-reviewer | P1 | – |
| T-107 | Bezpečnost – proxy webu PrusaLink bez Service Workeru a s povolenými hlavičkami (N1) (hotovo; ruční ověření v T-114 / T-115) | all | core-dev | P2 | T-028, T-051 |
| T-109 | Vychladnutí – prahy trysky i podložky u každé tiskárny (widget Vychladnutí) (hotovo; Windows snímky v T-114) | all | ui-dev + core-dev | P2 | T-079, T-080, T-088, T-092, T-103 |
| T-110 | Malý náhled – nastavitelný obsah pásu (implementace návrhu T-104) (hotovo; Windows snímky v T-114) | all | ui-dev | P3 | T-104, T-103, T-109 |
| T-111 | Okno Nastavení – minimální výška tlačítek 32 DIP (z revize T-101) (hotovo; Windows snímky v T-114) | all | ui-dev | P3 | T-101, T-109, T-110 |
| T-112 | Okno Nastavení – zvýraznění chybného pole a přístupný stav chyby (z revize T-109) (hotovo; Windows snímky v T-114) | all | ui-designer + ui-dev | P3 | T-101, T-109, T-110, T-111 |
| T-113 | Oddělené vydání macOS a Windows pod jednou verzí (hotovo; ověření u 0.3.1 v T-114 / T-115) | all | qa-reviewer | P2 | T-055, T-058 |
| T-117 | Okno kamery – špendlík „Vždy navrchu“ (indikace a přepínání přímo v okně) (hotovo; ověření a macOS snímky v T-114 / T-115) | all | ui-dev | P2 | T-037, T-091, T-102 |
| T-118 | Malý náhled kamery – tenký rámeček okna (hotovo; ověření a macOS snímky v T-114 / T-115) | all | ui-dev | P3 | T-089, T-090, T-117 |

### Windows (`windows`)

| ID | Název | Platforma | Owner | Priorita | Závisí na |
|---|---|---|---|---|---|
| T-010 | Systémové notifikace – Windows | windows | ui-dev | P1 | T-009 |
| T-056 | Velopack – ověření použitelnosti pro instalátor a aktualizace (spike) (hotovo) | windows | ui-dev | P2 | – |
| T-057 | FFmpeg pro win-arm64 (kamera na Windows ARM64) | windows | core-dev | P2 | – |
| T-063 | win-arm64 – ověření sestavení a balení včetně Velopack (MSVC ARM64) | windows | qa-reviewer | P2 | T-055, T-056 |
| T-017 | Balení a distribuce, autostart – Windows (hotovo; ověření v T-114) | windows | ui-dev | P2 | T-008, T-055, T-056 |
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
| T-114 | Ověření na Windows (sběrný úkol: build a testy po macOS úkolech, Windows snímky, instalátor, ruční kontroly) | windows | qa-reviewer | P1 | T-017, T-055, T-058, T-101, T-103, T-105, T-106, T-107, T-109–T-112 |

### macOS (`macos`) – jen na Macu, později

| ID | Název | Platforma | Owner | Priorita | Závisí na |
|---|---|---|---|---|---|
| T-095 | macOS – zelený build a testy (přípravný úkol, první na Macu) (hotovo; Windows v T-114) | macos | qa-reviewer + ui-dev | P1 | – |
| T-018 | macOS – ikona a menu v menu baru (hotovo; ověření v T-114 / T-115, obrázky tiskáren v menu v T-116) | macos | ui-dev | P1 | T-007, T-008, T-095 |
| T-019 | macOS – .app bundle, distribuce a autostart (hotovo; ověření v T-115) | macos | ui-dev | P1 | T-008, T-015, T-055, T-056, T-095 |
| T-020 | Systémové notifikace – macOS (hotovo; ruční ověření v T-115) | macos | ui-dev | P1 | T-010, T-019, T-095 |
| T-021 | macOS – úložiště tajných údajů v Keychainu (hotovo; ověření v T-114 / T-115) | macos | core-dev | P1 | T-002, T-030, T-095 |
| T-026 | macOS – FFmpeg pro živý stream kamery (hotovo; ověření v T-114 / T-115) | macos | ui-dev | P3 | T-014, T-019, T-095 |
| T-038 | macOS – ověření umístění okna kamery, monitoru a „vždy navrchu“ (hotovo; malý náhled na všech plochách; ověření a otázky 2–3 v T-115) | macos | ui-dev | P3 | T-026, T-035, T-037 |
| T-053 | macOS – zabezpečení socketu jediné instance (L2) a ověření `/usr/bin/open` (L4) (hotovo; ověření v T-114 / T-115) | macos | ui-dev | P3 | T-043, T-052, T-095 |
| T-096 | macOS – start aplikace bez aktivního displeje (hotovo; ruční ověření v T-115) | macos | ui-dev | P2 | T-019 |
| T-097 | macOS – oprávnění „Místní síť“ (NSLocalNetworkUsageDescription) (hotovo; ověření v T-114 / T-115) | macos | ui-dev | P1 | T-019, T-026 |
| T-098 | macOS – ikona aplikace s okrajem podle Applu (hotovo; ruční ověření v T-115) | macos | ui-designer | P3 | T-019, T-025 |
| T-099 | macOS – sledování tiskáren a notifikace během čekání na displej (hotovo; ruční ověření v T-115) | macos | ui-dev | P2 | T-096, T-020 |
| T-100 | macOS – klávesové zkratky podle zvyklostí Applu (hotovo; ověření a otázky 1–2 v T-114 / T-115) | macos | ui-dev | P2 | T-018, T-038 |
| T-102 | macOS – ikona v Docku a ⌘Tab při otevřeném okně, „Zobrazit kameru“ v menu (od 2026-10-31 „Přenést kameru do popředí“; hotovo; ověření v T-114 / T-115) | macos | ui-dev | P1 | T-018, T-019, T-038 |
| T-108 | macOS – hardened runtime v ad-hoc podpisu LayerPeek.app (bezpečnostní revize, N3) (hotovo) | macos | ui-dev | P2 | T-019, T-026 |
| T-060 | macOS – podpis Developer ID, notarizace a Homebrew tap – **odloženo do rozhodnutí o Apple Developer (99 USD/rok)** | macos | ui-dev | P3 | T-019 |
| T-115 | Ruční ověření na Macu (sběrný úkol: `tools/macos-verify.ps1`, ruční kroky, skutečná tiskárna, vydání) | macos | qa-reviewer | P1 | T-020, T-021, T-026, T-053, T-055, T-058, T-096–T-099, T-101, T-102, T-105–T-107, T-116 |
| T-116 | macOS – obrázky tiskáren v menu v menu baru (hotovo; ruční ověření v T-115) | macos | ui-dev | P3 | T-018, T-039, T-042 |
