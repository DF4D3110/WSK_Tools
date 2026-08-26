using System.Diagnostics;
using System.Text;

namespace DeviceLayoutToVhd;

/// <summary>
/// 17704+ 版本设备布局 VHD 创建器
/// 严格按照 imageapp 工作流程实现：
/// 1. 创建 VHD → 初始化 GPT
/// 2. 创建所有分区（前部分区 + 存储池分区 Type={5708A6E0-...}）
/// 3. 调用 IUSpaces.dll (imagestorageservice) CreateStoragePool
/// 4. 对每个虚拟 Store 调用 CreateStorageSpace
/// 5. 在虚拟磁盘上创建分区
/// </summary>
public class DeviceLayoutVhdCreator17704
{
    private readonly DeviceLayoutInfo _layout;
    private readonly string _outputDir;
    private readonly Action<string>? _log;
    private readonly string _iuHelperPath;

    // 存储池分区 Type GUID (Space Protective)
    private static readonly Guid StoragePoolPartitionGuid = new("5708A6E0-9001-4b99-b064-1fe564896bdb");

    public DeviceLayoutVhdCreator17704(DeviceLayoutInfo layout, string outputDir, Action<string>? log = null)
    {
        _layout = layout;
        _outputDir = outputDir;
        _log = log;
        // IUSpacesHelper 32位程序路径
        _iuHelperPath = Path.Combine(AppContext.BaseDirectory, "IUSpacesHelper", "IUSpacesHelper.exe");
        if (!File.Exists(_iuHelperPath))
        {
            _iuHelperPath = @"E:\WSK_Tools\v1.0.3\IUSpacesHelper\publish_x86\IUSpacesHelper.exe";
        }
    }

    private void Log(string msg) => _log?.Invoke(msg);

    public void CreateFullImage()
    {
        Directory.CreateDirectory(_outputDir);
        Log("=== 17704 设备布局 VHD 创建开始 (imageapp 流程) ===");
        Log($"扇区大小: {_layout.SectorSize}, 块大小: {_layout.ChunkSize}");
        Log($"物理 Store: {_layout.Stores.Count}, 存储池: {_layout.StoragePools.Count}");

        var physicalStore = _layout.GetPhysicalStore();
        if (physicalStore == null)
        {
            Log("错误: 未找到物理Store");
            return;
        }

        var spPart = _layout.GetStoragePoolPartition();
        if (spPart == null)
        {
            Log("错误: 未找到存储池分区");
            return;
        }

        // 计算物理磁盘总大小
        long totalBytes = physicalStore.SizeInSectors * _layout.SectorSize;
        string vhdPath = Path.Combine(_outputDir, $"{physicalStore.StoreType}_Physical.vhdx");
        Log($"创建VHD: {vhdPath} ({totalBytes / 1024.0 / 1024 / 1024:F2} GB)");

        int diskNum = -1;
        try
        {
            // === 步骤1: 创建并挂载 VHD ===
            Log("\n[1/6] 创建 VHD...");
            diskNum = CreateAndMountVhd(vhdPath, totalBytes);
            Log($"  磁盘号: {diskNum}");

            // === 步骤2: 初始化为 GPT ===
            Log("\n[2/6] 初始化为 GPT...");
            RunDiskpart($"select disk {diskNum}\r\nconvert gpt\r\nexit\r\n");
            Log("  GPT 初始化完成");

            // === 步骤3: 创建所有分区（前部分区 + 存储池分区）===
            Log("\n[3/6] 创建分区...");
            CreateAllPartitions(diskNum, physicalStore, spPart);

            // === 步骤4: 创建存储池 (IUSpaces.dll CreateStoragePool) ===
            Log("\n[4/6] 创建存储池...");
            string poolName = _layout.StoragePools.Count > 0 ? _layout.StoragePools[0].Name : "OSPool";
            Guid poolId = CreateStoragePool(poolName, diskNum);
            Log($"  存储池创建成功: {poolName} ({poolId})");

            // === 步骤5: 创建虚拟磁盘 (存储空间) ===
            Log("\n[5/6] 创建虚拟磁盘 (存储空间)...");
            var virtualDiskNumbers = new List<int>();
            foreach (var pool in _layout.StoragePools)
            {
                foreach (var vStore in pool.Stores)
                {
                    long sizeBytes = vStore.SizeInSectors * _layout.SectorSize;
                    Log($"  创建虚拟磁盘: {vStore.Id} ({sizeBytes / 1024.0 / 1024 / 1024:F2} GB)");
                    int vdDiskNum = CreateStorageSpace(poolId, vStore.Id, sizeBytes);
                    virtualDiskNumbers.Add(vdDiskNum);
                    Log($"    虚拟磁盘号: {vdDiskNum}");

                    // 在虚拟磁盘上创建分区
                    if (vStore.Partitions.Count > 0)
                    {
                        Log($"    分区虚拟磁盘 {vStore.Id}...");
                        PartitionVirtualDisk(vdDiskNum, vStore);
                    }
                }
            }

            // === 步骤6: 完成 ===
            Log("\n[6/6] 完成...");
            Log($"虚拟磁盘总数: {virtualDiskNumbers.Count}");

            Log("\n=== imageapp 流程 VHD 创建完成 ===");
        }
        catch (Exception ex)
        {
            Log($"FATAL: {ex.Message}");
            Log(ex.StackTrace ?? "");
        }
        finally
        {
            // 卸载 VHD
            if (diskNum >= 0)
            {
                try
                {
                    Log("\n[cleanup] 卸载 VHD...");
                    RunDiskpart($"select vdisk file=\"{vhdPath}\"\r\ndetach vdisk\r\nexit\r\n");
                    Log("  VHD 已卸载");
                }
                catch { }
            }
        }
    }

    private int CreateAndMountVhd(string vhdPath, long sizeBytes)
    {
        if (File.Exists(vhdPath)) File.Delete(vhdPath);
        // 创建动态 VHD
        RunDiskpart($"create vdisk file=\"{vhdPath}\" maximum={sizeBytes / 1024} type=expandable\r\n" +
                     $"select vdisk file=\"{vhdPath}\"\r\nattach vdisk\r\nexit\r\n");
        Thread.Sleep(3000);
        // 获取磁盘号
        for (int i = 0; i < 10; i++)
        {
            string output = RunPS($"Get-Disk | ? {{$_.Location -like '*{Path.GetFileName(vhdPath)}*'}} | select -First 1 -ExpandProperty Number");
            if (int.TryParse(output.Trim(), out int num)) return num;
            Thread.Sleep(2000);
        }
        throw new Exception("无法获取 VHD 磁盘号");
    }

    private void CreateAllPartitions(int diskNum, StoreInfo physicalStore, PartitionInfo spPart)
    {
        var script = new StringBuilder();
        script.AppendLine($"select disk {diskNum}");

        int partIndex = 0;
        foreach (var part in physicalStore.Partitions)
        {
            bool isSp = part.Type.Equals(DeviceLayoutInfo.StoragePoolPartitionType, StringComparison.OrdinalIgnoreCase) ||
                         part.Name.Equals("OSPool", StringComparison.OrdinalIgnoreCase);

            if (isSp)
            {
                // 存储池分区使用剩余空间
                Log($"  创建存储池分区: {part.Name} (使用剩余空间)");
                script.AppendLine("create partition primary");
                script.AppendLine($"set id={{{StoragePoolPartitionGuid}}} override");
                // 不格式化，不分配盘符
            }
            else if (part.TotalSectors > 0)
            {
                long sizeMB = part.TotalSectors * _layout.SectorSize / 1024 / 1024;
                if (sizeMB <= 0) continue;
                Log($"  创建分区: {part.Name} ({sizeMB} MB, Type={part.Type})");
                script.AppendLine($"create partition primary size={sizeMB}");
                // 设置 Type GUID
                if (Guid.TryParse(part.Type, out var typeGuid))
                {
                    script.AppendLine($"set id={{{typeGuid}}} override");
                }
                // 格式化（如果有文件系统）
                if (!string.IsNullOrEmpty(part.FileSystem))
                {
                    string fs = part.FileSystem.ToUpper();
                    if (fs == "FAT" || fs == "FAT32")
                    {
                        script.AppendLine($"format fs=fat32 label=\"{part.Name}\" quick");
                    }
                    else if (fs == "NTFS")
                    {
                        script.AppendLine($"format fs=ntfs label=\"{part.Name}\" quick");
                    }
                }
            }
            partIndex++;
        }

        script.AppendLine("exit");
        RunDiskpart(script.ToString());
        Log("  分区创建完成");
    }

    private Guid CreateStoragePool(string poolName, int diskNum)
    {
        string diskPath = $"\\\\.\\PhysicalDrive{diskNum}";
        Log($"  调用 IUSpacesHelper createpool {poolName} {diskPath}");
        string output = RunProcess(_iuHelperPath, $"createpool {poolName} {diskPath}");
        Log($"  输出: {output.Trim()}");
        // 解析 Pool created: {guid}
        var match = System.Text.RegularExpressions.Regex.Match(output, @"Pool created:\s*(\{?[0-9a-fA-F-]+\}?)");
        if (match.Success)
        {
            return Guid.Parse(match.Groups[1].Value.Trim('{', '}'));
        }
        throw new Exception($"创建存储池失败: {output}");
    }

    private int CreateStorageSpace(Guid poolId, string spaceName, long sizeBytes)
    {
        long sizeMB = sizeBytes / 1024 / 1024;
        Log($"    调用 IUSpacesHelper createspace {poolId} {spaceName} {sizeMB}");
        string output = RunProcess(_iuHelperPath, $"createspace {{{poolId}}} {spaceName} {sizeMB}");
        Log($"    输出: {output.Trim()}");
        if (!output.Contains("Space created") && !output.Contains("OK"))
        {
            throw new Exception($"创建虚拟磁盘失败: {output}");
        }
        Thread.Sleep(3000);
        // 获取新创建的虚拟磁盘号
        string diskOutput = RunPS("Get-Disk | ? {$_.BusType -eq 'Spaces' -or $_.FriendlyName -like '*" + spaceName + "*'} | sort Number -Descending | select -First 1 -ExpandProperty Number");
        if (int.TryParse(diskOutput.Trim(), out int num)) return num;
        // 回退：获取最大的磁盘号
        diskOutput = RunPS("(Get-Disk | sort Number -Descending | select -First 1).Number");
        int.TryParse(diskOutput.Trim(), out num);
        return num;
    }

    private void PartitionVirtualDisk(int diskNum, StoreInfo vStore)
    {
        var script = new StringBuilder();
        script.AppendLine($"select disk {diskNum}");
        script.AppendLine("convert gpt");

        foreach (var part in vStore.Partitions)
        {
            if (part.TotalSectors <= 0 && !part.UseAllSpace) continue;

            if (part.UseAllSpace)
            {
                Log($"      创建分区: {part.Name} (使用剩余空间, FS={part.FileSystem})");
                script.AppendLine("create partition primary");
            }
            else
            {
                long sizeMB = part.TotalSectors * _layout.SectorSize / 1024 / 1024;
                if (sizeMB <= 0) continue;
                Log($"      创建分区: {part.Name} ({sizeMB} MB, FS={part.FileSystem})");
                script.AppendLine($"create partition primary size={sizeMB}");
            }

            // 设置 Type GUID
            if (Guid.TryParse(part.Type, out var typeGuid))
            {
                script.AppendLine($"set id={{{typeGuid}}} override");
            }

            // 格式化
            if (!string.IsNullOrEmpty(part.FileSystem))
            {
                string fs = part.FileSystem.ToUpper();
                if (fs == "FAT" || fs == "FAT32")
                    script.AppendLine($"format fs=fat32 label=\"{part.Name}\" quick");
                else if (fs == "NTFS")
                    script.AppendLine($"format fs=ntfs label=\"{part.Name}\" quick");
            }
        }

        script.AppendLine("exit");
        RunDiskpart(script.ToString());
        Log($"      虚拟磁盘 {vStore.Id} 分区完成");
    }

    private string RunDiskpart(string script)
    {
        string tmpFile = Path.Combine(Path.GetTempPath(), $"dp_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tmpFile, script);
        try
        {
            return RunProcess("diskpart.exe", $"/s \"{tmpFile}\"");
        }
        finally
        {
            try { File.Delete(tmpFile); } catch { }
        }
    }

    private string RunPS(string command)
    {
        return RunProcess("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "\\\"")}\"");
    }

    private string RunProcess(string exe, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var p = Process.Start(psi);
        if (p == null) return "";
        string output = p.StandardOutput.ReadToEnd();
        string error = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return output + (string.IsNullOrEmpty(error) ? "" : "\nERR: " + error);
    }
}
