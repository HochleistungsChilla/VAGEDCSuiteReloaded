//---------------------------------------------------------------------------
//
// Reloaded/MapViewForm.cs
//
// Kennfeld (map) viewer: renders one map of an EDC15V binary as a
// colour-coded 2D heat map with OxyPlot 1.0.0 (net40).
//
// The editable cell table below the chart is itself the heat map now: every
// cell carries the background colour of its value, taken from the SAME ramp
// as the chart (MapColorRamp), and the viewer has three layouts —
// "Nur Tabelle" / "Nur Diagramm" / "Beides" (table above, chart below) —
// persisted under HKCU like the theme and the target-block mask.
//
// The form itself is dumb: it consumes metadata plus a rectangular value
// grid and a pair of axis-value arrays.  MapViewForm.FromBinary does the
// binary reading with exactly the calls the old DevExpress MapViewerEx path
// uses in frmMain.cs:
//
//   map bytes   Tools.Instance.readdatafromfile(path, Flash_start_address,
//               Length, EDCFileType.EDC15V)
//   X cells     Tools.Instance.readdatafromfileasint(path, Y_axis_address,
//               Y_axis_length, EDCFileType.EDC15V)       // frmMain: X_axisvalues
//   Y cells     Tools.Instance.readdatafromfileasint(path, X_axis_address,
//               X_axis_length, EDCFileType.EDC15V)       // frmMain: Y_axisvalues
//
// and MapViewerEx.ShowTable's decode: rows of Y_axis_length cells, 16-bit
// values with the high byte first and values above 0xF000 interpreted as
// negative (0x10000 - b).  Axis corrections mirror MapViewerEx's
// ConvertXAxisValue/ConvertYAxisValue (factor/offset applied only when the
// factor is not 1) and the X-axis 0xF000 sign fix.
//
// OxyPlot 1.0 notes (spike-verified):
//   * PlotModel has only a parameterless constructor.
//   * LabelFontSize > 0 renders the whole plot black under wine-mono, so the
//     cell labels stay off (LabelFontSize = 0).
//   * HeatMapSeries.Data is indexed [x, y]: first dimension along the X axis.
//   * The colour bar is a LinearColorAxis at AxisPosition.Right; its Min/Max
//     are fed automatically from the data.
//
// Visual style (see CHARTMATCH.md): the old DevExpress/Nevron map chart is a
// white chart with thin black axes, black tick labels, no in-chart title and
// no legend.  Its surface palette is the fixed five-anchor ramp
// Green -> Yellow -> Orange -> OrangeRed -> Red (MapViewerEx.FillData), not a
// rainbow/Jet ramp.  Everything below only styles the plot; the decoded
// values stay untouched.
//
// Cell editor (2026-11-14, see CELLEDITOR.md): below the heat map the form
// hosts a MapCellGrid, a plain DataGridView with one column per X value and
// one row per Y value.  It edits the same double[,] array the heat map draws,
// so a committed edit instantly recolours the chart.  Editing is validated
// with the original app's own rule (MapViewerEx.gridView1_ValidatingEditor:
// 16-bit -> |value| <= 78643, 8-bit -> value <= 255) and every commit can be
// undone with Ctrl+Z (per-grid in-memory stack).
//
// MapCellGrid has to survive MapViewForm's disposal: Main.OpenMapInline pulls
// the PlotView out of this form and disposes the shell.  The grid therefore
// follows the PlotView into its new parent (PlotView.ParentChanged) and
// disposes itself when the PlotView is disposed.
//
//---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using VAGSuite;

namespace Reloaded
{
    // ------------------------------------------------------------------
    // Shared colour ramp (heat map + coloured table) and the viewer
    // layout choice (table / chart / both).
    // ------------------------------------------------------------------

    /// <summary>
    /// The fixed five-anchor surface ramp (Green -> Yellow -> Orange ->
    /// OrangeRed -> Red) shared by the OxyPlot heat map and the coloured cell
    /// table.  One function is the single source of truth for the scale, so
    /// the table and the chart can never drift apart.
    /// </summary>
    internal static class MapColorRamp
    {
        private static readonly Color[] Anchors =
        {
            Color.FromArgb(0, 128, 0),      // Green
            Color.FromArgb(255, 255, 0),    // Yellow
            Color.FromArgb(255, 165, 0),    // Orange
            Color.FromArgb(255, 69, 0),     // OrangeRed
            Color.FromArgb(255, 0, 0)       // Red
        };

        /// <summary>256-step OxyPlot palette built from the shared anchors.</summary>
        public static OxyPalette Palette256()
        {
            OxyColor[] oxy = new OxyColor[Anchors.Length];
            for (int i = 0; i < Anchors.Length; i++)
                oxy[i] = OxyColor.FromRgb(Anchors[i].R, Anchors[i].G, Anchors[i].B);
            return OxyPalette.Interpolate(256, oxy);
        }

        /// <summary>Interpolated colour of <paramref name="value"/> in [min,max].</summary>
        public static Color ColorFor(double value, double min, double max)
        {
            if (max <= min) return Anchors[0];
            double t = (value - min) / (max - min);
            if (t < 0.0) t = 0.0;
            if (t > 1.0) t = 1.0;
            double pos = t * (Anchors.Length - 1);
            int lo = (int)Math.Floor(pos);
            int hi = lo + 1;
            if (hi >= Anchors.Length) hi = Anchors.Length - 1;
            double frac = pos - lo;
            Color a = Anchors[lo];
            Color b = Anchors[hi];
            int r = (int)Math.Round(a.R + (b.R - a.R) * frac);
            int g = (int)Math.Round(a.G + (b.G - a.G) * frac);
            int bl = (int)Math.Round(a.B + (b.B - a.B) * frac);
            return Color.FromArgb(r, g, bl);
        }

        /// <summary>Black or white text, whichever reads better on <paramref name="background"/>.</summary>
        public static Color TextColorFor(Color background)
        {
            int luminance = (background.R * 299 + background.G * 587 + background.B * 114) / 1000;
            return luminance < 140 ? Color.White : Color.Black;
        }
    }

    /// <summary>How the map viewer lays out the coloured table and the chart.</summary>
    internal enum MapViewMode
    {
        TableOnly = 0,   // "Nur Tabelle"
        ChartOnly = 1,   // "Nur Diagramm"
        Both = 2         // "Beides" (default)
    }

    /// <summary>
    /// Persisted viewer layout (HKCU\Software\VAGEDCSuiteReloaded\MapViewMode),
    /// defaulting to <see cref="MapViewMode.Both"/>.  The same key, pattern and
    /// DWORD storage as <see cref="TargetBlockStore"/> and the theme store.
    /// </summary>
    internal static class ViewModeStore
    {
        private const string KeyPath = @"Software\VAGEDCSuiteReloaded";
        private const string ValueName = "MapViewMode";

        private static bool _loaded;
        private static MapViewMode _mode = MapViewMode.Both;
        private static bool _overrideSet;
        private static MapViewMode _override;

        /// <summary>Raised after the persisted mode changed.</summary>
        public static event EventHandler ModeChanged;

        /// <summary>Transient CLI override (--view-mode=...); never persisted.</summary>
        public static void ForceForSession(MapViewMode mode)
        {
            _overrideSet = true;
            _override = mode;
        }

        /// <summary>The mode to use now: the CLI override if set, else the persisted value.</summary>
        public static MapViewMode Current
        {
            get { return _overrideSet ? _override : Mode; }
        }

        /// <summary>Persisted mode (default <see cref="MapViewMode.Both"/>).</summary>
        public static MapViewMode Mode
        {
            get
            {
                EnsureLoaded();
                return _mode;
            }
            set
            {
                EnsureLoaded();
                MapViewMode normalized = value;
                if ((int)normalized < (int)MapViewMode.TableOnly) normalized = MapViewMode.TableOnly;
                if ((int)normalized > (int)MapViewMode.Both) normalized = MapViewMode.Both;
                if (normalized == _mode) return;
                _mode = normalized;
                Save();
                EventHandler handler = ModeChanged;
                if (handler != null) handler(null, EventArgs.Empty);
            }
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            _mode = Load();
        }

        private static MapViewMode Load()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    object stored = key == null ? null : key.GetValue(ValueName);
                    if (stored is int)
                    {
                        int mode = (int)stored;
                        if (mode >= (int)MapViewMode.TableOnly && mode <= (int)MapViewMode.Both)
                            return (MapViewMode)mode;
                    }
                }
            }
            catch
            {
                // Registry unavailable -> default below.
            }
            return MapViewMode.Both;
        }

        private static void Save()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    if (key != null) key.SetValue(ValueName, (int)_mode, RegistryValueKind.DWord);
                }
            }
            catch
            {
                // Best effort; the layout still applies for this session.
            }
        }
    }

    // ------------------------------------------------------------------
    // Write path: target block selection + apply/save (see WRITEPATH.md)
    // ------------------------------------------------------------------

    /// <summary>
    /// One map as a write target: the file it was read from, the absolute
    /// flash address of its cells and its byte length.
    /// </summary>
    internal sealed class MapWriteTarget
    {
        public readonly string FilePath;
        public readonly int Address;
        public readonly int Length;
        public readonly string MapName;

        public MapWriteTarget(string filePath, int address, int length, string mapName)
        {
            FilePath = filePath;
            Address = address;
            Length = length;
            MapName = mapName;
        }
    }

    /// <summary>
    /// Persisted target block selection: bit 0 = block 1, bit 1 = block 2,
    /// bit 2 = block 3.  Stored under the same HKCU key as the theme
    /// (<c>Software\VAGEDCSuiteReloaded</c>) and defaulting to "all three
    /// blocks" (mask 7).
    /// </summary>
    internal static class TargetBlockStore
    {
        private const string KeyPath = @"Software\VAGEDCSuiteReloaded";
        private const string ValueName = "TargetBlockMask";

        private static bool _loaded;
        private static int _mask;

        /// <summary>Raised after the mask changed (the ribbon refreshes).</summary>
        public static event EventHandler MaskChanged;

        public static int Mask
        {
            get
            {
                EnsureLoaded();
                return _mask;
            }
            set
            {
                EnsureLoaded();
                int normalized = value & Edc15V256Layout.MaskAll;
                // 0 means "no selection" in the writer (source block only);
                // the UI has no such choice, so it maps back to the default.
                if (normalized == 0) normalized = Edc15V256Layout.MaskAll;
                if (normalized == _mask) return;
                _mask = normalized;
                Save();
                EventHandler handler = MaskChanged;
                if (handler != null) handler(null, EventArgs.Empty);
            }
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            _mask = Load();
        }

        private static int Load()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    object stored = key == null ? null : key.GetValue(ValueName);
                    if (stored is int)
                    {
                        int mask = ((int)stored) & Edc15V256Layout.MaskAll;
                        if (mask != 0) return mask;
                    }
                }
            }
            catch
            {
                // Registry unavailable -> default below.
            }
            return Edc15V256Layout.MaskAll;   // default: all three blocks
        }

        private static void Save()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    if (key != null) key.SetValue(ValueName, _mask, RegistryValueKind.DWord);
                }
            }
            catch
            {
                // Best effort; the selection still applies for this session.
            }
        }
    }

    /// <summary>Outcome of one apply/save run (see <see cref="MapWritePath"/>).</summary>
    internal sealed class MapSaveResult
    {
        /// <summary>Data written and (for 256 KB images) all three fields verified.</summary>
        public bool Ok;

        /// <summary>The grid bytes were identical to the file; nothing was written.</summary>
        public bool Unchanged;

        /// <summary>True when the 256 KB EDC15V-5 block/checksum layout applies.</summary>
        public bool LayoutApplies;

        public int SourceBlock;
        public int[] Targets = new int[0];
        public int[] ChangedBlocks = new int[0];
        public int[] SealedBlocks = new int[0];
        public int[] BlocksWithoutSymbol = new int[0];
        public int ChecksumsOkBefore;
        public int ChecksumsOkAfter;
        public int ChangedCells;
        public string VerifyBefore = string.Empty;
        public string VerifyAfter = string.Empty;
        public string TempPath = string.Empty;
        public string Message = string.Empty;

        public string BlockList(int[] blocks)
        {
            if (blocks == null || blocks.Length == 0) return "-";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (int b in blocks)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(Edc15V256Layout.BlockNames[b - 1]);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Writes one encoded map into a copy of the source file and replicates it
    /// into the selected target blocks.
    ///
    /// This is the same flow as <c>TargetBlockWriter</c> in the repository root
    /// (Plan: validate the address, derive targets from the mask; Execute:
    /// write block base + relative offset, compare the windows, seal only the
    /// written blocks with <c>EDC15VM256Checksum.FixSelected</c>, then Verify).
    /// It is mirrored here because <c>Reloaded/build.sh</c> and
    /// <c>Reloaded.csproj</c> list their sources explicitly and do not compile
    /// <c>TargetBlockWriter.cs</c>; the address arithmetic and the checksum
    /// calls are the exact same repo-root methods
    /// (<see cref="Edc15V256Layout"/> / <see cref="EDC15VM256Checksum"/>).
    ///
    /// The raw write goes through <c>Tools.Instance.savedatatobinary</c>, i.e.
    /// the original writer with its 16-bit byte swap
    /// (<c>Tools.reverseEndian</c>) and its 256 KB write guard.
    /// </summary>
    internal static class MapWritePath
    {
        /// <summary>Fixed temp copy name per source file (documented in WRITEPATH.md).</summary>
        public static string TempPathFor(string sourcePath)
        {
            return Path.Combine(Path.GetTempPath(),
                "reloaded-apply-" + Path.GetFileName(sourcePath));
        }

        /// <summary>
        /// Applies <paramref name="encoded"/> to a temp copy of
        /// <paramref name="target"/>.FilePath.  The original file is never
        /// touched here.
        /// </summary>
        public static MapSaveResult ApplyToCopy(MapWriteTarget target, byte[] encoded, int mask)
        {
            return ApplyToCopy(target, encoded, mask, 0);
        }

        /// <summary>Same, with the number of differing cells for the report.</summary>
        public static MapSaveResult ApplyToCopy(MapWriteTarget target, byte[] encoded, int mask,
                                                int changedCells)
        {
            MapSaveResult result = new MapSaveResult();
            result.ChangedCells = changedCells;
            if (target == null)
            {
                result.Message = "Kein Schreibziel (keine Datei/Adresse für diese Karte).";
                return result;
            }
            if (encoded == null || encoded.Length != target.Length)
            {
                result.Message = "Kodierte Daten passen nicht zur Kartenlänge (" +
                    (encoded == null ? "null" : encoded.Length.ToString()) + " statt " +
                    target.Length + ").";
                return result;
            }

            string temp = TempPathFor(target.FilePath);
            result.TempPath = temp;
            try
            {
                File.Copy(target.FilePath, temp, true);
            }
            catch (Exception ex)
            {
                result.Message = "Temp-Kopie fehlgeschlagen: " + ex.Message;
                return result;
            }

            Execute(target, encoded, mask, temp, result);
            return result;
        }

        /// <summary>
        /// The plan/write/seal/verify steps of <c>TargetBlockWriter</c>, applied
        /// to <paramref name="workingFile"/> (always a copy).
        /// </summary>
        private static void Execute(MapWriteTarget target, byte[] encoded, int mask,
                                    string workingFile, MapSaveResult r)
        {
            // Tools.savedatatobinary byte-swaps 16-bit words (reverseEndian);
            // for an odd length that helper returns a zeroed buffer, which
            // would destroy the map.  TargetBlockWriter refuses the same case.
            if ((target.Length & 1) != 0)
            {
                r.Message = "Ungerade Länge (" + target.Length +
                            " Bytes): der Original-Writer (reverseEndian) kann das nicht " +
                            "byteweise tauschen und würde Nullen schreiben. Nicht geschrieben.";
                return;
            }

            r.LayoutApplies = Tools.Instance.IsEdc15V256KImage(workingFile);

            int[] targets;
            if (r.LayoutApplies)
            {
                string problem = Edc15V256Layout.DescribeProblem(target.Address, target.Length);
                if (problem.Length > 0)
                {
                    r.Message = "Schreibziel abgelehnt: " + problem;
                    return;
                }

                r.SourceBlock = Edc15V256Layout.BlockOf(target.Address);
                int[] selected = Edc15V256Layout.BlocksFromMask(mask);
                if (selected.Length == 0) selected = new int[] { r.SourceBlock };

                List<int> planned = new List<int>();
                foreach (int b in selected)
                {
                    int targetAddress = Edc15V256Layout.AddressInBlock(b, target.Address);
                    string targetProblem = Edc15V256Layout.DescribeProblem(targetAddress, target.Length);
                    if (targetProblem.Length > 0)
                    {
                        r.Message = "Ziel " + Edc15V256Layout.BlockNames[b - 1] + ": " + targetProblem;
                        return;
                    }
                    planned.Add(b);
                }
                targets = planned.ToArray();
                r.Targets = targets;
                r.BlocksWithoutSymbol = FindBlocksWithoutSymbol(target, targets);
            }
            else
            {
                // Not a 256 KB EDC15V-5 image: previous behaviour, one address.
                if (target.Address <= 0)
                {
                    r.Message = "Karte hat keine gültige Schreibadresse.";
                    return;
                }
                targets = new int[0];
            }

            byte[] before = File.ReadAllBytes(workingFile);
            r.VerifyBefore = EDC15VM256Checksum.Verify(before).Report;
            string fehler;
            r.ChecksumsOkBefore = EDC15VM256Checksum.CountOkFields(before, out fehler);

            // 1) write - block base + relative offset, byte swap inside Tools
            bool oldSuppress = Tools.Instance.SuppressDialogOnRejectedWrite;
            Tools.Instance.SuppressDialogOnRejectedWrite = true;
            try
            {
                if (r.LayoutApplies)
                {
                    foreach (int b in targets)
                    {
                        int address = Edc15V256Layout.AddressInBlock(b, target.Address);
                        Tools.Instance.savedatatobinary(address, target.Length, encoded,
                            workingFile, false, "Reloaded: " + target.MapName, EDCFileType.EDC15V);
                    }
                }
                else
                {
                    Tools.Instance.savedatatobinary(target.Address, target.Length, encoded,
                        workingFile, false, "Reloaded: " + target.MapName, EDCFileType.EDC15V);
                }
            }
            catch (Exception ex)
            {
                r.Message = "Schreiben fehlgeschlagen: " + ex.Message;
                return;
            }
            finally
            {
                Tools.Instance.SuppressDialogOnRejectedWrite = oldSuppress;
            }

            // 2) compare, then seal only the written blocks
            byte[] after = File.ReadAllBytes(workingFile);
            if (r.LayoutApplies)
            {
                List<int> changed = new List<int>();
                foreach (int b in targets)
                {
                    int baseAddress = Edc15V256Layout.BlockBases[b - 1];
                    bool differs = false;
                    for (int i = 0; i < Edc15V256Layout.BlockSize; i++)
                    {
                        if (before[baseAddress + i] != after[baseAddress + i])
                        {
                            differs = true;
                            break;
                        }
                    }
                    if (differs) changed.Add(b);
                }
                r.ChangedBlocks = changed.ToArray();

                bool[] fields = new bool[EDC15VM256Checksum.BlockCount];
                foreach (int b in targets) fields[Edc15V256Layout.ChecksumIndex(b)] = true;

                string fixError;
                if (!EDC15VM256Checksum.FixSelected(after, fields, out fixError))
                {
                    r.Message = "Prüfsumme konnte nicht berechnet werden: " + fixError;
                    return;
                }
                File.WriteAllBytes(workingFile, after);
                r.SealedBlocks = targets;
            }

            // 3) final check
            byte[] final = File.ReadAllBytes(workingFile);
            EDC15VM256Checksum.VerifyResult verify = EDC15VM256Checksum.Verify(final);
            r.VerifyAfter = verify.Report;
            r.ChecksumsOkAfter = EDC15VM256Checksum.CountOkFields(final, out fehler);

            r.Ok = r.LayoutApplies
                ? r.ChecksumsOkAfter == EDC15VM256Checksum.BlockCount
                : true;
            r.Message = BuildMessage(target, r, verify);
        }

        /// <summary>
        /// Symbol probe from <c>TargetBlockWriter.Plan</c>: reports target
        /// blocks that have no map of the same length at the same relative
        /// offset.  Warning only, exactly like the original app.
        /// </summary>
        private static int[] FindBlocksWithoutSymbol(MapWriteTarget target, int[] targets)
        {
            List<int> without = new List<int>();
            if (targets == null || targets.Length == 0) return without.ToArray();

            List<MapList.Map> maps;
            try
            {
                maps = MapList.GetMaps(target.FilePath);
            }
            catch
            {
                return without.ToArray();   // no probe possible -> no warning
            }

            int relative = Edc15V256Layout.RelativeOffset(target.Address);
            foreach (int b in targets)
            {
                bool found = false;
                foreach (MapList.Map map in maps)
                {
                    if (map.Length != target.Length) continue;
                    if (Edc15V256Layout.BlockOf(map.Address) != b) continue;
                    if (Edc15V256Layout.RelativeOffset(map.Address) != relative) continue;
                    found = true;
                    break;
                }
                if (!found) without.Add(b);
            }
            return without.ToArray();
        }

        private static string BuildMessage(MapWriteTarget target, MapSaveResult r,
                                           EDC15VM256Checksum.VerifyResult verify)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            if (!r.LayoutApplies)
            {
                sb.AppendLine("Kein 256-KB-EDC15V-5-Image (keine bekannten Blockmarker).");
                sb.AppendLine("Geschrieben: 0x" + target.Address.ToString("X5") +
                              ", " + target.Length + " Bytes (nur diese Adresse, keine Prüfsumme).");
            }
            else
            {
                sb.AppendLine("Quelle: " + Edc15V256Layout.BlockNames[r.SourceBlock - 1] +
                              ", 0x" + target.Address.ToString("X5") + ", " + target.Length + " Bytes");
                sb.AppendLine("Blöcke geschrieben: " + r.BlockList(r.Targets));
                foreach (int b in r.Targets)
                {
                    sb.AppendLine("  " + Edc15V256Layout.BlockNames[b - 1] + " -> 0x" +
                                  Edc15V256Layout.AddressInBlock(b, target.Address).ToString("X5"));
                }
                sb.AppendLine("Geändert: " + r.BlockList(r.ChangedBlocks));
                sb.AppendLine("Neu versiegelt: " + r.BlockList(r.SealedBlocks));
                sb.AppendLine("Prüfsummen vorher: " + r.ChecksumsOkBefore + "/" +
                              EDC15VM256Checksum.BlockCount +
                              ", nachher: " + r.ChecksumsOkAfter + "/" +
                              EDC15VM256Checksum.BlockCount);
                if (r.BlocksWithoutSymbol != null && r.BlocksWithoutSymbol.Length > 0)
                {
                    sb.AppendLine("Hinweis: in " + r.BlockList(r.BlocksWithoutSymbol) +
                                  " liegt an diesem Offset keine Karte gleicher Länge.");
                }
                sb.AppendLine(r.Ok ? "Prüfsumme OK" : "Prüfsumme FEHLERHAFT");
            }
            sb.AppendLine("Zelldifferenzen: " + r.ChangedCells);
            sb.AppendLine("Datei: " + r.TempPath);
            sb.AppendLine(verify.Report.TrimEnd());
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// Plain WinForms form hosting one OxyPlot <see cref="PlotView"/> with a
    /// heat map plus colour bar.  Construct it with metadata and data, or use
    /// <see cref="FromBinary"/> / <see cref="ShowMap"/> to read a map from a
    /// binary first.
    /// </summary>
    public sealed class MapViewForm : Form
    {
        // MapViewerEx treats every 16-bit value above this as negative
        // (b = 0x10000 - b; b = -b).  Kept identical on purpose.
        private const int SixteenBitSignThreshold = 0xF000;

        private readonly PlotView _plotView;
        private readonly MapCellGrid _cellGrid;

        // 3D surface (see VIEWER3D.md).  The surface reads the same
        // double[,]/axis arrays as the heat map and the cell grid, so it always
        // shows the selected map - no demo data, no copy.
        private readonly SurfaceView _surfaceView;
        private readonly RoundedPanel _surfacePanel;
        private readonly Panel _viewHeader;
        private readonly Button _viewToggle;
        private readonly Button _viewReset;
        private readonly Button _modeTable;
        private readonly Button _modeChart;
        private readonly Button _modeBoth;
        private bool _surfaceMode;
        private MapViewMode _mode;
        private long _lastLayoutMs;

        // 2D/3D choice is remembered for the whole process session (no
        // persistence): the next map opens in the mode the user last picked.
        // The original app shows the 3D surface by default, so that is the
        // starting choice.
        private static bool _surfacePreferred = true;

        /// <summary>
        /// Metadata + data constructor.  <paramref name="values"/> is indexed
        /// [x, y] as OxyPlot's HeatMapSeries expects: the first dimension runs
        /// along the horizontal axis, the second along the vertical one.
        /// Kept for compatibility; 16-bit cell limits are assumed.
        /// </summary>
        public MapViewForm(
            string mapName,
            string description,
            string category,
            string xAxisName,
            string xUnits,
            string yAxisName,
            string yUnits,
            int address,
            int length,
            double[,] values,
            double[] xValues,
            double[] yValues)
            : this(mapName, description, category, xAxisName, xUnits,
                   yAxisName, yUnits, address, length, values, xValues, yValues, true, null)
        {
        }

        /// <summary>
        /// Metadata + data constructor with the cell width the map was decoded
        /// with (<paramref name="sixteenBit"/> = <c>sh.ValueBytes != 1</c>).
        /// The cell editor's plausibility limits depend on it.
        /// </summary>
        internal MapViewForm(
            string mapName,
            string description,
            string category,
            string xAxisName,
            string xUnits,
            string yAxisName,
            string yUnits,
            int address,
            int length,
            double[,] values,
            double[] xValues,
            double[] yValues,
            bool sixteenBit,
            MapWriteTarget target)
        {
            if (values == null) throw new ArgumentNullException("values");
            if (xValues == null || xValues.Length != values.GetLength(0))
                throw new ArgumentException("xValues must match the first data dimension", "xValues");
            if (yValues == null || yValues.Length != values.GetLength(1))
                throw new ArgumentException("yValues must match the second data dimension", "yValues");

            string title = string.IsNullOrEmpty(mapName) ? "Map" : mapName;
            if (!string.IsNullOrEmpty(description) &&
                !string.Equals(description, mapName, StringComparison.Ordinal))
            {
                title = title + " - " + description;
            }

            Text = "Kennfeld: " + title;
            ClientSize = new Size(980, 700);
            MinimumSize = new Size(560, 460);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.White;
            // "Luft" um die beiden Karten-Container (8 px), damit die runden
            // Ecken gegen den Formhintergrund sichtbar werden.
            Padding = new Padding(8);

            // The old table header shows the parsed axis description as-is
            // ("X: IQ (mg/stroke) / Y: Engine speed (rpm)"); only add a unit
            // when the description does not already name it.
            string xTitle = AxisTitle(xAxisName, xUnits, "X");
            string yTitle = AxisTitle(yAxisName, yUnits, "Y");

            _plotView = new PlotView
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Model = BuildModel(xTitle, yTitle, values, xValues, yValues)
            };

            // Footer: source facts, so a screenshot alone says which map and
            // which file range it shows.
            double valueMin = values[0, 0];
            double valueMax = values[0, 0];
            for (int i = 0; i < values.GetLength(0); i++)
            {
                for (int j = 0; j < values.GetLength(1); j++)
                {
                    if (values[i, j] < valueMin) valueMin = values[i, j];
                    if (values[i, j] > valueMax) valueMax = values[i, j];
                }
            }

            Label footer = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 24,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 6, 0),
                Font = new Font("Segoe UI", 8.5F),
                Text = string.Format(
                    "0x{0:X}  |  {1} bytes  |  {2} x {3} cells  |  raw {4} .. {5}  |  {6}",
                    address, length, values.GetLength(1), values.GetLength(0),
                    valueMin, valueMax,
                    string.IsNullOrEmpty(category) ? "?" : category)
            };

            // Cell grid below the chart, footer below that.  Add order decides
            // docking priority (the last child docks first), so the plot keeps
            // Dock=Fill and still is a direct child of the form - Main.cs
            // looks it up in Controls and reparents it into the inline viewer.
            _cellGrid = new MapCellGrid(
                values, xValues, yValues, sixteenBit, xTitle, yTitle, _plotView, target)
            {
                Dock = DockStyle.Bottom
            };

            // ---- 3D surface + the 2D/3D toggle --------------------------
            _surfaceView = new SurfaceView(values, xValues, yValues, xTitle, yTitle)
            {
                Dock = DockStyle.Fill
            };

            _viewToggle = new Button
            {
                Text = "3D",
                AutoSize = false,
                Size = new Size(48, 21),
                FlatStyle = FlatStyle.Flat,
                TabStop = false
            };
            _viewToggle.Click += delegate { SetSurfaceMode(!_surfaceMode); };

            _viewReset = new Button
            {
                Text = "Reset",
                AutoSize = false,
                Size = new Size(58, 21),
                FlatStyle = FlatStyle.Flat,
                Enabled = false,
                TabStop = false
            };
            _viewReset.Click += delegate { _surfaceView.ResetView(); };

            // Layout switch (table / chart / both), persisted like the theme.
            _modeTable = CreateModeButton("Nur Tabelle", delegate { SetViewMode(MapViewMode.TableOnly); });
            _modeChart = CreateModeButton("Nur Diagramm", delegate { SetViewMode(MapViewMode.ChartOnly); });
            _modeBoth = CreateModeButton("Beides", delegate { SetViewMode(MapViewMode.Both); });

            FlowLayoutPanel viewRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(4, 1, 4, 0),
                AutoSize = false
            };
            viewRow.Controls.Add(_modeTable);
            viewRow.Controls.Add(_modeChart);
            viewRow.Controls.Add(_modeBoth);
            viewRow.Controls.Add(_viewToggle);
            viewRow.Controls.Add(_viewReset);

            _viewHeader = new Panel { Dock = DockStyle.Top, Height = 25 };
            _viewHeader.Controls.Add(viewRow);
            _viewHeader.ParentChanged += delegate { ApplyViewerPalette(); };

            _surfacePanel = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                Visible = false,
                CornerRadius = 8
            };
            _surfacePanel.Controls.Add(_surfaceView);

            // Add order is docking priority: the header docks at the top, the
            // footer at the very bottom, the cell grid above it, and the two
            // Fill controls take whatever is left.  Only one Fill control is
            // visible at a time (SetSurfaceMode swaps them).
            Controls.Add(_plotView);
            Controls.Add(_surfacePanel);
            Controls.Add(_cellGrid);
            Controls.Add(footer);
            Controls.Add(_viewHeader);

            // Rounded corners on the table container (the diagram already has
            // its own RoundedPanel).  Region-only: the DataGridView paints its
            // own border, so no extra frame is drawn here.
            Rounded.Round(_cellGrid, 8, Color.Transparent);

            ApplyViewerPalette();
            _surfaceMode = _surfacePreferred;
            ApplyViewMode(ViewModeStore.Current);

            // Main.OpenMapInline removes _plotView from this form and adds it
            // to the viewer panel, then disposes this shell.  Move the grid
            // next to the chart and let it die with the chart.
            _plotView.ParentChanged += PlotViewParentChanged;
            _plotView.Disposed += PlotViewDisposed;

            // Follow a live theme switch (the header buttons + surface frame
            // are form-owned, so re-apply their palette here).
            ThemeStore.Changed += ThemeChanged;
        }

        /// <summary>Re-themes the viewer chrome when the active palette changes.</summary>
        private void ThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            ApplyViewerPalette();
            if (_surfaceView != null && !_surfaceView.IsDisposed) _surfaceView.Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ThemeStore.Changed -= ThemeChanged;
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Keeps the cell editor alive when the host reparents the PlotView
        /// (Main.OpenMapInline): the grid moves into the same parent, docked at
        /// the bottom, and follows the chart's palette from then on.
        /// </summary>
        private void PlotViewParentChanged(object sender, EventArgs e)
        {
            Control parent = _plotView.Parent;
            if (parent == null || ReferenceEquals(parent, this)) return;
            if (_cellGrid.IsDisposed) return;

            // Docking priority: later-added children dock FIRST.  The Fill
            // panel therefore goes in first (right after the plot) so the
            // header, cell grid and footer take their space before it fills
            // whatever is left.
            if (!_surfacePanel.IsDisposed && !ReferenceEquals(_surfacePanel.Parent, parent))
            {
                Controls.Remove(_surfacePanel);
                _surfacePanel.Dock = DockStyle.Fill;
                parent.Controls.Add(_surfacePanel);
            }

            if (!ReferenceEquals(_cellGrid.Parent, parent))
            {
                Controls.Remove(_cellGrid);
                // Mirror ApplyLayout: the table fills in "Nur Tabelle", sits on
                // top in "Beides" (chart below), and is hidden in "Nur Diagramm".
                _cellGrid.Dock = (_mode == MapViewMode.TableOnly)
                    ? DockStyle.Fill
                    : DockStyle.Top;
                parent.Controls.Add(_cellGrid);
            }

            if (!_viewHeader.IsDisposed && !ReferenceEquals(_viewHeader.Parent, parent))
            {
                Controls.Remove(_viewHeader);
                _viewHeader.Dock = DockStyle.Top;
                parent.Controls.Add(_viewHeader);
            }

            parent.PerformLayout();
            ApplyViewerPalette();
            _surfaceView.Invalidate();
        }

        /// <summary>
        /// Inline mode: the host disposes the chart, so everything that moved
        /// with it (cell grid, 3D surface, header) goes too.
        /// </summary>
        private void PlotViewDisposed(object sender, EventArgs e)
        {
            DisposeIfDetached(_cellGrid);
            DisposeIfDetached(_surfacePanel);
            DisposeIfDetached(_viewHeader);
        }

        private void DisposeIfDetached(Control control)
        {
            if (control == null || control.IsDisposed) return;
            if (ReferenceEquals(control.Parent, this)) return;
            control.Dispose();
        }

        // ------------------------------------------------------------------
        // 2D / 3D view switch (see VIEWER3D.md)
        // ------------------------------------------------------------------

        /// <summary>
        /// Switches the viewer between the OxyPlot heat map and the 3D surface.
        /// The map data, axis values and the cell grid stay the same controls;
        /// only the two Fill panels swap visibility.  The choice is kept in a
        /// static field so the next map of the session opens the same way.
        /// </summary>
        private void SetSurfaceMode(bool surface)
        {
            _surfaceMode = surface;
            _surfacePreferred = surface;
            ApplyLayout();
            ApplyViewerPalette();
            if (surface && _mode != MapViewMode.TableOnly)
            {
                _surfaceView.Invalidate();
                _surfaceView.Focus();
            }
            else if (!surface && _mode != MapViewMode.TableOnly)
            {
                _plotView.Invalidate();
            }
        }

        /// <summary>
        /// Applies the current layout (table / chart / both) together with the
        /// 2D/3D choice.  The table and the chart are the same controls in
        /// every mode; only their visibility and the table's dock change.
        /// </summary>
        private void ApplyLayout()
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

            bool chartVisible = _mode != MapViewMode.TableOnly;
            bool tableVisible = _mode != MapViewMode.ChartOnly;

            _plotView.Visible = chartVisible && !_surfaceMode;
            _surfacePanel.Visible = chartVisible && _surfaceMode;
            _cellGrid.Visible = tableVisible;

            // "Nur Tabelle" lets the grid fill the pane; in "Beides" the table
            // sits on top and the chart fills the space below it (table above,
            // map below, as in the reference screenshot).
            _cellGrid.Dock = (tableVisible && !chartVisible)
                ? DockStyle.Fill
                : DockStyle.Top;

            // The 2D/3D switch only matters while a chart is on screen.
            _viewToggle.Visible = chartVisible;
            _viewReset.Visible = chartVisible;
            _viewToggle.Text = _surfaceMode ? "2D" : "3D";
            _viewReset.Enabled = _surfaceMode;

            Control host = _viewHeader.Parent ?? (Control)this;
            host.PerformLayout();

            if (chartVisible)
            {
                if (_surfaceMode) _surfaceView.Invalidate();
                else _plotView.Invalidate();
            }

            _lastLayoutMs = sw.ElapsedMilliseconds;
        }

        /// <summary>Sets the layout without touching the persisted value.</summary>
        private void ApplyViewMode(MapViewMode mode)
        {
            _mode = mode;
            ApplyLayout();
            ApplyViewerPalette();
        }

        /// <summary>Sets and persists the layout (the mode-button action).</summary>
        private void SetViewMode(MapViewMode mode)
        {
            ViewModeStore.Mode = mode;
            ApplyViewMode(mode);
        }

        /// <summary>Test hook: switch the layout without persisting it.</summary>
        internal void SetViewModeForTest(MapViewMode mode)
        {
            ApplyViewMode(mode);
        }

        /// <summary>Layout time of the last switch (headless timing report).</summary>
        internal long LastLayoutMs
        {
            get { return _lastLayoutMs; }
        }

        /// <summary>
        /// Themes the viewer header (and thus the 3D surface) from the host
        /// surface, the same luminance rule MapCellGrid uses.
        /// </summary>
        private void ApplyViewerPalette()
        {
            if (_viewHeader == null || _viewHeader.IsDisposed) return;

            UiPalette c = ThemeStore.Active;
            Color surface = c.Card;
            Color header = c.Elevated;
            Color line = c.Line;
            Color text = c.Text;
            Color accent = c.Accent;

            _viewHeader.BackColor = header;
            _viewHeader.ForeColor = text;
            StyleViewButton(_viewToggle, surface, text, line);
            StyleViewButton(_viewReset, surface, text, line);
            StyleModeButton(_modeTable, MapViewMode.TableOnly, surface, text, line, accent);
            StyleModeButton(_modeChart, MapViewMode.ChartOnly, surface, text, line, accent);
            StyleModeButton(_modeBoth, MapViewMode.Both, surface, text, line, accent);

            if (_surfacePanel != null)
            {
                _surfacePanel.BackColor = surface;
                _surfacePanel.BorderColor = line;
            }
            if (_surfaceView != null) _surfaceView.ApplyPalette(surface);
        }

        /// <summary>Creates a compact flat layout button in the viewer header.</summary>
        private static Button CreateModeButton(string text, EventHandler click)
        {
            Button button = new Button
            {
                Text = text,
                AutoSize = true,
                Height = 21,
                FlatStyle = FlatStyle.Flat,
                TabStop = false,
                Margin = new Padding(0)
            };
            button.Click += click;
            return button;
        }

        /// <summary>Styles one layout button; the active mode gets the accent fill.</summary>
        private void StyleModeButton(Button button, MapViewMode mode, Color surface,
                                     Color text, Color line, Color accent)
        {
            if (button == null) return;
            bool active = (_mode == mode);
            button.UseVisualStyleBackColor = false;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = line;
            button.FlatAppearance.BorderSize = 1;
            button.BackColor = active ? accent : surface;
            button.ForeColor = active ? Color.White : text;
        }

        private static void StyleViewButton(Button button, Color surface, Color text, Color line)
        {
            if (button == null) return;
            button.UseVisualStyleBackColor = false;
            button.BackColor = surface;
            button.ForeColor = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = line;
            button.FlatAppearance.BorderSize = 1;
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            ApplyViewerPalette();
        }

        /// <summary>The cell editor / apply surface of this map (used by the self-test).</summary>
        internal MapCellGrid CellGrid
        {
            get { return _cellGrid; }
        }

        /// <summary>
        /// Re-derives the chrome (viewer header + cell grid) from the current
        /// <see cref="BackColor"/>.  Used by the --viewer script hook so a
        /// floating map window can be themed dark for screenshots.
        /// </summary>
        internal void ReapplyTheme()
        {
            ApplyViewerPalette();
            if (_cellGrid != null && !_cellGrid.IsDisposed) _cellGrid.ApplyHostPalette();
        }

        /// <summary>
        /// OxyPlot wiring: heat map + colour axis + two linear axes, styled
        /// like the original's map chart (white background, thin black axes,
        /// black labels, green/yellow/orange/red ramp, no chart title).
        /// </summary>
        private static PlotModel BuildModel(
            string xTitle,
            string yTitle,
            double[,] values,
            double[] xValues,
            double[] yValues)
        {
            double xMin, xMax, yMin, yMax;
            AxisRange(xValues, out xMin, out xMax);
            AxisRange(yValues, out yMin, out yMax);

            // PlotModel has no constructor taking a title in OxyPlot 1.0.
            // The old chart carries no in-chart title (the map name lives in
            // the window/panel caption), so none is set here either.
            PlotModel model = new PlotModel
            {
                Background = OxyColors.White,
                PlotAreaBackground = OxyColors.White,
                TextColor = OxyColors.Black,
                TitleColor = OxyColors.Black,
                // The original has only the left/bottom axis lines, no box.
                PlotAreaBorderColor = OxyColors.Transparent,
                // The old grid and chart used Tahoma 8.
                DefaultFont = "Tahoma",
                DefaultFontSize = 8
            };

            LinearColorAxis colorAxis = new LinearColorAxis
            {
                Key = "Values",
                Position = AxisPosition.Right,
                Palette = MapColorRamp.Palette256(),
                AxislineColor = OxyColors.Black,
                TicklineColor = OxyColors.Black,
                TextColor = OxyColors.Black,
                TitleColor = OxyColors.Black,
                MajorGridlineStyle = LineStyle.None,
                MinorGridlineStyle = LineStyle.None,
                FontSize = 8,
                AxisTitleDistance = 4
            };
            model.Axes.Add(colorAxis);

            model.Axes.Add(CreateLinearAxis(AxisPosition.Bottom, xTitle, xMin, xMax));
            model.Axes.Add(CreateLinearAxis(AxisPosition.Left, yTitle, yMin, yMax));

            HeatMapSeries heat = new HeatMapSeries
            {
                X0 = xMin,
                X1 = xMax,
                Y0 = yMin,
                Y1 = yMax,
                Data = values,
                // Edge: X0..X1/Y0..Y1 span the first/last cell edges.
                CoordinateDefinition = HeatMapCoordinateDefinition.Edge,
                // The old chart shades its surface smoothly (Nevron
                // SmoothPalette); a bitmap heat map matches that better than
                // hard-edged cells.
                RenderMethod = HeatMapRenderMethod.Bitmap,
                Interpolate = true,
                // MUST stay 0: > 0 paints the whole plot black under wine-mono.
                LabelFontSize = 0,
                ColorAxisKey = "Values"
            };
            model.Series.Add(heat);
            return model;
        }

        /// <summary>
        /// Axis caption: the parsed description, plus the unit in brackets
        /// only when the description does not already contain it.
        /// </summary>
        private static string AxisTitle(string name, string units, string fallback)
        {
            string title = string.IsNullOrEmpty(name) ? fallback : name;
            if (!string.IsNullOrEmpty(units) &&
                title.IndexOf(units, StringComparison.OrdinalIgnoreCase) < 0)
            {
                title = title + " [" + units + "]";
            }
            return title;
        }

        /// <summary>
        /// One linear axis in the original chart's style: thin black axis and
        /// tick lines, black labels, inward ticks, no grid (the heat map fills
        /// the plot area, like the old coloured surface).
        /// </summary>
        private static LinearAxis CreateLinearAxis(
            AxisPosition position, string title, double min, double max)
        {
            return new LinearAxis
            {
                Position = position,
                Title = title,
                Minimum = min,
                Maximum = max,
                AxislineColor = OxyColors.Black,
                TicklineColor = OxyColors.Black,
                TextColor = OxyColors.Black,
                TitleColor = OxyColors.Black,
                MajorGridlineStyle = LineStyle.None,
                MinorGridlineStyle = LineStyle.None,
                TickStyle = OxyPlot.Axes.TickStyle.Inside,
                FontSize = 8,
                TitleFontSize = 8,
                AxisTitleDistance = 4
            };
        }

        /// <summary>
        /// Data range of one axis array.  A degenerate axis (single cell, or a
        /// constant axis) is padded so the heat map keeps a visible extent.
        /// </summary>
        private static void AxisRange(double[] values, out double min, out double max)
        {
            min = double.MaxValue;
            max = double.MinValue;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] < min) min = values[i];
                if (values[i] > max) max = values[i];
            }

            if (max <= min)
            {
                double pad = Math.Abs(min) > 1e-9 ? Math.Abs(min) * 0.01 : 0.5;
                min -= pad;
                max += pad;
            }
        }

        // ---------------------------------------------------------------
        // Binary reading (mirrors frmMain/MapViewerEx)
        // ---------------------------------------------------------------

        /// <summary>Opens the map viewer for <paramref name="varname"/> in <paramref name="path"/>.</summary>
        public static void ShowMap(string path, string varname, IWin32Window owner)
        {
            MapViewForm form = FromBinary(path, varname);
            if (owner != null) form.Show(owner);
            else form.Show();
        }

        /// <summary>
        /// Parses <paramref name="path"/> with the same parser the map list
        /// uses, finds the map by varname, reads its cells and axes and builds
        /// the form.  Throws on unreadable files or unknown maps; callers
        /// report that.
        /// </summary>
        public static MapViewForm FromBinary(string path, string varname)
        {
            if (path == null) throw new ArgumentNullException("path");
            if (varname == null) throw new ArgumentNullException("varname");

            SymbolHelper sh = FindSymbol(path, varname);
            if (sh == null)
                throw new InvalidOperationException("map not found: " + varname);

            int cols = Math.Max(1, sh.Y_axis_length);   // cells per row  (grid columns)
            int rows = Math.Max(1, sh.X_axis_length);   // rows in the grid

            int address = sh.Flash_start_address > 0
                ? unchecked((int)sh.Flash_start_address)
                : sh.Internal_address;
            if (address <= 0 || sh.Length <= 0)
                throw new InvalidOperationException("map has no data address: " + varname);

            // Same call frmMain uses for tabdet.Map_content.
            byte[] content = Tools.Instance.readdatafromfile(
                path, address, sh.Length, EDCFileType.EDC15V);
            if (content == null || content.Length == 0)
                throw new InvalidOperationException("map data could not be read: " + varname);

            bool sixteenBit = sh.ValueBytes != 1;

            // [x, y] for OxyPlot: x = grid column (Y axis cells), y = grid row.
            double[,] values = new double[cols, rows];
            DecodeRows(content, cols, rows, sixteenBit, values);

            // frmMain: X_axisvalues = GetXaxisValues(...) reads Y_axis_address
            // for Y_axis_length values, with X_axis_correction/offset.
            double[] xValues = ReadAxisValues(
                path, sh.Y_axis_address, cols, sh.X_axis_correction, sh.X_axis_offset, true);

            // frmMain: Y_axisvalues = GetYaxisValues(...) reads X_axis_address
            // for X_axis_length values, with Y_axis_correction/offset.
            double[] yValues = ReadAxisValues(
                path, sh.X_axis_address, rows, sh.Y_axis_correction, sh.Y_axis_offset, false);

            return new MapViewForm(
                sh.Varname,
                sh.Description,
                sh.Category,
                // The old viewer names the horizontal axis from X_axis_descr
                // (values still come from Y_axis_address, as in frmMain).
                sh.X_axis_descr,   // horizontal plot axis = grid columns
                sh.XaxisUnits,
                sh.Y_axis_descr,   // vertical plot axis = grid rows
                sh.YaxisUnits,
                address,
                sh.Length,
                values,
                xValues,
                yValues,
                sixteenBit,
                new MapWriteTarget(Path.GetFullPath(path), address, sh.Length, sh.Varname));
        }

        /// <summary>
        /// Finds one symbol by varname (falling back to the user description),
        /// using the same EDC15V parser MapList.GetMaps uses.
        /// </summary>
        private static SymbolHelper FindSymbol(string path, string varname)
        {
            IEDCFileParser parser = new EDC15VFileParser();
            List<CodeBlock> codeBlocks;
            List<AxisHelper> axisHelpers;
            SymbolCollection symbols = parser.parseFile(path, out codeBlocks, out axisHelpers);
            if (symbols == null) return null;

            SymbolHelper byDescription = null;
            for (int i = 0; i < symbols.Count; i++)
            {
                SymbolHelper sh = symbols[i];
                if (sh == null) continue;
                if (string.Equals(sh.Varname, varname, StringComparison.Ordinal)) return sh;
                if (byDescription == null &&
                    string.Equals(sh.Userdescription, varname, StringComparison.Ordinal))
                {
                    byDescription = sh;
                }
            }
            return byDescription;
        }

        /// <summary>
        /// MapViewerEx.ShowTable's row loop, without the DevExpress grid: read
        /// <paramref name="rows"/> rows of <paramref name="cols"/> cells,
        /// high byte first for 16-bit symbols, and the 0xF000 sign rule.
        /// Incomplete trailing data reads as 0 instead of throwing.
        /// </summary>
        private static void DecodeRows(byte[] content, int cols, int rows, bool sixteenBit, double[,] values)
        {
            int offset = 0;
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    int b = 0;
                    if (sixteenBit)
                    {
                        if (offset + 1 < content.Length)
                        {
                            b = content[offset++];
                            b *= 256;
                            b += content[offset++];
                            if (b > SixteenBitSignThreshold) b -= 0x10000;
                        }
                    }
                    else
                    {
                        if (offset < content.Length) b = content[offset++];
                    }
                    values[col, row] = b;
                }
            }
        }

        /// <summary>
        /// Reads one axis array like frmMain's GetXaxisValues/GetYaxisValues
        /// (16-bit little-endian values via readdatafromfileasint), applies
        /// MapViewerEx's sign rule for the X axis and its correction behaviour,
        /// and falls back to 1..n when the map has no usable axis in the file.
        /// </summary>
        private static double[] ReadAxisValues(
            string path, int address, int length, double factor, double offset, bool signFix)
        {
            double[] result = new double[Math.Max(1, length)];

            if (address <= 0 || length <= 1)
            {
                for (int i = 0; i < result.Length; i++) result[i] = i + 1;
                return result;
            }

            int[] raw = Tools.Instance.readdatafromfileasint(
                path, address, length, EDCFileType.EDC15V);

            bool anyNonZero = false;
            for (int i = 0; i < result.Length; i++)
            {
                int value = (raw != null && i < raw.Length) ? raw[i] : 0;
                if (value != 0) anyNonZero = true;

                // MapViewerEx, x_axisvalues: values above 0xF000 are negative.
                if (signFix && value > SixteenBitSignThreshold) value -= 0x10000;

                double v = value;
                // ConvertXAxisValue/ConvertYAxisValue: raw value while factor == 1.
                if (factor != 1) v = (v * factor) + offset;
                result[i] = v;
            }

            if (!anyNonZero)
            {
                for (int i = 0; i < result.Length; i++) result[i] = i + 1;
            }
            return result;
        }
    }

    /// <summary>
    /// Plain WinForms cell grid for one Kennfeld.  Columns are the X values,
    /// rows are the Y values and every cell shows the decoded map value in the
    /// same orientation the heat map draws (<c>values[column, row]</c>).
    ///
    /// Editing: double-click / F2 / typing starts the edit, Enter commits, Esc
    /// cancels.  A commit is validated with the original app's own rule
    /// (MapViewerEx.gridView1_ValidatingEditor) and then written into the
    /// shared <c>double[,]</c>, so the heat map recolours immediately.  Ctrl+Z
    /// walks the per-grid in-memory undo stack.
    ///
    /// The control is deliberately self-contained (it owns the array, the
    /// limits and the undo stack, not the form) because Main.OpenMapInline
    /// disposes <see cref="MapViewForm"/> and keeps only the PlotView alive.
    /// </summary>
    internal sealed class MapCellGrid : UserControl
    {
        // MapViewerEx.gridView1_ValidatingEditor, 16-bit branch (decimal/Easy):
        //     if (Math.Abs(value) > 78643) { e.Valid = false; }
        private const int SixteenBitLimit = 78643;

        // ... 8-bit branch (decimal/Easy): if (value > 255) { e.Valid = false; }
        private const int ByteLimit = 255;

        private const int UndoCapacity = 200;

        private readonly double[,] _values;   // shared with the heat map: [column, row]
        private readonly double[] _xValues;   // one label per column
        private readonly double[] _yValues;   // one label per row
        private readonly bool _sixteenBit;
        private readonly PlotView _plotView;  // may be null (grid-only use)
        private readonly MapWriteTarget _target; // may be null (viewer-only use)

        private readonly Label _hint;
        private readonly Label _status;
        private readonly Panel _actions;
        private readonly Button _applyButton;
        private readonly Button _saveButton;
        private readonly DataGridView _grid;
        private readonly List<UndoEntry> _undo = new List<UndoEntry>();
        private string _statusInfo = string.Empty;
        private MapSaveResult _lastResult;
        private readonly string _hintXTitle;
        private readonly string _hintYTitle;
        private readonly int _hintColumns;
        private readonly int _hintRows;

        private Color _statusNormalColor = Color.FromArgb(96, 96, 96);
        private Color _statusWarningColor = Color.FromArgb(160, 30, 30);
        private Color _selectionBorder = Color.FromArgb(15, 82, 186);
        private double _valueMin;
        private double _valueMax;
        private long _lastColorizeMs;

        private struct UndoEntry
        {
            public int Column;
            public int Row;
            public int OldValue;
        }

        public MapCellGrid(
            double[,] values,
            double[] xValues,
            double[] yValues,
            bool sixteenBit,
            string xTitle,
            string yTitle,
            PlotView plotView,
            MapWriteTarget target)
        {
            if (values == null) throw new ArgumentNullException("values");
            if (xValues == null) throw new ArgumentNullException("xValues");
            if (yValues == null) throw new ArgumentNullException("yValues");

            _values = values;
            _xValues = xValues;
            _yValues = yValues;
            _sixteenBit = sixteenBit;
            _plotView = plotView;
            _target = target;

            int columns = values.GetLength(0);
            int rows = values.GetLength(1);
            _hintColumns = columns;
            _hintRows = rows;
            _hintXTitle = xTitle;
            _hintYTitle = yTitle;

            _hint = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 6, 0),
                Font = new Font("Segoe UI", 8.25F),
                Text = string.Empty
            };

            _status = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 22,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 6, 0),
                Font = new Font("Segoe UI", 8F)
            };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToOrderColumns = false,
                AllowUserToResizeRows = false,
                AllowUserToResizeColumns = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
                RowHeadersVisible = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 22,
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = false,
                StandardTab = false,
                EnableHeadersVisualStyles = false,
                ShowCellToolTips = true,
                CellBorderStyle = DataGridViewCellBorderStyle.Single,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
                Font = new Font("Tahoma", 8F)
            };
            _grid.RowTemplate.Height = 20;

            DataGridViewCellStyle cellStyle = new DataGridViewCellStyle
            {
                Font = new Font("Tahoma", 8F),
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                WrapMode = DataGridViewTriState.False,
                Padding = new Padding(0, 0, 0, 0)
            };
            _grid.DefaultCellStyle = cellStyle;
            _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle(cellStyle)
            {
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                Font = new Font("Tahoma", 8F, FontStyle.Bold),
                Padding = new Padding(2, 0, 2, 0)
            };

            // First column = the Y labels.  A frozen label column is used
            // instead of row headers: Wine's DataGridView clips row-header
            // text to a single pixel (verified with a screenshot), while a
            // plain cell renders fine.  It is read-only.
            DataGridViewTextBoxColumn axisColumn = new DataGridViewTextBoxColumn
            {
                HeaderText = "Y",
                ReadOnly = true,
                Frozen = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                Width = AxisColumnWidth(rows)
            };
            axisColumn.HeaderCell.ToolTipText = string.IsNullOrEmpty(yTitle) ? "Y" : yTitle;
            axisColumn.DefaultCellStyle = new DataGridViewCellStyle(cellStyle)
            {
                Alignment = DataGridViewContentAlignment.MiddleRight,
                Font = new Font("Tahoma", 8F, FontStyle.Bold),
                Padding = new Padding(2, 0, 6, 0)
            };
            _grid.Columns.Add(axisColumn);

            // Then one column per X value, labelled with the real axis value.
            for (int col = 0; col < columns; col++)
            {
                DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn
                {
                    HeaderText = AxisLabel(_xValues, col),
                    SortMode = DataGridViewColumnSortMode.NotSortable,
                    Width = ColumnWidth(AxisLabel(_xValues, col))
                };
                _grid.Columns.Add(column);
            }

            // Rows: one per Y value; column 0 carries the Y label, columns
            // 1..n the values of this row.
            for (int row = 0; row < rows; row++)
            {
                int index = _grid.Rows.Add();
                _grid.Rows[index].Cells[0].Value = AxisLabel(_yValues, row);
                for (int col = 0; col < columns; col++)
                {
                    _grid.Rows[index].Cells[col + 1].Value = (int)_values[col, row];
                }
            }

            _grid.CellValidating += GridCellValidating;
            _grid.CellValidated += GridCellValidated;
            _grid.CellEndEdit += GridCellEndEdit;
            _grid.CellPainting += GridCellPainting;
            _grid.CurrentCellChanged += delegate { UpdateStatus(); };

            // Apply / Save row: Apply always writes a temp copy, Save only
            // overwrites the opened file after an explicit confirmation.
            _applyButton = new Button
            {
                Text = "Übernehmen (Temp-Kopie)",
                AutoSize = false,
                Size = new Size(170, 23),
                FlatStyle = FlatStyle.Flat,
                Enabled = _target != null
            };
            _applyButton.Click += ApplyButtonClick;
            _saveButton = new Button
            {
                Text = "Datei speichern ...",
                AutoSize = false,
                Size = new Size(140, 23),
                FlatStyle = FlatStyle.Flat,
                Enabled = _target != null
            };
            _saveButton.Click += SaveButtonClick;
            _applyButton.Margin = new Padding(0, 3, 6, 0);
            _saveButton.Margin = new Padding(0, 3, 0, 0);

            FlowLayoutPanel actionRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(4, 2, 4, 0),
                AutoSize = false
            };
            actionRow.Controls.Add(_applyButton);
            actionRow.Controls.Add(_saveButton);
            _actions = new Panel { Dock = DockStyle.Bottom, Height = 30 };
            _actions.Controls.Add(actionRow);

            // Add order = dock priority (last added docks first):
            // hint top, status bottom, actions above status, grid fills.
            Controls.Add(_grid);
            Controls.Add(_actions);
            Controls.Add(_status);
            Controls.Add(_hint);

            // Default height fits the map when it is small, capped so the heat
            // map keeps the larger part of the window.
            int preferred = 34 + 22 + 30 + 22 + (rows * 20) + 4;
            Height = Math.Max(210, Math.Min(392, preferred));
            MinimumSize = new Size(220, 170);

            UpdateHint();
            TargetBlockStore.MaskChanged += TargetMaskChanged;
            ThemeStore.Changed += ThemeChanged;

            ApplyHostPalette();
            RefreshColors();
            UpdateStatus();

            // Start on the first editable cell, not on the Y label column.
            if (_grid.Rows.Count > 0 && _grid.Columns.Count > 1)
            {
                _grid.CurrentCell = _grid.Rows[0].Cells[1];
            }
        }

        /// <summary>
        /// Hint-line text for one axis: its caption (or a neutral fallback when
        /// the map has no axis description) plus the real value range.
        /// </summary>
        private static string AxisInfo(string title, string fallback, double[] values)
        {
            string caption = title;
            if (string.IsNullOrEmpty(caption) ||
                string.Equals(caption, "X", StringComparison.Ordinal) ||
                string.Equals(caption, "Y", StringComparison.Ordinal))
            {
                caption = fallback;
            }

            double min = 0, max = 0;
            if (values != null && values.Length > 0)
            {
                min = values[0];
                max = values[0];
                for (int i = 1; i < values.Length; i++)
                {
                    if (values[i] < min) min = values[i];
                    if (values[i] > max) max = values[i];
                }
            }
            return string.Format("{0}: {1} .. {2}", caption,
                min.ToString("0.##", CultureInfo.CurrentCulture),
                max.ToString("0.##", CultureInfo.CurrentCulture));
        }

        /// <summary>
        /// Hint line: dimensions, keys and the active target block selection.
        /// Rebuilt on a ribbon change so the armed target is always visible.
        /// </summary>
        private void UpdateHint()
        {
            _hint.Text = string.Format(
                "Zellen-Editor: {0} Spalten x {1} Zeilen   |   Doppelklick/F2: bearbeiten   " +
                "Enter: übernehmen   Esc: abbrechen   Ctrl+Z: rückgängig\r\n{2}   |   {3}   |   Zielblock: {4}",
                _hintColumns, _hintRows,
                AxisInfo(_hintXTitle, "X-Achse", _xValues),
                AxisInfo(_hintYTitle, "Y-Achse", _yValues),
                Edc15V256Layout.DescribeMask(TargetBlockStore.Mask));
        }

        private void TargetMaskChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            UpdateHint();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                TargetBlockStore.MaskChanged -= TargetMaskChanged;
                ThemeStore.Changed -= ThemeChanged;
            }
            base.Dispose(disposing);
        }

        /// <summary>Re-themes the grid chrome when the active palette changes.</summary>
        private void ThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            ApplyHostPalette();
            _grid.Invalidate();
        }

        // ------------------------------------------------------------------
        // write path (encode / apply / save) - see WRITEPATH.md
        // ------------------------------------------------------------------

        /// <summary>
        /// Encodes the grid into the byte layout the original viewer writes:
        /// <c>MapViewerEx.GetDataFromGridView(false)</c>, i.e. 16-bit values as
        /// high byte then low byte (unsigned 16-bit wrap of the signed cell
        /// value) and 8-bit maps as one byte per cell.
        ///
        /// The result is exactly what <c>Tools.savedatatobinary</c> expects:
        /// that method applies <c>Tools.reverseEndian</c> (the 16-bit swap the
        /// file needs) before writing.  Cells the grid does not cover (only
        /// possible when Length is larger than the table) keep their file
        /// bytes instead of being zeroed.
        /// </summary>
        internal byte[] Encode(out int changedCells)
        {
            int columns = _values.GetLength(0);
            int rows = _values.GetLength(1);
            int cellBytes = _sixteenBit ? 2 : 1;
            int length = (_target != null && _target.Length > 0)
                ? _target.Length
                : columns * rows * cellBytes;

            byte[] buffer;
            if (_target != null)
            {
                // Same read call the viewer used: byte-swapped view of the
                // file, so untouched cells keep their exact file bytes.
                byte[] original = Tools.Instance.readdatafromfile(
                    _target.FilePath, _target.Address, length, EDCFileType.EDC15V);
                if (original == null || original.Length != length)
                {
                    throw new InvalidOperationException(
                        "Kartendaten konnten nicht gelesen werden: " + _target.FilePath);
                }
                buffer = (byte[])original.Clone();
            }
            else
            {
                buffer = new byte[length];
            }

            changedCells = 0;
            int offset = 0;
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    int value = (int)_values[col, row];
                    if (_sixteenBit)
                    {
                        if (offset + 1 >= length)
                        {
                            offset += 2;
                            continue;
                        }
                        // GetDataFromGridView: ToString("X8").Substring(4, 2)
                        // then Substring(6, 2) = unsigned 16-bit, high first.
                        ushort raw = unchecked((ushort)value);
                        byte high = (byte)(raw >> 8);
                        byte low = (byte)(raw & 0xFF);
                        if (buffer[offset] != high || buffer[offset + 1] != low) changedCells++;
                        buffer[offset++] = high;
                        buffer[offset++] = low;
                    }
                    else
                    {
                        if (offset >= length)
                        {
                            offset++;
                            continue;
                        }
                        // GetDataFromGridView: Convert.ToByte(Math.Floor(v)),
                        // only for v >= 0 (the editor rejects negatives).
                        byte b = (byte)(value & 0xFF);
                        if (buffer[offset] != b) changedCells++;
                        buffer[offset++] = b;
                    }
                }
            }
            return buffer;
        }

        /// <summary>Absolute flash address of this map (0 when there is no write target).</summary>
        internal int MapAddress
        {
            get { return _target == null ? 0 : _target.Address; }
        }

        /// <summary>Byte length of this map (0 when there is no write target).</summary>
        internal int MapLength
        {
            get { return _target == null ? 0 : _target.Length; }
        }

        /// <summary>Outcome of the last apply run (diagnostics/self-test).</summary>
        internal MapSaveResult LastResult
        {
            get { return _lastResult; }
        }

        /// <summary>Apply with the persisted selection (what the button does).</summary>
        internal MapSaveResult ApplyToTempCopy()
        {
            return ApplyToTempCopy(TargetBlockStore.Mask);
        }

        /// <summary>
        /// Encodes the grid, writes it into a temp copy of the opened file,
        /// replicates it into the selected target blocks and reseals the
        /// affected checksum fields.  The original file is never touched.
        /// </summary>
        internal MapSaveResult ApplyToTempCopy(int mask)
        {
            if (_target == null)
            {
                MapSaveResult none = new MapSaveResult();
                none.Message = "Diese Karte hat kein Schreibziel (nur Viewer).";
                _lastResult = none;
                ShowResultInStatus(none);
                return none;
            }

            MapSaveResult result;
            int changedCells;
            try
            {
                byte[] encoded = Encode(out changedCells);
                if (changedCells == 0)
                {
                    result = new MapSaveResult();
                    result.Unchanged = true;
                    result.Ok = true;
                    result.LayoutApplies = Tools.Instance.IsEdc15V256KImage(_target.FilePath);
                    byte[] probe = File.ReadAllBytes(_target.FilePath);
                    result.VerifyAfter = EDC15VM256Checksum.Verify(probe).Report;
                    string fehler;
                    result.ChecksumsOkAfter = EDC15VM256Checksum.CountOkFields(probe, out fehler);
                    result.Message = "Keine Änderung: die Tabelle entspricht der Datei.\r\n" +
                                     result.VerifyAfter.TrimEnd();
                }
                else
                {
                    result = MapWritePath.ApplyToCopy(_target, encoded, mask, changedCells);
                }
            }
            catch (Exception ex)
            {
                result = new MapSaveResult();
                result.Message = "Übernehmen fehlgeschlagen: " + ex.Message;
            }

            _lastResult = result;
            ShowResultInStatus(result);
            return result;
        }

        /// <summary>
        /// Test hook: sets one cell exactly like a committed edit (same
        /// plausibility rule, same undo entry, same chart refresh).
        /// </summary>
        internal bool SetCellValue(int column, int row, int value, out string error)
        {
            error = null;
            if (column < 0 || column >= _values.GetLength(0) ||
                row < 0 || row >= _values.GetLength(1))
            {
                error = "Zelle außerhalb der Karte.";
                return false;
            }
            if (!IsPlausible(value, out error)) return false;

            int old = (int)_values[column, row];
            if (old != value)
            {
                UndoEntry entry;
                entry.Column = column;
                entry.Row = row;
                entry.OldValue = old;
                _undo.Add(entry);
                if (_undo.Count > UndoCapacity) _undo.RemoveAt(0);

                _values[column, row] = value;
                if (row < _grid.Rows.Count && column + 1 < _grid.Columns.Count)
                {
                    _grid.Rows[row].Cells[column + 1].Value = value;
                }
                RefreshColors();
                InvalidateHeatMap();
            }
            UpdateStatus();
            return true;
        }

        private void ApplyButtonClick(object sender, EventArgs e)
        {
            MapSaveResult result = ApplyToTempCopy();
            MessageBox.Show(this, result.Message,
                result.Ok ? "Übernehmen (Temp-Kopie)" : "Übernehmen fehlgeschlagen",
                MessageBoxButtons.OK,
                result.Ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }

        private void SaveButtonClick(object sender, EventArgs e)
        {
            if (_target == null) return;

            string question = "Originaldatei überschreiben?\r\n\r\n" + _target.FilePath +
                "\r\n\r\nZielblöcke: " + Edc15V256Layout.DescribeMask(TargetBlockStore.Mask) +
                "\r\n\r\nZuerst wird in eine Temp-Kopie geschrieben und geprüft; " +
                "die Originaldatei wird nur bei erfolgreicher Prüfsumme ersetzt.";
            DialogResult answer = MessageBox.Show(this, question, "Datei speichern",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            MapSaveResult result = ApplyToTempCopy();
            if (!result.Ok)
            {
                MessageBox.Show(this, result.Message, "Nicht gespeichert",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (result.Unchanged)
            {
                MessageBox.Show(this, result.Message, "Keine Änderung",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                File.Copy(result.TempPath, _target.FilePath, true);
                result.Message = result.Message + "\r\n\r\nOriginaldatei geschrieben: " +
                                 _target.FilePath;
                MessageBox.Show(this, result.Message, "Datei gespeichert",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Schreiben der Originaldatei fehlgeschlagen:\r\n" +
                    ex.Message, "Nicht gespeichert",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>Compact apply result in the status line (full text in the dialog).</summary>
        private void ShowResultInStatus(MapSaveResult result)
        {
            if (result == null) return;
            if (result.Unchanged)
            {
                _statusInfo = "Keine Änderung";
            }
            else if (result.Ok)
            {
                _statusInfo = string.Format("Übernommen: {0}, Prüfsumme {1}/{2} OK",
                    result.BlockList(result.Targets),
                    result.ChecksumsOkAfter, EDC15VM256Checksum.BlockCount);
            }
            else
            {
                _statusInfo = "Übernehmen fehlgeschlagen";
            }
            UpdateStatus();
        }

        /// <summary>
        /// The active palette, derived from the parent surface (the app paints
        /// its chrome in Main.ApplyTheme).  Falls back to the light grid when
        /// there is no parent yet.
        /// </summary>
        internal void ApplyHostPalette()
        {
            UiPalette c = ThemeStore.Active;
            bool dark = c.Dark;

            Color surface = c.Card;
            Color header = c.Elevated;
            Color line = c.Line;
            Color text = c.Text;
            Color muted = c.Muted;
            Color selection = c.Selection;
            Color selectionText = c.SelectionText;
            // Selected cell outline (the "Kontrastrahmen"): a cool blue that
            // reads against every warm ramp stop in both themes.
            _selectionBorder = dark ? Color.FromArgb(90, 180, 255) : Color.FromArgb(15, 82, 186);

            BackColor = surface;
            ForeColor = text;
            _hint.BackColor = surface;
            _hint.ForeColor = muted;
            _status.BackColor = surface;
            _statusNormalColor = muted;
            _statusWarningColor = dark ? Color.FromArgb(255, 150, 150) : Color.FromArgb(160, 30, 30);
            _status.ForeColor = muted;

            if (_actions != null) _actions.BackColor = surface;
            StyleActionButton(_applyButton, surface, text, line);
            StyleActionButton(_saveButton, surface, text, line);

            _grid.BackgroundColor = surface;
            _grid.GridColor = line;
            _grid.DefaultCellStyle.BackColor = surface;
            _grid.DefaultCellStyle.ForeColor = text;
            _grid.DefaultCellStyle.SelectionBackColor = selection;
            _grid.DefaultCellStyle.SelectionForeColor = selectionText;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = header;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = text;
            _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = header;
            _grid.RowHeadersDefaultCellStyle.BackColor = header;
            _grid.RowHeadersDefaultCellStyle.ForeColor = text;
            _grid.RowHeadersDefaultCellStyle.SelectionBackColor = header;
            _grid.AlternatingRowsDefaultCellStyle.BackColor = dark
                ? Color.FromArgb(41, 41, 43)
                : Color.FromArgb(248, 248, 249);
            _grid.AlternatingRowsDefaultCellStyle.ForeColor = text;

            // Rows are pinned explicitly.  Under wine's DataGridView the
            // inherited DefaultCellStyle background was not applied to rows
            // that already existed (they stayed white from the light palette
            // while the text turned light too).  Setting the row style — and
            // the label cell style — removes that inheritance dependency.
            Color alternate = dark ? Color.FromArgb(41, 41, 43) : Color.FromArgb(248, 248, 249);
            DataGridViewCellStyle labelStyle = new DataGridViewCellStyle
            {
                Font = new Font("Tahoma", 8F, FontStyle.Bold),
                Alignment = DataGridViewContentAlignment.MiddleRight,
                WrapMode = DataGridViewTriState.False,
                Padding = new Padding(2, 0, 6, 0),
                BackColor = header,
                ForeColor = text,
                SelectionBackColor = header,
                SelectionForeColor = text
            };

            for (int i = 0; i < _grid.Rows.Count; i++)
            {
                DataGridViewCellStyle rowStyle = _grid.Rows[i].DefaultCellStyle;
                rowStyle.BackColor = (i % 2 == 1) ? alternate : surface;
                rowStyle.ForeColor = text;
                rowStyle.SelectionBackColor = selection;
                rowStyle.SelectionForeColor = selectionText;

                if (_grid.Columns.Count > 0)
                {
                    _grid.Rows[i].Cells[0].Style = labelStyle;
                }
            }

            // Frozen Y label column header: header-coloured title cell.
            if (_grid.Columns.Count > 0)
            {
                DataGridViewCellStyle label = _grid.Columns[0].DefaultCellStyle;
                label.BackColor = header;
                label.ForeColor = text;
                label.SelectionBackColor = header;
                label.SelectionForeColor = text;
            }

            _grid.Invalidate();
        }

        /// <summary>Flat action button in the host palette (no visual styles).</summary>
        private static void StyleActionButton(Button button, Color surface, Color text, Color line)
        {
            if (button == null) return;
            button.UseVisualStyleBackColor = false;
            button.BackColor = surface;
            button.ForeColor = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = line;
            button.FlatAppearance.BorderSize = 1;
        }

        /// <summary>Colorize time of the last run (headless timing report).</summary>
        internal long LastColorizeMs
        {
            get { return _lastColorizeMs; }
        }

        /// <summary>Data min/max of the shared array, recomputed on every edit.</summary>
        private void RecalcRange()
        {
            double min = double.MaxValue;
            double max = double.MinValue;
            for (int i = 0; i < _values.GetLength(0); i++)
            {
                for (int j = 0; j < _values.GetLength(1); j++)
                {
                    if (_values[i, j] < min) min = _values[i, j];
                    if (_values[i, j] > max) max = _values[i, j];
                }
            }
            _valueMin = min;
            _valueMax = max;
        }

        /// <summary>
        /// Gives every data cell the background colour of its value (same ramp
        /// as the heat map) and black/white text chosen for readability.  The
        /// colours are precomputed once into cell styles, not re-derived on
        /// every paint; the Y label column keeps its header colour.
        /// </summary>
        private void ColorizeCells()
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            for (int row = 0; row < _grid.Rows.Count; row++)
            {
                for (int col = 1; col < _grid.Columns.Count; col++)
                {
                    int mcol = col - 1;
                    if (mcol < 0 || mcol >= _values.GetLength(0)) continue;
                    double value = _values[mcol, row];
                    Color bg = MapColorRamp.ColorFor(value, _valueMin, _valueMax);
                    Color fg = MapColorRamp.TextColorFor(bg);
                    // Selection keeps the ramp fill (the border is the cue),
                    // so the heat map look is never interrupted by selecting.
                    _grid.Rows[row].Cells[col].Style = new DataGridViewCellStyle
                    {
                        BackColor = bg,
                        ForeColor = fg,
                        SelectionBackColor = bg,
                        SelectionForeColor = fg
                    };
                }
            }
            _lastColorizeMs = sw.ElapsedMilliseconds;
        }

        /// <summary>Re-derives the value range and recolours every data cell.</summary>
        private void RefreshColors()
        {
            RecalcRange();
            ColorizeCells();
        }

        /// <summary>
        /// Draws a contrast outline around the current (selected) cell after the
        /// grid has painted it, so the selection never relies on fill colour
        /// alone — the border is a structural cue on top of the ramp fill.
        /// </summary>
        private void GridCellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (e.ColumnIndex == 0) return;   // Y label column: no ramp border

            DataGridViewCell current = _grid.CurrentCell;
            if (current == null ||
                current.RowIndex != e.RowIndex ||
                current.ColumnIndex != e.ColumnIndex)
            {
                return;
            }

            e.Paint(e.ClipBounds, DataGridViewPaintParts.All);
            using (Pen pen = new Pen(_selectionBorder, 2f))
            {
                Rectangle r = e.CellBounds;
                r.Width -= 1;
                r.Height -= 1;
                e.Graphics.DrawRectangle(pen, r);
            }
            e.Handled = true;
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            ApplyHostPalette();
        }

        /// <summary>
        /// Ctrl+Z undoes the last committed cell edit.  While a cell editor is
        /// open the text box keeps its own undo, so this only fires outside an
        /// edit.
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Z) && !_grid.IsCurrentCellInEditMode)
            {
                UndoLast();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void GridCellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            // Column 0 is the read-only Y label column.
            if (e.ColumnIndex == 0) return;

            string text = Convert.ToString(e.FormattedValue, CultureInfo.CurrentCulture);
            int value;
            string error;
            if (!TryParseValue(text, out value))
            {
                error = "Bitte eine ganze Zahl eingeben.";
            }
            else if (!IsPlausible(value, out error))
            {
                // IsPlausible filled error.
            }
            else
            {
                _grid.Rows[e.RowIndex].ErrorText = string.Empty;
                return;
            }

            e.Cancel = true;
            _grid.Rows[e.RowIndex].ErrorText = error;
            // Row headers are hidden (they do not render under Wine), so the
            // status line is the only place the user sees why the value was
            // refused.
            _status.ForeColor = _statusWarningColor;
            _status.Text = "Wert abgelehnt: " + error;
        }

        private void GridCellValidated(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.RowIndex < _grid.Rows.Count)
            {
                _grid.Rows[e.RowIndex].ErrorText = string.Empty;
            }
            _status.ForeColor = _statusNormalColor;
            UpdateStatus();
        }

        private void GridCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            // Any way out of the editor (commit or Esc) drops the rejection
            // message from the status line.
            _status.ForeColor = _statusNormalColor;

            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (e.RowIndex >= _grid.Rows.Count) return;
            // Data columns are 1..n; map column = grid column - 1.
            int column = e.ColumnIndex - 1;
            if (column < 0 || column >= _values.GetLength(0)) return;

            int value;
            if (!TryConvertCellValue(_grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value, out value))
            {
                // Validation should have caught this; keep the old value.
                _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value =
                    (int)_values[column, e.RowIndex];
                UpdateStatus();
                return;
            }

            int old = (int)_values[column, e.RowIndex];
            if (value == old)
            {
                UpdateStatus();
                return;
            }

            UndoEntry entry;
            entry.Column = column;
            entry.Row = e.RowIndex;
            entry.OldValue = old;
            _undo.Add(entry);
            if (_undo.Count > UndoCapacity) _undo.RemoveAt(0);

            _values[column, e.RowIndex] = value;
            RefreshColors();
            InvalidateHeatMap();
            UpdateStatus();
        }

        private void UndoLast()
        {
            if (_undo.Count == 0)
            {
                _status.Text = "Keine Änderung zum Rückgängigmachen.";
                return;
            }

            UndoEntry last = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);

            _values[last.Column, last.Row] = last.OldValue;
            if (last.Row < _grid.Rows.Count && last.Column + 1 < _grid.Columns.Count)
            {
                _grid.Rows[last.Row].Cells[last.Column + 1].Value = last.OldValue;
            }

            RefreshColors();
            InvalidateHeatMap();
            UpdateStatus();
        }

        private void InvalidateHeatMap()
        {
            if (_plotView == null || _plotView.Model == null || _plotView.IsDisposed) return;

            PlotModel model = _plotView.Model;

            // OxyPlot 1.0 caches the heat-map bitmap inside the series; a plain
            // InvalidatePlot() keeps the old colours when only a few cells
            // change.  Replacing the series with a fresh instance (and a copy
            // of the array) forces a full rebuild without touching the axes,
            // so the zoom/pan state survives.
            for (int i = 0; i < model.Series.Count; i++)
            {
                HeatMapSeries old = model.Series[i] as HeatMapSeries;
                if (old == null) continue;

                HeatMapSeries fresh = new HeatMapSeries
                {
                    X0 = old.X0,
                    X1 = old.X1,
                    Y0 = old.Y0,
                    Y1 = old.Y1,
                    Data = (double[,])_values.Clone(),
                    CoordinateDefinition = old.CoordinateDefinition,
                    RenderMethod = old.RenderMethod,
                    Interpolate = old.Interpolate,
                    LabelFontSize = old.LabelFontSize,
                    ColorAxisKey = old.ColorAxisKey
                };
                model.Series.RemoveAt(i);
                model.Series.Insert(i, fresh);
                break;
            }

            model.InvalidatePlot(true);
            _plotView.Invalidate();
        }

        private void UpdateStatus()
        {
            if (_status == null) return;

            DataGridViewCell cell = _grid.CurrentCell;
            string position;
            if (cell == null)
            {
                position = "Keine Zelle ausgewählt";
            }
            else
            {
                int col = cell.ColumnIndex - 1;   // 0 = read-only Y label column
                int row = cell.RowIndex;
                if (col < 0)
                {
                    position = string.Format(
                        "Y-Achse: Zeile {0} (Y = {1})", row + 1, AxisLabel(_yValues, row));
                }
                else
                {
                    int value = (col < _values.GetLength(0) && row >= 0 && row < _values.GetLength(1))
                        ? (int)_values[col, row]
                        : 0;
                    position = string.Format(
                        "Spalte {0} (X = {1})   Zeile {2} (Y = {3})   Wert = {4}",
                        col + 1, AxisLabel(_xValues, col),
                        row + 1, AxisLabel(_yValues, row),
                        value);
                }
            }

            string text = string.Format(
                "{0}   |   Änderungen: {1} (Ctrl+Z)", position, _undo.Count);
            if (!string.IsNullOrEmpty(_statusInfo)) text = text + "   |   " + _statusInfo;
            _status.Text = text;
        }

        /// <summary>
        /// The original app's plausibility rule, applied at commit time.
        /// 16-bit: |value| &lt;= 78643 (MapViewerEx.gridView1_ValidatingEditor).
        /// 8-bit: value &lt;= 255; the editor also rejects negatives because the
        /// old encoder writes bytes and silently drops negative values.
        /// </summary>
        private bool IsPlausible(int value, out string error)
        {
            error = null;
            if (_sixteenBit)
            {
                if (Math.Abs((long)value) > SixteenBitLimit)
                {
                    error = string.Format(
                        "16-Bit-Zelle: |Wert| darf höchstens {0} sein " +
                        "(Grenze aus MapViewerEx.gridView1_ValidatingEditor).",
                        SixteenBitLimit);
                    return false;
                }
            }
            else
            {
                if (value < 0 || value > ByteLimit)
                {
                    error = string.Format(
                        "8-Bit-Zelle: erlaubt ist 0 .. {0} (Original prüft nur " +
                        "Werte > {0}; negative Bytes kann der Encoder nicht schreiben).",
                        ByteLimit);
                    return false;
                }
            }
            return true;
        }

        private static bool TryParseValue(string text, out int value)
        {
            value = 0;
            if (text == null) return false;
            text = text.Trim();
            if (text.Length == 0) return false;

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value))
                return true;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryConvertCellValue(object raw, out int value)
        {
            value = 0;
            if (raw is int) { value = (int)raw; return true; }
            if (raw is double)
            {
                double d = (double)raw;
                if (d < int.MinValue || d > int.MaxValue) return false;
                value = (int)d;
                return true;
            }
            return TryParseValue(Convert.ToString(raw, CultureInfo.CurrentCulture), out value);
        }

        /// <summary>Real axis value as a compact label (invariant-style digits).</summary>
        private static string AxisLabel(double[] values, int index)
        {
            if (values == null || index < 0 || index >= values.Length) return "?";
            double v = values[index];
            if (Math.Abs(v) < 0.0005) return "0";
            return v.ToString("0.##", CultureInfo.CurrentCulture);
        }

        /// <summary>Column width that fits its header and the widest value.</summary>
        private int ColumnWidth(string header)
        {
            int w = TextRenderer.MeasureText(header, _grid.Font).Width;
            int sample = TextRenderer.MeasureText("-78643", _grid.Font).Width;
            if (sample > w) w = sample;
            w += 14;
            if (w < 44) w = 44;
            if (w > 110) w = 110;
            return w;
        }

        /// <summary>Width of the frozen Y label column, fitted to its widest label.</summary>
        private int AxisColumnWidth(int rows)
        {
            int w = 24;
            for (int i = 0; i < rows; i++)
            {
                int candidate = TextRenderer.MeasureText(AxisLabel(_yValues, i), _grid.Font).Width;
                if (candidate > w) w = candidate;
            }
            w += 14;
            if (w < 40) w = 40;
            if (w > 120) w = 120;
            return w;
        }
    }
}
