---
id: T-019
title: macOS – .app bundle, distribuce a autostart
status: todo
owner: ui-dev
priority: P1
platform: macos
depends_on: [T-008, T-015]
---

## Cíl
Spustitelná a distribuovatelná `.app` pro macOS bez ikony v Docku, spouštění po přihlášení.

Priorita P1, protože `.app` bundle s bundle identifikátorem je předpokladem pro macOS notifikace (T-020).

> **Pouze na macOS:** úkol implementuje a ověřuje uživatel později na Macu (Native AOT pro macOS lze sestavit jen na macOS, vyžaduje Xcode Command Line Tools – viz `docs/prerequisites.md`). Neblokuje žádný Windows úkol.

## Rozsah
- Native AOT publish pro `osx-arm64` (a případně `osx-x64`), spojení do universal binary (`lipo`)
- `.app` bundle s `Info.plist` (bundle identifikátor, `LSUIElement` – bez ikony v Docku)
- Pomocná okna otevřená z menu se u aplikace s `LSUIElement` musí dostat do popředí
- Autostart po přihlášení (Login Items)

## Akceptační kritéria
- [ ] Postup sestavení `.app` lokálně na macOS zdokumentovaný v `docs/` (CI odloženo, viz T-016)
- [ ] Aplikace běží bez ikony v Docku, ikona je jen v menu baru
- [ ] Okno Nastavení (T-015) otevřené z menu se zobrazí v popředí a dostane fokus
- [ ] Autostart po přihlášení funguje na macOS (ověřeno)
- [ ] `docs/prerequisites.md` aktuální, pokud přibyly nástroje
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Podepisování a notarizace (Apple Developer ID), nebo jen ad-hoc podpis pro vlastní potřebu? Ovlivňuje i T-020 (notifikace) a T-021 (přístup do Keychainu).
- Podporovat i Intel Macy (`osx-x64`), nebo jen Apple Silicon?
- Formát distribuce: `.dmg`, `.zip`, nebo jiný?
- Automatické aktualizace – viz rozhodnutí v T-017.

## Poznámky k implementaci
