---
id: T-147
title: macOS – FFmpeg s muxerem pro nahrávání kamery
status: todo
owner: ui-dev
priority: P2
platform: macos
depends_on: [T-144, T-026]
---

## Cíl
Na macOS se FFmpeg staví ze zdrojů minimálně (`build/macos/build-ffmpeg.sh`, T-026: `--disable-everything`, jen `protocol=tcp,file`, `demuxer=rtsp,h264`, `parser=h264`, `decoder=h264`). Pro nahrávání z T-144 chybí muxer výstupního kontejneru. Úkol doplní do buildu přesně to, co nahrávání potřebuje, a licence zůstane **LGPL 3+** (žádné `--enable-gpl`, žádný enkodér).

## Rozsah
- Doplnit do `CONFIGURE_FLAGS` v `build/macos/build-ffmpeg.sh` muxer kontejneru zvoleného v T-144 (např. `--enable-muxer=mp4` a/nebo `--enable-muxer=matroska`) a co muxer případně dál potřebuje (bitstream filtry jako `extract_extradata` / `h264_mp4toannexb`, jen pokud jsou nutné – ověřit). Žádný enkodér.
- Ověřit, že `configure.log` dál hlásí „License: LGPL version 3 or later“ (kontrola ve skriptu zůstává).
- Aktualizovat dokumentaci konfiguračního řádku: komentář ve skriptu, `docs/prerequisites.md`, `tasks/done/T-026` se nemění (historie), poznámka do `docs/architecture.md` (oddíl Camera.FFmpeg).
- Cache v `build/FFmpeg.targets` je klíčovaná i obsahem skriptu, takže změna se přeloží do nové složky sama – ověřit.
- Spustit testy nahrávání z T-144 na Macu (přehrání výsledného souboru v QuickTime u MP4 / VLC u MKV).

## Akceptační kritéria
- [ ] První `dotnet build` na Macu postaví FFmpeg s muxerem, licence LGPL 3+, závislosti knihoven jen `@loader_path` a systém (kontroly skriptu prochází).
- [ ] Testy nahrávání z T-144 prochází na `osx-arm64`; factory hlásí „k dispozici“.
- [ ] Nahrávka z kamery simulátoru i ze skutečné kamery jde na Macu přehrát.
- [ ] Velikost knihoven se zvětšila jen o muxer (zapsat do poznámek před / po).
- [ ] `dotnet build` a `dotnet test` na Macu bez chyb a varování; Native AOT publish `osx-arm64` bez nových varování.

## Otevřené otázky
- (žádné vlastní; kontejner rozhoduje otázka 1 v T-144)

## Poznámky k implementaci
(vyplní agent po dokončení)
