//---------------------------------------------------------------------------
//
// Edc15V256Layout.cs
//
// Geography of a 256 KB EDC15V-5 file (e.g. VAG 038 906 018 P, SG 1623)
// and the target block arithmetic derived from it.
//
// The three data blocks are copies of the same map set. Each block
// lies in its own 32 KB window and has the same structure there:
//
//   Block 1   window 0x38000 .. 0x3FFFF   checksum field 0x3FFFC
//   Block 2   window 0x00000 .. 0x07FFF   checksum field 0x07FFC
//   Block 3   window 0x08000 .. 0x0FFFF   checksum field 0x0FFFC
//   Code      0x10000 .. 0x37FFF           (never a write target)
//
// The data area of a window starts at +0xB80; the last four bytes of
// the window are the checksum field (see EDC15VM256Checksum).
//
// The block numbers correspond to the CodeID values assigned by the parser
// (CodeBlock 1 = 0x38000, 2 = 0x00000, 3 = 0x08000).
//
//---------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace VAGSuite
{
    /// <summary>
    /// Layout and address arithmetic of the three data blocks of a 256 KB EDC15V-5
    /// file.  All methods are purely computational and usable without a UI.
    /// </summary>
    public static class Edc15V256Layout
    {
        /// <summary>Exact file size this layout applies to.</summary>
        public const int ImageSize = 0x40000;

        /// <summary>Size of one block window.</summary>
        public const int BlockSize = 0x8000;

        /// <summary>Mask for the relative offset within a window.</summary>
        public const int LocalMask = BlockSize - 1;

        /// <summary>First byte of the data area in the window.</summary>
        public const int DataStart = 0xB80;

        /// <summary>First byte of the checksum field in the window (= end of data).</summary>
        public const int FieldOffsetInBlock = 0x7FFC;

        /// <summary>Start of the code region (inclusive).</summary>
        public const int CodeStart = 0x10000;

        /// <summary>End of the code region (exclusive).</summary>
        public const int CodeEnd = 0x38000;

        /// <summary>Window base per block number; index 0 = block 1.</summary>
        public static readonly int[] BlockBases = new int[] { 0x38000, 0x00000, 0x08000 };

        /// <summary>Display name per block number; index 0 = block 1.</summary>
        public static readonly string[] BlockNames = new string[] { "Block 1", "Block 2", "Block 3" };

        /// <summary>Address range per block number, as text.</summary>
        public static readonly string[] BlockRanges = new string[]
        {
            "0x38000-0x3FFFF",
            "0x00000-0x07FFF",
            "0x08000-0x0FFFF"
        };

        /// <summary>All block numbers in display order.</summary>
        public static readonly int[] AllBlocks = new int[] { 1, 2, 3 };

        /// <summary>Bit mask for "all three blocks".</summary>
        public const int MaskAll = 0x07;

        #region Bloecke und Adressen

        /// <summary>
        /// Block number (1..3) in whose window <paramref name="address"/> lies,
        /// or 0 if the address lies in no data block (e.g. code region).
        /// </summary>
        public static int BlockOf(int address)
        {
            for (int i = 0; i < BlockBases.Length; i++)
            {
                if (address >= BlockBases[i] && address < BlockBases[i] + BlockSize)
                {
                    return i + 1;
                }
            }
            return 0;
        }

        /// <summary>True if the address lies in the code region.</summary>
        public static bool IsCodeAddress(int address)
        {
            return address >= CodeStart && address < CodeEnd;
        }

        /// <summary>Relative offset within the 32 KB window.</summary>
        public static int RelativeOffset(int address)
        {
            return address & LocalMask;
        }

        /// <summary>
        /// The same position in another block: block base + relative
        /// offset.  This is deliberately <b>not</b> the absolute address.
        /// </summary>
        public static int AddressInBlock(int blockNumber, int address)
        {
            if (!IsValidBlockNumber(blockNumber))
            {
                throw new ArgumentOutOfRangeException("blockNumber");
            }
            return BlockBases[blockNumber - 1] + RelativeOffset(address);
        }

        public static bool IsValidBlockNumber(int blockNumber)
        {
            return blockNumber >= 1 && blockNumber <= 3;
        }

        /// <summary>Index of the checksum field (0..2) for a block number.</summary>
        public static int ChecksumIndex(int blockNumber)
        {
            switch (blockNumber)
            {
                case 1: return 2;   // checksum field 0x3FFFC
                case 2: return 0;   // checksum field 0x07FFC
                case 3: return 1;   // checksum field 0x0FFFC
                default: throw new ArgumentOutOfRangeException("blockNumber");
            }
        }

        /// <summary>Field address of the checksum field for a block number.</summary>
        public static int ChecksumFieldOffset(int blockNumber)
        {
            return BlockBases[blockNumber - 1] + FieldOffsetInBlock;
        }

        /// <summary>
        /// True if <paramref name="length"/> bytes from <paramref name="address"/>
        /// lie entirely in the data area of exactly one block: not in the
        /// vector/reserved range, not in the checksum field, not past the
        /// window end and not in the code region.
        /// </summary>
        public static bool RangeFitsDataArea(int address, int length, out int blockNumber)
        {
            blockNumber = BlockOf(address);
            if (blockNumber == 0) return false;
            if (length <= 0) return false;

            int local = RelativeOffset(address);
            if (local < DataStart) return false;
            if ((long)local + length > FieldOffsetInBlock) return false;
            return true;
        }

        /// <summary>
        /// Short reason why an address is unsuitable as a target; empty
        /// if it is fine.
        /// </summary>
        public static string DescribeProblem(int address, int length)
        {
            if (IsCodeAddress(address))
            {
                return "0x" + address.ToString("X5") + " lies in the code region (0x10000-0x37FFF).";
            }
            int block = BlockOf(address);
            if (block == 0)
            {
                return "0x" + address.ToString("X5") + " lies in none of the three data blocks.";
            }
            int local = RelativeOffset(address);
            if (local < DataStart)
            {
                return "0x" + address.ToString("X5") + " lies in the reserved area of " +
                       BlockNames[block - 1] + " (before 0x+" + DataStart.ToString("X") + ").";
            }
            if ((long)local + length > FieldOffsetInBlock)
            {
                return "0x" + address.ToString("X5") + " + " + length +
                       " bytes exceed the end of " + BlockNames[block - 1] + ".";
            }
            if (length <= 0)
            {
                return "Length must be greater than 0.";
            }
            return string.Empty;
        }

        #endregion

        #region Getriebeart

        private static readonly byte[] AutomaticMarker = new byte[]
        {
            0x45, 0x44, 0x43, 0x20, 0x20, 0x41, 0x47   // "EDC  AG"
        };

        private static readonly byte[] ManualMarker = new byte[]
        {
            0x45, 0x44, 0x43, 0x20, 0x20, 0x53, 0x47   // "EDC  SG"
        };

        /// <summary>
        /// Gearbox family named by the code block identifier of one data block:
        /// "Automatic" for "EDC  AG", "Manual" for "EDC  SG", empty when the
        /// block carries neither.  The value is read from the image, because the
        /// block order is not the same in every file.  A four-wheel drive cannot
        /// be told apart from a manual gearbox - both use "EDC  SG".
        /// </summary>
        public static string DescribeGearbox(byte[] image, int block)
        {
            if (image == null || !IsValidBlockNumber(block)) return string.Empty;
            int start = BlockBases[block - 1];
            if (start < 0 || start + BlockSize > image.Length) return string.Empty;

            int automatic = IndexOf(image, start, start + BlockSize, AutomaticMarker);
            int manual = IndexOf(image, start, start + BlockSize, ManualMarker);
            if (automatic >= 0 && (manual < 0 || automatic < manual)) return "Automatic";
            if (manual >= 0) return "Manual";
            return string.Empty;
        }

        /// <summary>
        /// Gearbox code of the code block identifier, for example "EAAJT20X"
        /// or "E88JT200".  The identifier reads
        /// "... EDC  SG  3102 0281010170 EAAJT20X ...", so the eight characters
        /// after the ten digit Bosch number are taken.  Some files glue the
        /// code to the part number ("E88JT200HEX038906018P"), which is why only
        /// the first eight characters of that token are used.  Empty when the
        /// block carries no such identifier.
        /// </summary>
        public static string DescribeGearboxCode(byte[] image, int block)
        {
            if (image == null || !IsValidBlockNumber(block)) return string.Empty;
            int start = BlockBases[block - 1];
            if (start < 0 || start + BlockSize > image.Length) return string.Empty;

            int marker = IndexOf(image, start, start + BlockSize, AutomaticMarker);
            int manual = IndexOf(image, start, start + BlockSize, ManualMarker);
            if (marker < 0 || (manual >= 0 && manual < marker)) marker = manual;
            if (marker < 0) return string.Empty;

            int end = Math.Min(marker + 160, image.Length);
            int i = marker;
            while (i < end - 10)
            {
                int digits = i;
                while (digits < end && image[digits] >= (byte)'0' && image[digits] <= (byte)'9') digits++;
                if (digits - i == 10 && digits < end && image[digits] == (byte)' ')
                {
                    int token = digits;
                    while (token < end && image[token] == (byte)' ') token++;
                    if (token + 8 <= image.Length)
                    {
                        System.Text.StringBuilder sb = new System.Text.StringBuilder(8);
                        for (int n = 0; n < 8; n++)
                        {
                            char c = (char)image[token + n];
                            if (!char.IsLetterOrDigit(c)) return string.Empty;
                            sb.Append(c);
                        }
                        return sb.ToString();
                    }
                }
                if (digits == i) i++;
                else i = digits;
            }
            return string.Empty;
        }

        private static int IndexOf(byte[] image, int start, int end, byte[] pattern)
        {
            int last = end - pattern.Length;
            for (int i = start; i <= last; i++)
            {
                int j = 0;
                while (j < pattern.Length && image[i + j] == pattern[j]) j++;
                if (j == pattern.Length) return i;
            }
            return -1;
        }

        #endregion

        #region Auswahlmaske

        /// <summary>
        /// Block numbers from a selection mask (bit 0 = block 1, bit 1 = block 2,
        /// bit 2 = block 3).  Mask 0 means "no selection" and returns an
        /// empty list - the caller then decides in favour of the source block.
        /// </summary>
        public static int[] BlocksFromMask(int mask)
        {
            List<int> result = new List<int>();
            for (int i = 0; i < 3; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    result.Add(i + 1);
                }
            }
            return result.ToArray();
        }

        public static int MaskFromBlocks(int[] blocks)
        {
            int mask = 0;
            if (blocks != null)
            {
                foreach (int b in blocks)
                {
                    if (IsValidBlockNumber(b)) mask |= 1 << (b - 1);
                }
            }
            return mask;
        }

        /// <summary>"Block 1 + Block 3" or "no selection".</summary>
        public static string DescribeMask(int mask)
        {
            int[] blocks = BlocksFromMask(mask);
            if (blocks.Length == 0) return "no selection";
            if (blocks.Length == 3) return "all three blocks";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (int b in blocks)
            {
                if (sb.Length > 0) sb.Append(" + ");
                sb.Append(BlockNames[b - 1]);
            }
            return sb.ToString();
        }

        #endregion

        #region Bilderkennung

        /// <summary>
        /// True if the buffer is exactly 256 KB and carries a known marker pair
        /// at all three block ends.  This way the target block logic becomes
        /// active only for genuine EDC15V-5 images and not for any other
        /// 256 KB file.
        /// </summary>
        public static bool HasKnownMarkers(byte[] bin)
        {
            if (bin == null || bin.Length != ImageSize)
            {
                return false;
            }
            foreach (EDC15VM256Checksum.Variant v in EDC15VM256Checksum.Variants)
            {
                bool all = true;
                for (int i = 0; i < BlockBases.Length && all; i++)
                {
                    int end = BlockBases[i] + BlockSize;
                    all = bin[end - 6] == v.Marker0 && bin[end - 5] == v.Marker1;
                }
                if (all) return true;
            }
            return false;
        }

        #endregion
    }
}
