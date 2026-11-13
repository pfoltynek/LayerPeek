---
id: T-208
title: macOS ověření – jeden regresní příkaz `macos-verify.ps1 -Preset release` s jednou zprávou
status: todo
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
- [ ] `pwsh tools/macos-verify.ps1 -ListChecks -Preset release` vypíše seznam kontrol presetu; `-SelfTest` ho validuje
- [ ] `caffeinate -d -u -i pwsh tools/macos-verify.ps1 -Preset release` proběhne bez jediné otázky a vytvoří
      `report.md` + `report.json` s hlavičkou (commit, čistý strom, verze aplikace, macOS, model, datum, preset) a odkaz
      `artifacts/verify/latest`
- [ ] Chybějící předběžná podmínka (např. zamčená obrazovka nebo chybějící Zpřístupnění) se vypíše jednou na začátku
      a dotčené kontroly jsou `SKIP` s důvodem, ne `FAIL`
- [ ] Pravidlo odškrtávání zapsané v `tasks/README.md` a v úvodu T-127 / T-126
- [ ] Doba celého běhu změřená a zapsaná v poznámkách
- [ ] Pester testy (`tools/*.Tests.ps1`) a `dotnet build` / `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
