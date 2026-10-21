---
id: T-045
title: Simulátor tiskárny (PrusaLink) a kamery (RTSP) pro vývoj a testy
status: in-progress
owner: core-dev
priority: P2
platform: all
depends_on: [T-003, T-014]
---

## Cíl
Aplikaci jde vyzkoušet bez skutečné tiskárny a kamery. Samostatný nástroj simuluje PrusaLink API tiskárny (stav, teploty, průběh tisku v čase) a RTSP kameru (smyčka nahraného úseku z Buddy3D). Požadavek uživatele 2026-10-21; nástroj využijí i E2E testy (T-044).

## Rozsah
- Konzolová aplikace `tools/PrusaSimulator` (.NET, součást solution, bez varování), spuštění `dotnet run --project tools/PrusaSimulator -- [volby]`.
- **Tiskárna (PrusaLink):** HTTP server na zvoleném portu se stejnými endpointy, jaké používá klient (`/api/version` vč. kódu modelu, `/api/v1/info`, `/api/v1/status`, `/api/v1/job` …; ověřit v `src/PrusaTrayDiagnostic.PrusaLink`), stejná autentizace (API klíč / Digest – podle klienta), odpovědi odvozené z nahraných fixture.
  - Scénáře: nečinná, tisk (průběh, zbývající čas, teploty trysky a podložky se mění v čase, soubor), pozastavená, dokončená, chyba / vyžaduje pozornost, offline (server přestane odpovídat); volitelně zrychlený čas.
  - Více tiskáren najednou (různé porty, různé modely – kód modelu pro T-039).
  - Případně jednoduchá webová stránka pro „Otevřít PrusaLink v prohlížeči“ (T-028 proxy).
- **Kamera (RTSP):** RTSP server (TCP, H.264) přehrávající ve smyčce `buddy3d.h264` (fixture z T-014); volitelně vlastní delší klip (soubor .h264 Annex B). Znovupoužít falešnou RTSP kameru z testů T-014 (`FakeRtspCamera`) – přesunout do sdíleného kódu, ne kopírovat.
- Výpis adres po spuštění (např. `http://127.0.0.1:8080` + API klíč, `rtsp://127.0.0.1:8554/live`), Ctrl+C ukončí a uklidí.
- Dokumentace v `docs/` (česky): jak simulátor spustit a nastavit v aplikaci.

## Rozhodnuto
- **Rozsah (2026-10-21):** kamera i tiskárna.
- **Video (2026-10-21):** nahraný úsek z Buddy3D ve smyčce (+ volitelně vlastní klip).
- **Spouštění (2026-10-21):** samostatný nástroj v `tools/`; použijí ho i E2E testy.
- **Dokončení (2026-10-21):** úkol zůstává rozpracovaný, dokud ho neověří E2E testy (T-044) nebo ruční test v GUI proti simulátoru.
- **Ovládací endpointy (2026-10-21):** `/_simulator/*` (a cokoli, co mění stav) vyžadují stejný Digest jako API, pokud simulátor poslouchá na jiné než loopback adrese (`--host` jiná než 127.0.0.1 / ::1 / localhost). Na loopbacku zůstávají bez přihlášení.

## Akceptační kritéria
- [x] Aplikace se k simulované tiskárně připojí (test spojení, monitoring, Diagnostika, překryv) bez úprav aplikace
- [x] Okno kamery přehrává simulovanou kameru plynule ve smyčce (bez výpadku na konci smyčky)
- [x] Scénáře tiskárny včetně offline a více tiskáren
- [x] Testy simulátoru (klient PrusaLink a FFmpeg feed proti simulátoru, smyčka, scénáře); testy po sobě nic nenechají
- [x] Dokumentace spuštění
- [x] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish aplikace win-x64 bez varování (simulátor se nepublikuje s aplikací)
- [ ] Ověřeno E2E testy (T-044) nebo ručně v GUI proti simulátoru

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- **Projekt** `tools/PrusaSimulator` (konzole, net11, ve slnx pod `/tools/`, bez NuGet balíčků). Kestrel ze sdíleného frameworku ASP.NET Core (`FrameworkReference`, stejně jako proxy T-028), RTSP nad `TcpListener`. Simulátor nereferencuje kód aplikace: tvar API napodobuje podle fixture firmwaru. Leží mimo `src/`, takže nemá `IsAotCompatible` a s aplikací se nepublikuje. Návod: `docs/simulator.md`, zmínka v `docs/architecture.md`.
- **PrusaLink**: `/api/version` (kód modelu), `/api/v1/info`, `/api/v1/status`, `/api/v1/job` (bez úlohy `204`). JSON se píše ručně, aby pořadí polí a formát čísel přesně odpovídaly fixture (`progress` na 2 desetinná místa, teploty na 1, `axis_x`/`axis_y` jen mimo tisk). Přihlášení stejně jako firmware a klient (T-027): Digest RFC 2069 (MD5, bez `qop`, realm `Printer API`), nonce platí 300 s a pak `stale=true`. Přijímá se i `X-Api-Key` s heslem (firmware to umí, klient ho nepoužívá). Stránka `/` je bez přihlášení (pro proxy T-028). Ovládací endpointy `/_simulator/…` slouží skriptům a E2E testům.
- **Scénáře** (`PrinterSimulation`, čistá funkce času přes `TimeProvider`): `idle`, `printing` (nekonečný cyklus ohřev → tisk → `FINISHED` → `IDLE` → další úloha), `paused`, `finished`, `error`, `attention`, `offline`, `flaky`. Teploty se mění jako článek 1. řádu a při přepnutí navazují. Pauza a „vyžaduje pozornost“ drží běžící úlohu a návrat na tisk v ní pokračuje. `--speed` zrychluje simulovaný čas, výpadky `flaky` jdou ve skutečném čase. Offline je přepínač nezávislý na scénáři: `drop` zavře spojení, `hang` neodpoví.
- **Modely**: Core One `7.1.0`, MK4S `1.4.1`, XL `3.1.0`, MINI `2.1.0`, každá tiskárna na vlastním portu (`--port` +1, +2…).
- **Kamera**: `FakeRtspCamera` z T-014 je nahrazená sdílenou `RtspCamera` v simulátoru. Testy FFmpeg na simulátor odkazují (`ProjectReference`), kopie nevznikla. `H264Clip` rozdělí Annex B na snímky a smyčka začíná prvním IDR snímkem. SPS/PPS se tam doplní, pokud je klip má jinde. Čísla sekvence i časová razítka RTP rostou přes hranice smyčky, tempo je 25 fps (`--fps`). Každý klient má vlastní relaci, keep-alive požadavky se vyřizují i během streamu, UDP se odmítá `461`. Kamera umí výpadek (`camera offline`). Výchozí klip `buddy3d.h264` je do výstupu simulátoru jen nalinkovaný z fixture T-014 (`Clips/`), vlastní klip jde zadat přes `--clip`.
- **„Aplikace se připojí“** se ověřuje testy, GUI se nespouštělo. Klient PrusaLink je registrovaný přes `AddPrusaLink()` jako v aplikaci. Testy pokrývají test spojení (`GetInfoAsync`, kód modelu pro všechny 4 modely), `DetectModelAsync`, snapshoty pro Diagnostiku a překryv (stav, soubor, průběh, zbývající čas, teploty, ventilátory), přechody scénářů, špatné heslo, obnovu nonce, offline a obnovu, timeout. Navíc běží `PrinterMonitor` se dvěma tiskárnami a výpadkem. Okno kamery: přes `ReconnectingCameraFeed` + FFmpeg (stejný řetězec jako okno) projde víc než 2 smyčky na jedno spojení bez „no signal“ a bez mezery nad 500 ms. Dále dva diváci najednou a obnova po výpadku kamery. Vizuální kontrola v GUI je doporučená, ale není nutná.
- **Úpravy po review (2026-10-21)**:
  - Ovládací endpointy mimo loopback vyžadují přihlášení (`SimulatedPrinterOptions.ControlRequiresLogin`, automaticky podle `--host`). `--host` přijímá i `localhost`.
  - Stavová stránka `/` jen čte, a proto zůstává otevřená.
  - RTSP při `--host ::` poslouchá i na IPv4 (`DualMode`).
  - Výchozí úloha pozastavené tiskárny bere další číslo úlohy.
  - Pauza během ohřevu po návratu pokračuje v ohřevu.
  - Výpadky `flaky` se počítají od přepnutí.
  - RTSP: `455` bez `SETUP`, `454` pro cizí nebo chybějící relaci, `PAUSE` opravdu pozastaví; přerostlý interleaved paket se přeskočí.
  - Test tempa snímků běží deterministicky na falešných hodinách.
  - README: řádek s nástrojem.
- **Testy**: nový projekt `tests/PrusaSimulator.Tests` (113 testů) a `SimulatedCameraTests` v testech FFmpeg (3). Používají porty od systému (`0`), falešné hodiny a v pracovním stromu nic nenechávají.
- **Ověření**: `dotnet build` 0 varování. `dotnet test` dvakrát po sobě 4219 testů, 0 chyb (1 přeskočený je původní test v PrusaLink.Tests). `dotnet publish src/PrusaTrayDiagnostic.App -c Release -r win-x64` (AOT) 0 varování.
