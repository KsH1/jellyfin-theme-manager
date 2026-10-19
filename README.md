# Theme Manager for Jellyfin

Seasonal and permanent themes for Jellyfin Web. Each user picks their own theme (or follows the server default or a calendar); library cards get themed banners, including a generic banner with the library's name for any library. Server branding, Custom CSS and library images are not changed.

**Status:** preview alpha 0.1.0.0, for **Jellyfin 12.2** only (other versions are not built or tested).

## Install

1. Jellyfin → Dashboard → Plugins → Repositories → add
   `https://raw.githubusercontent.com/KsH1/jellyfin-theme-manager/main/repository/manifest.json`
2. Catalog → **Theme Manager** → Install.
3. Optional, to show themes in the normal Jellyfin Web address: add the repository `https://www.iamparadox.dev/jellyfin/plugins/manifest.json` and install **File Transformation**. Without it, themes are at `<your server>/web/theme-manager.html`.
4. **Stop and start Jellyfin completely.** The dashboard's Restart keeps already loaded plugin DLLs, so a plugin update needs a full stop/start.
5. Dashboard → Plugins → Theme Manager to choose the server default and import theme packs (ZIP, see `docs/THEME-PACKS.md`). Users choose their own theme with the **Themes** button in Jellyfin Web.

The plugin ships 29 standard themes (`default-packs/`): 19 calendar themes with artwork (months and holidays) and 10 colour themes. They are copied into the server's theme folder on first start. A pack that already exists is never overwritten, and a standard pack an administrator deletes is not brought back. TV and mobile apps with their own interface do not show personal themes.

To rebuild the standard packs from full-size PNGs: `python scripts/convert_packs_to_webp.py` (needs Pillow).

---

## Dansk udviklingsdokumentation

Første fungerende **preview-alpha, 0.1.0.0**, til Jellyfin **12.2.0 / .NET 10**. Dokumenteret 19. oktober 2026. Aktuel status står i `docs/STATUS.md`; dele af teksten herunder er fra før installationen på produktion.

## Hvad virker nu?

- Almindeligt Jellyfin-serverplugin med administrationsside under pluginindstillingerne.
- Temakatalog med validering, lokale billeder, farver, arv og ZIP-import uden genkompilering.
- 19 kalenderpakker med de eksisterende baggrunde og alle 57 engelske bibliotekbannere.
- 10 permanente **farvepresets**: Cinematic Night, Deep Space, Epic Fantasy, Anime City, Nordic Noir, Retro Neon, Cozy Evening, Nature Escape, Minimal Dark og Ocean Blue. Nye illustrationer til disse er **ikke** lavet endnu.
- Kalender med månedsvalg og mærkedage. Nytår: 30. december–2. januar inklusive. Påske og fastelavn beregnes; Europe/Copenhagen er standardtidszone.
- Servervalg: Automatic Seasonal, et fast tema eller Original Jellyfin.
- Brugerpræferencer: Follow server default, Automatic Seasonal, fast tema eller Original Jellyfin. Gemmes pr. Jellyfin-bruger og bevares på tværs af genstart.
- Administratorens side kan forhåndsvise billeder/farver og gemme administratorens eget personlige valg.
- Almindelige brugere har en beskyttet API til deres eget valg. **Den almindelige brugers temavælger i Jellyfin Web er næste udviklingstrin.**
- Indbygget Jellyfin-opgave genindlæser og validerer kataloget ved opstart og hver sjette time. Det aktuelle kalenderresultat beregnes ved hvert API-kald; opgaven ændrer ikke global branding.

## Bevidste begrænsninger

Dette er fundamentet, ikke den færdige temaløsning i planen:

- Ingen automatisk indsættelse af JavaScript eller ændring af Jellyfins webfiler.
- Ingen personlig styling af forsiden endnu, og ingen påstand om understøttelse i native TV-/mobilklienter.
- Ingen overtagelse af Custom CSS, splashscreen eller fælles biblioteksbilleder endnu. Derfor kan pluginets `Original`-valg endnu ikke fjerne det gamle scripts styling.
- Ingen eksport, afinstallation/opdatering af temapakker, katalogets aktiver/deaktiver-funktion eller branding-backup/gendannelse endnu. ZIP-import overskriver aldrig en eksisterende pakke.
- Der er ikke udgivet et offentligt plugin-repository. Kompatibilitet er kun afprøvet med den lokalt installerede 12.2.0-server.
- Dashboardets HTML leveres og dets JavaScript er syntakskontrolleret. Visuel browser-/mobiltest er stadig en udgivelsesopgave.

## Hvorfor denne opbygning?

Strukturen følger [Jellyfins officielle pluginskabelon](https://github.com/jellyfin/jellyfin-plugin-template): `BasePlugin<T>`, `IHasWebPages`, indlejret konfigurationsside og serverens egne konfigurationsfiler. Registrering af tjenester sker gennem `IPluginServiceRegistrator`; katalogopgaven bruger Jellyfins `IScheduledTask`.

Pakkeversionerne er låst til 12.2.0 med `ExcludeAssets="runtime"`. Distributionen indeholder kun vores to assemblies, aldrig egne kopier af Jellyfin/.NET. Kalender og filvalidering ligger i en separat kerne, så de kan testes uden en server. Ingen produktions-API-nøgle er indlejret i kode, dokumentation eller pakke.

Forskellige brugeres temaer kan ikke implementeres sikkert ved at skrive forskellige værdier til serverfælles CSS/biblioteksbilleder. Det næste lag er derfor en særskilt Jellyfin Web-integration. Den må ikke ændre fælles billeder, når én bruger vælger et personligt tema. Se [udviklingsstatus](docs/STATUS.md).

## Byg og test

Krav: .NET SDK 10 og PowerShell 7 til scripts. Kør fra `theme-manager`:

```powershell
dotnet restore ThemeManager.slnx --locked-mode
dotnet build ThemeManager.slnx -c Release --no-restore
dotnet test ThemeManager.slnx -c Release --no-restore
```

Første restore kræver adgang til NuGet. Afhængigheder gemmes lokalt i `.packages`, og lock-filerne skal versionsstyres. `TreatWarningsAsErrors` er slået til. Kildekodeprojektet ligger endnu ikke i et Git-repository.

Forbered billederne fra den eksisterende opsætning én gang:

```powershell
.\scripts\Import-ExistingArtwork.ps1
```

Scriptet kopierer kun; det udfører ikke den gamle temaopgave og læser ikke dens nøgle. Det afviser en eksisterende destinationsmappe for ikke at overskrive tilpasninger. De allerede genererede pakker ligger i `ThemePacks`.

Integrationstest på denne Windows-maskine:

```powershell
.\scripts\Test-IsolatedServer.ps1
```

Testen starter den installerede Jellyfin-binær med nye, tomme databaser, nye testbrugere og kopierede pakker i `test-server/run-<id>`. Kun loopback-port 18196 bruges; 18096 og produktionsbibliotekerne røres ikke. Den stopper sin egen proces i `finally`. Testdata er lokale, ignoreres af Git og er ikke med i udgivelsen. Logfiler kan indeholde irrelevante værtsadvarsler om blokerede opdateringschecks/DPAPI i sandboxen; de er ikke erklæret fejlfrie.

Pak pluginet:

```powershell
.\scripts\Build-Package.ps1
```

ZIP og SHA-256 findes i en ny `artifacts/build-<tid>`-mappe. Billedpakker er separate og medtages ikke i plugin-ZIP'en.

## Manuel installation — kun på en testserver indtil videre

1. Stop den valgte **testserver** normalt.
2. Udpak plugin-ZIP'en i `<Jellyfin data>/plugins/ThemeManager_0.1.0.0/`.
3. Kopiér mapperne fra `ThemePacks/` til `<IApplicationPaths.DataPath>/theme-manager/packs/`. På den afprøvede Windows-opsætning svarer det til `<Jellyfin data>/data/theme-manager/packs/`.
4. Start testserveren. Kontrollér at **Theme Manager 0.1.0.0** står som aktiv.
5. Åbn pluginets indstillinger og vælg en forhåndsvisning. Den normale forside er uændret i denne alpha.

Ekstra temaer kan importeres som ZIP på administrationssiden. Format og sikkerhedsgrænser: [THEME-PACKS.md](docs/THEME-PACKS.md).

For at fjerne alphaen: stop testserveren og flyt dens pluginmappe ud af `plugins`. Behold `data/theme-manager` og pluginets XML-konfiguration, hvis præferencer og temaer skal kunne genbruges. Alphaen har ikke ændret mediefiler eller global branding, så der er ingen sådan ændring at rulle tilbage.

## Filer og API

| Del | Placering |
| --- | --- |
| Plugin/administrationsside | `src/Jellyfin.Plugin.ThemeManager` |
| Kalender, temaer og brugerlagring | `src/ThemeManager.Core` |
| Automatiske enhedstests | `tests/ThemeManager.Tests` |
| Build/import/integrationstest | `scripts` |
| Eksempelpakke | `examples/sample-ocean` |
| Pakker ved drift | `<DataPath>/theme-manager/packs/<id>/theme.json` |
| Brugervalg ved drift | `<DataPath>/theme-manager/users/<user-guid>.json` |
| Servervalg | Jellyfins egen plugin-XML-konfiguration |

Alle API-ruter starter med `/ThemeManager`. `Catalog`, `Themes/{id}/Assets/{slot}`, `Me` og `Effective` kræver login. `Me` kan ikke målrettes en vilkårlig bruger-ID: identiteten kommer fra Jellyfins autentificerede session. API-nøgler må ikke bruges som en personlig bruger. `Admin/Settings`, `Admin/Reload` og `Admin/Import` kræver Jellyfins administratorpolitik. Tokens sendes i en autorisationsheader, aldrig i billed-URL'er.

## Licens

Pluginets kildekode: GPL-3.0-or-later, se `LICENSE`. Denne licenserklæring tildeler ikke rettigheder til tredjepartsfigurer, logoer eller illustrationer i private billedpakker. De eksisterende personlige billeder distribueres derfor separat og er ikke en del af kodepakken.
