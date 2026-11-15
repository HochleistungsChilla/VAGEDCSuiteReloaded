# Feature parity — old VAGEDCSuite vs Reloaded

This is the parity checklist: each feature of the original DevExpress app
(`EDCSuite_merged.exe`, `master`) against the Reloaded rewrite (`Reloaded/`,
branch `reloaded-ui`). Reloaded is a **port** of the forked sources: the
parser/decode/checksum/label logic is inherited, only the UI is rebuilt on a
Krypton/OxyPlot WinForms stack.

Legend: ✅ done · ⏳ partial · ❌ missing

## Core

| Feature | old | Reloaded | Notes |
|---|---|---|---|
| Open / detect file (128 KB + 256 KB EDC15V-5) | ✅ | ✅ | `Edc15V256Layout` + `EDC15VM256Checksum` |
| Part number / software / Bosch / info extraction | ✅ | ✅ | title + status bar |
| Map list (Kennfelder) as tree (Category → Subcategory → map) | ✅ | ✅ | left tree, address + size |
| Block table (Block 1/2/3 + gearbox) | ✅ | ✅ | behind Views → Blocks |
| Checksum status (per block) | ✅ | ✅ | report + status bar "Checksum Ok." |
| Hex viewer | ✅ | ✅ | `HexViewerControl` (GDI+) |
| Dark / light theme | ⏳ (skins) | ✅ | Krypton palette |
| i18n (language switch) | ⏳ (nl resource only) | ✅ | EN + DE, locale files |
| Ribbon minimize / collapse | ✅ | ✅ | collapsed by default |

## Map viewer

| Feature | old | Reloaded | Notes |
|---|---|---|---|
| 2D heatmap (color ramp) | ✅ (Nevron) | ✅ | OxyPlot, matched green→red ramp (kept for 2D view) |
| **Coloured table = the heatmap** | ✅ (grid) | ✅ | every cell carries its value's colour from the **same** ramp |
| **View layout (table / chart / both)** | ✅ | ✅ | "Nur Tabelle" / "Nur Diagramm" / "Beides", HKCU-persisted, default "Beides" |
| **3D surface view** | ✅ (Nevron 3D) | ✅ | GDI+ Z-buffer, 72–77 fps, rotate/zoom/reset, 2D/3D toggle |
| Axis ranges / labels | ✅ | ✅ | real axis values |
| Color scale / legend | ⏳ | ⏳ | original had none; Reloaded has colorbar |
| **Cell values visible (per cell)** | ✅ | ✅ | coloured table (the grid is the heatmap) |
| **Cell editing** | ✅ | ⏳ | edit + undo + limits done; file write-back pending |
| Zoom / pan / rotate | ✅ | ⏳ (2D only) | 3D rotation pending |

## Tuning / write path

| Feature | old | Reloaded | Notes |
|---|---|---|---|
| **Target block selection (All / Block 1 / 2 / 3)** | ✅ | ✅ | View → Zielblock, persisted TargetBlockMask |
| Apply map edit to selected target blocks | ✅ | ✅ | temp-copy apply + replicate + reseal |
| Write checksum after edit | ✅ | ✅ | `EDC15VM256Checksum.FixSelected` + `Verify` 3/3 |
| Undo / redo | ✅ | ⏳ | cell undo done; no global undo |
| Plausibility limits (min/max per map) | ✅ | ✅ | 78643 / 255 / hex bounds (MapViewerEx rule) |

## File / project

| Feature | old | Reloaded | Notes |
|---|---|---|---|
| Save file | ✅ | ⏳ | temp-copy apply + explicit save done; no project/transaction log |
| Merge / split (low/high chip) | ✅ | ✅ | `ChipMergeSplit` + MergeSplitForm |
| Projects (create/open/close) | ✅ | ❌ | |
| Transaction log / project logbook | ✅ | ❌ | `TransactionLog`/`ProjectLog` exist |
| DAMOS label load/apply (UI) | ✅ | ✅ | File → Labels → Load .labels, re-apply via MapList |

## Other (deferred, lower priority)

| Feature | old | Reloaded | Notes |
|---|---|---|---|
| Dyno | ✅ | ❌ | |
| Skins (extra themes) | ✅ | ⏳ (2 themes) | |
| Excel export | ✅ | ❌ | stub exists for build |
| Update checker | ✅ | ❌ | `UpdateChecker.cs` exists |
| VIN decoder | ✅ | ❌ | `VINDecoder.cs` exists |
| XDF export | ✅ | ❌ | `XDFWriter.cs` exists |
| Logbook / notes | ✅ | ❌ | |

## Open items (next)

1. ~~Cell view + editing~~ — **done** (grid, select, edit, undo, limits,
   write-back).
2. ~~Target-block selection + write path~~ — **done** (ribbon group, temp-copy
   apply + replicate + reseal + verify).
3. ~~3D surface view~~ — **done** (GDI+ Z-buffer, 72–77 fps, integrated with
   2D/3D toggle + rotate/zoom/reset).
4. ~~Merge/split~~ + ~~DAMOS label UI~~ — **done**. Remaining: projects,
   dyno/Excel/XDF/VIN, transaction log (lower priority).

## 3D approach decision

OxyPlot 1.0.0 cannot draw 3D surfaces. The original uses a Nevron 3D surface.
Two candidate approaches for a .NET 4.0 / XP / Wine WinForms app:

- **(a) GDI+ self-implementation** — isometric projection, painter's algorithm
  (or a tiny Z-buffer), flat-shaded quads colored by the map ramp, mouse rotate /
  zoom. Dependency-free, XP-safe, fully controllable; more code but matches the
  "no new native deps" constraint.
- **(b) OpenTK / HelixToolkit** — HelixToolkit is WPF-only (not usable in a
  WinForms app without a WPF host, which is heavy on XP). OpenTK 1.x targets
  net40 but is a raw GL wrapper (still need to write the surface mesh + shading
  + interaction). OpenTK adds a native dependency and a GL context under Wine.

Decision: **(a) GDI+ self-implementation** — dependency-free, keeps the
net40/XP/Wine target, and a 3D surface of a rectangular grid is simple enough
for a small painter's-algorithm renderer. Verified feasibility pending a spike.
