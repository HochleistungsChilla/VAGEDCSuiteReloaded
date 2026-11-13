//---------------------------------------------------------------------------
//
// TargetBlockWriter.cs
//
// Writes a map change into selected target blocks of a
// 256 KB EDC15V-5 file and then seals only the affected
// blocks again.
//
// The write operation is deliberately separated from the UI: the raw write
// comes in as a delegate (in the application Tools.savedatatobinary, in the test
// a simple FileStream writer).  This way the whole flow is testable headless,
// without WinForms and without DevExpress.
//
// Flow:
//   1. Plan  - check the address, determine target blocks, symbol match
//   2. Write - per target block block base + relative offset
//   3. Seal - verify before/after, fix only the target blocks
//
//---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VAGSuite
{
    /// <summary>Raw write of a byte block to an absolute file address.</summary>
    public delegate void RawBlockWriter(int address, int length, byte[] data);

    /// <summary>
    /// True if a symbol of this length lies in <paramref name="blockNumber"/> at
    /// <paramref name="relativeOffset"/>.  Used for the safety warning so that
    /// writing into a foreign block position is not done blindly.
    /// </summary>
    public delegate bool BlockSymbolProbe(int blockNumber, int relativeOffset, int length);

    /// <summary>Result of a target block write operation.</summary>
    public sealed class TargetBlockWriteResult
    {
        /// <summary>Write performed and file afterwards sealed 3/3.</summary>
        public bool Ok;

        /// <summary>Refused before writing (address/length not allowed).</summary>
        public bool Refused;

        /// <summary>Aborted by the caller because a target block has no matching symbol.</summary>
        public bool Aborted;

        /// <summary>Reason for Refused/Aborted, otherwise empty.</summary>
        public string Refusal = string.Empty;

        /// <summary>Multi-line report for MessageBox/log.</summary>
        public string Message = string.Empty;

        /// <summary>Block the change came from (1..3), 0 = unknown.</summary>
        public int SourceBlock;

        /// <summary>Target blocks in write order.</summary>
        public int[] Targets = new int[0];

        /// <summary>Target blocks in which data bytes actually changed.</summary>
        public int[] ChangedBlocks = new int[0];

        /// <summary>Blocks whose checksum field was rewritten.</summary>
        public int[] SealedBlocks = new int[0];

        /// <summary>Target blocks without a symbol of equal length at the same relative position.</summary>
        public int[] BlocksWithoutSymbol = new int[0];

        /// <summary>Number of correct checksum fields before the write (0..3).</summary>
        public int ChecksumsOkBefore;

        /// <summary>Number of correct checksum fields after sealing (0..3).</summary>
        public int ChecksumsOkAfter;

        /// <summary>Verify report before the write.</summary>
        public string VerifyBefore = string.Empty;

        /// <summary>Verify report after sealing.</summary>
        public string VerifyAfter = string.Empty;

        public string BlockList(int[] blocks)
        {
            if (blocks == null || blocks.Length == 0) return "-";
            StringBuilder sb = new StringBuilder();
            foreach (int b in blocks)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(Edc15V256Layout.BlockNames[b - 1]);
            }
            return sb.ToString();
        }
    }

    /// <summary>Target block write for 256 KB EDC15V-5 images.</summary>
    public static class TargetBlockWriter
    {
        /// <summary>
        /// Checks the address and selection without writing anything.  The result
        /// carries the target blocks as well as the blocks without a matching
        /// symbol; the caller can build a confirmation prompt from it.
        /// </summary>
        public static TargetBlockWriteResult Plan(string filename, int address, int length, int mask,
                                                  BlockSymbolProbe probe)
        {
            TargetBlockWriteResult r = new TargetBlockWriteResult();

            if (string.IsNullOrEmpty(filename) || !File.Exists(filename))
            {
                r.Refused = true;
                r.Refusal = "File not found: " + filename;
                return r;
            }
            byte[] probeImage = File.ReadAllBytes(filename);
            if (probeImage.Length != Edc15V256Layout.ImageSize)
            {
                r.Refused = true;
                r.Refusal = "Wrong file size: 0x" + probeImage.Length.ToString("X") +
                            " instead of 0x" + Edc15V256Layout.ImageSize.ToString("X") + " (256 KB).";
                return r;
            }
            if (length <= 0)
            {
                r.Refused = true;
                r.Refusal = "Length must be greater than 0.";
                return r;
            }

            int source = Edc15V256Layout.BlockOf(address);
            if (source == 0)
            {
                r.Refused = true;
                r.Refusal = Edc15V256Layout.DescribeProblem(address, length);
                return r;
            }
            string problem = Edc15V256Layout.DescribeProblem(address, length);
            if (problem.Length > 0)
            {
                r.Refused = true;
                r.Refusal = problem;
                return r;
            }

            r.SourceBlock = source;

            int[] selected = Edc15V256Layout.BlocksFromMask(mask);
            if (selected.Length == 0)
            {
                // No selection = previous behaviour: only the source block.
                selected = new int[] { source };
            }

            int relative = Edc15V256Layout.RelativeOffset(address);
            List<int> targets = new List<int>();
            List<int> withoutSymbol = new List<int>();
            foreach (int b in selected)
            {
                int targetAddress = Edc15V256Layout.AddressInBlock(b, address);
                // Every target must pass the same check as the source:
                // data area, no checksum field, no window overflow, no code region.
                string targetProblem = Edc15V256Layout.DescribeProblem(targetAddress, length);
                if (targetProblem.Length > 0)
                {
                    r.Refused = true;
                    r.Refusal = "Target " + Edc15V256Layout.BlockNames[b - 1] + ": " + targetProblem;
                    return r;
                }
                targets.Add(b);
                if (probe != null && !probe(b, relative, length))
                {
                    withoutSymbol.Add(b);
                }
            }

            r.Targets = targets.ToArray();
            r.BlocksWithoutSymbol = withoutSymbol.ToArray();
            r.Message = BuildPlanMessage(r, address, length, relative);
            return r;
        }

        private static string BuildPlanMessage(TargetBlockWriteResult r, int address, int length, int relative)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Source: " + Edc15V256Layout.BlockNames[r.SourceBlock - 1] +
                          ", 0x" + address.ToString("X5") + ", " + length + " bytes");
            sb.AppendLine("Relative offset in the window: 0x" + relative.ToString("X4"));
            sb.AppendLine("Target blocks: " + r.BlockList(r.Targets));
            foreach (int b in r.Targets)
            {
                sb.AppendLine("  " + Edc15V256Layout.BlockNames[b - 1] + " -> 0x" +
                              Edc15V256Layout.AddressInBlock(b, address).ToString("X5"));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Performs a planned write operation.  <paramref name="writer"/>
        /// writes one block; in the application this is
        /// <c>Tools.savedatatobinary</c> so that the transaction log is filled.
        /// </summary>
        public static TargetBlockWriteResult Execute(TargetBlockWriteResult plan, string filename,
                                                    int address, int length, byte[] data,
                                                    RawBlockWriter writer)
        {
            if (plan == null)
            {
                throw new ArgumentNullException("plan");
            }
            if (plan.Refused)
            {
                return plan;
            }
            if (writer == null)
            {
                throw new ArgumentNullException("writer");
            }
            if (data == null || data.Length != length)
            {
                plan.Refused = true;
                plan.Refusal = "Data buffer does not match the length (" +
                               (data == null ? "null" : data.Length.ToString()) + " vs " + length + ").";
                return plan;
            }
            // Tools.savedatatobinary byte-swaps 16-bit words; with an odd length
            // reverseEndian returns a null buffer.  That must never happen.
            if ((length & 1) != 0)
            {
                plan.Refused = true;
                plan.Refusal = "Odd length (" + length + ") is not supported by the byte order.";
                return plan;
            }

            byte[] before = File.ReadAllBytes(filename);
            plan.VerifyBefore = EDC15VM256Checksum.Verify(before).Report;
            string fehler;
            plan.ChecksumsOkBefore = EDC15VM256Checksum.CountOkFields(before, out fehler);

            // 1) write - every address is block base + relative offset
            List<int> written = new List<int>();
            try
            {
                foreach (int b in plan.Targets)
                {
                    int targetAddress = Edc15V256Layout.AddressInBlock(b, address);
                    writer(targetAddress, length, data);
                    written.Add(b);
                }
            }
            catch (Exception ex)
            {
                plan.Refusal = "Write failed: " + ex.Message;
                plan.Ok = false;
                plan.Message = plan.Refusal;
                return plan;
            }

            // 2) compare and seal only the affected blocks
            byte[] after = File.ReadAllBytes(filename);
            List<int> changed = new List<int>();
            foreach (int b in written)
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
            plan.ChangedBlocks = changed.ToArray();

            bool[] fields = new bool[EDC15VM256Checksum.BlockCount];
            foreach (int b in written)
            {
                fields[Edc15V256Layout.ChecksumIndex(b)] = true;
            }

            string fixError;
            if (!EDC15VM256Checksum.FixSelected(after, fields, out fixError))
            {
                plan.Refusal = "Checksum could not be recomputed: " + fixError;
                plan.Ok = false;
                plan.Message = plan.Refusal;
                return plan;
            }
            File.WriteAllBytes(filename, after);
            plan.SealedBlocks = written.ToArray();

            // 3) final check
            byte[] final = File.ReadAllBytes(filename);
            plan.VerifyAfter = EDC15VM256Checksum.Verify(final).Report;
            plan.ChecksumsOkAfter = EDC15VM256Checksum.CountOkFields(final, out fehler);

            plan.Ok = plan.ChecksumsOkAfter == EDC15VM256Checksum.BlockCount;
            plan.Message = BuildResultMessage(plan, address, length);
            return plan;
        }

        private static string BuildResultMessage(TargetBlockWriteResult r, int address, int length)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Written: " + r.BlockList(r.Targets) +
                          " (" + length + " bytes from 0x" + address.ToString("X5") + " as source)");
            sb.AppendLine("Changed: " + r.BlockList(r.ChangedBlocks));
            sb.AppendLine("Resealed: " + r.BlockList(r.SealedBlocks));
            sb.AppendLine("Checksums before: " + r.ChecksumsOkBefore + "/" +
                          EDC15VM256Checksum.BlockCount);
            sb.AppendLine("Checksums after: " + r.ChecksumsOkAfter + "/" +
                          EDC15VM256Checksum.BlockCount);
            if (r.BlocksWithoutSymbol != null && r.BlocksWithoutSymbol.Length > 0)
            {
                sb.AppendLine("Note: there is no map of the same length at this offset in " +
                              r.BlockList(r.BlocksWithoutSymbol) + ".");
            }
            if (r.ChecksumsOkAfter < EDC15VM256Checksum.BlockCount)
            {
                sb.AppendLine("Warning: blocks that were not resealed were already faulty " +
                              "before the write and stay unchanged.");
            }
            return sb.ToString().TrimEnd();
        }
    }
}
