# Plan: Theme Manager som installerbart plugin til alle Jellyfin-servere

19. oktober 2026. Målet er, at enhver Jellyfin-administrator kan:

1. tilføje et repository-link i Dashboard → Plugins → Repositories,
2. installere Theme Manager fra kataloget,
3. hente temaer fra et temagalleri med ét klik eller uploade egne,
4. og at brugerne ser temaet i den **almindelige** Jellyfin-adresse.

Intet må være bundet til Kenneths server: ingen faste stier, nøgler, biblioteksnavne eller dansk tidszone som eneste mulighed.

## Udgangspunkt (verificeret)

| Emne | I dag | Problem for andre servere |
|---|---|---|
| Installation | DLL'er kopieres manuelt til `plugins\ThemeManager_0.1.0.0` | Intet repository; ingen opdateringer via Jellyfin |
| Jellyfin-version | Bygget mod 12.2.0 / .NET 10 | Servere på 10.10 (.NET 8) eller 10.11 (.NET 9) kan ikke bruge den |
| Temaer | 29 pakker kopieret manuelt til `data\theme-manager\packs` (209 MB, 7–12 MB pr. pakke) | Følger ikke med pluginet; for store til plugin-ZIP'en |
| Visning | Kun via `/web/theme-manager.html` | Brugerne finder det ikke |
| Tidszone | Standard `Europe/Copenhagen` | Forkert standard uden for Danmark |
| Opdatering af plugin | `POST /System/Restart` genindlæser ikke plugin-DLL'er (målt 19/10) | Jellyfins egen opdatering kræver fuld genstart; skal dokumenteres for brugeren |
| Dokumentation | README siger stadig "ikke installeret" | Forældet |

Teknisk grundlag, som er tjekket mod rigtig kode og rigtige manifester:

- **Repository-format:** `manifest.json` er en liste af plugins (`guid`, `name`, `description`, `overview`, `owner`, `category`, `imageUrl`). Hver plugin har `versions` med `version`, `changelog`, `targetAbi`, `sourceUrl` (ZIP), `checksum` (MD5, 32 hex-tegn), `timestamp` og eventuelt `dependencies`. Én ZIP pr. Jellyfin-målversion. Kilde: File Transformation-manifestet.
- **File Transformation 3.0.2.0** findes til `targetAbi` 12.2.0.0. Integrationen sker fra en opstartsopgave:
  - Opstartsopgaven finder File Transformation-assemblyen via `AssemblyLoadContext.All`.
  - Den kalder `Jellyfin.Plugin.FileTransformation.PluginInterface.RegisterTransformation` med et Newtonsoft-`JObject`: `id`, `fileNamePattern` (`index.html`), `callbackAssembly`, `callbackClass` og `callbackMethod`.
  - Callbacken er `public static string Metode(Payload p)`, hvor `Payload` har `[JsonPropertyName("contents")] string? Contents`.
  - Mangler pluginet, logges en advarsel.
  - Kilde: `StartupService.cs` og `PatchRequestPayload.cs` i Home Screen Sections.

## Faser

### Fase 1 — Almindelig adresse (File Transformation) — **lavet 19/10, ikke på produktion**

Status: `WebInjection.cs` + `IndexInjection.cs`, opgaven "Show theme in Jellyfin Web", indstillingen `ShowInWebClient` og statusboksen "Where users see themes" på admin-siden. Testet med File Transformation 3.0.2.0 (MD5 kontrolleret mod manifestet): `scripts/Test-WebInjection.ps1` (24 tjek i tre scenarier: med, uden og slået fra; `index.html` på disken uændret), `Test-IsolatedServer.ps1` (53 tjek) og browsertest af den almindelige `/web/` med seks biblioteker samt slå fra/til i admin uden genstart. Pluginet bruger nøglen `index.html` ligesom andre plugins, så transformationerne deler pipeline. Det bygger `JObject` ud fra File Transformations egen parametertype, fordi plugins ligger i hver sin load-context.

Oprindelig plan:

1. Ny opstartsopgave `RegisterWebInjectionTask`. Hvis File Transformation er installeret, registreres en transformation af `index.html`, der indsætter `<script defer src="../ThemeManager/Client/bootstrap.js">` før `</head>`. Det gøres kun én gang, selv hvis scriptet allerede er der.
2. Indstilling **"Vis tema i almindelig Jellyfin Web"**: standard til, når File Transformation findes. `/web/theme-manager.html` bevares som reserve.
3. Admin-siden viser status: "Aktiv via File Transformation", "File Transformation mangler – installér fra …" eller "Slået fra".
4. Repository-manifestet angiver File Transformation som afhængighed **kun hvis** det er nødvendigt. Anbefaling: ingen hård afhængighed. Pluginet virker stadig via den særlige adresse.
5. **Test:** isoleret server med og uden File Transformation, med kontrol af at `index.html` på disken aldrig ændres. Derefter browsertest af den almindelige `/web/`.

### Fase 2 — Temaer, der følger med eller kan hentes

Pluginet skal kunne installeres alene og stadig give noget at vælge imellem.

1. **Indbyggede temaer (følger med DLL'en):** de 10 permanente farvetemaer og en "Seasonal Lite"-kalender med farver til måneder og mærkedage. De fylder få KB, så der er altid temaer efter installation.
2. **Temagalleri (hentes):**
   - Et offentligt `themes.json`-indeks med id, navn, kategori, version, forfatter, størrelse, SHA-256, miniature-URL og download-URL til en ZIP pr. pakke.
   - Admin-siden får fanen **"Hent temaer"**: liste med miniaturer, "Installér", "Opdatér" og "Fjern".
   - Download går gennem den eksisterende `ThemePackInstaller`, som allerede validerer stier, størrelser og filtyper. SHA-256 skal matche indekset, ellers afvises pakken.
   - Kun HTTPS og kun det indeks-URL, administratoren har godkendt. Standard er projektets eget indeks.
3. **Mindre pakker:** de nuværende PNG'er (2–2,7 MB pr. billede) konverteres til WebP i 2560 px bredde. Det forventes at give ca. 1–2 MB pr. pakke i stedet for 7–12 MB. Det er ikke målt; det måles i fasen.
4. **Egne temaer:** ZIP-upload findes allerede. Tilføj eksport, slet og opdatér-med-backup (gap-analysens C2). Temaguiden (C1) kommer i en senere fase.
5. **Rettigheder:** billederne er lavet i Codex-sessionen. Før offentlig udgivelse skal det afklares, at de må distribueres. Det er ikke verificeret.

### Fase 3 — Neutrale standarder

1. Tidszone: standard er serverens lokale tidszone (`TimeZoneInfo.Local`), ikke `Europe/Copenhagen`. Eksisterende konfigurationer beholder deres værdi.
2. Kalenderregler: valentinsdag, påske og jul er generelle. Fastelavn og sankthans er danske og bliver valgfrie pakker i galleriet, ikke standard.
3. Brugerflade: engelsk som standard. Danske tekster via Jellyfins sprogindstilling er en senere udvidelse.
4. Biblioteksnavne til bannere: listen dækker allerede engelsk og dansk. Den udvides med de mest brugte sprog, og logikken falder altid tilbage til "første bibliotek af typen".

### Fase 4 — Repository og udgivelse — **delvist lavet 19/10**

Lavet: offentligt repository https://github.com/KsH1/jellyfin-theme-manager (GPL-3) med Jellyfin-manifest `repository/manifest.json` og `repository/theme-manager_0.1.0.0.zip`, lavet med `scripts/Build-Repository.ps1`. Repository-URL: `https://raw.githubusercontent.com/KsH1/jellyfin-theme-manager/main/repository/manifest.json`. Tilføjet på produktion sammen med File Transformations repository; Jellyfins katalog viser Theme Manager 0.1.0.0 @ 12.2.0.0. Mangler: CI via GitHub Actions, Releases i stedet for ZIP i git, og accepttest på en ren server.

Oprindelig plan:

1. **Versioner:** 0.2.0.0 bliver første offentlige version. Hver ZIP indeholder kun pluginets egne DLL'er og `meta.json`; der følger ingen Jellyfin-assemblies med.
2. **Målversioner:** først kun 12.2.0.0, som er den eneste, der er testet. 12.0/12.1 og 10.11 tilføjes kun, hvis de bygges og testes hver for sig (andre NuGet-versioner og .NET-versioner). Uden test lover manifestet dem ikke.
3. **CI (GitHub Actions):**
   - Ved et tag: restore → test → build pr. målversion → ZIP → MD5 → udgiv som GitHub Release.
   - Opdatér `manifest.json` og `themes.json` på GitHub Pages.
   - `build.yml` findes, men har aldrig kørt.
4. **Repository-URL** til Jellyfin: fx `https://<konto>.github.io/jellyfin-theme-manager/manifest.json`.
5. **Accepttest før udgivelse:** en ren Jellyfin 12.2-server tilføjer repository-URL'en, installerer pluginet, genstarter, henter et tema fra galleriet og viser temaet i `/web/`. Derefter testes opdatering fra 0.2.0.0 til 0.2.0.1 via Jellyfins katalog.
6. **Dokumentation:** README omskrives til installationsvejledning for andre (repository-URL, File Transformation, begrænsninger for native apps). Sikkerheds- og privatlivsafsnit: ingen data forlader serveren, undtagen når administratoren selv henter temaer.

### Fase 5 — Kenneths egen server over på den officielle vej

1. Afinstallér den manuelt kopierede version, og installér fra repository'et.
2. Temaerne i `data\theme-manager\packs` bevares; galleriet genkender dem på id og version.
3. Windows-opgaven og API-nøglen fjernes først, når den serverfælles fallback (gap-analysens D) er lavet.

## Rækkefølge og omfang

| Fase | Afhænger af | Omfang |
|---|---|---|
| 1 Almindelig adresse | – | Lille–mellem |
| 2 Temaer | – | Mellem–stor |
| 3 Neutrale standarder | – | Lille |
| 4 Repository | 1, 2, 3 og en offentlig placering | Mellem |
| 5 Egen server | 4 | Lille |

Fase 1, 2 og 3 kan laves uafhængigt af hinanden. Fase 4 kræver en afklaring fra dig.

## Åbne beslutninger

1. ~~Hvor skal repository og temagalleri ligge offentligt?~~ **Besluttet 19/10: GitHub.** Et offentligt repository med Releases (ZIP'er) og GitHub Pages (`manifest.json`, `themes.json`). Mangler stadig: hvilken konto/organisation, repository-navn og licens til kode og billeder.
2. **Hvilke Jellyfin-versioner skal understøttes?** Anbefaling: kun 12.2 i første udgivelse.
3. **Må de Codex-genererede billeder udgives offentligt?** Det skal afklares før fase 2's galleri bliver offentligt.

## Koordinering

Sessionen "Custom Automatic Seasonal" arbejder i samme mappe lige nu. Implementering af fase 1–3 bør vente, til den er færdig, eller ske i filer, den ikke rører.
