---
id: T-017
title: Balení a distribuce, autostart
status: todo
owner: ui-dev
priority: P2
depends_on: [T-008]
---

## Cíl
Instalovatelná aplikace pro Windows a macOS, spouštění po přihlášení.

## Rozsah
- Windows: Native AOT publish (`win-x64`, případně `win-arm64`) + instalátor
- macOS: Native AOT pro `osx-arm64` a `osx-x64` (sestavení jen na macOS), spojení do universal binary (`lipo`), `.app` bundle s `LSUIElement` (bez ikony v Docku)
- Autostart po přihlášení (Windows registry / macOS Login Items)

## Akceptační kritéria
- [ ] Postup sestavení zdokumentovaný v `docs/`
- [ ] Release workflow v CI
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Formát pro Windows: MSIX, instalátor (Inno Setup/WiX), nebo jen zip?
- Podepisování kódu (Windows certifikát, Apple Developer ID + notarizace)?
- Automatické aktualizace (např. Velopack – ověřit kompatibilitu s Native AOT)?
- Podporovat i Intel Macy (`osx-x64`), nebo jen Apple Silicon?

## Poznámky k implementaci
