---
id: T-003
title: PrusaLink klient
status: todo
owner: core-dev
priority: P1
platform: all
depends_on: [T-001]
---

## Cíl
Implementace `IPrinterClient` nad lokálním REST API PrusaLink (viz `docs/api/prusalink.md`).

## Rozsah
- `GET /api/version`, `GET /api/v1/info`, `GET /api/v1/status`, `GET /api/v1/job`
- Autentizace: HTTP Digest (uživatel `maker` + heslo) a/nebo hlavička `X-Api-Key` (starší firmware) – ověřit dle OpenAPI
- Mapování DTO -> doménový model, chybové stavy (offline, 401, timeout) jako `PrinterState`
- Registrace přes `IHttpClientFactory`
- DTO deserializace jen přes source-generated `JsonSerializerContext` (Native AOT)

## Akceptační kritéria
- [ ] Testy s JSON fixtures (tisk, pauza, idle, chyba, 401, timeout)
- [ ] Žádná reálná síť v testech
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Které modely tiskáren a verze firmwaru je nutné podporovat (MK4/S, MK3.9, XL, MINI, Core One, MK3S+ s Raspberry Pi)?
- Máš k dispozici reálnou tiskárnu pro nahrání fixtures?

## Poznámky k implementaci
