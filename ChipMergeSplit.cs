//---------------------------------------------------------------------------
//
// ChipMergeSplit.cs
//
// Byte-level merge and split of the two 128 KB halves of an EDC15V-5
// 256 KB image (e.g. VAG 038 906 018 P, SG 1623).
//
// The two flash chips are wired as a 16-bit word: the LOW chip supplies the
// even byte offsets (0x00000, 0x00002, ...) and the HIGH chip the odd ones
// (0x00001, 0x00003, ...).  On a little endian bus the LOW byte is therefore
// the low byte of every 16-bit word.  This class contains no UI and no file
// dialog logic; the dialogs and the round-trip test both use it, so the byte
// order is defined in exactly one place.
//
//---------------------------------------------------------------------------

using System;
using System.IO;
using System.Text;

namespace VAGSuite
{
    public static class ChipMergeSplit
    {
        /// <summary>Size of one chip / half image: 128 KB.</summary>
        public const int ChipSize = 0x20000;

        /// <summary>Size of the merged image: 256 KB.</summary>
        public const int ImageSize = 0x40000;

        /// <summary>Part number expected in a 256 KB EDC15V-5 image.</summary>
        public const string ExpectedPartnumber = "038906018P";

        /// <summary>
        /// Interleaves two equally long halves into one image:
        /// LOW byte first (even offset), HIGH byte second (odd offset).
        /// </summary>
        /// <exception cref="ArgumentNullException">A half is null.</exception>
        /// <exception cref="ArgumentException">The halves differ in length.</exception>
        public static byte[] Merge(byte[] low, byte[] high)
        {
            if (low == null) throw new ArgumentNullException("low");
            if (high == null) throw new ArgumentNullException("high");
            if (low.Length != high.Length)
            {
                throw new ArgumentException(
                    "LOW und HIGH sind unterschiedlich gross: " +
                    low.Length + " Byte bzw. " + high.Length + " Byte. " +
                    "Ein Merge ist nur bei gleicher Laenge moeglich.");
            }

            byte[] image = new byte[low.Length * 2];
            int o = 0;
            for (int i = 0; i < low.Length; i++)
            {
                image[o++] = low[i];   // even offset
                image[o++] = high[i];  // odd offset
            }
            return image;
        }

        /// <summary>
        /// Splits an image into its two halves.  <paramref name="lowIsEven"/>
        /// selects whether the even offsets go to LOW (standard little endian
        /// layout) or to HIGH (reversed view for a swapped chip population).
        /// </summary>
        /// <exception cref="ArgumentNullException">The image is null.</exception>
        /// <exception cref="ArgumentException">The image has an odd length.</exception>
        public static void Split(byte[] image, bool lowIsEven,
                                 out byte[] low, out byte[] high)
        {
            if (image == null) throw new ArgumentNullException("image");
            if ((image.Length & 1) != 0)
            {
                throw new ArgumentException(
                    "Die Datei hat eine ungerade Laenge (" + image.Length +
                    " Byte) und kann nicht in zwei gleich grosse Teile " +
                    "zerlegt werden.");
            }

            int half = image.Length / 2;
            low = new byte[half];
            high = new byte[half];
            for (int i = 0; i < half; i++)
            {
                byte first = image[2 * i];
                byte second = image[2 * i + 1];
                if (lowIsEven)
                {
                    low[i] = first;
                    high[i] = second;
                }
                else
                {
                    low[i] = second;
                    high[i] = first;
                }
            }
        }

        /// <summary>Hex preview ("00000000  12 34 56 ...") of the first bytes.</summary>
        public static string Preview(byte[] data, int byteCount)
        {
            if (data == null) return "(keine Daten)";
            int n = Math.Min(byteCount, data.Length);
            StringBuilder sb = new StringBuilder();
            sb.Append("00000000 ");
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(data[i].ToString("X2"));
            }
            if (n < byteCount) sb.Append("  ...");
            return sb.ToString();
        }

        /// <summary>
        /// Hex preview of the first merged bytes, i.e. the interleaved start of
        /// both halves.  Shows what the output will begin with.
        /// </summary>
        public static string PreviewMerged(byte[] low, byte[] high, int byteCount)
        {
            if (low == null || high == null) return "(keine Daten)";
            int n = Math.Min(byteCount, Math.Min(low.Length, high.Length) * 2);
            StringBuilder sb = new StringBuilder();
            sb.Append("00000000 ");
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(' ');
                byte b = ((i & 1) == 0) ? low[i / 2] : high[i / 2];
                sb.Append(b.ToString("X2"));
            }
            if (n < byteCount) sb.Append("  ...");
            return sb.ToString();
        }

        /// <summary>
        /// Searches the image for the ASCII part number.  A missing string is a
        /// hint, not proof: the number can be stored differently or absent in a
        /// modified image, so callers report it as a warning only.
        /// </summary>
        public static bool FindPartnumber(byte[] image, string partnumber,
                                          out int offset)
        {
            offset = -1;
            if (image == null || partnumber == null || partnumber.Length == 0)
            {
                return false;
            }
            byte[] needle = Encoding.ASCII.GetBytes(partnumber);
            int last = image.Length - needle.Length;
            for (int i = 0; i <= last; i++)
            {
                int j = 0;
                while (j < needle.Length && image[i + j] == needle[j])
                {
                    j++;
                }
                if (j == needle.Length)
                {
                    offset = i;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Default target name next to the LOW file: "&lt;name&gt;_merged.bin".</summary>
        public static string SuggestMergedPath(string lowPath)
        {
            if (string.IsNullOrEmpty(lowPath)) return string.Empty;
            string dir = Path.GetDirectoryName(lowPath);
            string name = Path.GetFileNameWithoutExtension(lowPath);
            return Path.Combine(dir, name + "_merged.bin");
        }

        /// <summary>
        /// Output name for one half.  A trailing "_merged" in the input name is
        /// dropped so that "ecu_merged.bin" yields "ecu_low.bin" / "ecu_high.bin".
        /// </summary>
        public static string SuggestHalfName(string imagePath, string suffix)
        {
            string name = Path.GetFileNameWithoutExtension(imagePath);
            if (name != null && name.EndsWith("_merged", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - "_merged".Length);
            }
            return name + "_" + suffix + ".bin";
        }

        /// <summary>Reads a file completely; the file is never modified.</summary>
        public static byte[] ReadFile(string path)
        {
            return File.ReadAllBytes(path);
        }

        /// <summary>Short German size text for dialogs.</summary>
        public static string SizeText(long bytes)
        {
            if (bytes == ChipSize) return bytes + " Byte (128 KB)";
            if (bytes == ImageSize) return bytes + " Byte (256 KB)";
            return bytes + " Byte";
        }
    }
}
