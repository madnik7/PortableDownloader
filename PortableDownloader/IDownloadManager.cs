using System;
using System.Collections.Generic;
using System.Text;

namespace PortableDownloader
{
    public interface IDownloadManager : IDisposable
    {
        void Add(string path, Uri remoteUrl, bool startByQueue = true);
        void Start(string path = null);
        void Stop(string path = null);
        void Cancel(string path = null);
        DownloadManagerItem[] Items { get; }
        DownloadManagerItem[] GetItems(string path = null);
        DownloadManagerItem GetItem(string path = null);

        void RemoveFinishedItems();
    }
}
