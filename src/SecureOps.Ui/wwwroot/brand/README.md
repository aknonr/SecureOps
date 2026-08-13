# Brand assets

Everything here is a **placeholder**, drawn locally and deliberately generic. Nothing in this folder
is a corporate logo, wordmark, or third-party asset, and per `docs/20-ui-visual-design-contract.md`
§7 none may be added without approval.

## Files

| File | Used by | Notes |
|---|---|---|
| `secureops-mark.svg` | App bar, `/login`, `/signed-out`, `/session-expired` | Referenced by `<use href="brand/secureops-mark.svg#mark">`. Inherits `currentColor`, so it follows the theme. |
| `favicon.svg` | Browser tab (modern browsers) | Standalone with baked-in colours. Favicons receive no CSS and cannot resolve cross-file `<use>`. |
| `favicon.ico` | Browser tab (fallback), `/favicon.ico` probes | 16/32/48 px PNG payloads in one ICO container. |

The application icon is a **red plate with a white glyph** — the brand red, which appears nowhere in
page content, so the tab is identifiable among a row of internal tools without competing with any
operational status colour. The 16 px entry drops the shield outline and keeps only the check: at that
size a 2 px stroke closes into a blob, and the plate already carries the identity.

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

`Shared/SecureOpsTheme.cs` is the single source of truth for colour. The brand red lives in its
`Tertiary` slot (`#B81D2B` light, `#E05263` dark) and reaches CSS as `--so-brand`; the shell's dark
navy is `AppbarBackground` (`#14233F`).

The icon files cannot read those variables, so `#B81D2B` is baked into `favicon.svg` and into the
generator that produces `favicon.ico`. **If the palette changes, update `favicon.svg` by hand and
regenerate the ICO** — nothing does this automatically, and it is not part of the build:

```powershell
powershell -File src/SecureOps.Ui/build/make-favicon.ps1
```
