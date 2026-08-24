using System.Text;
using DiscUtils;
using DiscUtils.Streams;
using DiscUtils.Ntfs;
using DiscUtils.Fat;
using DiscUtils.Vhdx;
using Img2Ffu.Reader;
using StorageSpace;
using StorageSpace.Data;
using StorageSpace.Data.Subtypes;

namespace ExtTools;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return;
        }

        string cmd = "";
        string path = "";
        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "-l" || args[i] == "-language") && i + 1 < args.Length)
            {
                Lang.SetLanguage(args[i + 1]);
                i++;
            }
            else if (string.IsNullOrEmpty(cmd))
            {
                cmd = args[i].ToLower();
            }
            else
            {
                path = args[i];
            }
        }

        if (string.IsNullOrEmpty(cmd))
        {
            PrintUsage();
            return;
        }

        if (!File.Exists(path))
        {
            Console.WriteLine(Lang.Get("FileNotFound") + path);
            return;
        }

        switch (cmd)
        {
            case "broadscan": BroadScan(path); break;
            case "btreedump": BTreeDump(path); break;
            case "ospooldiag": OSPoolDiag(path); break;
            case "ospooldump": OSPoolDump(path); break;
            case "ospoolscan": OSPoolScan(path); break;
            case "ospoolpartdiag": OSPoolPartDiag(path); break;
            case "vhxddump": VhdxDump(path); break;
            case "diag2": Diag2(path); break;
            default: Console.WriteLine($"Unknown command: {cmd}"); break;
        }
    }

    static void PrintUsage()
    {
        Console.WriteLine("exttools - " + Lang.Get("UsageDesc").Replace("FFU", "FFU/VHDX"));
        Console.WriteLine(Lang.Get("Usage").Replace("ffuinfo", "exttools <command>").Replace("<ffu文件路径>", "<file_path>"));
        Console.WriteLine("Commands:");
        Console.WriteLine("  broadscan       - Broad signature scan (GPT/NTFS/FAT/SPACEDB/SDBB)");
        Console.WriteLine("  btreedump       - Dump all SDBB B-tree entries in OSPool");
        Console.WriteLine("  ospooldiag      - OSPool structure diagnostic");
        Console.WriteLine("  ospooldump      - Full OSPool dump (all virtual disks + partitions)");
        Console.WriteLine("  ospoolscan      - Scan all OSPool partitions in FFU");
        Console.WriteLine("  ospoolpartdiag  - OSPool virtual disk partition diagnostic");
        Console.WriteLine("  vhxddump        - VHDX file header and metadata dump");
        Console.WriteLine("  diag2           - General FFU diagnostic (header/stores/partitions)");
        Console.WriteLine(Lang.Get("LangOption"));
        Console.WriteLine();
        Console.WriteLine("WinStory 2026 - https://wiki.win-story.cn");
    }

    static void BroadScan(string path)
    {
        Console.WriteLine("\n=== Broad Signature Scan ===");
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        var sigs = new Dictionary<string, byte[]> {
            ["EFI PART"] = Encoding.ASCII.GetBytes("EFI PART"),
            ["NTFS"] = Encoding.ASCII.GetBytes("NTFS    "),
            ["SPACEDB"] = Encoding.ASCII.GetBytes("SPACEDB"),
            ["SDBB"] = Encoding.ASCII.GetBytes("SDBB"),
            ["SDBC"] = Encoding.ASCII.GetBytes("SDBC"),
        };
        var counts = new Dictionary<string, long>();
        foreach (var k in sigs.Keys) counts[k] = 0;

        byte[] buf = new byte[1 << 20];
        for (long offset = 0; offset < fs.Length - 8; offset += buf.Length - 16)
        {
            fs.Position = offset;
            int read = fs.Read(buf, 0, (int)Math.Min(buf.Length, fs.Length - offset));
            for (int i = 0; i < read - 8; i++)
            {
                foreach (var kvp in sigs)
                {
                    byte[] sig = kvp.Value;
                    if (i + sig.Length > read) continue;
                    bool match = true;
                    for (int j = 0; j < sig.Length; j++)
                        if (buf[i + j] != sig[j]) { match = false; break; }
                    if (match)
                    {
                        long pos = offset + i;
                        if (counts[kvp.Key] < 30)
                            Console.WriteLine($"  [{kvp.Key}] at 0x{pos:X} ({pos:N0}, sector {pos / 512:N0})");
                        counts[kvp.Key]++;
                        i += sig.Length - 1;
                        break;
                    }
                }
            }
        }
        Console.WriteLine("\n--- Summary ---");
        foreach (var kvp in counts)
            Console.WriteLine($"  {kvp.Key}: {kvp.Value:N0} occurrences");
    }

    static void BTreeDump(string path)
    {
        Console.WriteLine("\n=== SDBB B-tree Dump ===");
        try
        {
            int storeCount = FullFlashUpdateReaderStream.GetStoreCount(path);
            for (uint store = 0; store < storeCount; store++)
            {
                using var reader = new FullFlashUpdateReaderStream(path, store);
                var disk = new DiscUtils.Raw.Disk(reader, Ownership.None);
                var parts = disk.Partitions;
                for (int i = 0; i < (parts?.Count ?? 0); i++)
                {
                    var p = parts[i];
                    string guidStr = p.GuidType.ToString("N").ToLower();
                    if (!guidStr.StartsWith("e75caf8f")) continue;
                    Console.WriteLine($"\nStore {store}, Partition {i} (OSPool):");
                    using var ps = p.Open();
                    ps.Position = 0;
                    var pool = new Pool(ps);
                    Console.WriteLine($"  SDBB entries: {pool.SDBBs.Count}");
                    Console.WriteLine($"  Volumes: {pool.SDBBVolumes.Count}");
                    Console.WriteLine($"  Slab allocations: {pool.SDBBSlabAllocation.Count}");
                    Console.WriteLine($"  Physical disks: {pool.SDBBPhysicalDisks.Count}");
                    Console.WriteLine($"  Storage pools: {pool.SDBBStorageInformation.Count}");
                    int idx = 0;
                    foreach (var entry in pool.SDBBs)
                    {
                        Console.WriteLine($"  [{idx}] Sig=\"{entry.Signature}\" BlockPos=0x{entry.CurrentSDBBBlockPosition:X} ParentIdx={entry.ParentSDBBIndex} ChainIdx={entry.CurrentSDBBBlockIndex} ChainCount={entry.CurrentSDBBBlockCount} DataLen={entry.Data.Length}");
                        idx++;
                    }
                    Console.WriteLine("\n  --- Volumes ---");
                    foreach (var vol in pool.SDBBVolumes)
                    {
                        Console.WriteLine($"    Vol#{vol.VolumeNumber} Name=\"{vol.Name}\" GUID={vol.VolumeGUID} BlockNumber={vol.VolumeBlockNumber} Provisioning={vol.ProvisioningType} Copies={vol.NumberOfCopies} Clusters={vol.NumberOfClusters}");
                    }
                    Console.WriteLine("\n  --- Slab Allocations (first 50) ---");
                    int sidx = 0;
                    foreach (var slab in pool.SDBBSlabAllocation)
                    {
                        if (sidx >= 50) { Console.WriteLine($"    ... and {pool.SDBBSlabAllocation.Count - 50} more"); break; }
                        Console.WriteLine($"    [{sidx}] VolID={slab.VolumeID} VolBlock={slab.VolumeBlockNumber} PhysDiskID={slab.PhysicalDiskID} PhysBlock={slab.PhysicalDiskBlockNumber}");
                        sidx++;
                    }
                }
            }
        }
        catch (Exception ex) { Console.WriteLine($"Error: {ex.Message}"); }
    }

    static void OSPoolDiag(string path)
    {
        Console.WriteLine("\n=== OSPool Structure Diagnostic ===");
        try
        {
            int storeCount = FullFlashUpdateReaderStream.GetStoreCount(path);
            for (uint store = 0; store < storeCount; store++)
            {
                using var reader = new FullFlashUpdateReaderStream(path, store);
                var disk = new DiscUtils.Raw.Disk(reader, Ownership.None);
                var parts = disk.Partitions;
                for (int i = 0; i < (parts?.Count ?? 0); i++)
                {
                    var p = parts[i];
                    string guidStr = p.GuidType.ToString("N").ToLower();
                    if (!guidStr.StartsWith("e75caf8f")) continue;
                    Console.WriteLine($"\nStore {store}, Partition {i}:");
                    Console.WriteLine($"  LBA: {p.FirstSector:N0} - {p.FirstSector + p.SectorCount - 1:N0}");
                    Console.WriteLine($"  Size: {p.SectorCount * 512:N0} bytes ({p.SectorCount * 512 / 1024.0 / 1024 / 1024:F2} GB)");
                    using var ps = p.Open();
                    ps.Position = 0;
                    byte[] hdr = new byte[512];
                    ps.Read(hdr, 0, 512);
                    string sig = Encoding.ASCII.GetString(hdr, 0, 7);
                    Console.WriteLine($"  Signature: \"{sig}\"");
                    if (sig == "SPACEDB")
                    {
                        ps.Position = 0;
                        var pool = new Pool(ps);
                        Console.WriteLine($"  SDBB entries: {pool.SDBBs.Count}");
                        Console.WriteLine($"  Volumes: {pool.SDBBVolumes.Count}");
                        Console.WriteLine($"  Slab allocations: {pool.SDBBSlabAllocation.Count}");
                        Console.WriteLine($"  Physical disks: {pool.SDBBPhysicalDisks.Count}");
                        Console.WriteLine($"  Storage pools: {pool.SDBBStorageInformation.Count}");
                        var disks = pool.GetDisks();
                        Console.WriteLine($"  Virtual disks: {disks.Count}");
                        foreach (var kvp in disks)
                            Console.WriteLine($"    [{kvp.Key}] {kvp.Value}");
                    }
                }
            }
        }
        catch (Exception ex) { Console.WriteLine($"Error: {ex.Message}"); }
    }

    static void OSPoolDump(string path)
    {
        Console.WriteLine("\n=== Full OSPool Dump ===");
        try
        {
            int storeCount = FullFlashUpdateReaderStream.GetStoreCount(path);
            for (uint store = 0; store < storeCount; store++)
            {
                using var reader = new FullFlashUpdateReaderStream(path, store);
                var disk = new DiscUtils.Raw.Disk(reader, Ownership.None);
                var parts = disk.Partitions;
                for (int i = 0; i < (parts?.Count ?? 0); i++)
                {
                    var p = parts[i];
                    string guidStr = p.GuidType.ToString("N").ToLower();
                    if (!guidStr.StartsWith("e75caf8f")) continue;
                    Console.WriteLine($"\n=== Store {store}, OSPool Partition {i} ===");
                    using var ps = p.Open();
                    ps.Position = 0;
                    var pool = new Pool(ps);
                    var disks = pool.GetDisks();
                    foreach (var kvp in disks)
                    {
                        Console.WriteLine($"\n--- Virtual Disk [{kvp.Key}] {kvp.Value} ---");
                        using var space = pool.OpenDisk(kvp.Key);
                        Console.WriteLine($"  Size: {space.Length:N0} bytes ({space.Length / 1024.0 / 1024 / 1024:F2} GB)");
                        space.Position = 0;
                        try
                        {
                            var vdisk = new DiscUtils.Raw.Disk(space, Ownership.None);
                            var vparts = vdisk.Partitions;
                            Console.WriteLine($"  Partitions: {vparts?.Count ?? 0}");
                            for (int j = 0; j < (vparts?.Count ?? 0); j++)
                            {
                                var vp = vparts[j];
                                string vpName = "";
                                try { if (vparts is DiscUtils.Partitions.GuidPartitionTable g && g[j] is DiscUtils.Partitions.GuidPartitionInfo gi) vpName = gi.Name; } catch { }
                                string fs = "?";
                                try { using var s = vp.Open(); if (NtfsFileSystem.Detect(s)) fs = "NTFS"; } catch { }
                                try { using var s = vp.Open(); if (FatFileSystem.Detect(s)) fs = "FAT32"; } catch { }
                                Console.WriteLine($"    [{j}] '{vpName}' Type={vp.GuidType} LBA {vp.FirstSector:N0}-{vp.FirstSector + vp.SectorCount - 1:N0} ({vp.SectorCount * 512 / 1024.0 / 1024:F1} MB) {fs}");
                            }
                        }
                        catch (Exception ex) { Console.WriteLine($"  Parse error: {ex.Message}"); }
                    }
                }
            }
        }
        catch (Exception ex) { Console.WriteLine($"Error: {ex.Message}"); }
    }

    static void OSPoolScan(string path)
    {
        Console.WriteLine("\n=== OSPool Partition Scan ===");
        try
        {
            int storeCount = FullFlashUpdateReaderStream.GetStoreCount(path);
            Console.WriteLine($"Stores: {storeCount}");
            for (uint store = 0; store < storeCount; store++)
            {
                using var reader = new FullFlashUpdateReaderStream(path, store);
                Console.WriteLine($"\nStore {store}: Length={reader.Length:N0} SectorSize={reader.SectorSize}");
                var disk = new DiscUtils.Raw.Disk(reader, Ownership.None);
                var parts = disk.Partitions;
                for (int i = 0; i < (parts?.Count ?? 0); i++)
                {
                    var p = parts[i];
                    string guidStr = p.GuidType.ToString("N").ToLower();
                    string pName = "";
                    try { if (parts is DiscUtils.Partitions.GuidPartitionTable g && g[i] is DiscUtils.Partitions.GuidPartitionInfo gi) pName = gi.Name; } catch { }
                    bool isOSPool = guidStr.StartsWith("e75caf8f");
                    Console.WriteLine($"  [{i}] '{pName}' Type={p.GuidType} LBA {p.FirstSector:N0} {(isOSPool ? "<= OSPOOL" : "")}");
                }
            }
        }
        catch (Exception ex) { Console.WriteLine($"Error: {ex.Message}"); }
    }

    static void OSPoolPartDiag(string path)
    {
        Console.WriteLine("\n=== OSPool Virtual Disk Partition Diagnostic ===");
        try
        {
            int storeCount = FullFlashUpdateReaderStream.GetStoreCount(path);
            for (uint store = 0; store < storeCount; store++)
            {
                using var reader = new FullFlashUpdateReaderStream(path, store);
                var disk = new DiscUtils.Raw.Disk(reader, Ownership.None);
                var parts = disk.Partitions;
                for (int i = 0; i < (parts?.Count ?? 0); i++)
                {
                    var p = parts[i];
                    string guidStr = p.GuidType.ToString("N").ToLower();
                    if (!guidStr.StartsWith("e75caf8f")) continue;
                    using var ps = p.Open();
                    ps.Position = 0;
                    var pool = new Pool(ps);
                    var disks = pool.GetDisks();
                    foreach (var kvp in disks)
                    {
                        Console.WriteLine($"\n--- Disk [{kvp.Key}] {kvp.Value} ---");
                        using var space = pool.OpenDisk(kvp.Key);
                        space.Position = 0;
                        byte[] boot = new byte[512];
                        space.Read(boot, 0, 512);
                        Console.WriteLine($"  Boot sector: sig=0x{boot[510]:X2}{boot[511]:X2}");
                        try
                        {
                            var vdisk = new DiscUtils.Raw.Disk(space, Ownership.None);
                            var vparts = vdisk.Partitions;
                            for (int j = 0; j < (vparts?.Count ?? 0); j++)
                            {
                                var vp = vparts[j];
                                string vpName = "";
                                try { if (vparts is DiscUtils.Partitions.GuidPartitionTable g && g[j] is DiscUtils.Partitions.GuidPartitionInfo gi) vpName = gi.Name; } catch { }
                                Console.WriteLine($"  Partition [{j}] '{vpName}':");
                                Console.WriteLine($"    Type GUID: {vp.GuidType}");
                                Console.WriteLine($"    First LBA: {vp.FirstSector:N0}");
                                Console.WriteLine($"    Last LBA: {vp.FirstSector + vp.SectorCount - 1:N0}");
                                Console.WriteLine($"    Sectors: {vp.SectorCount:N0}");
                                Console.WriteLine($"    Size: {vp.SectorCount * 512:N0} bytes ({vp.SectorCount * 512 / 1024.0 / 1024:F1} MB)");
                                using var ps2 = vp.Open();
                                ps2.Position = 0;
                                byte[] pboot = new byte[512];
                                ps2.Read(pboot, 0, 512);
                                string oem = Encoding.ASCII.GetString(pboot, 3, 8);
                                Console.WriteLine($"    OEM ID: \"{oem}\"");
                                bool ntfs = NtfsFileSystem.Detect(ps2);
                                ps2.Position = 0;
                                bool fat = FatFileSystem.Detect(ps2);
                                Console.WriteLine($"    NTFS: {ntfs}, FAT: {fat}");
                            }
                        }
                        catch (Exception ex) { Console.WriteLine($"  Error: {ex.Message}"); }
                    }
                }
            }
        }
        catch (Exception ex) { Console.WriteLine($"Error: {ex.Message}"); }
    }

    static void VhdxDump(string path)
    {
        Console.WriteLine("\n=== VHDX Dump ===");
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            byte[] hdr = new byte[64 * 1024];
            fs.Read(hdr, 0, hdr.Length);
            string sig = Encoding.ASCII.GetString(hdr, 0, 8);
            Console.WriteLine($"File signature: \"{sig}\"");
            if (sig != "vhdxfile")
            {
                Console.WriteLine("Not a VHDX file (signature mismatch)");
                return;
            }
            ulong creatorOffset = BitConverter.ToUInt64(hdr, 8);
            Console.WriteLine($"Creator offset: 0x{creatorOffset:X}");
            fs.Position = (long)creatorOffset;
            byte[] creator = new byte[64 * 1024];
            fs.Read(creator, 0, creator.Length);
            string creatorSig = Encoding.ASCII.GetString(creator, 0, 8);
            Console.WriteLine($"Creator signature: \"{creatorSig}\"");
            if (creatorSig == "creator ")
            {
                string creatorStr = Encoding.Unicode.GetString(creator, 8, 512).TrimEnd('\0');
                Console.WriteLine($"Creator: \"{creatorStr}\"");
            }
            using var vhdxDisk = new DiscUtils.Vhdx.Disk(path);
            Console.WriteLine($"\nDisk capacity: {vhdxDisk.Capacity:N0} bytes ({vhdxDisk.Capacity / 1024.0 / 1024 / 1024:F2} GB)");
            try
            {
                var parts = vhdxDisk.Partitions;
                Console.WriteLine($"Partitions: {parts?.Count ?? 0}");
                for (int j = 0; j < (parts?.Count ?? 0); j++)
                {
                    var p = parts[j];
                    string pName = "";
                    try { if (parts is DiscUtils.Partitions.GuidPartitionTable g && g[j] is DiscUtils.Partitions.GuidPartitionInfo gi) pName = gi.Name; } catch { }
                    Console.WriteLine($"  [{j}] '{pName}' Type={p.GuidType} LBA {p.FirstSector:N0}-{p.FirstSector + p.SectorCount - 1:N0}");
                }
            }
            catch (Exception ex) { Console.WriteLine($"Partition error: {ex.Message}"); }
        }
        catch (Exception ex) { Console.WriteLine($"Error: {ex.Message}"); }
    }

    static void Diag2(string path)
    {
        Console.WriteLine("\n=== General FFU Diagnostic ===");
        try
        {
            FileInfo fi = new(path);
            Console.WriteLine($"File: {fi.Name}");
            Console.WriteLine($"Size: {fi.Length:N0} bytes ({fi.Length / 1024.0 / 1024 / 1024:F2} GB)");
            int storeCount = FullFlashUpdateReaderStream.GetStoreCount(path);
            Console.WriteLine($"Stores: {storeCount}");
            for (uint store = 0; store < storeCount; store++)
            {
                using var reader = new FullFlashUpdateReaderStream(path, store);
                Console.WriteLine($"\n--- Store {store} ---");
                Console.WriteLine($"  Length: {reader.Length:N0} bytes ({reader.Length / 1024.0 / 1024 / 1024:F2} GB)");
                Console.WriteLine($"  SectorSize: {reader.SectorSize}");
                Console.WriteLine($"  DevicePath: {reader.DevicePath}");
                var disk = new DiscUtils.Raw.Disk(reader, Ownership.None);
                var parts = disk.Partitions;
                Console.WriteLine($"  Partitions: {parts?.Count ?? 0}");
                for (int i = 0; i < (parts?.Count ?? 0); i++)
                {
                    var p = parts[i];
                    string pName = "";
                    try { if (parts is DiscUtils.Partitions.GuidPartitionTable g && g[i] is DiscUtils.Partitions.GuidPartitionInfo gi) pName = gi.Name; } catch { }
                    string fs = "?";
                    try { using var s = p.Open(); if (NtfsFileSystem.Detect(s)) fs = "NTFS"; } catch { }
                    try { using var s = p.Open(); if (FatFileSystem.Detect(s)) fs = "FAT32"; } catch { }
                    string guidStr = p.GuidType.ToString("N").ToLower();
                    string tag = guidStr.StartsWith("e75caf8f") ? " [OSPool]" : "";
                    Console.WriteLine($"    [{i}] '{pName}' Type={p.GuidType}{tag} LBA {p.FirstSector:N0}-{p.FirstSector + p.SectorCount - 1:N0} ({p.SectorCount * 512 / 1024.0 / 1024:F1} MB) {fs}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            if (ex.InnerException != null) Console.WriteLine($"  Inner: {ex.InnerException.Message}");
        }
    }
}
