using System;
using System.Windows.Forms;
using Krypton.Ribbon;
using Krypton.Toolkit;
using OxyPlot;
using OxyPlot.Series;
using OxyPlot.WindowsForms;

namespace Reloaded
{
    /// <summary>
    /// Minimal proof-of-concept: a Krypton ribbon (one tab, one group, two
    /// buttons) plus an OxyPlot PlotView with two line series, targeting the
    /// .NET Framework 4.0 (runs on Windows XP SP3 through Windows 11).
    /// </summary>
    public class MainForm : KryptonForm
    {
        private readonly KryptonRibbon _ribbon;
        private readonly PlotView _plotView;

        public MainForm()
        {
            Text = "Reloaded — Krypton ribbon + OxyPlot on .NET 4.0";
            Width = 960;
            Height = 640;

            // --- Krypton ribbon: one tab, one group, two buttons -------------
            _ribbon = new KryptonRibbon { Dock = DockStyle.Top };

            KryptonRibbonTab tab = new KryptonRibbonTab { Text = "Home" };
            KryptonRibbonGroup group = new KryptonRibbonGroup
            {
                TextLine1 = "Actions",
                TextLine2 = "Sample group"
            };

            KryptonRibbonGroupButton button1 = new KryptonRibbonGroupButton
            {
                TextLine1 = "Button 1",
                TextLine2 = "First"
            };
            KryptonRibbonGroupButton button2 = new KryptonRibbonGroupButton
            {
                TextLine1 = "Button 2",
                TextLine2 = "Second"
            };
            button1.Click += (s, e) => MessageBox.Show(this, "Button 1 clicked", "Reloaded");
            button2.Click += (s, e) => MessageBox.Show(this, "Button 2 clicked", "Reloaded");

            // Buttons are KryptonRibbonGroupItem, not KryptonRibbonGroupContainer,
            // so they must be wrapped in a triple (a container) before being added
            // to the group's container collection.
            KryptonRibbonGroupTriple triple = new KryptonRibbonGroupTriple();
            triple.Items.Add(button1);
            triple.Items.Add(button2);
            group.Items.Add(triple);
            tab.Groups.Add(group);
            _ribbon.RibbonTabs.Add(tab);

            // --- OxyPlot PlotView with two line series -----------------------
            _plotView = new PlotView { Dock = DockStyle.Fill };

            PlotModel model = new PlotModel { Title = "Two line series (OxyPlot 1.0.0)" };
            model.Series.Add(MakeSeries("Series A", OxyColors.Blue, 0.0));
            model.Series.Add(MakeSeries("Series B", OxyColors.Red, 1.0));
            _plotView.Model = model;

            Controls.Add(_plotView);
            Controls.Add(_ribbon);
        }

        private static LineSeries MakeSeries(string title, OxyColor color, double phase)
        {
            LineSeries s = new LineSeries { Title = title, Color = color };
            for (int i = 0; i <= 40; i++)
            {
                double x = i / 4.0;
                s.Points.Add(new DataPoint(x, Math.Sin(x + phase)));
            }
            return s;
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
