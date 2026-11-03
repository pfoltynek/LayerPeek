---
id: T-144
title: Nahrávání kamery – zápis streamu do souboru bez překódování (Camera.FFmpeg + abstrakce v Core)
status: todo
owner: core-dev
priority: P2
platform: all
depends_on: [T-143]
---

## Cíl
Technický základ nahrávání videa z kamery (nápad 1, fáze 1). Pakety H.264 z RTSP se jen **přebalí (remux)** do souborového kontejneru, **bez překódování**: žádný enkodér, nízké CPU a žádná otázka licence GPL / x264. Licence FFmpeg zůstává LGPL.

Tento úkol řeší jen „zapiš pakety ze sdíleného spojení (T-143) do souboru a správně ho ukonči“. Kdy se nahrává, kam a jak dlouho se nahrávky drží, řeší T-145. UI řeší T-146.

## Výchozí stav (ověřeno 2026-11-03)
- **Windows (`win-x64`):** build BtbN `ffmpeg-n9.0.2-…-win64-lgpl-shared-9.0` (`build/FFmpeg.targets`) je plný LGPL build bez `--disable-everything`. V `avformat-63.dll` jsou muxery „MP4 (MPEG-4 Part 14)“ a „Matroska / WebM“ (ověřeno na stažených DLL v cache `%LOCALAPPDATA%\LayerPeek.Build\ffmpeg\00ab3bc8…\bin`). Pro tento úkol se na Windows nic nemění.
- **macOS (`osx-arm64`):** `build/macos/build-ffmpeg.sh` staví minimální FFmpeg (`--disable-everything`, jen `protocol=tcp,file`, `demuxer=rtsp,h264`, `parser=h264`, `decoder=h264`). **Muxer chybí**, nahrávání na Macu proto bude fungovat až po T-147 (úkol `macos`). Tento úkol musí za běhu poznat, že muxer není k dispozici (např. `av_guess_format` / `avformat_alloc_output_context2` vrátí chybu), a vrátit srozumitelný stav „nahrávání není k dispozici“ – ne výjimku při startu.

## Rozsah
- **Core (`Core/Cameras/Recording` nebo podobně):** abstrakce, pracovně `ICameraRecorderFactory` / `ICameraRecording` (návrh rozhraní je na core-dev):
  - vytvoření nahrávky do zadané cesty z odběru paketů hubu (T-143),
  - `IsAvailable` / důvod nedostupnosti (chybějící muxer, kamera vypnutá),
  - průběžná velikost souboru a délka nahrávky (pro limit v T-145),
  - korektní ukončení (`StopAsync` zapíše konec kontejneru) a chování při chybě (výpadek streamu, plný disk, odebraný disk).
- **Camera.FFmpeg:** implementace přes `avformat_alloc_output_context2` + `avio_open` + `avformat_write_header` + `av_interleaved_write_frame` + `av_write_trailer` (FFmpeg.AutoGen, jako dosavadní dekodér; žádný nový balíček).
  - Nahrávka začíná **na klíčovém snímku** (IDR); pakety před prvním klíčovým snímkem se zahodí.
  - Časová razítka z RTSP se přepočítají na časovou základnu výstupu a začínají od nuly; po výpadku streamu a novém připojení (T-014: `ReconnectingCameraFeed`) se buď pokračuje se spojitými razítky, nebo začne nový soubor – podle otázky 3.
  - Parametry kodeku (SPS/PPS, rozměr) z popisu relace (SDP) / ze streamu; ověřit, že výsledný soubor přehraje běžný přehrávač (Windows: Filmy a TV / VLC, macOS: QuickTime u MP4).
  - Zápis souboru mimo vlákno dekodéru, aby pomalý disk nezdržel živý obraz (fronta s limitem; při přetečení se nahrávka ukončí s chybou, živý obraz běží dál).
  - Adresa kamery jde do logu jen přes `UriRedaction`. Co se smí zapsat o cestě nahrávky, určí otázka 5.
- **Měření:** změřit datový tok kamery Buddy3D (MB za hodinu) na skutečné kameře uživatele – podklad pro výchozí limit velikosti v T-145. Výsledek zapsat do poznámek.

## Akceptační kritéria
- [ ] Nahrávka z fixture `buddy3d.h264` i z RTSP kamery simulátoru (loopback) vytvoří platný soubor zvoleného kontejneru; test ho otevře zpět přes FFmpeg demuxer a ověří počet snímků, rozměr 1280×720, kodek H.264 a monotónní časová razítka.
- [ ] Nahrávka začíná klíčovým snímkem (test).
- [ ] Žádné překódování: enkodér se neotevírá (test / revize kódu) a CPU při nahrávání bez okna je zanedbatelné (změřit, zapsat do poznámek).
- [ ] Výpadek kamery během nahrávání (simulátor `camera offline` / `online`) neshodí aplikaci; chování podle otázky 3 je otestované.
- [ ] Ukončení aplikace / zrušení tokenu nahrávku korektně uzavře (soubor jde přehrát). Chování po tvrdém pádu procesu odpovídá zvolenému kontejneru (otázka 1) a je popsané v poznámkách.
- [ ] Bez muxeru (simulovaná nedostupnost) vrátí factory stav „není k dispozici“ s důvodem, bez výjimky.
- [ ] Testy po sobě smažou všechny vytvořené soubory i po pádu testu (dočasná složka testu, úklid na začátku i na konci).
- [ ] Ustálený stav živého obrazu se zapnutým nahráváním bez alokací na snímek v dekodéru (test z T-014 dál prochází).
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; žádná nová varování AOT / trim.

## Otevřené otázky
1. **Kontejner:** MP4, MKV, nebo volba v nastavení?
   - MP4 přehraje vše (i QuickTime), ale klasický MP4 je po pádu aplikace bez `moov` nečitelný. Řešením je fragmentovaný MP4 (`movflags=frag_keyframe+empty_moov`); některé starší přehrávače / editory s ním mají potíže.
   - MKV je po pádu čitelný až do posledního zapsaného bloku, QuickTime na macOS ho ale neotevře.
2. **Rozdělování dlouhých nahrávek:** jeden soubor na celý tisk (i 20 h), nebo segmenty po N minutách (snazší rotace v T-145, menší ztráta při chybě)? Pokud segmenty, jaká délka?
3. **Výpadek streamu během nahrávání:** pokračovat ve stejném souboru (mezera v čase / skok) nebo začít nový soubor?
4. **Zvuk:** Buddy3D zvuk nemá (předpoklad z T-014 – jedna stopa H.264); nahrává se jen video. Potvrdit.
5. **Co se smí objevit v logu:** stačí název souboru nahrávky bez složky (složka může obsahovat uživatelské jméno), nebo celá cesta?

### Rozhodnutí uživatele 2026-11-03
1. Kontejner: **fragmentovaný MP4** (`movflags=frag_keyframe+empty_moov`), bez volby v nastavení.
2. **Jeden soubor na tisk**, bez segmentů.
3. Výpadek streamu během nahrávání: **nový soubor** (přípona `-2`, `-3`…).
4. **Jen video, bez zvuku** – potvrzeno.
5. V logu **jen název souboru**, bez složky.

## Poznámky k implementaci
(vyplní agent po dokončení)
