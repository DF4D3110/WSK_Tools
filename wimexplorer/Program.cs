using System;
using System.Windows.Forms;

namespace WimExplorer;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var form = new MainForm();
        if (args.Length > 0)
            form.OpenWimFile(args[0]);
        Application.Run(form);
    }
}
