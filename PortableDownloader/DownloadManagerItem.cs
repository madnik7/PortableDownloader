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
        public bool IsStarted { get; set; }
        public bool IsWaiting => !IsStarted && (DownloadState == DownloadState.None || DownloadState == DownloadState.Initialized);
        public bool IsIdle => !IsStarted && Downloader.IsIdleState(DownloadState);
        public Uri RemoteUri { get; set; }
        public string ErrorMessage { get; set; }

        
    }
}
