//---------------------------------------------------------------------------
//
// Reloaded/MapList.cs
//
// UI-free facade over the EDC15V map-list parser.  The parser itself
// (EDC15VFileParser) is pure byte handling; this class only hides the
// SymbolCollection / CodeBlock / AxisHelper plumbing behind a flat list of
// plain data records so a UI (or any other consumer) can render the
// Kennfelder of a file without knowing the parser internals.
//
// No WinForms, Krypton or DevExpress types are referenced here.
//
//---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VAGSuite
{
    public static class MapList
    {
        /// <summary>One recognised map (Kennfeld) in a binary.</summary>
        public sealed class Map
        {
            public string Varname;
            public string Description;
            public string Category;
            public string Subcategory;
            public string XAxis;
            public string YAxis;
            public string XUnits;
            public string YUnits;
            public int Address;
            public int Length;
            public int XLen;
            public int YLen;
            public int CodeBlock;

            public override string ToString()
            {
                return Varname;
            }
        }

        /// <summary>
        /// Parses <paramref name="filename"/> and returns its maps sorted by
        /// flash address (the parser already sorts the SymbolCollection that
        /// way).  Throws whatever the parser throws; callers decide how to
        /// report it.
        /// </summary>
        public static List<Map> GetMaps(string filename)
        {
            MapLabelApplyResult ignored;
            return GetMaps(filename, null, out ignored);
        }

        /// <summary>
        /// Same as <see cref="GetMaps(string)"/>, but additionally applies the
        /// supplied label rows on top of the labels the parser loads from the
        /// embedded resource.  <paramref name="labels"/> may be null.
        /// </summary>
        public static List<Map> GetMaps(string filename, List<MapLabel> labels)
        {
            MapLabelApplyResult ignored;
            return GetMaps(filename, labels, out ignored);
        }

        /// <summary>
        /// Parses the file and, when <paramref name="labels"/> is non-empty,
        /// overlays them through the same <see cref="MapLabelStore.Apply"/>
        /// call, the same address/name rules and the same code-block suffix
        /// provider that <c>EDC15VFileParser.ApplyMapLabels</c> uses for the
        /// embedded default label file.  No separate naming scheme.
        /// </summary>
        public static List<Map> GetMaps(string filename, List<MapLabel> labels,
                                       out MapLabelApplyResult labelResult)
        {
            if (filename == null) throw new ArgumentNullException("filename");

            IEDCFileParser parser = new EDC15VFileParser();
            List<CodeBlock> codeBlocks;
            List<AxisHelper> axisHelpers;
            SymbolCollection symbols = parser.parseFile(filename, out codeBlocks, out axisHelpers);

            labelResult = new MapLabelApplyResult();
            if (symbols != null && labels != null && labels.Count > 0)
            {
                // Same overlay as the parser's own label pass: label rows are
                // matched by address (all three block deltas) and only generic
                // names are replaced unless the row is a "-FIX" row.
                // See MapLabelFile.cs and EDC15VFileParser.ApplyMapLabels.
                byte[] allBytes = File.ReadAllBytes(filename);
                List<CodeBlock> blocks = codeBlocks;
                labelResult = MapLabelStore.Apply(
                    allBytes, symbols, blocks, labels,
                    delegate(long address) { return LabelBankSuffix(address, blocks); },
                    delegate(long address) { return LabelCodeBlock(address, blocks); });

                // The parser translates every description after its label
                // pass; do the same so renamed symbols get the same text.
                SymbolTranslator translator = new SymbolTranslator();
                for (int i = 0; i < symbols.Count; i++)
                {
                    SymbolHelper sh = symbols[i];
                    if (sh != null) sh.Description = translator.TranslateSymbolToHelpText(sh.Varname);
                }
            }

            List<Map> result = new List<Map>();
            if (symbols == null) return result;

            for (int i = 0; i < symbols.Count; i++)
            {
                SymbolHelper sh = symbols[i];
                if (sh == null) continue;

                Map m = new Map();
                m.Varname = sh.Varname;
                m.Description = sh.Description;
                m.Category = sh.Category;
                m.Subcategory = sh.Subcategory;
                m.XAxis = sh.X_axis_descr;
                m.YAxis = sh.Y_axis_descr;
                m.XUnits = sh.XaxisUnits;
                m.YUnits = sh.YaxisUnits;
                // Address: the sorted Flash_start_address is the address in
                // the file the parser detected; fall back to Internal_address.
                m.Address = sh.Flash_start_address > 0
                    ? unchecked((int)sh.Flash_start_address)
                    : sh.Internal_address;
                m.Length = sh.Length;
                m.XLen = sh.X_axis_length;
                m.YLen = sh.Y_axis_length;
                m.CodeBlock = sh.CodeBlock;
                result.Add(m);
            }
            return result;
        }

        /// <summary>Number of maps recognised in the given file.</summary>
        public static int Count(string filename)
        {
            return GetMaps(filename).Count;
        }

        /// <summary>
        /// Reads and validates one external <c>.labels</c> file for an image.
        /// Uses <see cref="MapLabelStore.Parse"/>, so the same header check as
        /// the embedded file applies: a label file for another software id or
        /// another image size is rejected instead of being applied silently.
        /// Returns null and a technical reason in <paramref name="error"/> when
        /// the file cannot be used; the image and its map tree are not touched.
        /// </summary>
        public static List<MapLabel> LoadLabelFile(string path, string softwareId,
                                                   int imageSize, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path))
            {
                error = "no label file selected";
                return null;
            }

            try
            {
                List<MapLabel> labels;
                using (StreamReader reader = new StreamReader(path, Encoding.UTF8, true))
                {
                    labels = MapLabelStore.Parse(reader,
                        softwareId == null ? string.Empty : softwareId, imageSize);
                }

                if (labels == null)
                {
                    error = "the file does not match this image (software id " +
                            (string.IsNullOrEmpty(softwareId) ? "?" : softwareId) +
                            ", size 0x" + imageSize.ToString("X") + ")";
                    return null;
                }
                if (labels.Count == 0)
                {
                    error = "the file contains no label rows";
                    return null;
                }
                return labels;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }


        /// <summary>
        /// Code-block suffix for a created curve/value name.  The same rule as
        /// <c>EDC15VFileParser.DetermineNumberByFlashBank</c>, so an external
        /// label file produces exactly the names the embedded one produces.
        /// </summary>
        private static string LabelBankSuffix(long address, List<CodeBlock> blocks)
        {
            if (blocks != null)
            {
                foreach (CodeBlock cb in blocks)
                {
                    if (cb.StartAddress <= address && cb.EndAddress >= address)
                    {
                        if (cb.BlockGearboxType == GearboxType.Automatic)
                            return "codeblock " + cb.CodeID.ToString() + ", automatic";
                        if (cb.CodeID == 2) return "codeblock " + cb.CodeID.ToString() + ", manual";
                        if (cb.CodeID == 3) return "codeblock " + cb.CodeID.ToString() + ", 4x4";
                        return "codeblock " + cb.CodeID.ToString();
                    }
                }
            }
            return "flashbank " + (address / 0x10000).ToString();
        }

        /// <summary>
        /// Code-block id for a created curve/value: the same rule as
        /// <c>EDC15VFileParser.DetermineCodeBlockByByAddress</c>.
        /// </summary>
        private static int LabelCodeBlock(long address, List<CodeBlock> blocks)
        {
            if (blocks != null)
            {
                foreach (CodeBlock cb in blocks)
                {
                    if (cb.StartAddress <= address && cb.EndAddress >= address)
                        return cb.CodeID;
                }
            }
            return 0;
        }
    }
}
