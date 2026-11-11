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
- [x] E2E test aktualizace (T-128, testovací feed) projde s feedem podepsaným testovacím klíčem a
      selže s nepodepsaným (macOS ověřeno; Windows → přesunuto do T-125)
- [x] `release-build.ps1` vytvoří `releases.<RID>.json.sig` pro každý RID, bez klíče skončí chybou; soukromý klíč se
      neobjeví v `artifacts/`, logu ani výpisu (Pester test)
- [x] `release-upload.ps1` nahraje `.sig` před feedem a odmítne build s `-SkipFeedSignature`; `release-publish.ps1`
      odmítne vydání bez `.sig` u feedu (Pester testy)
- [x] Při odmítnutém podpisu: chyba na záložce „Spuštění a aktualizace“ a notifikace, která se neopakuje při každé
      kontrole (UI / unit testy); testovací klíč pro GitHub feed nikdy neplatí (test)
- [x] Podpis z PowerShellu ověří C# kód (sdílený testovací vektor v obou sadách testů)
- [x] `docs/release.md`: vytvoření klíče, offline uložení a záloha, rotace, kompromitace, rozsah ochrany;
      `docs/architecture.md`: nový zdroj aktualizací a kde leží veřejný klíč
- [x] `CHANGELOG.md` (`[Unreleased]`, `### Security`): aktualizace jsou ověřované podpisem
- [x] Žádný nový NuGet balíček; žádné nové trim/AOT varování
- [ ] Zkušební vydání (`docs/release.md`, „Zkušební vydání“): podepsaný feed nahraný, nainstalovaná verze s ověřením
      aktualizaci přijme → přesunout do T-125 / T-127, pokud nejde ověřit v úkolu
- [x] `dotnet build` a `dotnet test` bez chyb a varování

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

Otevřené (ui-dev a qa-reviewer, 2026-11-10):
12. **Nabídka v menu ikony při odmítnutí (souvisí s 11):** menu může dál ukazovat „Je dostupná nová verze X.Y.Z“, zatímco
    záložka ukazuje chybu (instalace z ní nic nestáhne, feed se ověří znovu). Návrh: při aktivním odmítnutí položku skrýt nebo
    nahradit „Aktualizace odmítnuta…“ (otevře záložku) a starší notifikaci „dostupná aktualizace“ stáhnout.
13. **Tlačítko „Otevřít stránku vydání“** v rámečku chyby? Zatím jen text.
14. **Zvuk notifikace o odmítnutí:** zatím jen s volbou „všechny notifikace“ (jako běžná notifikace o aktualizaci). Má se
    počítat jako důležitá?
15. **Vypnuté notifikace:** odmítnutá verze se počítá jako oznámená, takže po pozdějším zapnutí notifikací se neukáže (chyba na
    záložce zůstává). V pořádku?
16. **Rotace × otázka 9:** protože aplikace čte jen feed nejnovějšího vydání, instalace starší než verze N, která verzi N
    přeskočí, odmítne N+1 podepsanou už novým klíčem a bude potřebovat ruční přeinstalaci (zapsáno v `docs/release.md`).
17. **Klíč v paměti během buildu:** šifrovaný text klíče a heslo zůstávají v paměti celý build (asi 20 min), aby šlo USB odpojit
    hned po kontrole. Na macOS není `SecureString` v paměti šifrovaný. Stačí to, nebo se má heslo zadat znovu až při podpisu?
18. **Minimální délka hesla** nového klíče 16 znaků – v pořádku?
19. **Zkušební vydání** v testovacím repozitáři teď potřebuje produkční klíč (upload odmítne build s `-SkipFeedSignature`).
    Je to záměr?
20. Má `release.ps1` dostat parametr `-FeedSigningKey`, nebo stačí proměnná prostředí `LAYERPEEK_FEED_SIGNING_KEY`?
21. `-AddToPublished` teď vyžaduje `.sig` i u feedu druhého OS, takže přidání OS k vydání zveřejněnému před T-181 se odmítne
    (předpoklad: v pořádku, T-180 `-AddToPublished` ruší).

Otevřené (qa-reviewer, bezpečnostní revize 2026-11-10):
22. **Rada „stáhněte a nainstalujte ručně“ při odmítnutí:** text na záložce (`SettingsUpdateRejected`) i manuály radí stáhnout
    novou verzi ze stránky vydání na GitHubu a nainstalovat ji ručně. Odmítnutý podpis je ale přesně situace, kdy vydání na GitHubu
    může být škodlivé (útočník s přístupem k vydáním), a notifikace navíc ukazuje verzi, kterou si vybral útočník („Verze 9.9.9
    nebyla nainstalována…“). Rada tak útočníkovi pomáhá obejít ochranu. Návrh: text změnit na „neinstalujte ji ručně; počkejte
    na další vydání nebo ověřte oznámení v README / na stránce projektu“ (8 jazyků + manuály) – rozhodne uživatel.
23. **Podpis neváže kanál stabilní / předběžné vydání:** podepsaná data jsou jen prefix + RID + feed. Podepsaný feed předběžného
    vydání (např. `0.5.0-rc.1`) může útočník znovu nahrát jako stabilní vydání. Nabídku stejně filtruje `UpdateCheckService`
    podle přípony semver, takže uživatel bez předběžných verzí ji nedostane; zbytkové riziko je jen „podstrčení“ pravé podepsané
    verze. Zapsat jako přijatelné zbytkové riziko do `docs/release.md`, nebo kanál do podpisu přidat?
24. **Odpověď k otázce 11 upřesněna:** nabídka z platné kontroly **v tomto běhu** se při pozdějším odmítnutí neověřuje znovu –
    `AppUpdates.ApplyAsync` použije uložený `UpdateInfo` z dřívější ověřené kontroly a stáhne balíček připnutý tímto ověřeným
    feedem (velikost + SHA-256). Bezpečnostně v pořádku, jen tvrzení „instalace z ní stejně nejdřív feed ověří znovu“ platí jen
    pro nabídku z `update-state.json`. Má instalace po aktivním odmítnutí raději vůbec neproběhnout?


Rozhodnuto uživatelem 2026-11-11 (otázky 8–24):
- 8: **fail closed** ponechat; produkční klíč vytvoří uživatel na offline USB a veřejný klíč přidá do
  `UpdateFeedKeys.Production`; teprve potom se T-181 sloučí do `develop`.
- 9: číst **jen feed nejnovějšího vydání** s daným RID (jak je implementováno).
- 10, 14, 15: **jak je implementováno** (klíč tvrzená verze → hash feedu → jednou za běh; zvuk jen s volbou „všechny
  notifikace“; při vypnutých notifikacích se verze počítá jako oznámená).
- 11, 12, 24: při aktivním odmítnutí **nabídku skrýt a stáhnout**: položku „Je dostupná nová verze …“ v menu ikony nahradit
  „Aktualizace odmítnuta…“ (otevře záložku „Spuštění a aktualizace“), starší notifikaci o dostupné aktualizaci stáhnout
  a instalaci z dřívější nabídky nedovolit.
- 13: **bez tlačítka** v rámečku chyby.
- 16, 18, 20, 21: **přijato** podle návrhu.
- 17: **heslo znovu při podpisu**: na začátku buildu se klíč jen ověří (heslo, veřejný klíč v seznamu) a hned zahodí;
  po zabalení se heslo zadá podruhé (USB musí být připojené i na konci), klíč je v paměti jen po dobu podpisu.
- 19: **i zkušební vydání se podepisuje** produkčním klíčem (jak je implementováno).
- 22: text odmítnutí (záložka, notifikace, příručky): **neinstalovat ručně, počkat** na další vydání nebo sledovat
  oznámení projektu; **bez řádku s odmítnutou verzí** (verzi volí autor feedu).
- 23: kanál stabilní / předběžné vydání v podpisu **ne** – přijato jako zbytkové riziko, zapsat do `docs/release.md`.
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

UI (ui-dev, 2026-11-10):
- Záložka „Spuštění a aktualizace“: při aktivním odmítnutí rámeček barvy upozornění pod „Zkontrolovat nyní“ (aktualizace
  odmítnuta, protože nešlo ověřit podpis, nic se nenainstalovalo, stáhnout ze stránky vydání a nainstalovat ručně; řádek
  s odmítnutou verzí). Výsledek ruční kontroly `FeedRejected` = „Aktualizace byla odmítnuta.“; výsledek, kterému odmítnutí
  odporuje, se smaže. Stav sleduje `UpdateOffer` (`IUpdateFeedRejections`), změny se převádějí na vlákno UI.
- Notifikace (rozhodnutí 7): `INotificationService.ShowUpdateRejected` (Windows toast i macOS, identifikátor
  `layerpeek-update-rejected`, kliknutí otevře záložku – argument `settings=updates`). Klíč `UpdateCheckService.RejectionKey`:
  `version:<tvrzená verze>`, jinak `sha256:<hash feedu>`; poslední klíč v `update-state.json` (`notifiedRejection`), restart
  neupozorní znovu; bez klíče jednou za běh (návrh z otázky 10). Vypnuté notifikace = verze se počítá jako oznámená.
- 6 nových řetězců ve všech 8 jazycích, manuály oddíl 12 „Aktualizace odmítnuta“. UI test: záložka se vejde ve všech jazycích
  i s chybou. Snímky `settings-startup-updates*` beze změny. Neověřeno: skutečná notifikace macOS / Windows toast a kliknutí.

Skripty vydání (qa-reviewer, 2026-11-10):
- `release-build.ps1`: klíč z `-FeedSigningKey` nebo `LAYERPEEK_FEED_SIGNING_KEY` (šifrovaný PEM PKCS#8, nešifrovaný se
  odmítne), heslo z výzvy (SecureString), pro skripty `-FeedSigningKeyPassword` (jen SecureString). Kontroly před buildem:
  soubor → `UpdateFeedKeys.Production` není prázdný → dešifrování → veřejný klíč je v seznamu. Po zabalení podpis
  `releases.<RID>.json.sig`, ověření proti seznamu, Dispose klíče a hesla. `.sig` v `SHA256SUMS` i `build-info.json`
  (`FeedSignature`: `signed`/`skipped`, `PublicKeySha256`).
- Seznam klíčů čte skript přímo z `UpdateFeedKeys.cs` (jediný zdroj pravdy); neznámý tvar seznamu = chyba.
- `release-upload.ps1` odmítne zkušební build, build bez stavu podpisu i feed bez `.sig`; `.sig` nahraje s balíčky před
  feedem. `release-publish.ps1` odmítne feed bez `.sig` (i s `-AllowIncomplete`). `New-UpdateFeedSigningKey` vytvoří pár
  klíčů (heslo aspoň 16 znaků).
- Pester: sdílený vektor v obou směrech, skutečný `release-build.ps1` v dočasném repozitáři, žádné tajemství ve výpisech ani
  souborech. Na Macu prošel `release-build.ps1 -SkipFeedSignature` (AOT, vpk, smoke) a skutečný feed podepsaný testovacím
  klíčem proti dočasnému seznamu ověřil C# `UpdateFeedVerifier`.

Stav 2026-11-10: zbývá produkční klíč (vytvoří uživatel na offline médiu, veřejný klíč do `UpdateFeedKeys.Production`),
odpovědi na otázky 8–21 a zkušební vydání. Dokud je `Production` prázdný, nesmí vyjít vydání z této větve (nepřijalo by
žádnou další aktualizaci) – podepsaný build ho ani nedovolí.

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

## Review
qa-reviewer, 2026-11-10 (bezpečnostní revize celé větve, `git diff origin/develop...HEAD`):
- **Blokující nálezy: žádné.** Úkol zůstává v `in-progress` kvůli produkčnímu klíči, zkušebnímu vydání a otázkám 8–24.
- Ověřeno: feed i `.sig` se čtou jen přes naše přepsané `GetReleaseFeed` (GitHub, loopback, složka), přesné bajty se ověří a teprve
  ty samé bajty se předají parseru Velopacku (žádné TOCTOU); záložní cesta `SimpleFileSource` na `*.nupkg` je zavřená;
  `GitBaseAsset` přenáší `SHA256`/`Size`/`SHA1` z ověřeného feedu (ověřeno za běhu nad Velopack 1.2.161); `DownloadUpdatesAsync`
  feed znovu nečte; testovací klíč jen v Debug a jen pro testovací zdroj; limity 1 MiB / 256 B a timeout 30 s; pojmenovaný
  klient přes `IHttpClientFactory` bez loggerů; P1363 s pevnou délkou, DER odmítnut, soukromý klíč a jiná křivka odmítnuty;
  skripty nevypisují klíč ani heslo, `-SkipFeedSignature` upload i publish odmítnou.
- Opraveno: `Get-TrustedFeedKeys` četl při dvou deklaracích `Production` (`#if DEBUG` / `#else`) tu první, tedy možná jinou, než
  zkompiluje Release – teď je odmítne (Pester test). Komentář v `UpdateCheckService` k nabídce z dřívější kontroly upřesněn
  (otázka 24).
- Drobnosti (ne blokující): zpráva výjimky „feed listed but not found“ obsahuje název vydání z GitHubu (nedůvěryhodný text v logu);
  kontrola křivky `Curve.Oid.Value` je ověřená jen na macOS – Windows běh testů v T-125 ji pokryje.

