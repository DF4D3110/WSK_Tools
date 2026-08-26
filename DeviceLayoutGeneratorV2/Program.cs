using System;
using System.IO;
using DeviceLayoutGeneratorV2.Builder;

namespace DeviceLayoutGeneratorV2
{
    class Program
    {
        static int Main(string[] args)
        {
            try
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: DeviceLayoutGeneratorV2.exe <DeviceLayout.xml> <output.vhd>");
                    Console.WriteLine("Example: DeviceLayoutGeneratorV2.exe DeviceLayout.xml E:\\output.vhd");
                    return 1;
                }

                string xmlPath = Path.GetFullPath(args[0]);
                string outputPath = Path.GetFullPath(args[1]);

                if (!File.Exists(xmlPath))
                {
                    Console.WriteLine($"ERROR: DeviceLayout.xml not found: {xmlPath}");
                    return 2;
                }

                // Ensure output directory exists
                string outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                    Directory.CreateDirectory(outputDir);

                // Delete existing output
                if (File.Exists(outputPath))
                    File.Delete(outputPath);

                using (var builder = new ImageBuilder(Console.WriteLine))
                {
                    builder.Build(xmlPath, outputPath);
                }

                if (File.Exists(outputPath))
                {
                    var fi = new FileInfo(outputPath);
                    Console.WriteLine($"Output file: {outputPath}");
                    Console.WriteLine($"File size: {fi.Length / 1048576.0:F2} MB");
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"=== FATAL ERROR ===");
                Console.WriteLine($"{ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return 99;
            }
        }
    }
}
