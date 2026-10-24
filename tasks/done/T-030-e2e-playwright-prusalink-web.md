---
id: T-030
title: E2E testy webu přes proxy (Playwright + Edge)
status: done
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
- [x] Test: stránka se otevře přes proxy, `fetch` na `/api/v1/info` vrátí data a stránka zobrazí výsledek. Žádná odpověď viditelná pro prohlížeč není `401` ani nemá `WWW-Authenticate`. Kontrola přes `page.Response` / `page.RequestFailed`.
- [x] Test: nahrání souboru přes `XMLHttpRequest` `PUT` (např. 20 MB) projde, falešná tiskárna dostane celé tělo a hlavičky `Overwrite` a `Print-After-Upload`.
- [x] Test: po vypršení nonce (falešná tiskárna s krátkou platností nebo řízeným časem) web dál funguje bez dialogu.
- [x] Test: odmítnuté heslo: prohlížeč nezobrazí přihlašovací dialog a stránka se nezacyklí v `setUpAuth`. Chování podle rozhodnutí T-028 Q3.
- [x] Test: externí odkaz nedostane v `Referer` token (`Referrer-Policy: no-referrer`).
- [x] Test: adresa se špatným tokenem a požadavek s cizím `Host` (přes `HttpClient`, ne přes prohlížeč) jsou odmítnuté.
- [x] Bez nainstalovaného Edge se testy přeskočí, neselžou. Ověřit dočasně neplatným kanálem nebo cestou.
- [x] Testy nic nestahují a nesahají mimo `127.0.0.1`.
- [x] Testy po sobě uklidí prohlížeč, kontext, porty a dočasné soubory i při pádu.
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- **Q1 – Balíček Playwright.** Ověřeno 2026-10-18 (nuget.org, pokusný projekt ve scratchpadu):
  - `Microsoft.Playwright` **1.63.0** (samotná knihovna) funguje s `xunit.v3` 4.0.2 na Microsoft.Testing.Platform. `Channel = "msedge"`, `Headless = true` spustí nainstalovaný Edge. S `PLAYWRIGHT_BROWSERS_PATH` na prázdnou složku se nic nestáhlo. `fetch` relativně k `pathname` na `http://127.0.0.1:<port>/tok/` prošel. Test trval 22 s (první spuštění Edge).
  - `Microsoft.Playwright.Xunit.v3` 1.63.0 (hotové `PageTest`/`ContextTest`) závisí na `Microsoft.Playwright.TestAdapter`, a ten na `Microsoft.TestPlatform.ObjectModel` 17.3.0, tedy na balíček VSTest. To je v rozporu s pravidlem „žádné balíčky VSTest“.
  - Balíček `Microsoft.Playwright` (nupkg 213 MB) přidá do výstupu testů složku `.playwright` s ovladačem Node.js (`win32_x64`), asi 103 MB. Licence MIT. Testovací balíček je z požadavku na AOT vyjmutý a do produkční aplikace se nedostane.
  - Dokumentace: https://playwright.dev/dotnet/docs/browsers (Edge přes `Channel`, „new headless“ chování se může lišit od headless shellu), https://playwright.dev/dotnet/docs/test-runners.
  - **Doporučení architekta:** `Microsoft.Playwright` 1.63.0 bez `Microsoft.Playwright.Xunit.v3`, s vlastní xUnit v3 fixture.
  - **Rozhodnuto 2026-10-18:** samotný **`Microsoft.Playwright` 1.63.0** s vlastní xUnit v3 fixture, bez `Microsoft.Playwright.Xunit.v3`.
- **Q2 – Spouštění E2E testů.** Mají běžet v rámci běžného `dotnet test` celého řešení (přidá desítky sekund a 100+ MB výstupu), nebo jen na vyžádání, např. samostatný projekt mimo výchozí běh nebo filtr přes trait?
  - **Doporučení architekta:** samostatný projekt v řešení, běží v `dotnet test`, bez Edge se přeskočí. CI (T-016, odloženo) na runnerech Windows Edge má.
  - **Rozhodnuto 2026-10-18:** **samostatný testovací projekt** v řešení, běží v běžném `dotnet test`. Když Edge chybí, testy se samy přeskočí (`Assert.Skip`).
- **Q3 – Popis rozložení testů v `CLAUDE.md` (pro hlavního agenta / uživatele, neblokuje).** `CLAUDE.md` uvádí „one xUnit v3 project per src library“. Projekt `PrusaTrayDiagnostic.E2E.Tests` žádné knihovně v `src/` neodpovídá (rozhodnutí Q2). Má se `CLAUDE.md` (oddíl Repository layout) doplnit? Agent `CLAUDE.md` neměnil. **Rozhodnuto 2026-10-18:** ano, `CLAUDE.md` doplněno o průřezové testovací projekty (E2E, App.UiTests) a `tests/Shared`.
- **Q4 – Ověření na macOS (neblokuje).** Na macOS se E2E testy zatím nespouštěly (Edge v `/Applications`, `TMPDIR`, `ps`). Má se ověření přidat k některému úkolu `macos` (např. T-026), nebo stačí, že se bez Edge přeskočí? **Rozhodnuto 2026-10-18:** přidáno jako kritérium do T-021 (macOS Keychain, bez kterého proxy na macOS nemá heslo), včetně zrychlení `ps`.

## Poznámky k implementaci

### qa-reviewer (2026-10-18)
Stav: hotovo a ověřené na Windows 11 (build 26300) s Edge 155.0.4283. Review proběhlo, viz `## Review`.

**Projekt `tests/PrusaTrayDiagnostic.E2E.Tests`:**
- xUnit v3 na Microsoft.Testing.Platform, je v `PrusaTrayDiagnostic.slnx` a běží v běžném `dotnet test` (rozhodnutí Q2).
- Balíček `Microsoft.Playwright` **1.63.0** (MIT), verze jen v `Directory.Packages.props`.
  - Závislosti balíčku jsou jen `Microsoft.Bcl.AsyncInterfaces`, `System.ComponentModel.Annotations` a `System.Text.Json`. Žádný balíček VSTest, s MTP funguje.
  - `Microsoft.Playwright.Xunit.v3` se nepoužívá (rozhodnutí Q1), fixture je vlastní.
  - Testovací balíček je vyjmutý z požadavků na AOT. `src/` ho nereferencuje, do aplikace se nedostane.
- Ovladač Playwright (`.playwright`, 103 MB, jen `win32_x64`) se kopíruje jen do výstupu tohoto projektu. Ostatní testovací projekty ho nemají.
- Falešná tiskárna sdílí kód s `PrusaLink.Tests`: `FakePrusaLinkServer.cs` a fixtures z `PrusaLink.Tests/Fixtures/firmware` jsou do projektu připojené odkazem (`Compile`/`None Link`). Kopie, která by se mohla rozejít, neexistuje. Stejně je připojený `tests/Shared/TestRunLock.cs`.

**Infrastruktura (`Infrastructure/`):**
- `EdgeFixture` (assembly fixture):
  - spouští jeden headless Edge pro celé sestavení (`Channel = "msedge"`). Každý test dostane vlastní izolovaný `BrowserContext` (`BrowserSession`);
  - bez Edge zapíše důvod a každý test prohlížeče zavolá `Assert.Skip`. Rozpoznává chyby ovladače „Chromium distribution '…' is not found / is not supported on …“ a „executable doesn't exist“. Jiná chyba spuštění test shodí;
  - nic nestahuje, `playwright install` se nikde nevolá.
- Vše, co ovladač a prohlížeč zapíší (profily, artefakty, dočasné soubory), leží pod `%TEMP%\PrusaTrayDiagnostic.E2E.Tests`. Ovladač Playwright se spouští s `TMP`/`TEMP`/`TMPDIR` nastavenými do této složky, proměnné se hned po spuštění ovladače vrátí. Edge tuto cestu dědí i v `--user-data-dir`.
- Úklid:
  - před prvním testem fixture smaže pozůstatky spuštění, které spadlo, a ukončí procesy, které po něm běží;
  - po posledním testu prohlížeč zavře, počká nejvýš 15 s na ukončení procesů s vyhrazenou cestou v příkazové řádce a složku smaže. Proces, který přežije, zabije a běh označí jako chybu;
  - souběžné běhy sestavení serializuje `TestRunLock`.
- Vlastní procesy (`OwnProcesses`) se poznají podle příkazové řádky s vyhrazenou cestou (Windows: `NtQueryInformationProcess`, macOS: `ps`). Edge uživatele se tak nikdy nezasáhne.
- `EdgeLauncher` přidává `--host-resolver-rules=MAP * ~NOTFOUND , EXCLUDE 127.0.0.1` a `--no-proxy-server`. Prohlížeč tak nedosáhne mimo `127.0.0.1`, ani vlastním provozem na pozadí. `BrowserSession` navíc zaznamená každý požadavek mimo `127.0.0.1` a testy kontrolují, že žádný nebyl.
- `FakePrinterSite`:
  - Kestrel na `127.0.0.1`, port 0, před sdíleným `FakePrusaLinkServer` (Digest jako firmware Buddy);
  - stránky a skripty bez přihlášení, `/api/*` s Digestem;
  - kontroluje každý bajt nahraného souboru a zaznamená hlavičky.
- `PrinterRig`: skutečná `PrusaLinkWebGateway` přes `AddPrusaLink()` (skutečný pojmenovaný `HttpClient`) s `InMemorySecretStore`, volitelně s `FakeTimeProvider`.
- `FakeWeb/index.html` + `app.js` napodobují Prusa-Link-Web:
  - `API_ROOT` z `window.location.pathname`;
  - `setUpAuth` se znovu volá po `401` s `Digest`, s horní mezí 5, aby šlo zacyklení změřit;
  - chyba `{"title","message"}` se zobrazí jako v `handleError`;
  - `XMLHttpRequest` `PUT` s `Overwrite: ?1` a `Print-After-Upload: ?1`.
  
  Stav se obnovuje jen voláním `refreshStatus()` z testu, ne časovačem, aby testy byly deterministické.

**Testy (15):**
- `ProxiedWebTests`:
  - stránka přes proxy je přihlášená a zobrazí data z `/api/v1/info` a `/api/v1/status`. Prohlížeč nedostal `401` ani `WWW-Authenticate`, nebyl žádný dialog, chyba skriptu ani selhaný požadavek;
  - opakované otevření vrátí stejnou adresu, druhá karta je přihlášená a obě dál obnovují stav;
  - nahrání 20 MB přes stránku dorazí celé, včetně `Content-Length`, `Overwrite` a `Print-After-Upload`;
  - vypršený nonce: malý požadavek se po `stale=true` zopakuje. Před streamovaným nahráním (nonce starší než 240 s, `FakeTimeProvider`) se nonce obnoví;
  - heslo změněné na tiskárně během relace: stránka dostane `403` s chybou ve formátu firmwaru, žádný `401` ani zacyklení (`setUpAuthCalls == 1`).
- `ProxyBoundaryTests`:
  - odmítnuté heslo před otevřením: brána vrátí holou adresu (`PasswordRejected`, Q3 a). Na holé adrese dostane API výzvu tiskárny a Edge si požadavek podrží pro vlastní přihlašovací dialog (headless ho nezobrazí). Skript proto nedostane `401` a nezacyklí se;
  - špatný nebo chybějící token z prohlížeče (`fetch` i navigace) → `404`, k tiskárně nic nedojde;
  - přes `HttpClient`: špatný token `404`, cizí `Host` (`attacker.example`, `localhost`) `421`;
  - externí odkaz nedostane žádný `Referer` a `document.referrer` je prázdný. Bez `no-referrer` by Chromium poslal aspoň origin proxy.
- `CredentialsInAddressTests`: regrese T-028. Stránka otevřená jako `http://maker:heslo@127.0.0.1:<port>/` se načte, ale `fetch` skončí chybou s „credentials“ a API se nikdo nezeptá. Spolehlivé (prošlo ve všech bězích), proto se nepřeskakuje.
- `BrowserLifecycleTests`:
  - rozpoznání chybějícího prohlížeče: neexistující `ExecutablePath` a text chyby ovladače;
  - sdílený Edge běží pod vyhrazenou cestou, takže kontrola úniku procesů ve fixture není prázdná;
  - samostatný Edge s vlastním profilem po zavření nenechá proces ani profil;
  - po uvolnění `PrinterRig` neposlouchá port tiskárny ani proxy.

**Ověřeno 2026-10-18:**
- `dotnet build` celého řešení: 0 varování, 0 chyb.
- `dotnet test` celého řešení 3× po sobě: 2046 testů, 1 přeskočený (stávající v `PrusaLink.Tests`), běh asi 8–10 s.
- Projekt E2E samostatně 8× po sobě bez chyby (k tomu 3× v rámci celého řešení). Sada trvá asi 4–5 s (15 testů včetně spuštění Edge, při prvním studeném spuštění Edge déle).
- Bez Edge: spuštění s `LOCALAPPDATA`, `PROGRAMFILES`, `PROGRAMFILES(X86)` a `HOMEDRIVE` nasměrovanými do prázdné složky (Playwright tam hledá `msedge.exe`) dalo 12 přeskočených testů s jasným důvodem a 3 testy bez prohlížeče prošly. Exit code 0.
- Simulovaný pád: testovací proces zabitý za běhu Edge. Procesy Edge skončily s ovladačem, složka zůstala a další běh ji smazal. Po každém běhu nezůstala složka ani proces s vyhrazenou cestou.
- macOS: neověřeno. Kód s tím počítá (`TMPDIR`, `ps`, Playwright hledá Edge v `/Applications`).

**Dokumentace:** `docs/prerequisites.md` – volitelný Microsoft Edge pro E2E testy.

## Review

### qa-reviewer (2026-10-18, druhý pohled)
Výsledek: **schváleno po drobných opravách**, blokující nálezy nezůstaly.

Opraveno v rámci review:
- `OwnProcesses`: vlastní proces se nově pozná jen podle přepínače `--user-data-dir=` (s uvozovkami i bez), jehož hodnota začíná vyhrazenou cestou. Dřív stačila jakákoli zmínka cesty v příkazové řádce. Uživatelův Edge, který otevře soubor z této složky, nebo shell, který ji maže, by se tak mohl ukončit i s celým stromem procesů. Nový test `OnlyTheBrowserProfileSwitchMarksAnOwnProcess`. Ověřeno na skutečných procesech běhu: všechny procesy Edge (renderer, gpu, utility, crashpad, identity_helper) přepínač nesou.
- `EdgeFixture.DisposeAsync`:
  - bez získaného zámku `TestRunLock` (vypršel, protože běží jiný běh, nebo druhé volání) už nic neukončuje ani nemaže. Dřív by mohl zabít Edge a smazat složku souběžného běhu;
  - když selže zavření prohlížeče, ovladač se přesto ukončí a úklid proběhne. Chyba se nahlásí až potom.
- `RejectedPasswordOpensThePlainAddressWithoutTheProxy`: přidána 1 s rezervy po odpovědi `401` tiskárny a kontrola, že prohlížeč `401` nedostal. Bez ní by stav „pending“ prošel i tehdy, kdyby `401` jen ještě nedorazila ke skriptu. Ověřeno sondou: ani po 3 s skript `401` nedostal.
- `DisposedRigReleasesThePrinterAndProxyPorts` má `Timeout` jako ostatní testy s tokenem.

Ověřeno:
- `dotnet build`: 0 varování, 0 chyb.
- Projekt E2E 5× po sobě: 16/16 prošlo, asi 4,7 s na běh.
- Celé řešení 2×: 2047 testů, 1 přeskočený (stávající), 0 chyb.
- Bez Edge (přesměrované `LOCALAPPDATA`/`PROGRAMFILES*`/`HOMEDRIVE`): 12 přeskočeno s jasným důvodem, 4 prošly, exit code 0, nic se nestáhlo.
- Po bězích nezůstala složka `%TEMP%\PrusaTrayDiagnostic.E2E.Tests` ani proces s jejím profilem.
- `dotnet list package --include-transitive`: žádný balíček VSTest.

Nezávažné, neopraveno (k úvaze):
- macOS: `OwnProcesses.UnixCommandLine` spouští `ps` pro každý proces zvlášť (stovky spuštění, pomalé). Lepší by bylo jedno `ps -axww -o pid=,command=`. Ověří se s Q4.
- `MissingBrowserIsASkipReasonNotADownload` ověřuje jen chybějící `ExecutablePath`, ne skutečný chybějící kanál. Ten pokrývá text chyby v `ChannelNotInstalledIsRecognizedAsMissingBrowser` a ruční ověření výše.

### qa-reviewer (2026-10-18, kontrola vlastních oprav)
Výsledek: **schváleno**, nálezy nezůstaly. Úkol přesunut do `done/`.

Doplněno:
- `OwnProcesses.IsOwnCommandLine`:
  - `--user-data-dir=` platí jen jako samostatný argument (začátek, mezera nebo `"` před ním), ne jako konec jiného přepínače (`--x--user-data-dir=`);
  - před cestou smí být nejvýš jedna uvozovka;
  - velikost písmen se ignoruje jen na Windows (jako Chromium a souborový systém), jinde se porovnává přesně;
  - test rozšířen o tyto případy a o sourozenecké složky (`…E2E.Tests2`, samotná `Root` bez oddělovače).
- `EdgeFixture`:
  - `DisposeAsync` zachytí jakoukoli chybu zavření prohlížeče, ukončení ovladače, čekání na procesy i úklidu. Vše proběhne a chyby se nahlásí najednou až na konci, zámek se uvolní vždy;
  - když selže úklid při chybě spuštění, původní chyba se neztratí (`AggregateException`);
  - bez zámku `DisposeAsync` nic nemaže: v tom případě běh nic nevytvořil. Případné pozůstatky po selhaném úklidu (s držením zámku) smaže úvodní úklid dalšího běhu.
- `RejectedPasswordOpensThePlainAddressWithoutTheProxy`: před 1 s rezervy se čeká na kontrolní požadavek ze stránky odeslaný po `401`. Ten musí projít celou cestou zpět do skriptu, takže při zatížení se čekání prodlouží. To, že se něco nestane, jde ukázat jen s mezí. Krátká mez může jen skrýt regresi, dobrý běh nikdy neshodí.

Ověřeno: `dotnet build` 0 varování, 0 chyb. Projekt E2E 5× po sobě 16/16. Celé řešení 2047 testů, 1 přeskočený (stávající). Bez Edge 12 přeskočeno, 4 prošly, exit code 0. Po bězích nezůstala složka ani proces.
