---
id: T-059
title: Publikace do winget
status: todo
owner: qa-reviewer
priority: P3
platform: windows
depends_on: [T-017, T-055]
---

## Cíl
Aplikaci jde nainstalovat a aktualizovat příkazem `winget install` z oficiálního repozitáře manifestů `microsoft/winget-pkgs` (https://github.com/microsoft/winget-pkgs). Fáze 2 distribuce (veřejné vydání pro Windows).

> **Předpoklad:** veřejný repozitář a veřejně stažitelné soubory vydání. Validace winget-pkgs stahuje instalátor z URL v manifestu, takže dokud je repozitář soukromý (rozhodnutí uživatele 2026-10-21: bude později veřejný), úkol nejde dokončit.

## Rozsah
- Identifikátor balíčku (např. `pfoltynek.LayerPeek`) a první manifest vytvořený nástrojem `wingetcreate` (https://github.com/microsoft/winget-create) z instalátoru na GitHub Releases (T-017).
- Typ instalátoru a přepínač tiché instalace podle instalátoru z T-017 / doporučení T-056 (Velopack `Setup.exe` → typ `exe` s tichým přepínačem); `Scope: user`; architektury `x64` a `arm64` (arm64 až po T-057, viz T-055).
- Licence v manifestu: `GPL-3.0-only`, odkaz na `LICENSE`.
- **Bez CI** (T-016 odložený): aktualizace manifestu ručně z lokálního postupu vydání – po zveřejnění vydání skriptem T-055 spustit `wingetcreate update <id> --version X.Y.Z --urls <url…> --submit`. Postup (případně volitelný krok skriptu T-055) zapsat do `docs/release.md`. Token pro `wingetcreate` jen lokálně (např. `gh auth token` / proměnná prostředí), nikdy v repozitáři ani v logu.
- Ověření manifestu `winget validate` a lokální instalace `winget install --manifest <složka>` před odesláním.
- **Nepodepsaný instalátor:** podle rozhodnutí uživatele (žádný placený podpis) se odesílá nepodepsaný. Riziko: automatická validace winget-pkgs kontroluje instalátor Microsoft Defenderem / SmartScreen a nepodepsaný, málo stahovaný instalátor může být označen nebo PR zdržen ruční kontrolou. Výsledek první validace zapsat do poznámek.
- Souhra s aktualizacemi Velopacku (aplikace aktualizovaná Velopackem vs. `winget upgrade`) – zdokumentovat.
- Předběžná vydání `0.x` (T-055): ověřit, jak s nimi winget zachází (winget-pkgs předběžné verze přijímá jen výjimečně) – viz otevřené otázky.

## Akceptační kritéria
- [ ] První manifest přijat do `microsoft/winget-pkgs`
- [ ] `winget install <id>` nainstaluje aplikaci bez dotazů, `winget uninstall <id>` ji beze zbytku odinstaluje (ověřeno)
- [ ] Postup aktualizace manifestu při dalším vydání zdokumentovaný v `docs/release.md` a jednou vyzkoušený
- [ ] Žádný token v repozitáři ani ve výstupu skriptů

## Otevřené otázky
- Kdy začít: až po zveřejnění repozitáře a první stabilní verzi `1.0.0`, nebo už s předběžnými `0.x`?
- Identifikátor balíčku a jméno vydavatele (`Publisher`) v manifestu?
- Má být odeslání do winget volitelným krokem skriptu vydání (T-055), nebo samostatným ručním příkazem?

## Poznámky k implementaci
(vyplní agent po dokončení)
