# Chart match — original VAGEDCSuite map viewer → Reloaded OxyPlot heat map

Status: 2026-11-14, branch `reloaded-ui`.
Scope: visual styling only. `MapViewForm.cs` is the only source file changed;
the data-reading path, decode rules and `HeatMapSeries.Data` are untouched.
No commit, no push.

## How the original was measured

The old viewer is DevExpress on the outside and **Nevron Chart** inside the
chart control (the map tree/grid is DevExpress XtraGrid/XtraTabControl, the
map chart itself is `Nevron.Chart`; see `MapViewerEx.cs`). The app was run on a
dedicated Xvfb (`:91`, 1280x900) against `Z:\tmp\ga-work\ga.stage1`, a map was
double-clicked in the tree, and the chart region was screenshotted.

Evidence in `evidence/` (gitignored):

| file | content |
|---|---|
| `chartmatch-original-3d.png` | original "3D Graph" tab, SOI 1 [codeblock 1] |
| `chartmatch-original-2d.png` | original "2D Graph" tab (line/slice view) |
| `chartmatch-reloaded-before-chart.png` | Reloaded before: OxyPlot Jet heat map |
| `chartmatch-reloaded-after-chart.png` | Reloaded after: matched heat map |
| `chartmatch-reloaded-before.png`, `-after.png` | full windows |
| `chartmatch-compare.png` | original 3D above, matched Reloaded below |

### What the screenshot and the original source show

| property | original | source of truth |
|---|---|---|
| chart background | white `#FFFFFF` | screenshot (dominant colour), `nChartControl*` |
| app chrome around it | light grey `#EBECEF` | screenshot |
| axis lines | black `#000000`, 1 px, left + bottom only (no box) | screenshot; `Init2dChart` |
| tick marks | black, pointing **inwards** | screenshot |
| tick labels / text | black, Tahoma 8 | screenshot; `MapViewerEx.cs:742-753` sets Tahoma 8 for the grid |
| grid lines | black in the 3D view (wireframe + floor grid); 2D view: dotted major grid at the back wall, plus a beige `#F5F5DC` interlaced strip (`NScaleStripStyle(Color.Beige)`) | `MapViewerEx.cs:2306-2312` |
| colour ramp | **Green → Yellow → Orange → OrangeRed → Red**, five anchors at 0 / 25 / 50 / 75 / 100 % of the range, smooth (`SmoothPalette = true`, `PaletteSteps = 4`) | `MapViewerEx.cs:1108-1128` (`FillData`) |
| low / high colours | `Color.Green` (0,128,0) → `Color.Red` (255,0,0) | same |
| in-chart title | **none** (map name lives in the window/tab caption) | screenshot |
| legend / colour scale | **none** in either graph view (`nChartControl2.Legends.Clear()`) | screenshot; `MapViewerEx.cs:2294` |

The ramp is the key difference: the Reloaded viewer used `OxyPalettes.Jet`
(blue → cyan → green → yellow → red). The old chart never shows blue; its low
end is dark green and its high end is pure red.

## What was changed in `MapViewForm.cs`

1. **Palette** — `OxyPalettes.Jet(256)` replaced by
   `OxyPalette.Interpolate(256, MapPalette)` with the five original anchors
   `(0,128,0) (255,255,0) (255,165,0) (255,69,0) (255,0,0)`; even spacing puts
   them exactly at 0/25/50/75/100 %, like `FillData`'s `Palette.Add` calls.
2. **Smooth shading** — the old surface is smoothly palette-shaded, not a
   checkerboard, so the series now renders
   `RenderMethod = Bitmap`, `Interpolate = true` (was `Rectangles`/`false`).
   `LabelFontSize` stays 0 (wine-mono black-plot workaround).
3. **Plot surfaces** — `Background` and `PlotAreaBackground` white,
   `TextColor`/`TitleColor` black, `PlotAreaBorderColor` transparent (the
   original draws only the left/bottom axis lines, not a box).
4. **Fonts** — `DefaultFont = "Tahoma"`, `DefaultFontSize = 8`, matching the
   old grid/chart text.
5. **Axes** — both linear axes and the colour axis now use
   `AxislineColor = TicklineColor = TextColor = TitleColor = Black`,
   `TickStyle = Inside`, `MajorGridlineStyle = MinorGridlineStyle = None`,
   `FontSize = TitleFontSize = 8`, `AxisTitleDistance = 4`.
6. **No in-chart title** — `PlotModel.Title` is no longer set; the map name is
   already in the window title (`Text = "Kennfeld: …"`) and in the Reloaded
   panel header. This mirrors the original, whose chart has no title.
7. **Axis titles corrected** — the horizontal axis is now titled from
   `sh.X_axis_descr` and the vertical from `sh.Y_axis_descr`, which is what
   `frmMain.cs` does (`X_axis_name = sh.X_axis_descr`, `Y_axis_name =
   sh.Y_axis_descr`) while the values still come from the crossed addresses
   (`X_axisAddress = sh.Y_axis_address`). Before this change the titles were
   swapped: the 0..40 IQ axis was labelled "Engine speed (rpm)" and the
   0..5000 rpm axis "IQ (mg/stroke)". Only label strings moved; the arrays,
   addresses and decode are unchanged.
8. **No duplicated unit suffix** — the unit is appended only when the parsed
   description does not already contain it, so the captions read exactly like
   the old header: "IQ (mg/stroke)" and "Engine speed (rpm)" instead of
   "IQ [mg/stroke] [mg/st]" / "Engine speed (rpm) [rpm]".

## Before / after

Before: Jet ramp (blue low end), hard cell rectangles, boxed plot area,
OxyPlot default font, in-chart title.

After: dark-green → yellow → orange → red ramp, smooth shading, white plot,
black thin axes with inward ticks, Tahoma 8, no in-chart title, correct
IQ/rpm axis titles. `chartmatch-compare.png` shows it next to the original.

## What matches

- Colour ramp order and anchor colours (measured pixel values in the new
  render: `(8,131,0)`, `(248,251,0)`, `(255,164,0)`, `(255,33,0)` — the same
  five anchors as `FillData`).
- White chart background; black axis/tick/label colour; no chart title; no
  legend box; small Tahoma labels.
- Axis orientation and titles now match the original table header
  (X = IQ mg/stroke 0..40, Y = engine speed rpm).

## What cannot be matched (OxyPlot 1.0 / 2D heat map limits)

- **3D surface geometry and wireframe.** The old chart is a Nevron
  `NMeshSurfaceSeries` viewed in 3D, with a black mesh over the shaded surface
  and a receding floor/back wall. OxyPlot 1.0 has only a flat
  `HeatMapSeries`; there is no 3D projection, no camera and no per-cell border
  that could draw the wireframe. The smooth bitmap shading is the closest
  approximation.
- **Beige interlaced strip / dotted back-wall grid** of the 2D line view.
  Those are Nevron wall decorations; OxyPlot's plot area is covered by the
  heat map, so grid/strip styling would be invisible or would only add noise.
- **No colour scale** in the original. The Reloaded viewer keeps its
  `LinearColorAxis` on the right (black, 8 pt) because a heat map without a
  value scale is hard to read; this is the one deliberate visual addition.
  It can be removed by deleting the `LinearColorAxis` from `BuildModel` if an
  exact match is preferred over readability.
- **Nevron evaluation watermark** in the original screenshot is a licence
  artefact, not a style element, and is not reproduced.
