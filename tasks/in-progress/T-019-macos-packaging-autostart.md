---
id: T-019
title: macOS – .app bundle, distribuce a autostart
status: in-progress
owner: ui-dev
priority: P1
platform: macos
depends_on: [T-008, T-015, T-055, T-056, T-095]
---

## Cíl
Spustitelná a distribuovatelná `.app` pro macOS bez ikony v Docku, spouštění po přihlášení.

Priorita P1, protože `.app` bundle s bundle identifikátorem je předpokladem pro macOS notifikace (T-020).

> **Pouze na macOS:** úkol implementuje a ověřuje uživatel později na Macu (Native AOT pro macOS lze sestavit jen na macOS, vyžaduje Xcode Command Line Tools – viz `docs/prerequisites.md`). Neblokuje žádný Windows úkol.

Rozhodnutí uživatele (2026-10-21):
- **RID:** jen `osx-arm64` (Apple Silicon). Žádný `osx-x64`, žádný universal binary (`lipo`).
- **Formáty:** přednostně instalátor, tedy **`.dmg`**, a k tomu archiv **`.tar.gz`**.
- **Podpis:** bez Apple Developer ID a bez notarizace, jen **ad-hoc podpis** (`codesign -s -`).
- **Sestavení a vydání:** lokálně na Macu skripty z T-055 (CI odloženo, T-016). Běh na Macu přidá soubory `osx-arm64` do stejného GitHub vydání, které vytvořil běh na Windows.

Rozhodnuto 2026-10-28:
- **Závislost na T-055:** T-019 může začít, i když je T-055 rozpracovaný a chybí jen zkušební vydání na GitHubu (stejná výjimka jako u T-017, rozhodnuto 2026-10-24).
- **Závislost na T-095:** začíná se až po zeleném buildu a testech na macOS (T-095).
- **Stav T-095 při začátku (2026-10-29):** T-095 je v `in-progress` a čeká už jen na ověření na Windows (build, testy a AOT na macOS jsou zelené). Uživatel požádal pokračovat s T-019.

## Rozsah
- Native AOT publish pro `osx-arm64`
- `.app` bundle s `Info.plist` (bundle identifikátor, `LSUIElement` – bez ikony v Docku), ad-hoc podpis celého bundlu včetně nativních knihoven (FFmpeg z T-026, až bude)
- Balicí kroky do bodu rozšíření skriptů T-055: `.app` → `.dmg` a `.tar.gz` s `.app` (místo dočasného `.tar.gz` publish složky z T-055)
- Pomocná okna otevřená z menu se u aplikace s `LSUIElement` musí dostat do popředí
- Autostart po přihlášení (Login Items)
- **Izolace testů (pravidlo v `docs/architecture.md`, „Izolace testů na macOS“):** v testovacím režimu (`LAYERPEEK_TEST_DATA_DIR`) autostart nikdy neregistruje skutečnou aplikaci přes `SMAppService` ani do `~/Library/LaunchAgents`; použije náhradu v testovací datové složce (obdoba složky `Startup` z `AppEnvironment.AutostartShortcutPath`, T-017). Testy po sobě nenechají Login Item ani LaunchAgent, ani po spadlém běhu. **Plný úklid (rozhodnuto 2026-10-28):** úklid na začátku každého běhu testů **a** Debug-only režim úklidu po pádu (obdoba `--layerpeek-e2e-clear-toasts`), který odregistruje testovací náhradu autostartu a skončí; na produkční registraci nikdy nesáhne.
- Dokumentace pro uživatele: jak spustit aplikaci staženou z internetu s ad-hoc podpisem (Gatekeeper; na macOS 15 a novějším přes Nastavení systému → Soukromí a zabezpečení → „Přesto otevřít“, případně odstranění atributu `com.apple.quarantine`)
- **Velopack pro macOS (podklad `docs/velopack.md`, bod 10, jen z dokumentace):** nejdřív na Macu ověřit, jestli Velopack jde použít i pro macOS:
  - `vpk pack` běží jen na Macu a vytvoří `.app` (s updaterem Velopacku), `.pkg` a `Portable.zip`. `.dmg` nevytváří. `.dmg` a `.tar.gz` by se sestavily z `.app` od `vpk`, jinak aktualizace nefungují;
  - `LSUIElement` a bundle identifikátor přes vlastní `--plist`;
  - chování s ad-hoc podpisem (Gatekeeper, karanténa po aktualizaci, `codesign --verify`) a aktualizace z GitHub Releases bez tokenu (`--channel osx-arm64`).

  Pokud Velopack na macOS nevyhoví, zůstává plán bez automatických aktualizací (`.app` + `.dmg` + `.tar.gz` vlastními nástroji).

  Převzato z T-056 (bod 10 rozsahu spiku, na žádost uživatele 2026-10-21 přesunuto sem, T-056 ho jen odkazuje): výsledek pokusu na Macu zapsat do `docs/velopack.md`, bod 10 (dnes „D, neověřeno“), včetně toho, zda aktualizace stažená Velopackem dostane atribut `com.apple.quarantine`, a zda updater v `/Applications` žádá o oprávnění správce.
- **Běh skriptů z T-055 na Macu (převzato z T-055, na žádost uživatele 2026-10-21; T-055 ho jen odkazuje):** na Windows ověřené jen Pester testy (`.tar.gz` přes `System32\tar.exe`, vynechání `.dSYM`). Na Macu ověřit skutečný běh `pwsh scripts/release-build.ps1` (výchozí RID `osx-arm64`): kontrola Xcode Command Line Tools přes `xcrun --find clang`, `/usr/bin/tar` s `--no-mac-metadata` a `COPYFILE_DISABLE=1`, vynechání `.dSYM` na skutečném buildu, `SHA256SUMS-macos.txt` (`shasum -a 256 -c`) a `release-upload.ps1` do existujícího konceptu vydání (postup v `docs/release.md`, „Běh na Macu“ a „Zkušební vydání (testovací repozitář)“, krok 4).
- **Další místa profilu, kam zapisuje Release exe (z T-095, rozhodnuto 2026-10-29):** se skutečnou `.app` a jejím bundle identifikátorem ověřit, co aplikace zapisuje mimo složky dat a logů: `~/Library/Preferences/<bundle id>.plist`, `~/Library/Saved Application State/<bundle id>.savedState`, `~/Library/Caches/<bundle id>` a případně další. Nalezená místa doplnit do `tools/profile-snapshot.ps1` (sledovat) a do zálohy a obnovy v `scripts/aot-smoke.ps1` (`Get-SmokeProfile` v `ReleaseTools.psm1`), včetně Pester testů nad zástupnou složkou.
- **Záložka „Spuštění a aktualizace“ v nastavení (z T-017, rozhodnuto 2026-10-24: řeší se tady):** na Windows nabízí autostart (jen nainstalovaná verze) a „Nabízet předběžné verze“. Na macOS rozhodnout: název záložky, jestli se má zobrazovat, autostart jako „Otevřít při přihlášení“ přes `SMAppService` (Login Items) místo zástupce ve Startup (`IAutostart` v App, dnes na macOS `null`) a aktualizace (Velopack pro macOS, nebo jen stránka vydání podle T-058). Dnes je záložka na macOS vidět jen s volbou předběžných verzí.

## Rizika ad-hoc podpisu
- **Gatekeeper:** stažená aplikace s ad-hoc podpisem (bez notarizace) se po prvním spuštění zablokuje a uživatel ji musí povolit ručně.
- **T-020 (notifikace):** `UNUserNotificationCenter` vyžaduje bundle a podpis. S ad-hoc podpisem nemusí notifikace fungovat nebo může macOS žádat o povolení znovu po každém sestavení. Ověřit v T-020.
- **T-021 (Keychain):** ad-hoc podpis se mění s každým sestavením, takže macOS může po každé aktualizaci znovu žádat o povolení přístupu k položkám Keychainu. Ověřit v T-021.

## Akceptační kritéria
- [x] Postup sestavení `.app`, `.dmg` a `.tar.gz` lokálně na macOS zdokumentovaný v `docs/release.md` (z T-055)
- [ ] Skript T-055 na Macu vytvoří `.dmg`, `.tar.gz` a `SHA256SUMS-macos.txt` a přidá je do existujícího GitHub vydání pro tag (ověřeno)
- [x] `tar -tzf` na `.tar.gz` z Macu neobsahuje položky AppleDouble `._*` (skript T-055 balí s `--no-mac-metadata` a `COPYFILE_DISABLE=1`, na Windows to nejde ověřit) ani `.dSYM` / `.pdb` (ladicí symboly se nevydávají, rozhodnutí 2026-10-21)
- [x] Kontrola Xcode Command Line Tools (`xcrun --find clang`) ve skriptu T-055 na Macu projde a `shasum -a 256 -c SHA256SUMS-macos.txt` souhlasí (převzato z T-055)
- [ ] Výsledek pokusu Velopack na macOS (ad-hoc podpis, Gatekeeper, karanténa po aktualizaci, aktualizace z GitHub Releases bez tokenu) je zapsaný v `docs/velopack.md`, bod 10, s doporučením ano / ne (převzato z T-056)
- [x] `codesign --verify --deep --strict` na `.app` projde (ad-hoc podpis)
- [ ] Aplikace běží bez ikony v Docku, ikona je jen v menu baru
- [ ] Okno Nastavení (T-015) otevřené z menu se zobrazí v popředí a dostane fokus
- [ ] Autostart po přihlášení funguje na macOS (ověřeno)
- [x] V testovacím režimu autostart zapisuje jen do náhrady v testovací datové složce, nikdy přes `SMAppService` / do `~/Library/LaunchAgents` (test)
- [x] Po `dotnet test` i po přerušeném běhu nezůstane žádný Login Item ani LaunchAgent; kontrola před/po podle `docs/architecture.md` bez rozdílu
- [ ] Spuštění stažené `.dmg` na Macu s Gatekeeperem ověřené a postup popsaný v dokumentaci
- [x] Místa profilu, kam Release `.app` zapisuje mimo složky dat a logů (`~/Library/Preferences/<bundle id>.plist`, Saved Application State, Caches…), jsou zjištěná a zapsaná v `docs/architecture.md`; `tools/profile-snapshot.ps1` je sleduje a `scripts/aot-smoke.ps1` je zálohuje a obnovuje (Pester testy nad zástupnou složkou); kontrola před/po kolem AOT smoke testu bez rozdílu
- [x] `docs/prerequisites.md` aktuální, pokud přibyly nástroje
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- ~~Podepisování a notarizace~~ → jen ad-hoc podpis (rozhodnuto 2026-10-21). Rizika pro T-020 a T-021 viz výše.
- ~~Podporovat i Intel Macy (`osx-x64`)~~ → ne, jen Apple Silicon `osx-arm64` (rozhodnuto 2026-10-21).
- ~~Formát distribuce~~ → `.dmg` + `.tar.gz` (rozhodnuto 2026-10-21).
- ~~Nástroj pro `.dmg`~~ → `hdiutil` ze systému, jednoduchý `.dmg` s aplikací a odkazem na Aplikace (rozhodnuto 2026-10-29).
- Automatické aktualizace – viz rozhodnutí v T-017 a výsledek T-056. T-056 Velopack pro macOS ani nedoporučil, ani nezamítl, protože podpis a notarizaci považuje dokumentace Velopacku za nutné a chování s ad-hoc podpisem nepopisuje. Rozhodne pokus na Macu v tomto úkolu (viz rozsah).
- ~~Může T-019 začít, i když T-055 je rozpracovaný?~~ → ano, chybí jen zkušební vydání na GitHubu, stejně jako u T-017 (rozhodnuto 2026-10-28).
- ~~Forma úklidu po pádu pro autostart~~ → plný úklid: na začátku každého běhu testů i Debug-only režim úklidu po pádu (rozhodnuto 2026-10-28).
- ~~„Otevřít při přihlášení“ v testovacím režimu~~ → ano, nabízí se, ale zapisuje jen náhradu v testovací datové složce, skutečné Login Items se nikdy nemění (rozhodnuto 2026-10-29).
- ~~Velopack na macOS a `.pkg`~~ → pokud pokus ukáže, že Velopack s ad-hoc podpisem funguje: `.dmg` + `.tar.gz` sestavené z `.app` od `vpk`, `.pkg` se nevydává (rozhodnuto 2026-10-29).
- ~~Záložka nastavení na macOS~~ → stejný název „Spuštění a aktualizace“, volba „Otevřít při přihlášení“ (termín macOS) a volba předběžných verzí; pokud Velopack na macOS nevyhoví, záložka bez volby předběžných verzí (rozhodnuto 2026-10-29). Nové texty do všech jazyků.
- Nové otázky z implementace (2026-10-29, ui-dev):
  1. **Bundle identifikátor (trvalé rozhodnutí).** Nikde nebyl rozhodnutý. Použil jsem návrh `io.github.pfoltynek.LayerPeek` (obrácené DNS adresy repozitáře `github.com/pfoltynek/LayerPeek`), na jednom místě: `build/macos/Info.plist` (a `AppEnvironment.ProductionBundleIdentifier`, hlídá unit test; skripty ho čtou ze šablony). Po prvním vydání se už nemá měnit: podle něj macOS vede předvolby, Login Item, oprávnění notifikací (T-020) a T-020 z něj odvozuje testovací `<id>.E2E`. Potvrdit, nebo určit jiný (např. vlastní doména).
  2. **Velopack na macOS teď, nebo později?** Balení přes `vpk` s ad-hoc podpisem funguje (`docs/velopack.md`, bod 10), ale samotná aktualizace (Gatekeeper, karanténa, heslo správce v `/Applications`, GitHub bez tokenu) lokálně ověřit nejde a v aplikaci ji zatím není čím spustit (T-058). Podle rozhodnutí 2026-10-29 jsem proto zvolil vlastní `.app` bez Velopacku a záložku bez předběžných verzí. Důsledek: kdo si nainstaluje `.app` z tohoto vydání, dostane aktualizace až po ruční reinstalaci verze s Velopackem. Varianty: (a) nechat takto a Velopack pro macOS vrátit s T-058 (nový úkol), (b) už teď balit `.app` od `vpk` (krok `app` by vzal `.app` z `Portable.zip`, do vydání přibyde `.nupkg` a feed `osx-arm64`) bez ověřené aktualizace.
  3. **Login Item vypnutý uživatelem v Nastavení systému** (`SMAppService` stav `requiresApproval`): aplikace ho bere jako vypnutý a zapnutí v nastavení LayerPeeku skončí hláškou „Spouštění po přihlášení se nepodařilo změnit“ (povolit ho jde jen v Nastavení systému → Obecné → Položky při přihlášení). Stačí to, nebo má aplikace v tom případě otevřít Nastavení systému (`SMAppService.openSystemSettingsLoginItems`), případně ukázat vlastní text?
  4. **Texty** `SettingsOpenAtLogin` a `SettingsNeedsAppBundle` (8 jazyků, termíny Applu podle nabídky Docku „Otevřít při přihlášení“): projít s ui-designerem. Důvod neaktivní volby říká „jen v LayerPeek.app přesunuté do složky Aplikace, ne ve vývojovém sestavení ani přímo z obrazu disku“; technicky stačí jakékoli trvalé místo (ne `.dmg`, ne App Translocation).
  5. **Okna kamery z menu baru** do popředí nepřivádím (změna je jen u Nastavení, Diagnostiky a O aplikaci, kritérium mluví o Nastavení). Má se aplikace aktivovat i při otevření okna kamery?
  6. **Ikona v Docku i ve vývojovém sestavení:** `ShowInDock = false` platí pro každý start na macOS, tedy i pro `dotnet run` (vývojové sestavení už nemá ikonu v Docku). Bez toho by Avalonia `LSUIElement` přebila. Je to v pořádku?
  7. **Malé velikosti ikony** (16–64 px) v `.icns` jsou zmenšené z 1024px předlohy, ne ze zjednodušené `app-icon-small.svg` (systém nemá převod SVG). Stačí, nebo má ui-designer dodat PNG malých velikostí?
  8. **Start bez aktivního displeje:** Release exe na tomto Macu spadl při spuštění, když byl displej uspaný (`Avalonia.Native was not able to start the RenderTimer. Native error code is: -6661`, tj. `kCVReturnInvalidDisplay`). Při běžném přihlášení displej svítí, ale může se to stát např. u Macu se zavřeným víkem bez monitoru nebo při přihlášení přes Sdílení obrazovky. Založit úkol (ošetření v aplikaci nebo hlášení do Avalonie)?

## Poznámky k implementaci

**Implementováno na Macu (2026-10-29, ui-dev; Apple Silicon, macOS 27.2, SDK `11.0.100-rc.3`, `pwsh` 7.6.9, Pester 6.2.0; nic necommitováno).** Žádný nový NuGet balíček (interop s Objective-C runtime a ServiceManagement přes `[LibraryImport]`, nástroje balení jsou součástí macOS).

**1. `.app`, `.dmg`, `.tar.gz` (krok `app` v `scripts/release-build.ps1`).** Velopack pro macOS se zatím nepoužívá (bod 4), `.app` sestavují vlastní funkce v `ReleaseTools.psm1`:
- `build/macos/Info.plist` (nová šablona): `CFBundleIdentifier` `io.github.pfoltynek.LayerPeek` (**návrh, otevřená otázka 1**), `LSUIElement` `true`, `CFBundleExecutable` `LayerPeek`, verze `@VERSION@` z `Directory.Build.props`, `LSMinimumSystemVersion` 14.0 (minimum AOT binárky), ikona `LayerPeek.icns`. Stejný identifikátor je v `AppEnvironment.ProductionBundleIdentifier` (unit test `AppBundleInfoPlistTests` i Pester).
- `New-MacIcon`: `.icns` z `app-icon-1024.png` (`sips` 16–1024 px, `iconutil`). `New-MacAppBundle`: `Contents/MacOS` = publish bez `.pdb`/`.dSYM` (`Get-ArchiveFiles`), `Info.plist`, `PkgInfo`, `Resources/LayerPeek.icns`.
- `Invoke-MacAdHocSign`: `codesign --force --sign - --timestamp=none` zevnitř ven (nejdřív `libAvaloniaNative`, `libHarfBuzzSharp`, `libSkiaSharp`, jejichž podpis Microsoftu se nahradí, nakonec bundle), bez hardened runtime. `Get-MacSignatureProblems` = `codesign --verify --deep --strict`.
- Smoke test samotného bundlu (`aot-smoke.ps1 -PublishDir LayerPeek.app/Contents/MacOS`), potom kontrola, že se bundle nezměnil a podpis platí.
- `.tar.gz` z `New-ReleaseArchive` nad složkou s `LayerPeek.app` (`--no-mac-metadata`, `COPYFILE_DISABLE=1`). `New-MacDiskImage`: `ditto` do pracovní složky, odkaz `Applications` → `/Applications`, `hdiutil create -fs HFS+ -format UDZO`, `hdiutil verify`.
- Krok `archive` je teď jen pro Windows (dřív `.tar.gz` publish složky i na macOS).

**2. „Otevřít při přihlášení“ (`IAutostart` na macOS).** `Platform/MacOS`: `ObjCRuntime` (`objc_msgSend` po jednom prototypu, autorelease pool), `SMAppServiceLoginItem` (`SMAppService.mainAppService`: `status`, `registerAndReturnError:`, `unregisterAndReturnError:`, chyba s kódem a textem `NSError`), `LoginItemAutostart` (zapnuto jen při stavu `enabled`; stav `requiresApproval` = vypnuto a zapnutí skončí chybou, otázka 3), `StandInLoginItem` (testovací režim: soubor `LoginItems/LayerPeek.loginitem` v testovací datové složce s cestou exe), `MacAppBundle` (exe v `…/X.app/Contents/MacOS/LayerPeek`, trvalé místo = ne App Translocation a ne svazek jen pro čtení, `access(W_OK)` → `EROFS`). `AppServices.CreateMacAutostart`: testhost nikdy nic; testovací režim vždy náhradu (rozhodnuto 2026-10-29); produkce jen aplikace v `LayerPeek.app` na trvalém místě, jinak volba neaktivní s důvodem `SettingsNeedsAppBundle`. Nikde se nezapisuje do `~/Library/LaunchAgents`.
- **Úklid (rozhodnuto 2026-10-28):** náhrada leží jen ve vyhrazených testovacích složkách, které se smažou na začátku každého běhu testů (`ReservedTempDirectory.SweepAsync`), a Debug-only režim `--layerpeek-e2e-clear-autostart` (`TestAutostartCleanup`, potřebuje `LAYERPEEK_TEST_DATA_DIR`) smaže náhrady autostartu macOS i Windows v testovací složce a skončí (0, jinak 1 s důvodem na stderr). Na `SMAppService` ani složku Po spuštění nesáhne; běží před logem, kontrolou jediné instance i Avalonií.

**3. Bez ikony v Docku, okna do popředí.** `Program`: `MacOSPlatformOptions { ShowInDock = false }` (Avalonia jinak při startu nastaví běžnou aplikaci s ikonou v Docku a `LSUIElement` přebije; otázka 6). `MacAppActivation.Activate()` (`[NSApp activate]`, záložně `activateIgnoringOtherApps:`; nic, když `NSApp` neexistuje, AppKit nenačítá) se volá před aktivací okna Nastavení, Diagnostiky a O aplikaci (`AuxiliaryWindowFactory` → `AvaloniaAuxiliaryWindow.Activate`) a v `MacWindowShell.RequestAttention` (druhé spuštění aplikace). Okna kamery beze změny (otázka 5).

**4. Velopack na macOS (podrobně `docs/velopack.md`, bod 10).** `vpk pack` na Macu vytvoří `.app`, `.pkg`, `Portable.zip`, `.nupkg` a feed `osx-arm64`, `.dmg` ne. `--plist` převezme `LSUIElement` i identifikátor. Bez `--signAppIdentity` `vpk` nepodepíše a `codesign --verify --deep --strict` selže; s `--signAppIdentity -` podepíše ad-hoc s hardened runtime a výchozími entitlements .NET a ověření projde. `.pkg` instaluje do `~/Applications` i `/Applications` a po instalaci aplikaci spustí. Aktualizaci, karanténu, Gatekeeper, heslo správce a GitHub bez tokenu lokálně ověřit nejde (a v aplikaci ji zatím nic nespouští, T-058). Doporučení: **zatím ne**, proto vlastní `.app` a na macOS záložka bez předběžných verzí (otázka 2).

**5. Záložka „Spuštění a aktualizace“ na macOS.** Stejný název. `SettingsViewModel.UsesMacTerms` / `ShowsPrereleaseOption` nastavuje `AppServices.CreateSettingsViewModel` podle OS: popisek `SettingsOpenAtLogin` („Otevřít při přihlášení“), důvod neaktivní volby `SettingsNeedsAppBundle`, volba předběžných verzí skrytá. Dva nové texty v 8 jazycích (otázka 4). Na Windows beze změny (snímky beze změny), UI test `StartupAndUpdatesTabFitsInEveryLanguage` má i variantu macOS (8 jazyků, nic se neořízne ani neroluje).

**6. Místa profilu podle bundle identifikátoru (z T-095).** Zjištěno smoke testem (exe v publish složce i uvnitř `LayerPeek.app`): aplikace zapisuje do **předvoleb** `~/Library/Preferences/<id>.plist` (mimo bundle `LayerPeek.plist`) klíče AppKitu `NSStatusItem VisibleCC Item-0` a `NSFullScreenMenuItemEverywhere`. `Saved Application State`, `Caches` ani `HTTPStorages` se při startu bez okna nevytvořily (s otevřeným oknem neověřeno, sledují a zálohují se přesto). Spuštění bundlu ho zaregistruje v LaunchServices.
- `tools/profile-snapshot.ps1`: sleduje `Preferences/<id>.plist`, `Preferences/LayerPeek.plist`, `Saved Application State/<id>.savedState`, `Caches/<id>`, `HTTPStorages/<id>` (i jednotlivé soubory) a řádky `lsregister -dump` s „LayerPeek“ (bez id záznamů). Pester: 4 nové testy.
- `aot-smoke.ps1` / `Get-SmokeProfile`: složky výše přejmenováním, domény `defaults` přes `defaults export` + `delete` a po běhu `delete` + `import` (nikdy jako soubor, plist drží `cfprefsd`). Zjištění: `defaults delete` nechá prázdný plist, ten skript smaže, když doména předtím neexistovala (první běh ho nechal, opraveno a uklizeno). Žurnál formátu 2 (čte i formát 1). Vypíše názvy klíčů, které exe zapsalo. Ověří typ procesu `UIElement` (`lsappinfo`) a odebere registraci bundlu v LaunchServices, kterou start vytvořil (`lsregister -u`, jen když předtím neexistovala). Pester nad zástupnou složkou se zástupným `defaults`: export/import existující domény, smazání nové domény přes `defaults`, selhaný export, přerušená obnova po importu, žurnál formátu 1, cizí doména v žurnálu, názvy klíčů.

**Ověřeno na Macu (2026-10-29):**
- `dotnet build`: 0 varování, 0 chyb. `dotnet test` (Debug): 7186 testů, 0 selhání, 219 přeskočeno (Windows-only, FFmpeg do T-026…), Pester všech 4 souborů v `ScriptPesterTestsPass` prošel (samostatně: `ReleaseTools.Tests.ps1` 175 prošlo, 1 přeskočeno; `profile-snapshot.Tests.ps1` 21 prošlo). Před/po (`tools/profile-snapshot.ps1`) kolem každého běhu testů: beze změny (Login Items „not checked“, rozhodnuto 2026-10-29).
- `pwsh scripts/release-build.ps1` (celý běh, fyzická cesta, výchozí RID `osx-arm64`): exit 0. Kontrola `xcrun --find clang` prošla, Release build 0 varování, Release testy 7053 / 0 selhání, AOT publish s `-warnaserror` bez varování, smoke test publish složky i `LayerPeek.app` prošel (typ `UIElement`, bez ikony v Docku), `codesign --verify --deep --strict`: valid. Výstup: `LayerPeek-0.2.0-osx-arm64.dmg` (21,3 MB), `LayerPeek-0.2.0-osx-arm64.tar.gz` (20,3 MB), `SHA256SUMS-macos.txt`. Před/po kolem celého běhu: beze změny.
- `shasum -a 256 -c SHA256SUMS-macos.txt`: oba soubory OK. `tar -tzf`: 8 souborů `./LayerPeek.app/…`, žádné `._*`, `.dSYM`, `.pdb`. Rozbalená `.app` z `.tar.gz` projde `codesign --verify --deep --strict`.
- `.dmg`: `hdiutil verify` VALID, UDZO, HFS+. Připojeno jen pro čtení (`-nobrowse -readonly -mountpoint` do scratchpadu) a odpojeno: `LayerPeek.app` + odkaz `Applications` → `/Applications`, podpis platí, `Info.plist` s identifikátorem, `LSUIElement` a verzí 0.2.0; `access(W_OK)` na aplikaci v obrazu vrací `EROFS` (= rozpoznání „z `.dmg`“).
- `pwsh scripts/release-upload.ps1 -DryRun` na Macu: `gh` funguje, skript správně odmítl (špinavý strom, HEAD bez tagu, `v0.2.0` na `origin` je jiný commit, HEAD není v `origin/main`, výstup ze špinavého stromu). Nic se nenahrálo.
- Smoke test zjistil: Release exe spadne, když je displej uspaný (`kCVReturnInvalidDisplay`, otázka 8). Běhy smoke testu proto proběhly s `caffeinate -d -u` (jen probudí a drží displej, nic nenastavuje).
- Jednou selhal v Release testech nestabilní test mimo T-019: `LayerPeek.PrusaLink.Tests.WebProxy.WebProxyLifecycleTests.ConcurrentRequestsDuringStopEnd` (`SocketException: Invalid argument` v `Socket.RemoteEndPoint` během zastavování proxy, macOS). V ostatních 5 bězích prošel. Neopravoval jsem ho (patří k T-095 / core-dev).

**Neověřeno (vyžaduje GUI, skutečné přihlášení, Gatekeeper nebo GitHub) – kroky pro uživatele:**
1. **Nahrání do konceptu (kritérium 2):** ve zkušebním repozitáři podle `docs/release.md`, „Zkušební vydání“: na Macu v čistém klonu na otagovaném commitu `pwsh scripts/release-build.ps1`, `pwsh scripts/release-upload.ps1 -Repo <owner>/LayerPeek-release-trial -DryRun` a pak bez `-DryRun` do konceptu, který vytvořil běh na Windows. Kontrola na GitHubu: `.dmg`, `.tar.gz`, `SHA256SUMS-macos.txt` vedle souborů Windows. Pak `release-publish.ps1 -Repo … -DryRun` bez `-AllowIncomplete` musí projít.
2. **Gatekeeper (kritérium 12):** stáhnout `.dmg` z konceptu prohlížečem (Safari), otevřít, přetáhnout LayerPeek do Aplikací, spustit. Zapsat přesné texty dialogu, ověřit Nastavení systému → Soukromí a zabezpečení → „Přesto otevřít“ a alternativu `xattr -dr com.apple.quarantine /Applications/LayerPeek.app`. Totéž pro `.tar.gz` (rozbalit ve Finderu). Spuštění přímo z `.dmg`: volba „Otevřít při přihlášení“ musí být neaktivní s důvodem.
3. **Dock a menu bar (kritérium 7):** spuštěná `/Applications/LayerPeek.app`: žádná ikona v Docku ani v přepínači aplikací (⌘Tab), ikona LayerPeeku je v menu baru (smoke test ověřil jen typ `UIElement`).
4. **Okno Nastavení (kritérium 8):** aktivní jiná aplikace (např. Finder s oknem), v menu baru LayerPeek → Nastavení: okno musí být nahoře a psaní jde do něj. Totéž s už otevřeným oknem schovaným za jinou aplikací a pro Diagnostiku a O aplikaci.
5. **Otevřít při přihlášení (kritérium 9):** v `/Applications/LayerPeek.app` zapnout volbu a Uložit; v Nastavení systému → Obecné → Položky při přihlášení se objeví LayerPeek (macOS může ukázat oznámení „Přidána položka při přihlášení“). Odhlásit a přihlásit: aplikace se spustí. Vypnout v LayerPeeku → položka zmizí. Navíc: položku vypnout v Nastavení systému a zkusit ji zapnout v LayerPeeku (očekávaná hláška „…nepodařilo změnit“, otázka 3).
6. **Velopack (kritérium 5), jen pokud se rozhodne pro variantu 2b:** vyžaduje kontrolu aktualizací v aplikaci (T-058), nebo testovací sestavení, které ji spustí; postup se doplní s rozhodnutím.
7. **Úklid po ručních zkouškách:** vypnout „Otevřít při přihlášení“, smazat `/Applications/LayerPeek.app`, `defaults delete io.github.pfoltynek.LayerPeek`, smazat `~/Library/Application Support/LayerPeek` a `~/Library/Logs/LayerPeek`, pokud tam nemají zůstat data.
