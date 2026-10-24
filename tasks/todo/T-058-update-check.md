---
id: T-058
title: Kontrola nové verze v aplikaci (GitHub Releases)
status: todo
owner: core-dev + ui-dev
priority: P3
platform: all
depends_on: [T-008, T-029, T-055]
---

## Cíl
Aplikace na Windows i macOS sama zjistí, že na GitHub Releases (`github.com/pfoltynek/LayerPeek`) je novější verze, a nabídne ji uživateli. Funguje nezávisle na instalátoru – i pro `.zip` (Windows) a `.tar.gz` / `.dmg` s ad-hoc podpisem (macOS, T-019), kde automatické aktualizace nejsou.

Vztah k Velopacku (T-056, T-017): pokud aplikace běží z instalace Velopacku na Windows, může kontrolu a samotnou aktualizaci převzít Velopack (`UpdateManager` s `GithubSource`). Tento úkol pak buď kontrolu předá Velopacku, nebo jen nabídne „Aktualizovat“ místo otevření stránky vydání – podle doporučení T-056 a odpovědi na otázku níže. Bez Velopacku (archivy, macOS) klik otevře stránku vydání v prohlížeči.

> **Omezení:** kontrola bez tokenu funguje až po zveřejnění repozitáře (rozhodnutí uživatele 2026-10-21: repozitář bude později veřejný, token se do aplikace neukládá). Dokud je repozitář soukromý, API vrátí 404 – aplikace to musí snést tiše (jen záznam v logu, žádná chyba pro uživatele).

## Rozsah
- **Core** (bez HTTP specifik): abstrakce `IUpdateChecker` (nejnovější vydání: verze, URL stránky vydání, příznak předběžného vydání), porovnání verzí SemVer 2 (tagy `vX.Y.Z`, předběžné přípony) a služba, která kontrolu spouští periodicky a událostí hlásí novou verzi. Čas a časovač přes abstrakci (testy bez čekání).
- **Implementace v App** (HTTP specifika do Core nepatří): GitHub REST API (https://docs.github.com/en/rest/releases/releases).
  - `GET /repos/pfoltynek/LayerPeek/releases/latest` vrací jen nejnovější zveřejněné vydání, které **není** koncept ani předběžné. Protože všechna vydání `0.x` jsou předběžná (`--prerelease`, T-055), tento endpoint by nic nenašel → pro předběžná vydání je potřeba `GET /repos/{owner}/{repo}/releases` (seznam) a výběr podle verze. Volba podle odpovědi na otázku o předběžných verzích.
  - `HttpClient` přes `IHttpClientFactory`, hlavička `User-Agent` (GitHub ji vyžaduje), `Accept: application/vnd.github+json`, timeout, bez autentizace (limit 60 dotazů za hodinu na IP adresu). JSON jen přes source-generated `JsonSerializerContext`.
  - Chyby sítě, 403 (limit), 404 (soukromý repozitář) a neplatná data se jen zalogují.
- **Verze aplikace** z `<Version>` v kořenovém `Directory.Build.props` (jediný zdroj, T-055), stejná jako v okně O aplikaci.
- **UI:** položka v tray menu „Je dostupná nová verze X.Y.Z“ (jen když existuje) a jednorázová notifikace pro každou novou verzi přes `INotificationService` (na macOS až s T-020). Texty přes lokalizaci z T-029 (všechny podporované jazyky).
- Zapamatování verze, pro kterou už notifikace proběhla (v nastavení, ne v tajných údajích).

## Akceptační kritéria
- [ ] Unit testy porovnání verzí (předběžné přípony, prefix `v`, neplatné tagy, `0.x`)
- [ ] Testy klienta GitHub API s nahranými JSON fixtures a falešným `HttpMessageHandler` (žádná reálná síť), včetně 403, 404, timeoutu a neplatného JSON
- [ ] Testy služby: nová verze → událost + jedna notifikace; stejná / starší verze → nic; opakovaná kontrola stejné verze → žádná další notifikace
- [ ] Headless UI test: položka menu se zobrazí jen při dostupné nové verzi
- [ ] Klik na položku / notifikaci otevře stránku vydání, případně spustí aktualizaci přes Velopack (ověřeno ručně)
- [ ] Žádná nová NuGet závislost, nebo ověřená AOT/trim kompatibilita; AOT publish `win-x64` bez varování
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Jak často kontrolovat (např. při startu a pak jednou za 24 h)?
- Jde kontrola vypnout v nastavení (opt-out)? Je ve výchozím stavu zapnutá?
- Předběžná vydání: všechna `0.x` jsou předběžná – nabízet předběžné verze vždy, jen dokud běží verze `0.x`, nebo podle volby v nastavení?
- Má být v tray menu (nebo v okně O aplikaci) ruční položka „Zkontrolovat aktualizace“?
- V instalaci přes Velopack: má kontrolu úplně převzít Velopack (a tento úkol se pro ni vypne), nebo zůstane společná kontrola a Velopack jen provede aktualizaci po kliknutí?

## Poznámky k implementaci
(vyplní agent po dokončení)
