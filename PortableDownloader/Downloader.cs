using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace PortableDownloader
{
    public class Downloader : IDisposable
    {
        private class SpeedData
        {
            public double Seconds;
            public int Count;
        }

        public static bool IsIdleState(DownloadState downloadState) =>
            downloadState == DownloadState.None ||
            downloadState == DownloadState.Initialized ||
            downloadState == DownloadState.Error ||
            downloadState == DownloadState.Finished;

        public event EventHandler DataReceived;
        public event EventHandler RangeDownloaded;
        public event EventHandler DownloadStateChanged;

        private readonly object _monitor = new object();
        private Stream _stream;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly ConcurrentQueue<SpeedData> _speedMonitor = new ConcurrentQueue<SpeedData>();
        private long _currentDownloadingSize;
        private DownloadState _state = DownloadState.None;

        public int BytesPerSecond
        {
            get
            {
                var curTotalSeconds = TimeSpan.FromTicks(DateTime.Now.Ticks).TotalSeconds;
                return _speedMonitor.Where(x => x.Seconds > curTotalSeconds - 5).Sum(x => x.Count);
            }
        }

        public Uri Uri { get; }
        public DownloadRange[] DownloadedRanges { get; private set; }
        public long TotalSize { get; private set; }
        public Exception LastException { get; private set; }
        public bool IsResumingSupported { get; private set; }
        public int MaxPartCount { get; set; }
        public int PartSize { get; private set; }
        public long CurrentSize => DownloadedRanges.Where(x => x.IsDone).Sum(x => x.To) + _currentDownloadingSize; // current downloading range + dowloaded chunk

        public bool AutoDisposeStream { get; }
        public bool AllowResuming { get; }
        public int MaxRetyCount { get; set; }

        public Downloader(DownloaderOptions options)
        {
            if (options.PartSize < 10000)
                throw new ArgumentException("PartSize parameter must be equals or greater than 10000", "PartSize");

            Uri = options.Uri;
            _stream = options.Stream;
            DownloadedRanges = options.DownloadedRanges ?? new DownloadRange[0];
            MaxPartCount = options.MaxPartCount;
            MaxRetyCount = options.MaxRetryCount;
            PartSize = options.PartSize;
            DownloadState = DownloadState.None;
            AutoDisposeStream = options.AutoDisposeStream;
            AllowResuming = options.AllowResuming;
        }

        public DownloadState DownloadState
        {
            get => _state; private set
            {
                if (value == _state)
                    return;
                _state = value;
                DownloadStateChanged?.Invoke(this, new EventArgs());
            }
        }

        protected void SetLastError(Exception ex)
        {
            Debug.WriteLine($"Error: {ex}");
            LastException = ex; //make sure called before setting DownloadState to raise LastException in change events
            DownloadState = DownloadState.Error;
        }

        public void Stop()
        {
            if (DownloadState == DownloadState.Finished)
                return;

            if (_cancellationTokenSource != null)
                _cancellationTokenSource.Cancel();

            DownloadState = DownloadState.None;
        }

        public async Task Init()
        {
            if (DownloadState != DownloadState.None)
                return;

            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                DownloadState = DownloadState.Initializing;
                using (var httpClient = new HttpClient())
                {
                    var response = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, Uri), _cancellationTokenSource.Token);
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        SetLastError(new Exception($"StatusCode is {response.StatusCode}"));
                        throw LastException;
                    }
                    IsResumingSupported = AllowResuming ? response.Headers.AcceptRanges.Contains("bytes") : false;
                    TotalSize = response.Content.Headers.ContentLength ?? 0;
                    if (response.Content.Headers.ContentLength == null)
                    {
                        SetLastError(new Exception($"Could not retrieve the stream size: {Uri}"));
                        throw LastException;
                    }
                    DownloadState = DownloadState.Initialized;
                }
            }
            catch (Exception ex)
            {
                SetLastError(ex);
            }
            finally
            {
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }
        }

        public async Task Start()
        {
            if (DownloadState == DownloadState.None)
                await Init();

            if (DownloadState != DownloadState.Initialized)
                return; // error

            // create download range)
            if (DownloadedRanges.Length == 0)
                DownloadedRanges = BuildDownloadRanges(TotalSize, IsResumingSupported ? PartSize : TotalSize);

            DownloadState = DownloadState.Downloading;
            _cancellationTokenSource = new CancellationTokenSource();

            //download all remaiing parts
            var ranges = DownloadedRanges.Where(x => !x.IsDone);
            await ForEachAsync(ranges, async range =>
             {
                 for (var i = 0; i < MaxRetyCount + 1 && !_cancellationTokenSource.IsCancellationRequested; i++) // retry
                 {
                     try
                     {
                         if (IsResumingSupported)
                             await DownloadPart(range, _cancellationTokenSource.Token);
                         else
                             await DownloadAll(_cancellationTokenSource.Token);

                         return; // finished
                     }
                     catch (Exception ex)
                     {
                         LastException = ex;
                     }
                 }

                 // report error
                 _cancellationTokenSource.Cancel(); // cancel all other parts
                 SetLastError(LastException);
             }, MaxPartCount, _cancellationTokenSource.Token);

            // release _cancellationTokenSource
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;

            // finish if there is no error
            if (AutoDisposeStream)
                _stream?.Dispose();

            // finish it
            if (DownloadState == DownloadState.Error)
                throw LastException;
            else
                DownloadState = DownloadState.Finished;
        }

        private async Task DownloadAll(CancellationToken cancellationToken)
        {

            using (var httpClient = new HttpClient())
            {
                var response = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, Uri), cancellationToken);
                using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    // download to downloadedStream
                    var buffer = new byte[1024];
                    while (true)
                    {
                        var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                        if (bytesRead == 0)
                            break;

                        GetStream().Write(buffer, 0, bytesRead);
                        OnDataReceived(bytesRead);
                    }

                    GetStream().Flush();
                }
            }
        }

        private async Task DownloadPart(DownloadRange downloadRange, CancellationToken cancellationToken)
        {
            using (var httpClient = new HttpClient())
            {
                // get part from server and copy it to a memory stream
                httpClient.DefaultRequestHeaders.Range = new RangeHeaderValue(downloadRange.From, downloadRange.To);
                var response = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, Uri), cancellationToken);
                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var downloadedStream = new MemoryStream(PartSize))
                {
                    // download to downloadedStream
                    var buffer = new byte[1024];
                    while (true)
                    {
                        var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                        if (bytesRead == 0)
                            break;

                        downloadedStream.Write(buffer, 0, bytesRead);
                        OnDataReceived(bytesRead);
                    }

                    // copy part to file
                    lock (_monitor)
                    {
                        downloadedStream.Position = 0;
                        GetStream().Position = downloadRange.From;
                        downloadedStream.CopyTo(GetStream());
                        GetStream().Flush();
                        downloadRange.IsDone = true;
                        _currentDownloadingSize -= downloadedStream.Length;
                        RangeDownloaded?.Invoke(this, new EventArgs());
                    }
                }
            }
        }

        private static DownloadRange[] BuildDownloadRanges(long totalSize, long partSize)
        {
            var ret = new List<DownloadRange>();
            for (var i = 0L; i < totalSize; i += partSize)
            {
                ret.Add(new DownloadRange()
                {
                    From = i,
                    To = Math.Min(totalSize, i + partSize) - 1,
                    IsDone = false
                });
            }

            return ret.ToArray();
        }

        private static Task ForEachAsync<T>(IEnumerable<T> source, Func<T, Task> body, int maxDegreeOfParallelism, CancellationToken cancellationToken)
        {
            // run tasks
            var tasks = from partition in Partitioner.Create(source).GetPartitions(maxDegreeOfParallelism)
                        select Task.Run(async delegate
                        {
                            using (partition)
                                while (partition.MoveNext())
                                {
                                    // break if canceled
                                    if (cancellationToken.IsCancellationRequested)
                                        break;

                                    await body(partition.Current).ContinueWith(t =>
                                    {
                                        //observe exceptions
                                    });
                                }
                        }, cancellationToken);

            return Task.WhenAll(tasks);
        }

        private void OnDataReceived(int readedCount)
        {
            var curTotalSeconds = TimeSpan.FromTicks(DateTime.Now.Ticks).TotalSeconds;

            lock (_monitor)
            {
                _currentDownloadingSize += readedCount;
                _speedMonitor.Enqueue(new SpeedData() { Seconds = curTotalSeconds, Count = readedCount });

                // notify new data received
                DataReceived?.Invoke(this, new EventArgs());
            }

            // remove old data
            while (_speedMonitor.TryPeek(out SpeedData speedData) && speedData.Seconds < curTotalSeconds - 5)
                _speedMonitor.TryDequeue(out _);
        }

        private Stream GetStream()
        {
            lock (_monitor)
            {
                if (_stream == null)
                    _stream = OpenStream();

                if (_stream == null)
                    throw new Exception("Neither Stream option nor OpenStream method provided!");

                return _stream;
            }
        }

        protected virtual Stream OpenStream()
        {
            return _stream;
        }

        public void Dispose()
        {
            if (AutoDisposeStream)
                _stream?.Dispose();
            _cancellationTokenSource?.Dispose();
        }
    }
}
