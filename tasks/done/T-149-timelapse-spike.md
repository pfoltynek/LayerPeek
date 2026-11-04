---
id: T-149
title: Timelapse tisku – průzkum (spike): způsob, licence, AOT
status: done
owner: architect
priority: P3
platform: all
depends_on: [T-144]
---

## Cíl
Timelapse (zrychlené video celého tisku) na rozdíl od nahrávání z T-144 **nejde udělat bez překódování**: snímky se vybírají (např. jeden za vrstvu nebo každých N sekund) a musí se z nich složit nové video nebo sekvence obrázků. Spike zjistí, jak to udělat v souladu s GPL-3.0-only, LGPL FFmpeg a Native AOT, a výsledek předloží uživateli k rozhodnutí. Neimplementuje se produkční kód (nápad 1, fáze 2).

## Rozsah
- **Zdroj snímků:** dekódované snímky ze sdíleného spojení (T-143); spouštěč po čase, nebo po vrstvě (změna `Telemetry.AxisZ` z PrusaLink; ověřit, jak spolehlivě se Z mění při Z-hopu). PrusaLink endpoint `PUT /api/v1/job/{id}/continue` („Continues the job after a timelapse capture“) ve specifikaci PrusaLink naznačuje podporu timelapse ve firmwaru – ověřit, co znamená a zda se dá použít (zdroj: https://github.com/prusa3d/Prusa-Link-Web/blob/master/spec/openapi.yaml).
- **Varianty výstupu – porovnat (licence, AOT, velikost, kvalita, přehratelnost na Windows i macOS):**
  1. sekvence obrázků (JPEG / PNG) – kodér obrázků v .NET bez reflexe, nebo přes FFmpeg (`mjpeg` / `png` enkodér, LGPL),
  2. MJPEG v kontejneru (AVI / MKV / MOV) přes vestavěný enkodér FFmpeg `mjpeg` (LGPL) – na Windows je v buildu BtbN (ověřeno 2026-11-03: „MJPEG (Motion JPEG)“ v `avcodec-63.dll`), na macOS by se musel doplnit do `build-ffmpeg.sh`,
  3. H.264 přes OpenH264 – build BtbN obsahuje `--enable-libopenh264` (ověřeno 2026-11-03). Ověřit licenci zdrojového kódu (BSD-2) **a** patentovou licenci Cisco, která podle dostupných informací platí jen pro binárky distribuované Ciscem, ne pro staticky vestavěný build – rozhodnutí patří uživateli,
  4. platformní enkodéry OS (Media Foundation na Windows, VideoToolbox na macOS) přes vlastní interop – AOT, náročnost, dvě implementace,
  5. vestavěný enkodér FFmpeg `mpeg4` (LGPL) – kvalita / přehratelnost.
- **Prusa Connect:** poskytuje jen snímky, ne stream; klient Prusa Connect ale v projektu není (T-005 vyřazen 2026-10-29, bez veřejného API). Timelapse ze snímků Prusa Connect proto do průzkumu jen jako poznámka.
- Výstup: tabulka variant s citovanými zdroji (licence, dokumentace FFmpeg), doporučení k rozhodnutí a návrh rozpadu na implementační úkoly. Zapsat do tohoto úkolu (česky).

## Akceptační kritéria
- [x] Porovnání variant 1–5 s ověřenými licencemi (odkazy na primární zdroje) a dopadem na AOT a na build FFmpeg na macOS.
- [x] Ověřené chování spouštěče „po vrstvě“ na datech ze simulátoru / skutečné tiskárny (nebo zdůvodnění, proč to ověřit nejde). – Zdůvodnění: simulátor nemá vrstvy ani z-hop, ke skutečné tiskárně agent nemá heslo PrusaLink. Chování odvozeno ze zdrojů firmwaru a profilu PrusaSliceru; ověření na záznamu tisku je v T-159.
- [x] Otázky pro uživatele zapsané níže, navržené implementační úkoly (ID, owner, závislosti) zapsané v poznámkách.

## Otevřené otázky
1. **Chce uživatel timelapse vůbec**, a pokud ano, ve fázi 2 hned po dashcamu (T-148), nebo později?
2. **Výstupní formát** (sekvence obrázků / MJPEG / H.264 / platformní enkodér) – rozhodne uživatel po spiku.
3. **Spouštěč snímku:** po čase, po vrstvě, obojí jako volba?

### Rozhodnutí uživatele 2026-11-03
1. Timelapse uživatel chce prověřit – spike je v plánu sezení 2026-11-03.
3. Spouštěč snímku: spike prověří **obojí jako volbu** (po čase i po vrstvě; ověřit, zda PrusaLink spolehlivě hlásí změnu vrstvy).
- Nové závislosti spike **jen doporučí**; prototyp jen mimo repozitář (scratchpad), do repozitáře jde jen zpráva.

### Otázky po spiku (2026-11-03, rozhodnuto 2026-11-04)
Podklady: [docs/timelapse-spike.md](../../docs/timelapse-spike.md). Odpovědi jsou v oddílu „Rozhodnutí uživatele 2026-11-04“ níže.

1. **Způsob (výstupní formát, původní otázka 2):** schvaluje uživatel **variantu D – MP4 z klíčových snímků kamery
   bez překódování**? Bez enkodéru, bez nové závislosti, 0 B navíc, ~78 MB za 10 h, kvalita klíčového snímku. Omezení:
   snímek jen v okamžiku klíčového snímku (zpoždění do ~2,4 s), rozlišení / kvalita podle kamery.
   Alternativy, pokud ne: B (MJPEG, +232 kB na macOS), C1+C2 (H.264 enkodérem OS, +84 kB na macOS, dvě chování),
   A1 (sekvence JPEG přes Avalonii, 0 B). Všechny alternativy potřebují dekódování streamu po celý tisk.
   *Doporučení architekta: D.*
2. **Spouštěč (upřesnění rozhodnutí 3):** obojí jako volba zůstává, ale spouštěč po vrstvě je jen odhad z `axis_z`
   (PrusaLink na Buddy vrstvy nehlásí, z-hop až 1,5 mm). Souhlasí uživatel s postupem: nejdřív „po čase“ (T-158, T-160),
   „po vrstvě“ až po ověření na záznamu ze skutečného tisku (T-159)? Záznam pořídí uživatel skriptem z T-159, protože
   vyžaduje heslo PrusaLink.
3. **Interval po čase:** výchozí hodnota a nabídka (návrh: 30 s; volby 10 / 30 / 60 s nebo volné číslo 5–600 s)?
   U spouštěče po vrstvě nabízet i „každou N-tou vrstvu“?
4. **Snímková frekvence výstupu:** pevně 30 fps (10 h po 30 s = 40 s videa), nebo volba (např. 24 / 30 / 60)?
5. **Kdy se timelapse pořizuje:** automaticky během tisku jako volba u tiskárny (po vzoru `recordPrints`, výchozí
   vypnuto), ručně z tray menu, nebo obojí? Má se ve stavu `Paused` / `Attention` snímat dál (návrh: ne, po čase
   se během pauzy nesnímá)?
6. **Ukládání:** stejný kořen a podsložka tiskárny jako nahrávky (T-145), název `yyyy-MM-dd_HH-mm-ss[_<tisk>]_timelapse.mp4`
   (návrh)? Počítá se do společného limitu velikosti a rotace nahrávek?
7. **Spojení s kamerou:** držet RTSP otevřené po celý tisk (~1,7 % jádra, ~1,07 Mbit/s v LAN, když neběží nahrávání;
   návrh), nebo se připojovat jen na snímek (~2–4 s na snímek, šetří síť, neověřená robustnost Buddy3D při
   opakovaném připojování)?
8. **Timelapse z hotové nahrávky (varianta F):** chce uživatel i dodatečné vytvoření timelapse z nahrávky
   (např. „Vytvořit timelapse“ v okně / menu)? Build FFmpeg na to nic nového nepotřebuje. Návrh: později, samostatný úkol.
9. **Sekvence obrázků (JPEG):** chce uživatel navíc export jednotlivých snímků (např. pro vlastní střih)? Bez nové
   závislosti (Avalonia JPEG), ale potřebuje dekódování. Návrh: ne v první verzi.
10. **Priorita:** timelapse (P3) před dashcamem T-148, nebo po něm?
11. **Vedlejší nález, mimo timelapse:** připnutý build FFmpeg pro Windows (BtbN `autobuild-2026-10-17-20-51`) už na
    GitHubu neexistuje (404), čistý build na Windows bez cache selže. Otázka, kterou verzi připnout, je v **T-162**.

### Rozhodnutí uživatele 2026-11-04
1. **Způsob:** schválena **varianta D** – MP4 z klíčových snímků kamery bez překódování. → T-158.
2. **Spouštěč:** nejdřív po čase (T-158, T-160), po vrstvě až po ověření na skutečném tisku (T-159). Interval
   nastavuje uživatel. **Celá funkce timelapse je v UI označená jako experimentální** (štítek „Experimentální“ u voleb
   a všude, kde se timelapse zapíná) → T-161, text ve všech jazycích.
3. **Interval:** výchozí 30 s, nastavitelný. Návrh nabídky (10 / 30 / 60 s nebo volné číslo 5–600 s) platí, ovládací
   prvek vybere T-161. Dílčí otázka „každá N-tá vrstva“ zůstává otevřená v T-159.
4. **Výstup** pevně 30 fps: schváleno. Během `Paused` / `Attention` se nesnímá: schváleno.
5. **Kdy:** **obojí** – automaticky při tisku jako volba u tiskárny (po vzoru `recordPrints`, výchozí vypnuto)
   **i ručně** z tray menu. Timelapse jde zapnout / vypnout i z okna kamery. Když se timelapse pořizuje, okno kamery
   to ukazuje (indikátor po vzoru indikátoru REC nahrávání). → T-160, T-161.
6. **Ukládání:** stejný kořen a podsložka tiskárny jako nahrávky, počítá se do společného limitu a rotace: schváleno.
7. **Spojení s kamerou:** RTSP zůstane otevřené po celý tisk – zatím schváleno.
8. **Timelapse z hotové nahrávky (varianta F):** samostatný úkol → **T-164** (P3).
9. **Export sekvence snímků JPEG:** samostatný úkol → **T-165** (P3).
10. **Priorita:** timelapse až **po dashcamu T-148** → T-158 až T-161 závisí na T-148.
11. **T-162:** varianta a) – připnout trvalý měsíční build BtbN `autobuild-2026-10-31-12-59` (SHA-256 ověřený ve spiku).

## Poznámky k implementaci
Spike 2026-11-03 (architekt, macOS arm64). Úplná zpráva, tabulka variant, měření a zdroje: **`docs/timelapse-spike.md`**.
Prototypy jen ve scratchpadu, v repozitáři žádný kód.

**Hlavní zjištění**
- Předpoklad z `## Cíl` („nejde udělat bez překódování“) spike vyvrátil: pro kameru Buddy3D to bez překódování jde (varianta D).
- Kamera Buddy3D: každý klíčový snímek je IDR se SPS/PPS v proudu, jinak jen P-snímky, `max_num_ref_frames = 1`,
  interval IDR 2,10–2,38 s (5 min živě), IDR ~65 kB. MP4 jen z IDR s novými razítky (bez enkodéru) přehraje FFmpeg
  (přísný režim) i AVFoundation a snímky jsou bitově shodné s plným dekódováním (`framemd5` 26/26). Sousední IDR
  se stejným `idr_pic_id` (cyklus 0–15) nevadí ani jednomu dekodéru. Windows (Media Foundation) zatím neověřeno.
- Velikost za 10 h po 30 s: D ~78 MB, MJPEG q5 132 MB, `mpeg4` q5 70 MB, VideoToolbox H.264 44 MB, JPEG 192 MB, PNG 1,1 GB.
- CPU: D nepřidá nic nad příjem RTSP (~1,7 % jádra, T-144). Varianty s enkodérem navíc potřebují dekódování
  (dnes všech snímků 2,35 % jádra, jen klíčové 0,15 %) a kopie BGRA (~11 MB). Kódování samo stojí 4–5 ms na snímek
  (MJPEG / `mpeg4`), VideoToolbox ~0 ms v procesu, PNG 106 ms.
- Binárka macOS navíc: D 0 B, mjpeg +232 kB, `mpeg4` +431 kB, VideoToolbox +84 kB, png +17,7 kB (vlastní buildy
  `build-ffmpeg.sh`, všechny LGPL v3+). Demuxer `mov` (pro variantu F) už v buildu je (vybírá ho `rtpdec`).
  Windows BtbN obsahuje `mjpeg`, `png`, `mpeg4`, `h264_mf`, `libopenh264`, `h264_amf` / `nvenc` / `qsv`
  (`strings avcodec-63.dll`), tedy 0 B navíc.
- Licence: enkodéry FFmpeg i obaly VideoToolbox / Media Foundation jsou LGPL (hlavičky zdrojů 9.0.2), API OS spadá
  pod „System Libraries“ GPLv3. OpenH264: kód BSD-2-Clause, patentová licence Cisco jen pro binárku staženou od Cisca
  (FAQ openh264.org), u vlastního nebo statického buildu ne. Rozhodnutí o patentech H.264 / MPEG-4 enkodérů
  (C4, E) patří uživateli. Varianta D nic nekóduje.
- AOT: žádná varianta nepřidává spravovaný balíček. D, A2, B, C1, C2, E, F jdou přes stávající P/Invoke FFmpeg,
  A1 přes Avalonii (`JpegBitmapEncoderOptions`, už v AOT publish).
- Spouštěč po vrstvě: `axis_z` = `logical_curr_pos[2]` (cíl zařazeného pohybu, 1 desetinné místo, včetně z-hopu).
  Číslo vrstvy PrusaLink na Buddy nehlásí. `PUT /api/v1/job/{id}/continue` Buddy neimplementuje (400), `/api/v1/cameras`
  je 404. Core One profil: `retract_lift = 0.2`, `travel_max_lift = 1.5`. Simulátor má Z lineární, bez vrstev.
  Skutečnou tiskárnu agent nepolloval (heslo). Návrh detektoru a ověření na záznamu je v T-159.
- Vedlejší nález: BtbN smazal připnutý denní build (drží jen ~2 týdny denních a poslední build každého měsíce) → T-162.

**Navržené úkoly** (implementují se až po odpovědích; všechny závisí na T-149):
| ID | Název | Owner | Závisí na |
|---|---|---|---|
| T-158 | Timelapse – zápis z klíčových snímků bez překódování (Core + Camera.FFmpeg) | core-dev | T-149, T-144 |
| T-159 | Detekce změny vrstvy z osy Z (PrusaLink `axis_z`) a spouštěč timelapse po vrstvě | core-dev + ui-dev | T-149, T-160, T-161 |
| T-160 | Timelapse – služba v Core (spouštěč po čase, automaticky při tisku, složka, rotace) | core-dev | T-158, T-145 |
| T-161 | Timelapse – UI (nastavení, tray menu, notifikace) a E2E | ui-designer + ui-dev | T-160, T-146 |
| T-162 | FFmpeg pro Windows – připnutý build BtbN už neexistuje (404) | core-dev | – |

Uzavřeno 2026-11-04: výstup spiku (zpráva `docs/timelapse-spike.md` a rozhodnutí uživatele) je hotový. T-158 až T-162
jsou upravené podle rozhodnutí (T-158 až T-161 navíc závisí na T-148) a přibyly T-164 (timelapse z hotové nahrávky)
a T-165 (export snímků JPEG).
