using DiscUtils;
using DiscUtils.Ntfs;
using DiscUtils.Fat;
using DiscUtils.Streams;
using Img2Ffu.Reader;
using System.Text;

namespace FFUView;

public class FfuImage : IDisposable
{
    private Stream? _ffuStream;
    private DiscUtils.Raw.Disk? _disk;
    private long _diskSize;
    private string _imageName = "";
    private string _devicePath = "";
    private int _sectorSize = 512;
    private readonly List<FfuPartition> _partitions = new();
    private bool _disposed;
    private bool _ownsStream = true;

    public string ImageName => _imageName;
    public string DevicePath => _devicePath;
    public long DiskSize => _diskSize;
    public int SectorSize => _sectorSize;
    public IReadOnlyList<FfuPartition> Partitions => _partitions;

    public static int GetStoreCount(string path)
    {
        return FullFlashUpdateReaderStream.GetStoreCount(path);
    }

    public static List<StoreInfo> GetStoreInfos(string path)
    {
        var infos = new List<StoreInfo>();
        int count = GetStoreCount(path);
        for (int i = 0; i < count; i++)
        {
            try
            {
                using var stream = new FullFlashUpdateReaderStream(path, (ulong)i);
                infos.Add(new StoreInfo
                {
                    Index = i,
                    DevicePath = stream.DevicePath,
                    Size = stream.Length,
                    SectorSize = stream.SectorSize
                });
            }
            catch
            {
                infos.Add(new StoreInfo { Index = i, DevicePath = $"Store {i}", Size = 0, SectorSize = 512 });
            }
        }
        return infos;
    }

    public bool Open(string path, int storeIndex = 0)
    {
        try
        {
            var ffuStream = new FullFlashUpdateReaderStream(path, (ulong)storeIndex);
            _ffuStream = ffuStream;
            _ownsStream = true;
            _diskSize = ffuStream.Length;
            _sectorSize = ffuStream.SectorSize;
            _devicePath = ffuStream.DevicePath;
            _imageName = string.IsNullOrEmpty(_devicePath) ? $"Store {storeIndex}" : _devicePath;

            var geometry = Geometry.FromCapacity(_diskSize, _sectorSize);
            _disk = new DiscUtils.Raw.Disk(_ffuStream, Ownership.None, geometry);
            var gptNames = GetGptPartitionNames();
            ParsePartitions(gptNames);
            return _partitions.Count > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Open error: {ex.Message}");
            return false;
        }
    }

    public bool OpenRaw(Stream stream, string name = "Raw Disk")
    {
        try
        {
            _ffuStream = stream;
            _ownsStream = false;
            _diskSize = stream.Length;
            _sectorSize = 512;
            _devicePath = name;
            _imageName = name;

            var geometry = Geometry.FromCapacity(_diskSize, _sectorSize);
            _disk = new DiscUtils.Raw.Disk(_ffuStream, Ownership.None, geometry);
            var gptNames = GetGptPartitionNames();
            ParsePartitions(gptNames);
            return _partitions.Count > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OpenRaw error: {ex.Message}");
            return false;
        }
    }

    private void ParsePartitions(List<string> gptNames)
    {
        _partitions.Clear();
        if (_disk == null) return;
        try
        {
            var table = _disk.Partitions;
            if (table == null || table.Count == 0) return;

            for (int i = 0; i < table.Count; i++)
            {
                var part = table[i];
                var guidType = Guid.Empty;
                try { guidType = part.GuidType; } catch { }

                string gptName = gptNames.Count > i ? gptNames[i] : "";
                string displayName = string.IsNullOrEmpty(gptName) ? $"Partition {i + 1}" : gptName;

                var ffuPart = new FfuPartition
                {
                    Name = displayName,
                    StartSector = part.FirstSector,
                    SectorCount = part.SectorCount,
                    GuidType = guidType
                };

                ffuPart.Type = DescribePartitionType(guidType);
                if (string.IsNullOrEmpty(ffuPart.Type))
                {
                    try { ffuPart.Type = part.TypeAsString ?? "Unknown"; } catch { }
                }
                ffuPart.FileSystem = DetectFileSystem(i);
                _partitions.Add(ffuPart);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Partition parse error: {ex.Message}");
        }
    }

    private List<string> GetGptPartitionNames()
    {
        var names = new List<string>();
        if (_ffuStream == null) return names;
        try
        {
            _ffuStream.Position = _sectorSize;
            byte[] gptHeader = new byte[92];
            if (_ffuStream.Read(gptHeader, 0, 92) < 92) return names;
            if (Encoding.ASCII.GetString(gptHeader, 0, 8) != "EFI PART") return names;

            long partTableLba = BitConverter.ToInt64(gptHeader, 72);
            uint numEntries = BitConverter.ToUInt32(gptHeader, 80);
            uint entrySize = BitConverter.ToUInt32(gptHeader, 84);
            if (numEntries == 0 || entrySize < 72) return names;

            long tableOffset = partTableLba * _sectorSize;
            for (uint i = 0; i < numEntries; i++)
            {
                _ffuStream.Position = tableOffset + i * entrySize + 56;
                byte[] nameBytes = new byte[72];
                if (_ffuStream.Read(nameBytes, 0, 72) < 72) break;
                string name = Encoding.Unicode.GetString(nameBytes).TrimEnd('\0');
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
            }
        }
        catch { }
        return names;
    }

    private string DetectFileSystem(int index)
    {
        try
        {
            using var partStream = _disk!.Partitions[index].Open();
            if (NtfsFileSystem.Detect(partStream)) return "NTFS";
            partStream.Position = 0;
            if (FatFileSystem.Detect(partStream)) return "FAT32";
        }
        catch { }
        return "Unknown";
    }

    public DiscUtilsFileSystem? OpenFileSystem(FfuPartition part)
    {
        if (_disk == null) return null;
        int index = _partitions.IndexOf(part);
        if (index < 0) return null;
        try
        {
            var partStream = _disk.Partitions[index].Open();
            if (part.FileSystem == "NTFS")
                return new DiscUtilsFileSystem(new NtfsFileSystem(partStream), partStream);
            if (part.FileSystem == "FAT32")
                return new DiscUtilsFileSystem(new FatFileSystem(partStream), partStream);
        }
        catch { }
        return null;
    }

    public Stream? OpenPartitionRaw(FfuPartition part)
    {
        if (_disk == null) return null;
        int index = _partitions.IndexOf(part);
        if (index < 0) return null;
        try { return _disk.Partitions[index].Open(); }
        catch { return null; }
    }

    private static string DescribePartitionType(Guid guid)
    {
        if (guid == new Guid("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7")) return "Basic Data";
        if (guid == new Guid("C12A7328-F81F-11D2-BA4B-00A0C93EC93B")) return "EFI System";
        if (guid == new Guid("E3C9E316-0B5C-4DB8-817D-F92DF00215AE")) return "Microsoft Reserved";
        if (guid == new Guid("DE94BBA4-06D1-4D40-A16A-BFD50179D6AC")) return "Windows Recovery";
        if (guid == new Guid("37AFFC90-EF7D-4E96-91C3-2D7AE055B174")) return "Windows Recovery";
        if (guid == Guid.Empty) return "Unused";
        return "";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _disk?.Dispose();
        if (_ownsStream) _ffuStream?.Dispose();
    }
}

public class StoreInfo
{
    public int Index { get; set; }
    public string DevicePath { get; set; } = "";
    public long Size { get; set; }
    public int SectorSize { get; set; }
}

public class FfuPartition
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string FileSystem { get; set; } = "";
    public long StartSector { get; set; }
    public long SectorCount { get; set; }
    public Guid GuidType { get; set; }
    public long SizeBytes => SectorCount * 512;
}

public class DiscUtilsFileSystem : IDisposable
{
    public DiscFileSystem FileSystem { get; }
    private readonly Stream _stream;
    private bool _disposed;

    public DiscUtilsFileSystem(DiscFileSystem fs, Stream stream)
    {
        FileSystem = fs;
        _stream = stream;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        FileSystem.Dispose();
        _stream.Dispose();
    }
}
