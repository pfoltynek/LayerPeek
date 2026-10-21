---
id: T-060
title: macOS – podpis Developer ID, notarizace a Homebrew tap
status: todo
owner: ui-dev
priority: P3
platform: macos
depends_on: [T-019]
---

## Cíl
Aplikaci pro macOS mohou instalovat i ostatní uživatelé bez ručního obcházení Gatekeeperu, včetně instalace přes Homebrew. Fáze 3 distribuce.

> **ODLOŽENO – neimplementovat**, dokud se uživatel nerozhodne zaplatit **Apple Developer Program (99 USD/rok)**. Platné rozhodnutí (2026-10-21) je bez placeného podpisu: macOS jen s ad-hoc podpisem (T-019). Při výběru dalšího úkolu přeskočit.

> **Pouze na macOS.**

## Proč
- S ad-hoc podpisem Gatekeeper staženou aplikaci zablokuje a uživatel ji musí povolit ručně (T-019); ad-hoc podpis se mění s každým sestavením, což může zhoršovat chování notifikací (T-020) a Keychainu (T-021).
- Homebrew 5.0.0 (12. 11. 2025) označil `--no-quarantine` za zastaralé a casky, které neprojdou kontrolou Gatekeeperu, v oficiálním `homebrew/cask` od 1. 9. 2026 vypíná (https://brew.sh/2025/11/12/homebrew-5.0.0/). Bez podpisu a notarizace tedy oficiální cask nejde a ani vlastní tap nedá uživateli bezproblémovou instalaci.

## Rozsah
- **Podpis:** certifikát *Developer ID Application*, hardened runtime, potřebné entitlements (síť, Keychain – T-021, notifikace – T-020); podpis celého `.app` včetně nativních knihoven FFmpeg (T-026) i `.dmg`.
- **Notarizace:** `xcrun notarytool submit --wait` a `xcrun stapler staple` pro `.app` i `.dmg` (https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution). Přihlašovací údaje (App Store Connect API klíč) jen v Keychainu vývojáře, nikdy v repozitáři ani v logu.
- Napojení na balicí kroky skriptů T-055 / T-019 na Macu (bez CI, T-016 odložený): podpis a notarizace nahradí ad-hoc podpis.
- Aktualizace přes Velopack na macOS, pokud to doporučí T-056 (Velopack na macOS vytváří `.pkg` a vyžaduje podpis a notarizaci).
- **Homebrew – nejdřív vlastní tap:** repozitář `github.com/pfoltynek/homebrew-tap` (konvence `homebrew-*`), cask `prusa-tray-diagnostic` odkazující na `.dmg` z GitHub Releases (vyžaduje veřejný repozitář), `sha256`, `livecheck`, `zap` (úklid nastavení); `brew audit --cask` a `brew style` bez chyb. Aktualizace casku ručně / krokem skriptu vydání.
- **Později oficiální `homebrew/cask`** – až projekt splní podmínky notability.
- Dokumentace: `docs/release.md`, `docs/prerequisites.md` (Developer ID, `notarytool`).

## Akceptační kritéria
- [ ] `codesign --verify --deep --strict`, `spctl --assess --type execute` a `xcrun stapler validate` procházejí pro `.app` i `.dmg`
- [ ] Na čistém Macu se aplikace stažená z GitHub Release spustí bez varování Gatekeeperu (ověřeno)
- [ ] `brew install --cask pfoltynek/tap/prusa-tray-diagnostic` nainstaluje aplikaci bez varování; `brew uninstall --zap` odstraní aplikaci i nastavení (ověřeno)
- [ ] Žádné tajné údaje v repozitáři ani ve výstupu skriptů
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- **Zaplatit Apple Developer Program (99 USD/rok)? Kdy?** Bez něj úkol zůstává odložený a macOS jen pro vývojáře a testery (T-019).
- Kdy podat žádost do oficiálního `homebrew/cask`?
- Má `zap` mazat i uložená hesla v Keychainu (T-021)?
- Pokud T-056 doporučí Velopack i pro macOS: nahradit `.dmg` balíčkem `.pkg` z Velopacku, nebo vydávat obojí?

## Poznámky k implementaci
(vyplní agent po dokončení)
