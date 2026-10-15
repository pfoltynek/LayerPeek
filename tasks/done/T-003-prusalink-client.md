---
id: T-003
title: PrusaLink klient
status: done
owner: core-dev
priority: P1
platform: all
depends_on: [T-001]
---

## Cíl
Implementace `IPrinterClient` nad lokálním REST API PrusaLink (viz `docs/api/prusalink.md`).

## Rozsah
- `GET /api/version`, `GET /api/v1/info`, `GET /api/v1/status`, `GET /api/v1/job`
- Autentizace: HTTP Digest (uživatel `maker` + heslo) – rozhodnutí uživatele: jen Digest, bez `X-Api-Key`
- Mapování DTO -> doménový model, chybové stavy (offline, 401, timeout) jako `PrinterState`
- Registrace přes `IHttpClientFactory`
- DTO deserializace jen přes source-generated `JsonSerializerContext` (Native AOT)

## Akceptační kritéria
- [x] Testy s JSON fixtures (tisk, pauza, idle, chyba, 401, timeout)
- [x] Žádná reálná síť v testech
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- ~~Které modely tiskáren a verze firmwaru je nutné podporovat?~~ → **Rozhodnuto:** firmware Buddy 6.x (Core One, MK4/S, MK3.9, XL, MINI), ověřuje se na uživatelových Core One a MK4S (obě s MMU3). PrusaLink na Raspberry Pi (MK3S+) není podporovaný.
- ~~Máš k dispozici reálnou tiskárnu pro nahrání fixtures?~~ → **Ano** (Core One, MK4S). Uživatel nahraje odpovědi skriptem `tools/record-prusalink-fixtures.ps1` sám, aby heslo neprošlo konverzací. Do té doby testy běží nad fixtures sestavenými podle kódu firmwaru.
- Stav MMU3 → samostatný úkol T-023 (rozhodnutí uživatele: později).

## Poznámky k implementaci
- **Projekt `PrusaTrayDiagnostic.PrusaLink`:**
  - `PrusaLinkClient` (`IPrinterClient` + `GetInfoAsync` pro test spojení), `PrusaLinkClientFactory`, `AddPrusaLink()`.
  - `PrusaLinkAddress` normalizuje adresu (IP, host, URL s portem nebo cestou; odmítá přihlašovací údaje v URL, query a fragment).
- **Výkon:**
  - Dotaz je obvykle jediný požadavek `/api/v1/status`. `/api/v1/job` (název souboru) se čte jen při změně ID úlohy.
  - Vlastní Digest (RFC 2069, MD5 bez qop, jak ho implementuje firmware) cachuje nonce (platnost 300 s) i hotovou hlavičku pro každou URI. Jeden 401 navíc tak přijde zhruba jednou za 5 minut místo při každém požadavku (vestavěný .NET Digest neumí předautentizaci).
  - Na tiskárnu je jedno keep-alive spojení (`MaxConnectionsPerServer = 1`, nečinný timeout 50 s pod limitem firmwaru 60 s), bez proxy a bez logování jednotlivých požadavků.
- **Odolnost:**
  - Všechny očekávané chyby se vrací jako stav, nikdy jako výjimka:
    - nedostupnost, timeout a přerušené spojení → `Offline`;
    - 401/403, chybějící heslo a nepodporovaná autentizace → `Unauthorized`;
    - neočekávaný HTTP kód, nevalidní JSON, odpověď nad 64 KB a nečitelné úložiště hesel → `Unknown`.
  - Výjimkou je jen zrušení volajícím.
  - Chyba `/job` nezpůsobí chybu celého dotazu.
  - Heslo se ze `ISecretStore` čte jen při novém (ne-stale) 401, takže změna hesla v nastavení se projeví bez restartu.
  - Neznámé stavy a pole (novější firmware) se tolerují.
- **Doména:**
  - Po skončení tisku (FINISHED/STOPPED) firmware úlohu nevrací. Klient proto vrací poslední známou úlohu: u FINISHED s progressem 100 %, u STOPPED bez zbývajícího času.
  - `PrinterSnapshot.EstimatedCompletion` se nově počítá jen pro tisk, pauzu a pozornost uprostřed tisku.
- **Timeouty:** požadavek 3 s, spojení 3 s; celý dotaz omezuje 10 s timeout monitoru (T-006).
- **Bezpečnost:** heslo ani HA1 nejsou v logu, zprávách ani hlavičkách. Vstup pro HA1 se po výpočtu nuluje. Výjimka analyzátoru CA5351 (MD5) platí jen pro Digest, který firmware vyžaduje.
- **Balíčky:**
  - `Microsoft.Extensions.Http` (už schválený v `Directory.Packages.props`, kompatibilní s AOT) je nově použitý v projektu PrusaLink.
  - Testovací `Microsoft.Extensions.TimeProvider.Testing` 10.11.0 (jen testy).
- **Testy:**
  - Simulátor serveru ověřuje Digest nezávisle na produkčním kódu (nonce, `stale`, změna hesla, souběh).
  - Fixtures jsou ve `Fixtures/firmware` (podle kódu firmwaru). Nahrané odpovědi ve `Fixtures/recorded` test přehraje automaticky; do té doby se přeskakuje.
- **AOT:** lokální publish win-x64 prošel bez varování. Aplikace PrusaLink zatím neregistruje, takže plné ověření AOT proběhne s napojením v aplikaci (T-015/T-008).
- Code review: 4 kola, poslední bez nálezů.
