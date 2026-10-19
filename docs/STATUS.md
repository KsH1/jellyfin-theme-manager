# Udviklingsstatus

Opdateret 19. oktober 2026. Version 0.1.0.0 er en **preview-alpha**.

## Implementeret

- Jellyfin 12.2.0-plugin, .NET 10, officiel pluginlivscyklus, tjenesteregistrering og indlejret administrationsside.
- Isoleret temakerne med enhedstests og låste pakkeversioner.
- Katalogvalidering, arv, kontrolleret ZIP-import, sikre filstier og SHA-256 for aktiver.
- Kalender og server-/brugerpræferencer med selvstændig vedvarende brugerlagring.
- Administrator- og brugeradgang gennem Jellyfins eksisterende login/politikker.
- Administrationsside med galleri/preview, servervalg, eget personligt valg og import.
- 19 komplette kalenderbilledpakker kopieret fra eksisterende aktiver samt 10 nye paletter.
- Seks-timers katalogopgave og opstartsvalidering. Kalenderen evalueres ved forespørgsel.
- Scripts til build, artwork-kopiering og isoleret afprøvning med den rigtige Jellyfin-binær.
- Opt-in Jellyfin Web-klient via `/web/theme-manager.html`: personlig baggrund, palette og bibliotekbannere pr. bruger, samt temavælger ("Themes"-knap) med søgning, forhåndsvisning og gem. Den almindelige `/web/`-indgang og Jellyfins `index.html` ændres ikke.

## Verificeret

- Release-build med nul compileradvarsler og nul fejl.
- 72 beståede enhedstests: egne kalendere (opløsning, prioritet, reservetema, tidslinje, validering, lagring), forhåndsvisningscache, datoer, tidszone, prioritet, personlige valg, persistens, filstier, ugyldige manifester, arvecirkler, ZIP-import og rollback.
- 25 beståede klienttests (`node --test tests/client/core.test.mjs`): kalendereditorens hjælpefunktioner, serverens fejlbeskeder, kategoriopdeling, sessionsidentitet, afvisning af token til fremmed server, paletteinjektion, oprydning ved annullering og skjulte sider (login/afspilning).
- 53 beståede integrationstjek (heraf 14 for egne kalendere og deres rettigheder) på en separat loopback-only Jellyfin 12.2.0-instans: pluginindlæsning, 29 katalogdefinitioner, billeder, indlejret side, brugeradskillelse, administratorrettigheder, opt-in webindgang, uændret normal indgang og `index.html`, reducerede WebP-kopier fra Jellyfins encoder, live ZIP-import/duplikatafvisning, afvisning af API-nøgle som personlig bruger og registreret planlagt opgave. Se `test-server/run-48c846f73123469d9333a3f151c70f38/passed-checks.json`.
- Browserafprøvning (Chromium, Jellyfin Web fra 12.2.0-installationen) som almindelig bruger: temavælger åbner, søger, forhåndsviser Halloween med baggrund og tre bannere, og gemmer. Forsiden viser derefter baggrund og bannere på Movies/TV Shows, også i 375 px mobilbredde. "Original Jellyfin" fjerner alle pluginets attributter og CSS-variabler. Temaknappen vises ikke på login- eller "Server Mismatch"-siden.
- Rettet under browserafprøvning: bibliotekskort blev grå tomme felter, fordi `client.css` overskrev Jellyfins `position: absolute` på `.cardContent` med `relative`, så kortet fik højden 0.

Ikke påstået verificeret: visuelt administrationsdashboard, to samtidige browsere med forskellige brugere, keyboardnavigation/kontrast, Firefox/Safari, native klienter, Linux-vært eller opgradering fra en tidligere pluginversion.

- Temaet vises i den almindelige Jellyfin-adresse `/web/`, når pluginet File Transformation (3.0.2.0 til 12.2) er installeret. `index.html` ændres kun, mens den serveres, aldrig på disken. Det kan slås fra på admin-siden og virker straks uden genstart. Uden File Transformation virker `/web/theme-manager.html` som før. Testet: `scripts/Test-WebInjection.ps1` (24 tjek) og browser. **Ikke installeret på produktion**; det kræver også File Transformation dér.
- "My Media" virker for alle biblioteker (B1). Hvert bibliotek af typerne film, serier og samlinger kan få temaets eget banner. Ved flere af samme type går det til det bibliotek, hvis navn passer til typen (fx "Film" frem for "Børnefilm"); ellers til det første. Alle andre biblioteker, fx musik, bøger, hjemmevideo og ekstra filmbiblioteker, får et generisk banner: et udsnit af temaets baggrund (forskelligt pr. bibliotek) med bibliotekets navn. Navnet vises kun som tekst via CSS `attr()` og fortolkes aldrig som HTML. Temaer med kun en farvepalette ændrer ikke kortene. Browserafprøvet på testserveren med seks biblioteker, på desktop og i 375 px bredde, samt skift mellem Halloween, Ocean Blue og Original.
- **Egne kalendere ("din egen Automatic Seasonal").** Administratoren laver fælles kalendere i administrationssiden; hver bruger kan lave én personlig kalender i temavælgeren (kan slås fra af administratoren). En kalender er en liste af tema + datoregel (hel måned, datointerval, dage omkring påske) med prioritet, plus et tema til øvrige dage (eller Original). "Start from Automatic Seasonal" kopierer den indbyggede kalender som udgangspunkt, og "Show the year" viser hvilke temaer der vises hvornår, før man gemmer. Fælles kalendere kan vælges som serverstandard og af alle brugere; en personlig kalender kan kun bruges af sin ejer og aldrig som serverstandard. En kalender, der er serverstandard, kan ikke slettes; sletter en bruger sin egen kalender, går brugeren tilbage til serverstandarden. Browserafprøvet: admin opretter "Børnekalender" ud fra Automatic Seasonal med Deep Space i juli og sætter den som serverstandard; en almindelig bruger laver sin egen kalender og ser jul i oktober, mens serverstandarden fortsat gælder for andre. Editoren fungerer i 375 px bredde.
- Temavælgeren er delt i faner: Holidays (7), Months (12), Permanent (10) og Custom. Kun én fane vises ad gangen; vælgeren åbner på fanen med brugerens gemte tema, ellers Permanent. Mærkedage og måneder står i årets rækkefølge. Søgning går på tværs af alle faner. Faner skjules, hvis de er tomme, og kan skiftes med venstre/højre piletast. Browserafprøvet på desktop og i 375 px bredde.
- Små visningskopier: temavælger, administrationsside og bibliotekskort på forsiden henter serverens reducerede, cachebare WebP-kopier i stedet for originalerne (ca. 2–2,7 MB pr. billede). Kun den aktive baggrund på forsiden hentes i fuld størrelse. Enhedstestet med falsk encoder (`PreviewCacheTests`) og integrationstestet med Jellyfins rigtige encoder. CI-definitionen for Windows/Linux er skrevet, men er ikke kørt på GitHub.

## Næste udviklingstrin, i rækkefølge

1. **Afslut Jellyfin Web-klientlaget.** Klientlag og temavælger er bygget og afprøvet for én bruger (se ovenfor). Mangler: to samtidige brugere i hver sin browser, logout/profilskift i samme fane, genberegning ved døgnskifte. Galleriets temakort viser stadig kun farver, ikke billeder.
2. **Resten af browserafprøvningen.** Importfejl, langsomme billeder, tomt katalog, keyboard og kontrast. Registrér Jellyfin Web 12.2.0 eksplicit som understøttet version.
3. **Serverfælles fallback og kontrolleret migration.** Backup og gendannelse af oprindelig branding/biblioteksbilleder; dry-run og konfliktdetektion. Overtag først Windows-opgaven efter godkendt paralleltest. Pluginet må aldrig styre personlige valg via global billedudskiftning.
4. **Ti selvstændige permanente billedsæt.** De eksisterende 10 farvepresets er ikke de færdige nye baggrunde/bannere.
5. **Temapakke-administration.** Aktivering/deaktivering, eksport, opdatering med backup, gendannelse, tydelig håndtering af slettede forældre og brugere. Tilføj ETag/304-cache.
6. **Udgivelse.** Versions-/opgraderingsmatrix, restore-test, fuld accepttest, reproducerbar release-pipeline og et reelt plugin-repository med checksums og mål-ABI. Der er ingen opdigtet download-URL i denne alpha.

## Afvigelser fra den oprindelige plan

- Format 1 accepterer deklarative farver/billeder; vilkårlig CSS og JavaScript er ikke tilladt i uploadede pakker.
- Ingen daglig automatisk billedskrivning kl. 00:10 endnu. Katalogopgaven er ikke en erstatning for den gamle temaopgave.
- Brugerdata er gemt i separate JSON-filer, atomisk pr. opdatering. Filbasen er ikke et offentligt endpoint med vilkårlig bruger-ID.
- Preview-only er en eksplicit udgivelsesgrænse, ikke et fuldført resultat for hele temaplanen.

## Aktive installation

**Installeret på produktion 19. oktober 2026** (se `PROD-INSTALL.md` for filer og tilbageførsel). Pluginet er indlæst med 29 gyldige pakker. Temaet vises kun via `/web/theme-manager.html`; den almindelige `/web/` er uændret. Global branding, biblioteksbilleder og Windows-opgaven `Jellyfin Seasonal Theme` er ikke ændret og kører videre. Genstarten skete via Jellyfins API med temaopgavens eksisterende nøgle, som kun blev læst i hukommelsen. Ingen produktionsnøgle er kopieret ind i projektet.

Mangler og anbefalet rækkefølge for videre udvikling: se `GAP-ANALYSIS.md`.
