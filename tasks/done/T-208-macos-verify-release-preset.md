---
id: T-208
title: macOS ověření – jeden regresní příkaz `macos-verify.ps1 -Preset release` s jednou zprávou
status: done
owner: qa-reviewer
priority: P2
platform: macos
depends_on: [T-105, T-140]
---

> **Rozhodnuto uživatelem 2026-11-13:** zbylé body T-127 / T-126, které jdou automatizovat, se převedou na kontroly
> `tools/macos-verify.ps1` (úkoly T-208 až T-217). Bod sběrného úkolu pak zní „kontrola `mac.X` = `PASS` v poslední
> zprávě `-Preset release`“. Tento úkol je základ: jeden příkaz, jedna zpráva, pravidlo odškrtávání.

## Cíl
Dnes se kontroly pouštějí po skupinách (`-Only …`, různé `-Allow`) a výsledky se ručně opisují do T-127 / T-126.
Cílem je jeden regresní příkaz pro Mac, který bez dozoru spustí vše, co jde spustit bez člověka, a vydá jednu zprávu
se stavem každé kontroly. Ten se pak dá zopakovat před každým vydáním.

## Rozsah
- **`-Preset release`** v `tools/macos-verify.ps1`: pevný seznam kontrol `Auto` a `Permission`, které běží bez člověka.
  Přepínač v sobě má `-Unattended` a `-Allow` pro kontroly z presetu. Ty potřebují jen jednorázová oprávnění:
  Zpřístupnění pro Terminál, volitelně Záznam obrazovky, pravidlo sudoers z `tools/macos-verify-sudoers.sh`.
  Kontroly `Manual` v presetu nejsou. Seznam presetu je v jednom místě skriptu (tabulka id) a ukáže ho `-ListChecks -Preset release`.
  Pozdější úkoly (T-209 až T-217) do něj přidávají své kontroly.
- **Předběžné podmínky jednou na začátku** (ne až u každé kontroly): odemčená obrazovka na konzoli (`helper session`),
  Zpřístupnění (`helper trusted`), Záznam obrazovky (`helper screenrecording`, volitelně), Sdílení obrazovky
  vypnuté (u `mac.display-sleep`), `caffeinate` běží. Chybějící podmínka se vypíše jednou se seznamem kontrol, kterých se
  týká. Tyto kontroly skončí `SKIP` s důvodem, žádný `FAIL`.
- **Zpráva:** k dnešnímu `report.md` přibude strojově čitelný `report.json` (id, výsledek, důvod, kritéria, která
  kontrola uzavírá, doba trvání). Hlavička zprávy obsahuje commit (`git rev-parse HEAD`), „čistý strom ano / ne“, verzi
  aplikace, verzi macOS (`sw_vers`), model Macu, datum a použitý preset. `artifacts/verify/latest` je odkaz na poslední
  složku běhu. Žádná tajemství, cesty profilu zkrácené na `~`.
- **Šetrnost:** testovací bundle, helper a simulátor se sestaví nebo spustí jednou za běh a sdílí se mezi kontrolami
  (dnešní `Context.Shared`). Kontroly se řadí tak, aby se bundle nepřestavoval. Cíl: celý preset do ~30 min na Macu
  s Apple Silicon. Pokud to nevyjde, změřit a zapsat dobu běhu a nejpomalejší kontroly.
- **Pravidlo odškrtávání (do `tasks/README.md`, oddíl „Sběrné ověřovací úkoly“ a do T-127 / T-126):** bod, u kterého je
  uvedená kontrola, se odškrtne, když je kontrola `PASS` ve zprávě `-Preset release` na commitu, který obsahuje
  ověřovaný kód. Do poznámek sběrného úkolu se zapíše složka běhu, commit a výňatek řádku. Ruční zbytek bodu (je-li)
  zůstává jako samostatný podbod.
- `docs/macos-manual-testing.md`: krátký oddíl „Regresní běh před vydáním“ s příkazem
  `caffeinate -d -u -i pwsh tools/macos-verify.ps1 -Preset release` a s jednorázovou přípravou.
- Testy: `-SelfTest` ověří, že každé id presetu existuje, je `Auto` / `Permission` a má vyplněná kritéria. Pester
  (`tools/VerifyTools.Tests.ps1`) ověří tvar `report.json` a hlavičky (bez spuštění skutečných kontrol).

## Akceptační kritéria
- [x] `pwsh tools/macos-verify.ps1 -ListChecks -Preset release` vypíše seznam kontrol presetu; `-SelfTest` ho validuje
- [x] `caffeinate -d -u -i pwsh tools/macos-verify.ps1 -Preset release` proběhne bez jediné otázky a vytvoří
      `report.md` + `report.json` s hlavičkou (commit, čistý strom, verze aplikace, macOS, model, datum, preset) a odkaz
      `artifacts/verify/latest`
- [x] Chybějící předběžná podmínka (např. zamčená obrazovka nebo chybějící Zpřístupnění) se vypíše jednou na začátku
      a dotčené kontroly jsou `SKIP` s důvodem, ne `FAIL`
- [x] Pravidlo odškrtávání zapsané v `tasks/README.md` a v úvodu T-127 / T-126
- [x] Doba celého běhu změřená a zapsaná v poznámkách (se zamčenou obrazovkou; běh s odemčenou obrazovkou a splněnými
      podmínkami → přesunuto do T-127)
- [x] Pester testy (`tools/*.Tests.ps1`) a `dotnet build` / `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
Implementováno 2026-11-13 na Macu (qa-reviewer, macOS 27.2.1 26B131, Mac14,12; obrazovka zamčená, přes SSH).

- **Preset:** tabulka `$presets` v `tools/macos-verify.ps1` (jediné místo), `release` = 20 kontrol: všech 16 `Auto`
  (včetně `mac.profile-unchanged` po úklidu) a 4 `Permission` (`mac.keys`, `mac.update-check`, `mac.socket-other-user`,
  `mac.display-sleep`). Pořadí běhu = pořadí definic ve skriptu (build a testy první, testovací bundle, helper
  a simulátor se sestaví / spustí jednou přes `Context.Shared`). `-Preset` zapne `-Unattended` a `-Allow` pro kontroly
  `Permission` presetu; `-Skip` jde, `-Preset` s `-Only` skončí kódem 2, stejně jako neznámý preset.
  `-ListChecks -Preset release` vypíše jen kontroly presetu. `-SelfTest` ověří přes `Test-VerifyPreset`, že každé id
  existuje, je `Auto` / `Permission`, není dvakrát a má kritéria, a že každý název podmínky (`-Needs`) skript zná.
- **Předběžné podmínky:** nový parametr `New-VerifyCheck -Needs` (`Session`, `Accessibility`, `ScreenRecording`,
  `NoScreenSharing`; význam v tabulce `$preconditions` ve skriptu). V běhu bez dozoru (preset i `-Unattended`) se
  zkontrolují jednou na začátku (`helper session`, `trusted`, `screenrecording`, `pmset -g assertions`), chybějící se
  vypíše jednou se seznamem dotčených kontrol (`Format-VerifyUnmetNeeds`) a ty skončí `SKIP` s důvodem bez spuštění
  a bez otázky (i povolená `Permission`). Interaktivní běh nechává kontroly požádat o oprávnění samy jako dřív.
  `Session` mají `mac.menubar`, `mac.settings-front`, `mac.camera`, `mac.camera-placement`, `mac.camera-pin`,
  `mac.camera-outline`, `mac.keys`, `mac.update-check`, `mac.display-sleep`; `Accessibility` `mac.camera-pin`,
  `mac.camera-outline`, `mac.keys`, `mac.update-check`; `ScreenRecording` `mac.camera-outline`; `NoScreenSharing`
  `mac.display-sleep`. `mac.camera-placement` dostal `Session` po zjištění, že se zamčenou obrazovkou hlásí `FAIL`
  („top distance is 46 pt, expected 16 pt“, 6 z 20 umístění), zatímco v odemčené relaci 2026-11-01 prošel (T-115).
  `caffeinate`: když skript neběží pod `caffeinate`, spustí si sám `caffeinate -d -i -u -w <pid>` (zapsáno v hlavičce
  jako podmínka `Awake`); bez `-u` displej zůstal spát a `mac.dock` skončil `SKIP`.
- **Zpráva:** hlavička `report.md` / pole `Run` v `report.json`: začátek, doba běhu (`Seconds`), commit, větev, čistý
  strom ano / ne (`Dirty`), verze aplikace, macOS s buildem (`sw_vers`), model (`sysctl hw.model`, `Machine`), režim,
  preset, stav předběžných podmínek, vybrané a povolené kontroly; u každé kontroly id, výsledek, důvod, kritéria,
  sekundy, důkazy. Domovská složka je ve zprávě `~` (`Hide-VerifyHomePath`, `/Users/me2` zůstane). Odkaz
  `artifacts/verify/latest` (relativní symbolický odkaz, skutečnou složku `latest` nepřepíše; `Set-VerifyLatestLink`).
- **Oprava při ověření:** `Get-VerifyTestTotals` počítal u `dotnet test --solution` každé sestavení dvakrát (řádek
  průběhu i souhrn na konci: „total 21058, failed 2“ místo 10529 / 1); teď každé sestavení jednou, s testem.
- **Pester:** `tools/VerifyTools.Tests.ps1` – nové testy presetu (validace, výběr, `-Skip`), podmínek (SKIP bez běhu
  a otázky, výpis jednou), tvaru `report.json` a hlavičky, `~`, odkazu `latest`, doby a skriptu
  (`-ListChecks -Preset release`, neznámý preset, `-Preset` + `-Only`). Engine self-test má kontrolu s nesplněnou
  podmínkou. Pester 60 prošlo, 1 přeskočen (Windows).
- **Doba běhu (zamčená obrazovka, bez Zpřístupnění, se Sdílením obrazovky):** `caffeinate -d -u -i pwsh
  tools/macos-verify.ps1 -Preset release < /dev/null` – **12 min 19 s**, bez otázky, kód 1; běh
  `artifacts/verify/20261113-210821/` (worktree T-208). Výsledek PASS 7, FAIL 3, SKIP 10. Nejpomalejší: `mac.test`
  390 s, `mac.camera-placement` 195 s (teď `SKIP` na `Session`), `mac.aot-smoke` 42 s, `mac.display-wait` 39 s,
  `mac.build` 34 s. Plný běh s odemčenou obrazovkou → T-127 (oddíl 1i).
  - `FAIL mac.test`: nestabilní `CameraStreamHubKeyFrameTests.WithoutDueSubscribersNoKeyFrameIsWanted` (samostatně 3×
    prošel) → nový úkol **T-219**.
  - `FAIL mac.camera-placement`: zamčená obrazovka, viz výše (opraveno podmínkou `Session`; ověřený `SKIP` v běhu
    `20261113-212146`).
  - `FAIL mac.profile-unchanged`: 8 odebraných záznamů LaunchServices z `/private/tmp/claude-501/…/88f4d4f5…/scratchpad/`
    – registrace jiné souběžné relace agenta, ne tohoto běhu. Opakovaný kratší běh: `PASS`.
  - `SKIP` bez podmínky presetu: `mac.gatekeeper` (chybí `.dmg`, `scripts/release-build.ps1`), `mac.notifications`
    (oznámení „LayerPeek E2E“ `NotDetermined` – jednou interaktivně `-Only mac.notifications`).
- **Na vědomí:** `mac.keychain` s uzamčenou Klíčenkou zůstává `FAIL` (záměr z T-134: přeskočený test zápisu se
  nepočítá); není mezi podmínkami úkolu. Pravidlo odškrtávání je v `tasks/README.md` („Sběrné ověřovací úkoly“);
  v úvodu T-127 / T-126 bylo zapsané už architektem 2026-11-13.
- Bez nových balíčků a bez změny aplikace.
