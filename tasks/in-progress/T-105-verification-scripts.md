---
id: T-105
title: Ověřovací skripty pro uživatele (tools/macos-verify.ps1, tools/windows-verify.ps1) místo ručních kroků
status: in-progress
owner: qa-reviewer
priority: P1
platform: all
depends_on: []
---

## Cíl
Ruční ověřovací kroky rozpracovaných úkolů převést na skripty, které spustí uživatel ve svém sezení, a rozpracované úkoly
pak uzavírat podle jejich výstupu (rozhodnuto uživatelem 2026-10-29). Mnoho „ručních“ kroků selhalo agentům jen kvůli
sandboxu (zamčená Klíčenka, bez TCC, bez uspání displeje, bez druhého uživatele); Terminál uživatele je zvládne.

## Rozsah
- `tools/macos-verify.ps1` a `tools/windows-verify.ps1` (pwsh, i jako dotnet global tool: podřízený pwsh přes
  `Get-PwshCommand` z `scripts/ReleaseTools`). Každá kontrola má id, úkol a kritérium, které uzavírá, a výsledek
  PASS / FAIL / SKIP (s důvodem) / MANUAL (potvrzeno y/n). Výstup: souhrn v konzoli a zpráva Markdown + JSON
  v `artifacts/verify/<čas>/` s důkazy, commitem a verzí aplikace.
- Režimy: interaktivní (výchozí; ptá se před každou částí s oprávněním a na lidský úsudek), `-Unattended` (nikdy se
  neptá, nic nepovoluje, části s oprávněním a ruční jako SKIP s důvodem, nenulový kód při FAIL), `-Only`, `-Skip`,
  `-ListChecks`, `-SelfTest` (zástupné kontroly, nic nemění).
- Společný engine `tools/VerifyTools.psm1` (výběr, běh, úklid, zpráva, parsery výstupu nástrojů), Pester testy
  `tools/VerifyTools.Tests.ps1`, xUnit `tests/LayerPeek.Scripts.Tests/VerifyScriptTests.cs`.
- Pomocný nástroj macOS `tools/macos-verify-helper.swift` (monitory, okna přes CGWindowList bez Screen Recording,
  Zpřístupnění, události klávesnice, NSBundle lokalizace, obrázek ikon); žádný nový kód aplikace, žádný NuGet balíček.
- Dokumentace: `docs/macos-manual-testing.md` nejdřív odkazuje na skript (tabulka kontrol), nový
  `docs/windows-manual-testing.md`, odkazy z rozpracovaných úkolů.

## Akceptační kritéria
- [x] Oba skripty s id, úkolem a kritériem každé kontroly, výsledky PASS / FAIL / SKIP / MANUAL, zpráva Markdown + JSON s důkazy, commitem a verzí
- [x] Interaktivní režim, `-Unattended` (bez dotazů a oprávnění, kód ≠ 0 při FAIL), `-Only`, `-Skip`, `-ListChecks`
- [x] `-Unattended -ListChecks` a `-Unattended -SelfTest` obou skriptů běží v `dotnet test` (xUnit + Pester) a nesahají na profil
- [x] macOS kontroly podle zadání (Keychain, Dock, menu bar, popředí, Gatekeeper, notifikace, čekání na displej, kamera, umístění oken, ikony, Info.plist, Zpřístupnění, sudo, uspání displeje, ruční kroky) s úklidem a kontrolou profilu před/po
- [x] Windows kontroly (build, test, AOT smoke, E2E, instalace/odinstalace s klíčem COM aktivátoru, klik na toast, autostart, T-102 jen po sloučení)
- [x] Dokumentace a odkazy z úkolů
- [ ] Skripty spuštěné uživatelem: macOS interaktivně na Macu a Windows na Windows, nalezené chyby skriptů opravené (agent je nemohl spustit, viz poznámky)
- [x] `dotnet build` (Debug i Release) bez varování, `dotnet test` bez selhání

## Otevřené otázky
1. Kontroly, které agent nemohl spustit (sandbox: spící displej, zamčená Klíčenka, nelze spustit `pwsh` přímo, Windows
   není k dispozici), jsou napsané podle API, ale neověřené: všechny macOS kontroly s oknem aplikace (`mac.dock`,
   `mac.menubar`, `mac.settings-front`, `mac.camera*`, `mac.keys`, `mac.display-sleep`, `mac.menu`), sudo, a celý
   `tools/windows-verify.ps1` mimo `-ListChecks` / `-SelfTest`. Úkol zůstává rozpracovaný do prvního běhu u uživatele.
2. Výsledek MANUAL znamená „uživatel potvrdil ano“; odpověď ne je FAIL. Stačí, aby se kritérium podle MANUAL zaškrtlo,
   nebo má správce úkolu chtít ještě výpis / snímek?
3. `mac.notification-click` ověřuje jen klik při běžící aplikaci (testovací bundle). Klik, když aplikace neběží, by
   macOS spustil testovací bundle bez testovacího režimu, tedy s produkčním profilem; zůstává v `mac.notifications-production`
   / ručně. Je to tak v pořádku?
4. `mac.keys` nečte vybranou záložku Nastavení (bez čtení přístupnosti Avalonie); přepnutí záložky ⌘⇧] potvrzuje uživatel.
5. Kontrola umístění (`mac.camera-placement`) bere rám okna z CGWindowList (s titulkovým pruhem) a toleranci ±2 body od
   16 bodů. Pokud se ukáže jiný výklad „okraje 16 DIP“ na macOS, upravit toleranci nebo pravidlo.

## Poznámky k implementaci
- **Engine (`tools/VerifyTools.psm1`):** kontrola = `New-VerifyCheck` (id `mac.*` / `win.*`, `-Closes` s ID úkolu, druh
  Auto / Permission / Manual, `-Final` pro kontrolu po úklidu). Permission se v interaktivním režimu ptá zvlášť, v
  `-Unattended` je SKIP bez dotazu; Manual v `-Unattended` SKIP; Auto, která potřebuje odpověď (`Confirm-VerifyStep`),
  v `-Unattended` SKIP. Výjimka = FAIL s `error.txt`, `Skip-VerifyCheck` = SKIP. Úklid (`Add-VerifyCleanup` se stavem
  místo closure, `-RunLast` pro sdílené fixture) běží vždy, i po Ctrl+C, a před kontrolami `-Final`. Výstup `dotnet test`
  se čte nezávisle na jazyce (řádky `[+n/xn/?n]`), skripty navíc nastaví `DOTNET_CLI_UI_LANGUAGE=en`.
- **macOS:** Debug aplikace v testovacím režimu jako `LayerPeek E2E.app` (stejné pravidlo Info.plist jako
  `MacNotificationsE2ETests`, ad-hoc podpis, `lsregister -f`, po běhu `--layerpeek-e2e-clear-toasts`, `lsregister -u`,
  `defaults delete`, místa `…E2E` v `~/Library`); spouští se jako potomek skriptu (zodpovědný proces je Terminál, takže
  bez dotazu na Místní síť), pro notifikace přes `open -n`. Příkazy přes socket instance (`settings`, `about`,
  `diagnostics`, `camera:<id>`, `quit`, `activate`). Simulátor na volných portech, ovládaný přes `/_simulator/`.
  Spící displej při startu testovací aplikace = SKIP s důvodem (aplikace čeká na displej, T-096). Profil:
  `profile-snapshot.ps1` (tečkou do funkce) před prvním a po úklidu; Login Items jen na vyžádání. Řízené ruční kroky
  s produkční aplikací nabídnou odsunutí profilu přes `Backup-SmokeProfile` / `Restore-SmokeProfile`.
- **Windows:** skutečná instalace odsune `%APPDATA%\LayerPeek` a celé `%LOCALAPPDATA%\LayerPeek` stejným žurnálem
  (vlastní soubor `%APPDATA%\LayerPeek.verify-install-journal.json`), uloží zástupce a cíle Správce pověření a
  `win.uninstall` vše vrátí; `-RestoreOnly` po přerušení. `win.e2e-installer` spouští existující `VelopackInstallerTests`.
- **Ověřeno agentem (macOS 27.2, 2026-10-29):** `-ListChecks` a `-SelfTest` obou skriptů, Pester `VerifyTools.Tests.ps1`
  (30 testů, 1 přeskočený jen pro Windows), na vyžádání `-Unattended` (`LAYERPEEK_VERIFY_UNATTENDED`): `mac.plist-localnetwork` PASS (8 jazyků),
  `mac.profile-unchanged` PASS, `mac.gatekeeper` SKIP (bez `.dmg`), `mac.notifications` SKIP (oprávnění „LayerPeek E2E“
  zamítnuté), `mac.dock` / `mac.menubar` SKIP (spící displej), `mac.keychain` FAIL (zamčená Klíčenka v sandboxu – správně
  podle zadání). Swift pomocník: `screens`, `trusted`, `localized`, `icons` vyzkoušené. Profil před/po beze změny.
- **Nové závislosti:** žádné (Swift a `swiftc` z Xcode Command Line Tools, které build na Macu už vyžaduje).
