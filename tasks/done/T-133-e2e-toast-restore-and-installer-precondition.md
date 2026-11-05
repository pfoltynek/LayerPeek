---
id: T-133
title: E2E – návrat upozornění testovací aplikace, předpoklad testu instalátoru, aktivátor po AOT smoke
status: done
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
- [x] Po opravě projdou na skutečné ploše `TestNotificationTests`, `UpdateTests` (všechny 3 případy bez instalace
      uživatele) a `VelopackInstallerTests` – běh koordinuje hlavní agent s uživatelem
      → 2026-11-03: `TestNotificationTests` 1/1 PASS, `UpdateTests` případ `.zip` PASS; oba instalační případy
      `UpdateTests` přeskočené (nainstalovaná 0.4.0, uživatel ji odinstalovat nechtěl) a nahrazené ostrou aktualizací
      0.3.1 → 0.4.0 uživatelem (rozhodnutí uživatele 2026-11-03, T-125); `VelopackInstallerTests` PASS 2026-11-01.
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

### Rozhodnutí 2026-11-03 a oprava 3
Rozhodnutí uživatele: (1) krok 4 `TestNotificationTests` už ve Windows nic nevypíná, místo toho Debug přepínač
testovacího režimu, který aplikaci podstrčí `DisabledForApplication`; (2) nové testovací AUMID.

- **Debug přepínač** (`src/LayerPeek.App/Notifications/PretendedToastSetting.cs`, celý soubor jen `#if DEBUG`): singleton
  v DI; `ToastNotificationService.Pretended` (vlastnost jen v Debug) ho dostane jen v izolovaném testovacím režimu
  (`AppServices.CreateNotificationService`, větev testovacích toastů), produkce `null`. `ReadState` čte
  `Pretended?.Value ?? session.Setting`, takže okno Nastavení i zkušební upozornění dostanou `TurnedOff` stejnou cestou jako
  od Windows. Zapíná se **za běhu** existujícím kanálem testovacích příkazů (jako `settings`, `quit`, `crash:…`): nový
  Debug příkaz `notifications:off` / `notifications:on` (`InstanceCommandKind.Notifications`, argument jen `off`/`on`),
  zpracuje ho `App.OnInstanceCommand` jen při `IsIsolated` → `TrayHost.PretendNotificationsOff`. Aplikace to zaloguje
  („Test mode: Windows' notification setting of the app is pretended to be DisabledForApplication.“ / „… is read again.“).
  Nová proměnná prostředí ani restart nejsou potřeba. Release build typ, vlastnost ani příkaz nemá (unit test Release
  buildu kontroluje chybějící typ; `ReleaseBuildsKnowOnlyActivate` i `notifications:off`). AOT: žádná reflexe, jen
  `Volatile` int.
- **`TestNotificationTests` krok 4:** pošle `notifications:off`, počká na log, kliká na zkušební tlačítko do varování,
  ověří text varování, tlačítko do Nastavení (log testovacího režimu) a otevření `ms-settings:notifications` testem (jen
  když Nastavení neběží); ve `finally` pošle `notifications:on` (chyba se jen zapíše do reportu, aby nezakryla původní
  selhání) a pak ověří log návratu. Žádný zápis do registru Windows. Skutečné čtení nastavení Windows hlídají unit testy
  `StateOf` (`ToastTestNotificationTests`) a ruční kontrola – doplněno do T-125 u položky T-101.
- **Odstraněno (mrtvý kód):** `TestNotificationTests.TurnOffAsync` se zápisem `Enabled = 0` / `ToastEnabled = 0`,
  `WithNotificationsOff` a jeho unit test, `ToastRun.SwitchTestAppOnAsync`, `ToggleSettleTime`, veřejné
  `ToastRun.AppSettings` / `ToastsEnabled`, `ToastRunOptions.AllowNotificationsOff`, jednorázová obnova
  `TestAppNotificationRecovery` (soubor smazán) i s testem příkazu v dokumentaci.
- **Zůstává (bezpečnost):** předběžná kontrola `ToastRun.AssertNotificationsOnAsync` (čeká až 15 s na `Enabled`, při
  `Disabled*` hned selže; zpráva nově radí zvýšit číslo AUMID), `WithNotificationsOn` na začátku běhu, žurnálované změny
  klíče aplikace a `ToastEnabled`, most `SwitchOnBridge` při návratu, účinný stav ve snímku systému.
- **Nové AUMID `LayerPeek.E2E.2`:** v aplikaci jediná konstanta `AppEnvironment.TestAppUserModelId` (používá ji
  `AppUserModelId` procesu/oken, Windows toast platforma testovacího režimu, zástupce „LayerPeek E2E.lnk“ – jméno zástupce
  zůstává – a úklid `TestToastCleanup`); v E2E testech jediná konstanta `AppSwitches.AppUserModelId`
  (= `TestAppUserModelIdRoot + ".2"`; z ní `ToastSetting`, `ToastHistory`, `ToastAppSettingsKind.SubKey`, snímek
  `ToastSystemState`, `TaskbarJumpList`, `WindowChecks`, `TestModeOsIntegrationTests`). Nový test
  `TheTestAppIdOfTheTestsIsTheAppsOwn` čte `AppEnvironment.cs` a hlídá shodu obou konstant. Pojistky, které dřív
  vyžadovaly příponu `.E2E`, jsou nově `AppSwitches.IsTestAppUserModelId` (kořen `LayerPeek.E2E` sám nebo s příponou za
  tečkou, bez `\` a mezer) s unit testy. Unit testy aplikace používají konstantu, pevnou hodnotu má jen
  `ProductIdentityTests`. Testovací prefix pověření `LayerPeek.E2E/` a macOS bundle `…LayerPeek.E2E` se nemění (nejsou
  AUMID Windows).
- **Dokumentace:** `docs/troubleshooting.md` sekce „Testy upozornění selžou…“ přepsaná (co se stalo, Debug přepínač, nové
  AUMID, obnova = zvýšit číslo na obou místech + `ProductIdentityTests` + tato sekce); test
  `TheRecoveryOfTheTestAppsNotificationsIsDocumentedWithTheCurrentTestAppId` hlídá, že sekce uvádí aktuální AUMID a obě
  konstanty. Tabulka ručního návratu a „Stopy“ uvádějí nové AUMID; `tools/run-e2e.ps1` (nápověda) a varování druhu
  `Toasts` v `tools/RunE2E.SystemChanges.psm1` upravené (test testovacího upozornění už nic nevypíná).
- **Pozůstatky starého AUMID `LayerPeek.E2E` (jen čteno, nic jsem neměnil):** klíč
  `HKCU\…\Notifications\Settings\LayerPeek.E2E` ani `HKCU\Software\Classes\AppUserModelId\LayerPeek.E2E` neexistují,
  testovací zástupce ve Start menu není, složka žurnálu `%LOCALAPPDATA%\LayerPeek.E2E.Restore` neexistuje. Zůstává jen
  záznam „vypnuto“ v databázi WPN (`wpndatabase.db`) a případně položka v Nastavení → Oznámení – to spravuje Windows,
  testy ho nečtou ani nemění. Pozor: záznam žurnálu `ToastSettings` z doby před změnou by se vrátil do klíče nového AUMID
  (klíč druhu je pevný) – na tomto stroji žádný nečeká.
- Běh na ploše (`TestNotificationTests`, `UpdateTests`) po opravě 3 zatím neproběhl – koordinuje hlavní agent.

## Otevřené otázky
- ~~Když se ukáže, že Windows nebere ani `Enabled` 0 → 1 za běhu aplikace (obnova pak vždy skončí u ručního zapnutí
  v Nastavení a `TestNotificationTests` po každém běhu selže): má krok 4 místo skutečného vypnutí ve Windows použít
  Debug-only přepínač testovacího režimu, který aplikaci podstrčí `DisabledForApplication` (bez zásahu do Windows,
  ale neověří skutečné čtení nastavení Windows)? Nebo krok 4 nechat jen jako ruční kontrolu v T-125?~~
  **Rozhodnuto uživatelem 2026-11-03:** Debug přepínač testovacího režimu + nové testovací AUMID; skutečné vypnutí ve
  Windows → ruční kontrola v T-125 (provedení viz „Rozhodnutí 2026-11-03 a oprava 3“).

### Běh na ploše 2026-11-01 večer (hlavní agent)
- `VelopackInstallerTests` 1/1 PASS (předpoklad prázdné složky opraven).
- `TestNotificationTests`: zapnutí za běhu aplikace (`SwitchTestAppOnAsync`, i 0 → 1) **nezabralo** – Windows dál
  `DisabledForApplication`. Jednorázová obnova `TestAppNotificationRecovery` dvakrát (Nastavení otevřené, přepínač
  „LayerPeek E2E“ se nepodařilo zapnout, v seznamu nejspíš není) – stav dál vypnutý. Hypotéza „Windows převezme změnu
  za běhu aplikace“ neplatí.
- Úkol zůstává otevřený: najít spolehlivý způsob, nebo rozhodnout otázku níže (Debug přepínač místo skutečného vypnutí
  ve Windows). Do té doby `TestNotificationTests` a `UpdateTests` na tomto stroji selžou na předběžné kontrole se
  srozumitelnou zprávou.

### Běh na ploše 2026-11-03 (hlavní agent, po opravě 3)
- `pwsh tools/run-e2e.ps1 -Class '*TestNotificationTests'`: 1/1 PASS (Debug exe, 58 s), žurnál systémových změn prázdný.
  Nové AUMID `LayerPeek.E2E.2` Windows zapnulo, předběžná kontrola prošla.
- `pwsh tools/run-e2e.ps1 -Class '*UpdateTests'`: 1 PASS (`ZipVersionOpensTheReleasePageFromTheBannerAndTheMenuItem`),
  2 SKIP (`InstalledOlderVersionUpdatesFromALocalFeedAfterAClick` – na stroji je nainstalovaná 0.4.0). Uživatel rozhodl
  instalaci nechat a instalační případy uzavřít na základě ostré aktualizace 0.3.1 → 0.4.0 z GitHub Releases (klik na
  položku v menu, znovuspuštění `Update.exe`), viz T-125.
- `VelopackInstallerTests` se znovu nespouštěl (s instalací by se přeskočil); prošel 2026-11-01 po opravě 1.
