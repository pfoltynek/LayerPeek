---
id: T-213
title: macOS ověření – systémové dialogy souborů (`mac.file-dialogs`, NSOpenPanel přes Accessibility)
status: todo
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
- [ ] `mac.file-dialogs` (a) = `PASS` (doplní v T-127 bod **T-146**: „Změnit…“ = systémový dialog složky,
      nezapisovatelná složka)
- [ ] `mac.file-dialogs` (b) = `PASS` (uzavře v T-127 bod **T-165**)
- [ ] `mac.file-dialogs` (c) = `PASS` (uzavře v T-127 bod **T-164**)
- [ ] Po běhu není otevřený žádný panel ani menu; `mac.profile-unchanged` = `PASS`
- [ ] Pester / `-SelfTest` a `dotnet build` / `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
