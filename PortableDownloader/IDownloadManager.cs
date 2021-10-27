using System;
using System.Threading.Tasks;

namespace PortableDownloader
{
    public interface IDownloadManager : IDisposable
    {
        void Add(DownloadOption option);
        void Add(string path, Uri remoteUrl, bool startByQueue = true);
        void Start(string path = null);
        Task Stop(string path = null);
        void Cancel(string path = null);
        DownloadManagerItem[] Items { get; }
        bool IsIdle { get; }
        DownloadManagerItem[] GetItems(string path = null);
        DownloadManagerItem GetItem(string path = null);
        void RemoveFinishedItems();
    }
}