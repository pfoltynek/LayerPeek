---
id: T-068
title: E2E – toasty: banner, klik otevře web přes proxy, centrum oznámení, jazyk, ikona
status: in-progress
owner: qa-reviewer
priority: P2
platform: windows
depends_on: [T-065, T-067]
---

## Cíl
Ověřit automaticky poslední ruční kontrolu T-010 (kliknutí na toast otevře web tiskárny) a toast s ikonou aplikace z T-025. Testuje se na skutečném exe v testovacím režimu se zapnutými toasty (T-067, AUMID `PrusaTrayDiagnostic.E2E`). Úkol zahrnuje i úklid toastů po pádu.

## Pokrytí
| Úkol | Kontrola | Test |
|---|---|---|
| T-010 | kliknutí na banner otevře web tiskárny | `ToastBannerClickOpensThePrinterWebThroughTheProxy` |
| T-010 | kliknutí na toast v centru oznámení, dokud aplikace běží | `ToastInTheNotificationCenterOpensThePrinterWeb` |
| T-010 | (volitelně) toast v novém jazyce bez restartu | `ToastFollowsTheLanguageSavedAtRuntime` |
| T-025 | toast s novou ikonou | součást testu banneru (snímek a porovnání ikony) |
| T-010 | (volitelně) zvuk | **zůstává doporučená** (T-065); během E2E jsou toasty tiché, atribut zvuku v XML toastu kryjí jednotkové testy (`ToastContentBuilderTests`, `ToastNotificationServiceTests`) |

## Rozhodnuto
- **Stopy v databázi oznámení (uživatel 2026-10-22, dřív Q1):** pokud Windows po úklidu dál pamatují aplikaci `PrusaTrayDiagnostic.E2E` v databázi oznámení (`%LOCALAPPDATA%\Microsoft\Windows\Notifications\wpndatabase.db`) nebo v seznamu Nastavení → Oznámení, je to přijatelné, podobně jako záznamy v protokolu událostí z T-046. Stopu je potřeba zdokumentovat: v poznámkách tohoto úkolu, v nápovědě `tools/run-e2e.ps1` a ve varování T-065.
- **Přístup k historii toastů při úklidu (uživatel 2026-10-22, dřív Q2):** Debug-only režim aplikace `--ptd-e2e-clear-toasts` (T-067), který použije vlastní interop aplikace. Cílová platforma testovacího projektu se nemění (žádné `net11.0-windows10.0.x`).
- **Testy T-067 se zástupcem jen s varováním (uživatel 2026-10-22, dřív Q3):** testy `TestModeOsIntegrationTests`, které spouštějí testovací aplikaci s toasty (a tím vytvářejí testovacího zástupce v nabídce Start), mají trait `SystemChanges`, běží jen po potvrzeném varování `tools/run-e2e.ps1` a s `-NoSystemChanges` se přeskočí. Zástupce a toasty jdou vždy do záznamu (`ToastHistory`), větvení „záznam jen při potvrzeném varování“ je odstraněné. Test `WithoutTheSwitchTheTestModeCreatesNoShortcut` nic nemění a běží dál v každém E2E běhu.
- **Zvuk toastů (uživatel 2026-10-22):** testovací toasty během E2E běhu nehrají žádný zvuk (nastavení testů `notifications.sound = "Off"`, toast má `<audio silent="true"/>`). Atribut zvuku v XML toastu kryjí jednotkové testy aplikace; slyšitelný zvuk zůstává doporučenou ruční kontrolou.

## Rozsah
- **Předpoklady (zásahy T-065, trait `SystemChanges`):** Nerušit vypnuté (`DoNotDisturbChange`). Oznámení systému povolená (`HKCU\Software\Microsoft\Windows\CurrentVersion\PushNotifications`, `ToastEnabled`) a pro testovací AUMID povolená (`HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\PrusaTrayDiagnostic.E2E`, `Enabled`). Pokud je uživatel vypnul, test je dočasně zapne jako zásah s návratem. Pokud je vypnula zásada skupiny, test se přeskočí s důvodem.
- **Vyvolání události:** simulátor (T-045) se scénářem `printing`, za běhu přepnutý na `paused` / `error` přes `POST /_simulator/scenario/…`. Aplikace pošle toast s názvem tiskárny.
- **Banner:** najít okno toastu přes UIA od kořene plochy (Windows 11: okno nového oznámení v procesu `ShellExperienceHost`; hledat podle názvu aplikace „Prusa Tray Diagnostic E2E“ ze zástupce a podle názvu tiskárny v textu, ne podle lokalizovaného názvu okna). Ověřit titulek = název tiskárny a text podle stavu. Snímek. **Ikona:** výřez ikony aplikace v hlavičce toastu porovnat s položkou `app-icon.ico` odpovídající velikosti (tolerance, pozadí toastu). Klik: `Invoke` přes UIA, a když nejde, skutečná myš.
- **Ověření webu:** v `opened-urls.txt` (T-067) se objeví adresa proxy tiskárny (`http://127.0.0.1:<port>/<token>/…`). Test ji otevře přes `HttpClient` a dostane stránku simulátoru („PrusaLink simulator – …“, `StatusPage`) bez dotazu na heslo. Tím se ověří i proxy (T-028, T-052) proti tomuto buildu.
- **Centrum oznámení:** nechat banner vypršet nebo ho zavřít. Otevřít centrum oznámení (Win+N) a najít skupinu testovací aplikace. Klik na toast → nová adresa v `opened-urls.txt`. Centrum zavřít (Esc).
- **Jazyk:** v nastavení (okno přes testovací příkaz `settings`) přepnout jazyk na Deutsch, Uložit, vyvolat další událost → text toastu je německy (porovnání s `Strings.de.resx` přes známý klíč).
- **Úklid (vše i po pádu, záznamy T-065):**
  - historie toastů testovacího AUMID: během testu ji uklidí aplikace při `quit`. Po pádu (úklid T-065) se spustí testovaný exe v režimu `--ptd-e2e-clear-toasts` (T-067). Ten uklidí historii i testovacího zástupce, test zkontroluje kód 0 a že zástupce zmizel;
  - zástupce `Prusa Tray Diagnostic E2E.lnk` v nabídce Start;
  - klíč `…\Notifications\Settings\PrusaTrayDiagnostic.E2E`, který Windows vytvoří po prvním toastu (smazat, pokud před během neexistoval);
  - případné další stopy (např. seznam aplikací v Nastavení → Oznámení, databáze `wpndatabase.db`) zjistit při implementaci (snímek registru a seznamu aplikací před a po). Co nejde odstranit, se přijímá (rozhodnutí 2026-10-22). Musí to být zdokumentované: co přesně zůstává a kde.
  - Ověřit, že produkční zástupce `Prusa Tray Diagnostic.lnk` a klíč `PrusaTrayDiagnostic` (pokud existují) jsou po běhu byte po bytu stejné.

## Akceptační kritéria
- [ ] Banner toastu testovací instance se zobrazí; klik otevře adresu proxy tiskárny a GET přes proxy vrátí stránku simulátoru (test)
- [ ] Toast v centru oznámení: klik, dokud aplikace běží, otevře web (test)
- [ ] Toast po změně jazyka za běhu je v novém jazyce (test)
- [ ] Ikona aplikace v toastu odpovídá `app-icon.ico` (snímek + porovnání)
- [ ] Nerušit, povolení oznámení, historie toastů, testovací zástupce a klíč registru jsou po běhu i po zabitém běhu jako před ním; produkční zástupce a klíč beze změny (test + samotest pádu s úklidem přes `--ptd-e2e-clear-toasts`); cílová platforma testovacího projektu beze změny
- [ ] Stopy, které nejdou odstranit (záznam `PrusaTrayDiagnostic.E2E` v databázi oznámení / v Nastavení → Oznámení), zjištěné a zdokumentované v poznámkách, v nápovědě `tools/run-e2e.ps1` a ve varování T-065
- [x] Testy se zásahy mají trait `SystemChanges` a bez varování se přeskočí (ověřeno během `-NoSystemChanges` 2026-10-22: 4 testy toastů i 2 testy T-067 se zástupcem přeskočené, nic nezměněno)
- [ ] Kritérium T-010 „Kliknutí na notifikaci otevře detail / web tiskárny“ zaškrtnuté a T-010 přesunutý do `done/` (pokud nic dalšího nebrání)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; `tools/run-e2e.ps1` zelené

## Otevřené otázky
- (žádné – Q1, Q2, Q3 a zvuk rozhodnuty 2026-10-22, viz Rozhodnuto)

## Poznámky k implementaci
Stav 2026-10-22 (qa-reviewer): kód a jednotkové testy hotové, `dotnet build` / `dotnet test` / `tools/run-e2e.ps1 -NoSystemChanges` zelené. **Testy se zásahy (skutečné toasty) zatím neběžely** – spustí je uživatel ve svém terminálu (příkaz níže). Kritéria proto zůstávají nezaškrtnutá a úkol v `in-progress`.

**Kód** (vše v `tests/PrusaTrayDiagnostic.App.E2E.Tests`, žádný nový NuGet balíček, cílová platforma `net11.0-windows` beze změny, aplikace beze změny):
- `Infrastructure/ToastChanges.cs` – tři nové druhy zásahů T-065 (záznam v `%LOCALAPPDATA%\PrusaTrayDiagnostic.E2E.Restore`, návrat i po pádu, hlídací proces, `-RestoreOnly`):
  - `ToastsEnabled`: `HKCU\…\PushNotifications\ToastEnabled` (0 = všechna oznámení vypnutá, chybí = zapnuto). Test ho zapne, jen když je vypnutý. Zásada skupiny `NoToastApplicationNotification` (HKCU i HKLM) → test se přeskočí s důvodem.
  - `ToastSettings`: klíč `HKCU\…\Notifications\Settings\PrusaTrayDiagnostic.E2E` (vznikne po prvním toastu). Uloží se celý (existence, hodnoty s typem, názvy podklíčů); návrat klíč smaže, když předtím neexistoval, jinak vrátí hodnoty přesně. Když existuje s `Enabled = 0` / `ShowBanner = 0`, test je dočasně přepne na 1.
  - `ToastHistory` (klíč = cesta testovaného exe): návrat spustí exe s `--ptd-e2e-clear-toasts` (historie toastů testovacího AUMID + testovací zástupce), zkontroluje kód 0 a že zástupce zmizel. Soubor záznamu se řadí před `ToastSettings`, takže po pádu se nejdřív uklidí toasty a teprve pak klíč.
  - `RegistryKeys` (čtení / plán návratu / zápis stavu klíče) a `ClearToasts.RunAsync` (režim úklidu se všemi přepínači testovacího režimu a `FORWARD_ONLY`, jako dřív v T-067; datová složka musí zůstat prázdná). `ClearToasts` spustí jen `PrusaTrayDiagnostic.exe`, jehož kód zná `--ptd-e2e-clear-toasts` (Debug build); Release exe na stejné cestě by přepínač ignoroval a spustil produkční aplikaci, proto se odmítne a záznam zůstane s ručním postupem.
  - Pojistky klíče `ToastSettings`: cesta se skládá jen z AUMID končícího `.E2E` (prázdný nebo produkční AUMID by znamenal smazání nastavení oznámení všech aplikací / produkční aplikace → výjimka). Návrat klíč zapíše, 2 s počká a když ho Windows mezitím znovu zapíše (počitadla po posledním toastu), zapíše ho znovu (nejvýš 3×); pak teprve kontrola záznamu.
- `Infrastructure/Toasts.cs` – UIA: banner se hledá od kořene plochy ve viditelných oknech nejvyšší úrovně procesů `ShellExperienceHost` / `ShellHost` (a oken třídy `Windows.UI.Core.CoreWindow` mimo Průzkumník) podle jedinečného názvu tiskárny (`PTD E2E <8 hex>`) a textu události, nikdy podle lokalizovaného názvu okna. „Oblast toastu“ = největší předek prvku s názvem tiskárny, ve kterém jsou jen texty tohoto toastu (název tiskárny, text události, název souboru úlohy `.bgcode`, název aplikace „Prusa Tray Diagnostic E2E“; obrázky a krátké popisky tlačítek se nepočítají). Snímky i výpisy prvků jsou jen z této oblasti, nikdy celá plocha ani cizí oznámení; při nenalezení banneru se vypíšou jen proces a třída nových oken, ne titulky. Klik: UIA `Invoke` na prvku s názvem tiskárny nebo jeho předkovi uvnitř oblasti (nikdy na jiném tlačítku), když nic neotevře, skutečná myš na titulek (jen pokud toast ještě existuje). Centrum oznámení: Win+N, hledání v okně v popředí, které patří shellu (jiné okno v popředí se nepočítá), Esc jen když centrum zůstalo v popředí. Soukromí: nalezená oblast se použije, jen když kromě textů toastu obsahuje nejvýš 2 krátké cizí názvy (časový údaj); jinak by mohla zahrnovat cizí oznámení. Ve výpisech prvků se cizí názvy nikdy nevypisují, jen jejich délka.
- `Infrastructure/ToastIcon.cs` – ikona v hlavičce toastu: výřez (prvek Image v oblasti, jinak oblast vlevo od názvu aplikace, jinak levý horní roh) se porovná s položkou `app-icon.ico` velikosti 16/20/24/32 DIP × měřítko, vykreslenou přes barvu pozadí výřezu (medián okraje), posouváním po pixelu. Shoda: průměrný rozdíl ≤ 15 % a místo se od samotného pozadí liší aspoň dvakrát víc.
- `Infrastructure/ToastTraces.cs` + `SystemStateSnapshot` – snímek na začátku a konci běhu nově obsahuje `ToastEnabled`, klíč testovací aplikace, existenci testovacího zástupce, SHA-256 produkčního zástupce a produkční klíč `…\Settings\PrusaTrayDiagnostic` (rozdíl = chyba běhu; u produkčního klíče jen poznámka, když běží produkční instance, protože ta může mezitím ukázat vlastní toast). Navíc „stopy“: každý klíč / hodnota pod `HKCU\…\CurrentVersion\Notifications`, `…\PushNotifications` a `HKCU\Software\Classes\AppUserModelId`, která zmiňuje `PrusaTrayDiagnostic.E2E`, a zda ho zmiňuje `wpndatabase.db` (-wal). Nové stopy jsou jen poznámka (přijato 2026-10-22), zapisují se do `system-state-end.txt`.
- `Infrastructure/AppStrings.cs` – texty z `Strings*.resx` aplikace podle klíče (test aplikaci neodkazuje).
- `ToastNotificationTests.cs` (trait `SystemChanges`, bez potvrzeného varování přeskočené): `ToastBannerClickOpensThePrinterWebThroughTheProxy` (pozastavení → banner, titulek = název tiskárny, text = `NotificationPrintPaused`, snímek oblasti, ikona, klik → `opened-urls.txt` → adresa `http://127.0.0.1:<port proxy>/…` ≠ port tiskárny → GET 200 bez `WWW-Authenticate` se stránkou „PrusaLink simulator – …“), `ToastInTheNotificationCenterOpensThePrinterWeb` (chyba → banner vyprší podle `MessageDuration` + 20 s → Win+N → klik → adresa + GET), `ToastFollowsTheLanguageSavedAtRuntime` (anglický toast, Nastavení → Obecné → Deutsch → Uložit, chyba → text `NotificationError` z `Strings.de.resx`), `ToastsOfAKilledRunAreClearedByTheStartCleanup` (podřízený testovací proces `ToastCrashChild` zapíše zásahy, ukáže skutečný toast a je zabit i s aplikací; úklid při startu vrátí oba záznamy, `--ptd-e2e-clear-toasts` skončí 0, zástupce zmizel, klíč je jako před tím a toast už není v centru oznámení). Každý test na konci ověří, že Nerušit zůstalo vypnuté a produkční zástupce je byte po bytu stejný.
- `TestModeOsIntegrationTests` (T-067): oba testy, které spouštějí testovací aplikaci s toasty, mají trait `SystemChanges` (rozhodnutí Q3) a zástupce s toasty vždy zapisují do záznamu `ToastHistory`; s `-NoSystemChanges` se přeskočí. Režim úklidu volají přes sdílené `ClearToasts.RunAsync`; kontrola „úklid nespustil aplikaci“ zůstala (`StartedApp`).
- Hodnota traitu všech testů s toasty je na jednom místě (`ToastSystemChanges.TraitValue` v `ToastChanges.cs`); po sloučení T-066 se přepne na `SystemChangeTraits.Toasts` tam a v konstruktorech tří druhů zásahů v témže souboru.
- Tiché toasty: `TestSettings.Json` píše `notifications.sound = "Off"` (rozhodnutí 2026-10-22).
- Jednotkové testy `ToastChangeTests` (22, běží v každém `dotnet test`, nic nemění): plán návratu klíče, porovnání stavů, zapnutí oznámení testovací aplikace, JSON stavů, registrace druhů a pořadí návratu, rozpoznání vlastních textů toastu, oblast jen jednoho toastu, nevypisování cizích textů, klíč jen testovací aplikace, spuštění jen Debug exe, tiché nastavení testů, trait u všech testů s toasty, vyhledání ikony v syntetické hlavičce (i negativní případy), texty z resx.
- `tools/run-e2e.ps1`: jen nápověda (`.DESCRIPTION`: tiché toasty testovací aplikace jako zásah s varováním, stopy v `wpndatabase.db` a v Nastavení → Oznámení) a dva řádky ve varování (tiché testovací toasty; zapnutí oznámení, když jsou vypnutá). Seznam ve varování jinak beze změny kvůli souběžné úpravě v T-066.
- `docs/troubleshooting.md`: ruční návrat tří nových zásahů a odstavec o stopách, které zůstávají.

**Ověřeno 2026-10-22 (bez zásahů do systému):** `dotnet build` 0 varování, 0 chyb; `dotnet test` 4764 testů, 0 selhalo (66 přeskočeno); `tools/run-e2e.ps1 -NoSystemChanges` (po review): 130 testů, 0 selhalo, 16 přeskočeno (4 testy toastů, podřízený vstup `ToastCrashChild` a 2 testy T-067 se zástupcem mezi nimi), „System changes (after the run): nothing pending“, konec běhu „Differences from the start: none“; nový snímek toastů na začátku i konci: `ToastEnabled` chybí, klíč `PrusaTrayDiagnostic.E2E` chybí, testovací zástupce chybí, `wpndatabase.db` testovací aplikaci nezmiňuje. Zjištění: Windows do klíče `…\Notifications\Settings\<AUMID>` po toastech zapisuje počitadla (`LastNotificationAddedTime`, `PeriodicNotificationCount`, `PeriodicInteractionCount`; vidět u produkčního klíče) – proto se klíč testovací aplikace ukládá a vrací celý.

**Čeká na běh uživatele (zásahy do systému):**
```
pwsh tools/run-e2e.ps1 -Class '*ToastNotificationTests'
pwsh tools/run-e2e.ps1 -Class '*TestModeOsIntegrationTests'
```
Co změní (vše se po testu vrátí, i po pádu): vypne Nerušit, pokud je zapnuté; zapne oznámení Windows a testovací aplikace, pokud jsou vypnutá; ukáže několik tichých toastů testovací aplikace „Prusa Tray Diagnostic E2E“ v rohu obrazovky a v centru oznámení; vytvoří a smaže zástupce `Prusa Tray Diagnostic E2E.lnk` v nabídce Start; otevře Win+N; pohne skutečnou myší (najetí na banner, případně klik na toast); Windows vytvoří a test smaže klíč `…\Notifications\Settings\PrusaTrayDiagnostic.E2E`. Pak vyplnit zjištěné stopy (níže) a zaškrtnout kritéria.

**Rizika neověřená předem** (UIA toastů nešlo prozkoumat bez skutečného toastu): struktura banneru / centra oznámení na Windows 11 26300 (při chybě test vypíše prvky oblasti toastu, případně proces a třídu nových oken); zda Windows po smazání klíč `…\Settings\PrusaTrayDiagnostic.E2E` znovu nevytvoří (návrat by pak selhal a nahlásil to); práh porovnání ikony (15 %) na akrylovém pozadí; zda zápis `ToastEnabled` do registru platí bez aplikace Nastavení (týká se jen počítače s vypnutými oznámeními).

**Stopy, které zůstávají:** (doplnit po běhu se zásahy podle `system-state-end.txt` – poznámky „Trace of the test app left by Windows“).

## Review
qa-reviewer 2026-10-22 (bez běhu se zásahy do systému). Nálezy opravené v rámci review:
- **Rozhodnutí uživatele zapracována:** testy T-067 se zástupcem mají trait `SystemChanges` a vždy zapisují `ToastHistory` (odstraněno větvení podle `IsConfirmed`); testovací nastavení `sound = "Off"` (tiché toasty). Hlídá to `EveryTestThatStartsATestAppWithToastsNeedsTheWarning` a `TestAppsPlayNoSound`.
- **Bezpečnost (blokující, opraveno):** návrat `ToastHistory` spouštěl exe ze záznamu bez kontroly; Release build by `--ptd-e2e-clear-toasts` ignoroval a spustil se jako produkční aplikace. Nově `ClearToasts.NotADebugBuild` (jen `PrusaTrayDiagnostic.exe`, jehož kód zná přepínač).
- **Bezpečnost (opraveno):** klíč `ToastSettings` se maže celý; cesta teď vzniká jen z AUMID končícího `.E2E` (`TestAppKey`), prázdný nebo produkční AUMID → výjimka.
- **Windows znovu zapisuje klíč testovací aplikace (opraveno):** návrat klíče počká 2 s a případně ho zapíše znovu (nejvýš 3×), teprve pak kontrola záznamu.
- **Soukromí (opraveno):** výpisy prvků toastu (`ToastElement.ToString`) nevypisují cizí názvy, jen délku; oblast toastu se použije jen s nejvýš 2 krátkými cizími názvy (časový údaj) – kontejner s cizím oznámením se nikdy nesnímá ani nevypisuje.
- **Nestabilita UIA (opraveno):** okno centra oznámení po Win+N musí patřit shellu, jiné okno v popředí se nepočítá.
- **Drobnost (opraveno):** dotaz WMI na produkční instanci v konečném snímku při chybě nespadne (rozdíl se pak nahlásí jako rozdíl).
Zbývá: běh se zásahy u uživatele (příkazy výše), pak doplnit stopy a zaškrtnout kritéria.
