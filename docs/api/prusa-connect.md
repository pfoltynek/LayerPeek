# Prusa Connect API – poznámky

> Výsledek průzkumu **T-004**, stav ověřen **2026-10-15** (Connect web `2026.21`, commit `c4a7ab44` podle `https://connect.prusa3d.com/version.json`).
> Ověřeno jen z veřejných zdrojů a anonymními požadavky (bez přihlášení). Nic z toho, co vyžaduje účet, nebylo vyzkoušeno.

## Shrnutí

- **Oficiální, dokumentované veřejné API pro uživatelské aplikace (čtení stavu tiskáren) Prusa Connect nemá.** Prusa oficiálně dokumentuje jen API pro **kamery**. To slouží k nahrávání snímků do Connectu, ne k jejich čtení. Komunita API pro uživatele označuje za soukromé („kept private“) a od Prusy na to na fóru žádná odpověď není.
- Existují tři **neoficiální / interní** cesty. Všechny používají přihlášení **Prusa Account (OAuth 2.0 authorization code + PKCE)** a JWT v hlavičce `Authorization: Bearer …`:
  1. **Webové API Connectu** `https://connect.prusa3d.com/app/...`. Používá ho webová aplikace Connect a komunitní SDK. Je nejlépe prozkoumané (reverse engineering).
  2. **API pro PrusaSlicer** `https://connect.prusa3d.com/slicer/...`. Používá ho PrusaSlicer (open source). Pro nás je ale svázané s `client_id` a `redirect_uri` PrusaSliceru.
  3. **Mobilní API gateway** `https://connect-mobile-api.prusa3d.com/api/v1/...`. Má veřejně dostupnou OpenAPI specifikaci (`0.0.1-dev`), ale chybí v ní, jak získat token.
- Rate limity nejsou nikde zveřejněné. Podmínky použití zakazují „scraping, umělé zvyšování počtu dotazů a přetěžování služby“ (čl. 5.3).
- Klient pro Connect je proto **technicky možný jen přes nedokumentované API**. Jestli je to přijatelné, rozhoduje uživatel (viz [Rozhodnutí pro uživatele](#rozhodnutí-pro-uživatele)).

## Oficiální a dokumentované

| Co | Popis | Pro nás |
|---|---|---|
| Camera API (Connect) – `https://connect.prusa3d.com/docs/cameras/` + [`openapi.yaml`](https://connect.prusa3d.com/docs/cameras/openapi/openapi.yaml) (`connect-api 0.22.0`) | Kamera třetí strany **posílá** snímky do Connectu (`PUT /c/snapshot`, `PUT /c/info`, hlavičky `Token` + `Fingerprint`). Ve specifikaci je API označené jako **deprecated** a nahrazuje ho Camera Service. | Ne – směr je kamera → Connect. Snímky odtud číst nejde. |
| Prusa Camera Service – `https://camera-service.prusa3d.com/docs/` | Nástupce: `camera-info` a `snapshot` pro nahrávání JPEG snímků. | Ne – stejný směr. |
| [Prusa-Connect-SDK-Printer](https://github.com/prusa3d/Prusa-Connect-SDK-Printer) | Python SDK pro **tiskárnu** (PrusaLink na RPi), které komunikuje s Connectem. Autentizace tokenem tiskárny z `prusa_printer_settings.ini`. | Ne – je to strana tiskárny, ne uživatele. |
| [Podmínky Prusa Connect & PrusaLink](https://www.prusa3d.com/page/prusa-connect-prusa-link-terms-and-conditions_233705/) (platné od 1. 1. 2020) | O API ani o klientech třetích stran se nezmiňují. Čl. 5.3: „refrain from scraping activities, artificially increasing queries and overloading the capacity of the service“. Čl. 1.4: službu smí používat jen registrovaný uživatel s vlastním Prusa Account. Čl. 3.4 a 7.1: provozovatel může službu kdykoli změnit a při porušení podmínek omezit přístup. | Výslovný zákaz klientů třetích stran tam není. Polling ale musí být šetrný. |
| Vlastní notifikační webhook v Connectu (nastavení notifikací → Channel Options), podle [fóra](https://forum.prusa3d.com/forum/introcuctions-instrutctions/prusa-connect-is-there-api-specification-or-mobile-android-app-with-notifications/) | Connect umí posílat notifikace na URL zadanou uživatelem. | Pro tray aplikaci prakticky nepoužitelné: potřebovala by veřejně dostupný endpoint. Jen pro úplnost. |

## Neoficiální / interní

### Přihlášení – Prusa Account OAuth 2.0 + PKCE

Ověřeno ze zdrojového kódu PrusaSliceru a komunitního SDK:

- Autorizace: `https://account.prusa3d.com/o/authorize/` (parametry `client_id`, `response_type=code`, `code_challenge`, `code_challenge_method=S256`, `scope`, `redirect_uri`).
- Token: `https://account.prusa3d.com/o/token/`
  - `grant_type=authorization_code` + `code_verifier`;
  - obnova přes `grant_type=refresh_token`;
  - bez client secret (veřejný klient).
- Profil: `https://account.prusa3d.com/api/v1/me/` (PrusaSlicer jím testuje platnost tokenu).
- Access token je JWT. Claims podle komunitního SDK: `jti`, `sub`, `exp`, `sid`, `app` (např. `connect`), `type`, `connect_id`. Platnost cca **1 hodina**, refresh token cca **30 dní** (podle komunity, neověřeno).
- **Refresh token se zřejmě rotuje.** PrusaSlicer výslovně řeší `invalid_grant`, když token mezitím obnovila jiná instance aplikace (`UserAccountCommunicationTokenBase.cpp`). Nový refresh token se proto musí po každé obnově hned uložit.
- **Neexistuje registrovaný `client_id` pro aplikace třetích stran** ani veřejný proces, jak ho získat. Známé `client_id`:

| Klient | `client_id` | `redirect_uri` | Poznámka |
|---|---|---|---|
| PrusaSlicer | `oamhmhZez7opFosnwzElIgE2oGgI2iJORSkw587O` | `prusaslicer://login` | Vlastní URL schéma registrované PrusaSlicerem. Pro nás nepoužitelné (otevřelo by PrusaSlicer) a znamenalo by vydávat se za PrusaSlicer. Scope `basic_info`. |
| Connect web | `MRHTlZhZqkNrrQ6FUPtjyusAz8nc59ErHXP8XkS4` | `https://connect.prusa3d.com/login/auth-callback` | Používá ho komunitní SDK. Kód se vrací na web Connectu, takže desktopová aplikace ho musí nějak zachytit (viz varianty přihlášení). Scopes podle komunity: `openid basic_info connect …`. |

Přihlášení **heslem** je možné jen napodobením přihlašovacího HTML formuláře: CSRF token, e-mail + heslo, případně TOTP 2FA (tak to dělá komunitní SDK `auth.py`). Je to křehké a aplikace by držela heslo uživatele.

### A) Webové API Connectu – `https://connect.prusa3d.com/app/...`

Zdroj: komunitní reverse engineering ([python-prusa-connect-sdk-client](https://github.com/dcode/python-prusa-connect-sdk-client), odvozená [OpenAPI](https://github.com/dcode/python-prusa-connect-sdk-client/blob/main/data/openapi.json) `20260225-5dfbf71e` a [poznámky ze zachyceného provozu](https://github.com/dcode/python-prusa-connect-sdk-client/blob/main/docs/development_notes/rest_api_reference.md) z 2026-02). Anonymní `GET /app/printers` vrací `401 {"message": "Missing or invalid security token", "code": "UNAUTHORIZED"}`, endpoint tedy existuje.

| Účel | Endpoint | Poznámka |
|---|---|---|
| Seznam tiskáren účtu | `GET /app/printers?limit=&offset=&team_id=` | Odpověď je `{"printers": [...]}` (podle SDK někdy holé pole). Každá položka už obsahuje stav, teploty i aktuální úlohu, takže **jedním dotazem dostaneme všechny tiskárny**. |
| Detail tiskárny | `GET /app/printers/{uuid}` | Viz pole níže. |
| Historie úloh | `GET /app/printers/{uuid}/jobs?state=&limit=&offset=` | Stavy `PRINTING`, `PAUSED`, `FIN_OK`, `FIN_ERROR`, `FIN_STOPPED`, `UNKNOWN`. |
| Detail úlohy | `GET /app/printers/{uuid}/jobs/{job_id}` | |
| Události | `GET /app/printers/{uuid}/events?event=STATE_CHANGED` | |
| Kamery | `GET /app/cameras`, `GET /app/printers/{uuid}/cameras` | |
| Poslední snímek | `GET /app/cameras/{camera_id}/snapshots/last` | Vrací přímo bajty obrázku, 404 když snímek není. |
| Verze serveru | `GET /version.json` | Bez přihlášení. |

Relevantní pole tiskárny (snake_case):
- `uuid`, `name`, `printer_state` (v některých odpovědích `state`), `state_reason`, `printer_model`, `printer_type_name`, `firmware`, `sn`, `last_online` (Unix čas);
- `temp.{temp_nozzle, target_nozzle, temp_bed, target_bed}`;
- `speed`, `flow`, `axis_z`;
- `job_info.{id, state, progress (0–100), time_printing (s), time_remaining (s), display_name, path, start, end, preview_url}`;
- `cameras[]`.

Stavy tiskárny: `READY`, `IDLE`, `BUSY`, `MANIPULATING`, `PRINTING`, `PAUSED`, `FINISHED`, `STOPPED`, `ATTENTION`, `ERROR`, `OFFLINE`, `UNKNOWN`. Otáčky ventilátorů odvozená specifikace neuvádí.

Chyby: JSON `{ "code": "UNAUTHORIZED" | "BAD_REQUEST" | ..., "message": "...", "reason": ... }`.

### B) API pro PrusaSlicer – `https://connect.prusa3d.com/slicer/...`

Zdroj: open source PrusaSlicer (`src/slic3r-shared/include/Slic3r/Biz/Network/ServiceConfig.hpp`, `UserAccountActionStore.hpp`).

| Účel | Endpoint |
|---|---|
| Stav všech tiskáren podle UUID | `GET /slicer/status` |
| Seznam tiskáren s modely | `GET /slicer/v2/printers` |
| Data tiskárny | `GET /app/printers/{uuid}` |

Všechno s `Authorization: Bearer <access_token>`. Tvar odpovědí `/slicer/*` ze zdrojů nemáme (neověřeno). Token se tu získává přes `client_id` PrusaSliceru, takže to pro nás jako primární cesta nedává smysl. Jde ale o nejsilnější důkaz, že `Bearer` token z Prusa Account funguje proti `connect.prusa3d.com`.

### C) Mobilní API gateway – `https://connect-mobile-api.prusa3d.com`

Zdroj: veřejně dostupná OpenAPI 3.1 na [`https://connect-mobile-api.prusa3d.com/api/docs`](https://connect-mobile-api.prusa3d.com/api/docs). Odkazuje na ni komunita na [fóru](https://forum.prusa3d.com/forum/general-discussion-user-experience-ideas/prusa-connect-api-for-automation/). Titulek je „Connect mobile API – API gateway for Prusa Connect mobile application“, verze `0.0.1-dev`. Operace mají tag „Public Endpoints“, ale Prusa tu specifikaci nikde neohlásila jako API pro třetí strany.

- Zabezpečení: `client_jwt_token` (apiKey v hlavičce `Authorization`) nebo `server_to_server`. Odkud se token bere, specifikace nepopisuje. Nevíme, jestli gateway přijme token vydaný pro web Connectu (neověřeno).
- Endpointy:
  - `GET /api/v1/printers` (stránkování `page`, `itemsPerPage`, filtr `state`, `team.id`);
  - `GET /api/v1/printers/{uuid}` („printer_with_telemetry“);
  - `GET /api/v1/printers/{uuid}/detail`;
  - `GET /api/v1/jobs?printer=&printerJobStatus=current|past`;
  - `GET /api/v1/cameras`;
  - `GET /api/v1/user`;
  - `GET /api/v1/notifications/unseen`;
  - příkazy (pause / resume / stop …) jsou mimo náš rozsah.
- Pole (camelCase):
  - tiskárna: `uuid`, `name`, `state` (stejný výčet jako výše), `printerModel`, `firmware`, `supportsTelemetry`, `defaultSnapshot`, `snapshots`, `cameraIds`;
  - `telemetry.{temperatureNozzleCurrent, temperatureNozzleTarget, temperatureHeatbedCurrent, temperatureHeatbedTarget, speed, axisZ, lastOnline}`;
  - úloha: `state` (`PRINTING`, `PAUSED`, `FIN_STOPPED`, `FIN_ERROR`, `FIN_OK`, `FIN_HARVESTED`, `UNKNOWN`), `progress`, `fileName`, `startAt`, `endAt`, `estimatedPrintTime`, `printerUuid`.
- Seznam tiskáren neobsahuje telemetrii ani úlohu. Na jednu tiskárnu by byly potřeba 1–2 dotazy navíc.

### Komunitní integrace (co skutečně používají)

| Projekt | Co používá |
|---|---|
| [dcode/python-prusa-connect-sdk-client](https://github.com/dcode/python-prusa-connect-sdk-client) (AGPL, aktivní 2026) | Webové API `/app/...`, PKCE s `client_id` webu Connectu, přihlášení scrapingem formuláře (heslo + TOTP). Tokeny ukládá do souboru JSON. |
| [Home Assistant – PrusaLink](https://www.home-assistant.io/integrations/prusalink/) (oficiální integrace HA) | Jen **lokální** PrusaLink. Oficiální cloudovou integraci pro Connect HA nemá. |
| [landmaj/prusa_connect](https://github.com/landmaj/prusa_connect) (HA, 2024) | Navzdory názvu lokální API tiskárny (konfiguruje se `host: 192.168.x.x`), ne cloud. |
| OctoPrint pluginy a ESP32 kamery ([Prusa-Firmware-ESP32-Cam](https://github.com/prusa3d/Prusa-Firmware-ESP32-Cam), [prusa_connect_uploader](https://github.com/rizz360/prusa_connect_uploader) …) | Jen Camera API (nahrávání snímků). |

## Omezení pro náš klient

- **Native AOT:** HTTP přes `IHttpClientFactory`, JSON jen přes zdrojově generovaný `JsonSerializerContext`. JWT nevalidujeme, jen z něj čteme `exp`. Stačí base64url dekódování payloadu a zdrojově generované DTO, žádná knihovna navíc. PKCE (`RandomNumberGenerator` + `SHA256`) je v BCL. Kvůli tomu není potřeba žádný NuGet balíček.
- **Testy:** fixtures `tests/PrusaTrayDiagnostic.PrusaConnect.Tests/Fixtures/*.json` a falešný `HttpMessageHandler`, bez sítě. Tvar JSON zatím známe jen z komunitní specifikace. Skutečné odpovědi musí nahrát uživatel ze svého účtu a anonymizovat je (UUID, sériová čísla, IP, MAC, jména, tokeny).
- **Tajné údaje a limit Windows Credential Manageru 2560 B:**
  - Skutečnou velikost tokenů jsme bez účtu nezměřili.
  - Nepřímý důkaz: PrusaSlicer ukládá `access_token|refresh_token|shared_session_key|timeout|pid` jako **jeden** tajný údaj (`UserAccountTokenStore.cpp`, na Windows přes `wxSecretStore`, tedy Credential Manager). To naznačuje, že se obvyklé tokeny do 2560 B vejdou (odvozeno, neověřeno).
  - Doporučení:
    - **trvale ukládat jen refresh token** (pod vlastním klíčem pro účet, ne pro tiskárnu);
    - access token držet jen v paměti;
    - při překročení limitu (`ISecretStore.SetAsync` vyhodí `ArgumentException`) ohlásit srozumitelnou chybu, nic tiše neořezávat.
- **Model:** přihlášení patří k **účtu**, ne k tiskárně. Jeden účet = více tiskáren, `PrinterDefinition.Address` = UUID tiskárny. `SecretKeys` zatím umí jen `ForPrinter(id)`, takže T-005 musí doplnit klíč pro účet Connect.
- **Šetrnost:** výchozí interval pollingu je 5 s (T-002). U cloudu to vzhledem k čl. 5.3 podmínek může být problém. Klient by měl:
  - načítat všechny tiskárny účtu jedním dotazem;
  - sdílet výsledek mezi `IPrinterClient` instancemi;
  - respektovat `429` / `Retry-After` a při chybách 5xx couvat (backoff).

## Mapování na doménový model (varianta A, webové API)

| Connect | `PrinterState` |
|---|---|
| `IDLE`, `READY` | `Idle` (`READY` = připravena pro tisk z fronty; jestli ji rozlišovat, viz otázky v T-005) |
| `BUSY`, `MANIPULATING` | `Busy` |
| `PRINTING` / `PAUSED` / `FINISHED` / `STOPPED` / `ERROR` / `ATTENTION` | `Printing` / `Paused` / `Finished` / `Stopped` / `Error` / `Attention` |
| `OFFLINE` | `Offline` (s `last_online` ve `StatusMessage`) |
| `UNKNOWN`, neznámá hodnota | `Unknown` |
| HTTP 401 po neúspěšné obnově tokenu | `Unauthorized` |
| síťová chyba, 5xx, timeout | `Offline` / `Unknown` se `StatusMessage` (sjednotit s T-003) |

| Connect | Doména |
|---|---|
| `job_info.display_name` (nebo `path`) | `PrintJob.FileName` |
| `job_info.progress` | `PrintJob.ProgressPercent` |
| `job_info.time_printing` / `time_remaining` | `PrintJob.Elapsed` / `Remaining` |
| `temp.temp_nozzle` + `target_nozzle` | `Telemetry.Nozzle` |
| `temp.temp_bed` + `target_bed` | `Telemetry.Bed` |
| `axis_z`, `speed`, `flow` | `Telemetry.AxisZ`, `SpeedPercent`, `FlowPercent` |
| ventilátory | není k dispozici → `null` |

## Doporučení (architekt)

1. **PrusaLink zůstává primárním a jediným oficiálně podporovaným zdrojem** (T-003). Prusa Connect je doplněk s rizikem. API se může kdykoli změnit bez ohlášení a přístup může být omezen.
2. Pokud uživatel nedokumentované API přijme, technicky doporučuji **variantu A (webové API `/app/...`)**:
   - je nejlépe zmapované a jedním dotazem vrátí stav, teploty i úlohu všech tiskáren;
   - v komunitě je aktivně používané (2026);
   - klient jen **čte** (žádné příkazy);
   - izolovat ho za `IPrinterClient` tak, aby se při změně API dal vypnout bez dopadu na zbytek aplikace.
3. Variantu C (mobilní gateway) brát jako záložní, až po ověření, že přijímá stejný token. Variantu B (PrusaSlicer `client_id`) nepoužívat.
4. Přihlášení heslem (scraping formuláře) nedoporučuji: aplikace by držela heslo, je to křehké a obchází to 2FA tok. Lepší je PKCE v prohlížeči nebo WebView (viz rozhodnutí 3).
5. Před implementací T-005 by uživatel měl jednorázově ze svého účtu zachytit skutečné odpovědi (DevTools prohlížeče na `connect.prusa3d.com`), anonymizovat je jako fixtures a změřit délku access a refresh tokenu.
6. Zvážit oslovení Prusa Research s žádostí o oficiální `client_id` / API pro třetí strany (mimo repozitář, rozhodnutí uživatele).

## Rozhodnutí uživatele (2026-10-15)

Prusa Connect se **odkládá**. Nedokumentované API se zatím nepoužije a úkoly T-005 a T-022 mají prioritu P3. Pro tiskárny v lokální síti se používá PrusaLink. Otázky níže se otevřou, až se Connect obnoví (např. po získání oficiálního přístupu od Prusy).

## Rozhodnutí pro uživatele

1. **Je přijatelné použít nedokumentované API Connectu?**
   - a) ano, varianta A (webové API `/app/...`);
   - b) ano, varianta C (mobilní gateway), po ověření;
   - c) ne – Connect zatím vynechat (T-005 odložit, aplikace jen s PrusaLink);
   - d) nejdřív požádat Prusu o oficiální přístup a do té doby odložit.
2. **Jaký `client_id` použít?** Vlastní registrovaný klient neexistuje.
   - a) `client_id` webu Connectu (jako komunitní SDK);
   - b) počkat na oficiální `client_id` od Prusy.
3. **Způsob přihlášení:**
   - a) vestavěné WebView v pomocném okně, které zachytí přesměrování na `https://connect.prusa3d.com/login/auth-callback?code=…`. Vyžaduje ověřit Avalonia WebView pro Native AOT na Windows i macOS a případně přidat balíček.
   - b) systémový prohlížeč + uživatel ručně vloží výslednou URL / kód zpět do aplikace;
   - c) e-mail + heslo (+ TOTP) scrapingem přihlašovacího formuláře (nedoporučeno);
   - d) uživatel vloží access/refresh token ručně (jen pro pokročilé, token vyprší).
4. **Interval pollingu pro Connect:** sdílet globální interval (výchozí 5 s), nebo mít pro cloud vlastní minimum (např. 30 s)? Konkrétní hodnota je na uživateli.
5. **Stav `READY`** (připravena pro tisk z fronty): mapovat na `Idle`, nebo přidat do domény samostatný stav?
6. **Více účtů Connect:** stačí jeden účet, nebo podporovat víc účtů / týmů?
7. **Kamera přes Connect (T-014):** použít `GET /app/cameras/{id}/snapshots/last` (nedokumentované)?

## Zdroje

- Camera API (oficiální): https://connect.prusa3d.com/docs/cameras/camera_registration/, https://connect.prusa3d.com/docs/cameras/openapi/openapi.yaml
- Prusa Camera Service: https://camera-service.prusa3d.com/docs/
- Mobilní API gateway (OpenAPI): https://connect-mobile-api.prusa3d.com/api/docs
- Prusa-Connect-SDK-Printer: https://github.com/prusa3d/Prusa-Connect-SDK-Printer
- PrusaSlicer – OAuth a Connect:
  - https://github.com/prusa3d/PrusaSlicer/blob/master/src/slic3r-shared/src/Slic3r/Biz/Network/ServiceConfig.cpp
  - https://github.com/prusa3d/PrusaSlicer/blob/master/src/slic3r-shared/include/Slic3r/Biz/Network/ServiceConfig.hpp
  - https://github.com/prusa3d/PrusaSlicer/blob/master/src/slic3r-shared/src/Slic3r/Biz/UserAccount/UserAccountCommunication.cpp
  - https://github.com/prusa3d/PrusaSlicer/blob/master/src/slic3r-shared/src/Slic3r/Biz/UserAccount/UserAccountSession.cpp
  - https://github.com/prusa3d/PrusaSlicer/blob/master/src/slic3r-shared/src/Slic3r/Biz/UserAccount/UserAccountTokenStore.cpp
  - https://github.com/prusa3d/PrusaSlicer/blob/master/src/slic3r-shared/src/Slic3r/Biz/UserAccount/UserAccountCommunicationTokenBase.cpp
  - https://github.com/prusa3d/PrusaSlicer/blob/master/src/slic3r-shared/include/Slic3r/Biz/UserAccount/UserAccountActionStore.hpp
- Komunitní SDK (reverse engineering):
  - https://github.com/dcode/python-prusa-connect-sdk-client
  - https://github.com/dcode/python-prusa-connect-sdk-client/blob/main/data/openapi.json
  - https://github.com/dcode/python-prusa-connect-sdk-client/blob/main/docs/development_notes/rest_api_reference.md
  - https://github.com/dcode/python-prusa-connect-sdk-client/blob/main/docs/authentication.md
- Podmínky: https://www.prusa3d.com/page/prusa-connect-prusa-link-terms-and-conditions_233705/
- Fórum:
  - https://forum.prusa3d.com/forum/general-discussion-user-experience-ideas/prusa-connect-api-for-automation/
  - https://forum.prusa3d.com/forum/introcuctions-instrutctions/prusa-connect-is-there-api-specification-or-mobile-android-app-with-notifications/
- Home Assistant PrusaLink: https://www.home-assistant.io/integrations/prusalink/
- Nápověda Prusa (API klíče tiskáren pro PrusaSlicer): https://help.prusa3d.com/article/sending-g-codes-to-printer-via-network-prusa-connect-prusalink-octoprint_196761
