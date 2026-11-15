---
id: T-201
title: Přeinstalace (winget upgrade) nemá znovu zapnout spouštění po přihlášení
status: done
owner: ui-dev
priority: P1
platform: windows
depends_on: []
---

## Cíl
Hook instalace Velopacku (`--veloapp-install`) dnes při **každé** instalaci zapne spouštění po přihlášení (výchozí zapnuto,
rozhodnutí uživatele 2026-10-26). Velopack ale stejný hook spustí i při **přeinstalaci přes existující instalaci** – a
přesně to dělá `winget upgrade` (manifest `UpgradeBehavior: install`, nový `Setup.exe --silent`, T-059). Uživatel, který si
autostart vypnul, ho tak po `winget upgrade` (i v rámci `winget upgrade --all`) má zase zapnutý. Týká se všech uživatelů
1.0.0, kteří instalují přes winget, proto P1. Aktualizace v aplikaci (Velopack `apply`) hook instalace nespouští a volbu
nemění.

Zjištěno v T-059 (ověřeno ve zdroji Velopacku 1.2.161, ne pokusem, `docs/release.md` „Velopack a `winget upgrade`“);
rozhodnutí uživatele 2026-11-11: opravit hook v samostatném úkolu.

## Současný stav (kód)
- `src/LayerPeek.App/Installer/VelopackStartup.cs` – `OnAfterInstallFastCallback` → `InstallerHooks.AfterInstall()`
  (jen Windows, produkční identita, `AppServices.CreateInstallerHooks`).
- `src/LayerPeek.App/Installer/InstallerHooks.cs` – `AfterInstall()` zaregistruje toasty a bezpodmínečně volá
  `autostart.SetEnabled(true)`; komentář tvrdí „Only a new install runs this hook, so an update keeps the user's
  choice“ – pro přeinstalaci to neplatí.
- Stav autostartu se nikde neukládá: je to jen existence zástupce `LayerPeek.lnk` ve složce Po spuštění
  (`src/LayerPeek.App/Platform/Windows/StartupShortcutAutostart.cs`, `IAutostart`). Vypnutí v nastavení zástupce smaže,
  takže hook při přeinstalaci nepozná „uživatel vypnul“ od „nová instalace“.
- Přeinstalace nahradí celou `%LOCALAPPDATA%\LayerPeek`; zachová se `%APPDATA%\LayerPeek` (nastavení,
  `settings.json`, `JsonSettingsStore` v Core) a zástupce ve složce Po spuštění (leží mimo instalaci). Odinstalace
  (`BeforeUninstall`) zástupce smaže, nastavení ponechá.

## Rozsah (navržené řešení, k potvrzení – viz otázky)
- **Varianta A (doporučená): uložit výslovnou volbu uživatele.** Nové pole v `AppSettings` (např. `bool? StartAtLogin`,
  `null` = uživatel nikdy nevolil), zapisované okno nastavení při uložení přepínače (Windows; na macOS se pole nepoužívá
  nebo se taky jen zapisuje). Hook `AfterInstall` načte nastavení (`JsonSettingsStore` jen pro čtení, bez migrací a
  bez zápisu, v časovém limitu fast callbacku) a autostart zapne jen když volba není `false`. Nečitelné nebo chybějící
  nastavení = zapnout (dnešní chování nové instalace). Výsledek: nová instalace zapne, přeinstalace i odinstalace +
  nová instalace respektují vypnutí.
- **Varianta B:** autostart zapnout jen když `%APPDATA%\LayerPeek\settings.json` ještě neexistuje (první instalace
  u účtu). Bez změny schématu, ale po odinstalaci a nové instalaci (nastavení zůstává) se autostart už nezapne, ani když
  ho uživatel měl zapnutý, a stejně tak u uživatele, který dřív používal `.zip`.
- Opravit komentáře v `InstallerHooks` a `docs/velopack.md` (bod 3, „běží jen při nové instalaci“), `docs/release.md`
  („Velopack a `winget upgrade`“: odrážka o autostartu) a podle výsledku T-059 kontrolu po instalaci.
- Kdyby oprava nestihla první vydání s winget (1.0.0), doplnit do kapitoly 12 všech 8 příruček (`manuals/*/manual.md`,
  odrážka „Windows, installed with winget“), že `winget upgrade` zapne autostart znovu.

## Akceptační kritéria
- [x] Přeinstalace přes existující instalaci (`Setup.exe --silent`, `winget upgrade`) nezapne autostart, když ho
      uživatel vypnul; když ho měl zapnutý, zůstane zapnutý
- [x] Nová instalace bez předchozí volby autostart zapne (beze změny, rozhodnutí 2026-10-26)
- [x] Unit testy `InstallerHooksTests` (falešné `IAutostart` a úložiště nastavení): nová instalace, přeinstalace po
      vypnutí, přeinstalace po zapnutí, nečitelné nastavení, selhání autostartu jen do logu; testy okna nastavení, že
      přepínač volbu uloží (varianta A)
- [x] Ruční ověření na Windows: nainstalovat, vypnout autostart, spustit tentýž / novější `Setup.exe --silent`
      (nebo `winget install --manifest` podruhé) → autostart zůstane vypnutý (lze přesunout do T-124) → přesunuto do T-124
- [x] Dokumentace (`docs/velopack.md`, `docs/release.md`) odpovídá novému chování
- [x] `dotnet build` a `dotnet test` bez chyb a varování (build 0 varování, testy dotčených projektů prochází; zbylých 47
      selhání celého řešení existuje už na `2ce7be01` bez změn T-201 a sleduje je T-227 – rozhodnutí uživatele 2026-11-12)

## Otevřené otázky
Rozhodnuto uživatelem 2026-11-12: 1 **varianta A** (uložená volba v nastavení); 2 po odinstalaci a nové instalaci
**respektovat dřívější vypnutí** (plyne z A); 3 volba se **neexportuje** v diagnostickém balíčku ani exportu nastavení,
na macOS se jen zapisuje (hook tam neexistuje); 4 závislost na T-059 **zrušena**, oprava má stihnout 1.0.0.

Další rozhodnutí uživatele 2026-11-12 (po kontrole qa): **jednorázová migrace**, aby vypnutí z verzí 0.x přežilo první
přeinstalaci. Při startu na Windows, jen v instalaci Velopacku s produkční identitou (ne vývojové sestavení, `.zip`,
testovací režim ani macOS): když volba `StartAtLogin` ještě uložená není a autostart právě **není** zapnutý, jednou uložit
`StartAtLogin = false`; když zapnutý je, neukládat nic. Levně, mimo kritickou cestu UI, chyby jen do logu, zástupce se
nemění. Úkol se smí uzavřít i se 47 selháními, která nesouvisí s T-201 (sleduje T-227); T-124 zůstává znovu otevřený.

1. **Varianta A (uložená volba v nastavení) nebo B (zapnout jen bez existujícího `settings.json`)?** Doporučení: A.
2. Po **odinstalaci a nové instalaci** (nastavení zůstává): respektovat dřívější vypnutí (A), nebo autostart zapnout
   jako u úplně nové instalace?
3. Má se uložená volba exportovat v diagnostickém balíčku / exportu nastavení (`SettingsExport`) a platit i na macOS
   (tam je autostart ve výchozím stavu vypnutý a hook instalace neexistuje)?
4. **Závislost na T-059:** T-059 bude hotový až po prvním odeslání do winget po vydání 1.0.0, takže by se T-201
   podle pravidel nesměl začít dřív a oprava by do 1.0.0 nestihla. Oprava sama na T-059 technicky nezávisí (týká se
   i ruční přeinstalace přes `Setup.exe`). Zrušit závislost, aby šlo opravit před 1.0.0?

## Poznámky k implementaci
ui-dev, 2026-11-12, Windows 11 Pro 10.0.26300 (varianta A podle rozhodnutí uživatele 2026-11-12):
- **Core:** nové `AppSettings.StartAtLogin` (`bool?`, `null` = uživatel nikdy nevolil), součást rovnosti; v `settings.json`
  jako `startAtLogin` jen po výslovné volbě (`WhenWritingNull`, jen zdrojově generovaný `SettingsJsonContext`, AOT beze
  změny). `SettingsExport.ToSupportJson` hodnotu vynechá (otázka 3). `JsonSettingsStore.ReadStartAtLoginChoice()`:
  jedno synchronní čtení pro hook (bez opakování, bez převodů mapperu, bez zápisu, žádný dočasný soubor); bez souboru
  `null`, poškozený JSON = `JsonException`, příliš velký soubor = `InvalidDataException`. Po kontrole qa čte jen kořenovou
  hodnotu `startAtLogin` (`Storage/StartAtLoginValue`, `Utf8JsonReader`, limit 1 MiB zůstává), takže chybný typ jiné
  hodnoty uložené „vypnuto“ neztratí; chybný typ samotné `startAtLogin` = nečitelné (`JsonException`).
- **Migrace (rozhodnutí 2026-11-12):** `AutostartChoiceMigration` na pozadí (`ServiceHost.StartMonitoring`, `Task.Run`, zrušení
  při ukončení), jen když `AppServices.CreateAutostartChoiceMigration` vrátí instanci: Windows, ne testovací režim
  (`IsIsolated`), existuje autostart (ten `CreateAutostart` dává jen exe uvnitř instalace Velopacku, ne `.zip`,
  vývojovému sestavení ani testhostu) a úložiště nastavení je `JsonSettingsStore`. Nejdřív levné čtení volby (od druhého
  startu konec), pak `IsEnabled()`, pak `JsonSettingsStore.SetStartAtLoginChoiceIfUnsetAsync(false)`: pod zámkem úložiště
  znovu ověří, že volba chybí, a přidá jen `"startAtLogin": false` za poslední kořenovou hodnotu (odsazení a konce řádků
  podle souboru, zbytek bajt po bajtu); bez souboru zapíše výchozí nastavení s volbou; soubor s hodnotou, neplatný,
  příliš velký, bez objektu nebo novějšího formátu nechá být. Zástupce se nikdy nemění, chyby jen do logu (příští start
  to zkusí znovu). Uloží-li okno nastavení souběžně starší stav bez volby, migrace se zopakuje při dalším startu.
- **Okno nastavení:** `SettingsViewModel` při uložení zapíše volbu, jen když uživatel přepínač v okně změnil (stejně jako
  „Nabízet předběžné verze“); přepnutí tam a zpět volbu nemění, jiné uložení nechá uloženou volbu beze změny. Volba se
  uloží i když OS přepnutí odmítne (zůstává to volba uživatele; další Uložit přepne autostart znovu bez dalšího zápisu
  souboru). Stav, který okno jen převzalo z OS (macOS, Login Items v Nastavení systému), volbou není. Přepnutí samotného
  autostartu se tím zapisuje i do `settings.json` (dřív jen stav OS).
- **Hook instalace:** `InstallerHooks` dostal `IAutostartChoiceSource` (produkčně `SettingsFileAutostartChoice` nad
  `%APPDATA%\LayerPeek`, `AppServices.CreateInstallerHooks`). Bez volby nebo „zapnuto“ → `SetEnabled(true)`;
  „vypnuto“ → autostart nechá být (nezapne ani nevypne, do logu Velopacku informace); nečitelné nastavení → zapnout
  a varování do logu; selhání autostartu jen do logu. Bez autostartu se nastavení nečte. Opravený komentář třídy
  (hook běží i při přeinstalaci). Odinstalace se nemění (autostart smaže, volba v nastavení zůstane → odinstalace +
  nová instalace respektuje dřívější vypnutí, otázka 2).
- **Testy:** `InstallerHooksTests` (nová instalace, přeinstalace po vypnutí – i se stávajícím zástupcem, po zapnutí,
  nečitelné nastavení, bez autostartu se nečte, odinstalace volbu nečte; selhání autostartu jen do logu už existovalo),
  `SettingsFileAutostartChoiceTests`, `JsonSettingsStoreStartAtLoginTests` (čtení nic nezapíše ani u souboru formátu 1),
  `SettingsStartupAndUpdatesTests` (přepínač ukládá volbu, přepnutí zpět ne, uložená volba zůstává, selhání OS, bez
  autostartu, stav převzatý z macOS), `SettingsMapperTests` (round-trip, nezapsaná bez volby), `AppSettingsTests`,
  `SettingsExportTests` (neexportuje se). Po kontrole qa: `AutostartChoiceMigrationTests` (instalace + vypnuto + bez
  volby → uloží `false`; zapnuto → nic; uložená volba → nic a autostart se nečte; nečitelné nastavení / autostart →
  jen log; selhání zápisu jen do logu; ukončení; jen instalovaná produkční aplikace na Windows, ne testovací režim,
  `.zip`, jiný OS), `JsonSettingsStoreStartAtLoginTests` (jiná chybná hodnota zachová „vypnuto“, chybný typ
  `startAtLogin` = nečitelné, zápis migrace), `SettingsFileAutostartChoiceTests` (složka se zjistí až při čtení), test
  okna: přepnutí + neuložené nastavení → autostart se nepřepne a uložená volba zůstane.
- **Dokumentace:** `docs/velopack.md` (bod 3 a popis hooků / autostartu), `docs/release.md` („Velopack a `winget upgrade`“
  a poznámka u T-059), `CHANGELOG.md` (Fixed). Příručky `winget upgrade` a autostart nespojují (kapitola 12 zmiňuje jen
  násilné ukončení, žádné nové spuštění a smazané logy) → beze změny.
- **Ověřeno (Windows 11 Pro 10.0.26300):** `dotnet build` 0 varování a 0 chyb. Po kontrole qa `LayerPeek.Core.Tests`
  3384/3384, `LayerPeek.App.Tests` 4744 prošlo / 105 přeskočeno / 0 selhání. Před ní celé `dotnet test`: 10996 testů, 47 selhání, žádné
  nesouvisí s T-201: `LayerPeek.App.UiTests` 43 (snímky `SettingsWindowSnapshotTests` a `AboutWindowLayoutTests` – stejných
  43 selhání i na výchozím commitu `2ce7be01` bez změn T-201, ověřeno v dočasném worktree; zřejmě neaktualizované Windows
  snímky po úkolech z Macu), `WebProxySecurityTests.ListenerIsNotReachableOnOtherAddresses` (PrusaLink, síťová rozhraní),
  `BrowserLifecycleTests.DisposedRigReleasesThePrinterAndProxyPorts` (E2E, porty),
  `GrayscaleSampleEquivalenceTests` (FFmpeg, odchylka 1,72 > 1,5) a `PesterScriptTests` pro `scripts/ReleaseTools/ReleaseTools.Tests.ps1` (Scripts; skripty vydání T-201 nemění). Na Windows **neověřeno** skutečnou
  přeinstalací (`Setup.exe --silent`, `winget`) → bod v T-124. macOS: jen se zapisuje (hook tam není), lokálně neověřeno.
- Ruční ověření přesunuto do T-124 (pokyn hlavního agenta; T-124 byl uzavřený, proto znovu otevřen do `todo/`).
- **Třetí kontrola qa (2026-11-12):**
  - Migrace rozlišuje „určitě vypnuto“ od „nevím“: nové `IAutostartState.QueryState()` (`On` / `Off` / `Unknown`), jen
    u `StartupShortcutAutostart`. Bez zástupce = `Off`; zástupce tohoto exe = `On`; nečitelný zástupce (např. zámek
    antiviru) nebo zástupce jiné kopie aplikace = `Unknown` → migrace nic neuloží a zkusí to při dalším startu.
    `IAutostart.IsEnabled()` (okno nastavení, hook) se nemění. Migrace bez `IAutostartState` (macOS) nevznikne.
  - Duplicitní `startAtLogin`: zapisovač se řídí stejně jako čtení poslední hodnotou – poslední `null` nahradí, poslední
    jiná hodnota = volba už uložená (soubor beze změny).
  - Prázdný kořenový objekt s komentáři (`{ // note` + nový řádek + `}`): komentáře zůstanou, hodnota se vloží těsně před
    uzavírací závorku.
  - Ukončení: migraci spouští a sleduje `ServiceHost.StartMonitoring` (dřív `TrayHost`, bez sledování); `StopAsync` ji po
    zrušení počká (`PendingSaveTimeout`, jako zápisy modelu a cesty monitoru) dřív, než kontejner zlikviduje úložiště
    nastavení. Automatický test pořadí při ukončení chybí: migrace vzniká jen s produkční identitou, takže by test sahal
    na skutečné `%APPDATA%` a složku Po spuštění; `ServiceHostTests` běží v testovacím režimu, kde migrace nevzniká.
    Stejně netestované jsou i sousední čekání na zápis modelu a cesty monitoru; nový testovací háček v `ServiceHost`
    jen kvůli tomu revize nedoporučila.
  - `StartupShortcutAutostart.IsEnabled()` je po revizi jen `QueryState() == AutostartState.On` (stejná odpověď i log).
  - Testy: `StartupShortcutAutostartStateTests` (čtyři stavy, `IsEnabled` beze změny), `AutostartChoiceMigrationTests`
    (`Unknown` nic neuloží, autostart bez tří stavů nemigruje), `JsonSettingsStoreStartAtLoginTests` (duplicity, komentáře
    v prázdném objektu, čtení i načtení po zápisu souhlasí). `LayerPeek.Core.Tests` 3389/3389, `LayerPeek.App.Tests`
    4750 prošlo / 105 přeskočeno / 0 selhání, `LayerPeek.App.UiTests` jen známých 43 selhání (T-227), `dotnet build`
    0 varování.
- Uzavřeno 2026-11-12: zbývající ruční ověření přesunuto do T-124 (rozhodnutí uživatele); selhání mimo T-201 sleduje T-227.
