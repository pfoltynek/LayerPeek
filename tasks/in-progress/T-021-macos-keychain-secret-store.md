---
id: T-021
title: macOS – úložiště tajných údajů v Keychainu
status: in-progress
owner: core-dev
priority: P1
platform: macos
depends_on: [T-002, T-030, T-095]
---

## Cíl
Implementace `ISecretStore` nad macOS Keychain, aby se hesla / API klíče / tokeny tiskáren na macOS ukládaly stejně bezpečně jako na Windows (T-002 – `WindowsCredentialSecretStore`).

> **Pouze na macOS:** úkol implementuje a ověřuje uživatel později na Macu. Neblokuje žádný Windows úkol.

## Rozsah
- Nová třída v `src/LayerPeek.App/Platform/MacOS/` (např. `KeychainSecretStore`), `[SupportedOSPlatform("macos")]`, `internal sealed`
- Nativní interop jen přes `[LibraryImport]` do `Security.framework` a `CoreFoundation.framework` – **bez knihoven třetích stran**, kompatibilní s Native AOT (žádná reflexe, žádné `BuiltInComInteropSupport`)
- Doporučené API: `SecItemAdd` / `SecItemCopyMatching` / `SecItemUpdate` / `SecItemDelete` s `kSecClassGenericPassword` (service = prefix aplikace, account = klíč); zastaralé `SecKeychain*` API nepoužívat, pokud k tomu nebude důvod uvedený v poznámkách
- Kontrakt zrcadlí Windows implementaci a `ISecretStore` v Core:
  - tajný údaj jako **striktní UTF-8** – neplatný Unicode při zápisu → `ArgumentException` (bez vložení obsahu do zprávy), neplatné UTF-8 při čtení → `SecretStoreException`; nic se tiše nemění
  - chyby argumentů se vyhazují hned; zrušení (`Task.FromCanceled`) a chyby OS (`SecretStoreException` s kódem `OSStatus`, nikdy s tajným údajem) se vracejí přes vrácený `Task`
  - chybějící položka (`errSecItemNotFound`): `GetAsync` → `null`, `DeleteAsync` → `false`
  - `SetAsync` vytvoří nebo přepíše existující položku
  - **klíče nerozlišují velikost písmen** (kontrakt `ISecretStore`; Keychain atributy velikost písmen rozlišují, takže je nutná jednotná normalizace klíče)
  - buffery s otevřeným textem se po použití nulují, všechny CF objekty se uvolní (`CFRelease`)
- Registrace v composition rootu: pokud už existuje výběr implementace podle OS (T-015), doplnit větev pro macOS; jinak ji doplní T-015
- Integrační testy v `tests/LayerPeek.App.Tests/Platform/MacOS/` nad skutečným Keychainem aktuálního uživatele, na jiném OS přeskočené (`Assert.SkipUnless(OperatingSystem.IsMacOS(), …)`)

## Akceptační kritéria
- [ ] Round-trip včetně prázdného řetězce, diakritiky, emoji a znaku NUL (stejné případy jako Windows testy)
- [ ] Přepis existující položky, `DeleteAsync` vrací `true`/`false` podle existence, chybějící klíč vrací `null`
- [ ] Klíče lišící se jen velikostí písmen adresují stejný tajný údaj (test)
- [ ] Izolace podle prefixu (dvě instance s různým prefixem se neovlivňují)
- [x] Neplatný Unicode při zápisu → `ArgumentException`; zrušený token → zrušený `Task`; argumenty `null`/prázdné → okamžitá výjimka
- [ ] Testy po sobě uklidí vše, co vytvořily, **i po spadlém předchozím běhu**: testovací položky mají vyhrazený testovací prefix (nikdy produkční `LayerPeek`) a před během testů se odstraní všechny pozůstatky s tímto prefixem; úklid je sám ověřen testem
- [ ] Kontrola před/po podle `docs/architecture.md` („Izolace testů na macOS“): po `dotnet test` (i po přerušeném běhu a úklidu) v Keychainu nezůstane žádná položka služeb LayerPeek / Prusa a produkční položky se nezmění
- [ ] Zprávy výjimek ani logy neobsahují tajný údaj
- [x] Lokální Native AOT publish `osx-arm64` bez trim/AOT varování z kódu projektu
- [ ] Windows build a testy beze změny chování (`WindowsCredentialSecretStore` se nemění)
- [x] Otevření webu PrusaLink přes lokální proxy (T-028) funguje na macOS: E2E testy z T-030 (`tests/LayerPeek.E2E.Tests`) na macOS s nainstalovaným Edge projdou, bez Edge se korektně přeskočí a nic po sobě nenechají. `OwnProcesses.UnixCommandLine` zrychlit na jedno volání `ps -axww -o pid=,command=` (rozhodnuto uživatelem 2026-10-18).
- [x] Přeskočení E2E testů na macOS z T-095 (`EdgeFixture.MacOsSkipReason`, Edge se na macOS vůbec nespouští) je odstraněné a zastavení `requestAnimationFrame` v headless Edge na macOS je vyřešené (viz poznámky): E2E na macOS projdou stabilně, aspoň dva celé běhy `dotnet test` po sobě bez selhání (rozhodnuto 2026-10-29).
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- ~~Synchronizace přes iCloud Keychain~~ → ne, jen lokálně (bez `kSecAttrSynchronizable`); hesla tiskáren v LAN jinde nejsou potřeba (rozhodnuto 2026-10-29).
- ~~Dostupnost položky (`kSecAttrAccessible`) → `kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly`: čitelné po prvním odemčení i při zamčené obrazovce (monitorování po autostartu běží), jen tento Mac, nepřenáší se do zálohy / migrace (rozhodnuto 2026-10-29).~~ → **Revidováno 2026-10-29 (Q1):** souborový login Keychain (funguje s ad-hoc podpisem). Položka je čitelná, jen když je login Keychain odemčený, a je součástí záloh Time Machine i přenosu přes Migration Assistant. `kSecAttrAccessible` se v kódu nenastavuje. Data protection keychain jen s Developer ID (T-060, poznámka doplněna).
- ~~Limit velikosti~~ → stejný jako Windows, 2560 B UTF-8 (rozhodnuto 2026-10-29).
- Podpis aplikace (T-019) ovlivňuje, zda macOS po novém sestavení zobrazí dialog „povolit přístup do Keychainu“ – je to pro vývojová/ad-hoc sestavení přijatelné? Rozhodnuto 2026-10-21: i vydání budou mít jen ad-hoc podpis (bez Apple Developer ID), takže se dialog může objevit po každé aktualizaci, nejen ve vývoji. Riziko ověřit v tomto úkolu. **Stav 2026-10-29: neověřeno**, login Keychain byl při implementaci zamčený a zápis nešel (viz Q3). Očekávání podle modelu souborového Keychainu: ACL položky důvěřuje aplikaci, která ji vytvořila, podle jejího designated requirement. U ad-hoc podpisu je to cdhash, takže každé nové sestavení nebo aktualizace je pro Keychain jiná aplikace a první čtení po aktualizaci nejspíš zobrazí dialog „LayerPeek chce použít důvěrné informace uložené v ‚LayerPeek‘ ve vaší klíčence“ (Povolit / Vždy povolit / Zamítnout). Ověřit na odemčeném Macu dvěma různými sestaveními.
- ~~**Q1 – `kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly` nejde s ad-hoc podpisem uplatnit.**~~ Data protection keychain vyžaduje entitlements z provisioning profilu (Apple TN3137); sonda s `kSecUseDataProtectionKeychain` z ad-hoc programu vrátila `-34018` (`errSecMissingEntitlement`). → **Rozhodnuto 2026-10-29:** souborový login Keychain, atribut z kódu odstraněn, rozhodnutí o dostupnosti revidováno (viz výše), T-060 a `docs/architecture.md` doplněny.
- ~~**Q2 – Dialog Keychainu za běhu aplikace.**~~ Volání SecItem čeká, dokud uživatel neodpoví na dialog macOS (zamčený Keychain, „povolit přístup“ po aktualizaci). → **Rozhodnuto 2026-10-29:** dialog povolit, ale každé volání Keychainu musí běžet mimo UI vlákno, aby okna a menu v liště nezamrzla. Produkční kód interakci nikdy nevypíná, vypíná ji jen testovací fixture.
- ~~**Q3 – Testy se zápisem při zamčeném login Keychainu.**~~ V této relaci byl login Keychain zamčený (`SecKeychainGetStatus` = 2); `SecItemAdd` vrátil `-25308` (`errSecInteractionNotAllowed`), s vypnutými dialogy `-25293` (`errSecAuthFailed`); žádný dialog se neobjevil. → **Rozhodnuto 2026-10-29:** přeskočení testů se zápisem s jasným důvodem je přijatelné (obdoba „bez Edge“). Testy se skutečným zápisem je ale ještě potřeba spustit na odemčeném Macu.

## Poznámky k implementaci
**Z T-095 (2026-10-28/29, Edge 156, Playwright, macOS 27.2):** E2E testy s nainstalovaným Edge na macOS náhodně selhávaly po 20 s: v části běhů 1–9 testů, sériově (`--parallel none`) víc. Selhávalo čekání `WaitForFunctionAsync("window.e2e.ready")` v `BrowserSession.WaitUntilReadyAsync` a kliknutí na odkaz („waiting for element to be visible, enabled and stable“).
- Sonda v `BrowserSession.OpenAsync` při timeoutu ukázala, že stránka je hotová: `window.e2e = {"setUpAuthCalls":1,"statusRefreshes":1,"ready":true}`, všechny požadavky přes proxy vrátily 200, žádný selhaný požadavek ani chyba skriptu, `document.visibilityState = visible`, `hidden = false`, `hasFocus() = true`, v kontextu jedna stránka.
- `requestAnimationFrame` ale do 2 s nezavolal callback (`raf = False`). Playwright čeká na rAF ve výchozím pollingu `waitForFunction` i v kontrole „stable“ u kliknutí. Proxy ani aplikace to nezpůsobují.
- Samostatně spuštěný test projde. Selhávají až pozdější stránky / kontexty ve sdíleném prohlížeči.
- Nepomohlo: `--disable-features=MacWebContentsOcclusion`, `--disable-gpu-vsync --disable-frame-rate-limit`.
- Možné směry: polling intervalem (`WaitForFunctionAsync` s `PollingInterval`) a kliknutí bez čekání na rAF (zeslabení kontroly akceschopnosti, rozhoduje uživatel), nový prohlížeč pro každý test, jiná verze Edge / headless režimu, hlášení chyby Playwright / Edge.
- Do vyřešení se E2E na macOS přeskakují (T-095, rozhodnuto 2026-10-29). Edge se na macOS vůbec nespouští, protože první spuštění zakládá vlastní soubory v `~/Library` (viz `docs/architecture.md`, „Izolace testů na macOS“).


**T-021 (2026-10-29, macOS 27.2, Edge 156.0.4314.50, Playwright 1.63):** T-095 (závislost) je stále v `in-progress`, chybí jen ověření na Windows; uživatel požádal pokračovat.
- **Úložiště:** `src/LayerPeek.App/Platform/MacOS/KeychainSecretStore.cs` (`internal sealed`, `[SupportedOSPlatform("macos")]`). Interop je v `KeychainNative.cs`: jen `[LibraryImport]` do `Security.framework` a `CoreFoundation.framework` (absolutní cesty), konstanty `kSec*` / `kCF*` přes `NativeLibrary.GetExport`, bez reflexe a knihoven třetích stran, vhodné pro Native AOT. SecItem API s `kSecClassGenericPassword`, service = prefix (`LayerPeek`), account = klíč. Zastaralé `SecKeychain*` API v produkčním kódu není.
- **Kontrakt jako Windows:** striktní UTF-8 (neplatný Unicode → `ArgumentException` bez obsahu, neplatné UTF-8 při čtení → `SecretStoreException` bez vnitřní výjimky), limit 2560 B, chyby argumentů hned, zrušení a chyby OS přes `Task`. Chyba OS je `SecretStoreException` s OSStatus ve zprávě a vnitřní `KeychainException` (`Status` + text z `SecCopyErrorMessageString`), nikdy s tajným údajem. `errSecItemNotFound` → `null` / `false`. `SetAsync` = `SecItemAdd`, při `errSecDuplicateItem` `SecItemUpdate` jen dat.
- **Velikost písmen:** jediná normalizace `KeychainSecretStore.ToAccount` = `ToUpperInvariant()`. Je to stejná ekvivalence jako `StringComparer.OrdinalIgnoreCase` ostatních úložišť (test porovnává obě pro diakritiku, `ß`, `ı`). V aplikaci Klíčenka je proto účet vidět velkými písmeny (`PRINTER/MK4`).
- **Paměť:** otevřený text je jen ve `stackalloc` bufferu, který se vynuluje. Kopie pro Keychain je měnitelná `CFData`, kterou úložiště po volání vynuluje a uvolní. Data vrácená `SecItemCopyMatching` se po dekódování vynulují. Všechny CF objekty se uvolní ve `finally`.
- **Composition root:** `AppServices.CreateSecretStore`: macOS → `KeychainSecretStore()`, jiné OS → `UnsupportedSecretStore`. Testovací režim (T-044) používá na macOS vždy `TestModeSecretStore`. Přepínač testovacího prefixu z T-067 zůstává jen na Windows, takže testovací běh aplikace Keychain uživatele nepoužije. `PasswordPolicy.For` zná `KeychainSecretStore` (2560 B), takže pole hesla v nastavení je na macOS povolené. `WindowsCredentialSecretStore` beze změny.
- **Testy:** `tests/LayerPeek.App.Tests/Platform/MacOS/KeychainSecretStoreTests.cs` pokrývá stejné případy jako Windows a navíc velikost písmen, ekvivalenci normalizace, izolaci prefixů oběma směry a úklid včetně pozůstatku „spadlého“ běhu. `TestKeychain.cs`: služby jen pod `LayerPeek.Tests/<guid>`; hledání čte jen atributy (nikdy heslo); mazání odmítne cokoli mimo `LayerPeek.Tests` a rozlišuje velikost písmen jako Keychain. Assembly fixture (`CredentialCleanupFixture`) na macOS:
  - vypne dialogy Keychainu pro testovací proces (`SecKeychainSetUserInteractionAllowed(false)`, zastaralé API jen v testech; na konci je zase zapne),
  - smaže pozůstatky pod `LayerPeek.Tests` před prvním i po posledním testu,
  - zjistí, zda je login Keychain odemčený. Odemčený: zkušební zápis a smazání. Zamčený: testy se zápisem přeskočí (Q3).
- **Výsledek Keychainu v této relaci:** login Keychain byl zamčený a žádný dialog se neobjevil. Testy bez zápisu prošly (chybějící klíč, `DeleteAsync` → `false`, argumenty, zrušení, neplatný Unicode, limit, normalizace, ochrana prefixu, composition). Testy se zápisem se přeskočily (Q3). Proto nejsou odškrtnutá kritéria round-trip, přepis, velikost písmen, izolace, úklid, kontrola před/po s položkami a „zprávy bez tajného údaje“ (test cizí položky). Windows build a testy jsem na Windows nespouštěl.
- **E2E na macOS:** `EdgeFixture.MacOsSkipReason` je odstraněný, Edge se na macOS spouští jako na Windows. Zastavení rAF se v této relaci **nepodařilo vyvolat**: 3× samostatně, 3× sériově (`-parallel none`) a 3 celé běhy `dotnet test` prošly bez selhání, i s vypnutým displejem. Edge je stejná verze jako v T-095 (156.0.4314.50, nainstalovaná 25. 10.).
  - `BrowserSession.WaitUntilReadyAsync` testuje stejnou podmínku se stejným timeoutem, ale intervalem 50 ms (`PollingInterval`) místo přes rAF. Kontrola se tím nezeslabila. Na tomto čekání v T-095 selhávala většina testů (sonda tehdy ukázala `ready = true` při stojícím rAF).
  - Jediné kliknutí (`ProxyBoundaryTests`, odkaz na externí stránku) dál čeká na kontrolu „stable“ Playwrightu přes rAF. Kdyby se zastavení vrátilo, zůstávají možnosti z T-095: nový prohlížeč pro každý test, nebo kliknutí bez kontroly akceschopnosti (zeslabení, rozhoduje uživatel).
- **`OwnProcesses`:** na macOS jedno volání `/bin/ps -axww -o pid=,command=` pro všechny procesy místo jednoho `ps` na proces (Linux dál `/proc`, Windows `NtQueryInformationProcess`). Parser a snímek mají testy. E2E sada se zrychlila asi ze 4,8 s na 3,0 s.
- **Ověření:**
  - `dotnet build`: 0 varování, 0 chyb.
  - Dva celé běhy `dotnet test` po sobě (po posledních změnách): oba 7127 testů, 0 selhání, 6908 prošlo, 219 přeskočeno, E2E 21/21; 1 min 36 s a 1 min 44 s.
  - `dotnet publish src/LayerPeek.App -c Release -r osx-arm64`: úspěch, 0 varování MSBuild / trim / AOT (jen známé hlášky dsymutil / linkeru jako v T-095). Exe jsem nespouštěl.
  - `tools/profile-snapshot.ps1 -Compare` po všech bězích: žádná změna (Login Items „not checked“), v Keychainu žádná služba LayerPeek / Prusa. Soubory Edge v `~/Library` jsou známý pozůstatek.
- **Licence / AOT:** žádný nový balíček.

**Rozhodnutí Q1–Q3 (2026-10-29), zapracováno:**
- **Q1:** `kSecAttrAccessible` je odstraněný z kódu (`NativeKeychainItems.Write` i konstanty v `KeychainNative`). Položka má jen třídu, službu, účet a data. Doplněny poznámky v T-060 a v `docs/architecture.md` (odstavec „Keychain na macOS“).
- **Q2:** `KeychainSecretStore` hned na volajícím vlákně jen ověří argumenty, velikost a zrušení. Každé volání Keychainu (čtení, zápis, mazání) pak spustí přes `Task.Run` na thread poolu. Nativní volání jsou oddělená do `IKeychainItems` / `NativeKeychainItems` (nový soubor `NativeKeychainItems.cs`), aby šla v testu nahradit. Volající v aplikaci jsem prošel, všichni úložiště jen `await`ují a nikde se na výsledek synchronně nečeká:
  - `SettingsEditorStore`: načtení příznaku hesla, uložení, smazání při odebrání tiskárny, návrat při chybě uložení,
  - `DigestAuthenticator` (PrusaLink): monitorování, test připojení v nastavení, náhledy, webová proxy / `PrusaLinkWebGateway`.

  Díky přesunu uvnitř úložiště nezablokuje UI vlákno žádný z nich, ani pokud by volal z UI vlákna. `OpenSecretAsync` (`IUrlLauncher`) úložiště nepoužívá, jen otevírá adresu proxy. Produkční kód dialogy nevypíná (`SecKeychainSetUserInteractionAllowed` je jen v `TestKeychain`).
  - Testy: `KeychainSecretStoreThreadingTests` (App.Tests) s falešným `BlockingKeychainItems`, jehož volání blokuje jako otevřený dialog. Každá operace se vrátí hned s nedokončeným `Task`, nativní volání neběží na vlákně volajícího a chyby argumentů a zrušení zůstávají synchronní.
  - `KeychainSecretStoreUiThreadTests` (UiTests) volá úložiště na UI vlákně Avalonie. Zatímco je volání Keychainu zablokované, úloha poslaná na UI vlákno (`Dispatcher.UIThread.InvokeAsync`) proběhne a UI vlákno nečeká.
- **Q3:** přeskočení testů se zápisem při zamčeném login Keychainu s důvodem je přijatelné (rozhodnuto). Zbývá je spustit na odemčeném Macu, dokud se to nestane, zůstávají příslušná kritéria neodškrtnutá. Stejně tak zbývá ověřit dialog po aktualizaci ad-hoc sestavení.
- **Ověření po rozhodnutích Q1–Q3:**
  - `dotnet build`: 0 varování, 0 chyb.
  - Celý `dotnet test`: 7131 testů, 0 selhání, 6912 prošlo, 219 přeskočeno, E2E 21/21, 1 min 43 s. První běh po změně odhalil chybu v novém testu: porovnával id vláken, ale pool smí vlákno volajícího po `await` znovu použít. Test teď místo toho hlídá, zda nativní volání proběhlo uvnitř volání úložiště (`BlockingKeychainItems.FromCaller`).
  - AOT publish `osx-arm64`: 0 varování MSBuild / trim / AOT (jen známé hlášky dsymutil / linkeru), exe jsem nespouštěl.
  - `tools/profile-snapshot.ps1 -Compare`: žádná změna.

**Ruční kroky pro uživatele** (testy se zápisem na odemčeném Macu, heslo v produkční aplikaci, dialog Klíčenky po aktualizaci ad-hoc sestavení): souhrnně v [`docs/macos-manual-testing.md`](../../docs/macos-manual-testing.md), sezení 3 a 9.
