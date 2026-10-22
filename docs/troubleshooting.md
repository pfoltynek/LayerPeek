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
