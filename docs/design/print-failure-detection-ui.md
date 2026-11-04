# Detekce chyb tisku – návrh UI (T-154)

Návrh nastavení detekce chyb tisku, editoru výřezu podložky a notifikace poplachu. Vychází z rozhodnutí uživatele
2026-11-04 (zapsaná v [T-154](../../tasks/done/T-154-print-failure-detection-ui.md)), infrastruktury T-153 a podkladů
spiku T-152 ([detection-spike.md](../detection-spike.md)). Okno nastavení má dál výchozí velikost 780 × 532 DIP (T-094)
a v žádném jazyce se v něm nesmí nic posouvat ani uříznout (T-033). Ověřují to headless testy
`SettingsWindowLayoutTests` (`DetectionSubTabFitsInEveryLanguage`, `RecordingTabFitsInEveryLanguage`,
`EverythingFitsWithoutScrollingInEveryState` s pohledem „Detection“).

## 1. Umístění

| Kde | Co | Proč |
|---|---|---|
| Tiskárny › podzáložka **Detekce** (čtvrtá, za „Okno kamery“) | „Hlídat tisk této tiskárny kamerou“, výřez podložky, seznam detektorů (zapnuto, citlivost, popis) | rozhodnutí 2026-11-04: u tiskárny vedle kamery; vlastní podzáložka, protože Kamera ani Okno kamery nemají volné místo (T-033, T-148) |
| Nahrávání › sekce **Detekce chyb tisku** (třetí, pod dashcamem) | hlavní vypínač, opakování poplachu, ukládání snímků, interval vzorkování 2–5 s | rozhodnutí 2026-11-04: globálně na záložce Nahrávání; interval zůstává globální jako v T-153 |

Po instalaci je vypnuté obojí (hlavní vypínač i zapnutí u tiskárny). Podzáložka Detekce nemá značku chyby: žádné její
pole nemůže být neplatné (výřez je vždy platný, citlivost je posuvník). Ctrl+PageDown / ⌘⌥→ ji obejdou jako ostatní
podzáložky.

## 2. Podzáložka Detekce

Šířka obsahu 548 DIP (780 − okraje − seznam tiskáren), výška ~380 DIP.

```
[✓] Hlídat tisk této tiskárny kamerou
    <jedna nápověda: kamera vypnutá / globálně vypnuto / kdy běží>             (12 px, nejvýš 2 řádky)

Výřez podložky          [Načíst snímek]     Co hledat
┌──────────────────────────────────┐       [✓] Změna na podložce
│ Poslední uložený snímek – …      │           Citlivost
│        (snímek 16:9)             │           ───────●──────────  50
│      ┌──────────────┐            │           Pozná zmizení nebo posun dílu…   (12 px)
│      │   výřez      │            │           Známé omezení: u tmavého filamentu… (12 px, tučně)
└──────┴──────────────┴────────────┘
[Celý snímek] [Výchozí pro Core One]
   256 × 144                    16          zbytek šířky (~276)
```

- **Nápověda pod vypínačem** – vždy jen jedna: „Detekce potřebuje kameru tiskárny…“ (kamera vypnutá, pole jsou
  zakázaná a hodnoty zůstávají), „Detekce chyb tisku je vypnutá pro všechny tiskárny: zapněte ji na záložce Nahrávání.“
  (globálně vypnuto), jinak „Běží jen během tisku a se svítícím osvětlením komory. Obraz kamery zůstává v tomto počítači.“
- **Editor výřezu** 256 × 144 DIP (16:9 jako kamery; menší než náhled 320 × 180 na podzáložce Kamera, aby se vešel
  sloupec detektorů i v nejdelším jazyce). Snímek je vykreslený rovnoměrně s černými pruhy jako náhled T-034, výřez jen
  nad snímkem, mimo výřez je snímek ztmavený (50 % černé). Obdélník: černý obrys 3 DIP + bílý 1,5 DIP (2,5 DIP
  s fokusem klávesnice), v rozích bílé úchyty 8 × 8 DIP.
  - Myš: tažení mimo výřez nakreslí nový, uvnitř ho přesune, za roh (do 8 DIP) změní velikost proti protějšímu rohu;
    kurzor podle místa (kříž, přesun, rohy). Tažení přes protější roh obdélník převrátí, mimo snímek se zastaví na
    okraji, nejmenší velikost 1 % snímku. Esc během tažení ho zruší. Hodnota se zapíše až po uvolnění tlačítka
    (ne při každém pohybu).
  - Klávesnice: editor je v pořadí Tab; šipky posouvají o 1 % snímku, Shift + šipky mění šířku a výšku (pravý a dolní
    okraj), s Ctrl (macOS ⌘) o 10 %. Šipky z editoru neodcházejí (nepřepínají podzáložku).
  - Přístupnost: název pro čtečky „Výřez podložky: 15 % zleva, 55 % shora, šířka 70 %, výška 45 %“ (celá procenta,
    mění se s výřezem), nápověda a tooltip = ovládání myší a klávesnicí.
  - Bez snímku je editor černý s textem „Zatím žádný snímek. Načtěte ho z kamery.“; obdélník jde upravit i tak.
  - Poznámky ke snímku leží v tmavém pruhu přes **horní** okraj snímku (tam bývá tiskárna, ne podložka) a propouštějí
    myš: „Poslední uložený snímek – kamera se mohla mezitím pohnout.“ a neúspěšné „Načíst snímek“ (text testu kamery,
    světle červeně).
- **Načíst snímek** (vpravo od nadpisu, před ním ukazatel průběhu) = stejný test kamery jako „Otestovat kameru“
  (sdílené spojení T-143); úspěšný snímek se ukáže v editoru i na podzáložce Kamera a uloží se jako poslední snímek.
- **Celý snímek** a **Výchozí pro Core One** (jen u Core One: model zvolený pro obrázek, jinak zjištěný, stejné pravidlo
  jako `DetectionRegion.DefaultFor`) pod editorem, zalomí se na dva řádky, když se nevejdou.
- **Detektory** (`ItemsControl` podle registrací, pořadí z `PrintFailureDetectors.All`): zaškrtávací pole s názvem,
  pod ním odsazeně „Citlivost“, posuvník 0–100 (krok 1, PageUp/PageDown 10) a číslo, popis (12 px) a známé omezení
  (12 px, tučně – označené omezení). Další detektor (T-155, T-156) přibude jako další položka bez změny rozložení; když
  by se pak sloupec nevešel, posune se obsah podzáložky (záložní posuvník), detaily detektorů se tím nezmění. Bez
  detektorů: „Tato verze LayerPeeku zatím nemá žádný detektor.“

## 3. Sekce Detekce chyb tisku (záložka Nahrávání)

Dva sloupce jako sekce Nahrávání a Dashcam:

```
Detekce chyb tisku
[ ] Hledat chyby tisku v obraze kamery         Poplach  [Jednou za tisk a druh chyby  ▾]
[✓] Ukládat snímek každého poplachu            Kontrolovat kameru každých (s) [ 2 ▴▾]
                                                <chyba intervalu, jen když je neplatný>
```

- Nápovědy obou zaškrtávacích polí a intervalu jsou tooltipem a nápovědou pro čtečky (na řádky nápovědy záložka nemá
  místo). Chyba intervalu („Zadejte číslo od 2 do 5.“) se ukáže pod polem, neúspěšné uložení přepne na záložku a dá
  poli fokus (`SettingsFieldIds.SettingsDetectionInterval`). Desetinná místa jsou povolená (T-153 ukládá ms).
- „Poplach“: „Jednou za tisk a druh chyby“ (výchozí) / „Znovu každých 10 min, dokud trvá“.
- Aby se sekce vešla: mezery mezi sekcemi 8 místo 12 DIP a nápověda „Ručně uložit jde vždy…“ dashcamu je pod nápovědou
  délky v levém sloupci (pravý sloupec se spouštěči byl nejvyšší).

## 4. Notifikace poplachu

Stejné texty na Windows i macOS (`PrintFailureNotificationText`, `ToastContentBuilder.PrintFailureLines`):

| Řádek | Obsah |
|---|---|
| Titulek | název tiskárny (prázdný → id) |
| Text | „Možná chyba tisku – Změna na podložce“ (název detektoru) |
| Detail | název souboru úlohy, je-li známý |
| Obrázek | snímek poplachu |
| Tlačítko | „Falešný poplach“ |

- Klik na notifikaci otevře (nebo vytáhne do popředí, z minimalizace obnoví) okno kamery tiskárny; před startem UI se
  klik podrží jako u nastavení. Tlačítko „Falešný poplach“ uloží lokální záznam (T-153: hodnoty a snímek v
  `detection/false-alarms`), citlivost se nemění, nic se neotevře a nic dalšího se neukáže. Poplach, který tento běh
  aplikace už nezná (po restartu, nebo víc než 8 novějších poplachů), se jen zapíše do logu.
- Zvuk jako u chyby tisku (`NotificationSeverity.ActionRequired`), řídí se „Zobrazovat notifikace“.
- Argumenty jsou nedůvěryhodný vstup systému: `camera=<id tiskárny, percent-encoded>` a `false-alarm=<id poplachu>`
  (1–64 znaků `a-z`, `0-9`, `-`), jen přesné tvary, délka nejvýš 512 znaků (`ToastLaunchArgument`).
- **Obrázek** (rozhodnutí 2026-11-04): Windows bere obrázek toastu i macOS přílohu jen ze souboru.
  - Windows: uložený snímek ve složce nahrávek, když je ukládání zapnuté; jinak se JPEG z paměti zapíše do
    `<složka mezipaměti>/notification-images/alarm-<id>.jpg` (Windows `%LOCALAPPDATA%\LayerPeek\cache`, macOS `~/Library/Caches/LayerPeek`, nikdy roamingový profil). Obrázek je vložený (`<image>` bez `placement`),
    takže si zachová poměr stran kamery (hero by ho ořízl na 2 : 1).
  - macOS: vždy kopie ve stejné složce, protože systém přiložený soubor přesune do svého úložiště (uložený snímek musí
    zůstat u nahrávek). `UNNotificationAttachment` + kategorie s akcí `UNNotificationAction` (bez přenesení aplikace
    do popředí).
  - Složka drží nejvýš 5 posledních obrázků; každý zápis smaže starší a pozůstatky přerušeného zápisu. Starší
    notifikace v centru oznámení pak mohou být bez obrázku. Obrázky nad 3 MB se nepřikládají.

## 5. Poslední snímek pro editor (rozhodnutí 2026-11-04, volba návrhu)

- Jeden zmenšený JPEG na tiskárnu (nejvýš 640 × 360, kvalita 80, ~20–60 KB) v `<složka mezipaměti>/last-frames/`,
  název = prvních 32 hex číslic SHA-256 id tiskárny (žádný název ani adresa na disku).
- Zapisuje se jen tehdy, když okno nastavení snímek stejně dostalo: úspěšný test kamery na podzáložce Kamera nebo
  „Načíst snímek“. Čte se jednou za život okna, když se podzáložka Detekce poprvé ukáže pro danou tiskárnu (dekódování
  mimo UI vlákno). Žádná práce na pozadí, žádný časovač, nic, když je okno zavřené. Smaže se, když se uloží nastavení
  bez dané tiskárny.
- Editor se **sám ke kameře nepřipojuje** (nejkonzervativnější volba, viz Otevřené otázky T-154): ukáže snímek z testu
  kamery v tomto okně, jinak poslední uložený snímek, jinak černou plochu.
