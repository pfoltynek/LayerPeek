# Detekce chyb tisku z kamery – výsledky průzkumu (spike T-152)

Stav k 2026-11-03, rozhodnutí uživatele 2026-11-04 v oddílu 7. Průzkum vedl agent architect. Prototypy běžely jen mimo
repozitář. Do repozitáře nepřibyla žádná závislost ani produkční kód. Otázky a rozhodnutí jsou v
[T-152](../tasks/done/T-152-print-failure-detection-spike.md).

## 1. Data

| Zdroj | Obsah | Kde |
|---|---|---|
| Snímky po 30 s | 214 JPEG 1280×720 z kamery Buddy3D (standardní držák) na Core One uživatele, tisk 2026-11-03 od ~18:00, snímky 18:13–20:00 | `~/LayerPeek-samples/coreone-frames/` (mimo repozitář) |
| Klip | 59 s H.264 1280×720, 21,8 fps, ~1 Mbit/s, klíčový snímek každých ~2,3 s (`ffprobe -skip_frame nokey`) | `~/LayerPeek-samples/clips/coreone-20261103-1905-60s.mp4` |
| Fixture v repozitáři | 10 zmenšených snímků 640×360 JPEG, celkem 480 KB, jen vnitřek tiskárny (rozhodnutí uživatele 2026-11-03, otázka 4) | `tests/LayerPeek.Detection.Tests/Fixtures/coreone/` |

Fixture (projekt `LayerPeek.Detection.Tests` vznikne v T-153, složka je připravená dopředu):
- `series-20261103-181335.jpg`, `series-20261103-183335.jpg`, `series-20261103-191337.jpg`, `series-20261103-200006.jpg` – průběh
  tisku (růst dílu, pohyb podložky v Z, různé polohy hlavy),
- `clip-1905-key01.jpg` až `clip-1905-key06.jpg` – šest po sobě jdoucích klíčových snímků klipu (odstup ~2,3 s), vhodné
  na test pozadí bez pohyblivé hlavy (běžící medián).

**Chybí:** záběry první vrstvy (snímání začalo ~13 min po startu tisku), záběry skutečného selhání (špagety, odlepení),
tisk tmavým filamentem a tisk s vypnutým osvětlením komory. Bez nich jsou čísla v oddílu 4 jen orientační.

## 2. Co je z výchozí polohy kamery vidět (Core One + Buddy3D)

Vlastní prohlídka snímků (např. `frame-20261103-181335.jpg`, `-183335.jpg`, `-195606.jpg` a výřez paty dílu):

- **Kamera je pevně na rámu a dívá se šikmo shora**, širokoúhlý objektiv se zkreslením na okrajích. Podložka je vidět
  zhruba z 85 %, přední okraj je oříznutý dolním okrajem obrazu.
- **Podložka se během tisku pohybuje v Z** (Core One je CoreXY se stolem v ose Z). Snímek prázdné podložky ze začátku
  tisku proto později neodpovídá: rozdíl mediánu prvních a posledních snímků ukazuje posunutou mřížku na celé podložce.
  Přední polovina podložky se ale mezi sousedními vzorky posouvá jen málo. Fázová korelace výřezu podložky dává
  posun nejvýš ~1 px při 320×180 (krátkodobě až ~7 px, když do výřezu vjede hlava).
- **Hlava a celý most osy X se pohybují přes velkou část obrazu.** Mezi dvěma snímky po 1 s se změní ~14 % pixelů celého
  obrazu (medián). Ve výřezu přední části podložky jen 0,25 %, při průjezdu hlavy až 2,5 %. Rozdíl dvou snímků proto
  sám nestačí. Je potřeba pozadí bez pohyblivých částí, viz oddíl 4.
- **Tryska** je vidět z boku, ale špička je většinou zakrytá silikonovou čepičkou, ventilátorem nebo dílem.
  Filament namotaný na trysce („blob“) by se poznal jen jako zvětšení obrysu hlavy, a to z jediného úhlu.
- **Díl:** stříbrný / šedý filament na černém texturovaném plátu má vysoký kontrast (díl ~200, plát ~40–60 v šedotónu).
  Obrys dílu jde spolehlivě oddělit prahem. Plocha masky dílu v mediánu snímků rostla monotónně z 1 665 na 2 655 px
  (při 320×180) a těžiště zůstalo na ±1 px.
- **První vrstva:** pata dílu s límcem (brim / první vrstvy) je při 1280×720 vidět jako světlý pruh široký ~8–10 px,
  při 640×360 ~4–5 px, při 320×180 ~2 px. Jde tedy poznat, **zda a kde** první vrstva leží (stopa dílu), ne její
  **kvalitu** (přimáčknutí, mezery, podextruze). Na plátu je vedle dílu vidět opotřebená / zašpiněná ploška. Při
  srovnání s pevnou referencí by vyvolala falešný poplach, při srovnání s krátkodobým pozadím ne. Závěr: detektor
  „první vrstva leží / chybí / odtrhla se“ je pravděpodobně proveditelný, „první vrstva je špatně položená“ z této
  kamery ne. Ověřit to jde až na záběrech prvních minut tisku (otázka 6).
- **Osvětlení:** LED komory svítí stále, průměrný jas snímků za 1 h 47 min kolísal jen mezi 63 a 79 (σ 3,4). Pravá
  strana má odlesky LED pásku. Vypnuté osvětlení se nezkoušelo. Detektor proto musí hlídat jas a při tmě se pozastavit.
- **Komprese H.264** při ~1 Mbit/s je pro šedotón 320×180 bez potíží, artefakty jsou pod prahem změny (25 úrovní).

**Ověření předpokladu architekta:** platí. Důsledky selhání (díl zmizí nebo se posune, na podložce přibude nová
hmota) jsou na Core One dobře vidět, kvalita první vrstvy ne.

## 3. Varianty

### 3.1 Runtime / knihovny

| Varianta | Licence (primární zdroj) | Native AOT | Nativní knihovny | CPU na vzorek (M2 Pro, 1 vlákno) | Paměť navíc |
|---|---|---|---|---|---|
| **Čistý C# bez závislostí** (zmenšení 1280×720 BGRA → 320×180 šedotón + běžící medián + porovnání ve výřezu) | GPL-3.0-only (vlastní kód) | ano, AOT publish testovací aplikace na osx-arm64 bez varování | 0 MB | 1,36 ms (skalární kód bez SIMD, z toho zmenšení 1,2 ms) | ~0,2 MB (bufery 320×180) |
| **OpenCvSharp5** 5.0.0.20261018 + `OpenCvSharp5.runtime.osx.arm64` / `.runtime.win.slim` | wrapper Apache-2.0 ([LICENSE](https://github.com/shimat/opencvsharp/blob/main/LICENSE), nuspec `Apache-2.0`), OpenCV Apache-2.0 ([opencv/opencv](https://github.com/opencv/opencv/blob/4.x/LICENSE)). Balík pro macOS má **staticky přilinkované FFmpeg (LGPL-2.1) a Tesseract** ([README](https://github.com/shimat/opencvsharp/blob/main/README.md)). Statické linkování LGPL v naší distribuci = povinnost umožnit přelinkování, nutné posoudit | AOT publish na osx-arm64 bez varování. Nativní knihovna se ale načetla jen z pracovního adresáře, ne ze složky aplikace, takže by byl potřeba vlastní `NativeLibrary.SetDllImportResolver` (jako u FFmpeg, T-052) | osx-arm64 **99,0 MB**, win-x64 slim **65,0 MB** (plný 82,5 MB + 30,9 MB `opencv_videoio_ffmpeg`) | 0,08 ms (resize + gray) | nezměřeno |
| **ONNX Runtime** `Microsoft.ML.OnnxRuntime` 1.31.1 | MIT ([microsoft/onnxruntime](https://github.com/microsoft/onnxruntime/blob/main/LICENSE)) | AOT publish na osx-arm64 bez varování, inference proběhla. Pozor: balík kopíruje i `onnxruntime.dll` pro Windows do výstupu osx-arm64, potřeba vyloučit | osx-arm64 **44,8 MB**, win-x64 **17,3 MB** | podle modelu, viz 3.2 | podle modelu |
| Emgu CV | dvojí licence: open source GPL v3 / komerční ([LICENSE](https://github.com/emgucv/emgucv/blob/EMGUCV_5_0/LICENSE)) | neověřeno | neměřeno (obaluje OpenCV jako OpenCvSharp) | – | – |

Win-x64 AOT publish s ONNX Runtime ani s OpenCvSharp se na Macu ověřit nedá (AOT se překládá na cílovém OS). Pokud
uživatel některou knihovnu vybere, ověří se v implementačním úkolu na Windows.

### 3.2 Modely ML

| Model | Kód | Váhy | Trénovací data | Velikost | Inference (ORT, 1 vlákno, M2 Pro) | Paměť navíc | Použitelné v GPL-3.0-only? |
|---|---|---|---|---|---|---|---|
| **Obico** (dříve The Spaghetti Detective), YOLO-like detektor „failure“, vstup 416×416 | AGPL-3.0 ([obico-server](https://github.com/TheSpaghettiDetective/obico-server/blob/release/LICENSE)) | nejsou v repozitáři, stahují se z `obico.io` ([model-weights.onnx.url](https://github.com/TheSpaghettiDetective/obico-server/blob/release/ml_api/model/model-weights.onnx.url)). **Licence vah není uvedena**, [podmínky webu](https://www.obico.io/terms.html) jen zakazují užití duševního vlastnictví bez svolení | neveřejná | **202 MB** | **321 ms** | ~340 MB | **ne** (licence vah chybí) |
| **PrintGuard** / Edge-FDM-Fault-Detection, kodér ShuffleNetV2 + nejbližší prototyp (úspěch / selhání), vstup 224×224 | **GPL-2.0-only** ([pyproject.toml](https://github.com/oliverbravery/PrintGuard/blob/main/pyproject.toml) `license = "GPL-2.0-only"`, [Edge-FDM-Fault-Detection](https://github.com/oliverbravery/Edge-FDM-Fault-Detection/blob/main/LICENSE.md)) | v repozitáři (`models/encoder_float32.onnx`), pod licencí repozitáře GPL-2.0-only | 7 sad z Roboflow Universe a Kaggle ([README](https://github.com/oliverbravery/Edge-FDM-Fault-Detection#dataset-preparation)), licence sad se nepodařilo ověřit (stránky vrací 403 / vyžadují JavaScript) | **5,0 MB** | **4,9 ms** | ~46 MB (včetně načteného ORT) | **ne** (GPL-2.0-only je s GPL-3.0 nekompatibilní, výjimku by musel udělit autor) |
| Modely na Roboflow Universe trénované v Ultralytics YOLO | AGPL-3.0 ([ultralytics LICENSE](https://github.com/ultralytics/ultralytics/blob/main/LICENSE)) | Ultralytics tvrdí, že AGPL-3.0 kryje i natrénované váhy ([ultralytics.com/license](https://www.ultralytics.com/license), [issue #2129](https://github.com/ultralytics/ultralytics/issues/2129)) | podle sady (často CC BY 4.0, u každé zvlášť ověřit) | typicky 6–25 MB | nezměřeno (YOLOv8n ~ desítky ms) | – | **sporné**: AGPL-3.0 jde s GPL-3.0 kombinovat (GPLv3 § 13), ale ta část si podrží síťovou povinnost AGPL. Rozhodne uživatel (otázka 3) |
| Vlastní trénink (např. MobileNetV3 / ShuffleNetV2 z torchvision, BSD-3-Clause) na záběrech uživatele a sadách s CC BY | vlastní (GPL-3.0-only) | vlastní, licenci určí uživatel. Předtrénované váhy ImageNet z torchvision mají vlastní podmínky (ImageNet jen pro výzkum), ověřit, pokud se použijí | vlastní záběry + sady s ověřenou licencí | 2–6 MB | ~5 ms (odhad podle PrintGuard) | ~46 MB s ORT | ano, ale je potřeba **stovky až tisíce označených snímků selhání z Core One**. Trénovací nástroje se nedistribuují |

Měření: AOT aplikace `Bench` ve scratch adresáři (mimo repozitář), `IntraOpNumThreads = 1`, 10 inferencí po zahřátí,
Apple M2 Pro, .NET SDK 11.0.100-rc.3. Paměť je rozdíl `Environment.WorkingSet` proti běhu bez modelu.

**Závěr k ML:** hotový model s licencí vah, kterou uživatel přijal (MIT, Apache-2.0…), se nenašel. Jediný malý
a rychlý model (PrintGuard) je GPL-2.0-only. Všechny modely navíc vznikly na záběrech jiných tiskáren, většinou
zepředu (MK3 / Ender). Na šikmý pohled Core One s pohyblivým mostem X by se musely dotrénovat. ML má smysl až jako
fáze 2, pokud by klasický detektor dával moc falešných poplachů a uživatel nasbíral data (T-153 ukládá snímky poplachů
a falešných poplachů).

## 4. Prototyp klasického detektoru na skutečných datech

Postup (Python + OpenCV jen pro rychlý pokus, výsledná implementace by byla čistý C#, viz 3.1):

1. Snímek → šedotón 320×180 (průměr bloků 4×4).
2. **Pozadí bez pohyblivých částí:** přibližný běžící medián, kde se každý pixel pozadí posune o nejvýš ±K úrovní
   k aktuálnímu snímku (McFarlane & Schofield, 1995). Nemá historii snímků, stojí O(1) na pixel a nealokuje. Na 59
   vzorcích klipu (1 za 1 s) z pozadí zmizela hlava i most X, zůstala podložka a díl.
3. **Skóre** = podíl pixelů výřezu přední části podložky (dolních 45 % obrazu, prostředních 70 % šířky), kde se
   současné pozadí liší od pozadí před ~5 min o víc než 25 úrovní (po rozmazání 3×3).

Výsledky na 214 snímcích (30 s odstup, celý zachycený tisk bez selhání) a s uměle vloženými chybami od vzorku 150:

| Scénář | Skóre (podíl změněných pixelů výřezu) | Poznámka |
|---|---|---|
| Úspěšný tisk | max **1,44 %**, p99 1,41 % | pomalý růst dílu a posun podložky v Z |
| Uměle „špagety“ (25 tenkých světlých křivek vedle dílu, narůstají 3 min) | max **3,9 %**, nad 2× maximem úspěšného tisku po 9 vzorcích | syntetické, skutečné špagety bývají větší |
| Uměle „odlepený / posunutý díl“ (díl zmizí a objeví se o 25 px vedle) | max **19,7 %**, nad 2× maximem po 2 vzorcích | syntetické |

Rozdíl mediánů 5 po sobě jdoucích snímků ve výřezu: 0,42 % (p95 0,6 %), samotné snímky 0,40 % (p95 2,2 %, když vjede
hlava). Hranice mezi „normálně“ a „selhání“ je tedy zřetelná u posunutého dílu a jen těsná u malého množství
špaget. **Jde o syntetické chyby, skutečná spolehlivost se musí změřit na záběrech skutečných selhání** (otázka 7).

Výkon čistého C# (AOT, osx-arm64, M2 Pro, skalární kód bez SIMD a bez optimalizace):

| Krok | Čas na vzorek |
|---|---|
| zmenšení 1280×720 BGRA → 320×180 šedotón | 1,2 ms |
| + běžící medián + skóre výřezu | 1,36 ms celkem |
| alokace v ustáleném stavu | 0 (1 312 B za 2 000 iterací = jen měření) |

Při 1 vzorku za 2 s to je ~0,07 % jednoho jádra. Na starším notebooku x64 odhadem 3× víc, pořád pod 0,3 %.

**Největší náklad detekce je dekódování streamu, ne detektor.** FFmpeg na jednom vlákně (`-threads 1`, Homebrew FFmpeg,
M2 Pro):

| Dekódování 59 s klipu | CPU | Podíl jednoho jádra |
|---|---|---|
| všechny snímky (21,8 fps) | 1,09 s | ~1,9 % |
| jen klíčové snímky (`skip_frame = AVDISCARD_NONKEY`, 1 za ~2,3 s) | 0,075 s | ~0,13 % |

Dnes hub (T-143) dekóduje všechny snímky, i když je jediným odběratelem detekce. Navíc každému odběrateli kopíruje
celé snímky BGRA 1280×720 do tří buferů (~11 MB na odběratele). Pro tray aplikaci na pozadí to jsou dvě hlavní úspory
(otázky 9 a 10):
- **jen klíčové snímky**, když je detekce jediný odběratel (okno kamery zavřené, nenahrává se). Interval klíčových
  snímků Buddy3D (~2,3 s) zhruba odpovídá požadované frekvenci,
- **zmenšení při kopii:** odběr snímků s volbou cílové velikosti / šedotónu (např. kanál Y z YUV420 před převodem na
  BGRA) místo kopie celého BGRA snímku. Paměť ~3 × 57,6 KB místo ~11 MB.

## 5. Doporučení

1. **První detektor: klasické CV bez nové závislosti, čistý C# v `LayerPeek.Detection`.** Pracovní název „Změna na
   podložce“ (`bed-change`): na podložce zmizí / posune se díl nebo přibude nová hmota (špagety, odtržený díl). Odpovídá
   doporučení architekta před spikem a potvrdily ho snímky. Žádné nativní knihovny, žádná otázka licence vah, Native AOT
   bez rizika, výkon v řádu desetin procenta jádra.
2. **Vzorkování:** 1 vzorek za ~2 s (na Buddy3D klíčový snímek, ~2,3 s), šedotón **320×180** pro analýzu. Snímek
   poplachu se ukládá z plného rozlišení posledního snímku. 640×360 pro analýzu nepřináší u tohoto detektoru nic,
   bude potřeba až pro případný detektor první vrstvy (stopa první vrstvy má při 320×180 jen ~2 px).
3. **Kontext:** detektor potřebuje `JobId` (reset při nové úloze), dobu od začátku tisku nebo od obnovení (rozjezd
   pozadí ~1–2 min, v té době bez poplachu), `Telemetry.AxisZ` (jen informativně, posun podložky řeší krátkodobé
   srovnání) a průměrný jas (při tmě se pozastavit). Číslo vrstvy PrusaLink nedává, stačí Z.
4. **Historie:** detektor si drží vlastní pozadí (57,6 KB) a několik starších kopií pozadí pro srovnání „před ~5 min“
   (např. kruhový seznam 3 kopií po 2,5 min, ~170 KB). Referenční snímek prázdné podložky **nepotřebuje** a kvůli pohybu
   podložky v Z ho ani použít nejde.
5. **Potvrzení poplachu:** skóre nad prahem v K z N vzorků (návrh 5 z 8 při 2 s = ~15 s). Výchozí práh odhadem 3 %
   změněných pixelů výřezu (2× maximum úspěšného tisku), citlivost ho posouvá zhruba v rozsahu 2–6 %. Čísla upřesní
   T-155 na skutečných datech.
6. **ML až později** (T-156 nebo samostatný spike), pokud klasický detektor nestačí a uživatel nasbírá data z poplachů.
   Runtime by byl ONNX Runtime (MIT, AOT bez varování, +17 MB win-x64 / +45 MB osx-arm64). OpenCvSharp kvůli velikosti
   (65–99 MB) a staticky přilinkovanému FFmpeg na macOS nedoporučuji.
7. **První vrstva:** nedoporučuji jako první detektor. Jako druhý detektor připadá v úvahu „stopa první vrstvy chybí
   nebo se odtrhla“ (porovnání pozadí po první vrstvě s pozadím před ní, výřez podle polohy dílu). Rozhodnout až podle
   záběrů začátku tisku.

## 6. Rozhraní detektoru pro T-153 (návrh)

Návrh pro `Core/Detection`. Názvy jsou orientační, upřesní je core-dev v T-153:

```csharp
public interface IPrintFailureDetector
{
    string Id { get; }                          // stable, e.g. "bed-change"; settings key
    DetectionFrameSpec FrameSpec { get; }       // e.g. Gray8 320x180
    void Reset(in PrintFailureContext context); // new job, resume after pause, camera reconnect
    DetectionResult Analyze(in DetectionSample sample); // synchronous, CPU only, no allocation in steady state
}

public readonly ref struct DetectionSample
{
    public ReadOnlySpan<byte> Pixels { get; }   // Gray8, rows top-down, borrowed until Analyze returns
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public long Timestamp { get; }              // TimeProvider timestamp
    public PrintFailureContext Context { get; } // JobId, AxisZ, ProgressPercent, PrintElapsed
}

public readonly record struct DetectionResult(bool IsWarmedUp, float Score); // Score 0..1; Region for the snapshot later
```

- Detektor nevolá síť, nečte soubory a nezná čas jinak než přes `Timestamp`. Díky tomu jde testovat nad fixture
  snímky deterministicky.
- Orchestrace (T-153) dělá vzorkování, zmenšení podle `FrameSpec`, převod citlivosti na práh, potvrzení K z N,
  pozastavení při tmě, snímek poplachu a událost. Detektor vrací jen skóre.
- Výřez podložky (ROI) je podle rozhodnutí 2026-11-04 editovatelný u každé tiskárny (oddíl 7), detektor ho dostává
  od orchestrace.

## 7. Rozhodnutí uživatele (2026-11-04)

- **Přístup:** klasické CV v čistém C# bez nové závislosti, první detektor „Změna na podložce“ (`bed-change`) → T-155.
- **Data:** záběry první vrstvy a uměle vyvolaných selhání uživatel ze skutečných tisků plánovaným způsobem nepořídí.
  Práh a ověření spolehlivosti proto stojí na syntetických / inscenovaných datech (zdroj vybere uživatel, otázka 3
  v T-155). Detektor první vrstvy (T-156) čeká na jiný zdroj dat.
- **Výřez podložky:** editor výřezu už v první verzi (obdélník nad náhledem kamery v nastavení tiskárny, T-154), model
  v normalizovaných souřadnicích u tiskárny (T-153). Výchozí výřez je celý snímek, u Core One se předvyplní výřez
  z prototypu (dolních 45 % výšky, prostředních 70 % šířky). Automatické nalezení podložky může přijít později.
- **Výkon:** dekódování jen klíčových snímků a zmenšení / šedotón při kopii v hubu schválené. Vzorek jen jednou za 2–5 s
  (nastavitelné, výchozí 2 s) a jen ve stavu `Printing`. Mimo tisk se nic nedekóduje ani nepočítá → T-153.
- **Tmavý filament na černém plátu:** zatím známé omezení, řešení později v T-163.
- **ML (fáze 2) a žádost o licenci PrintGuard:** odloženo.
