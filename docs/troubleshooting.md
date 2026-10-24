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
- uložené pozice oken kamer (`camera-windows.json`) ani vlastní obrázky tiskáren.

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

## Přechod ze starého názvu PrusaTrayDiagnostic (jen vývojové počítače)

Aplikace se do prvního vydání jmenovala Prusa Tray Diagnostic (T-074). Sama nic nepřevádí. Na počítači, kde běžely staré
vývojové buildy, převede data jednorázový skript (PowerShell 7.2+, obě aplikace musí být ukončené):

```powershell
pwsh tools/migrate-from-prusatraydiagnostic.ps1 -WhatIf     # jen ukáže, co by udělal
pwsh tools/migrate-from-prusatraydiagnostic.ps1             # převod, na konci se zeptá na smazání starých dat
pwsh tools/migrate-from-prusatraydiagnostic.ps1 -RemoveOld  # později: jen smazání starých dat (zeptá se)
```

- Zkopíruje `%APPDATA%\PrusaTrayDiagnostic` → `%APPDATA%\LayerPeek` a `%LOCALAPPDATA%\PrusaTrayDiagnostic\logs` →
  `%LOCALAPPDATA%\LayerPeek\logs` (logy přejmenuje na `LayerPeek*.log`), jen když cíl ještě neexistuje.
- Položky Správce pověření `PrusaTrayDiagnostic/<klíč>` zkopíruje na `LayerPeek/<klíč>` (existující nové nechá). Heslo
  kopíruje uvnitř Windows a nikdy ho nevypisuje. Testovací položky `PrusaTrayDiagnostic.E2E/…` nechá být.
- Smazání starých dat jen po napsání `REMOVE`: staré složky (jen když existuje jejich nový protějšek),
  `%LOCALAPPDATA%\PrusaTrayDiagnostic.Build` (cache FFmpeg starých buildů), staré položky pověření (jen převedené),
  zástupce `Prusa Tray Diagnostic.lnk` v nabídce Start, aktivátor toastů
  `HKCU\Software\Classes\CLSID\{E8389DCA-851B-498C-8082-69B1AB263C14}` a nastavení oznámení
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\PrusaTrayDiagnostic`. Nová aplikace si zástupce,
  aktivátor (nové CLSID `{EC47413C-984D-447A-866A-5F780E408DE7}`) i nastavení oznámení vytvoří při prvním startu.
- Staré toasty v centru oznámení skript nemaže (zmizí po jejich zavření). Složky výsledků starých E2E běhů
  `%TEMP%\PrusaTrayDiagnostic.App.E2E.Results.*` lze smazat ručně.
