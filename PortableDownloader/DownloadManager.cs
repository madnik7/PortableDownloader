using PortableStorage;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

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
        private readonly ConcurrentDictionary<string, DownloadController> _downloadControllers = new ConcurrentDictionary<string, DownloadController>();
        private readonly ConcurrentDictionary<string, DownloadManagerItem> _items = new ConcurrentDictionary<string, DownloadManagerItem>();
        public string DownloadingExtension { get; }
        public string DownloadingInfoExtension { get; }

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

        private string GetDownloadingPath(string path) => path + DownloadingExtension;
        private string GetDownloadingInfoPath(string path) => path + DownloadingInfoExtension;

        public DownloadManager(DownloadManagerOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.Storage == null) throw new ArgumentNullException(nameof(options.Storage));

            _storage = options.Storage;
            _dataPath = options.DataPath;
            DownloadingExtension = options.DownloadingExtension ?? new DownloadControllerOptions().DownloadingExtension;
            DownloadingInfoExtension = options.DownloadingInfoExtension ?? new DownloadControllerOptions().DownloadingInfoExtension;
            _maxOfSimultaneousDownloads = options.MaxOfSimultaneousDownloads;
            AllowResuming = options.AllowResuming;
            MaxPartCount = options.MaxPartCount;
            PartSize = options.PartSize;
            MaxRetryCount = options.MaxRetryCount;

            Load(options.DataPath);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "<Pending>")]
        private void Load(string dataPath)
        {
            // load last data
            try
            {
                var ret = JsonSerializer.Deserialize<DownloadManagerData>(_storage.ReadAllText(dataPath));
                foreach (var item in ret.Items)
                {
                    _items.TryAdd(item.Path, item);
                    var startMode = StartMode.None;
                    if (item.IsStarted) startMode = StartMode.AddToQueue;
                    if (item.State == DownloadState.Downloading) startMode = StartMode.Start;
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
                _storage.WriteAllText(_dataPath, JsonSerializer.Serialize(data));
            }
        }

        public void Add(string path, Uri remoteUri, bool startByQueue = true)
        {
            AddImpl(path, remoteUri, startByQueue ? StartMode.AddToQueue : StartMode.None);
            CheckQueue();
            Save();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "<Pending>")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "<Pending>")]
        private void AddImpl(string path, Uri remoteUri, StartMode startMode)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            path = ValidatePath(path);

            //add to list if it is not already exists
            if (!_items.TryGetValue(path, out DownloadManagerItem newItem))
            {
                newItem = new DownloadManagerItem { Path = path, RemoteUri = remoteUri };
                _items.TryAdd(path, newItem);
            }

            // restart finished item if it does not exists
            if (newItem.State == DownloadState.Finished)
            {
                if (_storage.StreamExists(path))
                    return;

                _downloadControllers.TryRemove(newItem.Path, out _);
                newItem.State = DownloadState.None;
            }


            // start
            try
            {
                var downloadController = GetOrCreateDownloadController(path, remoteUri, resume: true, isStopped: startMode == StartMode.None);
                if (startMode == StartMode.Start)
                    downloadController.Start().GetAwaiter();

                // restart if it is stopped or in error state
                if (startMode == StartMode.AddToQueue &&
                    (downloadController.State == DownloadState.Stopped || downloadController.State == DownloadState.Stopped || downloadController.State == DownloadState.Error))
                    downloadController.Init().GetAwaiter();
                else
                    CheckQueue();
            }
            catch (Exception err)
            {
                newItem.State = DownloadState.Error;
                newItem.ErrorMessage = err.Message;
            }
        }

        private DownloadController GetOrCreateDownloadController(string path, Uri remoteUri, bool resume, bool isStopped)
        {
            //delete if not in resume mode
            if (!resume)
            {
                Cancel(path);
            }
            //return if it is in progress
            else if (_downloadControllers.TryGetValue(path, out DownloadController downloadController))
            {
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

            return newDownloadController;
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
                var downloadingName = GetDownloadingPath(item.Path);
                var downloadingInfoName = GetDownloadingInfoPath(item.Path);
                if (_storage.EntryExists(downloadingName)) _storage.DeleteStream(downloadingName);
                if (_storage.EntryExists(downloadingInfoName)) _storage.DeleteStream(downloadingInfoName);
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
                    item.Value.State = downloadController.State;
                }
            }
        }

        private void CheckQueue()
        {
            var startedItems = Items.Where(x => x.IsStarted).ToArray();
            var waitingItems = Items.Where(x => x.IsWaiting).ToArray();

            //start new downloads
            for (var i = 0; i < waitingItems.Length && i < MaxOfSimultaneousDownloads - startedItems.Length; i++)
                Start(waitingItems[i].Path);
        }

        public DownloadManagerItem[] Items => GetItems(null);

        public DownloadManagerItem[] GetItems(string path = null)
        {
            path = ValidatePath(path);

            UpdateItems();

            // return all for root request
            if (path == Storage.SeparatorChar.ToString(CultureInfo.InvariantCulture))
                return _items.Select(x => x.Value).ToArray();

            // return only itelsef and items belong to sub storages
            return _items.Where(x => (x.Value.Path + "/").IndexOf(path + "/", StringComparison.InvariantCultureIgnoreCase) == 0)
                .Select(x => x.Value)
                .ToArray();
        }

        public DownloadManagerItem GetItem(string path = null)
        {
            path = ValidatePath(path);

            var items = GetItems(path);
            if (items == null || items.Length == 0)
                return null;

            var ret = new DownloadManagerItem()
            {
                BytesPerSecond = items.Sum(x => x.BytesPerSecond),
                CurrentSize = items.Sum(x => x.CurrentSize),
                TotalSize = items.Sum(x => x.TotalSize),
                State = items.Any(x => x.State != items[0].State) ? DownloadState.None : items[0].State,
                Path = path,
                ErrorMessage = items.FirstOrDefault(x => x.State == DownloadState.Error)?.ErrorMessage,
                RemoteUri = items.Length == 1 ? items.FirstOrDefault().RemoteUri : null,
                IsStarted = items.Any(x => x.IsStarted)
            };

            if (items.Any(x => !x.IsIdle && x.State != DownloadState.Stopping))
                ret.State = DownloadState.Downloading;
            else if (items.Any(x => x.State == DownloadState.Error))
                ret.State = DownloadState.Error;

            return ret;
        }

        private static string ValidatePath(string path)
        {
            if (string.IsNullOrEmpty(path)) path = Storage.SeparatorChar.ToString(CultureInfo.InvariantCulture);
            return path;
        }

        public void Start(string path = null)
        {
            path = ValidatePath(path);

            // get all items in path
            foreach (var item in GetItems(path))
            {
                AddImpl(item.Path, item.RemoteUri, StartMode.Start);
                Save();
            }
        }

        public Task Stop(string path = null)
        {
            path = ValidatePath(path);

            var tasks = new List<Task>();
            foreach (var item in GetItems(path))
                if (_downloadControllers.TryGetValue(item.Path, out DownloadController downloadController))
                    tasks.Add(downloadController.Stop());

            return Task.WhenAll(tasks.ToArray());
        }

        public void RemoveFinishedItems()
        {
            var items = Items.Where(x => x.State == DownloadState.Finished);
            foreach (var item in items)
            {
                _items.TryRemove(item.Path, out _);
                _downloadControllers.TryRemove(item.Path, out _);
            }

            Save();
        }

        public bool IsIdle => Items.All(x => x.IsIdle);

        public bool IsDownloadingStream(string path)
        {
            var ext = Path.GetExtension(path);
            return ext == DownloadingExtension || ext == DownloadingInfoExtension;
        }

        private bool _disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (_disposedValue)
                return;

            if (disposing)
            {
                Save();
                foreach (var item in _downloadControllers)
                    item.Value.Dispose();
            }

            _disposedValue = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
