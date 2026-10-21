---
id: T-057
title: FFmpeg pro win-arm64 (kamera na Windows ARM64)
status: todo
owner: core-dev
priority: P2
platform: windows
depends_on: []
---

## Cíl
Uživatel chce vydávat i `win-arm64` (rozhodnutí 2026-10-21) a kamera tam má fungovat stejně jako na `win-x64`. Dnes `build/FFmpeg.targets` stahuje nativní knihovny FFmpeg jen pro `win-x64` (podmínka `PtdFFmpegEnabled`, `RuntimeIdentifier` prázdný nebo `win-x64`), takže publish `win-arm64` žádné DLL FFmpeg neobsahuje a kamera tam hlásí chybějící knihovny.

Navazuje na kód T-014, který je už sloučený v `develop` (T-014 zůstává v `in-progress` jen kvůli ruční kontrole kamery, proto ho `depends_on` neuvádí).

## Rozsah
- **Zdroj LGPL buildu:** stejný projekt a stejné připnuté vydání jako pro `win-x64` (BtbN FFmpeg-Builds, `autobuild-2026-10-17-20-51`, FFmpeg `n9.0.2-28-g669f5ab083`, LGPL shared). Vydání obsahuje i ARM64 variantu:
  - `https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-10-17-20-51/ffmpeg-n9.0.2-28-g669f5ab083-winarm64-lgpl-shared-9.0.zip`
  - velikost 53 752 900 B, SHA256 podle GitHub API (pole `digest` souboru vydání): `991ca1d75dce59001467e30b8c98c3fce54448d708cd7bde0cb863b6326f5368`. U `win64` se hodnota z API shoduje s hashem připnutým v T-014, přesto ji vývojář ověří po stažení.
  - Ověřit, že archiv obsahuje stejné knihovny a stejné hlavní verze (`avformat-63`, `avcodec-63`, `avutil-61`, `swscale-10`, `swresample-7`), že jsou to ARM64 PE soubory (pole Machine `0xAA64`), a že `LICENSE.txt` a konfigurace buildu jsou LGPL (bez `--enable-gpl` a `--enable-nonfree`).
  - Zdroj buildu: https://github.com/BtbN/FFmpeg-Builds (cíl `winarm64`, varianta `lgpl-shared`).
- **`build/FFmpeg.targets`:** URL, SHA256 a složka archivu podle cílové architektury (`win-x64` → `win64`, `win-arm64` → `winarm64`). Cache zůstává oddělená podle hashe archivu. Když `RuntimeIdentifier` není zadaný (`dotnet build`, `dotnet test`, `dotnet run`), rozhoduje architektura procesu, který aplikaci spustí, tj. na ARM64 Windows se SDK pro ARM64 se použijí ARM64 knihovny. Návod v chybové hlášce (`_PtdFFmpegHelp`) platí pro obě architektury.
- **Testy FFmpeg** (`[FFmpegFact]`, dnes jen `win-x64`): spouštět i na `win-arm64`. Na x64 stroji se ARM64 knihovny načíst nedají. Tam stačí test nebo kontrola, že publish `win-arm64` obsahuje všech 5 DLL a `FFmpeg-LICENSE.txt` a že DLL jsou ARM64.
- **Licence a okno licencí:** odkaz na „přesný připnutý build“ a test shody s `PtdFFmpegUrl` (`ThirdPartyLicenses`, T-014) musí počítat se dvěma archivy. Okno licencí ukáže build odpovídající běžící architektuře, nebo oba.
- **Native AOT:** spravovaný kód se nemění (binding `FFmpeg.AutoGen.Bindings.DynamicallyLinked` načítá knihovny podle názvu). AOT publish `win-arm64` potřebuje MSVC ARM64 build tools (viz `docs/prerequisites.md`, doplňuje T-055). Publish `win-arm64` bez trim/AOT varování z kódu projektu.
- **Balení:** DLL se kopírují do publish složky stejně jako u `win-x64`, takže je archiv a instalátor z T-055 / T-017 převezmou bez dalších úprav. ARM64 knihovny jsou menší (archiv asi 54 MB oproti 77 MB).
- Dokumentace: `docs/prerequisites.md` (sekce FFmpeg: obě architektury, ruční naplnění cache pro ARM64) a poznámky v T-014 / T-026 tam, kde se píše „jen win-x64“.

## Akceptační kritéria
- [ ] `dotnet publish src/PrusaTrayDiagnostic.App -c Release -r win-arm64` obsahuje 5 DLL FFmpeg (ARM64) a `FFmpeg-LICENSE.txt`. Publish `win-x64` obsahuje beze změny x64 knihovny.
- [ ] SHA256 ARM64 archivu je připnutý v `build/FFmpeg.targets` a ověřený po stažení. Při neshodě build selže stejně jako u x64.
- [ ] Build bez RID na x64 stroji se chová stejně jako dnes (beze změny pro vývoj na x64)
- [ ] Okno licencí a test `ThirdPartyLicenses` pokrývají oba připnuté buildy
- [ ] AOT publish `win-arm64` bez trim/AOT varování z kódu projektu
- [ ] **Ručně (ARM64 zařízení):** AOT exe `win-arm64` se spustí (tray ikona, menu, okno nastavení), testy FFmpeg projdou a živý stream z kamery Buddy3D se zobrazí. `Setup.exe` `win-arm64` (Velopack) nainstaluje aplikaci bez admin práv, zástupce ve Start menu má AUMID `PrusaTrayDiagnostic` a odinstalace proběhne (převzato z T-063 a T-056, rozhodnutí uživatele 2026-10-21; `Setup.exe` vznikne podle T-063 nebo skriptem z T-017). Uživatel zatím ARM64 zařízení nemá (2026-10-21): po implementaci úkol zůstává `in-progress`, dokud zařízení nebude k dispozici a ruční ověření neproběhne.
- [ ] `docs/prerequisites.md` aktuální
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- ~~Ruční ověření na ARM64~~ → uživatel zatím nemá zařízení s Windows na ARM64 (2026-10-21). Po implementaci úkol zůstane `in-progress`, dokud zařízení nebude k dispozici. Do té doby se `win-arm64` nevydává (T-055).
- **Vývoj na ARM64 bez RID:** má build bez zadaného RID na ARM64 stroji opravdu brát ARM64 knihovny (navrženo výše), nebo se vývoj na ARM64 nepředpokládá a stačí knihovny jen pro publish `win-arm64`?

## Poznámky k implementaci
(vyplní agent po dokončení)
