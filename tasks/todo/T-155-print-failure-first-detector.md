---
id: T-155
title: Detekce chyb tisku – první detektor (typ podle spiku T-152)
status: todo
owner: core-dev
priority: P2
platform: all
depends_on: [T-152, T-153]
---

## Cíl
První skutečný detektor v `LayerPeek.Detection` nad infrastrukturou z T-153 (nápad 3, fáze 3). **Typ a přístup rozhodne uživatel po spiku T-152.** Doporučení architekta předem: důsledky selhání, které jsou z kamery Core One dobře vidět – „špagety“ / odlepený nebo posunutý díl / filament na trysce – před detekcí první vrstvy.

## Doporučení spiku T-152 (2026-11-03, čeká na potvrzení uživatele – otázky 1, 5 v T-152)
- **Detektor „Změna na podložce“** (`bed-change`): zmizení nebo posun dílu a nová hmota na podložce (špagety, odtržený díl). Klasické CV v čistém C# bez nové závislosti.
- **Algoritmus (prototyp ověřený na 214 skutečných snímcích, `docs/detection-spike.md` oddíl 4):** šedotón 320×180, přibližný běžící medián (±K úrovní na vzorek, O(1) na pixel, bez historie snímků) odstraní pohyblivou hlavu a most X. Skóre = podíl pixelů výřezu přední části podložky, kde se pozadí liší o víc než 25 úrovní od pozadí před ~5 min (kruhový seznam několika starších kopií pozadí). Pevný referenční snímek nepoužívat, podložka se pohybuje v Z.
- **Čísla z prototypu (orientační):** úspěšný tisk max 1,44 %, syntetické špagety 3,9 %, syntetický posunutý díl 19,7 %. Výchozí práh ~3 %, citlivost ~2–6 %, potvrzení 5 z 8 vzorků. Konečné hodnoty podle skutečných záběrů selhání (otázka 7 v T-152).
- **Výřez:** pevný pro Core One a výchozí polohu kamery (otázka 8 v T-152). Známé omezení: tmavý filament na černém plátu (otázka 11 v T-152).
- **Výkon:** prototyp 1,36 ms na vzorek (skalární kód, M2 Pro), 0 alokací v ustáleném stavu. Případná optimalizace přes `System.Numerics.Vector` / `Vector128` je bez závislostí.

## Rozsah
- Implementace `IPrintFailureDetector` podle rozhodnutí ze spiku (klasické CV nebo ML), registrace v App (explicitní DI).
- Nová závislost jen podle rozhodnutí ze spiku, s ověřenou licencí (GPL-3.0-only kompatibilní, včetně vah modelu a dat) a kompatibilitou s Native AOT – zapsat do poznámek; verze jen v `Directory.Packages.props`.
- Citlivost → práh skóre (mapování popsat v poznámkách).
- **Testy:** sada malých fixture snímků / klipů z Core One (jen se souhlasem uživatele, otázka 4 v T-152) – pozitivní i negativní případy; měřítka přesnosti (podíl odhalených selhání, falešné poplachy na hodinu tisku) zapsaná v poznámkách. Test na celém nahraném úspěšném tisku = žádný poplach při výchozí citlivosti.
- Výkon: CPU a paměť na win-x64 a osx-arm64 (macOS → T-127, pokud nejde ověřit na Windows).

## Akceptační kritéria
- [ ] Detektor odhalí uměle vyvolaná selhání z testovací sady a při výchozí citlivosti nehlásí poplach na úspěšných tiscích ze sady (konkrétní cíle doplní uživatel po spiku, otázka 1).
- [ ] Licence všech nových závislostí, vah a dat ověřené a zapsané; Native AOT publish bez nových varování.
- [ ] Spotřeba CPU a paměti změřená a v mezích rozhodnutí z T-153 (otázka 6).
- [ ] Poplach z detektoru projde celou cestou T-153 / T-154 (notifikace, snímek, falešný poplach) – E2E se záznamem v simulátoru, pokud simulátor umí přehrát vlastní klip (jinak rozšířit simulátor v rámci úkolu).
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. **Typ prvního detektoru** a cílová přesnost (např. odhalit X % selhání, nejvýš Y falešných poplachů za 10 h tisku) – po spiku T-152.
2. **Simulátor:** má simulátor umět přehrávat vlastní klip (nahrávku selhání) místo vestavěné fixture, aby šly E2E testy detektoru? (Rozšíření `tools/PrusaSimulator` volbou typu `--camera-clip <soubor>`.)

## Poznámky k implementaci
(vyplní agent po dokončení)
