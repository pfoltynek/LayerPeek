---
id: T-021
title: macOS – úložiště tajných údajů v Keychainu
status: todo
owner: core-dev
priority: P1
platform: macos
depends_on: [T-002, T-030]
---

## Cíl
Implementace `ISecretStore` nad macOS Keychain, aby se hesla / API klíče / tokeny tiskáren na macOS ukládaly stejně bezpečně jako na Windows (T-002 – `WindowsCredentialSecretStore`).

> **Pouze na macOS:** úkol implementuje a ověřuje uživatel později na Macu. Neblokuje žádný Windows úkol.

## Rozsah
- Nová třída v `src/LayerPeek.App/Platform/MacOS/` (např. `KeychainSecretStore`), `[SupportedOSPlatform("macos")]`, `internal sealed`
- Nativní interop jen přes `[LibraryImport]` do `Security.framework` a `CoreFoundation.framework` – **bez knihoven třetích stran**, kompatibilní s Native AOT (žádná reflexe, žádné `BuiltInComInteropSupport`)
- Doporučené API: `SecItemAdd` / `SecItemCopyMatching` / `SecItemUpdate` / `SecItemDelete` s `kSecClassGenericPassword` (service = prefix aplikace, account = klíč); zastaralé `SecKeychain*` API nepoužívat, pokud k tomu nebude důvod uvedený v poznámkách
- Kontrakt zrcadlí Windows implementaci a `ISecretStore` v Core:
  - tajný údaj jako **striktní UTF-8** – neplatný Unicode při zápisu → `ArgumentException` (bez vložení obsahu do zprávy), neplatné UTF-8 při čtení → `SecretStoreException`; nic se tiše nemění
  - chyby argumentů se vyhazují hned; zrušení (`Task.FromCanceled`) a chyby OS (`SecretStoreException` s kódem `OSStatus`, nikdy s tajným údajem) se vracejí přes vrácený `Task`
  - chybějící položka (`errSecItemNotFound`): `GetAsync` → `null`, `DeleteAsync` → `false`
  - `SetAsync` vytvoří nebo přepíše existující položku
  - **klíče nerozlišují velikost písmen** (kontrakt `ISecretStore`; Keychain atributy velikost písmen rozlišují, takže je nutná jednotná normalizace klíče)
  - buffery s otevřeným textem se po použití nulují, všechny CF objekty se uvolní (`CFRelease`)
- Registrace v composition rootu: pokud už existuje výběr implementace podle OS (T-015), doplnit větev pro macOS; jinak ji doplní T-015
- Integrační testy v `tests/LayerPeek.App.Tests/Platform/MacOS/` nad skutečným Keychainem aktuálního uživatele, na jiném OS přeskočené (`Assert.SkipUnless(OperatingSystem.IsMacOS(), …)`)

## Akceptační kritéria
- [ ] Round-trip včetně prázdného řetězce, diakritiky, emoji a znaku NUL (stejné případy jako Windows testy)
- [ ] Přepis existující položky, `DeleteAsync` vrací `true`/`false` podle existence, chybějící klíč vrací `null`
- [ ] Klíče lišící se jen velikostí písmen adresují stejný tajný údaj (test)
- [ ] Izolace podle prefixu (dvě instance s různým prefixem se neovlivňují)
- [ ] Neplatný Unicode při zápisu → `ArgumentException`; zrušený token → zrušený `Task`; argumenty `null`/prázdné → okamžitá výjimka
- [ ] Testy po sobě uklidí vše, co vytvořily, **i po spadlém předchozím běhu**: testovací položky mají vyhrazený testovací prefix (nikdy produkční `LayerPeek`) a před během testů se odstraní všechny pozůstatky s tímto prefixem; úklid je sám ověřen testem
- [ ] Zprávy výjimek ani logy neobsahují tajný údaj
- [ ] Lokální Native AOT publish `osx-arm64` bez trim/AOT varování z kódu projektu
- [ ] Windows build a testy beze změny chování (`WindowsCredentialSecretStore` se nemění)
- [ ] Otevření webu PrusaLink přes lokální proxy (T-028) funguje na macOS: E2E testy z T-030 (`tests/LayerPeek.E2E.Tests`) na macOS s nainstalovaným Edge projdou, bez Edge se korektně přeskočí a nic po sobě nenechají. `OwnProcesses.UnixCommandLine` zrychlit na jedno volání `ps -axww -o pid=,command=` (rozhodnuto uživatelem 2026-10-18).
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Synchronizovat tajné údaje přes iCloud Keychain (`kSecAttrSynchronizable`), nebo jen lokálně? (Na Windows bylo rozhodnuto, že hesla roamují s profilem.)
- Jakou dostupnost položky zvolit (`kSecAttrAccessible`, např. jen po odemčení zařízení)?
- Použít stejný limit velikosti tajného údaje jako Windows (2560 B UTF-8) kvůli jednotnému chování, nebo limit Keychainu?
- Podpis aplikace (T-019) ovlivňuje, zda macOS po novém sestavení zobrazí dialog „povolit přístup do Keychainu“ – je to pro vývojová/ad-hoc sestavení přijatelné? Rozhodnuto 2026-10-21: i vydání budou mít jen ad-hoc podpis (bez Apple Developer ID), takže se dialog může objevit po každé aktualizaci, nejen ve vývoji. Riziko ověřit v tomto úkolu.

## Poznámky k implementaci
