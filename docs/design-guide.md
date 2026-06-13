# Editor Design Guide

The reference for remodelling the editor UI. Direction agreed with the user:

- **Aesthetic:** modern dark *pro-tool* — anchored on the **osu!lazer editor** for familiarity, with the
  restraint of **IDE / dev tools** (VS Code, Linear): neutral greys, crisp type, hierarchy from subtle
  contrast rather than chrome or saturated fills.
- **Density:** **compact (pro)** — maximise useful content; small, precise controls; tight but deliberate
  spacing on a shared baseline.
- **Colour:** **functional palette** — saturated colour carries fixed meaning (timing/SV/selection/kiai/
  status). One brand accent (osu! pink), used sparingly.
- **Scope of this phase:** **components** + the design tokens that feed them.

All tokens live in code at [`OsuBeatmapEditor.Game/Graphics/EditorTheme.cs`](../OsuBeatmapEditor.Game/Graphics/EditorTheme.cs).
**Never hard-code** a hex, font size, radius, spacing or duration in UI code — reference a token. A single
edit should re-theme the whole app.

---

## 1. Principles

1. **Contrast over chrome.** Separate regions with a one-step surface change or a 1px border, not with
   heavy shadows, gradients or bright fills. Depth is a *quiet* surface ramp (`Sunken → Base → Surface →
   Raised → Overlay`).
2. **Colour means something.** Greys for structure; saturated colour only when it encodes data (red = BPM,
   green = SV, amber = selection, orange = kiai) or status (success/warning/error/info). If a colour isn't
   carrying meaning, it shouldn't be saturated.
3. **Accent is a spotlight, not paint.** osu! pink marks the *one* primary action, focus, or brand moment in
   a view — never as a generic "this is interactive" fill. Most interactive controls are neutral greys that
   simply lighten on hover.
4. **Compact, on a baseline.** Use the spacing scale and standard control heights so everything lines up;
   prefer one more visible row over generous padding.
5. **Immediate motion.** Transitions are short (80–150 ms) and all use one easing curve. Nothing bounces or
   lingers; a tool should feel instant.
6. **Legible always.** Body text on any surface meets comfortable contrast; live numeric readouts use
   fixed-width digits so they don't jitter.

---

## 2. Foundations (tokens)

### 2.1 Colour — `EditorTheme.Colours`

**Surfaces** (pick by elevation, low → high):

| Token      | Hex       | Use |
|------------|-----------|-----|
| `Sunken`   | `#101013` | Playfield void, timeline troughs, wells |
| `Base`     | `#18181B` | App background behind all chrome |
| `Surface`  | `#1F1F23` | Panels, toolbars, docked surfaces |
| `Raised`   | `#27272C` | Cards / section blocks inside a panel |
| `Overlay`  | `#2E2E34` | Popovers, menus, modals |

**Controls** (interactive fills) — `Control` `#303036` → `ControlHover` `#3A3A42` → `ControlActive` `#45454F`.

**Lines** — `Border` `#3A3A42` (hairlines), `BorderStrong` `#52525E` (separators, focus).

**Text** — `Text` `#ECECEF` (primary) · `TextMuted` `#A0A0AC` (labels/secondary) · `TextFaint` `#6E6E7A`
(tertiary/disabled).

**Brand accent** (sparingly) — `Accent` `#FF66AB` · `AccentHover` `#FF7DB7` · `AccentPressed` `#E0568F` ·
`AccentSoft` (pink @ 0.16, for tints/focus halos).

**Functional / semantic** (the *only* saturated chrome, each meaning fixed):

| Token       | Hex       | Meaning |
|-------------|-----------|---------|
| `Timing`    | `#FF5C6C` | Uninherited (red) timing point / BPM |
| `Velocity`  | `#52D38C` | Inherited (green) timing point / SV |
| `Selection` | `#FFC93C` | Selection rings, drag box, selected markers |
| `Kiai`      | `#FF9D4D` | Kiai sections |
| `Bookmark`  | `#4FB3FF` | Bookmarks / info markers |
| `Success` / `Warning` / `Error` / `Info` | `#46C77F` / `#FFB454` / `#FF5C5C` / `#4FB3FF` | Status & validation |

> **Combo colours** (`OsuColour.ComboColours`) are *beatmap content*, not chrome — they stay as-is and are
> not part of the theme palette.

> **Migration note:** the legacy `OsuColour` palette (blue/purple-tinted `#1A1A2E` etc.) is being folded
> into `EditorTheme`. New/edited components use `EditorTheme.Colours`; the remodel replaces `OsuColour`
> references screen by screen. Semantic colours that today live as user settings (uninherited/inherited/
> bookmark/kiai) keep their **defaults** from this palette.

### 2.2 Typography — `EditorTheme.Type`

Few steps, role-named. Default font throughout; pass `numeric: true` for live values (fixed-width digits).

| Role          | Size / Weight | Use |
|---------------|---------------|-----|
| `Display`     | 22 Bold       | The single biggest header (rare) |
| `Title`       | 18 Bold       | Overlay & panel titles |
| `Heading`     | 15 SemiBold   | Section headers, prominent values |
| `Body`        | 13 Regular    | Default UI text |
| `BodyStrong`  | 13 SemiBold   | Button text, active labels |
| `Label`       | 12 SemiBold   | Control labels, chips, tabs |
| `Caption`     | 11 Regular    | Captions, faint meta, pill readouts |

Usage: `Font = EditorTheme.Type.Body()` · time readout: `EditorTheme.Type.Heading(numeric: true)`.

### 2.3 Spacing — `EditorTheme.Spacing`

4-unit rhythm: `Xxs 2 · Xs 4 · Sm 6 · Md 8 · Lg 12 · Xl 16 · Xxl 24`.

- Inside controls (padding): `Sm`–`Md`. · Gaps between peers (flow spacing): `Sm`–`Md`.
- Panel insets: `Lg`–`Xl`. · Group separations: `Xl`–`Xxl`.

### 2.4 Radius — `EditorTheme.Radius`

`Sm 3` inputs/swatches/chips · `Md 5` buttons/rows/tabs · `Lg 8` panels/overlays · `Pill` fully round
(on a `CircularContainer`: dots, pills, the seek playhead). **Don't invent in-between radii** (today's
4 / 5.5 / 6 / 7 collapse onto this scale).

### 2.5 Motion — `EditorTheme.Motion`

`Fast 80ms` (hover/press) · `Normal 150ms` (active/toggle/selection) · `Slow 300ms` (overlay pop) — all with
`Motion.Ease` (`Easing.OutQuint`). Example: `background.FadeColour(EditorTheme.Colours.ControlHover,
EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);`

### 2.6 Sizing — `EditorTheme.Sizing`

`RowHeight 30 · ButtonHeight 28 · InputHeight 26 · MinTouchTarget 24 · BorderThickness 1`. Heights keep
controls on one baseline across panels.

---

## 3. Components

Each spec lists **anatomy**, **tokens**, **states**, and **rules**. States are the contract every
interactive component must satisfy: **idle → hover → pressed/active → focus → disabled**.

### 3.1 Button

A `ClickableContainer` with a `Box` background and centred text. Three intents:

- **Default** (most buttons): bg `Control`; hover `ControlHover` (`Fast`); pressed `ControlActive`; text
  `Text` via `BodyStrong`.
- **Primary** (the *one* main action in a view, e.g. an overlay's *Apply/Save*): bg `Accent`; hover
  `AccentHover`; pressed `AccentPressed`; text `Sunken` (dark-on-accent for contrast).
- **Danger** (destructive, e.g. *Delete*): default styling at rest; on hover the bg goes `Error` and text
  `Text`. Reserve red for the hover/commit moment so it reads as a warning.

**Anatomy:** height `Sizing.ButtonHeight`, radius `Radius.Md`, horizontal padding `Spacing.Md`, `Masking`.
**Disabled:** `Alpha 0.45`, no hover response. **Focus** (keyboard): 1px `BorderStrong`.

> *Do* keep exactly one Primary per view. *Don't* use accent for Default buttons — they're neutral greys.

### 3.2 Tool / toggle row (active-selectable)

The `ToolPanel` row pattern, generalised: a selectable row in a group where one is active.

- Idle: bg `Control`, label `Text`/`TextMuted`. · Hover (inactive): bg `ControlHover` (`Fast`).
- **Active:** bg `Accent`, label `Sunken` — transition `Normal`. (This is the sanctioned accent-fill case:
  a persistent selected state, not a hover.)
- Disabled/placeholder: `Alpha 0.55`, label `TextFaint`, no hover.
- Anatomy: height ≈ `RowHeight`, radius `Radius.Md`, a leading mono key-hint (`Label`, `TextMuted`) +
  label (`BodyStrong`).

### 3.3 Text & number input

`BasicTextBox` subclasses (`EditorTextBox`, `NumberBox`).

- Anatomy: height `InputHeight`, radius `Radius.Sm`, bg `Sunken` (inputs read as wells), 1px `Border`,
  text `Body` (numbers `numeric: true`).
- **Focus:** border → `Accent` (1px), bg unchanged. · Hover: border → `BorderStrong`.
- Invalid commit: border → `Error` briefly, then revert. · Disabled: `Alpha 0.45`.
- `NumberBox` clamps to range on commit and always formats with `.` decimal (invariant culture).

### 3.4 Colour swatch + picker

`ColourSwatch`: a `Radius.Sm` rounded box, 1px `Border`, showing the bound colour; click opens an HSV
popover (`Overlay` surface). Pair with a `Reset` Default-button to restore the palette default.

### 3.5 Key-rebind button

`KeyRebindButton`: a Default button showing the current shortcut (`BodyStrong`).

- **Listening state must not reuse the accent fill** (today it does, colliding with "primary/active"
  meaning). Use `AccentSoft` bg + 1px `Accent` border + caption *"Press keys…"* in `TextMuted`. Escape
  cancels; focus loss stops listening.

### 3.6 Tabs

Horizontal tab strip (`TabbedOverlay`): each tab is `Label` text with `Md` horizontal padding.

- Inactive: text `TextMuted`, transparent bg. · Hover: text `Text` (`Fast`).
- Active: text `Text` + a 2px `Accent` underline (animate the underline's X/width `Normal`). Underline is
  the active affordance — no filled tab backgrounds.

### 3.7 Panel / overlay (modal)

- **Panel** (docked, e.g. toolbox, HUD groups): `Surface` bg, optional 1px `Border` on the edge facing the
  content, inset `Lg`.
- **Modal overlay** (F5 Song Setup, F6 Timing): a centred card on `Overlay`, radius `Radius.Lg`, inset
  `Xl`, over a scrim (`Sunken` @ ~0.55). Pop in/out at `Slow` with `Ease` (fade + slight scale 0.98→1).
  Layout: `Title` header, a thin `Border` divider, then content; actions row pinned bottom-right with the
  Primary action last.

### 3.8 Pill / chip / tag

Small `CircularContainer` (`Radius.Pill`) readouts — BPM/SV pills on the timeline, "N beats" preview,
status tags. Fill = the relevant functional colour (or `Raised` for neutral), text `Caption` in `Sunken`
or `Text` for contrast. Padding `Xs` horizontal, `Xxs` vertical.

### 3.9 Slider (value)

Track: `Sunken`, height ~4px, `Radius.Pill`. Filled portion: `Accent` (or the relevant functional colour
when the value *is* that data). Thumb: small `Pill` circle, `Text`; grows slightly on hover (`Fast`).
Pair with a `NumberBox` for exact entry.

### 3.10 Tooltip

`Overlay` bg, radius `Radius.Sm`, padding `Sm`, text `Caption` in `Text`. Appears after a short hover
delay; fades `Fast`. Keyboard shortcuts shown as faint mono key-hints (`TextMuted`).

### 3.11 Divider

A 1px (`Sizing.BorderThickness`) `Border` line. Use to separate groups within a panel; prefer a divider +
`Lg` spacing over boxing every group.

---

## 4. Patterns

- **Labeled row** (`SettingsLayout.LabeledRow`): fixed `RowHeight`, label left (`Body`, `Text`), control
  right-aligned. The backbone of every settings panel.
- **Section**: a `Heading` (`TextMuted`, uppercase optional) + `Md` gap + a vertical flow of labeled rows;
  sections separated by a Divider + `Xl` spacing.
- **HUD readouts** (BPM / SV / time): `Heading(numeric: true)`; SV tinted `Velocity`, BPM neutral `Text`,
  time `Text`. Right-aligned, stacked, `Sm` gaps.

---

## 5. Functional-colour reference (single source of meaning)

| Colour | Token | Where it appears |
|--------|-------|------------------|
| Red    | `Timing`    | Red timing lines (top + bottom timeline), BPM pills, red-anchor handles |
| Green  | `Velocity`  | Green SV lines, SV pills, SV HUD readout |
| Amber  | `Selection` | Selection rings (playfield), selected blueprint borders/glow, drag boxes |
| Orange | `Kiai`      | Kiai bands (bottom timeline) |
| Blue   | `Bookmark`  | Bookmark lines, info markers |
| Pink   | `Accent`    | Primary action, active tool, focus, brand — **structure only, never data** |

If you reach for a saturated colour and it isn't in this table, it's wrong — use a grey.

---

## 6. Do / Don't

- ✅ Reference tokens (`EditorTheme.*`). ❌ Hard-code hex / size / radius / duration.
- ✅ One Primary (accent) action per view. ❌ Accent as a generic interactive fill.
- ✅ Neutral greys lightening on hover for most controls. ❌ Saturated hovers.
- ✅ Saturated colour = data/status only. ❌ Decorative saturation.
- ✅ Depth via the surface ramp + 1px borders. ❌ Drop shadows / gradients for separation.
- ✅ Fixed-width digits for live values. ❌ Proportional digits that jitter.
- ✅ All transitions `Fast`/`Normal`/`Slow` + `Ease`. ❌ Bespoke durations / mixed easings.

---

## 7. Migration checklist (next phase)

When remodelling a component/screen:

1. Replace `OsuColour.*` with the matching `EditorTheme.Colours.*` (greys for chrome, functional for data).
2. Replace literal font sizes with `EditorTheme.Type.*`; mark live numerics `numeric: true`.
3. Snap radii to `Radius.Sm/Md/Lg/Pill`; spacing to the `Spacing` scale; heights to `Sizing`.
4. Implement the full state set (idle/hover/pressed/active/focus/disabled) with `Motion` durations.
5. Ensure exactly one Primary accent in the view; demote the rest to Default greys.
6. Verify against §6 Do/Don't and the §5 functional table.
