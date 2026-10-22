# Simulátor tiskárny a kamery (`tools/PrusaSimulator`)

Konzolový nástroj pro vývoj a testy (T-045). Simuluje jednu nebo více tiskáren s PrusaLink API a kameru Buddy3D (RTSP), takže aplikaci jde vyzkoušet bez skutečné tiskárny a kamery. Nástroj je součástí solution, ale s aplikací se nepublikuje.

## Spuštění

```bash
dotnet run --project tools/PrusaSimulator                        # 1 tiskárna (Core One, tiskne) + kamera
dotnet run --project tools/PrusaSimulator -- --printers 4 --speed 20
dotnet run --project tools/PrusaSimulator -- --printers 2 --scenario printing,offline
dotnet run --project tools/PrusaSimulator -- --help
```

Po spuštění simulátor vypíše, co zadat v aplikaci, například:

```
  Printer 1: Core One (model code 7.1.0)
    Address:   http://127.0.0.1:8080/
    User name: maker
    Password:  simulator
    Camera:    rtsp://127.0.0.1:8554/live
```

**Ctrl+C** simulátor ukončí: zavře spojení a uvolní porty. Totéž udělá příkaz `quit`.

## Volby

| Volba | Výchozí | Význam |
|---|---|---|
| `--printers <n>` | 1 | počet tiskáren (1–16) |
| `--port <n>` | 8080 | port první tiskárny, další mají +1, +2… (`0` = libovolný volný port) |
| `--models <seznam>` | `coreone,mk4s,xl,mini` dokola | modely oddělené čárkou; poslední se opakuje |
| `--scenario <seznam>` | `printing` | scénář každé tiskárny oddělený čárkou; poslední se opakuje |
| `--speed <x>` | 1 | zrychlení simulovaného času (průběh tisku, ohřev, chladnutí) |
| `--print-minutes <m>` | 30 | délka simulovaného tisku |
| `--user <jméno>` | `maker` | uživatel PrusaLink |
| `--password <text>` | `simulator` | heslo PrusaLink (přijímá se i jako `X-Api-Key`) |
| `--host <ip>` | `127.0.0.1` | adresa, na které simulátor poslouchá (i `localhost`); `0.0.0.0` nebo `::` = dostupný i z jiných počítačů v síti, ovládací endpointy pak vyžadují přihlášení (viz níže) |
| `--offline-mode <drop\|hang>` | `drop` | jak se chová offline tiskárna: `drop` spojení hned zavře, `hang` nikdy neodpoví (klient narazí na timeout) |
| `--no-camera` | – | bez kamery |
| `--camera-port <n>` | 8554 | port RTSP kamery (`0` = libovolný volný) |
| `--clip <soubor>` | nahraný úsek z Buddy3D | vlastní klip H.264 (Annex B, `.h264`) přehrávaný ve smyčce |
| `--fps <x>` | 25 | snímková frekvence kamery |

## Modely

| Klíč | Model | Kód modelu (`/api/version`, pole `printer`) |
|---|---|---|
| `coreone` | Core One | `7.1.0` |
| `mk4s` | MK4S | `1.4.1` |
| `xl` | XL | `3.1.0` |
| `mini` | MINI | `2.1.0` |

Všechny tiskárny hlásí firmware `6.9.2+10123` a trysku 0,4 mm. Aplikace podle kódu modelu vybere obrázek tiskárny (T-039).

## Scénáře

| Scénář | Co tiskárna dělá |
|---|---|
| `idle` | `IDLE`, studená, bez úlohy |
| `printing` | Nekonečný cyklus: ohřev 90 s (`PRINTING`, průběh 0 %, bez odhadu zbývajícího času) → tisk (průběh, zbývající čas, teploty u cíle, soubor) → `FINISHED` 2 min (chladne) → `IDLE` 1 min → další úloha s novým ID a jiným souborem |
| `paused` | `PAUSED`: úloha zůstane, kde byla (nebo 52 %), tryska chladne na 170 °C, podložka drží teplotu |
| `finished` | `FINISHED`: bez úlohy, tiskárna chladne |
| `error` | `ERROR`: bez úlohy, tiskárna chladne |
| `attention` | `ATTENTION` (např. došel filament): úloha zůstane, kde byla (nebo 61 %), teploty se drží |
| `offline` | tiskárna neodpovídá (pod tím scénář `idle`) |
| `flaky` | jako `printing`, ale každých 80 s na 20 s přestane odpovídat. Počítá se od přepnutí na `flaky` (60 s odpovídá, 20 s ne) ve skutečném čase, nezrychluje se. |
| `malformed` | jako `idle`, ale stav (`/api/v1/status`) přijde jako uříznutý JSON. Aplikace zaloguje varování „sent a malformed response“ i s výjimkou a stack trace (T-064: E2E kontrola logu, i v Native AOT). Verze, informace o tiskárně a přihlášení fungují normálně. |

Teploty se k cíli blíží plynule (ohřev trysky rychlejší než podložky, chladnutí pomalejší) a mírně kolísají jako skutečné čidlo. Při přepnutí scénáře teploty navazují; pozastavení a „vyžaduje pozornost“ zachovají běžící úlohu a návrat na `printing` v ní pokračuje přesně tam, kde skončila (i uprostřed ohřevu). Bez běžící úlohy ukazují výchozí úlohu s dalším číslem úlohy.

## Příkazy za běhu

Napiš do konzole a potvrď Enterem:

| Příkaz | Význam |
|---|---|
| `list` | stav všech tiskáren a kamery |
| `2 paused` | přepne tiskárnu 2 na scénář (čísla od 1) |
| `all printing` | přepne všechny tiskárny |
| `1 offline` / `1 online` | tiskárna přestane / znovu začne odpovídat; scénář mezitím běží dál |
| `camera offline` / `camera online` | kamera odpojí klienty a nepřijímá nová spojení / znovu je přijímá |
| `help`, `quit` | nápověda, ukončení |

Skripty a testy mohou tiskárnu ovládat i přes HTTP (odpovídá i offline): `GET /_simulator/state`, `POST /_simulator/scenario/<scénář>`, `POST /_simulator/offline`, `POST /_simulator/online`.

- Na loopbacku (`127.0.0.1`, `::1`, `localhost`, výchozí) jsou ovládací endpointy bez přihlášení.
- Při `--host` s jinou adresou (např. `0.0.0.0`) vyžadují stejné přihlášení jako API (Digest nebo `X-Api-Key` s heslem), aby tiskárny nemohl přepínat nikdo jiný v síti.
- Stránka `/` jen čte, a proto zůstává bez přihlášení (jako web firmwaru). Data pro ni přes API přihlášení vyžadují vždy.

## Nastavení v aplikaci

1. Ikona v trayi → **Nastavení** → **Přidat tiskárnu**.
2. **Připojení**: PrusaLink. **Adresa**: vypsaná adresa, např. `http://127.0.0.1:8080/` (stačí i `127.0.0.1:8080`).
3. **Uživatelské jméno** `maker`, **Heslo** `simulator` (nebo hodnoty z `--user` / `--password`).
4. **Otestovat spojení** ukáže „Spojení funguje. Kód modelu: 7.1.0, firmware: 6.9.2+10123.“
5. Záložka **Kamera**: zapnout kameru a jako **Adresa RTSP streamu** zadat `rtsp://127.0.0.1:8554/live` (všechny tiskárny mohou sdílet jednu kameru). **Otestovat kameru** ukáže snímek 1280 × 720.
6. Uložit. Tray ikona, menu, Diagnostika i překryv v okně kamery pak ukazují simulovaná data.

Pro víc tiskáren opakuj kroky 1–6 s dalšími adresami (`:8081`, `:8082`…).

## Co simulátor napodobuje

- **PrusaLink API** jako firmware Buddy 6.x (viz `docs/api/prusalink.md`): `GET /api/version`, `/api/v1/info`, `/api/v1/status`, `/api/v1/job` (bez úlohy `204`). Tvar JSON i formát čísel odpovídají fixture z `tests/PrusaTrayDiagnostic.PrusaLink.Tests/Fixtures/firmware`. Neznámé cesty API vrací `404`, jiné metody než GET `405` (ovládání tisku se nesimuluje).
- **Přihlášení**: HTTP Digest podle RFC 2069 (MD5, bez `qop`, realm `Printer API`), nonce platí 300 s, po vypršení přijde `401` se `stale=true`. Chyby jsou v JSON, když je `application/json` první v hlavičce `Accept`.
- **Web**: stránka `/` bez přihlášení (jako web firmwaru) průběžně ukazuje stav tiskárny. Hodí se pro „Otevřít PrusaLink“ přes proxy aplikace (T-028).
- **Kamera** jako Buddy3D: RTSP přes TCP (interleaved), jedna stopa H.264, packetization-mode 1, SPS/PPS v proudu před každým IDR snímkem, UDP odmítne (`461`). `PLAY`, `PAUSE` a `TEARDOWN` před `SETUP` vrátí `455`, s cizí nebo chybějící relací `454`. `PAUSE` zastaví snímky do dalšího `PLAY`. Klip se přehrává ve smyčce 25 snímků/s. Každá smyčka začíná SPS + PPS + IDR a čísla sekvence i časová razítka RTP pokračují bez skoku, takže dekodér smyčku nepozná. Více klientů najednou dostává každý vlastní relaci.

## Testy

- `tests/PrusaSimulator.Tests` – časový model scénářů (falešné hodiny), HTTP a Digest serveru, tvar JSON proti fixture, RTSP kamera přes vlastního jednoduchého klienta (smyčka, sekvence a časová razítka, tempo 25 snímků/s, více klientů, offline), volby příkazové řádky. Klient PrusaLink a monitor aplikace se proti simulátoru připojují beze změn (registrace `AddPrusaLink()` jako v aplikaci).
- `tests/PrusaTrayDiagnostic.Camera.FFmpeg.Tests` – kameru simulátoru používají i testy FFmpeg (dřívější `FakeRtspCamera` z T-014). `SimulatedCameraTests` přehrají přes FFmpeg víc než dvě smyčky bez výpadku, dva diváky najednou a obnovu po výpadku kamery.
- Testy používají porty přidělené systémem (`0`) a po sobě nic nenechávají.

Simulátor jde použít i přímo z testů (např. E2E, T-044): `await SimulatorHost.StartAsync(new SimulatorOptions { FirstPort = 0, CameraPort = 0, … }, ct)`, nebo jednotlivě `SimulatedPrinter.StartAsync(...)` a `new RtspCamera(...)`.
