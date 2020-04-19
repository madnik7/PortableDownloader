using PortableStorage;
using System;
using System.IO;
using System.Text.Json;

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
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.DownloadPath == null) throw new ArgumentNullException(nameof(options.DownloadPath));
            options.DownloadingPath ??= options.DownloadPath + options.DownloadingExtension;
            options.DownloadingInfoPath ??= options.DownloadPath + options.DownloadingInfoExtension;

            var downloadData = Load(options);
            options.Uri ??= downloadData.Uri ?? throw new ArgumentNullException(nameof(options.Uri));
            var ret = new DownloadController(options, downloadData);
            if (!options.IsStopped)
                ret.Init().GetAwaiter();
            return ret;
        }

        private DownloadController(DownloadControllerOptions options, DownloadData downloadData)
            : base(new DownloaderOptions()
            {
                Uri = options.Uri,
                DownloadedRanges = downloadData.DownloadedRanges,
                AutoDisposeStream = true,
                IsStopped = options.IsStopped,
                AllowResuming = options.AllowResuming,
                MaxPartCount = options.MaxPartCount,
                MaxRetryCount = options.MaxRetryCount,
                PartSize = options.PartSize,
                WriteBufferSize = options.WriteBufferSize,
            })
        {
            DownloadPath = options.DownloadPath;
            _downloadingInfoPath = options.DownloadingInfoPath;
            _downloadingPath = options.DownloadingPath;
            _storage = options.Storage;
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

        private int _totalRead;
        protected override void OnDataReceived(int readedCount)
        {
            base.OnDataReceived(readedCount);
            _totalRead += readedCount;
            if (_totalRead > 1000000)
            {
                Save();
                _totalRead = 0;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "<Pending>")]
        protected override void OnBeforeFinish()
        {

            base.OnBeforeFinish();

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
                Save();
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

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "<Pending>")]
        private static DownloadData Load(DownloadControllerOptions options)
        {
            try
            {
                var json = options.Storage.ReadAllText(options.DownloadingInfoPath);
                return JsonSerializer.Deserialize<DownloadData>(json);
            }
            catch
            {
                return new DownloadData();
            }
        }


        private void Save()
        {
            Flush();

            var data = new DownloadData()
            {
                DownloadedRanges = DownloadedRanges,
                Uri = Uri
            };
            var json = JsonSerializer.Serialize(data);
            _storage.WriteAllText(_downloadingInfoPath, json);
        }
    }
}
