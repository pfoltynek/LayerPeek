---
id: T-125
title: Windows – zbývající ruční ověření (interaktivní windows-verify, tiskárna, vydání)
status: todo
owner: qa-reviewer
priority: P1
platform: windows
depends_on: [T-017, T-055, T-058, T-101, T-105, T-107, T-114, T-117, T-118]
---

## Cíl
Sběrný úkol se zbytkem ověření na Windows mimo instalátor: celý interaktivní běh `tools/windows-verify.ps1` (T-105),
zkušební upozornění (T-101), nepovinné subjektivní kontroly okna kamery (T-117, T-118), instalátor `win-arm64` (T-017),
ověření se skutečnou tiskárnou (T-107) a body vydání (T-055, T-058, SmartScreen z T-017). Vznikl rozdělením T-114
(rozhodnutí uživatele 2026-11-01): body se přesunuly sem (v T-114 odškrtnuté s „→ přesunuto do T-125“); skutečná
instalace, autostart a odinstalace jsou v T-124. Protějšek pro Mac je T-127.

Postup: [docs/windows-manual-testing.md](../../docs/windows-manual-testing.md) a skript `tools/windows-verify.ps1`;
vydání podle [docs/release.md](../../docs/release.md).

## Rozsah
- Kontroly se skutečnou instalací (`win.e2e-installer`, `win.install`, `win.toast-click`, `win.autostart`,
  `win.uninstall`) patří do T-124. Zda je má „celý interaktivní běh“ u T-105 obsahovat znovu, nebo stačí běh
  s `-Skip` těchto kontrol a jejich výsledek z T-124, rozhodne uživatel (otázka níže).
- Aktualizace přes Velopack (bod T-058) potřebuje nainstalovanou starší verzi – stejný předpoklad jako T-124
  (odložená instalace uživatele).
- Oddíl „Vydání“ zapisuje na GitHub – jen s výslovným souhlasem uživatele.
- Výsledky (verze Windows, datum, výňatek z `artifacts/verify/<čas>/report.md`) zapsat do `## Poznámky k implementaci`.
  Každý `FAIL` nebo nalezená chyba = nový úkol. Bod se odškrtne až po úspěšném ověření.
- Hesla ani API klíče nikdy nevkládat do chatu ani do úkolů.

## Akceptační kritéria

### 1. Skript `tools/windows-verify.ps1` (z T-114, oddíl 2)
Postup: `pwsh scripts/release-build.ps1`, pak `pwsh tools/windows-verify.ps1 -ListChecks` a
`pwsh tools/windows-verify.ps1` (interaktivně).
- [ ] **T-105** (krit. „Skripty spuštěné uživatelem“, část Windows): `tools/windows-verify.ps1` proběhl celý
      interaktivně (i `-RestoreOnly` po případném přerušení); nalezené chyby skriptu zapsané a opravené (nový úkol).

### 2. Ruční kontroly bez tiskárny (z T-114, oddíl 3)
- [ ] **T-101** (krit. „Ověřeno uživatelem na Macu i Windows“, část Windows): tlačítko „Poslat zkušební upozornění“
      pošle toast (zvuk podle hodnoty vybrané v okně, i neuložené), druhý klik předchozí toast nahradí; po vypnutí
      v Nastavení → Systém → Oznámení varování „Upozornění jsou ve Windows vypnutá.“ s tlačítkem, které otevře
      `ms-settings:notifications`; klik na zkušební toast přenese okno Nastavení na záložku Obecné.
      → Nahrazuje E2E test `TestNotificationTests.TestNotificationFollowsTheWindowReplacesItselfOpensGeneralAndWarnsWhenWindowsTurnedItOff`
      (T-128): `pwsh tools/run-e2e.ps1 -Class '*TestNotificationTests'` (systémové změny `Toasts`, `DoNotDisturb`;
      s `-NoSystemChanges` se přeskočí). Zvuk se ověřuje v XML toastu z historie oznámení (uložené „Všechna“, v okně
      neuložené „Vypnuto“ → toast je tichý). Odškrtnout po zeleném běhu.
- [ ] *Nepovinné, doporučené (zbytek T-117 po T-120):* čitelnost ztlumeného špendlíku na světlém obraze kamery a při
      200 % (subjektivní; E2E jen měří jas a ukládá výřezy) a skutečný hlas Předčítání v češtině („Vždy navrchu: zapnuto /
      vypnuto“; E2E ověřuje jen UIA název a stav, které Předčítání čte).
- [ ] *Nepovinné, doporučené (zbytek T-118 po T-120):* vzhled obrysu nad skutečnou tapetou a Průzkumníkem (E2E používá
      vlastní bílé a tmavé okno za náhledem); ztmavnutí obrysu během tažení úchopem je přijaté a netestuje se.
- [ ] **T-017** (krit. „Instalátor `win-arm64` vznikne stejným skriptem, pokud je `win-arm64` zapnutý“): po doinstalaci
      komponenty MSVC ARM64 jako správce (`docs/prerequisites.md`, T-063 bod 1) `pwsh scripts/release-build.ps1 -Runtime
      win-x64,win-arm64` vytvoří i `LayerPeek-<verze>-win-arm64-Setup.exe` a soubory kanálu `win-arm64` bez kolize
      s `win-x64`; nic se nenahrává. Spuštění na zařízení ARM64 se tu neověřuje (T-057). Lze spojit s T-063.

### 3. Se skutečnou tiskárnou (z T-114, oddíl 4)
- [ ] **T-107** (krit. „Ruční ověření uživatelem se skutečnou tiskárnou“, část Windows): Edge i Chrome, menu tiskárny →
      Otevřít web – web PrusaLink přes proxy se načte, zobrazí stav, nahraje a stáhne soubor; v konzoli prohlížeče žádná
      chyba kvůli Service Workeru nebo hlavičkám proxy.

### 4. Vydání (z T-114, oddíl 5; zapisuje na GitHub – jen s výslovným souhlasem uživatele, postup `docs/release.md`)
- [ ] **T-055** (krit. „Zkušební vydání … následné přidání souborů druhým během“, část Windows): ve zkušebním vydání
      z T-115 (běh na Macu první; od 2026-11-01 bod v T-127) doplní Windows své soubory (`release-upload.ps1
      -AddToPublished`, `docs/release.md`, „Zkouška doplnění druhého OS (T-113)“); `release-publish.ps1 -DryRun` bez
      `-AllowIncomplete` pak projde.
- [ ] **T-058** (krit. „Klik na položku / notifikaci otevře stránku vydání, případně spustí aktualizaci přes Velopack“,
      část Windows): skutečný klik na položku „Je dostupná nová verze X.Y.Z“ v menu a na banner toastu; nainstalovaná
      starší verze se přes Velopack aktualizuje z GitHub Releases (bez tokenu) a znovu spustí; `.zip` verze otevře
      stránku vydání.
      → Nahrazují E2E testy `UpdateTests` (T-128): `InstalledOlderVersionUpdatesFromALocalFeedAfterAClick` (klik na položku
      menu i na banner; přenosná instalace Velopack v `%TEMP%` z místního zdroje, ne z GitHubu; aktualizovanou kopii spustí
      test, protože testovací režim aplikaci po aktualizaci znovu nespouští) a
      `ZipVersionOpensTheReleasePageFromTheBannerAndTheMenuItem`. Běh: `pwsh tools/run-e2e.ps1 -Class '*UpdateTests'`, test
      instalace jen bez nainstalované LayerPeek (poprvé s T-124). Znovuspuštění samotným `Update.exe` se neověřuje, stačí
      spuštění aktualizované kopie testem (rozhodnutí uživatele 2026-11-01). Odškrtnout po zeleném běhu.
- [ ] **T-058** (ruční zbytek, rozhodnutí uživatele 2026-11-01): jednou po skutečném vydání aktualizace nainstalované
      starší verze z GitHub Releases (bez tokenu) – E2E testy používají jen místní zdroj.
- [ ] **T-017** (otevřená otázka SmartScreen): stažení `Setup.exe` z GitHub vydání v Edge – zapsat varování SmartScreen
      při stahování a doplnit ho do `docs/release.md` (spuštění se značkou Mark-of-the-Web ověřil T-056).

### 5. Závěr (z T-114, oddíl 6)
- [ ] Výsledky zapsané v poznámkách (verze Windows, datum, zpráva skriptu); každá chyba má nový úkol; profil vrácený
      (`win.uninstall`, případně `pwsh tools/windows-verify.ps1 -RestoreOnly`).

## Otevřené otázky
- Má „celý interaktivní běh“ `tools/windows-verify.ps1` (T-105) obsahovat i kontroly se skutečnou instalací, které
  ověřuje T-124 (pak i tady musí být instalace uživatele odložená), nebo stačí běh s
  `-Skip win.e2e-installer,win.install,win.toast-click,win.autostart,win.uninstall` a výsledek těchto kontrol z T-124?
- Aktualizace přes Velopack (T-058) potřebuje nainstalovanou starší verzi: dělat ji ve stejném sezení jako T-124
  (instalace uživatele už odložená), nebo samostatně? Rozhodne uživatel.

## Poznámky k implementaci
(vyplní agent / uživatel po ověření)
