using System.Diagnostics;
using System.Text;

namespace DeviceLayoutToVhd;

/// <summary>
/// 18963+ 版本设备布局 VHD 创建器
/// 在17704基础上增加：extentSize(ChunkSize)、4096物理扇区、BitLocker元数据预留
/// </summary>
public class DeviceLayoutVhdCreator18963
{
    private readonly DeviceLayoutInfo _layout;
    private readonly string _outputDir;
    private readonly Action<string>? _log;

    public DeviceLayoutVhdCreator18963(DeviceLayoutInfo layout, string outputDir, Action<string>? log = null)
    {
        _layout = layout;
        _outputDir = outputDir;
        _log = log;
    }

    public void CreateFullImage()
    {
        Directory.CreateDirectory(_outputDir);
        Log("=== 18963 设备布局 VHD 创建开始 (NativeStoragePool方案) ===");
        Log($"扇区大小: {_layout.SectorSize}, 块大小(ChunkSize): {_layout.ChunkSize}");
        Log($"StateSeparationLevel: {_layout.StateSeparationLevel}");

        var physicalStore = _layout.GetPhysicalStore();
        if (physicalStore == null) { Log("错误: 未找到物理Store"); return; }

        var spPart = _layout.GetStoragePoolPartition();
        if (spPart == null) { Log("错误: 未找到存储池分区"); return; }

        // 提取前部分区
        var frontPartitions = new List<FrontPartition>();
        var inFront = true;
        foreach (var part in physicalStore.Partitions)
        {
            var isSp = part.Type.Equals(DeviceLayoutInfo.StoragePoolPartitionType, StringComparison.OrdinalIgnoreCase) ||
                        part.Name.Equals("OSPool", StringComparison.OrdinalIgnoreCase);
            if (isSp) { inFront = false; continue; }
            if (inFront && part.TotalSectors > 0 && Guid.TryParse(part.Type, out var typeGuid))
            {
                frontPartitions.Add(new FrontPartition
                {
                    Name = part.Name,
                    SizeBytes = part.TotalSectors * _layout.SectorSize,
                    TypeGuid = typeGuid,
                    FileSystem = string.IsNullOrEmpty(part.FileSystem) ? null : part.FileSystem
                });
            }
        }
        Log($"前部分区总数: {frontPartitions.Count}");

        // 提取虚拟磁盘
        var virtualDisks = new List<VirtualDiskSpec>();
        foreach (var pool in _layout.StoragePools)
            foreach (var vStore in pool.Stores)
                virtualDisks.Add(new VirtualDiskSpec { Name = vStore.StoreType, SizeBytes = vStore.SizeInSectors * _layout.SectorSize });
        Log($"虚拟磁盘总数: {virtualDisks.Count}");

        var vhdPath = Path.Combine(_outputDir, $"{physicalStore.StoreType}_Physical.vhdx");
        var diskSize = physicalStore.SizeInSectors * _layout.SectorSize;
        var extentSize = (long)_layout.ChunkSize * _layout.SectorSize;
        Log($"extentSize: {extentSize} bytes");

        // 使用扩展的NativeStoragePoolCreator（18963特性）
        var creator = new NativeStoragePoolCreator
        {
            VhdPath = vhdPath,
            PoolName = spPart.Name,
            DiskSizeBytes = diskSize,
            Format = VhdFormat.Vhdx,
            FrontPartitions = frontPartitions,
            VirtualDisks = virtualDisks,
            Log = msg => Log(msg)
        };

        var success = creator.Create();
        Log(success ? "\n=== VHD 创建成功 ===" : "\n=== VHD 创建失败 ===");
        Log($"输出文件: {vhdPath}");
    }

    private void Log(string msg) => _log?.Invoke(msg);
}
