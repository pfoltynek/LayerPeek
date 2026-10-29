# Řešení potíží a hlášení chyb

Když aplikace nedělá, co má, pomůže vývojáři log a pár informací o prostředí. Tento návod popisuje, kde log najdete, jak
zapnout podrobné logování a jak předat diagnostický balíček.

## Kde jsou logy

| Systém | Složka |
|---|---|
| Windows | `%LOCALAPPDATA%\LayerPeek\logs` (např. `C:\Users\<jméno>\AppData\Local\LayerPeek\logs`) |
| macOS | `~/Library/Logs/LayerPeek` |

- Aktuální log je `LayerPeek.log`. Po 1 MB se odsune na `LayerPeek.1.log` a starší se posunou
  (`.2.log` až `.4.log`); nejstarší se smaže. Dohromady tedy nejvýš asi 5 MB.
- Každý řádek začíná časem, úrovní (`INF`, `WRN`, `ERR`…), číslem procesu (`pid`) a zdrojem zprávy. Chyby mají pod sebou
  odsazené podrobnosti včetně stack trace.
- Složku otevřete nejsnáz tlačítkem **Otevřít složku s logy** v okně **O aplikaci** (menu ikony v oznamovací oblasti
  → O aplikaci). Pokud složka ještě neexistuje, vytvoří se.

## Podrobné logování

Ve výchozím stavu aplikace zapisuje jen důležité události (úroveň `Information`). Když vás vývojář požádá o podrobný log:

1. Otevřete **Nastavení** → záložka **Obecné**.
2. Zaškrtněte **Podrobné logování (Debug)** a klikněte na **Uložit**. Změna platí hned, bez restartu.
3. Zopakujte problém a pak uložte diagnostický balíček (viz níže).
4. Podrobné logování zase vypněte – log s ním roste rychleji.

## Diagnostický balíček

V okně **O aplikaci** klikněte na **Uložit diagnostický balíček…** a zvolte, kam soubor uložit. Výchozí název je
`LayerPeek-diagnostics-<datum>-<čas>.zip`. Soubor pak přiložte k hlášení chyby (např. GitHub issue nebo e-mail).

Balíček obsahuje:

- **`logs/`** – všechny soubory ze složky s logy, beze změny.
- **`info.txt`** – anglický přehled: verze aplikace, systém, verze .NET, jazyk, dostupnost FFmpeg a seznam tiskáren
  s jejich stavem (název, adresa, typ připojení, uživatelské jméno PrusaLink, kamera).
- **`settings.json`** – nastavení aplikace sestavené znovu z načteného nastavení.

Balíček **neobsahuje**:

- hesla, API klíče ani tokeny (jsou v úložišti hesel systému, na Windows ve Správci pověření, a do balíčku se nikdy
  nedostanou),
- přihlašovací údaje ani dotazovou část (`?…`) v adresách kamer,
- uložené pozice oken kamer (`camera-windows.json`), uloženou velikost oken Nastavení a Diagnostika (`windows.json`)
  ani vlastní obrázky tiskáren.

Soubor `windows.json` (ve stejné složce jako `settings.json`) si pamatuje jen velikost a maximalizaci oken Nastavení
a Diagnostika. Když se okno otevírá v nevhodné velikosti, zavřete aplikaci a soubor smažte: okna se pak otevřou ve výchozí
velikosti a nastavení ani pozice oken kamer se nezmění. Poškozený soubor aplikace sama ignoruje a při dalším zavření okna
ho přepíše.

Názvy a adresy tiskáren a uživatelské jméno PrusaLink v balíčku zůstávají. Logy se kopírují tak, jak jsou; pokud v nich
nechcete nic sdílet, otevřete je před odesláním a zkontrolujte.

Když uložení selže (např. složka bez práv zápisu nebo plný disk), okno to oznámí a nedokončený soubor smaže. Zkuste jinou
složku.

## Kopírování informací do schránky

Když ZIP poslat nejde (např. rychlé hlášení do GitHub issue), klikněte v okně **O aplikaci** na **Kopírovat info do
schránky**. Tlačítko na dvě sekundy ukáže „✓ Zkopírováno“ a ve schránce je anglický text:

- stejný přehled jako `info.txt` v diagnostickém balíčku (verze, systém, tiskárny a jejich stav),
- pod ním **posledních 200 řádků logu**; když je aktuální soubor po rotaci krátký, doplní se z předchozího.

Celý text má nejvýš asi 64 KB: když je log delší, vynechají se nejstarší řádky, přehled o prostředí zůstane celý. Text
neobsahuje hesla, klíče ani přihlašovací údaje a dotazovou část adres kamer; řádky logu jsou takové, jaké jsou v souboru.
Před vložením do veřejného hlášení ho můžete projít.

## Přerušený E2E běh vrátil nastavení jen zčásti

*Pro vývojáře.* E2E testy skutečné aplikace (`tools/run-e2e.ps1`, T-044) mohou dočasně měnit nastavení Windows (T-065):
motiv, Nerušit, testovací ikonu na hlavním panelu, měřítko monitoru a dočasně vypnutý druhý monitor (T-066), toasty testovací aplikace (T-068) a položky Správce pověření testovací aplikace (T-073). Varování skriptu vypíše jen ty druhy zásahů, které vybrané testy opravdu udělají. Před každou
změnou se původní stav zapíše do složky **`%LOCALAPPDATA%\LayerPeek.E2E.Restore`** (jeden soubor `<druh>.json`
na zásah, s popisem původního stavu a ručním postupem). Záznam se smaže, až když se návrat ověří. Prázdná složka se
smaže. Vedle ní je během práce se záznamy zámek `LayerPeek.E2E.Restore.lock`.

Návrat proběhne sám: po testu (i neúspěšném), při startu dalšího běhu, ve `finally` skriptu (i po Ctrl+C) a v hlídacím
procesu, který počká na konec testovacího procesu (i zabitého) a uklidí po něm. Jeho výpis je v
`system-changes-watchdog.log` ve složce výsledků (`%TEMP%\LayerPeek.App.E2E.Results.<id>`). Vypnutý monitor se vrací jako první, teprve potom jeho měřítko.

Když přesto něco zůstane (např. restart počítače uprostřed běhu, nebo skript ohlásí „NOT restored“):

1. Spusťte `pwsh tools/run-e2e.ps1 -RestoreOnly`. Vrátí všechny čekající záznamy a vypíše, co vracel. Nic nesestavuje
   kromě testovacího projektu (jen když chybí) a nic jiného nespouští. Bez záznamů nic nemění. Záznamy testovacího
   procesu, který ještě běží, nechá být (vrátí je ten proces nebo jeho hlídací proces).
2. Když návrat selže, záznam zůstane a skript vypíše původní stav. Vraťte ho ručně:

| Zásah | Ruční návrat |
|---|---|
| Motiv | Nastavení → Přizpůsobení → Barvy → Zvolit režim (Vlastní: režim Windows / režim aplikací), nebo hodnoty DWORD `SystemUsesLightTheme` a `AppsUseLightTheme` v `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize` (chybějící hodnota = světlý). `ColorPrevalence` testy nemění. |
| Nerušit | Kliknout na hodiny na hlavním panelu (nebo Win+N) → tlačítko Nerušit (zvonek) nahoře v centru oznámení, nebo Nastavení → Systém → Oznámení → Nerušit. |
| Testovací ikona na hlavním panelu | Nastavení → Přizpůsobení → Hlavní panel → Další ikony na hlavním panelu (testovací ikonu vypnout), nebo v `HKCU\Control Panel\NotifyIconSettings` u položek testovaného exe vrátit `IsPromoted` na hodnotu ze záznamu (kde chyběla, a u položek vzniklých během běhu, ji smazat). Položky samotné se nemažou (rozhodnutí 2026-10-22). |
| Toasty testovací aplikace (`ToastHistory`) | Spusťte Debug exe z cesty uvedené v záznamu s argumentem `--layerpeek-e2e-clear-toasts` (vymaže toasty `LayerPeek.E2E` z centra oznámení, smaže zástupce `LayerPeek E2E.lnk` a registraci testovacího COM aktivátoru toastů `HKCU\Software\Classes\CLSID\{EDD3ED5E-8363-4CF4-977A-64A623E0A5A2}` z T-068, kód 0 = hotovo). Ručně: kliknout na hodiny na hlavním panelu (nebo Win+N) → u skupiny „LayerPeek E2E“ vymazat oznámení, smazat `%APPDATA%\Microsoft\Windows\Start Menu\Programs\LayerPeek E2E.lnk` a v Editoru registru klíč `HKCU\Software\Classes\CLSID\{EDD3ED5E-8363-4CF4-977A-64A623E0A5A2}`. Produkční zástupce `LayerPeek.lnk` ani registraci produkčního aktivátoru `{EC47413C-984D-447A-866A-5F780E408DE7}` testy nemění. |
| Nastavení oznámení testovací aplikace (`ToastSettings`) | Klíč `HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\LayerPeek.E2E`: když v záznamu chyběl, smazat ho; jinak vrátit hodnoty ze záznamu. Klíč produkční aplikace `…\Settings\LayerPeek` testy nemění. |
| Oznámení Windows (`ToastsEnabled`) | Nastavení → Systém → Oznámení → Oznámení (zapnuto/vypnuto podle záznamu), nebo hodnota DWORD `ToastEnabled` v `HKCU\Software\Microsoft\Windows\CurrentVersion\PushNotifications` (chybějící = zapnuto). |
| Položky Správce pověření (`Credentials`, T-073) | Ovládací panely → Správce pověření → Přihlašovací údaje systému Windows → obecné přihlašovací údaje `LayerPeek.E2E/…` odebrat. Jsou jen pro relaci (`CRED_PERSIST_SESSION`), takže zmizí i odhlášením. Každý další běh je na začátku i na konci maže sám. Položky `LayerPeek/…` patří skutečné aplikaci, ty testy nečtou ani nemění. |
| Měřítko (T-066) | Nastavení → Systém → Obrazovka → vybrat monitor → Měřítko (hodnoty ze záznamu). Potom klíč `HKCU\Control Panel\Desktop\PerMonitorSettings` uvést přesně do stavu ze záznamu (záznam obsahuje jeho export): podklíče a hodnoty, které v exportu nejsou, smazat; `(key missing)` = smazat celý klíč. Systémové DPI se mění jen odhlášením a testy ho nemění. |
| Vypnutý monitor (T-066) | Win+P → Rozšířit; pak Nastavení → Systém → Obrazovka: uspořádání, rozlišení, orientace a hlavní monitor podle záznamu. Testy konfiguraci do databáze Windows neukládají, takže po restartu počítače se použije uložená konfigurace se všemi monitory. Okna ostatních aplikací vrací Windows (nastavení „Pamatovat umístění oken podle připojení monitoru“), testy je nepřesouvají. |
| Testovací instalace Velopack (`VelopackInstall`, T-017) | Odinstalovat: `"%LOCALAPPDATA%\LayerPeek\Update.exe" --uninstall --silent`, nebo Nastavení → Aplikace → Nainstalované aplikace → LayerPeek → Odinstalovat. Ze zbylé složky `%LOCALAPPDATA%\LayerPeek` smazat všechno kromě původního obsahu (logy), soubory ze zálohy `%LOCALAPPDATA%\LayerPeek.E2E.InstallerBackup` zkopírovat zpět a zálohu smazat. Když složka v záznamu chyběla, smazat ji celou. Smazat i zbylé složky `%LOCALAPPDATA%\LayerPeek.<16 znaků>`, které nechal `Setup.exe`. Pak jednou spustit vývojové sestavení LayerPeeku: znovu vytvoří zástupce `LayerPeek.lnk` ve Start menu a registraci aktivátoru toastů `HKCU\Software\Classes\CLSID\{EC47413C-984D-447A-866A-5F780E408DE7}`. Klíč `HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\LayerPeek` vrátit podle záznamu (chyběl = smazat). Složku `%LOCALAPPDATA%\velopack` smazat jen tehdy, když v záznamu chyběla. Toasty LayerPeeku, které odinstalace vymazala z centra oznámení, vrátit nejde (varování skriptu to uvádí). |
| Testovací sonda | Klíč `HKCU\Software\LayerPeek.E2E.Tests` (jen samotesty infrastruktury) smazat. |

3. Až je vše vrácené, smažte soubory ve složce `%LOCALAPPDATA%\LayerPeek.E2E.Restore` (jinak je další běh
   zkusí vrátit znovu a před spuštěním testů skončí chybou).

Spuštění bez zásahů: `pwsh tools/run-e2e.ps1 -NoSystemChanges` (bez varování, testy se zásahy se přeskočí).

**Stopy, které se neodstraňují (přijato 2026-10-22, T-068):** po testech toastů si Windows může dál pamatovat testovací
aplikaci `LayerPeek.E2E` v databázi oznámení `%LOCALAPPDATA%\Microsoft\Windows\Notifications\wpndatabase.db`
a v seznamu aplikací v Nastavení → Systém → Oznámení. Běh všechny nalezené stopy testovací aplikace (klíče a hodnoty pod
`HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications`, `…\PushNotifications`, `HKCU\Software\Classes\AppUserModelId`
a zmínku v databázi oznámení) zapíše do `system-state-start.txt` / `system-state-end.txt` ve složce výsledků. Nijak
nevadí a nic nespouštějí. Co přesně v tomto počítači zůstává, je v poznámkách T-068.

**Záznamy z doby před přejmenováním (T-074):** do přejmenování na LayerPeek byla složka záznamů
`%LOCALAPPDATA%\PrusaTrayDiagnostic.E2E.Restore` a záznamy v ní pojmenovávají testovací aplikaci, zástupce, pověření a
klíče registru starého názvu, které nový kód nezná. `tools/run-e2e.ps1` proto při každém spuštění (i s `-RestoreOnly`)
nejdřív zkontroluje i tuto složku: prázdnou smaže, s čekajícími záznamy (`*.json`) skončí chybou a vypíše postup. Záznamy
vrátí kód před přejmenováním (commit `fc2bb11`) v dočasném worktree:

```powershell
git worktree add <prázdná dočasná složka> fc2bb11
pwsh <ta složka>/tools/run-e2e.ps1 -RestoreOnly
git worktree remove <ta složka>
```

Ručně: podle tabulky výše se starými názvy (`PrusaTrayDiagnostic.E2E`, `Prusa Tray Diagnostic E2E.lnk`, testovací
aktivátor `{1708A774-C00D-4A8B-B9E5-1BEB8527DD24}`, `HKCU\Software\PrusaTrayDiagnostic.E2E.Tests`), potom složku smazat.

## Upozornění se na macOS nezobrazují

- LayerPeek posílá upozornění jen jako aplikace `LayerPeek.app` (ne vývojové sestavení spuštěné přes `dotnet run`). O povolení macOS požádá až s prvním upozorněním, a to vlastním oznámením s dotazem (Volby → Povolit; přesné znění určuje macOS).
- Když jsou upozornění v macOS vypnutá, okno Nastavení to na záložce Obecné u sekce Upozornění řekne a tlačítkem otevře Nastavení systému → Oznámení → LayerPeek. Po povolení zmizí hláška hned, jak se do okna vrátíte. Log obsahuje větu „Notifications of io.github.pfoltynek.LayerPeek are turned off in macOS“.
- Do logu se zapíše i každá chyba macOS při zobrazení upozornění („macOS did not show a notification (code …)“).

*Pro vývojáře (T-020).* Skutečné notifikace ověřuje jen E2E test na vyžádání (běžný `dotnet test` žádný bundle nestaví a o povolení nežádá):

```bash
LAYERPEEK_E2E=1 dotnet test --project tests/LayerPeek.App.Tests -- --filter-class "*MacNotificationsE2ETests"
```

Test sestaví Debug aplikaci jako `LayerPeek E2E.app` s identifikátorem `io.github.pfoltynek.LayerPeek.E2E` (ad-hoc podpis) v `artifacts/e2e-macos-notifications/` a spustí ji v testovacím režimu proti simulované tiskárně. Produkční identifikátor ani jeho povolení nepoužije.
1. Při prvním běhu macOS požádá o povolení oznámením „LayerPeek E2E“. Test na odpověď čeká 2 minuty, jinak se přeskočí s postupem. Zvolte Povolit (nebo Nastavení systému → Oznámení → LayerPeek E2E → Povolit oznámení) a spusťte test znovu.
2. Při uspaném displeji aplikace s uživatelským rozhraním čeká, až se displej probudí (T-096); tiskárnu sleduje a oznámení posílá už během čekání (T-099), ale tento test tak ověřen nebyl. Pouštějte ho s probuzeným displejem, případně `caffeinate -d -u -t 600 &` předem.
3. Klik na oznámení test neumí (vyžadovalo by oprávnění Zpřístupnění). Ručně: při běžícím testu (nebo běžící `LayerPeek.app`) klikněte na oznámení – otevře se web tiskárny (v testovacím režimu se adresa jen zapíše do `opened-urls.txt`).
4. Po pádu testu uklidí notifikace testovacího bundlu `".../LayerPeek E2E.app/Contents/MacOS/LayerPeek" --layerpeek-e2e-clear-toasts` (jen Debug, jen v testovacím bundlu). Registraci v LaunchServices odstraní `lsregister -u <cesta k bundlu>` (další běh testu odregistruje staré testovací bundly sám).
5. Položka „LayerPeek E2E“ v Nastavení systému → Oznámení zůstane (macOS ji neumí odstranit skriptem); je to známý pozůstatek a nevadí.

## LayerPeek se na macOS po startu neobjeví v menu baru (uspaný displej)

- Když se LayerPeek spouští ve chvíli, kdy žádný displej není aktivní (displej uspaný, zavřené víko bez monitoru, „Otevřít při přihlášení“ během spánku displeje), počká, až se displej probudí, a teprve pak se objeví v menu baru (nejpozději asi sekundu po probuzení). Do té doby běží proces bez ikony, ale tiskárny už sleduje a posílá oznámení (T-099); klik na oznámení otevře web tiskárny. Ikona po probuzení hned ukazuje aktuální stav. Důvod: Avalonia na macOS při startu vytváří `CVDisplayLink` pro aktivní displeje a bez nich skončí chybou `kCVReturnInvalidDisplay` (-6661, „Avalonia.Native was not able to start the RenderTimer“); dřívější verze proto při startu spadly.
- Log obsahuje „No active display (all displays asleep or none connected): the menu bar item starts once a display is active.“, hned po něm „Starting printer monitoring and notifications while waiting for an active display.“ a po probuzení „… active display(s) after …; starting the UI.“ a „The menu bar item attaches to the monitoring that already runs.“.
- Druhý start během čekání jen předá aktivaci čekající instanci a skončí (jedna instance, T-043); čekající instance si aktivaci zapamatuje (víc startů = jedna aktivace) a po probuzení displeje udělá totéž co druhý start jindy (otevře naposledy použité okno, jinak Nastavení); v logu „The instance command Activate waits for the menu bar item …“. Ukončení přes `kill` (SIGTERM) nebo Ctrl+C čekání ukončí bez chyby a sledování tiskáren zastaví (nejdéle asi 5 s).
- Když se displej uspí až během startu (v okně milisekund mezi kontrolou a inicializací Avalonie), může aplikace stále skončit uvedenou chybou; v logu je pak její zásobník. Spusťte ji znovu.
- *Pro vývojáře.* `scripts/aot-smoke.ps1` ověřuje, že aplikace 10 s běží a je typu `UIElement`. S uspaným displejem už nespadne, ale čeká bez rozhraní a bez AppKitu, takže kontrola typu v LaunchServices nejspíš neprojde (neověřeno) a smoke test by ověřil jen čekání, ne start rozhraní. Smoke test (i `release-build.ps1`) proto dál pouštějte s probuzeným displejem, např. `caffeinate -d -u -i pwsh scripts/release-build.ps1` (`caffeinate` jen drží displej vzhůru, nic nenastavuje).
- *Pro vývojáře.* Čekání bez uspání displeje ověřuje E2E test na vyžádání (T-099): `LAYERPEEK_E2E=1 caffeinate -d -u dotnet test --project tests/LayerPeek.App.Tests -- --filter-class "*MacDisplayWaitE2ETests"`. Sestaví testovací `LayerPeek E2E.app` (jako test notifikací, po sobě ho uklidí včetně registrace v LaunchServices a domény předvoleb), spustí Debug aplikaci v testovacím režimu s `LAYERPEEK_TEST_NO_DISPLAY_MS` proti simulované tiskárně a ověří sledování během čekání, připojení menu baru, jednu aktivaci po čekání a ukončení s kódem 0 (příkaz quit i SIGTERM). Notifikace nezkouší (potřebují spuštění přes LaunchServices a oprávnění testovacího bundlu). Po probuzení se krátce otevře okno Nastavení (aktivace).

## Tiskárna nebo kamera na macOS není dostupná (oprávnění Místní síť)

- Od macOS 15 potřebuje každá aplikace pro spojení se zařízením v místní síti oprávnění **Místní síť**. Při prvním spojení s tiskárnou nebo kamerou se macOS zeptá („„LayerPeek“ chce vyhledávat a připojovat se k zařízením v místní síti“, přesné znění určuje macOS) s vysvětlením, proč ho LayerPeek potřebuje.
- Dokud oprávnění není povolené (dotaz ještě visí nebo bylo zamítnuto), macOS spojení hned odmítne chybou „No route to host“. LayerPeek pak u adresy v místní síti místo „Tiskárna není dostupná“ napíše „Bez spojení. LayerPeek možná nemá povolený přístup k místní síti v Nastavení systému > Soukromí a zabezpečení > Místní síť.“ (okno Diagnostika, Otestovat připojení, test kamery, okno kamery). Stejnou chybu mají i běžné příčiny (tiskárna vypnutá, jiná podsíť), proto „možná“.
- Povolení: **Nastavení systému → Soukromí a zabezpečení → Místní síť → LayerPeek** zapnout. Spojení se pak obnoví samo při dalším pokusu. V okně Diagnostika a pod výsledkem „Otestovat připojení“ a testu kamery je u hlášky tlačítko **Otevřít Nastavení systému**, které otevře panel Místní síť (když ho macOS nezná, aspoň Soukromí a zabezpečení).
- Log obsahuje větu „Local Network access for LayerPeek may be off (System Settings > Privacy & Security > Local Network)“, jednou za tiskárnu nebo kameru a běh.
- Aplikace s ad-hoc podpisem (bez Developer ID, T-060): macOS podle TN3179 nemusí identitu aplikace pro toto oprávnění spolehlivě sledovat, po aktualizaci se proto může zeptat znovu.
- Vývojové sestavení spuštěné z Terminálu (`dotnet run`, testy) se macOS neptá: procesy z Terminálu mají přístup povolený automaticky. Simulátor na `127.0.0.1` místní síť není.

## Přechod ze starého názvu PrusaTrayDiagnostic (jen vývojové počítače)

Aplikace se do prvního vydání jmenovala Prusa Tray Diagnostic (T-074). Sama nic nepřevádí. Na počítači, kde běžely staré
vývojové buildy, převede data jednorázový skript (PowerShell 7.2+, obě aplikace musí být ukončené):

```powershell
pwsh tools/migrate-from-prusatraydiagnostic.ps1 -WhatIf     # jen ukáže, co by udělal
pwsh tools/migrate-from-prusatraydiagnostic.ps1             # převod, na konci se zeptá na smazání starých dat
pwsh tools/migrate-from-prusatraydiagnostic.ps1 -RemoveOld  # později: jen smazání starých dat (zeptá se)
```

- Zkopíruje `%APPDATA%\PrusaTrayDiagnostic` → `%APPDATA%\LayerPeek`, jen když cíl ještě neexistuje. Do zkopírované
  staré složky zapíše značku `.migrated-to-LayerPeek`; smazat lze jen starou složku se značkou.
- **Staré logy nekopíruje** (`%LOCALAPPDATA%\PrusaTrayDiagnostic\logs`): LayerPeek začne vlastní log. Kdo je chce
  schovat, zkopíruje si je ručně před smazáním.
- **Pozor, LayerPeek už běžel** (např. `dotnet run`; `scripts/aot-smoke.ps1` od T-095 složky dat a logů po běhu vrátí do původního stavu): pak
  `%APPDATA%\LayerPeek` už existuje, skript starou složku nezkopíruje (stav `TargetExists`) a nikdy ji nesmaže
  („existed before the migration“). Kdo chce stará data, nejdřív ukončí LayerPeek, novou složku sám přesune jinam
  (nebo smaže) a skript spustí znovu.
- Položky Správce pověření `PrusaTrayDiagnostic/<klíč>` zkopíruje na `LayerPeek/<klíč>` (existující nové nechá). Heslo
  kopíruje uvnitř Windows a nikdy ho nevypisuje. Testovací položky `PrusaTrayDiagnostic.E2E/…` nechá být.
- Smazání starých dat jen po napsání přesně `REMOVE`: `%APPDATA%\PrusaTrayDiagnostic` (jen se značkou a s existujícím
  `%APPDATA%\LayerPeek`), složka starých logů `%LOCALAPPDATA%\PrusaTrayDiagnostic` – **jediná výjimka** z pravidla
  „jen co skript zkopíroval“, protože logy se nekopírují: smaže se bez kopie, ale také jen po `REMOVE`, jen přesně tato
  složka (ne sourozenci jako `PrusaTrayDiagnostic.E2E.Restore`) a jen když v ní není nic než skutečná (ne odkazem
  přesměrovaná) složka `logs`; jinak zůstane a skript vypíše důvod –,
  `%LOCALAPPDATA%\PrusaTrayDiagnostic.Build` (cache FFmpeg starých buildů), staré položky pověření (jen převedené),
  zástupce `Prusa Tray Diagnostic.lnk` v nabídce Start, aktivátor toastů
  `HKCU\Software\Classes\CLSID\{E8389DCA-851B-498C-8082-69B1AB263C14}` a nastavení oznámení
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\PrusaTrayDiagnostic`. Nová aplikace si zástupce,
  aktivátor (nové CLSID `{EC47413C-984D-447A-866A-5F780E408DE7}`) i nastavení oznámení vytvoří při prvním startu.
- Staré toasty v centru oznámení skript nemaže (zmizí po jejich zavření). Složky výsledků starých E2E běhů
  `%TEMP%\PrusaTrayDiagnostic.App.E2E.Results.*` lze smazat ručně.
