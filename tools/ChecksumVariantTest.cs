using System;
using System.Collections.Generic;
using System.IO;
using VAGSuite;

// Field test for EDC15VM256Checksum including variant detection.
// Usage: TestVariants.exe <dir-with-bins> [ga.orig ga.stage1 ...]
internal static class TestVariants
{
    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("usage: TestVariants.exe <bindir> [extra files...]");
            return 2;
        }

        List<string> files = new List<string>();
        string[] all = Directory.GetFiles(args[0], "*.bin");
        Array.Sort(all);
        foreach (string f in all)
        {
            if (new FileInfo(f).Length == EDC15VM256Checksum.ImageSize)
            {
                files.Add(f);
            }
        }
        for (int i = 1; i < args.Length; i++)
        {
            files.Add(args[i]);
        }

        int okFields = 0;
        int totalFields = 0;
        int badFiles = 0;
        int unexpected = 0;

        foreach (string f in files)
        {
            byte[] bin = File.ReadAllBytes(f);
            string markerInfo;
            EDC15VM256Checksum.Variant v = EDC15VM256Checksum.DetectVariant(bin, out markerInfo);
            EDC15VM256Checksum.VerifyResult vr = EDC15VM256Checksum.Verify(bin);

            bool expectBad = Path.GetFileName(f).ToLower().Contains("agroff_raw");
            totalFields += EDC15VM256Checksum.BlockCount;
            int hits = 0;
            foreach (string line in vr.Report.Split('\n'))
            {
                if (line.Contains("OK")) hits++;
            }
            okFields += hits;
            if (vr.AllOk) { }
            else if (!expectBad) { badFiles++; }

            Console.WriteLine(Path.GetFileName(f));
            Console.WriteLine("  Variante: " + (v == null ? "(keine) " + markerInfo : v.Name + " [0x" + v.MarkerText + "]"));
            Console.Write(vr.Report);
            Console.WriteLine("  -> " + hits + "/3" + (expectBad ? "  (erwartet 0/3)" : ""));
            if (!expectBad && hits != 3) unexpected++;
        }

        Console.WriteLine();
        Console.WriteLine("Summe: " + okFields + "/" + totalFields + " Felder korrekt; " +
                          badFiles + " unerwartet fehlerhafte Dateien; " +
                          unexpected + " Dateien ohne 3/3");
        return (unexpected == 0 && badFiles == 0) ? 0 : 1;
    }
}
