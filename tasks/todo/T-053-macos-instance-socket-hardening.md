---
id: T-053
title: macOS – zabezpečení socketu jediné instance (L2) a ověření `/usr/bin/open` (L4)
status: todo
owner: ui-dev
priority: P3
platform: macos
depends_on: [T-043, T-052]
---

## Cíl
Opravit nález L2 z bezpečnostní revize (2026-10-21, závažnost **nízká**) a na Macu ověřit opravu L4 z T-052.

> **Pouze na macOS:** úkol implementuje a ověřuje uživatel později na Macu. Neblokuje žádný Windows úkol.

L2:
- `src/LayerPeek.App/SingleInstance/UnixSocketInstanceTransport.cs:84-104`: server Unix socketu nekontroluje, kdo se připojil (žádné `getpeereid` / `LOCAL_PEERCRED`). Na Windows to řeší `PipeOptions.CurrentUserOnly`. Na macOS spoléhá jen na práva složky.
- `src/LayerPeek.App/SingleInstance/InstanceTransports.cs:95-107` (`UnixPaths`): když je cesta k socketu delší než 103 bajtů, socket se přesune do `Path.GetTempPath()`. Na macOS je `$TMPDIR` obvykle soukromý pro uživatele (`/var/folders/...`), ale když je nastavený na sdílené `/tmp`, může jiný uživatel cestu předem obsadit. Následek je DoS: aplikace nepoběží jako jediná instance, nebo neuslyší příkazy.

## Rozsah
- **Kontrola protistrany:** po `Accept` zjistit UID připojeného procesu, např. `getpeereid(fd, out uid, out gid)` z `libc` přes `[LibraryImport]`, nebo `LOCAL_PEERCRED` přes `Socket.GetRawSocketOption`. Spojení od jiného UID než `geteuid()` zavřít bez čtení a zalogovat (bez obsahu). Linux, který aplikace nepodporuje, ale kód pro něj existuje: `SO_PEERCRED`, případně jen zdokumentovat.
- **Soukromá složka pro socket:** záložní socket nedávat přímo do `$TMPDIR`, ale do vlastní podsložky (např. `LayerPeek-<uid>`) vytvořené s právy `0700` (`Directory.CreateDirectory(path, UnixFileMode.UserRead | UserWrite | UserExecute)`). Když složka už existuje, ověřit, že patří aktuálnímu uživateli, má práva `0700` a není to symbolický odkaz. Jinak ochranu jediné instance vypnout, zalogovat a aplikace poběží (stejná politika jako `InstanceLockUnavailableException` z T-043). Totéž ověření pro datovou složku, pokud je socket v ní.
- **L4:** na Macu ověřit, že odkazy z menu a okna O aplikaci a otevření webu PrusaLink přes proxy fungují s `/usr/bin/open` (T-052).
- Bez nového NuGet balíčku, jen `[LibraryImport]` (AOT).

## Akceptační kritéria
- [ ] Spojení od jiného UID se odmítne (test s injektovanou kontrolou UID; na Macu skutečný test, pokud jde bez druhého uživatele, jinak ručně)
- [ ] Záložní socket je v podsložce `0700` aktuálního uživatele; cizí, symbolickým odkazem nahrazená nebo příliš otevřená složka vypne ochranu bez pádu (testy)
- [ ] Druhé spuštění na Macu dál předá příkaz běžící instanci (krátká i dlouhá cesta k datové složce)
- [ ] Ručně na Macu: `/usr/bin/open` otevírá odkazy a web PrusaLink přes proxy (L4)
- [ ] Testy po sobě nic nenechají (sockety, složky, zámky)
- [ ] Lokální Native AOT publish `osx-arm64` bez varování; Windows build a testy beze změny chování
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
