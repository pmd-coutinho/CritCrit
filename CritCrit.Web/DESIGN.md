# CritCrit Design Language

## North star

**A quiet operator.** This is software for people who are responsible for things — brands, franchises, stores, devices, the access to all of them. Their day is dense. They look at this app a lot. So it should be calm, fast, legible, and honest about being a tool — not a brochure for one.

Reference points: Linear, Vercel, Geist, Datadog Dash, Stripe Dashboard read-views. Anti-references: Material 3, anything with floating action buttons, anything with gradients-as-decoration.

Three commitments:

1. **Density without anxiety.** Tight spacing, small type, but generous *internal* padding inside interactive regions. The eye should be able to scan a 30-row table at 13px without strain.
2. **One accent. Used sparingly.** Cyan-teal is the only chromatic moment in the UI. It marks the active route, the focus ring, the primary CTA, the loading bar. Everything else is neutral. When everything is highlighted, nothing is.
3. **Monospace where identity matters.** IDs, codes, serials, hashes, JWTs, audit-event payloads — Plex Mono. The brain reads `org_01J9...` and `device_DV3PX...` more accurately in mono. It also signals: this is a precise system.

## Typography

- **UI / body**: [IBM Plex Sans](https://fonts.google.com/specimen/IBM+Plex+Sans), weights 400 / 500 / 600. Plex has a slightly engineered quality — terminal lowercase `a`, narrow `t`, characterful but not loud. Reads small without falling apart.
- **Code / IDs / serials**: [IBM Plex Mono](https://fonts.google.com/specimen/IBM+Plex+Mono), weights 400 / 500. Same family DNA, no visual jump when switching from prose to identifier.
- **Display (optional)**: We do not use a separate display face. Bigger numbers and headings are just Plex Sans 600 at scale. Restraint.

Sizes (rem on 16px root, all 1.4 line-height unless noted):

| Token | Size | Use |
|---|---|---|
| `--text-xs` | 0.6875rem (11px) | secondary metadata, table headers (uppercased, tracked +0.08em) |
| `--text-sm` | 0.8125rem (13px) | **default UI text**, table rows, form labels, buttons |
| `--text-base` | 0.875rem (14px) | body prose, descriptions, audit payload |
| `--text-lg` | 1rem (16px) | section headings |
| `--text-xl` | 1.25rem (20px) | page titles |
| `--text-2xl` | 1.5rem (24px) | rare; brand name, empty-state hero |

Tracking: `-0.005em` on Plex Sans below 16px — softens the slightly wide default metrics. `0` on mono.

## Color

OKLCH throughout. Two themes — dark default, light toggle. Both share the same accent hue; lightness shifts.

### Neutral ramp (cool zinc, not blue)

The neutrals are pulled ~3° toward 240 to give the surface a faintly cool feel without ever reading as "blue". Twelve steps for either theme. They behave like inverses of each other — `--n-1` dark is the equivalent dimness of `--n-12` light.

### Accent — Beacon Cyan

`oklch(72% 0.15 200)` — a desaturated teal-cyan. Strong enough to anchor the eye, low enough chroma to feel honest, not Web2-glossy.

- `--accent` — primary accent
- `--accent-strong` — hover / pressed
- `--accent-soft` — 12% opacity wash for selected-row backgrounds
- `--accent-fg` — text color *on* accent (always near-black, both themes — high contrast)

### Semantic

Each has the same `--xxx`, `--xxx-soft`, `--xxx-fg` triplet structure.

- `--success` — muted moss `oklch(70% 0.12 145)`
- `--warn` — amber `oklch(78% 0.14 75)`
- `--danger` — desaturated red `oklch(64% 0.16 25)`
- `--info` — same as accent

## Spacing

4px base. Tailwind defaults work; we don't shadow them. Components prefer compact:

- Row height in tables: 36px (`h-9`)
- Form input height: 32px (`h-8`)
- Buttons: 28px small (`h-7`), 32px default (`h-8`), 36px loud (`h-9`)
- Page gutter: 24px (`px-6`)
- Section gap inside a page: 32px (`gap-8`)

## Surfaces & elevation

There is essentially **one surface**. The app does not stack panels.

- **`--bg`** — the page. ~98% lightness in light, ~14% in dark.
- **`--surface`** — a hair lighter (dark) / hair darker (light) for cards, popovers, inputs at rest. The contrast between `--bg` and `--surface` is ~3% lightness — *just* enough to suggest a plane without ever feeling like a popup.
- **`--surface-hover`** — row hover, dropdown item hover.
- **`--border`** — 1px hairlines. Lower contrast than you think you want. `oklch(... / 0.08)` against the background.
- **`--border-strong`** — for inputs and focused elements.

No box-shadows except a single soft shadow on floating popovers (`--shadow-pop`). Anything that *isn't* a popover stays flat.

## Motion

Almost none. Two allowed motions:

1. **Color transitions** at `120ms cubic-bezier(.4,0,.2,1)` on `background-color`, `color`, `border-color`. That's it.
2. **Skeleton shimmer** on loading rows, 1.6s linear, very low amplitude (~4% lightness sweep).

No springs. No translate-on-hover. No page-load orchestration. The UI **arrives**; it does not perform.

## Focus rings

`2px solid var(--accent)` with `box-shadow: 0 0 0 4px var(--accent-soft)`. Always visible — keyboard-first audience.

## Iconography

[Lucide](https://lucide.dev), stroke 1.5, 16px default. No filled variants. No emoji. No mixing icon families.

## Components / patterns

- **Tables**: zebra OFF. Hairline row separators. Sticky header. Row-hover background = `--surface-hover`. Column types: text, mono-id, badge, timestamp (relative + absolute on hover), action menu.
- **Badges**: text-only with a 1px border. No fills. Status implied by the border + foreground color (semantic ramp).
- **Buttons**: 3 styles only — `primary` (filled accent), `secondary` (bordered neutral), `ghost` (no border, hover-background only). No tertiary, no link-buttons.
- **Inputs**: 1px border, no inner shadow, no rounded-full. `rounded-md` (6px) globally.
- **Empty states**: a single short sentence, optionally one button. No illustrations.

## Code blocks & IDs

IDs render in `--text-xs` mono with `letter-spacing: 0`. Long IDs get a single hover-reveal copy button on the right edge — no permanent copy icon stealing visual weight.

## Voice

The interface speaks the way the API does. "Archive brand" — not "Are you sure you want to archive this brand?". "Restore", not "Click here to restore". Errors quote the exact server message; we do not soften them. The audience can handle real strings.

## Files

- `src/design/tokens.css` — every token defined here, both themes
- `src/design/fonts.css` — Plex Sans/Mono via Google Fonts
- `tailwind.config` is **not** used (Tailwind v4 reads tokens from CSS via `@theme`)
- `index.html` — `<html class="dark">` default; toggle via root class

If you change a token, change it here first. Components never define their own colors.
