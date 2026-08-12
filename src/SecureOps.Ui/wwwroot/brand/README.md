# Brand assets

Everything here is a **placeholder**, drawn locally and deliberately generic. Nothing in this folder
is a corporate logo, wordmark, or third-party asset, and per `docs/20-ui-visual-design-contract.md`
§7 none may be added without approval.

## Files

| File | Used by | Notes |
|---|---|---|
| `secureops-mark.svg` | App bar, `/login`, `/signed-out`, `/session-expired` | Referenced by `<use href="brand/secureops-mark.svg#mark">`. Inherits `currentColor`, so it follows the theme. |
| `favicon.svg` | Browser tab (modern browsers) | Standalone with baked-in colours and its own `prefers-color-scheme` rule. Favicons receive no CSS and cannot resolve cross-file `<use>`. |
| `favicon.ico` | Browser tab (fallback), `/favicon.ico` probes | 32×32 PNG inside an ICO container. |

## Replacing with approved corporate assets

The integration points are stable, so an approved asset set drops in without touching page markup:

1. Replace `favicon.svg` and `favicon.ico` in place, keeping the same filenames.
2. Replace `secureops-mark.svg`, **keeping the `id="mark"` symbol** — the app bar and the three auth
   pages reference that id. Keep the artwork on a `0 0 32 32` viewBox.
3. To follow the light/dark theme automatically, keep strokes and fills as `currentColor`. Use fixed
   colours only where the mark must not change tone.
4. If the approved package specifies a wordmark image instead of text, change the `.so-brand-name`
   span in `Shared/MainLayout.razor` and the `.so-auth-brand-text` block in the auth pages.

Approved assets are expected to live under `wwwroot/brand/approved/` when that package is supplied.
Until then, keep the placeholders minimal so replacement stays a file swap rather than a redesign.

## Theme colours

The placeholder mark uses the shell's dark navy `#14233F`, which is `AppbarBackground` in
`Shared/SecureOpsTheme.cs`. That file is the single source of truth for colour; if the palette
changes, update the baked colours in `favicon.svg` and regenerate `favicon.ico` to match.
