---
id: T-128
title: E2E testy místo ručních kontrol – zkušební upozornění a aktualizace přes Velopack
status: done
owner: qa-reviewer
priority: P2
platform: windows
depends_on: [T-058, T-101, T-125]
---

## Cíl
Uživatel 2026-11-01: „jsi schopen na T-125 napsat testy, ať to nemusím ručně testovat?“ – rozhodnuto napsat automatické
testy pro body T-101 (zkušební upozornění) a T-058 (aktualizace přes Velopack) z T-125. Body pak v T-125 odkazují na
testy, které je nahrazují.

## Rozsah
1. **T-101 – zkušební upozornění (E2E, skutečná aplikace):** tlačítko „Poslat zkušební upozornění“ v Nastavení → Obecné
   pošle toast (zvuk podle hodnoty vybrané v okně, i neuložené – ověřit v obsahu toastu, ne poslechem); druhý klik
   předchozí toast nahradí; po vypnutí upozornění ve Windows (systémová změna přes žurnál `run-e2e.ps1`, varování,
   odpočet 5 s, obnova i po pádu) se ukáže varování „Upozornění jsou ve Windows vypnutá.“ s tlačítkem, které otevře
   `ms-settings:notifications` (ověřit a okno Nastavení Windows zavřít jen pokud ho otevřel test); klik na zkušební toast
   v centru oznámení přenese okno Nastavení na záložku Obecné.
2. **T-058 – aktualizace přes Velopack (E2E):** izolovaná instalace starší verze z místního zdroje aktualizací (ne GitHub),
   položka menu „Je dostupná nová verze X.Y.Z“ a banner toastu, klik spustí aktualizaci, aplikace se znovu spustí v nové
   verzi; verze `.zip` (bez instalace) otevře stránku vydání (zachytit bez skutečného prohlížeče, pokud to jde).
   Na počítači s nainstalovanou LayerPeek uživatele se test přeskočí s důvodem (stejně jako `VelopackInstallerTests`);
   poprvé poběží spolu s T-124.
3. Testy po sobě uklidí vše i po pádu (instalace, zástupci, registrace toastů, nastavení Windows, procesy).

## Akceptační kritéria
- [x] E2E pro T-101 (body 1) a T-058 (bod 2); v T-125 u obou bodů odkaz na testy
- [x] Úklid i po pádu, systémové změny jen přes žurnál `run-e2e.ps1`; jména PascalCase bez podtržítek
- [x] Běh `tools/run-e2e.ps1` pro T-101 prošel (koordinuje hlavní agent s uživatelem); T-058 → s T-124 (případ `.zip` prošel, instalační případy → přesunuto do T-124)
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- **Znovuspuštění po aktualizaci:** testovací režim aplikaci po aktualizaci záměrně znovu nespouští
  (`VelopackUpdateEngine.RestartsAfterUpdate` je `false` se zdrojem testu – kvůli macOS, kde by `open -n` spustilo aplikaci
  mimo testovací režim). Test proto ověří, že `Update.exe` nahradil `current` novou verzí, a aktualizovanou kopii spustí
  sám. Na Windows by `Update.exe` aplikaci spustil s proměnnými testovacího režimu (zdědí je), ale bez značky běhu
  `--layerpeek-e2e-run=` (spouští ji bez argumentů). Má se restart ve Windows testu ověřit skutečně (změna v aplikaci:
  restart i se zdrojem testu na Windows + předání značky), nebo stačí současný stav? Rozhodne uživatel.
  **Vyřešeno (rozhodnutí uživatele 2026-11-01):** stačí současný stav, aktualizovanou kopii spouští test sám.
- **Skutečné GitHub Releases:** T-058 v T-125 zmiňuje aktualizaci „z GitHub Releases (bez tokenu)“. Testy používají jen
  místní zdroj (bez sítě, pravidlo testů); stažení z GitHubu (`GithubSource`) ověří až skutečné vydání. Má bod T-125
  zůstat částečně ruční (jen ověření s GitHubem po vydání), nebo stačí E2E s místním zdrojem?
  **Vyřešeno (rozhodnutí uživatele 2026-11-01):** v T-125 zůstává ruční bod „jednou po skutečném vydání aktualizace
  z GitHub Releases“; zbytek bodu T-058 pokrývají E2E testy.
- **Vypnutí oznámení:** test nejdřív vypne jen oznámení testovací aplikace (`Notifications\Settings\LayerPeek.E2E`,
  `Enabled = 0`); když to Windows do 15 s nepřevezme, vypne na pár sekund i hlavní přepínač oznámení Windows
  (`ToastEnabled = 0`, oznámení všech aplikací). Obojí je v žurnálu a v textu varování. Zda Windows převezme zápis do
  registru bez aplikace Nastavení, ukáže až první běh – je druhý krok přijatelný?
  **Vyřešeno (rozhodnutí uživatele 2026-11-01):** záložní krok s krátkým vypnutím `ToastEnabled` je přijatelný (je v žurnálu
  a ve varování).
- **Zvuk se zapnutou volbou:** ověřuje se jen směr „uloženo Všechna, v okně neuloženě Vypnuto → tichý toast“ (žádný
  zvuk během běhu, rozhodnutí 2026-10-22). Opačný směr (v okně zapnutý zvuk → toast bez `<audio silent>`) by během běhu
  zahrál výchozí zvuk Windows; pokrývají ho jednotkové testy XML toastu. Stačí to?
  **Vyřešeno (rozhodnutí uživatele 2026-11-01):** stačí ověřit jen tichý směr.

## Poznámky k implementaci
Agent qa-reviewer, 2026-11-01. Testy jsou napsané, sestavené a nalezené (`--list-tests`), ale **ještě neběžely na ploše**
(běh `tools/run-e2e.ps1` koordinuje hlavní agent s uživatelem), proto úkol zůstává v `in-progress`.

**T-101 – `TestNotificationTests.TestNotificationFollowsTheWindowReplacesItselfOpensGeneralAndWarnsWhenWindowsTurnedItOff`**
(druhy změn `Toasts`, `DoNotDisturb`; s `-NoSystemChanges` se přeskočí):
1. Aplikace bez tiskárny (žádná událost tiskárny nepošle toast), uložený zvuk „Všechna oznámení“; v okně Nastavení →
   Obecné se zvuk přepne na „Vypnuto“ bez uložení. Klik na „Poslat zkušební upozornění“ → banner (titulek a text),
   výsledek „Zkušební upozornění odesláno…“, log `sound: False` a v historii oznámení testovací aplikace (WinRT
   `ToastNotificationManager.History` přes Windows PowerShell 5.1, jen `LayerPeek.E2E`) právě jeden toast se značkou
   `layerpeek-test-notification` s `<audio silent="true"/>`.
2. Druhý klik → nový banner, aplikace předala Windows dvě upozornění, v historii je dál jediné (nahrazení značkou);
   `settings.json` má dál „All“.
3. Okno na záložce Tiskárny; v centru oznámení (otevřené přes hodiny na hlavním panelu, existující infrastruktura T-068,
   žádné klávesové zkratky) skutečný klik myší na toast → okno Nastavení na záložce Obecné a v popředí.
4. Vypnutí oznámení testovací aplikace (`ToastSettings` v žurnálu, viz otevřené otázky) a klik na tlačítko testu →
   varování „Notifications are turned off in Windows.“, tlačítko testu zmizí, tlačítko „Open notification settings“ je
   vidět; jeho klik v testovacím režimu jen zaloguje `ms-settings:notifications`. Tutéž adresu otevře test přes shell –
   jen když aplikace Nastavení neběží (jinak by nahradil stránku uživatele; pak to jen zapíše do výsledků) – ověří, že
   Nastavení (`SystemSettings`/`ApplicationFrameHost`) je v popředí, a zavře jen to, co otevřel. Okno LayerPeeku se
   zavře bez uložení.

**T-058 – `UpdateTests`** (druhy `Toasts`, `DoNotDisturb`; žádná síť):
- `InstalledOlderVersionUpdatesFromALocalFeedAfterAClick(MenuItem|Banner)`: `vpk` (lokální nástroj, verze z
  `.config/dotnet-tools.json`) zabalí Debug sestavení dvakrát jako přenosný balíček s testovacím id `LayerPeekE2E` – verze
  z `Directory.Build.props` a následující patch (balí se jednou za běh, do rezervované složky). Starší se rozbalí do
  `update-<id>` v rezervované složce běhu (přenosná instalace Velopack: žádný instalátor, zástupce, klíč Uninstall ani
  háčky aplikace), novější je v místní složce zdroje (`LAYERPEEK_TEST_UPDATE_SOURCE`). Ruční kontrola v Nastavení →
  Spuštění a aktualizace najde novou verzi → banner toastu („LayerPeek update available“) a položka menu „New version X.Y.Z
  available“; klik na ni (nebo na banner) stáhne aktualizaci, aplikace skončí s kódem 0, `Update.exe` nahradí `current`
  (`sq.version` = nová verze). Aktualizovanou kopii spustí test sám (viz otevřené otázky) a ta už nic novějšího nenajde.
  Přeskočí se s důvodem, když je LayerPeek nainstalovaná (stejná kontrola jako `VelopackInstallerTests`), v průchodu
  proti Native AOT exe, mimo x64 a bez obnoveného `vpk`.
- `ZipVersionOpensTheReleasePageFromTheBannerAndTheMenuItem`: vývojové exe se ptá falešné odpovědi GitHub REST API na
  `127.0.0.1` (`FakeGitHubReleases`); banner i položka menu otevřou `https://github.com/pfoltynek/LayerPeek/releases/tag/vX.Y.Z`,
  které testovací režim jen zapíše (žádný prohlížeč).

**Úklid i po pádu:** nový druh změny `UpdateCopy` (žurnál, druh varování `Toasts`): ukončí procesy kopie (`LayerPeek.exe`,
`Update.exe`), smaže složku kopie, položky `HKCU\Control Panel\NotifyIconSettings` jejího exe (jednorázová cesta, jinak by
se hromadily) a nové logy Velopacku `%LOCALAPPDATA%\velopack\velopack*` (řádky přidané do existujícího logu zůstávají,
jako u T-017). Toasty, zástupce a aktivátor testovací aplikace, nastavení oznámení, Nerušit a hlavní přepínač oznámení
vrací existující druhy (`ToastHistory`, `ToastSettings`, `ToastsEnabled`, `DoNotDisturb`). Ruční návrat je v
`docs/troubleshooting.md`, text varování v `tools/RunE2E.SystemChanges.psm1`.

**Infrastruktura:** `AppInstance` a `ToastRun` umí jinou kopii exe a další přepínače testovacího režimu (`ToastRunOptions`),
`ToastHistory` (čtení historie oznámení testovací aplikace), `WindowsSettingsApp`, `FakeGitHubReleases`, `UpdateCopyKind`;
čisté jednotkové testy pomocníků v `NotificationAndUpdateInfrastructureTests` (běží v každém `dotnet test`). Žádný nový
NuGet balíček.

**Běh 2026-11-01 (plocha uživatele, koordinoval hlavní agent):** `-Class '*UpdateTests'` prošel (případ `.zip` zelený,
oba instalační případy přeskočené – LayerPeek uživatele je nainstalovaná). `-Class '*TestNotificationTests'` selhal v kroku 1:
aplikace zkušební upozornění předala Windows (`sound: False`) 2 s po vytvoření zástupce `LayerPeek E2E.lnk`, ale banner
nepřišel a toast nebyl ani v centru oznámení; čtení `ToastNotifier.Setting` do té doby selhávalo. Příčina: Windows zahodí
toast ID aplikace, jejíž nový zástupce ve Start menu ještě nepřevzal. Ostatní testy toastů mají po startu prodlevu (dotaz
na tiskárnu + 8 s), běh bez tiskárny (`ToastRunOptions.WithoutPrinter`) ji neměl. Oprava v testu: `ToastRun` bez tiskárny
počká po vytvoření zástupce 10 s. Chyba produktu to není (instalátor vytváří zástupce dlouho před prvním toastem; jen
vývojové sestavení, které si zástupce zakládá při startu, by toast odeslaný v prvních sekundách ztratilo). Čeká na nový běh.

**Spuštění (Windows, plocha uživatele):**
- `pwsh tools/run-e2e.ps1 -Class '*TestNotificationTests'`
- `pwsh tools/run-e2e.ps1 -Class '*UpdateTests'` (instalační případ poběží jen bez nainstalované LayerPeek, tj. s T-124)
- s `-NoSystemChanges` se všechny tři testy přeskočí (všechny mění oznámení testovací aplikace).

**Běhy na skutečné ploše (hlavní agent, 2026-11-01, Windows 11 Pro 10.0.26300):** `TestNotificationTests` napoprvé selhal (toast odeslaný ~2 s po vytvoření zástupce testovací aplikace Windows zahodil), po opravě (čekání 10 s po zástupci) prošel 1/1, oznámení vrácena (žurnál: nic nezbylo); `UpdateTests`: `.zip` 1/1, oba instalační případy přeskočeny (nainstalovaná LayerPeek uživatele) → T-124; kontrolně `ToastNotificationTests` 6/6. Review bez blokujících nálezů. Uzavřeno 2026-11-01.
