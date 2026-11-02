---
id: T-140
title: macOS – kontroly s oprávněním bez uživatele (`-Allow`, sudoers, zámek obrazovky)
status: done
owner: qa-reviewer
priority: P2
platform: macos
depends_on: [T-105, T-136]
---

## Cíl
Uživatel 2026-11-02: „nejsi schopen si to pustit sám a zautomatizovat si to, ať se do budoucna o to nemusím starat já“.
Kontroly `mac.keys`, `mac.notifications`, `mac.socket-other-user` a `mac.display-sleep` má agent spouštět sám, bez otázek.
Rozhodnutí uživatele 2026-11-02: parametr `-Allow` (ne trvalý soubor souhlasu), úzké pravidlo sudoers, odložený zámek
obrazovky, spouštění jen na vyžádání (žádný launchd).

## Rozsah
1. `tools/macos-verify.ps1 -Allow <id,…>`: vyjmenované kontroly druhu Permission běží bez otázky i s `-Unattended`
   (motor `VerifyTools.psm1`, `Allowed` ve zprávě); vzor, který nesedí na žádnou kontrolu s oprávněním, skončí kódem 2.
2. `mac.keys`: otázka na přepnutí záložky (⌘⇧]) nahrazená čtením vybrané záložky z Accessibility; pomocník stiskne
   i klávesy modifikátorů; bez Zpřístupnění v `-Unattended` SKIP místo otázky.
3. `mac.socket-other-user`: `sudo -n -u nobody /usr/bin/nc`; heslo jen bez pravidla `tools/macos-verify-sudoers.sh`
   (`/etc/sudoers.d/layerpeek-verify`, jen `nc` jako `nobody`), v `-Unattended` bez pravidla SKIP s návodem.
4. `mac.display-sleep`: otázka na stav ikony nahrazená tooltipem položky v menu baru (AXHelp, „Paused“); zamčená
   obrazovka po probuzení = SKIP s návodem (Zamykací obrazovka).
5. Dokumentace `docs/macos-manual-testing.md` (jednorázová příprava).

## Akceptační kritéria
- [x] Bod 1 včetně Pester testu (`-Allow` spustí jen vyjmenovanou kontrolu s oprávněním, nic se neptá, ruční ne)
- [x] Bod 2: `mac.keys -Unattended -Allow mac.keys` běží; přepnutí záložky ověřené z Accessibility
- [x] Bod 3 a 4 implementované, `tools/macos-verify-sudoers.sh --print` projde `visudo -c`
- [x] Po jednorázové přípravě uživatele (sudoers, zámek, oznámení) proběhnou všechny čtyři kontroly agentem bez otázek
- [x] Pester `tools/VerifyTools.Tests.ps1` projde

## Otevřené otázky
(žádné)

## Poznámky k implementaci
- 2026-11-02 (macOS 27.2): `mac.keys` bez uživatele odhalil chybu aplikace – ⌘. nefunguje (T-141). Sonda AppKitu
  ukázala, že ⌘. přijde jako `performKeyEquivalent:` a pak `cancelOperation:`, ne jako `keyDown:` (⌘W a Esc ano).
  Kontrola proto správně hlásí `FAIL` (⌘. neopustí celou obrazovku, nezavře Nastavení), ostatní zkratky projdou;
  po neúspěšném ⌘. zavře Nastavení přes ⌘W, aby se další kroky hodnotily samostatně.
- Klávesa hned po přenesení aplikace do popředí nebo během animace celé obrazovky se ztratí – kontrola čeká 1 s.
- Avalonia nedává záložce hodnotu „vybráno“; vybraná záložka je ta, u které je ve stromu vidět `PART_SelectedPipe`
  (Fluent téma).
- Oznámení „LayerPeek E2E“ jsou v Nastavení systému zakázaná (`Denied`) – musí je jednou zapnout uživatel.
- 2026-11-02 po jednorázové přípravě uživatele (`sudo bash tools/macos-verify-sudoers.sh`, `sysadminctl -screenLock 3600`,
  oznámení „LayerPeek E2E“ povolená během běhu `mac.notifications`, který bundle nechá registrovaný, dokud uživatel
  nepotvrdí): agent sám `-Unattended -Allow …` – `mac.keys`, `mac.notifications`, `mac.socket-other-user` = `PASS`.
  `mac.display-sleep` = `SKIP`: Sdílení obrazovky drží displej vzhůru („Remote user active“ v `pmset -g assertions`),
  kontrola to teď pozná předem; ověření se spánkem displeje přesunuto do T-127 (rozhodnutí uživatele). Kritérium
  „všechny čtyři bez otázek“ je splněné v tom smyslu, že žádná se neptá; display-sleep jen nejde v relaci se Sdílením.
- Pester `tools/VerifyTools.Tests.ps1` 37/37.
