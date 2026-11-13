---
id: T-209
title: macOS ověření – ovládání menu v menu baru přes Accessibility (`menu` / `menupress`) a kontrola `mac.menu-items`
status: todo
owner: qa-reviewer
priority: P2
platform: macos
depends_on: [T-208]
---

## Cíl
Většina zbylých ručních bodů T-127 začíná klikem do menu LayerPeeku v menu baru, např. „Nahrávat“, „Uložit
posledních 2 min“, „Spustit timelapse“, „Exportovat timelapse do JPEG…“ nebo „Přenést kameru do popředí“. Helper
`tools/macos-verify-helper.swift` dnes umí přečíst stavovou položku (`statusitems`, AXExtrasMenuBar), ale neumí otevřít
její menu ani v něm vybrat položku. Tento úkol přidá ovládání menu. Je to předpoklad T-210, T-211, T-212, T-213,
T-215 a T-216. Zároveň automatizuje ruční část `mac.menu` (menu sleduje stavy tiskárny).

## Rozsah
- **Helper, nové příkazy** (Accessibility, bez oprávnění exit 3 jako dnes):
  - `menu <pid>` – otevře menu stavové položky procesu (AXPress na položce v AXExtrasMenuBar) a vypíše ho jako JSON:
    pro každou položku titulek, `enabled`, `checked` (AXMenuItemMarkChar), zkratku, `help` (AXHelp, je-li – tip
    zakázané položky), oddělovač a rekurzivně podmenu. Menu potom zavře (Esc). Podmenu otevírá jen pro čtení
    (AXPress / AXShowMenu na položce s podmenu), nic nevybere.
  - `menupress <pid> <cesta>` – otevře menu a projde cestu titulků oddělenou `>` (např. `Tiskárna 1>Nahrávat`), na
    poslední položce provede AXPress. Neznámá nebo zakázaná položka = exit 1 s výpisem dostupných titulků na dané úrovni.
    Menu po neúspěchu vždy zavře.
  - Shoda titulků přesná (Ordinal). Kontroly berou očekávané texty ze `Strings.resx` (testovací bundle běží v jazyce,
    který kontrola nastaví), ne z natvrdo zapsané češtiny.
- **Kontrola `mac.menu-items`** (Permission: Zpřístupnění, v presetu `release` z T-208): testovací bundle se simulátorem
  (`--speed 20`). Scénáře `idle`, `printing`, `paused`, `finished`, `error`, offline (`POST /_simulator/…`). U každého
  ověří strukturu menu (`menu`): název a stav tiskárny v položce, podmenu tiskárny z T-042, položky kamery a nahrávání
  podle stavu, „Ukončit“ na konci. Text tooltipu ikony (`statusitems`, AXHelp) musí odpovídat stavu. Nakonec
  `menupress … Ukončit` ukončí aplikaci a stavová položka zmizí. Zjištěné AXHelp zakázaných položek (tip v NSMenu) zapsat
  do zprávy jako důkaz: odpovídá na otázku „ukáže se tip?“ z bodů T-146 / T-148 / T-161 v T-127.
- Žádná změna produkčního kódu. Kdyby Avalonia `NativeMenu` některou vlastnost do AX nepropsala (např. AXHelp),
  zapsat to do poznámek a otázek (žádné obcházení v aplikaci bez rozhodnutí).
- Výkon a robustnost: každé otevření menu má časový limit (např. 2 s na úroveň). Helper nikdy nenechá otevřené menu, protože
  blokuje vstup. Úklid stačí přes Esc a kontrolu, že žádné okno vrstvy menu nezůstalo.

## Akceptační kritéria
- [ ] `helper menu <pid>` vrátí JSON celého menu testovacího bundlu včetně podmenu a `enabled` / `checked` / `help`;
      menu po skončení není otevřené
- [ ] `helper menupress <pid> "<tiskárna>>Přenést kameru do popředí"` otevře okno kamery; neznámá cesta = exit 1
      s výpisem titulků, menu zavřené
- [ ] `mac.menu-items` = `PASS` pro všech šest stavů a „Ukončit“ z menu (uzavře v T-127 oddíl 1b: **T-018** „Menu z T-008 …
      aktualizuje“, ruční část `mac.menu` mimo vzhled ikon)
- [ ] Zjištění AXHelp u zakázaných položek zapsané v poznámkách (vstup pro T-210 / T-211)
- [ ] Pester / `-SelfTest` a `dotnet build` / `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
