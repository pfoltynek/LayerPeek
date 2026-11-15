---
id: T-204
title: Nastavení > Kamera – volba „Hardwarové dekódování“ (Automaticky / Vypnuto)
status: done
owner: ui-dev
priority: P2
platform: all
depends_on: [T-203]
---

## Cíl
Rozhodnutí uživatele 2026-11-12 (T-192, otázka 3): hardwarové dekódování je výchozí zapnuté a v Nastavení > Kamera
je volba „Hardwarové dekódování“ s možnostmi **Automaticky** (výchozí) a **Vypnuto**. „Vypnuto“ je zároveň pojistka
z otázky 5 (bezpečnost, paměť, chyby ovladače): vždy software. Model nastavení a dekodér připravuje T-203.

Priorita: minimum prostředků, čitelný a robustní kód, žádná nová logika v UI navíc proti ostatním volbám záložky.

## Rozsah
- Záložka **Kamera** okna nastavení (vedle „Snímková frekvence malého náhledu“, T-185): volba „Hardwarové dekódování“
  (např. `ComboBox` nebo dvojice přepínačů podle stávajícího stylu záložky; rozhodne `ui-designer`, pokud záložka
  nemá vzor) s krátkou nápovědou: Automaticky = hardware grafiky, když je k dispozici, jinak software; Vypnuto = vždy
  software (méně paměti, bez systémového dekodéru). Compiled bindings, `AutomationId` pro testy.
- Hodnota jde do `AppSettings.CameraHardwareDecoding` (T-203) přes `SettingsViewModel` / `SettingsEditorStore` jako
  ostatní volby záložky; změna označí okno jako neuložené, „Obnovit“ / „Zrušit“ fungují.
- Uplatnění hned po uložení (rozhodnutí k otázce 2): dekodér běžících spojení se znovu otevře od dalšího klíčového snímku.
- Volba se zobrazuje na obou OS (rozhodnutí k otázce 1); na Windows s poznámkou, že „Automaticky“ zatím znamená software (do přepnutí po měření v T-125).
- **Texty ve všech 8 `Strings.*.resx`** (en, cs, de, es, fr, it, pl, sk): název volby, obě možnosti, nápověda.
  Kontrola délky v nejdelších jazycích (de, fr, es) při 780 DIP.
- **Příručky (všechny `manuals/<lang>/manual.md`, zdroj `manuals/en`):** oddíl 11.6 Camera – nová položka (co volba
  dělá, výchozí stav, kdy dát Vypnuto: problémy s obrazem, ovladač, paměť; na Windows zatím software). Oddíl 15
  (Troubleshooting) – řádek „obraz kamery je poškozený / černý → zkuste Hardwarové dekódování: Vypnuto“.
- **Snímky:** referenční snímky UI (`SettingsWindowSnapshotTests`, sada macOS na Macu; sada Windows → T-125, pokud se
  volba na Windows zobrazuje), případně `docs/images/<lang>/settings-*.png` použité v příručkách.
- `CHANGELOG.md` (anglicky) – položka do nevydané verze.

## Akceptační kritéria
- [x] Volba je na záložce Kamera, výchozí „Automaticky“, uloží se a po znovuotevření okna se načte; neuložená změna se
      hlásí jako ostatní volby (UI testy v `LayerPeek.App.UiTests`).
- [x] Po uložení se volba uplatní podle odpovědi na otázku 2 (test: dekodér otevřený po změně použije novou hodnotu;
      při okamžitém uplatnění i znovuotevření dekodéru běžícího streamu bez odpojení).
- [x] Texty ve všech 8 jazycích, nic se neořezává (layout testy záložky Kamera projdou i s novým řádkem).
- [x] Všechny příručky aktualizované (11.6 a 15), `UserManualTests` projdou.
- [x] Referenční snímky macOS přegenerované a zkontrolované okem; Windows sada → T-125 (pokud se mění). → přesunuto do T-125
- [x] Compiled bindings, žádné AOT varování (`dotnet publish -c Release -r osx-arm64` nebo `win-x64` podle OS).
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Poznámky k AOT a licencím
- Žádný nový balíček, jen stávající Avalonia ovládací prvky s compiled bindings.

## Otevřené otázky
1. **Zobrazit volbu na Windows, dokud tam „Automaticky“ znamená software?** (do rozhodnutí uživatele po měření v T-125)
   a) zobrazit na obou OS, nápověda / příručka uvede „na Windows zatím vždy software“ (stejné UI a snímky na obou OS);
   b) na Windows volbu skrýt, dokud se D3D11VA nepřepne do „Automaticky“ (méně matoucí, ale snímky a příručka se pak
   mění znovu). Doporučení architekta: b). Blokuje jen zobrazení na Windows a texty příručky, ne zbytek úkolu.

   Rozhodnuto uživatelem 2026-11-12: **a) zobrazit na obou OS s poznámkou**, že na Windows se zatím používá software
   (nápověda u volby i příručka). Snímky sady Windows se mění → T-125.
2. **Kdy se změna volby projeví u otevřeného okna kamery?** a) hned po Uložit / Použít: dekodér běžících spojení se
   znovu otevře od dalšího klíčového snímku (stejný mechanismus jako návrat k softwaru v T-203, bez odpojení RTSP,
   nahrávání běží dál; obraz může stát do dalšího klíčového snímku, u klipů simulátoru s GOP 50 při 25 fps 2 s); b) až při dalším otevření
   plného dekódování (zavření a otevření okna, nové připojení), bez zastavení obrazu. Doporučení architekta: a) – kdo
   volbu mění, řeší obvykle problém s obrazem a čeká okamžitý účinek. Blokuje kritérium „uplatnění po uložení“.

   Rozhodnuto uživatelem 2026-11-12: **a) hned po Uložit** (dekodér se znovu otevře od dalšího klíčového snímku,
   bez odpojení RTSP, nahrávání běží dál).

## Poznámky k implementaci
Hotovo 2026-11-12 na Macu (Mac mini M2 Pro, macOS 27.2, .NET SDK 11.0.100).

**Co vzniklo**
- **UI (záložka Kamera, `SettingsWindow.axaml`):** pod „Snímková frekvence malého náhledu“ stejný vzor jako u profilu
  T-185 (bez návrhu `ui-designer`, záložka vzor má): nadpis „Hardwarové dekódování“, `ComboBox` (`AutomationId`
  `HardwareDecoding`, `MinWidth` 180 jako profil) s možnostmi Automaticky / Vypnuto, pod ním popis vybrané možnosti
  (`HardwareDecodingDescription`, třída `hint`) a jen tam, kde „Automaticky“ zatím znamená software, druhý řádek
  `HardwareDecodingSoftwareNote` („Na Windows zatím i Automaticky dekóduje software.“). Popis je zároveň
  `AutomationProperties.HelpText` výběru. Compiled bindings (`x:DataType="vm:HardwareDecodingOption"`).
- **`SettingsViewModel.HardwareDecoding.cs`:** `HardwareDecodingOption` (`LocalizedOption`), `HardwareDecodingOptions`,
  `SelectedHardwareDecoding` (null od seznamu i stejná hodnota nic nedělají), `HardwareDecodingDescription`,
  `ShowsHardwareDecodingSoftwareNote` (init). Načtení, sestavení `AppSettings` (`TryBuildSettings`), rozpoznání neuložené
  změny (`UpdateDirty` porovnává se základem jako u ostatních voleb) a obnovení textů při změně jazyka jako u ostatních
  voleb. Zrušit zahodí změnu, znovuotevřené okno načte uloženou hodnotu.
- **Poznámka Windows podle jediného pravidla OS:** `FFmpegCameraFeedFactory.AutomaticUsesHardware` (veřejné, čte
  `HardwareDecoderSelection` z T-203); kompoziční kořen nastaví `ShowsHardwareDecodingSoftwareNote = !AutomaticUsesHardware`.
  Až T-206 přepne Windows na D3D11VA, poznámka zmizí sama (texty nápovědy a příručky se pak upraví v T-206).
- **Uplatnění hned po Uložit (otázka 2 a):** mechanismus je z T-203 – uložení jde přes `MonitoringSession.ApplyAsync`
  (změna volby nerestartuje monitoring, jen `UpdateSettings` pozorovatelů) do `CameraHardwareDecodingObserver`
  (před `CameraStreamHubObserver`), ten nastaví `FFmpegCameraFeedFactory.HardwareDecoding`; běžící okna přejdou od
  dalšího klíčového snímku bez odpojení RTSP (test T-203
  `HardwareDecodingVideoToolboxTests.AChangedSettingAppliesAtTheNextKeyFrameWithoutLosingFrames`). T-204 doplnil, že
  okno hodnotu vůbec ukládá, a test zapojení v kontejneru.
- **Oprava mimochodem:** před T-204 okno nastavení `CameraHardwareDecoding` do `AppSettings` nepřenášelo – každé uložení
  z okna zapsalo výchozí „Automaticky“ a ručně zapsané `"Off"` ze `settings.json` se ztratilo (okno se navíc hned po
  otevření hlásilo jako neuložené). Pokrývá `ASavedOffIsShownAndKeptWhenSomethingElseIsSaved`.
- **Texty (8 × `Strings.*.resx`):** `SettingsHardwareDecoding`, `…Automatic`, `…Off`, `…AutomaticHint`, `…OffHint`,
  `…SoftwareNote`; fr a it krátké (jako T-185), ve francouzštině nezlomitelná mezera před dvojtečkou. Okem zkontrolováno
  při 780 DIP s „Vypnuto“ a poznámkou v de, fr, es a pl (dočasné snímky, nezapsané): vše na jednom řádku, žádné ořezání.
- **Příručky (všech 8):** 11.6 nová položka (co volba dělá, výchozí stav, kdy Vypnuto: poškozený / černý obraz, ovladač,
  paměť; na Windows zatím procesor; test kamery a detekce vždy procesor; platí po Uložit od dalšího klíčového snímku),
  15 nový odstavec „Obraz kamery je poškozený nebo černý → Hardwarové dekódování: Vypnuto“ s odkazem na 11.6.
- **`CHANGELOG.md`:** položka „Hardware decoding of the camera video (macOS)“ v `[Unreleased] / Changed` (T-203 ji
  nechal na T-204).
- **T-125:** nový oddíl 2ac (snímek `settings-camera-tab.png` sady Windows s poznámkou, testy se Segoe UI, krátká ruční
  kontrola `settings.json`) a odkaz v bodu 2 souhrnu.

**Testy (nové 11 = App 5, UiTests 5, FFmpeg 1; 1 rozšířený):** `HardwareDecodingSettingsTests` (výchozí,
Vypnuto je neuložené a uloží se, zpět Automaticky je čisté, uložené Vypnuto se načte a přežije uložení jiné volby,
null / stejná hodnota, texty podle jazyka), `CameraHardwareDecodingObserverTests.TheAppRegistersItForItsDecoder`
(pozorovatel je v `MonitoringSession.Observers` před hubem), `SettingsWindowCameraTabUiTests` (výchozí Automaticky
s popisem a `HelpText`, bez poznámky; Vypnuto → neuložené → uloženo → znovuotevřené okno ho ukáže; Zrušit nic neuloží;
poznámka se ukáže s příznakem), `SettingsWindowSnapshotTests.CameraTab` (nový snímek `settings-camera-tab.png`, poznámka
jen v sadě Windows), `HardwareDecodingRuleTests.AutomaticUsesHardwareOnlyWhereTheRuleSelectsADevice`. První stav
`SettingsWindowLayoutTests` má teď na záložce Kamera „Vypnuto“ (delší popis) a poznámku Windows, takže test „vše se vejde
bez posuvníku a nic se neořezává“ kontroluje nejvyšší záložku ve všech 8 jazycích.

**Snímky:** sada macOS – nový `settings-camera-tab.png` (zkontrolován okem: nadpis, výběr „Automatic“, popis, žádná
poznámka); ostatní snímky nastavení se nezměnily (žádný jiný snímek neukazuje záložku Kamera). Sada Windows → T-125 (2ac).
Obrázky příruček (`docs/images/<lang>/settings-*.png`) hlavní záložku Kamera neukazují, beze změny.

**Ověření:** `dotnet build` 0 varování, 0 chyb. `dotnet test` celé řešení zelené: Core 3343, PrusaLink 404, Camera.FFmpeg
195, Detection 74, App 4660, App.UiTests 1384, E2E 22, Scripts 13, PrusaSimulator 151 (přeskočené jen testy jiného OS,
čtení obrazovky E2E a generátory snímků README). `dotnet publish src/LayerPeek.App -c Release -r osx-arm64` bez varování
trim / AOT (zbylé řádky jsou hlášky nativního `dsymutil` / `xcodebuild` mimo kód, stejné jako v T-203). Aplikace
(Debug, izolovaný testovací režim: dočasná složka, přípona instance, `settings.json` s `"cameraHardwareDecoding": "Off"`)
se spustila, běžela, příkaz instance `settings` otevřel okno nastavení bez chyby v logu a `quit` ji ukončil; soubor
nastavení zůstal beze změny. Skutečné okno se okem zkontrolovat nedalo (obrazovka Macu zamčená, `screencapture` bez
displeje) – vzhled pokrývají headless snímky výše. Přepnutí běžícího okna kamery na skutečné kameře / simulátoru
v aplikaci ověřuje a měří T-205 (automaticky pokrývá test T-203 uvedený výše).
Uzavřeno 2026-11-12: zbývající kontrola na Windows (snímky sady Windows, testy se Segoe UI, krátká ruční kontrola)
přesunuta do T-125, oddíl 2ac (pravidlo sběrných ověřovacích úkolů, rozhodnutí uživatele 2026-10-30).
