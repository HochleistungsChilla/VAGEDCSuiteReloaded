# Kennfeld (map) viewer — heat map via OxyPlot 1.0.0

Status: 2026-11-14, branch `reloaded-ui`.

`Reloaded/MapViewForm.cs` adds a plain WinForms form that renders one map
(Kennfeld) of a loaded EDC15V binary as a colour-coded 2D heat map with a Jet
colour bar.  OxyPlot 1.0.0 (`net40`), no DevExpress, no Nevron.

## Files and wiring

| File | Role |
|---|---|
| `Reloaded/MapViewForm.cs` | the form + the binary reader (`FromBinary`) |
| `Reloaded/Main.cs` | one new handler: double-click a row in the Maps grid |
| `Reloaded/MAPVIEWER.md` | this note |

`build.sh` / `Reloaded.csproj` are **not** touched here.  `MapViewForm.cs` has to
be added to the build by the orchestrator (the `wine csc` source list in
`build.sh` and the `<Compile>` item in `Reloaded.csproj`).  The verification
below used an exact copy of the `build.sh` csc invocation with
`MapViewForm.cs` appended; the source compiles clean (no warnings from this
file, same 55 legacy warnings as before).

## Using it

* Open a binary as usual, switch to `View > Views > Maps`.
* Double-click a row.  The row's `Varname` cell is looked up again in the
  parsed image and a modeless viewer window opens.
* `MapViewForm.ShowMap(path, varname, owner)` and
  `MapViewForm.FromBinary(path, varname)` are the entry points; failures
  (unreadable file, unknown map, no data address) are caught in `Main.cs` and
  appended to the checksum report box as `Map view: <message>`.
* The constructor takes metadata + the grid only
  (`values[x, y]`, `xValues`, `yValues`, names, units, address, length), so the
  form can also be fed from another source.

## Data-reading path (what is reused, exactly)

The map list (`MapList.GetMaps`) only carries metadata, so the viewer re-parses
the file with the same parser object (`EDC15VFileParser.parseFile`) and picks
the `SymbolHelper` by `Varname` (fallback `Userdescription`) — the same identity
`Main.cs` writes into the grid.  Everything else mirrors the old DevExpress
path in `frmMain.cs` / `MapViewerEx.cs`:

1. **Map bytes** — `Tools.Instance.readdatafromfile(path,
   Flash_start_address (or Internal_address), Length, EDCFileType.EDC15V)`,
   the same call frmMain uses for `tabdet.Map_content`
   (`frmMain.cs:560`; second site `frmMain.cs:740`). `readdatafromfile`
   reverses every byte pair for non-EDC16 files, which is why the subsequent
   16-bit decode reads high byte first.
2. **Cell decode** — `MapViewerEx.ShowTable` (`MapViewerEx.cs:1681-1710`),
   without the DevExpress grid:
   * width `tablewidth = Y_axis_length`, `rows = X_axis_length`;
   * 16-bit when `sh.ValueBytes != 1` (`frmMain.cs:559` passes
     `sh.ValueBytes != 1`);
   * 16-bit value `b = byte[off] * 256 + byte[off+1]`; if `b > 0xF000` then
     `b -= 0x10000` (the exact threshold and sign rule of the old viewer, not
     the usual `0x8000` rule);
   * 8-bit symbols: one byte per cell, no sign step;
   * an incomplete trailing cell reads as 0 instead of throwing.
3. **Axis values** — `frmMain.GetXaxisValues` / `GetYaxisValues`
   (`frmMain.cs:1438`, `frmMain.cs:1474`) with the same address/length
   crossing the old UI uses (`frmMain.cs:521-522`):
   * columns (`x_axisvalues`): `Tools.Instance.readdatafromfileasint(path,
     Y_axis_address, Y_axis_length, EDCFileType.EDC15V)`;
   * rows (`y_axisvalues`): same call at `X_axis_address` with
     `X_axis_length`.
4. **Axis scale** — mirror of `MapViewerEx.ConvertXAxisValue` /
   `ConvertYAxisValue` (`MapViewerEx.cs:3502-3534`): the raw value is shown
   while the correction factor is exactly 1; otherwise
   `value * factor + offset`.  The columns additionally get the old X-axis
   sign fix (`xval > 0xF000` → negative, `MapViewerEx.cs:2018-2026`).  The
   rows do not, exactly like the old code.
5. **Fallbacks** (documented deviation, only when the file has no usable
   axis): `address <= 0`, `length <= 1`, or an all-zero axis array falls back
   to `1..n` indices so the heat map keeps a real extent.  The old app showed
   an all-zero axis instead; cell values are unaffected.

Not reused: `MapViewerEx` itself.  It is a DevExpress/Nevron
`XtraUserControl` that needs a `Map_content` byte array, a DockPanel, edit
callbacks and a live grid; compiling it UI-free is exactly what `MAPLIST.md`
already avoided.  The reading logic above is the reusable 30 lines of it.

Value check against the test image `/tmp/ga-work/ga.stage1`:
`AG4 - withdrawal of amounts of - Map` at `0x1312` (200 B, 10x10) decodes to
7 distinct column values 500, 1000, 1500, 2000, 2300, 2500, 3000 (rows
constant) — identical to a byte-level re-decode of the file.  `Driver's Wish
[flashbank 0]` at `0x19DC` (256 B, 16 rows x 8 columns) decodes to 0..5600,
axes 0..5355 (rows) and 10..10000 (columns).

## OxyPlot wiring

```
PlotModel { Title }                     // 1.0 has only the parameterless ctor
  LinearColorAxis  Key="Values", Position=Right, Palette=OxyPalettes.Jet(256)
  LinearAxis       Position=Bottom, Title, Minimum, Maximum
  LinearAxis       Position=Left,   Title, Minimum, Maximum
  HeatMapSeries    X0/X1/Y0/Y1, Data, CoordinateDefinition=Edge,
                   RenderMethod=Rectangles, Interpolate=false,
                   LabelFontSize=0, ColorAxisKey="Values"
```

* `HeatMapSeries.Data` is indexed **`[x, y]`** in OxyPlot 1.0 — first
  dimension along the X axis.  The reader therefore stores
  `data[column, row]` while walking the binary row-major with
  `tablewidth = Y_axis_length`, so the picture matches the old grid
  (columns = `Y_axis_address` values, rows = `X_axis_address` values).
* `CoordinateDefinition.Edge`: `X0/X1/Y0/Y1` span the outer cell edges; the
  reader passes the first/last axis values.  A single-cell or constant axis is
  padded (`+/-1%`, or `+/-0.5`) so it still has a visible extent.
* `RenderMethod.Rectangles` draws one rectangle per cell; no bitmap path.
* `LabelFontSize = 0` is mandatory: any value > 0 renders the whole plot
  black under wine-mono (System.Drawing text-under-transform defect).  No cell
  text is drawn.
* The colour bar is the `LinearColorAxis`; its Min/Max come from the series
  automatically (`HeatMapSeries.UpdateAxisMaxMin` → `Include(MinValue/MaxValue)`).
  `ColorAxisKey` pins the series to it explicitly (`DefaultColorAxis` would
  also work: it is the first `IColorAxis` in `Axes`).
* `OxyPlot.HorizontalAlignment` is not used anywhere, so there is no
  `System.Windows.Forms.HorizontalAlignment` collision.

## Verification (headless)

* Build: temp copy of the `build.sh` csc command with `MapViewForm.cs` added —
  `Compilation succeeded - 55 warning(s)`.
* Run: fresh `Xvfb :95` (1280x800x24), `wine Reloaded.exe
  Z:\tmp\ga-work\ga.stage1`, no `wineserver -k` (a foreign Wine/Proton session
  shares the prefix); only this session's Xvfb and app were started.
* Flow: View tab → Views/Maps → double-click `AG4 ...` (10x10) and
  `Driver's Wish ...` (16 rows x 8 columns).  `import -window root` shows a
  graded Jet heat map, the colour bar (0..5600 with tick labels) and the two
  numeric axes.  Cell boundaries counted in the PNG: 8 columns x 16 rows for
  `Driver's Wish`, matching the binary.
* Screenshots: `/tmp/map95-driverwish.png` (window) and
  `/tmp/map95-heat4.png` (root capture, :95).

## Limits

* Read-only: no drag editing, no save-back, no 3D/surface chart.
* 1D maps render as one row (or column); scalar entries are not interesting.
* Non-monotonic axis arrays are rendered as an even span between min and max;
  the old grid had the same limitation in its heat-map-like colouring.
* `Correction`/`Offset` of the map itself (`sh.Correction`/`sh.Offset`, used
  by the old chart's Z scale) are not applied to the cell values: the heat map
  shows the same raw numbers the old grid showed.  Axis factors/offsets are
  applied as described above.
* Re-parsing the image per double-click costs the usual parser time
  (~12k symbols with labels); no cache yet.
