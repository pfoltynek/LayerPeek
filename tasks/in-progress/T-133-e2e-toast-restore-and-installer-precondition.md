---
id: T-133
title: E2E – návrat upozornění testovací aplikace, předpoklad testu instalátoru, aktivátor po AOT smoke
status: in-progress
owner: qa-reviewer
priority: P1
platform: windows
depends_on: [T-128, T-017]
---

## Cíl
Při ověření T-124 (2026-11-01) selhaly `UpdateTests` 3/3 a `VelopackInstallerTests`. Rozbor (uchované výsledky
`LayerPeek.App.E2E.Results.5302b4a5-failed-20261101-160905` a `…-161245`, kopie `wpndatabase.db`):

1. **Upozornění testovací aplikace zůstala vypnutá.** `TestNotificationTests` (T-128) vypnul upozornění `LayerPeek.E2E`
   zápisem `Notifications\Settings\LayerPeek.E2E\Enabled = 0`; služba WPN to převzala do `wpndatabase.db`
   (`HandlerSettings s:toast = 0`). `ToastAppSettingsKind.RestoreCoreAsync` (`Infrastructure/ToastChanges.cs`) vrátil
   původní stav „klíč chybí“ jen smazáním klíče – databáze WPN zůstala na 0, žurnál hlásil „nic nezbylo“. Ochrana
   `WithNotificationsOn` chybějící klíč nechá být, takže další testy upozornění aplikaci znovu nezapnou. Aplikace
   správně loguje `DisabledForApplication` / chybu `0x803E0111`, test ale čeká 30 s na banner.
2. **Předpoklad testu instalátoru.** Skutečná odinstalace 0.3.1 nechala prázdnou složku `%LOCALAPPDATA%\LayerPeek`;
   `VelopackInstallerTests` zapíše zástupný log jen když složka neexistuje (`!change.Original.InstallFolderExists`),
   pak `Assert.NotEmpty(logsBefore)` selže ještě před instalací.
3. **Vedlejší zjištění:** produkční aktivátor upozornění (HKCU `LocalServer32`) ukazoval na
   `artifacts/release/0.4.0/publish/win-x64/LayerPeek.exe` – zaregistroval ho AOT smoke při `release-build.ps1`.
   Prověřit, zda `scripts/aot-smoke.ps1` registraci po sobě vrací (záloha profilu), a případně opravit.

## Rozsah
1. Návrat nastavení upozornění testovací aplikace tak, aby platil i v databázi WPN (např. při původně chybějícím klíči
   nejdřív zapsat `Enabled = 1` / `ShowBanner = 1`, počkat na převzetí, pak klíč smazat); `WithNotificationsOn` zapne
   aplikaci i při chybějícím klíči; účinný stav (`ToastNotifier.Setting` testovací AUMID) v `SystemStateSnapshot`
   a předběžná kontrola v `ToastRun`, která při `DisabledForApplication` hned selže se srozumitelnou zprávou.
2. `VelopackInstallerTests`: zástupný log i pro existující prázdnou instalační složku; úklid dál vrátí přesně původní
   stav.
3. AOT smoke: aktivátor upozornění po běhu v původním stavu (nebo zdůvodnit, proč ne).

## Akceptační kritéria
- [ ] Unit testy pomocníků (bez plochy) pro návrat nastavení a předpoklad instalátoru
- [ ] Po opravě projdou na skutečné ploše `TestNotificationTests`, `UpdateTests` (všechny 3 případy bez instalace
      uživatele) a `VelopackInstallerTests` – běh koordinuje hlavní agent s uživatelem
- [ ] AOT smoke nenechá aktivátor upozornění ukazovat na sestavení (nebo zdůvodněno)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
(žádné)
