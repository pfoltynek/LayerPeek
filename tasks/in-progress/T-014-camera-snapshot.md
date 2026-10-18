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
- [ ] Sekce Kamera v okně nastavení (zapnutí, RTSP URL); tlačítko Otestovat
- [ ] Načtení starého souboru nastavení bez sekce kamery funguje (kamera vypnutá)
- [ ] Klient streamu vrací dekódované snímky; testy s fixture (krátký nahraný H.264 úsek z Buddy3D, např. `tests/**/Fixtures/buddy3d.h264`) bez sítě a bez skutečné kamery
- [ ] Znovupřipojení po výpadku a zrušení přes `CancellationToken` pokryté testy
- [ ] FFmpeg binding a nativní knihovny ověřené pro Native AOT (`dotnet publish -r win-x64` bez varování); licence LGPL a verze FFmpeg zapsané v poznámkách
- [ ] Položka Kamera… v tray menu; nejvýš jedno okno na tiskárnu
- [ ] Okno: normální režim, celá obrazovka (F11 / dvojklik / Esc), malý náhled vždy nahoře s překryvem stavu tisku
- [ ] Současně otevřené náhledy více tiskáren (jeden na tiskárnu)
- [ ] Stav okna se pamatuje mezi spuštěními
- [ ] Po zavření okna se stream ukončí (žádné visící spojení)
- [ ] Ruční ověření na Windows se skutečnou kamerou Buddy3D (`rtsp://192.168.0.227/live`)
- [ ] Všechny nové NuGet balíčky ověřené jako AOT/trim kompatibilní (uvedeno v poznámkách)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; `dotnet publish -r win-x64` (AOT) projde

## Rozhodnuto
- **Formát streamu (2026-10-18):** RTSP, kamera Prusa Buddy3D. Uživatel má obě varianty kamery. Testovací URL: `rtsp://192.168.0.227/live`.
- **Ověřeno RTSP DESCRIBE (2026-10-18):** odpověď `200 OK`, bez autentizace, `Server: rtsp_demo`, jediná video stopa `H264/90000`, profil Baseline (`sprop-parameter-sets=Z0LA…`), `packetization-mode=1`, control `rtsp://…/live/track1`. Bez zvuku. Podle Prusa help: HD, ~25 fps, jen lokální síť.
- **Dekodér (2026-10-18):** FFmpeg přes P/Invoke, vykreslení do `WriteableBitmap`.
- **Snímková frekvence (2026-10-18):** v tomto úkolu se neomezuje. Případné omezení doplní uživatel později.
- **Překryv stavu tisku v náhledu (2026-10-18):** ano, progress a zbývající čas.
- **Více náhledů (2026-10-18):** ano, nejvýš jeden na tiskárnu.

## Otevřené otázky
- Který binding: FFmpeg.AutoGen, nebo Sdcb.FFmpeg? Rozhodne se po ověření AOT, výsledek předložit uživateli.
- Odkud brát nativní knihovny FFmpeg (konkrétní LGPL build a verze) a jak je přibalit (souvisí s T-017)?
- Výchozí pozice malého náhledu na obrazovce (roh)?
- Přikládat snímek k notifikaci o dokončení/chybě (T-010)?
- Prusa Connect snímky (nedokumentované API) – řešit v tomto úkolu, nebo až s T-005?

**Stav (2026-10-18):** Rozsah rozšířen o nastavení kamery a okno živého streamu. Zdroj obrazu je RTSP URL z nastavení, takže úkol neblokuje chybějící kamera v PrusaLink ani odložený T-005. Formát a dekodér jsou rozhodnuté. Volba bindingu se rozhodne v první fázi úkolu (ověření AOT), ostatní otázky implementaci neblokují.

## Poznámky k implementaci
