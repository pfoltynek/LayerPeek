---
id: T-238
title: Testovací režim – volitelné skutečné otevírání odkazů přes /usr/bin/open pro `mac.open-fake` (T-053 L4)
status: todo
owner: ui-dev
priority: P3
platform: all
depends_on: [T-216]
---

## Cíl
Kontrola `mac.open-fake` (T-216) má ověřit, že aplikace otevírá odkazy přes `/usr/bin/open` (ne přes `open` z `PATH`,
T-053 L4) a že výchozí prohlížeč načte web PrusaLink přes proxy. Testovací bundle je ale Debug v izolovaném testovacím
režimu a ten adresy jen zapisuje do `opened-urls.txt` (`RecordingUrlLauncher`, T-067); `ProcessUrlLauncher` se
v kontrole vůbec nespustí, prohlížeč se neotevře a žádný požadavek na proxy nepřijde. Kontrola proto nikdy nemůže
skončit `PASS`.

## Zjištění (první běh s odemčenou obrazovkou, 2026-11-15)
- Běh `artifacts/verify/20261115-155430/` (commit `24d73aa3`): `FAIL` „the repository link did not bring the default
  browser (com.apple.safari) to the front …; the web proxy answered no request with 200 within 30 s after Open PrusaLink
  (answers: )“. Log aplikace (`evidence/mac.open-fake/app-vb986f349.log`): „RecordingUrlLauncher: Test mode: an address
  on github.com was recorded instead of opening the browser.“ a totéž pro 127.0.0.1.
- Od větve `feature/macos-verify-first-real-run` kontrola v tomto případě končí `SKIP` s důvodem (odkazuje sem),
  zapsanou adresu proxy načte sama (ověří proxy) a dál hlídá, že se falešné `open` nespustilo a licence se otevřou.

## Rozsah
- Debug sestavení: proměnná prostředí testovacího režimu (např. `LAYERPEEK_TEST_REAL_URL_LAUNCHER=1`), se kterou
  `AppServices` i v izolovaném režimu použije `ProcessUrlLauncher`. Release beze změny (testovací režim nemá).
- `mac.open-fake` proměnnou nastaví a `SKIP` větev zůstane jen pro starší sestavení.
- Test registrace (proměnná zapnutá / vypnutá) v `tests/LayerPeek.App.Tests`.

## Akceptační kritéria
- [ ] S proměnnou otevře Debug testovací režim odkazy přes `/usr/bin/open`, bez ní je zapisuje jako dosud.
- [ ] `mac.open-fake` = `PASS` s odemčenou obrazovkou (uzavře v T-127 bod T-053, oddíl 1q).
- [ ] `dotnet build` a `dotnet test` bez chyb a varování.

## Otevřené otázky
1. ~~Smí testovací režim dostat přepínač?~~ **Rozhodnutí uživatele 2026-11-15: ano** – přidat proměnnou testovacího režimu (Debug), se kterou se odkazy otevřou přes `/usr/bin/open` ve skutečném prohlížeči; Release beze změny.

## Poznámky k implementaci
(vyplní agent po dokončení)
