# 20 — UI Visual Design Contract

> **Partially superseded by `docs/25-ui-enterprise-shell.md`.**
>
> Sections **1–5** and **8** describe a presentation demo shell: a three-profile role picker on
> `/login`, "Demo ortamıdır" boundary text, and an in-memory demo team page. That direction was
> retired when the UI moved onto the API platform contracts — identity and permissions now come from
> `GET /api/v1/access/me`, and presenting a role choice would imply the operator selects their own
> authority. See doc 25 for the current rules on login, dashboard, identity lookup, navigation, and
> acceptance.
>
> Section **0** (product story and anti-patterns) and sections **6** (responsive acceptance),
> **7** (branding strategy), and **9** (IIS awareness) remain in force and are extended by doc 25.
>
> The remainder is retained unedited as the record of the presentation gate.

This is a design-review and deployment-awareness gate. It defines the approved visual direction for
the SecureOps internal operations demo UI **before** any visual redesign. No production code, CSS,
configuration, or tests change in this gate. It builds on `docs/17-ui-demo-shell.md`,
`docs/18-ui-demo-shell-handoff.md`, and the proven topology in `docs/19-ui-presentation-acceptance.md`.

## 0. Product Story and Anti-Patterns

SecureOps is an **internal Windows operations** product. The visual language must convey a calm,
audited, operator tool — not a consumer app.

Core product story (the spine every screen serves):

```
Alarm / olay bağlamı  →  PAM / AD kimlik sorgulama  →  audit kaydı  →  (gelecek) salt okunur tanılama
```

The UI must **not** resemble: a generic AI dashboard; a student sample application; a passenger
booking website; or a copied public Turkish Airlines website. No marketing hero, no decorative
imagery, no equal-weight feature grid, no consumer "call to action" styling.

## 1. Product Hierarchy

| Capability | Status | Visual weight |
|---|---|---|
| **PAM / AD Kimlik Sorgulama** | Only primary usable capability | Primary, highest emphasis |
| Takım ve Yetki Yönetimi | Secondary, demo-only | Secondary, muted, demo-labelled |
| Salt Okunur Tanılama | Future-state module | Tertiary, "Planlandı" status only |
| Denetim / Audit | System assurance capability | A supporting status line, **not** a competing dashboard card |

Audit is never presented as a clickable feature card that competes with Kimlik Sorgulama. It appears
as an assurance statement ("API üzerinden kayıt altına alınır").

## 2. Dashboard Information Architecture (`/dashboard`, Operasyon Panosu)

- **Primary action:** a prominent `Kimlik Sorgula` button inside one primary card titled
  `PAM / AD Kimlik Sorgulama`.
- **Compact operational flow strip** (new, single row, read-only, equal small steps):
  `Olay bağlamı → Kimlik sorgulama → Kayıt / audit → Gelecek tanılama fazı`. The current step
  (Kimlik sorgulama) is emphasized; future steps are muted.
- **Secondary phase/status summary:** the existing compact module-status list
  (Kimlik Sorgulama → Kullanıma Açık; Salt Okunur Tanılama → Planlandı; Denetim ve Uyum → audit
  assurance; Takım ve Yetki Yönetimi → Demo / Örnek Veri).
- **Prohibited:** equal-size generic module cards; and any remediation, AI/RAG, execution, restart,
  reboot, recycle, delete, snapshot, or command control.

## 3. Login Design (`/login`)

- Compact, Turkish-first sign-in. No large marketing hero, no background photography.
- **No authenticated navigation** (no drawer/app bar) before sign-in — the login uses `LoginLayout`.
- Three practical demo profiles only: Platform Yöneticisi, Takım Lideri, Salt Okunur Görüntüleyici,
  each with a one-line practical scope and a single `Bu profil ile devam et` action.
- A small **indeterminate** loading affordance with a generic route/flight (operasyon) icon **only
  while a real request is in flight**. No fake percentage, no artificial delay, no progress that is
  not backed by an actual server round-trip.
- Boundary text always visible:
  `Demo ortamıdır. Gerçek AD, PAM, LDAP veya SSO doğrulaması yapılmaz.`

## 4. Identity Lookup Page (`/identity-lookup`, PAM / AD Kimlik Sorgulama)

- **Desktop:** two columns — query form (left) and result panel (right). **Mobile:** single column;
  the form stacks above the result.
- **Required fields:** `Hesap`, `Sorgu amacı`.
- **Optional:** a collapsed `Olay Referansları (İsteğe Bağlı)` section (Alarm ID, Turuncuhat Olay ID).
- **Result states, all explicit and safe:** helpful initial/empty prompt; success (Bulundu);
  not-found (bulunamadı); authorization error (yetki); API unavailable (ulaşılamıyor).
- **No permanent connection-status noise** before an actual query. The provider indicator must be
  quiet/neutral until a real verification or query resolves it; it must not shout "bağlantı yok"
  from a render-time probe.
- **Never expose** raw exceptions, API URLs/ports, stack traces, headers, certificate errors, or
  sensitive/identity payloads in the UI. Technical detail is server-side log only.

## 5. Team Page (`/team`, Takım ve Yetki Yönetimi)

- Compact responsive table (desktop) / card list (mobile breakpoint).
- **No empty side panel before selection.** The detail panel renders **only after** a row is
  selected, and can be dismissed.
- The demo-only warning is always visible:
  `Demo only — gerçek erişim veya yetki değişikliği uygulanmaz.`
- Demo mutation actions (Takım Lideri Yap, Kimlik Kapsamı Ver, Pasife Al, Demo Üye Ekle) are visually
  **secondary** (text/outlined, not primary-filled) and every action clearly states that no real
  account, role, AD, PAM, LDAP, Entra ID, database, or authorization state changes.

## 6. Responsive Acceptance

The shell and every page must satisfy, at **1366×768**, **1440×900**, and **~390px** mobile width:

- No horizontal scroll.
- No oversized empty zones; content uses a sensible max width and is balanced.
- The left navigation drawer **collapses** on smaller widths (temporary/overlay drawer with a toggle),
  rather than permanently consuming width or overlapping content.
- The query form and result panel **stack** correctly on small widths.
- Tables degrade to readable card/stacked rows on small widths.

## 7. Temporary Branding Strategy

- **Centralized generic theme tokens only.** There must be a single source of truth for color,
  spacing, radius, and typography tokens, consumed by both MudBlazor components and custom CSS.
  (Today the MudBlazor palette in `SecureOpsTheme.cs` and the CSS variables in `secureops-theme.css`
  are two sources with slightly divergent hex values; the redesign must converge them.)
- **Palette:** dark navy/graphite shell; neutral surfaces; **corporate red as the brand accent** for
  primary actions, selected navigation, active tabs, and brand details. Because red now carries brand
  meaning as well as failure meaning, the two must be separated on **luminance** — error sits
  markedly darker in Light and markedly lighter in Dark/Night than the brand value — and **no state
  may rely on colour alone**: every error surface carries an icon, an explicit message, and a
  semantic container.
- **Accessible text contrast** (target WCAG AA for body and status text).
- **Corporate assets: local and approved only.** Corporate emblem and identity assets are permitted
  where an approved asset has been supplied into the repository under `wwwroot/brand/approved/`, and
  are used only from there. Still prohibited: fetching logos or imagery from public websites at build
  or run time, hotlinking external assets, public-site screenshots or photography, copied layouts,
  and copied CSS. Redrawing or materially altering a corporate emblem is prohibited; the supplied
  asset is used as-is, and derived raster sizes are generated from it by
  `src/SecureOps.Ui/build/make-brand-assets.js`.
- **This contract records a product decision, not a legal one.** It does not assert trademark
  clearance or licensing approval for any corporate asset; that determination sits with the asset
  owner. Branding stays isolated in the theme, `wwwroot/brand/`, and the brand components so it
  remains replaceable without touching page structure.
- The approved future corporate template must be able to replace **theme tokens and assets only**,
  without changing page structure or component hierarchy.
- Approved corporate assets live under `wwwroot/brand/approved`. The emblem master supplied for this
  milestone is `thy-emblem-master.png` (2000×2000 indexed PNG, red on transparent). The brand red
  `#C90119` is that file's own fully opaque palette entry — sampled from the artwork rather than
  transcribed from a colour site — and is the single source for `SecureOpsTheme.BrandRed`.

## 8. Screenshot-Based Acceptance Checklist

Each page is reviewed via screenshots captured at **1366×768**, **1440×900**, and **~390px** mobile
width. A page passes only when every check is true at all three widths.

**Correction to prior acceptance evidence.** The first pass (Phase 4 visual implementation) marked this
table passed after reviewing only the three widths above. A subsequent wider review (1024 → 3840 px,
light and dark) found a shell layout defect — a duplicated drawer-width offset measured at 248 px at every
authenticated width ≥1024 — so the earlier marks were **premature**. The defect was remediated and the
status below now reflects a re-validated run in real Chrome (puppeteer-core, dual-host Demo) across
**1024 / 1366 / 1440 / 1920 / 2560 / 3840 / 390 px in both light and dark**, with measured left/right
asymmetry of 0 px and no horizontal scroll on every cell. Root cause, fixes, and the full matrix are in
`docs/18` §18. This corrects acceptance evidence only; the approved design direction above is unchanged.

| Page | Route | 1366×768 | 1440×900 | ~390px | Wide (1024–3840, light+dark) |
|---|---|---|---|---|---|
| Login | `/login` | ✅ | ✅ | ✅ | ✅ |
| Operasyon Panosu | `/dashboard` | ✅ | ✅ | ✅ | ✅ |
| PAM / AD Kimlik Sorgulama | `/identity-lookup` | ✅ | ✅ | ✅ | ✅ |
| Takım ve Yetki | `/team` | ✅ | ✅ | ✅ | ✅ |

Per-page measurable checks:

- **Login:** fits without vertical scroll at 1366×768; three profile cards reflow to one column on
  mobile; boundary text visible; no app bar/drawer; no marketing hero.
- **Operasyon Panosu:** one primary action (`Kimlik Sorgula`); operational flow strip present and
  legible; module status list secondary; no remediation/AI/execution controls; no equal-size grid.
- **PAM / AD Kimlik Sorgulama:** two columns on desktop, single column on mobile; required fields
  marked; optional section collapsed by default; a real result/empty/error state visible; no raw
  URLs/exceptions; no permanent connection noise before a query.
- **Takım ve Yetki:** compact table on desktop, cards on mobile; no detail panel before selection;
  detail appears after selection; demo-only warning visible; demo actions visually secondary.

Required screenshot set (12 images): each of the 4 pages × 3 widths. Store review screenshots outside
the repository (or in an ignored scratch folder); do not commit binary screenshots.

## 9. IIS Presentation Awareness (Documentation Only)

This gate documents the deployment boundary; it does **not** implement it.

- Local `launchSettings.json` profiles (`SecureOps.Api (Demo)` → `http://localhost:5000`,
  `SecureOps.Ui (Demo)` → `https://localhost:63947`) are **development/demo only**.
- An IIS presentation deployment will host UI and API as **separate IIS applications/sites** with
  **separate application pools**.
- The UI receives the API address through the environment variable `IdentityLookupApi__BaseAddress`.
- `http://localhost:5000` is **local-demo-only**. On a real IIS demo server the UI must use an
  internal API **DNS/HTTPS** address backed by a **trusted certificate** (the local dev-cert HTTP
  workaround does not apply there).
- Demo authentication (`DemoMode`, the Demo API auth bridge) may exist **only** on a separately
  isolated **Demo** environment/site.
- **Dev, Test, and Production must not use DemoMode or Demo API authentication.**
- A separate IIS deployment/runbook task will be performed **after** UI visual approval. Nothing in
  this gate changes hosting, ports, certificates, or the proven local configuration.

## Out of Scope (this gate)

No code/CSS/config/test/launch-profile/contract changes; no new assets; no `wwwroot/brand/approved`
folder; no IIS implementation. Phase 1 diagnostics, AI/RAG, and remediation remain out of scope.

## Approval

This contract is the acceptance basis for the next gate (visual implementation). The implementation
gate must satisfy sections 1–8 and validate each page in a real browser at the three reference widths
before review.
