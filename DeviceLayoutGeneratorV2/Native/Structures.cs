using System;
using System.Runtime.InteropServices;

namespace DeviceLayoutGeneratorV2.Native
{
    /// <summary>
    /// STORE_ID - 20 bytes, from ImageStructures in imagestorageservicemanaged.dll
    /// Union: storeId_GPT (Guid) overlaps with storeId_MBR (uint)
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 20)]
    public struct STORE_ID
    {
        [FieldOffset(0)]
        public uint StoreType;

        [FieldOffset(4)]
        public Guid StoreId_GPT;

        [FieldOffset(4)]
        public uint StoreId_MBR;

        public static STORE_ID CreateGpt(uint storeType, Guid gptId)
        {
            return new STORE_ID { StoreType = storeType, StoreId_GPT = gptId };
        }
    }

    /// <summary>
    /// PARTITION_ENTRY - from ImageStructures in imagestorageservicemanaged.dll
    /// LayoutKind.Explicit with unions (id overlaps mbrFlags/mbrType)
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 208, CharSet = CharSet.Unicode)]
    public struct PARTITION_ENTRY
    {
        [FieldOffset(0)]
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 36)]
        public string Name;

        [FieldOffset(72)]
        public ulong SectorCount;

        [FieldOffset(80)]
        public uint AlignmentSizeInBytes;

        [FieldOffset(84)]
        public uint ClusterSize;

        [FieldOffset(88)]
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FileSystem;

        [FieldOffset(152)]
        public Guid Id;

        [FieldOffset(168)]
        public Guid Type;

        [FieldOffset(184)]
        public ulong Flags;

        [FieldOffset(152)]
        public byte MbrFlags;

        [FieldOffset(153)]
        public byte MbrType;

        [FieldOffset(192)]
        public ulong OffsetInSectors;

        [FieldOffset(200)]
        public byte FFvePrep;
    }

    /// <summary>
    /// Log level for ImageStorageService logging
    /// </summary>
    public enum LogLevel : uint
    {
        levelError = 0,
        levelWarning = 1,
        levelInfo = 2,
        levelDebug = 3
    }

    /// <summary>
    /// Log callback delegate
    /// </summary>
    public delegate void LogFunction(LogLevel level, [MarshalAs(UnmanagedType.LPWStr)] string message);
}
