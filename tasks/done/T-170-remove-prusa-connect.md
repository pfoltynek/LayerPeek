---
id: T-170
title: Ukončení podpory Prusa Connect
status: done
owner: core-dev
priority: P2
platform: all
depends_on: []
---

> Rozhodnutí uživatele 2026-11-05: podpora Prusa Connect končí úplně (T-005 zamítnut). Uložená tiskárna typu Prusa Connect
> se při načtení vynechá s varováním. Pole „Připojení“ v nastavení tiskárny zmizí. README se upraví všude, kde Connect zmiňuje.

## Cíl
Aplikace podporuje jen PrusaLink. Zmizí nedokončená kostra Prusa Connect (projekt bez klienta, nevybíratelná volba
v nastavení, položka „Open Prusa Connect“ v menu tiskárny, text „Not available for Prusa Connect“ v diagnostice).

## Rozsah
- Smazat projekty `src/LayerPeek.PrusaConnect` a `tests/LayerPeek.PrusaConnect.Tests` (ze solution, z odkazů App a z kontroly
  architektury).
- Core: `ConnectionType.PrusaConnect` pryč; mapper nastavení tiskárnu s `"connection": "PrusaConnect"` vynechá s varováním.
  Vynechá ji i při uložení (soubor se přepíše bez ní).
- App: menu tiskárny jen „Open PrusaLink“; nastavení bez pole „Připojení“ (a bez textů o Connectu u jména a hesla);
  diagnostika, test připojení, továrna klientů a obrázek tiskárny bez větví pro Connect; texty `*PrusaConnect*` ze
  všech `Strings.*.resx`.
- Testy upravit, snímky UI sady Windows přegenerovat (záhlaví podzáložky Tiskárna bez pole „Připojení“; macOS sada →
  T-127).
- Dokumentace: `CLAUDE.md`, `.claude/agents/*.md`, `docs/architecture.md`, `README.md`, `README.cz.md`, `SECURITY.md`;
  `docs/api/prusa-connect.md` zůstane jako historie s poznámkou nahoře.

## Akceptační kritéria
- [x] V kódu (`src/`, `tests/`) nezůstal Prusa Connect kromě testu, že starou tiskárnu Connect mapper vynechá
- [x] Nastavení se starou tiskárnou Prusa Connect se načte, tiskárna chybí, v logu je varování
- [x] Menu tiskárny a okno nastavení Prusa Connect nenabízí
- [x] README (EN i CZ), CLAUDE.md a architektura neuvádějí Prusa Connect jako zdroj dat
- [x] Snímky UI sady Windows přegenerované a zkontrolované; macOS sada → přesunuto do T-127
- [x] `dotnet build` a `dotnet test` bez chyb a varování; Native AOT publish `win-x64` bez varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- **Core:** `ConnectionType` zůstal s jedinou hodnotou `PrusaLink`. Je na něm postavený model i formát `settings.json`
  (`connectionType`) a nechává místo pro případný jiný zdroj. `SettingsMapper` tiskárnu s `"PrusaConnect"` vynechá
  s varováním „Printer entry N skipped: Prusa Connect is no longer supported.“ (test
  `JsonSettingsStoreTests.LoadSkipsAPrusaConnectPrinterWithAWarning`). Data vynechané tiskárny přežijí zápis
  zjištěného modelu (`JsonSettingsStoreDetectedModelTests`) a zmizí až při uložení z okna nastavení.
- **Smazáno:** projekty `LayerPeek.PrusaConnect` a `LayerPeek.PrusaConnect.Tests` (jen kostra bez klienta),
  `PrinterWebAddress.PrusaConnectHome`, `ConnectionTypeOption` a výběr typu v nastavení, `IsPrusaLink`,
  `ConnectionTestOutcome.NotSupported`, stav diagnostiky „není k dispozici“ (`IPrinterInfoSource` vrací vždy výsledek)
  a 8 textů ve všech 8 jazycích (`MenuOpenPrusaConnect`, `DiagnosticsInfoNotSupported`, `SettingsConnectionType`,
  `SettingsConnectionPrusaLink`, `SettingsConnectionPrusaConnect`, `SettingsUserNameNotUsed`, `SettingsPasswordNotUsed`,
  `SettingsTestNotSupported`). Uživatelské jméno a heslo jsou vždy povolené (heslo jen podle `IsPasswordSupported`).
  Tip pod jménem se váže přímo na `SettingsUserNameHint`.
- Obranné větve pro „jiný typ připojení“ (`PrinterClientFactory`, `PrinterWebAddress`, `PrusaLinkWebGateway`,
  `PrinterImageChoice`) zůstaly. Jejich testy jsou smazané, protože `PrinterDefinition` neznámou hodnotu výčtu odmítne
  a situace tak nemůže nastat.
- **Testy** (agent qa-reviewer): testy čistě o Prusa Connect jsou smazané. Testy, které Connect používaly jen jako
  „další tiskárnu“, používají tiskárnu PrusaLink. Fixture `settings-v1-without-username.json` má místo XL Connect
  tiskárnu PrusaLink. Testy klávesových zkratek Cmd-. na Macu používají `SettingsLanguage` na záložce Obecné.
  `tools/macos-verify.ps1` (`mac.shortcuts`) je upravený stejně, ověření na Macu → T-127.
- Snímky UI sady Windows: 8 změněných `settings-*.png` (podzáložka Tiskárna bez pole „Připojení“), smazaný
  `settings-prusaconnect.png` (i v sadě macOS). Prohlédnuté `settings-prusalink`, `settings-errors-fr` a
  `settings-printer-images`: jméno vede přes celou šířku, nic se neořezává.
- `dotnet build` 0 varování, `dotnet test` 10 403 testů, 0 chyb; `dotnet publish -r win-x64` (Native AOT) 0 varování.
- Dokumentace: CLAUDE.md, agenti `architect` a `core-dev`, `docs/architecture.md` (diagram, oddíl Prusa Connect →
  „nepodporováno“), `docs/api/prusalink.md`, `docs/api/prusa-connect.md` (poznámka „Jen historie“), README (EN i CZ:
  FAQ a struktura projektu), CHANGELOG (Removed). Ochranná známka v README a SECURITY.md (mimo rozsah) zůstaly.
- Uzavřeno 2026-11-05: ověření sady snímků macOS a `mac.shortcuts` přesunuto do T-127 (rozhodnutí uživatele 2026-10-30).
