---
id: T-030
title: E2E testy webu přes proxy (Playwright + Edge)
status: todo
owner: qa-reviewer
priority: P2
platform: all
depends_on: [T-028]
---

## Cíl
Automaticky ověřit chybu, kterou nezachytily jednotkové testy první verze T-028. Skutečný prohlížeč (lokálně nainstalovaný Microsoft Edge) otevře web přes lokální proxy z T-028 a JavaScript webu (`fetch` a `XMLHttpRequest` relativně k `window.location.pathname`) projde přihlášený, bez dialogu a bez prázdné stránky.

## Rozsah
- Rozhodnuto uživatelem 2026-10-18:
  - E2E testy přes Microsoft.Playwright;
  - lokální Edge přes `Channel = "msedge"`, headless;
  - žádné stahování prohlížečů;
  - falešná tiskárna;
  - jen localhost.
- Nový testovací projekt, např. `tests/PrusaTrayDiagnostic.E2E.Tests` (xUnit v3 na MTP, v `PrusaTrayDiagnostic.slnx`). Oddělený projekt, aby se ovladač Playwright (přes 100 MB) nekopíroval do ostatních testovacích projektů. Umístění viz Q2.
- Falešná tiskárna v testovacím projektu, naslouchá na `127.0.0.1` na náhodném portu:
  - Digest podle firmwaru Buddy (RFC 2069, MD5, bez `qop`, realm `Printer API`, nonce 300 s, `stale=true`, HA2 se skutečnou metodou);
  - `401` s `WWW-Authenticate: Digest ...` pro `/api/*` bez platného přihlášení;
  - stránky a skripty bez přihlášení;
  - minimální `index.html` + `app.js`, které napodobí Prusa-Link-Web: `API_ROOT` z `window.location.pathname` bez koncového `/`, `fetch(`${API_ROOT}/api/v1/info`)`, opakovaný `fetch` stavu, `XMLHttpRequest` `PUT ${API_ROOT}/api/v1/files/usb/<soubor>` s hlavičkami `Overwrite: ?1` a `Print-After-Upload: ?1`, odkaz na externí stránku (kontrola `Referer`);
  - pro testovací účely může používat libovolný server (testovací kód nemusí být AOT). Nesmí ale přidat produkční závislost.
- Proxy z T-028 se v testu spustí přímo přes její API s falešným `ISecretStore`. Prohlížeč dostane adresu `http://127.0.0.1:<port>/<token>/`.
- Pomocná fixture spravuje `IPlaywright` a `IBrowser` (Edge headless) pro celé sestavení a po testech vše zavře, i po pádu testu. Nesmí zůstat běžící proces `msedge` spuštěný testem ani otevřený port.
- Bez Edge (např. na Macu bez Edge nebo na CI bez Edge) se testy **přeskočí** s jasným důvodem (`Assert.Skip`), neselžou. Detekce: výjimka Playwright při `LaunchAsync` s `Channel = "msedge"`, případně předem kontrola cesty k Edge.
- Proměnná `PLAYWRIGHT_BROWSERS_PATH` se v testech nastavovat nemusí. Testy nesmí volat `playwright install` ani nic stahovat.

## Akceptační kritéria
- [ ] Test: stránka se otevře přes proxy, `fetch` na `/api/v1/info` vrátí data a stránka zobrazí výsledek. Žádná odpověď viditelná pro prohlížeč není `401` ani nemá `WWW-Authenticate`. Kontrola přes `page.Response` / `page.RequestFailed`.
- [ ] Test: nahrání souboru přes `XMLHttpRequest` `PUT` (např. 20 MB) projde, falešná tiskárna dostane celé tělo a hlavičky `Overwrite` a `Print-After-Upload`.
- [ ] Test: po vypršení nonce (falešná tiskárna s krátkou platností nebo řízeným časem) web dál funguje bez dialogu.
- [ ] Test: odmítnuté heslo: prohlížeč nezobrazí přihlašovací dialog a stránka se nezacyklí v `setUpAuth`. Chování podle rozhodnutí T-028 Q3.
- [ ] Test: externí odkaz nedostane v `Referer` token (`Referrer-Policy: no-referrer`).
- [ ] Test: adresa se špatným tokenem a požadavek s cizím `Host` (přes `HttpClient`, ne přes prohlížeč) jsou odmítnuté.
- [ ] Bez nainstalovaného Edge se testy přeskočí, neselžou. Ověřit dočasně neplatným kanálem nebo cestou.
- [ ] Testy nic nestahují a nesahají mimo `127.0.0.1`.
- [ ] Testy po sobě uklidí prohlížeč, kontext, porty a dočasné soubory i při pádu.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- **Q1 – Balíček Playwright.** Ověřeno 2026-10-18 (nuget.org, pokusný projekt ve scratchpadu):
  - `Microsoft.Playwright` **1.63.0** (samotná knihovna) funguje s `xunit.v3` 4.0.2 na Microsoft.Testing.Platform. `Channel = "msedge"`, `Headless = true` spustí nainstalovaný Edge. S `PLAYWRIGHT_BROWSERS_PATH` na prázdnou složku se nic nestáhlo. `fetch` relativně k `pathname` na `http://127.0.0.1:<port>/tok/` prošel. Test trval 22 s (první spuštění Edge).
  - `Microsoft.Playwright.Xunit.v3` 1.63.0 (hotové `PageTest`/`ContextTest`) závisí na `Microsoft.Playwright.TestAdapter`, a ten na `Microsoft.TestPlatform.ObjectModel` 17.3.0, tedy na balíček VSTest. To je v rozporu s pravidlem „žádné balíčky VSTest“.
  - Balíček `Microsoft.Playwright` (nupkg 213 MB) přidá do výstupu testů složku `.playwright` s ovladačem Node.js (`win32_x64`), asi 103 MB. Licence MIT. Testovací balíček je z požadavku na AOT vyjmutý a do produkční aplikace se nedostane.
  - Dokumentace: https://playwright.dev/dotnet/docs/browsers (Edge přes `Channel`, „new headless“ chování se může lišit od headless shellu), https://playwright.dev/dotnet/docs/test-runners.
  - **Doporučení architekta:** `Microsoft.Playwright` 1.63.0 bez `Microsoft.Playwright.Xunit.v3`, s vlastní xUnit v3 fixture.
- **Q2 – Spouštění E2E testů.** Mají běžet v rámci běžného `dotnet test` celého řešení (přidá desítky sekund a 100+ MB výstupu), nebo jen na vyžádání, např. samostatný projekt mimo výchozí běh nebo filtr přes trait?
  - **Doporučení architekta:** samostatný projekt v řešení, běží v `dotnet test`, bez Edge se přeskočí. CI (T-016, odloženo) na runnerech Windows Edge má.

## Poznámky k implementaci
(vyplní agent po dokončení)
