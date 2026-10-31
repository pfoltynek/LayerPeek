---
id: T-120
title: E2E testy místo ručních kontrol okna kamery (špendlík, obrys, hlavní panel, menu)
status: in-progress
owner: qa-reviewer
priority: P2
platform: windows
depends_on: [T-102, T-117, T-118, T-119]
---

## Cíl
Uživatel 2026-10-31 ručně ověřil body T-102, T-117 a T-118 v T-114 (vše v pořádku) a přeje si, aby je příště nemusel
kontrolovat znovu: „ideální by bylo, kdyby jsi i na toto dodal UI testy“. Ruční kontroly se nahradí automatickými testy
na skutečné aplikaci (E2E přes `tools/run-e2e.ps1`, simulátor, UI Automation), případně headless UI testy tam, kde
skutečná aplikace nic nepřidá. Pravidla: změny systému (měřítko, motiv) jen s obnovou i po pádu a s varováním
a odpočtem 5 s (`run-e2e.ps1`); testy po sobě uklidí vše; žádné globální klávesové zkratky; nikdy nepsat do cizí
aplikace ani ji nezavírat.

## Rozsah
Nejdřív zmapovat, co už pokrývají stávající testy (headless UI testy T-117/T-118, `BringToFrontTests` T-119,
`windows-verify.ps1 -Only win.taskbar`, testy měřítek T-071, `TrayMenuTests`), a doplnit jen chybějící.

1. **T-117 špendlík (skutečná aplikace):** špendlík v normálním okně i malém náhledu, na celé obrazovce není; klik
   přepne „vždy navrchu“ hned (Win32 `WS_EX_TOPMOST`, okno nad oknem jiného procesu) a tlačítko na hlavním panelu
   zmizí / objeví se; kontextové menu pravým tlačítkem, klávesou Menu a Shift+F10 (dvě položky v normálním okně,
   jedna zaškrtnutá v malém náhledu); přístupnost přes UIA (název, `Toggle` stav, mezerník/Enter přepíná, fokus
   zůstane); volba přežije restart aplikace a otevřené Nastavení ji ukáže.
2. **T-118 obrys (skutečná aplikace):** obrys jen v malém náhledu, viditelný na světlém i tmavém pozadí (snímek jen
   oblasti okna, ne celé plochy), zesvětlení při najetí; ostrost čáry při 100 / 125 / 150 % (1 px / 1 px / 2 px, bez
   půlpixelu) – se změnou měřítka přes stávající infrastrukturu T-071; úchopy mění velikost, tažení přesouvá okno.
3. **T-102 hlavní panel a menu:** tlačítko na hlavním panelu podle pravidla (normální okno a celá obrazovka ano,
   malý náhled ne, okno navrchu ne; Nastavení, Diagnostika, O aplikaci vždy); položky menu tiskárny s otevřenou kamerou
   a přístupové klávesy; přepnutí normální okno navrchu ↔ celá obrazovka bez ztráty fokusu, posunu a změny monitoru.
4. Co automatizovat nejde (např. subjektivní čitelnost ztlumeného špendlíku), zůstane jako nepovinná doporučená
   ruční kontrola v T-114.

## Akceptační kritéria
- [ ] Zmapované pokrytí (co už testují stávající testy) v poznámkách
- [ ] E2E / UI testy pro body 1–3, každý ruční bod T-114 (T-102, T-117, T-118) má odkaz na test, který ho nahrazuje
- [ ] Testy po sobě uklidí vše (okna, procesy, nastavení, měřítko, motiv) i po pádu; jména PascalCase bez podtržítek
- [ ] Běh `tools/run-e2e.ps1` na nové třídy prošel (výsledek v poznámkách)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
(žádné)
