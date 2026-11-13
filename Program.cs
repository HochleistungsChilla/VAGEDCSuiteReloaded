using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace VAGSuite
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            DevExpress.UserSkins.OfficeSkins.Register();
            DevExpress.UserSkins.BonusSkins.Register();
            frmMain mainForm = new frmMain();
            // Optional: open the file directly at startup (same path as the
            // "Open file" button). Makes tests and shortcuts easier.
            if (args.Length > 0 && System.IO.File.Exists(args[0]))
            {
                string startupFile = args[0];
                mainForm.StartupFile = startupFile;
                mainForm.Shown += delegate { mainForm.OpenFileFromCommandLine(startupFile); };
            }
            Application.Run(mainForm);
        }
    }
}
