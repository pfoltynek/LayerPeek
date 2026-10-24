---
id: T-051
title: Bezpečnost – proxy webu PrusaLink odmítá cizí zápisové požadavky (L1)
status: done
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
- [x] Zápisové požadavky s cizím `Sec-Fetch-Site` nebo `Origin` končí `403` a na tiskárnu nedorazí; `GET`/`HEAD` a požadavky bez těchto hlaviček beze změny (jednotkové testy)
- [x] E2E testy T-030 (Playwright + Edge) procházejí; doplněný E2E test zápisové akce z webu PrusaLink přes proxy (např. nahrání souboru na falešnou tiskárnu) a test cizí stránky na jiném portu `127.0.0.1`, jejíž `fetch`/formulář `POST` proxy odmítne
- [x] Ruční ověření uživatelem se skutečnou tiskárnou: web PrusaLink přes proxy umí nahrát soubor a ovládat tisk
- [x] `docs/api/prusalink.md` doplněno
- [x] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish win-x64 bez varování

## Rozhodnuto
- 2026-10-21 (uživatel, po review): zápisový požadavek se `Sec-Fetch-Site: same-origin` a zároveň `Origin: null` se povolí (úzká varianta). `Origin: null` se `Sec-Fetch-Site: none` nebo bez `Sec-Fetch-Site` se dál odmítá (přísná varianta pro prohlížeče bez Fetch Metadata), stejně jako `same-origin` s cizím `Origin`.

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- Kontrola původu je v `PrusaLinkWebProxy.ForeignWriteReason` a běží v `HandleAsync` hned po kontrole `Host` a tokenu. Neplatný token proto dál vrací prázdnou `404` a chybný `Host` `421`.
- Metody `GET`, `HEAD` a `OPTIONS` se nekontrolují. U ostatních vede k odmítnutí:
  - `Sec-Fetch-Site`, která není přesně `same-origin` nebo `none`. Porovnává se přesně, takže i prázdná hodnota, velká písmena nebo seznam hodnot znamenají odmítnutí.
  - `Origin`, který není přesně `http://127.0.0.1:<port>`. Patří sem prázdná hodnota, `https`, `localhost`, lomítko na konci a `null`, pokud `Sec-Fetch-Site` není `same-origin` (viz Rozhodnuto).
  - Opakovaná hlavička.
- Odmítnutí vrací `403` s JSON chybou ve formátu firmwaru (`ProxyErrors.ForeignPage`) a tělo požadavku se nečte. Do logu jde Warning jen s metodou a názvem hlavičky, bez tokenu, cesty a hodnoty hlavičky. Odmítnutý požadavek se nepočítá jako aktivita, takže neodkládá zastavení proxy po nečinnosti.
- `Cross-Origin-Resource-Policy: same-origin` je zapnutá u všech odpovědí proxy, stejnojmennou hlavičku tiskárny proxy zahodí. E2E testy T-030 s ní procházejí. V Edge navíc zablokuje i neprůhlednou odpověď na slepý `no-cors` `POST` z cizí stránky (`ERR_BLOCKED_BY_RESPONSE.NotSameOrigin`).
- Jednotkové testy jsou v `tests/PrusaTrayDiagnostic.PrusaLink.Tests/WebProxy/WebProxyOriginGuardTests.cs` (43 případů). Bez implementace jich 26 selže.
- E2E testy jsou v `tests/PrusaTrayDiagnostic.E2E.Tests/ProxyOriginGuardTests.cs`:
  - Akce webu přes proxy dorazí na tiskárnu: spuštění a smazání souboru, pauza, pokračování, zastavení úlohy a nahrání souboru. Falešná tiskárna (`FakePrinterSite.Commands`) a falešný web (`window.apiCall`) je umí.
  - Cizí stránka na jiném portu `127.0.0.1` neprojde: `no-cors` `fetch` `POST`, formulář `POST` do iframe (`403`), `DELETE` (zastaví ho preflight) ani načtení obsahu v režimu `no-cors` (CORP). Na tiskárnu nedorazí žádný zápis.
  - Test cizí stránky bez implementace selže.
- Ověřeno v Edge: klasický HTML formulář `POST` ze stránky proxy pošle kvůli `Referrer-Policy: no-referrer` hlavičku `Origin: null` se `Sec-Fetch-Site: same-origin`. Podle rozhodnutí z 2026-10-21 (viz níže) projde; E2E test `ClassicFormPostOfTheProxiedPageReachesThePrinter`.
- AOT publish win-x64 proběhl bez varování. Žádný nový NuGet balíček.
- Zbývá ruční ověření se skutečnou tiskárnou (třetí kritérium, nezaškrtnuto): nahrání souboru a ovládání tisku přes web PrusaLink v proxy.
- **Ruční ověření 2026-10-21 (uživatel, Windows 11, Debug z `develop`, skutečná tiskárna):** web PrusaLink otevřený z tray menu přes proxy se načetl bez dotazu na heslo. Nahrání souboru, spuštění, pozastavení, obnovení a zastavení tisku i smazání souboru proběhly bez chyby (žádné `403`). Úkol se uzavírá.
