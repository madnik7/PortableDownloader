using System;
using System.IO;

namespace PortableDownloader
{
    public class DownloaderOptions
    {
        public Uri Uri { get; set; }
        public Stream Stream { get; set; }
        public DownloadRange[] DownloadedRanges { get; set; }
        public int MaxPartCount { get; set; } = 4;
        public int PartSize { get; set; } = 500 * 1000;
        public int MaxRetryCount { get; set; }
        public bool AutoDisposeStream { get; set; } = true;
        public bool AllowResuming { get; set; } = true;
        public bool IsStopped { get; set; }
    }
}
