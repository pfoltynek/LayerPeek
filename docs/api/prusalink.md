# PrusaLink API (firmware Buddy 6.x)

Platí pro tiskárny s firmwarem Buddy 6.x: Core One, MK4/S, MK3.9, XL, MINI. Ověřeno proti zdrojovému kódu firmwaru (verze 6.2–6.10). Specifikace OpenAPI v Prusa-Link-Web je obecná a částečně neaktuální (pokrývá i SL1 a PrusaLink na Raspberry Pi). Pro Buddy je referencí kód firmwaru.

## Zdroje

- Specifikace: https://raw.githubusercontent.com/prusa3d/Prusa-Link-Web/master/spec/openapi.yaml
- Firmware: https://github.com/prusa3d/Prusa-Firmware-Buddy
  - `lib/WUI/nhttp/status_renderer.cpp` – `/api/v1/status`
  - `lib/WUI/link_content/basic_gets.cpp` – `/api/version`, `/api/v1/info`, `/api/v1/job`
  - `lib/WUI/nhttp/req_parser.cpp`, `status_page.cpp` – autentizace, chybové odpovědi
  - `src/common/http/types.h` – platnost nonce
  - `lib/WUI/nhttp/server.h` – limity spojení
  - `src/state/printer_state.cpp` – mapování stavů

## Endpointy používané klientem

| Endpoint | Kdy | Poznámka |
|---|---|---|
| `GET /api/v1/status` | každý polling | stav, teploty, osa Z, rychlost/průtok, ventilátory; objekt `job` (id, progress %, `time_remaining` s, `time_printing` s) jen při tisku/pauze |
| `GET /api/v1/job` | jen při změně `job.id` | název souboru (`file.display_name`); **204 No Content**, když úloha neběží (i ve stavu FINISHED/STOPPED) |
| `GET /api/version` | test spojení | `api`, `hostname`; `firmware` a `printer` až od 6.5 |
| `GET /api/v1/info` | test spojení | `mmu` (bool), `nozzle_diameter`, `hostname`, `serial` |

- Kamery (`/api/v1/cameras`) Buddy nemá a vrací 404.
- **MMU:** jedinou informací o MMU je `info.mmu`. Stav slotů API neposkytuje.

## Stavy (`printer.state`)

| PrusaLink | Doménový stav |
|---|---|
| `IDLE`, `READY` | `Idle` (READY posílá jen Connect) |
| `BUSY` | `Busy` (kalibrace, selftest, předehřev…) |
| `PRINTING` | `Printing` (včetně ukončování/rušení) |
| `PAUSED` | `Paused` |
| `ATTENTION` | `Attention` (varování, výměna filamentu, otázky před tiskem) |
| `FINISHED` | `Finished` (dokud uživatel nepotvrdí na displeji) |
| `STOPPED` | `Stopped` |
| `ERROR` | `Error` (Buddy ho v praxi neposílá – fatální chyba znamená, že web server neběží) |
| `UNKNOWN` a cokoli jiného | `Unknown` |

- Po skončení tisku zmizí objekt `job` ze `/status`. Klient proto u FINISHED/STOPPED vrací poslední známou úlohu (u FINISHED s progressem 100 %).
- `job.id` se mapuje na `PrintJob.Id`. Detekce událostí (T-009) podle něj rozliší dvě po sobě jdoucí úlohy i se stejným názvem souboru.
- `time_remaining` firmware vynechává, když odhad není k dispozici. Hodnota nikdy není `null`.

## Autentizace

**HTTP Digest podle RFC 2069:**
- MD5, **bez `qop`**, realm `Printer API`, heslo z menu tiskárny.
- Uživatelské jméno se nastavuje u každé tiskárny (T-027), výchozí je `maker`. Ukládá se v souboru nastavení (není tajné), heslo v `ISecretStore`.
- Výpočet: `response = MD5(MD5("<uživatel>:Printer API:<heslo>"):nonce:MD5("<METODA>:<uri>"))`. Metoda je skutečná metoda požadavku (`GET`, `PUT`, `DELETE`…), `uri` je přesně cesta a query odeslané v požadavku (`lib/WUI/nhttp/req_parser.cpp`, `check_digest_auth`).
- Firmware čte z hlavičky `Authorization` jen `nonce` a `response`.

**Nonce:**
- Je platný **300 s od vydání** a lze ho opakovaně použít; čítač `nc` se nekontroluje.
- Po vypršení vrátí firmware 401 s novým nonce a `stale=true`.
- Klient proto cachuje nonce i hotovou hlavičku pro každou URI. Jeden 401 navíc tak přijde zhruba jednou za 5 minut.
- Vestavěný Digest v .NET by platil 401 při každém požadavku, protože `PreAuthenticate` umí jen Basic.

**Ostatní:**
- `X-Api-Key` firmware 6.x stále přijímá se stejnou hodnotou jako heslo. Klient ho nepoužívá (rozhodnutí: jen Digest).
- 401 má tělo `{"title": "401: Unauthorized","message":""}`. Chybové odpovědi jsou v JSON jen tehdy, když je `application/json` první v hlavičce `Accept`.

**Reverzní proxy:** adresa smí obsahovat cestu (např. `http://proxy/printers/mk4`). Proxy ale musí cestu předat beze změny, protože firmware počítá Digest z cesty v požadavku. Proxy, která prefix odřízne, se projeví jako „The printer rejected the password.“

## Otevření webu s přihlášením (T-028)

**Stav:** proxy (Core + PrusaLink) i napojení na tray menu v App jsou implementované a uživatel je 2026-10-18 ručně ověřil v Edge se skutečnou tiskárnou (viz `tasks/done/T-028-prusalink-web-login.md`).

**Údaje v adrese (`http://jmeno:heslo@tiskarna/`) nefungují** (ověřeno v Edge 2026-10-18):
- Úvodní stránka a skripty webu se vracejí bez přihlášení (`200`). Digest výzvu vrací až API.
- Web (Prusa-Link-Web, https://github.com/prusa3d/Prusa-Link-Web, `src/auth.js`) skládá adresy API z `window.location.pathname` bez koncového `/` (`API_ROOT`) a volá `fetch(`${API_ROOT}/api/...`)`. Nahrávání souborů jde přes `XMLHttpRequest` `PUT ${API_ROOT}/api/v1/files/...` (`src/helpers/upload_request.js`).
- Chromium odmítne `fetch` na stránce, jejíž adresa obsahuje userinfo. Skript spadne a stránka zůstane prázdná.

**Chování webu důležité pro proxy:**
- Prostředky webu mají relativní cesty (`webpack.config.js`: `publicPath: ""`) a směrování je přes `#`. Web proto funguje i pod prefixem cesty, např. `/<token>/`.
- Na `401` web čte `WWW-Authenticate` a podle prvního slova si do `sessionStorage` uloží `authType`. U `Digest` zavolá ověření znovu (`setUpAuth`). Odpověď `401` bez `WWW-Authenticate` skript shodí a opakovaná `401` ho zacyklí. Proxy proto prohlížeči `401` nikdy nevrací.
- Firmware počítá HA2 ze skutečné metody požadavku: `MD5("<METODA>:<uri>")` (`lib/WUI/nhttp/req_parser.cpp`, `check_digest_auth`). Pro `PUT` a `DELETE` přes proxy tedy nestačí výpočet napevno s `GET`.

### Lokální proxy (`PrusaLink/WebProxy`)

Abstrakce v Core je `IPrinterWebGateway` (`Core/Web`). Implementace `PrusaLinkWebGateway` spouští pro každou tiskárnu vlastní `PrusaLinkWebProxy` (Kestrel ze sdíleného frameworku ASP.NET Core, bez YARP).

**Otevření (`OpenAsync`):**
- Prusa Connect vrátí `NotSupported`, neplatná adresa `InvalidAddress`. V obou případech bez adresy.
- Před otevřením prohlížeče proběhne přihlášený `GET /api/version` (kontrola hesla, limit 5 s).
- Při chybě se vrátí holá adresa tiskárny s důvodem: `PasswordMissing`, `PasswordUnreadable`, `PasswordRejected`, `AccessDenied`, `UnsupportedAuthentication`, `NotReachable`, `UnexpectedResponse`. Když proxy nejde spustit, důvod je `ProxyUnavailable`.
- Při úspěchu se vrátí `http://127.0.0.1:<port>/<token>/`. Opakované otevření použije běžící proxy, stejný port i token.

**Životní cyklus:**
- Proxy se spouští líně, až při prvním otevření. Každá tiskárna má vlastní listener na `127.0.0.1`, port `0` (vybere OS) a vlastní token.
- Token má 256 náhodných bitů (`RandomNumberGenerator`) v base64url bez `=`.
- Zastaví se po 10 minutách bez požadavku. Běžící požadavek (např. dlouhé nahrávání) zastavení odkládá.
- Zastaví se také přes `StopAsync(printerId)` / `StopAllAsync` (po změně nebo odebrání tiskárny, změně hesla) a při `DisposeAsync`/`Dispose` (ukončení aplikace).
- `OpenAsync` se změněnou adresou nebo jménem nahradí starou proxy novou.
- Zastavení zruší rozběhnuté požadavky na tiskárnu a uvolní port.

**Mapování a hlavičky:**
- `/<token>/<zbytek>?<query>` jde na `<adresa tiskárny>/<zbytek>?<query>`. Cesta a query se předávají beze změny, bez kanonikalizace `Uri` (`DangerousDisablePathAndQueryCanonicalization`), protože firmware počítá Digest z přesné cesty.
- Segmenty `.`/`..` (i `%2e`), `\` a `#` v cestě se odmítnou (`404`).
- `/<token>` bez lomítka vrátí `302` na `/<token>/`.
- K tiskárně nejdou hlavičky `Authorization`, `Proxy-Authorization`, `Cookie`, `X-Api-Key`, `Origin`, `Referer`, `Expect`, `Host` ani hop-by-hop hlavičky (včetně těch jmenovaných v `Connection`).
- K prohlížeči nejdou `WWW-Authenticate`, `Proxy-Authenticate`, `Set-Cookie` ani hop-by-hop hlavičky.
- `Location` na tiskárnu se přepíše na `/<token>/...`, cizí adresy a relativní odkazy zůstanou beze změny.
- Proxy přidá `Referrer-Policy: no-referrer` a `X-Content-Type-Options: nosniff`. U `/api/` přidá `Cache-Control: no-store`.

**Odmítnuté požadavky:**
- `Host` jiný než přesně `127.0.0.1:<port>` vrátí `421`. Chybějící nebo zdvojený `Host` odmítne už Kestrel (`400`).
- Chybějící nebo neplatný token vrátí `404` s prázdným tělem, stejně jako jakákoli jiná neznámá cesta.
- `CONNECT`/`TRACE` vrátí `405`, `Upgrade` (WebSocket) `501`.

**Těla:**
- Těla se v obou směrech streamují. Limity Kestrelu `MaxRequestBodySize`, `MinRequestBodyDataRate` a `MinResponseDataRate` jsou vypnuté, `HttpClient` proxy nemá celkový timeout (jen `ConnectTimeout` 5 s).
- Tělo do 64 KB se bufferuje, aby šel požadavek po `401` se `stale=true` jednou zopakovat. Větší nebo chunked tělo se streamuje.
- Před streamovaným tělem proxy obnoví nonce starší než 240 s (nebo chybějící) levným `GET /api/version`.
- Odkaz na tiskárnu má vlastní pojmenovaný `HttpClient` `PrusaLink.WebProxy`: `MaxConnectionsPerServer = 2`, nečinné spojení 50 s. Firmware zvládne 3 požadavky a 1 spojení má monitoring. Další požadavky prohlížeče čekají ve frontě.

**Digest a chyby:**
- Proxy má vlastní instanci `DigestAuthenticator` (bez cache hlaviček podle URI, protože cest k souborům je neomezeně). S monitoringem sdílí jen `ISecretStore`.
- Heslo se čte až při první výzvě a při odmítnutí.
- Prohlížeč nikdy nedostane `401`. Při odmítnutém, chybějícím nebo nečitelném hesle, nepodporované autentizaci nebo propadlém nonce u streamovaného těla dostane `403` s JSON ve formátu chyb firmwaru (`{"title":"403: Forbidden","message":"..."}`), které web zobrazí přes `handleError`.
- Nedostupná tiskárna vrátí `502`, timeout spojení `504`. Obojí je JSON ve stejném formátu.

**Logy:** jen id tiskárny, metoda, cesta za tokenem bez query a stavový kód. Nikdy token, adresa proxy, heslo, HA1 ani hlavičky `Authorization`/`Cookie`. Vlastní logování Kestrelu je vypnuté, protože by obsahovalo URL s tokenem.

**Známá omezení:**
- V adresním řádku je `127.0.0.1:<port>` místo adresy tiskárny.
- Záložky a otevřené karty po zastavení proxy (nečinnost, změna nastavení, restart aplikace) přestanou fungovat. Je nutné znovu otevřít web z menu.
- Adresa s tokenem je v historii prohlížeče a v příkazové řádce procesu prohlížeče. Platí ale jen do zastavení proxy.
- Klient, který se připojí přesně ve chvíli zavírání listeneru, může zůstat bez odpovědi, dokud nevyprší timeout prohlížeče. Spojení zůstane ve frontě OS a do proxy se nedostane.

## Spojení a zátěž

- Keep-alive je podporovaný.
- Server zpracovává nejvýš **3 aktivní požadavky** a nečinné spojení zavírá po **60 s**.
- Při zahlcení nevrací 503, jen zpomalí odpověď.
- Klient proto používá **jedno spojení na tiskárnu**, sekvenční požadavky a nečinný timeout 50 s (pod limitem firmwaru).
- Timeout jednoho požadavku je 3 s.
  - Běžný dotaz je jeden požadavek. Obnova nonce a nová úloha přidají pár dalších.
  - V nejhorším případě (obnova nonce a změna hesla zároveň pro `/status` i `/job`) to je 6 požadavků. Tento vzácný případ utne 10 s timeout monitoru.
- Odpověď větší než 64 KB se zahodí jako neočekávaná. Skutečné odpovědi mají pod 2 KB; limit chrání paměť při špatné adrese.
- Tiskárny za jednou reverzní proxy (stejný host a port) sdílejí jedno spojení a dotazují se postupně. U pomalé proxy a více tiskáren to může vést k falešnému „Offline“.
- Oficiální webové UI dotazuje `/status` jednou za sekundu, takže výchozí interval 5 s je bezpečný.

## Fixtures pro testy

- `tests/PrusaTrayDiagnostic.PrusaLink.Tests/Fixtures/firmware/` – odpovědi sestavené podle kódu firmwaru.
- `tests/PrusaTrayDiagnostic.PrusaLink.Tests/Fixtures/recorded/<tiskárna>/<stav>/` – skutečné odpovědi nahrané skriptem `tools/record-prusalink-fixtures.ps1`. Skript se zeptá na heslo (skrytě) a anonymizuje sériové číslo. Test `RecordedFixturesTests` je automaticky všechny přehraje.
