using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DeviceLayoutExchanger
{
    public class ImageStorageService : IDisposable
    {
        private const string DllName = "imagestorageservice.dll";

        [DllImport(DllName, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int CreateStorageService(out IntPtr serviceHandle, IntPtr logError, uint storeIdsCount, IntPtr storeIds, int fImaging);

        [DllImport(DllName, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int CloseImageStorageService(IntPtr serviceHandle);

        [DllImport(DllName, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int CreateStoragePool(IntPtr serviceHandle, SafeFileHandle diskHandle, string poolName, uint version, string directory, out IntPtr poolHandle);

        [DllImport(DllName, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int CloseStoragePool(IntPtr serviceHandle, IntPtr poolHandle, int detachSpaces);

        [DllImport(DllName, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int CreateStorageSpace(IntPtr serviceHandle, IntPtr poolHandle, string spaceName, ulong size, ulong extentSize, int thin, out Guid spaceId, ref Guid reserveId, uint usage);

        [DllImport(DllName, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int DeleteStorageSpace(IntPtr serviceHandle, IntPtr poolHandle, string spaceName);

        [DllImport(DllName, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int AttachStorageSpace(IntPtr serviceHandle, IntPtr poolHandle, string spaceName, int temporary, out SafeFileHandle diskHandle);

        [DllImport(DllName, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int DetachStorageSpace(IntPtr serviceHandle, IntPtr poolHandle, string spaceName, int discardChanges);

        private IntPtr _serviceHandle;
        private bool _disposed;

        public ImageStorageService()
        {
            int hr = CreateStorageService(out _serviceHandle, IntPtr.Zero, 0, IntPtr.Zero, 0);
            if (hr != 0 || _serviceHandle == IntPtr.Zero)
                throw new InvalidOperationException($"CreateStorageService failed: 0x{hr:X8}");
        }

        public IntPtr CreatePool(SafeFileHandle diskHandle, string poolName, uint version = 0x13, string directory = null)
        {
            IntPtr poolHandle;
            int hr = CreateStoragePool(_serviceHandle, diskHandle, poolName, version, directory, out poolHandle);
            if (hr != 0 || poolHandle == IntPtr.Zero)
                throw new InvalidOperationException($"CreateStoragePool failed: 0x{hr:X8}");
            return poolHandle;
        }

        public void ClosePool(IntPtr poolHandle, bool detachSpaces = false)
        {
            if (poolHandle != IntPtr.Zero)
                CloseStoragePool(_serviceHandle, poolHandle, detachSpaces ? 1 : 0);
        }

        public Guid CreateSpace(IntPtr poolHandle, string spaceName, ulong sizeBytes, ulong extentSizeBytes = 256UL * 1024 * 1024, bool thin = false, uint usage = 1)
        {
            Guid spaceId;
            Guid reserveId = Guid.Empty;
            int hr = CreateStorageSpace(_serviceHandle, poolHandle, spaceName, sizeBytes, extentSizeBytes, thin ? 1 : 0, out spaceId, ref reserveId, usage);
            if (hr != 0)
                throw new InvalidOperationException($"CreateStorageSpace failed: 0x{hr:X8}");
            return spaceId;
        }

        public SafeFileHandle AttachSpace(IntPtr poolHandle, string spaceName, bool temporary = false)
        {
            SafeFileHandle diskHandle;
            int hr = AttachStorageSpace(_serviceHandle, poolHandle, spaceName, temporary ? 1 : 0, out diskHandle);
            if (hr != 0 || diskHandle == null || diskHandle.IsInvalid)
                throw new InvalidOperationException($"AttachStorageSpace failed: 0x{hr:X8}");
            return diskHandle;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_serviceHandle != IntPtr.Zero)
                    CloseImageStorageService(_serviceHandle);
                _disposed = true;
            }
        }
    }
}
