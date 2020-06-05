using PortableStorage;
using System;

namespace PortableDownloader
{
    public class DownloadStateChangedEventArgs : EventArgs
    {
        public DownloadController DownloadController { get; }

        public DownloadStateChangedEventArgs(DownloadController sender)
        {
            DownloadController = sender;
        }
    }
}