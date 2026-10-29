# Ruční ověření na macOS (souhrnný postup)

Jeden průchod všemi kroky, které na Macu může ověřit jen člověk (GUI, Gatekeeper, oprávnění macOS, přihlášení, uspaný
displej, monitory). Kroky jsou posbírané z rozpracovaných macOS úkolů v `tasks/in-progress/` (T-095, T-053, T-018,
T-019, T-020, T-021, T-026, T-038, T-096, T-097, T-098, T-099, T-100), bez duplicit a seřazené do sezení tak, aby na sebe
navazovaly. Podrobnosti a pozadí jsou v uvedeném úkolu; tento soubor je jen pořadí a očekávání.

> Stav: sepsáno 2026-10-29 podle stavu úkolů k tomuto dni. Když úkol přidá nebo změní ruční krok, upraví se i tady.

## Než začnete

- **Mac:** Apple Silicon, macOS 15 nebo novější (dialog „Místní síť“ je od macOS 15), přihlášený uživatel s heslem
  správce. Pro sezení 6 externí monitor, pro krok 6.D5 dva stejné externí monitory (volitelné). Pro krok 10.1 druhý
  uživatelský účet (volitelné).
- **Nástroje:** .NET SDK z `global.json`, Xcode Command Line Tools, `pwsh` a Pester 5 (viz
  [prerequisites.md](prerequisites.md)). Terminál ve **fyzické cestě** k repozitáři (`echo $PWD` = `pwd -P`).
- **Tiskárna:** skutečná Prusa s PrusaLink (a kamerou Buddy3D) v místní síti; kde stačí, simulátor
  (`dotnet run --project tools/PrusaSimulator`, [simulator.md](simulator.md); vypíše adresu, jméno a heslo simulátoru,
  která nejsou tajná). **Dialog „Místní síť“ (sezení 4) se ukáže jen se zařízením v LAN, ne se simulátorem na
  `127.0.0.1`.**
- **Release aplikace používá skutečný profil:** data `~/Library/Application Support/LayerPeek`, logy
  `~/Library/Logs/LayerPeek`, předvolby `io.github.pfoltynek.LayerPeek`, Keychain služba `LayerPeek`. Pokud tam už máte
  data, která chcete zachovat, zálohujte je předem:
  ```bash
  ditto ~/Library/Application\ Support/LayerPeek ~/LayerPeek-zaloha/data
  ditto ~/Library/Logs/LayerPeek ~/LayerPeek-zaloha/logs
  ```
- **Bezpečnost:** hesla tiskáren, API klíče a heslo správce **nikdy nevkládejte do chatu** ani do úkolů. Heslo
  zadávejte jen do okna Nastavení LayerPeeku a do systémových dialogů macOS. Log ani `settings.json` hesla neobsahují,
  i tak před posláním výňatku zkontrolujte, že v něm nic citlivého není, a posílejte jen relevantní řádky.
- **Změny systému během testů** (jazyk, rozlišení, Dock, monitory, uspání displeje) dělá jen uživatel a po kroku je
  vrátí. `pmset displaysleepnow` jen uspí displej, nastavení napájení nemění. `caffeinate` drží displej probuzený jen
  po dobu běhu (ukončí ho Ctrl+C nebo `kill %1`).

### Kam hlásit výsledky
- Do úkolu, který krok uzavírá (uvedeno u každého kroku jako **Uzavírá**), do jeho poznámek (sekce „Výsledky ručního
  ověření“, pokud ji úkol má), s verzí macOS a datem. Zaškrtnutí kritéria nechte na agentovi / správci úkolu.
- Při chybě přiložte výňatek z `~/Library/Logs/LayerPeek/LayerPeek.log` (okno **O aplikaci → Otevřít složku s logy**;
  starší `LayerPeek.1.log` …). U E2E testů výstup `dotnet test`. Přesné texty systémových dialogů opište (nebo
  snímek obrazovky bez osobních údajů).

## Sezení 1 – Sestavení a instalace (Gatekeeper)

1. **Release sestavení.** LayerPeek nesmí běžet.
   ```bash
   cd -P /cesta/k/LayerPeek
   caffeinate -d -u -i pwsh scripts/release-build.ps1
   cd artifacts/release/0.3.0/dist && shasum -a 256 -c SHA256SUMS-macos.txt && cd -
   ```
   **Očekávání:** skript skončí kódem 0; v `artifacts/release/<verze>/dist/` jsou `LayerPeek-<verze>-osx-arm64.dmg`,
   `.tar.gz` a `SHA256SUMS-macos.txt`, kontrola součtů „OK“; bundle je v `artifacts/release/<verze>/app/osx-arm64/`.
   **Uzavírá:** podklad pro všechna další sezení (T-019 kritérium 2 jen s nahráním, viz sezení 11).
2. **Stažení prohlížečem (karanténa).** Aby Gatekeeper zasáhl jako u skutečného uživatele, musí `.dmg` přijít
   z prohlížeče. Buď z konceptu vydání na GitHubu (sezení 11), nebo místně:
   ```bash
   python3 -m http.server 8765 --bind 127.0.0.1 --directory artifacts/release/0.3.0/dist
   ```
   V Safari otevřít `http://127.0.0.1:8765/`, stáhnout `.dmg` i `.tar.gz`, server ukončit Ctrl+C. Kontrola:
   `xattr -p com.apple.quarantine ~/Downloads/LayerPeek-0.3.0-osx-arm64.dmg` musí něco vypsat (jinak stáhnout z GitHubu).
3. **Spuštění přímo z `.dmg`.** Otevřít `.dmg`, spustit LayerPeek z okna obrazu (ne přetažený). Zapsat přesné texty
   dialogu Gatekeeperu. Po povolení (krok 4) v Nastavení LayerPeeku → Spuštění a aktualizace: volba „Otevřít při
   přihlášení“ je **neaktivní s důvodem** (jen v Aplikacích). Aplikaci ukončit (menu → Ukončit).
   **Uzavírá:** T-019 kritérium 12 (část „z `.dmg`“).
4. **Instalace a Gatekeeper.** Z `.dmg` přetáhnout LayerPeek do Aplikace (případnou starší verzi nahradit), spustit
   z Finderu (ne z Terminálu). **Očekávání:** dialog „nelze ověřit“ jen s „Hotovo“ / „Přesunout do koše“ → „Hotovo“ →
   Nastavení systému → Soukromí a zabezpečení → dole „LayerPeek byl zablokován…“ → **Přesto otevřít** → heslo / Touch ID
   → Otevřít. Další spuštění už bez dotazu. Alternativu zkusit jen jednou na kopii: `xattr -dr com.apple.quarantine
   /Applications/LayerPeek.app`. Totéž s `.tar.gz` (rozbalit ve Finderu do jiné složky, spustit, pak smazat).
   **Uzavírá:** T-019 kritérium 12 (zapsat texty dialogů do T-019 a porovnat s `docs/release.md`, „Co uživatel uvidí
   na Macu“); T-026 kritérium 3 (čistý Mac: aplikace nepotřebuje Homebrew – ideálně na účtu nebo Macu bez Homebrew).

## Sezení 2 – Menu bar, Dock a ikony

1. **Bez Docku.** Spuštěná `/Applications/LayerPeek.app`: žádná ikona v Docku ani v přepínači ⌘Tab, ikona je v menu
   baru. **Uzavírá:** T-019 kritérium 7.
2. **Ikona v menu baru.** Ve světlém i tmavém vzhledu (Nastavení systému → Vzhled): bez tiskárny lomítko přes
   ztlumenou ikonu; s tiskárnou (simulátor: `--scenario` podle [simulator.md](simulator.md)) nečinná / ▶ s plněním
   vrstev / ❚❚ / ✓ / ! / ×. Ikona bílá na tmavém a černá na světlém menu baru, obrácená při otevřeném menu. Na externím
   monitoru bez Retiny (@1x) ostrá (při rozmazání zapsat – záloha je vícerozlišovací obrázek). **Uzavírá:** T-018
   kritérium 1.
3. **Menu a tooltip.** Najetí: tooltip se stavem tiskárny. Klik: menu (tiskárny, Diagnostika…, Nastavení…, O aplikaci,
   Ukončit), položky se mění se stavem. „Ukončit“ aplikaci ukončí a ikona zmizí. **Uzavírá:** T-018 kritérium 3.
4. **Okna do popředí.** Aktivní jiná aplikace (Finder s oknem): z menu Nastavení → okno je nahoře a psaní jde do něj.
   Totéž s oknem už otevřeným a schovaným za jinou aplikací, a pro Diagnostiku, O aplikaci a okno kamery.
   **Uzavírá:** T-019 kritérium 8.
5. **Ikona aplikace.** Ve Finderu (Aplikace, zobrazení ikon), v Launchpadu / Aplikacích a v Nastavení systému (např.
   Soukromí a zabezpečení, Položky při přihlášení) vedle systémových aplikací: stejně velká, macOS 26+ ji nezavře do
   šedého čtverce. Později zkontrolovat i ikonu v oznámeních (sezení 5) a v dialogu Místní síť (sezení 4).
   **Uzavírá:** T-098 kritérium 2.
6. **Odkazy.** O aplikaci → web projektu a odkazy licencí se otevřou ve výchozím prohlížeči; log nemá „Opening …
   failed“. Licence třetích stran: texty FFmpeg pro macOS bez „BtbN“ / „DLL“, odkazy na ffmpeg.org, tarball 9.0.2 a
   skript buildu (tag verze); plný text LGPL 3 se načte. **Uzavírá:** T-053 kritérium L4 (část b, kontrola `PATH` je
   v sezení 10), T-026 krok 4.

## Sezení 3 – Nastavení a Keychain

1. **Testy Keychainu na odemčeném Macu** (v přihlášené GUI relaci je login Keychain odemčený; při práci agenta byl
   zamčený, testy se zápisem se proto přeskočily):
   ```bash
   pwsh tools/profile-snapshot.ps1 -Save /tmp/lp-profile.json
   dotnet test --project tests/LayerPeek.App.Tests -- --filter-class "*KeychainSecretStoreTests"
   pwsh tools/profile-snapshot.ps1 -Compare /tmp/lp-profile.json
   ```
   **Očekávání:** 0 selhání a **0 přeskočených** (žádný důvod „login Keychain is locked“); kontrola profilu „No change“;
   v aplikaci Klíčenka nezůstala žádná položka `LayerPeek.Tests…`. Testy dialogy Keychainu samy vypínají; kdyby se
   přesto nějaký objevil, zvolit Zamítnout a zapsat. **Uzavírá:** T-021 kritéria round-trip, přepis, velikost písmen,
   izolace, úklid, kontrola před/po, „zprávy bez tajného údaje“.
2. **Heslo v produkční aplikaci.** V Nastavení přidat tiskárnu s heslem (PrusaLink), Uložit. V Klíčence: položka
   služby `LayerPeek`, účet velkými písmeny (např. `PRINTER/…`). Diagnostika tiskárny se připojí. **Uzavírá:** T-021
   (podklad pro dialog po aktualizaci, sezení 9).
3. **Nastavení na MacBooku 13".** Postup T-038 krok **J7** (sezení 6, provést klidně tady): všechny záložky bez
   posuvníku a bez uříznutého textu při „Vypadá jako“ 1440 × 900 / 1470 × 956, čeština a němčina nebo francouzština.

## Sezení 4 – Tiskárna a Místní síť

Aplikaci spouštět z Finderu (procesy z Terminálu mají přístup k místní síti povolený automaticky).

1. **Dialog v jazyce systému.** Při prvním spojení s tiskárnou nebo kamerou v LAN macOS ukáže dialog „Místní síť“
   s textem LayerPeeku v jazyce systému. Zkusit aspoň češtinu a angličtinu (Nastavení systému → Obecné → Jazyk a oblast,
   nebo jen pro LayerPeek: Aplikace → „+“ → LayerPeek). Zapsat přesné znění. **Uzavírá:** T-097 kritérium 3.
2. **Zamítnutí.** Zvolit „Nepovolovat“: aplikace nespadne; Diagnostika, Otestovat připojení, test kamery a okno kamery
   ukážou hlášku o místní síti; v logu „Local Network access for LayerPeek may be off“. (Ověří i předpoklad, že .NET
   a FFmpeg dostanou EHOSTUNREACH, ne timeout.) **Uzavírá:** T-097 kritérium 4.
3. **Tlačítko Otevřít Nastavení systému** v Diagnostice, pod „Otestovat připojení“ a pod testem kamery. Očekávání:
   Soukromí a zabezpečení → **Místní síť**. Možné výsledky: (a) rovnou Místní síť; (b) jen Soukromí a zabezpečení –
   pak v Terminálu zkusit `open "x-apple.systempreferences:com.apple.preference.security?Privacy_LocalNetwork"` a
   `open "x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension?privacy-localnetwork"` a zapsat, který
   funguje; (c) nic – v logu „Opening System Settings > Privacy & Security failed.“. **Uzavírá:** T-097 krok 6.
4. **Povolení.** Soukromí a zabezpečení → Místní síť → LayerPeek zapnout: spojení se obnoví samo. **Uzavírá:** T-097
   krok 3, T-026 krok 2.
5. **Prvky AppKitu v jiném jazyce.** Jazyk LayerPeeku jiný než jazyk systému (např. systém čeština, LayerPeek
   angličtina v Nastavení → Obecné): dialog výběru obrázku tiskárny, kontextové menu textového pole. Prvky AppKitu mají
   být v jazyce systému, ostatní v jazyce LayerPeeku; jen zapsat, co se ukázalo. **Uzavírá:** T-097 krok 5.

## Sezení 5 – Oznámení

1. **E2E testovací aplikace** (vývojové sestavení, vlastní identifikátor `…LayerPeek.E2E`):
   ```bash
   caffeinate -d -u -t 900 &
   LAYERPEEK_E2E=1 dotnet test --project tests/LayerPeek.App.Tests -- --filter-class "*MacNotificationsE2ETests"
   ```
   Při prvním běhu povolit oznámení „LayerPeek E2E“ (oznámení s dotazem → Volby → Povolit, nebo Nastavení systému →
   Oznámení → LayerPeek E2E → Povolit oznámení; položka už může existovat jako zamítnutá) a test spustit znovu.
   **Očekávání:** test projde (doručení, odstranění při ukončení, odstranění režimem úklidu po zabití). **Uzavírá:**
   T-020 kritéria 1 a 5.
2. **Klik na oznámení.** Během testu z kroku 1 (nebo v produkční aplikaci, krok 3) kliknout na banner i na oznámení
   v Centru oznámení: otevře se web tiskárny (v testovacím režimu se adresa jen zapíše do `opened-urls.txt` v datové
   složce testu). Volitelně klik, když aplikace neběží (macOS ji spustí). **Uzavírá:** T-020 kritérium 2.
3. **Produkční aplikace.** `/Applications/LayerPeek.app` s tiskárnou, vyvolat událost (pauza / dokončení tisku,
   simulátor). macOS se při prvním oznámení zeptá; po povolení banner s názvem tiskárny, zvuk podle nastavení, ikona
   LayerPeeku (T-098). Pak Nastavení systému → Oznámení → LayerPeek vypnout a v Nastavení LayerPeeku → Obecné: červená
   hláška a tlačítko „Otevřít nastavení oznámení“, které otevře právě tuto stránku; po zapnutí a návratu do okna hláška
   zmizí. **Uzavírá:** T-020 kritéria 1 a 3.

## Sezení 6 – Kamera a monitory

Předpoklad: tiskárna s kamerou (Buddy3D v LAN, nebo kamera simulátoru `rtsp://127.0.0.1:8554/live`).

1. **Stream ve všech režimech.** Okno kamery z menu: obraz běží v normálním okně, na celé obrazovce i v malém náhledu,
   překryv stavu tisku je vidět ve všech třech. **Uzavírá:** T-026 kritéria 1 a 3 (s krokem 1.4 na čistém Macu).
2. **Umístění, monitor, „vždy navrchu“, celá obrazovka** – kroky z [T-038](../tasks/in-progress/T-038-macos-camera-window-placement.md)
   („Ruční ověření“), stručně:
   - **Jeden monitor:** J1 název a rozlišení monitoru v bodech (stejné po restartu aplikace); J2 rohy a střed pro
     normální okno i náhled 16 bodů od kraje pracovní plochy, nikdy pod menu barem / Dockem (Dock dole, vlevo, skrytý),
     posun okna po otevření ano / ne; J3 náhled bez rámečku, přesun tažením, **změna velikosti všemi 8 hranami a rohy**,
     výška podle poměru stran po uvolnění; J4 „vždy navrchu“ nad Finderem / Safari, **náhled nad aplikací na celé
     obrazovce a na jiné ploše – zapsat, co se stane**; J5 celá obrazovka (F11 / dvojklik / tlačítko, vlastní plocha,
     Esc, zelené tlačítko), **z celé obrazovky rovnou do náhledu** a „Použít“ během celé obrazovky – zapsat velikost
     a polohu náhledu; J6 zapamatování polohy po restartu; J7 nastavení na MacBooku 13" (sezení 3).
   - **Dva monitory:** D1 okno, náhled i celá obrazovka na zvoleném externím monitoru; D2 monitor nad / vlevo; D3
     „Displeje mají samostatné plochy“ zapnuto / vypnuto; D4 odpojení (hlavní monitor, „– nepřipojený“) a návrat,
     restart Macu; D5 dva stejné monitory „(1)“ / „(2)“, prohození kabelů a restart → stejný fyzický monitor, vypnutý
     zvolený se nenahradí dvojčetem; D6 `grep -n '"devicePath"' ~/Library/Application\ Support/LayerPeek/settings.json`
     ukáže UUID; D7 náhled mezi Retinou a monitorem @1x.
   **Uzavírá:** T-038 kritérium 1 (a otázky 1–3 v T-038), T-026 krok 1 (vždy navrchu, celá obrazovka, bez rámečku,
   tažení a změna velikosti).
3. **Klávesové zkratky podle Applu (T-100)** – na vestavěné klávesnici Macu i na externí (např. Logitech). Pozor:
   horní řada posílá na Macu mediální funkce, F-klávesy jdou jen s `fn` (u Logitechu s Fn Lock / Fn+Esc), a F11 má
   macOS ve výchozím stavu pro „Zobrazit plochu“ – proto se k aplikaci nedostane. Postup:
   - **Okno kamery:** ⌃⌘F přepne na celou obrazovku a zpět; Esc i ⌘. vrátí z celé obrazovky (a z malého náhledu do
     normálního okna); ⌘W zavře okno ve všech třech režimech (aplikace běží dál); v režimu úprav rozvržení ⌘. zahodí
     změny jako Esc. Tooltip tlačítka celé obrazovky ukazuje „⌃⌘F“, ne „F11“ (zkusit i jiný jazyk aplikace). F11 jako
     záloha: jen pokud ji macOS nezabere (`fn`+F11 obvykle ukáže plochu – zapsat, co se stane).
   - **Nastavení:** ⌘⇧] / ⌘⇧[ přepíná hlavní záložky (dokola), ⌘⌥→ / ⌘⌥← záložky tiskárny (Připojení, Kamera, Okno
     kamery); ⌃Tab a ⌃⇧Tab fungují dál. Upravit jméno tiskárny a stisknout ⌘W (a v dalším pokusu ⌘.): okno se zavře
     jako Zrušit, změna se neuloží. Enter = Uložit, Esc = Zrušit beze změny.
   - **Diagnostika, O aplikaci, Licence:** ⌘W i ⌘. okno zavře (Licence zavřou jen sebe, O aplikaci zůstane).
   - **Jiné rozložení klávesnice:** totéž s **českým** rozložením (Nastavení systému → Klávesnice → Zdroje vstupu):
     ⌘⇧] / ⌘⇧[ = klávesy vpravo od P (na české klávesnici „ú“ a „)“); zapsat, zda přepínají záložky a zda se nestane
     něco jiného. ⌃⌘F, ⌘W a ⌘. zkusit také.
   **Uzavírá:** T-100 kritérium 4 (a otevřené otázky v T-100).

## Sezení 7 – Otevřít při přihlášení, odhlášení a přihlášení

1. **Zapnutí.** V `/Applications/LayerPeek.app` → Nastavení → Spuštění a aktualizace → „Otevřít při přihlášení“
   zapnout, Uložit. **Očekávání:** v Nastavení systému → Obecné → Položky při přihlášení (a rozšíření) je LayerPeek
   (macOS může ukázat oznámení o přidané položce).
2. **Odhlášení a přihlášení:** LayerPeek se spustí sám (ikona v menu baru, log s novým startem).
3. **Vypnutí v Nastavení systému.** Položku LayerPeek tam vypnout a vrátit se do okna Nastavení LayerPeeku: na
   záložce Spuštění a aktualizace hint „LayerPeek je vypnutý v Nastavení systému.“ a tlačítko „Otevřít Nastavení
   systému“, které otevře Položky při přihlášení. Zkusit volbu zapnout a Uložit: hláška „Nastavení je uloženo.
   Otevírání při přihlášení musíte povolit v Nastavení systému.“. Po povolení v Nastavení systému a návratu do okna
   hint zmizí a volba je zaškrtnutá; další Uložit projde. Zapsat, jaký stav macOS po vypnutí hlásí (hint vidět =
   `requiresApproval`; volba jen nezaškrtnutá bez hintu = spíš `notRegistered`).
4. **Vypnutí v LayerPeeku:** volbu vypnout, Uložit → položka z Nastavení systému zmizí.
   **Uzavírá:** T-019 kritérium 9 (a otázka 11 v T-019).

## Sezení 8 – Uspaný displej při startu

LayerPeek nesmí běžet; tiskárna nastavená (pro krok 3 se simulátorem nebo skutečným tiskem).

1. **Start během spánku displeje.**
   ```bash
   sleep 10; open -a /Applications/LayerPeek.app
   ```
   a během 10 s v druhém okně Terminálu `pmset displaysleepnow`. Počkat ~30 s, displej probudit.
   **Očekávání:** ikona v menu baru do ~1 s po probuzení; v logu „No active display …“ a „… active display(s) after …;
   starting the UI.“. **Uzavírá:** T-096 kritérium 1.
2. **Sledování během čekání.** Stejný postup, ale během ~1–2 min spánku displeje nechat proběhnout událost tisku
   (pauza / dokončení, simulátor). **Očekávání:** oznámení přijde ještě se zhasnutým displejem (po probuzení v Centru
   oznámení); v logu navíc „Starting printer monitoring and notifications while waiting for an active display.“ a po
   probuzení „The menu bar item attaches to the monitoring that already runs.“; ikona hned ukazuje stav tiskárny (ne
   „Spouštění…“). **Uzavírá:** T-099 kritéria 2 a 4.
3. **Ukončení během čekání** (volitelné, přes SSH z jiného zařízení, displej dál uspaný): `pkill -TERM LayerPeek` →
   v logu „Asked to quit … the UI was not started.“, proces skončí do ~5 s s kódem 0. **Uzavírá:** T-096 krok 4,
   T-099 krok 3.

## Sezení 9 – Aktualizace přes stávající instalaci (ad-hoc podpis)

1. Udělat malou změnu nebo jen znovu sestavit (nový ad-hoc podpis): `caffeinate -d -u -i pwsh scripts/release-build.ps1`,
   ukončit LayerPeek, novou `LayerPeek.app` z `.dmg` přetáhnout přes starou v Aplikacích, spustit.
2. **Zapsat:** (a) ptá se macOS znovu na Místní síť (T-097 krok 4)? (b) zůstalo povolení oznámení (T-020 krok 4)?
   (c) při prvním čtení hesla tiskárny dialog Klíčenky „LayerPeek chce použít důvěrné informace…“ (Povolit / Vždy
   povolit / Zamítnout)? Zvolit „Povolit“ a zapsat, jestli se objeví znovu při dalším spuštění (T-021 otázka k podpisu).
   **Uzavírá:** T-097 krok 4, T-020 krok 4, T-021 (riziko ad-hoc podpisu).

## Sezení 10 – Vývojové sestavení (T-053)

1. **Jiný uživatel (volitelné, potřebuje druhý účet `other`).**
   ```bash
   LAYERPEEK_TEST_DATA_DIR=/tmp/lp-t053 LAYERPEEK_INSTANCE_SUFFIX=t053 dotnet run --project src/LayerPeek.App
   # v jiném okně:
   chmod 755 /tmp/lp-t053 && chmod 777 /tmp/lp-t053/instance-t053.sock
   sudo -u other /usr/bin/nc -U /tmp/lp-t053/instance-t053.sock    # napsat: activate
   ```
   **Očekávání:** spojení se hned zavře, v `/tmp/lp-t053/logs/LayerPeek.log` „Refused a connection of another user
   (uid …)“, žádné okno se neaktivuje. **Uzavírá:** T-053 kritérium 1.
2. **`/usr/bin/open` se nehledá v `PATH`.**
   ```bash
   mkdir -p "$TMPDIR/fakebin" && printf '#!/bin/sh\ntouch "$TMPDIR/fakebin/HIT"\n' > "$TMPDIR/fakebin/open" && chmod +x "$TMPDIR/fakebin/open"
   dotnet run --project tools/PrusaSimulator          # v jiném okně
   PATH="$TMPDIR/fakebin:$PATH" LAYERPEEK_TEST_DATA_DIR="$TMPDIR/lp-t053b" LAYERPEEK_INSTANCE_SUFFIX=t053b dotnet run --project src/LayerPeek.App
   ```
   V Nastavení přidat tiskárnu simulátoru. „Otevřít web“ z menu tiskárny otevře prohlížeč na proxy
   `http://127.0.0.1:<port>/…` a web PrusaLink je přihlášený; odkazy v O aplikaci se otevřou. Soubor
   `$TMPDIR/fakebin/HIT` nevznikl, log nemá „Opening … failed“. **Uzavírá:** T-053 kritérium L4.

## Sezení 11 – Nahrání do konceptu vydání (volitelné, zapisuje na GitHub)

Jen ve zkušebním repozitáři podle [release.md](release.md), „Zkušební vydání (testovací repozitář)“: v čistém klonu na
otagovaném commitu `pwsh scripts/release-build.ps1`, `pwsh scripts/release-upload.ps1 -Repo <owner>/LayerPeek-release-trial -DryRun`,
pak bez `-DryRun` do konceptu z běhu na Windows; na GitHubu `.dmg`, `.tar.gz` a `SHA256SUMS-macos.txt` vedle souborů
Windows; `release-publish.ps1 -Repo … -DryRun` bez `-AllowIncomplete` projde. Stažení `.dmg` z konceptu je zároveň
nejvěrnější varianta kroku 1.2. **Uzavírá:** T-019 kritérium 2.

## Sezení 12 – Úklid

1. LayerPeek → Nastavení → „Otevřít při přihlášení“ vypnout a Uložit (položka zmizí z Položek při přihlášení);
   LayerPeek ukončit (menu → Ukončit). Simulátor ukončit (Ctrl+C), `caffeinate` ukončit (`kill %1`).
2. Aplikaci a stažené soubory smazat: `/Applications/LayerPeek.app` do koše, `~/Downloads/LayerPeek-*`, rozbalená
   kopie z `.tar.gz`.
3. Profil (jen pokud data nemají zůstat; jinak obnovit zálohu z „Než začnete“):
   ```bash
   defaults delete io.github.pfoltynek.LayerPeek
   defaults delete io.github.pfoltynek.LayerPeek.E2E 2>/dev/null
   rm -rf ~/Library/Application\ Support/LayerPeek ~/Library/Logs/LayerPeek
   rm -rf /tmp/lp-t053 "$TMPDIR/lp-t053b" "$TMPDIR/fakebin" /tmp/lp-profile.json
   ```
4. Klíčenka: položky služby `LayerPeek` (hesla tiskáren z kroku 3.2) smazat v aplikaci Klíčenka (hledat „LayerPeek“),
   nebo `security delete-generic-password -s LayerPeek -a '<ÚČET VELKÝMI PÍSMENY>'`. Položky `LayerPeek.Tests…`
   nesmí existovat (testy je uklízí).
5. Známé pozůstatky, které skriptem odstranit nejde a nevadí: položka „LayerPeek E2E“ (a „LayerPeek“) v Nastavení
   systému → Oznámení, LayerPeek v Soukromí a zabezpečení → Místní síť (lze vypnout přepínačem).
6. Vrátit změny systému: jazyk, „Vypadá jako“ u displeje, poloha a skrývání Docku, „Displeje mají samostatné plochy“,
   uspořádání monitorů. Případně obnovit zálohu dat: `ditto ~/LayerPeek-zaloha/data ~/Library/Application\ Support/LayerPeek`.
7. Kontrola: `pwsh tools/profile-snapshot.ps1 -Save` před sezeními a `-Compare` po úklidu ukáže jen to, co mělo
   zůstat (s úplným úklidem „No change“, Login Items bez autorizace „not checked“).

## Mimo Mac (připomínka)
Na Windows zbývá ověřit `dotnet build` a `dotnet test` po macOS úkolech (T-095 kritérium Windows, T-018 kritérium 4,
T-026, T-053, T-097, T-099 „Windows beze změny“) – mimo tento postup.
