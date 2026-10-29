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
| `GET /api/v1/job` | jen při změně `job.id` | název souboru (`file.display_name`) a odkazy na náhled (`file.refs`); **204 No Content**, když úloha neběží (i ve stavu FINISHED/STOPPED) |
| `GET /thumb/s/…`, `/thumb/l/…` | jen když UI náhled chce, nejvýš jednou na úlohu a velikost (T-086) | PNG náhled G-code, viz „Náhled G-code“ |
| `GET /api/version` | test spojení; jednou za běh monitoringu (model, T-039) | `api`, `hostname`; `firmware` a `printer` až od 6.5 |
| `GET /api/v1/info` | test spojení | `mmu` (bool), `nozzle_diameter`, `hostname`, `serial` |

- Kamery (`/api/v1/cameras`) Buddy nemá a vrací 404.
- **MMU:** jedinou informací o MMU je `info.mmu`. Stav slotů API neposkytuje.

## Soubor úlohy: odkazy na náhled a metadata (průzkum 2026-10-24, T-078 / T-086)

- `GET /api/v1/job` vrací ve firmwaru Buddy (`get_job_v1` v `lib/WUI/link_content/basic_gets.cpp`) objekt `file` s poli `refs`, `name` (8.3 název), `display_name`, `path` (složka), `size` a `m_timestamp`. Pole `size` a `m_timestamp` jsou jen při úspěšném `stat`. `refs` obsahuje:
  - `icon`: `/thumb/s<cesta>`,
  - `thumbnail`: `/thumb/l<cesta>`,
  - `download`: `<cesta>`.

  Příklad: fixture `firmware/job-printing.json`.
- **Metadata souboru Buddy neposílá.** Objekt `file.meta` (`layer_height`, `filament_type`, `nozzle_diameter`, `estimated_print_time`…) ze specifikace OpenAPI (`PrintFileMetadata`) ve firmwaru chybí. Údaje pro čipy widgetu Název tisku jde proto získat jen z `display_name` (T-078).
- **Šablona názvu souboru PrusaSliceru** (rozbírá ji `Core/Printers/PrintFileNameInfo.Parse`, T-078). Zdroj: `output_filename_format` v profilech Prusa, https://github.com/prusa3d/PrusaSlicer-settings-prusa-fff/blob/main/PrusaResearch/2.5.13.ini (ověřeno 2026-10-24):
  - většina profilů: `{input_filename_base}_{nozzle_diameter[0]}n_{layer_height}mm_{printing_filament_types}_{printer_model}_{print_time}.gcode`; některé mají trysku napevno (`_0.4n_`), model napevno (`COREONEINDX`, `XLIS`, `XL`, `XL+`) nebo trysku chybějící (XL+),
  - profily HT hotendu píšou trysku jako `HT0.4` / `HT0.6` (bez `n`),
  - `printer_model` může obsahovat tečku (`MK3.9S`) i podtržítko (`COREONE_INDX4T`, `COREONE_INDX8T`),
  - `print_time` je výstup `short_time()` ze zdrojů PrusaSliceru (`src/libslic3r/src/libslic3r/utils.cpp`): `1d2h3m`, `2h51m`, `45m`, krátké tisky `42s`,
  - víc materiálů je v `printing_filament_types` oddělených čárkou (`PLA,PETG`), parser přijme i středník.
  - přípony, které na konec názvu přidá prohlížeč nebo Průzkumník Windows (` (1)`, ` - Copy`, ` - Copy (2)`, české ` - kopie`, ` - kopie (2)`), parser před rozborem odřízne; jen přesně tyto tvary přímo na konci názvu (rozhodnutí 6, 2026-10-24).
- Náhled G-code popisuje oddíl „Náhled G-code“ níže.
- Zdroje:
  - https://github.com/prusa3d/Prusa-Firmware-Buddy/blob/master/lib/WUI/link_content/basic_gets.cpp
  - https://raw.githubusercontent.com/prusa3d/Prusa-Link-Web/master/spec/openapi.yaml

## Náhled G-code (`/thumb/s`, `/thumb/l`, T-086)

Ověřeno ve zdrojích firmwaru na `master` (commit `1ce23f33`, 2026-08-27). Na skutečné tiskárně zatím neověřeno.

**Odkazy:**
- Přicházejí v `file.refs` z `GET /api/v1/job`: `icon` = `/thumb/s` + cesta, `thumbnail` = `/thumb/l` + cesta.
- Cesta je krátká (8.3, SFN), např. `/usb/BENCHY~1.BGC`. Firmware ji v JSON jen escapuje (`JSONIFY_STR`), pro URL ji **nekóduje**. Požadavek naopak URL dekóduje (`RequestParser::uri_filename`, `lib/WUI/nhttp/req_parser.cpp`).
- Po skončení tisku vrací `/api/v1/job` `204` a odkazy už nepřijdou. Náhled se proto musí stáhnout během tisku.

**Směrování** (`lib/WUI/link_content/previews.cpp`):
- Prefix `/thumb/` vyžaduje přihlášení (Digest) dřív než cokoli jiného.
- Písmeno za prefixem určuje rozměr. Jiné písmeno vrátí `404` „Thumbnail size specification not recognized“.
- Jiná metoda než GET vrátí `405`, neexistující soubor `404`.

**Rozměry** (ověřeno: `previews.cpp`, výběr obrázku v `src/common/gcode/gcode_reader_binary.cpp` a `gcode_reader_plaintext.cpp`):

| Odkaz | Požadavek firmwaru | Co se vrátí |
|---|---|---|
| `/thumb/s` | `width = 16`, `height = 16`, `allow_larger = false` | PNG přesně **16 × 16 px**, jinak `404` |
| `/thumb/l` | `width = 17`, `height = 17`, `allow_larger = true` | **první** PNG v souboru s oběma stranami aspoň **17 px** |

- Rozměr `l` tedy není pevný, určuje ho nastavení `thumbnails` v profilu PrusaSliceru. Všechny profily Prusa pro tiskárny s firmwarem Buddy (MINI, MK3.5/3.9/4, XL, Core One, Core One L) v bundlu 2.5.13 mají `… 380x285/PNG`. `/thumb/l` z nich proto vrátí **380 × 285 px**.
  - Zdroj: https://github.com/prusa3d/PrusaSlicer-settings-prusa-fff/blob/main/PrusaResearch/2.5.13.ini, klíč `thumbnails` (ověřeno 2026-10-24).
- Tytéž profily mají 16 × 16 jen jako **QOI** (`16x16/QOI`). `/thumb/s` vrací jen PNG, takže pro soubory ze standardních profilů vrátí **404**. Malý náhled existuje jen u vlastních profilů s `16x16/PNG`.

**Odpověď** (`lib/WUI/nhttp/gcode_preview.cpp`, `headers.cpp`):
- Jen PNG (`Content-Type: image/png`), tělo chunked (bez `Content-Length`).
- `ETag` je číslo v uvozovkách (`"%u"`) spočítané z `stat` souboru (inode, velikost, `mtime`, `ctime`; `compute_etag`). Stejný soubor má stejný ETag i u další úlohy.
- `If-None-Match` firmware čte jako číslo (číslice, ostatní znaky ignoruje). Při shodě vrátí `304` s ETagem a bez těla.
- Soubor bez PNG náhledu požadované velikosti (nebo chyba čtení) → `404` „File doesn't contain preview“.

**V aplikaci:**
- `PrusaLinkClient` načte odkazy spolu s názvem souboru jednou za úlohu a předá je jako `PrintJob.PreviewReference` (`PrusaLinkPreviewReference`). Ten drží cesty bez úvodního `/` a s URL kódovanými segmenty (mezery, Unicode, `%`), takže se rozloží i pod prefixem reverzní proxy.
  - Odkaz s `?`, `#`, `\`, segmenty `.`/`..`, prázdným segmentem, řídicími znaky nebo osamocenými surrogaty se zahodí. Takový odkaz pak náhled nemá.
- `PrusaLinkPreviewFetcher` stahuje přes vlastní pojmenovaný `HttpClient` `PrusaLink.Preview` (1 spojení na tiskárnu, odděleně od pollingu).
  - Spolu s monitoringem (1 spojení) se vejde do 3 aktivních požadavků firmwaru. Když zároveň běží odkaz na tiskárnu přes proxy (T-028, až 2 spojení), je požadavků 4 a firmware je jen zpomalí (viz „Spojení a zátěž“).
  - Rozhodnutí 13 (uživatel, 2026-10-24): čtvrté spojení se zatím přijímá. Omezení proxy odkazu na 1 spojení se zváží, jen když se na skutečné tiskárně objeví problémy.
  - Výsledná adresa musí zůstat na hostu, portu a schématu tiskárny a pod její cestou (prefix proxy). Jinak se nic nestahuje (pojistka navíc ke kontrole odkazu).
  - Digest: přihlášení je pro každé stažení nové, takže stojí jednu výzvu `401` navíc. Náhled se stahuje zhruba jednou na úlohu a velikost.
  - Limity: celkový timeout 10 s (hlavičky i tělo), nejvýš 512 KB. Limit se hlídá **během čtení**: delší tělo se po 512 KB + 1 B přestane číst a zahodí. Větší `Content-Length` se odmítne bez čtení těla.
  - Kontroly: `Content-Type: image/png`, signatura PNG a hlavička `IHDR` s rozměry 1–2048 px (`PrintPreview.FromPng`). Obrázek se nedekóduje.
  - `404` = bez náhledu, bez záznamu v logu. Jiný stav, timeout, nedostupná tiskárna a chyba hesla = přechodná chyba, debug zpráva jen s id tiskárny, velikostí a důvodem. Adresa, cesta, heslo ani text výjimky v logu nejsou.
- Cache (`Core/Previews/PrintPreviewCache`) je jen v paměti, popis v [../architecture.md](../architecture.md) (widgety, „Náhled G-code“).

**Zdroje:**
- https://github.com/prusa3d/Prusa-Firmware-Buddy/blob/master/lib/WUI/link_content/previews.cpp
- https://github.com/prusa3d/Prusa-Firmware-Buddy/blob/master/lib/WUI/nhttp/gcode_preview.cpp
- https://github.com/prusa3d/Prusa-Firmware-Buddy/blob/master/lib/WUI/nhttp/headers.cpp (`write_headers`, `compute_etag`)
- https://github.com/prusa3d/Prusa-Firmware-Buddy/blob/master/lib/WUI/nhttp/req_parser.cpp (`If-None-Match`, `uri_filename`)
- https://github.com/prusa3d/Prusa-Firmware-Buddy/blob/master/src/common/gcode/gcode_reader_binary.cpp (`stream_thumbnail_start`)
- https://github.com/prusa3d/Prusa-Firmware-Buddy/blob/master/src/common/gcode/gcode_reader_plaintext.cpp (`IsBeginThumbnail`)
- https://help.prusa3d.com/article/model-preview_648687 (PNG / QOI náhledy v G-code)

## Model tiskárny (`/api/version`, pole `printer`, T-039)

**Formát:** `"printer": "<type>.<version>.<subversion>"`, např. `"1.4.1"`. Firmware ho skládá přes `JSON_FIELD_STR_FORMAT("printer", "%i.%i.%i", …)` z `PrinterModelInfo::current().version` (`lib/WUI/link_content/basic_gets.cpp`, funkce `get_version`). Pole existuje **od firmwaru 6.5** (v tagu `v6.4.2` chybí spolu s `firmware`, v `v6.5.1` už je).

**Rozšířený typ tiskárny:** `PrinterModelInfo::current()` (`src/common/printer_model.cpp`) vrací u firmwaru s rozšířeným typem (`include/common/extended_printer_type.hpp`) model nastavený v tiskárně, ne model sestavení firmwaru:
- firmware MK4: MK4, MK4S, MK3.9, MK3.9S;
- firmware MK3.5: MK3.5, MK3.5S;
- firmware XL: XL, XL+ (od 6.8; ve 6.10 se typ zjišťuje automaticky podle hardwaru, starší verze jsme v tomto ohledu nekontrolovali).

Kód proto rozliší i varianty se stejným firmwarem.

**Zdroj tabulky:** Prusa-Firmware-Buddy, `include/common/printer_model_data.hpp` (pole `printer_model_info`, položka `.version`):
- https://github.com/prusa3d/Prusa-Firmware-Buddy/blob/v6.10.3/include/common/printer_model_data.hpp
- https://github.com/prusa3d/Prusa-Firmware-Buddy/blob/v6.5.1/include/common/printer_model_data.hpp

Ověřeno v tazích `v6.5.1`, `v6.6.0`, `v6.8.0`, `v6.9.x`, `v6.10.0`, `v6.10.3` a na `master` (commit `1ce23f33`, 2026-08-27). Kódy se mezi verzemi neměnily, jen přibývaly modely.

| Kód | Model ve firmwaru (`PrinterModel::`, `id_str`) | Od tagu | `PrinterModel` v aplikaci |
|---|---|---|---|
| `1.3.0` | `mk3`, MK3 | – | `Unknown` (8bitový firmware bez PrusaLink Buddy) |
| `1.3.1` | `mk3s`, MK3S | – | `Unknown` (8bitový firmware bez PrusaLink Buddy) |
| `1.3.5` | `mk3_5`, MK3.5 | 6.5.1 | `Mk35` |
| `1.3.6` | `mk3_5s`, MK3.5S | 6.5.1 | `Mk35S` |
| `1.3.9` | `mk3_9`, MK3.9 | 6.5.1 | `Mk39` |
| `1.3.10` | `mk3_9s`, MK3.9S | 6.5.1 | `Mk39S` |
| `1.4.0` | `mk4`, MK4 | 6.5.1 | `Mk4` |
| `1.4.1` | `mk4s`, MK4S | 6.5.1 | `Mk4S` |
| `2.1.0` | `mini`, MINI | 6.5.1 | `Mini` (MINI i MINI+) |
| `3.1.0` | `xl`, XL | 6.5.1 | `Xl` |
| `3.1.1` | `xls`, XLP (na displeji „XL+“) | 6.8.0 | `XlPlus` |
| `4.1.0` | `ix`, iX | 6.5.1 | `Unknown` (průmyslová tiskárna, bez ilustrace) |
| `5.1.0` | `xl_dev_kit`, XL | 6.5.1 | `Xl` (ve firmwaru „works like standard XL“) |
| `7.1.0` | `coreone`, COREONE | 6.5.1 | `CoreOne` |
| `7.2.0` | `coreone_oak`, COREONEOAK | 6.6.0 | `Unknown` (nevydaná varianta, ve firmwaru „dead code“) |
| `7.10.0` | `coreone_indx`, COREONEINDX | 6.6.0 | `Unknown` (obecná ilustrace, rozhodnutí T-039 2026-10-20) |
| `8.1.0` | `coreonel`, COREONEL | 6.5.1 | `CoreOneL` |
| `8.10.0` | `coreonel_indx`, COREONEL-INDX | 6.6.0 | `Unknown` (obecná ilustrace, rozhodnutí T-039 2026-10-20) |
| cokoli jiného | – | – | `Unknown` (kód se ale uloží, novější verze aplikace ho může znát) |

**Co kód nerozliší:**
- MINI a MINI+ mají stejný firmware i kód `2.1.0`.
- XL s jedním a s více nástroji má stejný kód `3.1.0`. Počet nástrojů z `/api/version` ani `/api/v1/info` nezjistíme; jiný zdroj jsme neověřovali.
- MMU kód nemění (MMU varianty ve firmwaru mají jen jiný G-code kód). MMU hlásí `info.mmu`.

**Neověřeno na skutečné tiskárně:** nahrané odpovědi (`Fixtures/recorded/`) zatím nemáme. Tabulka vychází jen ze zdrojového kódu. Starší fixture `version.json` měla vymyšlený kód `7.0.0`, opraveno na `7.1.0` (Core One).

**Prusa Connect:** vrací `printer_model` a `printer_type_name` (viz [prusa-connect.md](prusa-connect.md)). Automatickou detekci u Prusa Connect podle rozhodnutí T-039 neděláme, takže hodnoty jsme neověřovali.

**V aplikaci:**
- Převod je `Core/Printers/PrinterModels.FromCode`, kód je `PrinterModelCode`.
- Klient: `PrusaLinkClient.DetectModelAsync` (jen `/api/version`) a `PrusaLinkPrinterInfo.ModelCode` (test spojení).
- Monitor se zeptá jednou za běh, po prvním úspěšném dotazu na stav, a vyvolá `IPrinterMonitor.ModelDetected`. Selhaný dotaz zkusí znovu po dalším úspěšném dotazu na stav, nejvýš 3× za běh. Firmware bez pole `printer` se už znovu neptá.
- Uložený kód je `printers[*].detectedModel` v `settings.json`.
- Kód zjištěný monitoringem uloží App (`MonitoringSession`) na pozadí, jen když se změnil. Do souboru zapíše jen tuto hodnotu, a jen do položky se stejnou adresou a typem připojení, pro které byl kód zjištěn (`ISettingsStore.SetDetectedModelAsync`, zbytek souboru zůstane bajt po bajtu). Monitoring kvůli tomu nerestartuje. Test spojení v okně nastavení kód uloží spolu s nastavením. Nová adresa nebo typ připojení v editoru kód zahodí do další detekce (T-039).

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
- K prohlížeči jdou jen hlavičky ze seznamu povolených (T-107, nález N1, `ProxyHeaders.ResponseAllowed`). Ostatní se zahodí, např. `Service-Worker-Allowed`, `Set-Cookie`, `WWW-Authenticate`, `Access-Control-*`, `Content-Security-Policy`, `Link`, `Refresh`, `Clear-Site-Data`, `Content-Location`, `Server` a jakákoli neznámá hlavička. Hop-by-hop hlavičky a hlavičky jmenované v `Connection` se zahodí i ze seznamu. Povolené jsou (firmware Buddy, `lib/WUI`, a Prusa-Link-Web `src/`, ověřeno 2026-10-29):

  | Hlavička | Proč |
  |---|---|
  | `Content-Type` | každý prostředek; s `nosniff` je nutná |
  | `Content-Encoding` | firmware posílá soubory webu zkomprimované (`Content-Encoding: gzip`, `link_content/static_file.cpp`) |
  | `Content-Disposition` | `attachment` u stažení souboru (`link_content/usb_files.cpp`) |
  | `Cache-Control` | `private, max-age=86400` u souborů webu (`static_file.cpp`); web ho čte u snímků kamery (`cameras.js`) |
  | `ETag` | soubory, náhledy a API (`nhttp/headers.cpp`); web ho čte (`auth.js`, `eTag`) |
  | `Last-Modified`, `Expires` | web je čte u snímků kamery (`cameras.js`) |
  | `Read-Only`, `Currently-Printing` | informace o souboru u `HEAD /api/v1/files/...` (`nhttp/file_info.cpp`); jen data bez významu pro prohlížeč |

  - `Content-Length` a `Location` nastavuje proxy sama. `Date` přidává Kestrel.
  - Firmware `Content-Range`, `Accept-Ranges`, `Vary` ani `Content-Language` neposílá, proto na seznamu nejsou.
- `Location` na tiskárnu se přepíše na `/<token>/...`, cizí adresy a relativní odkazy zůstanou beze změny.
- Proxy přidá ke každé odpovědi (i ke svým `404`, `421` a `403`) vlastní hlavičky. Stejnojmenné hlavičky tiskárny se zahodí. U `/api/` přidá navíc `Cache-Control: no-store`.
  - `Referrer-Policy: no-referrer` a `X-Content-Type-Options: nosniff`.
  - `Cross-Origin-Resource-Policy: same-origin`: cizí stránky nemohou obsah proxy vkládat ani načítat v režimu `no-cors`.
  - `Cross-Origin-Opener-Policy: same-origin` (T-107): stránka, která proxy otevře, si neudrží odkaz na její okno.
  - `X-Frame-Options: DENY` a `Content-Security-Policy: frame-ancestors 'none'; worker-src 'none'` (T-107): proxy nejde vložit do rámu (ani ze stránky na jiném portu `127.0.0.1`) a obsah tiskárny nespustí žádný Worker, hlavně ne Service Worker. Prusa-Link-Web nepoužívá rámy, Workery ani Service Worker (ověřeno ve zdroji, větev `master`, 2026-10-29). Ostatní direktivy CSP zůstávají volné, protože úplná politika (`script-src` apod.) by mohla web rozbít a ověřit ji jde jen se skutečnou tiskárnou. Vlastní politika obsahu (`<meta>`) může omezení jen přidat.

**Odmítnuté požadavky:**
- `Host` jiný než přesně `127.0.0.1:<port>` vrátí `421`. Chybějící nebo zdvojený `Host` odmítne už Kestrel (`400`).
- Chybějící nebo neplatný token vrátí `404` s prázdným tělem, stejně jako jakákoli jiná neznámá cesta.
- `CONNECT`/`TRACE` vrátí `405`, `Upgrade` (WebSocket) `501`.
- Stažení skriptu Service Workeru (T-107, nález N1) vrátí `403` s JSON chybou a na tiskárnu nedorazí. Prohlížeč posílá `Service-Worker: script` při registraci i při každé kontrole aktualizace. Odmítne se požadavek s hlavičkou `Service-Worker` (jakákoli hodnota) nebo se `Sec-Fetch-Dest: serviceworker`.
  - Proč: obsah na `http://127.0.0.1:<port>` pochází z firmwaru, nebo od kohokoli v LAN, kdo může měnit nešifrovaný provoz k tiskárně. Service Worker by přežil zastavení proxy a odpovídal by za cokoli, co později na stejném portu poběží.
  - Kontrola běží po kontrole `Host` a tokenu (neplatný token dál vrací `404`). Odmítnutí se loguje (Warning) jen s id tiskárny a neprodlužuje nečinnost proxy.
  - Druhá vrstva je `worker-src 'none'` v CSP: prohlížeč registraci odmítne dřív, než skript vůbec stáhne.
- Zápisové požadavky z cizí stránky (T-051, nález L1) vrátí `403` s JSON chybou a na tiskárnu nedorazí. Týká se všech metod kromě `GET`, `HEAD` a `OPTIONS`, a to když:
  - `Sec-Fetch-Site` je přítomná a není přesně `same-origin` ani `none` (jiný port na `127.0.0.1` je pro prohlížeč `same-site`), **nebo**
  - `Origin` je přítomná a není přesně `http://127.0.0.1:<port>` této proxy. `Origin: null` projde jen spolu se `Sec-Fetch-Site: same-origin` (rozhodnuto 2026-10-21); s `none` nebo bez `Sec-Fetch-Site` se odmítne.
  - Opakovaná hlavička (více hodnot) se také odmítne. Požadavek bez obou hlaviček (klient mimo prohlížeč, který stejně musí znát token) projde.
  - Kontrola běží až po kontrole `Host` a tokenu, takže neplatný token dál vrací `404` a odmítnutí neprozradí platnost tokenu.
  - Odmítnutí se loguje (Warning) jen s metodou a názvem hlavičky, bez tokenu, cesty a hodnoty hlavičky. Neprodlužuje nečinnost proxy.
  - Vlastní požadavky stránky proxy mají `Sec-Fetch-Site: same-origin` a buď skutečný `Origin` (`fetch`/`XMLHttpRequest` ve výchozím režimu `cors`), nebo kvůli `Referrer-Policy: no-referrer` `Origin: null` (HTML formuláře, `fetch` v režimu `same-origin`/`no-cors`, `sendBeacon`). Obojí projde, web PrusaLink proto funguje beze změny.

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
- Původ proxy je `127.0.0.1` (T-107, nález N1). Cookies nejsou oddělené podle portu, takže skript obsahu tiskárny může číst a nastavovat cookies bez `HttpOnly` ostatních služeb na `127.0.0.1`. Pro prohlížeč jsou navíc ostatní porty `127.0.0.1` „same-site“, takže jejich ochrana CSRF přes `SameSite` proti němu nefunguje. Oddělení náhodnou subdoménou `<náhodné>.localhost` se zatím nezavedlo:
  - Chrome, Edge a Firefox (od verze 84) `*.localhost` překládají samy na loopback.
  - Safari se spoléhá na systémový resolver. Ten `*.localhost` na loopback spolehlivě překládá až od macOS 26 (WebKit bug 160504). LayerPeek ale podporuje macOS 14 a novější.
  - Rozhodnutí je v otevřených otázkách T-107.

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

- `tests/LayerPeek.PrusaLink.Tests/Fixtures/firmware/` – odpovědi sestavené podle kódu firmwaru.
- `tests/LayerPeek.PrusaLink.Tests/Fixtures/recorded/<tiskárna>/<stav>/` – skutečné odpovědi nahrané skriptem `tools/record-prusalink-fixtures.ps1`. Skript se zeptá na heslo (skrytě) a anonymizuje sériové číslo. Test `RecordedFixturesTests` je automaticky všechny přehraje.
