---
id: T-142
title: E2E test aktualizace na Macu selže se zapnutým displejem
status: done
owner: qa-reviewer
priority: P2
platform: macos
depends_on: [T-058, T-099]
---

## Cíl
Při testu developu na Macu (2026-11-01, develop a83ad13, v rámci T-115) selhal
`MacUpdateE2ETests.TheInstalledAppUpdatesItselfFromALocalFeedAndStaysSignedWithoutQuarantine` pokaždé, když byl displej
zapnutý (`caffeinate -d -u -i`, jak předepisuje `docs/macos-manual-testing.md`): „Waited 90 s for the updated app to start
its services.“ Se zhasnutým displejem prošel. Krok 4 testu čekal na dvě hlášky „Starting printer monitoring“, které
aplikace zapíše jen při čekání na aktivní displej (`DisplayWaitStartup`, T-099); při běžném startu se nezapíše nic
takového. Aktualizace sama proběhla správně (0.3.2 stažena, aplikace se ukončila, UpdateMac nahradil bundle a aplikace
se znovu spustila).

## Rozsah
- Krok 4 testu čeká na hlášku, kterou aktualizovaná aplikace zapíše při každém startu bez ohledu na stav displeje.
- Jen test; kód aplikace beze změny.

## Akceptační kritéria
- [x] Test projde se zapnutým displejem (`LAYERPEEK_E2E=1 caffeinate -d -u -i dotnet test …`), opakovaně
- [x] Test projde i při čekání na displej (dočasně `LAYERPEEK_TEST_NO_DISPLAY_MS` v proměnných aplikace, ověřeno ručně)
- [x] Test projde spolu s ostatními macOS E2E (`MacDisplayWaitE2ETests`, `MacNotificationsE2ETests`)
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
(žádné)

## Poznámky k implementaci
- 2026-11-01 (macOS 27.2, Apple Silicon): krok 4 teď čeká na „Update check: version 0.3.2 found by an earlier check is
  offered.“ – zapíše ji jen druhý (aktualizovaný) běh, při zapnutém i uspaném displeji. Nepoužitá pomocná metoda
  `Occurrences` odstraněna.
- Před opravou: se zapnutým displejem 5 / 5 selhání, se zhasnutým 5 / 5 průchodů (test vznikl v T-058 při zhasnutém
  displeji). Po opravě: se zapnutým displejem 3 / 3, s dočasně nastaveným `LAYERPEEK_TEST_NO_DISPLAY_MS=60000` 1 / 1
  (změna vrácena), všechny tři macOS E2E třídy dohromady 2 / 2 (oznámení přeskočena – „LayerPeek E2E“ nemá povolená
  oznámení). `dotnet build` 0 upozornění, `dotnet test` 8478 testů, 0 selhalo.
- Přečíslováno z T-128 na T-133: číslo T-128 mezitím dostal na Windows jiný úkol (E2E zkušebního upozornění
  a aktualizace). Commit opravy (abe7705) je ještě označený T-128.
- Přečíslováno 2026-11-02 z T-133 na T-142: číslo T-133 mezitím dostal na Windows jiný úkol (obnovení oznámení testovací
  aplikace a předpoklad instalátoru, `origin/develop` 0.4.0).
