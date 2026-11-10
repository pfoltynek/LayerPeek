---
id: T-181
title: Podepsaný feed aktualizací (podpis releases.<RID>.json offline klíčem, ověření v aplikaci)
status: in-progress
owner: core-dev
priority: P2
platform: all
depends_on: []
---

## Cíl
Bezpečnostní revize (2026-11-06, problém A, schváleno uživatelem): Velopack ověřuje stažený balíček jen proti hashi ve
feedu `releases.<RID>.json`, a ten je ve stejném vydání na GitHubu jako balíček. Při převzetí účtu GitHub nebo úniku
tokenu jde vydat škodlivou aktualizaci, kterou nainstalované aplikace přijmou. Feed proto podepíše offline soukromý klíč
(nikdy na GitHubu ani v repozitáři) a aplikace aktualizaci odmítne, když podpis chybí nebo neodpovídá veřejnému klíči
zabudovanému v binárce. Hashe balíčků dál ověřuje Velopack proti (teď už ověřenému) feedu.

Dnes: `src/LayerPeek.App/Installer/VelopackUpdateEngine.cs` používá
`new GithubSource(AppUpdates.RepositoryUrl, accessToken: null, prerelease: …)`, v testovacím režimu
`SimpleFileSource` / `SimpleWebSource` (`UpdateSource.TryCreateForTest`, T-058). Feed vzniká v `release-build.ps1`
(`vpk pack`, `Copy-VelopackOutput`) a nahrává se `release-upload.ps1` (`docs/release.md`).

## Rozsah

### Průzkum (první krok, výsledky do `docs/velopack.md`)
- Ve zdroji Velopack 1.2.161 (verze v `Directory.Packages.props`) ověřit:
  - která pole s hashem obsahuje `releases.<RID>.json` (`SHA1`, `SHA256`, `Size`) a které z nich klient při
    `DownloadUpdatesAsync` skutečně kontroluje (a zda kontroluje i velikost), u full i delta balíčků (dnes
    `--delta None`);
  - jak `GithubSource` / `GitBase` stahuje feed (`GetReleaseFeed`) a balíček (`DownloadReleaseEntry`) a jaké body
    rozšíření nabízí (`IUpdateSource`, virtuální metody), aby šlo ověřit **přesné bajty** staženého feedu, ne až
    deserializovaný objekt;
  - zda Velopack sám nějaký podpis feedu nepodporuje (pak by se použil místo vlastního řešení – rozhodne uživatel);
  - zda `UpdateExe` / `UpdateMac` při aplikaci aktualizace feed nebo balíček stahuje znovu (pak by ověření v aplikaci
    nestačilo).
- (Ed25519 se neřeší, algoritmus je rozhodnutý: ECDSA P-256.)

### Podpis (skripty vydání, `qa-reviewer` může převzít tuto část)
- Nový krok v `release-build.ps1` (rozhodnuto 2026-11-06: podepisuje se na každém stroji vydání, offline USB s klíčem
  se připojí na Windows i na Macu) podepíše každý `releases.<RID>.json` a
  vytvoří `releases.<RID>.json.sig` v `dist/`; `.sig` patří do `SHA256SUMS-<os>.txt` a `build-info.json`.
- Podepisovaná data obsahují kromě bajtů feedu i doménový prefix a kanál (např. `LayerPeek-feed-v1\n<RID>\n` +
  bajty), aby podpis feedu jednoho RID nešel použít pro jiný RID ani pro jiný účel.
- Soukromý klíč se načte z cesty zadané parametrem / proměnnou prostředí (offline médium), chráněný heslem
  (šifrovaný PEM / PKCS#8 na offline USB, rozhodnuto 2026-11-06); skript ho nikdy nekopíruje, nevypisuje ani neukládá
  do `artifacts/`. Bez klíče build vydání skončí chybou.
- Zkušební běhy (rozhodnuto 2026-11-06): přepínač `-SkipFeedSignature` dovolí build bez podpisu; `release-upload.ps1`
  takový build (feed bez `.sig`, příznak v `build-info.json`) odmítne.
- `release-upload.ps1` nahraje `releases.<RID>.json.sig` **před** `releases.<RID>.json`; `release-publish.ps1`
  odmítne vydání, kde u feedu RID chybí `.sig` (nový řádek v tabulkách „Co skripty odmítnou“). `-AddToPublished` se
  ruší v T-180 (immutable releases), takže ho tento úkol neřeší.
- Pester testy: podpis a ověření v PowerShellu dávají stejný výsledek jako C# ověření (sdílený testovací vektor).

### Ověření v aplikaci (`core-dev`)
- Vlastní `IUpdateSource` (např. `SignedFeedUpdateSource`) obalí `GithubSource` (i testovací
  zdroje): stáhne feed i `releases.<RID>.json.sig`, ověří podpis proti veřejnému klíči zabudovanému v binárce a teprve
  potom vrátí feed Velopacku. Chybějící, poškozený nebo neplatný podpis = aktualizace se nenabídne (žádná instalace,
  žádný pokus o stažení balíčku).
- Logika ověření (formát podpisu, prefix, seznam důvěryhodných klíčů) je čistý kód bez Velopacku testovatelný v
  `LayerPeek.App.Tests` (nebo v Core, pokud to vrstvení dovolí – Core nesmí znát Velopack ani HTTP).
- Veřejný klíč (nebo seznam klíčů kvůli rotaci) je konstanta ve zdroji (PEM / SubjectPublicKeyInfo), žádné načítání ze
  souboru ani z nastavení.
- Testovací režim (rozhodnuto 2026-11-06, varianta a): testovací pár klíčů, soukromý v `tests/` (není tajný). Testovací
  veřejný klíč přijímá jen zdroj z `UpdateSource.TryCreateForTest`; pro GitHub feed se nikdy nepřijme (zajistit
  konstrukcí, ne podmínkou za běhu, a otestovat).
- Odmítnutí se zaloguje (bez obsahu klíče) a uživatel uvidí (rozhodnuto 2026-11-06) **chybu na záložce „Spuštění a
  aktualizace“ i notifikaci**; nové texty lokalizované ve všech `Strings.*.resx`. Notifikace se nesmí opakovat při
  každé kontrole aktualizací (jak často přesně – navrhnout a potvrdit s uživatelem při implementaci).
- Bez reflexe, AOT-safe (`System.Security.Cryptography`), žádný nový balíček.

### Klíč, záloha a rotace (dokumentace `docs/release.md`, oddíl „Podpis feedu aktualizací“)
- Vytvoření páru klíčů (příkaz v PowerShellu), uložení soukromého klíče offline (šifrovaný PEM na USB,
  rozhodnuto 2026-11-06) a **záloha** na druhém offline médiu na jiném místě. Ztráta klíče = nainstalované verze už
  aktualizaci nepřijmou a uživatelé musí nainstalovat ručně.
- **Rotace:** aplikace má seznam důvěryhodných veřejných klíčů; nový klíč se přidá do verze N, která je ještě podepsaná
  starým klíčem; verze N+1 a další se podepisují novým klíčem; starý klíč se ze seznamu odebere až v pozdější verzi.
  Při **kompromitaci** soukromého klíče bezpečná rotace aktualizací nejde (útočník může podepsat i „rotaci“) –
  postup: nový klíč, nová verze, oznámení v README / vydání, ruční přeinstalace.
- Podpis jen z čistého checkoutu tagu, na stroji vydání; klíč se po podpisu odpojí.

### Rozsah ochrany (zapsat do `docs/release.md` a `docs/velopack.md`)
- Chrání jen **aktualizace přes Velopack**. První stažení instalátoru (`Setup.exe`, `.dmg`, `.zip`, `.tar.gz`) z
  GitHubu podpis nemá – tam pomáhá jen T-180 a ruční kontrola `SHA256SUMS-*.txt` (které jsou ve stejném vydání).
- Aplikace mimo Velopack (`GitHubReleaseChecker`, T-058) jen otevře stránku vydání; nic neinstaluje.
- Už nainstalované verze bez ověření (do verze s tímto úkolem včetně) přijmou i nepodepsanou aktualizaci; ochranu mají
  až po aktualizaci na verzi s ověřením. Od prvního vydání s ověřením musí mít každé vydání `.sig` (jinak by ho verze
  s ověřením odmítly).
- Opětovné nahrání staršího podepsaného feedu (replay) nepovede k instalaci starší verze (Velopack instaluje jen vyšší
  verzi), jen k „zamrznutí“ aktualizací – přijatelné zbytkové riziko, zapsat.

## Akceptační kritéria
- [x] `docs/velopack.md`: výsledek průzkumu (hash pole ve feedu, co klient skutečně kontroluje, body rozšíření
      `GithubSource`, zda updater stahuje znovu) s odkazy na konkrétní soubory zdroje Velopack 1.2.161
- [x] Testy ověření (unit, bez sítě): platný podpis → feed přijat; feed změněný o jeden bajt → odmítnut; chybějící
      `.sig` → odmítnut; `.sig` podepsaný jiným klíčem → odmítnut; podpis feedu jiného RID → odmítnut; poškozený /
      prázdný / příliš dlouhý `.sig` → odmítnut bez výjimky ven
- [x] Test `VelopackUpdateEngine` / zdroje (fake zdroj nebo loopback server 127.0.0.1): při neplatném podpisu
      `CheckAsync` nevrátí aktualizaci a balíček se nestáhne
- [ ] E2E test aktualizace (T-128, testovací feed) projde s feedem podepsaným testovacím klíčem a
      selže s nepodepsaným
- [ ] `release-build.ps1` vytvoří `releases.<RID>.json.sig` pro každý RID, bez klíče skončí chybou; soukromý klíč se
      neobjeví v `artifacts/`, logu ani výpisu (Pester test)
- [ ] `release-upload.ps1` nahraje `.sig` před feedem a odmítne build s `-SkipFeedSignature`; `release-publish.ps1`
      odmítne vydání bez `.sig` u feedu (Pester testy)
- [ ] Při odmítnutém podpisu: chyba na záložce „Spuštění a aktualizace“ a notifikace, která se neopakuje při každé
      kontrole (UI / unit testy); testovací klíč pro GitHub feed nikdy neplatí (test)
- [ ] Podpis z PowerShellu ověří C# kód (sdílený testovací vektor v obou sadách testů)
- [ ] `docs/release.md`: vytvoření klíče, offline uložení a záloha, rotace, kompromitace, rozsah ochrany;
      `docs/architecture.md`: nový zdroj aktualizací a kde leží veřejný klíč
- [ ] `CHANGELOG.md` (`[Unreleased]`, `### Security`): aktualizace jsou ověřované podpisem
- [ ] Žádný nový NuGet balíček; žádné nové trim/AOT varování
- [ ] Zkušební vydání (`docs/release.md`, „Zkušební vydání“): podepsaný feed nahraný, nainstalovaná verze s ověřením
      aktualizaci přijme → přesunout do T-125 / T-127, pokud nejde ověřit v úkolu
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
Rozhodnuto uživatelem 2026-11-06:
1. Algoritmus: **ECDSA P-256 (SHA-256)** přes vestavěné `System.Security.Cryptography.ECDsa` (žádná nová závislost).
   Pokud průzkum zjistí, že Velopack podpis feedu podporuje sám, nejdřív se zeptat uživatele.
2. Soukromý klíč: **šifrovaný PEM na offline USB**; záloha na druhém offline médiu uloženém jinde.
3. Podpis: **(a)** na každém stroji vydání v `release-build.ps1` (USB se připojí na Windows i Macu).
4. Testovací feedy: **(a)** testovací pár klíčů, platný jen pro testovací zdroj.
5. Odmítnutý podpis: **chyba na záložce „Spuštění a aktualizace“ i notifikace.**
6. Zkušební běhy: **ano**, `-SkipFeedSignature`, který `release-upload.ps1` odmítne.

7. Opakování notifikace o odmítnutém podpisu (rozhodnuto 2026-11-10): **jednou pro každou nabízenou verzi** (stejně jako
   běžná notifikace o aktualizaci: nová verze s neplatným podpisem upozorní znovu); chyba na záložce „Spuštění a
   aktualizace“ zůstává vidět stále.

Otevřené (core-dev, 2026-11-10):
8. **Produkční klíč zatím neexistuje** (vytvoří ho uživatel na offline médiu). `UpdateFeedKeys.Production` je zatím prázdný
   zástupný seznam, takže každý feed z GitHubu se odmítne (fail closed) a instalace Velopacku nenabídne žádnou aktualizaci.
   Jak se má build chovat, dokud klíč není? Návrh: v kódu ponechat fail closed a `release-build.ps1` (bez `-SkipFeedSignature`)
   ať odmítne vydání, dokud je seznam prázdný nebo veřejný klíč ze zadaného soukromého klíče v seznamu není – tak nejde vydat
   verzi, která by žádnou další aktualizaci nepřijala.
9. **Které vydání se čte:** Velopack (`GitBase`) slučuje feedy až 10 nejnovějších vydání. Nový zdroj čte jen feed nejnovějšího
   vydání (podle zveřejnění), které má feed daného RID; při odmítnutí se na starší vydání nepřechází. Důvod: starší
   nepodepsaná vydání by jinak buď blokovala aktualizace, nebo by se musela tiše přeskakovat; navíc 3 požadavky místo 11.
   Rozdíl nastane jen při vydání mimo pořadí verzí (např. oprava starší řady zveřejněná po novější verzi). Potvrdit, nebo
   chtít variantu „ověřit každý z 10 feedů, neplatné vynechat, chybu hlásit jen u nejnovějšího“?
10. **Klíč notifikace:** `FeedRejection.ClaimedVersion` je verze, kterou odmítnutý feed **tvrdí** (nedůvěryhodná, vybral ji
    autor feedu; přečtená stejným parserem Velopacku jen jako semver). Bezpečná je jen pro rozlišení nabídek a zobrazení; útočník
    s přístupem k vydáním ale může zvyšováním verze vyvolat další notifikaci (každé zvýšení = nové nahrání). Když feed verzi
    nemá (chybí, je příliš velký, není JSON), je klíčem `FeedSha256`, a když feed nejde přečíst vůbec, žádný klíč není
    (návrh: notifikovat jednou za běh aplikace). Potvrdit, že je to pro UI v pořádku.
11. **Nabídka z dřívější kontroly:** při odmítnutém feedu zůstává `UpdateCheckService.Available` (nabídka z dřívější platné
    kontroly nebo z `update-state.json`) stejně jako po jiné chybě; instalace z ní v Velopacku stejně nejdřív feed ověří znovu,
    takže nic nestáhne. Má UI nabídku při odmítnutí skrýt?

## Poznámky k implementaci
Koncept (core-dev, 2026-11-10; skripty vydání, UI, notifikace, `docs/release.md` a CHANGELOG zbývají):
- Průzkum Velopacku 1.2.161 v `docs/velopack.md`, bod 13: Velopack podpis feedu nemá, updater nic znovu nestahuje, přesné
  bajty jdou ověřit přepsáním `GetReleaseFeed` v potomcích zdrojů (bez reflexe a forku).
- Core: `UpdateFeedVerifier` (ECDSA P-256/SHA-256, data `LayerPeek-feed-v1\n<RID>\n<bajty>`, `.sig` = base64 podpisu
  IEEE P1363, nejvýš 256 B, seznam klíčů kvůli rotaci), `UpdateFeedRejectedException` + `RejectedUpdateFeed` (důvod,
  tvrzená verze, SHA-256 feedu), `UpdateCheckOutcome.FeedRejected`, `UpdateCheckService.FeedRejection` a
  `FeedRejectionChanged`, `UpdateCheckResult.Rejection`.
- App: `Installer/SignedUpdateSource` (GitHub jen s produkčními klíči, testovací zdroje jen s testovacím klíčem, klíče nejdou
  předat zvenku), `Installer/UpdateFeedKeys` (produkční klíč zatím zástupně prázdný, testovací jen v Debug), pojmenovaný
  klient `LayerPeek.UpdateFeed` s limity a timeoutem, `VelopackUpdateEngine` ho používá.
- Testy: sdílený vektor `tests/Shared/Fixtures/UpdateFeed` (testovací pár klíčů – není tajný, i šifrovaná varianta s heslem
  `layerpeek-test-only`; podpis z PowerShellu 7), `UpdateFeedVerifierTests`, testy odmítnutí v `UpdateCheckServiceTests`,
  `SignedUpdateSourceTests` (složka, loopback přes falešný handler, GitHub přes falešný seznam vydání, engine s
  `TestVelopackLocator`). `MacUpdateE2ETests` nejdřív ověří odmítnutí nepodepsaného feedu, pak aktualizaci s podepsaným;
  verze bere z `Directory.Build.props` (dřív pevně 0.3.1 → 0.3.2, od verze 0.4.0 test neprocházel). Windows E2E
  (`UpdateTests`) feedy podepisuje testovacím klíčem, na Windows zatím nespuštěno.
- Žádný nový balíček (`System.Security.Cryptography` je součást .NET).

Mimo rozsah (zapsat jako alternativy, nerozhodovat):
- **SignPath Foundation** – bezplatný Authenticode podpis pro open-source projekty; vyžaduje build v GitHub Actions
  (T-016 zamítnut 2026-11-01) a reputaci projektu; podepisuje binárky, ne feed (viz T-075).
- **Apple Developer ID** – podpis a notarizace macOS, **není zdarma** (99 USD/rok, T-060).
- Obojí by chránilo i první stažení, ale ne nahrazuje podpis feedu proti převzetí účtu GitHub (podepsaná, ale
  starší / jiná binárka atd.).

Zdroje:
- Velopack zdroj (GithubSource, GitBase, UpdateManager): https://github.com/velopack/velopack/tree/main/src/lib-csharp
- .NET `ECDsa`: https://learn.microsoft.com/dotnet/api/system.security.cryptography.ecdsa
- minisign: https://jedisct1.github.io/minisign/
- NSec: https://nsec.rocks/
