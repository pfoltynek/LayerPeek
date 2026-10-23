# Řešení potíží a hlášení chyb

Když aplikace nedělá, co má, pomůže vývojáři log a pár informací o prostředí. Tento návod popisuje, kde log najdete, jak
zapnout podrobné logování a jak předat diagnostický balíček.

## Kde jsou logy

| Systém | Složka |
|---|---|
| Windows | `%LOCALAPPDATA%\PrusaTrayDiagnostic\logs` (např. `C:\Users\<jméno>\AppData\Local\PrusaTrayDiagnostic\logs`) |
| macOS | `~/Library/Logs/PrusaTrayDiagnostic` |

- Aktuální log je `PrusaTrayDiagnostic.log`. Po 1 MB se odsune na `PrusaTrayDiagnostic.1.log` a starší se posunou
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
`PrusaTrayDiagnostic-diagnostics-<datum>-<čas>.zip`. Soubor pak přiložte k hlášení chyby (např. GitHub issue nebo e-mail).

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
motiv, Nerušit, testovací ikonu na hlavním panelu, měřítko monitoru a dočasně vypnutý druhý monitor (T-066), později i toasty (T-068). Varování skriptu vypíše jen ty druhy zásahů, které vybrané testy opravdu udělají. Před každou
změnou se původní stav zapíše do složky **`%LOCALAPPDATA%\PrusaTrayDiagnostic.E2E.Restore`** (jeden soubor `<druh>.json`
na zásah, s popisem původního stavu a ručním postupem). Záznam se smaže, až když se návrat ověří. Prázdná složka se
smaže. Vedle ní je během práce se záznamy zámek `PrusaTrayDiagnostic.E2E.Restore.lock`.

Návrat proběhne sám: po testu (i neúspěšném), při startu dalšího běhu, ve `finally` skriptu (i po Ctrl+C) a v hlídacím
procesu, který počká na konec testovacího procesu (i zabitého) a uklidí po něm. Jeho výpis je v
`system-changes-watchdog.log` ve složce výsledků (`%TEMP%\PrusaTrayDiagnostic.App.E2E.Results.<id>`). Vypnutý monitor se vrací jako první, teprve potom jeho měřítko.

Když přesto něco zůstane (např. restart počítače uprostřed běhu, nebo skript ohlásí „NOT restored“):

1. Spusťte `pwsh tools/run-e2e.ps1 -RestoreOnly`. Vrátí všechny čekající záznamy a vypíše, co vracel. Nic nesestavuje
   kromě testovacího projektu (jen když chybí) a nic jiného nespouští. Bez záznamů nic nemění. Záznamy testovacího
   procesu, který ještě běží, nechá být (vrátí je ten proces nebo jeho hlídací proces).
2. Když návrat selže, záznam zůstane a skript vypíše původní stav. Vraťte ho ručně:

| Zásah | Ruční návrat |
|---|---|
| Motiv | Nastavení → Přizpůsobení → Barvy → Zvolit režim (Vlastní: režim Windows / režim aplikací), nebo hodnoty DWORD `SystemUsesLightTheme` a `AppsUseLightTheme` v `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize` (chybějící hodnota = světlý). `ColorPrevalence` testy nemění. |
| Nerušit | Win+N → tlačítko Nerušit (zvonek) nahoře v centru oznámení, nebo Nastavení → Systém → Oznámení → Nerušit. |
| Testovací ikona na hlavním panelu | Nastavení → Přizpůsobení → Hlavní panel → Další ikony na hlavním panelu (testovací ikonu vypnout), nebo v `HKCU\Control Panel\NotifyIconSettings` u položek testovaného exe vrátit `IsPromoted` na hodnotu ze záznamu (kde chyběla, a u položek vzniklých během běhu, ji smazat). Položky samotné se nemažou (rozhodnutí 2026-10-22). |
| Měřítko (T-066) | Nastavení → Systém → Obrazovka → vybrat monitor → Měřítko (hodnoty ze záznamu). Potom klíč `HKCU\Control Panel\Desktop\PerMonitorSettings` uvést přesně do stavu ze záznamu (záznam obsahuje jeho export): podklíče a hodnoty, které v exportu nejsou, smazat; `(key missing)` = smazat celý klíč. Systémové DPI se mění jen odhlášením a testy ho nemění. |
| Vypnutý monitor (T-066) | Win+P → Rozšířit; pak Nastavení → Systém → Obrazovka: uspořádání, rozlišení, orientace a hlavní monitor podle záznamu. Testy konfiguraci do databáze Windows neukládají, takže po restartu počítače se použije uložená konfigurace se všemi monitory. Okna ostatních aplikací vrací Windows (nastavení „Pamatovat umístění oken podle připojení monitoru“), testy je nepřesouvají. |
| Testovací sonda | Klíč `HKCU\Software\PrusaTrayDiagnostic.E2E.Tests` (jen samotesty infrastruktury) smazat. |

3. Až je vše vrácené, smažte soubory ve složce `%LOCALAPPDATA%\PrusaTrayDiagnostic.E2E.Restore` (jinak je další běh
   zkusí vrátit znovu a před spuštěním testů skončí chybou).

Spuštění bez zásahů: `pwsh tools/run-e2e.ps1 -NoSystemChanges` (bez varování, testy se zásahy se přeskočí).
