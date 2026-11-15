---
id: T-212
title: macOS ověření – čtení doručených notifikací testovacího bundlu a kontroly jejich obsahu
status: done
owner: ui-dev
priority: P2
platform: macos
depends_on: [T-208, T-209, T-210, T-211]
---

> **Rozhodnuto uživatelem 2026-11-13:** obsah notifikací se ověřuje strojově v testovacím režimu. Body T-127 / T-126
> se odškrtnou podle kontrol v poslední zprávě `-Preset release` (T-208).

## Cíl
Režim `--layerpeek-e2e-notification-status` dnes vypíše jen oprávnění a počet doručených notifikací. Body T-154,
T-146, T-161, T-164, T-165 a T-101 v T-127 ale potřebují znát **obsah**: titulek, text, přílohu, kategorii
s tlačítkem „Falešný poplach“ a co udělá klik. Tento úkol přidá čtení obsahu a kontroly, které ho používají.

## Rozsah
- **Aplikace (jen Debug a jen testovací bundle, jako dnešní `NotificationStatusArgument`):** nový argument např.
  `--layerpeek-e2e-notification-list`. Vypíše JSON doručených notifikací testovacího bundlu
  (`UNUserNotificationCenter.getDeliveredNotifications`): identifikátor, titulek, podtitulek, text, `categoryIdentifier`,
  `threadIdentifier`, počet a typy příloh, zda má zvuk. Přidá i registrované kategorie
  (`getNotificationCategories`) s titulky akcí. Nic se nemění ani nezobrazuje. Exit kódy a odmítnutí mimo testovací
  bundle jako u `--layerpeek-e2e-notification-status`. Kód v `src/LayerPeek.App/Platform/MacOS` vedle
  `TestNotificationCleanup`, AOT kompatibilní (`[LibraryImport]` / stávající ObjC interop), zdroj JSON přes
  `JsonSerializerContext`. Unit test formátu výstupu (bez skutečného centra oznámení).
- **Helper `ncpress <text titulku> <akce>`** (Zpřístupnění): v AX stromu procesu Centra oznámení
  (`com.apple.notificationcenterui`) najde banner nebo položku s titulkem. Provede pojmenovanou akci (vlastní akce
  „Falešný poplach“, AXPress = klik). Když macOS akci do AX nevystaví, exit 4 a kontrola tu část označí `SKIP`
  s důvodem („akce banneru není v AX“), ne `FAIL`.
- **Kontrola `mac.notifications-content`** (Permission: Zpřístupnění, preset `release`):
  - (a) **T-154:** `LAYERPEEK_TEST_FAKE_DETECTOR=1`, tisk simulátoru → poplach. Titulek = název tiskárny, text
    „Možná chyba tisku – …“, 1 příloha (obrázek), kategorie s akcí „Falešný poplach“ zaregistrovaná už před **prvním**
    poplachem. Stejný zvuk jako u chyby tisku (příznak zvuku). `ncpress … Falešný poplach` uloží
    `detection/false-alarms/*.json` v testovacích datech a nepřenese aplikaci do popředí (`lsappinfo front`).
    Klik (`ncpress … AXPress`) otevře okno kamery. Snímek zůstane ve složce nahrávek. V cache
    `notification-images` testovacích dat nezůstanou soubory.
  - (b) **T-146 / T-161:** složka nahrávek na neexistujícím svazku (cesta `/Volumes/<neexistující>`) → „Nahrávání
    kamery selhalo“ a „Timelapse selhal“. Klik na každou otevře Nastavení na záložce „Nahrávání“ (AX vybraná záložka).
  - (c) **T-101:** zkušební upozornění se zvukem i bez (příznak zvuku). Druhé zkušební upozornění nahradí první
    (v seznamu pořád 1 se stejným identifikátorem). Klik přenese Nastavení na záložku Obecné.
  - Úklid: `--layerpeek-e2e-clear-toasts` jako dnes.
- Notifikace exportu a timelapse z nahrávky (T-164, T-165) ověří kontroly T-213 přes tento výpis.
- Mimo rozsah (zůstává ruční v T-127 / T-126): vzhled banneru (náhled přílohy vpravo, po rozbalení velký), stav
  „zamítnuto“ a `notDetermined` z T-101 (vyžadují zásah do TCC v Nastavení systému) a notifikace produkční
  `/Applications/LayerPeek.app` (T-126).

## Akceptační kritéria
- [x] `--layerpeek-e2e-notification-list` vypíše JSON doručených notifikací a kategorií testovacího bundlu, mimo něj
      odmítne; v Release buildu neexistuje; unit test formátu
- [x] `mac.notifications-content` (a) = `PASS` (uzavře v T-127 bod **T-154** „Poplach zobrazí notifikaci …“ a „Falešný
      poplach funguje“ kromě vzhledu banneru a editoru výřezu (T-216); pokud `ncpress` akci nenajde, zapsat a část
      nechat ruční) → přesunuto do T-127
- [x] `mac.notifications-content` (b) = `PASS` (doplní body **T-146** a **T-161** v T-127: notifikace selhání a klik)
      → přesunuto do T-127
- [x] `mac.notifications-content` (c) = `PASS` (uzavře v T-127 bod **T-101** kromě stavu „zamítnuto“ a `notDetermined`, ty zůstávají ruční)
      → přesunuto do T-127
- [x] AOT: `dotnet publish` osx-arm64 bez varování (kód je jen v Debug, ověřit, že Release ho nepřekládá)
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
Implementováno 2026-11-15 na Macu (ui-dev, worktree `LayerPeek-t212`), postupem T-210 / T-211. Terminál bez
Zpřístupnění, obrazovka během běhů zamčená, oznámení „LayerPeek E2E“ `NotDetermined` (macOS by se ptal).

- **Aplikace (jen Debug):** argument `AppEnvironment.NotificationListArgument` = `--layerpeek-e2e-notification-list`
  v `Program.Main` vedle `--layerpeek-e2e-notification-status` (nic jiného se nespustí, jen macOS).
  - `Platform/MacOS/TestNotificationList.cs`: záznamy výstupu, `TestNotificationListJsonContext` (zdrojový generátor,
    camelCase, odsazeno, `null` se vypisuje), `Format`, `Write` (chyba čtení = jen důvod na stderr, exit 1) a
    `ListFromCommandLine`. Centrum otevře přes `TestNotificationCleanup.OpenTestCenter` (teď `internal`), takže mimo
    testovací bundle odmítne stejně jako režim stavu a pravidlo „`TryOpen` jen v `Program.cs` a `TestNotificationCleanup.cs`“
    platí dál.
  - `Platform/MacOS/MacUserNotificationCenter.TestList.cs` (partial, `#if DEBUG`): `getDeliveredNotificationsWithCompletionHandler:`
    a `getNotificationCategoriesWithCompletionHandler:` přes stávající `ObjCRuntime` / `ObjCBlocks` / `NativeReply`
    (globální bloky, žádná reflexe, žádný nový balíček). Objekty systému platí jen v bloku, proto se tam hned kopírují do
    záznamů. JSON: `bundleIdentifier`, `notifications` (identifikátor, titulek, podtitulek, text, `categoryIdentifier`,
    `threadIdentifier`, `hasSound`, `attachments` s `identifier` a `type` (UTI), `launchArgument` a `actionArgument`
    z `userInfo`, `deliveredAt`) a `categories` (identifikátor, akce s `identifier`, `title`, `options`). Argumenty
    kliku a tlačítka ve výpisu ukážou, kam klik povede, i bez Zpřístupnění. Nic se nemění ani nezobrazuje.
  - Testy: `TestNotificationListTests` (názvy a pořadí polí, `null`, čeština, prázdné centrum, exit 0 / 1, odmítnutí
    mimo bundle), `AppEnvironmentTests.ReleaseBuildsHaveNoTestMode` (v Release chybí typy, konstanta i
    `ListDeliveredAsync`), E2E `AppSwitches` / `IconAndBuildTests` (řetězec není v Release exe). `MacNotificationsE2ETests`
    (na vyžádání, `LAYERPEEK_E2E=1`) po doručení čte výpis: notifikace pauzy s titulkem tiskárny, bez zvuku, bez přílohy,
    argument kliku tiskárny.
- **Helper `ncpress <text> <akce> [<s>]`** (`tools/macos-verify-helper.swift`): v AX stromu
  `com.apple.notificationcenterui` najde nejhlubší stisknutelný prvek, jehož texty obsahují `<text>` (banner nebo
  položka Centra oznámení), a provede `AXPress` nebo vlastní akci tlačítka (`Name:<akce>…`). Čeká až `<s>` (výchozí
  10 s). Exit 1 = notifikace v AX není, 2 = akce selhala, 3 = bez Zpřístupnění, 4 = akce není v AX (její akce na stderr).
  Text se hledá v titulku i textu, protože notifikace selhání mají titulek = název tiskárny jako ostatní.
- **Kontrola `mac.notifications-content`** (`tools/macos-verify.ps1`, Permission, `Session` + `Accessibility`, preset
  `release`, teď 30 kontrol): bez povolených oznámení `SKIP` s postupem. (a) simulátor, `LAYERPEEK_TEST_FAKE_DETECTOR=1`,
  zapnutá detekce, zvuk „Jen důležité“: obsah prvního poplachu (`Get-VerifyNotificationProblems`: titulek, „Possible
  print failure – “, 1 příloha `public.jpeg`, `camera=verify`, `false-alarm=…`, kategorie `layerpeek.with-action`
  zaregistrovaná s tlačítkem „False alarm“ bez přenesení do popředí už u prvního poplachu); `ncpress … False alarm` →
  `detection/false-alarms/*.json`, aplikace není vpředu a nic se neotevře; snímek `*_alarm-e2e-alarm.jpg` ve složce
  nahrávek, prázdná `cache/notification-images`; poplach dalšího tisku (jeden poplach na tisk) a klik → okno kamery;
  chyba tiskárny má stejný příznak zvuku jako poplach. (b) složka nahrávek `/Volumes/LPV-missing-…`, nahrávání
  a timelapse tisku: „Camera recording failed“ a „Timelapse failed“ s `settings=recording`, klik → vybraná záložka
  `SettingsTabRecording` (u otevřeného Nastavení nejdřív přepnuto na Obecné), složka nevznikla. (c) bez tiskárny, zvuk
  „Vše“ a „Vypnuto“: `SettingsTestNotification` (AXPress) → `layerpeek-test-notification` s textem, zvukem podle
  nastavení a `settings=general`; druhé stisknutí → pořád jeden záznam s pozdějším `deliveredAt`; z Nahrávání klik →
  Obecné. Akce mimo AX (exit 4) nebo banner mimo AX (exit 1) = část se nezapočítá a kontrola skončí `SKIP` s důvody
  (nikdy `PASS`), jiné chyby `FAIL`. Úklid `--layerpeek-e2e-clear-toasts` před a po každé části.
  - `New-TestData` má `-Sound`, `-Detection` a absolutní `-RecordingFolder`. Čisté funkce `ConvertFrom-VerifyNotificationList`
    a `Get-VerifyNotificationProblems` ve `VerifyTools.psm1`, Pester blok „Delivered notifications“; test presetu
    obsahuje novou kontrolu.
  - Dokumentace: tabulka kontrol, Zpřístupnění, oznámení (styl **Trvalé**, aby banner nezmizel před `ncpress`)
    a pozůstatky v `docs/macos-manual-testing.md`.
- **Neověřeno proti skutečnému systému** (nešlo bez povolených oznámení a Zpřístupnění): podoba AX stromu Centra
  oznámení na macOS 27 (role banneru, názvy vlastních akcí), tedy celé `ncpress`, a obsah skutečně doručených
  notifikací (typ přílohy, kategorie, příznak zvuku). Režim výpisu proti skutečnému centru ověřen jen s prázdným centrem.
  Bod 1n v T-127.
- **Ověřeno 2026-11-15:**
  - režim výpisu ve skutečném testovacím bundlu (dočasná kopie Debug aplikace s ID `….E2E`, ad hoc podpis, registrace
    a úklid jako `Get-TestBundle`): exit 0 a `{"bundleIdentifier": "io.github.pfoltynek.LayerPeek.E2E", "notifications": [],
    "categories": []}`, režim stavu `permission=NotDetermined delivered=0`; spustitelný soubor z `bin/Debug` (bez bundlu)
    odmítne s exit 1 „…listed only in the test bundle: the app does not run from an app bundle“;
  - `pwsh tools/macos-verify.ps1 -Unattended -Only mac.notifications-content,mac.profile-unchanged -Allow
    mac.notifications-content`, běh `artifacts/verify/20261115-111246/`: `SKIP mac.notifications-content` (předběžné
    podmínky Session – zamčená obrazovka – a Accessibility), `PASS mac.profile-unchanged`. `mac.notifications` (běh
    `20261115-110719`) `SKIP`: oznámení `NotDetermined`;
  - AOT `dotnet publish src/LayerPeek.App -c Release -r osx-arm64`: exit 0, 0 varování kompilátoru / trimmeru / AOT
    (jen hlášky nativních nástrojů dsymutil a mezipaměti modulů Swift, nesouvisí). Binárka ani Release `LayerPeek.dll`
    neobsahuje `TestNotificationList` ani `notification-list` (Debug ano).
  - Pozor: při ověřování jsem omylem spustil Release `bin/Release/net11.0/LayerPeek` s tímto argumentem (Release ho
    nezná, takže se spustila normální aplikace v produkčním režimu bez displeje a po 2 min ji ukončil `timeout`).
    Zapsala `~/Library/Application Support/LayerPeek/update-state.json` (`lastCheck`, jedna kontrola aktualizací na
    GitHub, jen čtení) a řádky do `~/Library/Logs/LayerPeek/LayerPeek.log`. Nastavení ani tiskárny se nezměnily.
- **Build a testy 2026-11-15** (po `git merge develop` = `f9eefd23`): `dotnet build` Debug i `-c Release` 0 varování,
  0 chyb. Plný `dotnet test`: 10 844 testů, 10 572 prošlo, 271 přeskočeno, **1 selhal**:
  `ProxyOriginGuardTests.ClassicFormPostOfTheProxiedPageReachesThePrinter` (`LayerPeek.E2E.Tests`, Edge 157), selhává
  i samostatně; známá chyba mimo tento úkol, řeší ji **T-233**. Pester `tools/VerifyTools.Tests.ps1` 77 prošlo /
  1 přeskočen (Windows), `-SelfTest` PASSED (40 kontrol).
- **Po opravě T-233 (2026-11-15):** `git merge develop` (`3bdc359a`), `dotnet build` Debug i `-c Release` 0 varování,
  0 chyb; plný `dotnet test` 10 844 testů, 10 573 prošlo, 271 přeskočeno, 0 selhalo. Kritéria 2–4 jsou přesunutá do
  T-127 (oddíl 1n). Bez nových balíčků. Nic na GitHub.
- Uzavřeno 2026-11-15: zbývající ruční ověření (běh `mac.notifications-content`) přesunuto do T-127 (rozhodnutí uživatele)
- Release build testů padal na `AutostartChoiceMigrationTests` (T-201); opravil to souběžně T-231 v `develop`,
  sloučeno sem.
