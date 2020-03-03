using PortableStorage;
using System;
using System.Collections.Generic;
using System.Text;

namespace PortableDownloader
{
    public class DownloadControllerOptions
    {
        public Uri Uri { get; set; }
        public Storage Storage { get; set; }
        public string DownloadPath { get; set; }
        public bool IsStopped { get; set; }
        public string DownloadingInfoExtension { get; set; } = ".downloading_info";
        public string DownloadingExtension { get; set; } = ".downloading";
        internal string DownloadingInfoPath { get; set; }
        internal string DownloadingPath { get; set; }

    }
}
