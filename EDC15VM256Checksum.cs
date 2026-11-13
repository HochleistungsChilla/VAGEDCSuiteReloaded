//---------------------------------------------------------------------------
//
// EDC15VM256Checksum.cs
//
// Cleanroom implementation of the block checksum used by the 256 KB
// EDC15V-5 images (e.g. VAG 038 906 018 P, SG 1623).
//
// This code was written from the documented behaviour of the format, not by
// copying or transliterating disassembled code.  The only inputs were:
//   * the three checksum field offsets and the three data ranges,
//   * the two init values, the two final constants,
//   * the described 16-bit operations and their order,
//   * the observed property that a two byte marker sits six bytes before a
//     block end.
// It was validated field-by-field against reference images (see
// Docs/CHECKSUM_256KB.md).
//
// Two variants of the same arithmetic are known: they differ only in the
// marker bytes (0x27 0xF3 for 038 906 018 P, 0x84 0xF1 for 038 906 018 GA);
// the four mixing constants and the block layout are identical.
//
// Scope: exactly 256 KB images of this family.  Other sizes and other EDC15
// variants use different block layouts and are deliberately rejected.
//
//---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;

namespace VAGSuite
{
    /// <summary>
    /// Checksum for the three 16-bit-word blocks of a 256 KB EDC15V-5 image.
    /// </summary>
    public static class EDC15VM256Checksum
    {
        /// <summary>Exact image size this algorithm is defined for.</summary>
        public const int ImageSize = 0x40000;

        // Layout of the three blocks.  Every block starts 0xB80 bytes after
        // the beginning of its 32 KB window; its checksum field is the last
        // four bytes of the window (little endian, 32 bit).
        private const int WindowSize = 0x8000;
        private const int BlockSkip = 0xB80;

        // The marker is looked for in the last 4 KB of the window, on a
        // 0x80 byte grid, and must appear six bytes before the block end.
        private const int MarkerScanFrom = 0x7000;
        private const int MarkerScanStep = 0x80;

        /// <summary>
        /// One known variant of the format: the marker pair that identifies a
        /// block end plus the four mixing constants.  Marker and constants are
        /// kept together because they are the only family specific inputs; the
        /// block layout is shared by every variant.
        /// </summary>
        public sealed class Variant
        {
            /// <summary>Human readable name, e.g. "EDC15V-5 P".</summary>
            public readonly string Name;

            /// <summary>Marker byte six bytes before the block end.</summary>
            public readonly byte Marker0;

            /// <summary>Marker byte five bytes before the block end.</summary>
            public readonly byte Marker1;

            /// <summary>Start value of accumulator A.</summary>
            public readonly ushort InitA;

            /// <summary>Start value of accumulator B.</summary>
            public readonly ushort InitB;

            /// <summary>Value subtracted from A at the end of a block.</summary>
            public readonly ushort FinalSubA;

            /// <summary>Value xor-ed onto B at the end of a block.</summary>
            public readonly ushort FinalXorB;

            public Variant(string name, byte marker0, byte marker1,
                           ushort initA, ushort initB,
                           ushort finalSubA, ushort finalXorB)
            {
                Name = name;
                Marker0 = marker0;
                Marker1 = marker1;
                InitA = initA;
                InitB = initB;
                FinalSubA = finalSubA;
                FinalXorB = finalXorB;
            }

            /// <summary>True when the marker pair is present at that block end.</summary>
            public bool MatchesMarker(byte[] bin, int blockEnd)
            {
                return blockEnd - 6 >= 0 && blockEnd <= bin.Length &&
                       bin[blockEnd - 6] == Marker0 && bin[blockEnd - 5] == Marker1;
            }

            /// <summary>"27 F3" style rendering of the marker.</summary>
            public string MarkerText
            {
                get { return Marker0.ToString("X2") + " " + Marker1.ToString("X2"); }
            }
        }

        /// <summary>VAG 038 906 018 P, SG 1623 (marker 27 F3).</summary>
        public static readonly Variant VariantP =
            new Variant("EDC15V-5 P", 0x27, 0xF3, 0x8631, 0xEFCD, 0x8631, 0xDF9B);

        /// <summary>
        /// VAG 038 906 018 GA, SG 3102 (marker 84 F1).  Same constants as P, so
        /// the arithmetic is shared; only the marker differs.  Verified against
        /// the pristine image and its Stage1 derivative (3/3 fields each).
        /// </summary>
        public static readonly Variant VariantGA =
            new Variant("EDC15V-5 GA", 0x84, 0xF1, 0x8631, 0xEFCD, 0x8631, 0xDF9B);

        /// <summary>Every variant whose marker layout is known.</summary>
        public static readonly Variant[] Variants = new Variant[] { VariantP, VariantGA };

        /// <summary>
        /// Variant used when no marker is recognised.  Computing does not need
        /// the marker, and both known variants share the same constants, so the
        /// fallback cannot change any value; it only keeps old behaviour for
        /// images whose markers were moved.
        /// </summary>
        public static readonly Variant DefaultVariant = VariantP;

        /// <summary>File offsets of the three checksum fields (4 bytes each).</summary>
        public static readonly int[] FieldOffsets = new int[]
        {
            0x07FFC,
            0x0FFFC,
            0x3FFFC
        };

        /// <summary>First data byte of each block; the field closes the block.</summary>
        public static readonly int[] BlockStarts = new int[]
        {
            0x00B80,
            0x08B80,
            0x38B80
        };

        /// <summary>Number of blocks/fields of this format.</summary>
        public const int BlockCount = 3;

        #region public API

        /// <summary>
        /// Computes the three checksum values of <paramref name="bin"/> without
        /// modifying it.  The values are returned in block order, i.e.
        /// [0] belongs to 0x07FFC, [1] to 0x0FFFC and [2] to 0x3FFFC.
        /// </summary>
        /// <returns>true when the image has the expected size and all blocks
        /// could be evaluated; false otherwise, with a reason in
        /// <paramref name="fehler"/>.</returns>
        public static bool TryCompute(byte[] bin, out uint[] values, out string fehler)
        {
            string markerInfo;
            Variant variant = DetectVariant(bin, out markerInfo);
            if (variant == null)
            {
                // No marker matched: the arithmetic does not depend on it, so
                // the default variant is used and the markers stay a
                // plausibility question for CheckMarkers.
                variant = DefaultVariant;
            }
            return TryCompute(bin, variant, out values, out fehler);
        }

        /// <summary>
        /// Same as <see cref="TryCompute(byte[], out uint[], out string)"/>, but
        /// with an explicitly selected variant.  Only the four mixing constants
        /// depend on it, so a wrong choice is visible in every computed field.
        /// </summary>
        public static bool TryCompute(byte[] bin, Variant variant,
                                     out uint[] values, out string fehler)
        {
            values = null;
            fehler = null;

            if (!CheckImage(bin, out fehler))
            {
                return false;
            }
            if (variant == null)
            {
                fehler = "No checksum variant supplied.";
                return false;
            }

            uint[] result = new uint[BlockCount];
            for (int i = 0; i < BlockCount; i++)
            {
                int blockEnd = BlockStarts[i] + (WindowSize - BlockSkip);
                // Sanity: the field must be the last four bytes of the block
                // and must stay inside the image.
                if (blockEnd != FieldOffsets[i] + 4 || blockEnd > bin.Length)
                {
                    fehler = "Block " + i + " is outside the expected layout.";
                    return false;
                }

                result[i] = ComputeBlock(bin, BlockStarts[i], FieldOffsets[i], variant);
            }

            values = result;
            return true;
        }

        /// <summary>
        /// Recomputes the three fields and writes them into
        /// <paramref name="bin"/> (little endian).  The data ranges themselves
        /// are never touched.  On failure the buffer is left unchanged.
        /// </summary>
        public static bool Fix(byte[] bin, out string fehler)
        {
            fehler = null;

            uint[] values;
            if (!TryCompute(bin, out values, out fehler))
            {
                return false;
            }

            // All values are known before the first write, so a failure can
            // never leave a half-corrected image behind.
            for (int i = 0; i < BlockCount; i++)
            {
                int p = FieldOffsets[i];
                bin[p + 0] = (byte)(values[i] & 0xFF);
                bin[p + 1] = (byte)((values[i] >> 8) & 0xFF);
                bin[p + 2] = (byte)((values[i] >> 16) & 0xFF);
                bin[p + 3] = (byte)((values[i] >> 24) & 0xFF);
            }
            return true;
        }

        /// <summary>
        /// Recomputes only the selected fields and writes them into
        /// <paramref name="bin"/> (little endian).  <paramref name="blocks"/>[i]
        /// belongs to FieldOffsets[i]; unselected fields are never touched, so a
        /// block that was not edited keeps its existing (still valid) value.
        /// On failure the buffer is left unchanged.
        /// </summary>
        public static bool FixSelected(byte[] bin, bool[] blocks, out string fehler)
        {
            fehler = null;

            if (blocks == null || blocks.Length != BlockCount)
            {
                fehler = "Exactly " + BlockCount + " block flags must be supplied.";
                return false;
            }

            uint[] values;
            if (!TryCompute(bin, out values, out fehler))
            {
                return false;
            }

            for (int i = 0; i < BlockCount; i++)
            {
                if (!blocks[i])
                {
                    continue;
                }
                int p = FieldOffsets[i];
                bin[p + 0] = (byte)(values[i] & 0xFF);
                bin[p + 1] = (byte)((values[i] >> 8) & 0xFF);
                bin[p + 2] = (byte)((values[i] >> 16) & 0xFF);
                bin[p + 3] = (byte)((values[i] >> 24) & 0xFF);
            }
            return true;
        }

        /// <summary>
        /// Counts the fields whose stored value matches the computed
        /// one (0..3).  For reports and tests.
        /// </summary>
        public static int CountOkFields(byte[] bin, out string fehler)
        {
            fehler = null;
            uint[] values;
            if (!TryCompute(bin, out values, out fehler))
            {
                return 0;
            }

            int ok = 0;
            for (int i = 0; i < BlockCount; i++)
            {
                int p = FieldOffsets[i];
                uint stored = (uint)(bin[p] | (bin[p + 1] << 8) | (bin[p + 2] << 16) | (bin[p + 3] << 24));
                if (stored == values[i]) ok++;
            }
            return ok;
        }

        /// <summary>Result of a read-only checksum verification.</summary>
        public sealed class VerifyResult
        {
            /// <summary>True when all three stored fields match the computed values.</summary>
            public bool AllOk;

            /// <summary>Human readable, one line per block.</summary>
            public string Report = string.Empty;
        }

        /// <summary>
        /// Compares the three stored fields against the computed ones without
        /// touching the image.  Used by the merge dialog to decide whether a
        /// freshly merged image is structurally sound.
        /// </summary>
        public static VerifyResult Verify(byte[] bin)
        {
            VerifyResult result = new VerifyResult();
            uint[] values;
            string fehler;

            if (!TryCompute(bin, out values, out fehler))
            {
                result.AllOk = false;
                result.Report = "   " + (fehler ?? "unknown error") + Environment.NewLine;
                return result;
            }

            StringBuilder sb = new StringBuilder();
            bool allOk = true;
            for (int i = 0; i < BlockCount; i++)
            {
                int p = FieldOffsets[i];
                uint stored = (uint)(bin[p] | (bin[p + 1] << 8) | (bin[p + 2] << 16) | (bin[p + 3] << 24));
                bool hit = stored == values[i];
                if (!hit)
                {
                    allOk = false;
                }
                sb.AppendLine("   Block " + (i + 1) + " @0x" + p.ToString("X5") +
                              ": calc=0x" + values[i].ToString("X8") +
                              " file=0x" + stored.ToString("X8") +
                              (hit ? "  OK" : "  WRONG"));
            }

            result.AllOk = allOk;
            result.Report = sb.ToString();
            return result;
        }

        /// <summary>
        /// Plausibility check of the block boundaries using the variant marker
        /// (0x27 0xF3 for P, 0x84 0xF1 for GA).  The marker is a layout hint,
        /// not proof: an image can be structurally plausible and still carry
        /// wrong checksums, and a reworked image can move the marker without
        /// changing the ranges.
        /// </summary>
        public static bool CheckMarkers(byte[] bin, out string info)
        {
            info = null;
            if (!CheckImage(bin, out info))
            {
                return false;
            }

            Variant found = DetectVariant(bin, out info);
            if (found == null)
            {
                return false;
            }
            info = "All three block ends carry marker 0x" +
                   found.MarkerText + " (" + found.Name + ").";
            return true;
        }

        /// <summary>
        /// Returns the variant whose marker pair sits at all three expected
        /// block ends, or null with a diagnostic in <paramref name="info"/>.
        /// Two matching variants would be reported as ambiguous, never guessed.
        /// </summary>
        public static Variant DetectVariant(byte[] bin, out string info)
        {
            info = null;
            if (!CheckImage(bin, out info))
            {
                return null;
            }

            List<Variant> hits = new List<Variant>();
            foreach (Variant v in Variants)
            {
                bool all = true;
                for (int i = 0; i < BlockCount && all; i++)
                {
                    int expectedEnd = FieldOffsets[i] + 4;
                    all = FindBlockEnd(bin, BlockStarts[i] - BlockSkip, v) == expectedEnd;
                }
                if (all)
                {
                    hits.Add(v);
                }
            }

            if (hits.Count == 1)
            {
                return hits[0];
            }
            if (hits.Count > 1)
            {
                StringBuilder names = new StringBuilder();
                foreach (Variant v in hits)
                {
                    if (names.Length > 0) names.Append(", ");
                    names.Append(v.Name);
                }
                info = "Ambiguous markers: " + names.ToString() + ".";
                return null;
            }

            // Nothing matched: report what was actually found per block.
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < BlockCount; i++)
            {
                int expectedEnd = FieldOffsets[i] + 4;
                int scannedEnd = FindBlockEnd(bin, BlockStarts[i] - BlockSkip, null);
                sb.Append("Block " + (i + 1) + " @0x" + expectedEnd.ToString("X5") + ": ");
                if (scannedEnd == expectedEnd)
                {
                    sb.Append("marker of an unknown pair. ");
                }
                else if (scannedEnd < 0)
                {
                    sb.Append("no known marker. ");
                }
                else
                {
                    sb.Append("marker at 0x" + scannedEnd.ToString("X5") +
                              " instead of 0x" + expectedEnd.ToString("X5") + ". ");
                }
            }
            info = sb.ToString().TrimEnd();
            return null;
        }

        #endregion

        #region internals

        private static bool CheckImage(byte[] bin, out string fehler)
        {
            fehler = null;
            if (bin == null)
            {
                fehler = "No data buffer supplied.";
                return false;
            }
            if (bin.Length != ImageSize)
            {
                fehler = "Falsche Dateigroesse: 0x" + bin.Length.ToString("X") +
                         " statt 0x" + ImageSize.ToString("X") + " (256 KB).";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Finds the first position on the 0x80 grid in the scan window whose
        /// six-bytes-before bytes are the marker; returns the block end or -1.
        /// With a null variant, any known variant's marker counts.
        /// </summary>
        private static int FindBlockEnd(byte[] bin, int windowStart, Variant variant)
        {
            for (int end = windowStart + MarkerScanFrom;
                 end <= windowStart + WindowSize;
                 end += MarkerScanStep)
            {
                if (end - 6 < 0 || end > bin.Length)
                {
                    break;
                }
                if (variant != null)
                {
                    if (variant.MatchesMarker(bin, end))
                    {
                        return end;
                    }
                }
                else
                {
                    foreach (Variant v in Variants)
                    {
                        if (v.MatchesMarker(bin, end))
                        {
                            return end;
                        }
                    }
                }
            }
            return -1;
        }

        private static ushort ReadWord(byte[] bin, int pos)
        {
            return (ushort)(bin[pos] | (bin[pos + 1] << 8));
        }

        private static ushort Rol16(ushort value, int count)
        {
            count &= 0x0F;
            if (count == 0)
            {
                return value;
            }
            return (ushort)(((value << count) | (value >> (16 - count))) & 0xFFFF);
        }

        private static ushort Ror16(ushort value, int count)
        {
            count &= 0x0F;
            if (count == 0)
            {
                return value;
            }
            return (ushort)(((value >> count) | (value << (16 - count))) & 0xFFFF);
        }

        /// <summary>
        /// One block.  The body consumes four 16-bit words per cycle and stops
        /// as soon as the field offset is reached, which is why the cycle has
        /// two exit points (after word 2 and after word 4).  A block holds
        /// 14878 words, so the last cycle ends after word 2.
        /// </summary>
        private static uint ComputeBlock(byte[] bin, int start, int fieldOffset, Variant variant)
        {
            ushort sa = variant.InitA;
            ushort sb = variant.InitB;

            // The running counter starts at the block start, folded into
            // 16 bits.  If it is zero or in the upper half, 0x8000 is added
            // once so that it starts in the lower half.
            int cnt = start & 0xFFFF;
            if (cnt == 0 || (cnt & 0x8000) != 0)
            {
                cnt = (cnt + 0x8000) & 0xFFFF;
            }

            int pos = start;
            int end = fieldOffset;

            while (true)
            {
                int carry;

                // --- word 1: mix into A, then rotate A by the low nibble of B
                sa ^= ReadWord(bin, pos);
                pos += 2;
                cnt = (cnt + 2) & 0xFFFF;

                carry = 0;
                int nibbleB = sb & 0x0F;
                if (nibbleB != 0)
                {
                    sa = Rol16(sa, nibbleB);
                    // A rotation can lift bit 0 out of the low nibble; that
                    // bit is carried into the subtraction below.
                    carry = sa & 1;
                }

                // --- word 2: subtract from B (with carry), then fold in A
                sb = (ushort)((sb - ReadWord(bin, pos) - carry) & 0xFFFF);
                pos += 2;
                cnt = (cnt + 2) & 0xFFFF;

                sb ^= sa;

                if (pos >= end)
                {
                    break;
                }

                // --- word 3: subtract from A, add counter bias
                sa = (ushort)((sa - ReadWord(bin, pos)) & 0xFFFF);
                pos += 2;
                cnt = (cnt + 2) & 0xFFFF;

                sa = (ushort)((sa + cnt - 1) & 0xFFFF);

                // --- word 4: fold into B by xor, then rotate B by A's nibble
                sb ^= ReadWord(bin, pos);
                pos += 2;
                cnt = (cnt + 2) & 0xFFFF;

                int nibbleA = sa & 0x0F;
                if (nibbleA != 0)
                {
                    sb = Ror16(sb, nibbleA);
                }

                if (pos >= end)
                {
                    break;
                }
            }

            // Final mixing: the counter is part of the result, B gets a
            // constant xor.  The result is B in the high half, A in the low.
            sa = (ushort)((sa - variant.FinalSubA + cnt + 1) & 0xFFFF);
            sb = (ushort)(sb ^ variant.FinalXorB);

            return ((uint)sb << 16) | sa;
        }

        #endregion
    }
}
