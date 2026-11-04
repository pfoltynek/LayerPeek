---
id: T-152
title: Detekce chyb tisku z kamery – průzkum (spike): klasické CV vs. ML, licence, AOT, proveditelnost na Core One
status: done
owner: architect
priority: P2
platform: all
depends_on: []
---

## Cíl
Uživatel chce automaticky poznat selhání tisku z obrazu kamery (nápad 3). Tiskárna uživatele je **Prusa Core One** s kamerou v **standardní montážní poloze**. Začít chce detekcí chyb **první vrstvy**, další detektory přidávat postupně a každý typ detektoru jde zapnout / vypnout (pravděpodobně i s citlivostí).

Hodnocení architekta, které uživatel dostal: kvalita první vrstvy je z boční / šikmé kamery špatně vidět; důsledky selhání (odlepený nebo posunutý díl, filament namotaný na trysce, „špagety“) se poznají mnohem snáz. Spike má tento předpoklad **ověřit na skutečném záběru Core One** a doporučit první detektor. Neimplementuje produkční kód; výsledek předloží uživateli k rozhodnutí.

## Rozsah
- **Data:** záběry z kamery Core One uživatele – úspěšné tisky, první vrstvy, uměle vyvolané chyby (odlepení, špagety). Ideálně z nahrávání T-144 / T-145 (pokud bude hotové; není to závislost), jinak ručně pořízené záznamy. Data zůstávají lokálně, do repozitáře jde jen to, co uživatel výslovně schválí (soukromí, velikost; fixture pro testy musí být malé).
- **Klasické CV (bez ML):** např. rozdíl snímků v oblasti podložky, hustota hran / textury v ROI nad tiskem, porovnání s očekávanou výškou z `Telemetry.AxisZ`. Implementace čistě v .NET bez závislostí, nebo přes knihovnu (např. OpenCV přes OpenCvSharp) – u knihovny ověřit licenci (OpenCV 4.5+ Apache-2.0, OpenCvSharp Apache-2.0 – ověřit v primárních zdrojích), kompatibilitu s Native AOT a trimováním, velikost nativních knihoven na win-x64 a osx-arm64.
- **ML (inference):** např. ONNX Runtime (`Microsoft.ML.OnnxRuntime`, MIT – ověřit) – kompatibilita s Native AOT (P/Invoke vs. reflexe, varování analyzátorů), velikost nativních knihoven, CPU / paměť při ~1 snímku za 1–2 s, dostupnost pro osx-arm64.
- **Modely a datasety – licence ověřit, nepředpokládat:**
  - existující open-source projekty v této oblasti (např. Obico / dříve The Spaghetti Detective, další na GitHubu, datasety na Roboflow Universe / Kaggle) – licence kódu, **licence vah modelu** a **licence trénovacích dat** zvlášť; kompatibilita s **GPL-3.0-only** (AGPL, licence „jen nekomerční“, CC BY-NC apod. mohou být nekompatibilní – ověřit u každého zdroje s odkazem),
  - otázka „preferované formy pro úpravy“ u vah modelu v GPL projektu (zda stačí váhy, nebo je nutné zveřejnit i trénovací kód a data) – sepsat jako otázku pro uživatele,
  - vlastní trénink na datech uživatele: náročnost, nástroje (mimo aplikaci, licence nástrojů nevadí, pokud se nedistribuují).
- **Proveditelnost na Core One:** co je z výchozí polohy kamery vidět (celá podložka? tryska? první vrstva při jakém osvětlení?), vliv LED osvětlení komory, rozlišení 1280×720 a komprese H.264.
- **Vstup pro infrastrukturu (T-153):** jaké snímky detektor potřebuje (rozlišení, barva / šedotón, ROI, frekvence), jaký kontext tisku (Z, progress, číslo vrstvy, čas od začátku tisku), zda detektor potřebuje historii snímků (referenční snímek prázdné podložky / začátku tisku).
- **Výstup:** tabulka variant (licence s odkazy na primární zdroje, AOT, velikost, CPU, očekávaná spolehlivost na Core One), doporučený první detektor a přístup, upravený rozpad T-153 až T-156, otázky pro uživatele. Vše česky do tohoto úkolu, shrnutí do `docs/architecture.md` (oddíl „Plánované: … detekce chyb tisku“).

## Akceptační kritéria
- [x] Porovnání klasického CV a ML s ověřenými licencemi (kód, váhy, data) a odkazy na primární zdroje. *(Kód a váhy ověřené z primárních zdrojů, viz `docs/detection-spike.md` 3.2. Licence trénovacích sad na Roboflow Universe / Kaggle se ověřit nepodařilo, stránky vrací 403 nebo vyžadují JavaScript. Uživatel 2026-11-04 zvolil klasické CV bez ML a ML odložil (otázky 1 a 12), licence trénovacích sad proto teď potřeba nejsou. Při případném návratu k ML je ověří nový úkol.)*
- [x] Ověřená kompatibilita navrhovaných runtime knihoven s Native AOT (pokus o AOT publish malé zkušební aplikace mimo `src/`, výsledek a varování v poznámkách) a s GPL-3.0-only. *(osx-arm64: ONNX Runtime 1.31.1, OpenCvSharp5 i čistý C# bez varování. Uživatel 2026-11-04 schválil variantu bez nové závislosti (otázka 2), ověření knihoven na win-x64 proto odpadá. Čistý C# kompiluje T-153 / T-155 na obou OS.)*
- [x] Posouzení proveditelnosti detekce první vrstvy na záběru Core One (s ukázkovými snímky, pokud je uživatel poskytne). *(Posouzeno na stopě první vrstvy hotového dílu: stopa je vidět, kvalita ne. Záběry prvních minut tisku uživatel plánovaným způsobem nepořídí (rozhodnutí 2026-11-04, otázka 6). Detektor první vrstvy v T-156 proto čeká na jiný zdroj dat.)*
- [x] Doporučení prvního detektoru a seznam otázek pro uživatele.
- [x] Rozhraní detektoru pro T-153 popsané (vstupy / výstupy), T-153 až T-156 upravené podle výsledku.

## Otevřené otázky
1. **Klasické CV, nebo ML?** Rozhodne uživatel po spiku.
2. **Nové závislosti** (OpenCV / ONNX Runtime / jiné) – jaké a zda vůbec; zvětší instalaci (nativní knihovny pro každou platformu).
3. **Licence vah modelu** v projektu GPL-3.0-only: stačí uživateli váhy s kompatibilní licencí, nebo chce jen modely s veřejnými trénovacími daty a kódem (nebo vlastní trénink)?
4. **Data z kamery uživatele:** smí se malé ukázky (snímky) uložit do repozitáře jako fixture pro testy? Jak velké a které?
5. **První detektor:** potvrdit po spiku (doporučení architekta předem: špagety / odlepení dílu, první vrstva až podle výsledku spiku).

#### Otázky po spiku (2026-11-03)
Doporučení k otázkám 1 a 5: **klasické CV v čistém C# bez nové závislosti** a jako první detektor **„Změna na podložce“** (`bed-change`, zmizení nebo posun dílu a nová hmota na podložce, tedy špagety nebo odtržený díl). K otázce 2: žádná nová závislost. Pokud se později přidá ML, pak ONNX Runtime (MIT), ne OpenCvSharp. Podklady jsou v `docs/detection-spike.md`.

6. **Záběry první vrstvy:** pořídí uživatel při příštím tisku snímky prvních ~15 minut (ideálně po 2–5 s)? Bez nich nejde rozhodnout, zda detektor „stopa první vrstvy chybí / odtrhla se“ (T-156) dává smysl.
7. **Záběry selhání:** je uživatel ochoten selhání uměle vyvolat a nahrát (např. shodit díl během tisku, tisk bez přilnavosti, „tisk ve vzduchu“)? Bez nich nejde ověřit spolehlivost ani nastavit výchozí práh (T-155, otázka 1). Prototyp měl jen syntetické chyby.
8. **Výřez podložky (ROI):** pevný výřez pro Core One a výchozí polohu kamery, nebo ho má uživatel vyznačit v nastavení (UX v T-154)? Podporovat hned i jiné tiskárny a kamery (MK4, XL), nebo zatím jen Core One?
9. **Dekódování jen klíčových snímků**, když je detekce jediným odběratelem streamu (okno kamery zavřené, nenahrává se)? Změna v `Camera.FFmpeg` a hubu (T-143). Úspora při M2 Pro ~1,8 % jednoho jádra na tiskárnu (1,9 % → 0,13 %), vzorek pak chodí po intervalu klíčových snímků kamery (Buddy3D ~2,3 s).
10. **Zmenšení / šedotón už při kopii v hubu** (nová volba odběru snímků, případně přímo kanál Y z dekodéru) místo kopie celých snímků BGRA 1280×720 do tří buferů? Paměť ~11 MB → ~0,2 MB na tiskárnu.
11. **Tmavý filament:** tiskne uživatel i černým / tmavým filamentem na černém plátu? Kontrast pak klesá. Stačí to uvést jako známé omezení v popisu detektoru, nebo je to pro první detektor blokující?
12. **ML ve fázi 2:** plánovat ho vůbec (ONNX Runtime +17 MB na Windows / +45 MB na Macu, vlastní trénink na sesbíraných snímcích)? Hotový model s licencí vah, kterou uživatel přijal, se nenašel: Obico nemá licenci vah, PrintGuard je GPL-2.0-only. Jsou pro uživatele přijatelné váhy pod **AGPL-3.0** (modely z Ultralytics, kombinace podle GPLv3 § 13)?
13. **PrintGuard (GPL-2.0-only):** chce uživatel požádat autora o licenci kompatibilní s GPL-3.0 (např. „GPL-2.0-or-later“) pro model 5 MB? Agent nikoho nekontaktuje.

### Rozhodnutí uživatele 2026-11-03
2. Nové závislosti spike **jen doporučí** (s licencí a AOT); prototyp jen mimo repozitář, rozhodne uživatel.
3. Licence vah modelu: stačí **kompatibilní licence vah** (MIT, Apache-2.0…), veřejná trénovací data nejsou podmínkou.
4. Fixture: smí se uložit **nejvýš ~10 zmenšených snímků** (640×360 JPEG, celkem do ~1 MB) jen s vnitřkem tiskárny.
- Záběry: 2026-11-03 se z kamery uživatele (`rtsp://192.168.0.227/live`, Core One, standardní poloha) ukládá snímek každých 30 s během skutečného tisku (lokálně, mimo repozitář).

### Rozhodnutí uživatele 2026-11-04
1. a 5. **Přístup a první detektor schválené:** klasické CV v čistém C#, první detektor „Změna na podložce“ (`bed-change`). → T-155.
2. **Žádná nová závislost.**
6. a 7. **Záběry první vrstvy a uměle vyvolaných selhání** uživatel ze skutečných tisků plánovaným způsobem nepořídí („záběry vytvoříme jinak“). Práh a ověření spolehlivosti proto nesmí záviset na nahrávkách selhání od uživatele. T-155 počítá se syntetickými / inscenovanými testovacími daty (návrhy zdrojů jsou v otázkách T-155). Detektor první vrstvy (T-156) čeká, dokud nebude jiný zdroj dat.
8. **Výřez podložky (ROI):** uživatel podporuje různé typy tiskáren a různé držáky kamery. Editor výřezu proto patří už do první verze detekce: obdélník nakreslený nad náhledem kamery v nastavení tiskárny, výchozí výřez je celý snímek. U Core One se standardním držákem je výřez předvyplněný. Model výřezu → T-153, editor → T-154. Automatické nalezení podložky v obraze může přijít později (zatím bez úkolu).
9. a 10. **Dekódování jen klíčových snímků a zmenšení / šedotón v hubu schválené.** Navíc: detekce vzorkuje jen jednou za 2–5 s (nastavitelné v tomto rozsahu, rozumná výchozí hodnota) a běží **jen během tisku**. Bez tisku se nic nedekóduje a nic nepočítá. → T-153.
11. **Tmavý filament na tmavém plátu:** zatím známé omezení v popisu detektoru, vyřeší se později → nový úkol T-163 (P3, závisí na T-155).
12. **ML ve fázi 2:** odloženo.
13. **Žádost o licenci PrintGuard:** odloženo spolu s ML.

## Výsledky spiku (2026-11-03)
Podrobně v [`docs/detection-spike.md`](../../docs/detection-spike.md), shrnutí v `docs/architecture.md`.

- **Viditelnost (Core One, Buddy3D, standardní držák):** šikmý pohled shora, podložka vidět asi z 85 %, přední okraj oříznutý. Podložka jezdí v Z, takže pevný referenční snímek nefunguje, krátkodobé srovnání (~5 min) ano. Hlava a most X zabírají velkou část obrazu (mezi snímky po 1 s se změní ~14 % pixelů celého obrazu). Běžící medián je ale spolehlivě odstraní. Světlý filament na černém plátu má vysoký kontrast. Stopa první vrstvy je vidět (~8–10 px při 1280×720), její kvalita ne. Tryska je většinou zakrytá. Jas LED komory je stabilní (σ 3,4 za 1 h 47 min).
- **Předpoklad architekta potvrzen:** důsledky selhání (díl zmizí nebo se posune, přibude hmota) jsou dobře vidět, kvalita první vrstvy ne.
- **Prototyp klasického CV** (běžící medián + změna ve výřezu podložky proti pozadí před ~5 min) na 214 skutečných snímcích: úspěšný tisk max 1,44 % změněných pixelů. Syntetické špagety 3,9 % (nad 2× maximem po 9 vzorcích), syntetický posunutý díl 19,7 % (po 2 vzorcích). Čistý C# (AOT, M2 Pro): 1,36 ms na vzorek, 0 alokací v ustáleném stavu, při 1 vzorku za 2 s ~0,07 % jádra.
- **Hlavní náklad je dekódování streamu**, ne detektor: plné dekódování 1280×720 při 21,8 fps ~1,9 % jádra, jen klíčové snímky ~0,13 % (otázka 9). Hub dnes kopíruje celé snímky BGRA, ~11 MB na odběratele (otázka 10).
- **ML:** Obico (kód AGPL-3.0, váhy bez licence, 202 MB, 321 ms, ~340 MB RAM). PrintGuard (GPL-2.0-only, nekompatibilní, 5 MB, 4,9 ms, ~46 MB RAM). Ultralytics (AGPL-3.0 včetně vah podle Ultralytics). Žádný použitelný hotový model, otázky 12 a 13.
- **Runtime:** ONNX Runtime 1.31.1 (MIT, AOT bez varování na osx-arm64, nativní knihovny 17,3 MB win-x64 / 44,8 MB osx-arm64). OpenCvSharp5 (Apache-2.0, AOT bez varování, ale 65 MB win-x64 slim / 99 MB osx-arm64 se staticky přilinkovaným FFmpeg a Tesseractem). Čistý C# 0 MB.
- **Doporučení:** první detektor „Změna na podložce“ v čistém C# bez závislostí. Vzorek ~1 za 2 s, šedotón 320×180, výřez přední části podložky, rozjezd pozadí 1–2 min bez poplachu, potvrzení K z N (návrh 5 z 8), výchozí práh ~3 %. První vrstva jako možný druhý detektor (jen „stopa chybí / odtrhla se“) až podle záběrů (otázka 6). ML jen jako případná fáze 2.
- **Fixture:** 10 snímků 640×360 JPEG (480 KB) v `tests/LayerPeek.Detection.Tests/Fixtures/coreone/`, seznam a původ v `docs/detection-spike.md`, oddíl 1.

## Poznámky k implementaci
- Prototypy jen mimo repozitář (scratch adresář agenta): Python 3.9 venv s `numpy` 2.0.2 a `opencv-python-headless` 5.0.0 (jen k pokusům, nedistribuuje se) a AOT konzolová aplikace .NET 11 (`PublishAot=true`, `IsAotCompatible=true`) ve třech variantách: čistý C#, `Microsoft.ML.OnnxRuntime` 1.31.1 a `OpenCvSharp5` 5.0.0.20261018 + `OpenCvSharp5.runtime.osx.arm64`. Všechny `dotnet publish -r osx-arm64` prošly **bez varování IL2xxx / IL3xxx**. U ORT balík zkopíroval do výstupu osx-arm64 i Windows `onnxruntime.dll`. U OpenCvSharp se nativní knihovna v AOT načetla jen z pracovního adresáře.
- Modely Obico a PrintGuard byly staženy jen lokálně kvůli měření, do repozitáře nepatří a nejsou v něm.
- Do `.gitattributes` přibylo `*.jpg binary` kvůli fixture.
- Uzavřeno 2026-11-04: výstup spiku (zpráva `docs/detection-spike.md` a rozhodnutí uživatele) je hotový. T-153 až T-157 jsou upravené podle rozhodnutí a přibyl T-163 (tmavý filament na tmavém plátu). Otevřený zůstává zdroj syntetických / inscenovaných dat (T-155) a dat první vrstvy (T-156).
