---
id: T-183
title: Dokumentace – zabezpečení přístupu k PrusaLink (silné heslo, oddělená síť, HTTPS přes reverzní proxy)
status: todo
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
- [ ] `docs/prusalink-security.md` existuje (česky) a obsahuje: popis rizika Digest MD5, silné heslo 16+ znaků jako
      hlavní zmírnění, oddělenou síť / VLAN, HTTPS přes reverzní proxy s ukázkou Caddy a nginx, výsledek ověření HTTPS
- [ ] Každé tvrzení o PrusaLink / firmwaru má odkaz na primární zdroj (repozitář Prusa, nápověda Prusa)
- [ ] Ověřeno a zapsáno, jak se aplikace chová s `https://` a certifikátem soukromé CA v úložišti OS, se self-signed
      certifikátem a přes reverzní proxy (testy na 127.0.0.1, nebo přesunuto do T-125 / T-127 s postupem)
- [ ] Pokud self-signed certifikát nefunguje (očekávané): zapsáno jako otevřená otázka níže a navržen navazující úkol
      (důvěra / pinning certifikátu) **bez rozhodnutí o UX**
- [ ] `README.md` a `README.cz.md` odkazují na dokument ve své sekci Security / Bezpečnost, obsah obou je v souladu
- [ ] `docs/api/prusalink.md` zmiňuje HTTPS přes reverzní proxy a odkazuje na nový dokument
- [ ] `dotnet build` a `dotnet test` bez chyb a varování (jen pokud přibudou testy)

## Otevřené otázky
1. **Self-signed certifikát / vlastní důvěra.** Pokud aplikace přijímá jen certifikáty důvěryhodné pro OS (očekávané
   podle zdroje): má LayerPeek umět důvěřovat konkrétnímu certifikátu (pinning otisku v nastavení tiskárny, potvrzení
   „Důvěřovat tomuto certifikátu“ při prvním připojení, import CA jen pro aplikaci), nebo stačí návod „přidejte CA do
   úložiště OS“? Rozhoduje uživatel; implementace by byla samostatný úkol (`core-dev` + `ui-designer` + `ui-dev`).
2. **Jazyk dokumentu pro veřejnost.** `docs/**` jsou podle `CLAUDE.md` česky, ale tento návod je pro uživatele
   aplikace (anglické README). Stačí český dokument s odkazem, nebo má mít anglickou verzi (např. sekci v `README.md`
   nebo `SECURITY.md`)?
3. **Ukázkové konfigurace Caddy / nginx:** uvést je přímo v dokumentu (údržba, odpovědnost za správnost), nebo jen
   popsat princip a odkázat na dokumentaci Caddy / nginx?

## Poznámky k implementaci
(vyplní agent po dokončení)

Zdroje:
- RFC 2069 (Digest bez `cnonce` / `qop`): https://www.rfc-editor.org/rfc/rfc2069
- RFC 7616 (Digest s `qop`, `cnonce`, SHA-256 – firmware nepodporuje): https://www.rfc-editor.org/rfc/rfc7616
- Firmware (PrusaLink, Digest): https://github.com/prusa3d/Prusa-Firmware-Buddy (`lib/WUI`, ověřit konkrétní soubor)
- Caddy reverse_proxy: https://caddyserver.com/docs/caddyfile/directives/reverse_proxy
- nginx proxy_pass: https://nginx.org/en/docs/http/ngx_http_proxy_module.html
