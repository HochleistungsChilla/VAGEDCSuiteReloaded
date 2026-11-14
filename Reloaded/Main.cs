using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Krypton.Ribbon;
using Krypton.Toolkit;
using OxyPlot.WindowsForms;
using VAGSuite;

namespace Reloaded
{
    /// <summary>
    /// DevExpress-free VAGEDCSuite Reloaded main window.
    ///
    /// Layout (matched to the original frmMain):
    ///   ribbon (collapsed to the tab strip by default)
    ///   one-line info strip          file | size | part number
    ///   map tree (300 px, left)     Category > Subcategory > map with address
    ///   map viewer (fill, right)    placeholder until a map is opened, then
    ///                               the OxyPlot heat map inline
    ///   status bar                  file | gearbox | checksum | identity | N symbols
    ///
    /// The block table and the checksum report are no longer the centrepiece;
    /// they live behind View > Views > Blocks.
    ///
    /// The whole form is themed through two <see cref="KryptonPalette"/>
    /// instances (see <see cref="ThemePalettes"/>).  The plain WinForms
    /// surfaces (split container, headers, tree, status strip, placeholder)
    /// derive their colours from the active palette in
    /// <see cref="ApplyChromePalette"/> so dark and light stay consistent.
    ///
    /// All static UI text goes through <see cref="ApplyTexts"/>, which reads
    /// the <see cref="I18n"/> table.
    /// </summary>
    public class MainForm : KryptonForm
    {
        /// <summary>Which centre surface is currently shown.</summary>
        private enum MainView
        {
            Maps,
            Blocks
        }

        private readonly KryptonManager _manager;
        private readonly KryptonPalette _lightPalette;
        private readonly KryptonPalette _darkPalette;
        private readonly KryptonRibbon _ribbon;

        // --- ribbon tabs and groups -----------------------------------------
        private readonly KryptonRibbonTab _fileTab;
        private readonly KryptonRibbonTab _viewTab;
        private readonly KryptonRibbonTab _helpTab;
        private readonly KryptonRibbonGroup _fileGroup;
        private readonly KryptonRibbonGroup _imageGroup;
        private readonly KryptonRibbonGroup _themeGroup;
        private readonly KryptonRibbonGroup _viewsGroup;
        private readonly KryptonRibbonGroup _mapGroup;
        private readonly KryptonRibbonGroup _ribbonGroup;
        private readonly KryptonRibbonGroup _languageGroup;
        private readonly KryptonRibbonGroup _helpGroup;
        private readonly KryptonRibbonGroupButton _openButton;
        private readonly KryptonRibbonGroupButton _reloadButton;
        private readonly KryptonRibbonGroupButton _closeButton;
        private readonly KryptonRibbonGroupButton _lightButton;
        private readonly KryptonRibbonGroupButton _darkButton;
        private readonly KryptonRibbonGroupButton _minimizeButton;
        private readonly KryptonRibbonGroupButton _mapsButton;
        private readonly KryptonRibbonGroupButton _blocksButton;
        private readonly KryptonRibbonGroupButton _hexButton;
        private readonly KryptonRibbonGroupButton _openMapButton;
        private readonly KryptonRibbonGroupButton _closeMapButton;
        private readonly KryptonRibbonGroupButton _englishButton;
        private readonly KryptonRibbonGroupButton _germanButton;
        private readonly KryptonRibbonGroupButton _aboutButton;

        // --- chrome ----------------------------------------------------------
        private readonly Panel _infoPanel;
        private readonly KryptonLabel _infoLabel;
        private readonly SplitContainer _split;
        private readonly Panel _mapTreeHost;
        private readonly Panel _leftHeader;
        private readonly Label _treeHeaderLabel;
        private readonly Panel _viewerHeader;
        private readonly Label _viewerTitleLabel;
        private readonly Panel _viewerBody;
        private readonly Panel _placeholder;
        private readonly Label _placeholderTitle;
        private readonly Label _placeholderHint;
        private readonly Panel _blocksPanel;
        private readonly KryptonTextBox _report;
        private readonly Panel _hexPanel;
        private readonly HexViewerControl _hex;
        private readonly StatusStrip _statusStrip;
        private readonly ToolStripStatusLabel _statusFile;
        private readonly ToolStripStatusLabel _statusBlocks;
        private readonly ToolStripStatusLabel _statusChecksum;
        private readonly ToolStripStatusLabel _statusIdentity;
        private readonly ToolStripStatusLabel _statusSymbols;
        private readonly ToolStripStatusLabel[] _statusSeparators;

        private readonly string _initialFile;
        private KryptonDataGridView _grid;
        private TreeView _mapTree;
        private KryptonPalette _activePalette;
        private Control _inlinePlot;
        private MainView _view = MainView.Maps;

        // Last successfully requested path; re-inspected after a language
        // switch so the dynamic texts are re-rendered in the new language.
        private string _lastPath;
        private MapList.Map _selectedMap;
        private int _mapCount;

        // Colours derived from the active palette for the plain WinForms
        // chrome (headers, splitter, tree, status strip, placeholder).
        private Color _chromeSurface = Color.White;
        private Color _chromeHeader = Color.FromArgb(232, 232, 234);
        private Color _chromeLine = Color.FromArgb(214, 214, 216);
        private Color _chromeText = Color.FromArgb(34, 34, 34);
        private Color _chromeMuted = Color.FromArgb(120, 120, 120);

        public MainForm(ThemeKind initialTheme, string initialFile)
        {
            _initialFile = initialFile;
            _manager = new KryptonManager();
            _lightPalette = ThemePalettes.CreateLight();
            _darkPalette = ThemePalettes.CreateDark();

            Width = 1100;
            Height = 720;
            MinimumSize = new Size(1000, 520);
            StartPosition = FormStartPosition.CenterScreen;

            // --- ribbon -------------------------------------------------------
            _ribbon = new KryptonRibbon { Dock = DockStyle.Top };
            // Office-style minimize: double-click a tab (or the ribbon
            // minimize button) collapses the ribbon to just the tab strip.
            _ribbon.AllowMinimizedChange = true;
            // The original has no application tile before the tabs; the
            // "File" tab is the first tab.
            _ribbon.RibbonAppButton.AppButtonVisible = false;

            // File tab: File (Open) + Image (Reload / Close)
            _fileTab = new KryptonRibbonTab();
            _fileGroup = CreateGroup("file");
            _openButton = CreateButton("open");
            _openButton.Click += (s, e) => OpenFile();
            AddToTriple(_fileGroup, _openButton);
            _fileGroup.MinimumWidth = 150;
            _fileTab.Groups.Add(_fileGroup);

            _imageGroup = CreateGroup("image");
            _reloadButton = CreateButton("reload");
            _reloadButton.Click += (s, e) => ReloadFile();
            _closeButton = CreateButton("close");
            _closeButton.Click += (s, e) => CloseFile();
            AddToTriple(_imageGroup, _reloadButton, _closeButton);
            _imageGroup.MinimumWidth = 260;
            _fileTab.Groups.Add(_imageGroup);

            // View tab: Views (Maps / Blocks / Hex)
            _viewTab = new KryptonRibbonTab();
            _viewsGroup = CreateGroup("views");
            _mapsButton = CreateButton("maps");
            _mapsButton.ButtonType = GroupButtonType.Check;
            _mapsButton.Click += (s, e) => ShowView(MainView.Maps);
            _blocksButton = CreateButton("blocks");
            _blocksButton.ButtonType = GroupButtonType.Check;
            _blocksButton.Click += (s, e) => ShowView(MainView.Blocks);
            _hexButton = CreateButton("hex");
            _hexButton.ButtonType = GroupButtonType.Check;
            _hexButton.Click += (s, e) => ShowHexPanel(_hexButton.Checked);
            AddToTriple(_viewsGroup, _mapsButton, _blocksButton, _hexButton);
            // Generous widths: Krypton sizes a group to its contents, and
            // five tightly packed groups left half of the band empty.  These
            // widths distribute the View tab across the ribbon.
            _viewsGroup.MinimumWidth = 270;
            _viewTab.Groups.Add(_viewsGroup);

            // View tab: Map (open the selected heat map / close the viewer)
            _mapGroup = CreateGroup("map");
            _openMapButton = CreateButton("heat");
            _openMapButton.Click += (s, e) => ShowSelectedMap();
            _closeMapButton = CreateButton("close");
            _closeMapButton.Click += (s, e) => ClearInlineMap();
            AddToTriple(_mapGroup, _openMapButton, _closeMapButton);
            _mapGroup.MinimumWidth = 200;
            _viewTab.Groups.Add(_mapGroup);

            // View tab: Theme (Light / Dark)
            _themeGroup = CreateGroup("theme");
            _lightButton = CreateButton("light");
            _lightButton.ButtonType = GroupButtonType.Check;
            _darkButton = CreateButton("dark");
            _darkButton.ButtonType = GroupButtonType.Check;
            _lightButton.Click += (s, e) => SetTheme(ThemeKind.Light);
            _darkButton.Click += (s, e) => SetTheme(ThemeKind.Dark);
            AddToTriple(_themeGroup, _lightButton, _darkButton);
            _themeGroup.MinimumWidth = 190;
            _viewTab.Groups.Add(_themeGroup);

            // View tab: Language (English / Deutsch).  The button labels stay
            // literal: language names are never translated.
            _languageGroup = CreateGroup("language");
            _englishButton = CreateButton("en");
            _englishButton.ButtonType = GroupButtonType.Check;
            _englishButton.TextLine1 = "English";
            _englishButton.TextLine2 = string.Empty;
            _germanButton = CreateButton("de");
            _germanButton.ButtonType = GroupButtonType.Check;
            _germanButton.TextLine1 = "Deutsch";
            _germanButton.TextLine2 = string.Empty;
            _englishButton.Click += (s, e) => ChangeLanguage("en");
            _germanButton.Click += (s, e) => ChangeLanguage("de");
            AddToTriple(_languageGroup, _englishButton, _germanButton);
            _languageGroup.MinimumWidth = 180;
            _viewTab.Groups.Add(_languageGroup);

            // View tab: Ribbon minimize (collapse to just the tab strip).
            _ribbonGroup = CreateGroup("ribbon");
            _minimizeButton = CreateButton("minimize");
            _minimizeButton.ButtonType = GroupButtonType.Check;
            _minimizeButton.Click += (s, e) => _ribbon.MinimizedMode = _minimizeButton.Checked;
            AddToTriple(_ribbonGroup, _minimizeButton);
            _ribbonGroup.MinimumWidth = 130;
            _viewTab.Groups.Add(_ribbonGroup);

            // Help tab: About
            _helpTab = new KryptonRibbonTab();
            _helpGroup = CreateGroup("help");
            _aboutButton = CreateButton("info");
            _aboutButton.Click += (s, e) => ShowAbout();
            AddToTriple(_helpGroup, _aboutButton);
            _helpTab.Groups.Add(_helpGroup);

            _ribbon.RibbonTabs.Add(_fileTab);
            _ribbon.RibbonTabs.Add(_viewTab);
            _ribbon.RibbonTabs.Add(_helpTab);

            // Collapse the ribbon by default (the original is dense and a
            // collapsed ribbon reads cleaner).  The actual MinimizedMode is
            // applied in OnShown: before the ribbon has a window handle,
            // Krypton collapses it to zero height instead of keeping the tab
            // strip.
            _minimizeButton.Checked = true;

            // Keep the button in sync when the ribbon is collapsed by
            // double-clicking a tab (AllowMinimizedChange) as well.  The
            // bottom padding is a wine-mono workaround: the minimized popup
            // is laid out 24 px too short and otherwise clips the button
            // sublabels and the group captions.  Expanded mode clears it.
            _ribbon.MinimizedModeChanged += (s, e) =>
            {
                _minimizeButton.Checked = _ribbon.MinimizedMode;
                _ribbon.Padding = _ribbon.MinimizedMode
                    ? new Padding(0, 0, 0, 24)
                    : Padding.Empty;
            };

            // --- one-line info strip -----------------------------------------
            _infoPanel = new Panel { Dock = DockStyle.Top, Height = 26 };
            _infoLabel = new KryptonLabel
            {
                Dock = DockStyle.Fill,
                AutoSize = false
            };
            _infoLabel.StateCommon.ShortText.Font = new Font("Segoe UI", 9F);
            _infoLabel.StateCommon.ShortText.TextV = PaletteRelativeAlign.Center;
            _infoLabel.StateCommon.Padding = new Padding(8, 0, 8, 0);
            _infoPanel.Controls.Add(_infoLabel);
            _infoPanel.Paint += PaintChromeBorder;

            // --- map tree (left) + map viewer (right) ------------------------
            // The size must be set before the minimum panel sizes: a fresh
            // SplitContainer is only 150 px wide and wine-mono validates the
            // minimums against that width.
            _split = new SplitContainer
            {
                Orientation = Orientation.Vertical,
                FixedPanel = FixedPanel.Panel1,
                SplitterWidth = 4,
                BorderStyle = BorderStyle.None
            };
            _split.Size = new Size(1000, 600);
            _split.Panel1MinSize = 190;
            _split.Panel2MinSize = 300;
            _split.SplitterDistance = 300;
            _split.Dock = DockStyle.Fill;

            _leftHeader = new Panel { Dock = DockStyle.Top, Height = 26 };
            _treeHeaderLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 8, 0),
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            _leftHeader.Controls.Add(_treeHeaderLabel);
            _leftHeader.Paint += PaintChromeBorder;

            _viewerHeader = new Panel { Dock = DockStyle.Top, Height = 26 };
            _viewerTitleLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 8, 0),
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            _viewerHeader.Controls.Add(_viewerTitleLabel);
            _viewerHeader.Paint += PaintChromeBorder;

            _viewerBody = new Panel { Dock = DockStyle.Fill };

            _placeholderTitle = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 14F),
                TextAlign = ContentAlignment.MiddleCenter
            };
            _placeholderHint = new Label
            {
                AutoSize = false,
                Height = 40,
                Font = new Font("Segoe UI", 9F),
                TextAlign = ContentAlignment.TopCenter
            };
            _placeholder = new Panel { Dock = DockStyle.Fill };
            _placeholder.Controls.Add(_placeholderTitle);
            _placeholder.Controls.Add(_placeholderHint);
            _placeholder.Resize += (s, e) => LayoutPlaceholder();

            _viewerBody.Controls.Add(_placeholder);
            // Panel1: tree host (Fill) + header (Top).  The TreeView itself
            // is created in OnShown (wine-mono needs a message loop first).
            _mapTreeHost = new Panel { Dock = DockStyle.Fill };
            _split.Panel1.Controls.Add(_mapTreeHost);
            _split.Panel1.Controls.Add(_leftHeader);
            _split.Panel2.Controls.Add(_viewerBody);
            _split.Panel2.Controls.Add(_viewerHeader);

            // --- blocks + checksum (behind View > Views > Blocks) ------------
            _blocksPanel = new Panel { Dock = DockStyle.Fill, Visible = false };
            _report = new KryptonTextBox
            {
                // Multiline must be set before Dock/Height: Krypton 5.550
                // resets the height to the single-line value when Multiline
                // flips afterwards, which collapsed this box to 0 px.
                Multiline = true,
                Dock = DockStyle.Bottom,
                Height = 170,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9F)
            };
            _blocksPanel.Controls.Add(_report);

            // --- hex viewer panel --------------------------------------------
            _hex = new HexViewerControl { Dock = DockStyle.Fill };
            _hexPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 160,
                Visible = false
            };
            _hexPanel.Controls.Add(_hex);

            // --- status bar ---------------------------------------------------
            // Plain WinForms StatusStrip (there is no Krypton status strip);
            // its surface and text are pinned to the active palette in
            // ApplyStatusPalette, the same way the hex panel is themed.
            _statusFile = CreateStatusLabel(I18n.T("status_no_file"), false);
            _statusFile.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _statusBlocks = CreateStatusLabel(string.Empty, false);
            _statusChecksum = CreateStatusLabel(string.Empty, false);
            _statusIdentity = CreateStatusLabel(string.Empty, true);
            _statusIdentity.AutoToolTip = true;
            _statusSymbols = CreateStatusLabel(string.Empty, false);

            _statusSeparators = new ToolStripStatusLabel[4];
            for (int i = 0; i < _statusSeparators.Length; i++)
            {
                _statusSeparators[i] = CreateStatusLabel("|", false);
            }

            _statusStrip = new StatusStrip
            {
                Dock = DockStyle.Bottom,
                SizingGrip = false,
                ShowItemToolTips = true,
                // The system renderer honours BackColor; the default
                // professional renderer paints a gradient over it.
                RenderMode = ToolStripRenderMode.System
            };
            _statusStrip.Items.AddRange(new ToolStripItem[]
            {
                _statusFile,
                _statusSeparators[0],
                _statusBlocks,
                _statusSeparators[1],
                _statusChecksum,
                _statusSeparators[2],
                _statusIdentity,
                _statusSeparators[3],
                _statusSymbols
            });

            // Add order gives the desired dock priority (later index docks
            // first): ribbon, info strip, status strip, hex panel, then the
            // two alternative Fill surfaces (split / blocks).
            Controls.Add(_split);
            Controls.Add(_blocksPanel);
            Controls.Add(_hexPanel);
            Controls.Add(_statusStrip);
            Controls.Add(_infoPanel);
            Controls.Add(_ribbon);

            // Apply the palette before the form is first shown.  See THEMING.md.
            ApplyTheme(initialTheme);

            // All static texts are assigned from the I18n table.
            ApplyTexts();
            UpdateViewButtons();
            UpdateStatusSeparators();
            _reloadButton.Enabled = false;
            _openMapButton.Enabled = false;
            _closeMapButton.Enabled = false;
        }

        /// <summary>
        /// Builds the tree and the block grid once the form is visible.  This
        /// must happen after the message loop has started, because
        /// <see cref="KryptonDataGridView"/> calls <c>CreateGraphics()</c> in
        /// its constructor and wine-mono throws "Error creating window handle"
        /// for a handle created before the loop.
        /// </summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            // Collapse to the tab strip once the ribbon has a window handle,
            // and start on the View tab (where the layout controls live).
            // The 24 px bottom padding keeps the minimized popup from
            // clipping its button sublabels under wine-mono;
            // MinimizedModeChanged clears it again on expand.
            _ribbon.SelectedTab = _viewTab;
            _ribbon.Padding = new Padding(0, 0, 0, 24);
            _ribbon.MinimizedMode = true;
            _minimizeButton.Checked = true;

            // Left panel: header on top, tree filling the rest.
            _mapTree = new TreeView
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                HideSelection = false,
                ShowLines = false,
                ShowPlusMinus = true,
                ShowRootLines = false,
                ShowNodeToolTips = true,
                FullRowSelect = true,
                ItemHeight = 19,
                Indent = 16,
                Visible = true
            };
            _mapTree.NodeMouseDoubleClick += MapTree_NodeMouseDoubleClick;
            _mapTree.AfterSelect += MapTree_AfterSelect;
            _mapTree.KeyDown += MapTree_KeyDown;

            _mapTreeHost.Controls.Add(_mapTree);

            // ~300 px tree; set here because the split container only has its
            // final size after the form is laid out.
            try
            {
                _split.SplitterDistance = 300;
            }
            catch
            {
                // Keep the default distance if the form is too small.
            }

            // Block grid inside the blocks panel (Fill, report is Bottom).
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
            _blocksPanel.Controls.Add(_grid);
            _blocksPanel.Controls.SetChildIndex(_grid, 0);

            // The tree/grid did not exist when the constructor applied the
            // palette, so pin their colours now.
            ApplyChromePalette(_activePalette);
            if (_activePalette != null && _grid != null) _grid.Palette = _activePalette;

            // Grid headers are localized; the grid did not exist when the
            // constructor ran ApplyTexts.
            ApplyTexts();
            LayoutPlaceholder();

            if (_initialFile != null) Inspect(_initialFile);
        }

        // ------------------------------------------------------------------
        // ribbon construction helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Creates a ribbon group with a single-line caption.  A minimum width
        /// is set explicitly: Krypton computes a narrow group width from a
        /// one-button triple and then clips the caption ("Ribbo" instead of
        /// "Ribbon") without ellipsis.  110 px leaves room for the longest
        /// caption in either language plus the group padding.
        /// </summary>
        private static KryptonRibbonGroup CreateGroup(string captionKeySuffix)
        {
            KryptonRibbonGroup group = new KryptonRibbonGroup();
            group.MinimumWidth = 110;
            group.Tag = captionKeySuffix;
            return group;
        }

        /// <summary>Creates a large ribbon button with a generated 32 px icon.</summary>
        private static KryptonRibbonGroupButton CreateButton(string iconKind)
        {
            KryptonRibbonGroupButton button = new KryptonRibbonGroupButton();
            Image large = CreateIcon(iconKind, 32);
            button.ImageLarge = large;
            button.ImageSmall = new Bitmap(large, 16, 16);
            return button;
        }

        private static void AddToTriple(KryptonRibbonGroup group, params KryptonRibbonGroupButton[] buttons)
        {
            KryptonRibbonGroupTriple triple = new KryptonRibbonGroupTriple();
            for (int i = 0; i < buttons.Length; i++) triple.Items.Add(buttons[i]);
            group.Items.Add(triple);
        }

        /// <summary>
        /// Small themed toolbar icons, drawn with GDI+ so the sample needs no
        /// image resources and every button loses Krypton's placeholder
        /// puzzle-piece glyph.
        /// </summary>
        private static Image CreateIcon(string kind, int size)
        {
            Bitmap bitmap = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);
                float scale = size / 32f;
                g.ScaleTransform(scale, scale);

                Color accent = Color.FromArgb(45, 127, 193);
                using (Pen pen = new Pen(accent, 2.2f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;

                    switch (kind)
                    {
                        case "open":
                            using (GraphicsPath path = new GraphicsPath())
                            {
                                path.AddLines(new PointF[]
                                {
                                    new PointF(4, 10), new PointF(12, 10),
                                    new PointF(14, 7), new PointF(28, 7),
                                    new PointF(28, 25), new PointF(4, 25)
                                });
                                path.CloseFigure();
                                using (SolidBrush fill = new SolidBrush(Color.FromArgb(34, accent)))
                                    g.FillPath(fill, path);
                                g.DrawPath(pen, path);
                            }
                            break;

                        case "reload":
                            g.DrawArc(pen, 6, 6, 20, 20, 35, 285);
                            using (SolidBrush brush = new SolidBrush(accent))
                            {
                                g.FillPolygon(brush, new PointF[]
                                {
                                    new PointF(23, 3), new PointF(29, 4), new PointF(26, 9.5f)
                                });
                            }
                            break;

                        case "close":
                            g.DrawLine(pen, 9, 9, 23, 23);
                            g.DrawLine(pen, 23, 9, 9, 23);
                            break;

                        case "maps":
                            using (Pen thin = new Pen(accent, 1.6f))
                            {
                                g.DrawRectangle(thin, 5, 5, 22, 22);
                                g.DrawLine(thin, 12.5f, 5, 12.5f, 27);
                                g.DrawLine(thin, 19.5f, 5, 19.5f, 27);
                                g.DrawLine(thin, 5, 12.5f, 27, 12.5f);
                                g.DrawLine(thin, 5, 19.5f, 27, 19.5f);
                            }
                            break;

                        case "blocks":
                            using (SolidBrush brush = new SolidBrush(Color.FromArgb(200, accent)))
                            {
                                g.FillRectangle(brush, 5, 6, 22, 5);
                                g.FillRectangle(brush, 5, 13.5f, 16, 5);
                                g.FillRectangle(brush, 5, 21, 22, 5);
                            }
                            break;

                        case "heat":
                            for (int row = 0; row < 4; row++)
                            {
                                for (int col = 0; col < 4; col++)
                                {
                                    float t = (row * 4 + col) / 15f;
                                    Color cell = Blend(
                                        Color.FromArgb(45, 90, 200),
                                        Color.FromArgb(230, 90, 40), t);
                                    using (SolidBrush brush = new SolidBrush(cell))
                                        g.FillRectangle(brush, 5 + col * 5.5f, 5 + row * 5.5f, 4.5f, 4.5f);
                                }
                            }
                            break;

                        case "hex":
                            using (Font font = new Font("Consolas", 11F, FontStyle.Bold))
                            using (SolidBrush brush = new SolidBrush(accent))
                                g.DrawString("0x", font, brush, new PointF(1.5f, 7));
                            break;

                        case "light":
                            using (SolidBrush brush = new SolidBrush(accent))
                                g.FillEllipse(brush, 11, 11, 10, 10);
                            for (int i = 0; i < 8; i++)
                            {
                                double angle = i * Math.PI / 4;
                                g.DrawLine(pen,
                                    16 + (float)(Math.Cos(angle) * 8), 16 + (float)(Math.Sin(angle) * 8),
                                    16 + (float)(Math.Cos(angle) * 12), 16 + (float)(Math.Sin(angle) * 12));
                            }
                            break;

                        case "dark":
                            g.DrawArc(pen, 8, 5, 18, 22, 60, 250);
                            g.DrawArc(pen, 14, 5, 11, 22, 60, 250);
                            break;

                        case "minimize":
                            g.DrawLines(pen, new PointF[]
                            {
                                new PointF(9, 7), new PointF(16, 14), new PointF(23, 7)
                            });
                            g.DrawLines(pen, new PointF[]
                            {
                                new PointF(9, 15), new PointF(16, 22), new PointF(23, 15)
                            });
                            break;

                        case "en":
                        case "de":
                            using (Font font = new Font("Segoe UI", 10F, FontStyle.Bold))
                            using (SolidBrush brush = new SolidBrush(accent))
                                g.DrawString(kind == "en" ? "EN" : "DE", font, brush, new PointF(4, 7));
                            break;

                        case "info":
                            g.DrawEllipse(pen, 8, 8, 16, 16);
                            using (SolidBrush brush = new SolidBrush(accent))
                            {
                                g.FillEllipse(brush, 14.5f, 11, 3, 3);
                                g.FillRectangle(brush, 14.5f, 16, 3, 7);
                            }
                            break;
                    }
                }
            }
            return bitmap;
        }

        /// <summary>
        /// Assigns every static, translatable text from the current
        /// <see cref="I18n"/> table and syncs the language check buttons.
        /// </summary>
        private void ApplyTexts()
        {
            Text = I18n.T("window_title");

            _fileTab.Text = I18n.T("tab_file");
            _viewTab.Text = I18n.T("tab_view");
            _helpTab.Text = I18n.T("tab_help");

            // Group captions are single-line: two-line captions plus a narrow
            // group width were exactly what got clipped before.
            SetGroupText(_fileGroup, I18n.T("group_file"));
            SetGroupText(_imageGroup, I18n.T("group_image"));
            SetGroupText(_viewsGroup, I18n.T("group_views"));
            SetGroupText(_mapGroup, I18n.T("group_map"));
            SetGroupText(_themeGroup, I18n.T("view_theme"));
            SetGroupText(_languageGroup, I18n.T("language"));
            SetGroupText(_ribbonGroup, I18n.T("group_ribbon"));
            SetGroupText(_helpGroup, I18n.T("group_help"));

            _openButton.TextLine1 = I18n.T("file_open");
            _openButton.TextLine2 = I18n.T("file_open_binary");
            _reloadButton.TextLine1 = I18n.T("file_reload");
            _reloadButton.TextLine2 = I18n.T("file_reload_sub");
            _closeButton.TextLine1 = I18n.T("file_close");
            _closeButton.TextLine2 = I18n.T("file_close_sub");

            _mapsButton.TextLine1 = I18n.T("maps");
            _mapsButton.TextLine2 = I18n.T("maps_sub");
            _blocksButton.TextLine1 = I18n.T("views_blocks");
            _blocksButton.TextLine2 = I18n.T("views_blocks_sub");
            _hexButton.TextLine1 = I18n.T("view_hex");
            _hexButton.TextLine2 = I18n.T("view_hex_viewer");
            _openMapButton.TextLine1 = I18n.T("map_open");
            _openMapButton.TextLine2 = I18n.T("map_open_sub");
            _closeMapButton.TextLine1 = I18n.T("map_close");
            _closeMapButton.TextLine2 = I18n.T("map_close_sub");

            _lightButton.TextLine1 = I18n.T("theme_light");
            _lightButton.TextLine2 = I18n.T("theme_sub");
            _darkButton.TextLine1 = I18n.T("theme_dark");
            _darkButton.TextLine2 = I18n.T("theme_sub");

            _minimizeButton.TextLine1 = I18n.T("ribbon_minimize");
            _minimizeButton.TextLine2 = I18n.T("ribbon_sub");

            _aboutButton.TextLine1 = I18n.T("help_about");
            _aboutButton.TextLine2 = I18n.T("help_about_sub");

            if (_lastPath == null) _infoLabel.Text = I18n.T("info_open_binary");
            _treeHeaderLabel.Text = _lastPath == null
                ? I18n.T("tree_header_empty")
                : I18n.T("tree_header", _mapCount);
            _viewerTitleLabel.Text = _selectedMap != null
                ? MapTitle(_selectedMap)
                : I18n.T("viewer_title");
            _placeholderTitle.Text = I18n.T("viewer_placeholder");
            _placeholderHint.Text = I18n.T("viewer_hint");

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

            LayoutPlaceholder();
        }

        private static void SetGroupText(KryptonRibbonGroup group, string text)
        {
            group.TextLine1 = text;
            group.TextLine2 = string.Empty;
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

        // ------------------------------------------------------------------
        // theming
        // ------------------------------------------------------------------

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

            ApplyChromePalette(palette);
            ApplyRibbonColors(palette);
            ApplyHexPalette(palette);
            ApplyTreePalette();
            ApplyStatusPalette();

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
        /// Derives the plain-WinForms chrome colours (headers, splitter, tree,
        /// placeholder, info strip, status strip) from the active palette.
        /// Everything is computed from the form background, so a new palette
        /// needs no extra per-control case.
        /// </summary>
        private void ApplyChromePalette(KryptonPalette palette)
        {
            Color formBack = palette.GetBackColor1(PaletteBackStyle.FormMain, PaletteState.Normal);
            if (formBack.A != 255) formBack = SystemColors.Control;

            bool dark = formBack.GetBrightness() < 0.5f;
            _chromeSurface = dark ? formBack : Color.White;
            _chromeHeader = dark
                ? Blend(formBack, Color.White, 0.09f)
                : Blend(Color.White, Color.Black, 0.06f);
            _chromeLine = dark
                ? Blend(formBack, Color.White, 0.18f)
                : Blend(Color.White, Color.Black, 0.16f);

            Color text;
            try
            {
                text = palette.GetContentShortTextColor1(
                    PaletteContentStyle.LabelNormalPanel, PaletteState.Normal);
            }
            catch
            {
                text = dark ? Color.FromArgb(230, 230, 230) : Color.FromArgb(34, 34, 34);
            }
            if (text.A != 255)
                text = dark ? Color.FromArgb(230, 230, 230) : Color.FromArgb(34, 34, 34);
            _chromeText = text;
            _chromeMuted = Blend(text, _chromeSurface, 0.42f);

            // Split container and its panels.
            _split.BackColor = _chromeLine;
            _split.Panel1.BackColor = _chromeSurface;
            _split.Panel2.BackColor = _chromeSurface;

            // Headers (plain panels + labels).
            _leftHeader.BackColor = _chromeHeader;
            _treeHeaderLabel.BackColor = _chromeHeader;
            _treeHeaderLabel.ForeColor = _chromeText;
            _viewerHeader.BackColor = _chromeHeader;
            _viewerTitleLabel.BackColor = _chromeHeader;
            _viewerTitleLabel.ForeColor = _chromeText;

            // Viewer surface and the empty-state text.
            _viewerBody.BackColor = _chromeSurface;
            _placeholder.BackColor = _chromeSurface;
            _placeholderTitle.BackColor = _chromeSurface;
            _placeholderTitle.ForeColor = _chromeText;
            _placeholderHint.BackColor = _chromeSurface;
            _placeholderHint.ForeColor = _chromeMuted;

            _blocksPanel.BackColor = _chromeSurface;

            // Info strip.
            _infoPanel.BackColor = _chromeHeader;
            _infoLabel.BackColor = _chromeHeader;

            // Tree (created in OnShown).
            if (_mapTree != null)
            {
                _mapTree.BackColor = _chromeSurface;
                _mapTree.ForeColor = _chromeText;
            }

            _leftHeader.Invalidate();
            _viewerHeader.Invalidate();
            _infoPanel.Invalidate();
            _placeholder.Invalidate();
        }

        /// <summary>
        /// Pins the ribbon's surfaces and captions to the chrome colours.
        /// The base palettes draw the ribbon band in their own theme colours
        /// (office black stays black even in the light palette) and use
        /// near-white tab captions meant for a coloured header.  Setting the
        /// flat surface colours and the text colours explicitly gives both
        /// themes the same clean, readable ribbon.
        /// </summary>
        private void ApplyRibbonColors(KryptonPalette palette)
        {
            try
            {
                Color text = _chromeText;
                Color disabled = _chromeMuted;

                // Group band: flat surface, like the map viewer.
                SetRibbonBack(palette.Ribbon.RibbonGroupArea.StateCommon, _chromeSurface);
                SetRibbonBack(palette.Ribbon.RibbonGroupArea.StateCheckedNormal, _chromeSurface);
                SetRibbonBack(palette.Ribbon.RibbonGroupArea.StateContextCheckedNormal, _chromeSurface);

                // Tab strip: header colour; the selected tab uses the surface.
                SetRibbonDouble(palette.Ribbon.RibbonTab.StateCommon, _chromeHeader, text);
                SetRibbonDouble(palette.Ribbon.RibbonTab.StateNormal, _chromeHeader, text);
                SetRibbonDouble(palette.Ribbon.RibbonTab.StateTracking, _chromeSurface, text);
                SetRibbonDouble(palette.Ribbon.RibbonTab.StateCheckedNormal, _chromeSurface, text);
                SetRibbonDouble(palette.Ribbon.RibbonTab.StateCheckedTracking, _chromeSurface, text);
                SetRibbonDouble(palette.Ribbon.RibbonTab.StateContextTracking, _chromeSurface, text);
                SetRibbonDouble(palette.Ribbon.RibbonTab.StateContextCheckedNormal, _chromeSurface, text);
                SetRibbonDouble(palette.Ribbon.RibbonTab.StateContextCheckedTracking, _chromeSurface, text);

                palette.Ribbon.RibbonGroupButtonText.StateCommon.TextColor = text;
                palette.Ribbon.RibbonGroupButtonText.StateNormal.TextColor = text;
                palette.Ribbon.RibbonGroupButtonText.StateDisabled.TextColor = disabled;

                palette.Ribbon.RibbonGroupLabelText.StateCommon.TextColor = text;
                palette.Ribbon.RibbonGroupLabelText.StateNormal.TextColor = text;
                palette.Ribbon.RibbonGroupLabelText.StateDisabled.TextColor = disabled;

                SetRibbonDouble(palette.Ribbon.RibbonGroupNormalTitle.StateCommon, _chromeSurface, text);
                SetRibbonDouble(palette.Ribbon.RibbonGroupNormalTitle.StateNormal, _chromeSurface, text);
                SetRibbonDouble(palette.Ribbon.RibbonGroupNormalTitle.StateTracking, _chromeHeader, text);

                palette.Ribbon.RibbonGroupCollapsedText.StateCommon.TextColor = text;
                palette.Ribbon.RibbonGroupCollapsedText.StateNormal.TextColor = text;
                palette.Ribbon.RibbonGroupCollapsedText.StateTracking.TextColor = text;

                palette.Ribbon.RibbonGroupCheckBoxText.StateCommon.TextColor = text;
                palette.Ribbon.RibbonGroupCheckBoxText.StateNormal.TextColor = text;

                // Checked ribbon buttons: the base palettes use a bright
                // yellow highlight (white-on-yellow in the dark theme).  Use
                // the accent selection tint instead, with the same text
                // colour as the other buttons.
                bool darkChrome = text.GetBrightness() > 0.5f;
                Color checkedBack = darkChrome
                    ? Color.FromArgb(38, 79, 120)
                    : Color.FromArgb(205, 226, 244);
                Color hoverBack = darkChrome
                    ? Blend(_chromeSurface, Color.White, 0.10f)
                    : Blend(_chromeSurface, Color.Black, 0.05f);

                ApplyCheckButtonColors(palette.ButtonStyles.ButtonCommon, checkedBack, hoverBack);
                ApplyCheckButtonColors(palette.ButtonStyles.ButtonStandalone, checkedBack, hoverBack);

                if (_ribbon != null) _ribbon.Invalidate(true);
            }
            catch
            {
                // A missing palette member must not break theming.
            }
        }

        /// <summary>
        /// Sets the checked/hover backgrounds of one Krypton button style.
        /// Flat solid fills, so the ribbon check buttons do not fall back to
        /// the palette's yellow highlight.
        /// </summary>
        private static void ApplyCheckButtonColors(
            KryptonPaletteCheckButton style, Color checkedBack, Color hoverBack)
        {
            if (style == null) return;
            SetTripleBack(style.StateCheckedNormal, checkedBack);
            SetTripleBack(style.StateCheckedTracking, checkedBack);
            SetTripleBack(style.StatePressed, checkedBack);
            SetTripleBack(style.StateTracking, hoverBack);
        }

        private static void SetTripleBack(PaletteTriple triple, Color color)
        {
            if (triple == null || triple.Back == null) return;
            triple.Back.Color1 = color;
            triple.Back.Color2 = color;
            triple.Back.ColorStyle = PaletteColorStyle.Solid;
        }

        /// <summary>Fills all five ribbon gradient stops with one flat colour.</summary>
        private static void SetRibbonBack(PaletteRibbonBack back, Color color)
        {
            if (back == null) return;
            back.BackColor1 = color;
            back.BackColor2 = color;
            back.BackColor3 = color;
            back.BackColor4 = color;
            back.BackColor5 = color;
        }

        /// <summary>Flat ribbon tab/title surface plus caption colour.</summary>
        private static void SetRibbonDouble(PaletteRibbonDouble item, Color back, Color text)
        {
            if (item == null) return;
            item.BackColor1 = back;
            item.BackColor2 = back;
            item.BackColor3 = back;
            item.BackColor4 = back;
            item.BackColor5 = back;
            item.TextColor = text;
        }

        /// <summary>Draws the one-pixel separator along the bottom of a header panel.</summary>
        private void PaintChromeBorder(object sender, PaintEventArgs e)
        {
            Panel panel = sender as Panel;
            if (panel == null) return;
            using (Pen pen = new Pen(_chromeLine))
                e.Graphics.DrawLine(pen, 0, panel.Height - 1, panel.Width, panel.Height - 1);
        }

        /// <summary>Linear blend: t = 0 returns a, t = 1 returns b.</summary>
        private static Color Blend(Color a, Color b, float t)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return Color.FromArgb(
                255,
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
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
        /// Matches the plain WinForms TreeView to the chrome surface.  Krypton
        /// has no tree control, so BackColor/ForeColor are its theming hooks.
        /// The expand/collapse glyphs are drawn by the OS and do not follow
        /// the palette (see POLISH.md).
        /// </summary>
        private void ApplyTreePalette()
        {
            if (_mapTree == null) return;
            _mapTree.BackColor = _chromeSurface;
            _mapTree.ForeColor = _chromeText;
            _mapTree.Invalidate();
        }

        /// <summary>
        /// Matches the status strip and all its labels to the chrome colours.
        /// The status strip uses the header colour and the palette's text
        /// colour so it also works in dark mode.
        /// </summary>
        private void ApplyStatusPalette()
        {
            if (_statusStrip == null) return;
            _statusStrip.BackColor = _chromeHeader;
            for (int i = 0; i < _statusStrip.Items.Count; i++)
            {
                ToolStripItem item = _statusStrip.Items[i];
                item.BackColor = _chromeHeader;
                item.ForeColor = _chromeText;
            }
            for (int i = 0; i < _statusSeparators.Length; i++)
                _statusSeparators[i].ForeColor = _chromeMuted;
            _statusStrip.Invalidate();
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
        }

        private void SetTheme(ThemeKind kind)
        {
            ApplyTheme(kind);
            ThemeStore.Save(kind);
        }

        /// <summary>Status-bar label with a consistent font and padding.</summary>
        private static ToolStripStatusLabel CreateStatusLabel(string text, bool spring)
        {
            ToolStripStatusLabel label = new ToolStripStatusLabel
            {
                Text = text,
                Spring = spring,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = true,
                Font = new Font("Segoe UI", 9F),
                Margin = new Padding(4, 0, 4, 0)
            };
            return label;
        }

        // ------------------------------------------------------------------
        // view switching
        // ------------------------------------------------------------------

        /// <summary>Shows or hides the docked hex viewer.</summary>
        private void ShowHexPanel(bool visible)
        {
            _hexPanel.Visible = visible;
            _hexButton.Checked = visible;
        }

        /// <summary>
        /// Switches the centre surface between the map tree/viewer (default,
        /// like the original) and the block grid + checksum report.  The two
        /// are mutually exclusive so neither leaves a blank band.
        /// </summary>
        private void ShowView(MainView view)
        {
            _view = view;
            bool maps = view == MainView.Maps;
            _split.Visible = maps;
            _blocksPanel.Visible = !maps;
            UpdateViewButtons();
            if (maps && _mapTree != null) _mapTree.Focus();
        }

        private void UpdateViewButtons()
        {
            _mapsButton.Checked = _view == MainView.Maps;
            _blocksButton.Checked = _view == MainView.Blocks;
            _hexButton.Checked = _hexPanel.Visible;
        }

        /// <summary>Centres the empty-state title and its hint inside the viewer.</summary>
        private void LayoutPlaceholder()
        {
            if (_placeholder == null || _placeholderTitle == null || _placeholderHint == null) return;

            int width = _placeholder.ClientSize.Width;
            int height = _placeholder.ClientSize.Height;
            int titleWidth = _placeholderTitle.PreferredWidth;
            int titleHeight = _placeholderTitle.PreferredHeight;
            int hintWidth = Math.Max(120, width - 60);

            int blockHeight = titleHeight + 6 + _placeholderHint.Height;
            int top = Math.Max(8, (height - blockHeight) / 2);
            _placeholderTitle.Location = new Point(Math.Max(4, (width - titleWidth) / 2), top);
            _placeholderHint.Width = hintWidth;
            _placeholderHint.Location = new Point(Math.Max(4, (width - hintWidth) / 2), top + titleHeight + 6);
        }

        // ------------------------------------------------------------------
        // map tree
        // ------------------------------------------------------------------

        /// <summary>
        /// Opens the selected Kennfeld inline in the right-hand viewer.  The
        /// heat map is built by the existing <see cref="MapViewForm"/> (which
        /// owns the OxyPlot decoding); its PlotView is reparented into the
        /// viewer panel so the result is the original's tree-left,
        /// map-right layout instead of a floating window.  Any failure falls
        /// back to the floating viewer.
        /// </summary>
        private void OpenMapInline(MapList.Map map)
        {
            if (map == null || _lastPath == null) return;

            _selectedMap = map;
            ClearInlineMap();
            SelectTreeMap(map);

            MapViewForm shell = null;
            try
            {
                shell = MapViewForm.FromBinary(_lastPath, map.Varname);
                PlotView plot = null;
                for (int i = 0; i < shell.Controls.Count; i++)
                {
                    PlotView candidate = shell.Controls[i] as PlotView;
                    if (candidate != null) { plot = candidate; break; }
                }

                if (plot == null)
                {
                    // Unexpected shell layout: keep the proven window path.
                    shell.Show(this);
                    shell = null;
                    return;
                }

                shell.Controls.Remove(plot);
                plot.Dock = DockStyle.Fill;
                plot.BackColor = Color.White;
                _viewerBody.Controls.Add(plot);
                plot.BringToFront();
                _inlinePlot = plot;
                _placeholder.Visible = false;
                _viewerTitleLabel.Text = MapTitle(map);
                _closeMapButton.Enabled = true;
                plot.Invalidate();
            }
            catch (Exception ex)
            {
                _report.AppendText("\r\nMap view: " + ex.Message + "\r\n");
                _viewerTitleLabel.Text = map.Varname;
                try
                {
                    MapViewForm.ShowMap(_lastPath, map.Varname, this);
                }
                catch (Exception fallback)
                {
                    _report.AppendText("\r\nMap view: " + fallback.Message + "\r\n");
                }
            }
            finally
            {
                if (shell != null) shell.Dispose();
            }
        }

        /// <summary>Highlights the tree leaf that belongs to the given map.</summary>
        private void SelectTreeMap(MapList.Map map)
        {
            if (_mapTree == null || map == null) return;
            TreeNode node = FindMapNode(_mapTree.Nodes, map);
            if (node != null && _mapTree.SelectedNode != node) _mapTree.SelectedNode = node;
        }

        private static TreeNode FindMapNode(TreeNodeCollection nodes, MapList.Map map)
        {
            if (nodes == null) return null;
            for (int i = 0; i < nodes.Count; i++)
            {
                TreeNode node = nodes[i];
                if (ReferenceEquals(node.Tag, map)) return node;
                TreeNode inner = FindMapNode(node.Nodes, map);
                if (inner != null) return inner;
            }
            return null;
        }

        /// <summary>Removes an inline plot and restores the placeholder.</summary>
        private void ClearInlineMap()
        {
            if (_inlinePlot != null)
            {
                _viewerBody.Controls.Remove(_inlinePlot);
                _inlinePlot.Dispose();
                _inlinePlot = null;
            }
            if (_closeMapButton != null) _closeMapButton.Enabled = false;
            if (_placeholder != null) _placeholder.Visible = true;
            if (_viewerTitleLabel != null)
            {
                _viewerTitleLabel.Text = _selectedMap != null
                    ? MapTitle(_selectedMap)
                    : I18n.T("viewer_title");
            }
        }

        /// <summary>Opens the selected map (or the first leaf) in the viewer.</summary>
        private void ShowSelectedMap()
        {
            MapList.Map map = _selectedMap;
            if (map == null) map = FirstMapLeaf(_mapTree == null ? null : _mapTree.Nodes);
            if (map != null) OpenMapInline(map);
        }

        private static MapList.Map FirstMapLeaf(TreeNodeCollection nodes)
        {
            if (nodes == null) return null;
            for (int i = 0; i < nodes.Count; i++)
            {
                TreeNode node = nodes[i];
                MapList.Map map = node.Tag as MapList.Map;
                if (map != null) return map;
                map = FirstMapLeaf(node.Nodes);
                if (map != null) return map;
            }
            return null;
        }

        private void MapTree_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            MapList.Map map = e.Node == null ? null : e.Node.Tag as MapList.Map;
            if (map != null) OpenMapInline(map);
        }

        private void MapTree_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter || _mapTree == null) return;
            TreeNode node = _mapTree.SelectedNode;
            MapList.Map map = node == null ? null : node.Tag as MapList.Map;
            if (map != null)
            {
                OpenMapInline(map);
                e.Handled = true;
            }
        }

        private void MapTree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            MapList.Map map = e.Node == null ? null : e.Node.Tag as MapList.Map;
            if (map == null) return;
            _selectedMap = map;
            // While a plot is on screen its title stays authoritative; only
            // the empty viewer follows the tree selection.
            if (_inlinePlot == null) _viewerTitleLabel.Text = MapTitle(map);
        }

        // ------------------------------------------------------------------
        // file handling
        // ------------------------------------------------------------------

        private void ShowAbout()
        {
            MessageBox.Show(
                this,
                I18n.T("about_text"),
                I18n.T("about_title"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
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

        private void ReloadFile()
        {
            if (_lastPath != null) Inspect(_lastPath);
            else OpenFile();
        }

        /// <summary>Clears the loaded file and returns to the empty state.</summary>
        private void CloseFile()
        {
            _lastPath = null;
            _selectedMap = null;
            _mapCount = 0;

            if (_grid != null) _grid.Rows.Clear();
            if (_mapTree != null)
            {
                _mapTree.BeginUpdate();
                _mapTree.Nodes.Clear();
                _mapTree.EndUpdate();
            }
            _report.Clear();
            _hex.SetBytes(new byte[0]);
            ClearInlineMap();

            Text = I18n.T("window_title");
            _infoLabel.Text = I18n.T("info_open_binary");
            _treeHeaderLabel.Text = I18n.T("tree_header_empty");
            _viewerTitleLabel.Text = I18n.T("viewer_title");
            _statusFile.Text = I18n.T("status_no_file");
            _statusBlocks.Text = string.Empty;
            _statusBlocks.ToolTipText = string.Empty;
            _statusChecksum.Text = string.Empty;
            _statusIdentity.Text = string.Empty;
            _statusSymbols.Text = string.Empty;
            UpdateStatusSeparators();
            _reloadButton.Enabled = false;
            _openMapButton.Enabled = false;
            _closeMapButton.Enabled = false;
        }

        private void Inspect(string path)
        {
            // Remember the path so a language switch can re-render the
            // dynamic texts of the loaded file.
            _lastPath = path;
            _selectedMap = null;
            ClearInlineMap();

            _grid.Rows.Clear();
            if (_mapTree != null)
            {
                _mapTree.BeginUpdate();
                _mapTree.Nodes.Clear();
                _mapTree.EndUpdate();
            }
            _report.Clear();
            _treeHeaderLabel.Text = I18n.T("tree_header_empty");
            _reloadButton.Enabled = true;
            _openMapButton.Enabled = true;

            byte[] image;
            try
            {
                image = File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                _infoLabel.Text = I18n.T("info_read_error", ex.Message);
                _statusFile.Text = I18n.T("status_read_error", Path.GetFileName(path));
                _statusBlocks.Text = string.Empty;
                _statusChecksum.Text = string.Empty;
                _statusIdentity.Text = string.Empty;
                _statusSymbols.Text = string.Empty;
                UpdateStatusSeparators();
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

            // The original caption also carries the make and ECU type, which
            // come from partNumberConverter (see frmMain.DetectMaps: the
            // barAdditionalInfo bar is built from info.PartNumber + CarMake +
            // EcuType + ExtractInfo).  A converter failure must not cost us
            // the rest of the identity.
            string make = string.Empty;
            string ecuType = string.Empty;
            try
            {
                partNumberConverter converter = new partNumberConverter();
                ECUInfo ecu = converter.ConvertPartnumber(bosch, image.Length);
                if (ecu != null)
                {
                    make = ecu.CarMake ?? string.Empty;
                    ecuType = ecu.EcuType ?? string.Empty;
                }
            }
            catch
            {
                // Keep the identity from the four Extract* helpers.
            }

            // The original VAGEDCSuite title bar carries the full ECU
            // identity, not just the program name.
            string identity = JoinParts(part, software, bosch, make, ecuType, info);
            if (identityRead)
            {
                string title = JoinParts(Path.GetFileName(path), part, software, bosch, make, ecuType, info);
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
                UpdateStatusBar(path, I18n.T("status_checksum_na"), identity, 0, string.Empty, string.Empty);
                return;
            }

            // One row per data block, with the gearbox family and the code
            // block code read from the file.  The short gearbox sequence
            // (AUT MAN 4WD) goes to the status bar, like the original.
            StringBuilder gearboxSummary = new StringBuilder();
            StringBuilder gearboxTip = new StringBuilder();
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

                string abbreviation;
                if (role == "Automatic") abbreviation = "AUT";
                else if (role == "Manual") abbreviation = block == 3 ? "4WD" : "MAN";
                else abbreviation = "?";

                if (gearboxSummary.Length > 0) gearboxSummary.Append(' ');
                gearboxSummary.Append(abbreviation);

                if (gearboxTip.Length > 0) gearboxTip.Append(", ");
                gearboxTip.Append(I18n.T("block_" + block.ToString())).Append(": ").Append(shown);
            }

            // Checksum: computed vs stored for all three blocks.
            int okFields = EDC15VM256Checksum.CountOkFields(image, out string err);
            _report.AppendText(I18n.T("checksum_report", okFields, EDC15VM256Checksum.BlockCount));
            if (!string.IsNullOrEmpty(err)) _report.AppendText("  (" + err + ")");
            _report.AppendText("\r\n");
            _report.AppendText("\r\n");
            _report.AppendText(EDC15VM256Checksum.Verify(image).Report);
            // Show the report from its first line, not scrolled to the end.
            _report.Select(0, 0);
            _report.ScrollToCaret();

            // Map list (Kennfelder), shown as a Category > Subcategory tree.
            int mapCount = FillMapTree(path);

            string checksum = okFields >= EDC15VM256Checksum.BlockCount
                ? I18n.T("status_checksum_ok")
                : I18n.T("status_checksum_partial", okFields, EDC15VM256Checksum.BlockCount);
            UpdateStatusBar(
                path, checksum, identity, mapCount,
                gearboxSummary.ToString(), gearboxTip.ToString());
        }

        /// <summary>
        /// Sets the status-bar fragments in the original layout:
        /// <c>file | blocks | checksum | identity | N symbols</c>.
        /// </summary>
        private void UpdateStatusBar(
            string path, string checksum, string identity, int symbolCount,
            string gearboxSummary, string gearboxTip)
        {
            if (_statusFile == null) return;
            if (string.IsNullOrEmpty(identity)) identity = "?";
            _statusFile.Text = Path.GetFileName(path);
            _statusBlocks.Text = gearboxSummary ?? string.Empty;
            _statusBlocks.ToolTipText = string.IsNullOrEmpty(gearboxTip)
                ? string.Empty
                : I18n.T("status_blocks_tip", gearboxTip);
            _statusChecksum.Text = checksum;
            _statusIdentity.Text = identity;
            _statusSymbols.Text = I18n.T("status_symbols", symbolCount);
            UpdateStatusSeparators();
        }

        /// <summary>Hides the pipes next to empty status fields.</summary>
        private void UpdateStatusSeparators()
        {
            if (_statusSeparators == null) return;
            _statusSeparators[0].Visible = _statusFile.Text.Length > 0 && _statusBlocks.Text.Length > 0;
            _statusSeparators[1].Visible = _statusBlocks.Text.Length > 0 && _statusChecksum.Text.Length > 0;
            _statusSeparators[2].Visible = _statusChecksum.Text.Length > 0 && _statusIdentity.Text.Length > 0;
            _statusSeparators[3].Visible = _statusIdentity.Text.Length > 0 && _statusSymbols.Text.Length > 0;
        }

        /// <summary>
        /// Fills the map tree from <see cref="MapList.GetMaps"/>: category
        /// nodes ("Detected maps (N)"), subcategory nodes ("Fuel (N)"), and
        /// one leaf per map with its flash address.  Categories and
        /// subcategories are sorted alphabetically, maps by address.  Each
        /// leaf's Tag is its <see cref="MapList.Map"/> record.
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

                Font categoryFont = new Font(_mapTree.Font, FontStyle.Bold);

                for (int c = 0; c < categories.Count; c++)
                {
                    Dictionary<string, List<MapList.Map>> subs = groups[categories[c]];
                    int categoryCount = 0;
                    foreach (List<MapList.Map> entries in subs.Values) categoryCount += entries.Count;

                    TreeNode categoryNode = new TreeNode(
                        I18n.T("tree_category", categories[c], categoryCount));
                    categoryNode.NodeFont = categoryFont;
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
                            tip.Append(label);
                            if (!string.IsNullOrEmpty(map.Description))
                                tip.Append("\r\n").Append(map.Description);
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

            _mapCount = count;
            _treeHeaderLabel.Text = I18n.T("tree_header", count);
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

        /// <summary>Title of the right-hand viewer for one map.</summary>
        private static string MapTitle(MapList.Map map)
        {
            return I18n.T("viewer_title_map", map.Varname, map.Address, map.XLen, map.YLen);
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
