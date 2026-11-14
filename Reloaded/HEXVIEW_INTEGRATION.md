# Hex viewer in the main window

`HexViewerControl` is now wired into `MainForm` (no changes to the control, the
palettes or the checksum/layout classes).

## Approach

- **Panel**: a plain `Panel` (`Dock = Bottom`, `Height = 160`, `Visible = false`)
  hosts the control with `Dock = Fill`.
- **Dock order**: the panel is added *before* the checksum report, so the report
  (docked at the very bottom) is laid out first, then the hex panel above it, and
  the `Dock = Fill` grid takes the rest. Result top→bottom:
  ribbon, info strip, grid, **hex panel**, checksum report.
- **Toggle**: `View` tab → new group **Views / Panels** → check button
  **Hex / viewer** (`ButtonType = GroupButtonType.Check`, same style as
  Minimize). It sits between the `Theme` and `Ribbon` groups.
  Handler: `ShowHexPanel(_hexButton.Checked)` sets `_hexPanel.Visible`.
  A hidden docked control is skipped by layout, so the grid immediately
  reclaims the 160 px; when shown, the grid shrinks and keeps all three
  block rows visible.
- **Data**: `Inspect(path)` calls `_hex.SetBytes(image)` and
  `_hex.SetCurrentAddress(0)` after a successful read, for every file (not only
  known 256 KB images). With no file loaded the panel stays empty.
- **Theming**: `ApplyHexPalette(palette)` copies `BackColor` from
  `PaletteBackStyle.InputControlStandalone` and `ForeColor` from
  `PaletteContentStyle.InputControlStandalone`; any lookup failure keeps the
  control's `SystemColors` defaults. `OffsetColor` / `SelectionColor` /
  `SelectionTextColor` are left at their defaults, so the control is not
  hard-coded dark. Light and dark both verified.

## Pre-existing defect fixed on the way

The checksum report was **invisible** before this change: its runtime bounds
were `Height = 0` (grid ran to the window bottom). Cause: `KryptonTextBox`
resets its height to the single-line value when `Multiline` is flipped *after*
`Dock`/`Height`. Fix: set `Multiline = true` first in the initializer (2-line
move). The report is now 180 px tall, as intended.

## Evidence (Xvfb :98, wine, `--theme=dark`, `Z:\tmp\ga-work\ga.stage1`)

- `bash build.sh` → exit 0.
- `evidence/hexview-hidden.png` — hex panel off: grid + checksum report only.
- `evidence/hexview-shown.png` — `View > Hex` checked (gold highlight); hex dump
  with offset / 16 hex bytes (8+8 gap) / ASCII columns, report below it.
- `evidence/hexview-light.png` — same layout in the light theme (palette-driven
  surface, still readable).
- `evidence/hexview-empty.png` — started without a file: panel opens empty, no
  stale bytes, no crash.

Measured column x=480 (window capture 960x680):

| state | grid | hex | report |
|---|---|---|---|
| hidden | y 207–495 | – | y 497–674 |
| shown | y 207–335 | y 336–495 | y 497–674 |

Content check against the file (`xxd`): row 0 reads
`55 55 00 00 56 56 33 33 2E 2E 31 31 38 88 04 00  UU..VV33..118...`
and row 1 `10 00 11 00 12 00 13 00 14 00 15 00 16 00 17 00`, both matching the
image bytes. Theme switch (Light/Dark) and ribbon minimize were exercised with
the hex panel open; both still work and the panel reflows with the ribbon.

## Wine-specific notes

- `wine explorer /desktop=NAME,1000x700` produces a 1280x800 desktop window here
  and centers the 960x680 form at (160,60). Capture with
  `import -window <id>` (or crop at that offset), not at 0,0.
- Only one `explorer /desktop` stays alive per X display; starting a second one
  tears the first down. Run captures one at a time.
- `pkill -f "Xvfb :98"` matches the invoking shell's own command line (SIGTERM
  to itself). Find the PID with `pgrep -x Xvfb` and filter `/proc/<pid>/cmdline`
  for `:98` instead.
