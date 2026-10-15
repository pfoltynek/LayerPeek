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
- `depends_on` – seznam ID, které musí být v `done/`
- `## Otevřené otázky` – co musí rozhodnout uživatel. Úkol s nezodpovězenou otázkou, která blokuje implementaci, se neimplementuje.

## Jak přidat úkol

Napiš ho ručně podle šablony, nebo požádej Clauda např.: *„Použij agenta architect a rozpadni funkci X na úkoly.“*

## Přehled

| ID | Název | Owner | Priorita | Závisí na |
|---|---|---|---|---|
| T-000 | Založení repozitáře a kostry řešení (hotovo) | architect | P1 | – |
| T-001 | Doménový model a abstrakce | core-dev | P1 | – |
| T-002 | Konfigurace tiskáren a uložení tajných údajů | core-dev | P1 | T-001 |
| T-003 | PrusaLink klient | core-dev | P1 | T-001 |
| T-004 | Prusa Connect – průzkum API (spike) | architect | P1 | – |
| T-005 | Prusa Connect klient | core-dev | P2 | T-001, T-004 |
| T-006 | Monitorovací služba (polling) | core-dev | P1 | T-001 |
| T-007 | Tray ikona se stavem a progressem | ui-dev | P1 | T-006 |
| T-008 | Tray menu | ui-dev | P1 | T-007 |
| T-009 | Detekce událostí tisku | core-dev | P1 | T-006 |
| T-010 | Systémové notifikace (Windows + macOS) | ui-dev | P1 | T-009 |
| T-011 | Teploty a diagnostika | ui-dev | P2 | T-006 |
| T-012 | Historie telemetrie | core-dev | P3 | T-006 |
| T-013 | Více tiskáren | ui-dev | P2 | T-007, T-002 |
| T-014 | Snímek z kamery | core-dev + ui-dev | P3 | T-003, T-005 |
| T-015 | Okno nastavení | ui-dev | P1 | T-002, T-003 |
| T-016 | CI – GitHub Actions (Windows + macOS) | qa-reviewer | P1 | – |
| T-017 | Balení a distribuce, autostart | ui-dev | P2 | T-008 |
