using System.Text;
using DiscUtils;
using DiscUtils.Ntfs;
using DiscUtils.Fat;
using DiscUtils.Streams;
using DiscUtils.Partitions;
using Img2Ffu.Reader;
using StorageSpace;

namespace FFUInfo;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("FFUInfo - WSK Tools v1.0.4 Preview Build 260826");
            Console.WriteLine("⚠ 测试版本 — 部分功能可能存在无法正常工作");
            Console.WriteLine();
            Console.WriteLine(Lang.Get("Usage"));
            Console.WriteLine(Lang.Get("UsageDesc"));
            Console.WriteLine(Lang.Get("LangOption"));
            Console.WriteLine();
            Console.WriteLine("WinStory 2026 - https://wiki.win-story.cn");
            Console.WriteLine("Compiled by DF4D3110");
            return;
        }

        string path = "";
        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "-l" || args[i] == "-language") && i + 1 < args.Length)
            {
                Lang.SetLanguage(args[i + 1]);
                i++;
            }
            else
            {
                path = args[i];
            }
        }

        if (string.IsNullOrEmpty(path))
        {
            Console.WriteLine(Lang.Get("Usage"));
            return;
        }

        if (!File.Exists(path))
        {
            Console.WriteLine(Lang.Get("FileNotFound") + path);
            return;
        }

        Analyze(path);
    }

    static void Analyze(string path)
    {
        var fi = new FileInfo(path);
        Console.WriteLine("=".PadRight(72, '='));
        Console.WriteLine($"FFU File Analysis: {fi.Name}");
        Console.WriteLine($"File size: {fi.Length:N0} bytes ({fi.Length / 1024.0 / 1024.0 / 1024.0:F2} GB)");
        Console.WriteLine("=".PadRight(72, '='));

        DumpHeader(path);

        try
        {
            int storeCount = FullFlashUpdateReaderStream.GetStoreCount(path);
            Console.WriteLine($"\n--- Stores: {storeCount} ---");

            for (int i = 0; i < storeCount; i++)
            {
                using var stream = new FullFlashUpdateReaderStream(path, (ulong)i);
                Console.WriteLine($"\n=== Store {i} ===");
                Console.WriteLine($"  Device Path:  {stream.DevicePath}");
                Console.WriteLine($"  Disk Size:    {stream.Length:N0} bytes ({stream.Length / 1024.0 / 1024.0 / 1024.0:F2} GB)");
                Console.WriteLine($"  Sector Size:  {stream.SectorSize} bytes");
                Console.WriteLine($"  Min Sectors:  {stream.MinSectorCount}");

                var geometry = Geometry.FromCapacity(stream.Length, stream.SectorSize);
                using var disk = new DiscUtils.Raw.Disk(stream, Ownership.None, geometry);
                var partitions = disk.Partitions;

                if (partitions == null || partitions.Count == 0)
                {
                    Console.WriteLine("  Partitions:   (none / MBR not found)");
                    continue;
                }

                Console.WriteLine($"  Partitions:   {partitions.Count}");

                var gptNames = GetGptPartitionNames(stream, stream.SectorSize);

                Console.WriteLine($"  {"#",-3} {"Name",-24} {"GPT Label",-20} {"Type",-16} {"FS",-8} {"Start LBA",-14} {"Size",-12}");
                Console.WriteLine($"  {"---",-3} {"----",-24} {"---------",-20} {"----",-16} {"--",-8} {"---------",-14} {"----",-12}");

                for (int p = 0; p < partitions.Count; p++)
                {
                    var part = partitions[p];
                    string name = $"Partition {p + 1}";
                    string gptLabel = gptNames.Count > p ? gptNames[p] : "";
                    string type = "Unknown";
                    string fs = "Unknown";

                    try
                    {
                        var guidType = part.GuidType;
                        type = DescribePartitionType(guidType);
                        if (string.IsNullOrEmpty(type))
                            type = guidType.ToString().Substring(0, 8);
                    }
                    catch { }

                    try
                    {
                        using var partStream = part.Open();
                        if (NtfsFileSystem.Detect(partStream))
                            fs = "NTFS";
                        else
                        {
                            partStream.Position = 0;
                            if (FatFileSystem.Detect(partStream))
                                fs = "FAT32";
                        }
                    }
                    catch { }

                    long sizeBytes = part.SectorCount * 512;
                    string sizeStr = FormatSize(sizeBytes);

                    Console.WriteLine($"  {p + 1,-3} {name,-24} {gptLabel,-20} {type,-16} {fs,-8} {part.FirstSector,-14:N0} {sizeStr,-12}");
                }

                for (int p = 0; p < partitions.Count; p++)
                {
                    try
                    {
                        using var ps2 = partitions[p].Open();
                        byte[] sig = new byte[8];
                        if (ps2.Read(sig, 0, 8) >= 7 && Encoding.ASCII.GetString(sig, 0, 7) == "SPACEDB")
                        {
                            string partName = gptNames.Count > p ? gptNames[p] : "Partition " + (p + 1);
                            Console.WriteLine($"\n  --- OSPool detected in partition {p + 1} ({partName}) ---");
                            AnalyzeOSPool(ps2);
                        }
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[ERROR] Failed to parse FFU: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"  Inner: {ex.InnerException.Message}");
        }

        Console.WriteLine("\n" + "=".PadRight(72, '='));
    }

    static void DumpHeader(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] h = new byte[256];
        int n = fs.Read(h, 0, 256);

        Console.WriteLine("\n--- Header (first 128 bytes) ---");
        for (int i = 0; i < 128; i += 16)
        {
            int end = Math.Min(i + 16, n);
            string hex = string.Join(" ", h[i..end].Select(b => b.ToString("X2"))).PadRight(47);
            string ascii = new string(h[i..end].Select(b => b >= 32 && b <= 126 ? (char)b : '.').ToArray());
            Console.WriteLine($"  {i:X8}: {hex} {ascii}");
        }

        string sig19 = Encoding.ASCII.GetString(h, 0, 19).TrimEnd('\0');
        string sig12 = Encoding.ASCII.GetString(h, 4, 12).TrimEnd('\0', ' ');

        Console.WriteLine("\n--- Signature ---");
        Console.WriteLine($"  Bytes 0-18:  \"{sig19}\"");
        Console.WriteLine($"  Bytes 4-15:  \"{sig12}\"");

        if (sig19 == "ImageFlashUpdate")
            Console.WriteLine("  => ImageFlashUpdate (legacy V1 format)");
        else if (sig12 == "SignedImage")
            Console.WriteLine("  => SignedImage (V1/V1.1/V2 format, supports compression & multi-store)");
        else
            Console.WriteLine("  => Unknown signature");

        Console.WriteLine("\n--- Header Fields ---");
        Console.WriteLine($"  0x00 DWORD:  0x{BitConverter.ToUInt32(h, 0):X8}");
        Console.WriteLine($"  0x10 DWORD:  0x{BitConverter.ToUInt32(h, 0x10):X8} ({BitConverter.ToUInt32(h, 0x10)})");
        Console.WriteLine($"  0x14 DWORD:  0x{BitConverter.ToUInt32(h, 0x14):X8} ({BitConverter.ToUInt32(h, 0x14)})");
        Console.WriteLine($"  0x18 DWORD:  0x{BitConverter.ToUInt32(h, 0x18):X8} ({BitConverter.ToUInt32(h, 0x18)})");
        Console.WriteLine($"  0x1C DWORD:  0x{BitConverter.ToUInt32(h, 0x1C):X8} ({BitConverter.ToUInt32(h, 0x1C)})");
    }

    static List<string> GetGptPartitionNames(Stream diskStream, int sectorSize)
    {
        var names = new List<string>();
        try
        {
            diskStream.Position = sectorSize;
            byte[] gptHeader = new byte[92];
            if (diskStream.Read(gptHeader, 0, 92) < 92) return names;
            if (Encoding.ASCII.GetString(gptHeader, 0, 8) != "EFI PART") return names;

            long partTableLba = BitConverter.ToInt64(gptHeader, 72);
            uint numEntries = BitConverter.ToUInt32(gptHeader, 80);
            uint entrySize = BitConverter.ToUInt32(gptHeader, 84);
            if (numEntries == 0 || entrySize < 72) return names;

            long tableOffset = partTableLba * sectorSize;
            for (uint i = 0; i < numEntries; i++)
            {
                diskStream.Position = tableOffset + i * entrySize + 56;
                byte[] nameBytes = new byte[72];
                if (diskStream.Read(nameBytes, 0, 72) < 72) break;
                string name = Encoding.Unicode.GetString(nameBytes).TrimEnd('\0');
                names.Add(name);
            }
        }
        catch { }
        return names;
    }

    static void AnalyzeOSPool(Stream poolStream)
    {
        try
        {
            poolStream.Position = 0;
            var pool = new Pool(poolStream);
            var disks = pool.GetDisks();
            Console.WriteLine($"  Storage Pool: {disks.Count} virtual disks");
            int idx = 0;
            foreach (var kvp in disks)
            {
                string name = kvp.Value;
                using var space = pool.OpenDisk(kvp.Key);
                long size = space.Length;
                double gb = size / 1024.0 / 1024 / 1024;
                bool blockMapped = size > 2L * 1024 * 1024 * 1024;
                Console.WriteLine($"  [{idx}] {name}{(blockMapped ? " (块映射)" : "")}");
                Console.WriteLine($"      Size: {size:N0} bytes ({gb:F2} GB)");
                try
                {
                    space.Position = 0;
                    using var subDisk = new DiscUtils.Raw.Disk(space, Ownership.None);
                    var parts = subDisk.Partitions;
                    if (parts != null)
                    {
                        Console.WriteLine($"      Partitions: {parts.Count}");
                        for (int i = 0; i < Math.Min(parts.Count, 16); i++)
                        {
                            var p = parts[i];
                            string fs = "?";
                            try { using var ps3 = p.Open(); if (NtfsFileSystem.Detect(ps3)) fs = "NTFS"; } catch { }
                            try { using var ps3 = p.Open(); if (FatFileSystem.Detect(ps3)) fs = "FAT32"; } catch { }
                            string pName = "";
                            try { if (parts is GuidPartitionTable gpt && gpt[i] is GuidPartitionInfo gpi) pName = gpi.Name; } catch { }
                            Console.WriteLine($"        {i + 1}. {pName} LBA {p.FirstSector:N0}-{p.FirstSector + p.SectorCount - 1:N0}, {p.SectorCount * 512 / 1024.0 / 1024:F1} MB, {fs}");
                        }
                        if (parts.Count > 16)
                            Console.WriteLine($"        ... and {parts.Count - 16} more partitions");
                    }
                }
                catch (Exception ex) { Console.WriteLine($"      Parse error: {ex.Message}"); }
                idx++;
            }
        }
        catch (Exception ex) { Console.WriteLine($"  OSPool analysis error: {ex.Message}"); }
    }

    static string DescribePartitionType(Guid guid)
    {
        if (guid == new Guid("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7")) return "Basic Data";
        if (guid == new Guid("C12A7328-F81F-11D2-BA4B-00A0C93EC93B")) return "EFI System";
        if (guid == new Guid("E3C9E316-0B5C-4DB8-817D-F92DF00215AE")) return "MS Reserved";
        if (guid == new Guid("DE94BBA4-06D1-4D40-A16A-BFD50179D6AC")) return "Win Recovery";
        if (guid == new Guid("37AFFC90-EF7D-4E96-91C3-2D7AE055B174")) return "Win Recovery";
        if (guid == new Guid("B615F1F5-5088-43CD-809C-A16E52487D00")) return "eMMC User";
        if (guid == new Guid("12C55B20-25D3-41C9-8E06-282D94C676AD")) return "eMMC Boot1";
        if (guid == new Guid("6B76A6DB-0257-48A9-AA99-F6B1655F7B00")) return "eMMC Boot2";
        if (guid == Guid.Empty) return "Unused";
        return "";
    }

    static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }
}
