//---------------------------------------------------------------------------
//
// Reloaded/MapViewForm.cs
//
// Kennfeld (map) viewer: renders one map of an EDC15V binary as a
// colour-coded 2D heat map with OxyPlot 1.0.0 (net40).
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
//---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using VAGSuite;

namespace Reloaded
{
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

        // MapViewerEx.FillData's surface palette: five anchors at 0 / 25 / 50 /
        // 75 / 100 % of the data range.  System.Drawing's named colours, as the
        // old code passes them to Nevron.
        private static readonly OxyColor[] MapPalette =
        {
            OxyColor.FromRgb(0, 128, 0),      // Color.Green
            OxyColor.FromRgb(255, 255, 0),    // Color.Yellow
            OxyColor.FromRgb(255, 165, 0),    // Color.Orange
            OxyColor.FromRgb(255, 69, 0),     // Color.OrangeRed
            OxyColor.FromRgb(255, 0, 0)       // Color.Red
        };

        private readonly PlotView _plotView;

        /// <summary>
        /// Metadata + data constructor.  <paramref name="values"/> is indexed
        /// [x, y] as OxyPlot's HeatMapSeries expects: the first dimension runs
        /// along the horizontal axis, the second along the vertical one.
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
            ClientSize = new Size(900, 640);
            MinimumSize = new Size(480, 360);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.White;

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

            // Docking: the fill view is added first, the bottom label last, so
            // the label docks first and the plot takes the rest.
            Controls.Add(_plotView);
            Controls.Add(footer);
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
                Palette = OxyPalette.Interpolate(256, MapPalette),
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
                yValues);
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
}
