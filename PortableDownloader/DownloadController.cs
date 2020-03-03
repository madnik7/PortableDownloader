using Newtonsoft.Json;
using PortableStorage;
using System;
using System.IO;

namespace PortableDownloader
{
    public class DownloadController : Downloader
    {
        class DownloadData
        {
            public Uri Uri { get; set; }
            public DownloadRange[] DownloadedRanges { get; set; }
        }

        private readonly string _downloadingPath;
        private readonly string _downloadingInfoPath;
        private readonly Storage _storage;
        public string DownloadPath { get; }

        public static DownloadController Create(DownloadControllerOptions options)
        {
            var downloadData = Load(options);
            options.DownloadPath = options.DownloadPath ?? throw new ArgumentNullException("DownloadPath");
            options.DownloadingPath = options.DownloadingPath ?? options.DownloadPath + options.DownloadingExtension;
            options.DownloadingInfoPath = options.DownloadingInfoPath ?? options.DownloadPath + options.DownloadingInfoExtension;
            options.Uri = options.Uri ?? downloadData.Uri ?? throw new ArgumentNullException("RemoteUri!");
            options.DownloadingInfoPath = options.DownloadingInfoPath ?? options.DownloadPath + options.DownloadingInfoExtension;
            var ret = new DownloadController(options, downloadData);
            if (!options.IsStopped)
                ret.Init().GetAwaiter();
            return ret;
        }

        private DownloadController(DownloadControllerOptions options, DownloadData downloadData)
            : base(new DownloaderOptions() { 
                Uri = options.Uri, 
                DownloadedRanges = downloadData.DownloadedRanges, 
                AutoDisposeStream = true, 
                IsStopped = options.IsStopped,
                AllowResuming = options.AllowResuming,
                MaxPartCount = options.MaxPartCount,
                MaxRetryCount = options.MaxRetryCount,
                PartSize = options.PartSize
            })
        {
            DownloadPath = options.DownloadPath;
            _downloadingInfoPath = options.DownloadingInfoPath;
            _downloadingPath = options.DownloadingPath;
            _storage = options.Storage;

            // create download
            RangeDownloaded += Downloader_RangeDownloaded;
            DownloadStateChanged += Downloader_DownloadStateChanged;
        }

        protected override Stream OpenStream()
        {
            // open or create stream
            var stream = _storage.EntryExists(_downloadingPath)
                ? _storage.OpenStream(_downloadingPath, StreamMode.Open, StreamAccess.ReadWrite, StreamShare.Read)
                : _storage.CreateStream(_downloadingPath, StreamShare.None);
            if (!_storage.EntryExists(_downloadingPath))
                return stream;

            return stream;
        }

        private void Downloader_RangeDownloaded(object sender, EventArgs e)
        {
            // save new DownloadedRanges
            Save();
        }

        private void Downloader_DownloadStateChanged(object sender, EventArgs e)
        {
            if (DownloadState != DownloadState.Finished)
                return;

            //rename the temp downloading file
            try
            {
                if (_storage.StreamExists(DownloadPath))
                    _storage.DeleteStream(DownloadPath);
            }
            catch { }

            // rename stream
            try
            {
                _storage.Rename(_downloadingPath, Path.GetFileNameWithoutExtension(_downloadingPath));
            }
            catch (Exception ex)
            {
                SetLastError(ex);
            }

            //remove info file
            try
            {
                _storage.DeleteStream(_downloadingInfoPath);
            }
            catch { }
        }


        private static DownloadData Load(DownloadControllerOptions options)
        {
            try
            {
                var json = options.Storage.ReadAllText(options.DownloadingInfoPath);
                return JsonConvert.DeserializeObject<DownloadData>(json);
            }
            catch
            {
                return new DownloadData();
            }
        }


        private void Save()
        {
            var data = new DownloadData()
            {
                DownloadedRanges = DownloadedRanges,
                Uri = Uri
            };
            var json = JsonConvert.SerializeObject(data);
            _storage.WriteAllText(_downloadingInfoPath, json);
        }
    }
}
