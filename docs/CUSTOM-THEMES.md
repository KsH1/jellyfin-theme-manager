# Make your own themes and calendars

This guide covers two things:

1. **A theme**: a background, banners for the library cards and a set of colours, uploaded as a ZIP.
2. **A calendar**: your own "Automatic Seasonal" that decides which theme shows on which dates. It is made in the browser; no files needed.

Everything here works in Theme Manager 0.2.0.0 and later; hiding, exporting, deleting and replacing themes need 0.3.0.0. It was tested on Jellyfin 12.2. The full field reference is in [THEME-PACKS.md](THEME-PACKS.md) (Danish).

## Part 1 — A theme

### What a theme contains

| File | Used for | Required |
|---|---|---|
| `theme.json` | Name, id, category and colours | Yes |
| background image | Full-page background in Jellyfin Web | No |
| movies image | Banner on the first Movies library card | No |
| tv-shows image | Banner on the first TV Shows library card | No |
| collections image | Banner on the Collections card | No |

A theme with no images at all is a colour theme. It changes the colours but leaves the library cards alone.

Libraries without their own banner automatically get a generic banner: a slice of your background with the library's name on it. This applies to music, books, home videos and second movie libraries. So you do not need a banner for every library.

When there is more than one library of a type, the themed banner goes to the library whose name matches the type, such as "Movies", "Film" or "TV Shows". The others get the generic banner.

### Image tips

- **Formats:** PNG, JPEG or WebP. WebP at quality 85–90 is about a tenth of a PNG's size with no visible difference.
- **Size:** 16:9, for example 1672 × 941 like the standard themes, or 1920 × 1080. Banners are shown small, so keep the title text large and centred.
- **Limits:** each file at most 20 MB. The ZIP at most 32 MB, at most 128 MB unpacked, and at most 64 files.
- **No previews needed:** the server makes small copies for the theme picker by itself.
- Titles inside the images ("MOVIES") are not translated. Use the language your users read.

### Step 1: make the folder

```text
my-theme/
├── theme.json
├── background.webp
├── movies.webp
├── tv-shows.webp
└── collections.webp
```

### Step 2: write theme.json

This is the included example, [`examples/sample-aurora/theme.json`](../examples/sample-aurora/theme.json):

```json
{
  "schemaVersion": 1,
  "id": "sample-aurora",
  "name": "Sample Aurora",
  "version": "1.0.0",
  "author": "Theme Manager example",
  "category": "Custom",
  "palette": {
    "accent": "#5cf0b4",
    "secondary": "#a06cff",
    "surface": "#0c1a28",
    "text": "#f2fffb",
    "background": "#06101c"
  },
  "assets": {
    "background": "background.webp",
    "movies": "movies.webp",
    "tvShows": "tv-shows.webp",
    "collections": "collections.webp"
  }
}
```

| Field | Rule |
|---|---|
| `schemaVersion` | Always `1`. |
| `id` | Permanent and unique: lowercase letters, digits and single hyphens, e.g. `my-theme`. Users' saved choices refer to it, so do not change it later. |
| `name` | What users see, up to 100 characters. |
| `version` | Numbers only, e.g. `1.0.0` (not `1.0-beta`). |
| `category` | Picks the tab in the theme picker: `Permanent` (year-round), `Calendar` (months and holidays) or `Custom`. |
| `palette` | Optional. Up to five colours as `#RRGGBB`: `accent` (buttons), `secondary`, `surface` (header and menu), `text` and `background`. Missing colours get defaults. |
| `assets` | Optional. Slots `background`, `movies`, `tvShows`, `collections`. Every file you name must exist in the ZIP. `splash` and `thumbnail` are accepted but not shown anywhere yet. |
| `extends` | Optional. The id of an installed theme to inherit images and colours from; your own fields win. |
| `calendar` | Optional. Makes the theme part of the built-in "Automatic Seasonal". Most people should use a calendar (Part 2) instead. |
| `effect` | Optional. A seasonal effect in Jellyfin Web: `snow`, `leaves`, `hearts`, `confetti` or `none`. Effects only show after an administrator switches them on; users can switch them off in the theme picker, and they never show when the system asks for reduced motion. |

Unknown fields are rejected, so a typo gives a clear error instead of being ignored. Scripts, CSS, HTML, SVG and web links are not allowed in a theme.

### Step 3: zip it

Zip the **contents** of the folder, not the folder itself. `theme.json` must be at the top of the ZIP.

- **Windows:** open the folder, select all files, right-click → Send to → Compressed (zipped) folder.
- **PowerShell:** `Compress-Archive -Path 'my-theme\*' -DestinationPath my-theme.zip`
- **macOS/Linux:** `cd my-theme && zip ../my-theme.zip *`

A ready-made ZIP to try first: [`examples/sample-aurora.zip`](../examples/sample-aurora.zip).

### Step 4: upload it

1. Jellyfin → Dashboard → Plugins → **Theme Manager**.
2. Under **Theme packs**, choose the ZIP and click **Import ZIP**.
3. The theme is ready straight away; no restart is needed. It appears in the theme gallery on the same page and in every user's **Themes** button, under the tab for its category.

If the import fails, the page shows the reason, for example a missing file, a wrong colour value or an id that already exists. Nothing is installed when an import fails.

### Change, hide or remove a theme

Under **Theme packs → Installed themes** on the plugin page, each theme has:

- **In picker:** untick it to hide the theme from the theme picker. Users who already chose it keep it, and calendars still use it.
- **Export:** downloads the theme as a ZIP that can be edited and imported again.
- **Delete:** removes the theme. It cannot be deleted while it is the fixed server default, or while another theme builds on it with `extends`. Users who had chosen it get Automatic Seasonal until they choose again.

To update a theme, import a new ZIP with the same `id` and tick **Replace an installed theme with the same id**. If the new version is invalid, the old one stays.

Deleted and replaced versions are not thrown away. They are moved to `theme-manager/deleted-packs/` in Jellyfin's data folder:

- Windows: `C:\ProgramData\Jellyfin\Server\data	heme-manager\`
- Linux packages: typically `/var/lib/jellyfin/data/theme-manager/`
- Docker: typically `/config/data/theme-manager/`

To bring one back, move its folder to `packs/<id>/` and click **Refresh catalog**. The browser picks up changed images by itself, because each image's address includes a fingerprint of the file.

You can also put a theme folder directly in `theme-manager/packs/` instead of importing a ZIP, then click **Refresh catalog**. The folder name must equal the `id`.

## Part 2 — A calendar

A calendar is a list of dates, each with a theme, plus a theme for all other days. It repeats every year. There are two kinds:

- **Shared calendars:** made by an administrator. They can be the server default, and every user can choose them.
- **A personal calendar:** each user can make one for themselves. Only that user sees it. An administrator can switch this off.

### Make a shared calendar (administrator)

1. Dashboard → Plugins → **Theme Manager** → **Calendars**.
2. In **Calendar**, choose **+ New calendar**.
3. Fill in **Calendar name**, e.g. "Kids calendar", and **Id (permanent)**, e.g. `kids-calendar`. The id cannot be changed later.
4. Click **+ Add date** for each period, or **Start from Automatic Seasonal** to start from the built-in dates. For each date choose:
   - **Theme**.
   - **When:** **Whole month**, **Dates** (from–to, may cross New Year, e.g. 30 Dec–2 Jan), or **Around Easter** (days from Easter Sunday, e.g. −3 to 1 is Maundy Thursday to Easter Monday; it moves with Easter each year).
   - **Priority:** when two dates overlap, the higher priority wins. Use it for a holiday inside a whole month.
5. Choose the theme for **Other days**. With none, those days show original Jellyfin.
6. Click **Show the year** to check which theme shows when, then **Save calendar**.

Example, the calendar tested for this guide:

| Theme | When | Priority |
|---|---|---|
| Halloween | Dates 25 Oct – 31 Oct | 50 |
| Christmas | Whole month December | 10 |
| Easter | Around Easter −3 to 1 | 10 |
| Other days: Sample Aurora | | |

**Show the year** from 20 October 2026 gives:

- Sample Aurora until 24 October.
- Halloween 25–31 October.
- Sample Aurora in November.
- Christmas in December.
- Sample Aurora from 1 January.
- Easter 25–29 March 2027.
- Sample Aurora again after Easter.

### Use a shared calendar

- **As the server default:** under **Server default**, set **Mode** to **Calendar**, choose the calendar, and click **Save server default**. Everyone who follows the server default now gets it.
- **As a user:** **Themes** button → **Theme mode** → choose the calendar under **Calendars** → **Save my theme**.

### Make a personal calendar (any user)

1. In Jellyfin Web, click **Themes** (bottom right).
2. Open **My own calendar**. It is only shown when the administrator allows personal calendars.
3. Add dates and choose a theme for other days, as above.
4. Click **Save my calendar and use it**.

Administrators allow or block personal calendars with **Users may make their own personal calendar** under **Server default**.

### Built-in holidays

The built-in Automatic Seasonal can skip holidays: under **Server default**, untick them in **Holidays in Automatic Seasonal** and click **Save server default**. A skipped holiday shows the month's theme instead; users can still choose it as a fixed theme. Outside Denmark, Fastelavn and Midsummer start unticked. Your own calendars are not affected.

### Limits

- Up to 50 shared calendars, and one personal calendar per user.
- Up to 100 dates per calendar.
- Priority from −1000 to 1000.
- Around Easter: −90 to 90 days.
- Calendar names up to 60 characters.
- Every date must use an installed theme. A date whose theme is later removed is skipped, and the calendar falls back to **Other days**.
- Dates follow the server's calendar time zone (**Calendar time zone** under **Server default**).

## Where themes show

- **Jellyfin Web in a browser.** With the File Transformation plugin it works at the normal address; without it, at `<server>/web/theme-manager.html`. Apps that embed Jellyfin Web may work too, but this is not tested.
- **Not** in TV and mobile apps that have their own interface.
