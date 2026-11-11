---
id: T-194
title: Kamera – „Otestovat kameru“ zkrátí pauzu znovupřipojení a čas dalšího pokusu bez zastarání
status: done
owner: ui-dev + core-dev
priority: P2
platform: all
depends_on: [T-188]
---

## Cíl
Navazuje na T-188 (rozhodnutí uživatele 2026-11-11 k otázce 4 a k zastarání času v okně kamery).

1. **„Otestovat kameru“ během pauzy znovupřipojení zkrátí čekání** stejně jako otevření okna kamery (T-188, rozhodnutí 3):
   pokus o připojení hned a vynulovaná pauza. Dnes test při „Bez signálu“ během minutové pauzy skončí „Žádný obraz“,
   i když by nový pokus mohl uspět.
2. **Čas do dalšího pokusu v okně kamery („Další pokus asi za 20 s“) nesmí zastarávat**, nebo co nejméně. Dnes se počítá
   jen při změně stavu; po návratu z malého náhledu (kde je řádek skrytý), po obnovení okna nebo po delší době ukazuje
   starou hodnotu. Uživatel chce zastarání nulové nebo co nejmenší a robustní řešení.

## Rozsah
- Test kamery (`Otestovat kameru` v nastavení) použije stejný mechanismus probuzení jako okno (`CameraReconnectSignal` /
  `ShortensReconnectWait`); detekce ani nahrávání pauzu dál nezkracují.
- Text času se přepočítá z `CameraStatus.NextAttemptAt` a `TimeProvider` vždy, když se řádek stane viditelným (přepnutí
  z malého náhledu, obnovení/zobrazení okna, změna jazyka) a když by se zobrazená hodnota změnila. Návrh: jednorázový
  časovač na nejbližší okamžik, kdy se zaokrouhlený text změní (ne tikání po sekundě), běžící jen když je řádek
  viditelný a kamera čeká; při skrytí, zavření okna nebo změně stavu se zruší. Žádné dotazování na pozadí.
- Výkon: žádný časovač u skrytého/minimalizovaného okna ani v malém náhledu; nic navíc ve snímkové cestě.

## Akceptační kritéria
- [x] „Otestovat kameru“ během pauzy spustí pokus hned a vynuluje pauzu (test s falešným `TimeProvider`).
- [x] Detekce ani nahrávání pauzu nezkracují (stávající testy dál procházejí).
- [x] Zobrazený čas odpovídá skutečnému zbývajícímu času po návratu z malého náhledu, po obnovení okna i po uplynutí
      času (testy s falešným `TimeProvider`); odchylka nejvýš jeden krok zaokrouhlení.
- [x] Ve skrytém, minimalizovaném nebo zavřeném okně a v malém náhledu neběží žádný časovač (test).
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
(zatím žádné)

## Poznámky k implementaci
- **Test kamery zkracuje pauzu:** `CameraSnapshotTester.CaptureSharedAsync` bere lease s
  `CameraFrameSubscriptionOptions.ShortensReconnectWait = true` (stejný mechanismus jako okno, `CameraReconnectSignal`
  v hubu). Během pauzy proto pokus začne hned s vynulovaným backoffem a test nedostane starou chybu čekajícího spojení,
  ale hned `Connecting` a pak výsledek nového pokusu (snímek = úspěch, chyba = její důvod). Core se změnil jen
  v dokumentačních komentářích; detekce, nahrávání a timelapse příznak dál nemají (stávající
  `CameraStreamHubReconnectTests` beze změny zelené). Test neuložené adresy jde jako dřív vlastním spojením bez backoffu.
- **Čas dalšího pokusu bez zastarání (`CameraViewModel.NextAttempt.cs`):** místo délky spočítané při příchodu stavu
  (`_nextAttemptIn`) se text vždy počítá z `CameraStatus.NextAttemptAt` a `TimeProvider.GetUtcNow` v jediné idempotentní
  metodě `RefreshNextAttempt`. Volá se při změně stavu, při `SetActive` (zobrazení, obnovení, minimalizace, zavření), při
  změně režimu (návrat z malého náhledu) a při změně jazyka (`RefreshTexts`). Řádek je viditelný jen u aktivního okna
  (zobrazené, ne minimalizované, nezavřené), mimo malý náhled a při `NoSignal` / `Error` s budoucím `NextAttemptAt`;
  jinak je `NextAttemptText` `null` (nově i u minimalizovaného okna, takže po obnovení nikdy neukáže starý text).
- **Časovač:** jeden jednorázový `ITimer` z `TimeProvider.CreateTimer`, jen když je řádek viditelný. Je naplánovaný
  přesně na nejbližší okamžik, kdy se zaokrouhlený text změní (`UntilNextAttemptTextChanges`: pod minutou na celou
  sekundu, „1 min“ do 59 s, delší minuty na hranici zaokrouhlení), zaokrouhleno nahoru na celé ms, aby časovač nikdy
  nepřišel o chvilku dřív a nemusel se vracet. Při dalším přepočtu se jen posune (`Change`), při skrytí, minimalizaci,
  zavření, malém náhledu, `Connecting` / `Streaming` a `Dispose` se zlikviduje. Zpětné volání jen pošle přepočet na UI
  vlákno (předem vytvořený delegát); pozdě doručené volání po zrušení nic nepokazí, protože přepočet je idempotentní
  a u skrytého řádku časovač nevytvoří. Zastarání je tak nulové (až na latenci UI vlákna), práce jen při skutečné změně
  textu: pod minutou jedno probuzení za sekundu, od minuty žádné do hranice; nic na pozadí, nic ve snímkové cestě. Proti
  tikání po sekundě je to robustní i vůči skoku hodin (každé probuzení počítá z aktuálního času) a u čekání nad minutu
  levnější. Vlastní opakování okna po chybě lease (`_retryDelay`) ukazuje svůj čas stejně.
- **Testy:** `CameraSnapshotTesterTests` (test během 10s pauzy nahrávání spustí pokus hned a uspěje; neúspěch nového pokusu
  vrátí jeho důvod a další pauza je zase 5 s; bez příznaku oba testy selžou – ověřeno), `CameraViewModelNextAttemptTests`
  (text sleduje hodiny: 999 ms beze změny a bez práce, na hranici jedno odeslání a nový text, 5→4→3→2→1 s, pak zmizí;
  návrat z malého náhledu ukáže aktuální hodnotu; v malém náhledu, minimalizovaném a zavřeném okně žádný časovač ani
  práce při plynutí času; po obnovení nikdy starý text, nový pokus a čas od 5 s; `Dispose` časovač zruší; vlastnostní test
  `UntilNextAttemptTextChanges` – o tik dřív stejný text, v daném okamžiku jiný – pro hodnoty od 0,1 ms do 1 h), stávající
  testy upravené z „neodpočítává se“ na odpočet.
- **Dokumentace:** `docs/architecture.md` (znovupřipojení), `manuals/*/manual.md` oddíl 5.4 ve všech osmi jazycích
  (čas se průběžně aktualizuje; „Otestovat kameru“ zkusí připojení hned). Nové texty UI žádné. Ruční body v T-125 / T-127
  (2t / 1f) doplněné o odpočet a test kamery během pauzy. Žádný nový balíček.
- „Viditelný“ = stejná podmínka, podle které okno drží stream (`SetActive`: zobrazené a ne minimalizované). Okno
  skryté jinak, než že ho okno ohlásí přes `SetActive(false)` (např. skrytí celé aplikace ⌘H na macOS, pokud nezmění
  `WindowState` – neověřeno), drží stream i časovač dál jako dřív stream (nejvýš jedno probuzení za sekundu po dobu pauzy).
- **Ověřeno na macOS (Apple Silicon):** `dotnet build` bez varování, `dotnet test` zelený (10 149 testů, 0 selhání),
  aplikace se spustí přes `dotnet run` a běží (45 s, pak ukončena). Okno se skutečně nedostupnou kamerou a test kamery
  během pauzy vizuálně neověřeny (bez obsluhy) → ruční kontrola v T-125 (2t) a T-127 (1f).
