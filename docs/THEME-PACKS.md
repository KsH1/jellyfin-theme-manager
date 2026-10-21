# Theme packs — format 1

En pakke er en mappe med `theme.json` i roden og eventuelle lokale PNG-, JPEG- eller WebP-filer. Mappenavnet skal svare præcist til manifestets `id`. ZIP-import forventer selve indholdet i ZIP-roden, ikke en overliggende mappe.

```json
{
  "schemaVersion": 1,
  "id": "my-ocean",
  "name": "My Ocean",
  "version": "1.0.0",
  "author": "Local",
  "category": "Custom",
  "palette": {
    "accent": "#68e0d8",
    "secondary": "#6095df",
    "surface": "#101722",
    "text": "#ffffff",
    "background": "#06151e"
  },
  "assets": {
    "background": "background.webp",
    "collections": "collections.png",
    "movies": "movies.png",
    "tvShows": "tv-shows.png"
  }
}
```

Dette eksempel med billeder er først gyldigt, når de nævnte filer findes. `examples/sample-ocean` er et komplet importérbart **farveeksempel uden billeder**.

## Felter

- `id`: 1–80 tegn, små ASCII-bogstaver/tal adskilt af enkelte bindestreger. Et stabilt ID bruges i gemte brugerpræferencer.
- `name`: ikke-tom visningstekst, højst 100 tegn.
- `version`: numerisk .NET-version, fx `1.0.0` — ikke SemVer-prerelease som `1.0.0-beta` i schema 1.
- `category`: `Calendar`, `Permanent` eller `Custom`.
- `extends`: valgfrit ID på en allerede installeret forælder. Arver billeder og farver, hvorefter lokale felter overskriver. Cirkler og kæder over 16 niveauer afvises.
- `palette`: kun de fem viste felter, kun `#RRGGBB`. Manglende felter får standardfarver eller arves.
- `assets`: valgfri nøgler `background`, `splash`, `thumbnail`, `collections`, `movies`, `tvShows`, `liveTv` (fra 0.3.4). Manglende nøgler arves, ellers mangler billedet. En **erklæret** fil, der ikke findes, gør pakken ugyldig.
- `calendar`: valgfri kalenderregel. Kun pakker med regel indgår automatisk, uanset kategori.

Ukendte JSON-felter afvises, så stavefejl ikke stiltiende ignoreres. Manifestet må højst være 64 KiB og JSON-dybden højst 16.

## Kalender

```json
{ "kind": "Month", "month": 7, "priority": 0 }
```

```json
{ "kind": "Range", "start": "12-30", "end": "01-02", "priority": 100 }
```

```json
{ "kind": "EasterOffset", "startOffset": -3, "endOffset": 1, "priority": 80 }
```

Datoer og offsetgrænser er inklusive. `Range` kan passere nytår. `EasterOffset` bruger gregoriansk påskesøndag. Højeste prioritet vinder; lighed afgøres deterministisk efter ID. Prioritet: −1000 til 1000. Offset: −90 til 90.

De importerede regler bevarer den eksisterende opsætning: nytår 30/12–2/1; valentinsdag 14/2; fastelavn −51 til −49 dage før påske; påske skærtorsdag til 2. påskedag; sankthans 23–24/6; halloween hele oktober; jul 24/11–26/12. Derfor ses det almindelige oktoberbillede kun ved et fast valg, når Halloween har højere prioritet.

## Import og sikkerhed

- Administratorer uploader rå ZIP som `application/zip` til `POST /ThemeManager/Admin/Import` eller bruger administrationssiden.
- Maks. 32 MiB komprimeret, 128 MiB udpakket, 64 ZIP-entries og 20 MiB pr. fil. Billeder skal være mindst 16 byte.
- ZIP må indeholde `theme.json` og de understøttede billedformater; ingen JavaScript, CSS, HTML, SVG eller eksterne URL'er.
- Relative stier bruger `/`. Rodstier, `..`, drevbogstaver, alternative datastrømme, Windows-enhedsnavne, dubletter og links/reparse points afvises.
- Filtype kontrolleres mod signatur/header og filendelse. Dette er **ikke** en fuldstændig billeddekodnings- eller malwareanalyse.
- Filer pakkes først ud i en isoleret stagingmappe. ID-kollision afvises; valideringsfejl ved ny pakke rulles tilbage. Eksisterende pakker overskrives ikke.
- Billeder serveres fra validerede katalogstier, kun efter login, med `nosniff`. Fysiske stier sendes ikke i det almindelige katalog.
- Kataloget leverer SHA-256 pr. billede. HTTP-caching er foreløbig privat/genvalidering; ETag/304-optimering er ikke implementeret.
- Små visningskopier laves automatisk af serveren; pakker skal ikke indeholde dem. `GET /ThemeManager/Themes/{id}/Assets/{slot}/Preview?v={revision}` leverer en kopi højst 1280 px bred (baggrund, splash, thumbnail) eller 640 px (bannere), lavet med Jellyfins egen billedmotor som WebP (JPEG hvis WebP ikke understøttes). Kopien laves én gang pr. billedrevision og gemmes i `<data>/theme-manager/previews`. Med korrekt `v` må browseren cache den permanent. Er originalen allerede lille, eller fejler kodningen, leveres originalen. Forældede kopier slettes af opgaven "Refresh theme packs".

Format 1 er bevidst mere afgrænset end planens skitse med fri CSS: farver og billeder er data, ikke eksekverbar kode. Større CSS-temaer kræver særskilt design og validering af klientlaget.
