# UI research — replacing DevExpress with Krypton + OxyPlot on .NET 4.0

Status: **verified with a compiled, running sample** (see `Reloaded/`).

## Goal

The current VAGSuite UI depends on DevExpress WinForms (v11.2) and Nevron
Charts. DevExpress is commercial and its v11.2 controls are dated. This
research answers one question: can a WinForms app, built **without**
DevExpress, use the **free Krypton Toolkit** (ribbon) and **OxyPlot** (chart)
while still targeting **.NET Framework 4.0** — the last framework supported on
Windows XP SP3, and still supported on Windows 11 — and be built/run under
**Wine** on this Linux machine?

The answer is **yes**, with a specific, pinned set of package versions.

## Method

All facts below are read directly from the NuGet packages (their `.nupkg`
archives are ZIPs; `lib/` folder lists and the `.nuspec` licence/dependency
metadata are authoritative). Packages were fetched from
`https://api.nuget.org/v3-flatcontainer/`.

## Package findings

### Krypton (BSD-3-Clause)

| Version | `lib/` target frameworks shipped | Lowest .NET FW |
|---|---|---|
| **5.550.2108.1** (chosen) | net35, net40, net45, net451, net452, net46, net461, net462, net47, net471, net472, net48, net5.0-windows7.0, netcoreapp3.1 | **net35** |
| 5.490.2200 | net40, net45 … net48, netcoreapp3.1 | net40 |
| 110.26.11.306 (newest) | net472, net48, net481, net8.0-windows7.0, net9.0-windows7.0, net10.0-windows7.0, net11.0-windows7.0 | **net472** |

Key finding — **the ribbon is a separate package**:

- `Krypton.Toolkit` (5.550.2108.1) ships `Krypton.Toolkit.dll`. Its `.NETFramework4.0`
  dependency group is **empty** — no `System.Drawing.Common`, no other NuGet
  package. It only declares the framework assembly `System.Design` (designer
  support, in the .NET Framework GAC).
- The ribbon control (`KryptonRibbon`, `KryptonRibbonTab`, `KryptonRibbonGroup`,
  `KryptonRibbonGroupButton`, …) lives in a **second package, `Krypton.Ribbon`**
  (assembly `Krypton.Ribbon.dll`, namespace `Krypton.Ribbon`), which depends on
  `Krypton.Toolkit` of the exact same version.

Both packages ship `net40` (and `net35`) at version **5.550.2108.1**, so the
ribbon + toolkit combination runs on .NET 4.0. The newest Krypton (110.x) drops
everything below `net472`, so it does **not** run on XP.

### OxyPlot (MIT)

| Version | `lib/` target frameworks shipped | Lowest .NET FW |
|---|---|---|
| OxyPlot.Core **1.0.0** (chosen) | net40, net40-client, net45, portable-…, sl5 | **net40** |
| OxyPlot.WindowsForms **1.0.0** (chosen) | net40, net45 | **net40** |
| OxyPlot.WindowsForms 2.0.0 | net45, netcoreapp3.0 | net45 |
| OxyPlot.Core 2.2.0 | net462, net6.0, net8.0, netstandard2.0 | net462 |
| OxyPlot.WindowsForms 2.2.0 | net462, net6.0-windows7.0, net8.0-windows7.0 | **net462** |

`OxyPlot.WindowsForms` 1.0.0 depends on `OxyPlot.Core` 1.0.0 (exact). The 1.0.0
line ships a `net40` build; the 2.x line starts at `net45` (2.0.0) / `net462`
(2.2.0), so only 1.0.0 keeps XP reachable.

## Licence

- **Krypton** — BSD-3-Clause. Verified in the `.nuspec`
  (`<license type="expression">BSD-3-Clause</license>`) and in the `License.md`
  shipped in the newer package ("BSD 3-Clause License", Krypton Suite).
- **OxyPlot** — MIT. Verified in the `.nuspec`
  (`<license type="expression">MIT</license>`) and in the `LICENSE` file shipped
  in the 1.0.0 package ("The MIT License (MIT)", OxyPlot contributors).

Both are permissive and fine to redistribute in binary form.

## Verdict on net40 / XP feasibility

Feasible, with pinned versions: **Krypton.Toolkit 5.550.2108.1 +
Krypton.Ribbon 5.550.2108.1 + OxyPlot.Core 1.0.0 + OxyPlot.WindowsForms 1.0.0**.
All four ship genuine `net40` (Krypton even `net35`) assemblies, which is what
makes Windows XP SP3 (.NET 4.0) reachable; a `Reloaded/` sample using these
exact DLLs compiles with the in-box .NET 4.0 `csc.exe` under Wine and runs under
Wine, rendering the ribbon and the two-series chart (see
`Reloaded/evidence/screenshot-window.png`). On real Windows the same `net40`
assemblies load on .NET 4.0 and above, so the app targets XP SP3 through
Windows 11. The trade-off is that both libraries are pinned to their last
`.NET-4.0-compatible` lines (Krypton 5.550, 2021; OxyPlot 1.0.0, 2016).

## Minimal WinForms controls still needed

The rest of the port is covered by standard, framework-shipped or freely
licensed components:

- **Grid** — `System.Windows.Forms.DataGridView` (built into .NET 4.0; replaces
  the DevExpress grid for the map/table views).
- **Docking** — **DockPanelSuite** (WeifenLuo, MIT) for the docking layout; add
  later. (Krypton also ships a `Krypton.Docking` module if a themed dock is
  preferred, but DockPanelSuite is the common standalone choice.)

## Open risks

1. **Wine runtime ≠ Microsoft CLR.** Under Wine the sample runs on wine-mono
   (Mono's .NET 4.0 profile), not the real .NET Framework. The `net40` assemblies
   are what make XP viable; the Wine run is a strong but not identical proxy.
   A real XP SP3 / Windows 11 smoke test is the remaining unknown.
2. **Pinned old versions.** Krypton 5.550 (2021) and OxyPlot 1.0.0 (2016) are
   the last lines with `net40`. They are stable but effectively frozen; moving to
   a newer .NET (4.6.2+/4.8) would unlock current OxyPlot 2.x and Krypton 110.x.
3. **OxyPlot API drift.** 1.0.0 uses `LineSeries.Points`; 2.x uses `ItemsSource`.
   Porting code forward requires touching every series setup.
4. **Krypton designer dependency.** `Krypton.Toolkit`/`Krypton.Ribbon` declare
   `System.Design` (designer support). The sample runs without using the designer,
   but designer-tooling parity on the Linux/Wine side is not guaranteed.
5. **Rendering fidelity.** Both libraries render correctly in the sample under
   Wine, but theme/DPI edge cases (especially XP's classic theme and high-DPI)
   are untested.

## Evidence locations

- `Reloaded/README.md` — package/version/evidence table and build/run steps.
- `Reloaded/evidence/screenshot-window.png` — the running window (ribbon + chart).
- `Reloaded/evidence/screenshot-fullscreen.png` — full desktop capture.
- `Reloaded/evidence/console-run.log` — console output of the run (no exceptions).
