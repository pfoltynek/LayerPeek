---
id: T-086
title: Náhled G-code – načtení z PrusaLink (Core abstrakce + klient + simulátor)
status: done
owner: core-dev
priority: P3
platform: all
depends_on: []
---

## Cíl
Získat náhled (thumbnail) tištěného G-code z tiskárny přes PrusaLink pro widget Název tisku (zobrazení řeší T-087). Core dostane abstrakci bez HTTP, PrusaLink ji implementuje.

## Ověřeno z primárních zdrojů (architect, 2026-10-24)
- **Firmware Buddy** (`get_job_v1` v https://github.com/prusa3d/Prusa-Firmware-Buddy/blob/master/lib/WUI/link_content/basic_gets.cpp): `GET /api/v1/job` vrací v `file.refs` adresy `"icon": "/thumb/s<cesta SFN>"`, `"thumbnail": "/thumb/l<cesta SFN>"`, `"download": "<cesta SFN>"`. Stejný tvar je v naší nahrané fixture `tests/LayerPeek.PrusaLink.Tests/Fixtures/firmware/job-printing.json` (`/thumb/s/usb/BENCHY~1.BGC`, `/thumb/l/usb/BENCHY~1.BGC`).
- **Obsluha náhledu** (https://github.com/prusa3d/Prusa-Firmware-Buddy/blob/master/lib/WUI/nhttp/gcode_preview.cpp): odpovídá **jen PNG** (`Content-Type: image/png`, požaduje `IGcodeReader::ImgType::PNG`, s `allow_larger`), podporuje **ETag / `If-None-Match` → 304**, bez náhledu v souboru vrací **404** „File doesn't contain preview“. Rozměry pro `s` a `l` nastavuje volající kód, který jsem neověřil – **ověřit ve firmwaru** (směrování `/thumb/`) a zapsat do `docs/api/prusalink.md`.
- Soubor bez PNG náhledu (např. G-code jen s QOI náhledy) tedy vrátí 404 → widget bez náhledu. PrusaSlicer ≥ 2.7 pro Prusa profily vkládá QOI i PNG náhledy (https://help.prusa3d.com/article/model-preview_648687).
- Specifikace OpenAPI Prusa-Link-Web (https://raw.githubusercontent.com/prusa3d/Prusa-Link-Web/master/spec/openapi.yaml) má `PrintFileRefs` (`icon`, `thumbnail`, `download`) jen jako příklady (`/api/thumbnails/...`), cestu nedefinuje; pro Buddy platí firmware.
- **Prusa Connect:** neoficiální `job_info.preview_url` (viz `docs/api/prusa-connect.md`), klient Prusa Connect (T-005) je odložený → v tomto úkolu **mimo rozsah**.

## Rozsah
- Core: rozhraní např. `IPrintPreviewSource.GetAsync(PrinterDefinition, PrintJob, PrintPreviewSize, CancellationToken)` → `PrintPreview?` (bajty PNG + rozměry, pokud jsou známé) – bez Avalonie a bez HTTP typů. Odkaz na náhled nese `PrintJob` jako neprůhlednou hodnotu, které rozumí jen klient, který ji vytvořil (např. `PrintJob.PreviewReference`), nebo si ho klient pamatuje podle `job.id` – volbu popsat v poznámkách.
- PrusaLink: rozšířit DTO `/api/v1/job` o `file.refs`, stažení `/thumb/s` / `/thumb/l` přes stávající `HttpClient` z `IHttpClientFactory` a Digest autentizaci, timeout, **limit velikosti odpovědi** (např. 512 KB) a kontrolu `Content-Type: image/png` a signatury PNG; 404 = bez náhledu (bez varování v logu); jiné chyby = bez náhledu s debug zprávou bez adresy s tokenem; **cache jen v paměti** podle tiskárny + `job.id` + velikosti s ETagem; náhled se stahuje jen jednou na úlohu, během tisku a jen když ho někdo chce (widget M/L viditelný); náhled poslední úlohy zůstává v cache po dokončení / zastavení až do začátku dalšího tisku (pak se uvolní); při odebrání tiskárny nebo změně adresy se zahodí.
- Žádné dekódování obrázku v Core ani v klientovi (dělá App, T-087).
- Simulátor (`tools/PrusaSimulator`): `file.refs` v `/api/v1/job`, obsluha `/thumb/s` a `/thumb/l` (malé PNG generované nebo přiložené, licenčně čisté), přepínač „bez náhledu“ (404) pro testy a ruční kontrolu; dokumentace v `docs/simulator.md`.
- `docs/api/prusalink.md`: oddíl „Náhled G-code“ se zdroji výše a ověřenými rozměry.

## Akceptační kritéria
- [x] PrusaLink.Tests s fixtures a falešným `HttpMessageHandler`: odkazy z `/api/v1/job`, stažení malého i velkého náhledu, 404, 304 s ETagem, překročený limit velikosti, špatný `Content-Type` / ne-PNG, timeout, zrušení, žádná adresa ani heslo v logu
- [x] Core.Tests: chování abstrakce a cache:
  - jedno stažení na úlohu a velikost,
  - náhled je dostupný i po dokončení nebo zastavení tisku (204 z `/api/v1/job`),
  - začátek dalšího tisku = uvolnění starého náhledu a nové stažení,
  - odebrání tiskárny a uvolnění,
  - nic se nezapisuje na disk.
- [x] PrusaSimulator.Tests: `/thumb/s`, `/thumb/l`, režim bez náhledu
- [x] Rozměry `s` / `l` ověřené ve firmwaru a zapsané v `docs/api/prusalink.md` se zdrojem
- [x] Žádný test nesahá na skutečnou síť; AOT kompatibilní
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Rozhodnutí (uživatel, 2026-10-24)
- **Po skončení tisku (Q2):** náhled poslední úlohy se drží **až do začátku dalšího tisku**, i po dokončení nebo zastavení tisku (např. během vychladnutí). `/api/v1/job` po skončení vrací 204 a odkaz už nepřijde, proto se náhled stáhne během tisku a pak se drží v paměti.
- **Cache (Q3):** **jen v paměti**, nic se neukládá na disk.
- **Zdroj (Q1):** **jen PrusaLink**; Prusa Connect je mimo rozsah (klient T-005 je odložený).

## Otevřené otázky
- Žádné.

## Poznámky k implementaci
Hotovo 2026-10-24 (core-dev).

**Volba odkazu na náhled:** `PrintJob.PreviewReference` (abstraktní `record PrintPreviewReference` v `Core/Previews`), ne pamatování podle `job.id` v klientu.
- Klient PrusaLink už `/api/v1/job` čte jednou na úlohu kvůli názvu souboru. Odkaz (`PrusaLinkPreviewReference`) přidá ke stejné odpovědi a drží ho spolu s poslední úlohou i ve stavu FINISHED/STOPPED.
- Stahovač je díky tomu bezstavový a nepotřebuje přístup ke klientovi monitoringu.
- Odkaz je neprůhledný: rozumí mu jen `PrusaLinkPreviewFetcher`. Cizí typ odkazu nebo tiskárna Prusa Connect = bez náhledu.

**Veřejné API (Core, bez HTTP a Avalonie):**
- `PrintPreviewSize` (`Small` = `/thumb/s`, `Large` = `/thumb/l`), `PrintPreviewReference`, `PrintPreview` (PNG jako `ReadOnlyMemory<byte>`, `Width`, `Height` z hlavičky `IHDR`, strany 1–2048 px, `FromPng` / `TryReadSize`).
- `IPrintPreviewSource.GetAsync(PrinterDefinition, PrintJob, PrintPreviewSize, CancellationToken)` → `Task<PrintPreview?>`.
- `IPrintPreviewFetcher.FetchAsync(printer, reference, size, etag, ct)` → `PrintPreviewFetchResult` (`Downloaded`, `NotModified`, `NotAvailable`, `Failed`).
- `PrintPreviewCache` (`IPrintPreviewSource`, `IDisposable`): `GetAsync`, `Observe(PrinterSnapshot)`, `RetainOnly(printers)`, `Remove(printerId)`, `Clear`, `Count`; `PrintPreviewCacheOptions` (`RetryDelay` 30 s, `MaxPrinters` 32).
- PrusaLink: `PrusaLinkPreviewFetcher` (+ `PrusaLinkPreviewOptions`: `MaxBytes` 512 KB, `Timeout` 10 s), registrace v `AddPrusaLink` (singleton + pojmenovaný `HttpClient` `PrusaLink.Preview`).

**Cache (jen v paměti):**
- Pro každou tiskárnu jedna úloha (id + odkaz), v ní nejvýš jeden náhled na velikost. Stahuje se líně při prvním `GetAsync` a jen jednou. Souběžné požadavky sdílí jedno stažení (`Task.Run`, nic neblokuje volajícího). Zrušení tokenu volajícího ukončí jen jeho čekání.
- Náhled zůstává po dokončení i zastavení tisku, bez úlohy (nečinná, offline tiskárna) až do další úlohy. `Observe` ho uvolní, jakmile snapshot nese jinou úlohu. Snapshot téže úlohy bez odkazu (selhaný `/api/v1/job`) náhled nechá.
- Další tisk **stejného souboru**: starý náhled zůstane jen jako validátor. První stažení pošle `If-None-Match` s ETagem a při `304` ho použije. Takto se využívá podpora ETag/304 ve firmwaru (ETag závisí na `stat` souboru).
- `404` / ne-PNG / příliš velké = bez náhledu, zapamatováno pro úlohu. Přechodná chyba = bez náhledu, další pokus nejdřív po `RetryDelay`.
- Odebraná tiskárna nebo změněná adresa či typ připojení (`RetainOnly`, `Remove`, nebo rozdíl zjištěný v `GetAsync`) se zahodí a běžící stažení se zruší. Nejvýš 32 tiskáren, nejdéle nepoužitá jde pryč.
- Výjimka stahovače se nepropustí: zaloguje se (Warning) a znamená „bez náhledu“.

**PrusaLink:**
- DTO `JobFile.Refs` (`icon`, `thumbnail`, `download`). Odkazy se ukládají bez úvodního `/` a URL kódované po segmentech (mezery, Unicode, `%`), takže fungují i pod prefixem reverzní proxy. Digest se počítá z cíle skutečně odeslaného požadavku.
- Nebezpečné odkazy (`?`, `#`, `\`, `.`/`..`, prázdný segment, řídicí znaky, osamocené surrogaty, `//host`, delší než 1024 znaků) se zahodí.
- Stažení: vlastní pojmenovaný `HttpClient` (1 spojení na tiskárnu, odděleně od pollingu, aby stažení nezdrželo polling), `ResponseHeadersRead`. Celkový timeout 10 s přes `TimeProvider` pokrývá i tělo. Limit 512 KB se hlídá při čtení: čte se nejvýš limit + 1 B. Větší `Content-Length` se odmítne bez čtení těla.
- Kontroluje se `Content-Type: image/png` a signatura + `IHDR` (`PrintPreview.FromPng`). Nic se nedekóduje.
- Digest: přihlášení se pro každé stažení dělá znovu (`DigestAuthenticator` bez cache), stojí jednu výzvu `401` navíc. Stahuje se zhruba jednou na úlohu a velikost, takže se nemusí udržovat stav přihlášení pro každou tiskárnu. API klíč klient nepoužívá (stejně jako monitoring, rozhodnutí „jen Digest“).
- Odesílání s Digest přesunuto do sdíleného `Api/DigestRequests` (používá ho i `PrusaLinkClient`, chování beze změny).
- Logy: `404` se neloguje. Ostatní chyby jsou Debug jen s id tiskárny, velikostí a důvodem (kód HTTP, `HttpRequestError`, `PrusaLinkFailure`). Adresa, cesta, heslo ani text výjimky v logu nejsou (ověřují testy).

**Rozměry `s` / `l`:** ověřeny ve firmwaru (`previews.cpp`, `gcode_reader_*.cpp`) a zapsány v `docs/api/prusalink.md`, oddíl „Náhled G-code“.
- `/thumb/s` = PNG přesně 16 × 16 px. `/thumb/l` = první PNG v souboru s oběma stranami ≥ 17 px.
- Standardní profily Prusa (bundle 2.5.13) mají `16x16/QOI, …, 380x285/PNG`. `/thumb/l` proto vrátí 380 × 285 a **`/thumb/s` vrátí 404** (16 × 16 je jen jako QOI). Pro widget M/L (T-087) je použitelný prakticky jen `Large`.
- Na skutečné tiskárně neověřeno (nahrané odpovědi nemáme).

**Výklad zadání („stahuje se jen během tisku“):** stažení spustí jen `GetAsync` (UI, když náhled ukazuje) a jen pro úlohu s odkazem. Odkaz přichází jen během tisku, po skončení ho nese dál poslední úloha. Když se widget zobrazí až po dokončení, náhled se ještě stáhne (soubor je na USB stále, jinak `404` = bez náhledu). Stav tiskárny jsem stažení neomezoval.

**Simulátor:**
- `/thumb/s` (16 × 16) a `/thumb/l` (380 × 285): generovaná PNG (kruh v barvě souboru), bez přiložených souborů a licencí. Přihlášení, `ETag`, `304`, chybové `404` a `405` jako firmware.
- Přepínač bez náhledu (`404` „File doesn't contain preview“): `--no-previews`, příkaz `<n|all> no-previews | previews`, `POST /_simulator/previews/off|on`, pole `previews` ve stavu. Počítadlo `PreviewRequestCount` pro testy.
- Simulátor dál poslouchá jen na loopbacku (výchozí `127.0.0.1`), nic se nezměnilo.

**Testy (84 nových metod, s variantami Theory víc případů):**
- Core.Tests: `PrintPreviewCacheTests` (27), `PrintPreviewTests` (9), falešný stahovač, PNG z `tests/Shared/TestPng.cs`.
- PrusaLink.Tests: `PrusaLinkPreviewFetcherTests` (24, falešný `HttpMessageHandler` s ověřením Digest), `PrusaLinkPreviewReferenceTests` (10, fixtures `job-printing` / `job-next`), registrace v DI (1).
- PrusaSimulator.Tests: `SimulatedPreviewTests` (9), `SimulatorPreviewSwitchTests` (2) a 2 testy klienta + cache proti simulátoru (jen loopback).
- „Nic na disk“: cache nemá žádnou závislost na souborech; test ověřuje, že nová instance (jako po restartu) začíná prázdná a stahuje znovu.

**Závislosti a AOT:** žádný nový balíček. Bez reflexe, JSON jen přes stávající `PrusaLinkJsonContext`, logování přes `[LoggerMessage]`. Build s analyzátory AOT (`IsAotCompatible`) bez varování. Lokální AOT publish jsem nespouštěl (projde v T-093).

**Pro T-087:** v composition rootu vytvořit `PrintPreviewCache` nad `PrusaLinkPreviewFetcher`, volat `Observe` ze `SnapshotUpdated` a `RetainOnly` po uložení nastavení, `Dispose` při ukončení.
