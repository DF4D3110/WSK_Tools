using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Serialization;
using Microsoft.Win32.SafeHandles;
using Microsoft.WindowsPhone.Imaging;

namespace ImageStorageHelper
{
    class Program
    {
        #region P/Invoke NativeImaging
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        delegate void LogFunction(uint level, string message);

        enum LogLevel : uint { levelError = 0, levelWarning = 1, levelInfo = 2, levelDebug = 3 }

        [StructLayout(LayoutKind.Explicit)]
        struct STORE_ID { [FieldOffset(0)] public uint StoreType; [FieldOffset(4)] public Guid StoreId_GPT; [FieldOffset(4)] public uint StoreId_MBR; }

        [DllImport("ImageStorageService.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "CreateImageStorageService")]
        static extern int CreateImageStorageServiceNative(out IntPtr serviceHandle, LogFunction logError, uint storeIdsCount, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] STORE_ID[] storeIds);
        [DllImport("ImageStorageService.dll", CallingConvention = CallingConvention.StdCall)]
        static extern void CloseImageStorageService(IntPtr service);
        [DllImport("ImageStorageService.dll", CallingConvention = CallingConvention.StdCall)]
        static extern void SetLoggingFunction(IntPtr service, LogLevel level, LogFunction logFunction);
        [DllImport("ImageStorageService.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateStoragePool")]
        static extern int CreateStoragePoolNative(IntPtr service, IntPtr storeHandle, string poolName, ref Guid poolId);
        [DllImport("ImageStorageService.dll", CharSet = CharSet.Unicode, EntryPoint = "SetStoragePoolName")]
        static extern int SetStoragePoolNameNative(IntPtr service, Guid poolId, string poolName);
        [DllImport("ImageStorageService.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateStorageSpace")]
        static extern int CreateStorageSpaceNative(IntPtr service, Guid poolId, string spaceName, string spaceDescription, uint capacityInGB, ref Guid spaceId, out IntPtr diskHandle);
        #endregion

        #region Win32
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, IntPtr lpInBuffer, uint nInBufferSize, [Out] byte[] lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);
        const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3, FILE_ATTRIBUTE_NORMAL = 0x80;
        const uint IOCTL_STORAGE_GET_DEVICE_NUMBER = 0x2D1080;
        [StructLayout(LayoutKind.Sequential)]
        struct STORAGE_DEVICE_NUMBER { public uint DeviceType; public uint DeviceNumber; public uint PartitionNumber; }
        #endregion

        static IntPtr _service;
        static LogFunction _logError;
        static readonly object _consoleLock = new object();
        static int _lastProgressLen = 0;

        static void Log(string msg) { lock (_consoleLock) { Console.WriteLine("[{0:HH:mm:ss}] {1}", DateTime.Now, msg); } }

        static void ShowProgress(int percent, string phase)
        {
            lock (_consoleLock)
            {
                if (percent < 0) percent = 0; if (percent > 100) percent = 100;
                int barWidth = 30;
                int filled = (int)(barWidth * percent / 100.0);
                string bar = new string('█', filled) + new string('░', barWidth - filled);
                string line = string.Format("\r  [{0}] {1,3}%  {2}", bar, percent, phase.PadRight(30));
                if (line.Length < _lastProgressLen) line += new string(' ', _lastProgressLen - line.Length);
                _lastProgressLen = line.Length;
                Console.Write(line);
                if (percent >= 100) Console.WriteLine();
            }
        }

        static int Main(string[] args)
        {
            try
            {
                if (args.Length < 2) { Console.WriteLine("Usage: ImageStorageHelper <DeviceLayout.xml> <OutputVHDX> [GPT|MBR]"); return 1; }
                string deviceLayoutPath = args[0], outputVhdPath = args[1];
                Log("=== ImageStorageHelper v1.0 (imageapp-compatible flow) ===");
                Log("DeviceLayout: " + deviceLayoutPath);
                Log("Output: " + outputVhdPath);

                // === Phase 1: Parse XML ===
                ShowProgress(5, "Parsing DeviceLayout.xml");
                DeviceLayoutInputv2 layout = DeserializeDeviceLayout(deviceLayoutPath);
                int partCount = layout.Stores?.Sum(s => s.Partitions?.Length ?? 0) ?? 0;
                int spaceCount = layout.StoragePools?.Sum(p => p.Stores?.Length ?? 0) ?? 0;
                Log(string.Format("  SectorSize={0}, ChunkSize={1}, Partitions={2}, Spaces={3}", layout.SectorSize, layout.ChunkSize, partCount, spaceCount));
                ShowProgress(10, "XML parsed");

                // === Phase 2: Create VHD ===
                ShowProgress(12, "Creating virtual disk");
                string outputDir = Path.GetDirectoryName(Path.GetFullPath(outputVhdPath));
                if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
                if (File.Exists(outputVhdPath)) { try { File.Delete(outputVhdPath); } catch { } }

                ulong topBytes = 0;
                if (layout.Stores != null) foreach (var s in layout.Stores) if (s.Partitions != null) foreach (var p in s.Partitions) topBytes += (ulong)p.TotalSectors * layout.SectorSize;
                ulong totalBytes = topBytes + 30UL * 1073741824UL + 1073741824UL;
                Log(string.Format("  VHD size: {0:F2} GB (top-level {1:F2} GB + pool 30 GB)", totalBytes / 1073741824.0, topBytes / 1073741824.0));

                RunPS(string.Format("New-VHD -Path '{0}' -SizeBytes {1} -Dynamic | Out-Null", outputVhdPath, totalBytes));
                ShowProgress(20, "Virtual disk created");

                // === Phase 3: Mount VHD ===
                ShowProgress(22, "Mounting virtual disk");
                RunPS(string.Format("Mount-VHD -Path '{0}' -PassThru | Out-Null", outputVhdPath));
                int diskNumber = GetDiskNumber(outputVhdPath);
                Log("  Disk number: " + diskNumber);
                ShowProgress(25, "Disk mounted");

                // === Phase 4: Partition disk (diskpart native - fast) ===
                ShowProgress(27, "Partitioning disk (" + partCount + " partitions)");
                PartitionDiskFast(diskNumber, layout);
                int actualParts = RunPSGetInt(string.Format("(Get-Partition -DiskNumber {0}).Count", diskNumber));
                Log(string.Format("  Created {0} partitions", actualParts));
                ShowProgress(55, "Disk partitioned");

                // === Phase 5: Create storage pool ===
                ShowProgress(57, "Creating storage pool");
                string physicalDrive = @"\\.\PhysicalDrive" + diskNumber;
                _logError = new LogFunction((l, m) => { if (l == 0) Log("  [NATIVE ERR] " + m); });
                STORE_ID[] ids = new STORE_ID[1]; ids[0].StoreType = 1; ids[0].StoreId_GPT = Guid.NewGuid();
                int hr = CreateImageStorageServiceNative(out _service, _logError, 1, ids);
                if (hr < 0 || _service == IntPtr.Zero) { Log("  ERROR: CreateImageStorageService failed 0x" + hr.ToString("X8")); return 3; }
                SetLoggingFunction(_service, LogLevel.levelWarning, new LogFunction((l, m) => { }));
                SetLoggingFunction(_service, LogLevel.levelInfo, new LogFunction((l, m) => { }));

                SafeFileHandle diskHandle = CreateFile(physicalDrive, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
                if (diskHandle.IsInvalid) { Log("  ERROR: CreateFile failed " + Marshal.GetLastWin32Error()); return 4; }

                string poolName = layout.StoragePools?[0]?.Name ?? "OSPool";
                Guid poolId = Guid.Empty;
                hr = CreateStoragePoolNative(_service, diskHandle.DangerousGetHandle(), poolName, ref poolId);
                if (hr < 0) Log("  WARNING: CreateStoragePool returned 0x" + hr.ToString("X8"));
                else { Log("  Storage pool created: " + poolName); SetStoragePoolNameNative(_service, poolId, poolName); }
                ShowProgress(70, "Storage pool created");

                // === Phase 6: Create storage spaces (with partitioning) ===
                if (layout.StoragePools != null && layout.StoragePools.Length > 0 && layout.StoragePools[0].Stores != null)
                {
                    var spaces = layout.StoragePools[0].Stores;
                    for (int i = 0; i < spaces.Length; i++)
                    {
                        var sp = spaces[i];
                        uint capGB = sp.SizeInSectors > 0 ? (uint)((ulong)sp.SizeInSectors * layout.SectorSize / 1073741824UL) : 1;
                        if (capGB == 0) capGB = 1;
                        string sname = sp.StoreType ?? ("Space" + i);
                        ShowProgress(70 + (int)(20.0 * (i + 1) / spaces.Length), string.Format("Creating space {0}/{1}: {2}", i + 1, spaces.Length, sname));
                        Guid spaceId = Guid.Empty;
                        IntPtr spaceDisk;
                        hr = CreateStorageSpaceNative(_service, poolId, sname, "Created by WSK Tools", capGB, ref spaceId, out spaceDisk);
                        if (hr < 0) { Log(string.Format("  WARNING: CreateSpace '{0}' returned 0x{1:X8}", sname, hr)); }
                        else
                        {
                            int spaceDiskNum = GetDiskNumberFromHandle(spaceDisk);
                            if (spaceDiskNum >= 0)
                            {
                                Log(string.Format("  Space '{0}' -> PhysicalDrive{1}", sname, spaceDiskNum));
                                try { PartitionSpaceDisk(spaceDiskNum, sp, layout.SectorSize); }
                                catch (Exception pex) { Log("  WARNING: Partition space failed: " + pex.Message); }
                            }
                            else Log(string.Format("  WARNING: Could not get disk number for space '{0}'", sname));
                            SafeFileHandle sfh = new SafeFileHandle(spaceDisk, ownsHandle: true);
                            sfh.Close();
                        }
                    }
                }
                diskHandle.Close();
                ShowProgress(95, "Storage spaces created");

                // === Phase 7: Dismount ===
                ShowProgress(97, "Dismounting virtual disk");
                try { RunPS(string.Format("Dismount-VHD -Path '{0}'", outputVhdPath)); } catch { }
                if (_service != IntPtr.Zero) CloseImageStorageService(_service);
                ShowProgress(100, "Complete");

                Log("=== SUCCESS ===");
                Log("Output: " + outputVhdPath);
                return 0;
            }
            catch (Exception ex) { Log("=== FATAL ERROR ==="); Log(ex.ToString()); return 99; }
        }

        static DeviceLayoutInputv2 DeserializeDeviceLayout(string path)
        {
            XmlSerializer ser = new XmlSerializer(typeof(DeviceLayoutInputv2));
            using (FileStream fs = File.OpenRead(path)) return (DeviceLayoutInputv2)ser.Deserialize(fs);
        }

        static void PartitionDiskFast(int diskNumber, DeviceLayoutInputv2 layout)
        {
            // Step 1: Initialize GPT via PowerShell
            RunPS(string.Format("Clear-Disk -Number {0} -RemoveData -RemoveOEM -Confirm:$false; Initialize-Disk -Number {0} -PartitionStyle GPT -Confirm:$false", diskNumber));

            // Step 2: Use GptManager to directly write partition table (fast + reliable)
            string physicalDrive = @"\\.\PhysicalDrive" + diskNumber;
            int created = 0;
            using (var gpt = new GptManager(physicalDrive))
            {
                gpt.ClearAllPartitions();

                ulong currentLba = 2048; // 1MB alignment
                int slot = 0;

                // Add top-level partitions from physical stores
                if (layout.Stores != null) foreach (var store in layout.Stores)
                    {
                        if (store.Partitions == null) continue;
                        foreach (var part in store.Partitions)
                        {
                            string type = part.Type ?? "";
                            if (type.IndexOf("5708A6E0", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                            Guid partType = Guid.Parse(type.Trim('{', '}'));
                            ulong sectors = part.TotalSectors;
                            if (sectors < 2048) sectors = 2048; // min 1MB
                            ulong endLba = currentLba + sectors - 1;

                            gpt.SetPartition(slot++, partType, currentLba, endLba, part.Name ?? "");
                            currentLba = endLba + 1;
                            created++;
                        }
                    }

                // Storage pool partition (30GB thin)
                ulong poolEndLba = currentLba + (30UL * 1073741824UL / 512UL) - 1;
                gpt.SetPartition(slot++, Guid.Parse("5708A6E0-9001-4b99-b064-1fe564896bdb"), currentLba, poolEndLba, "OSPool");
                created++;

                Log("  Writing GPT with " + created + " partitions...");
                gpt.WriteGpt(true);
            }

            // Refresh disk so Windows sees new partitions
            RunPS("Update-Disk -Number " + diskNumber);
            Log("  GPT partition table written (" + created + " partitions)");
        }

        static int GetDiskNumberFromHandle(IntPtr handle)
        {
            byte[] outBuf = new byte[12];
            uint bytesReturned;
            if (DeviceIoControl(handle, IOCTL_STORAGE_GET_DEVICE_NUMBER, IntPtr.Zero, 0, outBuf, 12, out bytesReturned, IntPtr.Zero))
                return BitConverter.ToInt32(outBuf, 4);
            return -1;
        }

        static void PartitionSpaceDisk(int diskNumber, dynamic store, uint sectorSize)
        {
            if (store.Partitions == null || store.Partitions.Length == 0) return;
            string physicalDrive = @"\\.\PhysicalDrive" + diskNumber;
            int created = 0;
            using (var gpt = new GptManager(physicalDrive))
            {
                gpt.ClearAllPartitions();
                ulong currentLba = 2048;
                int slot = 0;
                foreach (var part in store.Partitions)
                {
                    string type = part.Type ?? "";
                    if (string.IsNullOrEmpty(type)) continue;
                    Guid partType;
                    if (!Guid.TryParse(type.Trim('{', '}'), out partType)) continue;
                    ulong sectors = part.TotalSectors;
                    if (sectors < 2048) sectors = 2048;
                    ulong endLba = currentLba + sectors - 1;
                    gpt.SetPartition(slot++, partType, currentLba, endLba, part.Name ?? "");
                    currentLba = endLba + 1;
                    created++;
                }
                if (created > 0) gpt.WriteGpt(true);
            }
            if (created > 0) { RunPS("Update-Disk -Number " + diskNumber); Log("    Space disk " + diskNumber + " partitioned (" + created + " partitions)"); }
        }

        static void RunPS(string script)
        {
            RunProcess("powershell.exe", "-NoProfile -Command \"" + script.Replace("\"", "\\\"") + "\"", 120000);
        }

        static int RunPSGetInt(string script)
        {
            ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", "-NoProfile -Command \"" + script.Replace("\"", "\\\"") + "\"");
            psi.RedirectStandardOutput = true; psi.UseShellExecute = false; psi.CreateNoWindow = true;
            Process p = Process.Start(psi);
            string outp = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            int r; int.TryParse(outp, out r); return r;
        }

        static int GetDiskNumber(string vhdPath)
        {
            return RunPSGetInt(string.Format("(Get-VHD -Path '{0}').DiskNumber", vhdPath));
        }

        static void RunProcess(string exe, string args, int timeout)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            Process p = Process.Start(psi);
            p.WaitForExit(timeout);
            if (!p.HasExited) try { p.Kill(); } catch { }
        }
    }
}
