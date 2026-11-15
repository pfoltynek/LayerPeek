---
id: T-212
title: macOS ověření – čtení doručených notifikací testovacího bundlu a kontroly jejich obsahu
status: todo
owner: ui-dev
priority: P2
platform: macos
depends_on: [T-208, T-209, T-210, T-211]
---

> **Rozhodnuto uživatelem 2026-11-13:** obsah notifikací se ověřuje strojově v testovacím režimu. Body T-127 / T-126
> se odškrtnou podle kontrol v poslední zprávě `-Preset release` (T-208).

## Cíl
Režim `--layerpeek-e2e-notification-status` dnes vypíše jen oprávnění a počet doručených notifikací. Body T-154,
T-146, T-161, T-164, T-165 a T-101 v T-127 ale potřebují znát **obsah**: titulek, text, přílohu, kategorii
s tlačítkem „Falešný poplach“ a co udělá klik. Tento úkol přidá čtení obsahu a kontroly, které ho používají.

## Rozsah
- **Aplikace (jen Debug a jen testovací bundle, jako dnešní `NotificationStatusArgument`):** nový argument např.
  `--layerpeek-e2e-notification-list`. Vypíše JSON doručených notifikací testovacího bundlu
  (`UNUserNotificationCenter.getDeliveredNotifications`): identifikátor, titulek, podtitulek, text, `categoryIdentifier`,
  `threadIdentifier`, počet a typy příloh, zda má zvuk. Přidá i registrované kategorie
  (`getNotificationCategories`) s titulky akcí. Nic se nemění ani nezobrazuje. Exit kódy a odmítnutí mimo testovací
  bundle jako u `--layerpeek-e2e-notification-status`. Kód v `src/LayerPeek.App/Platform/MacOS` vedle
  `TestNotificationCleanup`, AOT kompatibilní (`[LibraryImport]` / stávající ObjC interop), zdroj JSON přes
  `JsonSerializerContext`. Unit test formátu výstupu (bez skutečného centra oznámení).
- **Helper `ncpress <text titulku> <akce>`** (Zpřístupnění): v AX stromu procesu Centra oznámení
  (`com.apple.notificationcenterui`) najde banner nebo položku s titulkem. Provede pojmenovanou akci (vlastní akce
  „Falešný poplach“, AXPress = klik). Když macOS akci do AX nevystaví, exit 4 a kontrola tu část označí `SKIP`
  s důvodem („akce banneru není v AX“), ne `FAIL`.
- **Kontrola `mac.notifications-content`** (Permission: Zpřístupnění, preset `release`):
  - (a) **T-154:** `LAYERPEEK_TEST_FAKE_DETECTOR=1`, tisk simulátoru → poplach. Titulek = název tiskárny, text
    „Možná chyba tisku – …“, 1 příloha (obrázek), kategorie s akcí „Falešný poplach“ zaregistrovaná už před **prvním**
    poplachem. Stejný zvuk jako u chyby tisku (příznak zvuku). `ncpress … Falešný poplach` uloží
    `detection/false-alarms/*.json` v testovacích datech a nepřenese aplikaci do popředí (`lsappinfo front`).
    Klik (`ncpress … AXPress`) otevře okno kamery. Snímek zůstane ve složce nahrávek. V cache
    `notification-images` testovacích dat nezůstanou soubory.
  - (b) **T-146 / T-161:** složka nahrávek na neexistujícím svazku (cesta `/Volumes/<neexistující>`) → „Nahrávání
    kamery selhalo“ a „Timelapse selhal“. Klik na každou otevře Nastavení na záložce „Nahrávání“ (AX vybraná záložka).
  - (c) **T-101:** zkušební upozornění se zvukem i bez (příznak zvuku). Druhé zkušební upozornění nahradí první
    (v seznamu pořád 1 se stejným identifikátorem). Klik přenese Nastavení na záložku Obecné.
  - Úklid: `--layerpeek-e2e-clear-toasts` jako dnes.
- Notifikace exportu a timelapse z nahrávky (T-164, T-165) ověří kontroly T-213 přes tento výpis.
- Mimo rozsah (zůstává ruční v T-127 / T-126): vzhled banneru (náhled přílohy vpravo, po rozbalení velký), stav
  „zamítnuto“ a `notDetermined` z T-101 (vyžadují zásah do TCC v Nastavení systému) a notifikace produkční
  `/Applications/LayerPeek.app` (T-126).

## Akceptační kritéria
- [ ] `--layerpeek-e2e-notification-list` vypíše JSON doručených notifikací a kategorií testovacího bundlu, mimo něj
      odmítne; v Release buildu neexistuje; unit test formátu
- [ ] `mac.notifications-content` (a) = `PASS` (uzavře v T-127 bod **T-154** „Poplach zobrazí notifikaci …“ a „Falešný
      poplach funguje“ kromě vzhledu banneru a editoru výřezu (T-216); pokud `ncpress` akci nenajde, zapsat a část
      nechat ruční)
- [ ] `mac.notifications-content` (b) = `PASS` (doplní body **T-146** a **T-161** v T-127: notifikace selhání a klik)
- [ ] `mac.notifications-content` (c) = `PASS` (uzavře v T-127 bod **T-101** kromě stavu „zamítnuto“ a `notDetermined`, ty zůstávají ruční)
- [ ] AOT: `dotnet publish` osx-arm64 bez varování (kód je jen v Debug, ověřit, že Release ho nepřekládá)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
