using System;
using System.Collections.Generic;
using System.Text;
using PortableStorage;

namespace PortableDownloader
{
    public class DownloadManagerOptions
    {
        private static readonly DownloadControllerOptions DefaultOptions = new DownloadControllerOptions();

        public Storage Storage { get; set; }
        public string DataPath { get; set; } = ".downloadlist.json";
        public int MaxOfSimultaneousDownloads { get; set; } = 3;
        public string DownloadingInfoExtension { get; set; } = DefaultOptions.DownloadingInfoExtension;
        public string DownloadingExtension { get; set; } = DefaultOptions.DownloadingExtension;
        public int MaxPartCount { get; set; } = DefaultOptions.MaxPartCount;
        public long PartSize { get; set; } = DefaultOptions.PartSize;
        public int MaxRetryCount { get; set; } = DefaultOptions.MaxRetryCount;
        public bool AllowResuming { get; set; } = DefaultOptions.AllowResuming;
        public int WriteBufferSize { get; set; } = DefaultOptions.WriteBufferSize;
        public bool RestoreLastList { get; set; } = true;

    }
}
