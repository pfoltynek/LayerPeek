---
id: T-233
title: E2E test `ClassicFormPostOfTheProxiedPageReachesThePrinter` selhává s Edge 157 na macOS
status: todo
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
- [ ] Příčina zjištěná a zapsaná v poznámkách
- [ ] `dotnet test --project tests/LayerPeek.E2E.Tests` na Macu bez chyb
- [ ] Totéž na Windows (→ T-125, pokud se úkol dělá na Macu)

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
