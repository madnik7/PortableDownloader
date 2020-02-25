using Newtonsoft.Json;
using PortableStorage;
using System;
using System.IO;

namespace PortableDownloader
{
    class DownloadController : Downloader
    {
        class DownloadData
        {
            public Uri Uri { get; set; }
            public DownloadRange[] DownloadedRanges { get; set; }
        }

        private readonly string _downloadingStreamPath;
        private readonly string _downloadingInfoStreamPath;
        private readonly Storage _storage;
        public string Path { get; }

        public static DownloadController Create(DownloadControllerOptions options)
        {
            var downloadData = Load(options);
            options.Uri = options.Uri ?? downloadData.Uri ?? throw new ArgumentNullException("Could not find RemoteUri");
            return new DownloadController(options, downloadData);

        }

        private DownloadController(DownloadControllerOptions options, DownloadData downloadData)
            : base(new DownloaderOptions() { Uri = options.Uri, DownloadedRanges = downloadData.DownloadedRanges, AutoDisposeStream = true })
        {
            _downloadingStreamPath = options.DownloadingStreamPath;
            _downloadingInfoStreamPath = options.DownloadingInfoStreamPath;
            _storage = options.Storage;
            Path = Storage.PathCombine(System.IO.Path.GetDirectoryName(options.DownloadingStreamPath), System.IO.Path.GetFileNameWithoutExtension(options.DownloadingStreamPath));

            // create download
            RangeDownloaded += Downloader_RangeDownloaded;
            DownloadStateChanged += Downloader_DownloadStateChanged;
        }

        protected override Stream OpenStream()
        {
            // open or create stream
            var stream = _storage.EntryExists(_downloadingStreamPath)
                ? _storage.OpenStream(_downloadingStreamPath, StreamMode.Open, StreamAccess.ReadWrite, StreamShare.Read)
                : _storage.CreateStream(_downloadingStreamPath, StreamShare.None);
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
                if (_storage.StreamExists(Path))
                    _storage.DeleteStream(Path);
            }
            catch { }

            // rename stream
            try
            {
                _storage.Rename(_downloadingStreamPath, System.IO.Path.GetFileName(Path));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in renaming stream: {Path}, Error: {ex.Message}");
                SetLastError(ex);
            }

            //remove info file
            try
            {
                _storage.DeleteStream(_downloadingInfoStreamPath);
            }
            catch { }
        }


        private static DownloadData Load(DownloadControllerOptions options)
        {
            try
            {
                var json = options.Storage.ReadAllText(options.DownloadingInfoStreamPath);
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
            _storage.WriteAllText(_downloadingInfoStreamPath, json);
        }
    }
}
