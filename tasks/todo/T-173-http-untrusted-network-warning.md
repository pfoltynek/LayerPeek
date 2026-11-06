---
id: T-173
title: Varování u adresy http:// mimo místní síť (heslo PrusaLink přes nedůvěryhodnou síť)
status: todo
owner: core-dev + ui-dev
priority: P2
platform: all
depends_on: []
---

## Cíl
Bezpečnostní revize (2026-11-06, problém B, schváleno uživatelem): PrusaLink používá HTTP Digest s MD5 podle RFC 2069
(bez `cnonce` a `qop`, omezení firmwaru; `src/LayerPeek.PrusaLink/Api/DigestAuthenticator.cs`). Kdo odposlechne
jedinou výměnu (LAN, Wi-Fi), může heslo offline hádat hrubou silou. V místní síti je to přijatelné riziko (zmírnění
v T-174), ale adresa `http://` mimo místní síť posílá odpověď Digest přes cizí sítě. Nastavení tiskárny proto u takové
adresy zobrazí lokalizované varování.

## Rozsah

### Klasifikace adresy (`core-dev`)
- Čistá funkce bez sítě, např. `PrinterAddressExposure.Classify(Uri)` v Core (`System.Net.IPAddress` je v pořádku,
  nejde o HTTP klienta). „Bez varování“ pro:
  - schéma `https` (jakýkoli host),
  - loopback (`127.0.0.0/8`, `::1`, `localhost`),
  - RFC 1918 (`10/8`, `172.16/12`, `192.168/16`),
  - link-local IPv4 `169.254/16`, IPv6 `fe80::/10`,
  - IPv6 ULA `fc00::/7`,
  - IPv4 mapované do IPv6 se vyhodnotí jako IPv4,
  - jména mDNS `*.local`.
- Varování pro `http` se vším ostatním (veřejné IP, plně kvalifikovaná jména mimo `.local`), s výjimkami podle otázek
  1 a 4.
- V App už existuje podobná `LocalNetworkAddress` (T-097, oprávnění „Místní síť“ na macOS), ale s jinými pravidly
  (loopback = ne-místní, jednoslovná jména = místní, multicast). Sdílet logiku jen tehdy, když to nezmění chování T-097;
  jinak samostatná funkce (rozhodne implementace, zapsat do Poznámek).

### UI (`ui-dev`)
- `src/LayerPeek.App/Settings/SettingsViewModel.cs` (podzáložka Tiskárna): pod polem adresy text varování, když
  klasifikace vrátí varování; aktualizuje se při psaní adresy (stejně jako dnešní validace).
- Text: heslo (odpověď přihlášení) jde přes síť, které aplikace nemůže věřit; doporučení `https` přes reverzní proxy
  nebo VPN, odkaz na `docs/` z T-174 jen podle otázky 3. Nový text ve **všech** `Strings.*.resx` (en, cs, de, es, fr,
  it, pl, sk).
- Vzhled podle T-112 (zvýraznění pole a přístupný stav), ale jako varování, ne chyba, pokud otázka 2 nedopadne jinak;
  přístupné pro čtečky obrazovky (`AutomationProperties`).
- Varování neblokuje uložení, pokud otázka 2 nedopadne jinak.

## Akceptační kritéria
- [ ] Unit testy klasifikace (Core): `http://192.168.1.5`, `http://10.0.0.1`, `http://172.31.255.255`,
      `http://169.254.1.1`, `http://[fe80::1]`, `http://[fd12::1]`, `http://[::ffff:192.168.1.5]`,
      `http://127.0.0.1:18080`, `http://localhost`, `http://prusa-mk4.local` → bez varování;
      `http://172.32.0.1`, `http://8.8.8.8`, `http://[2001:db8::1]`, `http://printer.example.com` → varování;
      `https://8.8.8.8`, `https://printer.example.com` → bez varování; jednoslovné jméno a CGNAT `100.64/10` podle
      odpovědí na otázky 1 a 4
- [ ] Klasifikace nedělá žádný DNS dotaz ani síťový přístup (pokud otázka 1 nedopadne jinak)
- [ ] Okno nastavení zobrazí varování u adresy s varováním a skryje ho po opravě adresy (headless UI test
      `LayerPeek.App.UiTests`)
- [ ] Text varování ve všech `Strings.*.resx` (`Strings.resx` + cs, de, es, fr, it, pl, sk), přeložený, ne anglický
      zástupný text
- [ ] Referenční snímky UI nastavení (Windows) aktualizované, pokud se změnily; macOS snímky → přesunout do T-127
- [ ] Pokud `docs/prusalink-security.md` (T-174) už existuje, zmiňuje varování; jinak to T-174 doplní
- [ ] `docs/architecture.md`: kde leží klasifikace
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. **Jména přeložená přes DNS** (např. `prusa-mk4` bez tečky nebo `printer.home.arpa`, `printer.lan`, `printer.fritz.box`):
   (a) nepřekládat a rozhodnout jen podle textu (jednoslovná jména a vybrané místní přípony jako `.lan`, `.home.arpa`
   brát jako místní, ostatní jako varování), (b) přeložit přes DNS a rozhodnout podle výsledné IP (síťový přístup
   v nastavení, zpoždění, výsledek se může měnit), (c) varovat u všech jmen kromě `.local`. Rozhodne uživatel.
2. **Blokující, nebo informativní?** Doporučení architekta: informativní (uložení povoleno), protože jde o vědomou
   volbu uživatele (např. VPN). Varianta: potvrzovací dialog při uložení.
3. **Kde se varování zobrazí:** jen v nastavení tiskárny, nebo i v okně Diagnostika / v tooltipu tray menu? A má text
   odkazovat na dokumentaci T-174 (odkaz na GitHub)?
4. **Další rozsahy bez varování?** CGNAT `100.64.0.0/10` (Tailscale a jiné VPN) – šifrovaný tunel, ale také rozsah
   poskytovatelů; přidat, nebo varovat?
5. **Kamera:** adresa RTSP kamery může obsahovat jméno a heslo (`rtsp://user:pass@…`, RTSP bez šifrování). Má se
   stejné varování ukázat i na podzáložce Kamera, nebo je to samostatný úkol?

## Poznámky k implementaci
(vyplní agent po dokončení)
