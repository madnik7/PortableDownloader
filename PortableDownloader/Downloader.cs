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
            downloadState == DownloadState.Stopped ||
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

        public bool IsStarted
        {
            get
            {
                lock (_monitorState)
                    return _startTask != null;
            }
        }

        public int BytesPerSecond
        {
            get
            {
                var curTotalSeconds = TimeSpan.FromTicks(DateTime.Now.Ticks).TotalSeconds;
                var threshold = 5;
                return _speedMonitor.Where(x => x.Seconds > curTotalSeconds - threshold).Sum(x => x.Count) / threshold;
            }
        }

        public Uri Uri { get; }
        public DownloadRange[] DownloadedRanges { get; private set; }
        public long TotalSize { get; private set; }
        public Exception LastException { get; private set; }
        public bool IsResumingSupported { get; private set; }
        public int MaxPartCount { get; set; }
        public int PartSize { get; private set; }
        public long CurrentSize => DownloadedRanges.Where(x => x.IsDone).Sum(x => x.To - x.From + 1) + _currentDownloadingSize; // current downloading range + dowloaded chunk
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
            State = options.IsStopped ? DownloadState.Stopped : DownloadState.None;
            AutoDisposeStream = options.AutoDisposeStream;
            AllowResuming = options.AllowResuming;
        }

        private readonly object _monitorState = new object();
        public DownloadState State
        {
            get
            {
                lock (_monitorState)
                    return _state;
            }
            private set
            {
                lock (_monitorState)
                {
                    if (value == _state)
                        return;
                    _state = value;
                }
                DownloadStateChanged?.Invoke(this, new EventArgs());
            }
        }

        protected void SetLastError(Exception ex)
        {
            if (ex == null)
                throw new ArgumentNullException(nameof(ex));

            FinalizeStream();

            if (ex is OperationCanceledException)
            {
                State = DownloadState.Stopped;
                return;
            }

            Debug.WriteLine($"Error: {ex}");
            LastException = ex; //make sure called before setting DownloadState to raise LastException in change events
            State = DownloadState.Error;
        }

        public async Task Stop()
        {
            Task initTask;
            Task startTask;
            lock (_monitorState)
            {
                initTask = _initTask?.Task;
                startTask = _startTask?.Task;

                switch (State)
                {
                    case DownloadState.Finished:
                    case DownloadState.Stopped:
                    case DownloadState.Error:
                        return;
                }

                State = DownloadState.Stopping;
                _cancellationTokenSource?.Cancel();
            }

            if (initTask != null) await initTask;
            if (startTask != null) await initTask;

            State = DownloadState.Stopped;
        }

        private TaskCompletionSource<object> _initTask;
        private TaskCompletionSource<object> _startTask;


        public async Task Init()
        {
            if (State == DownloadState.Stopping)
                await Stop();

            await Init2();
        }

        private Task Init2()
        {
            lock (_monitorState)
            {
                if (_initTask != null)
                    return _initTask.Task;

                switch (State)
                {
                    case DownloadState.Initializing:
                    case DownloadState.Initialized:
                    case DownloadState.Downloading:
                    case DownloadState.Finished:
                        return Task.FromResult(0);
                }

                _initTask = new TaskCompletionSource<object>();
                _state = DownloadState.Initializing;
                _cancellationTokenSource = new CancellationTokenSource();
            }

            DownloadStateChanged?.Invoke(this, new EventArgs());
            return Init3();

        }

        private async Task Init3()
        {
            try
            {
                //make sure stream is created
                GetStream();

                // download the header
                using (var httpClient = new HttpClient())
                {
                    var response = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, Uri), _cancellationTokenSource.Token);
                    if (response.StatusCode != HttpStatusCode.OK)
                        throw new Exception($"StatusCode is {response.StatusCode}");

                    IsResumingSupported = AllowResuming ? response.Headers.AcceptRanges.Contains("bytes") : false;
                    TotalSize = response.Content.Headers.ContentLength ?? 0;
                    if (response.Content.Headers.ContentLength == null)
                        throw new Exception($"Could not retrieve the stream size: {Uri}");

                    // create new download range if previous size is different
                    if (DownloadedRanges.Length == 0 || DownloadedRanges.Sum(x => x.To - x.From + 1) != TotalSize)
                        DownloadedRanges = BuildDownloadRanges(TotalSize, IsResumingSupported ? PartSize : TotalSize);

                    // finish initializing
                    State = DownloadState.Initialized;
                }
            }
            catch (Exception ex)
            {
                SetLastError(ex);
                throw;
            }
            finally
            {
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
                _initTask.SetResult(null);
                lock (_monitorState)
                {
                    _initTask = null;
                }
            }
        }

        public async Task Start()
        {
            if (State == DownloadState.Stopping)
                await Stop();

            try
            {
                lock (_monitorState)
                {
                    if (IsStarted || State == DownloadState.Downloading || State == DownloadState.Finished)
                        return;

                    _startTask = new TaskCompletionSource<object>();
                }

                // init
                await Init();

                // Check point
                if (State != DownloadState.Initialized)
                    return; //error

                State = DownloadState.Downloading;

                await StartImpl();

                // close stream before setting the state
                FinalizeStream();

                // finish it
                OnBeforeFinish();
                State = DownloadState.Finished;
            }
            catch (Exception ex)
            {
                SetLastError(ex);
                throw;
            }
            finally
            {
                FinalizeStream();

                // change state
                lock (_monitorState)
                {
                    _startTask.SetResult(null);
                    _startTask = null;
                }
            }
        }

        virtual protected void OnBeforeFinish()
        {
        }

        private void FinalizeStream()
        {
            // close stream
            lock (_monitor)
            {
                if (AutoDisposeStream)
                {
                    _stream?.Dispose();
                    _stream = null;
                }
            }
        }

        private async Task StartImpl()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _cancellationTokenSource.Token;

            Exception exRoot = null;

            //download all remaiing parts
            var ranges = DownloadedRanges.Where(x => !x.IsDone);
            await ForEachAsync(ranges, async range =>
             {
                 Exception ex2 = null;
                 for (var i = 0; i < MaxRetyCount + 1 && !_cancellationTokenSource.IsCancellationRequested; i++) // retry
                 {
                     try
                     {
                         if (IsResumingSupported)
                             await DownloadPart(range, cancellationToken);
                         else
                             await DownloadAll(cancellationToken);

                         exRoot = null;
                         return; // finished
                     }
                     catch (Exception ex)
                     {
                         ex2 = ex;
                     }
                 }

                 // just first exception treated as the exception; next exceptions such as CancelOperationException should be ignored
                 lock (_monitor)
                 {
                     if (exRoot == null)
                         exRoot = ex2;

                     // cancel other jobs
                     _cancellationTokenSource.Cancel(); // cancel all other parts
                 }

             }, MaxPartCount, _cancellationTokenSource.Token);

            // release _cancellationTokenSource
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;

            if (exRoot != null)
                throw exRoot;
        }

        private async Task DownloadAll(CancellationToken cancellationToken)
        {
            using (var httpClient = new HttpClient())
            {
                var response = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, Uri), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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
                        if (cancellationToken.IsCancellationRequested)
                            throw new OperationCanceledException();

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
            lock (_monitor)
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                if (AutoDisposeStream)
                    _stream?.Dispose();
            }
        }
    }
}
