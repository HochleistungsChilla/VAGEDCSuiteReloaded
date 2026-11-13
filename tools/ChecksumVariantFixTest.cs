using System;
using System.IO;
using VAGSuite;

// Fix test: mutates a copy, corrects it and checks afterwards.
internal static class TestFix
{
    private static uint ReadLe(byte[] b, int p)
    {
        return (uint)(b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24));
    }

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.WriteLine("usage: TestFix.exe <in> <out>");
            return 2;
        }
        byte[] original = File.ReadAllBytes(args[0]);
        if (original.Length != EDC15VM256Checksum.ImageSize)
        {
            Console.WriteLine("falsche Groesse");
            return 2;
        }

        // 1) Fix on an unchanged copy must reproduce the file.
        byte[] same = (byte[])original.Clone();
        string fehler;
        if (!EDC15VM256Checksum.Fix(same, out fehler))
        {
            Console.WriteLine("Fix fehlgeschlagen: " + fehler);
            return 1;
        }
        bool identical = true;
        for (int i = 0; i < same.Length; i++)
        {
            if (same[i] != original[i]) { identical = false; break; }
        }
        Console.WriteLine("Fix(unveraendert) byte-identisch: " + identical);

        // 2) Mutate: data area of block 1 and 2, field bytes untouched.
        byte[] mutated = (byte[])original.Clone();
        mutated[0x01000] ^= 0x5A;
        mutated[0x01001] ^= 0xA5;
        mutated[0x09000] ^= 0x11;
        mutated[0x3F000] ^= 0x77;   // Block 3, dessen Feld in beiden Dateien gleich war
        File.WriteAllBytes(args[1], mutated);

        Console.WriteLine("mutierte Felder:");
        foreach (int p in EDC15VM256Checksum.FieldOffsets)
        {
            Console.WriteLine("  0x" + p.ToString("X5") + " = 0x" + ReadLe(mutated, p).ToString("X8"));
        }

        EDC15VM256Checksum.VerifyResult before = EDC15VM256Checksum.Verify(mutated);
        Console.WriteLine("Verify vor Fix: " + (before.AllOk ? "OK" : "FALSCH"));
        Console.Write(before.Report);

        if (!EDC15VM256Checksum.Fix(mutated, out fehler))
        {
            Console.WriteLine("Fix fehlgeschlagen: " + fehler);
            return 1;
        }
        File.WriteAllBytes(args[1], mutated);

        EDC15VM256Checksum.VerifyResult after = EDC15VM256Checksum.Verify(mutated);
        Console.WriteLine("Verify nach Fix: " + (after.AllOk ? "OK" : "FALSCH"));
        Console.Write(after.Report);

        Console.WriteLine("neue Felder:");
        foreach (int p in EDC15VM256Checksum.FieldOffsets)
        {
            Console.WriteLine("  0x" + p.ToString("X5") + " = 0x" + ReadLe(mutated, p).ToString("X8"));
        }

        // 3) Data areas must be untouched.
        bool dataSame = true;
        for (int i = 0; i < mutated.Length; i++)
        {
            bool isField = false;
            foreach (int p in EDC15VM256Checksum.FieldOffsets)
            {
                if (i >= p && i < p + 4) { isField = true; break; }
            }
            if (!isField && mutated[i] != (byte)(original[i] ^ (i == 0x01000 ? 0x5A : i == 0x01001 ? 0xA5 : i == 0x09000 ? 0x11 : i == 0x3F000 ? 0x77 : 0x00)))
            {
                dataSame = false;
                break;
            }
        }
        Console.WriteLine("Datenbereiche unveraendert: " + dataSame);

        return (identical && after.AllOk && dataSame) ? 0 : 1;
    }
}
