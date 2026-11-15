---
id: T-126
title: macOS – ověření instalátoru (Gatekeeper, upozornění, Otevřít při přihlášení, odinstalace)
status: todo
owner: qa-reviewer
priority: P1
platform: macos
depends_on: [T-019, T-020, T-115]
---

## Automatizace (rozhodnuto uživatelem 2026-11-13)
- **Pravidlo odškrtávání (T-208):** část bodu s automatickou kontrolou (`mac.gatekeeper`, `mac.notifications`) se
  odškrtne, když je kontrola `PASS` ve zprávě `caffeinate -d -u -i pwsh tools/macos-verify.ps1 -Preset release` na
  commitu s ověřovaným kódem. Do poznámek se zapíše složka běhu, commit a řádek zprávy.
- T-217 (`mac.release-bundle`) před instalací zkontroluje aplikaci z `.dmg` i `.tar.gz`: podpis `--deep --strict`,
  knihovny bez Homebrew a start bez `DOTNET_ROOT`. T-212 (`mac.notifications-content`) ověří obsah notifikací
  testovacího bundlu (titulek, příznak zvuku, klik).
- Ruční zůstává: dialogy Gatekeeperu po stažení prohlížečem, notifikace **produkční** `/Applications/LayerPeek.app`
  (banner, ikona, klik, když aplikace neběží, vypnutá oznámení), „Otevřít při přihlášení“ s odhlášením a odinstalace.

## Cíl
Sběrný úkol jen pro skutečnou instalaci na Macu: stažená `.dmg` / `.tar.gz` s Gatekeeperem a přetažení do Aplikací,
oznámení nainstalované `/Applications/LayerPeek.app` (včetně kliknutí, když aplikace neběží, a vypnutých oznámení),
„Otevřít při přihlášení“ po skutečném odhlášení a přihlášení a odstranění instalace. Vznikl rozdělením T-115 (rozhodnutí
uživatele 2026-11-01, stejně jako na Windows T-114 → T-124 / T-125): body se přesunuly sem (v T-115 odškrtnuté
s „→ přesunuto do T-126“). Zbytek ručních kontrol na Macu (bez instalace) je v T-127. Protějšek pro Windows je T-124.

Postup: [docs/macos-manual-testing.md](../../docs/macos-manual-testing.md) (dále „sezení N.M“): sezení 1 (instalace,
Gatekeeper), 5 (oznámení), 7 (Otevřít při přihlášení) a 12 (úklid), a skript `tools/macos-verify.ps1`.

## Rozsah
- Předpoklad: výstup `release-build.ps1` z T-115 (oddíl 1) v `artifacts/release/<verze>/dist/`, „Než začnete“
  v `docs/macos-manual-testing.md` (záloha profilu, `pwsh tools/profile-snapshot.ps1 -Save`). Řízené kroky
  s `/Applications/LayerPeek.app` nabídnou dočasné odsunutí profilu LayerPeek (žurnál `scripts/aot-smoke.ps1`).
- Kontroly `mac.gatekeeper`, `mac.gatekeeper-dialog`, `mac.notifications-production` a `mac.notification-click` běží
  i v interaktivním běhu skriptu z T-115 (oddíl 2); když tam prošly, stačí sem zapsat výsledek z jeho zprávy. Jinak
  samostatně `caffeinate -d -u -i pwsh tools/macos-verify.ps1 -Only mac.gatekeeper,mac.gatekeeper-dialog,mac.notifications-production,mac.notification-click`.
  `mac.login-item` (dva běhy s odhlášením) jako poslední.
- Výsledky (verze macOS, datum, výňatek z `artifacts/verify/<čas>/report.md`, přesné texty dialogů) zapsat do
  `## Poznámky k implementaci`. Každý `FAIL` nebo nalezená chyba = nový úkol. Bod se odškrtne až po úspěšném ověření.
- Hesla, API klíče ani heslo správce nikdy nevkládat do chatu ani do úkolů.

## Akceptační kritéria

### 1. Instalace a Gatekeeper (z T-115, oddíl 2)
- [ ] **T-019** (krit. „Spuštění stažené `.dmg` na Macu s Gatekeeperem ověřené a postup popsaný v dokumentaci“):
      `mac.gatekeeper` = `PASS` a `mac.gatekeeper-dialog` potvrzeno (stažení přes Safari z lokálního serveru, spuštění
      z `.dmg`, přetažení do Aplikací, „Přesto otevřít“, totéž s `.tar.gz`); přesné texty dialogů zapsat a porovnat
      s `docs/release.md`, „Co uživatel uvidí na Macu“ (sezení 1.2–1.4).
      **Automatizace:** `mac.gatekeeper` v presetu `release` (T-208); dialogy zůstávají ruční.

### 2. Oznámení nainstalované aplikace (z T-115, oddíl 2)
- [ ] **T-020** (krit. „Notifikace fungují na macOS ze sestavené `.app`“): `mac.notifications` = `PASS` (testovací bundle)
      a `mac.notifications-production` potvrzeno (`/Applications/LayerPeek.app`, banner s názvem tiskárny, zvuk podle
      nastavení, ikona LayerPeeku) (sezení 5.1, 5.3).
      **Automatizace:** `mac.notifications` v presetu `release` (T-208), obsah notifikací testovacího bundlu T-212;
      produkční aplikace zůstává ruční.
- [ ] **T-020** (krit. „Kliknutí na notifikaci otevře detail / web tiskárny“): `mac.notification-click` potvrzeno
      (aplikace běží); navíc ručně klik na oznámení v Centru oznámení, když produkční aplikace neběží – macOS ji spustí
      a otevře web tiskárny (sezení 5.2, rozhodnutí T-105 otázka 3).
- [ ] **T-020** (krit. „Odepřené oprávnění nezpůsobí pád; stav je srozumitelně ohlášen“): `mac.notifications-production`
      – po vypnutí v Nastavení systému → Oznámení červená hláška a tlačítko „Otevřít nastavení oznámení“ v Nastavení →
      Obecné, po zapnutí hláška zmizí, aplikace nespadne (sezení 5.3).

### 3. Otevřít při přihlášení (z T-115, oddíl 3)
- [ ] **T-019** (krit. „Autostart po přihlášení funguje na macOS“): `caffeinate -d -u -i pwsh tools/macos-verify.ps1
      -Only mac.login-item` (dva běhy, mezi nimi odhlášení a přihlášení) a sezení 7.3–7.4 (vypnutí v Nastavení
      systému → hint a tlačítko „Otevřít Nastavení systému“ s nápovědou končící „Povolte ho tam.“, zapsat stav, který
      macOS hlásí; po povolení v Nastavení systému a návratu do okna je neměněná volba zaškrtnutá bez Uložit – T-019
      otázka 11 a, rozhodnuto 2026-10-30). Výsledek zapsat i do poznámek T-019 (bod 11). Dělat jako poslední krok bez
      tiskárny (odhlášení).

### 4. Závěr
- [ ] Odstranění instalace podle sezení 12, kroky 1–2 („Otevřít při přihlášení“ vypnuto, položka zmizí z Položek při
      přihlášení; `/Applications/LayerPeek.app` a stažené soubory smazané nebo ponechané podle volby uživatele, např. pro
      T-127); odsunutý profil vrácený (`pwsh scripts/aot-smoke.ps1 -RestoreOnly` po přerušení).
- [ ] Výsledky zapsané v poznámkách (verze macOS, datum, zpráva skriptu, texty dialogů); každá chyba má nový úkol.

## Otevřené otázky
- (žádné vlastní)

## Poznámky k implementaci
(vyplní agent / uživatel po ověření)
