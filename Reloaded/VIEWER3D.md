# 3D surface view for the Kennfeld viewer

Status: 2026-11-14, branch `reloaded-ui`. **Not committed** — the changes are in
the working tree.

The inline map viewer can now show the selected map as a shaded, rotate-and-zoom
**3D surface** in addition to the OxyPlot 2D heat map. The surface is drawn with
plain GDI+ (`System.Drawing`) + WinForms — no OpenGL, no OxyPlot, no native
code — so it runs in the same `net40`/Wine environment as the rest of Reloaded.

| File | Role |
|---|---|
| `Reloaded/Gdi3dSurface.cs` | new — `SurfaceView : Control`, the renderer |
| `Reloaded/MapViewForm.cs` | 2D/3D toggle, header strip, palette, reparenting |
| `Reloaded/build.sh` | source list (`Gdi3dSurface.cs`) |
| `Reloaded/Reloaded.csproj` | `<Compile Include="Gdi3dSurface.cs" />` |
| `Reloaded/VIEWER3D.md` | this note |
| `Reloaded/evidence/ev-*.png`, `3d-fps-*.log` | verification, gitignored |

Build as always:

```
cd Reloaded && bash build.sh        # wine csc, net40, exit 0
```

## What you get

* A slim strip above the chart with two buttons: **`3D`** (switches to the
  surface; the button then reads **`2D`**) and **`Reset`** (default camera).
* The surface renders **the real map**: the very `double[,]` instance the heat
  map and the cell editor share, plus the real X/Y axis arrays and the axis
  captions (including the unit, exactly as the 2D chart builds them). Nothing is
  copied and nothing is synthetic, so a committed cell edit shows up in the 3D
  view the next time it is painted.
* The frame carries the real axis values as tick labels (first / middle / last
  cell per axis, e.g. `10 / 2500 / 10000` on X and `0 / 1113 / 5355` on Y for
  "Driver's Wish"), plus the value range on Z (`0 … 5600`).
* HUD: dimensions, value range, axis captions, yaw/pitch/zoom, smoothed paint
  time and (while interacting) the measured fps.

### 2D ⇄ 3D switching

`SetSurfaceMode` swaps two docked `Fill` panels (`PlotView` and the surface
panel) and hides the `MapCellGrid` while 3D is active, so the surface gets the
whole viewer pane instead of a 170 px strip. Switching back restores the heat
map **and** the cell editor untouched — the grid instance is never rebuilt.

The choice is stored in a static field (`_surfacePreferred`), i.e. it is
remembered for the rest of the process/session: after switching to 3D, the next
map opens in 3D as well. Nothing is persisted to disk.

`Main.OpenMapInline` pulls the `PlotView` out of `MapViewForm` and disposes the
shell, so the surface panel and the header follow the plot into its new parent
(`PlotView.ParentChanged`) and are disposed together with it
(`PlotView.Disposed`) — the same lifetime trick the cell grid already used.
Dock order matters: the `Fill` panel is added *before* the header/grid/footer
so the bottom-docked controls take their space first (adding it last made
WinForms give it the full client height, which left the surface centred far
below the visible pane — fixed during verification).

## Rendering

The proven spike pipeline is unchanged (`tmp/gdi3d-spike`, see its `notes.md`):

* grid → world `x = (u-0.5)*2.4`, `y = (0.5-v)*1.2`, `z = (value-0.45)*0.9`
  with the value normalised to the map's own min/max;
* yaw around Z, pitch around X, perspective with the camera at `(0,0,4.2)`:
  `d = 4.2 - z2`, `s = min(w,h)*1.45*zoom/d`, `sx = w/2 + x1*s`,
  `sy = h/2 - y2*s`;
* **software Z-buffer** (`int[]` colour + `float[]` 1/depth, two triangles per
  quad, barycentric weights, `LockBits` + `DrawImageUnscaled`) — painter's
  algorithm was wrong at steep folds;
* flat shading per quad from the average value: green `(24,148,68)` → yellow
  `(236,214,48)` → orange `(248,140,24)` → red `(202,28,28)`, times
  `0.68 + 0.32*lambert` with a fixed camera-space light;
* 1 px depth-tested wireframe; **no backface culling** (open heightfield — the
  Z-buffer already prevents overdraw).

Additions for the integration (none of them changes that algorithm):

* `_zoom` starts as a **fit zoom** so the default view fills a short, wide
  viewer pane (the projection is linear in zoom, so the world box measured at
  zoom 1 gives the exact factor). The user's wheel zoom takes over from there;
  `Reset` returns to the default yaw/pitch and re-fits.
* Real axis tick labels and axis captions, projected into the frame.
* A `1 x N` / `N x 1` map (a Kennlinie) has no quads; it is drawn as a
  depth-tested 3D polyline with node markers instead of an empty frame. A
  `1 x 1` map shows a single marker. This came out of verification: the first
  dark run accidentally opened an 11x1 map and the surface threw
  `IndexOutOfRangeException` (a single-row grid was clamped to two rows).
* Repaints happen **on demand only**: drag, wheel, `Reset`, resize, palette
  change, or switching back to 3D. There is no timer and no animation; a static
  view has zero CPU cost. Every cell is drawn — no downsampling anywhere.

## Interaction

| Input | Effect |
|---|---|
| left-drag | rotate: yaw free, pitch clamped to 6°…83° (grab-hand cursor) |
| right-drag / middle-drag | pan the surface in screen space |
| mouse wheel | zoom 0.35×…3.5× **on the cursor** (1.12× per notch, crosshair cursor) |
| double-click / `Reset` button / `Home` | default view (yaw −36°, pitch 53°, fit zoom, no pan) |
| `3D` / `2D` | switch view mode |

A plain `Control` only gets `WM_MOUSEWHEEL` while it has focus, so a thread-wide
`IMessageFilter` forwards wheel messages that happen over the surface (removed
again on dispose). Dragging also takes focus, so the usual click-then-wheel
works regardless. The zoom anchor is the point under the cursor: the pan offset
is recomputed so that point stays fixed while the zoom changes.

## Theme

The palette is derived from the viewer's background with the same luminance
rule `MapCellGrid` uses (`MapViewForm.ApplyHostPalette`): dark surface → dark
3D background `(26,27,32)` and light text; light surface → background
`(250,250,251)` and dark text. The wireframe is a fixed crisp black over the
shaded surface (the original Nevron look), the same in both themes.

The surface re-reads the host colour on every paint **and** subscribes to the
host's `BackColorChanged`, so a theme switch repaints it immediately. Verified
by launching with `--theme=light` and `--theme=dark` (screenshots below).

## Performance (measured, Xvfb :93, Wine 11.21, 724×560 pane)

| Map | Grid | Quads | Paint time | fps while dragging |
|---|---|---|---|---|
| Driver's Wish | 8 × 16 | 105 | 9.1–9.6 ms | 72–76 |
| N146 Pump voltage map | 17 × 16 | 240 | 7.4–8.0 ms | 77 |
| spike reference | 64 × 32 | 1953 | 15.6 ms | 54 |

First paint of a map is ~14–15 ms (JIT/font warm-up); later frames are the
figures above. The cost is dominated by the full-canvas clear + blit + text,
not by the quad count. Roughly 78 fps is the measured interactive ceiling with
xdotool-generated drags; the renderer itself would allow ~110 fps at 9 ms/frame.

Numbers come from `RELOADED_3D_FPS_LOG=<file>` (opt-in diagnostic: one line per
0.5 s window, `fps`, smoothed paint ms, grid) — see
`evidence/3d-fps-light.log` and `evidence/3d-fps-dark.log`. The same line is
shown in the HUD while interacting.

## Verification (headless, reproducible)

```
Xvfb :93 -screen 0 1280x900x24 &
DISPLAY=:93 wine explorer /desktop=Reloaded93,1140x800 \
  "Z:\\home\\fran\\Dokumente\\edcsuite-merged\\Reloaded\\Reloaded.exe" \
  --theme=dark "Z:\\tmp\\ga-work\\ga.stage1"
```

Then: expand `Labeled maps`, open `Driver's Wish`, click `3D`, drag, wheel,
`Reset`, open a second map (stays in 3D), switch back to `2D`.

Evidence in `Reloaded/evidence/` (gitignored):

* `ev-light-2d.png` / `ev-dark-2d.png` — the map in the 2D heat map before the
  switch (same map, same axis ranges as the surface).
* `ev-light-3d.png`, `ev-dark-3d.png` — 3D default view, yaw −36°, pitch 53°,
  zoom 1.17×, values `0 … 5600`, axis ticks `10/2500/10000` and `0/1113/5355`.
* `ev-*-3d-rotated.png` — after a left-drag (yaw 38°, pitch 83°).
* `ev-*-3d-zoom.png` — after five wheel notches (zoom 2.06×).
* `ev-*-3d-reset.png` — after `Reset` (back to −36°/53°/1.17×).
* `ev-*-3d-secondmap.png` — `N146 Pump voltage map` (17 × 16) opened while in
  3D: proves the mode is remembered and the denser grid renders.
* `ev-dark-3d-17x16.png` — the 17 × 16 map after a drag (fps measurement run).
* `ev-*-back2d.png` — back to the heat map **with the cell editor restored**.

2D and 3D agree on the data: the green low-value region sits at low X / high Y
and the red high-value region at high X in both views.

## Known limits

* Flat shading, 1 px un-antialiased wireframe (identical to the spike).
* Composition follows the spike's pinned projection, which magnifies the near
  edge: the surface sits slightly below/right of the pane centre, not exactly
  centred. The formula was deliberately left untouched.
* At extreme pitch/yaw the projected tick labels can still touch the surface
  edge; the three ticks per axis are a readability compromise for small panes.
* The 3D mode hides the cell editor (by design, to give the surface the pane);
  editing is a 2D-mode task.
* The mode is remembered per process only, not across restarts.
* The close-map path (`Main.ClearInlineMap` → plot disposed → surface/header
  disposed) is implemented and code-reviewed, but the headless run only
  exercised the 2D/3D switching path.
