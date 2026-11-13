//---------------------------------------------------------------------------
//
// TargetBlockTest.cs
//
// Headless test for the target block selection (Edc15V256Layout,
// TargetBlockWriter) and for the 256 KB checksum.
//
// Usage:
//   TargetBlockTest.exe <bindir> [ga.bin]
//
// Testing runs on copies of the reference file in a temp directory; the
// originals are never modified.
//
//---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using VAGSuite;

internal static class TargetBlockTest
{
    private static string currentFile = string.Empty;
    private static int failures = 0;
    private static int checks = 0;
    private static string err = string.Empty;
    private static string LastError { get { return err; } }

    private static void Check(bool condition, string what)
    {
        checks++;
        if (condition)
        {
            Console.WriteLine("  OK   " + what);
        }
        else
        {
            failures++;
            Console.WriteLine("  FAIL " + what);
        }
    }

    /// <summary>Raw writer like Tools.savedatatobinary (16-bit words swapped).</summary>
    private static void FileWriter(int address, int length, byte[] data)
    {
        byte[] reversed = new byte[data.Length];
        if (data.Length % 2 == 0)
        {
            for (int i = 0; i < data.Length; i += 2)
            {
                reversed[i] = data[i + 1];
                reversed[i + 1] = data[i];
            }
        }
        using (FileStream fs = File.OpenWrite(currentFile))
        {
            fs.Position = address;
            for (int i = 0; i < length; i++)
            {
                fs.WriteByte(reversed[i]);
            }
        }
    }

    /// <summary>Expects the swapped notation in the image.</summary>
    private static byte[] Payload(int count, byte start)
    {
        byte[] d = new byte[count];
        for (int i = 0; i < count; i++) d[i] = (byte)(start + i);
        return d;
    }

    private static byte[] Window(byte[] bin, int block)
    {
        byte[] w = new byte[Edc15V256Layout.BlockSize];
        Buffer.BlockCopy(bin, Edc15V256Layout.BlockBases[block - 1], w, 0, w.Length);
        return w;
    }

    /// <summary>Compares a 32 KB window; optionally without the checksum field.</summary>
    private static bool SameWindow(byte[] a, byte[] b, int block, bool ignoreField)
    {
        int baseAddress = Edc15V256Layout.BlockBases[block - 1];
        int end = Edc15V256Layout.BlockSize;
        if (ignoreField) end = Edc15V256Layout.FieldOffsetInBlock;
        for (int i = 0; i < end; i++)
        {
            if (a[baseAddress + i] != b[baseAddress + i]) return false;
        }
        return true;
    }

    private static bool FieldEquals(byte[] a, byte[] b, int block)
    {
        int p = Edc15V256Layout.ChecksumFieldOffset(block);
        for (int i = 0; i < 4; i++)
        {
            if (a[p + i] != b[p + i]) return false;
        }
        return true;
    }

    private static bool ContainsPayload(byte[] bin, int block, int address, byte[] payload)
    {
        int target = Edc15V256Layout.AddressInBlock(block, address);
        for (int i = 0; i < payload.Length; i += 2)
        {
            // the swapped pair was written
            if (bin[target + i] != payload[i + 1]) return false;
            if (bin[target + i + 1] != payload[i]) return false;
        }
        return true;
    }

    private static string NewCopy(string source, string name)
    {
        string dir = Path.Combine(Path.GetTempPath(), "targetblock-test");
        Directory.CreateDirectory(dir);
        string dest = Path.Combine(dir, name);
        File.Copy(source, dest, true);
        // the source's write protection must not be copied along
        File.SetAttributes(dest, FileAttributes.Normal);
        return dest;
    }

    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("usage: TargetBlockTest.exe <bindir> [ga.bin]");
            return 2;
        }

        string binDir = args[0];
        string reference = Path.Combine(binDir, "A4B5_038906018P_SG1623_Original.bin");
        if (!File.Exists(reference))
        {
            Console.WriteLine("Referenzdatei fehlt: " + reference);
            return 2;
        }

        Console.WriteLine("== Zielblock-Schreiben ==");
        TestSingleBlock(reference);
        TestAllBlocks(reference);
        TestBlocksOneAndThree(reference);
        TestNoSelection(reference);
        TestCodeRegionRefused(reference);
        TestVectorAreaRefused(reference);
        TestFieldOverflowRefused(reference);
        TestOddLengthRefused(reference);

        Console.WriteLine();
        Console.WriteLine("== Pruefsummen-Regression ==");
        Regression(binDir);

        Console.WriteLine();
        Console.WriteLine("== GA-Variante (Marker 84 F1) ==");
        string ga = args.Length > 1 ? args[1] : string.Empty;
        if (ga.Length > 0 && File.Exists(ga))
        {
            GaVariant(ga);
        }
        else
        {
            GaSynthetic(reference);
        }

        Console.WriteLine();
        Console.WriteLine("Summe: " + (checks - failures) + "/" + checks + " Pruefungen OK, " +
                          failures + " Fehler");
        return failures == 0 ? 0 : 1;
    }

    #region Einzelfaelle

    private static void TestSingleBlock(string reference)
    {
        Console.WriteLine();
        Console.WriteLine("Fall 1: nur Block 2 aendern (Maske 0x02)");
        currentFile = NewCopy(reference, "case1.bin");
        byte[] before = File.ReadAllBytes(currentFile);

        int address = 0x02000;                      // in Block 2, Datenbereich
        byte[] payload = Payload(16, 0x40);
        TargetBlockWriteResult plan = TargetBlockWriter.Plan(currentFile, address, payload.Length, 0x02,
            new BlockSymbolProbe(AlwaysTrue));
        Check(!plan.Refused, "Plan akzeptiert (Block 2, 0x2000)");
        Check(plan.SourceBlock == 2, "Quellblock = 2");
        Check(plan.Targets.Length == 1 && plan.Targets[0] == 2, "genau ein Ziel: Block 2");

        TargetBlockWriteResult r = TargetBlockWriter.Execute(plan, currentFile, address, payload.Length,
                                                            payload, new RawBlockWriter(FileWriter));
        Check(r.Ok, "Schreiben ok");
        Check(r.ChecksumsOkAfter == 3, "Pruefsummen nachher 3/3 (war " + r.ChecksumsOkAfter + ")");
        Check(r.SealedBlocks.Length == 1 && r.SealedBlocks[0] == 2, "nur Block 2 versiegelt");
        Check(r.ChangedBlocks.Length == 1 && r.ChangedBlocks[0] == 2, "nur Block 2 geaendert");

        byte[] after = File.ReadAllBytes(currentFile);
        Check(SameWindow(before, after, 1, false), "Block 1 byte-identisch (inkl. Feld)");
        Check(SameWindow(before, after, 3, false), "Block 3 byte-identisch (inkl. Feld)");
        Check(!SameWindow(before, after, 2, true), "Block 2 Daten geaendert");
        Check(!FieldEquals(before, after, 2), "Block 2 Feld neu");
        Check(ContainsPayload(after, 2, address, payload), "Wert steht in Block 2");
        Check(EDC15VM256Checksum.CountOkFields(after, out err) == 3, "Verify 3/3");
    }

    private static void TestAllBlocks(string reference)
    {
        Console.WriteLine();
        Console.WriteLine("Fall 2: alle drei Bloecke (Maske 0x07)");
        currentFile = NewCopy(reference, "case2.bin");

        int address = 0x1A20;                       // Block 2
        byte[] payload = Payload(24, 0x10);
        TargetBlockWriteResult plan = TargetBlockWriter.Plan(currentFile, address, payload.Length, 0x07,
            new BlockSymbolProbe(AlwaysTrue));
        Check(!plan.Refused, "Plan akzeptiert");
        Check(plan.Targets.Length == 3, "drei Ziele");

        TargetBlockWriteResult r = TargetBlockWriter.Execute(plan, currentFile, address, payload.Length,
                                                            payload, new RawBlockWriter(FileWriter));
        Check(r.Ok, "Schreiben ok");
        Check(r.ChecksumsOkAfter == 3, "Pruefsummen nachher 3/3");
        Check(r.SealedBlocks.Length == 3, "alle drei Felder versiegelt");
        Check(r.ChangedBlocks.Length == 3, "alle drei Bloecke geaendert");

        byte[] after = File.ReadAllBytes(currentFile);
        for (int b = 1; b <= 3; b++)
        {
            Check(ContainsPayload(after, b, address, payload),
                  "Wert in Block " + b + " an 0x" +
                  Edc15V256Layout.AddressInBlock(b, address).ToString("X5"));
        }
        Check(EDC15VM256Checksum.CountOkFields(after, out err) == 3, "Verify 3/3");
        // relative equality: all three windows carry the same offset
        Check((after[0x38000 + 0x1A20] == after[0x1A20]) &&
              (after[0x08000 + 0x1A20] == after[0x1A20]),
              "gleicher relativer Offset, nicht gleiche absolute Adresse");
    }

    private static void TestBlocksOneAndThree(string reference)
    {
        Console.WriteLine();
        Console.WriteLine("Fall 3: Block 1 + Block 3 (Maske 0x05)");
        currentFile = NewCopy(reference, "case3.bin");
        byte[] before = File.ReadAllBytes(currentFile);

        int address = 0x00B80 + 0x40;               // Block 2, erster Datenbereich
        byte[] payload = Payload(8, 0x77);
        TargetBlockWriteResult plan = TargetBlockWriter.Plan(currentFile, address, payload.Length, 0x05,
            new BlockSymbolProbe(AlwaysTrue));
        Check(!plan.Refused, "Plan akzeptiert");
        Check(plan.Targets.Length == 2 && plan.Targets[0] == 1 && plan.Targets[1] == 3,
              "Ziele: Block 1 und Block 3");

        TargetBlockWriteResult r = TargetBlockWriter.Execute(plan, currentFile, address, payload.Length,
                                                            payload, new RawBlockWriter(FileWriter));
        Check(r.Ok, "Schreiben ok");
        Check(r.ChecksumsOkAfter == 3, "Pruefsummen nachher 3/3");

        byte[] after = File.ReadAllBytes(currentFile);
        Check(SameWindow(before, after, 2, false), "Block 2 byte-identisch (inkl. Feld)");
        Check(ContainsPayload(after, 1, address, payload), "Wert in Block 1");
        Check(ContainsPayload(after, 3, address, payload), "Wert in Block 3");
    }

    private static void TestNoSelection(string reference)
    {
        Console.WriteLine();
        Console.WriteLine("Fall 4: keine Auswahl (Maske 0) = nur Quellblock");
        currentFile = NewCopy(reference, "case4.bin");
        byte[] before = File.ReadAllBytes(currentFile);

        int address = 0x3000;                       // Block 2
        byte[] payload = Payload(8, 0x99);
        TargetBlockWriteResult plan = TargetBlockWriter.Plan(currentFile, address, payload.Length, 0,
            new BlockSymbolProbe(AlwaysTrue));
        Check(!plan.Refused, "Plan akzeptiert");
        Check(plan.Targets.Length == 1 && plan.Targets[0] == 2, "Ziel = Quellblock 2");

        TargetBlockWriteResult r = TargetBlockWriter.Execute(plan, currentFile, address, payload.Length,
                                                            payload, new RawBlockWriter(FileWriter));
        Check(r.Ok, "Schreiben ok");
        byte[] after = File.ReadAllBytes(currentFile);
        Check(SameWindow(before, after, 1, false) && SameWindow(before, after, 3, false),
              "Block 1 und 3 unberuehrt");
        Check(ContainsPayload(after, 2, address, payload), "Wert in Block 2");
    }

    private static void TestCodeRegionRefused(string reference)
    {
        Console.WriteLine();
        Console.WriteLine("Fall 5: Schreibversuch in die Code-Region (0x20000)");
        currentFile = NewCopy(reference, "case5.bin");
        byte[] before = File.ReadAllBytes(currentFile);

        TargetBlockWriteResult plan = TargetBlockWriter.Plan(currentFile, 0x20000, 16, 0x07,
            new BlockSymbolProbe(AlwaysTrue));
        Check(plan.Refused, "Plan abgelehnt");
        Check(plan.Refusal.Contains("code region"), "reason names the code region: " + plan.Refusal);

        // nothing may be written even on a direct Execute
        TargetBlockWriteResult r = TargetBlockWriter.Execute(plan, currentFile, 0x20000, 16,
                                                            Payload(16, 1), new RawBlockWriter(FileWriter));
        Check(r.Refused && !r.Ok, "Execute schreibt nicht");
        byte[] after = File.ReadAllBytes(currentFile);
        Check(SameWindow(before, after, 1, false) && SameWindow(before, after, 2, false) &&
              SameWindow(before, after, 3, false), "Datei unveraendert");
    }

    private static void TestVectorAreaRefused(string reference)
    {
        Console.WriteLine();
        Console.WriteLine("Fall 6: Schreibversuch im Vektorbereich (0x400)");
        currentFile = NewCopy(reference, "case6.bin");
        TargetBlockWriteResult plan = TargetBlockWriter.Plan(currentFile, 0x400, 8, 0x02,
            new BlockSymbolProbe(AlwaysTrue));
        Check(plan.Refused, "Plan abgelehnt");
        Check(plan.Refusal.Contains("reserved area"), "reason: " + plan.Refusal);
    }

    private static void TestFieldOverflowRefused(string reference)
    {
        Console.WriteLine();
        Console.WriteLine("Fall 7: Schreibversuch ueber das Blockende (0x7FF8 + 8)");
        currentFile = NewCopy(reference, "case7.bin");
        TargetBlockWriteResult plan = TargetBlockWriter.Plan(currentFile, 0x7FF8, 8, 0x02,
            new BlockSymbolProbe(AlwaysTrue));
        Check(plan.Refused, "Plan abgelehnt");
        Check(plan.Refusal.Length > 0, "Begruendung: " + plan.Refusal);
    }

    private static void TestOddLengthRefused(string reference)
    {
        Console.WriteLine();
        Console.WriteLine("Fall 8: ungerade Laenge (7 Byte)");
        currentFile = NewCopy(reference, "case8.bin");
        byte[] before = File.ReadAllBytes(currentFile);
        TargetBlockWriteResult plan = TargetBlockWriter.Plan(currentFile, 0x2000, 7, 0x02,
            new BlockSymbolProbe(AlwaysTrue));
        Check(!plan.Refused, "Plan akzeptiert");
        TargetBlockWriteResult r = TargetBlockWriter.Execute(plan, currentFile, 0x2000, 7,
                                                            new byte[7], new RawBlockWriter(FileWriter));
        Check(r.Refused, "Execute abgelehnt (Byteordnung)");
        Check(SameWindow(before, File.ReadAllBytes(currentFile), 2, false), "Datei unveraendert");
    }

    #endregion

    #region Regression

    private static void Regression(string binDir)
    {
        string[] files = Directory.GetFiles(binDir, "*.bin");
        Array.Sort(files);
        int okFields = 0;
        int totalFields = 0;
        int badFiles = 0;
        int files256 = 0;
        int filesV5 = 0;
        int filesForeign = 0;
        int expectedBadFields = 0;

        foreach (string f in files)
        {
            if (new FileInfo(f).Length != EDC15VM256Checksum.ImageSize) continue;
            files256++;
            byte[] bin = File.ReadAllBytes(f);
            EDC15VM256Checksum.Variant v = EDC15VM256Checksum.DetectVariant(bin, out err);
            if (v == null)
            {
                // Foreign 256 KB file (different ECU family): the EDC15V-5 arithmetic
                // does not apply here and the target block logic must not engage.
                filesForeign++;
                Check(!Edc15V256Layout.HasKnownMarkers(bin),
                      "kein EDC15V-5-Marker, Zielblock-Logik inaktiv: " + Path.GetFileName(f));
                continue;
            }

            filesV5++;
            bool expectBad = Path.GetFileName(f).ToLower().Contains("agroff_raw");
            int ok = EDC15VM256Checksum.CountOkFields(bin, out err);
            totalFields += 3;
            okFields += ok;
            if (!expectBad && ok != 3) badFiles++;
            if (expectBad) expectedBadFields += 3 - ok;
            Console.WriteLine("  " + ok + "/3  " + v.Name + "  " + Path.GetFileName(f));
        }

        Console.WriteLine("  256-KB-Dateien: " + files256 + " (davon EDC15V-5: " + filesV5 +
                          ", fremd: " + filesForeign + ")");
        Console.WriteLine("  Felder korrekt: " + okFields + "/" + totalFields +
                          (LastError.Length > 0 ? " (" + LastError + ")" : "") +
                          ", unerwartet fehlerhaft: " + badFiles + " Dateien");
        Check(badFiles == 0, "keine unerwartet fehlerhafte EDC15V-5-Referenz");
        Check(filesV5 - 1 == 8, "8 saubere P-Referenzen (gefunden: " + (filesV5 - 1) + ")");
        Check(okFields == totalFields - expectedBadFields,
              "Feldbilanz: 24/24 bei den sauberen P-Referenzen, agroff_raw " +
              (3 - expectedBadFields) + "/3");

        // Fix(agroff_raw) must be byte-identical to agroff_checked
        string raw = Path.Combine(binDir, "A4B5_038906018P_agroff_raw.bin");
        string checkedFile = Path.Combine(binDir, "A4B5_038906018P_agroff_checked.bin");
        if (File.Exists(raw) && File.Exists(checkedFile))
        {
            byte[] a = File.ReadAllBytes(raw);
            byte[] b = File.ReadAllBytes(checkedFile);
            EDC15VM256Checksum.Fix(a, out err);
            Check(SameBytes(a, b), "Fix(agroff_raw) == agroff_checked");
            Check(EDC15VM256Checksum.CountOkFields(a, out err) == 3, "agroff_raw nach Fix 3/3");
        }

        // Fix on a correct image must be byte-identical (idempotent)
        string reference = Path.Combine(binDir, "A4B5_038906018P_SG1623_Original.bin");
        if (File.Exists(reference))
        {
            byte[] a = File.ReadAllBytes(reference);
            byte[] b = File.ReadAllBytes(reference);
            EDC15VM256Checksum.Fix(a, out err);
            Check(SameBytes(a, b), "Fix(Original) byte-identisch");
        }
    }

    private static bool SameBytes(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }

    private static void GaSynthetic(string reference)
    {
        // There is no GA file in the test directory.  A GA image of the same
        // family is produced by replacing the marker pair at the three
        // block ends.  The marker lies in the data area, so it changes the
        // arithmetic; after a fix the image must carry 3/3.
        byte[] bin = File.ReadAllBytes(reference);
        foreach (int b in Edc15V256Layout.AllBlocks)
        {
            int end = Edc15V256Layout.BlockBases[b - 1] + Edc15V256Layout.BlockSize;
            bin[end - 6] = 0x84;
            bin[end - 5] = 0xF1;
        }
        Check(Edc15V256Layout.HasKnownMarkers(bin), "HasKnownMarkers erkennt GA-Marker");
        EDC15VM256Checksum.Variant v = EDC15VM256Checksum.DetectVariant(bin, out err);
        Check(v != null && v.Name == "EDC15V-5 GA", "DetectVariant = GA");

        byte[] fixedBin = (byte[])bin.Clone();
        Check(EDC15VM256Checksum.Fix(fixedBin, out err), "Fix auf GA-Bild ok");
        Check(EDC15VM256Checksum.CountOkFields(fixedBin, out err) == 3, "GA-Bild nach Fix 3/3");
        Check(EDC15VM256Checksum.Verify(fixedBin).AllOk, "GA Verify.AllOk");

        string f = Path.Combine(Path.GetTempPath(), "targetblock-test", "ga_synth.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(f));
        File.WriteAllBytes(f, fixedBin);
    }

    private static void GaVariant(string path)
    {
        byte[] bin = File.ReadAllBytes(path);
        EDC15VM256Checksum.Variant v = EDC15VM256Checksum.DetectVariant(bin, out err);
        Console.WriteLine("  Datei: " + Path.GetFileName(path) + ", Variante: " +
                          (v == null ? "keine" : v.Name));
        Check(v != null, "Variante erkannt");
        Check(EDC15VM256Checksum.CountOkFields(bin, out err) == 3, "GA 3/3");
    }

    #endregion

    private static bool AlwaysTrue(int block, int relativeOffset, int length)
    {
        return true;
    }

}
