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
    ///
    /// The whole form is themed through two <see cref="KryptonPalette"/>
    /// instances (see <see cref="ThemePalettes"/>): a light Sparkle-based
    /// theme and a tuned dark Office2010Black theme, switched from the
    /// ribbon's "View > Theme" group and persisted under HKCU.
    /// </summary>
    public class MainForm : KryptonForm
    {
        private readonly KryptonManager _manager;
        private readonly KryptonPalette _lightPalette;
        private readonly KryptonPalette _darkPalette;
        private readonly KryptonRibbon _ribbon;
        private readonly KryptonLabel _infoLabel;
        private readonly KryptonTextBox _report;
        private readonly KryptonRibbonGroupButton _lightButton;
        private readonly KryptonRibbonGroupButton _darkButton;
        private readonly KryptonRibbonGroupButton _minimizeButton;
        private readonly string _initialFile;
        private KryptonDataGridView _grid;

        public MainForm(ThemeKind initialTheme, string initialFile)
        {
            _initialFile = initialFile;
            _manager = new KryptonManager();
            _lightPalette = ThemePalettes.CreateLight();
            _darkPalette = ThemePalettes.CreateDark();

            Text = "VAGEDCSuite-Reloaded";
            Width = 960;
            Height = 680;
            StartPosition = FormStartPosition.CenterScreen;

            // --- ribbon -------------------------------------------------------
            _ribbon = new KryptonRibbon { Dock = DockStyle.Top };
            // Office-style minimize: double-click a tab (or the ribbon
            // minimize button) collapses the ribbon to just the tab strip.
            _ribbon.AllowMinimizedChange = true;

            // File tab: Open
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
            KryptonRibbonGroupTriple fileTriple = new KryptonRibbonGroupTriple();
            fileTriple.Items.Add(openButton);
            fileGroup.Items.Add(fileTriple);
            fileTab.Groups.Add(fileGroup);

            // View tab: Theme (Light / Dark)
            KryptonRibbonTab viewTab = new KryptonRibbonTab { Text = "View" };
            KryptonRibbonGroup themeGroup = new KryptonRibbonGroup
            {
                TextLine1 = "Theme",
                TextLine2 = "Appearance"
            };
            _lightButton = new KryptonRibbonGroupButton
            {
                TextLine1 = "Light",
                TextLine2 = "theme",
                ButtonType = GroupButtonType.Check
            };
            _darkButton = new KryptonRibbonGroupButton
            {
                TextLine1 = "Dark",
                TextLine2 = "theme",
                ButtonType = GroupButtonType.Check
            };
            _lightButton.Click += (s, e) => SetTheme(ThemeKind.Light);
            _darkButton.Click += (s, e) => SetTheme(ThemeKind.Dark);
            KryptonRibbonGroupTriple themeTriple = new KryptonRibbonGroupTriple();
            themeTriple.Items.Add(_lightButton);
            themeTriple.Items.Add(_darkButton);
            themeGroup.Items.Add(themeTriple);
            viewTab.Groups.Add(themeGroup);

            // View tab: Ribbon minimize (collapse to just the tab strip).
            KryptonRibbonGroup ribbonGroup = new KryptonRibbonGroup
            {
                TextLine1 = "Ribbon",
                TextLine2 = "Layout"
            };
            _minimizeButton = new KryptonRibbonGroupButton
            {
                TextLine1 = "Minimize",
                TextLine2 = "ribbon",
                ButtonType = GroupButtonType.Check
            };
            _minimizeButton.Click += (s, e) => _ribbon.MinimizedMode = _minimizeButton.Checked;
            KryptonRibbonGroupTriple ribbonTriple = new KryptonRibbonGroupTriple();
            ribbonTriple.Items.Add(_minimizeButton);
            ribbonGroup.Items.Add(ribbonTriple);
            viewTab.Groups.Add(ribbonGroup);

            _ribbon.RibbonTabs.Add(fileTab);
            _ribbon.RibbonTabs.Add(viewTab);

            // Keep the button in sync when the ribbon is collapsed by
            // double-clicking a tab (AllowMinimizedChange) as well.
            _ribbon.MinimizedModeChanged += (s, e) => _minimizeButton.Checked = _ribbon.MinimizedMode;

            // --- info strip ---------------------------------------------------
            _infoLabel = new KryptonLabel
            {
                Dock = DockStyle.Top,
                Height = 52,
                AutoSize = false,
                Text = "Open a 256 KB EDC15V-5 binary."
            };
            _infoLabel.StateCommon.ShortText.Font = new Font("Segoe UI", 10F);
            _infoLabel.StateCommon.ShortText.TextV = PaletteRelativeAlign.Center;
            _infoLabel.StateCommon.Padding = new Padding(8, 0, 8, 0);

            // --- checksum report ---------------------------------------------
            _report = new KryptonTextBox
            {
                Dock = DockStyle.Bottom,
                Height = 180,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9F)
            };

            // The block grid is created in OnShown (see below); KryptonDataGridView
            // measures itself in its constructor and needs a window handle that
            // wine-mono cannot create before the message loop runs.

            Controls.Add(_report);
            Controls.Add(_infoLabel);
            Controls.Add(_ribbon);

            // Apply the palette before the form is first shown.  The global
            // manager assignment covers the grid/textbox/label; the form and
            // ribbon are pinned explicitly because in Krypton 5.550 they do
            // not reliably re-resolve a custom GlobalPalette set after they
            // have been created (see THEMING.md).
            ApplyTheme(initialTheme);
        }

        /// <summary>
        /// Builds the block grid once the form is visible.  This must happen
        /// after the message loop has started, because <see cref="KryptonDataGridView"/>
        /// calls <c>CreateGraphics()</c> in its constructor and wine-mono throws
        /// "Error creating window handle" for a handle created before the loop.
        /// </summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            _grid = new KryptonDataGridView
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

            // Add at index 0 so the Fill grid is docked last (after ribbon,
            // info strip and report have claimed their space).
            Controls.Add(_grid);
            Controls.SetChildIndex(_grid, 0);

            if (_initialFile != null) Inspect(_initialFile);
        }

        /// <summary>Applies a palette to the manager, the form and every control.</summary>
        private void ApplyPalette(KryptonPalette palette)
        {
            _manager.GlobalPaletteMode = PaletteModeManager.Custom;
            _manager.GlobalPalette = palette;

            // Form + ribbon are pinned explicitly: Krypton 5.550 does not
            // reliably re-resolve a custom GlobalPalette set after creation
            // (see THEMING.md).
            Palette = palette;
            _ribbon.Palette = palette;
            _infoLabel.Palette = palette;
            _report.Palette = palette;
            if (_grid != null) _grid.Palette = palette;

            // The form's client background is drawn from its own StateCommon
            // redirect (BackStyle = FormMain).  Pin it directly to the palette
            // value so a transparent label on the form shows a dark surface.
            Color formBack = palette.GetBackColor1(PaletteBackStyle.FormMain, PaletteState.Normal);
            StateCommon.Back.Draw = InheritBool.True;
            StateCommon.Back.Color1 = formBack;
            StateCommon.Back.Color2 = formBack;
            StateCommon.Back.ColorStyle = PaletteColorStyle.Solid;
            BackColor = formBack;
        }

        /// <summary>
        /// Switches the palette and reflects the choice in the ribbon.
        /// Does not persist; persistence is the caller's job (the click
        /// handlers persist, the startup path does not).
        /// </summary>
        public void ApplyTheme(ThemeKind kind)
        {
            ApplyPalette(kind == ThemeKind.Dark ? _darkPalette : _lightPalette);
            _lightButton.Checked = kind == ThemeKind.Light;
            _darkButton.Checked = kind == ThemeKind.Dark;
            // KryptonLabel rejects transparent backgrounds and defaults its
            // BackColor to SystemColors.Control, so pin its solid fill to the
            // themed form background (its *text* colour comes from the palette's
            // LabelStyles).  See THEMING.md.
            _infoLabel.BackColor = ThemePalettes.FormBackground(kind);
        }

        private void SetTheme(ThemeKind kind)
        {
            ApplyTheme(kind);
            ThemeStore.Save(kind);
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
            Match match = Regex.Match(ascii, @"03[0-9]{7}[A-Z]{1,2}");
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

            // Optional theme override for scripting/screenshots, e.g. --theme=dark.
            string file = null;
            ThemeKind? forced = null;
            foreach (string arg in args)
            {
                if (arg.StartsWith("--theme=", StringComparison.OrdinalIgnoreCase))
                {
                    string value = arg.Substring("--theme=".Length);
                    if (value.Equals("dark", StringComparison.OrdinalIgnoreCase)) forced = ThemeKind.Dark;
                    else if (value.Equals("light", StringComparison.OrdinalIgnoreCase)) forced = ThemeKind.Light;
                }
                else if (!arg.StartsWith("--", StringComparison.OrdinalIgnoreCase))
                {
                    file = arg;
                }
            }

            MainForm form = new MainForm(forced ?? ThemeStore.Load(), file);
            Application.Run(form);
        }
    }
}
