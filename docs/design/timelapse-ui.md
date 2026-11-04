# Timelapse – návrh UI (T-161)

Krátký návrh ovládání timelapse (T-160) v aplikaci. Vychází z rozhodnutí uživatele 2026-11-04 (T-149, T-161): celá funkce
je **experimentální**, timelapse je automaticky u tiskárny (výchozí vypnuto) i ručně z tray menu a z okna kamery, interval
je globální (výchozí 30 s, 5–600 s), výstup má pevně 30 snímků za sekundu. Okno nastavení má dál 780 × 532 DIP (T-094)
a v žádném jazyce se v něm nic neposouvá ani neuřízne (T-033) – ověřují headless testy `SettingsWindowLayoutTests`
a `SettingsWindowTimelapseUiTests`.

## 1. Štítek „Experimentální“

| Kde | Podoba |
|---|---|
| Nastavení (záložka Nahrávání, podzáložka Kamera) | malý jantarový štítek (`Border.badge`: pozadí jako upozornění, rámeček a text `ExperimentalBrush`, 11 px, polotučně) vpravo od nadpisu sekce / zaškrtávacího pole; tip a nápověda pro čtečky „Experimentální funkce: může se změnit nebo nemusí vždy fungovat spolehlivě.“ |
| Tray menu | součást textu položky: „Spustit timelapse (experimentální)“ / „Zastavit timelapse (experimentální)“ (nativní menu nemá štítky) |
| Okno kamery | součást tipu a přístupného názvu tlačítka: „Spustit timelapse (experimentální)“ / „Zastavit timelapse (experimentální)“ (na štítek v řadě tlačítek okna není místo, hlavně v malém náhledu 240 DIP) |

Text štítku je jedno slovo (`SettingsExperimental`) ve všech 8 jazycích.

## 2. Nastavení

**Podzáložka Kamera u tiskárny** – pod „Automaticky nahrávat tisk“ a „Uchovávat poslední minuty (dashcam)“ vlastní řádek
„Pořizovat timelapse tisku“ + štítek. Pravidla jako u nahrávání: zakázané (hodnota zůstává), dokud je kamera vypnutá nebo
tato verze timelapse neumí; co volba dělá (nebo proč je zakázaná), je tip a nápověda. Aby se řádek vešel, je popisek
„Adresa RTSP streamu“ nově vlevo od pole adresy (ne nad ním) – ušetří ~25 DIP, přesně tolik, kolik řádek potřebuje i ve
stavu „odebrané přihlašovací údaje + nejdelší chyba testu“ v němčině.

**Záložka Nahrávání** – sekce „Timelapse“ + štítek mezi dashcamem a detekcí chyb tisku, jeden řádek:

```
Timelapse [Experimentální]                    Čas mezi snímky (sekundy) [ 30 ▲▼]
<chyba pole / „Tato verze neumí pořizovat timelapse.“ – jen když nastane>
```

- **Ovládací prvek intervalu:** `NumericUpDown` celých sekund 5–600, krok 5 s, šířka 120 DIP. Proč ne pevné volby: služba
  přijme libovolnou hodnotu z rozsahu, pevné volby (10 / 30 / 60 s) by nedovolily krátký interval pro rychlý tisk ani
  dlouhý pro mnohahodinový, a ostatní čísla záložky (limit, délka dashcamu, interval detekce) jsou také volná čísla
  s ověřením. Chybná hodnota (mimo rozsah, desetinná, prázdná) se ukáže pod řádkem textem „Zadejte celé číslo od 5 do 600.“,
  zablokuje uložení, přepne na záložku Nahrávání a dá poli fokus (T-112).
- **Nápověda** („Jeden snímek za zvolený čas, během pauzy tisku žádný. Video má 30 snímků za sekundu.“) je tip a nápověda
  pro čtečky pole, ne vlastní řádek – na záložce na něj není místo (stejně jako interval detekce, T-154).
- **Úprava sekce dashcamu (T-148), aby se timelapse vešel:** délka v minutách je v řádku nadpisu (vpravo jako interval
  timelapse), nápověda délky přes celou šířku pod ním (chyba ji nahradí), čtyři spouštěče ve dvou sloupcích po dvou.
  Popisek „Ukládat automaticky“ a nápověda „Ručně jde uložit vždy …“ už nemají vlastní řádek; nápověda je tip a nápověda
  pro čtečky každého spouštěče. Sekce je o 57 DIP nižší. Francouzské texty nápovědy dashcamu a limitu nahrávek jsou kratší,
  aby měly jeden / dva řádky.
- Nejplnější stav (všechny chyby záložky ve francouzštině / italštině / španělštině) má 419 z 422 DIP.

## 3. Tray menu (podmenu tiskárny s kamerou)

```
Otevřít PrusaLink
Zobrazit kameru
────────────
Nahrávat
Uložit posledních 2 min            (jen s dashcamem)
Spustit timelapse (experimentální)  ⇄  Zastavit timelapse (experimentální)
Přehrát poslední nahrávku
Přehrát poslední timelapse          (zakázané s tipem „Tiskárna zatím nemá žádný timelapse.“)
Otevřít složku s nahrávkami         (sdílená s nahrávkami, beze změny)
```

- Start / Stop podle stavu služby (`TimelapseControlState`, sdílí ho i okno kamery), stejná přístupová klávesa v obou
  stavech; během `Starting` / `Stopping` a bez rekordéru zakázané s důvodem. Během pauzy tisku zůstane „Zastavit“ povolené
  s tipem „Během pauzy tisku se snímky nepořizují.“ Klik jedná podle toho, co položka ukazovala.
- „Pořizuje se timelapse“ = položka ukazuje „Zastavit timelapse“. Tooltip ikony se nemění (na Windows má limit 127 znaků
  a už nese „Nahrává se“; viz otevřená otázka v T-161).
- „Přehrát poslední timelapse“ otevře `CameraTimelapseStatus.LastFile` v přehrávači systému (`IFileLauncher`, jen
  existující `.mp4`). „Přehrát poslední nahrávku“ timelapse nevybírá (T-160).
- Přístupové klávesy jsou v podmenu unikátní ve všech jazycích (test), např. cs `_timelapse` → T, „Přehrát poslední
  ti_melapse“ → M.

## 4. Okno kamery

- Tlačítko **před tlačítkem nahrávání** ve všech režimech (normální okno, malý náhled, celá obrazovka): v klidu bílé
  stopky, skryté jako ostatní tlačítka, dokud není ukazatel nad oknem. Během timelapse **jantarové stopky (#F5A524)
  a „TL“** (`CameraTimelapseIndicator`, 2–4 znaky jako „REC“), trvale viditelné; během pauzy tisku ztlumené (60 %),
  tip doplní „Během pauzy tisku se snímky nepořizují.“ Pod ukazatelem / s fokusem se stopky změní na čtverec Stop.
- Statické: žádný časovač ani animace; stav se čte jen při `Changed` této tiskárny.
- Ve spodní řadě malého náhledu 240 × 168 DIP se při nahrávání i timelapse vejdou všechna tlačítka (test, snímek
  `camera-mini-timelapse-buttons.png`).

## 5. Notifikace

Chyba timelapse (`CameraRecordingError.IsTimelapse`) má vlastní druhý řádek „Timelapse selhal“; důvody, které mluví
o nahrávce, mají vlastní znění (bez muxeru, chyba zápisu souboru, překročený limit), ostatní (kamera, složka, disk, proud)
jsou společné s nahráváním. Klik otevře nastavení na záložce Nahrávání jako u nahrávání. Úspěšný timelapse se nenotifikuje
(jako nahrávky, T-145).

## 6. Snímky (macOS)

Nové `camera-timelapse.png`, `camera-mini-timelapse.png`, `camera-mini-timelapse-buttons.png`; změněné
`camera-mini-recording-buttons.png`, `settings-recording.png`, `settings-camera.png`, `settings-camera-preview.png`,
`settings-camera-preview-4x3-de.png`, `settings-camera-notice.png`. Sada Windows → T-125.
