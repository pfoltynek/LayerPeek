---
id: T-183
title: Dokumentace – zabezpečení přístupu k PrusaLink (silné heslo, oddělená síť, HTTPS přes reverzní proxy)
status: done
owner: architect + qa-reviewer
priority: P2
platform: all
depends_on: []
---

## Cíl
Bezpečnostní revize (2026-11-06, problém B, schváleno uživatelem): PrusaLink přihlašuje HTTP Digest s MD5 podle
RFC 2069 (bez `cnonce`, omezení firmwaru, `src/LayerPeek.PrusaLink/Api/DigestAuthenticator.cs`). Útočník, který
odposlechne jedinou výměnu v LAN / Wi-Fi, může heslo offline hádat hrubou silou. Aplikace to opravit nemůže; uživatel
ale může riziko výrazně snížit. Nový dokument to popíše česky a README na něj odkáže.

## Rozsah
- Nový `docs/prusalink-security.md` (česky):
  - Proč: Digest MD5 / RFC 2069 stručně, co útočník potřebuje (odposlech jedné výměny) a co získá (heslo PrusaLink,
    tedy plné ovládání tiskárny přes PrusaLink).
  - **Hlavní zmírnění: dlouhé náhodné heslo PrusaLink (16+ znaků)** ze správce hesel – offline hádání je pak
    nepraktické. Kde se heslo na tiskárně mění (menu tiskárny / PrusaLink) a že se pak změní i v LayerPeeku (Nastavení
    → Tiskárna). Odkaz na nápovědu Prusa (primární zdroj, ověřit URL).
  - **Oddělená síť pro IoT / VLAN** (host síť routeru), LayerPeek musí na tiskárnu dosáhnout (pravidlo firewallu).
  - **HTTPS přes reverzní proxy** (Caddy nebo nginx, např. na Raspberry Pi vedle tiskárny): princip (proxy ↔ tiskárna
    po krátkém kabelu / stejném segmentu, LayerPeek ↔ proxy přes TLS), ukázková minimální konfigurace Caddy a nginx
    (předávání hlavičky `Authorization`, bez ukládání do cache), poznámka, že kamera (RTSP) jde mimo proxy.
  - Výsledek ověření HTTPS níže (co funguje, co ne).
  - Varování z T-182 (pokud už je hotový; jinak T-182 odkaz doplní).
- Ověření, zda aplikace dnes funguje s `https://`:
  - Ze zdroje (zjištěno při zakládání úkolu 2026-11-06, ověřit): `PrusaLinkAddress` přijímá `http` i `https`; HTTP
    klienti v `PrusaLinkServiceCollectionExtensions` (API, web proxy T-028, náhledy G-code) používají `SocketsHttpHandler`
    bez vlastního `RemoteCertificateValidationCallback`, tedy výchozí ověření certifikátu proti úložišti důvěryhodných
    certifikátů OS; selhání TLS hlásí `PrusaLinkFailure` / `PrinterProblem` „secure connection failed“.
  - Ověřit testem nebo ručně (loopback 127.0.0.1, bez skutečné sítě): (1) certifikát vydaný soukromou CA, která je
    v úložišti OS → funguje (API, otevření webu přes proxy T-028, náhledy); (2) self-signed / CA mimo úložiště → selže
    s jasnou chybou v UI; (3) Digest přes reverzní proxy funguje (proxy předá `Authorization` i `WWW-Authenticate`).
    Testy nesmí trvale měnit úložiště certifikátů OS (pravidla `tests-leave-nothing` a E2E zásahů do systému, T-065);
    pokud bod (1) bez zásahu do úložiště ověřit nejde, přesunout do T-125 / T-127.
  - Výsledek zapsat do dokumentu a do `docs/api/prusalink.md`.
- `README.md` (anglicky, sekce „Security“) a `README.cz.md` (sekce „Bezpečnost“): jedna až dvě věty a odkaz na
  `docs/prusalink-security.md` (dokument je česky – v anglickém README to uvést, jako u `SECURITY.md` v českém).

## Akceptační kritéria
- [x] `docs/prusalink-security.md` existuje (česky) a obsahuje: popis rizika Digest MD5, silné heslo 16+ znaků jako
      hlavní zmírnění, oddělenou síť / VLAN, HTTPS přes reverzní proxy s ukázkou Caddy a nginx, výsledek ověření HTTPS
      → podle rozhodnutí 2 (2026-11-11) místo samostatného dokumentu nová kapitola 14 ve všech 8 příručkách
      `manuals/<jazyk>/manual.md`. „Silné heslo 16+ znaků“ podle firmwaru nejde (heslo si uživatel zadat nemůže, viz
      poznámky); kapitola proto popisuje heslo, které tiskárna vytváří, a kdy vytvořit nové. Výsledek ověření HTTPS je
      v `docs/api/prusalink.md` (pro vývojáře), v příručce jen chování, které uživatel uvidí.
- [x] Každé tvrzení o PrusaLink / firmwaru má odkaz na primární zdroj (repozitář Prusa, nápověda Prusa)
      → odstavec „Zdroje“ na konci kapitoly (firmware na commitu `7d8bf685`, nápověda Prusa, RFC, dokumentace Caddy,
      nginx, Apple, Microsoft).
- [x] Ověřeno a zapsáno, jak se aplikace chová s `https://` a certifikátem soukromé CA v úložišti OS, se self-signed
      certifikátem a přes reverzní proxy (testy na 127.0.0.1, nebo přesunuto do T-125 / T-127 s postupem)
      → self-signed, CA mimo úložiště a Digest přes proxy ověřeno na 127.0.0.1 (poznámky, `docs/api/prusalink.md`);
      soukromá CA v úložišti OS → přesunuto do T-125 a T-127 (vyžaduje změnu úložiště certifikátů).
- [x] Pokud self-signed certifikát nefunguje (očekávané): zapsáno jako otevřená otázka níže a navržen navazující úkol
      (důvěra / pinning certifikátu) **bez rozhodnutí o UX**
      → nefunguje (`SecureConnectionFailed`); otázka 1 rozhodnuta uživatelem 2026-11-11: stačí návod, navazující úkol
      na pinning se nezakládá.
- [x] `README.md` a `README.cz.md` odkazují na dokument ve své sekci Security / Bezpečnost, obsah obou je v souladu
      → odkazují na kapitolu 14 příručky (anglickou / českou).
- [x] `docs/api/prusalink.md` zmiňuje HTTPS přes reverzní proxy a odkazuje na nový dokument
      → nový oddíl „HTTPS přes reverzní proxy (T-183)“ s odkazem na kapitolu 14.
- [x] `dotnet build` a `dotnet test` bez chyb a varování (jen pokud přibudou testy)
      → testy nepřibyly; build a testy přesto spuštěny (viz poznámky).

## Otevřené otázky
1. **Self-signed certifikát / vlastní důvěra.** Pokud aplikace přijímá jen certifikáty důvěryhodné pro OS (očekávané
   podle zdroje): má LayerPeek umět důvěřovat konkrétnímu certifikátu (pinning otisku v nastavení tiskárny, potvrzení
   „Důvěřovat tomuto certifikátu“ při prvním připojení, import CA jen pro aplikaci), nebo stačí návod „přidejte CA do
   úložiště OS“? Rozhoduje uživatel; implementace by byla samostatný úkol (`core-dev` + `ui-designer` + `ui-dev`).
   → **Rozhodnutí uživatele 2026-11-11:** stačí návod „přidejte CA do úložiště důvěryhodných certifikátů OS“ (Windows
   a macOS). Aplikace důvěřuje jen tomu, čemu důvěřuje OS (ověřeno ve zdroji, viz poznámky). Pinning ani vlastní důvěra
   se neimplementují.
2. **Jazyk dokumentu pro veřejnost.** `docs/**` jsou podle `CLAUDE.md` česky, ale tento návod je pro uživatele
   aplikace (anglické README). Stačí český dokument s odkazem, nebo má mít anglickou verzi (např. sekci v `README.md`
   nebo `SECURITY.md`)?
   → **Rozhodnutí uživatele 2026-11-11:** žádný samostatný dokument v `docs/`, ale nová kapitola „Zabezpečení přístupu
   k PrusaLinku“ (nadpis přeložený do jazyka příručky) ve všech 8 příručkách `manuals/<jazyk>/manual.md` (zdrojem je
   `manuals/en`), s jednotným číslováním a obsahem; README odkazují na kapitolu.
3. **Ukázkové konfigurace Caddy / nginx:** uvést je přímo v dokumentu (údržba, odpovědnost za správnost), nebo jen
   popsat princip a odkázat na dokumentaci Caddy / nginx?
   → **Rozhodnutí uživatele 2026-11-11:** v kapitole jsou ukázky pro Caddy i nginx, minimální a správné. Ověřit je
   lokálně proti simulátoru (jen 127.0.0.1); co ověřit nešlo, výslovně uvést.

## Poznámky k implementaci
Hotovo 2026-11-11 (architekt, macOS 27.2.1).

**Příručky.** Nová kapitola 14 ve všech 8 jazycích, vložená před „Řešení problémů“ (to je nově 15, Odinstalace 16;
obsah, nadpisy a všechny odkazy `#14-…` / `#15-…` přečíslované, odkazy v příručkách zkontrolované podle pravidel
slugů GitHubu). Podkapitoly 14.1 riziko, 14.2 heslo, 14.3 oddělená síť a firewall, 14.4 HTTPS přes reverzní proxy
(Caddy, nginx, recept OpenSSL na vlastní CA), 14.5 důvěra v certifikát ve Windows a macOS, na konci zdroje. Do „Řešení
problémů“ přibyl bod „Zabezpečené spojení (https) selhalo“ s odkazem na 14.5. Texty chyb v příručkách jsou přesně
podle `Strings.*.resx`. Kotvy pro T-182:

| Jazyk | Nadpis | Kotva |
|---|---|---|
| en | 14. Securing access to PrusaLink | `#14-securing-access-to-prusalink` |
| cs | 14. Zabezpečení přístupu k PrusaLinku | `#14-zabezpečení-přístupu-k-prusalinku` |
| sk | 14. Zabezpečenie prístupu k PrusaLinku | `#14-zabezpečenie-prístupu-k-prusalinku` |
| de | 14. Zugriff auf PrusaLink absichern | `#14-zugriff-auf-prusalink-absichern` |
| es | 14. Proteger el acceso a PrusaLink | `#14-proteger-el-acceso-a-prusalink` |
| fr | 14. Accès sécurisé à PrusaLink | `#14-accès-sécurisé-à-prusalink` |
| it | 14. Accesso sicuro a PrusaLink | `#14-accesso-sicuro-a-prusalink` |
| pl | 14. Zabezpieczenie dostępu do PrusaLink | `#14-zabezpieczenie-dostępu-do-prusalink` |

Nadpisy fr a it jsou zvolené bez apostrofu, aby kotva nezávisela na tom, jak GitHub zachází s `'` / `’`.

**Zjištění z firmwaru (Buddy, `master` = `7d8bf685`, 2026-11-11), odchylka od rozsahu úkolu:**
- Heslo vytváří tiskárna (`lib/WUI/wui.cpp`, `wui_generate_password`, `rand_u_secure`): 15 znaků ze 57 (bez 0, O, 1, l,
  I), buffer `pl_password_size = 16` včetně koncové nuly (`config_store/constants.hpp`), tedy asi 87 bitů. Vlastní heslo
  zadat nejde: nabídka PrusaLink (`src/gui/screen_prusa_link.cpp`) má jen „Generate Password“, `prusa_printer_settings.ini`
  heslo nenastavuje (`doc/prusa_printer_settings.ini`, `src/common/ini_handler.cpp`) a jediný zápis hesla je
  `wui_store_password`. Doporučení „16+ znaků ze správce hesel“ z rozsahu proto nejde použít; offline hádání ze
  zachycené výměny je u vygenerovaného hesla nepraktické. Kapitola místo toho radí vytvořit nové heslo, když mohlo
  uniknout (QR kód nabídky obsahuje `http://maker:<heslo>@<ip>/`).
- `X-Api-Key` = samotné heslo (`req_parser.cpp`, `api_key = server->get_password()`), posílá se čitelně; PrusaSlicer
  má volbu HTTP digest (nápověda Prusa, PrusaLink Troubleshooting).
- Server jen TCP 80 bez TLS (`lib/WUI/http_lifetime.cpp`). Nonce platí 300 s (`src/common/http/types.h`), `nc` se
  nekontroluje → zachycený požadavek jde do 5 minut zopakovat pro stejnou metodu a URI.
- Prusa: PrusaLink je pro místní síť (článek „Prusa Connect and PrusaLink explained“), nedoporučuje přesměrování portu,
  doporučuje šifrovanou VPN (článek PrusaLink SL1/SL1S; jiný primární zdroj pro tiskárny Buddy jsem nenašel).

**Chování aplikace s `https://` (zdroj):** `PrusaLinkAddress` přijímá `http` i `https`. Všechny tři `SocketsHttpHandler`
v `PrusaLinkServiceCollectionExtensions` (API, web proxy T-028, náhledy) nemají `SslOptions` ani
`RemoteCertificateValidationCallback`, certifikát se tedy ověřuje výchozím způsobem .NET proti úložišti OS. Chyba TLS →
`PrusaLinkFailure.SecureConnectionFailed` / `PrinterProblem.SecureConnectionFailed` (`PrusaLinkClient.cs`).

**Ověření na 127.0.0.1 (podrobně v `docs/api/prusalink.md`, oddíl „HTTPS přes reverzní proxy (T-183)“):** simulátor
(port 18080), Caddy 2.11.9 (binárka z GitHub Releases, SHA-512 ověřen) a nginx 1.30.6 (přeložený ze zdrojů z nginx.org,
PGP podpis neověřen, `gpg` chybí), vše ve scratch adresáři, bez `sudo`, bez instalace a beze změny úložiště
certifikátů (Caddy se `skip_install_trust`). Konfigurace = ukázky z příručky, jen adresy na loopback. Certifikát pro
nginx podle receptu z příručky (OpenSSL 4.1 i LibreSSL 3.3.6). Výsledky:
- curl s `--cacert` přes obě proxy: Digest výzva i přihlášení projdou (`/api/version`, `/api/v1/status`,
  `/thumb/l/...`), `PUT` 2 MB s Digest projde (simulátor pak `405`); nginx bez `client_max_body_size 0` vrátí `413`.
- Klient aplikace (`AddPrusaLink()` beze změny, dočasná konzolová aplikace ve scratch adresáři): přes HTTP proxy Caddy
  i nginx funguje info, stav, náhled G-code i web proxy T-028; přes HTTPS s CA mimo úložiště (Caddy `tls internal`,
  vlastní CA) i se self-signed certifikátem `SecureConnectionFailed`, stav `Offline`, web proxy `NotReachable`; proxy
  s odříznutým prefixem cesty (`handle_path`) `PasswordRejected`.
- Neověřeno: soukromá CA v úložišti OS (→ T-125 bod 2y, T-127 bod 1g, včetně kontroly lokalizovaných názvů položek
  Windows / macOS v příručkách – jsou psané podle znalosti, na lokalizovaném systému nekontrolované), skutečná tiskárna,
  Raspberry Pi a skutečné nahrání souboru přes proxy.

**Build a testy:** testy nepřibyly. `dotnet build` 0 varování, 0 chyb; `dotnet test`: 10 347 celkem, 0 selhání,
10 080 prošlo, zbytek přeskočen (testy vázané na OS / Edge / E2E).

**T-182:** varování v nastavení tiskárny odkáže na kapitolu 14 v jazyce UI (kotvy výše); ten odkaz doplní T-182.

Zdroje:
- RFC 2069 (Digest bez `cnonce` / `qop`): https://www.rfc-editor.org/rfc/rfc2069
- RFC 7616 (Digest s `qop`, `cnonce`, SHA-256 – firmware nepodporuje): https://www.rfc-editor.org/rfc/rfc7616
- Firmware: https://github.com/prusa3d/Prusa-Firmware-Buddy/tree/7d8bf6858b363c453c0617bceab5e66997d8fc25 –
  `lib/WUI/http_lifetime.cpp`, `lib/WUI/nhttp/req_parser.cpp`, `lib/WUI/wui.cpp`, `src/common/http/types.h`,
  `src/gui/screen_prusa_link.cpp`, `src/persistent_stores/store_instances/config_store/constants.hpp`,
  `doc/prusa_printer_settings.ini`, `src/lang/po/<jazyk>/Prusa-Firmware-Buddy_<jazyk>.po` (překlady „Generate Password“)
- Nápověda Prusa: https://help.prusa3d.com/article/prusa-connect-and-prusalink-explained_302608,
  https://help.prusa3d.com/article/prusalink-sl1-sl1s_146094, https://help.prusa3d.com/article/prusalink-troubleshooting_304411
- Caddy: https://caddyserver.com/docs/caddyfile/directives/reverse_proxy, https://caddyserver.com/docs/caddyfile/directives/tls,
  https://caddyserver.com/docs/running, https://caddyserver.com/docs/automatic-https
- nginx: https://nginx.org/en/docs/http/ngx_http_proxy_module.html, https://nginx.org/en/docs/http/ngx_http_core_module.html
- Apple: https://support.apple.com/en-us/103769,
  https://support.apple.com/guide/keychain-access/change-the-trust-settings-of-a-certificate-kyca11871/mac,
  https://support.apple.com/guide/keychain-access/add-certificates-to-a-keychain-kyca2431/mac
- Microsoft: https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/certutil,
  https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/certificate-procedures
