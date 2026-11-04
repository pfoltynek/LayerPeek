---
id: T-127
title: macOS – zbývající ruční ověření (Dock, monitory, skutečná tiskárna, vydání)
status: todo
owner: qa-reviewer
priority: P1
platform: macos
depends_on: [T-019, T-020, T-021, T-026, T-038, T-053, T-055, T-058, T-097, T-101, T-102, T-106, T-107, T-115]
---

## Cíl
Sběrný úkol se zbytkem čistě ručních kontrol na Macu, které nejsou instalace a nepatří do jednoho sezení agenta
s uživatelem v T-115: ruční kroky bez tiskárny (Dock a ⌘Tab z T-102, zkušební upozornění z T-101, `/usr/bin/open`
z T-053, jazyk prvků AppKitu z T-097, monitory a plochy z T-038 včetně rozhodovacích bodů), kroky se skutečnou
tiskárnou (Místní síť z T-097, Buddy3D z T-026, graf teplot z T-106, web přes proxy z T-107) a vydání (T-055, T-058,
Velopack z T-019). Vznikl rozdělením T-115 (rozhodnutí uživatele 2026-11-01, stejně jako na Windows T-114 → T-124 /
T-125): body se přesunuly sem (v T-115 odškrtnuté s „→ přesunuto do T-127“). Skutečná instalace, Gatekeeper, oznámení
nainstalované aplikace a „Otevřít při přihlášení“ jsou v T-126. Protějšek pro Windows je T-125.

Postup: [docs/macos-manual-testing.md](../../docs/macos-manual-testing.md) (dále „sezení N.M“), skript
`tools/macos-verify.ps1` a pro vydání [docs/release.md](../../docs/release.md). U každého bodu je původní úkol
a kritérium.

## Rozsah
- Předpoklad: „Než začnete“ v `docs/macos-manual-testing.md`; pro oddíl 2 a 3 nainstalovaná
  `/Applications/LayerPeek.app` (sezení 1, viz T-126). Oddíl 3 zapisuje na GitHub – jen s výslovným souhlasem uživatele.
- Výsledky (verze macOS, datum, výňatek z `artifacts/verify/<čas>/report.md`, přesné texty dialogů) zapsat do
  `## Poznámky k implementaci`; výsledky rozhodovacích bodů T-038 (otázky 1–3) i do poznámek T-038.
- Každý `FAIL` nebo nalezená chyba = nový úkol. Bod se odškrtne až po úspěšném ověření.
- Hesla, API klíče ani heslo správce nikdy nevkládat do chatu ani do úkolů.

## Akceptační kritéria

### 1. Ručně na Macu (bez skutečné tiskárny, stačí simulátor; z T-115, oddíl 3)
- [ ] **T-102** (krit. „Ověřeno uživatelem na Macu: ikona v Docku a ⌘Tab …“): sezení 2.7 – ikona v Docku a ⌘Tab se
      objeví / zmizí podle oken, menu Docku se seznamem oken, klik na ikonu v Docku, fokus a pořadí oken, „Zobrazit
      kameru“ v menu. Zapsat i otázku 3 z T-102 (obnoví se menu aplikace v menu baru po přepnutí na běžnou aplikaci?);
      když ne, nový úkol.
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

- [ ] **T-146** (krit. „Ruční nahrávání jde spustit a zastavit … stav je vidět“, ruční část macOS; v sezení agenta nebyl
      aktivní displej, UI se nespustilo): simulátor s kamerou; v menu baru v podmenu tiskárny je za oddělovačem „Nahrávat“,
      „Přehrát poslední nahrávku“ (zakázané, dokud nahrávka není) a „Otevřít složku s nahrávkami“; „Nahrávat“ → tooltip
      ikony „Nahrává se“, v okně kamery „● REC“ i bez ukazatele ve všech třech režimech (pod ukazatelem čtverec Stop),
      „Zastavit nahrávání“ v menu i tlačítkem; „Přehrát poslední nahrávku“ otevře soubor v QuickTime Playeru, „Otevřít
      složku s nahrávkami“ Finder v podsložce tiskárny `~/Movies/LayerPeek/<tiskárna>`, bez nahrávek v `~/Movies/LayerPeek`
      (rozhodnutí 2026-11-04). Nastavení má pátou záložku „Nahrávání“, všech pět záhlaví na jednom řádku i ve francouzštině
      se systémovým písmem. „Změnit…“ otevře systémový dialog složky, složka bez práva zápisu ukáže hlášku; chyba nahrávání
      (např. složka na odpojeném disku) ukáže notifikaci „Nahrávání kamery selhalo“ a klik na ni otevře nastavení na záložce
      „Nahrávání“. Zapsat, jak vypadá zakázaná položka s tipem v NSMenu (tip se ukáže?).

- [ ] **T-148** (krit. „Uložení po spouštěči vytvoří přehratelný soubor …“, ruční část macOS; sezení agenta nemělo
      aktivní displej): simulátor s kamerou (`--speed 20`), tiskárna se zapnutým „Uchovávat poslední minuty (dashcam)“ (podzáložka
      Kamera, ve stejném řádku jako „Automaticky nahrávat tisk“) a bez automatického nahrávání; během tisku je v podmenu
      tiskárny v menu baru „Uložit posledních 2 min“ (mimo tisk zakázané s tipem) a totéž v místní nabídce okna kamery
      (⌃-klik); klik → za ~30 s soubor `…_dashcam-manual.mp4` v `~/Movies/LayerPeek/<tiskárna>`, přehraje se v QuickTime
      Playeru celý; `1 error` / `1 attention` / `1 stopped` v konzoli simulátoru během tisku → `…_dashcam-error.mp4` / `…-attention` / `…-stopped`. Se
      skutečnou Buddy3D (relay `tools/ncrelay.sh`): soubor obsahuje ~2 min před událostí a ~30 s po ní; v Monitoru aktivity
      roste paměť během tisku jen o ~17 MB a po konci tisku se vrátí. Záložka „Nahrávání“ má pod nahráváním sekci „Poslední
      minuty tisku (dashcam)“, nic se neuřízne se systémovým písmem.

- [ ] **T-154** (krit. „Poplach zobrazí notifikaci podle návrhu (macOS)“ a „Falešný poplach funguje“, ruční část macOS;
      sezení agenta nemělo aktivní displej): aplikace v `LayerPeek.app` (notifikace jen v bundlu), simulátor s kamerou,
      tiskárna se zapnutou detekcí (Nahrávání › „Hledat chyby tisku v obraze kamery“ + podzáložka Detekce), v testovacím
      režimu `LAYERPEEK_TEST_FAKE_DETECTOR=1` (Debug, testovací bundle `….E2E`) nebo s detektorem T-155. Notifikace má titulek
      = název tiskárny, text „Možná chyba tisku – …“, **přílohu se snímkem** (náhled vpravo, po rozbalení velký) a tlačítko
      „Falešný poplach“ (po najetí / rozbalení notifikace; už u **prvního** poplachu po startu – kategorie se registruje
      těsně před ním, zapsat, jestli tlačítko chybí). Tlačítko uloží `~/Library/Application Support/LayerPeek/detection/false-alarms/*.json`
      a aplikaci do popředí nepřenese; klik na notifikaci otevře okno kamery (i z centra oznámení). Uložený snímek zůstane ve
      `~/Movies/LayerPeek/<tiskárna>` (příloha je kopie, macOS ji přesune do svého úložiště), ve složce
      `~/Library/Caches/LayerPeek/notification-images` nezůstávají soubory. Zvuk jako u chyby tisku. Editor výřezu: tažení myší, šipky,
      ⌘+šipky o 10 %, VoiceOver přečte „Výřez podložky: … % zleva …“; záhlaví čtyř podzáložek na jednom řádku se systémovým písmem.
- [ ] **T-154** (krit. „Design spec schválený uživatelem“): uživatel prohlédne `docs/design/print-failure-detection-ui.md`
      a snímky `tests/LayerPeek.App.UiTests/Snapshots/macOS/settings-detection.png`, `settings-detection-camera-off-fr.png`,
      `settings-recording.png` (odpovědi 2026-11-04 jsou v T-154, neblokující otázky 11–15 tamtéž); změny = nový úkol.

### 1b. Subjektivní a fyzické body z T-115 (přesunuto 2026-11-02, rozhodnutí uživatele 2026-11-01; automatické části ověřil T-136)
- [ ] **T-105** (krit. „Skripty spuštěné uživatelem“, část macOS): `caffeinate -d -u -i pwsh tools/macos-verify.ps1`
      proběhl celý interaktivně včetně ručních kontrol níže; nalezené chyby skriptu zapsané (nový úkol). Zapsat odpověď
      na otázku 7 z T-105 (celá obrazovka se zamčenou obrazovkou u `mac.camera`). Otázka 6 je zodpovězená v T-115.
- [ ] **T-018** (krit. „ikona čitelná ve světlém i tmavém režimu“): `mac.icons` a `mac.menu` potvrzeno – světlý i tmavý
      menu bar, všechny stavy (lomítko offline, ▶ s vrstvami, ❚❚, ✓, !, ×), ostrost na monitoru @1x (při rozmazání
      zapsat; záloha je vícerozlišovací obrázek) (sezení 2.2).
- [ ] **T-018** (krit. „Menu z T-008 … aktualizuje“, ruční část): `mac.menu` potvrzeno (sezení 2.3); `mac.menubar` =
      `PASS` je zapsané v T-115.
- [ ] **T-116** (krit. „Ruční kontrola na Macu: obrázky tiskáren v menu …“): `mac.menu` – barevný obrázek 18 × 18 bodů
      u položky tiskárny, ostrý na Retině i na externím monitoru @1x, světlý i tmavý vzhled, zvýrazněná položka,
      zarovnání položek a podmenu tiskárny z T-042, vlastní obrázek jako čtverec (sezení 2.3). Rozmazání na @1x zapsat
      – oprava = nový úkol.
- [ ] **T-098** (krit. „V Docku a Finderu působí stejně velká jako systémové aplikace“): `mac.icons` (obrázek vedle
      systémových ikon) a pohled ve Finderu, Launchpadu a Nastavení systému (sezení 2.5).
- [ ] **T-026** (krit. „Stream … ve všech režimech okna“, ruční část): `mac.camera-look` potvrzeno (normální okno, celá
      obrazovka, malý náhled navrchu, překryv ve všech třech) (sezení 6.1); `mac.camera` = `PASS` je v T-115.
- [ ] **T-100** (krit. „Ověřeno uživatelem na Macu (včetně Logitech klávesnice)“, fyzická část): `mac.keyboard-physical`
      potvrzeno – vestavěná i Logitech klávesnice, české rozložení (⌘⇧] / ⌘⇧[, ⌃⌘F, ⌘W, ⌘.) (sezení 6.3); v Nastavení
      i v Diagnostice (výběr tiskárny, aspoň dvě tiskárny) ⌘. s otevřeným rozbalovacím seznamem zavře jen seznam (T-100
      otázka 3). `mac.keys` zůstal v T-115.
- [ ] **T-100** (otázka 1, rozhodovací bod; sezení 6.3, „Jiné rozložení klávesnice“): s **českým** rozložením
      přepínají ⌘⇧] / ⌘⇧[ (klávesy vpravo od P, „ú“ a „)“) hlavní záložky Nastavení? Nestane se nic jiného? Zapsat
      výsledek na vestavěné i Logitech klávesnici a rozhodnout, zda stačí fyzická pozice kláves (dnešní implementace);
      když ne, nový úkol.
- [ ] **T-100** (otázka 2, rozhodovací bod; sezení 6.3): dostanou se ⌃⌘F (okno kamery), ⌘W a ⌘. (všechna pomocná
      okna) na skutečném Macu k aplikaci, nebo je dřív zachytí systém / výchozí menu aplikace? Zapsat u každé zkratky;
      když některá nefunguje, nový úkol. Stav 2026-11-02: se skutečnými událostmi klávesnice (`mac.keys`) se ⌃⌘F, ⌘W
      a Esc dostanou k aplikaci; ⌘. AppKit posílá jako `cancelOperation:` a nedostalo se nikam – opraveno v T-141, pak
      `mac.keys` = `PASS`. Zbývá potvrdit na fyzické (vestavěné a Logitech) klávesnici.
- [ ] **T-122** (VoiceOver): se zapnutým VoiceOverem (⌘F5) přečte špendlík okna kamery jméno a stav a aktivace
      (⌃⌥mezerník) ho přepne, v normálním okně i malém náhledu. Akci AXPress ověřil `mac.camera-pin` (T-115).
- [ ] **T-117** (ruční část, vzhled a plochy): špendlík vpravo nahoře v normálním okně i malém náhledu, na celé
      obrazovce není; zapnutý je bez myši vidět ztlumený (čitelný i na světlém obrazu), vypnutý ne; malý náhled navrchu
      je na všech plochách (Spaces), po vypnutí jen na své; VoiceOver přečte „Vždy navrchu: zapnuto / vypnuto“;
      otevřené Nastavení ukáže stav. Snímek okna se špendlíkem do poznámek. Přepínání, Dock / ⌘Tab, kontextové menu
      a restart ověřil `mac.camera-pin` (T-115).
- [ ] **T-118** (ruční část): obrys malého náhledu nad světlou plochou i nad tmavým oknem, ve světlém i tmavém režimu;
      na celé obrazovce není; na externím monitoru @1x ostrý (1 px); úchopy po okrajích dál mění velikost, tažení obrazu
      a pásu přesouvá okno; porovnat s nativním obrysem oken macOS (návrh 4: záměrně o něco výraznější). Snímky na
      světlém a tmavém pozadí do poznámek. Obrys 2 px u hran a rohů, zesvětlení při najetí a žádný obrys v normálním
      okně ověřil `mac.camera-outline` (T-115).

- [ ] **T-096** (krit. „Start s uspaným displejem nezpůsobí pád …“) a **T-099** (krit. „Po probuzení displeje se objeví
      ikona … se stavem“, „Skutečný start se zhasnutým displejem“ a sezení 8.2): u Macu s monitorem **bez Sdílení
      obrazovky** `caffeinate -d -u -i pwsh tools/macos-verify.ps1 -Unattended -Allow mac.display-sleep -Only
      mac.notifications,mac.display-sleep` = `PASS` (oznámení pauzy doručené se zhasnutým displejem, tooltip ikony po
      probuzení „Paused“). Agent ho může spustit sám, když se Sdílení obrazovky na tu dobu odpojí. `mac.display-wait`
      = `PASS` je v T-115.

- [ ] **T-151** (krit. „Automaticky otevřené okno nebere fokus aktivnímu oknu jiné aplikace“, část macOS): s **odemčenou**
      obrazovkou `LAYERPEEK_E2E=1 caffeinate -d -u dotnet test --project tests/LayerPeek.App.Tests -- --filter-class
      "*MacCameraWindowAutoCloseE2ETests"` = PASS, ne „vynecháno … the screen is locked“. Agent ho může spustit sám, test
      přepne do popředí Finder. 2026-11-03 prošlo zavření a znovuotevření, ale obrazovka byla zamčená, takže fokus test
      neověřil.
- [ ] **T-151** (ruční, Dock a ⌘Tab): simulátor s kamerou, okno kamery otevřené, `POST /_simulator/offline` → po 30 s okno
      zmizí (Dock ikona zmizí, pokud nebylo jiné okno), `POST /_simulator/online` → po 10 s se okno objeví na stejném místě.
      Aktivní zůstane předchozí aplikace (menu bar s jejím jménem), LayerPeek je v Docku a v ⌘Tab (T-102). Zapsat, jestli je
      okno nad okny aktivní aplikace, nebo pod nimi. Malý náhled (vždy navrchu) se objeví navrchu na aktuální ploše bez
      ikony v Docku. Žádná notifikace.
- [ ] **T-151** (ruční, probuzení): se zapnutým podrobným logováním otevřít okno kamery, uspat Mac a probudit ho. V logu je
      „The computer resumed from sleep: the camera window delays start again.“ (`NSWorkspaceDidWakeNotification`)
      a okno se po probuzení nezavře podle stavu z doby před uspáním.

### 2. Se skutečnou tiskárnou (PrusaLink a kamera Buddy3D v místní síti; z T-115, oddíl 4)
Aplikaci spouštět z Finderu (`/Applications/LayerPeek.app`), ne z Terminálu.
- [ ] **T-097** (krit. „Dialog ‚Místní síť‘ ukáže text v jazyce systému“): `mac.plist-localnetwork` = `PASS` (oddíl 2 v T-115)
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
- [ ] **T-143** (krit. „Okno kamery, malý náhled a překryv fungují jako dřív“, ruční část se skutečnou kamerou): okno
      kamery Buddy3D ve všech třech režimech jako dřív; s otevřeným oknem „Otestovat kameru“ u uložené adresy v Nastavení
      vrátí snímek bez druhého spojení (s podrobným logováním žádné nové „Opening camera stream“ / připojení v logu),
      u změněné neuložené adresy vlastním spojením; minimalizace a obnovení do 10 s ukáže obraz hned, zavřené okno po
      10 s spojení uzavře („closed after its last subscriber left“ v logu s podrobným logováním).
- [ ] **T-147** (krit. „Nahrávka z kamery simulátoru i ze skutečné kamery jde na Macu přehrát“, přehrání v QuickTime):
      nahrávku z kamery simulátoru i ze skutečné Buddy3D (po T-146 z menu nebo okna kamery; do té doby soubor nahraný
      podle T-144 / T-147, poznámky) otevřít v QuickTime Playeru: obraz běží od začátku do konce, posuvník funguje, délka
      odpovídá. U nahrávky přerušené výpadkem kamery jdou přehrát oba soubory (`…mp4`, `…-2.mp4`).
- [ ] **T-106** (krit. „Ruční krok: na skutečné tiskárně ověřit, že kolísání s minimálním rozsahem 6 °C nepůsobí příliš
      dramaticky“): widget Teploty M a L a řádek grafu malého náhledu během ustáleného tisku; když působí dramaticky,
      zapsat a založit úkol (návrh 8–10 °C, rozhodne uživatel).
- [ ] **T-107** (krit. „Ruční ověření uživatelem se skutečnou tiskárnou“, část macOS): Safari, menu tiskárny → Otevřít
      web – web PrusaLink přes proxy se načte, zobrazí stav, nahraje soubor a stáhne soubor; v konzoli Safari žádná
      chyba kvůli Service Workeru nebo hlavičkám proxy.

### 3. Vydání (z T-115, oddíl 5; zapisuje na GitHub – jen s výslovným souhlasem uživatele, postup `docs/release.md`)
- [ ] **T-055** (krit. „Režim nanečisto vypíše plánované nahrání a na GitHubu nic nezmění“): `pwsh
      scripts/release-upload.ps1 -DryRun` (a `pwsh scripts/release.ps1 -Version X.Y.Z -DryRun`) vypíše plánované
      soubory; `gh release view vX.Y.Z --json assets,isDraft` před a po stejné.
- [ ] **T-055** (krit. „Orchestrátor ve zkušebním vydání“): v testovacím repozitáři (`-Repo`, `docs/release.md`,
      „Zkušební vydání (testovací repozitář)“) `release.ps1` pushne `main` + tag a `develop`, vytvoří koncept,
      po přerušeném buildu naváže a běh po dokončení nic neudělá.
- [ ] **T-055** (krit. „Zkušební vydání … vytvoření vydání a následné přidání souborů druhým během“, část macOS): ve
      stejném zkušebním vydání běh na Macu vytvoří koncept a nahraje soubory macOS; druhý běh (Windows) doplní své
      soubory – viz T-114 (od 2026-11-01 T-125). Postup „Zkouška doplnění druhého OS (T-113)“ v `docs/release.md`; **rozhodnuto 2026-10-30 (uživatel, T-113 Q1): doplnění druhého OS se poprvé ověří ostře u 0.3.1** (Mac zveřejní,
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

### 4. Závěr
- [ ] Výsledky zapsané v poznámkách (verze macOS, datum, zpráva skriptu); každá chyba má nový úkol; výsledky rozhodovacích
      bodů T-038 zapsané i do jeho poznámek; změny systému vrácené (sezení 12).

## Otevřené otázky
- (žádné vlastní; rozhodovací body T-038 otázky 1 b–3 jsou u kroků výše)

## Poznámky k implementaci
(vyplní agent / uživatel po ověření)
