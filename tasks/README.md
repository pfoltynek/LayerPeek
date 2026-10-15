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
- `owner` – agent: `architect`, `core-dev`, `ui-dev`, `qa-reviewer`
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
| T-004 | Prusa Connect – průzkum API (spike) | all | architect | P1 | – |
| T-005 | Prusa Connect klient | all | core-dev | P2 | T-001, T-004 |
| T-006 | Monitorovací služba (polling) | all | core-dev | P1 | T-001 |
| T-007 | Tray ikona se stavem a progressem | all | ui-dev | P1 | T-006 |
| T-008 | Tray menu | all | ui-dev | P1 | T-007 |
| T-009 | Detekce událostí tisku | all | core-dev | P1 | T-006 |
| T-011 | Teploty a diagnostika | all | ui-dev | P2 | T-006 |
| T-012 | Historie telemetrie | all | core-dev | P3 | T-006 |
| T-013 | Více tiskáren | all | ui-dev | P2 | T-007, T-002 |
| T-014 | Snímek z kamery | all | core-dev + ui-dev | P3 | T-003, T-005 |
| T-015 | Okno nastavení (+ registrace `ISecretStore` podle OS) | all | ui-dev | P1 | T-002, T-003 |
| T-023 | Stav MMU3 (průzkum zdroje dat) | all | architect | P3 | T-003 |
| T-016 | CI – GitHub Actions (Windows + macOS) – **odloženo** | all | qa-reviewer | P3 | – |

### Windows (`windows`)

| ID | Název | Platforma | Owner | Priorita | Závisí na |
|---|---|---|---|---|---|
| T-010 | Systémové notifikace – Windows | windows | ui-dev | P1 | T-009 |
| T-017 | Balení a distribuce, autostart – Windows | windows | ui-dev | P2 | T-008 |

### macOS (`macos`) – jen na Macu, později

| ID | Název | Platforma | Owner | Priorita | Závisí na |
|---|---|---|---|---|---|
| T-018 | macOS – ikona a menu v menu baru | macos | ui-dev | P1 | T-007, T-008 |
| T-019 | macOS – .app bundle, distribuce a autostart | macos | ui-dev | P1 | T-008, T-015 |
| T-020 | Systémové notifikace – macOS | macos | ui-dev | P1 | T-010, T-019 |
| T-021 | macOS – úložiště tajných údajů v Keychainu | macos | core-dev | P1 | T-002 |
