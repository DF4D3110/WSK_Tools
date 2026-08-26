using DeviceLayoutToVhd;

Console.WriteLine("========================================");
Console.WriteLine("  dl2vhd - DeviceLayout to VHD/VHDX");
Console.WriteLine("  WSK Tools v1.0.4 Preview Build 260826");
Console.WriteLine("  ⚠ 测试版本 — 部分功能可能存在无法正常工作");
Console.WriteLine("  WinStory 2026 - Compiled by DF4D3110");
Console.WriteLine("========================================");
Console.WriteLine();

if (args.Length < 1)
{
    PrintUsage();
    return;
}

var xmlPath = args[0];
if (!File.Exists(xmlPath))
{
    Console.WriteLine($"ERROR: File not found: {xmlPath}");
    return;
}

var outputPath = "";
var format = VhdFormat.Vhdx;
var multiStore = false;

for (int i = 1; i < args.Length; i++)
{
    switch (args[i].ToLowerInvariant())
    {
        case "-o":
        case "--output":
            if (i + 1 < args.Length)
                outputPath = args[++i];
            break;
        case "--vhd":
            format = VhdFormat.Vhd;
            break;
        case "--vhdx":
            format = VhdFormat.Vhdx;
            break;
        case "--multi":
            multiStore = true;
            break;
        case "-h":
        case "--help":
            PrintUsage();
            return;
    }
}

if (string.IsNullOrEmpty(outputPath))
{
    var ext = format == VhdFormat.Vhdx ? ".vhdx" : ".vhd";
    outputPath = Path.Combine(Directory.GetCurrentDirectory(), $"disk{ext}");
}

var outputDir = Path.GetDirectoryName(outputPath);
if (!string.IsNullOrEmpty(outputDir))
    Directory.CreateDirectory(outputDir);

Console.WriteLine($"Input:  {xmlPath}");
Console.WriteLine($"Output: {outputPath}");
Console.WriteLine($"Format: {format}");
Console.WriteLine($"Mode:   {(multiStore ? "multi-store (separate disks)" : "single disk with OSPool")}");
Console.WriteLine();

try
{
    Console.WriteLine("Parsing DeviceLayout XML...");
    var layout = DeviceLayoutParser.Parse(xmlPath);
    Console.WriteLine($"  Sector size: {layout.SectorSize}");
    Console.WriteLine($"  Stores:      {layout.Stores.Count}");
    Console.WriteLine($"  StoragePools:{layout.StoragePools.Count}");
    Console.WriteLine();

    if (multiStore)
    {
        Console.WriteLine("ERROR: Multi-store mode not implemented in this version");
        return;
    }

    var path = VhdCreator.CreateSingleDisk(layout, outputPath, format);

    Console.WriteLine();
    Console.WriteLine("========================================");
    if (!string.IsNullOrEmpty(path))
    {
        var fi = new FileInfo(path);
        Console.WriteLine($"  Created: {fi.Name}");
        Console.WriteLine($"  Size:    {fi.Length / 1024.0 / 1024.0:F1} MB (dynamic)");
    }
    Console.WriteLine("========================================");
}
catch (Exception ex)
{
    Console.WriteLine($"ERROR: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}

static void PrintUsage()
{
    Console.WriteLine("Usage: dl2vhd <DeviceLayout.xml> [options]");
    Console.WriteLine();
    Console.WriteLine("Creates a single virtual disk containing all physical partitions");
    Console.WriteLine("and an OSPool partition with embedded virtual disk files.");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  -o, --output <file>   Output VHD/VHDX file path (default: disk.vhdx)");
    Console.WriteLine("  --vhd                  Create VHD format");
    Console.WriteLine("  --vhdx                 Create VHDX format (default)");
    Console.WriteLine("  -h, --help             Show this help");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  dl2vhd DeviceLayout.xml -o C:\\disks\\talkman.vhdx");
    Console.WriteLine("  dl2vhd DeviceLayout.xml --vhd -o disk.vhd");
}
