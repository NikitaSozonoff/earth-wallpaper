# Privacy and network behavior

Earth Wallpaper has no user account, analytics, advertising or behavioral telemetry.

## Network requests

The application contacts its configured public Cloudflare R2 endpoint to:

- read the selected collection manifest and catalog;
- calculate available content changes and download size;
- download missing image assets after confirmation or according to the selected automatic-update mode.

Stage 6 will add an optional request to GitHub's public Releases API to check for application updates.

## Local data

The application stores these items under the current user's local application-data directory:

- widget and update preferences;
- downloaded wallpaper assets and catalogs;
- resumable partial downloads;
- operational logs retained for up to 14 days.

Logs include timestamps, release versions, counts, byte totals and exception types. They do not include image contents, credentials or authorization headers. Logs are not uploaded automatically.

Windows may retain its own cached copy of an image after it has been set as the desktop wallpaper. That operating-system copy is outside Earth Wallpaper's cache management.
