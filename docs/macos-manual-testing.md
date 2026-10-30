# Ruční ověření na macOS (souhrnný postup)

Jeden průchod všemi kroky, které na Macu může ověřit jen člověk (GUI, Gatekeeper, oprávnění macOS, přihlášení, uspaný
displej, monitory). Kroky jsou posbírané z macOS úkolů (T-095, T-053, T-018, T-019, T-020, T-021, T-026, T-038, T-096,
T-097, T-098, T-099, T-100, T-101, T-102), bez duplicit a seřazené do sezení tak, aby na sebe navazovaly. Podrobnosti
a pozadí jsou v uvedeném úkolu; tento soubor je jen pořadí a očekávání.

Od 2026-10-30 se zbývající body odškrtávají ve sběrném úkolu
[T-115 „Ruční ověření na Macu“](../tasks/todo/T-115-mac-manual-verification.md) (rozhodnutí uživatele); původní úkoly
jsou uzavřené, kromě T-018, T-019, T-038 a T-100, které čekají na odpovědi na otevřené otázky. Protějšek pro Windows je
T-114 ([windows-manual-testing.md](windows-manual-testing.md)).

> Stav: sepsáno 2026-10-29 podle stavu úkolů k tomuto dni, odkazy na T-114 / T-115 doplněny 2026-10-30. Když úkol
> přidá nebo změní ruční krok, upraví se i tady.

## Nejdřív skript `tools/macos-verify.ps1` (T-105)

Většinu kroků níže dělá skript sám, ostatní provede s otázkami ano / ne. Spouští ho uživatel ve **vlastním Terminálu**
(ne agent: v sandboxu agenta je login Keychain zamčený, oprávnění TCC nejdou udělit, displej nejde uspat a nefunguje
`sudo`). Displej musí být probuzený, proto pod `caffeinate`:

```bash
cd -P /cesta/k/LayerPeek
pwsh tools/macos-verify.ps1 -ListChecks                         # seznam kontrol a kritérií, která uzavírají
caffeinate -d -u -i pwsh tools/macos-verify.ps1                 # vše, interaktivně (ptá se před každým oprávněním)
caffeinate -d -u -i pwsh tools/macos-verify.ps1 -Unattended     # jen to, co běží bez vás (nic se neptá, nic nepovoluje)
caffeinate -d -u -i pwsh tools/macos-verify.ps1 -Only mac.keychain,mac.notifications
```

- **Výsledky:** `PASS` (ověřeno automaticky), `FAIL`, `SKIP` (vždy s důvodem, např. chybí `.dmg` nebo spí displej),
  `MANUAL` (potvrdili jste „y“; „n“ je `FAIL`). Kód 1 při jakémkoli `FAIL`. Zpráva `report.md` a `report.json`
  s commitem, verzí aplikace a důkazy (výstupy příkazů, logy testovací aplikace, JSON oken, obrázek ikon) je
  v `artifacts/verify/<čas>/`. Výsledek kontroly zapište do úkolu, který uvádí sloupec „Uzavírá“.
- **Izolace:** automatické kontroly používají Debug aplikaci v testovacím režimu jako „LayerPeek E2E.app“ (vlastní
  identifikátor `…LayerPeek.E2E`, sestavená v `artifacts/verify-bundle/`) a simulátor na `127.0.0.1`; produkční aplikaci,
  její profil ani oprávnění nepoužívají. Na začátku skript uloží stav profilu (`tools/profile-snapshot.ps1`) a po úklidu
  ho porovná (`mac.profile-unchanged`; Login Items jen na vyžádání, `sfltool` chce heslo správce). Úklid na konci:
  testovací aplikace, notifikace testovacího bundlu (`--layerpeek-e2e-clear-toasts`), náhrady Login Item v testovacích
  složkách (`--layerpeek-e2e-clear-autostart`), registrace v LaunchServices, předvolby a cache `…LayerPeek.E2E`, položky
  Klíčenky s prefixem `LayerPeek.Tests`, dočasné složky. Řízené ruční kroky s `/Applications/LayerPeek.app` nabídnou
  dočasné odsunutí profilu LayerPeek (žurnál `scripts/aot-smoke.ps1`, obnova i `pwsh scripts/aot-smoke.ps1 -RestoreOnly`).
- **Známé pozůstatky:** položka „LayerPeek E2E“ v Nastavení systému → Oznámení; Terminál v Soukromí a zabezpečení →
  Zpřístupnění, pokud jste ho povolili pro `mac.keys` (po běhu vypněte).

| Kontrola | Druh | Uzavírá | Sezení níže |
|---|---|---|---|
| `mac.build` | automaticky | T-018 krit. 4 (build na macOS), T-053 krit. 6 (část) | – |
| `mac.test` | automaticky | T-018 krit. 4 (testy na macOS), T-095 (kontrola) | – |
| `mac.aot-smoke` | automaticky | T-053 krit. 6 (AOT publish `osx-arm64` bez varování) | – |
| `mac.keychain` | automaticky | T-021 krit. 1–5 a 8 (přeskočený test = `FAIL`), krit. 7 s `mac.profile-unchanged` | 3.1 |
| `mac.dock` | automaticky | T-019 krit. 7 (bez Docku, `lsappinfo` typ `UIElement`) | 2.1 |
| `mac.menubar` | automaticky | T-019 krit. 7 (ikona v menu baru), T-018 krit. 3 (část: Ukončit) | 2.1, 2.3 |
| `mac.settings-front` | automaticky | T-019 krit. 8 (Nastavení, O aplikaci, Diagnostika a další spuštění nad Finderem, `lsappinfo front`) | 2.4 |
| `mac.plist-localnetwork` | automaticky | T-097 krit. 3 (podklad: text každého jazyka, jak ho vybere NSBundle) | 4.1 |
| `mac.gatekeeper` | automaticky | T-019 krit. 12 (část: `spctl` odmítne kopii z `.dmg` s karanténou; potřebuje `release-build.ps1`) | 1.4 |
| `mac.notifications` | automaticky* | T-020 krit. 1 a 5 (E2E test bundlu) | 5.1 |
| `mac.display-wait` | automaticky | T-096 krit. 1 a T-099 krit. 1 (podklad: simulované čekání) | – |
| `mac.camera` | automaticky | T-026 krit. 1 (část: okno a trvalé RTSP spojení v normálním, malém a celoobrazovkovém režimu) | 6.1 |
| `mac.camera-placement` | automaticky | T-038 krit. 1 (část: J2 rohy a střed 16 bodů od pracovní plochy, D1 zvolený monitor; každý připojený monitor) | 6.2 |
| `mac.keys` | oprávnění (Zpřístupnění pro Terminál) | T-100 krit. 4 (část: ⌃⌘F, Esc, ⌘., ⌘W, ⌘⇧] jako skutečné události) | 6.3 |
| `mac.socket-other-user` | oprávnění (`sudo`) | T-053 krit. 1 (spojení uživatele `nobody` se odmítne) | 10.1 |
| `mac.display-sleep` | oprávnění (uspání displeje) | T-096 krit. 1, T-099 krit. 1 a 3 | 8.1, 8.2 |
| `mac.icons` | ručně (obrázek) | T-018 krit. 1, T-098 krit. 2 (ikony v menu baru světlé / tmavé, @1x; ikona vedle systémových) | 2.2, 2.5 |
| `mac.menu` | ručně | T-018 krit. 1 a 3 (stavy ikony, tooltip, menu; Ukončit ze skutečného menu) | 2.2, 2.3 |
| `mac.camera-look` | ručně | T-026 krit. 1 (obraz a překryv ve všech režimech, simulátor) | 6.1 |
| `mac.camera-buddy3d` | ručně | T-026 krit. 1 (skutečná kamera Buddy3D; zadáte adresu RTSP) | 6.1 |
| `mac.notification-click` | ručně | T-020 krit. 2 (klik na doručenou notifikaci testovacího bundlu otevře web) | 5.2 |
| `mac.gatekeeper-dialog` | ručně | T-019 krit. 12 (texty dialogů), T-026 krit. 3 (podklad) | 1.2–1.4 |
| `mac.login-item` | ručně, dva běhy | T-019 krit. 9 (po odhlášení a přihlášení) | 7 |
| `mac.local-network` | ručně | T-097 krit. 3 a 4 (dialog Místní síť se skutečnou tiskárnou v LAN) | 4 |
| `mac.notifications-production` | ručně | T-020 krit. 1 (ze sestavené `.app`) a 3 (hláška při vypnutých notifikacích) | 5.3 |
| `mac.keyboard-physical` | ručně | T-100 krit. 4 (vestavěná a Logitech klávesnice, české rozložení) | 6.3 |
| `mac.profile-unchanged` | automaticky, po úklidu | T-021 krit. 7, T-095 (kontrola před/po) | 12.7 |

\* `mac.notifications` běží bez dotazu jen tehdy, když jsou notifikace „LayerPeek E2E“ už povolené; jinak se v
interaktivním běhu zeptá (macOS pak jednou položí svou otázku), v `-Unattended` je `SKIP`.

**Zůstává jen ručně podle sezení níže** (skript na ně jen odkáže nebo se zeptá): stažení `.dmg` prohlížečem a texty
Gatekeeperu (sezení 1), odkazy a licence v O aplikaci (2.6), heslo v produkční aplikaci a Nastavení na MacBooku 13"
(3.2, 3.3), tlačítko „Otevřít Nastavení systému“ a prvky AppKitu v jiném jazyce (4.3, 4.5), kroky J1, J3–J7 a D2–D7
okna kamery (6.2), aktualizace přes stávající instalaci (sezení 9), `/usr/bin/open` a `PATH` (10.2), nahrání do konceptu
vydání (sezení 11). Skript nic z toho neoznačí jako hotové.

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
- Do poznámek sběrného úkolu T-115 (bod podle úkolu a kritéria, které krok uzavírá – uvedeno u každého kroku jako
  **Uzavírá**), s verzí macOS a datem. U T-018, T-019, T-038 a T-100 i do poznámek původního úkolu (výsledek pomůže
  rozhodnout jejich otevřené otázky). Zaškrtnutí nechte na agentovi / správci úkolu.
- Při chybě přiložte výňatek z `~/Library/Logs/LayerPeek/LayerPeek.log` (okno **O aplikaci → Otevřít složku s logy**;
  starší `LayerPeek.1.log` …). U E2E testů výstup `dotnet test`. Přesné texty systémových dialogů opište (nebo
  snímek obrazovky bez osobních údajů).

## Sezení 1 – Sestavení a instalace (Gatekeeper)

1. **Release sestavení.** LayerPeek nesmí běžet.
   ```bash
   cd -P /cesta/k/LayerPeek
   caffeinate -d -u -i pwsh scripts/release-build.ps1
   cd artifacts/release/0.3.1/dist && shasum -a 256 -c SHA256SUMS-macos.txt && cd -
   ```
   **Očekávání:** skript skončí kódem 0; v `artifacts/release/<verze>/dist/` jsou `LayerPeek-<verze>-osx-arm64.dmg`,
   `.tar.gz` a `SHA256SUMS-macos.txt`, kontrola součtů „OK“; bundle je v `artifacts/release/<verze>/app/osx-arm64/`.
   **Uzavírá:** podklad pro všechna další sezení (T-019 kritérium 2 jen s nahráním, viz sezení 11).
2. **Stažení prohlížečem (karanténa).** Aby Gatekeeper zasáhl jako u skutečného uživatele, musí `.dmg` přijít
   z prohlížeče. Buď z konceptu vydání na GitHubu (sezení 11), nebo místně:
   ```bash
   python3 -m http.server 8765 --bind 127.0.0.1 --directory artifacts/release/0.3.1/dist
   ```
   V Safari otevřít `http://127.0.0.1:8765/`, stáhnout `.dmg` i `.tar.gz`, server ukončit Ctrl+C. Kontrola:
   `xattr -p com.apple.quarantine ~/Downloads/LayerPeek-0.3.1-osx-arm64.dmg` musí něco vypsat (jinak stáhnout z GitHubu).
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

1. **Bez Docku.** Spuštěná `/Applications/LayerPeek.app` bez otevřeného okna: žádná ikona v Docku ani v přepínači
   ⌘Tab, ikona je v menu baru (s otevřeným oknem se ikona v Docku objeví, krok 7). **Uzavírá:** T-019 kritérium 7.
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

7. **Ikona v Docku s otevřeným oknem (T-102).** Předpoklad: tiskárna s kamerou (sezení 6), výchozí volby okna kamery
   (normální okno ne navrchu, malý náhled navrchu). Po každém kroku zapsat, zda je ikona LayerPeeku v Docku a v ⌘Tab:
   - Bez okna: ne. Otevřít Nastavení → ano (okno zůstane vpředu s fokusem, žádné jiné okno se nepřeskládá). Zavřít →
     ikona zmizí, fokus dostane jiná aplikace.
   - Totéž pro Diagnostiku, O aplikaci (a Licence z ní) a okno kamery v normálním okně i na celé obrazovce.
   - Okno kamery → malý náhled: ikona zmizí, náhled zůstane navrchu a dál reaguje (dvojklik zpět) – zapsat, zda
     aplikace mezitím neztratila fokus. Náhled → normální okno: ikona se vrátí; zkontrolovat, že menu aplikace v menu
     baru („LayerPeek“) jde otevřít (známá vlastnost AppKitu: po přepnutí na běžnou aplikaci se menu bar někdy obnoví
     až po přepnutí na jinou aplikaci a zpět).
   - Nastavení → Okno kamery: „Vždy navrchu“ u normálního okna zapnout a Použít/Uložit s otevřeným oknem kamery (jiné
     okno zavřené) → ikona zmizí; vypnout → vrátí se. U malého náhledu „Vždy navrchu“ vypnout → náhled má ikonu v Docku.
   - Okno kamery schovat za Safari: klik na ikonu v Docku → okna LayerPeeku vpředu (minimalizované okno se obnoví,
     z celé obrazovky zpět na celou obrazovku). Pravý klik na ikonu v Docku: seznam oken s názvy („Alpha – kamera“,
     „Nastavení“…; malý náhled bez rámečku tam být nemusí – zapsat).
   - Menu v menu baru → tiskárna s otevřeným oknem kamery: „Zobrazit kameru“ nad „Skrýt kameru“; okno za jinou
     aplikací, minimalizované i malý náhled navrchu přijde dopředu s fokusem. Po zavření okna položka zmizí.
     (Windows: kroky W1–W3 v T-102.)
   - Vývojové sestavení (`dotnet run`): ikona v Docku je obecná ikona spustitelného souboru – zapsat, jak vypadá.
   **Uzavírá:** T-102 kritérium 6 (a otevřené otázky v T-102).

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
4. **Zkušební upozornění (T-101).** V produkční aplikaci Nastavení → Obecné, vpravo v řádku nadpisu „Upozornění“
   tlačítko „Poslat zkušební upozornění“ (je tam i při odškrtnutém „Zobrazovat upozornění“).
   (a) Oznámení povolená: klik → banner „Zkušební upozornění“ se zvukem (Zvuk ≠ Vypnuto; zvolit „Vypnuto“ bez uložení
   a kliknout znovu → bez zvuku), vlevo od tlačítka šedý text „Zkušební upozornění odesláno… režim Soustředění“.
   Druhý klik nahradí předchozí upozornění v Centru oznámení (je tam jen jedno). Klik na banner (i na položku v Centru
   oznámení) přenese okno Nastavení do popředí na záložce Obecné, neotevře web tiskárny.
   (b) Zamítnuto: Nastavení systému → Oznámení → LayerPeek vypnout, vrátit se do okna → místo tlačítka testu červená
   hláška a „Otevřít nastavení oznámení“ (stav z kroku 3).
   (c) Ještě se neptal (`notDetermined`): jen pokud lze bez zásahu do TCC, např. na čerstvém uživatelském účtu nebo po
   novém bundle identifikátoru – **ne** `tccutil reset` na produkční identifikátor bez souhlasu uživatele. Klik → text
   „Čekám na vaši odpověď…“ a systémový dotaz; Povolit → upozornění a „odesláno“, Nepovolit → hláška a „Otevřít nastavení
   oznámení“, fokus z klávesnice zůstane na tomto tlačítku.
   (d) Klik na zkušební upozornění, když okno Nastavení není otevřené (zavřít ho po odeslání) → okno se otevře na
   záložce Obecné. **Uzavírá:** T-101 kritérium 4 (macOS).

## Sezení 6 – Kamera a monitory

Předpoklad: tiskárna s kamerou (Buddy3D v LAN, nebo kamera simulátoru `rtsp://127.0.0.1:8554/live`).

1. **Stream ve všech režimech.** Okno kamery z menu: obraz běží v normálním okně, na celé obrazovce i v malém náhledu,
   překryv stavu tisku je vidět ve všech třech. **Uzavírá:** T-026 kritéria 1 a 3 (s krokem 1.4 na čistém Macu).
2. **Umístění, monitor, „vždy navrchu“, celá obrazovka** – kroky z [T-038](../tasks/done/T-038-macos-camera-window-placement.md)
   („Ruční ověření“), stručně:
   - **Jeden monitor:** J1 název a rozlišení monitoru v bodech (stejné po restartu aplikace); J2 rohy a střed pro
     normální okno i náhled 16 bodů od kraje pracovní plochy, nikdy pod menu barem / Dockem (Dock dole, vlevo, skrytý),
     posun okna po otevření ano / ne; J3 náhled bez rámečku, přesun tažením, **změna velikosti všemi 8 hranami a rohy**,
     výška podle poměru stran po uvolnění; J4 „vždy navrchu“ nad Finderem / Safari, **malý náhled navrchu je vidět
     nad aplikací na celé obrazovce i na každé jiné ploše** (T-038 otázka 1 b); J5 celá obrazovka (F11 / dvojklik / tlačítko, vlastní plocha,
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
3. **Přechod na hardened runtime (T-108).** Uložit heslo tiskárny ve verzi bez T-108 (např. 0.3.0), pak nainstalovat
   sestavení s T-108 přes ni. Očekávání: nejvýš jeden dialog Klíčenky, po „Vždy povolit“ už žádný; aplikace normálně
   běží (menu bar, kamera, notifikace). Zapsat výsledek do `tasks/done/T-108-macos-hardened-runtime.md`.

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
Vše, co zbývá ověřit na Windows (build a testy po macOS úkolech, windowsová sada snímků z T-101, T-103, T-106, T-109–T-112
a T-058, instalátor s autostartem z T-017, zkušební toast z T-101, hlavní panel z T-102, web přes proxy z T-107), je ve
sběrném úkolu [T-114 „Ověření na Windows“](../tasks/todo/T-114-windows-verification.md): skript
`tools/windows-verify.ps1`, postup v [windows-manual-testing.md](windows-manual-testing.md).
