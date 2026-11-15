# Cell editor for the Kennfeld viewer

Status: 2026-11-14, branch `reloaded-ui`.

`Reloaded/MapViewForm.cs` now contains a second control next to the heat map:
`MapCellGrid`, a plain `DataGridView` that shows the map's decoded values,
labels the columns with the real X-axis values and the rows with the real
Y-axis values, and lets the cells be edited in place — with the original
app's plausibility rule and an in-memory undo stack.

Everything is `net40`/XP-safe C# 4 (no LINQ, no tuples, no interpolation).

| File | Role |
|---|---|
| `Reloaded/MapViewForm.cs` | heat map + `MapCellGrid` (same file, see build note) |
| `Reloaded/CELLEDITOR.md` | this note |
| `Reloaded/evidence/celleditor-*.png` | verification screenshots (gitignored) |

`build.sh` and `Reloaded.csproj` are **not** touched: both list their sources
explicitly, so a new `CellGrid.cs` would not compile. `MapCellGrid` therefore
lives in `MapViewForm.cs` (one file, two classes).

## What you get

* Below the chart, one column per X value (header = the real axis value), one
  row per Y value (first, frozen column = the real axis value).
* Each cell shows the decoded value in decimal, right-aligned, Tahoma 8 (same
  font the chart uses).
* Double-click, F2 or just typing starts the edit; Enter commits; Esc cancels;
  arrow keys / Page Up/Down move. `Ctrl+C` copies like any `DataGridView`.
* A status line under the table names the selected cell
  (`Spalte 3 (X = 1000)  Zeile 2 (Y = 399)  Wert = 1650`) plus the number of
  pending edits.
* A commit writes into the same `double[,]` instance the heat map renders, so
  the chart recolours immediately (colour axis re-scales automatically).
* Invalid input is refused: the editor stays open and the status line turns
  red with the exact rule that was violated.
* `Ctrl+Z` undoes the last commit (per map, in memory).

## Data path — same decode as before, axes now kept

`MapViewForm.FromBinary` still does exactly what it did (and what
`frmMain`/`MapViewerEx` do):

1. `Tools.Instance.readdatafromfile(path, Flash_start_address (or
   Internal_address), Length, EDCFileType.EDC15V)` for the map bytes.
2. Row-major decode like `MapViewerEx.ShowTable` (`MapViewerEx.cs:1681-1720`):
   `cols = Y_axis_length`, `rows = X_axis_length`; 16-bit when
   `sh.ValueBytes != 1`, high byte first (`b = byte*256 + byte`); if
   `b > 0xF000` then `b -= 0x10000` (the old viewer's threshold, not 0x8000);
   8-bit symbols: one byte per cell, no sign step.
3. `readdatafromfileasint` for the two axis arrays, with the old
   `ConvertXAxisValue`/`ConvertYAxisValue` factor/offset handling and the
   X-axis 0xF000 sign fix (`MapViewerEx.cs:2018`).

The only change: the axis arrays are no longer thrown away after
`HeatMapSeries` got its `X0..X1`, they are passed to `MapCellGrid` and become
the header/row labels. `values[column, row]` is the single source of truth for
both views — the grid never keeps a second copy, so chart and table can never
drift apart.

Axis labels are formatted with `0.##` in the current culture; a degenerate
axis (no usable values in the file) still falls back to `1..n`, exactly like
the chart's axis.

## Plausibility limits — the original app's rule

The original has **no per-map min/max**. The only bounds anywhere in the
editing path are the storage-width checks in
`MapViewerEx.gridView1_ValidatingEditor` (`MapViewerEx.cs:5055-5180`), which
runs when a grid cell commits:

| map width | original check | line |
|---|---|---|
| 16 bit, hex view | `Convert.ToInt32(text, 16) > 0xFFFF` → invalid | 5064 |
| 16 bit, decimal / "Easy" | `Math.Abs(value) > 78643` → invalid | 5111 |
| 8 bit, hex view | `value > 0xFF` → invalid | 5130 |
| 8 bit, decimal / "Easy" | `value > 255` → invalid | 5171 |

`78643` is not a magic ECU number: it is `1.2 * 0x10000` rounded down — the
old editor allows 20 % "over-range" past the 16-bit span, in both directions
(the test is on `Math.Abs`). It is applied to the **raw** integer cell value
(the same value the table shows in decimal mode; correction factor/offset only
apply to the "Easy" display and the chart).

`MapCellGrid` reuses that rule verbatim:

* **16-bit maps** (`sh.ValueBytes != 1`): accept `|value| <= 78643`.
* **8-bit maps**: accept `0..255`.

One deliberate tightening on the 8-bit path: the old check only rejects
`value > 255`, so it would accept a negative byte; its own writer
(`GetDataFromGridView`) then drops negative 8-bit values silently. Since a
byte cannot hold a negative number, the editor rejects `< 0` too. This is the
only place the rule is stricter than the original, and it only affects 8-bit
symbols (in this label file those are all 1x1 scalars — the label applier
skips 8-bit curves, `MapLabelFile.cs:278-285`).

Related, not used for validation: the `+`/`-` shortcuts in the old grid clamp
with `if (value > 0xFFFF) value = 0xFFFF;` / `> 0xFF → 0xFF`
(`MapViewerEx.cs:3188`/`3193`). Our editor has no +/- shortcuts; typing is
validated against the table above.

No file is written. Saving a map needs the old transaction/checksum path
(`MapViewerEx.simpleButton2_Click` → `CastSaveEvent` → `GetDataFromGridView`
→ `Tools.writedatatofile` → checksum update), which is out of scope for a
viewer-only change; the exact encoder for a later step is "low 16 bits, high
byte first" (`GetDataFromGridView`, `MapViewerEx.cs:2848-2870`), i.e. the
inverse of the decoder above.

## Undo

Per grid instance (= per open map), in memory:

* one `UndoEntry { Column, Row, OldValue }` per committed edit, capacity 200
  (oldest entry dropped first);
* `Ctrl+Z` pops the most recent entry, writes the old value back into the
  shared array and the cell, and invalidates the chart;
* while a cell editor is open, `Ctrl+Z` is left to the text box (its own
  text undo) — `ProcessCmdKey` checks `IsCurrentCellInEditMode`;
* opening another map creates a new grid and a new, empty stack; closing the
  map drops it.

`Ctrl+Y`/redo does not exist on purpose (the task asked for undo only).

## Theme

The grid derives light/dark from the luminance of its host surface (the app
chrome from `Main.ApplyTheme`), so it follows the ribbon palette without
`MapCellGrid` needing a `KryptonPalette` reference:

* dark: background `#252526`, header `#333337`, lines `#3C3C3C`, text
  `#E6E6E6`, selection `#264F78` — the same values `Theming.cs` uses;
* light: `#FFFFFF` / `#E8E8EA` / `#D8D8DA` / `#222222` / `#CDE2F4`.

Row and label-cell colours are pinned explicitly instead of relying on
`DefaultCellStyle` inheritance: under Wine's `DataGridView` rows created
before a palette change kept the old (white) background while the text colour
did update — white-on-white cells in dark mode. Setting
`Rows[i].DefaultCellStyle` (plus an explicit style on the label cell of each
row) fixed it; verified by pixel sampling (no `#FFFFFF` cell in dark mode).

`RowHeadersVisible` is `false` on purpose: Wine's `DataGridView` clips
row-header text to a single pixel (verified with a screenshot), so the Y
labels live in a normal, `Frozen = true` first column instead.

## Wiring with `Main.OpenMapInline`

`Main.cs` cannot be changed here, and its inline path does this:

1. `shell = MapViewForm.FromBinary(...)`;
2. look for a `PlotView` **directly in `shell.Controls`**, remove it and add it
   to `_viewerBody`;
3. `finally { shell.Dispose(); }`.

So the `PlotView` must stay a direct child of the form (no `SplitContainer` —
that would send the viewer down the floating-window fallback). The grid
instead:

* is a direct child too (`Dock = Bottom`) for `MapViewForm.Show(...)`;
* subscribes to `PlotView.ParentChanged`; when the host reparents the chart,
  the grid moves itself into the same parent (`Dock = Bottom`) — still a
  `PlotView`-independent control, so it survives `shell.Dispose()`;
* disposes itself when the `PlotView` is disposed, which is exactly what
  `Main.ClearInlineMap()` does when another map or no map is selected. No leak,
  no stale grid over the next map (verified by switching maps).

`MapCellGrid` owns its values, limits and undo stack, so it never touches the
disposed form.

Height is chosen per map (`hint 34 + status 22 + header 22 + rows*20`, clamped
170..360 px), so a 2-row curve leaves most of the window to the chart.

## Verification (2026-11-14)

Build:

```
cd Reloaded && bash build.sh      # -> "Compilation succeeded - 55 warning(s)"
```

The 55 warnings are the pre-existing legacy ones (none from this change).

Runtime, on a dedicated Xvfb `:90` (no `wineserver -k`, the shared prefix was
left alone):

```
Xvfb :90 -screen 0 1280x900x24
DISPLAY=:90 wine explorer /desktop=Reloaded90,1140x800 \
    ".../Reloaded/Reloaded.exe" --theme=light "Z:\\tmp\\ga-work\\ga.stage1"
# tree: expand "Labeled maps (159)", find "Driver's Wish [flashbank 0] @0x99DC (16x8)",
# Enter -> inline viewer, then (xdotool, window-relative coordinates):
#   cell X=1000 / Y=399 at (491,420): click, F2, Ctrl+A, "1234", Enter
#   Ctrl+Z
#   cell X=10 / Y=0 at (381,400): click, F2, Ctrl+A, "99999", Enter  (rejected)
```

Results (all confirmed on screen and in the screenshots):

| Step | Evidence | Observed |
|---|---|---|
| open map | `celleditor-light.png`, `celleditor-light-grid.png` | 8 columns (10..10000) x 16 rows (0..3990), value 1700 at [0,0], status `Spalte 1 (X = 10)  Zeile 1 (Y = 0)  Wert = 1700 \| Änderungen: 0` |
| commit 1234 | `celleditor-edit-commit.png`, `...-grid.png` | cell at X=1000/Y=399 was 1650, now 1234; `Änderungen: 1 (Ctrl+Z)`; chart pixels changed (3242 px diff inside the plot area) |
| Ctrl+Z | `celleditor-undo.png`, `...-grid.png` | cell back to 1650; `Änderungen: 0`; chart pixel-identical to the pre-edit screenshot (0 px diff) |
| reject 99999 | `celleditor-reject.png`, `...-grid.png` | editor stays open, value stays 1700, red status text: `Wert abgelehnt: 16-Bit-Zelle: \|Wert\| darf höchstens 78643 sein (Grenze aus MapViewerEx.gridView1_ValidatingEditor).` |
| Esc | manual check | rejection message clears, value unchanged, `Änderungen` unchanged |
| boundary 78643 | manual check | accepted (the original test is strict `>`), chart re-scales, undo restores it exactly |
| switch map | manual check | old grid disappears with the old `PlotView`, new grid has the new map's dimensions and `Änderungen: 0` |
| dark theme | `celleditor-dark.png`, `...-grid.png` | cells `#252526`/`#29292B`-style alternating, header `#333337`, selection `#264F78`, light text; no white cells |

Not exercised at runtime: an 8-bit (`ValueBytes == 1`) symbol. In this label
file those are 1x1 scalars only; the 8-bit branch is the same code path with
the `0..255` check, covered by the table above.

## Known limits / next steps

* Edits are viewer-local. Writing them back needs the transaction log +
  checksum update (out of scope; see the encoder note above).
* No multi-cell paste/fill, no "reset map", no redo — deliberately small.
* The chart still has `LabelFontSize = 0` (wine-mono paints the plot black
  otherwise), so the *chart* shows no numbers; the table is the numeric view.
