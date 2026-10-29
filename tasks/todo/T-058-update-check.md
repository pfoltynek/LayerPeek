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
- **Velopack pro macOS (převzato z T-019, rozhodnuto 2026-10-29):** T-019 vydává vlastní `.app` bez Velopacku (pokus a podklady v `docs/velopack.md`, bod 10). V tomto úkolu přejít na `.app` od `vpk pack` (`--plist build/macos/Info.plist`, ad-hoc podpis `--signAppIdentity -`, kanál `osx-arm64`): krok `app` v `scripts/release-build.ps1` vezme `.app` z `Portable.zip` jako vstup `.dmg` a `.tar.gz`, do vydání přibydou `.nupkg` a soubory feedu `osx-arm64` (`.pkg` se nevydává, rozhodnuto 2026-10-29). V aplikaci rozpoznat instalaci Velopacku na macOS (`UpdateMac` a `sq.version` v `Contents/MacOS`) a spouštět `VelopackApp.Run()` i tam; na záložce „Spuštění a aktualizace“ pak na macOS ukázat volbu předběžných verzí (T-019: bez Velopacku skrytá).
  - Ověřit na Macu: aktualizace z GitHub Releases bez tokenu, Gatekeeper a atribut `com.apple.quarantine` u aktualizované `.app`, heslo správce pro `/Applications`, chování ad-hoc podpisu po aktualizaci (Keychain T-021, notifikace T-020). Výsledek zapsat do `docs/velopack.md`, bod 10.
  - **Důsledek pro uživatele:** kdo si nainstaloval `.app` z vydání bez Velopacku (T-019), dostane aktualizace až po jedné ruční reinstalaci verze s Velopackem. Uvést v `CHANGELOG.md` a v textu vydání.

## Akceptační kritéria
- [ ] Unit testy porovnání verzí (předběžné přípony, prefix `v`, neplatné tagy, `0.x`)
- [ ] Testy klienta GitHub API s nahranými JSON fixtures a falešným `HttpMessageHandler` (žádná reálná síť), včetně 403, 404, timeoutu a neplatného JSON
- [ ] Testy služby: nová verze → událost + jedna notifikace; stejná / starší verze → nic; opakovaná kontrola stejné verze → žádná další notifikace
- [ ] Headless UI test: položka menu se zobrazí jen při dostupné nové verzi
- [ ] Klik na položku / notifikaci otevře stránku vydání, případně spustí aktualizaci přes Velopack (ověřeno ručně)
- [ ] Žádná nová NuGet závislost, nebo ověřená AOT/trim kompatibilita; AOT publish `win-x64` bez varování
- [ ] macOS: `.app` od `vpk` (ad-hoc podpis, `codesign --verify --deep --strict`), `.dmg` a `.tar.gz` z ní, `.nupkg` a feed `osx-arm64` ve vydání; aktualizace na Macu ověřená a zapsaná v `docs/velopack.md`, bod 10 (převzato z T-019)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Rozbor (příprava 2026-10-29)

Připravil architekt jako podklad pro rozhodnutí uživatele. Nic z toho není rozhodnuté, varianty k otázkám jsou v sekci Otevřené otázky.

### Současný stav podle platformy (develop `617f10d`)

| Distribuce | Co v aplikaci existuje | Co uživatel dostane |
|---|---|---|
| **Windows, instalace Velopack** (`Setup.exe`, T-017) | `VelopackStartup` (hooky), `AppUpdates` + `VelopackUpdateEngine` (`GithubSource`, kanál = RID, `prerelease` podle `PrereleaseUpdates.Offered`) registrované v DI jen v instalaci a mimo testovací režim. Stažení a `WaitExitThenApplyUpdates` po běžném ukončení. Unit testy s `FakeUpdateEngine`. Volba „Nabízet předběžné verze“ na záložce Spuštění a aktualizace je vidět a funguje. | **Nic.** `AppUpdates.CheckAsync` nikdo nevolá: chybí časovač, položka v menu i notifikace. Dokud je repozitář soukromý, kontrola navíc skončí 404 (`docs/velopack.md`, bod 7). |
| **Windows `.zip`** (T-055) | Žádný engine (`AppUpdates.IsAvailable == false`). Volba předběžných verzí je neaktivní s textem „jen v nainstalované verzi“ (`IsPrereleaseOptionAvailable`). | Nic. O nové verzi se dozví jen z GitHubu. |
| **macOS `.dmg` / `.tar.gz`** (T-019, vydáno v 0.3.0) | Vlastní `LayerPeek.app` bez `UpdateMac` a `sq.version`. `CreateUpdateEngine` vrací engine jen na Windows. Volba předběžných verzí je skrytá (`ShowsPrereleaseOption`). | Nic: žádná kontrola ani aktualizace. |

Poznámka k macOS po přechodu na Velopack: lokátor Velopacku na macOS hledá jen `UpdateMac` a `sq.version` v `Contents/MacOS` cesty `.app`, ve které běží (`auto_locate_app_manifest`, [locator.rs](https://github.com/velopack/velopack/blob/develop/src/lib-rust/src/locator.rs)). Instalace je vždy „přenosná“ (`IsPortable: true`). **Aktualizovat se tedy bude umět každá `.app` od `vpk`, ať přišla z `.dmg`, `.tar.gz` nebo `Portable.zip`.** Bez Velopacku by po T-058 zůstal jen windowsový `.zip`.

### Důsledek pro uživatele macOS 0.3.0 a jak ho zmenšit

0.3.0 pro macOS nemá žádný kód kontroly ani aktualizace. Kdo ji má, musí první verzi s Velopackem nainstalovat ručně, tedy přetáhnout novou `LayerPeek.app` přes starou. Nastavení (`~/Library/Application Support/…`), hesla v Keychainu a Login Item se podle bundle identifikátoru zachovají. Keychain se ale může po novém ad-hoc podpisu zeptat na povolení přístupu (T-021, Q2). O nové verzi se uživatel 0.3.0 dozví jen mimo aplikaci (GitHub, README).

Kolik uživatelů se to týká: **dokud je repozitář soukromý, nikdo kromě uživatele a lidí s přístupem 0.3.0 stáhnout nemůže.** Důsledek je tedy prakticky nulový, pokud první veřejně oznámené vydání pro macOS bude už s Velopackem. Varianty jsou v otázce Q7.

### Velopack s ad-hoc podepsanou aplikací na macOS (zjištění ze zdrojů)

Dokumentace Velopacku ad-hoc podpis, karanténu ani App Translocation neřeší. Píše jen, že „Code signing and notarization is required by Apple before shipping your releases to users, or your app won't run“ ([macOS](https://docs.velopack.io/packaging/operating-systems/macos), [signing](https://docs.velopack.io/packaging/signing)). V issues Velopacku jsme ke slovu „translocation“ nic nenašli (0 výsledků) a ke slovu „quarantine“ jen nesouvisející PR ([#1065](https://github.com/velopack/velopack/pull/1065), [#1037](https://github.com/velopack/velopack/pull/1037)), stav 2026-10-29. Níže je proto rozbor zdrojového kódu updateru. Každý bod je **hypotéza k ověření**:

1. **Jak updater aplikuje aktualizaci** ([apply_osx_impl.rs](https://github.com/velopack/velopack/blob/develop/src/bins/src/commands/apply_osx_impl.rs)): `UpdateMac` rozbalí `.nupkg` do dočasné složky a vymění ji s instalovanou `.app` atomicky přes `renamex_np(RENAME_SWAP)`, stejně jako Sparkle. Když se výměna nepovede, zkusí dvojí `rename`. Při `PermissionDenied` požádá o zvýšení oprávnění dialogem a spustí `osascript … with administrator privileges` (`mv` a `rm -rf`). Nakonec zavolá `utimes` na `.app`, aby změnu zaznamenaly LaunchServices. Stahování (`DownloadUpdatesAsync`) dělá knihovna v procesu aplikace do `~/Library/Caches/velopack/LayerPeek/packages` ([macOS](https://docs.velopack.io/packaging/operating-systems/macos)).
2. **Karanténa po aktualizaci:** atribut `com.apple.quarantine` dostávají soubory, které stáhne aplikace s `LSFileQuarantineEnabled` nebo aplikace v sandboxu ([Apple, LSFileQuarantineEnabled](https://developer.apple.com/documentation/bundleresources/information-property-list/lsfilequarantineenabled)). LayerPeek ani `UpdateMac` nic z toho nemají. Velopack sandbox navíc vůbec nepodporuje ([macOS](https://docs.velopack.io/packaging/operating-systems/macos)). **Očekávání:** aktualizovaná `.app` atribut nedostane a Gatekeeper ji při dalším spuštění nezablokuje. Dialog „Přesto otevřít“ by se tak objevil jen u první ruční instalace. Ověřit pomocí `xattr -l` po aktualizaci (dá se automatizovat, viz níže).
3. **App Translocation:** aplikace spuštěná přímo z `.dmg` nebo s karanténou ze složky, kam se stáhla, běží z náhodné cesty jen pro čtení ([Eclectic Light, What causes App Translocation?](https://eclecticlight.co/2023/05/09/what-causes-app-translocation), [Lost in Translocation](https://eclecticlight.co/2024/05/14/lost-in-translocation/)). Translokovaná aplikace se podle těchto zdrojů často neumí aktualizovat ([M. Tsai, App Translocation in Ventura](https://mjtsai.com/blog/2023/05/10/app-translocation-in-ventura)). Lokátor Velopacku translokaci nerozpoznává (v `locator.rs` není žádná zmínka), takže by se pokusil vyměnit `.app` v translokované cestě. Podle kódu to nejspíš skončí jinou chybou než `PermissionDenied` (svazek jen pro čtení), tedy bez dialogu a s chybou v logu. **Návrh:** aplikace už translokaci rozpoznává kvůli Login Items (`MacAppBundle.IsPermanentLocation`, T-019), a stejně jako u autostartu by v takovém případě aktualizaci nenabízela. Místo toho by ukázala „nejdřív přesuňte do Aplikací“. Rozhodnutí je v Q8.
4. **Zápis do `/Applications`:** `/Applications` je `root:admin` s právem zápisu pro skupinu `admin`. Aplikaci přetaženou z `.dmg` vlastní uživatel, který ji přetáhl. **Očekávání:** u účtu správce projde výměna bez dialogu, u standardního účtu Velopack ukáže dialog a pak požádá o heslo správce přes AppleScript (bod 1). Dokumentace to shrnuje slovy „elevation may be required“ ([macOS](https://docs.velopack.io/packaging/operating-systems/macos)). Ověřit ručně s oběma typy účtu.
5. **Podpis a hardened runtime (riziko regrese T-108):** při pokusu v T-019 `vpk --signAppIdentity -` podepsal s **výchozími entitlements .NET**, včetně `allow-dyld-environment-variables`, `allow-jit` a `allow-unsigned-executable-memory` (`docs/velopack.md`, bod 10). T-108 je ale záměrně vyřadil. Proto je potřeba `--signEntitlements build/macos/LayerPeek.entitlements` (volba existuje, [vpk CLI pro macOS](https://docs.velopack.io/reference/cli/content/vpk-osx)) a pak `Get-MacHardenedRuntimeProblems` nad `.app` z `Portable.zip` **i** z `.nupkg`. Druhá kontrola je nutná, protože `.nupkg` je to, co dostane aktualizovaný uživatel. `UpdateMac` je další soubor Mach-O, i on musí mít příznak runtime. Varianty sestavení `.app` jsou v Q9.
6. **Keychain a notifikace po aktualizaci:** ad-hoc podpis má po každém sestavení jiný cdhash. Po aktualizaci se proto může objevit dialog „povolit přístup do Klíčenky“ (T-021, Q2, zatím neověřeno). Povolení notifikací se podle T-020 drží bundle identifikátoru. Obojí se týká i ruční reinstalace, nejen Velopacku.
7. **Integrita:** Velopack ověřuje jen SHA z feedu a nemá vlastní podpis balíčků (`docs/velopack.md`, bod 7, [#975](https://github.com/velopack/velopack/issues/975)). Bezpečnost tedy stojí na tom, kdo smí nahrávat do GitHub Releases. Na macOS je to stejné jako na Windows.

### Co jde ověřit automaticky a co jen ručně

**Automaticky (bez GitHubu a bez reálné sítě):**
- Unit testy Core: porovnání verzí SemVer 2, služba kontroly s falešným časem, zapamatování notifikované verze. Dál testy `AppUpdates` s `FakeUpdateEngine` (už existují) a testy klienta GitHub REST s fixtures a falešným `HttpMessageHandler`.
- Headless UI testy (T-031): položka v menu, stav ruční kontroly, volby v nastavení.
- **End-to-end aktualizace z lokálního feedu na loopbacku** (opt-in E2E přes `tools/run-e2e.ps1`, podle vzoru `VelopackInstallerTests`):
  - Z **jedné** AOT publish složky zabalit `vpk pack` dvakrát, jako verzi N a N+1. Druhý AOT publish tak není potřeba a balení trvá jen sekundy. Feed N+1 (`releases.<kanál>.json` + `.nupkg`) servírovat jednoduchým statickým serverem na `127.0.0.1` v testu, případně rovnou jako složku (`UpdateManager` přijímá i cestu, [Velopack: integrace](https://docs.velopack.io/integrating/overview)).
  - Windows: tichá instalace N, aplikace (nebo testovací přepínač) spustí kontrolu, stažení a `ApplyOnExit`. Kontrola: `current\sq.version` = N+1, stejná cesta exe a zástupce s `ToastActivatorCLSID` (T-010), aplikace znovu běží, uklizené zálohy.
  - macOS: `.app` N v dočasné složce (ne v `/Applications`), aktualizace na N+1. Kontrola: `CFBundleShortVersionString`, `codesign --verify --deep --strict`, `Get-MacHardenedRuntimeProblems`, **žádný `com.apple.quarantine`** (`xattr -l`), aplikace znovu běží a bez ikony v Docku (`lsappinfo`).
  - Potřebuje přepínač zdroje aktualizací jen pro testy (Q10).
- Pester: argumenty `vpk pack` pro macOS, soubory `osx-arm64` ve výstupu, `SHA256SUMS-macos.txt`, kontrola kompletnosti konceptu (`release-publish.ps1` musí vyžadovat i `releases.osx-arm64.json`).

**Jen ručně:**
- První spuštění staženého `.dmg` z prohlížeče (Gatekeeper, karanténa, „Přesto otevřít“) a App Translocation při spuštění z `.dmg` / Stažených souborů.
- Aktualizace v `/Applications` s účtem správce a se standardním účtem (dialog AppleScriptu).
- Dialog Klíčenky po aktualizaci (T-021) a zachované povolení notifikací (T-020).
- Čtení `releases.<RID>.json` z GitHub Releases bez tokenu. Funguje až s veřejným repozitářem, nebo v dočasně veřejném zkušebním repozitáři podle `docs/release.md`, „Zkušební vydání“ (rozhodnutí uživatele).
- Klik na notifikaci a skutečný restart po aktualizaci na obou OS.

### Odhad velikosti a návrh rozdělení

Celkem odhadem **4–5 sezení vývojáře** a **1 sezení ručního ověření uživatelem na Macu**. To je víc, než zvládne jedno sezení, proto navrhuju rozdělit úkol na dílčí úkoly. ID se přidělí při založení, další volné je T-109. T-058 by pak zůstal jako zastřešující úkol, nebo by se uzavřel odkazem na dílčí úkoly.

| # | Dílčí úkol | Platforma | Owner | Velikost | Závisí na |
|---|---|---|---|---|---|
| A | Core: `IUpdateChecker`, porovnání verzí SemVer 2, služba kontroly (časovač přes abstrakci, událost, zapamatovaná notifikovaná verze v `AppSettings`) + unit testy | all | core-dev | S–M | T-058 otázky Q1–Q3, Q5 |
| B | App: implementace `IUpdateChecker` přes `AppUpdates` (Velopack) a přes GitHub REST (`.zip`), registrace podle distribuce + testy s fixtures | all | core-dev | S–M | A |
| C | UI: položka v tray menu, notifikace, volba zapnutí kontroly, ruční kontrola, klik = aktualizovat / otevřít stránku vydání, texty ve všech jazycích + headless UI testy | all | ui-dev | M | A, B, Q4, Q6 |
| D | Windows E2E: aktualizace z lokálního feedu na loopbacku (N → N+1) | windows | qa-reviewer | M | B, Q10 |
| E | macOS: `.app` přes `vpk pack` v `release-build.ps1` (entitlements T-108, bez `.pkg`), `VelopackStartup`/`CreateUpdateEngine` i pro macOS (`UpdateMac` + `sq.version`), volba předběžných verzí na macOS, ochrana proti translokaci, Pester + E2E aktualizace z lokálního feedu | macos | ui-dev | M–L | B, C, Q8–Q10 |
| F | macOS ruční ověření (Gatekeeper, `/Applications`, Keychain, notifikace, GitHub bez tokenu) a zápis do `docs/velopack.md`, bod 10, plus text do `CHANGELOG.md` a textu vydání (důsledek pro 0.3.0) | macos | qa-reviewer (+ uživatel) | S | E, Q7 |

## Otevřené otázky
Ke každé otázce jsou varianty (příprava 2026-10-29). Doporučená varianta je označená **(doporučeno)**. Rozhoduje uživatel.

- **Q1** Jak často kontrolovat (např. při startu a pak jednou za 24 h)?
  - a) **Krátce po startu (např. za 1–2 min, aby nezdržovala start ani první polling tiskáren) a pak jednou za 24 h podle času poslední kontroly uloženého v nastavení (doporučeno).** Plus: jeden dotaz na GitHub API denně, hluboko pod limitem 60/h. Přežije uspání notebooku i dlouhý běh v tray. Minus: nové vydání se ukáže až do 24 h.
  - b) Jen při startu. Plus: nejjednodušší. Minus: aplikace v tray běží týdny, takže se o vydání uživatel nemusí dozvědět vůbec.
  - c) Při startu a pak každých 6 h. Plus: rychlejší reakce. Minus: víc dotazů a víc záznamů v logu. U verzí `0.x` vycházejících i několikrát denně by to mohlo působit dotěrně.
- **Q2** Jde kontrola vypnout v nastavení (opt-out)? Je ve výchozím stavu zapnutá?
  - a) **Ve výchozím stavu zapnutá, s volbou „Automaticky kontrolovat aktualizace“ na záložce Spuštění a aktualizace (doporučeno).** Plus: většina uživatelů dostane aktualizace a kdo nechce dotazy na GitHub (soukromí, firemní síť), ten je vypne. Minus: nová volba a texty v 8 jazycích.
  - b) Vždy zapnutá, bez volby. Plus: nejméně UI. Minus: aplikace kontaktuje GitHub (IP adresa) bez možnosti to vypnout.
  - c) Ve výchozím stavu vypnutá (opt-in). Plus: maximální soukromí. Minus: většina uživatelů o aktualizacích nebude vědět. U macOS 0.3.0 by se problém z Q7 opakoval.
- **Q3** Předběžná vydání: všechna `0.x` jsou předběžná – nabízet předběžné verze vždy, jen dokud běží verze `0.x`, nebo podle volby v nastavení?
  - Poznámka: pro Velopack je to už rozhodnuté (2026-10-24, T-017, `Core.Updates.PrereleaseUpdates`). Výslovná volba uživatele vždy vyhrává, a dokud žádná není, na `0.x` se předběžné verze nabízejí a od `1.0` ne.
  - a) **Stejné pravidlo `PrereleaseUpdates.Offered` pro všechny cesty (Velopack i GitHub REST pro `.zip`). Volbu „Nabízet předběžné verze“ zpřístupnit i ve `.zip` a na macOS s Velopackem (doporučeno).** Plus: jedno pravidlo, nic nového k rozhodování. Minus: ve `.zip` se mění dnešní stav „neaktivní, jen v instalaci“.
  - b) Na `0.x` nabízet předběžné verze vždy a volbu ignorovat. Plus: jednodušší. Minus: je to v rozporu s rozhodnutím 2026-10-24.
  - c) Jen podle volby, bez výchozího pravidla podle verze. Minus: během `0.x` by bez zásahu uživatele nic nenašla, protože všechna vydání jsou předběžná.
- **Q4** Má být v tray menu (nebo v okně O aplikaci) ruční položka „Zkontrolovat aktualizace“?
  - a) **Tlačítko „Zkontrolovat teď“ na záložce Spuštění a aktualizace vedle voleb z Q2/Q3 s výsledkem vedle tlačítka (aktuální / dostupná X.Y.Z / kontrola selhala), podobně jako „Odeslat zkušební oznámení“ z T-101 (doporučeno).** Plus: neprodlouží tray menu (T-042) a je u souvisejících voleb. Minus: hůř se hledá než v menu.
  - b) Tlačítko v okně O aplikaci vedle verze. Plus: je u čísla verze, kde ho uživatelé čekají (běžné na macOS). Minus: okno O aplikaci dnes nemá žádné akce.
  - c) Položka v tray menu. Plus: nejsnáz se najde. Minus: menu je už dlouhé a položka by tam byla napořád.
- **Q5** V instalaci přes Velopack: má kontrolu úplně převzít Velopack (a tento úkol se pro ni vypne), nebo zůstane společná kontrola a Velopack jen provede aktualizaci po kliknutí?
  - a) **Společná služba v Core nad `IUpdateChecker` se dvěma implementacemi: v instalaci Velopack (`AppUpdates`), ve `.zip` GitHub REST. UI je stejné, liší se jen akce po kliknutí (Velopack: „Aktualizovat a restartovat“, `.zip`: otevřít stránku vydání) (doporučeno).** Plus: jeden dotaz na kontrolu, žádná duplicita. Pro instalaci rozhoduje feed Velopacku, takže se nikdy nenabídne vydání bez souborů pro daný RID. Minus: dvě implementace.
  - b) Detekce vždy přes GitHub REST, Velopack jen aplikuje. Minus: dva dotazy, a vydání bez feedu pro daný kanál (např. jen Windows) by REST nabídl, Velopack pak nic nenašel.
  - c) Jen Velopack, bez kontroly pro `.zip`. Plus: nejméně kódu, protože po přechodu macOS na Velopack zbývá bez aktualizací jen windowsový `.zip`. Minus: uživatelé `.zip` se o nové verzi nedozví.
- **Q6 (nová, 2026-10-29)** Kdy aktualizaci (Velopack) stáhnout a aplikovat?
  - a) **Po kliknutí na „Aktualizovat“ stáhnout, běžně ukončit (uloží stav) a restartovat, jak to už dělá `AppUpdates.ApplyAsync` (doporučeno).** Plus: hotové a otestované, uživatel ví, kdy se aplikace restartuje. Minus: chvíli čeká na stažení (asi 76 MB na Windows, 38 MB na macOS).
  - b) Stáhnout na pozadí hned po nalezení a aplikovat při příštím ukončení. Plus: nic nečeká. Minus: stahuje bez souhlasu a aplikace v tray se skoro neukončuje.
  - c) Plně automaticky bez dotazu. Minus: neočekávaný restart uprostřed sledování tisku.
- **Q7 (nová, 2026-10-29)** Jak zmenšit dopad jedné ruční reinstalace pro uživatele macOS 0.3.0?
  - a) **Vydat macOS s Velopackem jako součást nejbližšího vydání (0.3.1, nebo 0.4.0, pokud T-058 nestihne 0.3.1) a repozitář zveřejnit, případně macOS veřejně oznámit, až potom. Do textu vydání a `CHANGELOG.md` dát jednu větu „macOS 0.3.0: jednou přetáhněte novou LayerPeek.app přes starou“ (doporučeno).** Plus: dokud je repozitář soukromý, 0.3.0 nikdo cizí nemá, takže dopad je jen na uživatele. Minus: macOS se oznámí až s T-058.
  - b) Navíc z vydání 0.3.0 odebrat soubory pro macOS, nebo je v textu vydání označit jako zastaralé. Plus: nikdo už 0.3.0 pro macOS nestáhne. Minus: zasahuje do zveřejněného vydání, `SHA256SUMS-macos.txt` by přestal odpovídat.
  - c) Nic dalšího nedělat, jen poznámka v textu vydání. Plus: bez práce. Minus: kdo si 0.3.0 stáhne, se o aktualizaci nedozví.
- **Q8 (nová, 2026-10-29)** Translokovaná nebo jen pro čtení umístěná `.app` na macOS (spuštěná z `.dmg` nebo ze Stažených souborů):
  - a) **Aktualizaci nenabízet a místo ní ukázat „Přesuňte LayerPeek do Aplikací, pak se bude aktualizovat“, stejně jako u Otevřít při přihlášení (`MacAppBundle.IsPermanentLocation`) (doporučeno).** Plus: žádný nepovedený pokus, jasný návod. Minus: další text v 8 jazycích.
  - b) Nabídnout a nechat Velopack selhat (jen log). Minus: uživatel klikne a nic se nestane.
- **Q9 (nová, 2026-10-29)** Jak sestavit `.app` pro Velopack na macOS?
  - a) **Vlastní hotový bundle z kroku `app` (Info.plist, `.lproj` z T-097, `Resources` z T-026, ikona T-098) předat do `vpk pack --packDir LayerPeek.app`. Podepsat ho `vpk` s `--signAppIdentity - --signEntitlements build/macos/LayerPeek.entitlements` a pak zkontrolovat `Get-MacHardenedRuntimeProblems` nad `.app` z `Portable.zip` i z `.nupkg` (doporučeno).** Plus: zachová všechny dosavadní kontroly a rozložení bundlu. Minus: že `vpk` přijme hotovou `.app` jako `--packDir`, dokumentace jen zmiňuje ([macOS](https://docs.velopack.io/packaging/operating-systems/macos)), je to potřeba ověřit pokusem.
  - b) Nechat `vpk` sestavit bundle z publish složky (`--plist`, `--icon`), jak to bylo při pokusu v T-019. Minus: rozložení `Contents/MacOS` vs. `Resources` a `.lproj` se musí řešit znovu. Bez `--signEntitlements` hrozí regrese T-108.
  - c) Bundle a podpis udělat vlastními kroky až po `vpk`. Nejde: `.nupkg` vzniká uvnitř `vpk pack` z podepsané `.app`, a pozdější podpis by se do aktualizace nedostal.
- **Q10 (nová, 2026-10-29)** Jak v E2E testu nasměrovat aktualizace na lokální feed na loopbacku?
  - a) **Přepínač zdroje (`SimpleWebSource` s `http://127.0.0.1:<port>/`, nebo složka), který aplikace přijme jen v izolovaném testovacím režimu (`AppEnvironment.IsIsolated`) a jen pro loopback adresu nebo místní cestu. Produkční běh ho ignoruje (doporučeno).** Plus: testuje skutečnou aplikaci včetně restartu, produkce zdroj přesměrovat nedovolí. Minus: `CreateUpdateEngine` dnes engine v testovacím režimu vůbec nevytváří, to se změní jen pro tento přepínač.
  - b) Samostatná malá testovací aplikace s Velopackem místo LayerPeeku. Plus: produkční kód se nemění. Minus: neověří zapojení v LayerPeeku (hooky, ukončení, restart, podpis bundlu LayerPeeku).
  - c) Proměnná prostředí platná i v produkci. Minus: kdokoli pod stejným uživatelem by přesměroval aktualizace na vlastní balíček. Nedoporučuju.

## Poznámky k implementaci
(vyplní agent po dokončení)
