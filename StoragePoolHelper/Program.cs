using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using DeviceLayoutExchanger;

namespace StoragePoolHelper
{
    class Program
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;

        static int Main(string[] args)
        {
            try
            {
                if (args.Length < 3)
                {
                    Console.Error.WriteLine("Usage: StoragePoolHelper <diskNumber> <poolName> <spaceCount> [name1 size1] [name2 size2] ...");
                    return 1;
                }

                int diskNumber = int.Parse(args[0]);
                string poolName = args[1];
                int spaceCount = int.Parse(args[2]);
                int partitionNumber = args.Length > 3 + spaceCount * 2 ? int.Parse(args[3 + spaceCount * 2]) : 0;

                Console.WriteLine($"=== StoragePoolHelper (32-bit) ===");
                Console.WriteLine($"Disk: PhysicalDrive{diskNumber}");
                if (partitionNumber > 0) Console.WriteLine($"Partition: {partitionNumber}");
                Console.WriteLine($"Pool: {poolName}");
                Console.WriteLine($"Spaces: {spaceCount}");

                string diskPath;
                if (partitionNumber > 0)
                {
                    diskPath = $"\\\\?\\GLOBALROOT\\Device\\Harddisk{diskNumber}\\Partition{partitionNumber}";
                    Console.WriteLine($"Opening partition: {diskPath}");
                }
                else
                {
                    diskPath = $"\\\\.\\PhysicalDrive{diskNumber}";
                    Console.WriteLine($"Opening disk: {diskPath}");
                }

                SafeFileHandle diskHandle = CreateFile(diskPath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (diskHandle.IsInvalid)
                {
                    int err = Marshal.GetLastWin32Error();
                    Console.Error.WriteLine($"Failed to open disk: Win32 error {err} (0x{err:X8})");
                    return 2;
                }
                Console.WriteLine("Disk opened successfully.");

                Console.WriteLine("Creating storage service...");
                using var service = new ImageStorageService();
                Console.WriteLine("Storage service created.");

                Console.WriteLine($"Creating storage pool '{poolName}'...");

                IntPtr poolHandle = IntPtr.Zero;
                try
                {
                    poolHandle = service.CreatePool(diskHandle, poolName, 0, "");
                    Console.WriteLine("Storage pool created (version=0, directory=\"\").");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to create storage pool: {ex.Message}");
                    return 3;
                }

                Console.WriteLine($"Storage pool created. Handle: 0x{poolHandle.ToInt64():X}");

                int argIdx = 3;
                var spaceNames = new System.Collections.Generic.List<string>();
                for (int i = 0; i < spaceCount; i++)
                {
                    if (argIdx + 1 >= args.Length) break;
                    string spaceName = args[argIdx++];
                    ulong spaceSize = ulong.Parse(args[argIdx++]);
                    spaceNames.Add(spaceName);

                    Console.WriteLine($"Creating space '{spaceName}' ({spaceSize / 1024.0 / 1024 / 1024:F2} GB)...");
                    try
                    {
                        Guid spaceId = service.CreateSpace(poolHandle, spaceName, spaceSize, 256UL * 1024 * 1024, true, 1);
                        Console.WriteLine($"Space '{spaceName}' created. ID: {spaceId}");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Failed to create space '{spaceName}': {ex.Message}");
                    }
                }

                // Attach all spaces so Windows can see them as disks
                Console.WriteLine("Attaching virtual disks...");
                foreach (var sn in spaceNames)
                {
                    try
                    {
                        var dh = service.AttachSpace(poolHandle, sn, false);
                        Console.WriteLine($"  Attached '{sn}'");
                        dh.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"  Failed to attach '{sn}': {ex.Message}");
                    }
                }

                Console.WriteLine("Closing pool (keep spaces attached)...");
                service.ClosePool(poolHandle, false);
                Console.WriteLine("Pool closed.");

                diskHandle.Dispose();
                Console.WriteLine("=== StoragePoolHelper completed successfully ===");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                return 99;
            }
        }
    }
}
