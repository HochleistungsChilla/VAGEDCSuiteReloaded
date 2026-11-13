# Reloaded — Krypton ribbon + OxyPlot on .NET Framework 4.0

A minimal, self-contained proof that a WinForms app can replace DevExpress with
the **free Krypton Toolkit** (ribbon) and **OxyPlot** (chart) and still target
**.NET Framework 4.0** — the last framework that runs on Windows XP SP3, and
still runs on Windows 11. The sample is compiled and run **under Wine** on this
Linux machine.

## What it proves

One form (`Main.cs`) that:

1. hosts a **Krypton ribbon** — one tab ("Home"), one group ("Actions /
   Sample group") and two buttons ("Button 1 / First", "Button 2 / Second");
2. hosts an **OxyPlot `PlotView`** with two line series ("Series A" blue,
   "Series B" red).

It compiles with the in-box .NET 4.0 C# compiler under Wine and runs under
Wine, rendering both the ribbon and the chart. See `evidence/`.

## Packages and target frameworks (evidence)

| Package | Version | Lowest .NET FW folder | Folders actually shipped | Licence |
|---|---|---|---|---|
| Krypton.Toolkit | 5.550.2108.1 | **net35** | net35, net40, net45, net451, net452, net46, net461, net462, net47, net471, net472, net48, net5.0-windows7.0, netcoreapp3.1 | BSD-3-Clause |
| Krypton.Ribbon | 5.550.2108.1 | **net35** | net35, net40, net45 … net48, net5.0-windows7.0, netcoreapp3.1 | BSD-3-Clause |
| OxyPlot.Core | 1.0.0 | **net40** | net40, net40-client, net45, portable-net45+netcore45+wpa81+wp8+MonoAndroid1+MonoTouch1+Xamarin.iOS10, sl5 | MIT |
| OxyPlot.WindowsForms | 1.0.0 | **net40** | net40, net45 | MIT |

> **Important:** the ribbon control is **not** inside `Krypton.Toolkit`. It is a
> separate package, `Krypton.Ribbon`, in its own assembly and namespace
> (`Krypton.Ribbon.KryptonRibbon`). `Krypton.Ribbon` depends on
> `Krypton.Toolkit` (exact version 5.550.2108.1). `Krypton.Toolkit` itself has
> **no** NuGet package dependencies on .NET 4.0 (the `.NETFramework4.0`
> dependency group in its nuspec is empty); it only declares the framework
> assembly `System.Design`.

### The XP-critical question

- **Krypton** runs on .NET 4.0: version **5.550.2108.1** ships a `net40` build
  (and even `net35`). The **newest** Krypton (`110.26.11.306`) ships only
  `net472` and above — it would **not** run on XP.
- **OxyPlot** runs on .NET 4.0 only in the **1.0.0** line (`net40`). OxyPlot
  `2.0.0` ships `net45`, and `2.2.0` ships `net462` — neither runs on XP.

So the pinned set (Krypton 5.550.2108.1 + OxyPlot 1.0.0) is the combination
that keeps `.NET 4.0 / XP SP3` reachable.

## Build

```sh
cd Reloaded
./build.sh
```

`build.sh` invokes the same compiler the main project uses:

```
wine "C:\windows\Microsoft.NET\Framework\v4.0.30319\csc.exe" \
    /nologo /target:winexe /out:Reloaded.exe \
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll \
    /r:System.Windows.Forms.dll /r:System.Design.dll \
    /reference:lib/Krypton.Toolkit.dll \
    /reference:lib/Krypton.Ribbon.dll \
    /reference:lib/OxyPlot.dll \
    /reference:lib/OxyPlot.WindowsForms.dll \
    Main.cs
```

Notes:

- The in-box `csc.rsp` is empty under Wine, so the BCL references
  (`System.Windows.Forms`, `System.Drawing`, …) must be listed explicitly
  (bare names, resolved from the reference-assembly directory — same approach
  as `tools/mkrsp.py`).
- After compiling, the private assemblies are copied next to `Reloaded.exe`
  (the `CopyLocal` step an MSBuild build performs via `<Private>true</Private>`).
  The .NET Framework CLR — and Mono's loader, which Wine uses — probes the
  application base directory, not `lib/`.
- `Reloaded.csproj` is the equivalent MSBuild description (`TargetFrameworkVersion
  = v4.0`, `HintPath` → `lib\*.dll`) for reference / future MSBuild builds.

## Run

```sh
cd Reloaded
DISPLAY=:0 XAUTHORITY=/run/user/1000/xauth_hoedEO wine Reloaded.exe
```

The window is 960×640, ribbon on top, chart filling the rest. See
`evidence/screenshot-window.png` and `evidence/console-run.log`.

## Layout

```
Reloaded/
  Main.cs                 the form + entry point
  Reloaded.csproj         MSBuild description (net40, for reference)
  build.sh                Wine csc build
  lib/                    extracted reference assemblies (source of truth)
  Reloaded.exe + *.dll    build output (assemblies copied next to the exe)
  evidence/               console log + screenshots
  README.md
```

## Notes / caveats

- Under Wine the runtime is **wine-mono** (Mono's .NET 4.0 profile), not the
  Microsoft .NET Framework CLR. The `net40` assemblies are the evidence that
  makes XP SP3 (real .NET 4.0) reachable; the Wine run exercises the code on
  this machine's Mono runtime, which is a strong but not byte-for-byte-identical
  proxy. See `Docs/RESEARCH_UI.md` for the open risks.
- OxyPlot 1.0.0 is old (2016); its `LineSeries` uses a `Points` collection,
  whereas 2.x uses `ItemsSource`. API porting is needed when moving forward.
- `Krypton.Ribbon` declares `System.Design` as a framework-assembly dependency
  (designer support). The sample does not use the designer and runs without it.
