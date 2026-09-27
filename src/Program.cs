using System;
using System.Windows.Forms;

namespace PZLauncher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try { L.Load(AppConfig.Load().Lang); } catch { L.Load("en"); }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
