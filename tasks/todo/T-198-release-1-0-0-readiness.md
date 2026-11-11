---
id: T-198
title: Připravenost vydání 1.0.0 (blokery, první podepsané vydání, zveřejnění repozitáře, plán dne vydání)
status: todo
owner: architect
priority: P1
platform: all
depends_on: []
---

## Cíl
Rozhodnutí uživatele 2026-11-11: příští vydání bude **1.0.0** (.NET 11 je GA, T-193) a s ním se repozitář
`pfoltynek/LayerPeek` zveřejní. Tento úkol je **analýza a příprava**: kontrolní seznam, nálezy a návrh postupu. Sám nic
nevydává.

**Nikdy v rámci tohoto úkolu:** větev `release/1.0.0`, změna `<Version>` na `develop`, sloučení do `main`, tag, push,
nahrání, zveřejnění vydání, zveřejnění repozitáře, odeslání do winget ani jiný zásah do externí služby. To udělá hlavní
agent a uživatel později s výslovným souhlasem podle oddílu 5.

Závislosti nejsou ve `depends_on` záměrně: blokery jsou body jiných úkolů (oddíl 1) a odškrtávají se tam. Tento úkol
se uzavře, až budou odškrtnuté blokery a kritéria níže (vydání samotné kritériem není).

## Shrnutí
- **Blokery: 9** (oddíl 1.1), mimo jiné na Windows: SDK GA + build vydání (T-193), snímky UI sady Windows (T-185), E2E ověření
  podpisu feedu (T-181). Na obou OS: generálka podepsaného buildu s produkčním klíčem bez GitHubu. Texty pro 1.0
  (CHANGELOG, README, SECURITY, příručky v 8 jazycích). Rozhodnutí uživatele o e-mailech v historii gitu před
  zveřejněním.
- **Sběrné úkoly:** neodškrtnutých bodů je **114** (T-124: 0, T-125: 42, T-126: 7, T-127: 65). Pro 1.0.0 je z nich
  blokujících 9 (automatické části, patří k B1–B3 a B5), doporučených 21 a 84 může počkat (oddíl 1.2).
- **Podpis feedu (T-181):** cesta 0.5.0 → 1.0.0 funguje bez ověření (0.5.0 ověřovač nemá). Skutečně ověřená
  aktualizace z GitHubu proběhne poprvé až 1.0.0 → 1.0.1. Zkušební vydání v testovacím repozitáři
  **nemusí** být před 1.0.0 a přijetí aplikací stejně ověřit neumí (oddíl 2).
- **Sken historie:** žádné tokeny, hesla ani soukromé klíče kromě zdokumentovaného testovacího klíče T-181. Žádné
  absolutní cesty s uživatelským jménem. Nález: **394 commitů má e-mail autora `…@MacMini2.home-lab.cz`** (jméno
  počítače + vnitřní doména) a 537 osobní adresu; obojí se zveřejněním repozitáře stane veřejným (oddíl 3.1).
- **Nastavení 0.5.0 se v `develop` načte beze změny:** soubory zapsané kódem 0.5.0 `develop` načte bez varování
  a znovu je uloží bajtově stejně (oddíl 4.3).

## 1. Blokery a doporučené body

Legenda: **W** = potřebuje Windows, **M** = potřebuje Mac, **U** = potřebuje uživatele (fyzicky, rozhodnutí, heslo,
účet GitHub), **A** = zvládne agent sám.

### 1.1 Blokery (před tagem `v1.0.0`)

| # | Bod | Kde | Kdo | Proč blokuje |
|---|---|---|---|---|
| B1 | **T-193** – SDK `11.0.100` na Windows, `dotnet build/test`, `aot-smoke.ps1`, `release-build.ps1` | T-125 2r | W, U | `global.json` zakazuje RC SDK; bez GA SDK se Windows soubory vydání nesestaví |
| B2 | **T-185** – snímky UI sady Windows (6. záložka „Kamera“, kratší záhlaví) a celý `dotnet test` na Windows (zahrnuje i automatické body 2n, 2p T-163, 2u T-196, 2w T-185, 2x T-195) | T-125 2w (+2n, 2p, 2u, 2x) | W, A | Snímky Windows (`tests/LayerPeek.App.UiTests/Snapshots/settings-*.png`) naposledy změnil T-170 (5edc4e47), od T-185 jsou zastaralé. `release-build.ps1` spouští celý `dotnet test`, takže build Windows z tagu selže. Tag je neměnný (ruleset „Tags v*“ bez bypassu), náhradní postup se `skip-worktree` v `docs/release.md` je jen nouzový |
| B3 | **T-181** – `pwsh tools/run-e2e.ps1 -Class '*UpdateTests*'` na Windows (podepsaný feed přijat, nepodepsaný odmítnut) | T-125 2v (1. bod) | W, A | 1.0.0 je první verze s ověřením. Chyba v napojení na Velopack na Windows by všem instalacím 1.0.0 vzala aktualizace a pomohla by jen ruční přeinstalace |
| B4 | **Generálka podepsaného buildu bez GitHubu** na Macu i na Windows (nový bod tohoto úkolu, kritérium níže) | T-198 | M, W, U | Celý `release-build.ps1` s produkčním klíčem (médium, heslo 2×, podpis, zpětné ověření `.sig`) ještě nikdy neproběhl; Pester ho bez `dotnet` nespouští. `release.ps1` pushne neměnný tag **před** buildem (kroky 3 → 7). Chyba až po pushi tagu = verze 1.0.0 propadne a vydá se 1.0.1 |
| B5 | **T-170** – snímky macOS a celý `dotnet test` na Macu | T-127 1c (1. bod) | M, A | Nejspíš už splněno (a3ccb79b 2026-11-05, sada macOS znovu přegenerovaná v T-185 9e48dd59 2026-11-11), bod ale není odškrtnutý. Ověřit zeleným `dotnet test` na Macu a odškrtnout |
| B6 | **CHANGELOG pro 1.0.0** (oddíl 3.3) | T-198 | A, U | Věta „Until 1.0.0, minor versions may contain breaking changes…“ po 1.0.0 neplatí. V `[Unreleased]` chybí uživatelsky viditelné změny T-188 / T-194 (čas do dalšího pokusu kamery, „Otestovat kameru“ zkrátí pauzu) a T-193 (runtime .NET 11 GA s opravami CVE). Text vydání se bere jen z této sekce |
| B7 | **Texty „0.x = předběžné“** v README (EN/CZ), `SECURITY.md` a příručkách všech 8 jazyků (oddíl 4.4) | T-198 | A, U | Po vydání 1.0.0 by první veřejná stránka i příručka tvrdily, že jde o předběžnou verzi a že se nastavení může měnit. Znění rozhodne uživatel (otázka 5) |
| B8 | **Rozhodnutí o identitě autorů v historii** (oddíl 3.1, otázka 1) | T-198 | U | Zveřejnění je nevratné (klony, archivy, cache GitHubu). Přepis historie po zveřejnění už nepomůže. Rozhodnout je nutné předem |
| B9 | **Kontroly na GitHubu před zveřejněním** (oddíl 3.2) | T-198, T-180 | U | Agent nevidí obsah na GitHubu (vydání 0.x, issues, vzdálené větve, nastavení). Zveřejnění ho odhalí celý |

### 1.2 Sběrné úkoly T-124–T-127: co je důležité pro 1.0.0

Počty neodškrtnutých bodů (2026-11-11): **T-124 0** (hotovo), **T-125 42**, **T-126 7**, **T-127 65**, celkem **114**.

**Blokující (9, už v 1.1):** T-125 2r (B1); 2w oba body, 2x bod 1, 2u, 2p a 2n – jen automatický `dotnet test`
a snímky, ruční části mohou počkat (B2); 2v bod 1 (B3); T-127 1c bod 1 (B5).

**Doporučené před zveřejněním nebo hned po něm (21):**

| Bod | Kde | Kdo | Důvod |
|---|---|---|---|
| T-181 – toast „Je dostupná nová verze“ zmizí při odmítnutí feedu, v menu „Aktualizace odmítnuta…“ | T-125 2v bod 2 | W | Chování při odmítnutí na Windows nikdo neviděl. Bez něj ale aktualizace fungují |
| T-184 – kvalita obrazu kamery při 100/125/150 % DPI | T-125 2s bod 1 | W | Mění převod obrazu všem uživatelům. Viditelná regrese v první veřejné verzi by byla vidět hned |
| T-184 – kvalita obrazu Retina / 1× | T-127 1e | M | Totéž na Macu |
| T-144 – nahrávka se přehraje ve Filmech a TV / VLC | T-125 2d bod 2 | W | Nahrávání **není** experimentální a vyšlo v 0.5.0 bez ručního ověření na Windows |
| T-146 – E2E `*CameraRecording*` | T-125 2e bod 1 | W, A | Dtto (automatické nahrávání bez okna) |
| T-146 – ruční nahrávání, „Změnit…“, toast chyby | T-125 2e bod 4 | W | Dtto |
| T-148 – dashcam, ruční uložení a spouštěče | T-125 2f bod 3 | W | Dashcam není experimentální |
| T-146 – nahrávání v menu baru a okně kamery | T-127 1 | M | Na skutečném displeji ho dosud nikdo neviděl (agent neměl displej) |
| T-148 – dashcam na Macu | T-127 1 | M | Dtto |
| T-145 – automatické nahrávání se skutečnou tiskárnou | T-127 2 | M, U | Hlavní scénář nahrávání, ověřený jen se simulátorem |
| T-147 – přehrání v QuickTime | T-127 2 | M | Nahrávky musí jít přehrát systémovým přehrávačem |
| T-026 – `.app` z `.dmg` na Macu / účtu bez Homebrew a .NET SDK | T-127 2 | M, U | Cizí uživatelé nemají vývojové prostředí; chybějící knihovna by vypnula kameru |
| T-019 – Gatekeeper `.dmg` / `.tar.gz`, texty dialogů proti README | T-126 1 | M, U | Návod v README je první kontakt nového uživatele s aplikací |
| T-020 – oznámení nainstalované `/Applications/LayerPeek.app` | T-126 2 bod 1 | M, U | Hlavní funkce aplikace v produkčním bundlu |
| T-058 – Gatekeeper `.dmg` staženého z GitHubu, App Translocation | T-127 3 | M, U | Jde ověřit až s veřejným repozitářem (anonymní stažení), hned po zveřejnění |
| T-058 – aktualizace `/Applications/LayerPeek.app` z GitHubu bez tokenu (správce i standardní účet) | T-127 3 | M, U | Ověří se přirozeně aktualizací 0.5.0 → 1.0.0 hned po zveřejnění |
| T-058 + T-021 + T-020 – Klíčenka, oznámení a Místní síť po aktualizaci | T-127 3 | M, U | Ad-hoc podpis mění cdhash každého buildu. Pokud se Klíčenka ptá po každé aktualizaci, patří to do README. Zapsat při 0.5.0 → 1.0.0 |
| T-017 – varování SmartScreen / Edge při stažení `Setup.exe` z GitHubu | T-125 4 | W, U | Jde ověřit až po zveřejnění. Text patří do README a `docs/release.md` |
| T-055 – `release-upload.ps1 -DryRun` / `release.ps1 -DryRun` nic nezmění | T-127 3 bod 1 | M, U | Splní se v den vydání (oddíl 5, krok B3), stačí zapsat |
| T-055 – příznak vydání | T-127 3 bod 4 | U | Kritérium se týká `0.x` (předběžné). U 1.0.0 naopak ověřit `isPrerelease = false` (oddíl 5, C1). Bod upravit nebo uzavřít |
| T-181 – zkušební vydání v testovacím repozitáři | T-127 3 | M, U | Ne před 1.0.0 nutně. Nahrazuje ho B4 + `-DryRun` (oddíl 2.3, otázka 3) |

**Může počkat** (zbylých 84 bodů): T-125 – T-105 interaktivní `windows-verify`, T-101 skutečné vypnutí upozornění,
nepovinné T-117 / T-118 / T-124, T-151 (probuzení, celá obrazovka), T-143 ruční, T-145 OneDrive, T-162 prázdná cache,
T-154 (obrázek toastu, editor výřezu), T-158, T-155 a T-167 (měření CPU), ruční části experimentálních funkcí (T-161,
T-165, T-164, T-159, T-163, T-167, štítek 2n), měření T-184 / T-185 / T-195, T-188 ruční (pokryto testy), T-107
(web přes proxy se skutečnou tiskárnou, funkce od 0.3), T-055 doplnění druhého OS přes `-AddToPublished` (T-180 tento
tok ruší) a závěrečný bod. T-126 – kliknutí na oznámení, když aplikace neběží, odepřené oznámení, „Otevřít při
přihlášení“, odinstalace, závěr. T-127 – oddíly 1 a 1b kromě bodů výše (Dock a ⌘Tab, monitory T-038, klávesnice T-100,
ikony T-018 / T-098 / T-116, VoiceOver T-122, T-117 / T-118, T-096 / T-099, T-151, návrhy T-154 / T-161, experimentální
funkce, T-160 měření), 1c bod 2 (`mac.keys` po T-170), 1d (prázdný text „Zkontrolovat nyní“ na Macu – kosmetické, aktualizace funguje), 1f T-188,
v oddíle 2 T-097, T-143, T-159, T-155, T-167, T-163, T-106, T-107 a v oddíle 3 orchestrátor ve zkušebním vydání,
doplnění druhého OS, kliknutí na položku a banner nové verze, `docs/velopack.md` bod 10 a závěr. Body experimentálních
funkcí (detekce, timelapse) mají v UI i v CHANGELOG štítek „Experimentální“, takže neověřené okrajové případy vydání
1.0.0 neblokují.

### 1.3 Otevřené úkoly

| Úkol | Hodnocení | Kdo | Důvod |
|---|---|---|---|
| T-180 GitHub – účet (2FA klíčem, záchranné kódy, úklid tokenů a aplikací), stav rulesetů | **doporučeno před zveřejněním** | U | Od zveřejnění se z repozitáře aktualizují cizí instalace. T-181 chrání aktualizace, ale ne první stažení (`Setup.exe`, `.dmg`) |
| T-180 – neměnná vydání (immutable releases) a zrušení `-AddToPublished` (část `qa-reviewer`) | může počkat, **ale ovlivní den vydání** | U, A | Když se neměnná vydání zapnou před 1.0.0, soubory po zveřejnění už nejde doplnit. 1.0.0 se pak musí zveřejnit až s oběma OS v konceptu, bez `-AllowIncomplete` (oddíl 5, otázka 4) |
| T-180 – krátkodobý fine-grained PAT v `GH_TOKEN` | doporučeno | U | Kritérium „`-DryRun` projde s tokenem jen v `GH_TOKEN`“ zatím nikdo neověřil. Ověřit v den vydání, jinak záložně `gh auth login` |
| T-183 dokumentace zabezpečení přístupu k PrusaLink | doporučeno | A | Jen dokumentace, hodí se k veřejnému repozitáři. Připravuje se souběžně |
| T-182 varování u `http://` mimo místní síť | může počkat | A | Nová funkce s texty v 8 jazycích. Kdyby šla do 1.0.0, přidá body do T-125 / T-127 |
| T-059 winget | po vydání | W, U | Potřebuje veřejné repozitář i vydání (validace stahuje instalátor). Připravuje se souběžně, odeslání je krok C5 |
| T-197 jen podepsat hotový `dist/` | může počkat | A | Zkrátí opakování po selhání podpisu; riziko dne vydání snižuje i B4 |
| T-174 další modely tiskáren | může počkat | komunita | Šablona issue už existuje |
| T-054 heslo přes UI Automation | může počkat | W | Měření; útočník by musel běžet pod stejným uživatelem |
| T-169 nestabilní E2E malého náhledu | může počkat | W | E2E se v `dotnet test` přeskakují (`LAYERPEEK_E2E`), build vydání neblokuje |
| T-186 / T-187 pozastavení kamery při zamčení | může počkat | W / M | Mění chování; buď po 1.0.0, nebo s plným ověřením |
| T-192 spike HW dekódování | může počkat | A | Průzkum |
| T-157, T-060, T-075 | odloženo | – | Rozhodnutí uživatele |

## 2. První podepsané vydání (T-181)

### 2.1 Cesta aktualizace
- **0.5.0 → 1.0.0:** 0.5.0 ověřovač nemá a feed přijme bez podpisu (`.sig` ignoruje). 1.0.0 je nepředběžné (`major ≠ 0`,
  `Test-PrereleaseVersion`), takže ho najde Velopack v 0.5.0 i s vypnutými předběžnými verzemi. Instalace mimo
  Velopack (`.zip`, `.app` mimo Aplikace) dostanou jen odkaz na stránku vydání (`GitHubReleaseChecker` vyžaduje
  nahraný `SHA256SUMS-<os>.txt` svého OS). **Dnes je repozitář soukromý, takže 0.5.0 vidí 404 = „žádná aktualizace“.**
  Nabídku 1.0.0 tedy instalace 0.5.0 dostanou až po zveřejnění repozitáře i vydání.
- **Předběžné verze:** kdo volbu „Nabízet předběžné verze“ nikdy neměnil, má ji v 0.x zapnutou a od 1.0.0 vypnutou
  (`PrereleaseUpdates.IsDefaultFor`; nastavení ukládá volbu jen při změně, `SettingsViewModel.PrereleaseChoice`). To je
  rozhodnutí z 2026-10-24, žádná migrace není potřeba.
- **Od 1.0.0 dál:** 1.0.0 přijme jen feed s `.sig` od klíče z `UpdateFeedKeys.Production` (otisk
  `4cb047d1…7b91e26`). Každé další vydání se musí podepsat tímto klíčem. Ztráta klíče (obou médií nebo hesla) =
  ruční přeinstalace všech instalací. Rotace podle `docs/release.md`, „Rotace klíče“.
- **První skutečně ověřená aktualizace z GitHubu je až 1.0.0 → 1.0.1.** Ověřovací kód je stejný pro testovací
  i produkční klíč, liší se jen seznam klíčů. Pokrytí dnes: C# test parsuje produkční klíč
  (`SignedUpdateSourceTests`: `TrustedKeyCount` = počet klíčů). `release-build.ps1` čte seznam klíčů přímo
  z `UpdateFeedKeys.cs` a každý zapsaný `.sig` hned ověří stejně jako aplikace. Sdílený testovací vektor kříží
  PowerShell a C#. E2E `UpdateTests` s testovacím klíčem prošel na Macu, na Windows chybí (B3). Zbytkové riziko je
  stažení `.sig` ze skutečného GitHubu (adresy, přesměrování). Lokálně ho ověřit nejde, viz otázka 3.

### 2.2 Musí zkušební vydání (T-127 oddíl 3) předcházet 1.0.0?
**Ne nutně.** Release build má adresu repozitáře napevno (`AppUpdates.RepositoryUrl`). Testovací zdroj
(`LAYERPEEK_TEST_UPDATE_SOURCE`) existuje jen v Debug a důvěřuje jen testovacímu klíči. V testovacím repozitáři proto jde
ověřit **jen skripty** (podepsaný upload, pořadí `.sig` → feed, odmítnutí `-SkipFeedSignature`, odmítnutí zveřejnění
bez `.sig`). Bod T-127 „nainstalovaná verze s ověřením podepsanou aktualizaci přijme“ ve zkušebním repozitáři
splnit nejde. Text bodu v T-127 a věta v `docs/release.md` („Ověření, že nainstalovaná verze … patří do T-125 / T-127“)
to neříkají (otázka 3). Skriptovou část pokrývají Pester testy proti falešnému `gh` a skutečný běh B4 bez GitHubu. Na
GitHubu ji pak ověří `-DryRun` a samotný upload 1.0.0, kde `release-publish.ps1` feed bez `.sig` odmítne.

### 2.3 Co musí být hotové před dnem vydání (klíč a podpis)
- [x] Produkční klíč vytvořený, záloha ověřená, veřejný klíč v `UpdateFeedKeys.Production` (2026-11-11, T-181).
- [ ] Generálka B4 na **obou** počítačích: médium s klíčem se musí dostat i k počítači s Windows. Na Windows ověřit
      písmeno jednotky (`-FeedSigningKey E:\layerpeek-feed-key.pem`) a to, že čtečka/USB funguje.
- [ ] Heslo klíče je dostupné ve správci hesel, ne u média. Při vydání se zadává **4×** (2× na každém OS).
- [ ] Rozhodnout tok obou OS (otázka 4). S neměnnými vydáními nejde `-AllowIncomplete` + `-AddToPublished`.
- [ ] Na Macu běží build asi 20 min a Mac usíná po 10 min nečinnosti: build spouštět pod `caffeinate -i` (v `tmux`
      při práci přes SSH). V `docs/release.md` to chybí (nález 3.5).

## 3. Zveřejnění repozitáře

### 3.1 Sken celé historie gitu (2026-11-11, lokálně, bez instalace nástrojů)
Rozsah: `git log --all -p` = **931 commitů** ze všech lokálních refů (`develop`, `main`, tagy `v0.1.0`–`v0.5.0`,
lokální větve `feature/*` souběžných worktree), skript v Pythonu (vzory níže) a cílené `git grep` / `git log -G`.
Vzdálené větve jsou známé jen podle místních sledovacích refů (`origin/develop`, `origin/main`). Co je na GitHubu navíc,
ověří uživatel (3.2). Hodnoty tajných údajů se nikam nevypisují.

| Kontrola | Výsledek |
|---|---|
| Soukromé klíče (`BEGIN … PRIVATE KEY`) | jen **testovací klíč T-181** `tests/Shared/Fixtures/UpdateFeed/test-feed-key.private.pem` a `…private.encrypted.pem` (commit a3ce2844). **Záměrný a zdokumentovaný:** `tests/Shared/Fixtures/UpdateFeed/README.md` („The test key is not a secret“, heslo `layerpeek-test-only`), komentář `UpdateFeedKeys.Test` (jen Debug), Pester i C# test hlídají, že v `Production` není. Ostatní výskyty (`ReleaseTools.psm1`, `ReleaseTools.Tests.ps1`, d3891b72) jsou jen kontrola hlavičky PEM, žádná data klíče. Jiný blok base64 ve tvaru PEM v historii mimo obrázky není (jen tento vektor a veřejné klíče v `UpdateFeedKeys.cs`) |
| Tokeny GitHub (`ghp_`, `gho_`, `github_pat_`…), AWS, Slack, Google, Anthropic/OpenAI, JWT | **0 nálezů** |
| Hesla / API klíče přiřazené v kódu | 22 souborů, **vše testovací hodnoty** v `tests/**` a `tools/PrusaSimulator` (např. heslo simulátoru), žádné skutečné heslo PrusaLink. Skutečné heslo v textu `tasks/`/`docs/` nenalezeno |
| Přihlašovací údaje v URL | 41 souborů, jen zástupné (`user:pw@`, `admin:secret@`, `jmeno:heslo@`) v testech a dokumentaci |
| Citlivé typy souborů v historii (`.env`, `.pfx`, `.p12`, `.key`, `.pem` mimo vektor, `settings.json`, logy, `.DS_Store`) | žádné |
| Absolutní cesty s uživatelským jménem | žádné (`/Users/me`, `C:\Users\test` apod. jsou zástupné; `/Volumes/LP-KEY` je příklad média) |
| E-maily v obsahu | jen veřejné adresy v licenčních textech třetích stran a `@example.invalid` v testech |
| Sériová čísla tiskáren | fixtures anonymizované (`SN-ANONYMIZED`), v historii žádné skutečné |
| **E-maily autorů commitů (metadata)** | **394 commitů `Petr Foltýnek <pfoltynek@MacMini2.home-lab.cz>`** (git si adresu odvodil ze jména počítače, protože na Macu chybí `user.email`; prozrazuje jméno počítače a vnitřní doménu sítě; posledně 2026-11-11, tedy i nové commity) a **537 `<pfoltynek@outlook.cz>`** (osobní adresa). Spoluautor `Claude Opus 5.5 <noreply@anthropic.com>` v 605 commitech |
| Soukromé IP adresy | 230 souborů, hlavně příklady `192.168.0.226`–`.230` a `192.168.1.x` v testech, zástupném textu adresy kamery v `Strings.*.resx` a úkolech. `192.168.0.227` je podle T-127 (relay `ncrelay.sh`) nejspíš skutečná kamera uživatele. Adresa RFC 1918 z internetu dostupná není, nízké riziko |
| Osobní / interní obsah | `tests/LayerPeek.Detection.Tests/Fixtures/coreone/` = 10 skutečných snímků z kamery uživatele (jen vnitřek tiskárny, rozhodnutí 2026-11-03/04). `tasks/**`, `docs/**`, `CLAUDE.md`, `.claude/agents/*.md` obsahují interní rozhodnutí a pracovní postup s agenty, odkazy na `~/LayerPeek-samples` a domácí síť. Nic tajného, ale zveřejní se (otázka 2) |

Možnosti k e-mailům autorů (rozhodne uživatel, otázka 1):
- **(a) Ponechat** a jen od teď nastavit `git config --global user.email <id>+pfoltynek@users.noreply.github.com` na
  obou počítačích (a „Block command line pushes that expose my email“ na GitHubu). Starých 931 commitů zůstane.
- **(b) Přepsat historii** (`git filter-repo --mailmap`) před zveřejněním. Změní SHA **všech** commitů i tagů
  `v0.1.0`–`v0.5.0`, vyžaduje force-push `main`/`develop` a přesun tagů (rulesety to bez bypassu zakazují, musely by se
  dočasně vypnout). Vydání 0.x by ukazovala na tagy jiných commitů a všechny klony a worktree by se musely znovu
  vytvořit. Navíc GitHub ponechá staré objekty dostupné podle SHA (z cache, PR refů) a úplné odstranění vyžaduje
  podporu GitHubu.
- **(c) `.mailmap`** jen sjednotí zobrazení v `git log`, surová adresa v commitu zůstane veřejná. Neskrývá nic.

### 3.2 Co zkontrolovat na GitHubu (uživatel, agent to nevidí)
- [ ] Vzdálené větve a tagy: `git ls-remote origin` (smazat zapomenuté `feature/*`, které nemají být veřejné).
- [ ] Obsah, který se zveřejní s repozitářem: text a soubory vydání `v0.1.0`–`v0.5.0` (koncepty smazat nebo zkontrolovat),
      issues, PR, Discussions, popisy commitů v PR. **`v0.2.0` je jako jediné 0.x vydání označené jako „Latest“
      (rozhodnuto 2026-10-30 nechat), dokud nevyjde 1.0.0.**
- [ ] Ponechat vydání 0.x veřejně? (otázka 6)
- [ ] Nastavení repozitáře: popis, web, témata (topics), náhled pro sociální sítě, Discussions zapnuté (odkazuje na ně
      `CONTRIBUTING.md` a `.github/ISSUE_TEMPLATE/config.yml`), private vulnerability reporting (odkaz v `SECURITY.md`),
      wiki vypnutá, žádné webhooky, deploy klíče ani spolupracovníci navíc.
- [ ] Rulesety 1–4 z T-180 ve stavu *Active* i po zveřejnění. Secret scanning, push protection a CodeQL jsou u veřejného
      repozitáře zdarma, po přepnutí zkontrolovat, že zůstaly zapnuté. Upozornění secret scanningu (Security → Secret
      scanning) projít ještě před zveřejněním: GitHub už historii skenoval.
- [ ] Účet podle T-180 (2FA hardwarovým klíčem, záchranné kódy, úklid PAT / OAuth / SSH klíčů).

### 3.3 Texty v repozitáři
- `CHANGELOG.md` (B6): nahradit větu „Until 1.0.0, minor versions may contain breaking changes (settings format,
  behaviour).“ příslibem pro 1.x (otázka 5); doplnit T-188, T-194 a T-193 do `[Unreleased]`. Odkazy dole
  (`[Unreleased]` → `v1.0.0...HEAD`, nový `[1.0.0]: …/compare/v0.5.0...v1.0.0`) a datum doplní `release.ps1` sám.
  **Pozor:** `release.ps1` přesune do `[1.0.0]` jen podsekce s položkami, úvodní odstavec bez položek pod `[Unreleased]`
  zůstane (`docs/release.md`, „CHANGELOG.md“). Pokud má mít text vydání 1.0.0 úvod (např. „první veřejné vydání“,
  otázka 7), musí se předem připravit jako sekce `## [1.0.0] - unreleased` (`[Unreleased]` pak prázdná).
- `README.md` / `README.cz.md`: upozornění „Versions 0.x are **previews**. Settings may change between versions.“
  (ř. 359 / 355) a Roadmap „`winget install` as soon as the first release is public“ (ř. 447). Odznak
  `include_prereleases` může zůstat.
- `SECURITY.md`: „Versions 0.x are previews.“ → znění pro 1.x (podporovaná je jen poslední verze).
- Šablony issue: zástupná verze `0.1.0` v `bug_report.yml` a `0.5.0` v `printer_compatibility.yml` (kosmetické).
- `docs/release.md` ř. 535 a `docs/velopack.md`: „Dokud je repozitář soukromý, kontrola skončí chybou 404“ – po
  zveřejnění upravit (může počkat).

### 3.4 Licence
- `LICENSE` = úplný text GPL-3.0 (674 řádků, SHA-256 `3972dc97…`, shodný s textem z gnu.org). `Directory.Build.props`
  má `PackageLicenseExpression` `GPL-3.0-only`, README licenci a záruku popisuje. Hlavičky licence v jednotlivých souborech
  projekt nepoužívá. GPL je nevyžaduje (jen doporučuje), může počkat (otázka 8).
- Ilustrace tiskáren jsou vlastní (T-039, žádné fotografie Prusa). Upozornění na ochranné známky je v README EN i CZ.
- **FFmpeg (LGPL) při veřejné distribuci:** aplikace odkazuje na přesný zdroj (BtbN zip pro Windows, tarball ffmpeg.org
  pro macOS, `ThirdPartyLicenses.FFmpeg`) a obsahuje `FFmpeg-LICENSE.txt`. Kontrolní seznam FFmpeg
  (https://ffmpeg.org/legal.html, body 3–8 a 10) doporučuje zdroj **hostovat u binárky** a v okně O aplikaci mít větu
  „This software uses libraries from the FFmpeg project under the LGPLv2.1“. Build BtbN, na který se odkazuje, už
  jednou zmizel (T-162, 404). Rozhodne uživatel (otázka 9).

### 3.5 Nálezy v dokumentaci
- Opraveno (mechanicky): README EN/CZ „preview versions are allowed“ / „povolené jsou i preview verze“ odporovalo
  `global.json` po T-193 (`allowPrerelease: false`). V `docs/release.md` byl ve zkušebním vydání starý příkaz
  `git switch feature/T-055-release-scripts`, teď `git switch develop`.
- Neopraveno (není mechanické): `docs/release.md` popisuje jako běžný tok „Vydání na dvou počítačích“ s `-AddToPublished`,
  který T-180 ruší (část `qa-reviewer`). Chybí upozornění na `caffeinate` na Macu. Věta o přijetí aktualizace ve
  zkušebním vydání (2.2) neodpovídá tomu, co jde ověřit.
- T-127 1c bod 1 (T-170) je nejspíš splněný, ale neodškrtnutý (B5).

## 4. Verze 1.0.0

### 4.1 Kde je verze
- Jediný zdroj: `<Version>` v kořenovém `Directory.Build.props` (dnes `0.5.0`). Odtud exe, okno O aplikaci, `Info.plist`
  (`build/macos/Info.plist`, `@VERSION@`), `vpk --packVersion`, názvy souborů a tag. Změní ho **jen** `release.ps1` krok 1
  na větvi `release/1.0.0` (spolu s datem v `CHANGELOG.md`), nikdy ručně na `develop`.
- Testy čtou verzi z `Directory.Build.props` dynamicky (`AboutViewModelTests.VersionComesFromDirectoryBuildProps`,
  `ThirdPartyLicensesTests.FFmpegMacOSBuildScriptLinkUsesTheApplicationVersion`). Kontrola 2026-11-11: `dotnet test`
  projektů Core, App a App.UiTests s `-p:Version=1.0.0` prošel. Selhaly jen tyto dva testy, protože porovnávají
  přepsanou vlastnost se souborem (s verzí změněnou v souboru projdou). Na verzi závislá logika (`IsDefaultFor`,
  `Test-PrereleaseVersion`) se přepne podle očekávání.

### 4.2 SemVer po 1.0.0
- Od 1.0.0 platí SemVer bez výjimky pro 0.x: nekompatibilní změna (nastavení, chování) jen v nové hlavní verzi.
  Návrh příslibu do CHANGELOG / README (rozhodne uživatel, otázka 5): soubory nastavení starších verzí 1.x (i 0.5.0)
  se vždy načtou; formát nastavení se v 1.x mění jen přidáním volitelných položek (`SettingsDocument.CurrentVersion`
  zůstává 2); novější soubor (vyšší `version`) starší verze nepřepíše (už tak funguje, `…Writer`, „newer format“).
- Aktualizace přes Velopack jdou jen nahoru. Návrat na starší verzi = ruční instalace (nastavení se načte, viz 4.3).

### 4.3 Nastavení z 0.5.0 v `develop` (ověřeno 2026-11-11)
- Postup: dočasný worktree `v0.5.0` (mimo repozitář) a dva malé konzolové programy (jeden nad `LayerPeek.Core` z 0.5.0,
  druhý nad `develop`), jen `JsonSettingsStore.LoadAsync` / `SaveAsync`. Vstupy: všech 9 platných fixtures
  `tests/LayerPeek.Core.Tests/Fixtures/settings-v*.json` (0.1.0–0.5.0) a ručně sestavený „maximální“ soubor 0.5.0 se
  všemi sekcemi (obecné, oznámení, překryv, `cameraWindowAutoClose`, `recording` včetně dashcamu a timelapse po vrstvě,
  `detection`, tiskárna s obrázkem, prahy vychladnutí, kamerou, oknem, pásem malého náhledu, nahráváním, dashcamem,
  timelapse a detekcí s výřezem, oběma detektory a tmavým filamentem). Kód 0.5.0 je načetl (bez varování) a uložil,
  tím vznikly soubory přesně ve tvaru, který zapisuje 0.5.0.
- Výsledek: `develop` všech 10 souborů zapsaných 0.5.0 načetl se stavem `Loaded`, **bez varování a bez oznámení
  o převodu**, a znovu je uložil **bajtově stejně**.
- Opačný směr (návrat z 1.0.0 na 0.5.0): soubor s novou položkou `miniPreviewFrameRate` (T-185) 0.5.0 načte bez
  varování a položku ignoruje (při uložení ji zahodí).
- Rozdíl formátu od `v0.5.0`: jediná nová volitelná položka `miniPreviewFrameRate` (zapisuje se jen mimo výchozí
  hodnotu), `CurrentVersion` stále 2. Stav kontroly aktualizací (`UpdateState`) dostal dvě volitelné položky (T-181),
  také jen přidané.

### 4.4 Zmínky o 0.x / „předběžné“ v textech
| Soubor | Místo |
|---|---|
| `README.md` ř. 359, `README.cz.md` ř. 355 | „Versions 0.x are **previews**. Settings may change between versions.“ |
| `SECURITY.md` ř. 5 | „Versions 0.x are previews.“ |
| `manuals/{en,cs,sk,de,es,fr,it,pl}/manual.md` ř. ~50 | „Versions 0.x are previews and the builds are not signed…“ (každý jazyk; zbytek věty o nepodepsaných buildech platí dál) |
| `CHANGELOG.md` ř. 6–7 | „Until 1.0.0, minor versions may contain breaking changes…“ |
| `README.md` ř. 447 (+ CZ) | Roadmap „`winget install` as soon as the first release is public“ |
| `docs/release.md` „Rozhodnutí“ a „Navazující úkoly“ | 0.x = `--prerelease` – platí dál, jen doplnit, že 1.x předběžné není |

Texty voleb „Nabízet předběžné verze“ v aplikaci a příručkách zůstávají (volba existuje dál).

## 5. Navržený postup dne vydání

### A. Předem (dny před vydáním)
1. **Windows (W):** B1, B2, B3 v T-125 na Windows (SDK GA, `dotnet test`, snímky přes `LAYERPEEK_UPDATE_SNAPSHOTS=1`
   na větvi `feature/*`, E2E `UpdateTests`). Snímky sloučit do `develop` **před** `release.ps1` (`docs/release.md`,
   „Opakování a chyby“, řádek o zastaralých snímcích).
2. **Mac (M):** B5 (`dotnet test` na Macu zelený, odškrtnout T-127 1c).
3. **Texty (A, U):** B6, B7 podle odpovědí na otázky 5 a 7 (CHANGELOG, README EN/CZ, SECURITY, 8 příruček) na větvi
   `feature/*`, sloučit do `develop`.
4. **Generálka B4 (M, W, U):** na každém OS v samostatném worktree dočasná místní větev z `develop` s
   `<Version>1.0.0</Version>` a datovanou sekcí `CHANGELOG.md` (nikdy nepushnout), čistý strom, LayerPeek ukončený
   (smoke test spouští aplikaci s tvými nastaveními). Spustit `pwsh scripts/release-build.ps1 -FeedSigningKey <médium>`
   (Mac pod `caffeinate -i`), heslo 2×. Výsledek: `build-info.json` se `Status` `signed` a otiskem `4cb047d1…`.
   Pak větev i `artifacts/release/1.0.0` smazat (`release.ps1` složku verze stejně přepíše) a médium odpojit.
   (`docs/release.md`, „Podpis feedu aktualizací“, „Podepisování při vydání“)
5. **GitHub (U):** T-180 účet, oddíl 3.2, rozhodnutí o neměnných vydáních (otázka 4) a o e-mailech (otázka 1). Případně
   vytvořit fine-grained PAT na 1–7 dní (T-180).
6. Doporučené body z 1.2, na které zbude čas (nahrávání, kvalita kamery, Gatekeeper/oznámení z lokálního buildu).

### B. Den vydání (oba OS do konceptu, pak zveřejnit)
1. Mac, `develop` čistý a stejný jako `origin/develop`: `git switch develop && git pull --ff-only`
   (`docs/release.md`, „Vydání jedním příkazem“).
2. (U) `$env:GH_TOKEN = Read-Host -MaskInput` (T-180), nebo dočasné `gh auth login`.
3. `pwsh scripts/release.ps1 -Version 1.0.0 -DryRun`: plán, žádné problémy; zapsat do T-127 3 bod 1 (T-055).
4. (U) Připojit médium, `$env:LAYERPEEK_FEED_SIGNING_KEY = '/Volumes/LP-KEY/layerpeek-feed-key.pem'`, pak
   `caffeinate -i pwsh scripts/release.ps1 -Version 1.0.0`. Potvrdit push `main` + `v1.0.0` (napsáním verze), push
   `develop`, heslo 2× během buildu, potvrdit upload do **konceptu**. Pak `Remove-Item Env:LAYERPEEK_FEED_SIGNING_KEY`
   a médium odpojit (`docs/release.md`, kroky 1–8 tabulky a „Podpis feedu (T-181)“).
5. Windows (W, U, s médiem): `git fetch origin --tags; git switch --detach v1.0.0`,
   `pwsh scripts/release-build.ps1 -FeedSigningKey E:\layerpeek-feed-key.pem` (heslo 2×),
   `pwsh scripts/release-upload.ps1 -DryRun`, `pwsh scripts/release-upload.ps1` **do konceptu** (bez
   `-AddToPublished`). (`docs/release.md`, „Postup“, krok 3)
6. Kontrola konceptu na GitHubu (U): text = sekce `[1.0.0]`, **ne předběžné**, soubory obou OS včetně
   `releases.win-x64.json.sig` a `releases.osx-arm64.json.sig`.
7. `pwsh scripts/release-publish.ps1 -DryRun`, pak `pwsh scripts/release-publish.ps1` **bez** `-AllowIncomplete`
   (`docs/release.md`, „Kontrola a zveřejnění“). Pořadí kroku 7 a zveřejnění repozitáře rozhodne otázka 10.
8. (U) Zveřejnit repozitář (Settings → General → Danger Zone → Change visibility), jen po oddíle 3.2.
9. Úklid: `Remove-Item Env:GH_TOKEN`, PAT smazat nebo nechat vypršet, `git switch develop` na obou počítačích.

### C. Hned po zveřejnění
1. (U) `gh release view v1.0.0 --json isPrerelease,isDraft` = `false,false`. Stránka vydání a stažení **bez přihlášení**
   (anonymní okno prohlížeče).
2. (M, U) Nainstalovaná 0.5.0 na Macu: aktualizace na 1.0.0 přes Velopack (T-127 3: T-058 aktualizace, Klíčenka,
   oznámení, Místní síť); `.dmg` z GitHubu přes Gatekeeper (T-058, T-126 1).
3. (W, U) Nainstalovaná 0.5.0 na Windows: aktualizace na 1.0.0; stažení `Setup.exe` v Edge a SmartScreen (T-017 v T-125).
4. (U) `shasum -a 256 -c SHA256SUMS-macos.txt` / `Get-FileHash` nad staženými soubory (`docs/release.md`, krok 6).
5. (W, U) winget podle T-059 (připravuje se souběžně; `wingetcreate` s tokenem jen lokálně).
6. Doplnit výsledky do T-125 / T-126 / T-127 a do tohoto úkolu. Každá chyba = nový úkol (případně `hotfix/1.0.1`).

## Akceptační kritéria
- [ ] B1 (T-125 2r), B2 (T-125 2w a automatické body 2n, 2p, 2u, 2x), B3 (T-125 2v bod 1) odškrtnuté na Windows
- [ ] B5 (T-127 1c bod 1) ověřený a odškrtnutý na Macu
- [ ] B4: generálka podepsaného buildu 1.0.0 bez GitHubu prošla na Macu i na Windows (`build-info.json` `signed`,
      otisk `4cb047d1…7b91e26`), výsledek (datum, OS, doba buildu) zapsaný v Poznámkách. Žádná větev ani tag se
      nepushnuly
- [ ] B6: `CHANGELOG.md` – věta o 0.x nahrazená podle otázky 5, `[Unreleased]` doplněná o T-188, T-194, T-193,
      případný úvod jako `## [1.0.0] - unreleased` (otázka 7)
- [ ] B7: README EN/CZ, `SECURITY.md` a všech 8 příruček bez tvrzení „0.x = předběžné“ (znění podle otázky 5),
      README EN a CZ ve shodě
- [ ] B8: rozhodnutí o e-mailech autorů zapsané (otázka 1) a provedené; `user.email` nastavený na obou počítačích
- [ ] B9 / oddíl 3.2 zkontrolovaný uživatelem a zapsaný
- [ ] Odpovědi na otázky 2–4 a 6–10 zapsané
- [ ] `docs/release.md` po rozhodnutí o otázce 4 popisuje tok 1.0.0 (oba OS do konceptu), doplněný `caffeinate`
      a oprava věty o zkušebním vydání (2.2)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování (jen pokud se v rámci úkolu změní něco mimo Markdown)

## Otevřené otázky
1. **E-maily autorů v historii** (394 commitů `pfoltynek@MacMini2.home-lab.cz`, 537 `pfoltynek@outlook.cz`): (a)
   ponechat a od teď používat adresu `…@users.noreply.github.com`, (b) přepsat historii před zveřejněním (mění SHA
   všech commitů i tagů 0.x, force-push, dočasné vypnutí rulesetů), nebo (c) jen `.mailmap`? Doporučení architekta
   je (a), rozhodne uživatel. Blokuje zveřejnění.
2. **Zveřejnit i interní pracovní materiály** (`tasks/**`, `docs/**` s rozhodnutími, `CLAUDE.md`, `.claude/agents/*.md`,
   snímky z kamery v `tests/LayerPeek.Detection.Tests/Fixtures/coreone/`, příklad `192.168.0.227`) tak, jak jsou?
   README je už dnes zmiňuje jako veřejné („The whole process is public“).
3. **Ověření přijetí podepsaného feedu aplikací před 1.0.0:** stačí dnešní pokrytí (oddíl 2.1) a první skutečná
   ověřená aktualizace 1.0.0 → 1.0.1, nebo něco navíc? Varianty: (a) nic navíc, 1.0.1 brzy jako kontrola; (b) hned po
   zveřejnění 1.0.0 nainstalovat místní Release build s nižší verzí (např. 0.9.9 z commitu 1.0.0) a nechat ho
   aktualizovat na 1.0.0 z GitHubu (ověří skutečné stažení `.sig`, ale až po zveřejnění); (c) změna kódu, aby testovací
   zdroj v Debug mohl důvěřovat i produkčnímu klíči (bezpečnostní rozhodnutí). A má se zkušební vydání v testovacím
   repozitáři (T-127 oddíl 3) dělat před 1.0.0, nebo ho nahradí generálka B4 + `-DryRun`?
4. **Neměnná vydání (T-180) před 1.0.0?** Když ano: 1.0.0 se zveřejní až s oběma OS v konceptu (oddíl 5 B). Část T-180
   pro `qa-reviewer` (odmítnutí `-AddToPublished` a `-AllowIncomplete` ve skriptech, `docs/release.md`) může přijít
   až po 1.0.0, nebo má být před ním? Když ne: zůstává i varianta Mac zveřejní, Windows doplní později.
5. **Znění pro 1.x** místo „Versions 0.x are previews“ (README, SECURITY, příručky) a příslib kompatibility místo věty
   „Until 1.0.0 …“ v CHANGELOG. Návrh: „Updates within 1.x keep your settings; settings files of older versions
   always load“ a SECURITY „Only the latest release gets security fixes“.
6. **Vydání 0.1.0–0.5.0** na GitHubu po zveřejnění ponechat (historie, `v0.2.0` přestane být „Latest“), nebo je
   skrýt / smazat (smazání vydání je nevratné, tagy zůstanou)?
7. **Úvod textu vydání 1.0.0** (např. „first public release“, odkaz na příručku, SmartScreen / Gatekeeper, kontrola
   `SHA256SUMS`)? Text vydání se bere jen ze sekce CHANGELOG (pak `## [1.0.0] - unreleased`, oddíl 3.3).
8. **Hlavičky licence (SPDX) v souborech** – zavést (velká mechanická změna), nebo ne?
9. **FFmpeg LGPL:** stačí dnešní odkazy na zdroj (BtbN zip a tarball ffmpeg.org), nebo přikládat zdroj FFmpeg
   (archiv přesně použité verze, configure) přímo k vydání na GitHubu a doplnit větu podle ffmpeg.org/legal.html,
   bod 10, do okna O aplikaci? Licenční / balicí rozhodnutí.
10. **Pořadí zveřejnění:** nejdřív zveřejnit vydání 1.0.0 v ještě soukromém repozitáři a pak repozitář (nikdo neuvidí
    mezistav, `v0.2.0` jako „Latest“), nebo nejdřív zveřejnit repozitář a pak vydání? Anonymní ověření (C1–C3) a winget
    jdou až po obojím.

## Poznámky k implementaci
Analýza 2026-11-11 (architekt, Mac, větev `feature/T-198-release-1-0-0-readiness`):
- Sken historie: `git log --all -p --text` (931 commitů) přes vlastní skript v Pythonu ve scratchpadu (vzory:
  hlavičky PEM, tokeny GitHub/AWS/Slack/Google/Anthropic/OpenAI, JWT, přiřazení `password/token/api key/secret`,
  přihlašovací údaje v URL, `/Users/…`, `C:\Users\…`, `/Volumes/…`, `home-lab`/`MacMini2`/`.lan`/`.home`, e-maily,
  soukromé IP, sériová čísla), dále bloky base64 ve tvaru PEM mimo obrázky, soubory citlivých typů přidané v historii
  a `git log --format='%an <%ae>|%cn <%ce>'`. Nic se nenahrávalo, nic se neinstalovalo.
- Kompatibilita nastavení: dočasný `git worktree add --detach … v0.5.0` ve scratchpadu (po kontrole odstraněný)
  a dva konzolové programy nad `LayerPeek.Core` (0.5.0 a `develop`). Do repozitáře se nic nepřidalo.
- Testy s `-p:Version=1.0.0`: Core.Tests 3277/0, App.Tests 4613/2 (dva testy, které porovnávají verzi se souborem,
  viz 4.1), App.UiTests 1374/0 (Mac, Inter).
- Mechanické opravy: README EN/CZ (SDK bez preview po T-193), `docs/release.md` (zkušební vydání: `git switch develop`).
- Zdroje: https://ffmpeg.org/legal.html (kontrolní seznam LGPL), immutable releases a PAT viz zdroje v T-180.
