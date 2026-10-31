---
id: T-115
title: Ruční ověření na Macu
status: todo
owner: qa-reviewer
priority: P1
platform: macos
depends_on: [T-020, T-021, T-026, T-053, T-055, T-058, T-096, T-097, T-098, T-099, T-101, T-102, T-105, T-106, T-107, T-116, T-117]
---

## Cíl
Sběrný úkol se vším, co na Macu musí ověřit uživatel sám (GUI, oprávnění macOS, Gatekeeper, uspaný displej, monitory,
skutečná tiskárna, vydání na GitHubu). Vznikl rozhodnutím uživatele 2026-10-30: rozpracované úkoly, kterým chybělo jen
ruční ověření, se uzavřely a zbývající body se přesunuly sem (v původním úkolu odškrtnuté s „→ přesunuto do T-115“).
Protějšek pro Windows je T-114.

Body jsou seřazené tak, aby šly projít v jednom sezení: nejdřív příprava, pak jeden běh skriptu
`tools/macos-verify.ps1`, potom ruční kroky bez tiskárny, kroky se skutečnou tiskárnou a nakonec vydání.
Podrobný postup a očekávání jsou v [docs/macos-manual-testing.md](../../docs/macos-manual-testing.md) (dále „sezení N.M“);
u každého bodu je původní úkol a kritérium.

T-018, T-019, T-038 a T-100 se uzavřely 2026-10-30 po rozhodnutí jejich otevřených otázek. Rozhodovací body, které
zbyly (T-038 otázky 2–3, T-100 otázky 1–2), jsou tu u příslušných kroků.

## Rozsah
- Před začátkem: „Než začnete“ v `docs/macos-manual-testing.md` (Apple Silicon, macOS 15+, externí monitor, Terminál
  ve fyzické cestě `cd -P`, záloha profilu LayerPeek, `pwsh tools/profile-snapshot.ps1 -Save`).
- Výsledky (verze macOS, datum, výňatek z `artifacts/verify/<čas>/report.md`, přesné texty dialogů) zapsat do
  `## Poznámky k implementaci` tohoto úkolu. U bodů z úkolů, které zůstaly rozpracované, i do poznámek toho úkolu.
- Každý `FAIL` nebo nalezená chyba = nový úkol (nebo oprava skriptu v rámci T-105 jako nový úkol). Bod se odškrtne až
  po úspěšném ověření.
- Hesla, API klíče ani heslo správce nikdy nevkládat do chatu ani do úkolů.

## Akceptační kritéria

### 1. Příprava
- [x] **T-058** (krit. „macOS: `.app` od `vpk`…“, část „celý běh `release-build.ps1` na Macu“): LayerPeek neběží,
      `caffeinate -d -u -i pwsh scripts/release-build.ps1` skončí kódem 0; v `artifacts/release/<verze>/dist/` jsou
      `.dmg`, `.tar.gz`, `SHA256SUMS-macos.txt`, `.nupkg` a soubory feedu `osx-arm64` (`releases.osx-arm64.json`,
      `assets.osx-arm64.json`, `RELEASES-osx-arm64`); `shasum -a 256 -c SHA256SUMS-macos.txt` = OK (sezení 1.1).
      Výstup potřebují `mac.gatekeeper*` a oddíl 5.
      Ověřeno 2026-10-30 (agent, `develop` 33a2678, verze 0.3.1): kód 0, všech 7 souborů v `dist/`, `shasum -c` = OK,
      `codesign --verify --deep --strict` platný, hardened runtime u všech Mach-O, AOT smoke test `.app` prošel (UIElement).
- [ ] **T-117** (krit. „Snímky UI: okno kamery s ikonou“, část macOS; bez toho na Macu selže 12 snímkových testů):
      přegenerovat macOS sadu `tests/LayerPeek.App.UiTests/Snapshots/macOS/` –
      `LAYERPEEK_UPDATE_SNAPSHOTS=1 dotnet test --project tests/LayerPeek.App.UiTests`, commitnout jen změněné
      `camera-mini-buttons.png`, `camera-mini-strip-240-pl.png`, `camera-mini-strip-chart-steady.png`,
      `camera-mini-strip-cooled-de.png`, `camera-mini-strip-cooling-240-it.png`, `camera-mini-strip-custom-240.png`,
      `camera-mini-strip-finish-320.png`, `camera-mini-strip-finish-400-es.png`, `camera-mini-strip-no-rows-320.png`,
      `camera-mini-strip-printing.png`, `camera-mini-strip-rows-240-de.png`, `camera-mini-strip-rows.png` (vpravo
      nahoře ztlumený plný špendlík, v `camera-mini-buttons` celá řada se špendlíkem vpravo), ostatní změny vrátit
      a testy spustit znovu bez proměnné. Potom přegenerovat obrázky README (`docs/readme-screenshots.md`, generují se
      na macOS): změní se `docs/images/{en,cs}/camera-mini.jpg` (špendlík), ostatní obrázky zkontrolovat (rezerva
      tlačítek okna 146 místo 112 může zúžit kartu nahoře uprostřed v `camera-window.jpg`), nesouvisející změny vrátit.

### 2. Skript `tools/macos-verify.ps1` (jeden interaktivní běh)
Postup: `pwsh tools/macos-verify.ps1 -ListChecks`, pak
`caffeinate -d -u -i pwsh tools/macos-verify.ps1 -Skip mac.local-network,mac.camera-buddy3d,mac.login-item`
(tiskárna je v oddílu 4, odhlášení v oddílu 3). Externí monitor připojit předem (`mac.camera-placement`, ostrost
ikony @1x v `mac.icons`). Zpráva: `artifacts/verify/<čas>/report.md`.
- [ ] **T-105** (krit. „Skripty spuštěné uživatelem“, část macOS): skript proběhl celý interaktivně; nalezené chyby
      skriptu zapsané a opravené (nový úkol). Zapsat i odpovědi na otázky 6 (okna vrstvy 25 v `menubar-windows.json`
      u `mac.menubar`) a 7 (celá obrazovka se zamčenou obrazovkou u `mac.camera`) z T-105.
- [ ] **T-021** (krit. round-trip, přepis / `DeleteAsync` / `null`, velikost písmen, izolace prefixu, úklid po pádu,
      zprávy bez tajného údaje): `mac.keychain` = `PASS` s **0 přeskočenými** testy (sezení 3.1).
- [ ] **T-021** (krit. „Kontrola před/po … v Keychainu nezůstane žádná položka“): `mac.keychain` a na konci
      `mac.profile-unchanged` = `PASS`; v aplikaci Klíčenka žádná položka `LayerPeek.Tests…`.
- [ ] **T-019** (krit. „Aplikace běží bez ikony v Docku, ikona je jen v menu baru“): `mac.dock` a `mac.menubar` = `PASS`
      (sezení 2.1).
- [ ] **T-019** (krit. „Okno Nastavení … v popředí a dostane fokus“): `mac.settings-front` = `PASS` (sezení 2.4).
- [ ] **T-018** (krit. „Menu z T-008 se v menu baru zobrazí, aktualizuje a ‚Ukončit‘ korektně ukončí aplikaci“):
      `mac.menubar` = `PASS` a `mac.menu` potvrzeno (sezení 2.3).
- [ ] **T-116** (krit. „Ruční kontrola na Macu: obrázky tiskáren v menu v menu baru jsou ostré na displeji @1x i @2x,
      čitelné ve světlém i tmavém režimu i u zvýrazněné položky, nerozbijí zarovnání položek“): `mac.menu` – otázka
      na obrázky tiskáren potvrzená (barevný obrázek 18 × 18 bodů u položky tiskárny, ostrý na Retině i na externím
      monitoru @1x, světlý i tmavý vzhled, zvýrazněná položka, zarovnání položek a podmenu tiskárny z T-042, vlastní
      obrázek jako čtverec) (sezení 2.3). Rozmazání na @1x zapsat (bitmapa je jedna, pro nejvyšší měřítko displejů,
      na @1x ji AppKit zmenší) – oprava = nový úkol.
- [ ] **T-018** (krit. „ikona čitelná ve světlém i tmavém režimu“): `mac.icons` a `mac.menu` potvrzeno – světlý i tmavý
      menu bar, všechny stavy (lomítko offline, ▶ s vrstvami, ❚❚, ✓, !, ×), ostrost na monitoru @1x (při rozmazání
      zapsat; záloha je vícerozlišovací obrázek) (sezení 2.2).
- [ ] **T-098** (krit. „V Docku a Finderu působí stejně velká jako systémové aplikace“): `mac.icons` (obrázek vedle
      systémových ikon) a pohled ve Finderu, Launchpadu a Nastavení systému (sezení 2.5).
- [ ] **T-020** (krit. „Notifikace fungují na macOS ze sestavené `.app`“): `mac.notifications` = `PASS` (testovací bundle)
      a `mac.notifications-production` potvrzeno (`/Applications/LayerPeek.app`, banner s názvem tiskárny, zvuk podle
      nastavení, ikona LayerPeeku) (sezení 5.1, 5.3).
- [ ] **T-020** (krit. „Doručené testovací notifikace se odstraní při ukončení i v režimu úklidu po pádu“):
      `mac.notifications` = `PASS` (sezení 5.1).
- [ ] **T-020** (krit. „Kliknutí na notifikaci otevře detail / web tiskárny“): `mac.notification-click` potvrzeno
      (aplikace běží); navíc ručně klik na oznámení v Centru oznámení, když produkční aplikace neběží – macOS ji spustí
      a otevře web tiskárny (sezení 5.2, rozhodnutí T-105 otázka 3).
- [ ] **T-020** (krit. „Odepřené oprávnění nezpůsobí pád; stav je srozumitelně ohlášen“): `mac.notifications-production`
      – po vypnutí v Nastavení systému → Oznámení červená hláška a tlačítko „Otevřít nastavení oznámení“ v Nastavení →
      Obecné, po zapnutí hláška zmizí, aplikace nespadne (sezení 5.3).
- [ ] **T-096** (krit. „Start s uspaným displejem nezpůsobí pád …“): `mac.display-wait` = `PASS` a `mac.display-sleep`
      potvrzeno; v logu „No active display …“ a „… starting the UI.“ (sezení 8.1).
- [ ] **T-099** (krit. „Po probuzení displeje se objeví ikona … se stavem“ a „Skutečný start se zhasnutým displejem“):
      `mac.display-sleep` potvrzeno; navíc ručně sezení 8.2 (událost tisku ze simulátoru během spánku displeje →
      oznámení přijde ještě se zhasnutým displejem, ikona po probuzení hned ukazuje stav).
- [ ] **T-026** (krit. „Stream … ve všech režimech okna“, část se simulátorem): `mac.camera` = `PASS` a `mac.camera-look`
      potvrzeno (normální okno, celá obrazovka, malý náhled navrchu, překryv ve všech třech) (sezení 6.1).
- [ ] **T-038** (krit. „Ruční ověření … s jedním a se dvěma monitory“, automatická část J2 a D1): `mac.camera-placement`
      = `PASS` na každém připojeném monitoru.
- [ ] **T-053** (krit. „Spojení od jiného UID se odmítne“): `mac.socket-other-user` (sudo, uživatel `nobody`) = `PASS`;
      volitelně se druhým účtem podle sezení 10.1.
- [ ] **T-100** (krit. „Ověřeno uživatelem na Macu (včetně Logitech klávesnice)“): `mac.keys` (Zpřístupnění pro
      Terminál, po běhu vypnout) a `mac.keyboard-physical` potvrzeno – vestavěná i Logitech klávesnice, české rozložení
      (⌘⇧] / ⌘⇧[, ⌃⌘F, ⌘W, ⌘.) (sezení 6.3); v Nastavení i v Diagnostice (výběr tiskárny, aspoň dvě
      tiskárny) ⌘. s otevřeným rozbalovacím seznamem zavře jen seznam (T-100 otázka 3, rozhodnuto 2026-10-30).
- [ ] **T-100** (otázka 1, rozhodovací bod; sezení 6.3, „Jiné rozložení klávesnice“): s **českým** rozložením
      přepínají ⌘⇧] / ⌘⇧[ (klávesy vpravo od P, „ú“ a „)“) hlavní záložky Nastavení? Nestane se nic jiného? Zapsat
      výsledek na vestavěné i Logitech klávesnici a rozhodnout, zda stačí fyzická pozice kláves (dnešní implementace);
      když ne, nový úkol.
- [ ] **T-100** (otázka 2, rozhodovací bod; sezení 6.3): dostanou se ⌃⌘F (okno kamery), ⌘W a ⌘. (všechna pomocná
      okna) na skutečném Macu k aplikaci, nebo je dřív zachytí systém / výchozí menu aplikace (`performKeyEquivalent`,
      „Celá obrazovka“, `cancelOperation:`)? Zapsat u každé zkratky; když některá nefunguje, nový úkol.
- [ ] **T-019** (krit. „Spuštění stažené `.dmg` na Macu s Gatekeeperem ověřené a postup popsaný v dokumentaci“):
      `mac.gatekeeper` = `PASS` a `mac.gatekeeper-dialog` potvrzeno (stažení přes Safari z lokálního serveru, spuštění
      z `.dmg`, přetažení do Aplikací, „Přesto otevřít“, totéž s `.tar.gz`); přesné texty dialogů zapsat a porovnat
      s `docs/release.md`, „Co uživatel uvidí na Macu“ (sezení 1.2–1.4).

### 3. Ručně na Macu (bez skutečné tiskárny, stačí simulátor)
- [ ] **T-102** (krit. „Ověřeno uživatelem na Macu: ikona v Docku a ⌘Tab …“): sezení 2.7 – ikona v Docku a ⌘Tab se
      objeví / zmizí podle oken, menu Docku se seznamem oken, klik na ikonu v Docku, fokus a pořadí oken, „Zobrazit
      kameru“ v menu. Zapsat i otázku 3 z T-102 (obnoví se menu aplikace v menu baru po přepnutí na běžnou aplikaci?);
      když ne, nový úkol.
- [ ] **T-117** (krit. „Snímky UI: okno kamery s ikonou“ a ruční část krit. „Tlačítko na hlavním panelu (Windows) a
      ikona v Docku (macOS) následují přepnutí z okna“ a „Přístupnost“, část macOS): okno kamery se simulátorem –
      špendlík vpravo nahoře v normálním okně i v malém náhledu, na celé obrazovce není; zapnutý je bez myši vidět
      ztlumený (čitelný i na světlém obrazu), vypnutý ne. Klik přepne „vždy navrchu“ hned (okno nad jinou aplikací),
      ikona v Docku / ⌘Tab zmizí / objeví se podle pravidla T-102, malý náhled navrchu je na všech plochách (Spaces),
      po vypnutí jen na své. Kontextové menu přes pravé tlačítko i ⌃-klik v normálním okně (dvě položky) i v borderless
      malém náhledu (jen „Vždy navrchu“, zaškrtnutá podle stavu). VoiceOver přečte přepínač se jménem „Vždy navrchu:
      zapnuto / vypnuto“ a stavem, mezerník přepíná. Volba přežije restart aplikace a otevřené Nastavení ji ukáže.
      Snímek okna se špendlíkem do poznámek.
- [ ] **T-101** (krit. „Ověřeno uživatelem na Macu i Windows“, část macOS): sezení 5.4 (a)–(d) – zkušební upozornění se
      zvukem i bez, nahrazení v Centru oznámení, klik přenese Nastavení na záložku Obecné, stav „zamítnuto“ s hláškou,
      `notDetermined` jen bez zásahu do TCC.
- [ ] **T-053** (krit. „Ručně na Macu: `/usr/bin/open` otevírá odkazy a web PrusaLink přes proxy (L4)“): sezení 10.2
      (falešné `open` v `PATH` se nespustí, web PrusaLink simulátoru přes proxy přihlášený, odkazy v O aplikaci se
      otevřou) a sezení 2.6 (odkazy a licence v O aplikaci).
- [ ] **T-097** (otázka 5, „uživatel to ověří při ručním testu“): sezení 4.5 – jazyk LayerPeeku jiný než jazyk systému;
      zapsat, v jakém jazyce jsou prvky AppKitu (výběr obrázku tiskárny, kontextové menu textového pole).
- [ ] **T-038** (krit. „Ruční ověření na macOS s jedním a se dvěma monitory, výsledky v poznámkách“, ruční část):
      kroky J1, J3–J7 a D2–D7 z T-038 („Ruční ověření“, sezení 6.2; J7 = Nastavení na MacBooku 13", sezení 3.3).
      Výsledky J4, J5 a J7 zapsat i do T-038 k otázkám 1–3.
- [ ] **T-038** (otázka 1 b, rozhodnuto a implementováno 2026-10-30; u kroku J4): malý náhled se zapnutým „vždy
      navrchu“ je vidět **na každé ploše** (Mission Control → přidat plochu, přepnout) a **nad aplikací na celé
      obrazovce** (Safari ⌃⌘F, přepnout na jeho plochu); normální okno se zapnutým „vždy navrchu“ a náhled bez „vždy
      navrchu“ zůstávají na své ploše; po přepnutí náhledu na normální okno nebo na celou obrazovku (a zpět) i po vypnutí
      „vždy navrchu“ v nastavení se okno chová jako dřív (celá obrazovka ve vlastní ploše funguje). Když ne, nový úkol.
- [ ] **T-038** (otázka 2, oprava schválená podmíněně 2026-10-30; u kroku J5): z celé obrazovky rovnou do malého
      náhledu (tlačítko náhledu, Esc u celé obrazovky otevřené z náhledu, „Použít“ v nastavení během celé obrazovky) –
      zapsat velikost, roh a rámeček náhledu po skončení animace. **Když náhled skončí ve velikosti normálního okna nebo
      jinde**, nahlásit výsledek J5: oprava je schválená (na macOS zopakovat umístění náhledu až po skončení animace, např.
      po první změně velikosti okna, nejpozději po 1 s) a založí se na ni nový úkol. Když je náhled v pořádku, nic.
- [ ] **T-038** (otázka 3, rozhodovací bod; u kroku J7): Nastavení na MacBooku 13" se systémovým písmem (San
      Francisco) – když se v některém jazyce něco uřízne nebo se objeví posuvník, rozhodnout: přizpůsobit texty /
      rozložení (ui-designer), nebo dodávat písmo Inter i na macOS (rozhodnutí produktu); výsledek a rozhodnutí zapsat
      a založit úkol. Když se nic neuřízne, otázka odpadá (zapsat).
- [ ] **T-019** (krit. „Autostart po přihlášení funguje na macOS“): `caffeinate -d -u -i pwsh tools/macos-verify.ps1
      -Only mac.login-item` (dva běhy, mezi nimi odhlášení a přihlášení) a sezení 7.3–7.4 (vypnutí v Nastavení
      systému → hint a tlačítko „Otevřít Nastavení systému“ s nápovědou končící „Povolte ho tam.“, zapsat stav, který
      macOS hlásí; po povolení v Nastavení systému a návratu do okna je neměněná volba zaškrtnutá bez Uložit – T-019
      otázka 11 a, rozhodnuto 2026-10-30). Výsledek zapsat i do poznámek T-019 (bod 11). Dělat jako poslední krok bez
      tiskárny (odhlášení).

### 4. Se skutečnou tiskárnou (PrusaLink a kamera Buddy3D v místní síti)
Aplikaci spouštět z Finderu (`/Applications/LayerPeek.app`), ne z Terminálu.
- [ ] **T-097** (krit. „Dialog ‚Místní síť‘ ukáže text v jazyce systému“): `mac.plist-localnetwork` = `PASS` (oddíl 2)
      a `caffeinate -d -u -i pwsh tools/macos-verify.ps1 -Only mac.local-network` – dialog aspoň v češtině
      a angličtině, přesné znění zapsat (sezení 4.1).
- [ ] **T-097** (krit. „Odepřené oprávnění nezpůsobí pád a je srozumitelně ohlášeno“): „Nepovolovat“ → aplikace
      nespadne; Diagnostika, Otestovat připojení, test kamery a okno kamery (i malý náhled s krátkou hláškou) ukážou
      hlášku o místní síti, v logu „Local Network access for LayerPeek may be off“; po povolení se spojení obnoví samo
      (sezení 4.2, 4.4).
- [ ] **T-097** (otázka 3, ověření adresy panelu): tlačítko „Otevřít Nastavení systému“ v Diagnostice, u „Otestovat
      připojení“ a u testu kamery otevře Soukromí a zabezpečení → Místní síť; zapsat, která adresa funguje (sezení 4.3).
- [ ] **T-026** (krit. „Stream z Buddy3D se na macOS zobrazí ve všech režimech okna“): `caffeinate -d -u -i pwsh
      tools/macos-verify.ps1 -Only mac.camera-buddy3d` – normální okno, celá obrazovka, malý náhled navrchu s překryvem
      stavu tisku (sezení 6.1).
- [ ] **T-026** (krit. „Podepsaný bundle (T-019) se spustí a stream funguje na čistém Macu“): `LayerPeek.app` z `.dmg`
      na účtu nebo Macu bez Homebrew a bez .NET SDK se spustí a stream Buddy3D běží (sezení 1.4 a 6.1).
- [ ] **T-106** (krit. „Ruční krok: na skutečné tiskárně ověřit, že kolísání s minimálním rozsahem 6 °C nepůsobí příliš
      dramaticky“): widget Teploty M a L a řádek grafu malého náhledu během ustáleného tisku; když působí dramaticky,
      zapsat a založit úkol (návrh 8–10 °C, rozhodne uživatel).
- [ ] **T-107** (krit. „Ruční ověření uživatelem se skutečnou tiskárnou“, část macOS): Safari, menu tiskárny → Otevřít
      web – web PrusaLink přes proxy se načte, zobrazí stav, nahraje soubor a stáhne soubor; v konzoli Safari žádná
      chyba kvůli Service Workeru nebo hlavičkám proxy.

### 5. Vydání (zapisuje na GitHub – jen s výslovným souhlasem uživatele, postup `docs/release.md`)
- [ ] **T-055** (krit. „Režim nanečisto vypíše plánované nahrání a na GitHubu nic nezmění“): `pwsh
      scripts/release-upload.ps1 -DryRun` (a `pwsh scripts/release.ps1 -Version X.Y.Z -DryRun`) vypíše plánované
      soubory; `gh release view vX.Y.Z --json assets,isDraft` před a po stejné.
- [ ] **T-055** (krit. „Orchestrátor ve zkušebním vydání“): v testovacím repozitáři (`-Repo`, `docs/release.md`,
      „Zkušební vydání (testovací repozitář)“) `release.ps1` pushne `main` + tag a `develop`, vytvoří koncept,
      po přerušeném buildu naváže a běh po dokončení nic neudělá.
- [ ] **T-055** (krit. „Zkušební vydání … vytvoření vydání a následné přidání souborů druhým během“, část macOS): ve
      stejném zkušebním vydání běh na Macu vytvoří koncept a nahraje soubory macOS; druhý běh (Windows) doplní své
      soubory – viz T-114. Postup „Zkouška doplnění druhého OS (T-113)“ v `docs/release.md`; **rozhodnuto 2026-10-30 (uživatel, T-113 Q1): doplnění druhého OS se poprvé ověří ostře u 0.3.1** (Mac zveřejní,
      Windows doplní); zkušební vydání je volitelné.
- [ ] **T-055** (krit. „Vydání verze `0.x` je na GitHubu označené jako předběžné“): po příštím vydání
      `gh release view vX.Y.Z --json isPrerelease` = `true`. Pozor: stav 2026-10-30 – `v0.1.0` a `v0.3.0` jsou
      předběžné, **`v0.2.0` ne** (označené „Latest“); **rozhodnuto 2026-10-30 (uživatel): `v0.2.0` nechat beze změny**; bod se týká jen nových vydání.
- [ ] **T-058** (krit. „macOS: … ruční body“, Gatekeeper): první spuštění `.dmg` staženého prohlížečem z GitHub vydání
      (Gatekeeper, „Přesto otevřít“) a App Translocation (spuštění přímo z `.dmg`: záložka Spuštění a aktualizace ukáže
      „Přesuňte LayerPeek do složky Aplikace…“, klik na novou verzi otevře jen stránku vydání).
- [ ] **T-058** (krit. „macOS: … ruční body“, aktualizace): nainstalovaná `/Applications/LayerPeek.app` najde novější
      vydání z GitHub Releases **bez tokenu** (feed `releases.osx-arm64.json`), aktualizace proběhne s účtem správce
      i se **standardním účtem** (zapsat případný dialog AppleScriptu / heslo) a aplikace se po ní sama spustí
      (`open -n`).
- [ ] **T-058** (krit. „Klik na položku / notifikaci otevře stránku vydání, případně spustí aktualizaci“, část macOS):
      skutečný klik na položku „Je dostupná nová verze X.Y.Z“ v menu baru a na banner notifikace o nové verzi.
- [ ] **T-058** (krit. „macOS: … ruční body“, Klíčenka a notifikace) + **T-021** (otázka k podpisu) + **T-020** (riziko
      ad-hoc podpisu): po aktualizaci nebo přetažení nové `.app` přes starou (sezení 9.1–9.2) zapsat: dialog Klíčenky
      „LayerPeek chce použít důvěrné informace…“ ano / ne, a po „Povolit“ znovu při dalším startu?; zůstalo povolení
      oznámení?; ptá se macOS znovu na Místní síť?
- [ ] **T-019** (krit. „Výsledek pokusu Velopack na macOS … zapsaný v `docs/velopack.md`, bod 10“): výsledky tří
      předchozích bodů zapsané v `docs/velopack.md`, bod 10 (odstavec „Stále neověřeno“ aktualizovaný), doporučení
      platí, nebo je upravené.

### 6. Úklid a závěr
- [ ] Úklid podle sezení 12 (Otevřít při přihlášení vypnuto, aplikace a stažené soubory smazané nebo ponechané podle
      volby uživatele, Klíčenka, změny systému vrácené); `pwsh tools/profile-snapshot.ps1 -Compare` ukáže jen to, co
      mělo zůstat.
- [ ] Výsledky zapsané v poznámkách (verze macOS, datum, zpráva skriptu); každá chyba má nový úkol; výsledky rozhodovacích bodů
      T-038 a T-100 zapsané i do jejich poznámek.

## Otevřené otázky
- (žádné vlastní; rozhodovací body T-038 otázky 2–3 a T-100 otázky 1–2 jsou u kroků výše)

## Poznámky k implementaci
(vyplní agent / uživatel po ověření)
