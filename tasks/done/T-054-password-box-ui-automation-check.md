---
id: T-054
title: Ověření, že pole hesla nevydá heslo přes UI Automation (I4)
status: done
owner: qa-reviewer
priority: P3
platform: windows
depends_on: [T-044]
---

## Cíl
Ověřit nejistý informační nález I4 z bezpečnostní revize (2026-10-21). Nejisté je, zda `TextBox` Avalonie s `PasswordChar` (pole hesla PrusaLink a přihlašovacích údajů kamery v okně nastavení) nevydává skutečné heslo přes UI Automation. Kdyby ano, mohl by ho přečíst jakýkoli proces stejného uživatele přes UIA, nejen čtečka obrazovky. Revize to **nepotvrdila ani nevyvrátila**. Úkol je měření a hlášení, ne oprava.

## Rozsah
- E2E test nad infrastrukturou T-044 (FlaUI, testovací režim aplikace, spouštění na vyžádání): otevřít Nastavení, zadat známé heslo do pole hesla (a do pole hesla kamery) a přes UIA přečíst:
  - `ValuePattern.Value`, `Name`, `HelpText`, `LegacyIAccessiblePattern.Value` / `Name`, `TextPattern` (`DocumentRange.GetText`), pokud ho prvek nabízí;
  - vlastnost `IsPassword`.
- Pokud kterýkoli zdroj vrátí heslo v čitelné podobě, test selže a nález se zapíše do Poznámek k implementaci (verze Avalonie, které vlastnosti heslo vydávají). Návrh opravy, např. vlastní `AutomationPeer`, předat jako otevřenou otázku a novým úkolem; v tomto úkolu se neopravuje.
- Pokud heslo nevydá žádný zdroj, test zůstane v E2E sadě jako regresní kontrola, třeba při aktualizaci Avalonie.
- Bez nového NuGet balíčku (FlaUI už je v E2E projektu z T-044; jen testy, nedodává se).

## Akceptační kritéria
- [x] E2E test čte všechny uvedené vlastnosti UIA polí hesla a ověřuje, že žádná neobsahuje zadané heslo
- [x] Výsledek měření (vydává / nevydává, které vlastnosti, verze Avalonie) zapsaný v Poznámkách k implementaci; při úniku nový úkol s návrhem opravy
- [x] Test po sobě nic nenechá (proces, testovací složka)
- [x] `dotnet build` a `dotnet test` bez chyb a varování; E2E běh na Windows úspěšný

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- Test: `tests/LayerPeek.App.E2E.Tests/PasswordAutomationTests.cs`, `PasswordBoxDoesNotExposeThePasswordThroughUiAutomation`
  (bez zásahu do systému, bez traitu `SystemChanges`). Spuštění:
  `pwsh tools/run-e2e.ps1 -Class '*PasswordAutomationTests' -NoSystemChanges` (s `-PublishAot` i proti Native AOT exe).
- Postup: simulovaná tiskárna PrusaLink s náhodným heslem `E2E-pw-<16 hex>` (23 znaků), tiskárna uložená bez hesla.
  Heslo se zadá do `PrinterPassword` přes `ValuePattern.SetValue`; že v poli opravdu je, dokazuje úspěšný test spojení
  (simulátor bez něj přihlášení odmítne). Pak se čte pole hesla a celý surový strom (raw view) okna nastavení: `Name`,
  `HelpText`, `ItemStatus`, `ValuePattern.Value`, `LegacyIAccessible.Value` / `Name` / `Description` / `Help`,
  `TextPattern.DocumentRange.GetText` a `GetSelection` (pokud ho prvek nabízí) a `IsPassword`. Únik = celé heslo nebo
  jakýkoli jeho 6znakový úsek. Čtení `ValuePattern.Value` pole hesla musí uspět (maska nebo prázdná hodnota).
  Totéž po uložení a znovuotevření nastavení; tam navíc nový test spojení s nedotčeným polem uspěje, takže heslo bylo
  opravdu uložené (simulátor ho dál vyžaduje). Kontrola měření: jedinečný text v běžném poli `PrinterUserName` stejné
  procházení najde a test vyžaduje oba zdroje (`ValuePattern.Value` i `LegacyIAccessible.Value`), takže kontrola
  nemůže projít naprázdno. Neúspěšné kroky procházení stromu (prvek mezitím zmizel) se počítají a zapisují do zprávy;
  test selže, když jich je 5 % počtu prvků nebo víc (pár zmizelých prvků je normální, rozbité procházení selže skoro
  u všech). Naměřeno 0. Zpráva bez hesla (jen „LEAK“ a délka) jde do výsledků jako `NNN-password-automation-readings.txt`.
- **Výsledek měření (2026-11-15, Avalonia 12.1.6, Windows 11 Pro 10.0.26300, Debug i Native AOT): heslo NEUNIKÁ.**
  - `ValuePattern.Value` a `LegacyIAccessible.Value` = maska `●` o délce hesla (23 ×); po znovuotevření prázdné.
    Potvrzeno i ve zdroji: `TextBoxAutomationPeer.Value` i událost změny hodnoty vracejí `new string(PasswordChar, Text.Length)`.
  - `Name` / `LegacyIAccessible.Name` = „Password“, `HelpText` / `LegacyIAccessible.Help` = nápověda pole,
    `LegacyIAccessible.Description` prázdné, `ItemStatus` neposkytnuto, `TextPattern` prvek nenabízí.
  - V surovém stromu okna (≈ 214 prvků) heslo nemá žádný prvek.
  - **`IsPassword` = `False`**: `Avalonia.Win32.Automation.AutomationNode` vlastnost vůbec neobsluhuje a `AutomationPeer`
    pro ni nemá API. Není to únik hesla, test ji proto jen zapisuje do zprávy; nález (spolu s vydanou délkou hesla)
    je nový úkol **T-234** s návrhem opravy (upstream Avalonia, volitelně vlastní peer bez délky).
  - Kopírování / vyjmutí z pole hesla Avalonia zakazuje (`TextBox.IsPasswordBox`), schránkou heslo neunikne (zjištěno
    ze zdroje, testem neověřováno – test by přepsal uživatelovu schránku).
- Pole hesla kamery v aplikaci **neexistuje**: přihlašovací údaje v adrese kamery se odmítají (T-014), kamera nemá
  samostatné pole hesla. Kontroluje se proto jen `PrinterPassword`; adresa kamery je běžné textové pole.
- Úklid: aplikace i simulátor se ukončí (`await using`), data testovací aplikace jsou v rezervované složce běhu
  v `%TEMP%`, kterou infrastruktura T-044 smaže. Po bězích nezůstal žádný proces testovací aplikace ani testovací
  složka (běží jen nainstalovaný LayerPeek uživatele, ten se nemění); ve výsledcích ani logu heslo není.
- `dotnet build`: 0 varování, 0 chyb; `dotnet test`: 11414 celkem, 10822 prošlo, 592 přeskočeno, 0 selhalo.
- Běhy E2E: 4× zeleně Debug exe (po dokončení testu), 1× zeleně s `-PublishAot` (Debug i Native AOT exe); po připomínkách revize 2× zeleně Debug exe.
