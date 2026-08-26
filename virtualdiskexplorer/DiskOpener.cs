using DiscUtils;
using DiscUtils.Partitions;
using DiscUtils.Ntfs;
using DiscUtils.Fat;
using DiscUtils.Streams;

namespace VirtualDiskExplorer;

public static class DiskOpener
{
    public static VirtualDisk? OpenDisk(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            return ext switch
            {
                ".vhd" => new DiscUtils.Vhd.Disk(path, FileAccess.Read),
                ".vhdx" => new DiscUtils.Vhdx.Disk(path, FileAccess.Read),
                ".vmdk" => new DiscUtils.Vmdk.Disk(path, FileAccess.Read),
                ".vdi" => new DiscUtils.Vdi.Disk(path, FileAccess.Read),
                ".qcow" or ".qcow2" => OpenQcow2(path),
                ".raw" or ".img" or ".dd" => new DiscUtils.Raw.Disk(path, FileAccess.Read),
                _ => VirtualDisk.OpenDisk(path, FileAccess.Read)
            };
        }
        catch
        {
            try { return VirtualDisk.OpenDisk(path, FileAccess.Read); }
            catch { return null; }
        }
    }

    public static VirtualDisk? OpenDiskFromStream(Stream stream)
    {
        try
        {
            stream.Position = 0;
            var header = new byte[64];
            var read = stream.Read(header, 0, 64);
            stream.Position = 0;

            if (read >= 16)
            {
                var vhdxCookie = System.Text.Encoding.Unicode.GetString(header, 0, 16);
                if (vhdxCookie.Contains("vhdxfile"))
                    return new DiscUtils.Vhdx.Disk(stream, Ownership.None);
            }
            if (read >= 8)
            {
                var vhdCookie = System.Text.Encoding.ASCII.GetString(header, 0, 8);
                if (vhdCookie == "conectix")
                    return new DiscUtils.Vhd.Disk(stream, Ownership.None);
            }
            return new DiscUtils.Raw.Disk(stream, Ownership.None);
        }
        catch
        {
            try { stream.Position = 0; return new DiscUtils.Raw.Disk(stream, Ownership.None); }
            catch { return null; }
        }
    }

    private static VirtualDisk? OpenQcow2(string path)
    {
        try { return VirtualDisk.OpenDisk(path, FileAccess.Read); }
        catch { return null; }
    }

    public static string GetDiskFormat(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".vhd" => "VHD",
            ".vhdx" => "VHDX",
            ".vmdk" => "VMDK",
            ".vdi" => "VDI",
            ".qcow" or ".qcow2" => "QCOW2",
            ".raw" or ".img" or ".dd" => "RAW",
            _ => "Unknown"
        };
    }

    public static List<PartitionInfo> GetPartitions(VirtualDisk disk)
    {
        var result = new List<PartitionInfo>();
        try
        {
            if (disk.Partitions != null)
            {
                for (int i = 0; i < disk.Partitions.Count; i++)
                    result.Add(disk.Partitions[i]);
            }
        }
        catch { }
        return result;
    }

    public static bool IsSpaceDB(PartitionInfo partition)
    {
        try
        {
            using var stream = partition.Open();
            var header = new byte[8];
            stream.Read(header, 0, 8);
            return System.Text.Encoding.ASCII.GetString(header) == "SPACEDB ";
        }
        catch { return false; }
    }

    public static SpaceDBInfo? GetSpaceDBInfo(PartitionInfo partition)
    {
        var info = new SpaceDBInfo
        {
            Signature = "SPACEDB",
            Version = 0,
            PoolId = Guid.Empty,
            TotalSize = 0
        };

        try
        {
            using var stream = partition.Open();
            info.TotalSize = stream.Length;

            long bufSize = Math.Min(0x100000, stream.Length);
            var buffer = new byte[bufSize];
            int totalRead = 0;
            while (totalRead < bufSize)
            {
                int r = stream.Read(buffer, totalRead, (int)(bufSize - totalRead));
                if (r <= 0) break;
                totalRead += r;
            }

            if (totalRead >= 8)
            {
                var sig = System.Text.Encoding.ASCII.GetString(buffer, 0, 8);
                if (sig == "SPACEDB ")
                {
                    info.Version = BitConverter.ToUInt32(buffer, 8);
                    var poolIdBytes = new byte[16];
                    Array.Copy(buffer, 24, poolIdBytes, 0, 16);
                    info.PoolId = new Guid(poolIdBytes);
                }
            }

            int sdbCount = 0;
            int vdTypeCount = 0;
            var recordBuffer = new byte[64];
            var currentVD = -1;
            var blockRecords = new List<List<uint>>();
            
            for (int offset = 0x1000; offset < totalRead - 64; offset += 64)
            {
                Array.Copy(buffer, offset, recordBuffer, 0, 64);
                if (System.Text.Encoding.ASCII.GetString(recordBuffer, 0, 4) != "SDBB") continue;
                sdbCount++;

                uint recordType = BitConverter.ToUInt32(recordBuffer, 12);
                if (recordType == 0x04000000)
                {
                    vdTypeCount++;
                    currentVD++;
                    blockRecords.Add(new List<uint>());
                    var name = System.Text.Encoding.BigEndianUnicode.GetString(recordBuffer, 0x2C, 20).TrimEnd('\0');
                    var vd = new SpaceDBVirtualDisk
                    {
                        Name = string.IsNullOrEmpty(name) ? $"VD_{vdTypeCount}" : name,
                        DiskId = Guid.Empty,
                        Capacity = 0,
                        DataOffset = 0
                    };
                    info.VirtualDisks.Add(vd);
                }
                else if (recordType == 0x01000000 && currentVD >= 0)
                {
                    uint physical = BitConverter.ToUInt32(recordBuffer, 44);
                    blockRecords[currentVD].Add(physical);
                }
            }

            try
            {
                for (int i = 0; i < info.VirtualDisks.Count; i++)
                {
                    var vd = info.VirtualDisks[i];
                    if (vd.Name.StartsWith("[")) continue;
                    if (i >= blockRecords.Count || blockRecords[i].Count == 0) continue;
                    
                    foreach (uint phys in blockRecords[i])
                    {
                        if (phys == 0) continue;
                        long diskOffset = (long)(phys - 1) * 512 * 1024;
                        if (diskOffset + 1024 >= stream.Length) continue;
                        
                        try
                        {
                            stream.Seek(diskOffset, SeekOrigin.Begin);
                            var mbr = new byte[512];
                            if (stream.Read(mbr, 0, 512) < 512) continue;
                            if (mbr[510] != 0x55 || mbr[511] != 0xAA) continue;
                            
                            stream.Seek(diskOffset + 512, SeekOrigin.Begin);
                            var gptHeader = new byte[512];
                            if (stream.Read(gptHeader, 0, 512) < 512) continue;
                            if (System.Text.Encoding.ASCII.GetString(gptHeader, 0, 8) != "EFI PART") continue;
                            
                            long altLBA = BitConverter.ToInt64(gptHeader, 32);
                            long capacity = (altLBA + 1) * 512;
                            
                            vd.DataOffset = diskOffset;
                            vd.Capacity = capacity > 0 ? capacity : 0;
                            break;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            
            try
            {
                var foundDisks = new List<(long offset, long capacity)>();
                long scanStart = 0x100000;
                long scanEnd = stream.Length;
                long scanStep = 0x40000;
                var scanBuffer = new byte[512];
                
                for (long pos = scanStart; pos < scanEnd; pos += scanStep)
                {
                    try
                    {
                        stream.Seek(pos, SeekOrigin.Begin);
                        int read = stream.Read(scanBuffer, 0, 512);
                        if (read < 512) break;
                        
                        if (scanBuffer[510] == 0x55 && scanBuffer[511] == 0xAA &&
                            System.Text.Encoding.ASCII.GetString(scanBuffer, 0, 8) != "EFI PART")
                        {
                            stream.Seek(pos + 512, SeekOrigin.Begin);
                            var gptHeader = new byte[512];
                            if (stream.Read(gptHeader, 0, 512) >= 512 &&
                                System.Text.Encoding.ASCII.GetString(gptHeader, 0, 8) == "EFI PART")
                            {
                                long altLBA = BitConverter.ToInt64(gptHeader, 32);
                                long capacity = (altLBA + 1) * 512;
                                if (capacity > 50 * 1024 * 1024)
                                {
                                    bool alreadyFound = false;
                                    foreach (var fd in foundDisks)
                                    {
                                        if (Math.Abs(fd.offset - pos) < 0x100000) { alreadyFound = true; break; }
                                    }
                                    if (!alreadyFound)
                                        foundDisks.Add((pos, capacity));
                                }
                            }
                        }
                    }
                    catch { break; }
                }
                
                foundDisks.Sort((a, b) => a.offset.CompareTo(b.offset));
                
                int foundIdx = 0;
                for (int i = 0; i < info.VirtualDisks.Count; i++)
                {
                    var vd = info.VirtualDisks[i];
                    if (vd.Name.StartsWith("[")) continue;
                    if (vd.DataOffset > 0) continue;
                    
                    while (foundIdx < foundDisks.Count)
                    {
                        vd.DataOffset = foundDisks[foundIdx].offset;
                        vd.Capacity = foundDisks[foundIdx].capacity;
                        foundIdx++;
                        break;
                    }
                }
            }
            catch { }
        }
        catch (Exception ex)
        {
            var errInfo = new SpaceDBInfo
            {
                Signature = "SPACEDB",
                Version = 0,
                PoolId = Guid.Empty,
                TotalSize = 0
            };
            errInfo.VirtualDisks.Add(new SpaceDBVirtualDisk
            {
                Name = $"[ERROR] {ex.Message}",
                DiskId = Guid.Empty,
                Capacity = 0,
                DataOffset = 0
            });
            return errInfo;
        }

        return info;
    }


    public static string? ExtractSpaceDBVirtualDisk(PartitionInfo partition, long offset, string outputPath)
    {
        try
        {
            using var stream = partition.Open();
            stream.Position = offset;

            var header = new byte[512];
            stream.Read(header, 0, 512);
            stream.Position = offset;

            bool isVhdx = System.Text.Encoding.Unicode.GetString(header, 0, 16).Contains("vhdxfile");
            bool isVhd = System.Text.Encoding.ASCII.GetString(header, 0, 8) == "conectix";

            long capacity = 0;
            using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            var buffer = new byte[16 * 1024 * 1024];
            long totalWritten = 0;
            long maxSize = stream.Length - offset;

            while (totalWritten < maxSize)
            {
                int toRead = (int)Math.Min(buffer.Length, maxSize - totalWritten);
                int r = stream.Read(buffer, 0, toRead);
                if (r <= 0) break;
                fs.Write(buffer, 0, r);
                totalWritten += r;

                if (totalWritten > 256 * 1024 * 1024)
                {
                    try
                    {
                        fs.Flush();
                        VirtualDisk? test = isVhdx
                            ? new DiscUtils.Vhdx.Disk(outputPath, FileAccess.Read)
                            : isVhd ? new DiscUtils.Vhd.Disk(outputPath, FileAccess.Read)
                            : null;
                        if (test != null)
                        {
                            capacity = test.Capacity;
                            test.Dispose();
                            if (capacity > 0 && totalWritten >= capacity * 1.1)
                                break;
                        }
                    }
                    catch { }
                }
            }

            if (capacity > 0 && totalWritten > capacity)
            {
                fs.SetLength(capacity);
            }

            return outputPath;
        }
        catch { return null; }
    }

    public static DiscFileSystem? OpenFileSystem(PartitionInfo partition)
    {
        try
        {
            using var stream = partition.Open();
            stream.Position = 0;
            if (NtfsFileSystem.Detect(stream))
            {
                stream.Position = 0;
                return new NtfsFileSystem(stream);
            }
            stream.Position = 0;
            if (FatFileSystem.Detect(stream))
            {
                stream.Position = 0;
                return new FatFileSystem(stream);
            }
        }
        catch { }
        return null;
    }

    public static string GetFileSystemType(PartitionInfo partition)
    {
        try
        {
            if (IsSpaceDB(partition)) return "SpaceDB (OSPool)";
            using var stream = partition.Open();
            stream.Position = 0;
            if (NtfsFileSystem.Detect(stream)) return "NTFS";
            stream.Position = 0;
            if (FatFileSystem.Detect(stream)) return "FAT";
            return "Unknown";
        }
        catch { return "Error"; }
    }
}

public class SpaceDBInfo
{
    public string Signature { get; set; } = "";
    public uint Version { get; set; }
    public Guid PoolId { get; set; }
    public long TotalSize { get; set; }
    public List<SpaceDBVirtualDisk> VirtualDisks { get; set; } = new();
}

public class SpaceDBVirtualDisk
{
    public string Name { get; set; } = "";
    public Guid DiskId { get; set; }
    public long Capacity { get; set; }
    public double CapacityGB => Capacity / 1024.0 / 1024 / 1024;
    public long DataOffset { get; set; }
    public List<SpaceDBBlockExtent> Extents { get; set; } = new();
    public List<string> PartitionNames { get; set; } = new();
}

public class SpaceDBBlockExtent
{
    public long LogicalOffset { get; set; }
    public long PhysicalOffset { get; set; }
    public long Length { get; set; }
}
