---
id: T-002
title: Konfigurace tiskáren a uložení tajných údajů
status: done
owner: core-dev
priority: P1
platform: all
depends_on: [T-001]
---

## Cíl
Uložit seznam tiskáren a nastavení aplikace mezi spuštěními.

## Rozsah
- Model nastavení (tiskárny, interval pollingu, povolené notifikace)
- Uložení JSON do `%APPDATA%/PrusaTrayDiagnostic` (Windows) a `~/Library/Application Support/PrusaTrayDiagnostic` (macOS)
- Abstrakce `ISecretStore` pro hesla / API klíče / tokeny
- Serializace přes source-generated `JsonSerializerContext` (Native AOT)

## Akceptační kritéria
- [x] Načtení/uložení nastavení s testy (vč. chybějícího a poškozeného souboru)
- [x] Tajné údaje nejsou v JSON v čitelné podobě
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- ~~Jak bezpečně ukládat tajné údaje?~~ → **Rozhodnuto:** nativní úložiště OS, bez knihoven třetích stran. Windows Credential Manager teď, macOS Keychain později jako samostatný úkol.
- ~~Výchozí interval pollingu?~~ → **Rozhodnuto:** 5 s, uživatel ho může změnit.
- ~~Poškozený soubor nastavení?~~ → **Rozhodnuto:** použít výchozí nastavení a chybu ohlásit do UI. Soubor se nezálohuje ani nepřejmenovává.
- ~~Roaming?~~ → **Rozhodnuto:** hesla roamují s profilem stejně jako nastavení v `%APPDATA%` (`CRED_PERSIST_ENTERPRISE`).
- ~~Tolerance ručních úprav JSON?~~ → **Rozhodnuto:** čísla se přijímají i v uvozovkách. Jiná typová chyba znamená poškozený soubor.

## Poznámky k implementaci
- **Core (`PrusaTrayDiagnostic.Core.Settings`):**
  - `AppSettings`, `NotificationSettings`, `ISettingsStore`, `JsonSettingsStore`, `SettingsLoadResult`/`SettingsLoadStatus`, `AppDataPaths`.
  - Na macOS se cesta skládá explicitně, protože `SpecialFolder.ApplicationData` tam vede do `~/.config`.
- **Validace v `AppSettings`:**
  - Interval pollingu musí být v rozsahu 1 s – 5 min (rozhodnutí uživatele).
  - Id tiskáren musí být unikátní bez ohledu na velikost písmen, protože Credential Manager velikost písmen nerozlišuje.
  - Seznam tiskáren je read-only kopie. Rovnost `AppSettings` je hodnotová včetně seznamu tiskáren.
- **Perzistence:**
  - Interní DTO (`Settings/Storage/SettingsDocument`) je oddělené od doménového modelu a má vlastní mapper.
  - Načítání je tolerantní:
    - komentáře a čárky na konci jsou povolené;
    - neznámé vlastnosti se ignorují;
    - chybějící hodnoty se nahradí výchozími;
    - neplatné nebo duplicitní tiskárny se přeskočí s varováním v `SettingsLoadResult.Warnings`;
    - interval mimo rozsah se ořízne do povolených mezí, také s varováním;
    - novější verze formátu vyvolá varování.
  - Soubor větší než 1 MB se považuje za poškozený.
  - Chyby čtení (soubor zamčený, odepřený přístup) vrací stav `Unreadable` s výchozím nastavením. `LoadAsync` nikdy nevyhodí výjimku kvůli souboru.
  - Ukládání je atomické: zápis do dočasného souboru, `fsync` a pak `File.Move(overwrite)`. Při chybě nebo zrušení zůstane původní soubor beze změny.
  - Operace jsou serializované přes `SemaphoreSlim`.
- **Tajné údaje (`PrusaTrayDiagnostic.Core.Secrets`):**
  - `ISecretStore`, `SecretKeys.ForPrinter(id)`, `SecretStoreException`. Model nastavení žádné tajné údaje neobsahuje (ověřuje test).
- **Windows (`App/Platform/Windows`):**
  - `WindowsCredentialSecretStore` používá generic credentials přes `[LibraryImport]` (`CredWriteW/CredReadW/CredDeleteW/CredFree`). V App csproj je kvůli tomu `AllowUnsafeBlocks`.
  - Tajné údaje se ukládají jako striktní UTF-8 (max. 2560 B). Neplatný Unicode se odmítne, nic se tiše nemění.
  - Buffery s otevřeným textem se po použití nulují.
  - Chyby argumentů se vyhazují hned. Zrušení a chyby OS se vracejí přes vrácený `Task`.
  - macOS implementace zatím chybí a DI registrace (composition root) se doplní s prvním konzumentem (T-015).
- **Testy:**
  - Nový projekt `tests/PrusaTrayDiagnostic.App.Tests`.
  - Testy Credential Manageru jsou integrační nad skutečným úložištěm aktuálního uživatele (bez sítě). Každá instance testu používá unikátní prefix `PrusaTrayDiagnostic.Tests/<guid>` a na konci smaže vše pod ním. Assembly fixture (`CredentialCleanupFixture`) navíc na začátku i na konci běhu smaže vše pod `PrusaTrayDiagnostic.Tests/*`. Pozůstatky po spadlém běhu tak zmizí nejpozději při dalším spuštění testů. Stejně funguje úklid dočasných adresářů v Core testech (`TestDirectories`, kořen `%TEMP%/PrusaTrayDiagnostic.Core.Tests`). Souběžné běhy téže testovací sady se serializují zámkovým souborem (`tests/Shared/TestRunLock.cs`), aby úklid jednoho běhu nemazal data jiného. Testovací credentials se zapisují s `CRED_PERSIST_SESSION`, takže nikdy nejsou na disku ani neroamují. Úklid odmítne jakýkoli prefix mimo `PrusaTrayDiagnostic.Tests`, aby nikdy nesmazal skutečné údaje. Testy nesmí po sobě nic zanechat.
- Názvy testů jsou PascalCase bez podtržítek, CA1707 platí i pro testy.
- Code review proběhlo v 5 kolech, poslední bez nálezů. Lokální Native AOT publish (win-x64) prošel bez varování z kódu projektu.
- Žádné nové NuGet balíčky.
