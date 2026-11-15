---
id: T-223
title: macOS – písmo Inter jako výchozí písmo aplikace (místo San Francisco, které Avalonia rozvrhuje úzce)
status: done
owner: ui-dev
priority: P2
platform: macos
depends_on: [T-222]
---

> **Rozhodnuto uživatelem 2026-11-15 (T-222, otázky 1–3):** A + B – dodávat Inter na macOS hned (celý balíček
> `Avalonia.Fonts.Inter`, jen macOS, Windows zůstává Segoe UI) a chybu nahlásit Avalonii (text issue se nejdřív ukáže
> uživateli). K San Francisco se vrátit v budoucím úkolu, až Avalonia chybu opraví.

## Cíl
Avalonia 12.1 rozvrhuje proměnné systémové písmo macOS (San Francisco) s posuny výchozí instance, ale kreslí
požadovanou váhu: text je o 7–20 % užší než v nativních aplikacích a polotučná / tučná písmena se dotýkají (T-222).
Okna LayerPeeku na macOS proto dostanou přibalené písmo Inter, se kterým už pracují všechny snímky a testy rozložení
UI (T-031).

## Rozsah
- Balíček `Avalonia.Fonts.Inter` (stejná verze jako Avalonia) přes centrální správu balíčků v aplikaci; ověřit licenci
  (balíček MIT, písmo SIL OFL 1.1) a kompatibilitu s Native AOT / trimováním.
- Inter jako výchozí písmo oken jen na macOS (`WithInterFont()` + `FontManagerOptions.DefaultFamilyName`), bez
  reflexe; Windows beze změny. UI testy používají stejné nastavení jako aplikace.
- Licence: Inter v seznamu licencí třetích stran (`Licenses/ThirdPartyLicenses.cs`, okno O aplikaci → Licence třetích
  stran) se zněním OFL; README a příručky, pokud licence vypisují.
- `tests/LayerPeek.App.UiTests.SystemFont` (T-214) nechat (pro návrat k SF), ale jasně popsat, že netestuje písmo
  aplikace; otázku ponechat / odstranit zapsat do Otevřených otázek.
- Snímky macOS přegenerovat, pokud se změní, a zkontrolovat okem; T-127 bod 1k upravit; `docs/architecture.md`,
  `CHANGELOG.md`; změřit rozdíl velikosti Release publikace `osx-arm64`.

## Akceptační kritéria
- [x] Na macOS je výchozí písmo oken Inter, na Windows beze změny (Segoe UI)
- [x] Balíček přes `Directory.Packages.props`; licence a AOT kompatibilita ověřené a zapsané v poznámkách
- [x] Inter (OFL 1.1) v seznamu licencí třetích stran ve všech jazycích; README / příručky zkontrolované
- [x] UI testy používají stejné nastavení písma jako aplikace; projekt SystemFont popsaný, otázka v Otevřených otázkách
- [x] Snímky macOS zkontrolované (přegenerované, pokud se změnily); T-127 bod 1k upravený
- [x] `docs/architecture.md` a `CHANGELOG.md` doplněné; rozdíl velikosti publikace změřený
- [x] `dotnet build` a `dotnet test` bez chyb a varování, `dotnet publish -c Release -r osx-arm64` bez varování AOT

## Otevřené otázky
1. **Projekt `tests/LayerPeek.App.UiTests.SystemFont` (T-214) ponechat, nebo odstranit?** (neblokuje, úkol je hotový)
   Od T-223 netestuje písmo aplikace (ta má na macOS Inter); testy dál procházejí se SF a v komentářích projektu
   a `SystemFontCalibrationTests` je to popsané. Ponechat = připravené ověření pro návrat k SF (po opravě v Avalonii
   stačí přepnout `AppFont` a projekt hned ukáže, zda se vše vejde), cena ~10 s běhu testů na Macu a údržba
   propojených souborů. Odstranit = méně kódu, při návratu k SF obnovit z historie gitu.
   **Doporučení: ponechat** do rozhodnutí o návratu k SF.
   Rozhodnuto uživatelem 2026-11-15: otázka 1 – projekt LayerPeek.App.UiTests.SystemFont **ponechat**.

## Poznámky k implementaci
- **Balíček:** `Avalonia.Fonts.Inter` 12.1.4 (stejná verze jako Avalonia) přesunutý v `Directory.Packages.props`
  ze skupiny `Tests` do `App`, `PackageReference` v `LayerPeek.App.csproj`. Licence: balíček **MIT** (nuspec
  `<license type="expression">MIT</license>`), písmo **SIL OFL 1.1** – Inter 3.019 (tabulka name všech šesti řezů:
  „Version 3.019;git-0a5106e0b“, „Copyright 2020 The Inter Project Authors“); obojí GPL-3.0 kompatibilní (OFL písmo se
  smí distribuovat s jakýmkoli softwarem, písmo není kód linkovaný do programu). **AOT/trim:** balíček obsahuje jen
  vložené prostředky (`avares`) a `WithInterFont()` = `EmbeddedFontCollection` bez reflexe;
  `dotnet publish -c Release -r osx-arm64` bez varování IL/AOT (jen dřívější varování `dsymutil` o modulové cache,
  stejná i bez Inter).
- **Kód:** `src/LayerPeek.App/Platform/AppFont.cs` – `UseAppFont()` (v `Program` u aplikace i vstupu pro designer):
  na macOS `UseInterFont()` = `WithInterFont()` + `FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" }`,
  jinde builder beze změny (Windows: Segoe UI). Znaky mimo Inter (např. CJK v názvu tiskárny) jdou na fallback systému.
  Nativní menu v menu baru, notifikace a systémové dialogy zůstávají v systémovém písmu.
- **UI testy:** `HeadlessTestApp` volá tutéž `AppFont.UseInterFont(fallbacks: [])` a `FontFamilyName =
  AppFont.InterFamilyName` – testy a aplikace mají vždy stejnou kolekci, rodinu a verzi písma (jen bez fallbacku, který
  snímky zakazují). Snímky se proto nezměnily (`dotnet test` prošel bez přegenerování, macOS i Windows sady beze změny);
  kontrola okem: `Snapshots/macOS/settings-camera-tab.png` – Inter jako dosud.
- **Licence:** balíček v komponentě Avalonia (MIT), písmo jako nová komponenta „Inter“ 3.019, `OFL-1.1`,
  `https://github.com/rsms/inter`, text `Licenses/Texts/ofl-inter.txt` = `LICENSE.txt` repozitáře Inter na tagu
  `v3.19` (copyright + celé znění OFL 1.1). Popis „Inter (font, macOS only)“ (`LicensesComponentInter`) ve všech 8
  jazycích. Testy: počet popsaných komponent 4, OFL text obsahuje znění licence a autory. README / README.cz.md
  a příručky komponenty jednotlivě nevypisují (odkazují na okno O aplikaci → Licence třetích stran) → beze změny.
- **SystemFont (T-214):** projekt ponechán a dál prochází; v `.csproj` a `SystemFontCalibrationTests` popsáno, že od
  T-223 netestuje písmo aplikace (otázka výše).
- **T-127 bod 1k:** přepsán na Inter – očekávané šířky záložek `SettingsTabStartupUpdates` / `SettingsTabRecording`
  **197 / 109 pt** (se SF byly 177 / 101 pt), vypisuje je nový informativní test
  `SettingsWindowTabWidthsUiTests.MainTabWidthsForTheCheckInTheRunningApp`. V oddílu Automatizace poznámka, že části
  se systémovým písmem pokrývají od T-223 běžné UI testy s Inter.
- **Velikost (Release `osx-arm64`, Native AOT, 2026-11-15):** spustitelný soubor `LayerPeek` 36 015 208 → 37 914 104 B
  (**+1 898 896 B ≈ +1,9 MB**), komprimovaně (`gzip -9`) 14 786 789 → 15 726 234 B (**+0,94 MB**, odhad přírůstku
  `.dmg` / `.tar.gz`). Pokus se stejným kódem, ale větví pro „jiný OS“ (`OperatingSystem.IsWindows()` na macOS): jen
  +16 512 B – ILC s konstantní podmínkou OS písmo do binárky nedá, na Windows se proto čeká jen zanedbatelný přírůstek
  (neověřeno na Windows, viz níže).
- **Ověřeno (macOS 27.2.1, Apple Silicon):** `dotnet build` 0 varování, `dotnet test` zelený, AOT publikace bez
  varování AOT; `dotnet run --project src/LayerPeek.App` (testovací datová složka) nastartoval a běžel bez chyb v logu,
  dokud nebyl ukončen.
- **Neověřeno:** okna běžící aplikace s Inter na obrazovce – terminál nemá oprávnění Zpřístupnění, okno Nastavení se
  nedalo otevřít z menu baru; potvrzení šířek je bod 1k v T-127. Windows (beze změny chování, velikost AOT binárky
  na Windows nezměřena) – ověří se běžným během testů / CI na Windows.
