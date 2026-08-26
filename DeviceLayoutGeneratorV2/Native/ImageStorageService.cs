using System;
using System.Runtime.InteropServices;

namespace DeviceLayoutGeneratorV2.Native
{
    /// <summary>
    /// P/Invoke wrapper for ImageStorageService.dll (32-bit, __stdcall)
    /// All entry points use name mangling: _FunctionName@ParamBytes
    /// </summary>
    public static class ImageStorageServiceNative
    {
        private const string DllName = "ImageStorageService.dll";

        // === Service Lifecycle ===

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, EntryPoint = "_CreateImageStorageService@16")]
        public static extern int CreateImageStorageService(
            out IntPtr serviceHandle,
            IntPtr logFunction,
            uint storeIdsCount,
            IntPtr storeIds);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, EntryPoint = "_CloseImageStorageService@4")]
        public static extern void CloseImageStorageService(IntPtr service);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, EntryPoint = "_SetLoggingFunction@12")]
        public static extern void SetLoggingFunction(IntPtr service, LogLevel level, LogFunction logFunction);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, EntryPoint = "_RefreshImageStorageService@12")]
        public static extern int RefreshImageStorageService(IntPtr service, IntPtr storeIds, uint count);

        // === Virtual Hard Disk ===

        /// <summary>
        /// Create virtual hard disk. storeId is passed BY VALUE (20 bytes).
        /// Entry point _CreateVirtualHardDisk@44 confirms 44 bytes total params.
        /// </summary>
        [DllImport(DllName, CharSet = CharSet.Unicode, EntryPoint = "_CreateVirtualHardDisk@44")]
        public static extern int CreateVirtualHardDisk(
            IntPtr service,
            string fileName,
            ulong maxSizeInBytes,
            STORE_ID storeId,
            uint sectorSize,
            out IntPtr diskHandle);

        [DllImport(DllName, CharSet = CharSet.Unicode, EntryPoint = "_PartitionVirtualHardDisk@20")]
        public static extern int PartitionVirtualHardDisk(
            IntPtr service,
            IntPtr diskHandle,
            ref STORE_ID storeId,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] PARTITION_ENTRY[] partitions,
            uint partitionCount);

        // === Storage Pool ===

        [DllImport(DllName, CharSet = CharSet.Unicode, EntryPoint = "_CreateStoragePool@16")]
        public static extern int CreateStoragePool(
            IntPtr service,
            IntPtr storeHandle,
            string poolName,
            ref Guid poolId);

        [DllImport(DllName, CharSet = CharSet.Unicode, EntryPoint = "_SetStoragePoolName@24")]
        public static extern int SetStoragePoolName(IntPtr service, Guid poolId, string poolName);

        [DllImport(DllName, CharSet = CharSet.Unicode, EntryPoint = "_RemoveStoragePool@20")]
        public static extern int RemoveStoragePool(IntPtr service, Guid poolId, string poolName);

        [DllImport(DllName, CharSet = CharSet.Unicode, EntryPoint = "_AddDriveToStoragePool@24")]
        public static extern int AddDriveToStoragePool(IntPtr service, Guid poolId, IntPtr storeHandle, string poolName);

        // === Storage Space ===

        [DllImport(DllName, CharSet = CharSet.Unicode, EntryPoint = "_CreateStorageSpace@40")]
        public static extern int CreateStorageSpace(
            IntPtr service,
            Guid poolId,
            string spaceName,
            string spaceDescription,
            uint capacityInGB,
            ref Guid spaceId,
            out IntPtr diskHandle);

        [DllImport(DllName, CharSet = CharSet.Unicode, EntryPoint = "_SetStorageSpaceName@24")]
        public static extern int SetStorageSpaceName(IntPtr service, Guid spaceId, string spaceName);

        [DllImport(DllName, CharSet = CharSet.Unicode, EntryPoint = "_RemoveStorageSpace@20")]
        public static extern int RemoveStorageSpace(IntPtr service, Guid spaceId, string spaceName);

        [DllImport(DllName, CharSet = CharSet.Unicode, EntryPoint = "_GetStorageSpace@32")]
        public static extern int GetStorageSpace(IntPtr service, Guid spaceId, out IntPtr diskHandle, out ulong capacityInGB);

        // === Partition Operations ===

        [DllImport(DllName, CharSet = CharSet.Unicode, EntryPoint = "_UpdatePartitionProperties@36")]
        public static extern int UpdatePartitionProperties(
            IntPtr service,
            IntPtr diskHandle,
            STORE_ID storeId,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] PARTITION_ENTRY[] partitions,
            uint partitionCount);

        [DllImport(DllName, CharSet = CharSet.Unicode, EntryPoint = "_SetPartitionId@28")]
        public static extern int SetPartitionId(IntPtr service, IntPtr diskHandle, uint partitionNumber, ref Guid partitionId);

        [DllImport(DllName, CharSet = CharSet.Unicode, EntryPoint = "_SetPartitionType@44")]
        public static extern int SetPartitionType(IntPtr service, IntPtr diskHandle, uint partitionNumber, ref Guid partitionType, string partitionName);

        [DllImport(DllName, CharSet = CharSet.Unicode, EntryPoint = "_SetPartitionAttributes@36")]
        public static extern int SetPartitionAttributes(IntPtr service, IntPtr diskHandle, uint partitionNumber, ulong attributes, string partitionName);

        [DllImport(DllName, CharSet = CharSet.Unicode, EntryPoint = "_GetPartitionPath@40")]
        public static extern int GetPartitionPath(IntPtr service, IntPtr diskHandle, uint partitionNumber, System.Text.StringBuilder path, uint pathSize);

        [DllImport(DllName, CharSet = CharSet.Unicode, EntryPoint = "_FormatPartition@36")]
        public static extern int FormatPartition(
            IntPtr service,
            IntPtr diskHandle,
            uint partitionNumber,
            string fileSystem,
            uint clusterSize,
            string label);

        [DllImport(DllName, CharSet = CharSet.Unicode, EntryPoint = "_ExtendPartition@36")]
        public static extern int ExtendPartition(
            IntPtr service,
            IntPtr diskHandle,
            uint partitionNumber,
            ulong newSizeInSectors,
            string partitionName);

        // === Disk Operations ===

        [DllImport(DllName, CharSet = CharSet.Unicode, EntryPoint = "_SetDiskId@28")]
        public static extern int SetDiskId(IntPtr service, IntPtr diskHandle, ref Guid diskId);

        [DllImport(DllName, CharSet = CharSet.Unicode, EntryPoint = "_SetDiskOnline@8")]
        public static extern int SetDiskOnline(IntPtr service, IntPtr diskHandle);

        [DllImport(DllName, CharSet = CharSet.Unicode, EntryPoint = "_SetDiskOffline@8")]
        public static extern int SetDiskOffline(IntPtr service, IntPtr diskHandle);

        [DllImport(DllName, CharSet = CharSet.Unicode, EntryPoint = "_GetVirtualDiskImagePath@16")]
        public static extern int GetVirtualDiskImagePath(IntPtr service, IntPtr diskHandle, System.Text.StringBuilder path, uint pathSize);

        [DllImport(DllName, CharSet = CharSet.Unicode, EntryPoint = "_GetDiskNumAndPartitionCount@40")]
        public static extern int GetDiskNumAndPartitionCount(IntPtr service, IntPtr diskHandle, out uint diskNumber, out uint partitionCount);

        // === Storage Allocation ===

        [DllImport(DllName, CharSet = CharSet.Unicode, EntryPoint = "_GetStorageAllocationBitmap@20")]
        public static extern int GetStorageAllocationBitmap(
            IntPtr service,
            IntPtr diskHandle,
            IntPtr bitmapBuffer,
            ref uint bitmapSizeInBytes,
            out ulong totalAllocationUnits);

        // === Helper ===

        public static bool Succeeded(int hr) => hr >= 0;
        public static bool Failed(int hr) => hr < 0;
    }
}
