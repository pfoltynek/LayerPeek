---
id: T-161
title: Timelapse – UI (nastavení, tray menu, okno kamery, štítek Experimentální, notifikace) a E2E
status: todo
owner: ui-designer + ui-dev
priority: P3
platform: all
depends_on: [T-160, T-146, T-148]
---

> **Návrh ze spiku T-149 (2026-11-03), rozsah upřesněný rozhodnutím uživatele 2026-11-04** (otázky 2–6 a 10 v T-149).
> Implementuje se až po dashcamu T-148.

## Cíl
Ovládání timelapse v aplikaci po vzoru UI nahrávání (T-146): volby v nastavení, ruční start / stop z tray menu a z okna
kamery, indikátor pořizování v okně kamery, otevření posledního timelapse a notifikace chyb. Celá funkce je označená
jako **experimentální**.

## Rozsah
- **ui-designer:** umístění voleb (záložka Kamera u tiskárny / globální sekce nahrávání), ovládací prvek intervalu
  (pevné volby 10 / 30 / 60 s, nebo volné číslo 5–600 s – vybere designer, výchozí 30 s), podoba štítku
  „Experimentální“, indikátor timelapse v okně kamery a v malém náhledu vedle indikátoru REC (T-146), délka textů
  v překladech.
- **ui-dev:**
  - nastavení: automatický timelapse u tiskárny (výchozí vypnuto) a interval (výchozí 30 s). Výstup je pevně 30 fps,
    bez volby,
  - **štítek „Experimentální“** (rozhodnutí uživatele 2026-11-04) u každé volby timelapse v nastavení a všude, kde
    se timelapse zapíná (položka tray menu, ovládání v okně kamery). Nový lokalizovaný text ve všech `Strings.*.resx`,
  - tray menu: ruční start / stop timelapse u tiskárny, indikace „pořizuje se timelapse“, „Přehrát poslední
    timelapse“, „Otevřít složku“ (sdílená s nahrávkami),
  - okno kamery: zapnutí / vypnutí timelapse (tlačítko nebo položka kontextového menu podle návrhu) a **indikátor
    pořizování timelapse** po vzoru indikátoru REC (okno kamery i malý náhled),
  - notifikace chyb ze služby (T-160) přes `INotificationService`, texty ve všech `Strings.*.resx`,
  - E2E / headless UI testy po vzoru T-146.
- Volbu spouštěče „po vrstvě“ přidá až T-159.

## Akceptační kritéria
- [ ] Volby se uloží a načtou, chybné hodnoty se odmítnou jako u ostatních polí nastavení (T-112).
- [ ] Štítek „Experimentální“ je u všech voleb timelapse v nastavení, u položky tray menu i u ovládání v okně kamery;
      text je ve všech jazycích (headless UI test, okno nastavení bez posuvníku i s nejdelším překladem).
- [ ] Ruční start / stop z tray menu i z okna kamery funguje a obě místa ukazují stejný stav (test).
- [ ] Okno kamery i malý náhled ukazují indikátor, když se timelapse pořizuje, a skryjí ho po skončení (headless UI test, E2E).
- [ ] Tray menu ukazuje stav a otevře poslední timelapse v přehrávači systému.
- [ ] Notifikace chyb lokalizované ve všech jazycích.
- [ ] Headless UI testy a E2E prochází; snímky UI přegenerované, pokud se mění.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování.

## Otevřené otázky
- (žádné – otázky 2–6 v T-149 rozhodnuté 2026-11-04)

## Poznámky k implementaci
(vyplní agent po dokončení)
