---
id: T-027
title: PrusaLink – uživatelské jméno v nastavení
status: done
owner: core-dev + ui-dev
priority: P2
platform: all
depends_on: [T-003, T-015]
---

## Cíl
Uživatelské jméno pro PrusaLink už nebude natvrdo `maker`, ale půjde zadat u každé tiskárny v nastavení. Použije se pro digest přihlášení klienta PrusaLink a v T-028 i pro otevření webu.

## Rozsah
- `PrinterDefinition`: nová vlastnost s uživatelským jménem (např. `UserName`), výchozí hodnota `maker`. Jméno není tajný údaj, ukládá se do běžného souboru nastavení. Heslo zůstává v `ISecretStore`.
- `SettingsDocument` / `SettingsMapper`: nové pole v souboru nastavení. Pokud v existujícím souboru chybí, doplní se `maker` (zpětná kompatibilita, žádná chyba).
- Validace jména: neprázdné po oříznutí mezer, rozumná maximální délka, bez řídicích znaků. Dvojtečka není povolená (v Digest/URL by rozbila zápis `user:heslo`).
- PrusaLink klient: `DigestAuthenticator` přestane používat konstantu `maker` a vezme jméno z konfigurace tiskárny. Jméno se nikdy neloguje spolu s heslem a heslo se neloguje vůbec.
- Okno nastavení (`PrinterEditorViewModel` + view): pole **Uživatel** u tiskáren PrusaLink, předvyplněné `maker`. U Prusa Connect se nezobrazuje nebo je neaktivní (stejně jako heslo). Upraví se nápověda u hesla (teď říká, že se automaticky použije „maker“).
- Tlačítko **Otestovat připojení** použije zadané jméno.
- UI texty v `Strings.resx` i `Strings.cs.resx`.

## Akceptační kritéria
- [x] Jméno jde v nastavení zadat, uložit a po restartu aplikace se načte. (ověřeno uživatelem 2026-10-18)
- [x] Starý soubor nastavení bez jména se načte s `maker` (test s fixture).
- [x] Klient PrusaLink posílá v digest hlavičce nastavené jméno (test s falešným `HttpMessageHandler`).
- [x] Neplatné jméno (prázdné, s dvojtečkou, s řídicími znaky, příliš dlouhé) okno nastavení odmítne se srozumitelnou chybou.
- [x] Test připojení v nastavení používá zadané jméno.
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné) – rozhodnuto 2026-10-18: jméno volitelné, výchozí `maker`, ukládá se v souboru nastavení.
- (core-dev, 2026-10-18) Validace kromě zadaných pravidel odmítá i `"`, `\` a znaky mimo ASCII. **Rozhodnuto 2026-10-18:** omezení zůstávají.
- (core-dev, 2026-10-18) Neplatné jméno v ručně upraveném souboru nastavení. **Rozhodnuto 2026-10-18:** nahradí se `maker` s varováním, tiskárna se načte.
- (core-dev, 2026-10-18) Text chyby „heslo odmítnuto“. **Rozhodnuto 2026-10-18:** okno nastavení hlásí, že tiskárna odmítla uživatelské jméno nebo heslo (`SettingsTestPasswordRejected` upraveno).
- (ui-dev, 2026-10-18) Popisek pole. **Rozhodnuto 2026-10-18:** „Uživatelské jméno“ / „User name“ (shodně s nápovědou a chybami).

## Poznámky k implementaci
Část Core + PrusaLink (core-dev):
- `PrinterDefinition.UserName` (výchozí `PrinterUserName.Default` = `maker`), při nastavení se ořízne a ověří; neplatná hodnota vyhodí `ArgumentException`, jejíž text hodnotu neopakuje.
- Sdílená pravidla v `Core/Printers/PrinterUserName.cs`: `Default`, `MaxLength` = 64 (po oříznutí), `Validate(string?)` vrací `PrinterUserNameError` (`None`, `Empty`, `TooLong`, `ContainsControlCharacter`, `ContainsColon`, `ContainsUnsupportedCharacter`), `IsValid(string?)`. Okno nastavení má podle `PrinterUserNameError` zobrazit srozumitelnou chybu, pravidla neduplikovat.
- `SettingsDocument.PrinterDocument.UserName` (JSON `userName`), zapisuje se vždy, i u Prusa Connect. Chybějící nebo `null` → `maker` bez varování; neplatná hodnota → `maker` s varováním. Formát souboru zůstává verze 1 (pole je volitelné).
- `DigestAuthenticator` dostává jméno v konstruktoru (`PrusaLinkClient` předává `printer.UserName`), konstanta `maker` odstraněna. Heslo ani jméno se nelogují.
- Testy: `PrinterUserNameTests`, rozšířené `PrinterDefinitionTests`, `SettingsMapperTests`, `JsonSettingsStoreTests` (fixture `tests/PrusaTrayDiagnostic.Core.Tests/Fixtures/settings-v1-without-username.json`), `PrusaLinkClientTests` (falešný server nově ověřuje i `username`).
- Projekt App se neměnil. Pozor: `PrinterEditorViewModel` vytváří `PrinterDefinition` přes `new` bez `UserName`, takže dokud ho ui-dev neupraví, uložení editované tiskárny vrátí jméno na `maker`.
- Žádný nový NuGet balíček; JSON dál jen přes zdrojově generovaný `SettingsJsonContext`.

Část okno nastavení (ui-dev):
- `PrinterEditorViewModel.UserName`: načte se z uložené definice, nová tiskárna má `maker`. `ToDefinition()` jméno předává (opravena chyba, kdy se uložením resetovalo na `maker`); ořízne ho `PrinterDefinition`, takže jméno lišící se jen mezerami nedělá okno „změněným“.
- Validace výhradně přes `PrinterUserName.Validate`; ve view modelu je jen převod `PrinterUserNameError` na text (`SettingsErrorUserName*`). Neplatné jméno blokuje uložení stejně jako jiné chyby polí (tiskárna se vybere, stavový řádek hlásí chybu) a test připojení se s ním vůbec nespustí (zobrazí se stejná chyba jako u pole).
- Prusa Connect: pole je neaktivní (`IsEnabled="{Binding IsPrusaLink}"`), nápověda říká, že Prusa Connect jméno nepoužívá, jméno se nevaliduje a ukládá se původní hodnota. Přepnutí na Prusa Connect vrátí pole na uložené jméno (podobně jako se zapomíná napsané heslo). Bez úložiště hesel (macOS do T-016) zůstává pole jména aktivní – jméno není tajný údaj.
- Test připojení posílá `PrinterDefinition` se zadaným jménem; `PrusaLinkConnectionTester` se neměnil (klient bere `printer.UserName`).
- Texty (EN / CS): popisek „User name“ / „Uživatelské jméno“, nápověda „PrusaLink user name from the printer settings. The default is “maker”.“ / „Uživatelské jméno PrusaLink z nastavení tiskárny. Výchozí je „maker“.“, chyby pro prázdné, příliš dlouhé (max. 64), řídicí znaky, dvojtečku a nepodporované znaky. Nápověda k heslu už nezmiňuje automatické `maker`. `SettingsTestPasswordRejected`: „The printer rejected the user name or password.“ / „Tiskárna odmítla uživatelské jméno nebo heslo.“
- Testy: `SettingsViewModelTests` (sekce „PrusaLink user name“: načtení, výchozí hodnota, zachování jména při uložení jiné změny, uložení oříznutého jména, nová tiskárna, dirty, každá hodnota `PrinterUserNameError`, max. délka, čeština, blokace uložení, Prusa Connect, přepnutí typu, bez úložiště hesel, tester dostane jméno, neplatné jméno se netestuje, editace maže starý výsledek testu), `PrusaLinkConnectionTesterTests` (falešná tiskárna nově ověřuje `username` v Digest; zadané jméno projde, špatné jméno = odmítnuto). Testy nepracují se soubory ani se správcem pověření.
- Ověřeno na Windows 11: `dotnet build` 0 varování, `dotnet test` 1587 testů (1 přeskočený, existující). Aplikace se spustí a běží (15 s, bez chyb). Okno nastavení jsem ručně neprocházel – zbývá ruční kontrola: zadat jméno, uložit, restartovat aplikaci a ověřit načtení (proto první kritérium zůstává nezaškrtnuté). Na macOS neověřeno (změna je jen ve sdíleném view modelu a XAML, bez platformního kódu).
