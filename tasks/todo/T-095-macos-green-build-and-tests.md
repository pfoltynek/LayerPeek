---
id: T-095
title: macOS – zelený build a testy
status: todo
owner: qa-reviewer + ui-dev
priority: P1
platform: macos
depends_on: []
---

## Cíl
Na Macu (Apple Silicon) projde `dotnet build`, `dotnet test` i AOT publish `osx-arm64` bez chyb, varování a zaseknutí, a testy nic nezapíšou do profilu uživatele. Je to přípravný úkol: **musí být hotový před ostatními macOS úkoly** (T-018, T-019, T-020, T-021, T-026, T-053 na něm závisí, rozhodnuto 2026-10-28).

> **Pouze na macOS:** úkol se implementuje a ověřuje na Macu. Neblokuje žádný Windows úkol. Windows chování ani Windows referenční snímky se nemění.

### Výchozí stav (kontrola na Macu 2026-10-28)
Apple Silicon, macOS 27.2, .NET SDK `11.0.100-rc.3`, Xcode Command Line Tools, `gh`, `pwsh` 7.6.9 (dotnet global tool); Pester a Microsoft Edge nenainstalované.
- `dotnet build` (Debug): 0 varování.
- `dotnet publish src/LayerPeek.App -c Release -r osx-arm64`: z čistého klonu 0 varování (Mach-O arm64, 148 MB včetně `.pdb` / `.dSYM`). V pracovní kopii selhal (CS0246 `ServiceProvider` / `IHttpClientFactory`), protože terminál byl v repozitáři přes symbolický odkaz. Ověřená příčina: dotnet CLI skládá relativní cestu k projektu (`src/LayerPeek.App`) s proměnnou `$PWD`, která drží logickou cestu přes odkaz, kdežto `ProjectReference` se řeší přes fyzickou cestu. Kvůli neshodě obnova NuGet `LayerPeek.App` zahodí všechny `projectReferences`. Smazání `bin`/`obj` nepomůže; s `$PWD` rovným fyzické cestě publish projde bez varování. Pravidlo je v `docs/prerequisites.md`.
- `dotnet test`: 6948 testů, 119 selhalo a `LayerPeek.App.Tests` se zasekne:
  - **zaseknutí:** `LayerPeek.App.Tests.Platform.Windows.TrayIconPromotionTests.OtherExecutablesAreNeverTouched` (cesty `C:\...`) na macOS nikdy neskončí; selhávají i další případy `TrayIconPromotionTests.AppliesOnlyToTheAppExecutable`;
  - **94 selhání v `LayerPeek.App.UiTests`:** pixelové snímky (reference vykreslené na Windows, vykreslení textu na macOS se liší) a několik testů rozložení (text oříznutý šířkou, tolerance velikosti, „Timed out waiting for: no signal shown“). Třídy: `SettingsWindowSnapshotTests`, `CameraWindowMiniStripUiTests`, `CameraWindowCooldownWidgetUiTests`, `CameraWindowMiniTogglesUiTests`, `DiagnosticsWindowSnapshotTests`, `CameraWindowTimesWidgetUiTests`, `StatusWidgetUiTests`, `CameraWindowTemperaturesWidgetUiTests`, `CameraWindowPrintNameWidgetUiTests`, `CameraWindowMiniFullScreenUiTests`, `CameraWindowLayoutModeUiTests`, `AboutWindowLayoutTests`, `CameraWindowPrintThumbnailUiTests`, `CameraWindowOverlayUiTests`, `DiagnosticsWindowImageUiTests`, `CameraWindowUiTests`;
  - **asi 17 selhání formátu času** (`OverlayFormatTests`, `CooldownWidgetTests`, `TimesWidgetTests`): ICU na macOS dává před AM/PM znak U+202F NARROW NO-BREAK SPACE (`"1:05\u202FPM"`), testy čekají obyčejnou mezeru;
  - **asi 10 Windows testů se na macOS nepřeskočí:** `InstallerHooksTests` (`BeforeUninstall*`), `VelopackStartupTests` (4), `ProcessFolderLauncherTests.WindowsStartsExplorerFromTheWindowsFolder`, `AppShortcutRegistrationTests.TargetPathIsComparedIgnoringCaseAndNormalized`;
  - testy Core, PrusaLink, PrusaConnect, PrusaSimulator a E2E prošly, FFmpeg se na macOS přeskakuje (T-026), testy skriptů se přeskočily (tehdy bez `pwsh`).
- **Kontrola sandboxu:** porovnání `~/Library/Application Support`, `~/Library/Logs`, `~/Library/LaunchAgents` a Keychainu (služby LayerPeek / Prusa) před a po `dotnet test` – testy nic nevytvořily. Zůstaly jen soubory v `$TMPDIR` (rozdílové obrázky snímků, záměrně). Release AOT exe nemá testovací režim (`LAYERPEEK_TEST_DATA_DIR` je jen v Debug), takže jeho spuštění zapisuje do skutečného profilu.
- Repozitář leží na svazku připojeném s `noowners` (vlastnictví souborů se nevynucuje), testy vlastnictví a práv (T-053) proto musí běžet v `$TMPDIR`, ne v repozitáři.

## Rozsah
1. **Zaseknutí `TrayIconPromotionTests`:** zjistit, proč test na macOS nikdy neskončí (smyčka v normalizaci windowsových cest?). Windows-only testy přeskočit mimo Windows s důvodem. Pokud jde o chybu kódu, který na macOS může běžet, opravit ji nebo zapsat do poznámek / otevřených otázek.
2. **Windows-only testy** (`InstallerHooksTests`, `VelopackStartupTests`, `ProcessFolderLauncherTests.WindowsStartsExplorerFromTheWindowsFolder`, `AppShortcutRegistrationTests.TargetPathIsComparedIgnoringCaseAndNormalized` a další nalezené) přeskočit mimo Windows přes `Assert.SkipUnless(OperatingSystem.IsWindows(), "…")` s důvodem, stejně jako ostatní Windows testy. Nic nemazat a neoslabovat na Windows.
3. **Formát času (U+202F):** testy udělat odolné vůči ICU a kultuře (očekávaná hodnota ze stejného formátování / kultury, nebo porovnání, které bere U+202F a U+00A0 jako mezeru – volí implementace a uvede ji v poznámkách). Chování aplikace se nemění: U+202F z ICU v UI zůstává (rozhodnuto uživatelem 2026-10-28). Pokud se ukáže, že U+202F v UI dělá potíž (např. chybějící glyf, zalomení, šířka widgetu), zapsat do otevřených otázek, ne opravovat potichu.
4. **Samostatné referenční snímky pro macOS (rozhodnuto 2026-10-28):**
   - vlastní sada referenčních PNG pro macOS, např. podsložka podle OS v `tests/LayerPeek.App.UiTests/Snapshots/` (přesné umístění volí implementace a popíše ho v poznámkách); Windows reference zůstávají beze změny (bajtově stejné soubory);
   - `SnapshotAssert` vybírá referenci podle OS;
   - `LAYERPEEK_UPDATE_SNAPSHOTS=1` přegeneruje jen sadu aktuálního OS;
   - týká se i snímků tray ikon (`Snapshots/TrayIcons`), pokud na macOS selhávají;
   - když macOS reference pro snímek chybí, test selže s jasnou zprávou (žádné tiché přeskočení);
   - nové macOS reference zkontrolovat okem (žádné zjevně rozbité rozložení), než se uloží.
5. **Testy rozložení selhávající na macOS** (oříznutý text, tolerance velikosti, „no signal shown“): u každého zjistit příčinu. `SnapshotAssert` používá jen přibalený font Inter, takže rozdíl v šířce textu není samozřejmý. Skutečné chyby rozložení (text se na macOS opravdu nevejde, okno má jinou velikost) se zapíšou do poznámek a otevřených otázek, tolerance se **nepovolují potichu**. Časování (`Timed out waiting for`) opravit v testu jen tehdy, když jde o závod v testu, ne o chybu aplikace.
6. **Testy skriptů:** s nainstalovaným `pwsh` a Pesterem 5 běží `ScriptPesterTestsPass` i na macOS a projde. Skutečný běh skriptů vydání na Macu zůstává v T-019.
7. **Izolace testů na macOS** – pravidlo zapsané v `docs/architecture.md` („Izolace testů na macOS“): testy nesmí zapisovat do `~/Library/Application Support/LayerPeek`, `~/Library/Logs/LayerPeek`, `~/Library/LaunchAgents`, Login Items, do Keychainu mimo testovací prefix ani pod produkčním bundle identifikátorem notifikací. Postup kontroly před/po popsat tamtéž.
   - **Skript kontroly před/po (rozhodnuto 2026-10-28):** nový skript v `tools/` (PowerShell 7, např. `tools/profile-snapshot.ps1`) uloží stav sledovaných míst (`~/Library/Application Support/LayerPeek`, `~/Library/Logs/LayerPeek`, `~/Library/LaunchAgents`, Login Items přes `sfltool dumpbtm`, názvy služeb LayerPeek / Prusa v Keychainu bez obsahu tajných údajů) do souboru mimo repozitář a druhým voláním ho porovná s aktuálním stavem; rozdíl = nenulový kód a výpis změn. Na Windows obdobně (`%APPDATA%\LayerPeek`, `%LOCALAPPDATA%\LayerPeek`, Startup, Správce pověření s prefixem LayerPeek), pokud to jde bez zásahu do systému. Skript jen čte, nic nemaže. Pester testy nad zástupnou složkou (ne nad skutečným profilem).
9. **Izolace `scripts/aot-smoke.ps1` (rozhodnuto 2026-10-28: záloha a obnova):** Release AOT exe nemá testovací režim, proto smoke test před spuštěním exe zapíše žurnál, přesune existující složky dat a logů aplikace do zálohy (obdoba `InstallerBackup` z T-017), po běhu smaže, co exe vytvořilo, a zálohu vrátí. Po pádu se obnova dokončí při dalším běhu nebo přepínačem `-RestoreOnly`. Skript nikdy nesmaže data, která tam byla před během (test nad zástupnou složkou). Platí pro macOS i Windows (`release-build.ps1` ho volá na obou). Kontrola skriptem z bodu 7 po smoke testu: žádná změna.
8. **`docs/prerequisites.md`, sekce macOS:** `pwsh` (Homebrew nebo dotnet global tool), instalace Pesteru 5, nepracovat v repozitáři přes symbolický odkaz (terminál / VS Code otevírat ve fyzické cestě nebo `cd -P`, `echo $PWD` se musí rovnat `pwd -P`; jinak obnova NuGet `LayerPeek.App` zahodí `projectReferences`), a verze SDK (`global.json` připíná `rc.2`, `rollForward: latestFeature` přijme novější `rc`). Architekt to doplnil 2026-10-28, ověřit a případně upravit.

## Akceptační kritéria
- [ ] Na macOS `dotnet build` bez chyb a varování
- [ ] Na macOS `dotnet test` doběhne bez zaseknutí, 0 selhání; přeskočené testy mají důvod (Windows-only, bez Edge, FFmpeg do T-026)
- [ ] S `pwsh` a Pesterem 5 na macOS `ScriptPesterTestsPass` projde
- [ ] Windows-only testy se mimo Windows přeskočí s důvodem a na Windows běží beze změny
- [ ] Testy formátu času projdou s ICU na macOS (U+202F) i na Windows; chování aplikace beze změny (nebo změna schválená uživatelem)
- [ ] `SnapshotAssert` vybírá referenci podle OS, macOS má vlastní sadu, `LAYERPEEK_UPDATE_SNAPSHOTS=1` přepisuje jen sadu aktuálního OS (test infrastruktury v `SnapshotAssertTests`), Windows reference beze změny (`git diff` je nemění)
- [ ] Každý test rozložení, který na macOS selhával, má v poznámkách příčinu; skutečné chyby rozložení jsou v otevřených otázkách nebo jako nový úkol, ne skryté povolenou tolerancí
- [ ] Na Windows `dotnet build` a `dotnet test` beze změny výsledku (ověří uživatel na Windows, nebo se zapíše jako otevřený bod)
- [ ] `dotnet publish src/LayerPeek.App -c Release -r osx-arm64` bez varování (spuštěno s `$PWD` rovným `pwd -P`)
- [ ] Kontrola před/po (`~/Library/Application Support`, `~/Library/Logs`, `~/Library/LaunchAgents`, Login Items, Keychain – služby LayerPeek / Prusa) po `dotnet test`: žádná změna; v `$TMPDIR` zůstanou jen rezervované složky testů
- [ ] Skript kontroly před/po v `tools/` existuje, jen čte, rozdíl hlásí nenulovým kódem (Pester testy) a po `dotnet test` na macOS nehlásí žádnou změnu
- [ ] `aot-smoke.ps1` zálohuje a obnovuje složky dat a logů; po běhu i po simulovaném pádu (`-RestoreOnly`) je profil stejný jako před ním a původní data zůstanou (Pester testy nad zástupnou složkou)
- [ ] Pravidlo izolace testů na macOS a postup kontroly v `docs/architecture.md`, macOS prerekvizity v `docs/prerequisites.md`
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- ~~U+202F v UI~~ → ponechat, upraví se jen testy (rozhodnuto 2026-10-28).
- ~~Kontrola před/po ručně, nebo skriptem~~ → skript v `tools/` (rozhodnuto 2026-10-28, rozsah bod 7).
- `scripts/aot-smoke.ps1` (a tím `release-build.ps1`) spouští Release AOT exe, které nemá testovací režim, takže na Macu zapíše do skutečného `~/Library/Application Support/LayerPeek` a `~/Library/Logs/LayerPeek` (na Windows stejně do `%APPDATA%` / `%LOCALAPPDATA%`). → řeší se tady zálohou a obnovou ve skriptu (rozhodnuto 2026-10-28, rozsah bod 9).

## Poznámky k implementaci
(vyplní agent po dokončení)
