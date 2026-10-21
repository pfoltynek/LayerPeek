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
| T-005 | Prusa Connect klient (odloženo – bez oficiálního API) | all | core-dev | P3 | T-001, T-002, T-004 |
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
| T-055 | Skripty pro vydání (build, AOT publish, archivy, kontrolní součty, GitHub Release) | all | qa-reviewer | P2 | – |
| T-016 | CI – GitHub Actions (Windows + macOS) – **odloženo, přeskočit** (náklady u soukromého repozitáře, vydání přes T-055) | all | qa-reviewer | P3 | – |
| T-058 | Kontrola nové verze v aplikaci (GitHub Releases) | all | core-dev + ui-dev | P3 | T-008, T-029, T-055 |
| T-022 | Prusa Connect – přihlášení k účtu (UI, odloženo) | all | ui-dev | P3 | T-005, T-015 |

### Windows (`windows`)

| ID | Název | Platforma | Owner | Priorita | Závisí na |
|---|---|---|---|---|---|
| T-010 | Systémové notifikace – Windows | windows | ui-dev | P1 | T-009 |
| T-056 | Velopack – ověření použitelnosti pro instalátor a aktualizace (spike) | windows | ui-dev | P2 | – |
| T-057 | FFmpeg pro win-arm64 (kamera na Windows ARM64) | windows | core-dev | P2 | – |
| T-017 | Balení a distribuce, autostart – Windows | windows | ui-dev | P2 | T-008, T-055, T-056 |
| T-052 | Bezpečnost – omezené hledání nativních knihoven a absolutní cesta k `open` (L3, L4) | windows | core-dev | P3 | T-014 |
| T-054 | Ověření, že pole hesla nevydá heslo přes UI Automation (I4) | windows | qa-reviewer | P3 | T-044 |
| T-059 | Publikace do winget | windows | qa-reviewer | P3 | T-017, T-055 |

### macOS (`macos`) – jen na Macu, později

| ID | Název | Platforma | Owner | Priorita | Závisí na |
|---|---|---|---|---|---|
| T-018 | macOS – ikona a menu v menu baru | macos | ui-dev | P1 | T-007, T-008 |
| T-019 | macOS – .app bundle, distribuce a autostart | macos | ui-dev | P1 | T-008, T-015, T-055, T-056 |
| T-020 | Systémové notifikace – macOS | macos | ui-dev | P1 | T-010, T-019 |
| T-021 | macOS – úložiště tajných údajů v Keychainu | macos | core-dev | P1 | T-002, T-030 |
| T-026 | macOS – FFmpeg pro živý stream kamery | macos | ui-dev | P3 | T-014, T-019 |
| T-038 | macOS – ověření umístění okna kamery, monitoru a „vždy navrchu“ | macos | ui-dev | P3 | T-026, T-035, T-037 |
| T-053 | macOS – zabezpečení socketu jediné instance (L2) a ověření `/usr/bin/open` (L4) | macos | ui-dev | P3 | T-043, T-052 |
| T-060 | macOS – podpis Developer ID, notarizace a Homebrew tap – **odloženo do rozhodnutí o Apple Developer (99 USD/rok)** | macos | ui-dev | P3 | T-019 |
