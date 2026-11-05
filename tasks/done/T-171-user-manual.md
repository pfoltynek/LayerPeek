---
id: T-171
title: Uživatelská příručka ve všech jazycích aplikace
status: done
owner: architect
priority: P2
platform: all
depends_on: []
---

> Rozhodnutí uživatele 2026-11-05: příručka v adresáři `manuals/`, jeden soubor na jazyk pro Windows i macOS zároveň
> (rozdíly v odstavcích „Windows“ / „macOS“), snímky obrazovky generované aplikací pro všech 8 jazyků, odkaz z README.
> Odkaz z aplikace je samostatný úkol T-172.

## Cíl
Uživatel najde v repozitáři úplný návod k aplikaci ve svém jazyce: instalace, první tiskárna, menu ikony, okno kamery,
widgety, oznámení, nahrávání, detekce chyb tisku, diagnostika, nastavení, aktualizace, data, řešení potíží, odinstalace.

## Rozsah
- `manuals/en/manual.md` (zdroj) a překlady `cs`, `sk`, `de`, `es`, `fr`, `it`, `pl`; popisky UI přesně podle
  `Strings.<jazyk>.resx`.
- `manuals/README.md`: rozcestník jazyků.
- Generátor snímků (`tests/LayerPeek.App.UiTests/Readme`) pro všech 8 jazyků do `docs/images/<jazyk>/`.
- Odkazy v `README.md` a `README.cz.md`, pravidlo jazyka v `CLAUDE.md`, `docs/readme-screenshots.md`, `CHANGELOG.md`.

## Akceptační kritéria
- [x] Příručka ve všech 8 jazycích, pokrývá Windows i macOS
- [x] Popisky UI odpovídají textům aplikace v daném jazyce
- [x] Snímky ve všech jazycích vygenerované a zkontrolované
- [x] Odkazy uvnitř příručky (obsah, kotvy) a na obrázky fungují
- [x] README (EN i CZ) odkazuje na příručku
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- Zdroj je `manuals/en/manual.md`; překlady (cs, sk, de, es, fr, it, pl) mají popisky UI dohledané podle klíče
  v `Strings.<jazyk>.resx`. Termíny systému (Gatekeeper, SmartScreen, Nastavení systému) a menu firmwaru tiskárny
  jsou nejlepší odhad, kde si překlad nebyl jistý, je u nich jednou anglický originál v závorce.
- Kotvy a obrázky všech příruček ověřené skriptem (GitHub slug); snímky `docs/images/<jazyk>/` vygenerované na macOS
  (okno nastavení s texty pro macOS), en/cs se přitom obnovily po T-170.
- Snímky odhalily oříznutý text „dashcam“ na podzáložce Kamera v pl/fr/es → T-173.
- Při změně viditelné uživateli je potřeba upravit všech 8 příruček (pravidlo v `CLAUDE.md`).
