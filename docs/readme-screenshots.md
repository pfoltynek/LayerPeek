# Snímky obrazovky v README a v příručce

Obrázky v `README.md` (anglicky), `README.cz.md` (česky) a v uživatelské příručce `manuals/<jazyk>/manual.md` se
negenerují ručně, ale vykresluje je přímo aplikace. Jsou v `docs/images/<jazyk>/` pro všech osm jazyků aplikace (en, cs,
sk, de, es, fr, it, pl), se stejnými názvy souborů ve všech jazycích.

## Jak vznikají

Generátor je volitelný test `tests/LayerPeek.App.UiTests/Readme/ReadmeScreenshots.cs`. Bez proměnné prostředí
`LAYERPEEK_README_SCREENSHOTS=1` se přeskočí, takže běžné `dotnet test` nic nepřepisuje.

- Skutečná okna aplikace (okno kamery, nastavení, diagnostika) na headless platformě Avalonia, vykreslená přes Skia
  s měřítkem 2× (ostré i na Retina / HiDPI a při zvětšení na GitHubu). Okno se nikdy neukáže na obrazovce.
- Stejné harnessy a fake objekty jako UI testy (`CameraHarness`, `SettingsWindowHarness`, `DiagnosticsWindowHarness`),
  produkční registrace widgetů a skutečné view modely.
- Obraz kamery: snímek ze skutečného záznamu kamery Buddy3D (`tests/LayerPeek.Camera.FFmpeg.Tests/Fixtures/buddy3d.h264`,
  1280 × 720), dekódovaný vlastním FFmpeg feedem aplikace z lokálního souboru (bez sítě). Okno ho dostane přes
  testovací `ICameraFeedFactory` (`StillCameraFeedFactory`), tedy stejnou cestou jako živý stream.
- Data: scénář `ReadmeScenario` – tiskárna „CORE One“ tiskne na 60 % kryt baterie Makita
  (`makita-battery-cover_0.4n_0.2mm_PLA_COREONE_2h41m.bgcode`, tedy díl, který je na záznamu kamery opravdu vidět),
  realistické teploty s historií od začátku tisku. Náhled z G-code je PNG, který generuje simulátor
  (`tools/PrusaSimulator`, `PreviewImages`, náhled jeho prvního souboru: plochý oranžový disk); vlastní asset nemá. Žádné IP adresy, sériová čísla ani osobní údaje (adresy jsou host names
  `*.local`).
- Jazyky: texty aplikace v daném jazyce a formáty čísel a času podle jeho země (`ReadmeScenario.Cultures`: `en-US`,
  `cs-CZ`, `sk-SK`, `de-DE`, `es-ES`, `fr-FR`, `it-IT`, `pl-PL`). Čas je pevný
  (16. 10. 2026 12:00 UTC, zobrazený v UTC+2), takže výsledek je opakovatelný.
- Obrázky s obrazem kamery jsou JPEG (kvalita 86; jako PNG by měla fotka megabajty), okna bez kamery PNG s nejvyšší
  kompresí. JPEG je i podzáložka Detekce, protože ve výřezu podložky ukazuje snímek kamery. Jeden jazyk má asi 2 MB, všech osm
  asi 15 MB.
- Popupy (např. nabídka velikosti widgetu v režimu rozložení) jsou v UI testech samostatná okna (`OverlayPopups = false`,
  T-121), takže nejsou ve snímku okna: `ReadmeImage.Capture(window, popupContent)` je dokreslí na jejich místo.
- Nahrávání, timelapse a detekce: `FakeCameraRecordings` (nahrávání a timelapse běží, indikátory REC a TL),
  `FakeCameraTester` vrátí snímek ze záznamu kamery pro **Načíst snímek** na podzáložce Detekce; adresa kamery je host
  name (`rtsp://prusa-coreone-camera.local/live`).

| Soubor | Obsah |
|---|---|
| `camera-window.jpg` | okno kamery v normálním režimu, rozložení widgetů jako výchozí s teplotami ve velikosti L (hlavní obrázek) |
| `camera-layout-mode.jpg` | režim rozložení s otevřenou nabídkou velikosti widgetu Teploty; widget Vychladnutí je vypnutý (během tisku by ukazoval jen prázdnou kartu) |
| `widget-sizes.jpg` | widget Teploty ve velikostech S, M a L (výřezy tří vykreslení vedle sebe) |
| `camera-mini.jpg` | malý náhled, šířka 400, s rozbaleným řádkem názvu tisku a grafu |
| `settings-widgets.png` | nastavení, záložka Widgety, vybraný widget Vychladnutí |
| `settings-printers.png` | nastavení, záložka Tiskárny se třemi tiskárnami |
| `diagnostics.png` | okno diagnostiky s grafem teplot za 2 hodiny |
| `camera-recording.jpg` | horní pruh okna kamery během nahrávání a timelapse (indikátory TL a REC, karty stavu a názvu tisku) |
| `settings-camera.png` | nastavení, podzáložka Kamera tiskárny CORE One se zapnutým automatickým nahráváním, dashcamem a timelapse |
| `settings-recording.png` | nastavení, záložka Nahrávání se zapnutou detekcí chyb tisku (štítky Experimentální) |
| `settings-detection.jpg` | nastavení, podzáložka Detekce tiskárny CORE One: výřez podložky přes snímek kamery, detektor Změna na podložce, Tmavý filament |

## Přegenerování

```bash
LAYERPEEK_README_SCREENSHOTS=1 dotnet test --project tests/LayerPeek.App.UiTests -- --filter-class "LayerPeek.App.UiTests.Readme.ReadmeScreenshots"
```

Na Windows v PowerShellu: `$env:LAYERPEEK_README_SCREENSHOTS = "1"` a potom stejný `dotnet test`.

Potřebné jsou nativní knihovny FFmpeg (na Windows x64 a macOS arm64 je dodá běžný build, viz
[prerequisites.md](prerequisites.md)). Písmo je jen přibalené Inter, ale vyhlazování se mezi Windows a macOS mírně
liší: obrázky v repozitáři jsou vygenerované na macOS. Okno nastavení se vykreslí s texty pro macOS
(např. „Open at login“); rozdíly na Windows popisuje text příručky. Po přegenerování každý obrázek zkontroluj (useknutý text,
prázdné stavy) a teprve potom commitni.

Když se změní vzhled oken, stačí obrázky přegenerovat. Když přibude nový obrázek, přidej test do
`ReadmeScreenshots`, odkaz do obou README a řádek do tabulky výše.
