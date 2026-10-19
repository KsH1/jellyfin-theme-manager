# Gap-analyse: tilpasning, egne temaer og "My Media"

> **Historical (19 October 2026).** This plan is done or replaced; the current state is in [STATUS.md](STATUS.md). Kept for the reasoning behind decisions.

19. oktober 2026. Grundlag: koden i `src/`, produktionsinstallationen (se `PROD-INSTALL.md`) og de kilder, der er nævnt nederst. Punkter markeret *ikke verificeret* er ikke afprøvet her.

## Kort fortalt

Pluginet kører på produktion, men brugerne ser det kun, hvis de selv åbner `/web/theme-manager.html`. Bibliotekbannere virker kun for tre bibliotekstyper, og alle biblioteker af samme type får det samme banner. Egne temaer kan kun lægges på som en færdig ZIP-fil af en administrator. De tre største mangler er derfor:

1. Temaet vises ikke i den almindelige Jellyfin-adresse.
2. "My Media" understøtter ikke vilkårlige biblioteker.
3. Der er ingen måde at lave et tema i brugerfladen på.

## 1. Hvordan andre gør

| Løsning | Hvad den gør | Relevans |
|---|---|---|
| Jellyfins egen "Rediger billeder" på et bibliotek | Sætter et serverfælles Primary-billede pr. bibliotek. Standard er en tilfældig collage af bibliotekets backdrops. | Den eneste måde, der også virker i TV- og mobilapps. Det er det, Windows-opgaven gør i dag. |
| Cover-makere (Jellyfin Cover Maker, jellyfin-tools, Library Cover Maker) | Laver billeder med bibliotekets navn over en baggrund, som man derefter uploader manuelt. | Viser behovet for genererede bannere med titeltekst til *alle* biblioteker. |
| Skin Manager | Installerer færdige community-temaer (Ultrachromic, JellyFlix m.fl.) som Custom CSS. | Serverfælles CSS, ingen personlige valg og ingen kalender. |
| Jellyfin Seasonals | Kalenderbaserede effekter (sne, hjerter, fyrværkeri) med administratorvalg. Brugeren kan kun slå effekter til eller fra. | Nærmeste konkurrent. Indsprøjter i `index.html` og falder tilbage til File Transformation. Har ingen personlige temaer, egne temaer eller bibliotekbilleder. |
| File Transformation | Lader plugins ændre det, Jellyfin Web leverer (fx `index.html`), mens det serveres, uden at skrive i filerne. | **Version 3.0.2.0 findes til målversion 12.2.0.0** (læst i manifestet den 19. oktober 2026). Det løser hovedmangel 1. |
| Plugin Pages | Lader plugins tilføje egne sider i brugerens menu. | 3.0.2.0 findes til 12.2.0.0. Det kan give temavælgeren et rigtigt menupunkt. |

## 2. Mangler

### A. Synlighed: den almindelige Jellyfin-adresse — lavet 19/10 (se STATUS), ikke på produktion

- **Nu:** Temaet virker kun via `/web/theme-manager.html`. Den normale `/web/` er bevidst uændret.
- **Mangler i koden:**
  - Valgfri registrering hos File Transformation: indsæt `<script src="../ThemeManager/Client/bootstrap.js">` i `index.html`, når den serveres. Det sker via reflection, så File Transformation ikke bliver en hård afhængighed.
  - En administratorindstilling "Aktivér i almindelig web".
  - Tydelig status på admin-siden, hvis File Transformation mangler.
- *Ikke verificeret:* callback-signaturen og kompatibiliteten med 12.2.0 i praksis. Begge skal afprøves på testserveren først.

### B. "My Media" med vilkårlige biblioteker

- **Nu:** `librarySlots` kender kun `movies`, `tvshows` og `boxsets`. På produktion svarer det til Film, Serier og Collections. Musik, bøger, hjemmevideo, musikvideoer, blandede biblioteker og ekstra film-biblioteker får intet banner. To film-biblioteker får det samme banner.
- **Mangler i koden:**
  1. **Generisk banner (lavet 19. oktober 2026, se STATUS):** Hvis der ikke er et særligt billede, bruges temaets baggrund som udsnit med bibliotekets navn over i CSS. Så virker alle biblioteker i alle temaer uden nye billeder. Det er kun en klientændring.
  2. **Flere bannertyper:** Nye pladser i manifestet: `music`, `books`, `homeVideos`, `musicVideos`, `mixed`, `playlists`, `liveTv`, plus en `library`-standard.
  3. **Banner pr. bibliotek:** En administrator kan knytte et bestemt bibliotek (ID) til et banner eller en pakkeplads. Det gemmes i pluginets konfiguration, ikke i pakken, fordi bibliotekernes ID'er er forskellige fra server til server.
- Skema 1 afviser ukendte pladser. Nye pladser kræver enten en udvidelse af skema 1, der stadig kan læse gamle pakker, eller skema 2.

### C. Egne temaer og tilpasning

- **Nu:**
  - Administratorer kan importere en færdig ZIP.
  - Serverstandard og tidszone kan sættes.
  - Brugere kan vælge tema.
  - Import overskriver aldrig eksisterende pakker.
- **Mangler i koden:**
  1. **Temaguide i admin-siden:** navn, basistema, farvevælgere, upload af baggrund og bannere, forhåndsvisning og gem. Serveren bygger pakken, så ingen skal skrive `theme.json` selv.
  2. **Pakkehåndtering:** slet, deaktivér/skjul for brugere, eksportér som ZIP og opdatér med backup. Planens trin 5 er ikke lavet.
  3. **Kategori og navn:** egne temaer bør kunne vælge kategori (fx Permanent), så de lander i den rigtige fane.
  4. **Personlige justeringer (valgfrit):** brugeren kan ændre accentfarve eller sløring af baggrunden oven på et tema. Det gemmes i brugerens præference.
  5. **Brugeroprettede temaer (valgfrit):** almindelige brugere må uploade egne baggrunde. Det kræver kvote, størrelsesgrænse og en administratorindstilling. Det anbefales ikke i første omgang.
- Danske tekster: brugerfladen er kun på engelsk.

### D. Native apps (TV, mobil)

- **Nu:** Native apps ser intet fra pluginet. Windows-opgaven sætter stadig serverfælles branding og biblioteksbilleder med en API-nøgle.
- **Mangler:** En serverfælles fallback, der sætter bibliotekernes Primary-billede efter serverens standardtema, med backup og gendannelse. Herefter kan Windows-opgaven og API-nøglen fjernes. Det er planens trin 3.

## 3. Billeder, der mangler at blive lavet

| Mangler | Antal | Bemærkning |
|---|---|---|
| Baggrunde til de 10 permanente temaer | 10 | I dag er de kun farvepaletter. |
| Bannere (Collections/Movies/TV Shows) til permanente temaer | 30 | Som planen beskriver. |
| Bannere til andre bibliotekstyper | 0–(antal temaer × nye typer) | Kan undgås med generisk banner (B1). Kun nødvendigt, hvis der ønskes særlige motiver. |
| Galleri-miniaturer | 0 | Laves automatisk af serveren (forhåndsvisningscachen). |

Billederne skal laves i et billedværktøj; de kan ikke genereres med koden her.

## 4. Anbefalet rækkefølge

1. **A:** File Transformation, så temaet vises i den almindelige adresse. Det har størst effekt for brugerne. Afprøves på testserveren før produktion.
2. ~~**B1:** Generisk banner med biblioteksnavn~~ — lavet; mangler produktion.
3. **C1 + C2:** Temaguide og pakkehåndtering, så egne temaer kan laves i brugerfladen.
4. **B2/B3:** Flere bannertyper og banner pr. bibliotek.
5. **D:** Serverfælles fallback, så Windows-opgaven kan erstattes.
6. Billeder til de 10 permanente temaer.

## Kilder

- [File Transformation (README)](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation) og [manifest](https://www.iamparadox.dev/jellyfin/plugins/manifest.json), læst 19. oktober 2026.
- [Jellyfin Seasonals](https://github.com/Bioflash257/Jellyfin-Seasonals)
- [Jellyfin Cover Maker](https://github.com/KartoffelChipss/Jellyfin-Cover-Maker), [jellyfin-tools](https://libraries.io/pypi/Jellyfin-Tools)
- [Skin Manager-guide](https://jellywatch.app/blog/best-jellyfin-themes-skin-manager-2026) (tredjepartsguide)
- [Forumtråd om index.html-injektion](https://forum.jellyfin.org/t-there-is-no-way-for-a-plugin-to-hook-or-alter-index-html)
