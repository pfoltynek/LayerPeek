---
id: T-209
title: macOS ověření – ovládání menu v menu baru přes Accessibility (`menu` / `menupress`) a kontrola `mac.menu-items`
status: done
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
- [x] `helper menu <pid>` vrátí JSON celého menu testovacího bundlu včetně podmenu a `enabled` / `checked` / `help`;
      menu po skončení není otevřené → přesunuto do T-127
- [x] `helper menupress <pid> "<tiskárna>>Přenést kameru do popředí"` otevře okno kamery; neznámá cesta = exit 1
      s výpisem titulků, menu zavřené → přesunuto do T-127
- [x] `mac.menu-items` = `PASS` pro všech šest stavů a „Ukončit“ z menu (uzavře v T-127 oddíl 1b: **T-018** „Menu z T-008 …
      aktualizuje“, ruční část `mac.menu` mimo vzhled ikon) → přesunuto do T-127
- [x] Zjištění AXHelp u zakázaných položek zapsané v poznámkách (vstup pro T-210 / T-211) → přesunuto do T-127
- [x] Pester / `-SelfTest` a `dotnet build` / `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- **Helper** (`tools/macos-verify-helper.swift`): příkazy `menu <pid>` a `menupress <pid> <cesta>`. Menu stavové položky
  se otevře přes AXPress na položce v AXExtrasMenuBar (druhý pokus AXShowMenu). AXPress spouští sledování menu uvnitř
  akce, odpověď by přišla až po zavření menu, proto má stisk časový limit zpráv 0,25 s. Výsledek „cannot complete“ znamená
  otevřené menu. Ostatní volání Accessibility mají limit 1 s (místo výchozích 6 s), každá úroveň menu 2 s na otevření.
  Podmenu se otevírá jen tehdy, když jeho položky ještě nejdou přečíst. JSON: `items` (title, enabled, checked
  = AXMenuItemMarkChar, mark, shortcut z AXMenuItemCmdChar / CmdModifiers, help = AXHelp, separator, submenu)
  a `menuWindows` (okna procesu na úrovni pop-up menu během čtení). Zavření na každé cestě ven: AXCancel na menu, potom Esc,
  dokud zůstává okno úrovně menu (101), nejvýš třikrát. Menu, které zůstane otevřené, znamená exit 1. Když macOS
  okno menu v procesu aplikace neukáže (`menuWindows` = 0), jde jedno Esc naslepo, kromě případu, kdy menu zavřela
  stisknutá položka. `menupress` porovnává titulky přesně (Ordinal); neznámá nebo zakázaná položka = exit 1 s titulky
  úrovně (u zakázané i s jejím AXHelp).
- **Kontrola `mac.menu-items`** (`tools/macos-verify.ps1`, Permission, `-Needs Session, Accessibility`, v presetu
  `release` hned za `mac.menubar`): testovací bundle s tiskárnou a kamerou simulátoru, který běží s `--speed 20`
  (`Get-Simulator -Speed`, vlastní instance vedle sdíleného simulátoru s rychlostí 1). Pro `idle`, `printing`, `paused`,
  `finished`, `error`, `offline` počká, až tooltip ikony (AXHelp ze `statusitems`) ukáže `[E2E] <název>` a stav (nejvýš
  60 s). Potom přečte menu a ověří položku „Název: Stav“ (u printing / paused volitelně „ – N %“, jinde bez procent),
  podmenu (první „Open PrusaLink“, „Show camera“, oddělovač, položky nahrávání a timelapse, „Play last …“ zakázané bez
  souboru) a pevné položky Diagnostics…, Settings…, User manual, About, Quit (Quit poslední). Pak přes `menupress`
  zkusí neznámou položku (exit 1, titulky, žádné okno menu), Show camera → okno kamery, Finder do popředí → „Bring camera
  to front“ → aplikace v popředí s oknem kamery, Hide camera → okno pryč. Nakonec „Quit“ z menu (exit 0, stavová
  položka zmizí). Texty bere ze `Strings.resx` (testovací data mají `language: en`), značku přístupové klávesy odstraní
  `ConvertFrom-VerifyAccessText` (jako `NativeMenuText.DisplayTextOf`). Důkazy: `menu-<stav>.json`,
  `disabled-items-help.txt`, `menupress-unknown.txt`, log aplikace.
- **Kritérium 2:** „Bring camera to front“ je v podmenu jen při otevřeném okně kamery (T-102), proto kontrola okno
  otevře přes „Show camera“ a „Bring camera to front“ ověří z Finderu v popředí.
- **Čisté funkce** ve `VerifyTools.psm1` (`Get-VerifyResxStrings`, `ConvertFrom-VerifyAccessText`,
  `Get-VerifyMenuProblems`, `Get-VerifyMenuHeaderPattern`, `Get-VerifyMenuHelps`, `Get-VerifyToolTipProblem`) mají Pester
  testy v `tools/VerifyTools.Tests.ps1` (kontext „the menu of the status item“, včetně kontroly, že `mac.menu-items` je
  v presetu `release` jako Permission). Dokumentace: tabulka a příprava v `docs/macos-manual-testing.md`. Produkční kód
  se neměnil.
- **Ověřeno na Macu 2026-11-13 (agent, zamčená obrazovka, Terminál bez Zpřístupnění):** helper se přeloží bez varování.
  `menu` / `menupress` bez Zpřístupnění končí exit 3 se zprávou, chybný pid exit 1.
  `pwsh tools/macos-verify.ps1 -Unattended -Only mac.menu-items -Allow mac.menu-items` = `SKIP` s důvodem (Session:
  zamčená obrazovka, Accessibility: chybí), nic se nespustilo. `-SelfTest` PASSED (31 kontrol), Pester
  `tools/VerifyTools.Tests.ps1` 66 passed / 1 skipped (Windows), `-ListChecks -Preset release` = 21 kontrol.
- **AXHelp zakázaných položek (krit. 4):** zatím nezjištěno, protože skutečný běh potřebuje odemčenou obrazovku. Postup je
  v T-127 (oddíl 1j). Pokud `disabled-items-help.txt` ukáže „help none“, Avalonia `NativeMenuItem.ToolTip` do NSMenu
  nepropisuje. Pak vznikne otázka pro uživatele, zda tip doplnit jinak (žádné obcházení v aplikaci bez rozhodnutí).
- **Build a testy 2026-11-13:** `dotnet build` 0 varování, 0 chyb. Plný `dotnet test` běžel souběžně se zátěžovým
  testem T-219 (load average ~100): 10 534 testů, 10 265 prošlo, 267 přeskočeno a 2 selhaly. Obě selhání byla časový
  limit Pester sad `ReleaseOrchestrator.Tests.ps1` (15 min) a `ReleaseWinget.Tests.ps1` (5 min), které tento úkol
  nemění. Opakovaný běh `dotnet test --project tests/LayerPeek.Scripts.Tests` při běžné zátěži: 19 testů, 18 prošlo,
  1 přeskočen (Windows), 0 selhalo.
- Uzavřeno 2026-11-13: zbývající ruční ověření přesunuto do T-127 (rozhodnutí uživatele)
