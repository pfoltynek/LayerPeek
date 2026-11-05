# Fixture Core One (T-152, T-153)

Deset snímků z kamery Buddy3D na Core One uživatele (tisk 2026-11-03, jen vnitřek tiskárny), původ a obsah viz
[docs/detection-spike.md](../../../../docs/detection-spike.md), oddíl 1.

| Soubory | Obsah |
|---|---|
| `*.jpg` | zmenšené snímky 640×360 ze spiku T-152 (pro prohlížení a pro budoucí detektory) |
| `*.gray` | stejné snímky jako surový šedotón 8 bit **320×180** (57 600 B, řádky shora dolů, bez hlavičky) – vstup testů |

## Jak vznikly soubory `.gray` (T-153, rozhodnutí uživatele 2026-11-04, otázka 7)

Testy nesmí mít dekodér JPEG jako závislost, proto se snímky jednou převedly mimo repozitář nástrojem FFmpeg 9.0.2
(Homebrew, macOS) a výsledek je commitnutý (~0,6 MB):

```sh
for f in *.jpg; do
  ffmpeg -v error -y -i "$f" -vf scale=320:180:flags=area -pix_fmt gray -f rawvideo "${f%.jpg}.gray"
done
```

- `flags=area` = průměr ploch (stejný princip jako zmenšení v hubu, `GrayscaleScaler`).
- JPEG je `yuvj420p` (plný rozsah), `gray` je kanál Y v plném rozsahu 0–255. Průměrný jas snímků je ~70–75, jako
  ve spiku (63–79).
- V `.gitattributes` jsou soubory `*.gray` označené jako binární.

Nové fixture se přidávají stejným příkazem; limit ~10 snímků z rozhodnutí 2026-11-03 platí dál.

## Syntetická selhání (T-155, rozhodnutí uživatele 2026-11-04)

Testy detektoru „Změna na podložce“ z těchto snímků za běhu tvoří syntetické tisky a selhání (`tests/Shared/Detection`:
šest klíčových snímků klipu dokola jako pohyblivá hlava, šum komprese, průběh jasu, špagety, hrouda, posunutý nebo zmizelý
díl). Žádné vygenerované soubory se necommitují. Celá lokální série a klip z `~/LayerPeek-samples` se čtou jen
v `BedChangeLocalSamplesTests` a bez nich se test přeskočí.

