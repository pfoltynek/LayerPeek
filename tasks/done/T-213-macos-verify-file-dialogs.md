---
id: T-213
title: macOS ověření – systémové dialogy souborů (`mac.file-dialogs`, NSOpenPanel přes Accessibility)
status: done
owner: qa-reviewer
priority: P3
platform: macos
depends_on: [T-209, T-211, T-212]
---

## Cíl
Systémové dialogy souborů (`NSOpenPanel`) z 0.4.0 / 0.5.0 na Macu nikdo neviděl. Dosud chyběl aktivní displej
a dialog nešel zobrazit. Tento úkol je ověří přes Accessibility na testovacím bundlu: „Změnit…“ složky nahrávek
(T-146), „Exportovat timelapse do JPEG…“ (T-165), „Vytvořit timelapse z nahrávky…“ (T-164).

## Rozsah
- **Helper:**
  - `panel <pid>` – najde otevřený panel procesu (LayerPeek není v sandboxu, panel je v procesu aplikace;
    pokud ne, zapsat skutečný proces). Vypíše JSON: role / subrole okna (volné okno vs. list), titulek (AXTitle nebo
    text zprávy), aktuální složku, zda je okno hlavní a klíčové, seznam položek s `enabled`;
  - `panelgo <pid> <cesta>` – ⌘⇧G, zadání cesty (`type <text>`, nový příkaz pro psaní textu přes CGEvent unicode),
    Enter, výběr položky a „Otevřít“ (AXDefaultButton);
  - `panelcancel <pid>` – „Zrušit“.
  Všechny příkazy s časovým limitem. Panel nikdy nezůstane otevřený, úklid přes Esc.
- **Kontrola `mac.file-dialogs`** (Permission: Zpřístupnění, preset `release`), testovací bundle se simulátorem,
  složka nahrávek v dočasné složce:
  - (a) **T-146:** Nastavení › Nahrávání › „Změnit…“ otevře dialog složky. Vybrat dočasnou složku → pole v nastavení
    ji ukáže. Nezapisovatelná složka → hláška (AX text).
  - (b) **T-165:** timelapse z kontroly T-211 (sdílená příprava, žádné nové nahrávání). Položka menu „Exportovat
    timelapse do JPEG… (experimentální)“ otevře **volné okno** (ne list):
    - okno je v popředí a s fokusem i nad jinou aplikací (`helper front`);
    - titulek „Vyberte timelapse k exportu do JPEG (experimentální)“, okno otevřené v podsložce tiskárny, povolené jen
      `.mp4`;
    - výběr nahrávky (ne timelapse) → notifikace „Export timelapse do JPEG selhal / Soubor není timelapse z LayerPeeku“
      (výpis z T-212);
    - výběr timelapse → položka „Zrušit export do JPEG (x %)“ a procento roste (`helper menu` opakovaně);
    - výsledek je složka `…_timelapse_jpg` s `0001.jpg`…, počet = počet snímků (`decode`), notifikace „Timelapse
      exportován do JPEG“;
    - druhý klik na položku při otevřeném panelu nic nového neotevře (jedno okno panelu) a okno kamery mezitím běží
      (stav AX);
    - zrušení uprostřed nezanechá složku ani skrytou `.….partial`.
  - (c) **T-164:** nahrávka z T-210 s výpadkem kamery (endpoint simulátoru `camera/offline` z T-210), tedy
    i `…-2.mp4`.
    - položka „Vytvořit timelapse z nahrávky… (experimentální)“ → volné okno s titulkem „Vyberte nahrávku pro
      vytvoření timelapse (experimentální)“;
    - průběh „Zrušit timelapse z nahrávky (x %)“, výsledek `…_recording_timelapse.mp4` vedle nahrávky (`decode` OK,
      30 fps, i část po výpadku), notifikace „Timelapse z nahrávky vytvořen“;
    - výběr timelapse → notifikace „Vytvoření timelapse z nahrávky selhalo / Soubor není nahrávka z LayerPeeku.“;
    - zrušení nezanechá soubor;
    - během vytváření se dialog exportu do JPEG otevře jen tehdy, když žádný jiný dialog není otevřený.
- Jazyk prvků AppKitu (T-097 otázka 5) se zapíše tady jako vedlejší výsledek (titulky tlačítek panelu přes AX).
  Samotný bod ale uzavře T-216.

## Akceptační kritéria
- [x] `mac.file-dialogs` (a) = `PASS` (doplní v T-127 bod **T-146**: „Změnit…“ = systémový dialog složky,
      nezapisovatelná složka) → přesunuto do T-127
- [x] `mac.file-dialogs` (b) = `PASS` (uzavře v T-127 bod **T-165**) → přesunuto do T-127
- [x] `mac.file-dialogs` (c) = `PASS` (uzavře v T-127 bod **T-164**) → přesunuto do T-127
- [x] Po běhu není otevřený žádný panel ani menu; `mac.profile-unchanged` = `PASS` (`mac.profile-unchanged` = `PASS`
      v běhu `20261115-114811`; panel a menu po skutečném běhu kontroly) → přesunuto do T-127
- [x] Pester / `-SelfTest` a `dotnet build` / `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
Implementováno 2026-11-15 na Macu (qa-reviewer, worktree `LayerPeek-t213`), postupem T-210 / T-211 / T-212. Terminál bez
Zpřístupnění, obrazovka během běhů zamčená, oznámení „LayerPeek E2E“ nepovolená – skutečný běh kontroly je bod 1o v T-127.

- **Aplikace (jen Debug):** přepínač testovacího režimu `LAYERPEEK_TEST_FILE_JOB_DELAY_MS` (`AppEnvironment.TestFileJobDelayVariable`,
  0–1000 ms, mimo testovací režim ignorován a hlášen jako ostatní). Export do JPEG (T-165) a timelapse z nahrávky (T-164)
  pak čtou soubor přes `TestPacedCameraFeedFactory` (`Cameras/Recording`, celý soubor `#if DEBUG`): každá položka přijde
  o tolik ms později (čeká se před předáním, zapůjčený snímek zůstává platný; zrušení ukončí čekání hned). Bez toho je
  timelapse z nahrávky (jen přepakování paketů) hotový za zlomek sekundy a průběh ani zrušení uprostřed nejde spolehlivě
  zachytit. Kamery, nahrávky a živé timelapse se nezpomalují. Zapojeno v `AppServices.FileJobFeeds`. Testy:
  `TestPacedCameraFeedFactoryTests` (pořadí a zpoždění snímků i položek, zrušení, předání `DisposeAsync`),
  `TestModeSwitchesTests.TheFileJobDelayIsOnlyForTheTestMode`, `AppEnvironmentTests.ReleaseBuildsHaveNoTestMode` (v Release
  chybí typ i vlastnost). Bez nového balíčku, bez reflexe (AOT beze změny).
- **Helper** (`tools/macos-verify-helper.swift`, všechny příkazy s časovým limitem, AX volání s limitem 1 s):
  - `type <text>` – Unicode znaky přes CGEvent (`keyboardSetUnicodeString`, nezávisle na rozložení klávesnice);
  - `panel <pid> [<s>]` – panely procesu (okno nebo list s výchozím a zrušovacím tlačítkem a prohlížečem souborů), když
    žádný není, panely služby `com.apple.appkit.xpc.openAndSavePanelService` (sandbox); JSON: `pid` a `process` (kde panel
    skutečně je), `role` / `subrole` / `sheet`, `title`, `texts` (zpráva), `main`, `focused` (klíčové okno aplikace),
    `frontmost`, `folder` (AXDocument) a `folderTitle` (rozbalovací cesta), `defaultButton` / `cancelButton` (titulek =
    jazyk AppKitu, `enabled`), `items` (název, role, `enabled`, kde ho AX má), `frame`;
  - `panelgo <pid> <cesta> [<s>]` – ⌘⇧G, ⌘A, cesta přes `type`, Return, výchozí tlačítko; exit 1 bez panelu, 2 pole
    „Přejít na složku“ se neotevřelo / panel zůstal otevřený, 4 výchozí tlačítko zůstalo zakázané (položku nejde vybrat);
    při každé chybě panel zruší;
  - `panelcancel <pid>` – „Zrušit“ (AXPress), jinak Esc; exit 1, když panel zůstane;
  - `menupress`: segment cesty končící `*` vybere položku podle začátku titulku (položka s měnícím se procentem).
- **Kontrola `mac.file-dialogs`** (`tools/macos-verify.ps1`, Permission, `Session` + `Accessibility`, preset `release`
  za `mac.notifications-content`, teď 31 kontrol). Média: nahrávka tisku 1 s výpadkem (`…mp4`, `…-2.mp4`) z
  `mac.recording-files` a timelapse podle času z `mac.timelapse-files` téhož běhu (`$Context.Shared`, jen když prošly
  decode), jinak `Get-FileDialogMedia` udělá jeden vlastní tisk (simulátor x20, nahrávka + timelapse, kamera 10 s vypnutá).
  Každá část si je zkopíruje do podsložky tiskárny své testovací složky spolu s `notes.txt`.
  - (a) Nastavení › Nahrávání › „Změnit…“ (AXPress): panel (role zapsaná do poznámky, titulek
    `SettingsRecordingFolderPickerTitle`), `panelgo` na složku `chmod 0500` → `SettingsRecordingFolderError` =
    „Do vybrané složky nelze zapisovat.“; `panelgo` na zapisovatelnou složku → pole `SettingsRecordingFolder` ji ukáže,
    chyba zmizí, Uložit ji zapíše do `settings.json`.
  - (b) zpoždění 500 ms; okno kamery otevřené, vpředu náhradní aplikace (helper `front`); položka exportu → jeden panel,
    `Get-VerifyPanelProblems` (volné okno, titulek, podsložka tiskárny, aplikace vpředu a panel klíčovým oknem),
    `Get-VerifyPanelItemProblems` (`.mp4` povolené, `notes.txt` zakázaný; neznámý stav = jen poznámka), druhý klik →
    pořád jeden panel, kamera běží (AX `CameraOverlay` bez `CameraNextAttempt`, okno, RTSP spojení), `panelgo notes.txt`
    nesmí projít; nahrávka → log `NotATimelapse` + notifikace; timelapse → procenta z `helper menu` (`Get-VerifyMenuPercent`)
    musí růst, složka `…_timelapse_jpg` s `0001.jpg`… (počet = `decode` = log), notifikace s počtem snímků; zrušení při
    ≥ 1 % (`menupress …(*`) → log `Cancelled`, `ls -a` bez `_timelapse_jpg*`.
  - (c) zpoždění 20 ms na paket; panel (volné okno, titulek, složka, vpředu); timelapse → `NotARecording` + notifikace;
    nahrávka → rostoucí průběh, `…_recording_timelapse.mp4`, `decode` bez problémů, 30 fps (±0,5), snímky v rozsahu
    podle délky obou částí a víc, než dá samotná první část (= i část po výpadku), notifikace; během úlohy otevře položka
    exportu jeden panel, druhý klik nic, panel zrušen, pak zrušení úlohy → `Cancelled`, bez `_recording_timelapse*`.
  - Na konci každé části: žádný otevřený panel ani menu (jinak problém a úklid), `~/Movies/LayerPeek` beze změny.
    Notifikace přes režim `--layerpeek-e2e-notification-list` (T-212) jen s povolenými oznámeními (pak start přes
    `open` s `LAYERPEEK_TEST_TOASTS=1`); bez nich se zbytek ověří a kontrola skončí `SKIP` s důvodem, nikdy `PASS`.
  - Čisté funkce ve `VerifyTools.psm1`: `Get-VerifyFileJobEvents` (výsledky úloh z logu), `Get-VerifyPanelProblems`,
    `Get-VerifyPanelItemProblems`, `Get-VerifyMenuPercent`; Pester blok „Open panels“ (6 testů), test presetu obsahuje
    novou kontrolu. Dokumentace: tabulka kontrol, Zpřístupnění, oznámení, vzorky a pozůstatky v
    `docs/macos-manual-testing.md`; T-127 oddíl 1o.
- **Neověřeno proti skutečnému systému** (bez Zpřístupnění a se zamčenou obrazovkou nešlo): podoba panelu v AX stromu
  na macOS 27 (výchozí / zrušovací tlačítko, prohlížeč souborů, AXDocument, pole ⌘⇧G, `enabled` položek), tedy celé
  `panel` / `panelgo` / `panelcancel` / `type`; kde panel běží; zda je „Změnit…“ (dialog Avalonie) list Nastavení; jazyk
  tlačítek AppKitu (T-097 otázka 5) proto zatím nezapsán – bod 1o v T-127, bod sám uzavře T-216. Helper se přeloží bez
  varování a bez Zpřístupnění vrací exit 3.
- **Ověřeno 2026-11-15:** `pwsh tools/macos-verify.ps1 -Unattended -Only mac.file-dialogs,mac.profile-unchanged -Allow
  mac.file-dialogs`, běh `artifacts/verify/20261115-114811/`: `SKIP mac.file-dialogs` (předběžné podmínky Session –
  zamčená obrazovka – a Accessibility), `PASS mac.profile-unchanged`. `-SelfTest` PASSED (41 kontrol), `-ListChecks
  -Preset release` 31 kontrol s `mac.file-dialogs` mezi povolenými předem. Žádná Release aplikace se nespouštěla, profil
  uživatele beze změny.
- **Build a testy 2026-11-15** (na `c51c5718` + tato změna): `dotnet build` Debug i `-c Release` 0 varování, 0 chyb.
  Plný `dotnet test`: 10 848 testů, 10 577 prošlo, 271 přeskočeno, 0 selhalo; `AppEnvironmentTests` v Release 10 prošlo.
  Pester `tools/VerifyTools.Tests.ps1` 83 prošlo / 1 přeskočen (Windows) / 0 selhalo. AOT `dotnet publish
  src/LayerPeek.App -c Release -r osx-arm64`: exit 0, 0 varování kompilátoru / trimmeru / AOT (jen hlášky nativních
  nástrojů: dsymutil, mezipaměť modulů Swift, `xcodebuild` bez Xcode – nesouvisí); binárka ani Release `LayerPeek.dll`
  neobsahují `TestPacedCameraFeedFactory` ani `LAYERPEEK_TEST_FILE_JOB_DELAY_MS` (Debug ano). Bez nových balíčků. Nic na
  GitHub.
- Uzavřeno 2026-11-15: zbývající ruční ověření (běh `mac.file-dialogs` se Zpřístupněním a odemčenou obrazovkou)
  přesunuto do T-127, oddíl 1o (rozhodnutí uživatele)
