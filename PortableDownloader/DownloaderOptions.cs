using System;
using System.IO;
using System.Net.Http;

namespace PortableDownloader
{
    public class DownloaderOptions
    {
        public Uri Uri { get; set; }
        public Stream Stream { get; set; }
        public DownloadRange[] DownloadedRanges { get; set; }
        public int MaxPartCount { get; set; } = 1;
        public long PartSize { get; set; } = 4000 * 1000;
        public int MaxRetryCount { get; set; }
        public bool AutoDisposeStream { get; set; } = true;
        public bool AllowResuming { get; set; } = true;
        public bool IsStopped { get; set; }
        public int WriteBufferSize { get; set; } = 0xFFFF;
        internal string Host { get; set; }
        internal Uri Referrer { get; set; }
        internal HttpMessageHandler ClientHandler { get; set; }
    }
}