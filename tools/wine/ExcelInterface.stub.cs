using System;
using System.Data;

namespace VAGSuite
{
    // Wine/Linux build stub: Microsoft.Office.Interop.Excel is not available,
    // neither under Wine's csc nor under Mono. The repository keeps the real
    // implementation in ExcelInterface.cs; tools/mkrsp.py --wine-stub compiles
    // this file instead, so the rest of the suite can run and be tested.
    class ExcelInterface
    {
        public void ExportToExcel(string mapname, int address, int length, byte[] mapdata,
                                  int cols, int rows, bool isSixteenbit, int[] xaxisvalues,
                                  int[] yaxisvalues, bool isupsidedown, string xaxisdescr,
                                  string yaxisdescr, string zaxisdescr)
        {
            throw new PlatformNotSupportedException(
                "Excel export is disabled in the Wine test build");
        }

        public DataTable getDataFromXLS(string strFilePath)
        {
            throw new PlatformNotSupportedException(
                "Excel import is disabled in the Wine test build");
        }

        public DataTable getDataFromXLSSymbolHelper(string strFilePath)
        {
            throw new PlatformNotSupportedException(
                "Excel import is disabled in the Wine test build");
        }
    }
}
