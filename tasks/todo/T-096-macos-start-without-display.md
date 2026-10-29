---
id: T-096
title: macOS – start aplikace bez aktivního displeje
status: todo
owner: ui-dev
priority: P2
platform: macos
depends_on: [T-019]
---

## Cíl
Aplikace na macOS nesmí spadnout, když se spouští ve chvíli, kdy není aktivní žádný displej. Při ověřování T-019 (2026-10-29) Release exe při startu skončil chybou `Avalonia.Native was not able to start the RenderTimer. Native error code is: -6661` (`kCVReturnInvalidDisplay`), protože displej byl uspaný. Smoke testy proto běží pod `caffeinate -d -u`.

Kdy to hrozí: „Otevřít při přihlášení“ (T-019) na Macu se zavřeným víkem bez monitoru, přihlášení přes Sdílení obrazovky, start během spánku displeje.

## Rozsah
- Reprodukovat (displej uspaný → start Release `LayerPeek.app`) a zjistit, kde přesně Avalonia.Native selže (`CVDisplayLink` při inicializaci platformy).
- Ošetřit v aplikaci, např. počkat na dostupný displej a start zopakovat, nebo odložit inicializaci renderování; pád bez hlášení není přijatelný.
- Pokud jde o chybu Avalonie, připravit hlášení do Avalonie (minimální reprodukce) a do té doby obejít v aplikaci.
- Záznam do `docs/troubleshooting.md`.
- Izolace testů podle `docs/architecture.md` („Izolace testů na macOS“); start Release aplikace jen přes `scripts/aot-smoke.ps1` se zálohou a obnovou profilu.

## Akceptační kritéria
- [ ] Start s uspaným displejem nezpůsobí pád; aplikace se po probuzení displeje objeví v menu baru (ověřeno)
- [ ] Smoke test (`aot-smoke.ps1`) projde i bez `caffeinate`, nebo je důvod zdokumentovaný
- [ ] Windows beze změny chování
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné; vzniklo rozhodnutím 2026-10-29 k otázce 8 v T-019)

## Poznámky k implementaci
(vyplní agent po dokončení)
