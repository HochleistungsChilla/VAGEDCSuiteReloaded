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
            if (filename == null) throw new ArgumentNullException("filename");

            IEDCFileParser parser = new EDC15VFileParser();
            List<CodeBlock> codeBlocks;
            List<AxisHelper> axisHelpers;
            SymbolCollection symbols = parser.parseFile(filename, out codeBlocks, out axisHelpers);

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
    }
}
