---
id: T-252
title: Okno kamery ve vzdálené relaci (RDP) zůstane na „Connecting to the camera…“ – stav konzolového displeje se ignoruje
status: in-progress
owner: ui-dev
priority: P2
platform: windows
depends_on: [T-186]
---

> **Rozhodnutí uživatele 2026-11-28:** ve vzdálené relaci se stav konzolového displeje (`GUID_CONSOLE_DISPLAY_STATE`)
> nebere v úvahu; zámek relace platí dál. Na konzoli se chování T-186 nemění. Úkol se implementuje rovnou (logika
> a testy na Linuxu, build a testy na Windows ve VM).

## Cíl
Nalezeno 2026-11-28 při zkoušení ve Windows 11 VM se simulátorem (`tools/PrusaSimulator`), připojení přes RDP
(Remmina): okno kamery zůstane černé s nápisem „Connecting to the camera…“, i když test kamery v Nastavení obraz ukáže
a nahrávání běží. V logu je při každém otevření okna jen `Camera video decoding: software (not used on this OS yet).`
a žádná chyba. Přes konzoli hypervizoru (Incus) obraz běží.

Příčina: T-186 pozastaví okno kamery, když „obrazovka není vidět“ (`WindowsDisplayActivityService`
→ `DisplayActivityTracker`). Stav displeje se bere z `GUID_CONSOLE_DISPLAY_STATE`, tedy z fyzického konzolového
displeje. Ve vzdálené relaci Windows hlásí konzolový displej jako vypnutý, takže okno po otevření hned uvolní svůj
odběr snímků (`CameraViewModel.UpdateActive` → `StopStream`). Nahrávání, dashcam a test kamery mají vlastní odběr, proto
fungují. Pozastavení se nezapisuje do logu, proto v něm nic není.

## Rozsah
- `DisplayActivityTracker`: příznak vzdálené relace (`SetRemoteSession`). Ve vzdálené relaci je obrazovka vidět, když
  relace není zamčená; stav konzolového displeje se ignoruje, ale pamatuje se pro návrat na konzoli.
- `WindowsDisplayActivityService`: typ relace z `WTSQuerySessionInformationW(WTS_CURRENT_SESSION, WTSClientProtocolType)`
  (0 = konzole, jinak vzdálená relace; když Windows neodpoví, platí konzole jako dosud). Dotaz při startu, po registraci
  a znovu při `WM_WTSSESSION_CHANGE` s `WTS_CONSOLE_CONNECT`, `WTS_CONSOLE_DISCONNECT`, `WTS_REMOTE_CONNECT`
  a `WTS_REMOTE_DISCONNECT`, aby se přepnutí RDP ↔ konzole téže relace projevilo za běhu.
- Bez reflexe, `[LibraryImport]` (AOT). macOS (`MacDisplayActivityService`) se nemění.
- Testy logiky trackeru (běží na všech OS) a test dotazu na typ relace (jen Windows).

## Akceptační kritéria
- [x] Ve vzdálené relaci hlášení „konzolový displej vypnutý“ obrazovku neskryje; zámek relace ji skryje dál.
- [x] Na konzoli se chování T-186 nemění (vypnutý displej i zámek obrazovku skryjí).
- [x] Přepnutí relace mezi RDP a konzolí za běhu přepočítá viditelnost (na konzoli platí poslední hlášený stav displeje).
- [x] Testy trackeru pro vzdálenou relaci; test, že `WindowsDisplayActivityService` na Windows typ relace zjistí.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování (Linux: build bez FFmpeg; Windows: celé řešení ve VM).
- [ ] Ruční ověření ve Windows VM: přes RDP (Remmina) okno kamery ukáže obraz; přes konzoli po vypnutí displeje
      (nebo zamčení) se pozastaví jako dosud.

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- `DisplayActivityTracker.SetRemoteSession`: viditelnost = odemčeno a (vzdálená relace nebo displej zapnutý). Stav
  konzolového displeje se ve vzdálené relaci dál ukládá, po návratu na konzoli platí poslední hlášený.
- `WindowsDisplayActivityService.QueryRemoteSession`: `WTSQuerySessionInformationW(WTS_CURRENT_SESSION,
  WTSClientProtocolType)` (USHORT, 0 = konzole); dotaz v `Start`, po registraci a při `WM_WTSSESSION_CHANGE` 0x1–0x4
  (`WTS_CONSOLE_CONNECT` … `WTS_REMOTE_DISCONNECT`). Neznámá odpověď = konzole (chování T-186). Žádná nová závislost.
- Testy: 5 nových v `DisplayActivityTrackerTests`, `CurrentSessionTypeCanBeQueried` ve
  `WindowsDisplayActivityServiceTests` (jen Windows).
- Linux (2026-11-28, .NET SDK 11.0.100, `-p:LayerPeekFFmpegEnabled=false`): build 0 chyb, 0 varování; testy trackeru
  prošly. Celé `dotnet test` na Linuxu má 158 selhání nesouvisejících s úkolem (156 UI snapshotů bez linuxových
  referenčních obrázků, 2 testy licencí kvůli balíčku `runtime.linux-x64.Microsoft.DotNet.ILCompiler`) – podklad pro
  port na Linux.
- Mimo rozsah (k zvážení): odpojená relace (RDP zavřené bez odhlášení) se dál počítá jako vzdálená, takže okno kamery
  v ní stream nepozastaví.
- Čeká na: `dotnet build` a `dotnet test` na Windows a ruční ověření ve VM (RDP i konzole).
