# Development status

Updated 19 October 2026 for version 0.3.2.0 (preview). User documentation is in the [README](../README.md) and [CUSTOM-THEMES.md](CUSTOM-THEMES.md).

## Supported

- Jellyfin 12.0, 12.1 and 12.2, one build per version (`targetAbi` 12.0.0.0, 12.1.0.0, 12.2.0.0), .NET 10.
- Installed from the plugin repository `repository/manifest.json` on GitHub. Plugin ZIPs are GitHub release assets. Versions before 0.3.0 were removed from the manifest and from git history on 19 October 2026; installed servers update to 0.3.0 or later.
- Jellyfin 10.x is not supported.

## Built

| Area | What |
|---|---|
| Themes | 29 standard packs (19 calendar themes with artwork, 10 colour themes), seeded once on first start. Data-only packs: `theme.json` plus PNG/JPEG/WebP. |
| Choice | Per user: follow server default, Automatic Seasonal, fixed theme, calendar, or original Jellyfin. Saved per Jellyfin account. |
| Calendars | Shared calendars (administrator) and one personal calendar per user. Holidays can be left out of Automatic Seasonal; Fastelavn and Midsummer start left out outside Denmark. |
| Web client | Background, palette and library banners in Jellyfin Web; theme picker with tabs, thumbnails, search and preview. At the normal address with File Transformation, otherwise at `/web/theme-manager.html`. |
| Library banners | Themed banner for one library per type, generic banner with the library name for the rest. Choice per library: any theme banner, generic, or the library's own image. |
| Apps | Optional library images for TV and mobile apps from the server default, with backup and exact restore. Off by default. |
| Effects | Snow, leaves, hearts, confetti. Off by default; users can switch them off; never during playback, on admin pages or with reduced motion. |
| Theme management | Import, replace (old version kept), hide from the picker, export as ZIP, delete (moved to `theme-manager/deleted-packs`). |
| Status | Settings page shows web-client state, versions, and when a newer installed version needs a full restart. |
| Release | GitHub Actions workflow on tag `v*`: tests, builds for all three Jellyfin versions, creates the release and commits the manifest. |

## Verified

Every test below starts its own Jellyfin with empty data on a loopback port; none touches a running server.

| Test | Result (19 October 2026) |
|---|---|
| Unit tests (`dotnet test`) | 102 passed |
| Client tests (`node --test tests/client/core.test.mjs`) | 30 passed |
| `Test-IsolatedServer.ps1` (API, access rules, import, theme management, previews, holidays, effects default) | 67 checks passed on 12.2 |
| `Test-ServerImages.ps1` (app images, per-library banners, restore) | 22 checks passed on 12.2 |
| `Test-WebInjection.ps1` (File Transformation, stale browser cache) | 25 checks passed on 12.2 |
| `Test-DefaultPacks.ps1` | 9 checks passed |
| `Test-CatalogInstall.ps1` (install from the published repository) | 0.3.0.0 from the GitHub release: 7 checks passed on 12.2. Earlier versions also passed on 12.0; 12.1 confirmed on a real 12.1 server |

Jellyfin Web's new layout (MUI app bar, menus, dialogs) was checked page by page in a real browser on 19 October 2026: home, library, item details, search, favourites, settings, Live TV, queue, the item menu and the user menu. The surfaces found are listed in a client test.

Not verified: Linux and Docker hosts, Firefox and Safari, two users in parallel browsers, keyboard navigation and contrast across the whole picker, native apps beyond library images.

## Open

1. Artwork for the 10 colour themes (needs an image tool).
2. Jellyfin 10.11 support, only if there is demand.
