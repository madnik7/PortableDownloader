using System;
using System.Net.Http;

namespace PortableDownloader
{
    public sealed class DownloadOption
    {
        public string Path { get; set; }
        public string Host { get; set; }
        public Uri Referrer { get; set; }
        public Uri RemoteUri { get; set; }
        public string UserAgent { get; set; }
        public bool StartByQueue { get; set; }
        public HttpMessageHandler MessageHandler { get; set; }
        internal DownloadManager.StartMode Mode =>
            StartByQueue ? DownloadManager.StartMode.AddToQueue : DownloadManager.StartMode.None;
    }
}