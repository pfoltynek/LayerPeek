---
id: T-194
title: Kamera – „Otestovat kameru“ zkrátí pauzu znovupřipojení a čas dalšího pokusu bez zastarání
status: todo
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
- [ ] „Otestovat kameru“ během pauzy spustí pokus hned a vynuluje pauzu (test s falešným `TimeProvider`).
- [ ] Detekce ani nahrávání pauzu nezkracují (stávající testy dál procházejí).
- [ ] Zobrazený čas odpovídá skutečnému zbývajícímu času po návratu z malého náhledu, po obnovení okna i po uplynutí
      času (testy s falešným `TimeProvider`); odchylka nejvýš jeden krok zaokrouhlení.
- [ ] Ve skrytém, minimalizovaném nebo zavřeném okně a v malém náhledu neběží žádný časovač (test).
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
(zatím žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
