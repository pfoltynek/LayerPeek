---
id: T-107
title: Bezpečnost – proxy webu PrusaLink bez Service Workeru a s povolenými hlavičkami (N1)
status: in-progress
owner: core-dev
priority: P2
platform: all
depends_on: [T-028, T-051]
---

## Cíl
Nález N1 z bezpečnostní revize (2026-10-29), oprava odsouhlasená uživatelem. Lokální proxy (T-028) servíruje HTML a JS tiskárny pod `http://127.0.0.1:<port>`. Ten obsah pochází z firmwaru, nebo od kohokoli v LAN, kdo může měnit nešifrovaný provoz HTTP k tiskárně. Skripty v tomto původu mohou:
- číst a nastavovat cookies bez `HttpOnly` ostatních služeb na `127.0.0.1` (cookies nejsou oddělené podle portu);
- posílat „same-site“ požadavky na jiné porty localhostu, takže ochrana CSRF přes `SameSite` u nich nefunguje;
- zaregistrovat Service Worker, který přežije proxy. Proxy totiž předávala `Service-Worker-Allowed` a další hlavičky odpovědi, protože `ProxyHeaders.cs` měl jen seznam zakázaných hlaviček.

## Rozsah
- Odmítnout požadavky se `Service-Worker: script` a nepředávat `Service-Worker-Allowed`.
- Hlavičky odpovědi tiskárny předávat podle seznamu povolených (jen to, co web PrusaLink potřebuje, s odůvodněním).
- Zachovat vlastní hlavičky proxy (`Referrer-Policy`, `X-Content-Type-Options`, CORP). Přidat zpevňující hlavičky (COOP, `X-Frame-Options`, CSP `frame-ancestors`), pokud nerozbijí web PrusaLink.
- Posoudit náhodnou subdoménu `<náhodné>.localhost` pro oddělení cookies a původu od ostatních služeb na `127.0.0.1`. Zavést ji, jen když ji jasně podporují všechny hlavní prohlížeče na Windows i macOS a nepotřebuje žádnou změnu systému.
- Testy na loopbacku bez skutečné sítě. Dokumentace (`docs/api/prusalink.md`, `docs/architecture.md`), CHANGELOG `[0.3.1]` (Security).

## Akceptační kritéria
- [x] Požadavek se `Service-Worker` (jakákoli hodnota) nebo `Sec-Fetch-Dest: serviceworker` končí `403` a na tiskárnu nedorazí. Neplatný token dál vrací `404` (jednotkové testy).
- [x] Hlavičky odpovědi tiskárny jdou k prohlížeči jen ze seznamu povolených. Neznámé a nebezpečné hlavičky se zahodí, potřebné zůstanou (jednotkové testy).
- [x] Každá odpověď proxy má COOP, `X-Frame-Options: DENY` a CSP `frame-ancestors 'none'; worker-src 'none'`. Tiskárna je nemůže přepsat (jednotkové testy).
- [x] E2E (Edge): obsah tiskárny nezaregistruje Service Worker. Dosavadní E2E testy webu přes proxy procházejí i se zpevňujícími hlavičkami.
- [x] Posouzení `*.localhost` je zdokumentované, rozhodnutí je v otevřených otázkách.
- [x] Dokumentace a CHANGELOG doplněny.
- [x] `dotnet build` (Debug i Release) a `dotnet test` bez chyb a varování.
- [ ] Ruční ověření uživatelem se skutečnou tiskárnou: web PrusaLink přes proxy se načte, zobrazí stav, umí nahrát a stáhnout soubor (Windows: Edge/Chrome; macOS: Safari).

## Otevřené otázky
- **Rozhodnuto 2026-10-29: zůstává `127.0.0.1`** (varianta a), dokud aplikace podporuje macOS 14 a 15; znovu zvážit po zvednutí minima na macOS 26.
- **Náhodná subdoména `<náhodné>.localhost`:** zatím nezavedena. Plně by oddělila cookies i „site“ proxy od ostatních služeb na `127.0.0.1`: podle Public Suffix List je `localhost` veřejná přípona, takže `abc.localhost` je samostatný site.
  - Chrome a Edge (Chromium) překládají `localhost` i `*.localhost` samy na loopback. Firefox to dělá od verze 84 (Mozilla bug 1220810).
  - Safari na macOS se spoléhá na systémový resolver a `*.localhost` spolehlivě funguje až od macOS 26 Tahoe (WebKit bug 160504, komentáře 19–20 z 2025-09-19, „implemented in OS frameworks below WebKit“). Na macOS 14 a 15 se překlad může dostat do DNS sítě. LayerPeek ale podporuje macOS 14+ (`LSMinimumSystemVersion` 14.0) a Safari je tam výchozí prohlížeč.
  - Na Windows rozhoduje jen prohlížeč. Chromium a Firefox systémový resolver pro `*.localhost` nepoužívají, takže na jeho chování nezáleží. Chování resolveru Windows samotného agent neověřoval.
  - Kestrel by mohl dál poslouchat na `127.0.0.1`. Změnila by se jen očekávaná hlavička `Host` a adresa pro prohlížeč, a pokud se vybere varianta b), i výběr podle prohlížeče a OS.
  - **Varianty k rozhodnutí:**
    - a) nechat `127.0.0.1` (současný stav s T-107);
    - b) `*.localhost` jen tam, kde výchozí prohlížeč spolehlivě překládá na loopback (Windows všude; macOS 26+). Zjistit výchozí prohlížeč na macOS je samostatná práce a výsledek není spolehlivý;
    - c) `*.localhost` všude s opakovaným pokusem (fallback) na `127.0.0.1`. Na macOS < 26 by ale požadavek mohl odejít do DNS sítě a fallback se z prohlížeče detekovat nedá.
  - Doporučení agenta: a), dokud LayerPeek podporuje macOS < 26. Rozhodne uživatel.

## Poznámky k implementaci
- **Service Worker:** `PrusaLinkWebProxy.IsServiceWorkerScript` běží v `HandleAsync` po kontrole `Host`, tokenu a původu (T-051).
  - Odmítne hlavičku `Service-Worker` s jakoukoli hodnotou a `Sec-Fetch-Dest: serviceworker` (bez ohledu na velikost písmen).
  - Odpověď je `403` s JSON chybou `ProxyErrors.ServiceWorker`, na tiskárnu nic nejde.
  - Do logu jde Warning jen s id tiskárny, bez cesty a tokenu. Odmítnutí se nepočítá jako aktivita.
  - Druhá vrstva je CSP `worker-src 'none'`. V Edge registraci odmítne už prohlížeč (E2E test), takže se skript ani nestáhne.
- **Seznam povolených hlaviček odpovědi** (`ProxyHeaders.ResponseAllowed`): `Content-Type`, `Content-Encoding`, `Content-Disposition`, `Cache-Control`, `ETag`, `Last-Modified`, `Expires`, `Read-Only`, `Currently-Printing`.
  - Odůvodnění každé hlavičky je v `docs/api/prusalink.md`, oddíl „Lokální proxy“. Zdroje: firmware Buddy `lib/WUI/nhttp/headers.cpp`, `send_file.cpp`, `status_page.cpp`, `file_info.cpp`, `lib/WUI/link_content/static_file.cpp`, `usb_files.cpp`; Prusa-Link-Web `src/auth.js`, `src/printer/components/cameras.js` (commit `a67f306`, větev `master`).
  - Web čte z odpovědí jen `WWW-Authenticate`, `etag`, `cache-control`, `expires` a `last-modified`. Service Worker, Workery ani rámy nepoužívá. `WWW-Authenticate` prohlížeč nedostane (proxy nikdy nevrací `401`, viz T-028).
  - Hop-by-hop hlavičky a hlavičky jmenované v `Connection` se zahodí i ze seznamu povolených. `Content-Length` a `Location` nastavuje proxy sama. `Date` přidává Kestrel.
- **Zpevňující hlavičky** přidává `HandleAsync` ke každé odpovědi, i k `404`/`421`/`403` proxy: `Cross-Origin-Opener-Policy: same-origin`, `X-Frame-Options: DENY`, `Content-Security-Policy: frame-ancestors 'none'; worker-src 'none'`.
  - Původ `127.0.0.1` je pro prohlížeč „potentially trustworthy“, takže COOP platí.
  - Úplnou CSP (`script-src` apod.) proxy nepřidává. Web ji nedeklaruje a bez skutečné tiskárny ji nejde ověřit.
  - Všech 22 E2E testů v Edge na macOS prošlo, včetně testů T-030/T-051 s formulářem do iframe cizí stránky (`403` dorazí, rám se jen nezobrazí).
- **Testy:**
  - `tests/LayerPeek.PrusaLink.Tests/WebProxy/WebProxyHardeningTests.cs`: odmítnutí Service Workeru, neplatný token, ostatní hodnoty `Sec-Fetch-Dest`, 14 zahozených hlaviček, zachované hlavičky, hlavička jmenovaná v `Connection`, zpevňující hlavičky na všech typech odpovědí.
  - `tests/LayerPeek.E2E.Tests/ProxyServiceWorkerTests.cs`: podvržený `sw.js` se `Service-Worker-Allowed: /` z `FakePrinterSite` se v Edge nezaregistruje. Obyčejný `fetch` téhož souboru projde bez `Service-Worker-Allowed` a se zpevňujícími hlavičkami.
  - `WebProxyForwardingTests` upraveny: vlastní hlavičky `X-Method` a `X-Kept-Back` se už nepředávají. Metoda se ověřuje přes `ETag` a test očekává, že `X-Kept-Back` je zahozená.
- Žádný nový NuGet balíček, žádná změna AOT (jen konstanty a `HashSet`).
- Zbývá ruční ověření se skutečnou tiskárnou (poslední kritérium) a rozhodnutí o `*.localhost` (otevřená otázka).
