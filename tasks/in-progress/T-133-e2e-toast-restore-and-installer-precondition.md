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
- [x] Unit testy pomocníků (bez plochy) pro návrat nastavení a předpoklad instalátoru
- [ ] Po opravě projdou na skutečné ploše `TestNotificationTests`, `UpdateTests` (všechny 3 případy bez instalace
      uživatele) a `VelopackInstallerTests` – běh koordinuje hlavní agent s uživatelem
- [x] AOT smoke nenechá aktivátor upozornění ukazovat na sestavení (nebo zdůvodněno)
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Poznámky k implementaci
- **Účinný stav upozornění** (`Infrastructure/ToastSetting.cs`): `ToastNotifier.Setting` testovací AUMID `LayerPeek.E2E`
  přes Windows PowerShell 5.1 (jako `ToastHistory`; sdílený spouštěč `WindowsPowerShell.RunAsync`). Ověřeno jen čtením:
  hodnota je čitelná i bez zástupce ve Start menu (na tomto stroji teď `DisabledForApplication`, neznámé AUMID vrací null
  → `(unknown)`). Nastavení Windows jsem neměnil.
- **`ToastAppSettingsKind`**: `WithNotificationsOn` zapíše `Enabled` / `ShowBanner` = 1 (DWORD), i když hodnota nebo celý
  klíč chybí (dřív chybějící klíč nechal být). Návrat: když by úklid smazal přepínač, který klíč teď má (původně chyběl
  klíč nebo hodnota), zapíše se nejdřív „most“ (`SwitchOnBridge`: aktuální klíč s přepínači = 1), počká se 2 s a pak až
  15 s, než `ToastNotifier.Setting` hlásí `Enabled`, a teprve potom se zapíše přesný originál (smazání klíče). Když se
  `Enabled` do limitu neukáže, úklid stejně pokračuje (žurnál kontroluje klíč) – problém pak ukáže snímek stavu
  a předběžná kontrola příštího běhu. Ruční postup v `Describe` to zmiňuje.
- **Předběžná kontrola** v `ToastRun.StartCoreAsync` (po zástupci a aktivátoru): čeká až 15 s na `Enabled`; každá hodnota
  `Disabled*` (hlavně `DisabledForApplication`) hned selže se zprávou, co Windows drží a jak to zapnout ručně. Výsledek
  jde i do reportu běhu.
- **`SystemStateSnapshot`**: `ToastSystemState.TestAppSetting` (účinný stav) v textu snímku; změna z `Enabled` na
  `Disabled*` je rozdíl (selže běh), jiná změna (např. tento stroj: `DisabledForApplication` → `Enabled`) jen poznámka.
- **Instalátor**: `InstallerKind.StandInLog(original)` – zástupný log, kdykoli složka nemá žádný soubor (chybí, nebo
  existuje prázdná). Vždy jako nová položka nejvyšší úrovně, aby ji úklid smazal: `logs\LayerPeek.log`, a když už je
  prázdná podsložka `logs`, soubor `LayerPeek.E2E-stand-in.log` přímo ve složce. Aby návrat zůstal přesný: záloha
  (`CopyTree`) i obnova kopírují i prázdné podsložky a prázdná původní složka, kterou odinstalace smazala, se znovu
  vytvoří (dřív by `Matches` u prázdné složky selhal).
- **AOT smoke**: ano, `scripts/aot-smoke.ps1` registraci nevracel – exe zapíše svou cestu do
  `HKCU\Software\Classes\CLSID\{EC47413C-…}\LocalServer32` (na tomto stroji teď ukazuje na
  `D:\00-Source\LayerPeek-T124\artifacts\release\0.4.0\publish\win-x64\LayerPeek.exe`; nechal jsem to být, vrátí to
  spuštění nainstalované aplikace). Nově skript před startem uloží celý klíč (`Get-RegistryTreeState`, CLSID čte
  z `AppEnvironment.cs`) a ve `finally` ho vrátí přesně, nebo smaže, když nebyl (`Restore-RegistryTreeState`); selhání
  návratu = exit 1 se zprávou. Jako zástupce jen v paměti skriptu (ne v žurnálu): po pádu skriptu ho opraví další start
  nainstalované aplikace, která registraci při rozdílu přepíše sama. S `-SkipShellIntegration` (testy) se registr
  nečte ani nemění. Pester testy: CLSID z aplikace = CLSID testů E2E, přesný návrat přepsaného klíče (včetně
  `REG_EXPAND_SZ`), smazání nově vytvořeného, pořadí zálohy/startu/návratu ve skriptu; testy pracují jen ve vlastním
  klíči `HKCU\Software\LayerPeek.Scripts.Tests\<guid>` a mažou ho.
- Vedlejší (mimo rozsah): smoke exe zanechá i položku Exploreru v `NotifyIconSettings` (zapisuje ji Explorer, ne aplikace).
- Review (qa-reviewer): `ToastHistory.ReadAsync` nově používá stejný spouštěč `WindowsPowerShell.RunAsync` (po limitu proces
  ukončí; dřív zůstal běžet).
- Běh na ploše (`TestNotificationTests`, `UpdateTests`, `VelopackInstallerTests`) zatím neproběhl – koordinuje hlavní agent.

### Běh na ploše 2026-11-01 a oprava 2
- `VelopackInstallerTests` 1/1 prošel. `TestNotificationTests` a `UpdateTests` 3/3 selhaly na nové předběžné kontrole:
  klíč `Enabled=1, ShowBanner=1` (zapsaný před startem aplikace), Windows dál `DisabledForApplication`. Předpoklad
  „Windows převezme `Enabled = 1` z registru“ tím **neplatí** obecně: `Enabled = 0` zapsané **za běhu** testovací
  aplikace převzal (T-128, výsledky `aadd22ce`), smazání klíče po skončení aplikace ani `1` před jejím startem ne.
  Veřejné API pro zapnutí upozornění aplikace neexistuje (`ToastNotifier.Setting` je jen pro čtení); služby
  (`WpnUserService`) ani neveřejná API nepoužívám. Webový průzkum nebyl možný (vyhledávače vrací jen captcha/skripty),
  vycházím z pozorování.
- Hypotéza: služba bere zápis do klíče jen jako změnu, kterou vidí za běhu aplikace (se zástupcem). Proto:
  - `ToastRun.SwitchTestAppOnAsync` (za běhu aplikace): zapne `ToastEnabled`, když je vypnutý; zapíše
    `Enabled/ShowBanner = 1`, čeká 15 s; když to nestačí, zapíše `Enabled = 0`, 3 s, znovu `1` a čeká 15 s. Všechno
    přes žurnálované změny.
  - Předběžná kontrola: při `Disabled*` nejdřív `SwitchTestAppOnAsync`, teprve pak selže; zpráva odkazuje na obnovu.
    Když hypotéza platí, další běh se z dnešního stavu stroje vzpamatuje sám.
  - `TestNotificationTests`: krok 4 (vypnutí) je v `try/finally`, `finally` upozornění hned zapne **ještě za běhu
    aplikace** a test ověří `Enabled` (jinak selže s odkazem na obnovu). Trvalé „vypnuto“ tak už nezůstane potichu.
  - Most při návratu (`SwitchOnBridge`) zůstává jen jako poslední pokus (po skončení aplikace podle pozorování
    nepomáhá; když je stav `Enabled`, nic nečeká).
- Jednorázová obnova `TestAppNotificationRecovery` (spouští se jen s `LAYERPEEK_E2E_RECOVER_NOTIFICATIONS=1`, jinak se
  přeskočí): spustí testovací aplikaci, zkusí `SwitchTestAppOnAsync`, a když Windows dál drží „vypnuto“, otevře
  `ms-settings:notifications` (jen když Nastavení neběží) a až 5 min čeká, než uživatel zapne „LayerPeek E2E“. Postup je
  v `docs/troubleshooting.md` (nová sekce), test hlídá, že příkaz v dokumentaci odpovídá kódu.
- Globální `ToastEnabled` místo vypnutí aplikace jsem **nezvolil**: nejde ověřit, že Windows bere `ToastEnabled = 1`
  za běhu, a kdyby se choval stejně nesymetricky, zůstala by vypnutá upozornění **všech** aplikací uživatele (horší
  než dnes). Zůstává jen jako dosavadní záložní krok T-128.

## Otevřené otázky
- Když se ukáže, že Windows nebere ani `Enabled` 0 → 1 za běhu aplikace (obnova pak vždy skončí u ručního zapnutí
  v Nastavení a `TestNotificationTests` po každém běhu selže): má krok 4 místo skutečného vypnutí ve Windows použít
  Debug-only přepínač testovacího režimu, který aplikaci podstrčí `DisabledForApplication` (bez zásahu do Windows,
  ale neověří skutečné čtení nastavení Windows)? Nebo krok 4 nechat jen jako ruční kontrolu v T-125?
