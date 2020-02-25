using System;
using System.Collections.Generic;
using System.Text;
using PortableStorage;

namespace PortableDownloader
{
    public class DownloadManagerOptions
    {
        public Storage Storage { get; set; }
        public string DataPath { get; set; } = ".downloadlist.json";
        public string DownloadingInfoExtension { get; set; } = ".downloading_info";
        public string DownloadingExtension { get; set; } = ".downloading";
        public int MaxOfSimultaneousDownloads { get; set; } = 3;
    }
}
