---
id: T-004
title: Prusa Connect – průzkum API (spike)
status: todo
owner: architect
priority: P1
platform: all
depends_on: []
---

## Cíl
Zjistit, jak lze z desktopové aplikace číst stav tiskáren z Prusa Connect, a zapsat výsledek do `docs/api/prusa-connect.md`.

## Rozsah
- Existuje oficiální veřejné API pro uživatele (ne pro tiskárny)? Jak funguje přihlášení (Prusa Account / OAuth / API token)?
- Endpointy pro seznam tiskáren, stav, úlohu, telemetrii, kameru
- Rate limity, podmínky použití

## Akceptační kritéria
- [ ] `docs/api/prusa-connect.md` s odkazy na zdroje
- [ ] Doporučení + seznam rozhodnutí pro uživatele
- [ ] Upravená akceptační kritéria T-005
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Je přijatelné použít neoficiální/nedokumentované API, pokud oficiální neexistuje?

## Poznámky k implementaci
