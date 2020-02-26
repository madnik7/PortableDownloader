using System;

namespace PortableDownloader
{
    public class DownloadManagerItem
    {
        public string Path { get; set; }
        public int BytesPerSecond { get; set; }
        public long CurrentSize { get; set; }
        public long TotalSize { get; set; }
        public DownloadState DownloadState { get; set; } = DownloadState.None;
        public Uri RemoteUri { get; set; }
        public string ErrorMessage { get; set; }
        public bool IsIdle => Downloader.IsIdleState(DownloadState);

        
    }
}
