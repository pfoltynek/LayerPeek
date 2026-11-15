---
id: T-224
title: Windows ARM64 – sestavovací prostředí a Native AOT publish `win-arm64` z x64 (bez FFmpeg)
status: done
owner: core-dev
priority: P2
platform: windows
depends_on: []
---

> **Rozhodnutí uživatele 2026-11-12:** LayerPeek se bude vydávat **i pro Windows ARM64 (`win-arm64`)**, vedle
> `win-x64`, a má se připravit sestavovací prostředí pro Windows ARM64. Tím se ruší rozhodnutí z 2026-11-01 (T-130:
> „Windows jen `win-x64`“). Série úkolů: **T-224** (prostředí a AOT publish), **T-225** (FFmpeg `win-arm64`),
> **T-226** (vydání, instalátor, aktualizace, winget, dokumentace), **T-227** (ověření na zařízení ARM64).
> Zamítnuté T-057 a T-063 zůstávají v `tasks/rejected/` jako historie; jejich zjištění tato série přebírá.

## Cíl
Na vývojovém stroji Windows x64 jde příkazem `dotnet publish src/LayerPeek.App -c Release -r win-arm64` vytvořit
Native AOT `LayerPeek.exe` pro ARM64 bez trim/AOT varování a se všemi nativními knihovnami pro ARM64. Kamera (FFmpeg)
v tomto úkolu ještě není: `build/FFmpeg.targets` pro RID `win-arm64` dnes FFmpeg vypíná (`_LayerPeekFFmpegPlatform`
je prázdné), to řeší T-225. Skripty vydání tu RID ještě nepovolují (T-226), mění se jen kontrola toolchainu a
dokumentace prostředí.

## Rozsah
1. **Toolchain (ověřený zdroj):** Microsoft Learn, „Cross-compilation“
   (https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/cross-compile): „Cross-compiling from x64 Windows
   to ARM64 Windows or vice versa works as long as the appropriate VS 2022 C++ build tools are installed. To target
   ARM64 make sure the Visual Studio component "VS 2022 C++ ARM64/ARM64EC build tools (Latest)" is installed.“
   - Komponenta `Microsoft.VisualStudio.Component.VC.Tools.ARM64` je na vývojovém stroji Windows nainstalovaná od
     2026-11-12 (hlavní agent se souhlasem uživatele; VS Build Tools 2026, MSVC 14.51.36231, otázka 1). Na dalších
     strojích ji instaluje uživatel jako správce (pokus bez zvýšených oprávnění v T-055 skončil kódem 5007).
     Návrh příkazu (ověřit): `& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\setup.exe" modify
     --installPath <cesta z vswhere> --add Microsoft.VisualStudio.Component.VC.Tools.ARM64 --passive --norestart`.
   - Obnovení balíčků cílového RID (`runtime.win-arm64.Microsoft.DotNet.ILCompiler`,
     `Microsoft.NETCore.App.Runtime.win-arm64`, `Microsoft.AspNetCore.App.Runtime.win-arm64`) proběhne samo při
     `dotnet publish -r win-arm64`; hostitelský ILCompiler zůstává `runtime.win-x64.*`.
2. **Kontrola toolchainu ve skriptech:** `Get-NativeToolchainProblems` (`scripts/ReleaseTools/ReleaseTools.psm1`, tabulka
   `$components`) zná pro `win-arm64` komponentu `…VC.Tools.ARM64` (vrácení stavu před T-130) – chybějící komponenta =
   srozumitelná hláška s odkazem na `docs/prerequisites.md`. Samotné povolení RID v `$script:ReleaseRuntimes` je až
   v T-226. Pester test kontroly upravit.
3. **Publish `win-arm64`:** bez varování IL2xxx/IL3xxx z kódu projektu (stávající hlášky `ILC: Method … will always
   throw` z Avalonia DComposition zapsat, pokud se objeví stejně jako u x64). `LayerPeek.exe` i všechny nativní DLL
   v publish složce mají v hlavičce PE pole Machine `0xAA64`, žádná x64 DLL – kontrola funkcí v `ReleaseTools.psm1`
   s Pester testem, volaná z `release-build.ps1` a `aot-smoke.ps1` (otázka 2).
4. **Nativní závislosti NuGet (ověřeno 2026-11-12 v místní NuGet cache, verze podle `project.assets.json` aplikace):**
   `SkiaSharp.NativeAssets.Win32` 3.119.4, `HarfBuzzSharp.NativeAssets.Win32` 8.3.1.3 a
   `Avalonia.Angle.Windows.Natives` 2.1.27548.20260419 mají `runtimes/win-arm64/native`. `Velopack` 1.2.161 je čistě
   spravovaný (`lib/net*`), nativní části (`Setup.exe` / `Update.exe`) přidává `vpk` podle `-r` (T-056: pro `win-arm64`
   ARM64 PE). Ověřit, že publish všechny tři nativní sady skutečně zkopíruje (Skia, HarfBuzz, ANGLE `av_libglesv2.dll`).
   Licence těchto balíčků se nemění (stejné balíčky, jiný RID).
5. **Licence a okno licencí:** `ThirdPartyLicenses` uvádí balíčky runtime podle RID
   (`Microsoft.AspNetCore.App.Runtime.win-x64`, `runtime.win-x64.Microsoft.DotNet.ILCompiler`, `osx-arm64`). Doplnit
   `win-arm64` a upravit test `ThirdPartyLicensesTests` (dnes mapuje jen `win-x64` ↔ `osx-arm64`), aby `win-arm64`
   publish neměl nepopsaný balíček.
6. **Vlastní kód:** projít `[LibraryImport]` / COM interop Windows v `LayerPeek.App/Platform` na předpoklady architektury
   (velikosti struktur, `IntPtr.Size`, x64 specifické konstanty). Oba cíle jsou 64bitové, změny se nečekají – výsledek
   zapsat do poznámek.
7. **Dokumentace:** `docs/prerequisites.md` – komponenta MSVC ARM64 (instalace jako správce, ověření `vswhere`,
   `dotnet publish -r win-arm64`), odstranit řádek „`Unknown runtime 'win-arm64'` … nepodporuje (T-130)“ až s T-226;
   věta „Vydávané RID“ na začátku souboru až s T-226. Spuštění na x64 nejde (Windows na x64 neumí emulovat ARM64),
   ověření běhu je v T-227.

## Akceptační kritéria
- [x] Komponenta MSVC ARM64 je nainstalovaná (na vývojovém stroji 2026-11-12, otázka 1) a `vswhere -requires
      Microsoft.VisualStudio.Component.VC.Tools.ARM64` ji najde; postup v `docs/prerequisites.md`
- [x] `dotnet publish src/LayerPeek.App -c Release -r win-arm64` na Windows x64 projde bez trim/AOT varování z kódu
      projektu; exe a všechny nativní DLL publish složky jsou ARM64 PE (`0xAA64`), kontrola automatizovaná
- [x] Publish obsahuje nativní knihovny Skia, HarfBuzz a ANGLE pro ARM64
- [x] `Get-NativeToolchainProblems` hlásí chybějící komponentu ARM64 pro `win-arm64` (Pester test)
- [x] `ThirdPartyLicenses` a `ThirdPartyLicensesTests` pokrývají balíčky runtime `win-arm64`
- [x] Publish `win-x64` a build bez RID beze změny
- [x] `dotnet build` a `dotnet test` bez chyb a varování (agent: build + dotčené testovací projekty; celé `dotnet test` a E2E
      ověřuje hlavní agent před sloučením)

## Otevřené otázky
1. ~~**Instalace komponenty MSVC ARM64:** provede ji uživatel sám jako správce, nebo skript s UAC?~~
   **Rozhodnuto uživatelem 2026-11-12:** komponentu `Microsoft.VisualStudio.Component.VC.Tools.ARM64` nainstaloval
   2026-11-12 hlavní agent se souhlasem uživatele na tomto vývojovém stroji Windows (VS Build Tools 2026, MSVC
   14.51.36231; přítomné `Hostx64\arm64\link.exe` a `lib\arm64`). Ruční instalační příkaz (jako správce) a ověření
   `vswhere` přesto patří do `docs/prerequisites.md` pro další stroje.
2. ~~**Kontrola PE hlavičky:** Pester/pwsh funkce, nebo i xUnit test nad publish složkou?~~ **Rozhodnuto uživatelem
   2026-11-12:** funkce v `scripts/ReleaseTools/ReleaseTools.psm1` + Pester test (malé ukázkové soubory), volaná
   z `release-build.ps1` a `aot-smoke.ps1`; žádný xUnit test s publish.
3. ~~**FFmpeg pro `win-arm64` do T-225:** vypnout automaticky podle RID, nebo jen výslovným
   `-p:LayerPeekFFmpegEnabled=false`?~~ **Rozhodnuto uživatelem 2026-11-15:** ponechat automatické vypnutí podle RID
   (`LayerPeekAppRuntimeIdentifier`, viz poznámky); T-225 ho nahradí výběrem ARM64 FFmpeg.

## Poznámky k implementaci
- **Toolchain:** `vswhere -products * -requires Microsoft.VisualStudio.Component.VC.Tools.ARM64` na vývojovém stroji
  vypíše „Visual Studio Build Tools 2026“. ILCompiler 11.0.0 hledá pro cíl `arm64` právě tuto komponentu
  (`Microsoft.NETCore.Native.Windows.targets`, `_FindVCVarsallToolsSuffix` = `ARM64`), komponentu x86.x64 pro ARM64
  nepotřebuje. Tabulka komponent je teď `$script:MsvcToolchainComponents` + `Get-MsvcToolchainComponent`;
  `Get-NativeToolchainProblems` zná `win-arm64`, RID bez známé komponenty hlásí místo dotazu `vswhere -requires` bez
  hodnoty a má parametr `-VswherePath` (Pester testy volají zástupný skript, žádné Visual Studio). `$script:ReleaseRuntimes`
  beze změny (T-226).
- **Instalační příkaz** ověřený v Microsoft Learn („Use command-line parameters to install Visual Studio“): `setup.exe
  modify --installPath <cesta> --add Microsoft.VisualStudio.Component.VC.Tools.ARM64 --passive --norestart`; `modify`
  vyžaduje `--installPath`, `--norestart` jen s `--passive`/`--quiet`, operace instalátoru potřebují správce a `setup.exe`
  se musí spustit z jiné složky než `Installer` (proto `Push-Location $env:TEMP` v `docs/prerequisites.md`). Na tomto
  stroji se znovu nespouštěl (komponenta už je nainstalovaná).
- **FFmpeg a `win-arm64` (zjištění):** předpoklad z cíle úkolu („`build/FFmpeg.targets` pro RID `win-arm64` FFmpeg
  vypíná“) neplatil. `LayerPeek.Camera.FFmpeg` je knihovna bez RID, SDK ji v publish aplikace sestaví **bez**
  `RuntimeIdentifier` (RID-agnostic reference), takže `FFmpeg.targets` viděl prázdný RID → `windows` a první publish
  `win-arm64` obsahoval x64 `avcodec-63.dll` … `swscale-10.dll` (`0x8664`, zachytila to kontrola PE). Oprava bez změny
  `build/FFmpeg.targets` (kvůli souběžnému T-202): `src/LayerPeek.App/LayerPeek.App.csproj` pro
  `RuntimeIdentifier == win-arm64` dá referenci na `LayerPeek.Camera.FFmpeg`
  `AdditionalProperties="LayerPeekAppRuntimeIdentifier=$(RuntimeIdentifier)"`; `LayerPeek.Camera.FFmpeg.csproj` pro
  hodnotu `win-arm64` nastaví před importem `FFmpeg.targets` `LayerPeekFFmpegEnabled=false` a své referenci na
  `LayerPeek.Core` vlastnost odřízne (`UndefineProperties` + `GlobalPropertiesToRemove`). Publish `win-arm64` pak nemá
  DLL FFmpeg ani `FFmpeg-LICENSE.txt` (kamera nefunguje, do T-225); `win-x64` a build bez RID beze změny.
  - **Oprava po review (2026-11-15):** první verze předávala přímo `LayerPeekFFmpegEnabled=false`; globální vlastnost
    tekla dál na referenci `Camera.FFmpeg → Core`, takže `LayerPeek.Core` vznikal jako druhá instance projektu do
    stejného `obj/`/`bin/` (`LayerPeek.Core ->` dvakrát, latentní závod MSB3026/CS2012). Navržené
    `UndefineProperties="LayerPeekFFmpegEnabled"` na referenci Core by rozbilo zdokumentované
    `dotnet build -p:LayerPeekFFmpegEnabled=false` (ověřeno: Core pak dvakrát, protože App, PrusaLink a Detection vlastnost
    z příkazové řádky předávají a Camera.FFmpeg ne), proto vlastní vlastnost `LayerPeekAppRuntimeIdentifier`, kterou
    nikdo z příkazové řádky nenastavuje. Ověřeno: `LayerPeek.Core ->` jednou u `dotnet build src/LayerPeek.App` s
    `-r win-arm64`, `-r win-x64`, bez RID i s `-p:LayerPeekFFmpegEnabled=false` a u obou `dotnet publish`; 0 varování.
    Pester test vyhodnotí (`dotnet msbuild -getItem` / `-getProperty`, nic nesestaví) metadata obou referencí a
    `LayerPeekFFmpegEnabled` knihovny.
  - **Pro T-225:** RID se do knihovny nedostane sám; výběr ARM64 FFmpeg použije `LayerPeekAppRuntimeIdentifier`
    (zapsáno do T-225).
- **Publish `win-arm64`** (`dotnet publish src/LayerPeek.App -c Release -r win-arm64 -warnaserror`, Windows 11 x64,
  .NET SDK 11.0.100, MSVC 14.51.36231): 0 varování, 0 chyb. Objeví se stejné dvě hlášky `ILC: Method
  '[Avalonia.Win32]Avalonia.Win32.DComposition.Impl.__MicroCom…VTable.CreateTargetForHwnd(…)' will always throw` jako
  u x64 (Avalonia, nejsou to varování). Publish složka: `LayerPeek.exe`, `libSkiaSharp.dll`, `libHarfBuzzSharp.dll`,
  `av_libglesv2.dll` (ANGLE) – všechny ARM64 `0xAA64` – a `.pdb`. NuGet doplnil `runtime.win-arm64.Microsoft.DotNet.ILCompiler`,
  `Microsoft.NETCore.App.Runtime.(NativeAOT.)win-arm64`, `Microsoft.AspNetCore.App.Runtime.win-arm64`,
  `Microsoft.WindowsDesktop.App.Runtime.win-arm64`; hostitelský překladač zůstal `runtime.win-x64.Microsoft.DotNet.ILCompiler`.
  Publish `win-x64` po změně: 9 PE souborů včetně FFmpeg, všechny x64 `0x8664`, 0 varování.
- **Kontrola PE:** `Get-PeMachine` (čte jen `MZ`, `e_lfanew`, `PE\0\0` a pole Machine), `Format-PeMachine` a
  `Get-PublishArchitectureCheck -Directory -Runtime` (exe a všechny `.exe`/`.dll` i v podsložkách; vrací `Expected`,
  `Files`, `Problems`; neznámý RID = problém). `release-build.ps1` ji volá po publish každého Windows RID před smoke
  testem (i když se smoke test na hostiteli spustit nedá), `aot-smoke.ps1` pro `win-*` před přesunem profilu a startem.
  Nápověda funkce uvádí, že je pro AOT publish (IL-only AnyCPU DLL mají `0x014C` a hlásily by se). Nové balíčky
  žádné (čisté PowerShell/.NET API).
- **Licence:** doplněno `Microsoft.AspNetCore.App.Runtime.win-arm64` a `runtime.win-arm64.Microsoft.DotNet.ILCompiler`
  (stejné balíčky a licence MIT, jiný RID). `ThirdPartyLicensesTests`: seznam publikovaných RID
  (`win-x64`, `win-arm64`, `osx-arm64`), sdílené frameworky vyžadují runtime pack pro každý, nový test
  `EveryRuntimeSpecificEntryCoversEveryPublishedRuntime`, test zastaralých záznamů porovnává se všemi RID. Testy prošly
  i nad `project.assets.json` po `dotnet restore -r win-arm64`.
- **Vlastní interop (bod 6):** `LayerPeek.App/Platform/Windows` – struktury jen s pevnými typy (`uint`, `int`, `char`,
  `nint`, ukazatele), konstanty velikostí `MonitorNameNative` (`DISPLAYCONFIG_PATH_INFO` 72, `MODE_INFO` 64) jsou
  stejné na x64 i ARM64 (obě LLP64 se stejným zarovnáním), žádné `IntPtr.Size`, `__arglist` ani varargs;
  `[Stdcall]`/`[Cdecl]` se na 64bitových platformách ignorují; `NativeLibrarySearch` architekturu nepředpokládá.
  Architekturu řeší jen `MacUnixNative` (macOS, `lstat$INODE64` na x64). Změny kódu nebyly potřeba; běh ověří T-227.
- **Dokumentace:** `docs/prerequisites.md` – oddíl „Windows ARM64 (`win-arm64`, T-224)“ (komponenta, příkaz jako
  správce, ověření `vswhere`, publish, FFmpeg vypnutý, kontrola PE, spuštění v T-227) a dva řádky „Časté chyby“.
  Věta „Vydávané RID“ a řádek T-130 zůstaly (T-226). README/CHANGELOG beze změny (úkol je nežádá).
- **Testy:** Pester `Native AOT toolchain check (T-224)` (7), `PE architecture of a publish folder (T-224)` (ukázkové
  PE soubory, 14, plus vyhodnocení referencí App → Camera.FFmpeg → Core a `LayerPeekFFmpegEnabled` přes
  `dotnet msbuild -getItem` / `-getProperty` – nic nesestaví ani nezapíše) a `aot-smoke.ps1` s DLL jiné architektury (nic nespustí ani nepřesune, jen
  Windows). Dočasné soubory pod `LAYERPEEK_PESTER_TESTS_TEMP`, AfterAll je maže. Spuštěno: `dotnet build` (0 varování),
  `LayerPeek.Scripts.Tests` (všechny soubory Pester) a třídy `ThirdPartyLicensesTests` / `LicensesViewModelTests`
  v `LayerPeek.App.Tests`; celé `dotnet test` (včetně E2E) spouští hlavní agent.

## Review
Review qa-reviewer 2026-11-15: **změny nutné** (1 blokující nález, úkol vrácen do `in-progress`).

Ověřeno: `dotnet build` 0 varování; `dotnet publish src/LayerPeek.App -c Release -r win-arm64` (čistá složka) projde,
publish obsahuje `LayerPeek.exe`, `libSkiaSharp.dll`, `libHarfBuzzSharp.dll`, `av_libglesv2.dll` – vše ARM64 `0xAA64`,
žádné DLL FFmpeg, `Get-PublishArchitectureCheck` 0 problémů; `-r win-x64` 9 PE souborů včetně FFmpeg, vše `0x8664`,
0 problémů; `Get-NativeToolchainProblems -Runtimes win-x64,win-arm64` na tomto stroji prázdné;
`LayerPeek.Scripts.Tests` 18/18 (1 přeskočen na vyžádání); `ThirdPartyLicensesTests` + `LicensesViewModelTests` 40/40.
`dotnet build src/LayerPeek.App -r win-arm64` (Debug) nemá ve výstupu DLL FFmpeg.

1. **Blokující – `LayerPeek.Core` se při `-r win-arm64` sestavuje dvakrát** (`src/LayerPeek.App/LayerPeek.App.csproj:91-93`).
   `AdditionalProperties="LayerPeekFFmpegEnabled=false"` je globální vlastnost a MSBuild ji předává dál i na reference
   `LayerPeek.Camera.FFmpeg` → `LayerPeek.Core`. Core tak vzniká jako dvě instance projektu (bez vlastnosti z App,
   PrusaLink, Detection; s ní z Camera.FFmpeg), obě do stejného `obj/` a `bin/`. Doloženo: `dotnet publish -r win-arm64`
   i `dotnet build src/LayerPeek.App -r win-arm64` vypíšou `LayerPeek.Core -> …` dvakrát, `-r win-x64` jednou. Při
   paralelním čistém buildu (App staví reference s `BuildInParallel`) hrozí souběžný zápis `LayerPeek.Core.dll`
   (MSB3026/CS2012); ve třech čistých pokusech se neprojevil, ale je to latentní závod a zbytečná práce. Tvrzení
   v poznámkách („žádné další sestavení knihovny“) proto neplatí.
   Ověřená oprava (zkusmo, vráceno): v `src/LayerPeek.Camera.FFmpeg/LayerPeek.Camera.FFmpeg.csproj:14`
   `<ProjectReference Include="..\LayerPeek.Core\LayerPeek.Core.csproj" UndefineProperties="LayerPeekFFmpegEnabled" />`
   (pro statický graf případně i `GlobalPropertiesToRemove`) – Core se pak vypíše jednou, build 0 varování. Doplnit
   komentář a Pester test (např. že Core se sestaví jen jednou / reference Core nese `UndefineProperties`), poznámky
   opravit. Kde přesně vlastnost odříznout, rozhodne implementátor; T-225 tu podmínku stejně nahradí.

Drobnosti (neblokující):
- `scripts/ReleaseTools/ReleaseTools.psm1` `Get-PublishArchitectureCheck`: spravované IL-only (AnyCPU) DLL mají Machine
  `0x014C` a hlásily by se jako chyba. V AOT publish žádné nejsou (ověřeno), takže je to v pořádku; stačí to zmínit
  v nápovědě funkce, kdyby ji někdo použil na publish bez AOT.
- Na hostiteli Windows ARM64 (T-227) dá prostý `dotnet build` bez RID dál x64 FFmpeg (prázdný RID → `windows`
  v `build/FFmpeg.targets`); věc T-225, jen pro jistotu zapsat do T-225.

Opakované review qa-reviewer 2026-11-15: **schváleno**, blokující nález 1 je opravený (jinou cestou, viz „Oprava po review“),
drobnosti zapracované (nápověda `Get-PublishArchitectureCheck`, zjištění pro hostitele ARM64 v T-225). Úkol může zůstat
v `done/`. Ověřeno: `LayerPeek.Core ->` jednou u `dotnet build src/LayerPeek.App` s `-r win-arm64`, `-r win-x64`, bez
RID, s `-p:LayerPeekFFmpegEnabled=false` i s `-r win-arm64 -p:LayerPeekFFmpegEnabled=false` a u celého `dotnet build`
(i s `-p:LayerPeekFFmpegEnabled=false`; testovací projekty referují App/Camera.FFmpeg bez RID, tedy bez nové vlastnosti),
vše 0 varování; čisté `dotnet publish -c Release -warnaserror` `win-arm64` (4 PE, vše ARM64, bez FFmpeg a
`FFmpeg-LICENSE.txt`) a `win-x64` (9 PE včetně FFmpeg, vše x64), kontrola architektury 0 problémů;
`LayerPeek.Scripts.Tests` 18/18 (1 přeskočen na vyžádání), licenční testy 40/40. Slučitelnost s T-202: přepsaný
`build/FFmpeg.targets` (hlavní pracovní strom) zachovává sémantiku `LayerPeekFFmpegEnabled` (předem nastavené `false`
respektuje), `LayerPeek.Camera.FFmpeg.csproj` T-202 nemění – konflikt se nečeká.
