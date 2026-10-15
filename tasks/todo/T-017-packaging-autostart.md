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
- Windows: self-contained publish / instalátor
- macOS: `.app` bundle s `LSUIElement` (bez ikony v Docku), Apple Silicon + Intel
- Autostart po přihlášení (Windows registry / macOS Login Items)

## Akceptační kritéria
- [ ] Postup sestavení zdokumentovaný v `docs/`
- [ ] Release workflow v CI
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Formát pro Windows: MSIX, instalátor (Inno Setup/WiX), nebo jen zip?
- Podepisování kódu (Windows certifikát, Apple Developer ID + notarizace)?
- Automatické aktualizace (např. Velopack)?

## Poznámky k implementaci
