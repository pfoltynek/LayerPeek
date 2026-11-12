---
id: T-207
title: Windows ARM64 – sestavovací prostředí a Native AOT publish `win-arm64` z x64 (bez FFmpeg)
status: todo
owner: core-dev
priority: P2
platform: windows
depends_on: []
---

> **Rozhodnutí uživatele 2026-11-12:** LayerPeek se bude vydávat **i pro Windows ARM64 (`win-arm64`)**, vedle
> `win-x64`, a má se připravit sestavovací prostředí pro Windows ARM64. Tím se ruší rozhodnutí z 2026-11-01 (T-130:
> „Windows jen `win-x64`“). Série úkolů: **T-207** (prostředí a AOT publish), **T-208** (FFmpeg `win-arm64`),
> **T-209** (vydání, instalátor, aktualizace, winget, dokumentace), **T-210** (ověření na zařízení ARM64).
> Zamítnuté T-057 a T-063 zůstávají v `tasks/rejected/` jako historie; jejich zjištění tato série přebírá.

## Cíl
Na vývojovém stroji Windows x64 jde příkazem `dotnet publish src/LayerPeek.App -c Release -r win-arm64` vytvořit
Native AOT `LayerPeek.exe` pro ARM64 bez trim/AOT varování a se všemi nativními knihovnami pro ARM64. Kamera (FFmpeg)
v tomto úkolu ještě není: `build/FFmpeg.targets` pro RID `win-arm64` dnes FFmpeg vypíná (`_LayerPeekFFmpegPlatform`
je prázdné), to řeší T-208. Skripty vydání tu RID ještě nepovolují (T-209), mění se jen kontrola toolchainu a
dokumentace prostředí.

## Rozsah
1. **Toolchain (ověřený zdroj):** Microsoft Learn, „Cross-compilation“
   (https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/cross-compile): „Cross-compiling from x64 Windows
   to ARM64 Windows or vice versa works as long as the appropriate VS 2022 C++ build tools are installed. To target
   ARM64 make sure the Visual Studio component "VS 2022 C++ ARM64/ARM64EC build tools (Latest)" is installed.“
   - Komponenta `Microsoft.VisualStudio.Component.VC.Tools.ARM64` (ID ověřit v instalátoru Build Tools 2026, na tomto
     stroji 2026-11-12 není nainstalovaná – `vswhere -requires Microsoft.VisualStudio.Component.VC.Tools.ARM64` nic
     nevypíše). Instaluje ji **uživatel jako správce** (pokus agenta v T-055 skončil kódem 5007 bez zvýšených oprávnění).
     Návrh příkazu (ověřit): `& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\setup.exe" modify
     --installPath <cesta z vswhere> --add Microsoft.VisualStudio.Component.VC.Tools.ARM64 --passive --norestart`.
   - Obnovení balíčků cílového RID (`runtime.win-arm64.Microsoft.DotNet.ILCompiler`,
     `Microsoft.NETCore.App.Runtime.win-arm64`, `Microsoft.AspNetCore.App.Runtime.win-arm64`) proběhne samo při
     `dotnet publish -r win-arm64`; hostitelský ILCompiler zůstává `runtime.win-x64.*`.
2. **Kontrola toolchainu ve skriptech:** `Get-NativeToolchainProblems` (`scripts/ReleaseTools/ReleaseTools.psm1`, tabulka
   `$components`) zná pro `win-arm64` komponentu `…VC.Tools.ARM64` (vrácení stavu před T-130) – chybějící komponenta =
   srozumitelná hláška s odkazem na `docs/prerequisites.md`. Samotné povolení RID v `$script:ReleaseRuntimes` je až
   v T-209. Pester test kontroly upravit.
3. **Publish `win-arm64`:** bez varování IL2xxx/IL3xxx z kódu projektu (stávající hlášky `ILC: Method … will always
   throw` z Avalonia DComposition zapsat, pokud se objeví stejně jako u x64). `LayerPeek.exe` i všechny nativní DLL
   v publish složce mají v hlavičce PE pole Machine `0xAA64` – kontrola skriptem nebo testem (ne ručně), žádná x64 DLL.
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
   `dotnet publish -r win-arm64`), odstranit řádek „`Unknown runtime 'win-arm64'` … nepodporuje (T-130)“ až s T-209;
   věta „Vydávané RID“ na začátku souboru až s T-209. Spuštění na x64 nejde (Windows na x64 neumí emulovat ARM64),
   ověření běhu je v T-210.

## Akceptační kritéria
- [ ] Komponenta MSVC ARM64 je nainstalovaná (uživatel, jako správce) a `vswhere -requires
      Microsoft.VisualStudio.Component.VC.Tools.ARM64` ji najde; postup v `docs/prerequisites.md`
- [ ] `dotnet publish src/LayerPeek.App -c Release -r win-arm64` na Windows x64 projde bez trim/AOT varování z kódu
      projektu; exe a všechny nativní DLL publish složky jsou ARM64 PE (`0xAA64`), kontrola automatizovaná
- [ ] Publish obsahuje nativní knihovny Skia, HarfBuzz a ANGLE pro ARM64
- [ ] `Get-NativeToolchainProblems` hlásí chybějící komponentu ARM64 pro `win-arm64` (Pester test)
- [ ] `ThirdPartyLicenses` a `ThirdPartyLicensesTests` pokrývají balíčky runtime `win-arm64`
- [ ] Publish `win-x64` a build bez RID beze změny
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. **Instalace komponenty MSVC ARM64:** provede ji uživatel sám jako správce (doporučeno, stejně jako v T-055/T-063),
   nebo má agent připravit skript, který si o zvýšení oprávnění řekne (UAC)? Doporučení: uživatel ručně, příkaz
   v `docs/prerequisites.md`.
2. **Kontrola PE hlavičky:** stačí Pester/pwsh kontrola v `release-build.ps1` (T-209), nebo i xUnit test nad publish
   složkou (publish v testu je pomalý)? Doporučení: funkce v `ReleaseTools.psm1` + Pester test nad malými ukázkovými
   soubory, volaná z `release-build.ps1` a `aot-smoke.ps1`.

## Poznámky k implementaci
(vyplní agent po dokončení)
