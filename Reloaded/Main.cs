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
        private readonly StatusStrip _statusStrip;
        private readonly ToolStripStatusLabel _statusLabel;
        private readonly string _initialFile;
        private KryptonDataGridView _grid;
        private TreeView _mapTree;
        private KryptonPalette _activePalette;

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
            _mapsButton.Click += (s, e) => ShowMapsView(_mapsButton.Checked);
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

            // --- status bar ---------------------------------------------------
            // Plain WinForms StatusStrip (there is no Krypton tree either);
            // its surface and text are pinned to the active palette in
            // ApplyPalette, the same way the hex panel is themed.
            _statusLabel = new ToolStripStatusLabel
            {
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = I18n.T("status_no_file")
            };
            _statusStrip = new StatusStrip
            {
                Dock = DockStyle.Bottom,
                SizingGrip = false,
                ShowItemToolTips = false,
                // The system renderer honours BackColor; the default
                // professional renderer paints a gradient over it.
                RenderMode = ToolStripRenderMode.System
            };
            _statusStrip.Items.Add(_statusLabel);

            // The block grid and the map tree are created in OnShown (see below);
            // KryptonDataGridView measures itself in its constructor and needs
            // a window handle that wine-mono cannot create before the message
            // loop runs.

            // Add order gives the desired dock priority (later index docks
            // first): ribbon, info strip, status strip, report, hex panel,
            // then the Fill grid.  The status strip is added after the report
            // so it owns the very bottom edge and the report sits above it.
            Controls.Add(_hexPanel);
            Controls.Add(_report);
            Controls.Add(_statusStrip);
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

            // Map (Kennfeld) tree: the alternative Fill surface to the block
            // grid.  Krypton has no tree control, so this is a plain WinForms
            // TreeView; its BackColor/ForeColor are pinned to the palette in
            // ApplyPalette.  The View > Views > Maps check button swaps it
            // with the block grid.
            _mapTree = new TreeView
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                HideSelection = false,
                ShowLines = true,
                ShowPlusMinus = true,
                ShowRootLines = true,
                ShowNodeToolTips = true,
                ItemHeight = 20,
                Visible = false
            };

            // Double-click a map leaf: open the OxyPlot heat-map viewer for it.
            _mapTree.NodeMouseDoubleClick += MapTree_NodeMouseDoubleClick;

            Controls.Add(_mapTree);
            Controls.SetChildIndex(_mapTree, 0);

            // The tree did not exist when the constructor applied the
            // palette, so pin its colours now.
            if (_activePalette != null) ApplyTreePalette(_activePalette);

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
            if (_statusLabel != null) _statusLabel.Text = I18n.T("status_no_file");

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
            _activePalette = palette;

            // The form's client background is drawn from its own StateCommon
            // redirect (BackStyle = FormMain).  Pin it directly to the palette
            // value so a transparent label on the form shows a dark surface.
            ApplyHexPalette(palette);
            ApplyTreePalette(palette);
            ApplyStatusPalette(palette);

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
        /// Matches the plain WinForms TreeView to the active palette.  The
        /// TreeView is used instead of a Krypton grid because Krypton has no
        /// tree control; BackColor/ForeColor are its theming hooks, exactly
        /// like the hex panel.  The expand/collapse glyphs are drawn by the
        /// OS and do not follow the palette (see LOOKMATCH.md).
        /// </summary>
        private void ApplyTreePalette(KryptonPalette palette)
        {
            if (_mapTree == null) return;
            try
            {
                Color surface = palette.GetBackColor1(PaletteBackStyle.InputControlStandalone, PaletteState.Normal);
                Color text = palette.GetContentShortTextColor1(PaletteContentStyle.InputControlStandalone, PaletteState.Normal);
                if (surface.A == 255) _mapTree.BackColor = surface;
                if (text.A == 255) _mapTree.ForeColor = text;
            }
            catch
            {
                // Keep SystemColors defaults (light surface).
            }
            _mapTree.Invalidate();
        }

        /// <summary>
        /// Matches the status strip and its single label to the active
        /// palette.  The status strip uses the form background and the
        /// palette's label text colour so it also works in dark mode.
        /// </summary>
        private void ApplyStatusPalette(KryptonPalette palette)
        {
            if (_statusStrip == null) return;
            try
            {
                Color surface = palette.GetBackColor1(PaletteBackStyle.FormMain, PaletteState.Normal);
                Color text = palette.GetContentShortTextColor1(PaletteContentStyle.LabelNormalPanel, PaletteState.Normal);
                if (surface.A == 255) _statusStrip.BackColor = surface;
                if (text.A == 255)
                {
                    _statusLabel.ForeColor = text;
                    _statusLabel.BackColor = surface;
                }
            }
            catch
            {
                // Keep SystemColors defaults (light surface).
            }
            _statusStrip.Invalidate();
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
        /// map (Kennfeld) tree.  They are mutually exclusive: only one is
        /// visible at a time, so each one gets the whole client area.
        /// </summary>
        private void ShowMapsView(bool visible)
        {
            if (_grid == null || _mapTree == null) return;
            _mapTree.Visible = visible;
            _grid.Visible = !visible;
        }

        /// <summary>
        /// Opens the Kennfeld viewer for the double-clicked map leaf.  The
        /// node's Tag carries the <see cref="MapList.Map"/> record whose
        /// Varname the viewer looks up in the parsed binary; a parser or
        /// reader failure is reported in the report box instead of breaking
        /// the UI.
        /// </summary>
        private void MapTree_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            try
            {
                if (e.Node == null || _mapTree == null || _lastPath == null) return;

                MapList.Map map = e.Node.Tag as MapList.Map;
                if (map == null || string.IsNullOrEmpty(map.Varname)) return;

                MapViewForm.ShowMap(_lastPath, map.Varname, this);
            }
            catch (Exception ex)
            {
                _report.AppendText("\r\nMap view: " + ex.Message + "\r\n");
            }
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
            if (_mapTree != null)
            {
                _mapTree.BeginUpdate();
                _mapTree.Nodes.Clear();
                _mapTree.EndUpdate();
            }
            _report.Clear();

            byte[] image;
            try
            {
                image = File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                _infoLabel.Text = I18n.T("info_read_error", ex.Message);
                if (_statusLabel != null)
                    _statusLabel.Text = I18n.T("status_read_error", Path.GetFileName(path));
                return;
            }

            // Hex viewer: show the bytes of whatever file was loaded.
            _hex.SetBytes(image);
            _hex.SetCurrentAddress(0);

            // ECU identity, from the parser's extraction helpers.  A failure
            // anywhere in this block keeps the current window title (the
            // fallback required for unknown images).
            string part = string.Empty;
            string software = string.Empty;
            string bosch = string.Empty;
            string info = string.Empty;
            bool identityRead = false;
            try
            {
                EDC15VFileParser parser = new EDC15VFileParser();
                part = parser.ExtractPartnumber(image) ?? string.Empty;
                software = parser.ExtractSoftwareNumber(image) ?? string.Empty;
                bosch = parser.ExtractBoschPartnumber(image) ?? string.Empty;
                info = parser.ExtractInfo(image) ?? string.Empty;
                identityRead = true;
            }
            catch
            {
                // Keep the title we already have; the regex fallback below
                // still supplies a part number for the info strip.
            }

            if (string.IsNullOrEmpty(part)) part = FindVagPartNumber(image);

            // The original VAGEDCSuite title bar carries the full ECU
            // identity, not just the program name.
            string identity = JoinParts(part, software, bosch, info);
            if (identityRead)
            {
                string title = JoinParts(Path.GetFileName(path), part, software, bosch, info);
                if (title.Length > 0) Text = title;
            }

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
                UpdateStatusBar(path, I18n.T("status_checksum_na"), identity, 0);
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

            // Map list (Kennfelder), shown as a Category > Subcategory tree.
            int mapCount = FillMapTree(path);

            string checksum = okFields >= EDC15VM256Checksum.BlockCount
                ? I18n.T("status_checksum_ok")
                : I18n.T("status_checksum_partial", okFields, EDC15VM256Checksum.BlockCount);
            UpdateStatusBar(path, checksum, identity, mapCount);
        }

        /// <summary>
        /// Sets the single status-bar label in the original layout:
        /// <c>file | checksum state | ECU identity | N symbols</c>.  All
        /// fragments go through <see cref="I18n"/>.
        /// </summary>
        private void UpdateStatusBar(string path, string checksum, string identity, int symbolCount)
        {
            if (_statusLabel == null) return;
            if (string.IsNullOrEmpty(identity)) identity = "?";
            _statusLabel.Text = I18n.T(
                "status_bar",
                Path.GetFileName(path),
                checksum,
                identity,
                symbolCount);
        }

        /// <summary>
        /// Fills the map tree from <see cref="MapList.GetMaps"/>: category
        /// nodes ("Detected maps (N)"), subcategory nodes ("Fuel (N)"), and
        /// one leaf per map with its flash address.  Categories and
        /// subcategories are sorted alphabetically, maps by address.  Each
        /// leaf's Tag is its <see cref="MapList.Map"/> record, which keeps the
        /// Varname the viewer needs.  A parser failure leaves the tree empty
        /// and appends a short note to the report instead of aborting the
        /// inspection.
        /// </summary>
        private int FillMapTree(string path)
        {
            if (_mapTree == null) return 0;

            int count = 0;
            _mapTree.BeginUpdate();
            try
            {
                _mapTree.Nodes.Clear();

                List<MapList.Map> maps = MapList.GetMaps(path);
                Dictionary<string, Dictionary<string, List<MapList.Map>>> groups =
                    new Dictionary<string, Dictionary<string, List<MapList.Map>>>();

                for (int i = 0; i < maps.Count; i++)
                {
                    MapList.Map map = maps[i];
                    if (map == null) continue;
                    // Only real tables: a symbol with both axes of length 1
                    // is a scalar label, not a map (the label file adds 11k
                    // of them).
                    if (map.XLen <= 1 && map.YLen <= 1) continue;

                    string category = GroupLabel(map.Category, I18n.T("tree_unknown_category"));
                    string subcategory = GroupLabel(map.Subcategory, I18n.T("tree_unknown_subcategory"));

                    Dictionary<string, List<MapList.Map>> subs;
                    if (!groups.TryGetValue(category, out subs))
                    {
                        subs = new Dictionary<string, List<MapList.Map>>();
                        groups[category] = subs;
                    }

                    List<MapList.Map> list;
                    if (!subs.TryGetValue(subcategory, out list))
                    {
                        list = new List<MapList.Map>();
                        subs[subcategory] = list;
                    }

                    list.Add(map);
                    count++;
                }

                List<string> categories = new List<string>(groups.Keys);
                categories.Sort(StringComparer.OrdinalIgnoreCase);

                for (int c = 0; c < categories.Count; c++)
                {
                    Dictionary<string, List<MapList.Map>> subs = groups[categories[c]];
                    int categoryCount = 0;
                    foreach (List<MapList.Map> entries in subs.Values) categoryCount += entries.Count;

                    TreeNode categoryNode = new TreeNode(
                        I18n.T("tree_category", categories[c], categoryCount));
                    _mapTree.Nodes.Add(categoryNode);

                    List<string> subcategories = new List<string>(subs.Keys);
                    subcategories.Sort(StringComparer.OrdinalIgnoreCase);

                    for (int s = 0; s < subcategories.Count; s++)
                    {
                        List<MapList.Map> list = subs[subcategories[s]];
                        list.Sort(delegate(MapList.Map a, MapList.Map b)
                        {
                            return a.Address.CompareTo(b.Address);
                        });

                        TreeNode subNode = new TreeNode(
                            I18n.T("tree_subcategory", subcategories[s], list.Count));
                        categoryNode.Nodes.Add(subNode);

                        for (int m = 0; m < list.Count; m++)
                        {
                            MapList.Map map = list[m];
                            // Prefer the map's own name: the label file's
                            // descriptions are long paragraphs, not the short
                            // captions the original tree showed.  The full
                            // description stays reachable as a tooltip.
                            string label = map.Varname;
                            if (string.IsNullOrEmpty(label)) label = map.Description;
                            if (string.IsNullOrEmpty(label)) label = "(?)";

                            TreeNode leaf = new TreeNode(
                                I18n.T("tree_map", label, map.Address, map.XLen, map.YLen));
                            leaf.Tag = map;

                            StringBuilder tip = new StringBuilder();
                            if (!string.IsNullOrEmpty(map.Description))
                                tip.Append(map.Description);
                            if (map.CodeBlock > 0)
                            {
                                if (tip.Length > 0) tip.Append("\r\n");
                                tip.Append("codeblock ").Append(map.CodeBlock);
                            }
                            leaf.ToolTipText = tip.ToString();

                            subNode.Nodes.Add(leaf);
                        }
                    }
                }

                // Expand the first category and its first subcategory so the
                // grouping is visible at a glance; everything else, including
                // the large auto-detected groups, starts collapsed.
                if (_mapTree.Nodes.Count > 0)
                {
                    TreeNode first = _mapTree.Nodes[0];
                    first.Expand();
                    if (first.Nodes.Count > 0) first.Nodes[0].Expand();
                    _mapTree.SelectedNode = first;
                }
            }
            catch (Exception ex)
            {
                _report.AppendText("\r\nMaps: " + ex.Message + "\r\n");
            }
            finally
            {
                _mapTree.EndUpdate();
            }
            return count;
        }

        /// <summary>Trims a parser category/subcategory, substituting a label for blanks.</summary>
        private static string GroupLabel(string value, string fallback)
        {
            if (value == null) return fallback;
            string trimmed = value.Trim();
            return trimmed.Length == 0 ? fallback : trimmed;
        }

        /// <summary>Joins the non-empty parts with single spaces.</summary>
        private static string JoinParts(params string[] parts)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (string.IsNullOrEmpty(part)) continue;
                part = part.Trim();
                if (part.Length == 0) continue;
                if (builder.Length > 0) builder.Append(' ');
                builder.Append(part);
            }
            return builder.ToString();
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
