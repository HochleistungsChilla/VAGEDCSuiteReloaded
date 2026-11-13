using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using DevExpress.XtraEditors;

namespace VAGSuite
{
    /// <summary>
    /// Merge dialog: combines the 128 KB LOW and HIGH chips into one 256 KB
    /// image and validates the result (size, part number, checksums).
    ///
    /// The byte order is not implicit any more: LOW always supplies the even
    /// offsets (first byte of each 16-bit word), HIGH the odd ones.  The
    /// dialog labels both inputs accordingly and offers a swap button, so a
    /// mixed-up selection is visible before anything is written.
    /// </summary>
    public partial class frmBinmerger : DevExpress.XtraEditors.XtraForm
    {
        private const int PreviewBytes = 16;

        private string m_lastSuggestedTarget = string.Empty;
        private bool m_targetChosenInDialog;

        public frmBinmerger()
        {
            InitializeComponent();
            RefreshState();
        }

        #region file selection

        private void btnBrowseLow_Click(object sender, EventArgs e)
        {
            string path = BrowseForBinary(buttonEditLow.Text);
            if (path != null)
            {
                buttonEditLow.Text = path;
                RefreshState();
            }
        }

        private void btnBrowseHigh_Click(object sender, EventArgs e)
        {
            string path = BrowseForBinary(buttonEditHigh.Text);
            if (path != null)
            {
                buttonEditHigh.Text = path;
                RefreshState();
            }
        }

        private void btnSwap_Click(object sender, EventArgs e)
        {
            string tmp = buttonEditLow.Text;
            buttonEditLow.Text = buttonEditHigh.Text;
            buttonEditHigh.Text = tmp;
            RefreshState();
        }

        private void buttonEditLow_TextChanged(object sender, EventArgs e)
        {
            RefreshState();
        }

        private void buttonEditHigh_TextChanged(object sender, EventArgs e)
        {
            RefreshState();
        }

        private string BrowseForBinary(string current)
        {
            openFileDialog1.FileName = string.Empty;
            if (!string.IsNullOrEmpty(current))
            {
                try
                {
                    string dir = Path.GetDirectoryName(current);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        openFileDialog1.InitialDirectory = dir;
                    }
                }
                catch (Exception)
                {
                }
            }
            if (openFileDialog1.ShowDialog(this) == DialogResult.OK)
            {
                return openFileDialog1.FileName;
            }
            return null;
        }

        private void btnBrowseOut_Click(object sender, EventArgs e)
        {
            saveFileDialog1.FileName = Path.GetFileName(buttonEditOut.Text);
            if (string.IsNullOrEmpty(saveFileDialog1.FileName))
            {
                saveFileDialog1.FileName = "image_merged.bin";
            }
            if (!string.IsNullOrEmpty(buttonEditOut.Text))
            {
                try
                {
                    string dir = Path.GetDirectoryName(buttonEditOut.Text);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        saveFileDialog1.InitialDirectory = dir;
                    }
                }
                catch (Exception)
                {
                }
            }
            if (saveFileDialog1.ShowDialog(this) == DialogResult.OK)
            {
                buttonEditOut.Text = saveFileDialog1.FileName;
                m_targetChosenInDialog = true;
            }
        }

        #endregion

        #region preview and status

        private void RefreshState()
        {
            StringBuilder preview = new StringBuilder();
            bool lowThere = File.Exists(buttonEditLow.Text);
            bool highThere = File.Exists(buttonEditHigh.Text);

            preview.Append("LOW    " + PreviewOf(buttonEditLow.Text, lowThere));
            preview.AppendLine();
            preview.Append("HIGH   " + PreviewOf(buttonEditHigh.Text, highThere));
            preview.AppendLine();

            if (lowThere && highThere)
            {
                try
                {
                    byte[] lowHead = ReadHead(buttonEditLow.Text, PreviewBytes);
                    byte[] highHead = ReadHead(buttonEditHigh.Text, PreviewBytes);
                    preview.Append("MERGE  " +
                        ChipMergeSplit.PreviewMerged(lowHead, highHead, PreviewBytes));
                }
                catch (Exception ex)
                {
                    preview.Append("MERGE  (Vorschau nicht lesbar: " + ex.Message + ")");
                }
            }
            else
            {
                preview.Append("MERGE  (beide Dateien waehlen)");
            }
            memoPreview.Text = preview.ToString();

            if (lowThere && highThere)
            {
                FileInfo fl = new FileInfo(buttonEditLow.Text);
                FileInfo fh = new FileInfo(buttonEditHigh.Text);
                if (fl.Length != fh.Length)
                {
                    SetStatus("LOW " + ChipMergeSplit.SizeText(fl.Length) +
                        " / HIGH " + ChipMergeSplit.SizeText(fh.Length) +
                        " - Laengen unterschiedlich, Merge nicht moeglich.", true);
                }
                else if (fl.Length != ChipMergeSplit.ChipSize)
                {
                    SetStatus("LOW/HIGH je " + ChipMergeSplit.SizeText(fl.Length) +
                        " - erwartet werden 128 KB pro Chip.", true);
                }
                else
                {
                    SetStatus("LOW/HIGH je " + ChipMergeSplit.SizeText(fl.Length) +
                        " - bereit zum Merge.", false);
                }
            }
            else if (lowThere || highThere)
            {
                SetStatus("Bitte noch die zweite Datei waehlen.", false);
            }
            else
            {
                SetStatus(string.Empty, false);
            }

            string suggestion = lowThere
                ? ChipMergeSplit.SuggestMergedPath(buttonEditLow.Text)
                : string.Empty;
            if (buttonEditOut.Text.Length == 0 ||
                buttonEditOut.Text == m_lastSuggestedTarget)
            {
                buttonEditOut.Text = suggestion;
            }
            m_lastSuggestedTarget = suggestion;
        }

        private void SetStatus(string text, bool warn)
        {
            labelStatus.Text = text;
            labelStatus.ForeColor = warn ? Color.DarkRed : SystemColors.ControlText;
        }

        private static string PreviewOf(string path, bool exists)
        {
            if (!exists)
            {
                return "(nicht gewaehlt)";
            }
            try
            {
                return ChipMergeSplit.Preview(ReadHead(path, PreviewBytes), PreviewBytes);
            }
            catch (Exception ex)
            {
                return "(nicht lesbar: " + ex.Message + ")";
            }
        }

        private static byte[] ReadHead(string path, int count)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open,
                                                  FileAccess.Read, FileShare.ReadWrite))
            {
                int n = (int)Math.Min(count, fs.Length);
                byte[] buffer = new byte[n];
                int offset = 0;
                while (offset < n)
                {
                    int read = fs.Read(buffer, offset, n - offset);
                    if (read <= 0)
                    {
                        break;
                    }
                    offset += read;
                }
                if (offset != n)
                {
                    byte[] shorter = new byte[offset];
                    Array.Copy(buffer, shorter, offset);
                    return shorter;
                }
                return buffer;
            }
        }

        #endregion

        #region merge and validation

        private void btnMerge_Click(object sender, EventArgs e)
        {
            try
            {
                if (!File.Exists(buttonEditLow.Text))
                {
                    MessageBox.Show(this, "Die LOW-Datei existiert nicht.",
                        "Merge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!File.Exists(buttonEditHigh.Text))
                {
                    MessageBox.Show(this, "Die HIGH-Datei existiert nicht.",
                        "Merge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                FileInfo lowInfo = new FileInfo(buttonEditLow.Text);
                FileInfo highInfo = new FileInfo(buttonEditHigh.Text);
                if (lowInfo.Length != highInfo.Length)
                {
                    MessageBox.Show(this,
                        "LOW und HIGH sind unterschiedlich gross:\r\n\r\n" +
                        "LOW : " + lowInfo.Length + " Byte\r\n" +
                        "HIGH: " + highInfo.Length + " Byte\r\n\r\n" +
                        "Ein Merge ist nur bei gleicher Laenge moeglich. " +
                        "Es wurde nichts geschrieben.",
                        "Merge abgebrochen", MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }
                if (lowInfo.Length != ChipMergeSplit.ChipSize)
                {
                    if (MessageBox.Show(this,
                            "Die Teile sind nicht 128 KB gross (" +
                            ChipMergeSplit.SizeText(lowInfo.Length) +
                            "), das Ergebnis waere keine 256-KB-Datei.\r\n\r\n" +
                            "Trotzdem fortfahren?",
                            "Unerwartete Groesse", MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning) != DialogResult.Yes)
                    {
                        return;
                    }
                }

                string target = buttonEditOut.Text.Trim();
                if (target.Length == 0)
                {
                    MessageBox.Show(this, "Bitte eine Zieldatei angeben.",
                        "Merge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (string.Equals(Path.GetFullPath(target),
                                  Path.GetFullPath(buttonEditLow.Text),
                                  StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFullPath(target),
                                  Path.GetFullPath(buttonEditHigh.Text),
                                  StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(this,
                        "Die Zieldatei darf nicht mit einer der Eingangsdateien " +
                        "identisch sein. Es wurde nichts geschrieben.",
                        "Merge abgebrochen", MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }
                if (File.Exists(target) && !m_targetChosenInDialog)
                {
                    if (MessageBox.Show(this,
                            "Die Zieldatei existiert bereits:\r\n\r\n" + target +
                            "\r\n\r\nUeberschreiben?",
                            "Datei existiert", MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question) != DialogResult.Yes)
                    {
                        return;
                    }
                }

                byte[] low = ChipMergeSplit.ReadFile(buttonEditLow.Text);
                byte[] high = ChipMergeSplit.ReadFile(buttonEditHigh.Text);
                byte[] image;
                try
                {
                    image = ChipMergeSplit.Merge(low, high);
                }
                catch (ArgumentException ex)
                {
                    MessageBox.Show(this, ex.Message, "Merge abgebrochen",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                File.WriteAllBytes(target, image);
                m_targetChosenInDialog = false;

                string report;
                bool partOk;
                bool allOk = BuildValidationReport(target, low, high, image,
                                                   out partOk, out report);
                memoResult.Text = report;

                if (allOk && partOk)
                {
                    MessageBox.Show(this,
                        "Merge abgeschlossen.\r\n\r\n" +
                        "Ergebnis: " + ChipMergeSplit.SizeText(image.Length) + "\r\n" +
                        "Teilenummer " + ChipMergeSplit.ExpectedPartnumber + ": gefunden\r\n" +
                        "Pruefsumme: 3/3 OK",
                        "Merge und Validierung", MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else if (allOk)
                {
                    MessageBox.Show(this,
                        "Merge abgeschlossen.\r\n\r\n" +
                        "Ergebnis: " + ChipMergeSplit.SizeText(image.Length) + "\r\n" +
                        "Pruefsumme: 3/3 OK\r\n\r\n" +
                        "Hinweis: Die Teilenummer " +
                        ChipMergeSplit.ExpectedPartnumber +
                        " wurde nicht gefunden. Das kann bei umgebauten Images " +
                        "normal sein.",
                        "Merge mit Hinweis", MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show(this,
                        "Merge abgeschlossen, aber die Validierung meldet Abweichungen.\r\n\r\n" +
                        report + "\r\n" +
                        "Es wurde nichts automatisch korrigiert.",
                        "Merge mit Warnungen", MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Merge fehlgeschlagen",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Builds the text shown in the validation pane.  Returns true only when
        /// size, part number and all three checksum fields are fine.  The image
        /// is never modified here.
        /// </summary>
        private bool BuildValidationReport(string target, byte[] low, byte[] high,
                                           byte[] image, out bool partOk,
                                           out string report)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Zieldatei: " + target);
            sb.AppendLine("LOW      : " + buttonEditLow.Text +
                          "  (" + low.Length + " Byte)");
            sb.AppendLine("HIGH     : " + buttonEditHigh.Text +
                          "  (" + high.Length + " Byte)");
            sb.AppendLine();

            bool sizeOk = image.Length == ChipMergeSplit.ImageSize;
            sb.AppendLine("1) Groesse");
            sb.AppendLine("   " + image.Length + " Byte - " +
                (sizeOk ? "OK (256 KB)" : "FEHLER (erwartet 262144 Byte / 256 KB)"));
            sb.AppendLine();

            int partOffset;
            partOk = ChipMergeSplit.FindPartnumber(
                image, ChipMergeSplit.ExpectedPartnumber, out partOffset);
            sb.AppendLine("2) Teilenummer");
            if (partOk)
            {
                sb.AppendLine("   " + ChipMergeSplit.ExpectedPartnumber +
                    " gefunden bei 0x" + partOffset.ToString("X5") + " - OK");
            }
            else
            {
                sb.AppendLine("   " + ChipMergeSplit.ExpectedPartnumber +
                    " nicht gefunden - HINWEIS (kein Beweis fuer ein falsches Image)");
            }
            sb.AppendLine();

            sb.AppendLine("3) Pruefsumme");
            EDC15VM256Checksum.VerifyResult verify =
                EDC15VM256Checksum.Verify(image);
            sb.Append(verify.Report);
            sb.AppendLine("   Keine automatische Korrektur.");

            report = sb.ToString();
            // A missing part number is a hint, not a hard failure; size and
            // checksum decide whether the merge is structurally sound.
            return sizeOk && verify.AllOk;
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        #endregion
    }
}
