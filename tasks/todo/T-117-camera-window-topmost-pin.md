---
id: T-117
title: Okno kamery – špendlík „Vždy navrchu“ (indikace a přepínání přímo v okně)
status: todo
owner: ui-dev
priority: P2
platform: all
depends_on: [T-037, T-091, T-102]
---

## Cíl
Uživatel teď v okně kamery nevidí, zda je okno „vždy navrchu“. Volbu může změnit jen v Nastavení → tiskárna → Okno kamery
(zvlášť normální okno a malý náhled, T-037). Okno má stav ukázat a umožnit ho přepnout přímo (požadavek uživatele
2026-10-31). Implementace na Windows i macOS.

## Rozsah (rozhodnuto 2026-10-31)
1. **Tlačítko se špendlíkem** v řadě tlačítek okna vpravo nahoře (`CameraWindowButtons`), v normálním okně i v malém
   náhledu. Na celé obrazovce se neukazuje, protože ta navrchu není nikdy (T-037). Zapnuto = plný špendlík, vypnuto =
   obrysový. Ovládací prvek je přepínač (`ToggleButton`): popisek a přístupný název říkají stav, čtečka hlásí
   zapnuto / vypnuto. Pořadí v řadě a vzhled navrhne `ui-designer`.
2. **Trvalá indikace:** ostatní tlačítka se dál ukazují jen při najetí myší nebo s fokusem. Špendlík zůstane při zapnutém
   „vždy navrchu“ vidět i bez myši, ztlumený na svém místě v řadě. Při najetí se řada ukáže celá a špendlík v plné
   síle. Vypnutý špendlík se bez myši neukazuje.
3. **Kliknutí uloží volbu** tiskárny pro aktuální režim (`TopmostNormal` v normálním okně, `TopmostMini` v malém
   náhledu) cíleným zápisem do nastavení, jako `ISettingsStore.SetCameraOverlayLayoutAsync` (mění se jen ta hodnota).
   Okno se přepne hned. Otevřené Nastavení ukáže novou hodnotu stejně jako u ostatních cílených zápisů, bez ztráty
   neuložených úprav jinde.
4. **Kontextové menu obrazu:** zaškrtávací položka „Vždy navrchu“. Kontextové menu se rozšíří i na malý náhled
   (teď je jen v normálním okně, T-091). V malém náhledu nabízí jen tuto položku, rozložení widgetů dál jen v normálním
   okně.
5. Navazující chování T-102 (tlačítko na hlavním panelu, ikona v Docku / ⌘Tab, Spaces na macOS) už reaguje na změnu
   `Window.Topmost` a zůstává beze změny. Ověřit, že platí i při přepnutí z okna.

## Akceptační kritéria
- [ ] Špendlík v normálním okně i malém náhledu, ne na celé obrazovce; stav odpovídá `CameraViewModel.IsTopmost`
      i po přepnutí režimu a po uložení volby v Nastavení
- [ ] Zapnutý špendlík vidět bez myši (ztlumený), vypnutý ne; při najetí / fokusu celá řada (headless UI testy)
- [ ] Klik a položka kontextového menu přepnou `Window.Topmost` hned a uloží jen `TopmostNormal` / `TopmostMini`
      dané tiskárny (ostatní nastavení byte po bytu beze změny, jako u `SetCameraOverlayLayoutAsync`)
- [ ] Selhání zápisu: okno se vrátí do uloženého stavu, chyba v logu bez citlivých údajů, aplikace nespadne
- [ ] Otevřené Nastavení se zobrazenou tiskárnou ukáže novou hodnotu a neztratí neuložené úpravy
- [ ] Tlačítko na hlavním panelu (Windows) a ikona v Docku (macOS) následují přepnutí z okna (UI testy s `AppPresence`)
- [ ] Přístupnost: přepínač s názvem a stavem pro čtečky, ovladatelný klávesnicí; kontextové menu i v malém náhledu
- [ ] Texty ve všech 8 jazycích (popisky zapnuto / vypnuto, položka menu), přístupové klávesy jedinečné
- [ ] Snímky UI: okno kamery s ikonou (Windows → T-114, macOS → T-115)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné – rozhodnuto 2026-10-31: trvale viditelný špendlík, ukládá do nastavení, i v kontextovém menu; bez klávesové
  zkratky a bez položky v menu v oznamovací oblasti)

## Poznámky k implementaci
(vyplní agent po dokončení)
