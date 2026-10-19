# Theme Manager for Jellyfin

![Theme Manager](logo.png)

Seasonal and permanent themes for Jellyfin Web. Each user picks their own theme (or follows the server default or a calendar); library cards get themed banners, including a generic banner with the library's name for any library. Server branding, Custom CSS and library images are not changed.

**Status:** preview alpha 0.1.0.0, for **Jellyfin 12.2** only (other versions are not built or tested).

## Install

1. Jellyfin → Dashboard → Plugins → Repositories → add
   `https://raw.githubusercontent.com/KsH1/jellyfin-theme-manager/main/repository/manifest.json`
2. Catalog → **Theme Manager** → Install.
3. Optional, to show themes in the normal Jellyfin Web address: add the repository `https://www.iamparadox.dev/jellyfin/plugins/manifest.json` and install **File Transformation**. Without it, themes are at `<your server>/web/theme-manager.html`.
4. **Stop and start Jellyfin completely.** The dashboard's Restart keeps already loaded plugin DLLs, so a plugin update needs a full stop/start.
5. Dashboard → Plugins → Theme Manager to choose the server default, make calendars and import your own themes. Users choose their own theme with the **Themes** button in Jellyfin Web.

**Your own themes and calendars:** see [docs/CUSTOM-THEMES.md](docs/CUSTOM-THEMES.md), with a ready-to-import example in [`examples/sample-aurora.zip`](examples/sample-aurora.zip).

The plugin ships 29 standard themes (`default-packs/`): 19 calendar themes with artwork (months and holidays) and 10 colour themes. They are copied into the server's theme folder on first start. A pack that already exists is never overwritten, and a standard pack an administrator deletes is not brought back. TV and mobile apps with their own interface do not show personal themes.

To rebuild the standard packs from full-size PNGs: `python scripts/convert_packs_to_webp.py` (needs Pillow).

---

## Features

- A personal theme per user: follow the server default, Automatic Seasonal, a fixed theme, a calendar, or original Jellyfin. Saved per Jellyfin account.
- Automatic Seasonal: monthly themes plus holidays (New Year, Valentine's Day, Fastelavn, Easter, Midsummer, Halloween, Christmas); Easter-based dates are calculated each year.
- Custom calendars: shared calendars made by an administrator, and one personal calendar per user.
- Library banners on the home screen for every library, with a generic banner showing the library's name where a theme has no specific banner.
- Theme picker with category tabs, search and live preview; previews use small copies made by the server.
- Theme packs as data only (`theme.json` + images): no scripts or CSS are accepted from uploads.
- Fail-safe: a broken pack or plugin error never stops Jellyfin; the reason goes to the server log.

## Build and test

Requirements: .NET SDK 10 and PowerShell 7 for the scripts.

```powershell
dotnet restore ThemeManager.slnx --locked-mode
dotnet test ThemeManager.slnx -c Release --no-restore
node --test tests/client/core.test.mjs
.scriptsBuild-Package.ps1                      # tests, builds, artifacts/build-<time>/
.scriptsTest-IsolatedServer.ps1                # throw-away Jellyfin on loopback port 18196
.scriptsTest-DefaultPacks.ps1 -Package artifactsuild-<time>
.scriptsTest-WebInjection.ps1 -FileTransformationDir <unzipped File Transformation>
.scriptsBuild-Repository.ps1 -Package artifactsuild-<time> -Changelog '...'   # repository/manifest.json + ZIP
```

The test scripts start the installed Jellyfin binary with new, empty data folders under `test-server/` (ignored by git). They never touch a running server on port 8096/18096. The version is set only in `Directory.Build.props`; bump it before `Build-Repository.ps1`, which never overwrites a published version.

## Layout

| Part | Location |
| --- | --- |
| Plugin, API, admin page and web client | `src/Jellyfin.Plugin.ThemeManager` |
| Catalog, calendar, preferences, pack install | `src/ThemeManager.Core` |
| Tests | `tests/ThemeManager.Tests`, `tests/client` |
| Standard themes shipped with the plugin | `default-packs` |
| Examples | `examples` |
| Jellyfin plugin repository | `repository` |
| Docs (status, plan, pack format) | `docs` |

On a server, packs live in `<data>/theme-manager/packs/<id>/`, user choices in `<data>/theme-manager/users/`, and server settings in Jellyfin's own plugin configuration.

All API routes start with `/ThemeManager` and require a Jellyfin login; `Admin/*` requires an administrator. A user's identity always comes from their session, never from a user id in the request, and API keys cannot act as a personal user.

## Licence

- Code: GPL-3.0-or-later, see [`LICENSE`](LICENSE).
- Artwork in `default-packs/` and `examples/`: AI-generated, released under CC0 1.0 (no rights reserved), see [`ARTWORK-LICENSE.md`](ARTWORK-LICENSE.md).
