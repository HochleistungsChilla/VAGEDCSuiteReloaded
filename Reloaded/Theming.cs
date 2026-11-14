using System;
using System.Drawing;
using Microsoft.Win32;
using Krypton.Toolkit;

namespace Reloaded
{
    /// <summary>The two UI themes this sample ships.</summary>
    public enum ThemeKind
    {
        Light,
        Dark
    }

    /// <summary>
    /// Persists the theme choice under HKCU\Software\VAGEDCSuiteReloaded.
    /// If the registry is unavailable (or the write fails) the app silently
    /// falls back to the light theme — the choice simply is not remembered.
    /// </summary>
    public static class ThemeStore
    {
        private const string KeyPath = @"Software\VAGEDCSuiteReloaded";
        private const string ValueName = "Theme";

        public static ThemeKind Load()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    string value = key == null ? null : key.GetValue(ValueName) as string;
                    if (string.Equals(value, "Dark", StringComparison.OrdinalIgnoreCase)) return ThemeKind.Dark;
                    if (string.Equals(value, "Light", StringComparison.OrdinalIgnoreCase)) return ThemeKind.Light;
                }
            }
            catch
            {
                // Registry unavailable -> default below.
            }
            return ThemeKind.Light;
        }

        public static void Save(ThemeKind kind)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    key.SetValue(ValueName, kind == ThemeKind.Dark ? "Dark" : "Light", RegistryValueKind.String);
                }
            }
            catch
            {
                // Best effort; the theme still applies for this session.
            }
        }
    }

    /// <summary>
    /// Builds the two <see cref="KryptonPalette"/> instances and keeps them
    /// alive.  Everything (ribbon, buttons, grid, textboxes, labels and the
    /// form chrome) is drawn by Krypton from the palette, so switching themes
    /// is a single assignment of <see cref="KryptonManager.GlobalPalette"/> —
    /// no per-control colour hacks.
    /// </summary>
    public static class ThemePalettes
    {
        // --- dark scheme ------------------------------------------------------
        private static readonly Color DarkBg        = Color.FromArgb(30, 30, 30);    // #1E1E1E
        private static readonly Color DarkSurface   = Color.FromArgb(37, 37, 38);    // #252526
        private static readonly Color DarkHeader    = Color.FromArgb(51, 51, 55);    // #333337
        private static readonly Color DarkInput     = Color.FromArgb(45, 45, 45);    // #2D2D2D
        private static readonly Color DarkLine      = Color.FromArgb(60, 60, 60);    // #3C3C3C
        private static readonly Color DarkBorder    = Color.FromArgb(74, 74, 74);    // #4A4A4A
        private static readonly Color DarkText      = Color.FromArgb(230, 230, 230); // #E6E6E6
        private static readonly Color DarkAccent    = Color.FromArgb(45, 127, 193);  // #2D7FC1
        private static readonly Color DarkSelection = Color.FromArgb(38, 79, 120);   // #264F78
        private static readonly Color DarkRibbon    = Color.FromArgb(43, 43, 43);    // #2B2B2B
        private static readonly Color DarkRibbonTab = Color.FromArgb(35, 35, 35);    // #232323

        // --- light scheme -----------------------------------------------------
        private static readonly Color LightBg        = Color.FromArgb(246, 246, 246); // #F6F6F6
        private static readonly Color LightSurface   = Color.White;
        private static readonly Color LightHeader    = Color.FromArgb(232, 232, 234); // #E8E8EA
        private static readonly Color LightLine      = Color.FromArgb(216, 216, 218); // #D8D8DA
        private static readonly Color LightText      = Color.FromArgb(34, 34, 34);    // #222222
        private static readonly Color LightAccent    = Color.FromArgb(45, 127, 193);  // #2D7FC1
        private static readonly Color LightSelection = Color.FromArgb(205, 226, 244); // #CDE2F4

        /// <summary>Light theme: Sparkle (Office 2013) base — the modern, rounded look.</summary>
        public static KryptonPalette CreateLight()
        {
            KryptonPalette p = new KryptonPalette();
            p.BasePaletteMode = PaletteMode.SparkleBlue;

            p.FormStyles.FormCommon.StateCommon.Back.Color1 = LightBg;
            p.FormStyles.FormMain.StateCommon.Back.Color1 = LightBg;
            p.PanelStyles.PanelCommon.StateCommon.Color1 = LightBg;
            p.PanelStyles.PanelClient.StateCommon.Color1 = LightBg;

            p.LabelStyles.LabelCommon.StateCommon.ShortText.Color1 = LightText;
            p.LabelStyles.LabelCommon.StateCommon.LongText.Color1 = LightText;

            p.InputControlStyles.InputControlCommon.StateCommon.Back.Color1 = LightSurface;
            p.InputControlStyles.InputControlCommon.StateCommon.Content.ShortText.Color1 = LightText;
            p.InputControlStyles.InputControlStandalone.StateCommon.Back.Color1 = LightSurface;
            p.InputControlStyles.InputControlStandalone.StateCommon.Content.ShortText.Color1 = LightText;

            // Control client area behind labels and other transparent controls.
            p.ControlStyles.ControlCommon.StateCommon.Back.Color1 = LightBg;
            p.ControlStyles.ControlClient.StateCommon.Back.Color1 = LightBg;

            ApplyGridScheme(p, LightSurface, LightSurface, LightHeader, LightText, LightAccent, LightSelection, Color.White, LightLine);

            RoundControls(p, 5f);
            return p;
        }

        /// <summary>Dark theme: Office2010Black base, tuned to a coherent dark scheme.</summary>
        public static KryptonPalette CreateDark()
        {
            KryptonPalette p = new KryptonPalette();
            p.BasePaletteMode = PaletteMode.Office2010Black;

            p.FormStyles.FormCommon.StateCommon.Back.Color1 = DarkBg;
            p.FormStyles.FormMain.StateCommon.Back.Color1 = DarkBg;
            p.PanelStyles.PanelCommon.StateCommon.Color1 = DarkBg;
            p.PanelStyles.PanelClient.StateCommon.Color1 = DarkBg;

            p.LabelStyles.LabelCommon.StateCommon.ShortText.Color1 = DarkText;
            p.LabelStyles.LabelCommon.StateCommon.LongText.Color1 = DarkText;

            p.InputControlStyles.InputControlCommon.StateCommon.Back.Color1 = DarkInput;
            p.InputControlStyles.InputControlCommon.StateCommon.Border.Color1 = DarkBorder;
            p.InputControlStyles.InputControlCommon.StateCommon.Content.ShortText.Color1 = DarkText;
            p.InputControlStyles.InputControlStandalone.StateCommon.Back.Color1 = DarkInput;
            p.InputControlStyles.InputControlStandalone.StateCommon.Border.Color1 = DarkBorder;
            p.InputControlStyles.InputControlStandalone.StateCommon.Content.ShortText.Color1 = DarkText;

            // Control client area behind labels and other transparent controls.
            p.ControlStyles.ControlCommon.StateCommon.Back.Color1 = DarkBg;
            p.ControlStyles.ControlClient.StateCommon.Back.Color1 = DarkBg;
            p.ControlStyles.ControlAlternate.StateCommon.Back.Color1 = DarkBg;

            ApplyGridScheme(p, DarkSurface, DarkSurface, DarkHeader, DarkText, DarkAccent, DarkSelection, Color.White, DarkLine);

            // Office2010Black's ribbon body is grey, not dark (verified against the
            // assembly: RibbonGroupArea BackColor1 = 94,94,94 for every "Black" mode).
            // Force a coherent dark chrome explicitly.
            ApplyRibbonScheme(p, DarkRibbon, DarkRibbonTab, DarkText, DarkLine);

            RoundControls(p, 5f);
            return p;
        }

        /// <summary>
        /// The solid background colour of the form for a given theme.  Used for
        /// controls such as <see cref="KryptonLabel"/> that reject transparent
        /// backgrounds and default their <c>BackColor</c> to SystemColors.Control.
        /// </summary>
        public static Color FormBackground(ThemeKind kind)
        {
            return kind == ThemeKind.Dark ? DarkBg : LightBg;
        }

        /// <summary>
        /// Applies one coherent colour set to the DataGridView palette entry.
        /// <paramref name="list"/> and <paramref name="sheet"/> are themed the
        /// same way; the sample only uses the List style but the shared
        /// <c>GridCommon</c> entry is tuned too so every grid stays coherent.
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
        /// Forces a dark ribbon chrome.  Krypton 5.550's "Black" palette modes
        /// (Office2010Black, Office365Black and their DarkMode variants) all
        /// resolve the ribbon group area to a mid grey (94,94,94), so a dark
        /// ribbon must be set here by hand.
        /// </summary>
        private static void ApplyRibbonScheme(KryptonPalette p, Color groupBg, Color tabBg, Color text, Color line)
        {
            var ribbon = p.Ribbon;

            // Group content area (the big body of the ribbon).
            ribbon.RibbonGroupArea.StateCommon.BackColor1 = groupBg;
            ribbon.RibbonGroupArea.StateCommon.BackColor2 = groupBg;
            ribbon.RibbonGroupArea.StateCommon.BackColor3 = groupBg;
            ribbon.RibbonGroupArea.StateCommon.BackColor4 = groupBg;
            ribbon.RibbonGroupArea.StateCommon.BackColor5 = groupBg;

            // Tab strip (all states) + tab text.
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

            // Group titles + button/label text.
            ribbon.RibbonGroupNormalTitle.StateCommon.TextColor = text;
            ribbon.RibbonGroupNormalTitle.StateNormal.TextColor = text;
            ribbon.RibbonGroupButtonText.StateCommon.TextColor = text;
            ribbon.RibbonGroupLabelText.StateCommon.TextColor = text;

            // Group border + separators.
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
}
