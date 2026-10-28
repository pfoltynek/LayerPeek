---
id: T-019
title: macOS – .app bundle, distribuce a autostart
status: todo
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
- **Záložka „Spuštění a aktualizace“ v nastavení (z T-017, rozhodnuto 2026-10-24: řeší se tady):** na Windows nabízí autostart (jen nainstalovaná verze) a „Nabízet předběžné verze“. Na macOS rozhodnout: název záložky, jestli se má zobrazovat, autostart jako „Otevřít při přihlášení“ přes `SMAppService` (Login Items) místo zástupce ve Startup (`IAutostart` v App, dnes na macOS `null`) a aktualizace (Velopack pro macOS, nebo jen stránka vydání podle T-058). Dnes je záložka na macOS vidět jen s volbou předběžných verzí.

## Rizika ad-hoc podpisu
- **Gatekeeper:** stažená aplikace s ad-hoc podpisem (bez notarizace) se po prvním spuštění zablokuje a uživatel ji musí povolit ručně.
- **T-020 (notifikace):** `UNUserNotificationCenter` vyžaduje bundle a podpis. S ad-hoc podpisem nemusí notifikace fungovat nebo může macOS žádat o povolení znovu po každém sestavení. Ověřit v T-020.
- **T-021 (Keychain):** ad-hoc podpis se mění s každým sestavením, takže macOS může po každé aktualizaci znovu žádat o povolení přístupu k položkám Keychainu. Ověřit v T-021.

## Akceptační kritéria
- [ ] Postup sestavení `.app`, `.dmg` a `.tar.gz` lokálně na macOS zdokumentovaný v `docs/release.md` (z T-055)
- [ ] Skript T-055 na Macu vytvoří `.dmg`, `.tar.gz` a `SHA256SUMS-macos.txt` a přidá je do existujícího GitHub vydání pro tag (ověřeno)
- [ ] `tar -tzf` na `.tar.gz` z Macu neobsahuje položky AppleDouble `._*` (skript T-055 balí s `--no-mac-metadata` a `COPYFILE_DISABLE=1`, na Windows to nejde ověřit) ani `.dSYM` / `.pdb` (ladicí symboly se nevydávají, rozhodnutí 2026-10-21)
- [ ] Kontrola Xcode Command Line Tools (`xcrun --find clang`) ve skriptu T-055 na Macu projde a `shasum -a 256 -c SHA256SUMS-macos.txt` souhlasí (převzato z T-055)
- [ ] Výsledek pokusu Velopack na macOS (ad-hoc podpis, Gatekeeper, karanténa po aktualizaci, aktualizace z GitHub Releases bez tokenu) je zapsaný v `docs/velopack.md`, bod 10, s doporučením ano / ne (převzato z T-056)
- [ ] `codesign --verify --deep --strict` na `.app` projde (ad-hoc podpis)
- [ ] Aplikace běží bez ikony v Docku, ikona je jen v menu baru
- [ ] Okno Nastavení (T-015) otevřené z menu se zobrazí v popředí a dostane fokus
- [ ] Autostart po přihlášení funguje na macOS (ověřeno)
- [ ] V testovacím režimu autostart zapisuje jen do náhrady v testovací datové složce, nikdy přes `SMAppService` / do `~/Library/LaunchAgents` (test)
- [ ] Po `dotnet test` i po přerušeném běhu nezůstane žádný Login Item ani LaunchAgent; kontrola před/po podle `docs/architecture.md` bez rozdílu
- [ ] Spuštění stažené `.dmg` na Macu s Gatekeeperem ověřené a postup popsaný v dokumentaci
- [ ] `docs/prerequisites.md` aktuální, pokud přibyly nástroje
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- ~~Podepisování a notarizace~~ → jen ad-hoc podpis (rozhodnuto 2026-10-21). Rizika pro T-020 a T-021 viz výše.
- ~~Podporovat i Intel Macy (`osx-x64`)~~ → ne, jen Apple Silicon `osx-arm64` (rozhodnuto 2026-10-21).
- ~~Formát distribuce~~ → `.dmg` + `.tar.gz` (rozhodnuto 2026-10-21).
- Nástroj pro `.dmg`: `hdiutil` (součást macOS), nebo jiný nástroj? Velopack na macOS vytváří `.pkg`, ne `.dmg` (viz T-056) – pokud by se Velopack používal i pro aktualizace na macOS, je `.pkg` místo `.dmg` přijatelný?
- Automatické aktualizace – viz rozhodnutí v T-017 a výsledek T-056. T-056 Velopack pro macOS ani nedoporučil, ani nezamítl, protože podpis a notarizaci považuje dokumentace Velopacku za nutné a chování s ad-hoc podpisem nepopisuje. Rozhodne pokus na Macu v tomto úkolu (viz rozsah).
- ~~Může T-019 začít, i když T-055 je rozpracovaný?~~ → ano, chybí jen zkušební vydání na GitHubu, stejně jako u T-017 (rozhodnuto 2026-10-28).
- ~~Forma úklidu po pádu pro autostart~~ → plný úklid: na začátku každého běhu testů i Debug-only režim úklidu po pádu (rozhodnuto 2026-10-28).
- Má být „Otevřít při přihlášení“ v testovacím režimu vůbec nabízené?
- Pokud by se Velopack na macOS použil: má se nabízet i `.pkg` od Velopacku, nebo jen `.dmg` + `.tar.gz` sestavené z `.app` od `vpk`?

## Poznámky k implementaci
