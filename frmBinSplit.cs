using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using DevExpress.XtraEditors;

namespace VAGSuite
{
    /// <summary>
    /// Split dialog: cuts one 256 KB image into the two 128 KB chip files.
    ///
    /// The dialog makes the byte order explicit instead of hard-coding
    /// chip1/chip2: the normal layout writes even offsets to "&lt;name&gt;_low.bin"
    /// and odd offsets to "&lt;name&gt;_high.bin".  A reversed option exists for
    /// boards whose chips are populated the other way round.
    /// </summary>
    public partial class frmBinSplit : DevExpress.XtraEditors.XtraForm
    {
        public frmBinSplit()
        {
            InitializeComponent();
            RefreshState();
        }

        public frmBinSplit(string inputFile)
            : this()
        {
            if (!string.IsNullOrEmpty(inputFile))
            {
                buttonEditIn.Text = inputFile;
                RefreshState();
            }
        }

        #region selection

        private void btnBrowseIn_Click(object sender, EventArgs e)
        {
            openFileDialog1.FileName = string.Empty;
            if (!string.IsNullOrEmpty(buttonEditIn.Text))
            {
                try
                {
                    string dir = Path.GetDirectoryName(buttonEditIn.Text);
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
                buttonEditIn.Text = openFileDialog1.FileName;
                RefreshState();
            }
        }

        private void btnBrowseDir_Click(object sender, EventArgs e)
        {
            if (Directory.Exists(buttonEditOutDir.Text))
            {
                folderBrowserDialog1.SelectedPath = buttonEditOutDir.Text;
            }
            else if (File.Exists(buttonEditIn.Text))
            {
                string dir = Path.GetDirectoryName(buttonEditIn.Text);
                if (Directory.Exists(dir))
                {
                    folderBrowserDialog1.SelectedPath = dir;
                }
            }
            if (folderBrowserDialog1.ShowDialog(this) == DialogResult.OK)
            {
                buttonEditOutDir.Text = folderBrowserDialog1.SelectedPath;
                RefreshState();
            }
        }

        private void buttonEditIn_TextChanged(object sender, EventArgs e)
        {
            RefreshState();
        }

        private void buttonEditOutDir_TextChanged(object sender, EventArgs e)
        {
            RefreshState();
        }

        #endregion

        #region state

        private void RefreshState()
        {
            bool exists = File.Exists(buttonEditIn.Text);
            if (exists)
            {
                long length = new FileInfo(buttonEditIn.Text).Length;
                if ((length & 1) != 0)
                {
                    SetStatus("Laenge " + length + " Byte ist ungerade - " +
                              "ein Split ist nicht moeglich.", true);
                }
                else if (length != ChipMergeSplit.ImageSize)
                {
                    SetStatus("Laenge " + ChipMergeSplit.SizeText(length) +
                              " - erwartet werden 256 KB.", true);
                }
                else
                {
                    SetStatus("Laenge " + ChipMergeSplit.SizeText(length) + " - bereit.", false);
                }

                if (buttonEditOutDir.Text.Length == 0)
                {
                    buttonEditOutDir.Text = Path.GetDirectoryName(buttonEditIn.Text);
                }
            }
            else
            {
                SetStatus(string.Empty, false);
            }

            if (exists && Directory.Exists(buttonEditOutDir.Text))
            {
                labelNames.Text = "Ziel: " +
                    Path.Combine(buttonEditOutDir.Text,
                                 ChipMergeSplit.SuggestHalfName(buttonEditIn.Text, "low")) +
                    "  und  " +
                    ChipMergeSplit.SuggestHalfName(buttonEditIn.Text, "high");
            }
            else
            {
                labelNames.Text = "Ziel: Ausgabe-Verzeichnis waehlen";
            }
        }

        private void SetStatus(string text, bool warn)
        {
            labelInStatus.Text = text;
            labelInStatus.ForeColor = warn ? Color.DarkRed : SystemColors.ControlText;
        }

        #endregion

        #region split

        private void btnSplit_Click(object sender, EventArgs e)
        {
            try
            {
                if (!File.Exists(buttonEditIn.Text))
                {
                    MessageBox.Show(this, "Die Eingabedatei existiert nicht.",
                        "Split", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                string outDir = buttonEditOutDir.Text.Trim();
                if (!Directory.Exists(outDir))
                {
                    MessageBox.Show(this, "Das Ausgabe-Verzeichnis existiert nicht:\r\n\r\n" +
                        outDir, "Split", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                FileInfo info = new FileInfo(buttonEditIn.Text);
                if ((info.Length & 1) != 0)
                {
                    MessageBox.Show(this,
                        "Die Datei hat eine ungerade Laenge (" + info.Length +
                        " Byte). Sie kann nicht in zwei gleich grosse Teile " +
                        "zerlegt werden. Es wurde nichts geschrieben.",
                        "Split abgebrochen", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                if (info.Length != ChipMergeSplit.ImageSize)
                {
                    if (MessageBox.Show(this,
                            "Die Datei ist nicht 256 KB gross (" +
                            ChipMergeSplit.SizeText(info.Length) + ").\r\n\r\n" +
                            "Trotzdem aufteilen?",
                            "Unerwartete Groesse", MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning) != DialogResult.Yes)
                    {
                        return;
                    }
                }

                string lowPath = Path.Combine(outDir,
                    ChipMergeSplit.SuggestHalfName(buttonEditIn.Text, "low"));
                string highPath = Path.Combine(outDir,
                    ChipMergeSplit.SuggestHalfName(buttonEditIn.Text, "high"));
                if (File.Exists(lowPath) || File.Exists(highPath))
                {
                    if (MessageBox.Show(this,
                            "Mindestens eine Zieldatei existiert bereits:\r\n\r\n" +
                            lowPath + "\r\n" + highPath +
                            "\r\n\r\nUeberschreiben?",
                            "Datei existiert", MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question) != DialogResult.Yes)
                    {
                        return;
                    }
                }

                byte[] image = ChipMergeSplit.ReadFile(buttonEditIn.Text);
                byte[] low;
                byte[] high;
                bool lowIsEven = rdoLowEven.Checked;
                try
                {
                    ChipMergeSplit.Split(image, lowIsEven, out low, out high);
                }
                catch (ArgumentException ex)
                {
                    MessageBox.Show(this, ex.Message, "Split abgebrochen",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                File.WriteAllBytes(lowPath, low);
                File.WriteAllBytes(highPath, high);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Eingabe : " + buttonEditIn.Text +
                              "  (" + image.Length + " Byte)");
                sb.AppendLine("Reihenfolge: " + (lowIsEven
                    ? "gerade Bytes -> _low, ungerade Bytes -> _high"
                    : "ungerade Bytes -> _low, gerade Bytes -> _high"));
                sb.AppendLine();
                sb.AppendLine("LOW     : " + lowPath + "  (" + low.Length + " Byte)");
                sb.AppendLine("HIGH    : " + highPath + "  (" + high.Length + " Byte)");
                sb.AppendLine();
                sb.AppendLine("Hinweis: Ein erneuter Merge mit derselben " +
                              "Reihenfolge muss wieder byte-identisch zur " +
                              "Eingabe fuehren (Round-Trip-Test in tools/).");
                memoResult.Text = sb.ToString();

                MessageBox.Show(this,
                    "Split abgeschlossen.\r\n\r\n" +
                    "LOW : " + ChipMergeSplit.SizeText(low.Length) + "\r\n" +
                    "HIGH: " + ChipMergeSplit.SizeText(high.Length),
                    "Split", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Split fehlgeschlagen",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        #endregion
    }
}
