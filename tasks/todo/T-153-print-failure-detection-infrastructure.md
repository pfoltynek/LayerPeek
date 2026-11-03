---
id: T-153
title: Detekce chyb tisku – infrastruktura (projekt LayerPeek.Detection, vzorkování snímků, kontext tisku, události)
status: todo
owner: core-dev
priority: P2
platform: all
depends_on: [T-143, T-152]
---

## Cíl
Společný základ pro všechny detektory selhání tisku (nápad 3, fáze 1): vzorkování snímků ze sdíleného spojení kamery na pozadí, detekce jen během tisku s kontextem z PrusaLink, vyhodnocení výsledků detektorů, událost pro notifikaci, uložení snímku při poplachu a zpětná vazba „falešný poplach“. Skutečný detektor přijde v T-155, UI v T-154.

## Navržené vrstvy (návrh architekta, potvrdit v otázce 1)
```
App ──► LayerPeek.Detection ──► Core
 │                               ▲
 └──► Camera.FFmpeg ─────────────┘
```
- **Core (`Core/Detection`)** – jen abstrakce a orchestrace bez závislostí: `IPrintFailureDetector` (typ detektoru, analýza vzorku → skóre / výsledek), `PrintFailureKind` (nebo textové id typu, aby přibývaly detektory bez změny enumu – volí core-dev podle spiku), nastavení detektoru (zapnuto, citlivost), `PrintFailureDetectionService` (orchestrace), `PrintFailureContext` (stav, Z, progress, id úlohy, čas od začátku tisku).
- **`src/LayerPeek.Detection`** (nový projekt, `IsAotCompatible=true` přes `src/Directory.Build.props`) – implementace detektorů, později případně závislost na runtime z T-152. Referencuje jen Core. Testy v `tests/LayerPeek.Detection.Tests` (xUnit v3, MTP).
- **App** – composition root: registruje detektory explicitně (žádné skenování assembly), napojí notifikace a UI (T-154).
- Core nesmí znát Avalonii, FFmpeg ani runtime ML; Detection nesmí znát Avalonii ani FFmpeg (snímky dostává jako `CameraFrame` / vlastní zmenšený buffer z Core).

## Rozsah
- **Vzorkování:** odběr dekódovaných snímků přes hub (T-143) jen během tisku tiskárny, která má zapnutou kameru a aspoň jeden zapnutý detektor. Frekvence ~1 snímek za 1–2 s (podle spiku), zmenšení (např. na šířku 320–640 px, šedotón / BGRA podle spiku) do znovupoužívaného bufferu bez alokací na vzorek. Okno kamery přitom nemusí být otevřené.
- **Kontext tisku** ze snapshotů (`PrinterState`, `Telemetry.AxisZ`, `PrintJob.ProgressPercent`, id úlohy), aby detektor věděl např., že se tiskne první vrstva. Detekce se spouští jen ve stavu `Printing`; při `Paused`, `Attention`, konci tisku a offline se zastaví a vyčistí stav detektorů.
- **Vyhodnocení:** detektor vrací skóre; poplach až po potvrzení (např. K z N po sobě jdoucích vzorků nad prahem podle citlivosti), nejvýš jeden poplach na typ a úlohu (nebo s odstupem – otázka 3). Analýza mimo vlákno dekodéru i mimo UI vlákno; pomalý detektor zahazuje vzorky, nebrzdí stream.
- **Událost poplachu** (např. nový `PrintEventKind` nebo vlastní událost) s tiskárnou, typem, skóre, časem a cestou k uloženému snímku; doručení přes `INotificationService` (texty v T-154).
- **Snímek při poplachu:** uložení snímku (formát a složka – otázka 4) a jeho rotace (limit počtu / velikosti).
- **Zpětná vazba „falešný poplach“:** API pro označení poplachu jako falešného; uloží se lokálně metadata (typ, skóre, čas, snímek) jako podklad pro ladění detektorů; nic se neodesílá ze stroje.
- **Testovací detektor** (jen v testech): falešný detektor řízený skriptem, aby šla infrastruktura otestovat bez skutečného detektoru.
- Dokumentace: `docs/architecture.md` (Pravidla vrstev, nový projekt, tok dat). Úpravu oddílu „Repository layout“ v `CLAUDE.md` předložit uživateli ke schválení (agent ji sám neprovádí).

## Akceptační kritéria
- [ ] Nový projekt `LayerPeek.Detection` a testovací projekt jsou v `LayerPeek.slnx`, build bez varování (včetně AOT / trim analyzátorů).
- [ ] Vzorkování běží jen během tisku a jen se zapnutým detektorem, bez otevřeného okna kamery (test se simulátorem: scénář tisku → vzorky; nečinná tiskárna → žádné spojení kvůli detekci).
- [ ] Frekvence vzorkování a zmenšení odpovídají nastavení; ustálený stav bez alokací na vzorek (test).
- [ ] Potvrzení poplachu (K z N), jeden poplach na typ a úlohu a vyčištění stavu na konci tisku – testy s falešným detektorem a falešnými hodinami.
- [ ] Poplach vyvolá notifikaci přes `INotificationService` a uloží snímek; rotace snímků funguje (test v dočasné složce, úklid i po pádu).
- [ ] „Falešný poplach“ uloží lokální záznam (test).
- [ ] Core nereferencuje Detection ani žádný runtime ML / CV; Detection nereferencuje Avalonii ani FFmpeg (test architektury nebo revize referencí).
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. **Vrstvy:** souhlasí uživatel s novým projektem `src/LayerPeek.Detection` (App → Detection → Core) a s úpravou oddílu repozitáře v `CLAUDE.md`? Název projektu?
2. **Frekvence vzorkování a rozlišení:** potvrdit podle spiku T-152 (návrh ~1 snímek za 1–2 s, zmenšený).
3. **Opakování poplachu:** jen jeden poplach na typ a tisk, nebo znovu po N minutách, pokud problém trvá?
4. **Snímky poplachů:** ukládat (formát JPEG / PNG – kodér musí být AOT a licenčně v pořádku), do jaké složky (vedle nahrávek T-145?), jaký limit?
5. **Zpětná vazba „falešný poplach“:** jen lokální záznam pro ruční ladění, nebo i ovlivnění citlivosti automaticky (např. zvýšit práh u tiskárny)?
6. **Výkon:** jaký nejvyšší přípustný průměrný odběr CPU detekce na pozadí (např. < 2 % jednoho jádra)?

## Poznámky k implementaci
(vyplní agent po dokončení)
