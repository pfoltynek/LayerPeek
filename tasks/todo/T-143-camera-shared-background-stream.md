---
id: T-143
title: Kamera – sdílené spojení se streamem na pozadí (okno, nahrávání, detekce)
status: todo
owner: core-dev + ui-dev
priority: P2
platform: all
depends_on: [T-014, T-034]
---

## Cíl
Dnes patří spojení s kamerou oknu kamery: `CameraViewModel` si přes `ICameraFeedFactory` a `ReconnectingCameraFeed` otevře vlastní RTSP spojení a po zavření (i minimalizaci) okna ho zavře (T-014, „Životní cyklus“). Nahrávání (T-144 až T-148), detekce chyb tisku (T-153) i automatické zavírání okna (T-151) ale potřebují stream **i bez otevřeného okna** a nemají otevírat druhé, třetí spojení na tutéž kameru.

Úkol zavede jedno sdílené spojení na tiskárnu s více odběrateli. Je to společný základ pro nápady 1 (nahrávání) a 3 (detekce chyb), pro nápad 2 (automatické okno) je volitelný.

## Rozsah
- **Core (`Core/Cameras`), core-dev:**
  - Nová služba, pracovně `CameraStreamHub` (název volí core-dev): pro každou tiskárnu se zapnutou kamerou nejvýš jedno spojení (`ReconnectingCameraFeed`), sdílené odběrateli.
  - Odběr přes „lease“ (`IAsyncDisposable`): spojení se otevře s prvním odběratelem a zavře po posledním (případně po krátké prodlevě, viz otázky). Každý odběratel hlásí, co potřebuje:
    - **dekódované snímky** (okno kamery, malý náhled, detekce),
    - **zakódované pakety H.264** bez dekódování (nahrávání, T-144) – dekódování se pak spouští jen tehdy, když ho někdo potřebuje (nižší CPU při nahrávání bez okna).
  - Pomalý odběratel nesmí brzdit ostatní ani dekodér: každý odběratel dostane vlastní omezenou frontu / „poslední snímek vyhrává“ (detekci stačí ~1 snímek za 1–2 s, oknu každý snímek). Půjčený buffer `CameraFrame` se dál nesmí držet přes posun enumerátoru – sdílení řeší kopie nebo výměna bufferů (po vzoru `App/Cameras/CameraFrameExchange`), ne držení cizího bufferu.
  - Rozšíření `ICameraFeed` / `ICameraFeedFactory` o přístup k paketům (rozhraní pro pakety navrhne core-dev; Core nesmí znát typy FFmpeg – paket = bajty + pts/dts + příznak klíčového snímku + časová základna, parametry kodeku = extradata SPS/PPS).
  - Stav spojení (`CameraStatus`) dostanou všichni odběratelé.
  - Změna adresy kamery, vypnutí kamery nebo odebrání tiskárny v nastavení spojení restartuje / ukončí pro všechny odběratele.
- **Camera.FFmpeg, core-dev:** `FFmpegDecoder` umí předat přečtené pakety (`av_read_frame`) odběrateli paketů ještě před dekódováním; dekóduje jen tehdy, když existuje odběratel snímků. Ustálený stav dál bez alokací na snímek (test z T-014).
- **App, ui-dev:** okno kamery a malý náhled odebírají stream přes hub místo vlastního spojení. Chování okna se nemění: minimalizované okno odběr snímků uvolní, zavření okna uvolní lease. Náhled na záložce Kamera v nastavení (T-034) a „Otestovat kameru“ zůstávají podle otázky 3.
- Registrace v DI (explicitní), uvolnění hubu při ukončení aplikace (`TrayHost`) s časovým limitem jako u ostatních služeb.

## Akceptační kritéria
- [ ] Dva a více odběratelů téže tiskárny sdílí jedno RTSP spojení (test proti kameře simulátoru: simulátor vidí jednoho klienta).
- [ ] Spojení se otevře s prvním odběratelem a zavře po posledním; po zavření nezůstane žádné visící spojení ani vlákno dekodéru (test).
- [ ] Odběratel jen paketů nespustí dekódování (test: dekodér se neotevře / počítadlo dekódovaných snímků = 0).
- [ ] Pomalý odběratel nezpomalí ostatní (test s falešným odběratelem, který blokuje).
- [ ] Změna adresy, vypnutí kamery a odebrání tiskárny restartují / ukončí spojení pro všechny odběratele (test).
- [ ] Okno kamery, malý náhled a překryv fungují jako dřív; stávající testy (unit, headless UI, E2E okna kamery) prochází beze změny chování.
- [ ] Ustálený stav streamu bez alokací na snímek (stávající test z T-014 prochází).
- [ ] Core dál nereferencuje FFmpeg ani Avalonii; `docs/architecture.md` (Pravidla vrstev, Camera.FFmpeg) popisuje hub.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. **Kolik souběžných RTSP spojení kamera Buddy3D snese?** Pokud jen jedno, je hub nutný; pokud víc, jde o optimalizaci. Ověřit proti skutečné kameře uživatele (simulátor dává každému klientovi vlastní relaci, to nic nedokazuje). Neblokuje implementaci, ovlivní prioritu.
2. **Prodleva zavření spojení po posledním odběrateli:** hned (jako dnes po zavření okna), nebo např. 5–10 s, aby rychlé zavření a otevření okna nečekalo znovu ~2 s na klíčový snímek?
3. **Náhled v nastavení (T-034) a „Otestovat kameru“:** mají jít přes hub (sdílet spojení s otevřeným oknem), nebo zůstat samostatné (testují neuloženou adresu z formuláře, takže samostatné spojení dává smysl)? Doporučení architekta: test neuložené adresy samostatně, náhled uložené kamery přes hub – rozhodne uživatel.
4. **Minimalizované okno + běžící nahrávání:** dnes minimalizace stream zastaví. S hubem minimalizované okno jen přestane odebírat snímky; spojení drží jiný odběratel. Souhlasí uživatel, že se tím chování okna samotného nemění?

## Poznámky k implementaci
(vyplní agent po dokončení)
