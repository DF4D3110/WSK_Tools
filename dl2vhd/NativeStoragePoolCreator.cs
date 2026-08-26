using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace DeviceLayoutToVhd
{
    /// <summary>
    /// Native Windows Storage Spaces pool creator using direct GPT manipulation.
    /// Flow: create blank VHD -> New-StoragePool on whole disk -> backup -> remove pool ->
    /// directly edit GPT to shrink pool and insert front partitions -> format front partitions ->
    /// refresh storage service. Pool data stays in place (Storage Spaces uses absolute LBA).
    /// </summary>
    public class NativeStoragePoolCreator
    {
        public string VhdPath { get; set; }
        public string PoolName { get; set; } = "OSPool";
        public long DiskSizeBytes { get; set; }
        public VhdFormat Format { get; set; } = VhdFormat.Vhdx;

        /// <summary>Front partitions to insert before the pool (in order).</summary>
        public List<FrontPartition> FrontPartitions { get; set; } = new();

        /// <summary>Virtual disks to create inside the storage pool.</summary>
        public List<VirtualDiskSpec> VirtualDisks { get; set; } = new();

        public Action<string> Log { get; set; } = Console.WriteLine;

        public bool Create()
        {
            try
            {
                string backupFile = null;
                long poolSize = 0;
                ulong newPoolStart = 0;
                Log($"=== Native Storage Pool Creator ===");
                Log($"VHD: {VhdPath}");
                Log($"Pool: {PoolName}");
                Log($"Disk size: {DiskSizeBytes / 1024.0 / 1024 / 1024:F2} GB");

                // Cleanup existing
                RunPS($"Dismount-VHD -Path '{VhdPath}' -ErrorAction SilentlyContinue");
                Thread.Sleep(2000);
                if (File.Exists(VhdPath)) File.Delete(VhdPath);

                // Step 1: Create and mount VHD
                Log("\n[1/8] Creating VHD...");
                RunPS($"New-VHD -Path '{VhdPath}' -SizeBytes {DiskSizeBytes} -Dynamic | Out-Null; Mount-VHD -Path '{VhdPath}' -PassThru | Out-Null");
                Thread.Sleep(5000);

                int diskNum = GetDiskNumber();
                if (diskNum < 0) { Log("ERROR: Cannot find VHD disk"); return false; }
                Log($"  Disk: {diskNum}");

                // Initialize
                RunPS($"Initialize-Disk -Number {diskNum} -PartitionStyle GPT -Confirm:$false");
                Thread.Sleep(1000);

                // Step 2: Create native storage pool on whole disk
                Log("\n[2/8] Creating native storage pool (thin provisioning)...");
                string poolScript = "$pd=Get-PhysicalDisk|?{$_.DeviceId-eq'" + diskNum + "'};" +
                    $"New-StoragePool -FriendlyName {PoolName} -StorageSubSystemFriendlyName 'Windows Storage*' -PhysicalDisks $pd -ProvisioningTypeDefault Thin -ResiliencySettingNameDefault Simple|Out-Null;";
                foreach (var vd in VirtualDisks)
                {
                    poolScript += $"New-VirtualDisk -StoragePoolFriendlyName {PoolName} -FriendlyName '{vd.Name}' -Size {vd.SizeBytes} -ProvisioningType Thin -ResiliencySettingName Simple|Out-Null;";
                }
                poolScript += "Write-Host 'OK'";
                Log(RunPS(poolScript));
                Thread.Sleep(3000);

                // Step 3: Read GPT and find pool partition
                Log("\n[3/8] Reading GPT...");
                using (var gpt = new GptManager($"\\\\.\\PhysicalDrive{diskNum}"))
                {
                    var parts = gpt.GetNonEmptyPartitions();
                    Log($"  Partitions: {parts.Count}");
                    foreach (var p in parts)
                        Log($"    {p.Name}: LBA {p.StartingLba}-{p.EndingLba} ({Math.Round(p.SizeInSectors * 512.0 / 1024 / 1024, 1)} MB)");

                    int poolIdx = -1;
                    for (int i = 0; i < gpt.Partitions.Count; i++)
                    {
                        if (!gpt.Partitions[i].IsEmpty && gpt.Partitions[i].SizeInSectors > 100000)
                        { poolIdx = i; break; }
                    }
                    if (poolIdx < 0) { Log("ERROR: Pool partition not found"); return false; }

                    var poolPart = gpt.Partitions[poolIdx];
                    long poolOffset = (long)poolPart.StartingLba * 512;
                    poolSize = (long)poolPart.SizeInSectors * 512;
                    Log($"  Pool partition: LBA {poolPart.StartingLba}, offset={poolOffset / 1024 / 1024}MB, size={Math.Round(poolSize / 1024.0 / 1024 / 1024, 2)}GB");

                    // Step 4: Backup pool data (use output dir to avoid C: space issues)
                    Log("\n[4/8] Backing up pool data...");
                    backupFile = Path.Combine(Path.GetDirectoryName(VhdPath)!, $"nspc_backup_{Guid.NewGuid():N}.bin");
                    using (var src = new FileStream($"\\\\.\\PhysicalDrive{diskNum}", FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var dst = File.Create(backupFile))
                    {
                        src.Seek(poolOffset, SeekOrigin.Begin);
                        byte[] buf = new byte[64 * 1024 * 1024];
                        long remaining = poolSize;
                        while (remaining > 0)
                        {
                            int toRead = (int)Math.Min(buf.Length, remaining);
                            int read = src.Read(buf, 0, toRead);
                            if (read <= 0) break;
                            dst.Write(buf, 0, read);
                            remaining -= read;
                        }
                    }
                    Log($"  Backup: {Math.Round(new FileInfo(backupFile).Length / 1024.0 / 1024 / 1024, 2)}GB");

                    // Step 5: Disconnect virtual disks and set disk offline for GPT editing
                    Log("\n[5/8] Preparing disk for GPT editing...");
                    RunPS("Get-VirtualDisk -ErrorAction SilentlyContinue | Disconnect-VirtualDisk -Confirm:$false -ErrorAction SilentlyContinue");
                    Thread.Sleep(3000);
                    // 设置磁盘离线，这样可以直接修改GPT而不需要卸载VHD
                    RunPS($"Set-Disk -Number {diskNum} -IsOffline $true -ErrorAction SilentlyContinue");
                    Thread.Sleep(3000);
                    Log($"  Disk {diskNum} set offline");
                }

                // Step 6: Direct GPT manipulation
                Log("\n[6/8] Editing GPT (insert front partitions, shrink pool)...");
                Thread.Sleep(2000);
                using (var gpt2 = new GptManager($"\\\\.\\PhysicalDrive{diskNum}"))
                {
                    var parts2 = gpt2.GetNonEmptyPartitions();
                    int poolIdx2 = -1;
                    for (int i = 0; i < gpt2.Partitions.Count; i++)
                    {
                        if (!gpt2.Partitions[i].IsEmpty && gpt2.Partitions[i].SizeInSectors > 100000)
                        { poolIdx2 = i; break; }
                    }
                    if (poolIdx2 < 0) { Log("ERROR: Pool partition not found after remount"); return false; }

                    var poolPart2 = gpt2.Partitions[poolIdx2];
                    ulong poolStart = poolPart2.StartingLba;

                    // Calculate total front space needed
                    ulong totalFrontSectors = 0;
                    foreach (var fp in FrontPartitions)
                        totalFrontSectors += (ulong)(fp.SizeBytes / 512);
                    // Add alignment padding (1MB)
                    totalFrontSectors += 2048;
                    // Align to 1MB
                    totalFrontSectors = ((totalFrontSectors + 2047) / 2048) * 2048;

                    newPoolStart = poolStart + totalFrontSectors;
                    Log($"  Moving pool from LBA {poolStart} to {newPoolStart} (freeing {totalFrontSectors} sectors = {totalFrontSectors * 512 / 1024 / 1024}MB)");

                    // Shrink pool from front
                    gpt2.ShrinkPartitionFromFront(poolIdx2, totalFrontSectors);

                    // Insert front partitions
                    ulong currentLba = poolStart;
                    foreach (var fp in FrontPartitions)
                    {
                        ulong sizeSectors = (ulong)(fp.SizeBytes / 512);
                        // Align start to 1MB
                        currentLba = ((currentLba + 2047) / 2048) * 2048;
                        gpt2.InsertPartition(fp.TypeGuid, currentLba, currentLba + sizeSectors - 1, fp.Name);
                        Log($"  Inserted: {fp.Name} LBA {currentLba}-{currentLba + sizeSectors - 1} ({fp.SizeBytes / 1024 / 1024}MB)");
                        currentLba += sizeSectors;
                    }

                    Log("  Writing GPT...");
                    gpt2.WriteGpt();
                    Log("  GPT written successfully");
                }

                // 将磁盘设为在线
                Log("\n[6.5/8] Bringing disk online...");
                RunPS($"Set-Disk -Number {diskNum} -IsOffline $false -ErrorAction SilentlyContinue");
                Thread.Sleep(3000);
                RunPS("Update-HostStorageCache");
                Thread.Sleep(3000);
                Log($"  Disk {diskNum} online");

                // 恢复存储池数据到新位置（直接打开分区设备写入）
                Log("\n[6.6/8] Restoring pool data to new location...");
                long newPoolOffset2 = (long)newPoolStart * 512;
                Log($"  New pool offset: {newPoolOffset2 / 1024 / 1024}MB");
                // 找到存储池分区号
                string partInfo = RunPS($"Get-Partition -DiskNumber {diskNum} | ? {{$_.Type -eq 'Unknown' -and $_.Size -gt 1GB}} | select -First 1 -ExpandProperty PartitionNumber");
                int partNum = 0;
                if (!int.TryParse(partInfo.Trim(), out partNum))
                {
                    // 尝试通过大小查找
                    partInfo = RunPS($"Get-Partition -DiskNumber {diskNum} | Sort-Object Size -Descending | select -First 1 -ExpandProperty PartitionNumber");
                    int.TryParse(partInfo.Trim(), out partNum);
                }
                Log($"  Pool partition number: {partNum}");
                // 使用分区设备路径打开
                string partPath = $"\\\\?\\GLOBALROOT\\Device\\Harddisk{diskNum}\\Partition{partNum}";
                Log($"  Opening: {partPath}");
                using (var src = File.OpenRead(backupFile))
                using (var dst = OpenPhysicalDiskPath(partPath))
                {
                    byte[] buf = new byte[64 * 1024 * 1024];
                    long remaining = poolSize;
                    while (remaining > 0)
                    {
                        int toRead = (int)Math.Min(buf.Length, remaining);
                        int read = src.Read(buf, 0, toRead);
                        if (read <= 0) break;
                        dst.Write(buf, 0, read);
                        remaining -= read;
                    }
                    dst.Flush();
                }
                Log("  Pool data restored");
                try { File.Delete(backupFile); } catch { }

                // Step 7: Format front partitions
                Log("\n[7/8] Formatting front partitions...");
                Thread.Sleep(3000);
                RunPS("Update-HostStorageCache");
                Thread.Sleep(3000);
                foreach (var fp in FrontPartitions)
                {
                    if (string.IsNullOrEmpty(fp.FileSystem)) continue;
                    string fs = fp.FileSystem.ToUpper();
                    string typeFilter = fs == "FAT32" ? "System" : "Basic";
                    string script = $"Get-Partition -DiskNumber {diskNum} | ? {{$_.Type -eq '{typeFilter}'}} | select -First 1 | Format-Volume -FileSystem {fs} -NewFileSystemLabel '{fp.Name}' -Confirm:$false -Force -ErrorAction SilentlyContinue | Out-Null";
                    RunPS(script);
                    Log($"  Formatted: {fp.Name} as {fs}");
                }

                // Step 8: Verify
                Log("\n[8/8] Verifying storage pool...");
                Thread.Sleep(5000);
                RunPS("Get-StorageProvider -ErrorAction SilentlyContinue | Out-Null");
                Thread.Sleep(5000);

                string verify = RunPS($"$p=Get-StoragePool -FriendlyName {PoolName} -ErrorAction SilentlyContinue | select -First 1;" +
                    "if($p){Write-Host 'SUCCESS';Write-Host \"Size:$($p.Size)\";" +
                    "$v=Get-VirtualDisk -ErrorAction SilentlyContinue;if($v){Write-Host \"VDisks:$($v.Count)\";$v|%{Write-Host \"  $($_.FriendlyName) $($_.Size) $($_.HealthStatus)\"};" +
                    "$v|Connect-VirtualDisk -ErrorAction SilentlyContinue;Start-Sleep 3;" +
                    "Get-Disk|?{$_.BusType -eq 'Spaces'}|%{Write-Host \"  Spaces:$($_.Number) $($_.FriendlyName) $($_.Size)\"}}}else{Write-Host 'FAILED'}");
                Log(verify);

                Log("\n=== Native Storage Pool creation complete ===");
                return verify.Contains("SUCCESS");
            }
            catch (Exception ex)
            {
                Log($"FATAL: {ex}");
                return false;
            }
            finally
            {
                // 确保VHD被卸载
                try
                {
                    RunPS("Get-VirtualDisk -ErrorAction SilentlyContinue | Disconnect-VirtualDisk -Confirm:$false -ErrorAction SilentlyContinue");
                    Thread.Sleep(1000);
                    RunPS($"Dismount-VHD -Path '{VhdPath}' -ErrorAction SilentlyContinue");
                    Log("  [finally] VHD dismounted");
                }
                catch { }
                // 清理备份文件
                try
                {
                    var backupFiles = Directory.GetFiles(Path.GetDirectoryName(VhdPath)!, "nspc_backup_*.bin");
                    foreach (var f in backupFiles) { try { File.Delete(f); } catch { } }
                }
                catch { }
            }
        }

        private int GetDiskNumber()
        {
            for (int i = 0; i < 15; i++)
            {
                string info = RunPS($"$d=Get-Disk | ? {{$_.Location -eq '{VhdPath.Replace("\\", "\\\\")}'}} | select -First 1; if($d){{$d.Number}}else{{''}}");
                if (string.IsNullOrWhiteSpace(info))
                    info = RunPS($"$d=Get-Disk | ? {{$_.Location -like '*{Path.GetFileName(VhdPath)}*'}} | select -First 1; if($d){{$d.Number}}else{{''}}");
                if (!string.IsNullOrWhiteSpace(info) && int.TryParse(info.Trim(), out int num)) return num;
                Thread.Sleep(3000);
            }
            return -1;
        }

        private string RunPS(string script)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" + script.Replace("\"", "\\\"") + "\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var p = Process.Start(psi);
                string output = p.StandardOutput.ReadToEnd();
                string error = p.StandardError.ReadToEnd();
                p.WaitForExit();
                return output + (string.IsNullOrEmpty(error) ? "" : "\nERR: " + error);
            }
            catch (Exception ex) { return "RunPS exception: " + ex.Message; }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
        private const uint FILE_FLAG_WRITE_THROUGH = 0x80000000;

        private FileStream OpenPhysicalDiskForWrite(int diskNumber)
        {
            string path = $"\\\\.\\PhysicalDrive{diskNumber}";
            return OpenPhysicalDiskPath(path);
        }

        private FileStream OpenPhysicalDiskPath(string path)
        {
            IntPtr handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH, IntPtr.Zero);
            if (handle == new IntPtr(-1))
                throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), $"Failed to open {path}");
            return new FileStream(handle, FileAccess.ReadWrite);
        }
    }

    public class FrontPartition
    {
        public string Name { get; set; }
        public long SizeBytes { get; set; }
        public Guid TypeGuid { get; set; }
        public string FileSystem { get; set; } // "NTFS", "FAT32", or null for no format
    }

    public class VirtualDiskSpec
    {
        public string Name { get; set; }
        public long SizeBytes { get; set; }
    }
}
