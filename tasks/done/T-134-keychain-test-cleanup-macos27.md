---
id: T-134
title: Úklid testů Klíčenky na macOS 27 smaže jen jednu položku služby
status: done
owner: qa-reviewer
priority: P2
platform: macos
depends_on: [T-021]
---

## Cíl
Na macOS 27.2 selhával `mac.keychain` (T-115, 2026-11-01) – vždy jeden test z 25:
`KeychainSecretStoreTests.TestCleanupRemovesEverythingUnderPrefix` („Collection: [(LayerPeek.Tests/…, NESTED/B)]“).
`TestKeychain.DeleteAll` mazal `SecItemDelete` jen podle služby s předpokladem, že bez limitu smaže všechny položky
služby. Na macOS 27.2 smaže jen jednu (ověřeno samostatným programem ve Swiftu: dvě položky, první volání smaže jednu,
druhé druhou, třetí -25300).

## Rozsah
- Jen pomocník testů `tests/LayerPeek.App.Tests/Platform/MacOS/TestKeychain.cs`: mazat každou nalezenou položku podle
  služby a účtu.
- Kód aplikace se netýká: `NativeKeychainItems.Delete` maže vždy jednu položku (služba + účet).

## Akceptační kritéria
- [x] `KeychainSecretStoreTests` na macOS 27.2 s odemčenou Klíčenkou: 25 / 25, 0 přeskočeno, opakovaně
- [x] Po bězích nezůstane v Klíčence žádná položka `LayerPeek*`
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
(žádné)

## Poznámky k implementaci
- 2026-11-01 (macOS 27.2): `DeleteAll` maže po dvojicích služba + účet. `KeychainSecretStoreTests` 3× za sebou 25 / 25,
  0 přeskočeno; výpis Klíčenky (SecItemCopyMatching, všechny generic passwords) bez položky `LayerPeek*`.
