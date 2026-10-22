---
id: T-071
title: E2E – okno kamery na monitorech (rohy, celá obrazovka, odpojený monitor, DPI), vždy navrchu a okna při 125/150 %
status: todo
owner: qa-reviewer
priority: P2
platform: windows
depends_on: [T-065, T-066]
---

## Cíl
Ověřit automaticky ruční kontroly T-035 (monitor, rohy, celá obrazovka, odpojený monitor, názvy monitorů, okno neposkočí, mezera 16 DIP při 125/150 %), T-037 (skutečné pořadí oken nad jinou aplikací) a T-041 (Diagnostika při 125/150 % a v tmavém motivu). Staví na `CameraWindowTests` a `SettingsAndDiagnosticsTests` z T-044 a na zásazích T-065/T-066.

## Pokrytí
| Úkol | Kontrola | Test (návrh názvu) |
|---|---|---|
| T-035 | otevření na zvoleném monitoru v rohu (normální okno, malý náhled) | `CameraOpensInEveryCornerOfTheChosenMonitor` (5 umístění × 2 režimy, druhý monitor, okno pokaždé znovu otevřené) |
| T-035 | celá obrazovka na zvoleném monitoru | `FullScreenStartModeUsesTheChosenMonitor` |
| T-035 | odpojený monitor → hlavní, „nepřipojený“ | `MissingMonitorFallsBackToThePrimary` (neexistující monitor v `settings.json`, bez zásahu) + `DisconnectedMonitorFallsBackAndComesBack` (`MonitorDisconnectChange`) |
| T-035 | skutečné názvy monitorů v nastavení | `SettingsListsTheRealMonitorNames` |
| T-035 | 2 stejné monitory: dvě položky, otevře se na zvoleném | `TwoIdenticalMonitorsAreTwoChoices` (jen se 2 stejnými monitory, jinak přeskočeno) |
| T-035 | okno při otevření neposkočí | `CameraWindowDoesNotJumpWhenItOpens` (100/125/150 %) |
| T-035 | mezera přesně 16 DIP při 125/150 % | `CornerMarginIs16DipAtEveryScale` |
| T-037 | normální okno / náhled zůstane nad jinou aplikací, po vypnutí volby ne (bez zavření okna) | `TopmostKeepsTheCameraAboveAnotherApp` |
| T-037 | celá obrazovka nikdy navrchu | `FullScreenIsNeverAboveAnotherApp` |
| T-041 | bez posuvníku při 125/150 % (8 jazyků, hlavní stavy) | `DiagnosticsAndSettingsFitAtEveryScale` |
| T-041 | tmavý motiv, Esc a Tab skutečnou klávesnicí | `DiagnosticsInTheDarkThemeWithTheKeyboard` |

## Rozsah
- **Monitory:** test si sám zjistí monitory (`EnumDisplayMonitors`, `QueryDisplayConfig` + `DisplayConfigGetDeviceInfo` pro přátelský název a `monitorDevicePath`) a podle nich sestaví `camera.window.monitor` v `settings.json`. Testy, které potřebují 2 monitory, se bez nich přeskočí s důvodem. Tento počítač: 2560×1440 + 1440×2560 na výšku, 2× DELL U2719D.
- **Rohy:** pro každé umístění (4 rohy + střed) a režim (normální, malý náhled) zapsat nastavení, otevřít okno příkazem `camera:<id>`, ověřit viditelný rámeček (`DWMWA_EXTENDED_FRAME_BOUNDS`) 16 DIP od okrajů pracovní plochy zvoleného monitoru (střed: vycentrováno), zavřít. Toleranci převzít z T-044 (±2 px).
- **Celá obrazovka:** počáteční režim Celá obrazovka + druhý monitor → okno = celé `rcMonitor` druhého monitoru. Esc → normální okno na tomtéž monitoru.
- **Neexistující monitor:** `settings.json` s monitorem „E2E Missing Monitor“ (+ neexistující `devicePath`) → okno na primárním monitoru. V nastavení na podzáložce Okno kamery je volba „… – nepřipojený“ (UIA text v 8 jazycích podle `.resx`).
- **Skutečně vypnutý monitor (trait `SystemChanges`):** zvolený druhý monitor, aplikace běží. `MonitorDisconnectChange` → otevřít kameru → primární monitor, nastavení ukazuje „nepřipojený“. Po návratu monitoru (nové otevření okna nastavení, seznam se čte jednou za okno, T-035 Q10) je monitor zase připojený a kamera se otevře na něm.
- **Názvy:** položky výběru monitoru (UIA) = „Primární monitor“ + názvy z EDID zjištěné testem (formát podle `.resx`). V AOT průchodu (T-073) totéž ověří `MonitorNameNative` v Native AOT.
- **Neposkočí:** `SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_LOCATIONCHANGE)` mimo proces pro PID aplikace. Od prvního zobrazení okna kamery do ustálení (1 s) se poloha ani velikost nezmění (0 událostí změny polohy okna kamery po `SHOW`). Pro normální okno i náhled při 100, 125 a 150 % (`DisplayScaleChange` na monitoru okna).
- **16 DIP při 125/150 %:** stejné jako rohy, s `DisplayScaleChange` na zvoleném monitoru; DIP podle `GetDpiForMonitor`.
- **Vždy navrchu (z-order):**
  - pomocné okno **jiného procesu**: okno vytvořené testovacím procesem (vlastní vlákno se smyčkou zpráv, `CreateWindowEx`, výrazná barva, stejný obdélník jako okno kamery). Test ho přesune do popředí (`SetWindowPos(HWND_TOP)` + aktivace; když `SetForegroundWindow` nejde, klik skutečnou myší do pomocného okna);
  - volba zapnutá: v bodě uprostřed okna kamery `WindowFromPoint` vrátí okno kamery a v pořadí `EnumWindows` je kamera nad pomocným oknem; snímek obrazovky ukazuje obraz kamery, ne barvu pomocného okna;
  - v **otevřeném** okně vypnout volbu (nastavení → Okno kamery → zaškrtávací pole → Uložit, okno kamery se nezavírá), znovu aktivovat pomocné okno → nahoře je pomocné okno;
  - totéž pro malý náhled (výchozí zapnuto);
  - celá obrazovka: pomocné okno aktivované nad kamerou zůstane nahoře;
  - pomocné okno se na konci zavře (i po neúspěšném ověření).
- **Diagnostika a nastavení při 125/150 %:** existující kontroly „bez posuvníku“ (`WindowChecks.AssertNoScrolling`, Segoe UI) pro Diagnostiku (tiskne, offline, bez tiskáren) a hlavní podzáložky nastavení v 8 jazycích při 125 % a 150 % primárního monitoru. Okno 780×508 DIP se při 150 % vejde na 2560×1440. Pokud by se nevešlo (malý monitor), ověří se přizpůsobení pracovní ploše.
- **Tmavý motiv (T-065, `AppsUseLightTheme = 0`):** Diagnostika a nastavení v tmavém motivu: pozadí okna je tmavé (snímek), žádný text se neořízne (kontroly z T-044), Esc zavře Diagnostiku, Tab prochází výběr tiskárny → rozsah → Zavřít (skutečné klávesy přes `SendInput`).

## Akceptační kritéria
- [ ] Testy z tabulky Pokrytí procházejí; testy pro 2 monitory na tomto počítači neskončí přeskočením
- [ ] Neexistující i skutečně vypnutý monitor → primární, „nepřipojený“ v nastavení; po návratu monitoru znovu na něm
- [ ] Okno kamery neposkočí při 100/125/150 % (WinEvent hook)
- [ ] Z-order nad oknem jiného procesu odpovídá volbě, i po změně v otevřeném okně; celá obrazovka nikdy navrchu
- [ ] Diagnostika a nastavení bez posuvníku v 8 jazycích při 125 % a 150 %; tmavý motiv; Esc/Tab klávesnicí
- [ ] Měřítko, motiv a monitory jsou po běhu jako před ním (T-065/T-066)
- [ ] Ruční kontroly T-035, T-037 a T-041 (kromě „celkového dojmu“) zaškrtnuté podle výsledků
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; `tools/run-e2e.ps1` zelené

## Otevřené otázky
- (žádné – okna jiných aplikací při vypnutí monitoru rozhodnuta v T-066, 2026-10-22)

## Poznámky k implementaci
(vyplní agent po dokončení)
