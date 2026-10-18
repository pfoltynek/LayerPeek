---
id: T-028
title: Otevření webu PrusaLink s přihlášením (lokální proxy)
status: in-progress
owner: core-dev + ui-dev
priority: P2
platform: all
depends_on: [T-027]
---

## Cíl
Položka „Otevřít PrusaLink v prohlížeči“ v tray menu otevře web tiskárny ve výchozím prohlížeči uživatele rovnou přihlášený, bez dialogu na jméno a heslo. Heslo se nikdy nedostane do prohlížeče, do adresy, do příkazové řádky ani do logu.

## Proč ne údaje v URL (první pokus, commit `90d3bf3`)
- Varianta `http://jmeno:heslo@tiskarna/` byla implementována a v Edge **nefunguje**.
- Úvodní stránka a skripty webu PrusaLink se vracejí bez přihlášení (`200`). Digest výzvu vrací až API.
- Web (Prusa-Link-Web, `src/auth.js`) skládá adresu API z cesty stránky: `API_ROOT = window.location.pathname` bez koncového `/`, volání `fetch(`${API_ROOT}${endPoint}`)`. Nahrávání souborů jde přes `XMLHttpRequest` `PUT ${API_ROOT}${url}` (`src/helpers/upload_request.js`).
- Chromium odmítne `fetch` na stránce, jejíž adresa obsahuje userinfo („Request cannot be constructed from a URL that includes credentials“). Skript spadne a stránka zůstane prázdná.
- Zdroj: https://github.com/prusa3d/Prusa-Link-Web (větev `master`): `src/auth.js` ř. 8–10 a 63–97, `src/helpers/upload_request.js` ř. 36–46, `webpack.config.js` ř. 109 (`publicPath: ""`, prostředky webu mají relativní cesty). Směrování webu je přes `#` (`src/router.js`).

## Rozhodnutí uživatele (2026-10-18)
- **Lokální reverzní proxy** v aplikaci:
  - naslouchá jen na `127.0.0.1`, na náhodném portu;
  - první segment cesty je náhodný, neuhodnutelný token: `http://127.0.0.1:<port>/<token>/`;
  - kontroluje hlavičku `Host` (ochrana proti DNS rebinding);
  - na Digest výzvy tiskárny odpovídá sama z uloženého hesla, heslo do prohlížeče nikdy nejde;
  - adresa se otevře ve výchozím prohlížeči uživatele.
- **Automatické UI testy:** obě sady. E2E testy přes Microsoft.Playwright s lokálním Edge proti falešné tiskárně jsou v úkolu **T-030**. Headless testy Avalonie (okno nastavení, tray menu) jsou v úkolu **T-031**. Tento úkol dodá proxy, napojení na menu a integrační testy proxy.

## Rozsah

### Architektura a vrstvy
- Core: nová abstrakce pro „webový přístup k tiskárně“, např. `IPrinterWebGateway` s metodou `Task<PrinterWebGatewayResult> OpenSessionAsync(PrinterDefinition printer, CancellationToken)`. Výsledek je buď adresa pro prohlížeč, nebo důvod, proč to nejde (chybí heslo, chyba úložiště, heslo odmítnuto, tiskárna nedostupná, nepodporováno). Core nezná HTTP server ani Avalonii.
- PrusaLink: implementace proxy (složka `WebProxy/`, např. `PrusaLinkWebProxy`). Leží v projektu PrusaLink, protože používá `DigestAuthenticator` (interní typ tohoto projektu) a pojmenovaný `HttpClient` přes `IHttpClientFactory`. Směr závislostí zůstává `App -> PrusaLink -> Core`.
- App: `PrinterWebOpener` volá `IPrinterWebGateway` místo sestavování adresy s userinfo. Composition root zaregistruje proxy explicitně (žádné skenování) a při ukončení aplikace ji zastaví.
- Technologie HTTP serveru: **viz Otevřené otázky (Q1)**. Do rozhodnutí se proxy neimplementuje.

### Životní cyklus proxy (podrobnosti viz Q2)
- Proxy se spouští **líně**, až při prvním kliknutí na položku menu, ne při startu aplikace.
- Každá tiskárna má vlastní token. Token mapuje požadavek na jednu konkrétní tiskárnu, nikdy podle vstupu z prohlížeče.
- Token vzniká z `RandomNumberGenerator`, má nejméně 128 bitů a kóduje se jako base64url bez `=`. Platí do zastavení proxy. Nový start proxy znamená nový token i nový port.
- Proxy (nebo token tiskárny) se zneplatní, když se změní adresa, uživatelské jméno nebo heslo tiskárny, když se tiskárna odebere (po `ISettingsApplier`) a při ukončení aplikace.
- Nečinnost: po N minutách bez požadavku se proxy zastaví (hodnota viz Q2). Otevřený web PrusaLink se dotazuje na stav periodicky, takže otevřená karta proxy udrží naživu.
- Zastavení proxy zruší rozběhnuté požadavky (`CancellationToken`) a uvolní port. `Dispose`/`StopAsync` jsou idempotentní a bezpečné při souběhu s příchozím požadavkem.

### Přeposílání požadavků
- Požadavek `/<token>/<zbytek>?<query>` jde na `<adresa tiskárny>/<zbytek>?<query>`. Adresa tiskárny smí obsahovat vlastní cestu (viz `docs/api/prusalink.md`, oddíl Reverzní proxy). Cesta a query se předávají beze změny kódování, protože firmware počítá Digest z `uri` v požadavku.
- `/<token>` bez koncového lomítka přesměruje (`302`) na `/<token>/`, jinak by `API_ROOT` webu nebyl správný.
- Všechny metody (`GET`, `HEAD`, `POST`, `PUT`, `PATCH`, `DELETE`, `OPTIONS`). `CONNECT`, `TRACE` a `Upgrade`/WebSocket se odmítnou (`405` / `501`). Web PrusaLink WebSocket ani SSE nepoužívá.
- **Streamování těl v obou směrech**, bez načítání celého těla do paměti:
  - nahrávání G-code (stovky MB) přes `PUT /api/v1/files/...` s hlavičkami `Overwrite`, `Print-After-Upload`, `Content-Length`;
  - odpovědi přes `HttpCompletionOption.ResponseHeadersRead`, kopírování po blocích;
  - žádný limit velikosti těla (pozor na výchozí limity serveru, např. Kestrel `MaxRequestBodySize` = 30 MB);
  - žádný celkový timeout požadavku (`HttpClient.Timeout = Infinite`), jen timeout připojení k tiskárně a zrušení přes `CancellationToken`.
- Hlavičky **od prohlížeče k tiskárně**:
  - `Host` se nastaví na host tiskárny;
  - `Origin` a `Referer` se odstraní, nebo přepíšou na adresu tiskárny bez tokenu;
  - `Authorization`, `Proxy-Authorization` a `Cookie` se odstraní (cookies na `127.0.0.1` sdílí všechny lokální porty a aplikace);
  - hop-by-hop hlavičky se odstraní podle RFC 9110 §7.6.1: `Connection` a v ní jmenované, `Keep-Alive`, `TE`, `Trailer`, `Transfer-Encoding`, `Upgrade`, `Proxy-Connection`;
  - `Expect: 100-continue` řeší server proxy sám;
  - `X-Api-Key` od prohlížeče se odstraní, proxy používá jen Digest.
- Hlavičky **od tiskárny k prohlížeči**:
  - `WWW-Authenticate` a `Set-Cookie` se nikdy nepředají;
  - hop-by-hop hlavičky se odstraní;
  - `Location` se přepíše: relativní cesta i absolutní adresa tiskárny se převede na `/<token>/...`, cizí adresy zůstanou beze změny;
  - proxy přidá ke všem odpovědím `Referrer-Policy: no-referrer` (web odkazuje na prusa3d.com a token v adrese nesmí uniknout v `Referer`), `X-Content-Type-Options: nosniff` a `Cache-Control: no-store` u odpovědí API.
- Prohlížeč nesmí nikdy dostat odpověď `401`. Web na `401` čte `WWW-Authenticate` (bez ní spadne) a volá `setUpAuth` znovu, takže by se zacyklil. Prohlížeč s `WWW-Authenticate` by navíc ukázal vlastní přihlašovací dialog. Co místo `401` vracet, viz Q3.

### Digest a nonce
- Využít logiku `DigestAuthenticator` (RFC 2069, MD5, bez `qop`, nonce platný 300 s, `stale=true`). Úpravy:
  - HA2 se počítá ze **skutečné metody** požadavku: `MD5("<METODA>:<uri>")`. Dnes je `GET` napevno. Firmware počítá `md5(to_str(method), ":", uri())`, viz `lib/WUI/nhttp/req_parser.cpp` ř. 401 v https://github.com/prusa3d/Prusa-Firmware-Buddy;
  - cache hotových hlaviček podle URI je pro proxy neomezená (cesty k souborům). Proxy ji buď nepoužije, nebo dostane cache s omezenou velikostí. Stávající klient monitoringu se nesmí zpomalit (stávající testy alokací musí projít);
  - instance pro proxy je oddělená od instance klienta monitoringu. Sdílí jen `ISecretStore` a klíč `SecretKeys.ForPrinter(id)`.
- Opakování po `401` a `stale=true`: požadavek bez těla nebo s malým tělem (do např. 64 KB, které se kvůli tomu bufferuje) se zopakuje **jednou** s novým nonce.
- Velké streamované tělo opakovat nejde. Před jeho odesláním proto proxy zajistí čerstvý nonce. Když je nonce starší než např. 240 s nebo chybí, pošle napřed levný `GET /api/version`. Když přesto přijde `401`, vrátí prohlížeči chybu (viz Q3), nikdy `401`.
- Heslo se čte z `ISecretStore` až při první výzvě a při odmítnutí, ne při startu proxy. HA1 se drží v paměti jen po dobu života proxy. Vstupní buffery s heslem se nulují (jako dnes `Md5Hex`).

### Souběh a zátěž
- Firmware zpracuje nejvýš 3 aktivní požadavky a nečinné spojení zavírá po 60 s (`docs/api/prusalink.md`, Spojení a zátěž). Monitoring používá 1 spojení.
- Pojmenovaný `HttpClient` proxy má `MaxConnectionsPerServer = 2` a `PooledConnectionIdleTimeout` pod 60 s. Další požadavky z prohlížeče (Chromium otevře až 6 spojení) čekají ve frontě.
- Bezpečné při souběhu: víc karet, víc tiskáren najednou, souběh s monitoringem a s `ApplyAsync` nastavení.
- Odpojení prohlížeče zruší odpovídající požadavek na tiskárnu.

### Chování při chybách (UX viz Q3)
- Tiskárna Prusa Connect nebo neznámá tiskárna: otevře se holá adresa jako dosud, proxy se nepoužije.
- Chybí heslo nebo selže úložiště tajných údajů: holá adresa (prohlížeč se zeptá sám), jako v první verzi.
- Heslo odmítnuto nebo tiskárna nedostupná: podle rozhodnutí Q3.
- Proxy se nepodaří spustit (port, oprávnění): log s varováním, otevře se holá adresa, aplikace nespadne.

### Bezpečnost
- Naslouchá jen na `IPAddress.Loopback` (`127.0.0.1`), ne na `localhost`, `::1` ani `0.0.0.0`. Adresa pro prohlížeč používá `127.0.0.1`.
- `Host` musí být přesně `127.0.0.1:<port>`, jinak `421` nebo `400` bez dalších informací. Totéž platí, když `Host` chybí nebo je jich víc.
- Token se porovnává v konstantním čase (`CryptographicOperations.FixedTimeEquals` nad bajty). Neplatný nebo chybějící token vrátí obecnou `404` se stejnou odpovědí jako jiná neznámá cesta.
- Do logu se nikdy nedostane token, celá adresa proxy, heslo, HA1, hlavička `Authorization`, `Cookie` ani tělo požadavku. Logovat smí jen metodu, cestu **za** tokenem, stavový kód a host tiskárny. Pozor na texty výjimek: `Process.Start` vkládá do `Win32Exception` celé `FileName`, tedy i adresu s tokenem (zjištěno v první verzi).
- Adresa s tokenem se předá prohlížeči jako jediný argument (Windows ShellExecute, macOS `open` bez shellu). Je vidět v příkazové řádce procesu prohlížeče a v historii prohlížeče. Platí ale jen do zastavení proxy, což se zapíše do `docs/` jako známé omezení.
- Žádné CORS hlavičky. Cizí stránky v prohlížeči neznají port ani token.

### Co z větve (commit `90d3bf3`) zůstává a co se odstraní
- **Odstranit:**
  - `PrinterWebAddress.WithCredentials` a jeho testy;
  - `IUrlLauncher.OpenWithCredentialsAsync` a větev spouštění s údaji v `ProcessUrlLauncher` včetně testů;
  - oddíl „Otevření webu s přihlášením (T-028)“ v `docs/api/prusalink.md`: přepsat na popis proxy a odstranit tvrzení, že Edge/Chrome údaje z URL přijmou.
- **Ponechat a upravit:**
  - `PrinterWebOpener`: rozhodnutí proxy / holá adresa;
  - čtení z úložiště mimo UI vlákno (`Task.Run`);
  - všechny záložní cesty (bez hesla, chyba úložiště, Prusa Connect, neznámá tiskárna);
  - zrušení rozpracovaného otevření při `Dispose` view modelu;
  - logy bez tajných údajů. Místo hesla a userinfo se nově hlídá token.
- **Ponechat beze změny:**
  - `TrayMenuViewModel` předává definici tiskárny jen tehdy, když položka ukazuje aktuální adresu své tiskárny;
  - `IsSafeToLaunch` dál odmítá adresy s userinfo (i prázdným `http://@host/`). Adresa proxy userinfo nemá, takže výjimka pro aplikaci už není potřeba;
  - zabezpečené spouštění v `ProcessUrlLauncher`: logování selhání bez textu výjimky;
  - validace adresy tiskárny v nastavení.

### Dokumentace
- `docs/api/prusalink.md`: oddíl o proxy (mapování cest, hlavičky, nonce, chování webu na `401`, známá omezení: v adresním řádku je `127.0.0.1` místo tiskárny, záložky nefungují po restartu proxy, token v historii prohlížeče).
- `docs/architecture.md`: tok „Otevřít v prohlížeči“ přes `IPrinterWebGateway`.
- `docs/prerequisites.md`: jen pokud zvolená technologie změní požadavky na toolchain.

## Akceptační kritéria
- [ ] Otevřené otázky Q1–Q3 rozhodnuté uživatelem a zapsané v tomto úkolu.
- [ ] Integrační testy proxy (PrusaLink.Tests): příchozí strana je skutečný listener na `127.0.0.1`, tiskárna je falešný `HttpMessageHandler` s Digest podle firmwaru (HA2 se skutečnou metodou). Žádná síť mimo loopback.
- [ ] Testy přeposílání: všechny metody, query, procentově kódovaná cesta, adresa tiskárny s vlastní cestou, přesměrování `/<token>` → `/<token>/`, přepis `Location`, odstranění hop-by-hop hlaviček, `Cookie`/`Set-Cookie`, `Authorization`, `WWW-Authenticate`, přidání `Referrer-Policy: no-referrer`.
- [ ] Test nahrání velkého těla (např. 256 MB generovaný stream): projde, tělo se nebufferuje (falešná tiskárna čte po blocích; alokace a paměť pod pevnou mezí), `Content-Length` a `Overwrite`/`Print-After-Upload` dorazí.
- [ ] Testy nonce: první výzva, opakované použití nonce (jen jeden `401` na sérii), `stale=true` → jedno opakování, předběžné obnovení nonce před velkým tělem, odmítnuté heslo → prohlížeč nikdy nedostane `401` ani `WWW-Authenticate`.
- [ ] Bezpečnostní testy: špatný nebo chybějící token → `404`; token jiné tiskárny nepovolí přístup k této tiskárně; `Host` jiný než `127.0.0.1:<port>` (včetně `localhost:<port>` a cizí domény) → odmítnuto; listener není dostupný na jiné adrese než loopback; token v logu ani v textech výjimek není (zachycení logů jako v první verzi).
- [ ] Testy životního cyklu: líný start, nečinnost → zastavení (s `FakeTimeProvider`), zneplatnění po změně adresy, jména nebo hesla a po odebrání tiskárny, zastavení při ukončení aplikace, souběžné požadavky během zastavení. Žádný visící task ani otevřený port po testu (testy po sobě uklidí i při pádu).
- [ ] Testy zrušení: odpojení klienta zruší požadavek na tiskárnu; `StopAsync` zruší rozběhnuté požadavky.
- [ ] `PrinterWebOpener` testy: PrusaLink s heslem → adresa proxy; bez hesla, chyba úložiště, Prusa Connect, neznámá tiskárna, proxy nejde spustit → holá adresa; chování podle Q3 pro odmítnuté heslo a nedostupnou tiskárnu.
- [ ] Odstraněný kód s údaji v URL (viz výše) včetně testů; žádné volání `OpenWithCredentialsAsync` nezůstalo.
- [ ] Native AOT: `dotnet publish src/PrusaTrayDiagnostic.App -c Release -r win-x64` bez varování trimu/AOT; publikovaná aplikace otevře web přes proxy (ověří `qa-reviewer`). Změnu velikosti binárky zapsat do poznámek.
- [ ] Ověřeno na Windows (ručně, uživatel): v Edge se web PrusaLink skutečné tiskárny otevře přihlášený bez dialogu, funguje nahrání G-code a stav se průběžně obnovuje.
- [ ] Dokumentace aktualizovaná (viz Rozsah).
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky

### Q1 – Technologie HTTP serveru proxy
Ověřeno 2026-10-18 na Windows 11 (build 26300), SDK `11.0.100-rc.2.26504.105`. Měření z pokusných projektů ve scratchpadu: Native AOT `win-x64` Release, `IsAotCompatible`, `TreatWarningsAsErrors`. Základ s `HttpClient` = 4,84 MB.

- **(a) `System.Net.HttpListener`** (součást BCL, žádný balíček):
  - AOT publikace bez varování, +0,17 MB;
  - na Windows staví na http.sys. Ověřeno bez administrátora: prefixy `http://127.0.0.1:<port>/`, `http://localhost:<port>/` i `http://[::1]:<port>/` jdou spustit, `http://+:`, `http://*:` a IP adresa LAN skončí „Access is denied“. URL ACL ani `netsh` tedy pro loopback nejsou potřeba;
  - nevýhody: Microsoft ho „nedoporučuje pro nový vývoj“, dostává jen omezenou údržbu, na macOS běží jiná, spravovaná implementace s užší sadou funkcí (https://learn.microsoft.com/dotnet/api/system.net.httplistener, Remarks). Neumí port `0`, takže je potřeba náhodný port a opakování při kolizi. http.sys přidává hlavičku `Server: Microsoft-HTTPAPI/2.0` a `Host` kontroluje po svém (`400`). Chování se na Windows a macOS liší, testy by musely běžet na obou OS.
- **(b) ASP.NET Core Kestrel** (`<FrameworkReference Include="Microsoft.AspNetCore.App" />`, `WebApplication.CreateEmptyBuilder` + `UseKestrelCore()`, `Listen(IPAddress.Loopback, 0)`):
  - AOT je oficiálně podporované (https://learn.microsoft.com/aspnet/core/fundamentals/native-aot). AOT publikace bez varování, +4,26 MB (binárka 9,10 MB). Port `0` funguje (ověřeno);
  - stejná implementace na Windows i macOS, robustní parser HTTP/1.1, streamování, `RequestAborted`. Bez balíčku na NuGetu, je součástí sdíleného frameworku;
  - nevýhody: větší binárka. Ověřeno, že s framework reference hlásí NuGet `NU1510` (s warnings-as-errors to je chyba) pro explicitní `PackageReference` `Microsoft.Extensions.Hosting` a `Microsoft.Extensions.Http` (dnes 10.0.13). Ty by se musely z projektu odstranit a brát ze sdíleného frameworku 11.0. To se týká projektu s framework reference a pravděpodobně i projektů, které ho referencují. Nutno nastavit `MaxRequestBodySize = null` (výchozí 30 MB).
- **(c) YARP** (`Yarp.ReverseProxy` 2.3.0, vydáno 2025-02-27) nad Kestrelem:
  - od 2.1 má `IsAotCompatible=true` (https://github.com/dotnet/yarp/pull/2144, `src/ReverseProxy/Yarp.ReverseProxy.csproj`). `IHttpForwarder` řeší hop-by-hop hlavičky a streamování a jde mu předat vlastní `HttpMessageInvoker` s Digest handlerem;
  - nevýhody: vše z (b) plus další balíček a závislost `System.IO.Hashing`. Poslední vydání je z února 2025 a balíček cílí nejvýš na `net8.0` (`net6.0`, `net7.0`, `net8.0`). Pro jednu cílovou tiskárnu je to spíš zbytečně velký nástroj. Velikost s AOT neměřena.
- **(d) Vlastní minimální HTTP/1.1 proxy nad `Socket`/`TcpListener`**:
  - bez závislostí, prakticky 0 MB navíc, port `0`, plná kontrola hlaviček;
  - nevýhody: vlastní parser HTTP (chunked, keep-alive, `Expect: 100-continue`, request smuggling, limity hlaviček) je bezpečnostně citlivý a pracný. Nejvyšší riziko chyb.
- **Doporučení architekta:** (b) Kestrel bez YARP. Je to podporovaná a stejná implementace na obou OS s minimem vlastního parsování a výchozím portem `0`. Cena je +4,3 MB a úklid `PackageReference` kvůli `NU1510`. Pokud je velikost binárky priorita, pak (a) `HttpListener`.

### Q2 – Životní cyklus proxy
- Jeden listener (port) pro všechny tiskárny, nebo **samostatný listener na tiskárnu**? Samostatný port znamená samostatný origin, takže `sessionStorage` webu (`authType`, `apiKey`) a případná data webu se mezi tiskárnami nemíchají.
- Kdy zastavit: po nečinnosti (kolik minut?), nebo až při ukončení aplikace?
- Token na celou dobu běhu proxy (opakované kliknutí otevře stejnou adresu), nebo nový token při každém kliknutí (staré karty přestanou fungovat)?
- **Doporučení architekta:** samostatný listener na tiskárnu, líný start, opakované kliknutí použije běžící proxy a stejný token, zastavení po 10 minutách bez požadavku, zneplatnění při změně nastavení tiskárny a při ukončení aplikace.

### Q3 – Chování při odmítnutém hesle a nedostupné tiskárně
- Před otevřením prohlížeče udělat **předběžný dotaz** (přihlášené `GET /api/version` přes Digest proxy)? Podle výsledku pak:
  - (a) při odmítnutém hesle nebo nedostupné tiskárně otevřít holou adresu tiskárny (prohlížeč se zeptá na heslo nebo ukáže svou chybu spojení) a zapsat varování do logu;
  - (b) neotevřít nic a zobrazit systémovou notifikaci nebo položku v menu s chybou;
  - (c) otevřít proxy vždy a chybu ukázat ve webu.
- Co vrátit prohlížeči, když tiskárna heslo odmítne **během** otevřené relace (např. heslo změněné na tiskárně)? `401` nejde (viz výše). Možnosti: `403` s JSON tělem `{"title":..., "message":...}` (web ho zobrazí jako chybu přes `handleError`), nebo `502` s jednoduchou stránkou.
- **Doporučení architekta:** předběžný dotaz a (a). Během relace `403` s JSON tělem ve tvaru chyb firmwaru. Texty chyby v proxy zůstanou anglické a technické, protože lokalizaci řeší jen UI aplikace.

## Poznámky k implementaci
(vyplní agent po dokončení)

### Ověřené verze a poznámky k technologiím (architekt, 2026-10-18)
- Stávající verze v `Directory.Packages.props`: Avalonia 12.1.4, CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions.Hosting/Http 10.0.13, xunit.v3 4.0.2 (MTP v2), SDK `11.0.100-rc.2.26504.105`.
- Prusa-Link-Web: `API_ROOT` z `window.location.pathname` (`src/auth.js`), relativní prostředky (`publicPath: ""`), směrování přes `#`. Prefix `/<token>/` proto web nerozbije.
- Firmware Buddy: HA2 = `MD5(metoda:uri)` se skutečnou metodou (`lib/WUI/nhttp/req_parser.cpp`, `check_digest_auth`).
- Pokusy (AOT velikosti, `HttpListener` bez administrátora, `NU1510`) proběhly ve scratchpadu mimo repozitář. Do repozitáře se nic nepřidalo.
