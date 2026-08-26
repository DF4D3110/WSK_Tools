using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace VirtualDiskExplorer;

internal static class Program
{
    private const string AppName = "virtualdiskexplorer";

    [STAThread]
    static void Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
        ApplicationConfiguration.Initialize();
        var form = new MainForm();
        if (args.Length > 0 && File.Exists(args[0]))
        {
            form.Load += (s, e) => form.OpenDiskFilePublic(args[0]);
        }
        Application.Run(form);
    }

    private static Assembly? CurrentDomain_AssemblyResolve(object? sender, ResolveEventArgs args)
    {
        try
        {
            var asmName = new AssemblyName(args.Name).Name;
            if (asmName == null) return null;
            var appDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (appDir == null) return null;
            var renamedPath = Path.Combine(appDir, $"{AppName}.{asmName}.dll");
            if (File.Exists(renamedPath))
            {
                return Assembly.LoadFrom(renamedPath);
            }
        }
        catch { }
        return null;
    }
}
