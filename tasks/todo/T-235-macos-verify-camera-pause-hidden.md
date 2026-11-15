---
id: T-235
title: macOS ověření – kontrola pozastavení kamery při zakrytí okna a zamčení (`mac.camera-pause-hidden`)
status: todo
owner: qa-reviewer
priority: P3
platform: macos
depends_on: [T-187, T-215]
---

## Cíl
T-187 pozastaví stream okna kamery na macOS při zamčení, spánku displeje a úplném zakrytí okna (i na jiném Space).
Logiku kryjí testy s falešnými službami, skutečné chování zatím jen ruční body T-127 1r. Automatická kontrola v
`tools/macos-verify.ps1` by z nich zakrytí a jiný Space ověřila bez uživatele (s odemčenou obrazovkou, v `-Preset release`).

## Rozsah
- Nová kontrola `mac.camera-pause-hidden` (Auto, potřebuje Zpřístupnění a odemčenou obrazovku, jinak `SKIP` přes
  `Invoke-SessionCheck`): testovací bundle a simulátor s kamerou, okno kamery otevřené příkazem `camera:<id>`, čeká se na
  spojení RTSP (`Get-RtspConnections`).
- Zakrytí: okno jiné aplikace (např. Finder) přes celé okno kamery – helper potřebuje akci posunu a velikosti okna
  (`axwin … frame x y w h` přes `kAXPositionAttribute` / `kAXSizeAttribute`) nebo vlastní okno helperu. Očekává se konec
  spojení do 2 s + 10 s (linger hubu) + rezerva a po odkrytí nové spojení do 10 s.
- Jiný Space: okno kamery do celé obrazovky (`axwin … fullscreen`) a Finder do popředí (`Invoke-FinderToFront`) → okno
  kamery na svém Space zakryté → konec spojení; návrat (`axwin … raise`) → nové spojení. Po kontrole celou obrazovku
  ukončit.
- Krátké zakrytí (pod 2 s) spojení neukončí (žádné nové připojení v logu).
- Důkazy: výpisy `lsof`, log aplikace, rámce oken (`windows`).
- Zamčení a spánek displeje zůstávají ruční (T-127 1r): zamknout obrazovku skript bez uživatele nemá jak vrátit.

## Akceptační kritéria
- [ ] `mac.camera-pause-hidden` je `PASS` s odemčenou obrazovkou a `SKIP` se zamčenou; v `-Preset release`.
- [ ] Pester testy nových čistých funkcí ve `VerifyTools.psm1` (jsou-li).
- [ ] Při `PASS` se v T-127 odškrtnou body 1r „zakrytí“, „jiná plocha“ a „přechody“.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
