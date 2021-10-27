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
        public double DownloadDuration { get; protected set; }
        private readonly object _monitor = new object();
        private Stream _stream;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly ConcurrentQueue<SpeedData> _speedMonitor = new ConcurrentQueue<SpeedData>();
        private bool _disposedValue; // To detect redundant calls
        private DownloadState _state = DownloadState.None;

        private int SpeedThresholdSeconds { get; } = 20;
        public bool IsStarted
        {
            get
            {
                lock (_monitorState)
                    return _startTask != null && State != DownloadState.Finished;
            }
        }
        public int BytesPerSecond
        {
            get
            {
                var threshold = SpeedThresholdSeconds;
                var curTotalSeconds = TimeSpan.FromTicks(DateTime.Now.Ticks).TotalSeconds;
                return _speedMonitor.Where(x => x.Seconds > curTotalSeconds - threshold).Sum(x => x.Count) / threshold;
            }
        }
        public Uri Uri { get; }
        public DownloadRange[] DownloadedRanges { get; private set; }
        public long TotalSize { get; private set; }
        public Exception LastException { get; private set; }
        public bool IsResumingSupported { get; private set; }
        public int MaxPartCount { get; set; }
        public long PartSize { get; }
        public long CurrentSize => DownloadedRanges.Sum(x => x.CurrentOffset);
        public bool AutoDisposeStream { get; }
        public bool AllowResuming { get; }
        public int MaxRetryCount { get; set; }
        public string Host { get; }
        public Uri Referrer { get; }
        public string UserAgent { get; set; }
        public HttpMessageHandler ClientHandler { get; }
        private readonly int _writeBufferSize;
        public Downloader(DownloaderOptions options)
        {
            if (options is null) throw new ArgumentNullException(nameof(options));
            if (options.PartSize < 10000)
                throw new ArgumentException("PartSize parameter must be equals or greater than 10000", nameof(options.PartSize));
            Uri = options.Uri;
            _stream = options.Stream;
            _writeBufferSize = options.WriteBufferSize;
            DownloadedRanges = options.DownloadedRanges ?? Array.Empty<DownloadRange>();
            MaxPartCount = options.MaxPartCount;
            MaxRetryCount = options.MaxRetryCount;
            PartSize = options.PartSize;
            Host = options.Host;
            Referrer = options.Referrer;
            UserAgent = options.UserAgent;
            State = options.IsStopped ? DownloadState.Stopped : DownloadState.None;
            AutoDisposeStream = options.AutoDisposeStream;
            AllowResuming = options.AllowResuming;
            ClientHandler = options.ClientHandler;
            TotalSize = options.DownloadedRanges != null && options.DownloadedRanges.Length > 0 ? DownloadedRanges.Sum(x => x.To - x.From + 1) : 0;
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
                DownloadStateChanged?.Invoke(this, EventArgs.Empty);
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
            if (initTask != null) await initTask.ConfigureAwait(false);
            if (startTask != null) await startTask.ConfigureAwait(false);
            State = DownloadState.Stopped;
        }
        private TaskCompletionSource<object> _initTask;
        private TaskCompletionSource<object> _startTask;

        public async Task Init()
        {
            if (State == DownloadState.Stopping)
                await Stop().ConfigureAwait(false);

            await Init2().ConfigureAwait(false);
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
            DownloadStateChanged?.Invoke(this, EventArgs.Empty);
            return Init3();
        }

        private async Task Init3()
        {
            try
            {
                //make sure stream is created
                GetStream();

                // download the header
                using var httpClient = ClientHandler == null ? new HttpClient() : new HttpClient(ClientHandler);
                
                if (!string.IsNullOrEmpty(Host)) httpClient.DefaultRequestHeaders.Host = Host;
                if (Referrer != null) httpClient.DefaultRequestHeaders.Referrer = Referrer;
                if (!string.IsNullOrEmpty(UserAgent)) httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd(UserAgent);
                
                using var requestMessage = new HttpRequestMessage(HttpMethod.Head, Uri);
                var response = await httpClient.SendAsync(requestMessage, _cancellationTokenSource.Token).ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new Exception($"StatusCode is {response.StatusCode}");

                IsResumingSupported = AllowResuming && response.Headers.AcceptRanges.Contains("bytes");
                TotalSize = response.Content.Headers.ContentLength ?? 0;
                if (response.Content.Headers.ContentLength == null)
                    throw new Exception($"Could not retrieve the stream size: {Uri}");

                // create new download range if previous size is different
                if (DownloadedRanges.Length == 0 || DownloadedRanges.Sum(x => x.To - x.From + 1) != TotalSize)
                    DownloadedRanges = BuildDownloadRanges(TotalSize, IsResumingSupported && MaxPartCount >= 2 ? PartSize : TotalSize);

                // finish initializing
                State = DownloadState.Initialized;
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
                await Stop().ConfigureAwait(false);

            lock (_monitorState)
            {
                if (IsStarted || State == DownloadState.Downloading || State == DownloadState.Finished)
                    return;
                _startTask = new TaskCompletionSource<object>();
            }
            
            var dateTime = DateTime.Now;
            try
            {
                // init
                await Init().ConfigureAwait(false);
                // Check point
                if (State != DownloadState.Initialized)
                    return; //error
                State = DownloadState.Downloading;
                await StartImpl().ConfigureAwait(false);
                // save time before any notification
                DownloadDuration += (DateTime.Now - dateTime).TotalSeconds;
                dateTime = DateTime.Now; //prevent add again if an exception occurred
                // close stream before setting the state
                FinalizeStream();
                // pre finish job
                OnBeforeFinish();
                // mark as finish
                State = DownloadState.Finished;
            }
            catch (Exception ex)
            {
                DownloadDuration += (DateTime.Now - dateTime).TotalSeconds;
                SetLastError(ex);
                throw;
            }
            finally
            {
                FinalizeStream();
                // change state
                lock (_monitorState)
                {
                    _startTask?.SetResult(null);
                    _startTask = null;
                }
            }
        }
        protected virtual void OnBeforeFinish()
        {
        }

        private void FinalizeStream()
        {
            // close stream
            lock (_monitor)
            {
                Flush();
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
            
            //download all remaining parts
            var ranges = DownloadedRanges.Where(x => !x.IsDone);
            await ForEachAsync(ranges, async range =>
             {
                 Exception ex2 = null;
                 for (var i = 0; i < MaxRetryCount + 1 && !cancellationToken.IsCancellationRequested; i++) // retry
                 {
                     try
                     {
                         await DownloadPart(range, cancellationToken).ConfigureAwait(true);
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
                     exRoot ??= ex2;
                     
                     // cancel other jobs
                     _cancellationTokenSource.Cancel(); // cancel all other parts
                 }
             }, MaxPartCount, cancellationToken).ConfigureAwait(false);
            
            // release _cancellationTokenSource
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
            if (exRoot != null)
                throw exRoot;
        }
        private async Task DownloadPart(DownloadRange downloadRange, CancellationToken cancellationToken)
        {
            using var httpClient = ClientHandler == null ? new HttpClient() : new HttpClient(ClientHandler);
            
            if (!string.IsNullOrEmpty(Host)) httpClient.DefaultRequestHeaders.Host = Host;
            if (Referrer != null) httpClient.DefaultRequestHeaders.Referrer = Referrer;
            if (!string.IsNullOrEmpty(UserAgent)) httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd(UserAgent);
            
            // get part from server and copy it to a memory stream
            if (IsResumingSupported)
                httpClient.DefaultRequestHeaders.Range = new RangeHeaderValue(downloadRange.From + downloadRange.CurrentOffset, downloadRange.To);
            else if (downloadRange.From != 0)
                throw new InvalidOperationException("downloadRange.From should be zero when resuming not supported!");
            
            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, Uri);
            var response = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            
            // download to downloadedStream
            var buffer = new byte[_writeBufferSize];
            while (true)
            {
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                    break;
                
                // copy part to file
                lock (_monitor)
                {
                    GetStream().Position = downloadRange.From + downloadRange.CurrentOffset;
                    GetStream().Write(buffer, 0, bytesRead);
                    downloadRange.CurrentOffset = GetStream().Position - downloadRange.From;
                    OnDataReceived(bytesRead);
                }
            }
            
            // copy part to file
            lock (_monitor)
            {
                downloadRange.IsDone = true;
                RangeDownloaded?.Invoke(this, EventArgs.Empty);
            }
        }
        public void Flush()
        {
            lock (_monitor)
            {
                if (_stream != null)
                    _stream.Flush();
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
                                    }, TaskScheduler.Current).ConfigureAwait(true);
                                }
                        }, cancellationToken);
            return Task.WhenAll(tasks);
        }
        protected virtual void OnDataReceived(int readCount)
        {
            var curTotalSeconds = TimeSpan.FromTicks(DateTime.Now.Ticks).TotalSeconds;
            lock (_monitor)
            {
                _speedMonitor.Enqueue(new SpeedData { Seconds = curTotalSeconds, Count = readCount });
                // notify new data received
                DataReceived?.Invoke(this, EventArgs.Empty);
            }
            // remove old data
            while (_speedMonitor.TryPeek(out SpeedData speedData) && speedData.Seconds < curTotalSeconds - SpeedThresholdSeconds)
                _speedMonitor.TryDequeue(out _);
        }
        private Stream GetStream()
        {
            lock (_monitor)
            {
                _stream ??= OpenStream();
                if (_stream == null)
                    throw new Exception("Neither Stream option nor OpenStream method provided!");
                return _stream;
            }
        }
        protected virtual Stream OpenStream()
        {
            return _stream;
        }
        
        protected virtual void Dispose(bool disposing)
        {
            if (_disposedValue)
                return;
            if (disposing)
            {
                lock (_monitor)
                {
                    _cancellationTokenSource?.Cancel();
                    _cancellationTokenSource?.Dispose();
                    if (AutoDisposeStream)
                        _stream?.Dispose();
                }
            }
            _disposedValue = true;
        }
        
        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}