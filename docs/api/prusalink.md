# PrusaLink API (firmware Buddy 6.x)

Platí pro tiskárny s firmwarem Buddy 6.x: Core One, MK4/S, MK3.9, XL, MINI. Ověřeno proti zdrojovému kódu firmwaru (verze 6.2–6.10). Specifikace OpenAPI v Prusa-Link-Web je obecná a částečně neaktuální (pokrývá i SL1 a PrusaLink na Raspberry Pi). Pro Buddy je referencí kód firmwaru.

## Zdroje

- Specifikace: https://raw.githubusercontent.com/prusa3d/Prusa-Link-Web/master/spec/openapi.yaml
- Firmware: https://github.com/prusa3d/Prusa-Firmware-Buddy
  - `lib/WUI/nhttp/status_renderer.cpp` – `/api/v1/status`
  - `lib/WUI/link_content/basic_gets.cpp` – `/api/version`, `/api/v1/info`, `/api/v1/job`
  - `lib/WUI/nhttp/req_parser.cpp`, `status_page.cpp` – autentizace, chybové odpovědi
  - `src/common/http/types.h` – platnost nonce
  - `lib/WUI/nhttp/server.h` – limity spojení
  - `src/state/printer_state.cpp` – mapování stavů

## Endpointy používané klientem

| Endpoint | Kdy | Poznámka |
|---|---|---|
| `GET /api/v1/status` | každý polling | stav, teploty, osa Z, rychlost/průtok, ventilátory; objekt `job` (id, progress %, `time_remaining` s, `time_printing` s) jen při tisku/pauze |
| `GET /api/v1/job` | jen při změně `job.id` | název souboru (`file.display_name`); **204 No Content**, když úloha neběží (i ve stavu FINISHED/STOPPED) |
| `GET /api/version` | test spojení | `api`, `hostname`; `firmware` a `printer` až od 6.5 |
| `GET /api/v1/info` | test spojení | `mmu` (bool), `nozzle_diameter`, `hostname`, `serial` |

- Kamery (`/api/v1/cameras`) Buddy nemá a vrací 404.
- **MMU:** jedinou informací o MMU je `info.mmu`. Stav slotů API neposkytuje.

## Stavy (`printer.state`)

| PrusaLink | Doménový stav |
|---|---|
| `IDLE`, `READY` | `Idle` (READY posílá jen Connect) |
| `BUSY` | `Busy` (kalibrace, selftest, předehřev…) |
| `PRINTING` | `Printing` (včetně ukončování/rušení) |
| `PAUSED` | `Paused` |
| `ATTENTION` | `Attention` (varování, výměna filamentu, otázky před tiskem) |
| `FINISHED` | `Finished` (dokud uživatel nepotvrdí na displeji) |
| `STOPPED` | `Stopped` |
| `ERROR` | `Error` (Buddy ho v praxi neposílá – fatální chyba znamená, že web server neběží) |
| `UNKNOWN` a cokoli jiného | `Unknown` |

- Po skončení tisku zmizí objekt `job` ze `/status`. Klient proto u FINISHED/STOPPED vrací poslední známou úlohu (u FINISHED s progressem 100 %).
- `job.id` se mapuje na `PrintJob.Id`. Detekce událostí (T-009) podle něj rozliší dvě po sobě jdoucí úlohy i se stejným názvem souboru.
- `time_remaining` firmware vynechává, když odhad není k dispozici. Hodnota nikdy není `null`.

## Autentizace

**HTTP Digest podle RFC 2069:**
- MD5, **bez `qop`**, uživatel `maker`, realm `Printer API`, heslo z menu tiskárny.
- Výpočet: `response = MD5(MD5("maker:Printer API:<heslo>"):nonce:MD5("GET:<uri>"))`.
- Firmware čte z hlavičky `Authorization` jen `nonce` a `response`.

**Nonce:**
- Je platný **300 s od vydání** a lze ho opakovaně použít; čítač `nc` se nekontroluje.
- Po vypršení vrátí firmware 401 s novým nonce a `stale=true`.
- Klient proto cachuje nonce i hotovou hlavičku pro každou URI. Jeden 401 navíc tak přijde zhruba jednou za 5 minut.
- Vestavěný Digest v .NET by platil 401 při každém požadavku, protože `PreAuthenticate` umí jen Basic.

**Ostatní:**
- `X-Api-Key` firmware 6.x stále přijímá se stejnou hodnotou jako heslo. Klient ho nepoužívá (rozhodnutí: jen Digest).
- 401 má tělo `{"title": "401: Unauthorized","message":""}`. Chybové odpovědi jsou v JSON jen tehdy, když je `application/json` první v hlavičce `Accept`.

**Reverzní proxy:** adresa smí obsahovat cestu (např. `http://proxy/printers/mk4`). Proxy ale musí cestu předat beze změny, protože firmware počítá Digest z cesty v požadavku. Proxy, která prefix odřízne, se projeví jako „The printer rejected the password.“

## Spojení a zátěž

- Keep-alive je podporovaný.
- Server zpracovává nejvýš **3 aktivní požadavky** a nečinné spojení zavírá po **60 s**.
- Při zahlcení nevrací 503, jen zpomalí odpověď.
- Klient proto používá **jedno spojení na tiskárnu**, sekvenční požadavky a nečinný timeout 50 s (pod limitem firmwaru).
- Timeout jednoho požadavku je 3 s.
  - Běžný dotaz je jeden požadavek. Obnova nonce a nová úloha přidají pár dalších.
  - V nejhorším případě (obnova nonce a změna hesla zároveň pro `/status` i `/job`) to je 6 požadavků. Tento vzácný případ utne 10 s timeout monitoru.
- Odpověď větší než 64 KB se zahodí jako neočekávaná. Skutečné odpovědi mají pod 2 KB; limit chrání paměť při špatné adrese.
- Tiskárny za jednou reverzní proxy (stejný host a port) sdílejí jedno spojení a dotazují se postupně. U pomalé proxy a více tiskáren to může vést k falešnému „Offline“.
- Oficiální webové UI dotazuje `/status` jednou za sekundu, takže výchozí interval 5 s je bezpečný.

## Fixtures pro testy

- `tests/PrusaTrayDiagnostic.PrusaLink.Tests/Fixtures/firmware/` – odpovědi sestavené podle kódu firmwaru.
- `tests/PrusaTrayDiagnostic.PrusaLink.Tests/Fixtures/recorded/<tiskárna>/<stav>/` – skutečné odpovědi nahrané skriptem `tools/record-prusalink-fixtures.ps1`. Skript se zeptá na heslo (skrytě) a anonymizuje sériové číslo. Test `RecordedFixturesTests` je automaticky všechny přehraje.
