using PortableStorage;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;

namespace PortableDownloader
{
    public class DownloadController : Downloader
    {
        class DownloadData
        {
            public double DownloadDuration { get; set; }
            public Uri Uri { get; set; }
            public DownloadRange[] DownloadedRanges { get; set; }
        }
        private readonly string _downloadingPath;
        private readonly string _downloadingInfoPath;
        public Storage LocalStorage { get; }
        public string LocalPath { get; }
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
                Host = options.Host,
                Referrer = options.Referrer,
                UserAgent = options.UserAgent,
                ClientHandler = options.ClientHandler
            })
        {
            LocalPath = options.DownloadPath;
            _downloadingInfoPath = options.DownloadingInfoPath;
            _downloadingPath = options.DownloadingPath;
            LocalStorage = options.Storage;
            DownloadDuration = downloadData.DownloadDuration;
        }
        protected override Stream OpenStream()
        {
            // open or create stream
            var stream = LocalStorage.EntryExists(_downloadingPath)
                ? LocalStorage.OpenStream(_downloadingPath, StreamMode.Open, StreamAccess.ReadWrite, StreamShare.Read)
                : LocalStorage.CreateStream(_downloadingPath, StreamShare.None);
            if (!LocalStorage.EntryExists(_downloadingPath))
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
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "<Pending>")]
        protected override void OnBeforeFinish()
        {
            base.OnBeforeFinish();
            //rename the temp downloading file
            try
            {
                if (LocalStorage.StreamExists(LocalPath))
                    LocalStorage.DeleteStream(LocalPath);
            }
            catch { }
            // rename stream
            try
            {
                Save();
                LocalStorage.Rename(_downloadingPath, Path.GetFileNameWithoutExtension(_downloadingPath));
            }
            catch (Exception ex)
            {
                SetLastError(ex);
            }
            //remove info file
            try
            {
                LocalStorage.DeleteStream(_downloadingInfoPath);
            }
            catch { }
        }
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "<Pending>")]
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
                Uri = Uri,
                DownloadDuration = DownloadDuration
            };
            var json = JsonSerializer.Serialize(data);
            LocalStorage.WriteAllText(_downloadingInfoPath, json);
        }
    }
}