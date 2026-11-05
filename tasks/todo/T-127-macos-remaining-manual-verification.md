---
id: T-127
title: macOS – zbývající ruční ověření (Dock, monitory, skutečná tiskárna, vydání)
status: todo
owner: qa-reviewer
priority: P1
platform: macos
depends_on: [T-019, T-020, T-021, T-026, T-038, T-053, T-055, T-058, T-097, T-101, T-102, T-106, T-107, T-115]
---

## Před vydáním 0.5.0 – doporučené pořadí
Doplněno 2026-11-04 (architekt). Jeden průchod na ~1–2 h se skutečnou Core One a kamerou Buddy3D. Řadí body tohoto
úkolu pro funkce od 0.4.0 (nahrávání, dashcam, detekce, timelapse). Postupy jsou u bodů níže, odkaz má tvar oddíl ·
úkol, a odškrtávají se tam. Nové UI od 0.4.0 na Macu **ještě nikdo neviděl na skutečném displeji** (agent neměl
aktivní displej). To platí i pro systémové dialogy souborů (`NSOpenPanel`) a notifikace s přílohou a tlačítkem, proto
mají přednost. Co z průchodu blokuje vydání, rozhodne uživatel. Každá nalezená chyba = nový úkol.

0. **Příprava (~10 min).** `LayerPeek.app` z `caffeinate -d -u -i pwsh scripts/release-build.ps1` (notifikace fungují jen
   v bundlu; sezení 1 v `docs/macos-manual-testing.md`). Pozor: na `develop` je ještě `<Version>0.4.0</Version>`, build
   proto smaže a přepíše `artifacts/release/0.4.0` daného pracovního stromu. Spouštět ho v samostatném worktree, nebo až
   na větvi `release/0.5.0`. Zálohovat profil („Než začnete“). Core One s kamerou, světlo komory zapnuté, podrobné
   logování.
1. **Nastavení a první dialog souboru (~10 min):** oddíl 1 · **T-146** (jen část nastavení: pátá záložka „Nahrávání“,
   „Změnit…“ = systémový dialog složky, nezapisovatelná složka) → oddíl 1 · **T-154** (editor výřezu: „Načíst snímek“
   ze skutečné Buddy3D, myš, šipky, ⌘+šipky, VoiceOver; „Výchozí pro Core One“ sedí na přední část podložky). Potom
   zapnout detekci (hlavní vypínač + podzáložka Detekce) a u tiskárny „Uchovávat poslední minuty (dashcam)“ a „Pořizovat
   timelapse tisku“, **bez** „Automaticky nahrávat tisk“ (jinak dashcam stojí).
2. **Tisk A – dashcam, detekce, timelapse (~25 min, krátký díl):** oddíl 1 · **T-161** (TL v okně kamery a malém náhledu,
   ztlumené během pauzy tisku) → oddíl 1 · **T-148** (během tisku „Uložit posledních 2 min“ z menu i z místní nabídky okna
   kamery; se skutečnou Buddy3D ~2 min před událostí a ~30 s po ní, paměť +~17 MB) → oddíl 2 · **T-155** krok (2)
   (inscenované selhání na přední části podložky, až po rozjezdu detektoru: aspoň 2 min tisku a Z nad 1 mm) a s ním oddíl 1 · **T-154** (notifikace s přílohou, tlačítko „Falešný
   poplach“ už u prvního poplachu, klik otevře okno kamery, `…_alarm-bed-change.jpg` a `…_dashcam-failure.mp4`). Nakonec
   tisk zastavit na tiskárně → `…_dashcam-stopped.mp4` (oddíl 1 · T-148).
3. **Tisk B – automatické nahrávání (~15 min, krátký díl):** zapnout „Automaticky nahrávat tisk“ → oddíl 2 · **T-145**
   (doplněný bod) a zároveň oddíl 2 · **T-155** krok (1) (celý běžný tisk bez poplachu; delší tisk podle času). Před tiskem
   přepnout timelapse na „Každou N. vrstvu (odhad)“ (záložka Nahrávání) → oddíl 2 · **T-159** (timelapse po vrstvách
   téhož tisku, ideálně s `record-z.sh` vedle, a díl s vrstvou 0,1 mm).
   Před tiskem B zapnout u tiskárny i detektor „Tisk do vzduchu“ (štítek „Experimentální“) → oddíl 2 · **T-167** krok (1).
4. **Po tiscích – přehrání a dialogy souborů (~20 min, nejvyšší priorita):** oddíl 1 · **T-146** (zbytek: tray menu,
   „● REC“, ruční Start / Stop, „Přehrát poslední nahrávku“ v QuickTime, „Otevřít složku s nahrávkami“) → oddíl 2 ·
   **T-147** (přehrání nahrávky z Buddy3D v QuickTime) → oddíl 1 · **T-164** (`NSOpenPanel` „Vytvořit timelapse
   z nahrávky…“ nad nahrávkou tisku B) → oddíl 1 · **T-165** (`NSOpenPanel` „Exportovat timelapse do JPEG…“ nad timelapse
   tisku A) → oddíl 1 · **T-158** (přehrání vzorků timelapse v QuickTime, 2 min).
5. **Když zbývá čas (~15 min):** oddíl 2 · **T-143** (sdílené spojení se skutečnou kamerou) → oddíl 1 · **T-151** (Dock
   a ⌘Tab: vypnout tiskárnu → po 30 s okno zmizí, zapnout → po 10 s se tiše objeví; místo simulátoru stačí skutečná
   tiskárna) → oddíl 1 · **T-151** (fokus, E2E s odemčenou obrazovkou) → oddíl 1 · **T-154** a **T-161** (prohlédnutí
   návrhů a snímků).
6. Výsledky zapsat do `## Poznámky k implementaci`: verze macOS, datum, názvy vzniklých souborů, u T-155 čas do poplachu
   a skóre z logu.

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
- [ ] **T-158** (krit. „dva po sobě jdoucí vybrané IDR se stejným `idr_pic_id` … přehrání v QuickTime ručně“; agent ověřil
      jen dekódování přes AVFoundation `AVAssetReader` – 5/5 a 25/25 snímků bez chyby): v QuickTime Playeru otevřít
      `~/LayerPeek-samples/timelapse/t158-same-idr-pic-id.mp4` (5 snímků, `idr_pic_id` 7, 7, 8, 8, 7) a
      `t158-coreone-clip-25-keyframes.mp4` (25 klíčových snímků skutečné Buddy3D, 0,83 s při 30 fps); přehrají se bez
      rozpadlých snímků, krokování šipkami projde všechny snímky.

- [ ] **T-160** (krit. „Měření na skutečné kameře (relay): CPU a velikost souboru za ≥ 10 min, s nahráváním i bez něj“;
      kamera byla při implementaci vypnutá, agent změřil simulátor se skutečným klipem: 0,28 % jádra bez nahrávání,
      0,60 % s nahráváním, ~75 kB na snímek): se zapnutou Buddy3D spustit relay `~/LayerPeek-samples/tools/ncrelay.sh 8556
      192.168.0.227` a měřicí program z T-160 (hub + `CameraRecordingService` s timelapse, 10 min, interval 30 s; postup
      v poznámkách T-160) jednou jen s timelapse a jednou i s nahráváním; zapsat CPU procesu, alokace a velikost souboru
      do T-160. Zároveň přehrát vzniklý `…_timelapse.mp4` v QuickTime Playeru (20 snímků, bez rozpadlých snímků).

- [ ] **T-161** (krit. „Ruční start / stop z tray menu i z okna kamery“, „Okno kamery i malý náhled ukazují indikátor“,
      „Tray menu … otevře poslední timelapse v přehrávači systému“, ruční část macOS; sezení agenta nemělo aktivní displej):
      simulátor s kamerou (`--speed 20`); v podmenu tiskárny v menu baru „Spustit timelapse (experimentální)“ → soubor
      `~/Movies/LayerPeek/<tiskárna>/…_timelapse.mp4`, položka „Zastavit timelapse (experimentální)“; okno kamery i malý
      náhled ukazují jantarové stopky s „TL“ bez ukazatele nad oknem, během `1 pause` simulátoru ztlumené; stop z okna kamery
      (tlačítko, VoiceOver přečte „Zastavit timelapse (experimentální)“) → „Přehrát poslední timelapse“ otevře soubor
      v QuickTime Playeru. Nastavení: štítek „Experimentální“ u sekce Timelapse (záložka Nahrávání) a u „Pořizovat timelapse
      tisku“ (podzáložka Kamera), se systémovým písmem nic neuříznuté; notifikace „Timelapse selhal“ při chybě (např. složka
      na odpojeném disku), klik otevře nastavení na záložce Nahrávání. Zapsat, jak vypadá tip zakázané položky v NSMenu.
- [ ] **T-159** (krit. „volba je v UI a uloží se“, ruční část macOS; sezení agenta nemělo displej): na záložce Nahrávání
      v řádku „Timelapse“ pole „Každých N sekund“ / „Každou N. vrstvu (odhad)“ se systémovým písmem nic neuřízne (ve všech
      jazycích to hlídá headless test), přepnutí vymění číslo vedle (interval ↔ počet vrstev), tip vysvětlí odhad z osy Z;
      VoiceOver přečte „Pořizovat snímek“ a u čísla „Vrstev mezi snímky“. Snímek `settings-recording.png` v
      `tests/LayerPeek.App.UiTests/Snapshots/macOS/` prohlédnout spolu s T-161.
- [ ] **T-161** (návrh): uživatel prohlédne `docs/design/timelapse-ui.md` a snímky `settings-recording.png` (zhuštěná sekce
      dashcamu, nová sekce Timelapse), `settings-camera.png`, `camera-timelapse.png`, `camera-mini-timelapse-buttons.png`
      v `tests/LayerPeek.App.UiTests/Snapshots/macOS/`; neblokující otázky v T-161; změny = nový úkol.
- [ ] **T-165** (krit. „UI akce …“, ruční část macOS; sezení agenta nemělo displej, `NSOpenPanel` se nedalo zobrazit):
      simulátor s kamerou, timelapse z T-161 hotový; v podmenu tiskárny v menu baru „Exportovat timelapse do JPEG…
      (experimentální)“ → volné okno Otevřít (`NSOpenPanel`, ne list u okna) se ukáže v popředí s fokusem i nad jinou aplikací,
      s titulkem „Vyberte timelapse k exportu do JPEG (experimentální)“, otevřené v `~/Movies/LayerPeek/<tiskárna>`, vybrat
      jdou jen `.mp4`. Výběr nahrávky (ne timelapse) → notifikace „Export timelapse do JPEG selhal / Soubor není timelapse
      z LayerPeeku“. Výběr timelapse → položka „Zrušit export do JPEG (x %)“ (procento roste i v otevřeném menu), pak složka
      `…_timelapse_jpg` s `0001.jpg`… (Náhled je otevře, pořadí jako ve videu), notifikace „Timelapse exportován do JPEG“.
      Druhý klik na položku, dokud je panel otevřený, nic nového neotevře; okno kamery během otevřeného panelu dál běží;
      zrušení uprostřed nezanechá žádnou složku (ani skrytou `.….partial`, `ls -a`).
- [ ] **T-164** (krit. „Akce v UI, průběh, notifikace …“, ruční část macOS; sezení agenta nemělo displej): simulátor
      s kamerou, hotová nahrávka tisku (T-145, ideálně s výpadkem kamery, tedy i `…-2.mp4`); v podmenu tiskárny v menu baru
      „Vytvořit timelapse z nahrávky… (experimentální)“ → volné okno Otevřít (`NSOpenPanel`) v popředí s titulkem „Vyberte
      nahrávku pro vytvoření timelapse (experimentální)“, otevřené v `~/Movies/LayerPeek/<tiskárna>`. Výběr nahrávky →
      položka „Zrušit timelapse z nahrávky (x %)“ (procento roste i v otevřeném menu), potom vedle nahrávky
      `…_recording_timelapse.mp4` (QuickTime Player ho přehraje, 30 fps, i část po výpadku), notifikace „Timelapse z nahrávky
      vytvořen“. Výběr timelapse → notifikace „Vytvoření timelapse z nahrávky selhalo / Soubor není nahrávka z LayerPeeku.“
      Zrušení uprostřed nezanechá žádný soubor (ani skrytý `.….partial`, `ls -a`); během vytváření jde otevřít dialog exportu
      do JPEG jen tehdy, když žádný jiný dialog není otevřený.

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
- [ ] **T-145** (automatické nahrávání tisku se skutečnou tiskárnou; doplněno 2026-11-04 před vydáním 0.5.0, ruční bod
      dosud chyběl): Core One s Buddy3D, „Automaticky nahrávat tisk“ zapnuté, okno kamery zavřené. S krátkým tiskem
      nahrávání začne samo (tooltip ikony „Nahrává se“, v podmenu tiskárny „Zastavit nahrávání“), soubor
      `~/Movies/LayerPeek/<tiskárna>/<datum>_<čas>_<název tisku>.mp4`, a skončí asi minutu po konci tisku. „Přehrát poslední
      nahrávku“ ho přehraje v QuickTime Playeru celý (obraz od začátku, posuvník, délka odpovídá). Během nahrávání je
      „Uložit posledních 2 min“ zakázané s tipem „Nahrává se celý tisk.“ (dashcam stojí). Další tisk dá nový soubor.
- [ ] **T-159** (timelapse po vrstvě se skutečnou tiskárnou; detektor ověřený jen na jednom záznamu Core One s vrstvou
      0,2 mm): Core One s Buddy3D, „Pořizovat timelapse tisku“ zapnuté, na záložce Nahrávání „Každou N. vrstvu (odhad)“ s N = 1,
      polling 5 s. Krátký díl (ideálně se slicerem hlášeným počtem vrstev a drobnými vrstvami nahoře, např. kužel nebo
      pyramida). Hotový `…_timelapse.mp4` má zhruba počet vrstev + 2 snímky (začátek a hotový díl; vrstvy kratší než asi dva
      dotazy se sloučí) – zapsat počet snímků a počet vrstev ze sliceru do poznámek T-159. Během tisku pauza (snímek pauzy
      nevznikne). Vedle běžet `~/LayerPeek-samples/tools/record-z.sh` a záznam s vrstvou 0,1 nebo 0,05 mm, se sekvenčním
      tiskem („complete objects“) a s výměnou nástroje MMU3 uložit do `~/LayerPeek-samples/z-recordings/` jako podklad dalšího
      ověření (T-159 otázky).
- [ ] **T-155** (krit. „Poplach z detektoru projde celou cestou“, ruční část se skutečnou tiskárnou a Buddy3D): Core One
      s výchozím výřezem (přední část podložky), zapnutá detekce (po T-154 v nastavení, do té doby v `settings.json`
      `detection.enabled` a `printers[*].detection.enabled` = `true`) a dashcam. (1) Celý běžný tisk včetně první vrstvy
      a pauzy: žádný poplach. (2) Inscenované selhání během tisku, např. na přední část podložky položit chomáč světlého
      filamentu nebo předmět ~2 cm: do ~1–3 min poplach (toast T-154 se snímkem), v `~/Movies/LayerPeek/<tiskárna>/` snímek
      `…_alarm-bed-change.jpg` a `…_dashcam-failure.mp4`; „Falešný poplach“ uloží záznam do `detection/false-alarms`.
      Výsledek (čas do poplachu, skóre z logu „Print failure suspected … score“) zapsat do poznámek T-155.
- [ ] **T-167** (experimentální detektor „Tisk do vzduchu“, doplněno 2026-11-05 před vydáním 0.5.0; automatické části
      a skutečné úspěšné tisky ověřil agent, skutečný tisk do vzduchu zatím nikdo nezaznamenal): podzáložka Detekce u
      Core One ukazuje „Tisk do vzduchu“ vypnutý se štítkem „Experimentální“ (tip „Experimentální funkce…“), u tiskárny MK4
      / MINI se nezobrazí (model zvolený pro obrázek). (1) Zapnout ho a nechat běžet celý běžný tisk aspoň ~15 mm vysokého
      dílu (tisk B): žádný poplach „Možná chyba tisku – Tisk do vzduchu“. (2) Volitelně, rozhodne uživatel (riziko pro
      tiskárnu malé, ale filament se může zamotat): inscenovaný tisk do vzduchu – krabička ~40 mm, po ~15 mm při vypnutém
      senzoru filamentu vytáhnout filament z hlavy (nebo ho odstřihnout nad podavačem) a nechat tisknout dál: poplach do
      ~3 mm Z (při 0,4 mm/min ~8–10 min), snímek `…_alarm-air-printing.jpg`. Výsledek (výška Z při selhání a při poplachu,
      skóre z logu „Print failure suspected … score“) a nahrávku nebo snímky po 5 s se záznamem Z zapsat do poznámek T-167
      (data mimo repozitář).
- [ ] **T-163** (režim „Tmavý filament“, experimentální; smoke test před vydáním 0.5.0): u tiskárny na podzáložce
      Detekce zaškrtnout „Tmavý filament“ (odznak „Experimentální“, vysvětlení v tooltipu), uložit, znovu otevřít nastavení:
      volba zůstala, v `settings.json` je `printers[*].detection.darkFilament: true` (vypnutá se nezapisuje). Celý běžný
      tisk (ideálně černým filamentem) s Core One a výchozím výřezem: žádný poplach; v logu při spuštění detekce žádná
      chyba. Volitelně inscenované selhání: chomáč černého filamentu na přední část plátu → zapsat, jestli a za jak dlouho
      přišel poplach (skóre z logu) do poznámek T-163.
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
- **Před vydáním 0.5.0** (architekt 2026-11-04, neblokuje tento úkol, rozhodne uživatel):
  1. Z jakého buildu testovat? `release-build.ps1` na `develop` dá aplikaci s verzí 0.4.0 a přepíše
     `artifacts/release/0.4.0` pracovního stromu. Varianta: build v samostatném worktree, nebo testovat až build větve
     `release/0.5.0` (pak ale `release.ps1` už vytvořil větev a čeká na push).
  2. Které body „doporučeného pořadí“ musí projít, aby šlo 0.5.0 vydat (např. jen dialogy souborů a notifikace), a co
     může zůstat na později?
  3. Má být i detekce chyb tisku v UI a v CHANGELOG označená jako experimentální (dnes má štítek jen timelapse)?
     Detektor je naladěný jen na syntetických selháních a na úspěšných tiscích, skutečné selhání zachycené kamerou
     zatím nemáme (T-156).

## Poznámky k implementaci
(vyplní agent / uživatel po ověření)
