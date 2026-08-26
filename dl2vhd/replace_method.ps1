$file = "E:\WSK_Tools\v1.0.3\dl2vhd\VhdCreator.cs"
$lines = Get-Content $file -Encoding UTF8

$startLine = -1
$endLine = -1
$braceCount = 0
$inMethod = $false

for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'public static string GenerateStoragePoolScript\(') {
        $startLine = $i
        $inMethod = $true
        $braceCount = 0
    }
    if ($inMethod) {
        $braceCount += ([regex]::Matches($lines[$i], '\{')).Count
        $braceCount -= ([regex]::Matches($lines[$i], '\}')).Count
        if ($braceCount -eq 0 -and $i -gt $startLine) {
            $endLine = $i
            break
        }
    }
}

Write-Host "Method found: start=$startLine, end=$endLine"
Write-Host "Start line: $($lines[$startLine])"
Write-Host "End line: $($lines[$endLine])"

$newMethod = @'
    public static string GenerateStoragePoolScript(DeviceLayoutInfo layout, string vhdPath, string scriptPath)
    {
        if (layout.StoragePools.Count == 0)
        {
            Console.WriteLine("ERROR: No StoragePool found");
            return "";
        }

        var pool = layout.StoragePools[0];
        var poolName = string.IsNullOrEmpty(pool.Name) ? "OSPool" : pool.Name;
        var sectorSize = layout.SectorSize > 0 ? layout.SectorSize : 512;

        long vhdSize = 0;
        var topStore = layout.Stores.FirstOrDefault();
        if (topStore != null && topStore.SizeInSectors > 0)
        {
            vhdSize = topStore.SizeInSectors * sectorSize;
        }
        else
        {
            long maxStoreSize = 0;
            foreach (var store in pool.Stores)
            {
                var sz = store.SizeInSectors * sectorSize;
                if (sz > maxStoreSize) maxStoreSize = sz;
            }
            vhdSize = maxStoreSize;
        }

        if (vhdSize <= 0)
        {
            Console.WriteLine("  WARNING: No valid size found, using default 4GB");
            vhdSize = 4L * 1024 * 1024 * 1024;
        }

        if (vhdSize < 1L * 1024 * 1024 * 1024) vhdSize = 1L * 1024 * 1024 * 1024;
        vhdSize = (vhdSize / (1024 * 1024)) * (1024 * 1024);

        Console.WriteLine($"  VHD size: {vhdSize} bytes ({vhdSize / 1024.0 / 1024 / 1024:F2} GB)");

        var vhdExt = Path.GetExtension(vhdPath).ToLowerInvariant();
        var isVhdx = vhdExt == ".vhdx";
        const string ospoolGuid = "5708A6E0-9001-4b99-b064-1fe564896bdb";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("#Requires -RunAsAdministrator");
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine("");
        sb.AppendLine($"$vhdPath = '{vhdPath}'");
        sb.AppendLine($"$poolName = '{poolName}'");
        sb.AppendLine($"$vhdSize = {vhdSize}");
        sb.AppendLine("");
        sb.AppendLine("Write-Host '=== Creating VHD ===' -ForegroundColor Cyan");
        sb.AppendLine("if (Test-Path $vhdPath) { Remove-Item $vhdPath -Force }");
'@

        if ($isVhdx) {
            $newMethod += '        sb.AppendLine("New-VHD -Path $vhdPath -SizeBytes $vhdSize -Dynamic | Out-Null");' + "`n"
        } else {
            $newMethod += '        sb.AppendLine("New-VHD -Path $vhdPath -SizeBytes $vhdSize -Dynamic -VHDFormat VHD | Out-Null");' + "`n"
        }

        $newMethod += @'
        sb.AppendLine("Write-Host 'VHD created.' -ForegroundColor Green");
        sb.AppendLine("");
        sb.AppendLine("Write-Host '=== Mounting VHD ===' -ForegroundColor Cyan");
        sb.AppendLine("Mount-VHD -Path $vhdPath -PassThru | Out-Null");
        sb.AppendLine("Start-Sleep -Seconds 2");
        sb.AppendLine("$disk = Get-Disk | Where-Object { $_.Location -eq $vhdPath } | Select-Object -First 1");
        sb.AppendLine("if (-not $disk) { Write-Host 'ERROR: VHD disk not found' -ForegroundColor Red; exit 1 }");
        sb.AppendLine("Write-Host \"Found disk: $($disk.Number)\"");
        sb.AppendLine("");
        sb.AppendLine("Write-Host '=== Initializing disk ===' -ForegroundColor Cyan");
        sb.AppendLine("Set-Disk -Number $disk.Number -IsOffline $false");
        sb.AppendLine("Set-Disk -Number $disk.Number -IsReadOnly $false");
        sb.AppendLine("Initialize-Disk -Number $disk.Number -PartitionStyle GPT -Confirm:$false");
        sb.AppendLine("Write-Host 'Disk initialized as GPT.'");
        sb.AppendLine("");
'@

        if ($topStore -ne $null) {
            $newMethod += '        sb.AppendLine("Write-Host ''=== Creating top-level partitions ==='' -ForegroundColor Cyan");' + "`n"
            $pIdx = 0
            foreach ($part in $topStore.Partitions) {
                $pIdx++
                $partName = if ([string]::IsNullOrEmpty($part.Name)) { "Partition$pIdx" } else { $part.Name }
                $partType = if ([string]::IsNullOrEmpty($part.Type)) { "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7" } else { $part.Type }
                $isOspool = $partType.Equals($ospoolGuid, [StringComparison]::OrdinalIgnoreCase)

                if ($isOspool) {
                    $newMethod += '        sb.AppendLine("# OSPool partition (uses remaining space)");' + "`n"
                    $newMethod += '        sb.AppendLine("Write-Host ''Creating OSPool partition...''");' + "`n"
                    $newMethod += "        sb.AppendLine(`"`$ospoolPart = New-Partition -DiskNumber `$disk.Number -UseMaximumSize -GptType '$ospoolGuid'`");" + "`n"
                    $newMethod += '        sb.AppendLine("Write-Host ''OSPool partition created.''");' + "`n"
                    $newMethod += '        sb.AppendLine("");' + "`n"
                } else {
                    $partSize = $part.TotalSectors * $sectorSize
                    if ($part.UseAllSpace -or $partSize -le 0) {
                        $newMethod += "        sb.AppendLine(`"# Partition $pIdx : $partName (UseAllSpace)`");" + "`n"
                        $newMethod += "        sb.AppendLine(`"`$p = New-Partition -DiskNumber `$disk.Number -UseMaximumSize -GptType '$partType'`");" + "`n"
                    } else {
                        $sizeMB = [math]::Round($partSize / 1MB, 1)
                        $newMethod += "        sb.AppendLine(`"# Partition $pIdx : $partName ($sizeMB MB)`");" + "`n"
                        $newMethod += "        sb.AppendLine(`"`$p = New-Partition -DiskNumber `$disk.Number -Size $partSize -GptType '$partType'`");" + "`n"
                    }
                    $newMethod += '        sb.AppendLine("Start-Sleep -Milliseconds 100");' + "`n"
                    $newMethod += '        sb.AppendLine("");' + "`n"
                }
            }
        }

        $newMethod += @'
        sb.AppendLine("Write-Host '=== Top-level partitions created ===' -ForegroundColor Green");
        sb.AppendLine("");
        sb.AppendLine("Write-Host '=== Partition layout ===' -ForegroundColor Cyan");
        sb.AppendLine("Get-Partition -DiskNumber $disk.Number | Select-Object PartitionNumber, Type, Size, Offset | Format-Table -AutoSize");
        sb.AppendLine("");
        sb.AppendLine("Write-Host 'Script complete. The main program will now create the storage pool using native API.' -ForegroundColor Yellow");
        sb.AppendLine("Write-Host 'Press any key to exit...'");
        sb.AppendLine("$null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')");

        File.WriteAllText(scriptPath, sb.ToString(), System.Text.Encoding.UTF8);
        Console.WriteLine($"  Script generated: {scriptPath}");
        return scriptPath;
    }
'@

$newLines = $lines[0..($startLine - 1)] + $newMethod + $lines[($endLine + 1)..($lines.Count - 1)]
Set-Content -Path $file -Value $newLines -Encoding UTF8
Write-Host "Method replaced successfully. New file has $($newLines.Count) lines."
