using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DiscUtils;
using DiscUtils.Streams;
using StorageSpace;

namespace FFUView;

public class OSPoolParser : IDisposable
{
    private Stream _stream;
    private bool _ownsStream;
    private Pool _pool;
    public bool IsOSPool { get; private set; }
    public List<OSPoolVirtualDisk> VirtualDisks { get; } = new();

    public OSPoolParser(Stream stream, bool ownsStream = false)
    {
        _stream = stream;
        _ownsStream = ownsStream;
        Parse();
    }

    private void Parse()
    {
        try
        {
            _stream.Position = 0;
            byte[] sig = new byte[8];
            if (_stream.Read(sig, 0, 8) < 8) return;
            if (Encoding.ASCII.GetString(sig, 0, 7) != "SPACEDB") return;
            _stream.Position = 0;
            _pool = new Pool(_stream);
            IsOSPool = true;
            LoadVirtualDisks();
        }
        catch { }
    }

    private void LoadVirtualDisks()
    {
        var disks = _pool.GetDisks();
        foreach (var kvp in disks)
        {
            var vd = new OSPoolVirtualDisk
            {
                Index = (int)kvp.Key,
                BTreeName = kvp.Value,
                Name = kvp.Value
            };
            try
            {
                using var space = _pool.OpenDisk(kvp.Key);
                vd.DeclaredSize = space.Length;
                try
                {
                    var rawDisk = new DiscUtils.Raw.Disk(space, Ownership.None);
                    vd.PartitionCount = rawDisk.Partitions.Count;
                }
                catch { }
            }
            catch { }
            vd.IsBlockMapped = vd.DeclaredSize > 2L * 1024 * 1024 * 1024;
            VirtualDisks.Add(vd);
        }
    }

    public Stream OpenVirtualDisk(int index)
    {
        if (_pool == null) return null;
        return _pool.OpenDisk(index);
    }

    public void Dispose()
    {
        if (_ownsStream) _stream?.Dispose();
    }
}

public class OSPoolVirtualDisk
{
    public int Index { get; set; }
    public long Offset { get; set; }
    public long DeclaredSize { get; set; }
    public string Name { get; set; } = "";
    public string BTreeName { get; set; } = "";
    public int PartitionCount { get; set; }
    public bool IsBlockMapped { get; set; }
    public List<OSPoolPartitionInfo> Partitions { get; } = new();
    public string DisplayName
    {
        get
        {
            string baseName = string.IsNullOrEmpty(BTreeName) ? Name : BTreeName;
            return IsBlockMapped ? baseName + " (块映射)" : baseName;
        }
    }
}

public class OSPoolPartitionInfo
{
    public int Index { get; set; }
    public long FirstSector { get; set; }
    public long SectorCount { get; set; }
}

public class OSPoolBTreeEntry
{
    public long Offset { get; set; }
    public uint Id { get; set; }
    public uint ParentId { get; set; }
    public uint Type { get; set; }
    public string Text { get; set; } = "";
}
