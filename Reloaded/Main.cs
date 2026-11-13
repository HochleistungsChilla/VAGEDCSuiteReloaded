using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Krypton.Ribbon;
using Krypton.Toolkit;
using VAGSuite;

namespace Reloaded
{
    /// <summary>
    /// Skeleton of the DevExpress-free VAGEDCSuite Reloaded UI: a Krypton
    /// ribbon and, on "Open", a 256 KB EDC15V-5 image is inspected with the
    /// clean core (Edc15V256Layout + EDC15VM256Checksum).  No DevExpress.
    /// </summary>
    public class MainForm : KryptonForm
    {
        private readonly Label _infoLabel;
        private readonly DataGridView _grid;
        private readonly TextBox _report;

        public MainForm()
        {
            Text = "VAGEDCSuite-Reloaded";
            Width = 900;
            Height = 640;
            StartPosition = FormStartPosition.CenterScreen;

            // --- ribbon -------------------------------------------------------
            KryptonRibbon ribbon = new KryptonRibbon { Dock = DockStyle.Top };
            KryptonRibbonTab fileTab = new KryptonRibbonTab { Text = "File" };
            KryptonRibbonGroup fileGroup = new KryptonRibbonGroup
            {
                TextLine1 = "File",
                TextLine2 = "Image"
            };
            KryptonRibbonGroupButton openButton = new KryptonRibbonGroupButton
            {
                TextLine1 = "Open",
                TextLine2 = "binary"
            };
            openButton.Click += (s, e) => OpenFile();
            KryptonRibbonGroupTriple triple = new KryptonRibbonGroupTriple();
            triple.Items.Add(openButton);
            fileGroup.Items.Add(triple);
            fileTab.Groups.Add(fileGroup);
            ribbon.RibbonTabs.Add(fileTab);

            // --- info strip ---------------------------------------------------
            _infoLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 52,
                Padding = new Padding(8),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 10F),
                Text = "Open a 256 KB EDC15V-5 binary."
            };

            // --- block grid ---------------------------------------------------
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            _grid.Columns.Add("block", "Block");
            _grid.Columns.Add("range", "Address range");
            _grid.Columns.Add("gearbox", "Gearbox");
            _grid.Columns.Add("code", "Code");

            // --- checksum report ---------------------------------------------
            _report = new TextBox
            {
                Dock = DockStyle.Bottom,
                Height = 180,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9F),
                BackColor = Color.White
            };

            Controls.Add(_grid);
            Controls.Add(_infoLabel);
            Controls.Add(_report);
            Controls.Add(ribbon);
        }

        private void OpenFile()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "Binary images (*.bin)|*.bin|All files (*.*)|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                Inspect(dlg.FileName);
            }
        }

        /// <summary>Loads and inspects a file (also used for the CLI arg).</summary>
        public void LoadInitial(string path) { Inspect(path); }

        private void Inspect(string path)
        {
            _grid.Rows.Clear();
            _report.Clear();

            byte[] image;
            try
            {
                image = File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                _infoLabel.Text = "Could not read file: " + ex.Message;
                return;
            }

            string part = FindVagPartNumber(image);
            _infoLabel.Text = string.Format(
                "{0}   |   {1} bytes (0x{1:X})   |   part number: {2}",
                Path.GetFileName(path), image.Length, string.IsNullOrEmpty(part) ? "?" : part);

            bool known256 = image.Length == Edc15V256Layout.ImageSize &&
                            Edc15V256Layout.HasKnownMarkers(image);

            if (!known256)
            {
                _infoLabel.Text += "   |   not a 256 KB EDC15V-5 image (no known markers)";
                _report.Text = "Not a 256 KB EDC15V-5 image.";
                return;
            }

            // One row per data block, with the gearbox family and the code
            // block code read from the file.
            for (int block = 1; block <= 3; block++)
            {
                string role = Edc15V256Layout.DescribeGearbox(image, block);
                string shown = role;
                if (role == "Manual" && block == 2) shown = "Manual, FWD";
                if (role == "Manual" && block == 3) shown = "4x4";
                _grid.Rows.Add(
                    Edc15V256Layout.BlockNames[block - 1],
                    Edc15V256Layout.BlockRanges[block - 1],
                    shown,
                    Edc15V256Layout.DescribeGearboxCode(image, block));
            }

            // Checksum: computed vs stored for all three blocks.
            int okFields = EDC15VM256Checksum.CountOkFields(image, out string err);
            _report.AppendText(string.Format(
                "Checksum: {0}/{1} fields OK{2}\r\n",
                okFields, EDC15VM256Checksum.BlockCount, string.IsNullOrEmpty(err) ? "" : "  (" + err + ")"));
            _report.AppendText("\r\n");
            _report.AppendText(EDC15VM256Checksum.Verify(image).Report);
        }

        /// <summary>
        /// First VAG part number found in the file (for example 038906018P),
        /// scanned from the printable bytes of the code block headers.
        /// </summary>
        private static string FindVagPartNumber(byte[] image)
        {
            string ascii = Encoding.GetEncoding(28591).GetString(image);
            Match match = Regex.Match(ascii, @"03[0-9]{7}[A-Z]");
            return match.Success ? match.Value : string.Empty;
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            MainForm form = new MainForm();
            if (args.Length > 0) form.LoadInitial(args[0]);
            Application.Run(form);
        }
    }
}
