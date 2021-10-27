using PortableStorage;
using System;
using System.Net.Http;

namespace PortableDownloader
{
    public class DownloadControllerOptions
    {
        private static readonly DownloaderOptions DefaultOptions = new DownloaderOptions();
        public int MaxPartCount { get; set; } = DefaultOptions.MaxPartCount;
        public long PartSize { get; set; } = DefaultOptions.PartSize;
        public int MaxRetryCount { get; set; } = DefaultOptions.MaxRetryCount;
        public bool AllowResuming { get; set; } = DefaultOptions.AllowResuming;
        public int WriteBufferSize { get; set; } = DefaultOptions.WriteBufferSize;
        public Uri Uri { get; set; }
        public Storage Storage { get; set; }
        public string DownloadPath { get; set; }
        public bool IsStopped { get; set; }
        public string DownloadingInfoExtension { get; set; } = ".downloading_info";
        public string DownloadingExtension { get; set; } = ".downloading";
        internal Uri Referrer { get; set; }
        internal string Host { get; set; }
        internal HttpMessageHandler ClientHandler { get; set; }
        internal string DownloadingInfoPath { get; set; }
        internal string DownloadingPath { get; set; }
    }
}