---
id: T-051
title: Bezpečnost – proxy webu PrusaLink odmítá cizí zápisové požadavky (L1)
status: todo
owner: core-dev
priority: P3
platform: all
depends_on: [T-028, T-030]
---

## Cíl
Zabránit tomu, aby cizí webová stránka v prohlížeči uživatele ovládala tiskárnu přes lokální proxy PrusaLink (T-028). Nález L1 z bezpečnostní revize (2026-10-21, závažnost **nízká**):
- `src/PrusaTrayDiagnostic.PrusaLink/WebProxy/PrusaLinkWebProxy.cs:311-344` (`HandleAsync`) kontroluje `Host` a token v cestě, ale nekontroluje, odkud požadavek přišel;
- token je v cestě (`/<token>/...`). Proxy sice posílá `Referrer-Policy: no-referrer` a hlavičky `Origin` a `Referer` dál netlačí (`ProxyHeaders.cs`), ale obsah, který servíruje tiskárna, může zásadu referreru přebít (`<meta name="referrer">`) nebo token přečíst z `location`, pokud ho tiskárna nebo obsah na ní ovládá útočník;
- stránka, která token zná, může naslepo poslat `POST`/`PUT`/`DELETE` na `http://127.0.0.1:<port>/<token>/api/...` a proxy k němu přidá přihlášení Digest.

## Rozsah
- U požadavků s metodou jinou než `GET`, `HEAD` a `OPTIONS` proxy odpoví `403` (bez přeposlání na tiskárnu), když:
  - hlavička `Sec-Fetch-Site` je přítomná a není `same-origin` ani `none` (jiný port na `127.0.0.1` je pro prohlížeč `same-site`, a proto se také odmítne), **nebo**
  - hlavička `Origin` je přítomná a není přesně `http://127.0.0.1:<port>` této proxy (včetně `Origin: null`).
  - Požadavek bez obou hlaviček, typicky klient mimo prohlížeč, který stejně musí znát token, projde jako dnes.
- Volitelně `Cross-Origin-Resource-Policy: same-origin` u všech odpovědí, aby cizí stránky nemohly vkládat obsah proxy. Zapnout jen tehdy, pokud nerozbije web PrusaLink (ověřit E2E).
- Kontrola se provede až po kontrole `Host` a tokenu. Odpověď na neznámý nebo chybný token zůstává stejná (`404`), aby odmítnutí neprozradilo platnost tokenu. Odmítnutí zalogovat bez tokenu a bez cesty (jen metodu a důvod).
- Vlastní požadavky webu PrusaLink (nahrání souboru, pozastavení a zrušení tisku, mazání souborů) jsou z jeho stránky `same-origin`, musí tedy dál fungovat.
- Dokumentace: `docs/api/prusalink.md`, oddíl „Lokální proxy“, doplnit o kontrolu původu.
- Bez nového NuGet balíčku. Kestrel ze sdíleného frameworku zůstává.

## Akceptační kritéria
- [ ] Zápisové požadavky s cizím `Sec-Fetch-Site` nebo `Origin` končí `403` a na tiskárnu nedorazí; `GET`/`HEAD` a požadavky bez těchto hlaviček beze změny (jednotkové testy)
- [ ] E2E testy T-030 (Playwright + Edge) procházejí; doplněný E2E test zápisové akce z webu PrusaLink přes proxy (např. nahrání souboru na falešnou tiskárnu) a test cizí stránky na jiném portu `127.0.0.1`, jejíž `fetch`/formulář `POST` proxy odmítne
- [ ] Ruční ověření uživatelem se skutečnou tiskárnou: web PrusaLink přes proxy umí nahrát soubor a ovládat tisk
- [ ] `docs/api/prusalink.md` doplněno
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish win-x64 bez varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
