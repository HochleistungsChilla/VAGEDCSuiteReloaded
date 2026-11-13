//---------------------------------------------------------------------------
//
// WriteGuardTest.cs
//
// Headless test for the write guard in Tools.savedatatobinary for
// 256 KB EDC15V-5 images (code region, reserved range) and for the
// transaction log.
//
// Build (full source tree, without Program.cs):
//   csc /nologo /target:exe /main:GuardProbe /out:build\WriteGuardTest.exe
//       @b.rsp-without-Program.cs /tmp/... tools/WriteGuardTest.cs
//
// Usage: WriteGuardTest.exe <reference.bin>
//
//---------------------------------------------------------------------------

using System;
using System.IO;
using VAGSuite;

internal static class GuardProbe
{
    private static int bad = 0;
    private static void Check(bool c, string w)
    {
        Console.WriteLine((c ? "  OK   " : "  FAIL ") + w);
        if (!c) bad++;
    }

    private static int Main(string[] args)
    {
        string src = args[0];
        string f = Path.Combine(Path.GetTempPath(), "guardprobe.bin");
        File.Copy(src, f, true);
        File.SetAttributes(f, FileAttributes.Normal);

        Tools t = Tools.Instance;
        t.m_currentfile = f;
        t.m_currentfilelength = (int)new FileInfo(f).Length;
        t.m_currentFileType = EDCFileType.MSA15;
        t.SuppressDialogOnRejectedWrite = true;

        byte[] before = File.ReadAllBytes(f);
        Check(t.IsEdc15V256KImage(f), "IsEdc15V256KImage erkennt das Bild");

        // 1) Code region: must be refused
        byte[] payload = new byte[16];
        for (int i = 0; i < payload.Length; i++) payload[i] = 0x5A;
        t.savedatatobinary(0x20000, payload.Length, (byte[])payload.Clone(), f, false, "code", EDCFileType.MSA15);
        Check(Same(before, File.ReadAllBytes(f)), "Code-Region 0x20000 nicht beschrieben");

        // 2) Reserved range
        t.savedatatobinary(0x400, 8, (byte[])payload.Clone(), f, false, "vec", EDCFileType.MSA15);
        Check(Same(before, File.ReadAllBytes(f)), "Reserviertbereich 0x400 nicht beschrieben");

        // 3) valid address in block 2, with transaction log
        t.m_ProjectTransactionLog = new TransactionLog();
        string proj = Path.Combine(Path.GetTempPath(), "guardprobe-project");
        Directory.CreateDirectory(proj);
        t.m_ProjectTransactionLog.OpenTransActionLog(proj, "test");
        t.m_CurrentWorkingProject = string.Empty;
        int logBefore = t.m_ProjectTransactionLog.TransCollection.Count;
        t.savedatatobinary(0x2000, payload.Length, (byte[])payload.Clone(), f, true, "map", EDCFileType.MSA15);
        byte[] after = File.ReadAllBytes(f);
        Check(!Same(before, after), "gueltige Adresse 0x2000 beschrieben");
        Check(t.m_ProjectTransactionLog.TransCollection.Count == logBefore + 1,
              "TransactionEntry angelegt (" + t.m_ProjectTransactionLog.TransCollection.Count + ")");

        // 4) the image's checksum is broken afterwards (no fix in the raw writer)
        int ok = EDC15VM256Checksum.CountOkFields(after, out string e);
        Console.WriteLine("  Info: Felder nach Rohschreiben " + ok + "/3");
        return bad == 0 ? 0 : 1;
    }

    private static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
}
