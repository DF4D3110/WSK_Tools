using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DeviceLayoutToVhd
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct GptHeader
    {
        public ulong Signature;       // "EFI PART"
        public uint Revision;
        public uint HeaderSize;
        public uint HeaderCrc32;
        public uint Reserved;
        public ulong CurrentLba;
        public ulong BackupLba;
        public ulong FirstUsableLba;
        public ulong LastUsableLba;
        public Guid DiskGuid;
        public ulong PartitionEntryLba;
        public uint PartitionEntryCount;
        public uint PartitionEntrySize;
        public uint PartitionEntryCrc32;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 128)]
    public struct GptPartitionEntry
    {
        public Guid PartitionType;
        public Guid UniqueGuid;
        public ulong StartingLba;
        public ulong EndingLba;
        public ulong Attributes;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 72)]
        public byte[] NameBytes;

        public string Name => Encoding.Unicode.GetString(NameBytes ?? new byte[72]).TrimEnd('\0');
        public bool IsEmpty => PartitionType == Guid.Empty;
        public ulong SizeInSectors => EndingLba - StartingLba + 1;
    }

    public class GptManager : IDisposable
    {
        private FileStream _stream;
        private GptHeader _header;
        private List<GptPartitionEntry> _partitions;
        private readonly uint _bytesPerSector = 512;

        public GptManager(string physicalDrivePath)
        {
            _stream = new FileStream(physicalDrivePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            ReadGpt();
        }

        private void ReadGpt()
        {
            // Read GPT header at LBA 1
            _stream.Seek(_bytesPerSector, SeekOrigin.Begin);
            byte[] headerBytes = new byte[Marshal.SizeOf<GptHeader>()];
            _stream.Read(headerBytes, 0, headerBytes.Length);
            _header = ByteArrayToStructure<GptHeader>(headerBytes);

            // Read partition entries
            _stream.Seek((long)_header.PartitionEntryLba * _bytesPerSector, SeekOrigin.Begin);
            int entrySize = (int)_header.PartitionEntrySize;
            int count = (int)_header.PartitionEntryCount;
            byte[] entriesBytes = new byte[entrySize * count];
            _stream.Read(entriesBytes, 0, entriesBytes.Length);

            _partitions = new List<GptPartitionEntry>();
            for (int i = 0; i < count; i++)
            {
                byte[] entryBytes = new byte[entrySize];
                Array.Copy(entriesBytes, i * entrySize, entryBytes, 0, entrySize);
                _partitions.Add(ByteArrayToStructure<GptPartitionEntry>(entryBytes));
            }
        }

        public IReadOnlyList<GptPartitionEntry> Partitions => _partitions.AsReadOnly();

        public List<GptPartitionEntry> GetNonEmptyPartitions()
        {
            var result = new List<GptPartitionEntry>();
            foreach (var p in _partitions)
                if (!p.IsEmpty) result.Add(p);
            result.Sort((a, b) => a.StartingLba.CompareTo(b.StartingLba));
            return result;
        }

        /// <summary>
        /// Move a partition to a new starting LBA, preserving its size.
        /// Returns the data offset (in bytes) for copy operations.
        /// </summary>
        public long MovePartition(int index, ulong newStartLba)
        {
            var p = _partitions[index];
            ulong size = p.SizeInSectors;
            p.StartingLba = newStartLba;
            p.EndingLba = newStartLba + size - 1;
            _partitions[index] = p;
            return (long)newStartLba * _bytesPerSector;
        }

        /// <summary>
        /// Shrink a partition from the front (move start forward), freeing space at the beginning.
        /// </summary>
        public void ShrinkPartitionFromFront(int index, ulong sectorsToFree)
        {
            var p = _partitions[index];
            p.StartingLba += sectorsToFree;
            _partitions[index] = p;
        }

        /// <summary>
        /// Insert a new partition at the given LBA range.
        /// </summary>
        public int InsertPartition(Guid type, ulong startLba, ulong endLba, string name)
        {
            // Find empty slot
            int slot = -1;
            for (int i = 0; i < _partitions.Count; i++)
            {
                if (_partitions[i].IsEmpty) { slot = i; break; }
            }
            if (slot == -1) throw new InvalidOperationException("No empty partition slots");

            var entry = new GptPartitionEntry
            {
                PartitionType = type,
                UniqueGuid = Guid.NewGuid(),
                StartingLba = startLba,
                EndingLba = endLba,
                Attributes = 0,
                NameBytes = new byte[72]
            };
            byte[] nameBytes = Encoding.Unicode.GetBytes(name);
            Array.Copy(nameBytes, entry.NameBytes, Math.Min(nameBytes.Length, 70));
            _partitions[slot] = entry;
            return slot;
        }

        public void WriteGpt(bool writeBackup = true)
        {
            // Serialize partition entries
            int entrySize = (int)_header.PartitionEntrySize;
            byte[] entriesBytes = new byte[entrySize * (int)_header.PartitionEntryCount];
            for (int i = 0; i < _partitions.Count; i++)
            {
                byte[] eb = StructureToByteArray(_partitions[i]);
                Array.Copy(eb, 0, entriesBytes, i * entrySize, entrySize);
            }

            // Calculate partition entry CRC
            _header.PartitionEntryCrc32 = Crc32.Compute(entriesBytes);

            // Calculate header CRC (with HeaderCrc32 = 0)
            _header.HeaderCrc32 = 0;
            byte[] headerBytes = StructureToByteArray(_header);
            _header.HeaderCrc32 = Crc32.Compute(headerBytes, 0, (int)_header.HeaderSize);
            headerBytes = StructureToByteArray(_header);

            // Write primary GPT header at LBA 1 (must write full sector)
            Console.WriteLine($"  Writing primary header at offset {_bytesPerSector}");
            _stream.Seek(_bytesPerSector, SeekOrigin.Begin);
            byte[] headerSector = new byte[_bytesPerSector];
            Array.Copy(headerBytes, headerSector, Math.Min(headerBytes.Length, _bytesPerSector));
            _stream.Write(headerSector, 0, (int)_bytesPerSector);
            _stream.Flush();

            // Write primary partition entries at LBA 2 (must write full sectors)
            long entriesOffset = (long)_header.PartitionEntryLba * _bytesPerSector;
            int entriesBytesToWrite = (int)(((entriesBytes.Length + _bytesPerSector - 1) / _bytesPerSector) * _bytesPerSector);
            Console.WriteLine($"  Writing primary entries at offset {entriesOffset} ({entriesBytesToWrite} bytes)");
            _stream.Seek(entriesOffset, SeekOrigin.Begin);
            byte[] entriesSector = new byte[entriesBytesToWrite];
            Array.Copy(entriesBytes, entriesSector, entriesBytes.Length);
            _stream.Write(entriesSector, 0, entriesBytesToWrite);
            _stream.Flush();

            if (writeBackup)
            {
                try
                {
                    // Write backup GPT at the end of disk
                    ulong backupHeaderLba = _header.BackupLba;
                    ulong backupEntryLba = backupHeaderLba - 32; // 32 sectors for partition entries

                    Console.WriteLine($"  Writing backup entries at LBA {backupEntryLba}, header at LBA {backupHeaderLba}");

                    // Backup partition entries (full sectors)
                    long backupEntriesOffset = (long)backupEntryLba * _bytesPerSector;
                    _stream.Seek(backupEntriesOffset, SeekOrigin.Begin);
                    byte[] backupEntriesSector = new byte[entriesBytesToWrite];
                    Array.Copy(entriesBytes, backupEntriesSector, entriesBytes.Length);
                    _stream.Write(backupEntriesSector, 0, entriesBytesToWrite);
                    _stream.Flush();

                    // Backup header (swap CurrentLba and BackupLba)
                    var backupHeader = _header;
                    backupHeader.CurrentLba = _header.BackupLba;
                    backupHeader.BackupLba = _header.CurrentLba;
                    backupHeader.PartitionEntryLba = backupEntryLba;
                    backupHeader.HeaderCrc32 = 0;
                    byte[] backupHeaderBytes = StructureToByteArray(backupHeader);
                    backupHeader.HeaderCrc32 = Crc32.Compute(backupHeaderBytes, 0, (int)backupHeader.HeaderSize);
                    backupHeaderBytes = StructureToByteArray(backupHeader);

                    long backupHeaderOffset = (long)backupHeaderLba * _bytesPerSector;
                    _stream.Seek(backupHeaderOffset, SeekOrigin.Begin);
                    byte[] backupHeaderSector = new byte[_bytesPerSector];
                    Array.Copy(backupHeaderBytes, backupHeaderSector, Math.Min(backupHeaderBytes.Length, _bytesPerSector));
                    _stream.Write(backupHeaderSector, 0, (int)_bytesPerSector);
                    _stream.Flush();
                    Console.WriteLine("  Backup GPT written");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Warning: Backup GPT write failed: {ex.Message}");
                    Console.WriteLine("  Primary GPT is valid, Windows will repair backup on next mount.");
                }
            }

            _stream.Flush();
        }

        private static T ByteArrayToStructure<T>(byte[] bytes) where T : struct
        {
            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { return (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T)); }
            finally { handle.Free(); }
        }

        private static byte[] StructureToByteArray<T>(T structure) where T : struct
        {
            int size = Marshal.SizeOf(structure);
            byte[] bytes = new byte[size];
            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { Marshal.StructureToPtr(structure, handle.AddrOfPinnedObject(), false); }
            finally { handle.Free(); }
            return bytes;
        }

        public void WriteRawData(long offset, Stream source, long length)
        {
            _stream.Seek(offset, SeekOrigin.Begin);
            byte[] buf = new byte[64 * 1024 * 1024];
            long remaining = length;
            while (remaining > 0)
            {
                int toRead = (int)Math.Min(buf.Length, remaining);
                int read = source.Read(buf, 0, toRead);
                if (read <= 0) break;
                _stream.Write(buf, 0, read);
                remaining -= read;
            }
            _stream.Flush();
        }

        public void Dispose()
        {
            _stream?.Dispose();
        }
    }

    public static class Crc32
    {
        private static readonly uint[] _table = new uint[256];

        static Crc32()
        {
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int j = 0; j < 8; j++)
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                _table[i] = c;
            }
        }

        public static uint Compute(byte[] data, int offset = 0, int length = -1)
        {
            if (length < 0) length = data.Length - offset;
            uint crc = 0xFFFFFFFF;
            for (int i = offset; i < offset + length; i++)
                crc = _table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return ~crc;
        }
    }
}
