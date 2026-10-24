---
id: T-086
title: Náhled G-code – načtení z PrusaLink (Core abstrakce + klient + simulátor)
status: todo
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
- [ ] PrusaLink.Tests s fixtures a falešným `HttpMessageHandler`: odkazy z `/api/v1/job`, stažení malého i velkého náhledu, 404, 304 s ETagem, překročený limit velikosti, špatný `Content-Type` / ne-PNG, timeout, zrušení, žádná adresa ani heslo v logu
- [ ] Core.Tests: chování abstrakce a cache:
  - jedno stažení na úlohu a velikost,
  - náhled je dostupný i po dokončení nebo zastavení tisku (204 z `/api/v1/job`),
  - začátek dalšího tisku = uvolnění starého náhledu a nové stažení,
  - odebrání tiskárny a uvolnění,
  - nic se nezapisuje na disk.
- [ ] PrusaSimulator.Tests: `/thumb/s`, `/thumb/l`, režim bez náhledu
- [ ] Rozměry `s` / `l` ověřené ve firmwaru a zapsané v `docs/api/prusalink.md` se zdrojem
- [ ] Žádný test nesahá na skutečnou síť; AOT kompatibilní
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Rozhodnutí (uživatel, 2026-10-24)
- **Po skončení tisku (Q2):** náhled poslední úlohy se drží **až do začátku dalšího tisku**, i po dokončení nebo zastavení tisku (např. během vychladnutí). `/api/v1/job` po skončení vrací 204 a odkaz už nepřijde, proto se náhled stáhne během tisku a pak se drží v paměti.
- **Cache (Q3):** **jen v paměti**, nic se neukládá na disk.
- **Zdroj (Q1):** **jen PrusaLink**; Prusa Connect je mimo rozsah (klient T-005 je odložený).

## Otevřené otázky
- Žádné.

## Poznámky k implementaci
(vyplní agent po dokončení)
