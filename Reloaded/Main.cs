using System;
using System.Collections.Generic;
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
    ///
    /// All static UI text goes through <see cref="ApplyTexts"/>, which reads
    /// the <see cref="I18n"/> table.  The "View > Language" group switches
    /// between English and German, re-applies <see cref="ApplyTexts"/> and
    /// re-runs <see cref="Inspect"/> for the loaded file so dynamic text is
    /// translated as well.
    /// </summary>
    public class MainForm : KryptonForm
    {
        private readonly KryptonManager _manager;
        private readonly KryptonPalette _lightPalette;
        private readonly KryptonPalette _darkPalette;
        private readonly KryptonRibbon _ribbon;
        private readonly KryptonLabel _infoLabel;
        private readonly KryptonTextBox _report;
        private readonly KryptonRibbonTab _fileTab;
        private readonly KryptonRibbonTab _viewTab;
        private readonly KryptonRibbonGroup _fileGroup;
        private readonly KryptonRibbonGroup _themeGroup;
        private readonly KryptonRibbonGroup _viewsGroup;
        private readonly KryptonRibbonGroup _ribbonGroup;
        private readonly KryptonRibbonGroup _languageGroup;
        private readonly KryptonRibbonGroupButton _openButton;
        private readonly KryptonRibbonGroupButton _lightButton;
        private readonly KryptonRibbonGroupButton _darkButton;
        private readonly KryptonRibbonGroupButton _minimizeButton;
        private readonly KryptonRibbonGroupButton _hexButton;
        private readonly KryptonRibbonGroupButton _mapsButton;
        private readonly KryptonRibbonGroupButton _englishButton;
        private readonly KryptonRibbonGroupButton _germanButton;
        private readonly Panel _hexPanel;
        private readonly HexViewerControl _hex;
        private readonly string _initialFile;
        private KryptonDataGridView _grid;
        private KryptonDataGridView _mapGrid;

        // Last successfully requested path; re-inspected after a language
        // switch so the dynamic texts (info strip, grid rows, report) are
        // re-rendered in the new language.
        private string _lastPath;

        public MainForm(ThemeKind initialTheme, string initialFile)
        {
            _initialFile = initialFile;
            _manager = new KryptonManager();
            _lightPalette = ThemePalettes.CreateLight();
            _darkPalette = ThemePalettes.CreateDark();

            Width = 960;
            Height = 680;
            StartPosition = FormStartPosition.CenterScreen;

            // --- ribbon -------------------------------------------------------
            _ribbon = new KryptonRibbon { Dock = DockStyle.Top };
            // Office-style minimize: double-click a tab (or the ribbon
            // minimize button) collapses the ribbon to just the tab strip.
            _ribbon.AllowMinimizedChange = true;

            // File tab: Open
            _fileTab = new KryptonRibbonTab();
            _fileGroup = new KryptonRibbonGroup();
            _openButton = new KryptonRibbonGroupButton();
            _openButton.Click += (s, e) => OpenFile();
            KryptonRibbonGroupTriple fileTriple = new KryptonRibbonGroupTriple();
            fileTriple.Items.Add(_openButton);
            _fileGroup.Items.Add(fileTriple);
            _fileTab.Groups.Add(_fileGroup);

            // View tab: Theme (Light / Dark)
            _viewTab = new KryptonRibbonTab();
            _themeGroup = new KryptonRibbonGroup();
            _lightButton = new KryptonRibbonGroupButton
            {
                ButtonType = GroupButtonType.Check
            };
            _darkButton = new KryptonRibbonGroupButton
            {
                ButtonType = GroupButtonType.Check
            };
            _lightButton.Click += (s, e) => SetTheme(ThemeKind.Light);
            _darkButton.Click += (s, e) => SetTheme(ThemeKind.Dark);
            KryptonRibbonGroupTriple themeTriple = new KryptonRibbonGroupTriple();
            themeTriple.Items.Add(_lightButton);
            themeTriple.Items.Add(_darkButton);
            _themeGroup.Items.Add(themeTriple);
            _viewTab.Groups.Add(_themeGroup);

            // View tab: Views group (hex viewer + map list panel toggles).
            _viewsGroup = new KryptonRibbonGroup();
            _hexButton = new KryptonRibbonGroupButton
            {
                ButtonType = GroupButtonType.Check
            };
            _hexButton.Click += (s, e) => ShowHexPanel(_hexButton.Checked);
            _mapsButton = new KryptonRibbonGroupButton
            {
                ButtonType = GroupButtonType.Check
            };
            _mapsButton.Click += (s, e) => ShowMapsGrid(_mapsButton.Checked);
            KryptonRibbonGroupTriple viewsTriple = new KryptonRibbonGroupTriple();
            viewsTriple.Items.Add(_hexButton);
            viewsTriple.Items.Add(_mapsButton);
            _viewsGroup.Items.Add(viewsTriple);
            _viewTab.Groups.Add(_viewsGroup);

            // View tab: Ribbon minimize (collapse to just the tab strip).
            _ribbonGroup = new KryptonRibbonGroup();
            _minimizeButton = new KryptonRibbonGroupButton
            {
                ButtonType = GroupButtonType.Check
            };
            _minimizeButton.Click += (s, e) => _ribbon.MinimizedMode = _minimizeButton.Checked;
            KryptonRibbonGroupTriple ribbonTriple = new KryptonRibbonGroupTriple();
            ribbonTriple.Items.Add(_minimizeButton);
            _ribbonGroup.Items.Add(ribbonTriple);
            _viewTab.Groups.Add(_ribbonGroup);

            // View tab: Language (English / Deutsch).  The button labels stay
            // literal: language names are never translated.
            _languageGroup = new KryptonRibbonGroup();
            _englishButton = new KryptonRibbonGroupButton
            {
                TextLine1 = "English",
                ButtonType = GroupButtonType.Check
            };
            _germanButton = new KryptonRibbonGroupButton
            {
                TextLine1 = "Deutsch",
                ButtonType = GroupButtonType.Check
            };
            _englishButton.Click += (s, e) => ChangeLanguage("en");
            _germanButton.Click += (s, e) => ChangeLanguage("de");
            KryptonRibbonGroupTriple languageTriple = new KryptonRibbonGroupTriple();
            languageTriple.Items.Add(_englishButton);
            languageTriple.Items.Add(_germanButton);
            _languageGroup.Items.Add(languageTriple);
            _viewTab.Groups.Add(_languageGroup);

            _ribbon.RibbonTabs.Add(_fileTab);
            _ribbon.RibbonTabs.Add(_viewTab);

            // Keep the button in sync when the ribbon is collapsed by
            // double-clicking a tab (AllowMinimizedChange) as well.
            _ribbon.MinimizedModeChanged += (s, e) => _minimizeButton.Checked = _ribbon.MinimizedMode;

            // --- info strip ---------------------------------------------------
            _infoLabel = new KryptonLabel
            {
                Dock = DockStyle.Top,
                Height = 52,
                AutoSize = false
            };
            _infoLabel.StateCommon.ShortText.Font = new Font("Segoe UI", 10F);
            _infoLabel.StateCommon.ShortText.TextV = PaletteRelativeAlign.Center;
            _infoLabel.StateCommon.Padding = new Padding(8, 0, 8, 0);

            // --- checksum report ---------------------------------------------
            _report = new KryptonTextBox
            {
                // Multiline must be set before Dock/Height: Krypton 5.550
                // resets the height to the single-line value when Multiline
                // flips afterwards, which collapsed this box to 0 px.
                Multiline = true,
                Dock = DockStyle.Bottom,
                Height = 180,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9F)
            };

            // --- hex viewer panel --------------------------------------------
            // Docked bottom, between the Fill grid and the checksum report:
            // the report is added after this panel, so it is docked first and
            // owns the very bottom edge; this panel then claims the strip
            // above it and the grid takes what is left.  Hidden by default;
            // the View > Views > Hex check button toggles it.
            _hex = new HexViewerControl { Dock = DockStyle.Fill };
            _hexPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 160,
                Visible = false
            };
            _hexPanel.Controls.Add(_hex);

            // The block and map grids are created in OnShown (see below);
            // KryptonDataGridView measures itself in its constructor and needs
            // a window handle that wine-mono cannot create before the message
            // loop runs.

            // Add order gives the desired dock priority (later index docks
            // first): ribbon, info strip, report, hex panel, then the Fill grid.
            Controls.Add(_hexPanel);
            Controls.Add(_report);
            Controls.Add(_infoLabel);
            Controls.Add(_ribbon);

            // Apply the palette before the form is first shown.  The global
            // manager assignment covers the grid/textbox/label; the form and
            // ribbon are pinned explicitly because in Krypton 5.550 they do
            // not reliably re-resolve a custom GlobalPalette set after they
            // have been created (see THEMING.md).
            ApplyTheme(initialTheme);

            // All static texts are assigned from the I18n table.  The grids do
            // not exist yet; their headers are set in OnShown /
            // ApplyTexts after creation.
            ApplyTexts();
        }

        /// <summary>
        /// Builds the block and map grids once the form is visible.  This must
        /// happen after the message loop has started, because
        /// <see cref="KryptonDataGridView"/> calls <c>CreateGraphics()</c> in
        /// its constructor and wine-mono throws "Error creating window handle"
        /// for a handle created before the loop.
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
            _grid.Columns.Add("block", "");
            _grid.Columns.Add("range", "");
            _grid.Columns.Add("gearbox", "");
            _grid.Columns.Add("code", "");

            // Add at index 0 so the Fill grid is docked last (after ribbon,
            // info strip and report have claimed their space).
            Controls.Add(_grid);
            Controls.SetChildIndex(_grid, 0);

            // Map (Kennfeld) grid: same construction rules, hidden by default.
            // It is the alternative Fill surface to the block grid; the
            // View > Views > Maps check button swaps between them.
            _mapGrid = new KryptonDataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                Visible = false
            };
            _mapGrid.Columns.Add("varname", "Varname");
            _mapGrid.Columns.Add("description", "Description");
            _mapGrid.Columns.Add("address", "Address");
            _mapGrid.Columns.Add("length", "Length");
            _mapGrid.Columns.Add("xlen", "XLen");
            _mapGrid.Columns.Add("ylen", "YLen");
            _mapGrid.Columns.Add("units", "Units");
            _mapGrid.Columns.Add("category", "Category");

            Controls.Add(_mapGrid);
            Controls.SetChildIndex(_mapGrid, 0);

            // Grid headers are localized; the grids did not exist when the
            // constructor ran ApplyTexts.
            ApplyTexts();

            if (_initialFile != null) Inspect(_initialFile);
        }

        /// <summary>
        /// Assigns every static, translatable text from the current
        /// <see cref="I18n"/> table and syncs the language check buttons.
        /// Called at the end of the constructor, after grid creation in
        /// <see cref="OnShown"/>, and on every language change.  Dynamic texts
        /// (info strip for a loaded file, grid rows, report) are re-applied by
        /// <see cref="Inspect"/>.
        /// </summary>
        private void ApplyTexts()
        {
            Text = I18n.T("window_title");

            // The ribbon's own application button (the leading tile before
            // the tabs) is not one of our tabs, but it is user-visible text
            // too, so it follows the File tab label.
            _ribbon.RibbonAppButton.AppButtonText = I18n.T("tab_file");

            _fileTab.Text = I18n.T("tab_file");
            _viewTab.Text = I18n.T("tab_view");

            _fileGroup.TextLine1 = I18n.T("group_file");
            _fileGroup.TextLine2 = I18n.T("group_image");
            _openButton.TextLine1 = I18n.T("file_open");
            _openButton.TextLine2 = I18n.T("file_open_binary");

            _themeGroup.TextLine1 = I18n.T("view_theme");
            _themeGroup.TextLine2 = I18n.T("view_appearance");
            _lightButton.TextLine1 = I18n.T("theme_light");
            _lightButton.TextLine2 = I18n.T("theme_sub");
            _darkButton.TextLine1 = I18n.T("theme_dark");
            _darkButton.TextLine2 = I18n.T("theme_sub");

            _viewsGroup.TextLine1 = I18n.T("group_views");
            _viewsGroup.TextLine2 = I18n.T("group_panels");
            _hexButton.TextLine1 = I18n.T("view_hex");
            _hexButton.TextLine2 = I18n.T("view_hex_viewer");
            _mapsButton.TextLine1 = I18n.T("maps");
            _mapsButton.TextLine2 = I18n.T("maps_sub");

            _ribbonGroup.TextLine1 = I18n.T("group_ribbon");
            _ribbonGroup.TextLine2 = I18n.T("group_layout");
            _minimizeButton.TextLine1 = I18n.T("ribbon_minimize");
            _minimizeButton.TextLine2 = I18n.T("ribbon_sub");

            _languageGroup.TextLine1 = I18n.T("language");
            _languageGroup.TextLine2 = I18n.T("language_sub");

            _infoLabel.Text = I18n.T("info_open_binary");

            // Keep the check state in sync with the active language.
            bool english = string.Equals(I18n.CurrentLanguage, "en", StringComparison.OrdinalIgnoreCase);
            bool german = string.Equals(I18n.CurrentLanguage, "de", StringComparison.OrdinalIgnoreCase);
            _englishButton.Checked = english;
            _germanButton.Checked = german;

            if (_grid != null)
            {
                _grid.Columns["block"].HeaderText = I18n.T("block");
                _grid.Columns["range"].HeaderText = I18n.T("address_range");
                _grid.Columns["gearbox"].HeaderText = I18n.T("gearbox");
                _grid.Columns["code"].HeaderText = I18n.T("code");
            }
        }

        /// <summary>
        /// Switches the UI language, re-applies all static texts and (when a
        /// file is loaded) re-runs <see cref="Inspect"/> so the dynamic texts
        /// are translated too.
        /// </summary>
        private void ChangeLanguage(string code)
        {
            if (!I18n.SetLanguage(code))
            {
                // Language could not be loaded; leave the UI as it is but
                // restore the button state that the click toggled.
                ApplyTexts();
                return;
            }

            ApplyTexts();
            if (_lastPath != null) Inspect(_lastPath);
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
            if (_mapGrid != null) _mapGrid.Palette = palette;

            // The form's client background is drawn from its own StateCommon
            // redirect (BackStyle = FormMain).  Pin it directly to the palette
            // value so a transparent label on the form shows a dark surface.
            ApplyHexPalette(palette);

            Color formBack = palette.GetBackColor1(PaletteBackStyle.FormMain, PaletteState.Normal);
            StateCommon.Back.Draw = InheritBool.True;
            StateCommon.Back.Color1 = formBack;
            StateCommon.Back.Color2 = formBack;
            StateCommon.Back.ColorStyle = PaletteColorStyle.Solid;
            BackColor = formBack;
        }

        /// <summary>
        /// Matches the plain GDI+ hex surface to the active palette.  The
        /// control's own <c>BackColor</c>/<c>ForeColor</c>/<c>Font</c> and its
        /// public colour fields are the theming hooks; nothing here is
        /// dark-specific, and any lookup failure keeps the control defaults.
        /// </summary>
        private void ApplyHexPalette(KryptonPalette palette)
        {
            try
            {
                Color surface = palette.GetBackColor1(PaletteBackStyle.InputControlStandalone, PaletteState.Normal);
                Color text = palette.GetContentShortTextColor1(PaletteContentStyle.InputControlStandalone, PaletteState.Normal);
                if (surface.A == 255) _hex.BackColor = surface;
                if (text.A == 255) _hex.ForeColor = text;
            }
            catch
            {
                // Keep SystemColors defaults (light surface).
            }
            _hex.Invalidate();
        }

        /// <summary>
        /// Shows or hides the docked hex viewer.  Hiding a docked control
        /// removes it from layout, so the Fill grid immediately reclaims the
        /// space.
        /// </summary>
        private void ShowHexPanel(bool visible)
        {
            _hexPanel.Visible = visible;
        }

        /// <summary>
        /// Switches the Fill surface between the block grid (default) and the
        /// map (Kennfeld) list.  The two grids are mutually exclusive: only
        /// one is visible at a time, so each one gets the whole client area.
        /// </summary>
        private void ShowMapsGrid(bool visible)
        {
            if (_grid == null || _mapGrid == null) return;
            _mapGrid.Visible = visible;
            _grid.Visible = !visible;
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
                dlg.Filter = I18n.T("dialog_filter");
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                Inspect(dlg.FileName);
            }
        }

        private void Inspect(string path)
        {
            // Remember the path so a language switch can re-render the
            // dynamic texts of the loaded file.
            _lastPath = path;

            _grid.Rows.Clear();
            if (_mapGrid != null) _mapGrid.Rows.Clear();
            _report.Clear();

            byte[] image;
            try
            {
                image = File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                _infoLabel.Text = I18n.T("info_read_error", ex.Message);
                return;
            }

            // Hex viewer: show the bytes of whatever file was loaded.
            _hex.SetBytes(image);
            _hex.SetCurrentAddress(0);

            string part = FindVagPartNumber(image);
            _infoLabel.Text = I18n.T(
                "info_file_format",
                Path.GetFileName(path),
                image.Length,
                string.IsNullOrEmpty(part) ? "?" : part);

            bool known256 = image.Length == Edc15V256Layout.ImageSize &&
                            Edc15V256Layout.HasKnownMarkers(image);

            if (!known256)
            {
                _infoLabel.Text += "   |   " + I18n.T("info_not_256kb");
                _report.Text = I18n.T("report_not_256kb");
                return;
            }

            // One row per data block, with the gearbox family and the code
            // block code read from the file.
            for (int block = 1; block <= 3; block++)
            {
                string role = Edc15V256Layout.DescribeGearbox(image, block);
                string shown;
                if (role == "Manual" && block == 2) shown = I18n.T("gearbox_manual_fwd");
                else if (role == "Manual" && block == 3) shown = I18n.T("gearbox_4x4");
                else if (role == "Manual") shown = I18n.T("gearbox_manual");
                else if (role == "Automatic") shown = I18n.T("gearbox_automatic");
                else shown = role;

                _grid.Rows.Add(
                    I18n.T("block_" + block.ToString()),
                    Edc15V256Layout.BlockRanges[block - 1],
                    shown,
                    Edc15V256Layout.DescribeGearboxCode(image, block));
            }

            // Checksum: computed vs stored for all three blocks.
            int okFields = EDC15VM256Checksum.CountOkFields(image, out string err);
            _report.AppendText(I18n.T("checksum_report", okFields, EDC15VM256Checksum.BlockCount));
            if (!string.IsNullOrEmpty(err)) _report.AppendText("  (" + err + ")");
            _report.AppendText("\r\n");
            _report.AppendText("\r\n");
            _report.AppendText(EDC15VM256Checksum.Verify(image).Report);

            // Map list (Kennfelder).  Only the real tables are shown: axes
            // with a length of 1 are scalar labels, not maps.
            FillMapGrid(path);
        }

        /// <summary>
        /// Fills the map grid from <see cref="MapList.GetMaps"/> with the real
        /// Kennfelder only (an axis longer than one cell), sorted by flash
        /// address.  A parser failure leaves the grid empty and appends a
        /// short note to the report instead of aborting the inspection.
        /// </summary>
        private void FillMapGrid(string path)
        {
            if (_mapGrid == null) return;

            try
            {
                List<MapList.Map> all = MapList.GetMaps(path);
                List<MapList.Map> real = new List<MapList.Map>();
                for (int i = 0; i < all.Count; i++)
                {
                    MapList.Map map = all[i];
                    if (map == null) continue;
                    if (map.XLen > 1 || map.YLen > 1) real.Add(map);
                }

                real.Sort(delegate(MapList.Map a, MapList.Map b)
                {
                    return a.Address.CompareTo(b.Address);
                });

                for (int i = 0; i < real.Count; i++)
                {
                    MapList.Map map = real[i];
                    _mapGrid.Rows.Add(
                        map.Varname,
                        map.Description,
                        "0x" + map.Address.ToString("X"),
                        map.Length,
                        map.XLen,
                        map.YLen,
                        map.XUnits,
                        map.Category);
                }
            }
            catch (Exception ex)
            {
                _report.AppendText("\r\nMaps: " + ex.Message + "\r\n");
            }
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

            // Load the persisted language (or English) before any UI text is
            // built so the first ApplyTexts already reads the right table.
            I18n.Initialize();

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
