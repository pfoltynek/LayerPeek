---
id: T-172
title: Položka „Příručka“ v menu ikony
status: done
owner: ui-dev
priority: P2
platform: all
depends_on: [T-171]
---

> Rozhodnutí uživatele 2026-11-05: z aplikace se dá otevřít uživatelská příručka (T-171) v jazyce UI.

## Cíl
Uživatel otevře příručku přímo z aplikace, v jazyce, ve kterém má aplikaci.

## Rozsah
- Nová položka menu ikony (Windows tray / macOS menu bar) těsně nad „About“: otevře v prohlížeči
  `https://github.com/pfoltynek/LayerPeek/blob/main/manuals/<jazyk>/manual.md`, kde `<jazyk>` je aktuální jazyk textů
  aplikace (jeden z `AppLanguages.Supported`; jinak `en`).
- Odkaz na `main`: příručka v `main` odpovídá vydané verzi.
- Nový řetězec ve všech `Strings.*.resx`; testy (view model menu, výběr URL podle jazyka).

## Akceptační kritéria
- [x] Položka je v menu na Windows i macOS a otevře příručku v jazyce UI (systémový jazyk → jeho příručka, nepodporovaný → en)
- [x] Řetězec přeložený do všech 8 jazyků
- [x] Testy menu a URL
- [ ] Příručka (`manuals/*/manual.md`, kapitola o menu) položku zmiňuje → po sloučení T-171
- [x] `dotnet build` a `dotnet test` bez chyb a varování (macOS arm64: build 0 varování, test 9690 úspěšných, 0 neúspěšných, 212 přeskočených)

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
Implementováno 2026-11-05 na macOS arm64 (ui-dev), větev `feature/T-172-manual-menu-item`.

- **Menu:** nová pevná položka `TrayMenuCommand.Manual` těsně nad „O aplikaci“ (`TrayMenuViewModel.CreateFixedItems`);
  položka nabídky aktualizace (T-058) zůstává na indexu 3, tedy nad příručkou. Společné pro Windows i macOS: obě
  platformy staví menu ze stejných `FixedItems` přes `TrayMenuTarget` / `AvaloniaNativeTrayMenu`, žádný kód pro jednu
  platformu. Bez tipu (ostatní pevné položky ho nemají).
- **Otevření:** `TrayMenuPresenter` → `ITrayAppCommands.OpenManual()` → `App` volá existující `IUrlLauncher.OpenAsync`
  (stejný jako odkaz na GitHub v okně O aplikaci); žádný nový kód spouštění procesu. Během ukončování nic.
- **Adresa:** `Tray/Menu/UserManual.UriFor(UiLanguage.CurrentTextCulture())` →
  `https://github.com/pfoltynek/LayerPeek/blob/main/manuals/<jazyk>/manual.md`. Jazyk je dvoupísmenný kód kultury textů
  (zvolený jazyk, jinak systémový včetně regionálních variant `de-AT` → `de`) normalizovaný přes
  `AppLanguages.TryNormalize`; cokoli jiného → `en`.
- **Řetězec `MenuManual`** (komentář s T-172 v neutrálním `Strings.resx`, překlady bez komentáře jako ostatní klíče);
  přístupové klávesy se v hlavním menu nekříží v žádném jazyce (kontroluje `TrayMenuAccessKeyTests`):
  en „_User manual“, cs „_Uživatelská příručka“, sk „Používateľská p_ríručka“ (P má „Pridať tlačiareň…“),
  de „Benutzer_handbuch“ (B má „Beenden“), es „_Manual de usuario“, fr „_Manuel d'utilisation“, it „_Manuale utente“,
  pl „_Podręcznik użytkownika“.
- **Testy:** `UserManualTests` (všech 8 jazyků, regionální varianty, nepodporovaný jazyk a invariantní kultura → en;
  zvolený jazyk vs. systémový přes `UiLanguage` v kolekci `GlobalLanguageGroup`), view model (pořadí, text, přístupové
  klávesy, s nabídkou aktualizace), presenter (klik volá `OpenManual`), headless UI testy menu a klávesnice.
  Windows E2E testy menu (UIA, `LayerPeek.App.E2E.Tests`) mají položku „User manual“ / klávesu U doplněnou, ale na macOS
  se nespouštějí → ověření na Windows v T-125. Snímky UI menu nepokrývají (žádná regenerace).
