---
id: T-233
title: E2E test `ClassicFormPostOfTheProxiedPageReachesThePrinter` selhává s Edge 157 na macOS
status: done
owner: qa-reviewer
priority: P2
platform: all
depends_on: []
---

## Cíl
Při T-230 (2026-11-15, macOS, Edge 157.0.4341.60) test
`tests/LayerPeek.E2E.Tests/ProxyOriginGuardTests.cs` → `ClassicFormPostOfTheProxiedPageReachesThePrinter` selhává
na řádku `Assert.Single(browser.FailedRequests)`: kolekce je prázdná. Ostatní kontroly testu (hlavičky `Origin: null`,
`Sec-Fetch-Site: same-origin`, stav 204, příkaz tiskárny) prošly. Selhání je stálé (i samostatný běh) a stejné
s Playwright 1.63.0 i 1.64.1, nezpůsobila ho tedy aktualizace balíčku v T-230. Pravděpodobná příčina: Edge 157 už
navigaci formuláře zodpovězenou 204 nehlásí jako `requestfailed` (`net::ERR_ABORTED`).

## Rozsah
- Zjistit, co Edge 157 hlásí místo `net::ERR_ABORTED` (událost `requestfailed` / `requestfinished`, případně nic) a zda
  je to změna prohlížeče (ověřit i na Windows s aktuálním Edge).
- Upravit očekávání testu tak, aby ověřovalo podstatu (požadavek prošel proxy, tiskárna odpověděla 204, nic jiného
  neselhalo) a nezáviselo na tom, jak konkrétní verze Edge hlásí zrušenou navigaci. Test neoslabovat: žádný jiný
  neúspěšný požadavek nesmí projít.

## Akceptační kritéria
- [x] Příčina zjištěná a zapsaná v poznámkách
- [x] `dotnet test --project tests/LayerPeek.E2E.Tests` na Macu bez chyb
- [x] Totéž na Windows (→ T-125, pokud se úkol dělá na Macu) → přesunuto do T-125

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- **Příčina** (macOS, Edge 157.0.4341.60, Playwright 1.64.1): diagnostický záznam událostí stránky ukázal
  `request` (navigace) → `response 204` → `requestfinished`; `requestfailed` nepřijde ani po 3 s a stránka zůstane na
  adrese proxy. Edge 157 tedy navigaci zodpovězenou 204 hlásí jako dokončený požadavek, ne jako zrušený
  (`net::ERR_ABORTED`). Je to změna hlášení v prohlížeči, ne v produktu: strážce původu propustil požadavek správně
  (`Origin: null` + `Sec-Fetch-Site: same-origin` → 204, příkaz došel do simulátoru). Kód proxy se neměnil.
- **Úprava testu** (`tests/LayerPeek.E2E.Tests/ProxyOriginGuardTests.cs`): test čeká, až se formulářový POST vyřídí
  (událost kontextu `RequestFinished` nebo `RequestFailed`, nejdéle 20 s), takže nezávisí na pořadí hlášení. Přijme
  jen dva stavy tohoto jednoho požadavku – bez chyby, nebo `net::ERR_ABORTED`; jakákoli jiná chyba nebo jiný neúspěšný
  požadavek test shodí (`FailedRequests` musí být přesně prázdné, resp. jen ten zrušený POST). Rozhodující kontrola
  zůstává na straně serveru: simulátor tiskárny přijal přesně `POST /api/v1/files/usb/form.gcode`. Nově test ověřuje
  i to, co dosud jen tvrdil komentář: stránka po odpovědi 204 zůstane na adrese proxy. Kontroly hlaviček, stavu 204,
  401/`WWW-Authenticate`, dialogů, chyb skriptů a provozu mimo 127.0.0.1 beze změny.
- Ověřeno na Macu: `ProxyOriginGuardTests` opakovaně 3/3; celé `dotnet build` (Debug i Release) 0 varování, `dotnet test` 10838 testů,
  0 selhání (271 přeskočeno – platformní). Ověření na Windows → T-125, oddíl 2ae.
