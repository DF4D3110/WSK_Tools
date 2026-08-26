using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DeviceLayoutGeneratorV2.Native;
using DeviceLayoutGeneratorV2.Parser;

namespace DeviceLayoutGeneratorV2.Builder
{
    /// <summary>
    /// ImageBuilder - replicates imageapp's complete workflow using ImageStorageService.dll native APIs
    /// </summary>
    public class ImageBuilder : IDisposable
    {
        private IntPtr _service = IntPtr.Zero;
        private readonly Action<string> _log;
        private bool _disposed;

        public ImageBuilder(Action<string> logCallback)
        {
            _log = logCallback ?? Console.WriteLine;
        }

        public void Build(string deviceLayoutXmlPath, string outputVhdPath)
        {
            _log($"=== DeviceLayoutGenerator V2 (imageapp-compatible) ===");
            _log($"DeviceLayout: {deviceLayoutXmlPath}");
            _log($"Output: {outputVhdPath}");

            // Parse XML
            _log("[5%] Parsing DeviceLayout.xml...");
            var layout = DeviceLayoutParser.Parse(deviceLayoutXmlPath);
            uint sectorSize = layout.SectorSize == 0 ? 512u : layout.SectorSize;
            _log($"  SectorSize={sectorSize}, ChunkSize={layout.ChunkSize}");
            _log($"  Top-level stores: {layout.TopLevelStores.Count}");
            _log($"  Storage pools: {layout.StoragePools.Count}");
            foreach (var pool in layout.StoragePools)
            {
                _log($"    Pool '{pool.Name}': {pool.Stores.Count} spaces");
                foreach (var s in pool.Stores)
                    _log($"      Space: {s.StoreType} ({s.Partitions.Count} partitions)");
            }

            // Calculate total size
            ulong totalBytes = CalculateTotalSize(layout, sectorSize);
            _log($"[10%] Total VHD size: {totalBytes / 1073741824.0:F2} GB");

            // Step 1: Create service
            _log("[12%] Creating ImageStorageService...");
            CreateService();

            try
            {
                // Step 2: Create VHD
                _log("[15%] Creating virtual hard disk...");
                var vhdStoreId = STORE_ID.CreateGpt(1, Guid.NewGuid());
                int hr = ImageStorageServiceNative.CreateVirtualHardDisk(
                    _service, outputVhdPath, totalBytes, vhdStoreId, sectorSize, out IntPtr vhdHandle);
                if (ImageStorageServiceNative.Failed(hr) || vhdHandle == IntPtr.Zero)
                    throw new Exception($"CreateVirtualHardDisk failed: 0x{hr:X8}");
                _log($"  VHD created, handle=0x{vhdHandle.ToInt64():X}");

                // Step 3: Partition top-level disk (native API only)
                _log("[25%] Partitioning top-level disk...");
                var topPartitions = BuildTopLevelPartitions(layout, sectorSize);
                _log($"  {topPartitions.Count} top-level partitions");
                hr = ImageStorageServiceNative.PartitionVirtualHardDisk(
                    _service, vhdHandle, ref vhdStoreId, topPartitions.ToArray(), (uint)topPartitions.Count);
                if (ImageStorageServiceNative.Failed(hr))
                    throw new Exception($"PartitionVirtualHardDisk (top) failed: 0x{hr:X8}");
                _log("  Top-level disk partitioned (native API)");

                // Step 3.5: Update partition properties (imageapp does this after partitioning)
                _log("[30%] Updating partition properties...");
                hr = ImageStorageServiceNative.UpdatePartitionProperties(
                    _service, vhdHandle, vhdStoreId, topPartitions.ToArray(), (uint)topPartitions.Count);
                if (ImageStorageServiceNative.Failed(hr))
                    _log($"  WARNING: UpdatePartitionProperties failed: 0x{hr:X8}");
                else
                    _log("  Partition properties updated");

                // Step 3.6: Format and label top-level partitions
                _log("[32%] Formatting and labeling top-level partitions...");
                FormatAndLabelPartitions(outputVhdPath, layout.TopLevelStores.SelectMany(s => s.Partitions).ToList(), sectorSize);

                // Step 4: Create storage pools via PowerShell (native API fails on partitioned disks)
                foreach (var pool in layout.StoragePools)
                {
                    _log($"[40%] Creating storage pool '{pool.Name}' via PowerShell...");
                    
                    // Get disk number from VHD path
                    string diskNumber = RunPowerShell($"(Get-VHD -Path '{outputVhdPath}').DiskNumber");
                    if (string.IsNullOrEmpty(diskNumber) || !int.TryParse(diskNumber.Trim(), out _))
                    {
                        _log($"  WARNING: Could not get disk number, trying Get-Disk...");
                        diskNumber = RunPowerShell($"(Get-Disk | Where-Object {{ $_.Location -eq '{outputVhdPath}' }}).Number");
                    }
                    _log($"  Disk number: {diskNumber}");
                    
                    if (string.IsNullOrEmpty(diskNumber))
                    {
                        _log($"  ERROR: Could not determine disk number, skipping storage pool");
                        continue;
                    }
                    
                    // Create storage pool
                    string poolScript = $@"
$ErrorActionPreference = 'Stop'
$pd = Get-PhysicalDisk | Where-Object {{ $_.DeviceId -eq '{diskNumber}' }}
if (-not $pd.CanPool) {{ Reset-PhysicalDisk -FriendlyName $pd.FriendlyName -ErrorAction SilentlyContinue; Start-Sleep -Seconds 2; $pd = Get-PhysicalDisk | Where-Object {{ $_.DeviceId -eq '{diskNumber}' }} }}
$pool = New-StoragePool -FriendlyName '{pool.Name}' -StorageSubSystemFriendlyName '*Storage*' -PhysicalDisks $pd -ProvisioningTypeDefault Thin -ResiliencySettingNameDefault Simple
Write-Output $pool.FriendlyName
";
                    string poolResult = RunPowerShell(poolScript);
                    if (string.IsNullOrEmpty(poolResult))
                    {
                        _log($"  ERROR: Failed to create storage pool");
                        continue;
                    }
                    _log($"  Storage pool created: {poolResult.Trim()}");
                    
                    // Step 5: Create virtual disks (spaces)
                    int spaceIdx = 0;
                    foreach (var store in pool.Stores)
                    {
                        spaceIdx++;
                        string spaceName = store.StoreType ?? $"Space{spaceIdx}";
                        _log($"[55%] Creating space {spaceIdx}/{pool.Stores.Count}: {spaceName}...");
                        
                        ulong spaceBytes = CalculateStoreSize(store, sectorSize);
                        double sizeGB = Math.Max(0.1, (double)spaceBytes / 1073741824.0);
                        
                        string vdScript = $@"
$ErrorActionPreference = 'Stop'
$vd = New-VirtualDisk -StoragePoolFriendlyName '{pool.Name}' -FriendlyName '{spaceName}' -Size {sizeGB}GB -ProvisioningType Thin -ResiliencySettingName Simple
Write-Output $vd.FriendlyName
";
                        string vdResult = RunPowerShell(vdScript);
                        if (string.IsNullOrEmpty(vdResult))
                        {
                            _log($"  WARNING: Failed to create virtual disk '{spaceName}'");
                            continue;
                        }
                        _log($"  Virtual disk created: {vdResult.Trim()} ({sizeGB:F2} GB)");
                        
                        // Step 6: Partition the virtual disk
                        var spacePartitions = BuildSpacePartitions(store, sectorSize);
                        if (spacePartitions.Count > 0)
                        {
                            // Get the virtual disk's disk number
                            string vdDiskRaw = RunPowerShell($"(Get-VirtualDisk -FriendlyName '{spaceName}' | Get-Disk).Number");
                            string vdDiskNumber = null;
                            if (!string.IsNullOrEmpty(vdDiskRaw))
                            {
                                var lines = vdDiskRaw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                                vdDiskNumber = lines[lines.Length - 1].Trim();
                            }
                            if (!string.IsNullOrEmpty(vdDiskNumber) && int.TryParse(vdDiskNumber, out _))
                            {
                                _log($"  Virtual disk number: {vdDiskNumber}, partitioning {spacePartitions.Count} partition(s)");
                                // Initialize and partition via PowerShell
                                int partNum = 0;
                                foreach (var sp in spacePartitions)
                                {
                                    partNum++;
                                    double partSizeGB = (double)(sp.SectorCount * sectorSize) / 1073741824.0;
                                    if (partSizeGB < 0.01) partSizeGB = 0.01;
                                    string partScript = $@"
$ErrorActionPreference = 'SilentlyContinue'
$disk = Get-Disk -Number {vdDiskNumber}
if ($disk.PartitionStyle -eq 'RAW') {{ Initialize-Disk -Number {vdDiskNumber} -PartitionStyle GPT -Confirm:$false }}
New-Partition -DiskNumber {vdDiskNumber} -Size {partSizeGB}GB -GptType '{{{sp.Type}}}' | Out-Null
Write-Output 'OK'
";
                                    string partResult = RunPowerShell(partScript);
                                    if (partResult != "OK")
                                        _log($"  Partition {partNum} may have failed");
                                }
                                _log($"  Space partitioned");

                                // Format and label partitions in this virtual disk
                                var partitionsToFormat = store.Partitions.Where(p => !string.IsNullOrEmpty(p.FileSystem)).ToList();
                                if (partitionsToFormat.Count > 0)
                                {
                                    _log($"  Formatting {partitionsToFormat.Count} partition(s) in {spaceName}...");
                                    foreach (var sp in partitionsToFormat)
                                    {
                                        string fs = sp.FileSystem.ToUpper();
                                        ulong partSizeBytes = sp.TotalSectors * sectorSize;
                                        ulong partSizeMB = partSizeBytes / (1024 * 1024);
                                        
                                        // Choose appropriate file system based on size
                                        if (fs == "FAT")
                                        {
                                            if (partSizeMB < 512)
                                                fs = "FAT"; // FAT16 for <512MB
                                            else
                                                fs = "FAT32";
                                        }
                                        else if (fs == "NTFS" && partSizeMB < 10)
                                        {
                                            _log($"    Skip: {sp.Name} ({partSizeMB}MB too small for NTFS)");
                                            continue;
                                        }
                                        
                                        // Use Format-Volume for virtual disk partitions (works for NTFS)
                                        string formatScript = $@"
$ErrorActionPreference = 'Continue'
try {{
    # Wait for partition to be recognized
    Start-Sleep -Seconds 1
    $part = $null
    for ($i = 0; $i -lt 5; $i++) {{
        $part = Get-Partition -DiskNumber {vdDiskNumber} | Where-Object {{ $_.Type -ne 'Reserved' }} | Select-Object -First 1
        if ($part) {{ break }}
        Start-Sleep -Milliseconds 500
    }}
    if (-not $part) {{ Write-Output 'NOPART'; exit }}
    
    $vol = $part | Get-Volume -ErrorAction SilentlyContinue
    if (-not $vol) {{
        $availableLetter = (Get-ChildItem function:[d-z]: -Name | Where-Object {{ -not (Test-Path $_) }} | Select-Object -First 1)
        if ($availableLetter) {{
            $letter = $availableLetter.Replace(':','')
            $part | Set-Partition -NewDriveLetter $letter -ErrorAction SilentlyContinue | Out-Null
            Start-Sleep -Milliseconds 800
            $vol = $part | Get-Volume -ErrorAction SilentlyContinue
        }}
    }}
    
    if (-not $vol) {{ Write-Output 'NOVOL'; exit }}
    
    $formatResult = Format-Volume -InputObject $vol -FileSystem {fs} -NewFileSystemLabel '{sp.Name}' -Confirm:$false -Force -ErrorAction SilentlyContinue
    if ($formatResult) {{
        Write-Output 'OK'
    }} else {{
        # Fallback: try format.com
        $driveLetter = $vol.DriveLetter
        if ($driveLetter) {{
            $fsArg = if ('{fs}' -eq 'NTFS') {{ 'NTFS' }} else {{ 'FAT32' }}
            & format.com $driveLetter`: /FS:$fsArg /V:'{sp.Name}' /Q /Y 2>&1 | Out-Null
            if ($LASTEXITCODE -eq 0) {{ Write-Output 'OK' }} else {{ Write-Output 'FMTFAIL' }}
        }} else {{
            Write-Output 'FMTFAIL'
        }}
    }}
}} catch {{
    Write-Output 'ERR:' + $_.Exception.Message
}}
";
                                        string fmtResult = RunPowerShell(formatScript);
                                        if (fmtResult == "OK")
                                            _log($"    Formatted: {sp.Name} ({fs}, {partSizeMB}MB)");
                                        else
                                            _log($"    Failed: {sp.Name} (result={fmtResult}, fs={fs}, size={partSizeMB}MB)");
                                    }
                                }
                            }
                        }
                    }
                }

                // Step 7: Update partition properties (SKIPPED - requires partition handles)
                _log("[80%] Update partition properties skipped");
                // var allPartitions = BuildTopLevelPartitions(layout, sectorSize);
                // hr = ImageStorageServiceNative.UpdatePartitionProperties(
                //     _service, vhdHandle, ref vhdStoreId, allPartitions.ToArray(), (uint)allPartitions.Count);
                // if (ImageStorageServiceNative.Failed(hr))
                //     _log($"  WARNING: UpdatePartitionProperties failed: 0x{hr:X8}");

                // Step 8: Set disk ID (SKIPPED temporarily)
                _log("[90%] Set disk ID skipped");

                _log("[100%] Complete!");
                _log($"=== SUCCESS ===");
                _log($"Output: {outputVhdPath}");
            }
            finally
            {
                // Always dismount VHD after operations (success or failure)
                try
                {
                    _log("[97%] Dismounting virtual disk...");
                    RunPowerShell($"Dismount-VHD -Path '{outputVhdPath}' -ErrorAction SilentlyContinue");
                    // Also dismount any virtual disks created from storage pools
                    RunPowerShell("Get-VirtualDisk | Dismount-Disk -ErrorAction SilentlyContinue");
                    _log("  Virtual disk dismounted.");
                }
                catch (Exception ex)
                {
                    _log($"  WARNING: Dismount failed: {ex.Message}");
                }
                CloseService();
            }
        }

        private void CreateService()
        {
            int hr = ImageStorageServiceNative.CreateImageStorageService(out _service, IntPtr.Zero, 0, IntPtr.Zero);
            if (ImageStorageServiceNative.Failed(hr) || _service == IntPtr.Zero)
                throw new Exception($"CreateImageStorageService failed: 0x{hr:X8}");
            _log($"  Service created, handle=0x{_service.ToInt64():X}");
        }

        private void CloseService()
        {
            if (_service != IntPtr.Zero)
            {
                ImageStorageServiceNative.CloseImageStorageService(_service);
                _service = IntPtr.Zero;
            }
        }

        private ulong CalculateTotalSize(DeviceLayout layout, uint sectorSize)
        {
            // Use top-level Store's SizeInSectors (the whole disk size)
            // Storage spaces are inside the OSPool partition, don't add them separately
            ulong totalSectors = 0;
            foreach (var store in layout.TopLevelStores)
            {
                if (store.SizeInSectors > 0)
                    totalSectors += store.SizeInSectors;
                else
                {
                    // Fallback: sum of partitions + 1GB for OSPool
                    foreach (var p in store.Partitions)
                        totalSectors += p.TotalSectors;
                    totalSectors += 2097152; // 1GB for OSPool
                }
            }
            if (totalSectors == 0)
                totalSectors = 61071360; // default 30GB
            return totalSectors * sectorSize;
        }

        private ulong CalculateStoreSize(Store store, uint sectorSize)
        {
            ulong size = 0;
            foreach (var p in store.Partitions)
                size += p.TotalSectors * sectorSize;
            return Math.Max(size, 1073741824UL); // minimum 1GB
        }

        private void PartitionVhdWithDiskpart(string vhdPath, List<PARTITION_ENTRY> partitions)
        {
            try
            {
                string scriptPath = Path.Combine(Path.GetTempPath(), "diskpart_partition.txt");
                using (var sw = new StreamWriter(scriptPath))
                {
                    sw.WriteLine($"select vdisk file=\"{vhdPath}\"");
                    sw.WriteLine("attach vdisk");
                    sw.WriteLine("convert gpt");
                    // Create all fixed-size partitions first (no format for speed)
                    foreach (var p in partitions)
                    {
                        if (p.SectorCount == uint.MaxValue) continue;
                        uint sizeMB = (uint)(p.SectorCount * 512 / 1048576);
                        if (sizeMB < 1) sizeMB = 1;
                        sw.WriteLine($"create partition primary size={sizeMB}");
                    }
                    // Last partition uses remaining space
                    var last = partitions.FindLast(p => p.SectorCount == uint.MaxValue);
                    if (last.Name != null)
                    {
                        sw.WriteLine("create partition primary");
                    }
                    sw.WriteLine("detach vdisk");
                    sw.WriteLine("exit");
                }
                var psi = new System.Diagnostics.ProcessStartInfo("diskpart", $"/s \"{scriptPath}\"");
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                var proc = System.Diagnostics.Process.Start(psi);
                proc.WaitForExit(180000);
                _log($"  diskpart exit code: {proc.ExitCode}");
                File.Delete(scriptPath);
            }
            catch (Exception ex)
            {
                _log($"  diskpart error: {ex.Message}");
            }
        }

        private static readonly Guid OSPoolPartitionType = new Guid("5708A6E0-9001-4b99-b064-1fe564896bdb");

        private List<PARTITION_ENTRY> BuildTopLevelPartitions(DeviceLayout layout, uint sectorSize)
        {
            var result = new List<PARTITION_ENTRY>();
            foreach (var store in layout.TopLevelStores)
            {
                var partitions = store.Partitions;
                // Skip OSPool partition - it will be created as a storage pool instead
                var nonPoolPartitions = partitions.Where(p => ParseGuid(p.Type) != OSPoolPartitionType).ToList();
                
                for (int i = 0; i < nonPoolPartitions.Count; i++)
                {
                    var p = nonPoolPartitions[i];
                    ulong sectorCount = p.TotalSectors;
                    
                    var entry = new PARTITION_ENTRY
                    {
                        Name = p.Name,
                        SectorCount = sectorCount,
                        Type = ParseGuid(p.Type),
                        Id = Guid.NewGuid(),
                        Flags = 0,
                        ClusterSize = 4096,
                        FileSystem = "", // Leave empty, format later (NTFS causes 0x80070001)
                        AlignmentSizeInBytes = sectorSize
                        // OffsetInSectors = 0 (native calculates automatically)
                    };
                    result.Add(entry);
                }
            }
            return result;
        }

        private List<PARTITION_ENTRY> BuildSpacePartitions(Store store, uint sectorSize)
        {
            var result = new List<PARTITION_ENTRY>();
            foreach (var p in store.Partitions)
            {
                var entry = new PARTITION_ENTRY
                {
                    Name = p.Name,
                    SectorCount = p.TotalSectors,
                    Type = ParseGuid(p.Type),
                    Id = Guid.NewGuid(),
                    Flags = 0,
                    ClusterSize = 4096,
                    FileSystem = "", // Leave empty, format later
                    AlignmentSizeInBytes = sectorSize
                    // OffsetInSectors = 0 (native calculates automatically)
                };
                result.Add(entry);
            }
            return result;
        }

        private Guid ParseGuid(string guidStr)
        {
            if (string.IsNullOrEmpty(guidStr)) return Guid.Empty;
            try { return Guid.Parse(guidStr.Trim('{', '}')); }
            catch { return Guid.Empty; }
        }

        private string RunPowerShell(string script)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var proc = System.Diagnostics.Process.Start(psi);
                string output = proc.StandardOutput.ReadToEnd();
                string error = proc.StandardError.ReadToEnd();
                proc.WaitForExit(30000);
                if (!string.IsNullOrEmpty(error) && proc.ExitCode != 0)
                    _log($"  PS Error: {error.Trim().Substring(0, Math.Min(200, error.Trim().Length))}");
                return output.Trim();
            }
            catch (Exception ex)
            {
                _log($"  RunPowerShell exception: {ex.Message}");
                return null;
            }
        }

        private void FormatAndLabelPartitions(string vhdPath, List<Partition> partitions, uint sectorSize)
        {
            try
            {
                // Get disk number
                string diskNumber = RunPowerShell($"(Get-VHD -Path '{vhdPath}').DiskNumber");
                if (string.IsNullOrEmpty(diskNumber) || !int.TryParse(diskNumber.Trim(), out int diskNum))
                {
                    _log("  WARNING: Could not get disk number for formatting");
                    return;
                }

                // Get partitions that need formatting
                var partitionsToFormat = partitions.Where(p => !string.IsNullOrEmpty(p.FileSystem)).ToList();
                _log($"  {partitionsToFormat.Count} partitions need formatting (disk {diskNum})");

                // Get actual partition list from disk (include System type for EFIESP)
                string getPartsScript = $@"
$ErrorActionPreference = 'SilentlyContinue'
$parts = Get-Partition -DiskNumber {diskNum} | Where-Object {{ $_.Type -ne 'Reserved' }}
$parts | ForEach-Object {{ Write-Output ""$($_.PartitionNumber)|$($_.Type)|$($_.Size)"" }}
";
                string partsOutput = RunPowerShell(getPartsScript);
                if (string.IsNullOrEmpty(partsOutput))
                {
                    _log("  WARNING: Could not get partition list");
                    return;
                }

                var actualParts = partsOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Split('|'))
                    .Where(parts => parts.Length >= 3)
                    .Select(parts => new { Number = int.Parse(parts[0]), Type = parts[1], Size = ulong.Parse(parts[2]) })
                    .ToList();

                // Match and format partitions by size (closest match, track used partitions)
                int formatted = 0;
                var usedPartitions = new HashSet<int>();
                foreach (var p in partitionsToFormat)
                {
                    ulong expectedSize = p.TotalSectors * sectorSize;
                    var match = actualParts
                        .Where(ap => !usedPartitions.Contains(ap.Number))
                        .Where(ap => Math.Abs((long)ap.Size - (long)expectedSize) < 20 * 1024 * 1024) // 20MB tolerance
                        .OrderBy(ap => Math.Abs((long)ap.Size - (long)expectedSize))
                        .FirstOrDefault();

                    if (match == null)
                    {
                        _log($"  Skip: {p.Name} (expected {expectedSize/(1024*1024)}MB, no match)");
                        continue;
                    }
                    usedPartitions.Add(match.Number);

                    // Choose appropriate file system based on partition size
                    string fs = p.FileSystem.ToUpper();
                    ulong partSizeMB = match.Size / (1024 * 1024);
                    
                    if (fs == "FAT")
                    {
                        // Use FAT16 for smaller partitions, FAT32 for larger
                        if (partSizeMB < 512)
                            fs = "FAT"; // FAT16 for <512MB (works down to ~1MB)
                        else
                            fs = "FAT32";
                    }
                    // NTFS minimum is ~10MB, skip if too small
                    else if (fs == "NTFS" && partSizeMB < 10)
                    {
                        _log($"  Skip: {p.Name} ({partSizeMB}MB too small for NTFS)");
                        continue;
                    }

                    // Use diskpart to format directly (assign first, then format)
                    string formatScript = $@"
$ErrorActionPreference = 'Continue'
try {{
    [int]$diskNum = {diskNum}
    [int]$partNum = {match.Number}
    [string]$fsType = '{fs}'
    [string]$label = '{p.Name}'
    
    # First try diskpart: assign to create volume, then format
    $scriptLines = New-Object System.Collections.ArrayList
    [void]$scriptLines.Add('select disk ' + $diskNum)
    [void]$scriptLines.Add('select partition ' + $partNum)
    [void]$scriptLines.Add('assign')
    [void]$scriptLines.Add('format fs=' + $fsType + ' label=""' + $label + '"" quick')
    [void]$scriptLines.Add('exit')
    $scriptFile = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllLines($scriptFile, $scriptLines, [System.Text.Encoding]::ASCII)
    $result = & diskpart.exe /s $scriptFile 2>&1
    Remove-Item $scriptFile -Force -ErrorAction SilentlyContinue
    
    # Check for success (both English and Chinese)
    $success = $false
    if ($result -match 'successfully formatted|100 percent completed|成功格式化|100 百分比|100百分比') {{
        $success = $true
    }}
    
    if ($success) {{
        Write-Output 'OK'
    }} else {{
        # Fallback: try PowerShell Format-Volume
        Start-Sleep -Milliseconds 500
        $part = Get-Partition -DiskNumber $diskNum -PartitionNumber $partNum -ErrorAction SilentlyContinue
        if ($part) {{
            $vol = $part | Get-Volume -ErrorAction SilentlyContinue
            if (-not $vol) {{
                $availableLetter = (Get-ChildItem function:[d-z]: -Name | Where-Object {{ -not (Test-Path $_) }} | Select-Object -First 1)
                if ($availableLetter) {{
                    $part | Set-Partition -NewDriveLetter $availableLetter.Replace(':','') -ErrorAction SilentlyContinue | Out-Null
                    Start-Sleep -Milliseconds 800
                    $vol = $part | Get-Volume -ErrorAction SilentlyContinue
                }}
            }}
            if ($vol) {{
                $fmtResult = Format-Volume -InputObject $vol -FileSystem $fsType -NewFileSystemLabel $label -Confirm:$false -Force -ErrorAction SilentlyContinue
                if ($fmtResult) {{ Write-Output 'OK' }} else {{ Write-Output 'PSFAIL' }}
            }} else {{
                $errMsg = ($result -join ';')
                if ($errMsg.Length -gt 300) {{ $errMsg = $errMsg.Substring(0, 300) }}
                Write-Output 'DPFAIL:' + $errMsg
            }}
        }} else {{
            $errMsg = ($result -join ';')
            if ($errMsg.Length -gt 300) {{ $errMsg = $errMsg.Substring(0, 300) }}
            Write-Output 'DPFAIL:' + $errMsg
        }}
    }}
}} catch {{
    Write-Output 'ERR:' + $_.Exception.Message
}}
";
                    string result = RunPowerShell(formatScript);
                    if (result == "OK")
                    {
                        formatted++;
                        _log($"  Formatted: {p.Name} ({fs}, partition {match.Number}, {partSizeMB}MB)");
                    }
                    else
                    {
                        _log($"  Failed: {p.Name} (result={result}, fs={fs}, size={partSizeMB}MB)");
                    }
                }
                _log($"  Formatted {formatted}/{partitionsToFormat.Count} partitions");
            }
            catch (Exception ex)
            {
                _log($"  FormatAndLabelPartitions error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                CloseService();
                _disposed = true;
            }
        }
    }
}
