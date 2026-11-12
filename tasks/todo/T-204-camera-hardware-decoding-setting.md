---
id: T-204
title: Nastavení > Kamera – volba „Hardwarové dekódování“ (Automaticky / Vypnuto)
status: todo
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
- [ ] Volba je na záložce Kamera, výchozí „Automaticky“, uloží se a po znovuotevření okna se načte; neuložená změna se
      hlásí jako ostatní volby (UI testy v `LayerPeek.App.UiTests`).
- [ ] Po uložení se volba uplatní podle odpovědi na otázku 2 (test: dekodér otevřený po změně použije novou hodnotu;
      při okamžitém uplatnění i znovuotevření dekodéru běžícího streamu bez odpojení).
- [ ] Texty ve všech 8 jazycích, nic se neořezává (layout testy záložky Kamera projdou i s novým řádkem).
- [ ] Všechny příručky aktualizované (11.6 a 15), `UserManualTests` projdou.
- [ ] Referenční snímky macOS přegenerované a zkontrolované okem; Windows sada → T-125 (pokud se mění).
- [ ] Compiled bindings, žádné AOT varování (`dotnet publish -c Release -r osx-arm64` nebo `win-x64` podle OS).
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

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
(vyplní agent po dokončení)
