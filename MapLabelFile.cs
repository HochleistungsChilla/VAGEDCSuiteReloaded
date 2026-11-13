using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace VAGSuite
{
    /// <summary>Kind of object a label row describes.</summary>
    public enum MapLabelType
    {
        Map3D,   // type=3d : X/Y axis + 2-D table
        Curve1D, // type=1d : one axis + N values
        Scalar   // type=0d : one value, no axis
    }

    /// <summary>
    /// One row of a map label file. Addresses are absolute file offsets of
    /// the Block-2 instance; the loader derives the Block-3/Block-1
    /// candidates from the detected code blocks.
    /// </summary>
    public class MapLabel
    {
        public MapLabelType Type = MapLabelType.Map3D;
        public long Address;
        public string Name = string.Empty;
        public string Unit = string.Empty;
        public double Factor = 1.0;
        public double Offset = 0.0;
        public int Cols;   // 3d: suite X_axis_length ; 1d: number of points ; 0d: 1
        public int Rows;   // 3d: suite Y_axis_length ; 1d/0d: 1
        public double AxisMin;
        public double AxisMax;
        public bool HasAxisRange;
        public string AxisUnit = string.Empty;
        public double AxisFactor = 1.0;
        public double AxisOffset = 0.0;
        public bool HasAxisScale;
        public string AxisName = string.Empty;
        public string Source = string.Empty;

        /// <summary>Storage width of one value: 1 or 2 bytes (DAMOS eByte/eLoHi).</summary>
        public int Bytes = 2;
        /// <summary>Absolute file offset of the breakpoints, 0 when absent.</summary>
        public long AxisAddress;

        /// <summary>Number of bytes the object occupies in the image.</summary>
        public int Length { get { return Cols * Rows * Bytes; } }

        public bool IsForceFix
        {
            get { return Source.EndsWith("-FIX", StringComparison.OrdinalIgnoreCase); }
        }
    }

    /// <summary>Counters of one Apply() run, for logging.</summary>
    public class MapLabelApplyResult
    {
        public int Maps3D;
        public int Curves1D;
        public int ScalarsRenamed;
        public int ScalarsAdded;
        public int ScalarsCovered;
        public int Skipped;
        public int AlreadyNamed;
        public int Rejected;

        public int TotalApplied
        {
            get { return Maps3D + Curves1D + ScalarsRenamed + ScalarsAdded; }
        }
    }

    /// <summary>
    /// Loads label files embedded as "*.labels" resources and applies them to
    /// detected symbols.  The label file is optional: without a matching file
    /// the parser keeps whatever NameKnownMaps produced.
    /// </summary>
    public static class MapLabelStore
    {
        private const string ResourceSuffix = ".labels";

        private static readonly Dictionary<string, List<MapLabel>> _cache =
            new Dictionary<string, List<MapLabel>>(StringComparer.OrdinalIgnoreCase);

        private static bool _warned8Bit;

        /// <summary>Test seam: skip label loading (baseline symbol dump).</summary>
        public static bool Disabled;

        /// <summary>
        /// Find and parse the first embedded label file whose header matches
        /// the software id and image size.  Returns null when none matches.
        /// </summary>
        public static List<MapLabel> Load(string softwareId, int imageSize)
        {
            if (Disabled) return null;
            if (string.IsNullOrEmpty(softwareId)) return null;
            string key = softwareId.Trim().ToUpperInvariant() + "/" + imageSize.ToString("X");
            lock (_cache)
            {
                if (_cache.ContainsKey(key)) return _cache[key];
            }

            List<MapLabel> result = null;
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                string[] names = asm.GetManifestResourceNames();
                foreach (string resName in names)
                {
                    if (!resName.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase))
                        continue;
                    using (Stream s = asm.GetManifestResourceStream(resName))
                    {
                        if (s == null) continue;
                        using (StreamReader r = new StreamReader(s, Encoding.UTF8, true))
                        {
                            List<MapLabel> parsed = Parse(r, softwareId, imageSize);
                            if (parsed != null)
                            {
                                result = parsed;
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[labels] load failed: " + ex.Message);
                result = null;
            }

            lock (_cache)
            {
                _cache[key] = result;
            }
            return result;
        }

        /// <summary>
        /// Parse one label file.  Returns null if the header does not match the
        /// requested software id / image size, so a label file for a different
        /// software stand can never be applied silently.
        ///
        /// Two row layouts are accepted:
        ///   v1: addr;name;unit;factor;offset;cols;rows;source      (type = 3d)
        ///   v2: type;addr;name;unit;factor;offset;len;rows;axis_min;axis_max
        ///       [;axis_unit;axis_factor;axis_offset;axis_name];source
        /// The axis_* columns are optional; the short v2 row (11 fields) uses
        /// the image's own axis unit/scale (heuristic behaviour).
        /// </summary>
        public static List<MapLabel> Parse(TextReader reader, string softwareId, int imageSize)
        {
            string headerSoftwareId = null;
            long headerSize = -1;
            List<MapLabel> labels = new List<MapLabel>();
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length > 0 && line[0] == '\uFEFF') line = line.Substring(1);
                line = line.Trim();
                if (line.Length == 0) continue;
                if (line[0] == '#')
                {
                    string meta = line.TrimStart('#').Trim();
                    int eq = meta.IndexOf('=');
                    if (eq > 0)
                    {
                        string k = meta.Substring(0, eq).Trim().ToLowerInvariant();
                        string v = meta.Substring(eq + 1).Trim();
                        if (k == "software-id") headerSoftwareId = v;
                        else if (k == "image-size") headerSize = ParseHexOrDec(v);
                    }
                    continue;
                }
                if (line.StartsWith("addr;", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("type;", StringComparison.OrdinalIgnoreCase)) continue;

                string[] p = line.Split(';');
                MapLabel lbl = TryParseRow(p);
                if (lbl == null) continue;
                labels.Add(lbl);
            }

            if (headerSoftwareId != null &&
                !string.Equals(headerSoftwareId, softwareId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[labels] skipping file for software id " + headerSoftwareId +
                                  " (image reports " + softwareId + ")");
                return null;
            }
            if (headerSize > 0 && headerSize != imageSize)
            {
                Console.WriteLine("[labels] skipping file for image size 0x" +
                                  headerSize.ToString("X") + " (image is 0x" + imageSize.ToString("X") + ")");
                return null;
            }
            return labels;
        }

        private static MapLabel TryParseRow(string[] p)
        {
            if (p == null || p.Length < 8) return null;
            MapLabel lbl = new MapLabel();
            try
            {
                int o;
                MapLabelType type;
                if (TryParseType(p[0], out type))
                {
                    // v2 layout
                    if (p.Length < 10) return null;
                    o = 1;
                    lbl.Type = type;
                    lbl.Address = ParseHexOrDec(p[o + 0]);
                    lbl.Name = p[o + 1].Trim();
                    lbl.Unit = p[o + 2].Trim();
                    lbl.Factor = ParseDouble(p[o + 3]);
                    lbl.Offset = ParseDouble(p[o + 4]);
                    lbl.Cols = ParseInt(p[o + 5]);
                    lbl.Rows = ParseInt(p[o + 6]);
                    string amin = p[o + 7].Trim();
                    string amax = p[o + 8].Trim();
                    if (p.Length >= o + 14)
                    {
                        lbl.AxisUnit = p[o + 9].Trim();
                        string af = p[o + 10].Trim();
                        string ao = p[o + 11].Trim();
                        lbl.AxisName = p[o + 12].Trim();
                        lbl.Source = p[o + 13].Trim();
                        if (af.Length > 0)
                        {
                            lbl.AxisFactor = ParseDouble(af);
                            lbl.AxisOffset = ParseDouble(ao);
                            lbl.HasAxisScale = true;
                        }
                    }
                    else
                    {
                        lbl.Source = (p.Length > o + 9) ? p[o + 9].Trim() : string.Empty;
                    }
                    if (amin.Length > 0 && amax.Length > 0)
                    {
                        lbl.AxisMin = ParseDouble(amin);
                        lbl.AxisMax = ParseDouble(amax);
                        lbl.HasAxisRange = true;
                    }
                    // v2.1 appends the storage width and the axis address.
                    if (p.Length > o + 14 && p[o + 14].Trim().Length > 0)
                        lbl.Bytes = ParseInt(p[o + 14]);
                    if (p.Length > o + 15 && p[o + 15].Trim().Length > 0)
                        lbl.AxisAddress = ParseHexOrDec(p[o + 15]);
                }
                else
                {
                    // v1 layout -> 3D map
                    lbl.Type = MapLabelType.Map3D;
                    lbl.Address = ParseHexOrDec(p[0]);
                    lbl.Name = p[1].Trim();
                    lbl.Unit = p[2].Trim();
                    lbl.Factor = ParseDouble(p[3]);
                    lbl.Offset = ParseDouble(p[4]);
                    lbl.Cols = ParseInt(p[5]);
                    lbl.Rows = ParseInt(p[6]);
                    lbl.Source = p[7].Trim();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[labels] skipping malformed line: " + ex.Message);
                return null;
            }

            if (lbl.Address <= 0 || lbl.Name.Length == 0) return null;
            if (lbl.Bytes != 1 && lbl.Bytes != 2) lbl.Bytes = 2;
            if (lbl.Type == MapLabelType.Curve1D && lbl.Bytes != 2)
            {
                // An 8 bit curve is read as 16 bit by CheckMap(); its symbol
                // length would have to be retargeted, which is not safe.  Such
                // objects (XCOM conversion tables, ASCII ident strings) are
                // reported and skipped; single values support both widths.
                if (!_warned8Bit)
                {
                    Console.WriteLine("[labels] ignoring 8 bit curves (not representable)");
                    _warned8Bit = true;
                }
                return null;
            }
            switch (lbl.Type)
            {
                case MapLabelType.Map3D:
                    if (lbl.Cols <= 0 || lbl.Rows <= 0) return null;
                    break;
                case MapLabelType.Curve1D:
                    if (lbl.Cols < 2) return null;
                    lbl.Rows = 1;
                    break;
                case MapLabelType.Scalar:
                    lbl.Cols = 1;
                    lbl.Rows = 1;
                    break;
            }
            return lbl;
        }

        private static bool TryParseType(string token, out MapLabelType type)
        {
            type = MapLabelType.Map3D;
            string t = (token ?? string.Empty).Trim().ToLowerInvariant();
            if (t == "3d") { type = MapLabelType.Map3D; return true; }
            if (t == "1d") { type = MapLabelType.Curve1D; return true; }
            if (t == "0d") { type = MapLabelType.Scalar; return true; }
            return false;
        }

        /// <summary>
        /// Apply labels to symbols.  Matching is by address, by the suite
        /// dimensions (X=Cols, Y=Rows) and - for curves - by the exact axis
        /// layout.  Only generic symbols ("3D ...", "2D ...", empty name) are
        /// renamed; already named symbols are left alone unless the row's
        /// source ends in "-FIX".  Block-3/Block-1 addresses are derived from
        /// the detected code blocks; if none were detected, the fixed spacing
        /// of this ECU family (+0x8000, +0x38000) is used.
        ///
        /// 0d rows additionally create a symbol when the address is not
        /// covered by an already detected symbol (a scalar has no axis, so the
        /// heuristic can never find it).
        /// </summary>
        public static MapLabelApplyResult Apply(byte[] allBytes, SymbolCollection newSymbols,
                                                List<CodeBlock> newCodeBlocks, List<MapLabel> labels,
                                                Func<long, string> suffixProvider,
                                                Func<long, int> codeBlockProvider)
        {
            MapLabelApplyResult result = new MapLabelApplyResult();
            if (labels == null || labels.Count == 0 || allBytes == null) return result;

            List<long> deltas = BuildDeltas(newCodeBlocks, labels);

            Dictionary<long, MapLabel> byAddress3D = new Dictionary<long, MapLabel>();
            Dictionary<long, MapLabel> byAddress1D = new Dictionary<long, MapLabel>();
            Dictionary<long, MapLabel> byAddress0D = new Dictionary<long, MapLabel>();
            foreach (MapLabel lbl in labels)
            {
                foreach (long d in deltas)
                {
                    long addr = lbl.Address + d;
                    if (addr <= 0 || addr >= allBytes.Length) continue;
                    if (addr + lbl.Length > allBytes.Length) continue;
                    Dictionary<long, MapLabel> target = byAddress3D;
                    if (lbl.Type == MapLabelType.Curve1D) target = byAddress1D;
                    else if (lbl.Type == MapLabelType.Scalar) target = byAddress0D;
                    if (!target.ContainsKey(addr)) target.Add(addr, lbl);
                }
            }

            // One pass over the heuristic symbols: address index, name index,
            // and an occupancy map so a new curve/value never lands inside an
            // existing object (including its axis header and breakpoints).
            Dictionary<long, SymbolHelper> at = new Dictionary<long, SymbolHelper>();
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            bool[] used = new bool[allBytes.Length];
            foreach (SymbolHelper sh in newSymbols)
            {
                long a = sh.Flash_start_address;
                if (a > 0 && !at.ContainsKey(a)) at.Add(a, sh);
                if (!string.IsNullOrEmpty(sh.Varname)) names.Add(sh.Varname);

                long lo = a;
                if (sh.X_axis_address > 0 && sh.X_axis_address - 4L < lo) lo = sh.X_axis_address - 4L;
                long hi = a + Math.Max(sh.Length, 0);
                if (sh.X_axis_address > 0)
                    hi = Math.Max(hi, sh.X_axis_address + 2L * sh.X_axis_length);
                if (lo < 0) lo = 0;
                if (hi > used.Length) hi = used.Length;
                for (long i = lo; i < hi; i++) used[i] = true;
            }

            // ------------------------------------------------------------------
            // 3-D maps: rename only.  A map the heuristics did not find is not
            // invented, because a wrong 2-D grid is worse than no name.
            // ------------------------------------------------------------------
            foreach (SymbolHelper sh in newSymbols)
            {
                MapLabel lbl;
                if (!byAddress3D.TryGetValue(sh.Flash_start_address, out lbl)) continue;
                LabelOutcome outcome = Apply3D(allBytes, sh, lbl, suffixProvider);
                if (outcome == LabelOutcome.Rejected) result.Rejected++;
                else if (outcome == LabelOutcome.Renamed) result.Maps3D++;
                else result.AlreadyNamed++;
            }

            // ------------------------------------------------------------------
            // 1-D curves and single values: the Suite has no heuristic for
            // them, so they are created when the address is free.
            // ------------------------------------------------------------------
            foreach (long addr in SortedKeys(byAddress1D))
            {
                MapLabel lbl = byAddress1D[addr];
                SymbolHelper existing;
                if (at.TryGetValue(addr, out existing))
                {
                    LabelOutcome outcome = Apply1D(allBytes, existing, lbl, suffixProvider);
                    if (outcome == LabelOutcome.Rejected) result.Rejected++;
                    else if (outcome == LabelOutcome.Renamed) result.Curves1D++;
                    else result.AlreadyNamed++;
                    continue;
                }
                if (!RangeIsFree(used, addr, lbl.Length)) { result.Skipped++; continue; }
                string curveName = BuildName(lbl, addr, suffixProvider);
                if (names.Contains(curveName))
                {
                    // DAMOS reuses names for different addresses; the Suite
                    // looks symbols up by name, so disambiguate.
                    curveName = curveName + " @0x" + addr.ToString("X5");
                    if (names.Contains(curveName)) { result.Rejected++; continue; }
                }

                SymbolHelper sh = new SymbolHelper();
                sh.Flash_start_address = addr;
                sh.Start_address = addr;
                sh.X_axis_length = lbl.Cols;
                sh.Y_axis_length = 1;
                sh.Length = lbl.Length;
                sh.Is1D = true;
                sh.Is3D = false;
                sh.IsScalar = false;
                sh.Varname = curveName;
                ApplyValueFields(sh, lbl);
                ApplyAxisFields(sh, lbl);
                // Derive the axis from THIS candidate's address; a stored
                // axis_addr belongs to the Block-2 copy and must be shifted
                // with the same block delta.
                int axisAddr = (int)(addr - 2L * lbl.Cols);
                if (lbl.AxisAddress > 0 &&
                    lbl.AxisAddress + (addr - lbl.Address) != axisAddr)
                {
                    result.Rejected++;
                    continue;
                }
                if (axisAddr <= 0 || !AxisMatches(allBytes, axisAddr, lbl.Cols, lbl))
                {
                    result.Rejected++;
                    continue;
                }
                sh.X_axis_address = axisAddr;
                sh.Category = "Detected maps";
                sh.Subcategory = "Labeled curves";
                sh.CodeBlock = (codeBlockProvider != null) ? codeBlockProvider(addr) : 0;
                newSymbols.Add(sh);
                names.Add(curveName);
                at[addr] = sh;
                MarkRange(used, addr, lbl.Length);
                result.Curves1D++;
            }

            foreach (long addr in SortedKeys(byAddress0D))
            {
                MapLabel lbl = byAddress0D[addr];
                SymbolHelper existing;
                if (at.TryGetValue(addr, out existing))
                {
                    if (existing.Length != lbl.Length) { result.Skipped++; continue; }
                    if (!IsGeneric(existing) && !lbl.IsForceFix) { result.AlreadyNamed++; continue; }
                    ApplyValueFields(existing, lbl);
                    Rename(existing, lbl, suffixProvider);
                    existing.IsScalar = true;
                    existing.Is1D = false;
                    existing.Is3D = false;
                    result.ScalarsRenamed++;
                    continue;
                }
                if (!RangeIsFree(used, addr, lbl.Length)) { result.Skipped++; continue; }
                string valueName = BuildName(lbl, addr, suffixProvider);
                if (names.Contains(valueName))
                {
                    valueName = valueName + " @0x" + addr.ToString("X5");
                    if (names.Contains(valueName)) { result.Rejected++; continue; }
                }

                SymbolHelper sh = new SymbolHelper();
                sh.Flash_start_address = addr;
                sh.Start_address = addr;
                sh.X_axis_length = 1;
                sh.Y_axis_length = 1;
                sh.Length = lbl.Length;
                sh.IsScalar = true;
                sh.Is1D = false;
                sh.Is3D = false;
                sh.Varname = valueName;
                ApplyValueFields(sh, lbl);
                sh.Category = "Detected maps";
                sh.Subcategory = "Labeled values";
                sh.CodeBlock = (codeBlockProvider != null) ? codeBlockProvider(addr) : 0;
                newSymbols.Add(sh);
                names.Add(valueName);
                at[addr] = sh;
                MarkRange(used, addr, lbl.Length);
                result.ScalarsAdded++;
            }

            if (result.Curves1D + result.ScalarsAdded > 0)
            {
                try
                {
                    newSymbols.SortColumn = "Flash_start_address";
                    newSymbols.SortingOrder = GenericComparer.SortOrder.Ascending;
                    newSymbols.Sort();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[labels] re-sort failed: " + ex.Message);
                }
            }

            if (result.Rejected > 0)
                Console.WriteLine("[labels] " + result.Rejected.ToString() +
                                  " address hit(s) rejected by dimension/axis/layout check");
            if (result.Skipped > 0)
                Console.WriteLine("[labels] " + result.Skipped.ToString() +
                                  " object(s) skipped (address inside a detected symbol)");
            return result;
        }

        private static List<long> SortedKeys(Dictionary<long, MapLabel> dict)
        {
            List<long> keys = new List<long>(dict.Keys);
            keys.Sort();
            return keys;
        }

        private static bool RangeIsFree(bool[] used, long addr, int length)
        {
            if (length <= 0 || addr + length > used.Length) return false;
            for (long i = addr; i < addr + length; i++)
                if (used[i]) return false;
            return true;
        }

        private static void MarkRange(bool[] used, long addr, int length)
        {
            for (long i = addr; i < addr + length && i < used.Length; i++) used[i] = true;
        }

        private static string BuildName(MapLabel lbl, long addr, Func<long, string> suffixProvider)
        {
            string suffix = (suffixProvider != null) ? suffixProvider(addr) : string.Empty;
            return lbl.Name + " [" + suffix + "]";
        }

        /// <summary>Unit, factor and storage width of a curve or single value.</summary>
        private static void ApplyValueFields(SymbolHelper sh, MapLabel lbl)
        {
            sh.Correction = lbl.Factor;
            sh.Offset = lbl.Offset;
            sh.ValueBytes = lbl.Bytes;
            if (!string.IsNullOrEmpty(lbl.Unit))
                sh.Z_axis_descr = (lbl.Unit == "-") ? string.Empty : lbl.Unit;
        }

        /// <summary>Description, unit and scale of a 1-D curve's breakpoints.</summary>
        private static void ApplyAxisFields(SymbolHelper sh, MapLabel lbl)
        {
            if (!string.IsNullOrEmpty(lbl.AxisName) && lbl.AxisName != "-")
                sh.X_axis_descr = lbl.AxisName;
            if (!string.IsNullOrEmpty(lbl.AxisUnit) && lbl.AxisUnit != "-")
                sh.XaxisUnits = lbl.AxisUnit;
            if (lbl.HasAxisScale)
            {
                sh.X_axis_correction = lbl.AxisFactor;
                sh.X_axis_offset = lbl.AxisOffset;
            }
        }

        /// <summary>Backwards compatible entry point (3d/1d only, no scalars).</summary>
        public static int Apply(byte[] allBytes, SymbolCollection newSymbols,
                                List<CodeBlock> newCodeBlocks, List<MapLabel> labels,
                                Func<long, string> suffixProvider)
        {
            MapLabelApplyResult r = Apply(allBytes, newSymbols, newCodeBlocks, labels, suffixProvider, null);
            return r.Maps3D + r.Curves1D;
        }

        private static List<long> BuildDeltas(List<CodeBlock> newCodeBlocks, List<MapLabel> labels)
        {
            long lowest = labels[0].Address;
            foreach (MapLabel l in labels)
                if (l.Address < lowest) lowest = l.Address;

            long refBase = 0;
            if (newCodeBlocks != null)
            {
                foreach (CodeBlock cb in newCodeBlocks)
                {
                    if (cb.StartAddress <= lowest && cb.EndAddress >= lowest)
                    {
                        refBase = cb.StartAddress;
                        break;
                    }
                }
            }

            List<long> deltas = new List<long>();
            deltas.Add(0);
            if (refBase != 0)
            {
                foreach (CodeBlock cb in newCodeBlocks)
                    deltas.Add(cb.StartAddress - refBase);
            }
            else
            {
                // Known block spacing of the MSA15.5 / EDC15V-5 256 KiB images.
                deltas.Add(0x8000);
                deltas.Add(0x38000);
            }
            return deltas;
        }

        private enum LabelOutcome
        {
            Rejected,
            Renamed,
            KeptHeuristicName
        }

        private static LabelOutcome Apply3D(byte[] allBytes, SymbolHelper sh, MapLabel lbl,
                                           Func<long, string> suffixProvider)
        {
            // Dimension check: label Cols is suite X, label Rows is suite Y.
            if (sh.X_axis_length != lbl.Cols || sh.Y_axis_length != lbl.Rows) return LabelOutcome.Rejected;
            if (sh.Length != lbl.Length) return LabelOutcome.Rejected;
            if (!IsGeneric(sh) && !lbl.IsForceFix) return LabelOutcome.KeptHeuristicName;
            if (!AxesPlausible(allBytes, sh)) return LabelOutcome.Rejected;

            Rename(sh, lbl, suffixProvider);
            sh.Is3D = true;
            sh.Is1D = false;
            sh.IsScalar = false;
            if (string.IsNullOrEmpty(sh.Category) || sh.Category == "Unknown maps")
            {
                sh.Category = "Detected maps";
                if (string.IsNullOrEmpty(sh.Subcategory) || sh.Subcategory == "Unknown")
                    sh.Subcategory = "Labeled maps";
            }
            return LabelOutcome.Renamed;
        }

        private static LabelOutcome Apply1D(byte[] allBytes, SymbolHelper sh, MapLabel lbl,
                                           Func<long, string> suffixProvider)
        {
            if (sh.X_axis_length != lbl.Cols || sh.Y_axis_length != 1) return LabelOutcome.Rejected;
            if (sh.Length != lbl.Length) return LabelOutcome.Rejected;
            // A 1d curve of this family is stored as: axis header (id,len),
            // axis values, data.  Requiring the exact axis address proves the
            // layout far better than a monotonic check alone.
            long expectedAxis = sh.Flash_start_address - 2L * lbl.Cols;
            if (sh.X_axis_address <= 0 ||
                (sh.X_axis_address != expectedAxis && sh.X_axis_address != lbl.AxisAddress))
                return LabelOutcome.Rejected;
            if (!IsGeneric(sh) && !lbl.IsForceFix) return LabelOutcome.KeptHeuristicName;
            if (!AxisMatches(allBytes, sh.X_axis_address, sh.X_axis_length, lbl)) return LabelOutcome.Rejected;

            Rename(sh, lbl, suffixProvider);
            ApplyValueFields(sh, lbl);
            sh.Is1D = true;
            sh.Is3D = false;
            sh.IsScalar = false;
            if (!string.IsNullOrEmpty(lbl.AxisUnit))
                sh.XaxisUnits = (lbl.AxisUnit == "-") ? string.Empty : lbl.AxisUnit;
            if (lbl.HasAxisScale)
            {
                sh.X_axis_correction = lbl.AxisFactor;
                sh.X_axis_offset = lbl.AxisOffset;
            }
            if (!string.IsNullOrEmpty(lbl.AxisName))
                sh.X_axis_descr = lbl.AxisName;
            if (string.IsNullOrEmpty(sh.Category) || sh.Category == "Unknown maps")
            {
                sh.Category = "Detected maps";
                if (string.IsNullOrEmpty(sh.Subcategory) || sh.Subcategory == "Unknown")
                    sh.Subcategory = "Labeled curves";
            }
            return LabelOutcome.Renamed;
        }

        private static void Rename(SymbolHelper sh, MapLabel lbl, Func<long, string> suffixProvider)
        {
            string suffix = (suffixProvider != null) ? suffixProvider(sh.Flash_start_address) : string.Empty;
            sh.Varname = lbl.Name + " [" + suffix + "]";
            sh.Correction = lbl.Factor;
            sh.Offset = lbl.Offset;
            if (!string.IsNullOrEmpty(lbl.Unit))
                sh.Z_axis_descr = (lbl.Unit == "-") ? string.Empty : lbl.Unit;
        }

        private static bool IsGeneric(SymbolHelper sh)
        {
            return string.IsNullOrEmpty(sh.Varname)
                || sh.Varname.StartsWith("3D ")
                || sh.Varname.StartsWith("2D ")
                || sh.Varname.StartsWith("Generated");
        }

        private static SymbolHelper FindSymbolAt(SymbolCollection syms, long address)
        {
            foreach (SymbolHelper sh in syms)
                if (sh.Flash_start_address == address) return sh;
            return null;
        }

        /// <summary>
        /// True when the address is already part of any detected symbol
        /// (axis header, axis values or data).  Scalars must not be created
        /// inside existing objects.
        /// </summary>
        private static bool IsAddressCovered(SymbolCollection syms, long address)
        {
            foreach (SymbolHelper sh in syms)
            {
                long start = sh.Flash_start_address;
                long end = start + Math.Max(sh.Length, 0);
                long lo = start;
                if (sh.X_axis_address > 0 && sh.X_axis_address - 4L < lo) lo = sh.X_axis_address - 4L;
                if (address >= lo && address < end) return true;
            }
            return false;
        }

        /// <summary>
        /// Guard against a label file from a different software stand: both
        /// axes must be monotonic (ascending or descending) and stay inside
        /// the image.
        /// </summary>
        private static bool AxesPlausible(byte[] allBytes, SymbolHelper sh)
        {
            return AxisPlausible(allBytes, sh.X_axis_address, sh.X_axis_length)
                && AxisPlausible(allBytes, sh.Y_axis_address, sh.Y_axis_length);
        }

        /// <summary>
        /// 1d axis check: when the label carries the raw axis endpoints
        /// (axis_min/axis_max) they must match exactly; otherwise fall back to
        /// the monotonic check.  The endpoints are the stronger test because
        /// DAMOS exports a few curves whose axis is intentionally unsorted.
        /// </summary>
        private static bool AxisMatches(byte[] allBytes, int address, int length, MapLabel lbl)
        {
            if (address <= 0 || length < 2) return false;
            if ((long)address + length * 2 > allBytes.Length) return false;
            // The axis header (id,len) sits directly before the breakpoints.
            if (address < 4) return false;
            if ((allBytes[address - 4] | (allBytes[address - 3] << 8)) == 0) return false;
            if ((allBytes[address - 2] | (allBytes[address - 1] << 8)) != length) return false;
            if (lbl.HasAxisRange && EndpointsMatch(allBytes, address, length, lbl)) return true;
            return AxisPlausible(allBytes, address, length);
        }

        /// <summary>
        /// Exact breakpoint endpoints.  The stronger test: a different software
        /// stand or a different block variant rarely has the same first/last
        /// breakpoint even when the axis is monotonic.
        /// </summary>
        private static bool EndpointsMatch(byte[] allBytes, int address, int length, MapLabel lbl)
        {
            int first = allBytes[address] | (allBytes[address + 1] << 8);
            int last = allBytes[address + (length - 1) * 2] | (allBytes[address + (length - 1) * 2 + 1] << 8);
            return first == (int)Math.Round(lbl.AxisMin) && last == (int)Math.Round(lbl.AxisMax);
        }

        private static bool AxisPlausible(byte[] allBytes, int address, int length)
        {
            if (address <= 0 || length < 2) return false;
            if ((long)address + length * 2 > allBytes.Length) return false;
            bool ascending = true;
            bool descending = true;
            int prev = allBytes[address] | (allBytes[address + 1] << 8);
            for (int i = 1; i < length; i++)
            {
                int cur = allBytes[address + i * 2] | (allBytes[address + i * 2 + 1] << 8);
                if (cur < prev) ascending = false;
                if (cur > prev) descending = false;
                prev = cur;
            }
            return ascending || descending;
        }

        private static double ParseDouble(string text)
        {
            if (text == null) return 0.0;
            text = text.Trim();
            if (text.Length == 0) return 0.0;
            double v;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return v;
            text = text.Replace(',', '.');
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return v;
            return 0.0;
        }

        private static int ParseInt(string text)
        {
            return int.Parse((text ?? string.Empty).Trim(), CultureInfo.InvariantCulture);
        }

        private static long ParseHexOrDec(string text)
        {
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return long.Parse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            long v;
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return long.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
    }
}
