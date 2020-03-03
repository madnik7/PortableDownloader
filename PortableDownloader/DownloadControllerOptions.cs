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
        public int MaxPartCount { get; set; } = 4;
        public int PartSize { get; set; } = 500 * 1000;
        public int MaxRetryCount { get; set; }
        public bool AllowResuming { get; set; } = true;
        public string DownloadingInfoExtension { get; set; } = ".downloading_info";
        public string DownloadingExtension { get; set; } = ".downloading";
        internal string DownloadingInfoPath { get; set; }
        internal string DownloadingPath { get; set; }

    }
}
