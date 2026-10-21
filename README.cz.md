<p align="center">
  <img src="src/PrusaTrayDiagnostic.App/Assets/AppIcon/app-icon.svg" alt="Ikona Prusa Tray Diagnostic" width="96" height="96">
</p>

<h1 align="center">Prusa Tray Diagnostic</h1>

<p align="center">
  Přehled o 3D tiskárnách Prusa přímo z oznamovací oblasti.
  <br>
  <a href="README.md">English</a> ·
  <a href="https://github.com/pfoltynek/PrusaTrayDiagnostic/releases">Vydání</a> ·
  <a href="https://github.com/pfoltynek/PrusaTrayDiagnostic/issues">Hlášení chyb</a>
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-GPL--3.0--only-blue.svg" alt="Licence: GPL-3.0-only"></a>
  <img src="https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20(planned)-lightgrey.svg" alt="Platforma: Windows, macOS plánováno">
  <img src="https://img.shields.io/badge/.NET-11%20Native%20AOT-512BD4.svg" alt=".NET 11 Native AOT">
</p>

---

**Prusa Tray Diagnostic** je malá aplikace pro sledování 3D tiskáren Prusa, která žije jen v oznamovací oblasti
(na macOS v menu baru). Ikona ukazuje stav a průběh tisku, při důležitých událostech přijde systémová notifikace
a z menu se rychle dostaneš k diagnostice, teplotám a kameře tiskárny. Hlavní okno aplikace nemá, všechno začíná
u ikony.

S tiskárnami komunikuje přes **PrusaLink**, lokální REST API zabudované ve firmwaru tiskárny. Podpora
**Prusa Connect** (cloud) je plánovaná.

> [!NOTE]
> Jde o nezávislý komunitní projekt. **Není** to oficiální produkt společnosti Prusa Research a s Prusa Research a.s.
> nijak nesouvisí. *Prusa*, *PrusaLink* a *Prusa Connect* jsou ochranné známky svých vlastníků.

## Funkce

- **Stav na první pohled.** Ikona ukazuje stav tiskárny (nečinná, tiskne s průběhem, pozastaveno, hotovo, vyžaduje
  zásah, chyba, offline). Podrobnosti jsou v tooltipu.
- **Více tiskáren.** Jedna společná ikona ukazuje nejdůležitější stav ze všech tiskáren. Každá tiskárna má v menu
  vlastní podmenu.
- **Notifikace** při spuštění, dokončení, pozastavení, obnovení a zastavení tisku, při chybě, když tiskárna vyžaduje
  zásah nebo je offline, a nastavitelný počet minut před koncem tisku. Zvuk může být vypnutý, jen pro důležité
  události, nebo pro všechny.
- **Okno diagnostiky** s informacemi o tiskárně a firmwaru a živým grafem teplot trysky a podložky za posledních
  24 hodin.
- **Okno kamery** pro RTSP kamery (například Prusa Buddy3D) s překryvnými údaji o průběhu tisku, teplotách
  a odhadovaném konci. Má režim celé obrazovky, normální a kompaktní.
- **Web PrusaLink jedním kliknutím.** Webové rozhraní tiskárny se otevře v prohlížeči rovnou přihlášené. Heslo se do
  prohlížeče nikdy nepředává.
- **Bezpečné uložení hesel.** Hesla jsou ve Správci pověření Windows, nikdy v čitelném textu.
- **Osm jazyků:** angličtina, čeština, slovenština, němčina, španělština, francouzština, italština a polština.
  Ve výchozím stavu se aplikace řídí jazykem systému.
- **Nenáročná.** Publikuje se jako jeden nativní spustitelný soubor (Native AOT) a nepotřebuje nainstalovaný .NET.

## Podporované platformy

| Platforma | Stav |
|---|---|
| Windows 10/11 x64 | ✅ podporováno |
| macOS (Apple Silicon) | 🚧 plánováno: tray menu, notifikace, Klíčenka a kamera se připravují |
| Prusa Connect (cloud) | 🚧 plánováno |

## Instalace

> [!IMPORTANT]
> Zatím neexistuje hotové vydání. Plánovaný je instalátor s automatickými aktualizacemi.
> Do té doby [sestav aplikaci ze zdrojáků](#sestavení-ze-zdrojových-kódů).

Vydání budou na stránce [Releases](https://github.com/pfoltynek/PrusaTrayDiagnostic/releases). První verze nebudou
podepsané, takže Windows SmartScreen může zobrazit varování „Neznámý vydavatel“.

## Začínáme

### 1. Zapni PrusaLink na tiskárně

Na tiskárně otevři **Nastavení → Síť → PrusaLink**. Zkontroluj, že je PrusaLink zapnutý, a poznamenej si
**uživatelské jméno** (výchozí je `maker`), **heslo** a **IP adresu** tiskárny.

### 2. Přidej tiskárnu

1. Spusť aplikaci. V oznamovací oblasti se objeví ikona Prusa. Na Windows ji možná budeš muset připnout ze skrytých
   ikon (`^`).
2. V menu ikony zvol **Přidat tiskárnu…** (nebo **Nastavení…** → **Tiskárny** → **Přidat tiskárnu**) a vyplň:
   - **Název:** libovolné pojmenování.
   - **Adresa:** IP adresa nebo název počítače, např. `192.168.1.20` nebo `http://prusa-mk4.local`.
   - **Uživatelské jméno** a **Heslo:** údaje z kroku 1.
3. Klikni na **Otestovat spojení** a pak na **Uložit**.

Ikona hned začne ukazovat stav tiskárny.

### 3. Další nastavení (volitelné)

- **Kamera:** v nastavení tiskárny zadej adresu RTSP streamu kamery, např. `rtsp://192.168.0.227/live` pro kameru
  Buddy3D. Uživatelské jméno ani heslo do adresy nepiš. Kameru pak zapneš v podmenu tiskárny v menu ikony.
- **Notifikace:** vyber události, režim zvuku a upozornění na zbývající minuty tisku.
- **Obecné:** interval dotazování (jak často se aplikace ptá tiskáren na stav) a jazyk.

### Kde jsou uložená data

| Co | Kde (Windows) |
|---|---|
| Nastavení | `%APPDATA%\PrusaTrayDiagnostic\settings.json` |
| Hesla | Správce pověření Windows |

Historie teplot je jen v paměti a na disk se nikdy neukládá.

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
git clone https://github.com/pfoltynek/PrusaTrayDiagnostic.git
cd PrusaTrayDiagnostic

dotnet build                                      # sestaví celé řešení
dotnet test                                       # spustí všechny testy
dotnet run --project src/PrusaTrayDiagnostic.App  # spustí tray aplikaci
```

### Native AOT publish

```bash
dotnet publish src/PrusaTrayDiagnostic.App -c Release -r win-x64     # na Windows
dotnet publish src/PrusaTrayDiagnostic.App -c Release -r osx-arm64   # na macOS
```

AOT nejde kompilovat napříč operačními systémy: binárky pro Windows se sestavují na Windows, pro macOS na macOS.

### Vydání

Vydání se sestavují lokálně (bez CI) skripty v PowerShellu 7.2+: `scripts/release-build.ps1` (build, testy, AOT publish,
smoke test, archivy, kontrolní součty SHA-256), `scripts/release-upload.ps1` (koncept vydání na GitHubu)
a `scripts/release-publish.ps1`. Verze je v `<Version>` v `Directory.Build.props`, poznámky k vydání
v [CHANGELOG.md](CHANGELOG.md). Celý postup je v [docs/release.md](docs/release.md).

### Struktura projektu

| Cesta | Obsah |
|---|---|
| `src/PrusaTrayDiagnostic.Core` | doménový model, abstrakce, monitoring a detekce událostí (bez UI a bez HTTP) |
| `src/PrusaTrayDiagnostic.PrusaLink` | klient PrusaLink REST API a lokální proxy webu |
| `src/PrusaTrayDiagnostic.PrusaConnect` | klient Prusa Connect (plánováno) |
| `src/PrusaTrayDiagnostic.Camera.FFmpeg` | kamera RTSP/H.264 přes FFmpeg |
| `src/PrusaTrayDiagnostic.App` | Avalonia tray aplikace, okna, platformní kód |
| `tests/` | unit, UI (Avalonia headless) a E2E testy v xUnit v3 |
| `tools/` | vývojové nástroje, např. simulátor tiskárny PrusaLink a kamery |
| `docs/` | architektura a poznámky k API |
| `tasks/` | backlog úkolů |

Postaveno na [.NET](https://dotnet.microsoft.com/), [Avalonia UI](https://avaloniaui.net/),
[CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) a [FFmpeg](https://ffmpeg.org/).

## Jak přispět

Příspěvky jsou vítané: hlášení chyb, nápady, překlady i pull requesty.

- **Chyby a nápady:** založ [issue](https://github.com/pfoltynek/PrusaTrayDiagnostic/issues). Uveď model tiskárny,
  verzi firmwaru a operační systém.
- **Pull requesty:** projekt používá Gitflow. Větev založ z `develop` a PR směřuj do `develop`. `dotnet build`
  a `dotnet test` musí projít **bez jediného varování**, protože varování se počítají jako chyby.
- **Native AOT:** žádná neomezená reflexe, `System.Text.Json` jen přes generované kontexty a každý nový NuGet balíček
  musí být kompatibilní s trimmingem a AOT. Všechna pravidla jsou v [CLAUDE.md](CLAUDE.md).
- **Jazyk:** kód, komentáře a commit zprávy jsou anglicky. Úkoly v `tasks/` a dokumentace v `docs/` jsou česky.

Žádná CLA se nepodepisuje. Odesláním příspěvku souhlasíš s tím, že je licencovaný pod licencí projektu
(GPL-3.0-only).

### Bezpečnost

Bezpečnostní chyby prosím nehlas ve veřejných issues. Použij
[soukromé hlášení zranitelností](https://github.com/pfoltynek/PrusaTrayDiagnostic/security/advisories/new) na GitHubu.

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
