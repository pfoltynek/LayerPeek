# PrusaLink API – poznámky

> Pracovní poznámky. Před implementací (T-003) ověřit proti oficiální OpenAPI specifikaci.

## Zdroje

- Prusa-Link-Web (OpenAPI spec v repozitáři): https://github.com/prusa3d/Prusa-Link-Web
- Prusa-Link (MK3S+ / Raspberry Pi): https://github.com/prusa3d/Prusa-Link
- Firmware Buddy (MK4, XL, MINI, Core One): https://github.com/prusa3d/Prusa-Firmware-Buddy

## Předpokládané endpointy (ověřit)

| Endpoint | Účel |
|---|---|
| `GET /api/version` | verze API, firmware |
| `GET /api/v1/info` | informace o tiskárně |
| `GET /api/v1/status` | stav tiskárny, teploty, aktuální úloha |
| `GET /api/v1/job` | detail aktuální úlohy (soubor, progress, časy) |
| `GET /api/v1/cameras` | seznam kamer (pokud podporováno) |

## Autentizace (ověřit)

- Novější firmware: HTTP Digest, uživatel `maker`, heslo z displeje tiskárny.
- Starší firmware / PrusaLink: hlavička `X-Api-Key`.

## Otevřené body

- Rozdíly mezi firmwary a modely – doplní T-003.
