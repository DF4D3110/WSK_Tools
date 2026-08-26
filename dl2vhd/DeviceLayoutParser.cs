using System.Xml;
using System.Xml.Serialization;

namespace DeviceLayoutToVhd;

public class DeviceLayoutInfo
{
    public int SectorSize { get; set; } = 512;
    public int ChunkSize { get; set; } = 128;
    public long DefaultPartitionByteAlignment { get; set; } = 0x200000;
    public int StateSeparationLevel { get; set; }
    public List<StoreInfo> Stores { get; set; } = new();
    public List<StoragePoolInfo> StoragePools { get; set; } = new();

    // 存储池分区类型 GUID (17704+)
    public const string StoragePoolPartitionType = "{5708A6E0-9001-4b99-b064-1fe564896bdb}";

    /// <summary>获取物理Store中的存储池分区</summary>
    public PartitionInfo? GetStoragePoolPartition()
    {
        foreach (var store in Stores)
        {
            foreach (var part in store.Partitions)
            {
                if (part.Type.Equals(StoragePoolPartitionType, StringComparison.OrdinalIgnoreCase) ||
                    part.Name.Equals("OSPool", StringComparison.OrdinalIgnoreCase))
                    return part;
            }
        }
        return null;
    }

    /// <summary>获取物理Store（非存储池中的）</summary>
    public StoreInfo? GetPhysicalStore() => Stores.FirstOrDefault();
}

public class StoragePoolInfo
{
    public string Name { get; set; } = "";
    public List<StoreInfo> Stores { get; set; } = new();
}

public class StoreInfo
{
    public string Id { get; set; } = "";
    public string StoreType { get; set; } = "";
    public string DevicePath { get; set; } = "";
    public long SizeInSectors { get; set; }
    public bool OnlyAllocateDefinedGptEntries { get; set; }
    public List<PartitionInfo> Partitions { get; set; } = new();
}

public class PartitionInfo
{
    public string Name { get; set; } = "";
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string FileSystem { get; set; } = "";
    public long TotalSectors { get; set; }
    public long MinFreeSectors { get; set; }
    public bool UseAllSpace { get; set; }
    public bool Bootable { get; set; }
    public bool RequiredToFlash { get; set; }
    public bool AttachDriveLetter { get; set; }
    public bool PrepareFveMetadata { get; set; }
    public long ByteAlignment { get; set; }
    public long ClusterSize { get; set; }
}

public static class DeviceLayoutParser
{
    public static DeviceLayoutInfo Parse(string xmlPath)
    {
        var doc = new XmlDocument();
        doc.Load(xmlPath);

        var nsmgr = new XmlNamespaceManager(doc.NameTable);
        nsmgr.AddNamespace("ns", "http://schemas.microsoft.com/embedded/2004/10/ImageUpdate/v2");

        var result = new DeviceLayoutInfo();

        var sectorSizeNode = doc.SelectSingleNode("//ns:SectorSize", nsmgr);
        if (sectorSizeNode != null && int.TryParse(sectorSizeNode.InnerText, out var ss))
            result.SectorSize = ss;

        var chunkSizeNode = doc.SelectSingleNode("//ns:ChunkSize", nsmgr);
        if (chunkSizeNode != null && int.TryParse(chunkSizeNode.InnerText, out var cs))
            result.ChunkSize = cs;

        var alignNode = doc.SelectSingleNode("//ns:DefaultPartitionByteAlignment", nsmgr);
        if (alignNode != null)
        {
            var text = alignNode.InnerText.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                result.DefaultPartitionByteAlignment = Convert.ToInt64(text, 16);
            else if (long.TryParse(text, out var align))
                result.DefaultPartitionByteAlignment = align;
        }

        var stateSepNode = doc.SelectSingleNode("//ns:StateSeparationLevel", nsmgr);
        if (stateSepNode != null && int.TryParse(stateSepNode.InnerText, out var ssl))
            result.StateSeparationLevel = ssl;

        foreach (XmlNode storeNode in doc.SelectNodes("/ns:DeviceLayout/ns:Stores/ns:Store", nsmgr)!)
        {
            result.Stores.Add(ParseStore(storeNode, nsmgr, result.SectorSize));
        }
        foreach (XmlNode storeNode in doc.SelectNodes("/ns:MSDeviceLayout/ns:Stores/ns:Store", nsmgr)!)
        {
            result.Stores.Add(ParseStore(storeNode, nsmgr, result.SectorSize));
        }

        foreach (XmlNode poolNode in doc.SelectNodes("/ns:DeviceLayout/ns:StoragePools/ns:StoragePool", nsmgr)!)
        {
            var pool = new StoragePoolInfo
            {
                Name = GetNodeText(poolNode, "ns:Name", nsmgr)
            };
            foreach (XmlNode storeNode in poolNode.SelectNodes("ns:Stores/ns:Store", nsmgr)!)
            {
                pool.Stores.Add(ParseStore(storeNode, nsmgr, result.SectorSize));
            }
            result.StoragePools.Add(pool);
        }
        foreach (XmlNode poolNode in doc.SelectNodes("/ns:MSDeviceLayout/ns:StoragePools/ns:StoragePool", nsmgr)!)
        {
            var pool = new StoragePoolInfo
            {
                Name = GetNodeText(poolNode, "ns:Name", nsmgr)
            };
            foreach (XmlNode storeNode in poolNode.SelectNodes("ns:Stores/ns:Store", nsmgr)!)
            {
                pool.Stores.Add(ParseStore(storeNode, nsmgr, result.SectorSize));
            }
            result.StoragePools.Add(pool);
        }

        return result;
    }

    private static StoreInfo ParseStore(XmlNode node, XmlNamespaceManager nsmgr, int sectorSize)
    {
        var store = new StoreInfo
        {
            Id = GetNodeText(node, "ns:Id", nsmgr),
            StoreType = GetNodeText(node, "ns:StoreType", nsmgr),
            DevicePath = GetNodeText(node, "ns:DevicePath", nsmgr),
            OnlyAllocateDefinedGptEntries = GetNodeText(node, "ns:OnlyAllocateDefinedGptEntries", nsmgr)
                .Equals("true", StringComparison.OrdinalIgnoreCase)
        };

        var sizeNode = node.SelectSingleNode("ns:SizeInSectors", nsmgr);
        if (sizeNode != null && long.TryParse(sizeNode.InnerText, out var size))
            store.SizeInSectors = size;

        var sizeBytesNode = node.SelectSingleNode("ns:SizeInBytes", nsmgr);
        if (sizeBytesNode != null && long.TryParse(sizeBytesNode.InnerText, out var sizeBytes) && sizeBytes > 0)
        {
            var ss = sectorSize > 0 ? sectorSize : 512;
            store.SizeInSectors = sizeBytes / ss;
        }

        foreach (XmlNode partNode in node.SelectNodes("ns:Partitions/ns:Partition", nsmgr)!)
        {
            store.Partitions.Add(ParsePartition(partNode, nsmgr, sectorSize));
        }

        return store;
    }

    private static PartitionInfo ParsePartition(XmlNode node, XmlNamespaceManager nsmgr, int sectorSize)
    {
        var part = new PartitionInfo
        {
            Name = GetNodeText(node, "ns:Name", nsmgr),
            Id = GetNodeText(node, "ns:Id", nsmgr),
            Type = GetNodeText(node, "ns:Type", nsmgr),
            FileSystem = GetNodeText(node, "ns:FileSystem", nsmgr),
            Bootable = GetNodeText(node, "ns:Bootable", nsmgr).Equals("true", StringComparison.OrdinalIgnoreCase),
            RequiredToFlash = GetNodeText(node, "ns:RequiredToFlash", nsmgr).Equals("true", StringComparison.OrdinalIgnoreCase),
            AttachDriveLetter = GetNodeText(node, "ns:AttachDriveLetter", nsmgr).Equals("true", StringComparison.OrdinalIgnoreCase),
            PrepareFveMetadata = GetNodeText(node, "ns:PrepareFveMetadata", nsmgr).Equals("true", StringComparison.OrdinalIgnoreCase),
            UseAllSpace = GetNodeText(node, "ns:UseAllSpace", nsmgr).Equals("true", StringComparison.OrdinalIgnoreCase)
        };

        var totalNode = node.SelectSingleNode("ns:TotalSectors", nsmgr);
        if (totalNode != null && long.TryParse(totalNode.InnerText, out var total))
            part.TotalSectors = total;

        var totalBytesNode = node.SelectSingleNode("ns:TotalBytes", nsmgr);
        if (totalBytesNode != null && long.TryParse(totalBytesNode.InnerText, out var totalBytes) && totalBytes > 0)
        {
            var ss = sectorSize > 0 ? sectorSize : 512;
            part.TotalSectors = totalBytes / ss;
        }

        var minFreeNode = node.SelectSingleNode("ns:MinFreeSectors", nsmgr);
        if (minFreeNode != null && long.TryParse(minFreeNode.InnerText, out var minFree))
            part.MinFreeSectors = minFree;

        var alignNode = node.SelectSingleNode("ns:ByteAlignment", nsmgr);
        if (alignNode != null)
        {
            var text = alignNode.InnerText.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                part.ByteAlignment = Convert.ToInt64(text, 16);
            else if (long.TryParse(text, out var align))
                part.ByteAlignment = align;
        }

        var clusterNode = node.SelectSingleNode("ns:ClusterSize", nsmgr);
        if (clusterNode != null)
        {
            var text = clusterNode.InnerText.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                part.ClusterSize = Convert.ToInt64(text, 16);
            else if (long.TryParse(text, out var cluster))
                part.ClusterSize = cluster;
        }

        return part;
    }

    private static string GetNodeText(XmlNode parent, string xpath, XmlNamespaceManager nsmgr)
    {
        var node = parent.SelectSingleNode(xpath, nsmgr);
        return node?.InnerText ?? "";
    }
}
