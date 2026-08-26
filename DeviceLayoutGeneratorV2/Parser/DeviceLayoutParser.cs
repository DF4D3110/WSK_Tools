using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace DeviceLayoutGeneratorV2.Parser
{
    [XmlRoot("DeviceLayout", Namespace = "http://schemas.microsoft.com/embedded/2004/10/ImageUpdate/v2")]
    public class DeviceLayout
    {
        [XmlAttribute("SectorSize")]
        public uint SectorSize { get; set; } = 512;

        [XmlAttribute("ChunkSize")]
        public uint ChunkSize { get; set; } = 128;

        [XmlArray("StoragePools")]
        [XmlArrayItem("StoragePool")]
        public List<StoragePool> StoragePools { get; set; } = new List<StoragePool>();

        [XmlArray("Stores")]
        [XmlArrayItem("Store")]
        public List<Store> TopLevelStores { get; set; } = new List<Store>();
    }

    public class StoragePool
    {
        [XmlElement("Name")]
        public string Name { get; set; } = "OSPool";

        [XmlArray("Stores")]
        [XmlArrayItem("Store")]
        public List<Store> Stores { get; set; } = new List<Store>();
    }

    public class Store
    {
        [XmlElement("Id")]
        public string Id { get; set; }

        [XmlElement("StoreType")]
        public string StoreType { get; set; }

        [XmlElement("SizeInSectors")]
        public ulong SizeInSectors { get; set; }

        [XmlArray("Partitions")]
        [XmlArrayItem("Partition")]
        public List<Partition> Partitions { get; set; } = new List<Partition>();
    }

    public class Partition
    {
        [XmlElement("Name")]
        public string Name { get; set; }

        [XmlElement("FileSystem")]
        public string FileSystem { get; set; }

        [XmlElement("TotalSectors")]
        public ulong TotalSectors { get; set; }

        [XmlElement("Type")]
        public string Type { get; set; }

        [XmlElement("UseAllSpace")]
        public bool UseAllSpace { get; set; }

        [XmlElement("RequiredToFlash")]
        public bool RequiredToFlash { get; set; }
    }

    public static class DeviceLayoutParser
    {
        public static DeviceLayout Parse(string xmlPath)
        {
            var serializer = new XmlSerializer(typeof(DeviceLayout));
            using (var reader = new StreamReader(xmlPath))
            {
                return (DeviceLayout)serializer.Deserialize(reader);
            }
        }
    }
}
