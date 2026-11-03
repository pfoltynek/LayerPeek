---
id: T-152
title: Detekce chyb tisku z kamery – průzkum (spike): klasické CV vs. ML, licence, AOT, proveditelnost na Core One
status: todo
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
- [ ] Porovnání klasického CV a ML s ověřenými licencemi (kód, váhy, data) a odkazy na primární zdroje.
- [ ] Ověřená kompatibilita navrhovaných runtime knihoven s Native AOT (pokus o AOT publish malé zkušební aplikace mimo `src/`, výsledek a varování v poznámkách) a s GPL-3.0-only.
- [ ] Posouzení proveditelnosti detekce první vrstvy na záběru Core One (s ukázkovými snímky, pokud je uživatel poskytne).
- [ ] Doporučení prvního detektoru a seznam otázek pro uživatele.
- [ ] Rozhraní detektoru pro T-153 popsané (vstupy / výstupy), T-153 až T-156 upravené podle výsledku.

## Otevřené otázky
1. **Klasické CV, nebo ML?** Rozhodne uživatel po spiku.
2. **Nové závislosti** (OpenCV / ONNX Runtime / jiné) – jaké a zda vůbec; zvětší instalaci (nativní knihovny pro každou platformu).
3. **Licence vah modelu** v projektu GPL-3.0-only: stačí uživateli váhy s kompatibilní licencí, nebo chce jen modely s veřejnými trénovacími daty a kódem (nebo vlastní trénink)?
4. **Data z kamery uživatele:** smí se malé ukázky (snímky) uložit do repozitáře jako fixture pro testy? Jak velké a které?
5. **První detektor:** potvrdit po spiku (doporučení architekta předem: špagety / odlepení dílu, první vrstva až podle výsledku spiku).

### Rozhodnutí uživatele 2026-11-03
2. Nové závislosti spike **jen doporučí** (s licencí a AOT); prototyp jen mimo repozitář, rozhodne uživatel.
3. Licence vah modelu: stačí **kompatibilní licence vah** (MIT, Apache-2.0…), veřejná trénovací data nejsou podmínkou.
4. Fixture: smí se uložit **nejvýš ~10 zmenšených snímků** (640×360 JPEG, celkem do ~1 MB) jen s vnitřkem tiskárny.
- Záběry: 2026-11-03 se z kamery uživatele (`rtsp://192.168.0.227/live`, Core One, standardní poloha) ukládá snímek každých 30 s během skutečného tisku (lokálně, mimo repozitář).

## Poznámky k implementaci
(vyplní agent po dokončení)
