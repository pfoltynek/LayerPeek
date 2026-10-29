<p align="center">
  <img src="src/LayerPeek.App/Assets/AppIcon/app-icon.svg" alt="Ikona LayerPeek" width="112" height="112">
</p>

<h1 align="center">LayerPeek</h1>
<p align="center"><i>pro tiskárny Prusa</i></p>

<p align="center">
  <b>Sleduj své tiskárny Prusa živě, přímo z oznamovací oblasti.</b><br>
  Kamera v reálném čase, průběh tisku a notifikace přes lokální síť. Bez cloudu, bez účtu, bez otevřené záložky v prohlížeči.
</p>

<p align="center">
  <a href="https://github.com/pfoltynek/LayerPeek/releases"><b>Stáhnout</b></a> ·
  <a href="#rychlý-start">Rychlý start</a> ·
  <a href="#funkce">Funkce</a> ·
  <a href="README.md">English</a>
</p>

<p align="center">
  <a href="https://github.com/pfoltynek/LayerPeek/releases"><img src="https://img.shields.io/github/v/release/pfoltynek/LayerPeek?include_prereleases&label=release" alt="Poslední vydání"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-GPL--3.0--only-blue.svg" alt="Licence: GPL-3.0-only"></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4.svg" alt="Platforma: Windows 10/11">
  <img src="https://img.shields.io/badge/.NET-11%20Native%20AOT-512BD4.svg" alt=".NET 11 Native AOT">
  <a href="https://github.com/pfoltynek/LayerPeek/stargazers"><img src="https://img.shields.io/github/stars/pfoltynek/LayerPeek?style=social" alt="Hvězdičky na GitHubu"></a>
</p>

<!--
  SNÍMEK (hlavní): docs/images/camera-overlay.png
  Okno kamery v normálním režimu během skutečného tisku, překryv s průběhem, teplotami a časem dokončení.
  Doporučeno 1600×900 px, PNG, bez osobních údajů v názvu souboru a v názvu tiskárny.
<p align="center">
  <img src="docs/images/camera-overlay.png" alt="Živé okno kamery s překryvnými údaji o tisku" width="800">
</p>
-->

---

Spustíš devítihodinový tisk a vrátíš se k práci. Drží první vrstva? Nezamotala se cívka? Kolik ještě zbývá?
Obvykle se jdeš podívat k tiskárně, nebo máš otevřenou záložku v prohlížeči a pořád ji obnovuješ.

**LayerPeek** ti odpověď dá na jeden pohled. Ikona v oznamovací oblasti ukazuje stav a průběh tisku
všech tiskáren. Jedním kliknutím otevřeš **živé okno kamery** s průběhem tisku, teplotami a časem dokončení přímo
v obraze. Když se něco stane, hned přijde **systémová notifikace**: tisk je hotový, tiskárna vyžaduje zásah, chyba,
tiskárna je offline.

S tiskárnami komunikuje **přímo přes lokální síť** pomocí PrusaLink, API zabudovaného ve firmwaru tiskárny. Nic
neopouští tvoji síť: žádná cloudová služba, žádný účet, žádná telemetrie.

> [!NOTE]
> Jde o nezávislý komunitní projekt. **Není** to oficiální produkt společnosti Prusa Research a s Prusa Research a.s.
> nijak nesouvisí. *Prusa*, *PrusaLink* a *Prusa Connect* jsou ochranné známky svých vlastníků.

## Proč se ti bude líbit

- 🎥 **Živá kamera, ne jednotlivé snímky.** Stream RTSP/H.264 v reálném čase z kamery Prusa Buddy3D (nebo jiné RTSP
  kamery), dekódovaný lokálně přes FFmpeg. Malé okno vždy navrchu může zůstat v rohu obrazovky, zatímco pracuješ.
- 🏠 **Jen lokální síť.** Aplikace mluví s tiskárnami ve tvé síti. Funguje, i když nejde internet, a obraz z kamery
  nikdy neteče přes cizí server.
- 🔔 **Dozvíš se to, když na tom záleží.** Nativní notifikace Windows při dokončení, pozastavení a zastavení tisku,
  při chybě, když tiskárna vyžaduje zásah nebo přestane odpovídat, a připomínka pár minut před koncem tisku.
- 🖨️ **Celá tiskárenská farma v jedné ikoně.** Přidej tolik tiskáren, kolik máš. Společná ikona ukazuje
  nejdůležitější stav a každá tiskárna má vlastní podmenu.
- 🪶 **Malá a tichá.** Jeden nativní spustitelný soubor (Native AOT), žádný .NET k instalaci, žádné hlavní okno,
  žádná služba na pozadí. Prostě žije v oznamovací oblasti.
- 🔒 **Bezpečná k heslům.** Hesla jsou ve Správci pověření Windows. Webové rozhraní PrusaLink se otevře rovnou
  přihlášené a heslo se do prohlížeče nikdy nepředává.
- 🌍 **Mluví tvým jazykem.** Angličtina, čeština, slovenština, němčina, španělština, francouzština, italština
  a polština.
- 💚 **Zdarma a open source** pod GPL-3.0. Žádné reklamy, žádné předplatné, žádná „pro“ verze.

## Snímky obrazovky

<!--
  SNÍMKY: přidej soubory do docs/images/ a odstraň obal tohoto komentáře.
  Doporučeno: PNG, měřítko zobrazení 100 % nebo 150 %, světlý motiv Windows, žádné skutečné IP adresy ani sériová čísla.

| Menu v oznamovací oblasti | Malé okno kamery |
|---|---|
| <img src="docs/images/tray-menu.png" alt="Menu se dvěma tiskárnami" width="380"> | <img src="docs/images/camera-mini.png" alt="Malé okno kamery vždy navrchu" width="380"> |

| Notifikace | Diagnostika |
|---|---|
| <img src="docs/images/notification.png" alt="Notifikace o dokončení tisku" width="380"> | <img src="docs/images/diagnostics.png" alt="Okno diagnostiky s grafem teplot" width="380"> |
-->

*Snímky obrazovky přibudou s prvním vydáním.*

## Funkce

### V oznamovací oblasti

- Ikona ukazuje stav tiskárny: nečinná, tiskne (ikona se s průběhem tisku postupně plní), pozastaveno, hotovo,
  vyžaduje zásah, chyba, offline. Podrobnosti jsou v tooltipu: průběh, zbývající čas a název souboru.
- Menu má podmenu pro každou tiskárnu: zobrazení a skrytí kamery, diagnostika, web PrusaLink.

### Okno kamery

- Živý RTSP stream s widgety přes obraz: stav (název, stav, průběh), časy (zbývá, dokončení, doba tisku), teploty
  s malým grafem, název tisku s náhledem z G-code a chladnutí po tisku. Widgety, jejich velikost i místo si vybereš
  sám, přetáhneš je přímo v okně kamery.
- Tři režimy: **normální**, **malý náhled** (zůstává nad ostatními okny, pod obrazem má informační pás) a **celá
  obrazovka** (F11).
- Pro každou tiskárnu zvlášť: výchozí režim, velikost, umístění, monitor a „vždy navrchu“. Okno si umí pamatovat,
  kde jsi ho nechal.
- Po výpadku kamery nebo Wi-Fi se samo znovu připojí.

### Notifikace

- Spuštění, dokončení, pozastavení, obnovení a zastavení tisku, chyba, tiskárna vyžaduje zásah, tiskárna offline.
- Připomínka „zbývá N minut“, abys byl u tiskárny, až tisk skončí.
- Volitelné upozornění „tiskárna vychladla“, abys věděl, kdy ji můžeš vypnout.
- Zvuk vypnutý, jen pro důležité události, nebo pro všechny.

### Diagnostika

- Model tiskárny a firmware, aktuální stav a čas poslední aktualizace.
- Teplota trysky a podložky (skutečná a cílová), otáčky ventilátoru hotendu a tiskového ventilátoru, rychlost tisku,
  průtok a výška osy Z.
- Živý graf teplot trysky a podložky za posledních 30 minut, 2 hodiny nebo 24 hodin (jen v paměti).

### Řešení problémů v aplikaci

- Okno **O aplikaci** uloží diagnostický balíček (logy, informace o verzi a systému, nastavení bez hesel) nebo
  zkopíruje krátké hlášení do schránky, takže nahlášení chyby zabere minutu.

## Podporované tiskárny

Každá tiskárna Prusa s **firmwarem Buddy 6.x a PrusaLink**:

| Tiskárna | Stav |
|---|---|
| Prusa CORE One | ✅ otestováno (s kamerou Buddy3D) |
| Original Prusa MK4 / MK4S | 🟡 mělo by fungovat, zatím neotestováno |
| Original Prusa MK3.9 / MK3.9S, MK3.5 / MK3.5S | 🟡 mělo by fungovat, zatím neotestováno |
| Original Prusa XL | 🟡 mělo by fungovat, zatím neotestováno |
| Original Prusa MINI / MINI+ | 🟡 mělo by fungovat, zatím neotestováno |
| Original Prusa MK3 / MK3S (8bitový firmware) | ❌ tiskárna nemá zabudovaný PrusaLink |

Všechny tiskárny s firmwarem Buddy 6.x mají stejné PrusaLink API. Používáš aplikaci s jiným modelem? Dej prosím
[vědět](https://github.com/pfoltynek/LayerPeek/issues), jestli funguje, ať ho můžeme označit jako
otestovaný.

Kamera je volitelná. I bez ní máš ikonu, notifikace a diagnostiku. Kamera musí poskytovat stream **RTSP H.264**
dostupný z tvého počítače, například kamera Prusa Buddy3D nebo většina IP kamer.

| Platforma | Stav |
|---|---|
| Windows 10/11 x64 | ✅ podporováno |
| Windows na ARM64 | 🚧 plánováno |
| macOS (Apple Silicon) | 🚧 předběžná verze: menu bar, Klíčenka a otevření při přihlášení; notifikace a kamera se připravují |

## Rychlý start

### 1. Stažení

Stáhni poslední verzi ze stránky [Releases](https://github.com/pfoltynek/LayerPeek/releases):

- **Instalátor** `LayerPeek-X.Y.Z-win-x64-Setup.exe`: instaluje jen pro tvého uživatele, bez práv správce, do
  `%LOCALAPPDATA%\LayerPeek` a přidá LayerPeek do nabídky Start. Odinstaluješ ho v **Nastavení → Aplikace**.
- **ZIP** `LayerPeek-X.Y.Z-win-x64.zip`: rozbal ho kamkoli a spusť `LayerPeek.exe`.

> [!IMPORTANT]
> Verze 0.x jsou **předběžné**. Nastavení se mezi verzemi může změnit. Sestavení nejsou podepsaná, takže Windows
> SmartScreen zobrazí „Systém Windows ochránil váš počítač“ s „Neznámým vydavatelem“. Klikni na
> **Další informace → Přesto spustit**, nebo si [aplikaci sestav sám](#sestavení-ze-zdrojových-kódů).

Odinstalace ponechá tvoje nastavení (`%APPDATA%\LayerPeek`) a hesla (Správce pověření Windows) a instalátor po sobě
nechá vlastní log v `%LOCALAPPDATA%\velopack`. Smazat je můžeš ručně. Logy aplikace v `%LOCALAPPDATA%\LayerPeek\logs`
se s aplikací smažou. Instalace přes verzi ZIP tuto složku také nahradí, včetně logů verze ZIP.

#### macOS (Apple Silicon, předběžná verze)

- **Obraz disku** `LayerPeek-X.Y.Z-osx-arm64.dmg`: otevři ho a přetáhni **LayerPeek** do složky **Aplikace**.
- **Archiv** `LayerPeek-X.Y.Z-osx-arm64.tar.gz`: obsahuje `LayerPeek.app`; rozbal ho a aplikaci přesuň do Aplikací.

LayerPeek je jen v menu baru, bez ikony v Docku. Notifikace a okno kamery na macOS zatím nejsou.

> [!IMPORTANT]
> Sestavení pro macOS mají jen ad-hoc podpis a Apple je **nenotarizoval**, takže Gatekeeper první spuštění zablokuje
> („Apple nemůže ověřit…“). Klikni na **Hotovo**, otevři **Nastavení systému → Soukromí a zabezpečení**, u zprávy
> o LayerPeeku klikni na **Přesto otevřít**, potvrď heslem nebo Touch ID a aplikaci otevři znovu. Pokročilí uživatelé
> můžou místo toho stažené aplikaci odebrat příznak karantény: `xattr -dr com.apple.quarantine /Applications/LayerPeek.app`.
> Spouštěj aplikaci ze složky Aplikace, ne přímo z obrazu disku: „Otevřít při přihlášení“ funguje jen tam.

### 2. Zapni PrusaLink na tiskárně

Na tiskárně otevři **Nastavení → Síť → PrusaLink**. Zkontroluj, že je PrusaLink zapnutý, a poznamenej si
**uživatelské jméno** (výchozí je `maker`), **heslo** a **IP adresu** tiskárny.

### 3. Přidej tiskárnu

1. V oznamovací oblasti se objeví ikona Prusa. Na Windows ji možná budeš muset připnout ze skrytých ikon (`^`).
2. V menu ikony zvol **Přidat tiskárnu…** a vyplň:
   - **Název:** libovolné pojmenování.
   - **Adresa:** IP adresa nebo název počítače, např. `192.168.1.20` nebo `http://prusa-mk4.local`.
   - **Uživatelské jméno** a **Heslo:** údaje z kroku 2.
3. Klikni na **Otestovat spojení** a pak na **Uložit**.

Ikona hned začne ukazovat stav tiskárny.

### 4. Zapni kameru (volitelné)

V nastavení tiskárny zadej RTSP adresu kamery, např. `rtsp://192.168.1.21/live` pro kameru Buddy3D. Uživatelské
jméno ani heslo do adresy nepiš. Klikni na **Otestovat kameru**, ulož a v podmenu tiskárny zvol **Zobrazit kameru**.

### Další nastavení

- **Notifikace:** které události chceš, režim zvuku a upozornění na zbývající minuty.
- **Okno kamery:** výchozí režim, velikost, monitor a vždy navrchu.
- **Widgety:** které widgety se ukazují přes obraz, jejich nastavení (např. práh vychladnutí), neprůhlednost karet a
  rozložení.
- **Obecné:** jak často se aplikace ptá tiskáren na stav, jazyk a podrobné logování.
- **Spuštění a aktualizace:** spouštění LayerPeeku po přihlášení do Windows (ve výchozím stavu vypnuté,
  jen v nainstalované verzi) a jestli aktualizace nabízejí předběžné verze. Na macOS: **Otevřít při přihlášení**
  (ve výchozím stavu vypnuté, jen pro LayerPeek.app zkopírovanou z obrazu disku, např. do Aplikací).

### Kde jsou uložená data

| Co | Kde (Windows) | Kde (macOS) |
|---|---|---|
| Nastavení | `%APPDATA%\LayerPeek\settings.json` | `~/Library/Application Support/LayerPeek/settings.json` |
| Hesla | Správce pověření Windows | přihlašovací Klíčenka |
| Logy | `%LOCALAPPDATA%\LayerPeek\logs` | `~/Library/Logs/LayerPeek` |

Historie teplot je jen v paměti a na disk se nikdy neukládá. Aplikace se nepřipojuje nikam jinam než k tiskárnám
a kamerám, které přidáš.

## Plán

- Kontrola nové verze v aplikaci a aktualizace nainstalované verze jedním kliknutím
- `winget install` hned po zveřejnění prvního vydání
- Podepsaná sestavení (bez varování SmartScreenu) a instalace přes Homebrew na macOS. Záleží na tom, jestli se
  podaří sehnat peníze na podpisové certifikáty (Apple Developer Program, podpis pro Windows); možnosti podpory
  projektu přibudou později.
- Aplikace pro menu bar na macOS (Apple Silicon)
- Windows na ARM64
- Stav MMU3
- Prusa Connect (cloud) jako volitelný druhý zdroj pro tiskárny mimo tvoji síť

Máš nápad? [Založ issue](https://github.com/pfoltynek/LayerPeek/issues).

## Časté otázky

**Funguje to i mimo domácí síť?**
Zatím ne. Aplikace mluví s tiskárnami přímo přes lokální síť. Můžeš použít VPN do domácí sítě; volitelná podpora
Prusa Connect je v plánu.

**Nahrazuje to Prusa Connect nebo web PrusaLink?**
Ne, doplňuje je. Je to rychlý pohled přímo na ploše. Pro nahrávání souborů a spouštění tisků otevři z menu web
PrusaLink: přihlásí tě automaticky.

**Umí tiskárnu ovládat (pozastavit, zastavit)?**
V této verzi ne. Je to záměrně nástroj jen pro sledování: přístup jen pro čtení je bezpečnější u nástroje, který běží
pořád.

**Nezpomaluje to tiskárnu?**
Ne. Na stav se ptá jednou za pár sekund (nastavitelné), stejně jako web PrusaLink.

## Sestavení ze zdrojových kódů

### Prerekvizity

- [.NET SDK 11](https://dotnet.microsoft.com/download/dotnet/11.0). Přesná verze je v [`global.json`](global.json),
  povolené jsou i preview verze.
- Git.
- Jen pro **Native AOT publish**: na Windows Visual Studio Build Tools s workloadem *Desktop development with C++*,
  na macOS Xcode Command Line Tools.
- První build na Windows stáhne LGPL knihovny FFmpeg pro kameru (~77 MB, ověřené přes SHA-256).

Podrobnosti jsou v [docs/prerequisites.md](docs/prerequisites.md).

### Sestavení, testy, spuštění

```bash
git clone https://github.com/pfoltynek/LayerPeek.git
cd LayerPeek

dotnet build                                      # sestaví celé řešení
dotnet test                                       # spustí všechny testy
dotnet run --project src/LayerPeek.App  # spustí tray aplikaci
```

Nemáš po ruce tiskárnu? V repozitáři je [simulátor](docs/simulator.md) tiskáren PrusaLink a RTSP kamery pro vývoj.

### Native AOT publish

```bash
dotnet publish src/LayerPeek.App -c Release -r win-x64     # na Windows
dotnet publish src/LayerPeek.App -c Release -r osx-arm64   # na macOS
```

AOT nejde kompilovat napříč operačními systémy: binárky pro Windows se sestavují na Windows, pro macOS na macOS.

### Vydání

Vydání se sestavují lokálně skripty v PowerShellu 7.2+ ve složce `scripts/` (build, testy, AOT publish, smoke test,
archivy, kontrolní součty SHA-256, vydání na GitHubu). Verze je v `<Version>` v `Directory.Build.props`, poznámky
k vydání v [CHANGELOG.md](CHANGELOG.md). Jeden příkaz, `pwsh scripts/release.ps1 -Version X.Y.Z` (nejdřív s `-DryRun`),
provede vydání podle Gitflow (větev release, sloučení do `main` s tagem `vX.Y.Z`, sloučení zpět do `develop`), sestaví
tag a nahraje ho do konceptu vydání na GitHubu; před každým pushem a nahráním se zeptá. Zveřejnění konceptu zůstává
samostatný ruční krok (`scripts/release-publish.ps1`). Celý postup je v [docs/release.md](docs/release.md).

### Struktura projektu

| Cesta | Obsah |
|---|---|
| `src/LayerPeek.Core` | doménový model, abstrakce, monitoring a detekce událostí (bez UI a bez HTTP) |
| `src/LayerPeek.PrusaLink` | klient PrusaLink REST API a lokální proxy webu |
| `src/LayerPeek.PrusaConnect` | klient Prusa Connect (plánováno) |
| `src/LayerPeek.Camera.FFmpeg` | kamera RTSP/H.264 přes FFmpeg |
| `src/LayerPeek.App` | Avalonia tray aplikace, okna, platformní kód |
| `tests/` | unit, UI (Avalonia headless) a E2E testy v xUnit v3 |
| `tools/` | vývojové nástroje, např. simulátor tiskárny PrusaLink a kamery |
| `docs/` | architektura a poznámky k API |
| `tasks/` | backlog úkolů |

Postaveno na [.NET](https://dotnet.microsoft.com/), [Avalonia UI](https://avaloniaui.net/),
[CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) a [FFmpeg](https://ffmpeg.org/).

### Jak projekt vzniká

Projekt se vyvíjí s pomocí AI: většinu kódu, testů a dokumentace napsal [Claude Code](https://claude.com/claude-code)
podle zadání autora. Všechna produktová rozhodnutí dělá autor, každá změna prochází automatickými testy (unit,
headless UI a end-to-end testy proti simulátoru tiskárny) a revizí a aplikace je vyzkoušená na skutečné tiskárně.
Celý postup je veřejný: backlog úkolů je v `tasks/`, instrukce agentů v `.claude/agents/` a pravidla pro kód
v [CLAUDE.md](CLAUDE.md).

## Jak přispět

Příspěvky jsou vítané: hlášení chyb, nápady, překlady i pull requesty. Podrobnosti jsou
v [CONTRIBUTING.md](CONTRIBUTING.md) (anglicky).

- **Chyby a nápady:** založ [issue](https://github.com/pfoltynek/LayerPeek/issues). Uveď model tiskárny,
  verzi firmwaru a operační systém. Hodně pomůže diagnostický balíček z okna O aplikaci.
- **Překlady:** texty aplikace jsou v `src/LayerPeek.App/Resources/Strings.*.resx`. Opravy i nové jazyky
  jsou velmi vítané.
- **Pull requesty:** projekt používá Gitflow. Větev založ z `develop` a PR směřuj do `develop`. `dotnet build`
  a `dotnet test` musí projít **bez jediného varování**, protože varování se počítají jako chyby.
- **Native AOT:** žádná neomezená reflexe, `System.Text.Json` jen přes generované kontexty a každý nový NuGet balíček
  musí být kompatibilní s trimmingem a AOT. Všechna pravidla jsou v [CLAUDE.md](CLAUDE.md).
- **Jazyk:** kód, komentáře a commit zprávy jsou anglicky. Úkoly v `tasks/` a dokumentace v `docs/` jsou česky.

Žádná CLA se nepodepisuje. Odesláním příspěvku souhlasíš s tím, že je licencovaný pod licencí projektu
(GPL-3.0-only).

### Bezpečnost

Bezpečnostní chyby prosím nehlas ve veřejných issues. Použij
[soukromé hlášení zranitelností](https://github.com/pfoltynek/LayerPeek/security/advisories/new) na GitHubu.
Viz [SECURITY.md](SECURITY.md) (anglicky).

## Licence

Copyright © Petr Foltýnek

Tento program je svobodný software: můžeš ho šířit a upravovat podle podmínek
**GNU General Public License, pouze verze 3** (SPDX: `GPL-3.0-only`). Plné znění (anglicky) je v souboru
[LICENSE](LICENSE). Závazné je pouze anglické znění licence.

Program je šířen v naději, že bude užitečný, ale **bez jakékoli záruky**, a to i bez předpokládané záruky
prodejnosti nebo vhodnosti pro určitý účel.

### Komponenty třetích stran

Aplikace obsahuje nativní knihovny **FFmpeg** pod licencí LGPL. Jsou linkované dynamicky a dají se vyměnit. Text
licence FFmpeg je přibalený jako `FFmpeg-LICENSE.txt`. Všechny komponenty třetích stran, jejich verze a plná znění
licencí najdeš v aplikaci v okně **O aplikaci → Licence třetích stran…**.

---

<p align="center">
  Ušetřil ti LayerPeek cestu k tiskárně? Dej mu prosím ⭐. Pomůžeš tím ostatním makerům ho najít.
</p>
