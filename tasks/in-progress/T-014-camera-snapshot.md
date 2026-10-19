---
id: T-014
title: Kamera – živý stream a snímek
status: in-progress
owner: core-dev + ui-dev
priority: P3
platform: all
depends_on: [T-003, T-015]
---

## Cíl
U každé tiskárny jde v nastavení zadat kameru (RTSP adresu streamu). Z tray menu se pak otevře okno s živým obrazem. Okno jde roztáhnout na celou obrazovku, nebo zmenšit na malý náhled, který zůstává vždy nad ostatními okny (picture-in-picture) a sleduje tisk při jiné práci.

## Rozsah

### Nastavení (T-015 – okno nastavení)
- U tiskárny přibude sekce **Kamera**: zapnout/vypnout a RTSP URL streamu (např. `rtsp://192.168.0.227/live`, výchozí port 554).
- Buddy3D kamera přihlášení nevyžaduje. Pokud se později přidají kamery s přihlášením, údaje půjdou přes `ISecretStore` (ne v plain-text nastavení).
- Tlačítko **Otestovat** ověří, že se stream otevře a dorazí aspoň jeden snímek.
- Úprava konfiguračního modelu a úložiště nastavení v Core (`PrinterDefinition`, `SettingsDocument`) včetně migrace stávajících souborů (chybějící sekce = kamera vypnutá).

### Zdroj obrazu (core-dev)
- Formát: **RTSP s videem H.264**, zdroj je kamera **Prusa Buddy3D** (viz Rozhodnuto).
- Abstrakce v Core (např. `ICameraStream`), která vrací průběžné dekódované snímky (šířka, výška, BGRA pixely) a podporuje `CancellationToken`. Bez závislosti na Avalonii a FFmpeg.
- Dekódování: **FFmpeg přes P/Invoke** (FFmpeg.AutoGen nebo Sdcb.FFmpeg; volba podle ověření AOT). FFmpeg obstará RTSP i dekódování H.264.
- Kód, který přímo volá FFmpeg, je v samostatném projektu (např. `PrusaTrayDiagnostic.Camera.FFmpeg`). Core obsahuje jen abstrakci a s FFmpeg nemá žádnou závislost.
- Nativní knihovny FFmpeg se přibalí k aplikaci. Použije se LGPL build (bez GPL komponent), pro Windows `win-x64`. Pro macOS se řeší v navazujícím úkolu `macos`.
- Musí se ověřit, že vybraný binding projde `dotnet publish` s `PublishAot=true` bez trim/AOT varování (bez potlačování). Pokud neprojde, zastavit se a zeptat uživatele.
- Jednorázový snímek (snapshot) = první dekódovaný snímek ze streamu (Buddy3D samostatnou URL snímku zřejmě nemá, ověřit).
- Výpadek streamu: automatické znovupřipojení s prodlevou a srozumitelný stav (připojuji / bez signálu / chyba).
- Volitelně (jen pokud uživatel schválí nedokumentované API, T-005): Prusa Connect `GET /app/cameras/{camera_id}/snapshots/last` (viz `docs/api/prusa-connect.md`). Není podmínkou dokončení úkolu.
- PrusaLink na Buddy kamery nemá (`/api/v1/cameras` vrací 404, viz `docs/api/prusalink.md`). Proto je zdrojem URL zadaná uživatelem.

### Okno živého streamu (ui-dev)
- V tray menu u tiskárny s nastavenou kamerou je položka **Kamera…**, která otevře okno se streamem. U tiskárny bez kamery položka chybí nebo je neaktivní.
- Pro každou tiskárnu se otevře nejvýš jedno okno. Opakované vyvolání okno jen aktivuje. Okna (i malé náhledy) různých tiskáren mohou být otevřená současně.
- **Normální režim:** běžné okno se změnou velikosti, obraz se škáluje s zachováním poměru stran.
- **Celá obrazovka:** přepnutí tlačítkem, dvojklikem na obraz a klávesou F11; `Esc` vrátí normální režim.
- **Malý náhled (PiP):** přepnutí do malého okna bez rámečku a titulku, které je **vždy nad všemi okny** (`Topmost`). Jde ho přesouvat tažením a měnit mu velikost. Přes obraz se zobrazuje stav tisku (progress, zbývající čas).
- Z náhledu jde jedním klikem/dvojklikem zpět do normálního okna.
- Stav okna (režim, pozice, velikost náhledu) se pamatuje mezi spuštěními.
- Stream běží, jen když je okno otevřené. Po zavření okna se spojení ukončí. Minimalizované okno stream pozastaví.
- Při výpadku okno zobrazí poslední snímek a stav připojení.
- Obraz se vykresluje do `WriteableBitmap`, ne přes nativní video control, aby fungoval PiP bez rámečku i překryvy.
- Compiled bindings, žádná reflexe (Native AOT).

## Akceptační kritéria
- [x] Sekce Kamera v okně nastavení (zapnutí, RTSP URL); tlačítko Otestovat
- [x] Načtení starého souboru nastavení bez sekce kamery funguje (kamera vypnutá)
- [x] Klient streamu vrací dekódované snímky; testy s fixture (krátký nahraný H.264 úsek z Buddy3D, např. `tests/**/Fixtures/buddy3d.h264`) bez sítě a bez skutečné kamery
- [x] Znovupřipojení po výpadku a zrušení přes `CancellationToken` pokryté testy (logika v Core; FFmpeg klient zatím chybí)
- [x] FFmpeg binding a nativní knihovny ověřené pro Native AOT (`dotnet publish -r win-x64` bez varování); licence LGPL a verze FFmpeg zapsané v poznámkách
- [x] Položka Kamera… v tray menu; nejvýš jedno okno na tiskárnu
- [ ] Okno: normální režim, celá obrazovka (F11 / dvojklik / Esc), malý náhled vždy nahoře s překryvem stavu tisku
- [x] Současně otevřené náhledy více tiskáren (jeden na tiskárnu)
- [x] Stav okna se pamatuje mezi spuštěními
- [x] Po zavření okna se stream ukončí (žádné visící spojení)
- [ ] Ruční ověření na Windows se skutečnou kamerou Buddy3D (`rtsp://192.168.0.227/live`)
- [x] Všechny nové NuGet balíčky ověřené jako AOT/trim kompatibilní (uvedeno v poznámkách)
- [x] `dotnet build` a `dotnet test` bez chyb a varování; `dotnet publish -r win-x64` (AOT) projde

## Rozhodnuto
- **Formát streamu (2026-10-18):** RTSP, kamera Prusa Buddy3D. Uživatel má obě varianty kamery. Testovací URL: `rtsp://192.168.0.227/live`.
- **Ověřeno RTSP DESCRIBE (2026-10-18):** odpověď `200 OK`, bez autentizace, `Server: rtsp_demo`, jediná video stopa `H264/90000`, profil Baseline (`sprop-parameter-sets=Z0LA…`), `packetization-mode=1`, control `rtsp://…/live/track1`. Bez zvuku. Podle Prusa help: HD, ~25 fps, jen lokální síť.
- **Dekodér (2026-10-18):** FFmpeg přes P/Invoke, vykreslení do `WriteableBitmap`.
- **Snímková frekvence (2026-10-18):** v tomto úkolu se neomezuje. Případné omezení doplní uživatel později.
- **Překryv stavu tisku v náhledu (2026-10-18):** ano, progress a zbývající čas.
- **Více náhledů (2026-10-18):** ano, nejvýš jeden na tiskárnu.
- **Binding (2026-10-18):** `FFmpeg.AutoGen` 9.0.1.1, balíčky `FFmpeg.AutoGen.Abstractions` + `FFmpeg.AutoGen.Bindings.DynamicallyLinked` (`[DllImport]`, srozumitelná `DllNotFoundException`). Licence MIT. Ověřeno pokusem: AOT publish `win-x64` s 0 trim/AOT varováními, dekódování z Buddy3D (1280×720, `yuvj420p`, 25 fps), timeout i zrušení přes interrupt callback (`[UnmanagedCallersOnly]`). Sdcb.FFmpeg zamítnut (FFmpeg 7, net6.0, bez vydání od 2024, vlastní nativní balíček je GPL).
- **Nativní knihovny (2026-10-18):** BtbN FFmpeg-Builds, LGPL shared build tak, jak je (LGPL-3.0+, `--enable-version3`, bez GPL/nonfree), připnutý na konkrétní vydání:
  `https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-10-17-20-51/ffmpeg-n9.0.2-28-g669f5ab083-win64-lgpl-shared-9.0.zip`, SHA256 `00ab3bc8fe02554bd0fffac9968bba498d1ab7e899a4871b85fbba828a50bfcc`. Potřebné DLL: `avformat-63`, `avcodec-63`, `avutil-61`, `swscale-10`, `swresample-7` (avcodec ho importuje), celkem ~114 MiB.
- **Dodání knihoven (2026-10-18):** stažení při buildu – MSBuild krok stáhne připnutou URL, ověří SHA256, rozbalí do cache mimo repozitář a zkopíruje jen potřebné DLL k výstupu (app i testy). DLL se necommitují.
- **Fixture (2026-10-18):** nahraný úsek z kamery `buddy3d.h264` (356 339 B, ~3 s, 71 snímků, 1280×720, H.264 Constrained Baseline, Annex B, SHA256 `f8c82cc8…a50bfcc`) se smí commitnout do `tests/**/Fixtures/`.
- **Nízké zpoždění živého obrazu (2026-10-18):** ladit na co nejmenší zpoždění (méně probingu a bufferování), s fallbackem, když parametry streamu nestačí. Změřit proti skutečné kameře před i po.
- **Výpisy FFmpeg (2026-10-18):** zůstávají úplně vypnuté (`AV_LOG_QUIET`), do logu aplikace se neposílají.
- **Odmítnuté spojení (2026-10-18):** stav „bez signálu“ po `ConnectTimeout` u nedostupné kamery (Windows build FFmpeg odmítnuté spojení nepozná) je v pořádku.
- **Timeout tlačítka Otestovat (2026-10-18):** 15 s.
- **Časování streamu (2026-10-18):** `ConnectTimeout` 15 s, `FrameTimeout` 10 s, `ReconnectDelay` 5 s, pevná prodleva bez backoffu.
- **Neplatná adresa v souboru nastavení (2026-10-18):** kamera se vypne, ale adresa se **zachová** (zůstane v souboru a zobrazí se v nastavení k opravě).
- **Schéma a přihlášení (2026-10-18):** jen `rtsp://` bez přihlašovacích údajů v URL. Kamery s přihlášením později přes `ISecretStore`, údaje doplní do URL až implementace streamu.
- **Přihlašovací údaje v uložené adrese (2026-10-18):** údaje (`user:heslo@`) se při načtení z adresy odstraní a zbytek se zachová (např. `rtsp://admin:pw@192.168.0.227/live` → `rtsp://192.168.0.227/live`). Kamera zůstane vypnutá, dokud ji uživatel po kontrole znovu nezapne. Načtení vypíše varování a UI zobrazí hlášku, že byly údaje odstraněny. Heslo se nesmí dostat do souboru při uložení, do logů, do `ToString()` ani do textů výjimek.
- **Význam stavů (2026-10-18):** „bez signálu“ (`NoSignal`) = vypršel čas na snímek nebo kamera ukončila stream; „chyba“ (`Error`) = otevření nebo čtení streamu skončilo výjimkou (s důvodem). Stav „zastaveno“ není, po zrušení se už žádný stav nehlásí.
- **Výchozí pozice malého náhledu (2026-10-18):** poprvé se náhled otevře tam, kde bylo normální okno (levý horní roh, 320×180). Pak si pamatuje vlastní pozici a velikost.
- **Překryv stavu tisku ve všech režimech (2026-10-18):** progress a zbývající čas se zobrazují nenápadně v rohu obrazu v normálním okně, na celé obrazovce i v malém náhledu (doplňuje rozhodnutí výše).
- **Položka Kamera… v menu (2026-10-18):** schváleno, jak je. U jediné tiskárny je samostatná položka pod tiskárnou, u více tiskáren poslední položka podmenu. Bez zapnuté kamery chybí.
- **Stav okna kamery a tlačítka (2026-10-18):** schváleno. Stav se ukládá do samostatného souboru `camera-windows.json` vedle `settings.json`. Tlačítka okna se ukazují jen při najetí myší nebo při fokusu klávesnicí.
- **Automatické ověření UI (2026-10-18):** UI se má ověřovat automaticky přes Avalonia.Headless (`Avalonia.Headless.XUnit`, samostatný testovací projekt, vykreslování přes Skia). Skutečné chování OS (z-order, klik v tray menu) zůstává ručním ověřením. `Avalonia.Headless.XUnit` 12.1.4 nefunguje s xunit.v3 4.x. Rozhodnutí (2026-10-18): samostatný projekt `tests/PrusaTrayDiagnostic.App.UiTests` připne starší xunit.v3 přes `VersionOverride`, jen v tomto projektu. Ostatní testovací projekty zůstávají na 4.0.2.
- **Licence (2026-10-18):** všechny informace o licencích jsou v okně O aplikaci, vedle licence aplikace (Apache 2.0): tlačítko „Licence třetích stran…“ otevře okno se seznamem. Pro FFmpeg (LGPL-3.0-or-later, build BtbN) je tam verze za běhu, odkaz na zdroj FFmpeg a na přesný připnutý build, poznámka o dynamickém linkování a možnosti DLL vyměnit a plný text licence. Pro ostatní dodávané závislosti je uvedena verze, licence, odkaz na projekt a plný text. Seznam má jediný zdroj pravdy, který hlídá test.
- **Testy FFmpeg mimo win-x64 (2026-10-18):** testy, které potřebují nativní FFmpeg, se automaticky přeskočí, když FFmpeg nejde načíst a platforma není win-x64 (xunit v3 `Skip`/`SkipUnless` s důvodem). Na Windows x64 chybějící FFmpeg testy **shodí**.
- **Znak `@` v adrese kamery (2026-10-18):** adresa s `@` kdekoli se odmítne (i `rtsp://admin:1234/x@host`, kde `Uri` žádné přihlašovací údaje nevidí). Při načtení ze souboru se odstraní vše od `schéma://` po **poslední** `@` (`CredentialsRemoved = true`). `UriRedaction` takovou adresu maskuje stejně.
- **Automatický nový pokus okna kamery (2026-10-18):** když běh streamu skončí sám neočekávanou vnitřní chybou a okno je aktivní, okno to po `ReconnectDelay` (5 s z `CameraFeedOptions`, přes `TimeProvider`) zkusí znovu. Platí to, dokud je okno aktivní a nezavřené. Minimalizace, zavření nebo změna adresy čekající pokus zruší.
- **Hláška o odstraněných údajích (2026-10-18):** zobrazí se jen u načtených tiskáren s kamerou, ne u vynechaných položek ani u souboru, který se načetl jako poškozený (`Corrupted`). Z takového souboru, pokud je syntakticky platný, se údaje také odstraní.
- **Zrcadlo archivu FFmpeg (2026-10-18):** žádné. Když stažení selže, build skončí srozumitelnou chybou s návodem: aktualizovat `PtdFFmpegUrl` + SHA256, nebo předvyplnit cache / `PtdFFmpegCacheRoot`.
- **Odstranění přihlašovacích údajů ze souboru při startu (2026-10-18):** smí se změnit **jen** text adresy kamery (a `enabled` na `false` u dotčené kamery). Nic jiného se nepřepisuje: neznámé hodnoty, přeskočené tiskárny, komentáře ani formátování. Řeší Core (`ISettingsStore.RemoveCameraCredentialsAsync`), ne uložení celého nastavení.
- **Headless UI testy po merge s develop (2026-10-19):** mění rozhodnutí „Automatické ověření UI“ výše. UI testy kamery a licencí (22) se přesunuly do projektu `tests/PrusaTrayDiagnostic.App.UiTests` z T-031 a používají jeho infrastrukturu (holý `Avalonia.Headless`, vlastní `HeadlessSession` na xunit.v3 4.x, `UiTestScope`). Balíček `Avalonia.Headless.XUnit` a `VersionOverride` na `xunit.v3.mtp-v2` 3.2.2 se odstranily, všechny testovací projekty jsou na stejné verzi xunit.v3.
- **Odstranění údajů i z „poškozeného“ souboru (2026-10-18):** platí pro každý syntakticky platný JSON soubor nastavení, i když ho `LoadAsync` kvůli chybnému typu hodnoty hlásí jako `Corrupted`. „Neparsovatelný“ dál znamená jen syntakticky neplatný JSON (včetně neplatného UTF-8). Upozornění v UI se týká jen načtených tiskáren (řeší ui-dev).

## Otevřené otázky
- Přikládat snímek k notifikaci o dokončení/chybě (T-010)?
- Prusa Connect snímky (nedokumentované API) – řešit v tomto úkolu, nebo až s T-005?

**Stav (2026-10-18):** Rozsah rozšířen o nastavení kamery a okno živého streamu. Zdroj obrazu je RTSP URL z nastavení, takže úkol neblokuje chybějící kamera v PrusaLink ani odložený T-005. Formát a dekodér jsou rozhodnuté. Volba bindingu se rozhodne v první fázi úkolu (ověření AOT), ostatní otázky implementaci neblokují.

## Poznámky k implementaci

### Core (bez FFmpeg) – 2026-10-18
- Nový jmenný prostor `PrusaTrayDiagnostic.Core.Cameras`. Žádný nový NuGet balíček, žádná závislost na Avalonii ani FFmpeg.
- **Konfigurace:** `CameraSettings` (`Enabled`, `StreamUri`), `PrinterDefinition.Camera` (výchozí `CameraSettings.Disabled`). Validace (`ValidateStreamUri`, `TryParseStreamUri` s `CameraUrlProblem` pro hlášky v UI): absolutní URL, schéma jen `rtsp`, neprázdný host, bez přihlašovacích údajů. Vypnutá kamera si adresu může ponechat. Rovnost porovnává text adresy (`Uri.Equals` ignoruje user-info a fragment).
- **Úložiště:** v `PrinterDocument` přibyla nepovinná sekce `camera` (`enabled`, `streamUrl`). Formát zůstává `version: 1`, protože jde jen o přidané nepovinné pole. Chybějící sekce znamená vypnutou kameru. Neplatná adresa ze souboru se podle rozhodnutí zachová: `CameraSettings.FromInvalidStreamUrl(text)` vytvoří vypnutou kameru s `InvalidStreamUrl` (přesný původní text) a `StreamUrlProblem` (důvod pro hlášku v UI). Při uložení se text zapíše zpět beze změny. Varování při načtení adresu neobsahuje a `ToString()` ji skrývá.
- **Odstranění přihlašovacích údajů:** při načtení (`CameraSettings.FromStoredStreamUrl`, interní) se user-info hledá textově, takže se odstraní i z adres, které nejdou parsovat. Odstraní se vše za `schéma://` (bez `://` od začátku) až po **poslední** `@` celého textu (od code review, kolo 1; dřív jen v authority do prvního `/`, `?` nebo `#`). Co zbyde, se použije jako platná, ale vypnutá adresa, nebo se zachová jako `InvalidStreamUrl`, nebo se zahodí, když nezbyde nic. Pak se nastaví `CameraSettings.CredentialsRemoved = true` (jen informace pro UI, neukládá se a nepatří do rovnosti). `FromInvalidStreamUrl` text s údaji odmítne. Načtení soubor nemění; heslo ze souboru odstraní `ISettingsStore.RemoveCameraCredentialsAsync` (viz code review, kolo 1). Test s fixture `tests/PrusaTrayDiagnostic.Core.Tests/Fixtures/settings-v1-without-camera.json` (formát z T-015).
- **Abstrakce:** `ICameraFeedFactory.Create(Uri)` → `ICameraFeed` (`IAsyncDisposable`, `ReadFramesAsync(CancellationToken)`). Název `ICameraStream` z popisu úkolu nejde použít, protože analyzátor CA1711 zakazuje příponu `Stream` u typů, které nedědí z `System.IO.Stream`.
- **Snímky:** `CameraFrame` je `readonly struct` (šířka, výška, stride, BGRA `ReadOnlyMemory<byte>`). Snímek si jen **půjčuje** buffer dekodéru a platí do dalšího posunu enumerátoru. Díky tomu přeposílání snímků nealokuje (ověřeno testem v Debug buildu). Trvalou kopii vytvoří `CameraFrame.ToImage()` → `CameraImage`.
- **Znovupřipojení:** `ReconnectingCameraFeed` (s `TimeProvider`, `ILogger`, `CameraFeedOptions`). Hlídá timeout prvního snímku i mezery mezi snímky. Čas, který snímek zpracovává konzument, se nepočítá. Stav hlásí přes `IProgress<CameraStatus>` (`Connecting`, `Streaming`, `NoSignal`, `Error` + důvod). Po zrušení tokenu enumerace skončí bez výjimky a spojení se zavře. Do logů jde adresa vždy přes `UriRedaction.Redact` (user-info → `***`). U jiných výjimek než `CameraFeedException` obsahuje důvod jen název typu výjimky.
- **Snapshot:** `CameraSnapshot.CaptureAsync(factory, uri, ct)` vrací kopii prvního snímku a stream zavře.
- **Pro ui-dev:** `PrinterEditorViewModel.ToDefinition()` (App) zatím vytváří `PrinterDefinition` bez `Camera`. Uložení nastavení z okna tedy kameru vypne. Při doplnění sekce Kamera je potřeba `Camera` přenášet. Pokud má kamera `InvalidStreamUrl`, zobrazit tento text v poli URL a k němu hlášku podle `StreamUrlProblem`. Při `CredentialsRemoved` zobrazit hlášku, že byly přihlašovací údaje odstraněny a kameru je potřeba po kontrole znovu zapnout.

### FFmpeg klient (core-dev) – 2026-10-18
- **Projekt** `src/PrusaTrayDiagnostic.Camera.FFmpeg` (odkazuje jen na Core) a testy `tests/PrusaTrayDiagnostic.Camera.FFmpeg.Tests`, oba ve `PrusaTrayDiagnostic.slnx`. Nové balíčky `FFmpeg.AutoGen.Abstractions` a `FFmpeg.AutoGen.Bindings.DynamicallyLinked` 9.0.1.1 (MIT) jsou v `Directory.Packages.props`. AOT kompatibilitu ověřil zkušební konzolový projekt s odkazem na projekt: `dotnet publish -r win-x64` s `PublishAot=true` proběhl s 0 varováními, bez potlačení. Nativní binárka dekóduje fixture i živou kameru.
- **FFmpeg:** BtbN `n9.0.2-28-g669f5ab083` (LGPL-3.0+, `avcodec_license()` = „LGPL version 3 or later“). Načtené knihovny: avformat 63.1.102, avcodec 63.1.102, avutil 61.1.102, swscale 10.1.102 a swresample-7. Text licence se kopíruje k DLL jako `FFmpeg-LICENSE.txt`.
- **Veřejné API:** `FFmpegCameraFeedFactory : ICameraFeedFactory` (bezparametrický konstruktor, `Create(Uri)`) a `FFmpegCameraFeedFactory.LibraryVersion` (verze a licence pro diagnostiku / O aplikaci). Podporované adresy: `rtsp://` (TCP transport) a `file://`. Lokální soubor slouží testům a diagnostice, v nastavení se nenabízí a validace nastavení v Core se neměnila. Jiné schéma (i `rtsps`) vyhodí `CameraFeedException`. Chybějící DLL vyhodí `CameraFeedException` už v `Create`.
- **Vlákna:** každá enumerace má vlastní dekódovací vlákno (`Camera decoder`). Na volajícím vlákně FFmpeg nikdy neblokuje. Dekóduje se striktně po krocích: jeden snímek na jedno `MoveNextAsync`, takže půjčený buffer se během držení snímku nepřepíše. Pokračování po `await` (bez `SynchronizationContext`) běží přímo na dekódovacím vlákně. Díky tomu ustálený stav nealokuje, test měří 0 B na 50 snímků. Zrušení tokenu i dispose nastaví příznak, který kontroluje interrupt callback (`[UnmanagedCallersOnly]`). Čekající volání pak skončí do zhruba 100 ms: zrušení vyhodí `OperationCanceledException`, dispose `ObjectDisposedException`. Dispose počká, až vlákno uvolní všechny nativní prostředky. Je idempotentní a jde volat i během enumerace. Na konci streamu nebo při chybě se nativní prostředky uvolní hned.
- **Převod obrazu:** swscale do BGRA, řádek zarovnaný na 64 B. Buffer je připnuté pole (POH), znovu použité pro každý snímek. Nový buffer se alokuje jen při změně rozměru. Formáty `yuvj*` se mapují na `yuv*` + plný rozsah (`sws_setColorspaceDetails`), koeficienty podle BT.601/BT.709. Poškozené pakety a snímky (`AVERROR_INVALIDDATA`) se přeskočí.
- **Zjištěné chyby bindingu:** `ffmpeg.sws_scale(byte*[]…)` z FFmpeg.AutoGen pod JIT padá (access violation, pokus fungoval jen díky AOT). Proto se volá přes vlastní `[LibraryImport]` s ukazateli (`NativeMethods.Scale`). `ffmpeg.AVERROR(int)` alokuje při každém volání, proto se hodnota počítá jednou.
- **Chyby:** zprávy `CameraFeedException` jsou technické, anglicky a bez adresy, např. „Connecting to the camera failed: Connection timed out.“ Síťové chyby Windows, které `av_strerror` nezná (CRT errno 1xx a WSAE*), se překládají vlastní tabulkou. I/O timeout socketu je 20 s, tedy déle než výchozí timeouty `CameraFeedOptions`. Výpisy FFmpeg jsou podle rozhodnutí vypnuté (`AV_LOG_QUIET`).
- **Nativní knihovny:** `build/FFmpeg.targets` importuje jen projekt FFmpeg. DLL jsou položky `None` s `CopyToOutputDirectory`/`CopyToPublishDirectory`, takže se tranzitivně dostanou do výstupu i do publish složky každého projektu, který na FFmpeg odkazuje (testy, později App, i vedle AOT exe; ověřeno). Do App se `.targets` importovat **nesmí**, DLL by byly dvakrát (NETSDK1152). Cache: `%LOCALAPPDATA%\PrusaTrayDiagnostic.Build\ffmpeg\<sha256>\` (`bin\*.dll`, `LICENSE.txt`, značka `verified.sha256`), jde přepsat vlastností nebo proměnnou prostředí `PtdFFmpegCacheRoot`. První build stáhne archiv (~77 MB) do dočasné složky v cache, ověří SHA256 (při neshodě build selže a stažený soubor se smaže), rozbalí ho a ponechá jen 5 DLL a licenci. Další buildy jen kontrolují značku. Design-time build (IDE) nic nestahuje. Mimo Windows a pro RID jiný než `win-x64` se krok přeskočí (macOS: T-026). `PtdFFmpegEnabled=false` krok vypne úplně.
- **Fixture:** `tests/PrusaTrayDiagnostic.Camera.FFmpeg.Tests/Fixtures/buddy3d.h264` (SHA256 ověřeno), v `.gitattributes` označený jako binární.
- **Testy (34):** dekódování všech 71 snímků 1280×720, BGRA stride, neprůhlednost a jas, znovupoužití bufferu, konec souboru, zrušení uprostřed streamu (oba tokeny), už zrušený token, dispose během enumerace i během čekajícího snímku, dvojí dispose, jediná enumerace, souběžné `MoveNextAsync`, chybějící soubor a soubor, který není video. RTSP proti falešným serverům na loopbacku: celý záznam přes RTSP/RTP (falešná kamera posílá fixture jako H.264 v RTP, FU-A, TCP interleaved; ověřuje cestu bez probingu, všech 71 snímků), odmítnuté spojení, zrušení a dispose během připojování, timeout mlčícího serveru, zavřené spojení, odpověď 401. Integrace s `ReconnectingCameraFeed` (konec → `NoSignal` → nové připojení) a `CameraSnapshot`, test bez alokací. Žádný test nesahá na skutečnou kameru a nevytváří soubory.
- **Ruční ověření:** zkušební konzole proti `rtsp://192.168.0.227/live` dekódovala 125 snímků 1280×720 (JIT i AOT).
- **Nízké zpoždění (podle rozhodnutí):** u RTSP se přeskakuje `avformat_find_stream_info`, pokud popis relace (SDP) určí kodek. U Buddy3D je to H.264 a extradata 40 B, rozměr 0×0 dekodéru nevadí. Když kodek z SDP určit nejde, nebo jde o soubor, probíhá probing jako dřív (fallback). Dál `fflags=nobuffer`, dekodér `AV_CODEC_FLAG_LOW_DELAY` a `thread_count=1` (bez zpoždění vláknového dekódování). Volby `max_delay`/`reorder_queue_size` platí jen pro RTP přes UDP, proto se nenastavují. **Měření proti kameře:** předtím první snímek za 3,5–3,6 s, hned po něm dávka 30 snímků (obraz ~1,2 s za skutečností). Potom první snímek za 2,2–2,5 s, dávka 1–2 snímky (≤ 80 ms), dál plynule ~24 fps. Zbylé ~2 s jsou čekání na první klíčový snímek kamery (bez něj obraz dekódovat nejde). Testy s fixture prošly a ustálený stav dál nealokuje.
- **Pro ui-dev:** App zatím na projekt FFmpeg neodkazuje. Při zapojení stačí `ProjectReference` a explicitní registrace `ICameraFeedFactory` → `FFmpegCameraFeedFactory`. Snímky z `await foreach` bez `ConfigureAwait(false)` na UI vlákně fungují. Dekodér pak na UI vlákno čeká, takže snímek je potřeba zkopírovat do `WriteableBitmap` rychle.

### Okno nastavení, tray menu a okno kamery (ui-dev) – 2026-10-18
- **Bez nového NuGet balíčku.** App odkazuje na `PrusaTrayDiagnostic.Camera.FFmpeg` (`ProjectReference`, `build/FFmpeg.targets` se v App neimportuje). DLL FFmpeg a `FFmpeg-LICENSE.txt` jsou i ve výstupu AOT publish. Dekodér se volí jen v `AppServices.CreateCameraFeedFactory` (`new FFmpegCameraFeedFactory()`). Testy mohou předat vlastní továrnu přes `AppServices.Create(cameraFeedFactory: …)`.
- **Nastavení:** v `PrinterEditorViewModel` přibyla sekce Kamera (`CameraEnabled`, `CameraUrl`, `CameraUrlError`, `CameraCredentialsRemoved`, `TestCameraCommand`/`CancelCameraTestCommand`). `ToDefinition()` kameru přenáší, uložení z okna ji už nevypne. Pravidla: zapnutá kamera potřebuje platnou adresu. Přihlašovací údaje v adrese blokují uložení vždy, i u vypnuté kamery. Vypnutá kamera smí mít neplatnou adresu (zůstane beze změny jako `InvalidStreamUrl`). Prázdná adresa vypnuté kamery znamená žádnou kameru. Hlášky jsou podle `CameraUrlProblem` (CZ + EN).
- **Otestovat:** `CameraSnapshotTester` (`ICameraTester`) volá `CameraSnapshot.CaptureAsync` na thread poolu s timeoutem 15 s (`TimeProvider`). Test jde zrušit a zruší ho i úprava adresy nebo zavření okna. Výsledek ukazuje rozlišení, timeout nebo důvod z `CameraFeedException`. U jiných výjimek se zobrazí jen obecná hláška. Adresa jde do logu jen přes `UriRedaction`.
- **Odstraněné údaje při startu:** `MonitoringSession` po načtení (stav `Loaded`) zjistí kamery s `CredentialsRemoved` a hned nastavení uloží. Heslo tak zmizí ze souboru ještě před spuštěním monitoringu. Selhání uložení se zaloguje a monitoring běží dál. Dotčené tiskárny si pamatuje `RemovedCameraCredentials` (jen v paměti). Okno nastavení podle toho ukáže hlášku, dokud uživatel nastavení neuloží (úspěšné uložení ji smaže).
- **Tray menu:** nový příkaz `TrayMenuCommand.Camera` a `ITrayAppCommands.ShowCamera(printerId)`. Položka „Kamera…“ je jen u tiskárny se zapnutou kamerou s platnou adresou (rozložení schváleno, viz Rozhodnuto).
- **Okna:** v `App` je jeden `AuxiliaryWindowSlot` na každou tiskárnu. Opakované vyvolání okno jen aktivuje, okna různých tiskáren mohou být otevřená současně. Při ukončení aplikace se zavřou všechna okna a stav oken se stihne uložit (čeká se nejvýš 1 s).
- **Cesta snímku:** stream běží na thread poolu (`ReconnectingCameraFeed`, 15/10/5 s, `CameraFeedOptions` v DI), pokračování běží na vlákně dekodéru. Každý půjčený snímek se hned zkopíruje do znovu používaného bufferu `CameraFrameExchange` (trojitý buffer). Dekodér tedy na UI nečeká. Do UI se posílá nejvýš jedno zpětné volání najednou. To vezme jen nejnovější snímek a jedním blokovým kopírováním ho zapíše do `WriteableBitmap`. Bitmapa se vytváří znovu jen při změně rozlišení. Když UI nestíhá, starší snímky se zahodí a nehromadí se. Ustálený stav nealokuje (test). `Image` se `Stretch=Uniform` zachová poměr stran.
- **Životní cyklus:** stream běží, jen když je okno otevřené a není minimalizované. Minimalizace stream zastaví, obnovení ho znovu připojí. Nový běh začne až po skončení předchozího, takže na okno připadá nejvýš jedno spojení. Zavření okna zruší token a spojení se zavře (test ověřuje dispose feedu). Při výpadku zůstane vidět poslední snímek a přes něj stav (připojuji / bez signálu / chyba + důvod). Vypnutí kamery nebo odebrání tiskárny v nastavení okno zavře, změna adresy stream restartuje a přejmenování změní titulek.
- **Režimy:** normální okno, celá obrazovka (tlačítko, dvojklik na obraz, F11; Esc zpět) a malý náhled (bez rámečku a titulku, `Topmost`, přesun tažením obrazu, změna velikosti okraji 6 px). Překryv stavu tisku (progress + zbývající čas) je vlevo dole ve všech režimech (`IsPrintOverlayVisible`), jakmile je známý stav tiskárny. Z náhledu zpět vede dvojklik nebo jedno kliknutí na tlačítko v rohu. Když OS přepne celou obrazovku sám (např. zelené tlačítko na macOS), režim se přizpůsobí. Tlačítka mají tooltipy a `AutomationProperties.Name` a jde na ně přejít klávesnicí.
- **Pamatování stavu:** `JsonCameraWindowStateStore` ukládá `camera-windows.json` v `AppDataPaths` (podle printer id: režim, pozice a velikost normálního okna, pozice a velikost náhledu). Zápis je atomický a probíhá na pozadí. Poškozený soubor znamená výchozí hodnoty. Uchová se nejvýš 64 tiskáren, nezměněný stav se nezapisuje. Pozice mimo připojené obrazovky se při otevření přesune na hlavní obrazovku. Bez složky s daty se stav pamatuje jen po dobu běhu.
- **Testy (App.Tests, bez sítě a kamery, falešný `ICameraFeedFactory` a `FakeTimeProvider`):** `CameraFrameExchangeTests` (+ test bez alokací), `CameraViewModelTests`, `CameraWindowStateStoreTests` (dočasné složky pod `%TEMP%\PrusaTrayDiagnostic.App.Tests`, úklid i po pádu přes assembly fixture), `CameraSnapshotTesterTests`, `CameraSettingsTests`, `MonitoringSessionCameraTests`, `TrayMenuCameraTests` a rozšířené `AppServicesTests`.
- **Ověřeno na Windows:** `dotnet build` celé solution s 0 varováními, `dotnet test` (1770 testů, 0 selhání, 1 dřívější skip), `dotnet publish -c Release -r win-x64` (AOT) s 0 varováními. Aplikace (`dotnet run` i AOT exe) se spustí a běží dál. **Neověřeno:** okno kamery a sekce Kamera v nastavení se skutečnou kamerou ani vizuálně. Obsluha tray menu se nedá automatizovat, takže zbývá ruční ověření (tažení a změna velikosti náhledu, dvojklik po `BeginMoveDrag`, F11/Esc, `Topmost`). macOS neověřeno (zelené tlačítko, okno bez rámečku).

### Licence třetích stran a pokus o headless UI testy (ui-dev) – 2026-10-18
- **Okno licencí:** O aplikaci → „Licence třetích stran…“ otevře `LicensesWindow` (vlastněné oknem O aplikaci, nejvýš jedno). Nahoře je FFmpeg: verze a licence načtených knihoven z `FFmpegCameraFeedFactory.LibraryVersion`, která se čte mimo UI vlákno. Když FFmpeg chybí, okno zobrazí srozumitelnou hlášku. Dál jsou tam odkazy na https://ffmpeg.org/download.html, na přesný připnutý build BtbN (shodu s `PtdFFmpegUrl` v `build/FFmpeg.targets` hlídá test) a na skripty buildu BtbN. Následuje poznámka o dynamickém linkování a výměně DLL a plný text `FFmpeg-LICENSE.txt` ze složky aplikace (když chybí, odkaz na GNU LGPL 3.0). Pod tím jsou rozbalovací položky všech ostatních komponent s verzí, licencí, odkazem na projekt (přes `IUrlLauncher`) a plným textem. Text se načte až při rozbalení.
- **Jediný zdroj pravdy:** `src/PrusaTrayDiagnostic.App/Licenses/ThirdPartyLicenses.cs` (komponenty, balíčky s verzemi, SPDX licence z nuspec, názvy textů). Texty jsou v `Licenses/Texts/*.txt` jako `EmbeddedResource` s pevným `LogicalName`. Čtou se přesným názvem (`GetManifestResourceStream`), bez skenování, což je bezpečné pro AOT. Licenční soubory jsou zkopírované z balíčků (ANGLE, SkiaSharp, HarfBuzzSharp, FFmpeg.AutoGen, THIRD-PARTY-NOTICES Skia/HarfBuzz, .NET runtime a Microsoft.Extensions). U balíčků jen s výrazem `MIT` (bez souboru) je to text MIT ze SPDX s copyrightem z nuspec.
- **Test hlídá úplnost:** `ThirdPartyLicensesTests` projde `src/PrusaTrayDiagnostic.App/obj/project.assets.json`, tedy všechny přímé i tranzitivní balíčky App. Selže, když některý balíček nemá položku se stejnou verzí. Výjimky jsou jen buildovací balíčky `Microsoft.NET.ILLink.Tasks` a `Avalonia.BuildServices`. Druhý test kontroluje `Directory.Packages.props` (skupiny kromě Tests). Další testy ověřují, že každý text je vložený a neprázdný, že MIT položky obsahují MIT text a ANGLE BSD text, a odkaz na build FFmpeg.
- **Ověřené licence (nuspec):** Avalonia* 12.1.4 MIT; Avalonia.Angle.Windows.Natives 2.1.27548.20260419 – licenční soubor = licence ANGLE (BSD-3-Clause); SkiaSharp(+NativeAssets) 3.119.4 MIT; HarfBuzzSharp(+NativeAssets) 8.3.1.3 MIT (nativní knihovny Skia/HarfBuzz: THIRD-PARTY-NOTICES z balíčku); CommunityToolkit.Mvvm 8.4.2 MIT; Microsoft.Extensions.* 10.0.13 MIT; .NET runtime (Microsoft.DotNet.ILCompiler, AOT) MIT; FFmpeg.AutoGen.* 9.0.1.1 MIT (soubor); MicroCom.Runtime 0.11.6 MIT; Tmds.DBus.Protocol 0.94.1 MIT (jen Linux). Neověřená licence nezůstala žádná.
- **Headless UI testy:** podle zadání jsem zkusil `Avalonia.Headless.XUnit` 12.1.4 v novém projektu `tests/PrusaTrayDiagnostic.App.UiTests`. Na xunit.v3 4.0.2 padá discovery (`MissingMethodException`). Pokus s holým `Avalonia.Headless` + `HeadlessUnitTestSession` se zasekl. Podle zadání jsem se zastavil a projekt, balíček i záznam ve slnx jsem odstranil. Později vyřešeno přes `VersionOverride`, viz sekce Headless UI testy níže. Zůstala jen příprava: správu oken kamery jsem vytáhl z `App` do testovatelné třídy `Windows/CameraWindows.cs` (`Show`, `Shutdown`, `OpenWindows`) a falešný feed v testech umí jednobarevné snímky (`SendColor`).
- **Pokrytí:** automaticky (view modely a služby) je pokryto: sekce Kamera v nastavení a Otestovat, položka Kamera… a jedno okno na tiskárnu (slot), režimy a příkazy (F11/Esc/dvojklik/náhled), překryvy stavu, ukončení streamu po zavření a při minimalizaci, uložení stavu okna, licence. **Ručně zbývá** (dokud nebudou headless testy): skutečné vykreslení obrazu a letterbox, okno bez rámečku, `Topmost` a z-order, tažení a změna velikosti náhledu, klávesy v reálném okně, klik v tray menu, okno licencí na obrazovce. Ručně zůstává i ověření se skutečnou kamerou a vše na macOS.
- **Ověřeno:** `dotnet build` celé solution s 0 varováními, `dotnet test` (1786 testů, 0 selhání, 1 dřívější skip), `dotnet publish -c Release -r win-x64` (AOT) s 0 varováními (výstup obsahuje DLL FFmpeg a `FFmpeg-LICENSE.txt`). AOT exe se spustí a běží.

### Headless UI testy (ui-dev) – 2026-10-18
- **Projekt** `tests/PrusaTrayDiagnostic.App.UiTests` (je ve slnx). Balíček `Avalonia.Headless.XUnit` 12.1.4 (MIT, jen pro testy, nedodává se) je v `Directory.Packages.props` ve skupině Tests. Projekt používá `xunit.v3.mtp-v2` s `VersionOverride="3.2.2"`, protože Avalonia.Headless.XUnit 12.1.4 závisí na `xunit.v3.extensibility.core` 3.2.2. Varianta MTP v2 odpovídá generaci testovací platformy ostatních projektů (xunit.v3 4.0.2 = `xunit.v3.mtp-v2` 4.0.2). Repo `CentralPackageVersionOverrideEnabled` nevypíná. `dotnet test` celé solution se smíšenými verzemi funguje.
- **Proč samostatný projekt:** jiná verze xunit, všechny `[AvaloniaFact]` běží na jednom UI vlákně a sdílejí kulturu i myš. Proto je v projektu vypnutá paralelizace (`CollectionBehavior(DisableTestParallelization = true)`). App.Tests zůstávají rychlé paralelní testy bez Avalonie.
- **Prostředí:** vlastní `TestApplication` (Fluent theme, bez tray ikony a bez služeb), `UseSkia()` + `UseHeadless(UseHeadlessDrawing = false)`, takže `CaptureRenderedFrame` vrací skutečně vykreslené pixely. Falešné objekty sdílí s App.Tests (odkazy na soubory): falešný `ICameraFeedFactory` s jednobarevnými BGRA snímky, `FakeTimeProvider`, `FakePrinterMonitor`, falešné úložiště nastavení. Bez sítě a kamery. Dočasné soubory vznikají jen pod `%TEMP%\PrusaTrayDiagnostic.App.UiTests` a úklid zajišťuje assembly fixture i po pádu. Každý test má timeout 60 s.
- **Testy (22):**
  - Okno kamery přes `CameraWindows.Show` (cesta za `ITrayAppCommands.ShowCamera`): jedno okno na tiskárnu, opakované vyvolání vrátí totéž okno bez druhého spojení, dvě tiskárny = dvě okna, tiskárna bez kamery nic neotevře, ukončení aplikace zavře vše.
  - Vykreslení: červený snímek 160×90 je v okně 400×400 vidět s pruhy nahoře a dole, v okně 800×200 s pruhy po stranách. Výměna snímku a změna rozlišení se projeví, malý náhled vykresluje taky.
  - Režimy: F11 → celá obrazovka, Esc → zpět, dvojklik na obraz přepíná. Malý náhled má `WindowDecorations.None`, `Topmost` a velikost 320×180. Tlačítko v rohu i dvojklik vrátí normální okno v původní velikosti.
  - Překryvy: „Připojuji…“, „Chyba kamery“ s důvodem, „Bez signálu“ přes poslední snímek. Stav tisku s progressem a zbývajícím časem je vidět ve všech třech režimech.
  - Životní cyklus: zavření okna ukončí feed (dispose i konec enumerace), minimalizace ho zastaví, obnovení znovu připojí.
  - Pamatování: režim, pozice a velikost náhledu i normálního okna přežijí „restart“ přes `JsonCameraWindowStateStore` v dočasné složce.
  - Nastavení: hláška u neplatné URL (zadané klávesnicí), hláška o odstraněných přihlašovacích údajích, Otestovat s výsledkem úspěch (1280×720) i timeout 15 s (skutečný `CameraSnapshotTester` s falešným feedem a časem), Uložit přenese kameru.
  - Licence: O aplikaci → „Licence třetích stran…“ otevře jediné okno, ukáže FFmpeg (verzi načte na pozadí), rozbalení položky Avalonia zobrazí plný text a odkaz otevře projekt přes `IUrlLauncher`. Zavření okna O aplikaci zavře i okno licencí.
- **Změny v aplikaci kvůli testům a z jejich nálezů:** v malém náhledu se okno začne přesouvat (`BeginMoveDrag`) až po posunu ukazatele o 4 px, takže klik i dvojklik zůstanou klikem (dřív stisk hned zahájil přesun a dvojklik se nedal spolehlivě rozpoznat). Přepnutí reaguje jen na přesně dvojklik, trojklik nepřepíná dvakrát.
- **Stabilita:** 55 běhů po sobě bez chyby (po vypnutí paralelizace a opravě čekání na zápis stavu na konci testu). Dřív se jednou objevil 10s timeout v testu „bez signálu“, který se už nezopakoval.
- **Automaticky pokryto:** sekce Kamera a Otestovat, položka Kamera… (VM a presenter) a jedno okno na tiskárnu, více oken současně, vykreslení a poměr stran, F11/Esc/dvojklik, vlastnosti malého náhledu, překryvy ve všech režimech, ukončení a pozastavení streamu, pamatování stavu, okno licencí.
- **Ručně zbývá:** skutečný z-order `Topmost` nad jinými aplikacemi, přesun a změna velikosti náhledu tažením v OS, aktivace (popředí) již otevřeného okna (headless okna aktivaci nehlásí), klik v tray menu, ověření se skutečnou kamerou Buddy3D a vše na macOS.
- **Ověřeno:** `dotnet build` celé solution s 0 varováními, `dotnet test` celé solution (1808 testů, 0 selhání, 1 dřívější skip), `dotnet publish -c Release -r win-x64` (AOT) s 0 varováními.

### Code review, kolo 1 – opravy v App (ui-dev) – 2026-10-18
- **Údaje ze souboru při startu:** `MonitoringSession` už při startu neukládá celé `AppSettings`. Volá `ISettingsStore.RemoveCameraCredentialsAsync` z Core, které změní jen adresy kamer (vynechané položky, neznámé hodnoty a další opravy zůstanou). Hlášku pro okno nastavení dostanou tiskárny ze sjednocení načtených `CredentialsRemoved` a ID, která vrátí Core. Test: soubor s vynechanou tiskárnou (duplicitní id) a neznámou hodnotou si je po startu ponechá, heslo zmizí a celé nastavení se neuloží.
- **`@` v adrese:** Core odmítá jakékoli `@` jako `CameraUrlProblem.ContainsCredentials`. Hláška zní „Adresa nesmí obsahovat uživatelské jméno, heslo ani znak @.“ (EN obdobně). Testy pokrývají i `rtsp://admin:1234/x@host` a `@` v query.
- **CTS streamu:** token source teď patří UI vláknu. `StopStream` ho jen zruší, `OnRunEnded` (poslané z konce běhu) uvolní `_run` a token source zlikviduje. Běh, který skončí sám, už neblokuje další spuštění. Nový běh vynuluje nedoručenou notifikaci snímku. Testy: chybující sink, pak pozastavení a obnovení, a běh ukončený výjimkou dispatcheru se dá pozastavit a znovu spustit bez výjimky.
- **Testy a uživatelská data:** `AppServices.Create(cameraWindowStateStore: …)`, takže test AppServices používá paměťové úložiště. Žádný jiný test na AppData nesahá (kontrola přes grep).
- **UI test pamatování:** druhý harness jde přes `await using` a test čeká na zápis před smazáním složky.
- **Licence FFmpeg v testu:** na Windows x64 musí `FFmpeg-LICENSE.txt` existovat a obsahovat LGPL, jinde se test při chybějícím FFmpeg přeskočí (`Assert.SkipUnless`).
- **Drobnosti:**
  - Test, že nové spojení čeká na `DisposeAsync` předchozího.
  - Test, že zobrazená verze komponenty odpovídá verzím jejích balíčků.
  - Srozumitelná chyba, když chybí `project.assets.json`.
  - Komentář v `Directory.Packages.props` u xunit.v3 odkazuje na `VersionOverride` v App.UiTests.
  - Úložiště stavu oken před zápisem nejdřív načte soubor (stav nastavený před načtením nesmaže ostatní tiskárny). Serializuje se až při zápisu a načtené položky se řadí jako starší.
  - Kontrola pozice mimo obrazovku (`CameraWindowPlacementRules`) vyžaduje i horní okraj (titulek) uvnitř pracovní plochy.
  - Při změně adresy kamery se smaže starý obraz (`ICameraFrameSink.Clear`, `HasFrame = false`, „Připojuji…“) a snímky starého běhu se už nevykreslí (generace ve `CameraFrameBuffer`).
  - Pořadí `using` v `AppServices.cs`.
- **Ověřeno:** všechny projekty App (App.Tests 1029, App.UiTests 22) a ostatní testovací projekty prošly. `dotnet build` celé solution s 0 varováními a AOT publish win-x64 s 0 varováními. V Core.Tests v době běhu selhávaly 2 testy rozpracované core-dev (`TmpProbe.Probe`, `ReconnectingCameraFeedAllocationTests`), App se jich netýkají.

### Code review, kolo 1 – opravy v Core a FFmpeg (core-dev) – 2026-10-18
- **Ztracený příznak `CredentialsRemoved`:** adresa jen s údaji (např. `admin:secret@`) dávala `CameraSettings.Disabled` s příznakem. Mapper ho porovnával přes `==`, kde se příznak nezapočítává, a tak se ztratil. Teď porovnává referenci. Přibyl test v mapperu i v úložišti.
- **`@` v adrese (rozhodnutí):** `ValidateStreamUri` vrací `CameraUrlProblem.ContainsCredentials` i pro adresu bez user-info, která obsahuje `@` (nová hodnota výčtu nepřibyla, hláška v UI zůstává). Při načtení se odstraňuje vše po poslední `@` celého textu, takže už nevadí ani heslo s `/`, `?` nebo `#`. `UriRedaction.Redact` maskuje i tvar `rtsp://admin:1234/x@host` → `rtsp://***@host`.
- **Odstranění údajů ze souboru (rozhodnutí):** `ISettingsStore.RemoveCameraCredentialsAsync(CancellationToken)` → `Task<IReadOnlyList<string>>` s id dotčených tiskáren. Má výchozí implementaci (prázdný seznam), aby falešné úložiště v testech App nemusela nic měnit. `JsonSettingsStore` načte surové bajty a `Utf8JsonReader` (AOT-safe) najde v `printers[*].camera` hodnoty `streamUrl`. Názvy vlastností porovnává bez ohledu na velikost písmen jako deserializátor. Nahradí se jen bajty dotčených hodnot: adresa bez údajů a `enabled: true` → `false`. Komentáře, formátování, neznámá pole, přeskočené tiskárny (i ty bez id se vyčistí, jen se nevrátí v seznamu) i nesmyslné hodnoty zůstanou beze změny. Zápis je atomický stejně jako `SaveAsync`. Chybějící, příliš velký, nečitelný nebo syntakticky neplatný soubor (i neplatné UTF-8) zůstane, jak je. Soubor s chybným typem hodnoty, který `LoadAsync` hlásí jako `Corrupted`, se vyčistí také (rozhodnutí, kolo 2). Soubor bez údajů se nepřepisuje.
- **Alokace `ReconnectingCameraFeed`:** s reálným (asynchronně dokončovaným) čtením alokoval ~190 B na snímek: stav metody `AwaitNextAsync`. Ani `PoolingAsyncValueTaskMethodBuilder` nepomohl (box se dál alokoval, ověřeno přes GC AllocationTick). Čtení se proto awaituje přímo v async iterátoru, jehož stavový automat se znovu používá. Nové testy: falešný stream dokončovaný z jiného vlákna (0 B na 256 snímků) a reálná cesta FFmpeg + `ReconnectingCameraFeed` nad fixture (0 B na 50 snímků).
- **Drobnosti:**
  - Watchdog si pamatuje, kterou aktuální instanci pokusu zrušil (`_firedFor`). Timer, který vyprší zrovna při přechodu na nový pokus, tak nový pokus neoznačí jako „bez signálu“.
  - `ICameraFeed` a `CameraFrame` dokumentují, že pokračování může běžet na vlákně dekodéru a že se v cyklu nesmí synchronně blokovat (hlavně na `DisposeAsync`).
  - `FFmpegRuntime` převede jakoukoli výjimku při načítání na `CameraFeedException`.
  - Opakovaná stejná chyba (typ + zpráva) se loguje celá jen poprvé, pak krátce („failed again: důvod“). Po snímku nebo jiné chybě se počítá znovu.
  - Factory nepředává FFmpeg fragment URL (`#…`); ověřeno testem s falešnou kamerou.
  - Texty timeoutů používají invariantní kulturu.
  - Odmítající port v testech drží socket navázaný, ale bez `Listen`, takže ho nikdo jiný nemůže obsadit.
- **`build/FFmpeg.targets` (robustnost):** pracovní složky se jmenují `tmp-<UTC čas>-<guid>`, složky starší než 2 h (i s neznámým názvem) se na začátku stahování smažou. Cache se sestaví ve staging složce (značka `verified.sha256` jako poslední) a do cíle se přesune jedním přejmenováním (`move`, jen Windows; pro macOS v T-026 `mv`). Pokud mezitím jiný build dokončil kompletní cache, vlastní stažení se zahodí. Neúplná cache se nahradí (nejdřív se smaže značka, pak zbytek). Při chybě stahování nebo neshodě hashe build selže se zprávou, jak to opravit (rozhodnutí C). **Ověřeno:** dva souběžné první buildy do prázdné cache (oba prošly, výsledek kompletní, staré složky smazané, čerstvá cizí zůstala), inkrementální build bez stahování, oprava neúplné cache, chybná URL (404), neshoda hashe, design-time build bez stahování, `-r osx-arm64` přeskočeno.
- **Testy FFmpeg mimo win-x64 (rozhodnutí):** `[FFmpegFact]`/`[FFmpegTheory]` (`FFmpegTestEnvironment.CanRun`, xunit v3 `Skip` + `SkipUnless`). Na win-x64 běží vždy, jinde jen když FFmpeg jde načíst. Přeskočení ověřeno dočasným vynucením. Testy factory, které FFmpeg nepotřebují, jsou běžné `[Fact]`.
- **NuGet (kritérium):** dodávané nové balíčky jsou jen `FFmpeg.AutoGen.Abstractions` a `FFmpeg.AutoGen.Bindings.DynamicallyLinked` 9.0.1.1 (ověřeno AOT publishem s 0 varováními). `Avalonia.Headless.XUnit` 12.1.4 a `xunit.v3.mtp-v2` 3.2.2 (`VersionOverride`) používají jen testy a nedodávají se, takže AOT/trim kompatibilita pro ně neplatí.
- **Testy:** FFmpeg 36 (nově falešná RTSP kamera s fragmentem, alokace přes `ReconnectingCameraFeed`), Core nové testy pro `@`, příznak, `RemoveCameraCredentialsAsync` (5), alokace s asynchronním streamem a logování opakované chyby.

### Code review, kolo 2 – App (ui-dev) – 2026-10-18
- **Nový pokus po vnitřní chybě:** `CameraViewModel` dostává `TimeProvider` a `CameraFeedOptions`. Když běh skončí sám a okno je aktivní, naplánuje se časovač (`ReconnectDelay`) a po jeho vypršení se na UI vlákně spustí nový běh. Minimalizace, zavření i změna adresy čekající pokus zruší (`StopStream` → `CancelRetry`). Pokus, který se zruší až po odeslání, nic neudělá. Testy: nový pokus po 5 s (ne dřív), zrušení minimalizací, zavřením i změnou adresy (bez druhého spojení).
- **`CameraFrameExchange`:** komentáře opravené. Po `ResetNotification` mohou být krátce ve frontě dvě zpětná volání, což je neškodné.
- **Hláška o odstraněných údajích:** je jen pro průnik ID vrácených z Core a načtených tiskáren s kamerou. Čištění souboru běží i pro stav `Corrupted`. Testy: vynechaná položka s heslem se vyčistí bez hlášky, poškozený (ale platný JSON) soubor se vyčistí bez hlášky a ostatní obsah zůstane.
- **Log:** „Credentials could not be removed from camera addresses in the settings file; they stay there until the settings are saved.“
- **Pozice okna:** horní okraj (titulek) stačí pokrýt sjednocením pracovních ploch, tedy i přes dva monitory nad sebou nebo vedle sebe (nejméně 48 px souvisle). Mezera mezi monitory pokrytí přeruší. Testy pro monitory nad sebou, mezeru i monitory vedle sebe.
- **UI testy:** `CameraHarness.DisposeAsync` obnoví kulturu ve `finally` i při timeoutu.
- **Ověřeno:** `dotnet build` celé solution s 0 varováními, `dotnet test` celé solution (1865 testů, 0 selhání, 1 dřívější skip; App.Tests 1038, App.UiTests 22) a AOT publish win-x64 s 0 varováními.

### Code review, kolo 2 – opravy v Core a FFmpeg (core-dev) – 2026-10-18
- **Testy escape sekvencí (M1):** původní test „escapes“ žádné escape sekvence neobsahoval, protože nástroj při zápisu souboru převedl `\u…` na znaky. Proto vznikla nová třída `JsonSettingsStoreScrubTests`, kde se zpětné lomítko skládá v kódu (`(char)92`), takže ho nic nemůže převést. Pokrývá:
  - `@` místo `@` a `s` uvnitř hodnoty, dále `\"`, `\` a `\/`
  - escapované id i názvy vlastností
  - zachování UTF-8 BOM (výstup začíná `EF BB BF`)
  - názvy vlastností velkými písmeny
  - duplicitní `streamUrl`, `enabled` i `camera`
  - vnořené objekty v `camera` zůstanou beze změny
  - komentáře, koncové čárky, tabulátory, CRLF a mezery kolem `:` zůstanou zachované
  - neplatné UTF-8 → soubor beze změny
  - soubor s chybným typem hodnoty (`Corrupted`) se vyčistí také (rozhodnutí)

  Starý test je přejmenovaný na `RemovingCameraCredentialsHandlesHiddenCredentials`.
- **Neplatné UTF-8 (m1):** `GetString` vyhodí `InvalidOperationException`. Ta se teď zachytí stejně jako `JsonException` a soubor zůstane beze změny.
- **Čtení souboru (m3):** `RemoveCameraCredentialsAsync` čte přes `FileStream` se `FileShare.Read | FileShare.Delete` jako `LoadAsync`. Velikost kontroluje na otevřeném streamu (`ReadExactlyAsync`, bez TOCTOU). Když se soubor mezitím zkrátí, zůstane beze změny.
- **Watchdog (n1):** rozhoduje podle záznamu o natažení, timer ho jen probouzí. `Arm` uloží pokus, čas natažení (`TimeProvider.GetTimestamp`) a timeout, `Disarm` záznam smaže. Callback zruší pokus jen tehdy, když je natažený pro aktuální pokus a uplynul celý timeout. Pokud přijde dřív (z dřívějšího natažení, nebo timer o chvíli předběhne), timer se natáhne na zbývající čas, takže skutečný timeout se neztratí. Bez alokací (testy alokací prošly). Nové testy `ReconnectingCameraFeedWatchdogTests` s ručně spouštěným timerem: pozdní callback po snímku nic nezruší, dřívější callback před termínem nic nezruší, callback po termínu ohlásí „bez signálu“.
- **Duplicity (n2):** zdokumentováno v `CameraCredentialScrubber` a `ISettingsStore`. Vyčistí se každá `streamUrl` s `@` ve všech objektech `camera` tiskárny. Každé `enabled: true` v objektu, kde se adresa čistila, se změní na `false`, i když by loader použil jinou (poslední) hodnotu (bezpečná strana). Objekt `camera` s čistými adresami si `enabled` ponechá.
- **Souběžné buildy (n4):** každý krok nahrazení cache těsně předtím znovu zkontroluje stav složky:
  - značka se maže, jen když chybí soubory
  - složka se maže, jen když nemá značku
  - přejmenování proběhne, jen když cílová složka neexistuje

  Kompletní cache, kterou mezitím přejmenoval jiný build, tak zůstane nedotčená a vlastní stažení se zahodí. Znovu ověřeno: dva souběžné první buildy, inkrementální build, oprava cache se značkou i bez ní, design-time build, `-r osx-arm64`.
- **Dokumentace (n5):** `docs/prerequisites.md` uvádí, že ručně vytvořený `verified.sha256` má obsahovat jeden řádek s hodnotou `PtdFFmpegSha256` (build zatím kontroluje jen existenci).

### Merge s develop (2026-10-19)
- **Konflikty (19 souborů)** jsou vyřešené tak, aby zůstalo chování obou stran: uživatelské jméno PrusaLink (T-027), jazyk aplikace (T-029), webová proxy PrusaLink (T-028), toasty (T-010) i kamera.
  - `PrinterDefinition`, `SettingsDocument`, `SettingsMapper` mají `UserName` i `Camera`. `PrinterEditorViewModel.ToDefinition()` přenáší obojí.
  - `SettingsEditorStore` dostává `IPrinterWebGateway` i `RemovedCameraCredentials`.
  - `AppServices.Create` má přetížení s `windows` (T-010) i s továrnou kamery a úložištěm stavu oken.
  - `TrayHost` po startu spustí toasty a načte stav oken kamery. Při ukončení počká na uložení stavu oken kamery a pak souběžně zastaví monitoring a webové proxy.
  - Develop načítá nastavení před startem (`MonitoringSession.LoadSettingsAsync`, statická metoda kvůli jazyku). Odstranění přihlašovacích údajů ze souboru se proto přesunulo do ní (nový nepovinný parametr `RemovedCameraCredentials`). Platí stejně pro start z `TrayHost` i pro `Start()` bez načteného nastavení.
  - `Strings.resx` a `Strings.cs.resx` jsou sjednocením položek `<data>` podle názvu. Žádný klíč nebyl změněný na obou stranách. Kódování bez BOM zůstalo.
- **Překlady (T-029):** všech 42 textů kamery (sekce Kamera v nastavení, okno kamery, hláška o odstraněných údajích, okno licencí, O aplikaci, položka „Kamera…“) je přeložených do sk, de, es, fr, it a pl podle konvencí T-029:
  - zástupné symboly beze změny, výpustka „…“ u položek, které otevírají okno;
  - ve francouzštině nezlomitelná mezera před `:` a `;`;
  - formální oslovení jako ve stávajících překladech;
  - kamera = Kamera / Cámara / Caméra / Telecamera; tray menu = nabídka v oznamovací oblasti, Infobereich, área de notificación, zone de notification, area di notifica, obszar powiadomień.
  Technický důvod chyby z dekodéru (anglicky) se nepřekládá, přeloží se jen věta kolem něj. Překlady nekontroloval rodilý mluvčí, stejně jako v T-029.
- **Přepínání jazyka za běhu:** XAML okna kamery a sekce Kamera v nastavení používá `{Binding [Klíč], Source={x:Static loc:LocalizedStrings.Instance}}`. `XamlLocalizationTests` to hlídá.
  - `CameraViewModel` implementuje `ILanguageAware`: titulek, stav připojení, tooltip a překryv stavu tisku se zformátují znovu ze zobrazeného stavu. Stream ani režim se nemění.
  - `CameraWindows.RefreshTexts()` volá `App.OnLanguageChanged`.
  - `LicensesViewModel` a `LicenseItemViewModel` formátují texty ze stavu (verze FFmpeg se znovu nenačítá). `AboutViewModel.RefreshTexts()` obnoví i okno licencí, které z něj bylo otevřené.
  - `PrinterEditorViewModel.RefreshTexts()` znovu zformátuje chybu adresy kamery a výsledek testu kamery (uložený jako zdroj textu, stejně jako test spojení).
  - Texty se formátují až při zobrazení. Jazyk textů se čte ze `Strings.Culture`, formát čísel ze systému.
  - Ovládací prvky sekce Kamera mají `AutomationProperties.AutomationId` (jen atributy).
- **UI testy (rozhodnutí 2026-10-19):** testy jsou rozdělené do složek `Cameras/`, `Settings/` a `About/`. Těla testů běží přes `HeadlessSession.RunAsync`. Jazyk a kulturu nastavuje relace (en-US), vlastní `TestApplication` a `CultureScope` v harnessu odpadly a testy používají skutečnou třídu `App`. Pomocné metody T-014 (`UntilAsync`, `Pixel`, `ShowsText`…) jsou v `Infrastructure/Ui.cs`.
  - Dvojklik už nečeká 800 ms (`Task.Delay`). Druhý dvojklik míří na jiný bod, takže začne novou sérii kliknutí.
  - `UntilAsync` dál krátce čeká v cyklu (10 ms), ale jen na práci na thread poolu (falešný stream, test kamery). UI se ustálí přes `Settle()`.
  - Snímkové reference okna nastavení (4 PNG) jsou přegenerované, protože pod testem spojení je teď vidět sekce Kamera. Jiný rozdíl v nich není (zkontrolováno vizuálně).
  - Nové testy přepnutí jazyka: UI 4 (okno kamery de + návrat na systém, O aplikaci + okno licencí fr, sekce Kamera sk), App.Tests 14 (`CameraViewModel`, `LicensesViewModel`, `AboutViewModel`, kamera v `PrinterEditorViewModel`, položka Kamera… v menu).
- **Zámky a dočasné složky (T-032):**
  - `App.Tests` `TestDirectories` je `ReservedTempDirectory` `PrusaTrayDiagnostic.App.Tests.Files.<id>`. Úklid po pádu maže i starou složku `%TEMP%\PrusaTrayDiagnostic.App.Tests` pod starým zámkem `PrusaTrayDiagnostic.App.Tests.Files.lock`.
  - UI testy mají vlastní rezervaci `PrusaTrayDiagnostic.App.UiTests.Files.<id>` (`Infrastructure/UiTestDirectories.cs`, assembly fixture), oddělenou od složky selhání snímků. Starou složku `%TEMP%\PrusaTrayDiagnostic.App.UiTests` z T-014 neuklízí: její zámek patří rezervaci snímků, takže nejde ověřit, že ji nepoužívá starší běh. Starý kód ji maže sám na konci běhu.
  - Testy FFmpeg a Core kamer dočasné soubory nevytvářejí, nebo už používají `TestDirectories` z Core (T-032). Po celém běhu `dotnet test` v `%TEMP%` nic nezůstalo.
- **Licence:** T-028 nahradil balíčky `Microsoft.Extensions.*` 10.0.13 sdíleným frameworkem ASP.NET Core 11.0 (Kestrel). Ověřeno přes `project.assets.json` a seznam referencí ILC v AOT publish (`*.ilc.rsp`): do exe se kompiluje runtime pack `Microsoft.AspNetCore.App.Runtime.win-x64` 11.0.0-rc.2.26504.105 (Kestrel i `Microsoft.Extensions.*`). WindowsDesktop se nekompiluje.
  - Položka „Microsoft.Extensions 10.0.13“ je nahrazená položkou „ASP.NET Core shared framework“: MIT (nuspec), texty `LICENSE.txt` a `THIRD-PARTY-NOTICES.TXT` z runtime packu (win-x64 a osx-arm64 jsou shodné). Verze se zobrazuje jako verze .NET.
  - `extensions-third-party-notices.txt` je odstraněný.
  - Nové testy: každý sdílený framework z referencí zdrojových projektů musí mít položku, a žádná položka nesmí uvádět balíček, který aplikace už neobnovuje (zastaralé položky).
  - `aspnetcorev2_inprocess.dll` ve výstupu AOT publish není a DLL FFmpeg i `FFmpeg-LICENSE.txt` ano, obě úpravy výstupu fungují současně.
- **Ověřeno (Windows 11):**
  - `dotnet build` celé solution: 0 varování.
  - `dotnet test`: 2492 testů, 0 selhání, 1 dřívější skip. App.Tests 1343, App.UiTests 67, Core 784, FFmpeg 36, PrusaLink 245, E2E 16, PrusaConnect 1.
  - `dotnet publish -c Release -r win-x64`: 0 varování.
  - Publikované AOT exe se spustí a běží (15 s, pak ukončeno).
  - Neověřeno: vzhled přeložených textů v okně kamery a licencí na obrazovce (délka popisků), macOS.
