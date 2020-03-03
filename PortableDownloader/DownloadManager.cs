using Newtonsoft.Json;
using PortableStorage;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PortableDownloader
{
    public class DownloadManager : IDownloadManager
    {
        private class DownloadManagerData
        {
            public DownloadManagerItem[] Items { get; set; }
        }

        private enum StartMode
        {
            None,
            AddToQueue,
            Start
        }

        public bool AllowResuming { get; private set; }
        public int MaxPartCount { get; private set; }
        public int PartSize { get; private set; }
        public int MaxRetryCount { get; private set; }


        private readonly object _monitor = new object();
        private readonly Storage _storage;
        private readonly string _dataPath;
        private readonly string _downloadingExtension;
        private readonly string _downloadingInfoExtension;
        private readonly ConcurrentDictionary<string, DownloadController> _downloadControllers = new ConcurrentDictionary<string, DownloadController>();
        private readonly ConcurrentDictionary<string, DownloadManagerItem> _items = new ConcurrentDictionary<string, DownloadManagerItem>();

        private int _maxOfSimultaneousDownloads;
        public int MaxOfSimultaneousDownloads
        {
            get => _maxOfSimultaneousDownloads;
            set
            {
                _maxOfSimultaneousDownloads = value;
                CheckQueue();
            }
        }

        private string GetDownloadingPath(string path) => path + _downloadingExtension;
        private string GetDownloadingInfoPath(string path) => path + _downloadingInfoExtension;

        public DownloadManager(DownloadManagerOptions options)
        {
            if (options.Storage == null)
                new ArgumentNullException("Storage");
            _storage = options.Storage;
            _dataPath = options.DataPath;
            _downloadingExtension = options.DownloadingExtension;
            _downloadingInfoExtension = options.DownloadingInfoExtension;
            _maxOfSimultaneousDownloads = options.MaxOfSimultaneousDownloads;
            AllowResuming = options.AllowResuming;
            MaxPartCount = options.MaxPartCount;
            PartSize = options.PartSize;
            MaxRetryCount = options.MaxRetryCount;

            Load(options.DataPath);
        }

        private void Load(string dataPath)
        {
            // load last data
            try
            {
                var ret = JsonConvert.DeserializeObject<DownloadManagerData>(_storage.ReadAllText(dataPath));
                foreach (var item in ret.Items)
                {
                    _items.TryAdd(item.Path, item);
                    var startMode = StartMode.None;
                    if (item.DownloadState == DownloadState.None || item.DownloadState == DownloadState.Initializing || item.DownloadState == DownloadState.Initialized || item.DownloadState == DownloadState.Error) startMode = StartMode.AddToQueue;
                    if (item.DownloadState == DownloadState.Downloading) startMode = StartMode.Start;
                    AddImpl(item.Path, item.RemoteUri, startMode);
                }
            }
            catch
            {
            }
        }

        private void Save()
        {
            lock (_monitor)
            {
                var data = new DownloadManagerData
                {
                    Items = Items
                };
                _storage.WriteAllText(_dataPath, JsonConvert.SerializeObject(data));
            }
        }

        public void Add(string path, Uri remoteUri, bool startByQueue = true)
        {
            AddImpl(path, remoteUri, startByQueue ? StartMode.AddToQueue : StartMode.None);
            CheckQueue();
            Save();
        }

        private void AddImpl(string path, Uri remoteUri, StartMode startMode)
        {
            //add to list if it is not already exists
            if (!_items.TryGetValue(path, out DownloadManagerItem newItem))
            {
                newItem = new DownloadManagerItem { Path = path, RemoteUri = remoteUri };
                _items.TryAdd(path, newItem);
            }

            if (newItem.DownloadState != DownloadState.Finished)
            {
                try
                {
                    var downloadController = GetOrCreateDownloadController(path, remoteUri, resume: true,  isStopped: startMode==StartMode.None);
                    if (startMode == StartMode.Start)
                        StartContoller(downloadController);
                    else 
                        CheckQueue();
                }
                catch (Exception err)
                {
                    newItem.DownloadState = DownloadState.Error;
                    newItem.ErrorMessage = err.Message;
                }
            }
        }

        private DownloadController GetOrCreateDownloadController(string path, Uri remoteUri, bool resume, bool isStopped)
        {
            var start = false;

            //delete if not in resume mode
            if (!resume)
            {
                Cancel(path);
            }
            //return if it is in progress
            else if (_downloadControllers.TryGetValue(path, out DownloadController downloadController))
            {
                if (start)
                    StartContoller(downloadController);
                return downloadController;
            }

            //build download
            var newDownloadController = DownloadController.Create(new DownloadControllerOptions()
            {
                Uri = remoteUri,
                Storage = _storage,
                DownloadPath = path,
                IsStopped = isStopped,
                AllowResuming = AllowResuming,
                MaxPartCount = MaxPartCount,
                PartSize = PartSize,
                MaxRetryCount = MaxRetryCount,

            });
            newDownloadController.DownloadStateChanged += DownloadController_DownloadStateChanged;
            _downloadControllers.TryAdd(path, newDownloadController);
            if (start)
                StartContoller(newDownloadController);

            return newDownloadController;
        }

        private void StartContoller(DownloadController downloadController)
        {
            downloadController.Start().GetAwaiter();
        }

        private void DownloadController_DownloadStateChanged(object sender, EventArgs e)
        {
            CheckQueue();
            Save();
        }

        public void Cancel(string path = null)
        {
            foreach (var item in GetItems(path))
            {
                // remove conroller
                if (_downloadControllers.TryGetValue(item.Path, out DownloadController downloadController))
                {
                    downloadController.Dispose();
                    _downloadControllers.TryRemove(item.Path, out _);
                }

                // remove item
                _items.TryRemove(item.Path, out _);

                // remove old files
                try
                {
                    var downloadingName = GetDownloadingPath(item.Path);
                    var downloadingInfoName = GetDownloadingInfoPath(item.Path);
                    if (_storage.EntryExists(downloadingName)) _storage.DeleteStream(downloadingName);
                    if (_storage.EntryExists(downloadingInfoName)) _storage.DeleteStream(downloadingInfoName);
                }
                catch { }
            }
        }

        private void UpdateItems()
        {
            foreach (var item in _items)
            {
                if (_downloadControllers.TryGetValue(item.Value.Path, out DownloadController downloadController))
                {
                    item.Value.BytesPerSecond = downloadController.BytesPerSecond;
                    item.Value.CurrentSize = downloadController.CurrentSize;
                    item.Value.TotalSize = downloadController.TotalSize;
                    item.Value.ErrorMessage = downloadController.LastException?.Message;
                    item.Value.IsStarted = downloadController.IsStarted;
                    item.Value.DownloadState = downloadController.DownloadState;
                }
            }
        }

        private void CheckQueue()
        {
            var startedItems = Items.Where(x => x.IsStarted).ToArray();
            var waitingItems = Items.Where(x => x.IsWaiting).ToArray();

            //start new downloads
            for (var i = 0; i < waitingItems.Count() && i < MaxOfSimultaneousDownloads - startedItems.Count(); i++)
                Start(waitingItems[i].Path);
        }

        public DownloadManagerItem[] Items => GetItems(null);

        public DownloadManagerItem[] GetItems(string path = null)
        {
            UpdateItems();

            // return all for root request
            if (string.IsNullOrEmpty(path) || path == "/")
                return _items.Select(x => x.Value).ToArray();

            // return only itelsef and items belong to sub storages
            return _items.Where(x => (x.Value.Path + "/").IndexOf(path + "/", StringComparison.InvariantCultureIgnoreCase) == 0)
                .Select(x => x.Value)
                .ToArray();
        }

        public DownloadManagerItem GetItem(string path = null)
        {
            var items = GetItems(path);
            var ret = new DownloadManagerItem()
            {
                BytesPerSecond = items.Sum(x => x.BytesPerSecond),
                CurrentSize = items.Sum(x => x.CurrentSize),
                TotalSize = items.Sum(x => x.TotalSize),
                DownloadState = DownloadState.None,
                Path = path,
                ErrorMessage = items.FirstOrDefault(x => x.DownloadState == DownloadState.Error)?.ErrorMessage,
                RemoteUri = items.Count() == 1 ? items.FirstOrDefault().RemoteUri : null,
                IsStarted = items.Any(x => x.IsStarted)
            };

            if (items.Any(x => !x.IsIdle))
                ret.DownloadState = DownloadState.Downloading;
            else if (items.Any(x => x.DownloadState == DownloadState.Error))
                ret.DownloadState = DownloadState.Error;
            else if (items.All(x => x.DownloadState == DownloadState.Finished))
                ret.DownloadState = DownloadState.Finished;

            return ret;
        }


        public void Start(string path = null)
        {
            // get all items in path
            foreach (var item in GetItems(path))
            {
                AddImpl(item.Path, item.RemoteUri, StartMode.Start);
                Save();
            }
        }

        public void Stop(string path = null)
        {
            foreach (var item in GetItems(path))
                if (_downloadControllers.TryGetValue(item.Path, out DownloadController downloadController))
                    downloadController.Stop();
        }

        public void RemoveFinishedItems()
        {
            var items = Items.Where(x => x.DownloadState == DownloadState.Finished);
            foreach (var item in items)
            {
                _items.TryRemove(item.Path, out _);
                _downloadControllers.TryRemove(item.Path, out _);
            }

            Save();
        }

        public bool IsIdle => Items.All(x => x.IsIdle);

        public void Dispose()
        {
            Save();
            foreach (var item in _downloadControllers)
                item.Value.Dispose();
        }
    }
}
