---
id: T-004
title: Prusa Connect – průzkum API (spike)
status: done
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
- [x] `docs/api/prusa-connect.md` s odkazy na zdroje
- [x] Doporučení + seznam rozhodnutí pro uživatele
- [x] Upravená akceptační kritéria T-005
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Je přijatelné použít neoficiální/nedokumentované API, pokud oficiální neexistuje? → Průzkum potvrdil, že oficiální API pro uživatelské aplikace **neexistuje**. Otázka zůstává na uživateli a je rozepsaná s možnostmi v `docs/api/prusa-connect.md` (sekce „Rozhodnutí pro uživatele“) a jako blokující otázka v T-005 a T-022. Kritéria spiku neblokuje.

## Poznámky k implementaci
- Výsledek je v `docs/api/prusa-connect.md`. Zdroje ověřeny 2026-10-15.
- **Oficiální:** jen Camera API (nahrávání snímků do Connectu, označené jako deprecated ve prospěch Camera Service) a SDK pro tiskárny. API pro čtení stavu z pohledu uživatele Prusa nezveřejnila.
- **Neoficiální:** tři cesty, všechny přes Prusa Account OAuth 2.0 + PKCE a `Authorization: Bearer <JWT>`:
  - webové API `connect.prusa3d.com/app/...` (komunitní reverse engineering);
  - `/slicer/...` (PrusaSlicer, open source);
  - mobilní gateway `connect-mobile-api.prusa3d.com` (veřejná OpenAPI `0.0.1-dev`, chybí způsob získání tokenu).
- Registrovaný OAuth `client_id` pro třetí strany neexistuje. Rate limity nejsou zveřejněné. Podmínky (čl. 5.3) zakazují přetěžování služby.
- Refresh token se zřejmě rotuje (PrusaSlicer řeší `invalid_grant` mezi instancemi). Velikost tokenů jsme bez účtu nezměřili. Nepřímo: PrusaSlicer ukládá všechny tokeny do jednoho záznamu v Credential Manageru, takže se do 2560 B nejspíš vejdou.
- Nic se nevolalo s přihlášením. Anonymně jsme ověřili jen existenci endpointů (`401`) a `version.json`.
- Nový úkol **T-022** (UI přihlášení, `ui-dev`). T-005 přepsán (kritéria, rozsah, otázky). T-014 odkazuje na endpoint snímku.
- Kód se neměnil. `dotnet build` (0 varování) a `dotnet test` (125/125) prošly.
