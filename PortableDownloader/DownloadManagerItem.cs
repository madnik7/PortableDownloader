using System;
using System.Net.Http;

namespace PortableDownloader
{
    public class DownloadManagerItem
    {
        public string Path { get; set; }
        public int BytesPerSecond { get; set; }
        public long CurrentSize { get; set; }
        public long TotalSize { get; set; }
        public DownloadState State { get; set; } = DownloadState.None;
        public bool IsStarted { get; set; }
        public bool IsWaiting => !IsStarted && (State == DownloadState.None || State == DownloadState.Initialized);
        public bool IsIdle => !IsStarted && Downloader.IsIdleState(State);
        public Uri RemoteUri { get; set; }
        public string ErrorMessage { get; set; }
        public HttpMessageHandler ClientHandler { get; set; }
        public Uri Referrer { get; set; }
        public string Host { get; set; }
    }
}