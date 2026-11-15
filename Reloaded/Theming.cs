using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Win32;
using System.Windows.Forms;
using Krypton.Toolkit;

namespace Reloaded
{
    /// <summary>The four resolved UI themes this app ships.</summary>
    public enum ThemeKind
    {
        ModernLight,
        ModernDark,
        AeroGreen,
        AeroGreenDark
    }

    /// <summary>The two theme families (palette looks).</summary>
    public enum ThemeFamilie
    {
        Modern,
        AeroGreen
    }

    /// <summary>How the light/dark choice is made.</summary>
    public enum ThemeModus
    {
        Auto,   // follow the OS dark/light setting
        Light,
        Dark
    }

    /// <summary>
    /// Reads the OS dark/light preference.  On Windows 10+ the value lives in
    /// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme</c>
    /// (0 = dark, 1 = light).  When the key is absent — Windows XP/7, or Wine
    /// without a Personalize tree — the result is <c>false</c> (light) with no
    /// error and no logging.
    /// </summary>
    public static class OsTheme
    {
        public static bool IsDark()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    if (value is int) return (int)value == 0;
                }
            }
            catch
            {
                // Missing/unreadable key -> light.
            }
            return false;
        }
    }

    /// <summary>
    /// The colour data of one theme.  The drawing code reads from a
    /// <see cref="UiPalette"/> instance (via <see cref="ThemeStore.Active"/>)
    /// instead of branching on <c>if (theme == …)</c>, so a palette is pure
    /// data and adding a theme means adding one instance — never touching the
    /// drawing code.
    /// </summary>
    public sealed class UiPalette
    {
        public readonly ThemeKind Kind;
        public readonly string Name;
        public readonly bool Dark;

        public readonly Color Window;      // Fenster/Fläche
        public readonly Color Card;        // Karte/Panel
        public readonly Color Elevated;    // erhöht / Header
        public readonly Color Input;       // Input-Hintergrund
        public readonly Color Line;        // 1-px-Rahmen
        public readonly Color Text;
        public readonly Color Muted;
        public readonly Color Accent;
        public readonly Color Selection;   // Hover/Auswahl-Fläche
        public readonly Color SelectionText;
        public readonly Color Ribbon;
        public readonly Color RibbonTab;
        public readonly Color GlossTop;    // Aero-Glas-Andeutung (A == 0 = keiner)

        public UiPalette(ThemeKind kind, string name, bool dark,
            Color window, Color card, Color elevated, Color input, Color line,
            Color text, Color muted, Color accent, Color selection, Color selectionText,
            Color ribbon, Color ribbonTab, Color glossTop)
        {
            Kind = kind;
            Name = name;
            Dark = dark;
            Window = window;
            Card = card;
            Elevated = elevated;
            Input = input;
            Line = line;
            Text = text;
            Muted = muted;
            Accent = accent;
            Selection = selection;
            SelectionText = selectionText;
            Ribbon = ribbon;
            RibbonTab = ribbonTab;
            GlossTop = glossTop;
        }
    }

    /// <summary>
    /// Persists the theme choice (family + mode) under
    /// HKCU\Software\VAGEDCSuiteReloaded and exposes the active
    /// <see cref="UiPalette"/> so every control — including the plain-WinForms
    /// map viewer — can follow the theme without a restart.  In <c>Auto</c>
    /// mode the light/dark choice follows <see cref="OsTheme"/>; a manual
    /// <c>Light</c>/<c>Dark</c> choice always wins over the OS setting.
    /// </summary>
    public static class ThemeStore
    {
        private const string KeyPath = @"Software\VAGEDCSuiteReloaded";
        private const string FamilyValue = "ThemeFamily";
        private const string ModeValue = "ThemeMode";

        private static UiPalette _active;
        private static ThemeFamilie _familie = ThemeFamilie.Modern;
        private static ThemeModus _modus = ThemeModus.Auto;
        private static bool _loaded;

        /// <summary>Raised after the resolved palette changed (theme switch).</summary>
        public static event EventHandler Changed;

        /// <summary>The palette of the currently active theme (pure data).</summary>
        public static UiPalette Active
        {
            get { return _active ?? (_active = ThemePalettes.Palette(ResolveKind())); }
        }

        public static ThemeFamilie Familie
        {
            get { EnsureLoaded(); return _familie; }
        }

        public static ThemeModus Modus
        {
            get { EnsureLoaded(); return _modus; }
        }

        /// <summary>The effective theme: family + mode + (Auto ? OS detection).</summary>
        public static ThemeKind ResolveKind()
        {
            EnsureLoaded();
            bool dark = _modus == ThemeModus.Dark ||
                        (_modus == ThemeModus.Auto && OsTheme.IsDark());
            return _familie == ThemeFamilie.AeroGreen
                ? (dark ? ThemeKind.AeroGreenDark : ThemeKind.AeroGreen)
                : (dark ? ThemeKind.ModernDark : ThemeKind.ModernLight);
        }

        /// <summary>Sets both family and mode, persists and re-resolves.</summary>
        public static void Apply(ThemeFamilie family, ThemeModus mode)
        {
            EnsureLoaded();
            if (family == _familie && mode == _modus) return;
            _familie = family;
            _modus = mode;
            Save();
            Refresh();
        }

        public static void SetFamilie(ThemeFamilie family)
        {
            Apply(family, _modus);
        }

        public static void SetModus(ThemeModus mode)
        {
            Apply(_familie, mode);
        }

        /// <summary>
        /// Re-resolves the palette (after an OS dark/light change or a manual
        /// change).  Fires <see cref="Changed"/> only when the resolved theme
        /// actually changed.
        /// </summary>
        public static void Refresh()
        {
            UiPalette next = ThemePalettes.Palette(ResolveKind());
            if (ReferenceEquals(next, _active)) return;
            _active = next;
            EventHandler handler = Changed;
            if (handler != null) handler(null, EventArgs.Empty);
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    if (key == null) return;
                    string fam = key.GetValue(FamilyValue) as string;
                    string mod = key.GetValue(ModeValue) as string;
                    ThemeFamilie f;
                    ThemeModus m;
                    if (!string.IsNullOrEmpty(fam) && Enum.TryParse(fam, true, out f)) _familie = f;
                    if (!string.IsNullOrEmpty(mod) && Enum.TryParse(mod, true, out m)) _modus = m;
                }
            }
            catch
            {
                // Registry unavailable -> defaults (Modern + Auto).
            }
        }

        private static void Save()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    if (key != null)
                    {
                        key.SetValue(FamilyValue, _familie.ToString(), RegistryValueKind.String);
                        key.SetValue(ModeValue, _modus.ToString(), RegistryValueKind.String);
                    }
                }
            }
            catch
            {
                // Best effort; the theme still applies for this session.
            }
        }
    }

    /// <summary>
    /// Builds the <see cref="KryptonPalette"/> instances from the four
    /// <see cref="UiPalette"/> data records.  Everything (ribbon, buttons,
    /// grid, textboxes, labels and the form chrome) is drawn by Krypton from
    /// the palette, so switching themes is a single assignment of
    /// <see cref="KryptonManager.GlobalPalette"/> — no per-control colour
    /// hacks and no <c>if (theme == …)</c> in the drawing code.
    /// </summary>
    public static class ThemePalettes
    {
        // --- the four palettes as pure data ---------------------------------

        private static readonly UiPalette ModernLight = new UiPalette(
            ThemeKind.ModernLight, "Modern light", false,
            Color.FromArgb(245, 246, 248),   // #F5F6F8 Fenster
            Color.White,                     // #FFFFFF Karte
            Color.FromArgb(236, 239, 243),   // #ECEFF3 erhöht
            Color.White,                     // Input
            Color.FromArgb(225, 228, 232),   // #E1E4E8 Rahmen
            Color.FromArgb(31, 35, 40),      // #1F2328
            Color.FromArgb(107, 114, 128),   // #6B7280 sekundär
            Color.FromArgb(47, 111, 235),    // #2F6FEB Akzent
            Color.FromArgb(234, 241, 254),   // #EAF1FE Akzent hell
            Color.FromArgb(31, 35, 40),      // Auswahl-Text
            Color.FromArgb(245, 246, 248),   // Ribbon
            Color.FromArgb(236, 239, 243),   // Ribbon-Tab
            Color.Transparent);              // kein Gloss

        private static readonly UiPalette ModernDark = new UiPalette(
            ThemeKind.ModernDark, "Modern dark", true,
            Color.FromArgb(22, 24, 28),      // #16181C Fenster
            Color.FromArgb(30, 33, 38),      // #1E2126 Karte
            Color.FromArgb(38, 42, 49),      // #262A31 erhöht
            Color.FromArgb(38, 42, 49),      // Input
            Color.FromArgb(52, 58, 66),      // #343A42 Rahmen
            Color.FromArgb(230, 232, 235),   // #E6E8EB
            Color.FromArgb(154, 163, 173),   // #9AA3AD sekundär
            Color.FromArgb(76, 141, 255),    // #4C8DFF Akzent
            Color.FromArgb(46, 74, 115),     // Auswahl
            Color.White,                     // Auswahl-Text
            Color.FromArgb(27, 30, 35),      // #1B1E23 Ribbon
            Color.FromArgb(22, 24, 28),      // Ribbon-Tab
            Color.Transparent);

        private static readonly UiPalette AeroGreen = new UiPalette(
            ThemeKind.AeroGreen, "Win7 Aero green", false,
            Color.FromArgb(234, 244, 234),   // #EAF4EA Fenster
            Color.FromArgb(247, 252, 247),   // #F7FCF7 Karte
            Color.FromArgb(222, 235, 222),   // #DEEBDE erhöht
            Color.White,                     // Input
            Color.FromArgb(169, 201, 169),   // #A9C9A9 Rahmen
            Color.FromArgb(30, 43, 30),      // #1E2B1E
            Color.FromArgb(92, 110, 92),     // #5C6E5C sekundär
            Color.FromArgb(46, 125, 50),     // #2E7D32 Akzent
            Color.FromArgb(214, 235, 214),   // #D6EBD6 Akzent hell
            Color.FromArgb(30, 43, 30),      // Auswahl-Text
            Color.FromArgb(234, 244, 234),   // Ribbon
            Color.FromArgb(222, 235, 222),   // Ribbon-Tab
            Color.FromArgb(102, 255, 255, 255)); // Gloss: Weiß 40 %

        private static readonly UiPalette AeroGreenDark = new UiPalette(
            ThemeKind.AeroGreenDark, "Win7 Aero green dark", true,
            Color.FromArgb(20, 32, 26),      // #14201A Fenster
            Color.FromArgb(27, 43, 34),      // #1B2B22 Karte
            Color.FromArgb(34, 53, 41),      // #223529 erhöht
            Color.FromArgb(34, 53, 41),      // Input
            Color.FromArgb(47, 74, 56),      // #2F4A38 Rahmen
            Color.FromArgb(227, 239, 228),   // #E3EFE4
            Color.FromArgb(157, 179, 161),   // #9DB3A1 sekundär
            Color.FromArgb(102, 187, 106),   // #66BB6A Akzent
            Color.FromArgb(38, 60, 44),      // #263C2C Auswahl
            Color.FromArgb(227, 239, 228),   // Auswahl-Text
            Color.FromArgb(23, 36, 29),      // Ribbon (leicht abgesetzt)
            Color.FromArgb(20, 32, 26),      // Ribbon-Tab
            Color.Transparent);

        /// <summary>The data palette for a theme kind.</summary>
        public static UiPalette Palette(ThemeKind kind)
        {
            switch (kind)
            {
                case ThemeKind.ModernDark: return ModernDark;
                case ThemeKind.AeroGreen: return AeroGreen;
                case ThemeKind.AeroGreenDark: return AeroGreenDark;
                default: return ModernLight;
            }
        }

        /// <summary>All four theme kinds in display order.</summary>
        public static ThemeKind[] AllKinds
        {
            get
            {
                return new ThemeKind[]
                {
                    ThemeKind.ModernLight, ThemeKind.ModernDark,
                    ThemeKind.AeroGreen, ThemeKind.AeroGreenDark
                };
            }
        }

        /// <summary>Builds the <see cref="KryptonPalette"/> for one theme kind.</summary>
        public static KryptonPalette Create(ThemeKind kind)
        {
            return Create(Palette(kind));
        }

        private static KryptonPalette Create(UiPalette c)
        {
            KryptonPalette p = new KryptonPalette();
            p.BasePaletteMode = c.Dark ? PaletteMode.Office2010Black : PaletteMode.SparkleBlue;

            p.FormStyles.FormCommon.StateCommon.Back.Color1 = c.Window;
            p.FormStyles.FormMain.StateCommon.Back.Color1 = c.Window;
            p.PanelStyles.PanelCommon.StateCommon.Color1 = c.Window;
            p.PanelStyles.PanelClient.StateCommon.Color1 = c.Window;

            p.LabelStyles.LabelCommon.StateCommon.ShortText.Color1 = c.Text;
            p.LabelStyles.LabelCommon.StateCommon.LongText.Color1 = c.Text;

            p.InputControlStyles.InputControlCommon.StateCommon.Back.Color1 = c.Input;
            p.InputControlStyles.InputControlCommon.StateCommon.Content.ShortText.Color1 = c.Text;
            p.InputControlStyles.InputControlStandalone.StateCommon.Back.Color1 = c.Input;
            p.InputControlStyles.InputControlStandalone.StateCommon.Content.ShortText.Color1 = c.Text;
            if (c.Dark)
            {
                p.InputControlStyles.InputControlCommon.StateCommon.Border.Color1 = c.Line;
                p.InputControlStyles.InputControlStandalone.StateCommon.Border.Color1 = c.Line;
            }

            // Control client area behind labels and other transparent controls.
            p.ControlStyles.ControlCommon.StateCommon.Back.Color1 = c.Window;
            p.ControlStyles.ControlClient.StateCommon.Back.Color1 = c.Window;
            if (c.Dark) p.ControlStyles.ControlAlternate.StateCommon.Back.Color1 = c.Window;

            ApplyGridScheme(p, c.Card, c.Card, c.Elevated, c.Text, c.Accent,
                            c.Selection, c.SelectionText, c.Line);

            ApplyRibbonScheme(p, c.Ribbon, c.RibbonTab, c.Text, c.Line);

            RoundControls(p, 7f);
            return p;
        }

        /// <summary>
        /// The solid background colour of the form for a given theme.  Used for
        /// controls such as <see cref="KryptonLabel"/> that reject transparent
        /// backgrounds and default their <c>BackColor</c> to SystemColors.Control.
        /// </summary>
        public static Color FormBackground(ThemeKind kind)
        {
            return Palette(kind).Window;
        }

        /// <summary>
        /// Applies one coherent colour set to the DataGridView palette entry.
        /// </summary>
        private static void ApplyGridScheme(
            KryptonPalette p,
            Color background, Color cellBack, Color headerBack,
            Color text, Color accent, Color selection, Color selectionText, Color gridLine)
        {
            foreach (KryptonPaletteGrid grid in new[]
            {
                p.GridStyles.GridCommon, p.GridStyles.GridList, p.GridStyles.GridSheet
            })
            {
                grid.StateCommon.Background.Color1 = background;
                grid.StateCommon.DataCell.Back.Color1 = cellBack;
                grid.StateCommon.DataCell.Border.Color1 = gridLine;
                grid.StateCommon.DataCell.Content.Color1 = text;
                grid.StateCommon.HeaderColumn.Back.Color1 = headerBack;
                grid.StateCommon.HeaderColumn.Content.Color1 = text;
                grid.StateCommon.HeaderRow.Back.Color1 = headerBack;
                grid.StateCommon.HeaderRow.Content.Color1 = text;
                grid.StateSelected.DataCell.Back.Color1 = selection;
                grid.StateSelected.DataCell.Content.Color1 = selectionText;
            }
        }

        /// <summary>
        /// Forces a flat ribbon chrome.  Krypton 5.550's "Black" palette modes
        /// (Office2010Black, Office365Black and their DarkMode variants) all
        /// resolve the ribbon group area to a mid grey (94,94,94), so the
        /// ribbon band must be pinned by hand.
        /// </summary>
        private static void ApplyRibbonScheme(KryptonPalette p, Color groupBg, Color tabBg, Color text, Color line)
        {
            var ribbon = p.Ribbon;

            ribbon.RibbonGroupArea.StateCommon.BackColor1 = groupBg;
            ribbon.RibbonGroupArea.StateCommon.BackColor2 = groupBg;
            ribbon.RibbonGroupArea.StateCommon.BackColor3 = groupBg;
            ribbon.RibbonGroupArea.StateCommon.BackColor4 = groupBg;
            ribbon.RibbonGroupArea.StateCommon.BackColor5 = groupBg;

            PaletteRibbonDouble[] tabs = new PaletteRibbonDouble[]
            {
                ribbon.RibbonTab.StateCommon,
                ribbon.RibbonTab.StateNormal,
                ribbon.RibbonTab.StateTracking,
                ribbon.RibbonTab.StateCheckedNormal,
                ribbon.RibbonTab.StateCheckedTracking
            };
            foreach (PaletteRibbonDouble tab in tabs)
            {
                tab.BackColor1 = tabBg; tab.BackColor2 = tabBg; tab.BackColor3 = tabBg;
                tab.BackColor4 = tabBg; tab.BackColor5 = tabBg;
                tab.TextColor = text;
            }

            ribbon.RibbonGroupNormalTitle.StateCommon.TextColor = text;
            ribbon.RibbonGroupNormalTitle.StateNormal.TextColor = text;
            ribbon.RibbonGroupButtonText.StateCommon.TextColor = text;
            ribbon.RibbonGroupLabelText.StateCommon.TextColor = text;

            ribbon.RibbonGroupNormalBorder.StateCommon.BackColor1 = line;
            ribbon.RibbonGeneral.GroupSeparatorDark = line;
            ribbon.RibbonGeneral.GroupSeparatorLight = line;
        }

        /// <summary>
        /// Control-level corner rounding only (buttons + input controls).
        /// Window corners are deliberately left square: rounding them needs
        /// DWM / Windows 11 and would break Windows XP SP3.
        /// </summary>
        private static void RoundControls(KryptonPalette p, float rounding)
        {
            var buttons = p.ButtonStyles;
            buttons.ButtonCommon.StateCommon.Border.Rounding = rounding;
            buttons.ButtonStandalone.StateCommon.Border.Rounding = rounding;
            buttons.ButtonAlternate.StateCommon.Border.Rounding = rounding;
            buttons.ButtonLowProfile.StateCommon.Border.Rounding = rounding;
            buttons.ButtonForm.StateCommon.Border.Rounding = rounding;
            buttons.ButtonInputControl.StateCommon.Border.Rounding = rounding;

            p.InputControlStyles.InputControlCommon.StateCommon.Border.Rounding = rounding;
            p.InputControlStyles.InputControlStandalone.StateCommon.Border.Rounding = rounding;
        }
    }

    /// <summary>
    /// A <see cref="Panel"/> with rounded corners, a flat 1 px border and a
    /// very soft inner shadow — the "modern card" look used across the viewer
    /// and the dialogs.  The corners are clipped with a <see cref="Region"/>
    /// (so the parent shows through), the control is double-buffered and the
    /// border is anti-aliased on paint.  Purely XP-safe GDI+; no WPF.
    ///
    /// For the Aero-green theme a soft white gloss band is drawn over the top
    /// edge (a glass hint, not real DWM transparency).
    /// </summary>
    public class RoundedPanel : Panel
    {
        private int _radius = 7;
        private Color _border = Color.Transparent;
        private Color _shadow = Color.FromArgb(28, 0, 0, 0);

        public RoundedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
        }

        /// <summary>Corner radius in pixels (6–8 reads as "modern").</summary>
        public int CornerRadius
        {
            get { return _radius; }
            set { _radius = value; UpdateRegion(); Invalidate(); }
        }

        /// <summary>Border colour; <see cref="Color.Transparent"/> = none.</summary>
        public Color BorderColor
        {
            get { return _border; }
            set { _border = value; Invalidate(); }
        }

        /// <summary>Subtle soft shadow tint (low alpha); used along the inner edge.</summary>
        public Color ShadowColor
        {
            get { return _shadow; }
            set { _shadow = value; Invalidate(); }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateRegion();
        }

        private void UpdateRegion()
        {
            if (Width < 2 || Height < 2) return;
            using (GraphicsPath path = Rounded.RoundedRect(Width, Height, _radius))
                Region = new Region(path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Aero-glass hint: a soft white gradient over the top edge.  Data
            // palettes without a gloss colour (A == 0) skip this.
            Color gloss = ThemeStore.Active.GlossTop;
            if (gloss.A != 0)
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    new Rectangle(0, 0, Width, Height),
                    gloss, Color.FromArgb(0, gloss), LinearGradientMode.Vertical))
                using (GraphicsPath path = Rounded.RoundedRect(Width, Height, _radius))
                {
                    g.FillPath(brush, path);
                }
            }

            if (_border.A == 0 && _shadow.A == 0) return;

            // Soft shadow: two low-alpha passes just inside the edge, so the
            // bottom/right read as slightly recessed without a 3D sunken frame.
            if (_shadow.A != 0)
            {
                using (Pen s1 = new Pen(Color.FromArgb(_shadow.A, _shadow), 2f))
                using (Pen s2 = new Pen(Color.FromArgb(_shadow.A / 2, _shadow), 2f))
                {
                    using (GraphicsPath p1 = Rounded.RoundedRect(Width - 2, Height - 2, _radius))
                        g.DrawPath(s1, p1);
                    using (GraphicsPath p2 = Rounded.RoundedRect(Width - 4, Height - 4, Math.Max(0, _radius - 2)))
                        g.DrawPath(s2, p2);
                }
            }

            if (_border.A != 0)
            {
                using (Pen pen = new Pen(_border, 1f))
                using (GraphicsPath path = Rounded.RoundedRect(Width - 1, Height - 1, _radius))
                    g.DrawPath(pen, path);
            }
        }
    }

    /// <summary>
    /// Theme settings dialog: theme family (Modern / Aero green) and light/dark
    /// mode (Auto / Light / Dark).  Choices apply immediately through
    /// <see cref="ThemeStore"/> and are persisted; in Auto mode the light/dark
    /// follows the OS setting (see <see cref="OsTheme"/>).
    /// </summary>
    public class ThemeSettingsForm : KryptonForm
    {
        private readonly KryptonRadioButton _familyModern;
        private readonly KryptonRadioButton _familyAero;
        private readonly KryptonRadioButton _modeAuto;
        private readonly KryptonRadioButton _modeLight;
        private readonly KryptonRadioButton _modeDark;

        public ThemeSettingsForm(KryptonPalette palette)
        {
            Text = L("theme_settings_title", "Theme settings");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(360, 240);

            if (palette != null)
            {
                Palette = palette;
            }

            // Form chrome follows the active palette (like MergeSplitForm): the
            // radios/buttons pick up the global Krypton palette, but the form
            // surface and the plain labels need the theme colours explicitly.
            UiPalette c = ThemeStore.Active;
            try
            {
                StateCommon.Back.Draw = InheritBool.True;
                StateCommon.Back.Color1 = c.Window;
                StateCommon.Back.Color2 = c.Window;
                StateCommon.Back.ColorStyle = PaletteColorStyle.Solid;
            }
            catch
            {
                // A missing palette member must not break the dialog.
            }
            BackColor = c.Window;
            ForeColor = c.Text;

            Label familyLabel = new Label
            {
                Text = L("theme_family", "Theme family"),
                Location = new Point(16, 14),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.75F, FontStyle.Bold),
                BackColor = c.Window,
                ForeColor = c.Text
            };
            Controls.Add(familyLabel);

            _familyModern = new KryptonRadioButton
            {
                Text = L("theme_family_modern", "Modern"),
                Location = new Point(28, 42),
                AutoSize = true,
                Checked = ThemeStore.Familie == ThemeFamilie.Modern
            };
            _familyAero = new KryptonRadioButton
            {
                Text = L("theme_family_aero", "Aero green"),
                Location = new Point(28, 66),
                AutoSize = true,
                Checked = ThemeStore.Familie == ThemeFamilie.AeroGreen
            };
            _familyModern.CheckedChanged += delegate
            {
                if (_familyModern.Checked) ThemeStore.SetFamilie(ThemeFamilie.Modern);
            };
            _familyAero.CheckedChanged += delegate
            {
                if (_familyAero.Checked) ThemeStore.SetFamilie(ThemeFamilie.AeroGreen);
            };
            Controls.Add(_familyModern);
            Controls.Add(_familyAero);

            Label modeLabel = new Label
            {
                Text = L("theme_mode", "Theme mode"),
                Location = new Point(16, 100),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.75F, FontStyle.Bold),
                BackColor = c.Window,
                ForeColor = c.Text
            };
            Controls.Add(modeLabel);

            _modeAuto = new KryptonRadioButton
            {
                Text = L("theme_mode_auto", "Auto (system)"),
                Location = new Point(28, 128),
                AutoSize = true,
                Checked = ThemeStore.Modus == ThemeModus.Auto
            };
            _modeLight = new KryptonRadioButton
            {
                Text = L("theme_mode_light", "Light"),
                Location = new Point(28, 152),
                AutoSize = true,
                Checked = ThemeStore.Modus == ThemeModus.Light
            };
            _modeDark = new KryptonRadioButton
            {
                Text = L("theme_mode_dark", "Dark"),
                Location = new Point(28, 176),
                AutoSize = true,
                Checked = ThemeStore.Modus == ThemeModus.Dark
            };
            _modeAuto.CheckedChanged += delegate { if (_modeAuto.Checked) ThemeStore.SetModus(ThemeModus.Auto); };
            _modeLight.CheckedChanged += delegate { if (_modeLight.Checked) ThemeStore.SetModus(ThemeModus.Light); };
            _modeDark.CheckedChanged += delegate { if (_modeDark.Checked) ThemeStore.SetModus(ThemeModus.Dark); };
            Controls.Add(_modeAuto);
            Controls.Add(_modeLight);
            Controls.Add(_modeDark);

            KryptonButton close = new KryptonButton
            {
                Text = L("theme_close", "Close"),
                Location = new Point(256, 196),
                Size = new Size(88, 26),
                DialogResult = DialogResult.Cancel
            };
            close.Click += delegate { Close(); };
            Controls.Add(close);
            CancelButton = close;
        }

        private static string L(string key, string fallback)
        {
            string value = I18n.T(key);
            return string.IsNullOrEmpty(value) || value == key ? fallback : value;
        }
    }

    /// <summary>GDI+ rounded-corner helpers shared by the rounded controls.</summary>
    public static class Rounded
    {
        /// <summary>Builds a rounded-rectangle path for a w×h rectangle.</summary>
        public static GraphicsPath RoundedRect(int w, int h, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0 || w < radius * 2 || h < radius * 2)
            {
                path.AddRectangle(new Rectangle(0, 0, Math.Max(1, w), Math.Max(1, h)));
                return path;
            }
            int d = radius * 2;
            path.AddArc(0, 0, d, d, 180, 90);
            path.AddArc(w - d, 0, d, d, 270, 90);
            path.AddArc(w - d, h - d, d, d, 0, 90);
            path.AddArc(0, h - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>
        /// Applies a rounded region + flat 1 px border to any control (used for
        /// controls that cannot be replaced by <see cref="RoundedPanel"/>,
        /// such as a DataGridView or a PlotView).  XP-safe: Region only.
        /// </summary>
        public static void Round(Control control, int radius, Color border)
        {
            if (control == null) return;
            control.Region = new Region(RoundedRect(control.Width, control.Height, radius));
            control.Resize += delegate
            {
                control.Region = new Region(RoundedRect(control.Width, control.Height, radius));
            };
            control.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(border, 1f))
                using (GraphicsPath path = RoundedRect(control.Width - 1, control.Height - 1, radius))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.DrawPath(pen, path);
                }
            };
        }
    }
}
